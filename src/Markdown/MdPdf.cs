// Stackshot - Markdown to PDF: the preview's document laid out by WPF on A4 pages and written with real text (the glyphs it
// uses embedded, so it can be selected, searched and copied), vector shapes, the pictures, page numbers and bookmarks.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot
{
    internal static class MdPdf
    {
        // A4 in WPF units (1/96 inch); margins of 2 cm, 2.5 cm at the bottom where the page number goes.
        public const double PageWidth = 793.7008, PageHeight = 1122.5197;
        const double Side = 75.6, Top = 75.6, Bottom = 94.5;
        const int MaxPages = 5000;

        // Writes the document as a PDF at path (replacing it only once complete). progress gets the pages done so far; stop
        // is asked before each page (true: OperationCanceledException, nothing written). Runs on the calling thread, which
        // must be an STA thread of its own: the layout is WPF's. The number of pages written.
        internal static int Export(string markdown, string baseDir, string path, string title, Action<int> progress, Func<bool> stop)
        {
            List<MdBlock> blocks = MarkdownDoc.Parse(markdown ?? "");
            List<Block> made = new List<Block>();
            FlowDocument doc = MarkdownFlow.Printable(blocks, baseDir, PageHeight - Top - Bottom - 12, made);
            doc.PageWidth = PageWidth;
            doc.PageHeight = PageHeight;
            doc.PagePadding = new Thickness(Side, Top, Side, Bottom);
            doc.ColumnWidth = PageWidth;
            DynamicDocumentPaginator pg = (DynamicDocumentPaginator)((IDocumentPaginatorSource)doc).DocumentPaginator;
            pg.PageSize = new Size(PageWidth, PageHeight);
            if (stop != null && stop()) throw new OperationCanceledException();
            int pages = 0;
            AtomicFile.Write(path, delegate(Stream fs)
            {
                MdPdfFile pdf = new MdPdfFile(fs, title, PageWidth, PageHeight);
                for (int i = 0; i < MaxPages; i++)
                {
                    if (stop != null && stop()) throw new OperationCanceledException();
                    DocumentPage page = pg.GetPage(i);
                    if (page == null || page == DocumentPage.Missing || page.Visual == null) break;
                    pdf.AddPage(page.Visual);
                    page.Dispose();
                    pages++;
                    if (progress != null) progress(pages);
                }
                if (pages == 0) { pdf.AddPage(null); pages = 1; }
                pdf.Bookmarks(Marks(pg, blocks, made));
                pdf.Finish(Footer);
            });
            return pages;
        }

        // The page number, centred under the text.
        static Visual Footer(int page, int total)
        {
            DrawingVisual v = new DrawingVisual();
            FormattedText t = new FormattedText(page.ToString(CultureInfo.InvariantCulture) + " / " + total.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("es-ES"),
                                                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                                10.5, Ds.Brush(Ds.Argb(0.5, 0, 0, 0)), 1.0);
            using (DrawingContext dc = v.RenderOpen()) dc.DrawText(t, new Point(Math.Round((PageWidth - t.WidthIncludingTrailingWhitespace) / 2, 2), Math.Round(PageHeight - Bottom / 2 - t.Height / 2, 2)));
            return v;
        }

        // The outline's headings (H1 to H3) with the page each one starts on.
        static List<MdPdfFile.Mark> Marks(DynamicDocumentPaginator pg, List<MdBlock> blocks, List<Block> made)
        {
            List<MdPdfFile.Mark> o = new List<MdPdfFile.Mark>();
            foreach (MdHeading h in MdOutline.Headings(blocks))
            {
                Block b = h.Block < made.Count ? made[h.Block] : null;
                if (b == null) continue;
                int page;
                try { page = pg.GetPageNumber(b.ContentStart); }
                catch (Exception) { continue; }
                if (page >= 0) o.Add(new MdPdfFile.Mark { Level = h.Depth, Title = h.Text, Page = page });
            }
            return o;
        }
    }

    // A PDF written as it goes: each page's content goes to the file at once; fonts, page objects and the cross-reference
    // table at the end. Colours are mixed over white (no transparency), so it prints the same everywhere.
    internal sealed class MdPdfFile
    {
        public sealed class Mark
        {
            public int Level, Page;
            public string Title;
        }

        internal sealed class Font
        {
            public string Name, Base;
            public int Obj;
            public MdPdfFont Ttf;
            public readonly SortedDictionary<ushort, string> Used = new SortedDictionary<ushort, string>();
        }

        readonly Stream fs;
        readonly List<long> offsets = new List<long>();
        readonly double width, height;
        readonly string title;
        readonly int catalog, tree, resources, info;
        readonly List<int> contents = new List<int>();
        readonly Dictionary<GlyphTypeface, Font> fonts = new Dictionary<GlyphTypeface, Font>();
        readonly List<Font> embedded = new List<Font>();
        readonly Dictionary<ImageSource, string> images = new Dictionary<ImageSource, string>();
        readonly List<KeyValuePair<string, int>> imageObjs = new List<KeyValuePair<string, int>>();
        readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
        List<Mark> marks;

        public MdPdfFile(Stream fs, string title, double width, double height)
        {
            this.fs = fs;
            this.title = title;
            this.width = width;
            this.height = height;
            Write("%PDF-1.4\n");
            fs.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);
            catalog = Reserve();
            tree = Reserve();
            resources = Reserve();
            info = Reserve();
        }

        int Reserve() { offsets.Add(-1); return offsets.Count; }

        void Begin(int obj)
        {
            offsets[obj - 1] = fs.Position;
            Write(obj.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
        }

        void Object(int obj, string body)
        {
            Begin(obj);
            Write(body);
            Write("\nendobj\n");
        }

        void Write(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            fs.Write(b, 0, b.Length);
        }

        void Stream(int obj, string dict, byte[] data, CompressionLevel level)
        {
            byte[] z = Zlib(data, level);
            Begin(obj);
            Write("<< " + dict + (dict.Length > 0 ? " " : "") + "/Filter /FlateDecode /Length " + z.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            fs.Write(z, 0, z.Length);
            Write("\nendstream\nendobj\n");
        }

        // zlib (header, deflate, Adler-32), as FlateDecode expects.
        static byte[] Zlib(byte[] data, CompressionLevel level)
        {
            using (MemoryStream ms = new MemoryStream(data.Length / 3 + 64))
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (DeflateStream ds = new DeflateStream(ms, level, true)) ds.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                int i = 0;
                while (i < data.Length)
                {
                    int n = Math.Min(5552, data.Length - i);
                    for (int k = 0; k < n; k++) { a += data[i + k]; b += a; }
                    a %= 65521;
                    b %= 65521;
                    i += n;
                }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        public void AddPage(Visual v)
        {
            MdPdfCanvas c = new MdPdfCanvas(this, height);
            if (v != null) c.Visual(v);
            int obj = Reserve();
            Stream(obj, "", c.Bytes(), CompressionLevel.Optimal);
            contents.Add(obj);
        }

        public void Bookmarks(List<Mark> list) { marks = list; }

        // The font for a typeface, or null when its glyphs have to be drawn as shapes.
        internal string FontName(GlyphTypeface face, out Font font)
        {
            if (!fonts.TryGetValue(face, out font))
            {
                MdPdfFont ttf = face.StyleSimulations == StyleSimulations.None ? MdPdfFont.Open(face, files) : null;
                font = new Font { Ttf = ttf };
                if (ttf != null)
                {
                    font.Name = "F" + (embedded.Count + 1).ToString(CultureInfo.InvariantCulture);
                    font.Obj = Reserve();
                    font.Base = Subset(embedded.Count) + "+" + PsName(face);
                    embedded.Add(font);
                }
                fonts[face] = font;
            }
            return font.Ttf != null ? font.Name : null;
        }

        internal int GlyphWidth(Font f, ushort g) { return f.Ttf.Width(g); }

        internal static void Note(Font f, ushort g, string text)
        {
            string had;
            if (!f.Used.TryGetValue(g, out had) || (had == null && text != null)) f.Used[g] = text;
        }

        static string Subset(int i)
        {
            char[] c = { 'S', 'T', 'K', 'A', 'A', 'A' };
            for (int k = 5; k >= 3 && i > 0; k--) { c[k] = (char)('A' + i % 26); i /= 26; }
            return new string(c);
        }

        static string PsName(GlyphTypeface face)
        {
            string fam = Pick(face.FamilyNames), sty = Pick(face.FaceNames);
            StringBuilder sb = new StringBuilder();
            foreach (char ch in fam + "-" + sty) if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-') sb.Append(ch);
            string s = sb.ToString().Trim('-');
            return s.Length == 0 ? "Font" : s.Length > 60 ? s.Substring(0, 60) : s;
        }

        static string Pick(IDictionary<CultureInfo, string> names)
        {
            string v;
            if (names.TryGetValue(CultureInfo.GetCultureInfo("en-us"), out v)) return v;
            foreach (KeyValuePair<CultureInfo, string> kv in names) return kv.Value;
            return "";
        }

        // A picture as an image object (written at once, shared by every page that shows it), mixed over white.
        internal string ImageName(BitmapSource bmp)
        {
            string name;
            if (images.TryGetValue(bmp, out name)) return name;
            BitmapSource src = bmp.Format == PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            if (w <= 0 || h <= 0) return null;
            byte[] px = new byte[(long)w * h * 4];
            src.CopyPixels(px, w * 4, 0);
            byte[] rgb = new byte[(long)w * h * 3];
            for (long i = 0, o = 0; i < px.Length; i += 4)
            {
                int a = 255 - px[i + 3];
                rgb[o++] = (byte)Math.Min(255, px[i + 2] + a);
                rgb[o++] = (byte)Math.Min(255, px[i + 1] + a);
                rgb[o++] = (byte)Math.Min(255, px[i] + a);
            }
            int obj = Reserve();
            Stream(obj, "/Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceRGB /BitsPerComponent 8", rgb, CompressionLevel.Optimal);
            name = "Im" + (imageObjs.Count + 1).ToString(CultureInfo.InvariantCulture);
            imageObjs.Add(new KeyValuePair<string, int>(name, obj));
            images[bmp] = name;
            return name;
        }

        // Page numbers (footer(page, total), drawn now that the total is known), the page objects, the fonts with the glyphs
        // every page used, the bookmarks and the cross-reference table.
        public void Finish(Func<int, int, Visual> footer)
        {
            int n = contents.Count;
            List<int> pageObjs = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int extra = 0;
                if (footer != null)
                {
                    MdPdfCanvas c = new MdPdfCanvas(this, height);
                    c.Visual(footer(i + 1, n));
                    extra = Reserve();
                    Stream(extra, "", c.Bytes(), CompressionLevel.Optimal);
                }
                int page = Reserve();
                pageObjs.Add(page);
                Object(page, "<< /Type /Page /Parent " + tree + " 0 R /MediaBox [0 0 " + MdPdfCanvas.Num(Math.Round(width * 0.75, 2)) + " " + MdPdfCanvas.Num(Math.Round(height * 0.75, 2)) + "] /Resources " + resources +
                             " 0 R /Contents " + (extra > 0 ? "[" + contents[i] + " 0 R " + extra + " 0 R]" : contents[i] + " 0 R") + " >>");
            }
            StringBuilder kids = new StringBuilder();
            foreach (int p in pageObjs) kids.Append(p).Append(" 0 R ");
            Object(tree, "<< /Type /Pages /Kids [" + kids.ToString().TrimEnd() + "] /Count " + n + " >>");
            foreach (Font f in embedded) WriteFont(f);
            StringBuilder res = new StringBuilder("<< /ProcSet [/PDF /Text /ImageC]");
            if (embedded.Count > 0)
            {
                res.Append(" /Font <<");
                foreach (Font f in embedded) res.Append(" /").Append(f.Name).Append(' ').Append(f.Obj).Append(" 0 R");
                res.Append(" >>");
            }
            if (imageObjs.Count > 0)
            {
                res.Append(" /XObject <<");
                foreach (KeyValuePair<string, int> kv in imageObjs) res.Append(" /").Append(kv.Key).Append(' ').Append(kv.Value).Append(" 0 R");
                res.Append(" >>");
            }
            Object(resources, res.Append(" >>").ToString());
            int outlines = WriteBookmarks(pageObjs);
            Object(catalog, "<< /Type /Catalog /Pages " + tree + " 0 R" + (outlines > 0 ? " /Outlines " + outlines + " 0 R" : "") + " >>");
            string when = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            TimeSpan zone = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            string tz = (zone < TimeSpan.Zero ? "-" : "+") + Math.Abs(zone.Hours).ToString("00") + "'" + Math.Abs(zone.Minutes).ToString("00") + "'";
            Object(info, "<< /Title " + Utf16(title ?? "") + " /Producer (Stackshot) /Creator (Stackshot) /CreationDate (D:" + when + tz + ") >>");
            long xref = fs.Position;
            StringBuilder x = new StringBuilder();
            x.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (long o in offsets)
            {
                if (o < 0) throw new InvalidOperationException("PDF object not written");
                x.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }
            x.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root ").Append(catalog).Append(" 0 R /Info ").Append(info).Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            Write(x.ToString());
        }

        // A text string in UTF-16 with its byte order mark, as hex.
        static string Utf16(string s)
        {
            StringBuilder sb = new StringBuilder("<FEFF");
            foreach (char c in s) sb.Append(((int)c).ToString("X4"));
            return sb.Append('>').ToString();
        }

        // Type 0 font over a CID TrueType font: two-byte codes that are the glyph numbers, the subset embedded, and a map
        // from glyphs back to text so it can be selected and copied.
        void WriteFont(Font f)
        {
            MdPdfFont t = f.Ttf;
            if (!f.Used.ContainsKey(0)) f.Used[0] = null;
            byte[] sub = t.Subset(f.Used.Keys);
            int file = Reserve(), desc = Reserve(), cid = Reserve(), map = Reserve();
            Stream(file, "/Length1 " + sub.Length, sub, CompressionLevel.Optimal);
            int flags = 4 | (t.Fixed ? 1 : 0) | (t.ItalicAngle != 0 ? 64 : 0);
            Object(desc, "<< /Type /FontDescriptor /FontName /" + f.Base + " /Flags " + flags + " /FontBBox [" + t.Scale(t.Box[0]) + " " + t.Scale(t.Box[1]) + " " + t.Scale(t.Box[2]) + " " + t.Scale(t.Box[3]) +
                         "] /ItalicAngle " + MdPdfCanvas.Num(t.ItalicAngle) + " /Ascent " + t.Scale(t.Ascent) + " /Descent " + t.Scale(t.Descent) + " /CapHeight " + t.Scale(t.CapHeight) +
                         " /StemV 80 /FontFile2 " + file + " 0 R >>");
            StringBuilder w = new StringBuilder("[");
            int last = -2;
            foreach (ushort g in f.Used.Keys)
            {
                if (g != last + 1) { if (last >= 0) w.Append(']'); w.Append(' ').Append(g).Append(" ["); }
                else w.Append(' ');
                w.Append(t.Width(g));
                last = g;
            }
            if (last >= 0) w.Append(']');
            w.Append(" ]");
            Object(cid, "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /" + f.Base + " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor " + desc +
                        " 0 R /DW 1000 /W " + w + " /CIDToGIDMap /Identity >>");
            Stream(map, "", Encoding.ASCII.GetBytes(ToUnicode(f.Used)), CompressionLevel.Optimal);
            Object(f.Obj, "<< /Type /Font /Subtype /Type0 /BaseFont /" + f.Base + " /Encoding /Identity-H /DescendantFonts [" + cid + " 0 R] /ToUnicode " + map + " 0 R >>");
        }

        static string ToUnicode(SortedDictionary<ushort, string> used)
        {
            List<KeyValuePair<ushort, string>> m = new List<KeyValuePair<ushort, string>>();
            foreach (KeyValuePair<ushort, string> kv in used) if (!string.IsNullOrEmpty(kv.Value) && kv.Value.Length <= 64) m.Add(kv);
            StringBuilder sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
            sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            for (int i = 0; i < m.Count; i += 100)
            {
                int k = Math.Min(100, m.Count - i);
                sb.Append(k).Append(" beginbfchar\n");
                for (int j = i; j < i + k; j++)
                {
                    sb.Append('<').Append(m[j].Key.ToString("X4")).Append("> <");
                    foreach (char c in m[j].Value) sb.Append(((int)c).ToString("X4"));
                    sb.Append(">\n");
                }
                sb.Append("endbfchar\n");
            }
            sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            return sb.ToString();
        }

        // The headings as nested bookmarks, each opening its page at the top. The outline dictionary's number, or 0.
        int WriteBookmarks(List<int> pageObjs)
        {
            if (marks == null || marks.Count == 0) return 0;
            int root = Reserve();
            int[] obj = new int[marks.Count], parent = new int[marks.Count];
            for (int i = 0; i < marks.Count; i++) obj[i] = Reserve();
            // A bookmark's parent: the nearest one before it at a lower level.
            for (int i = 0; i < marks.Count; i++)
            {
                parent[i] = -1;
                for (int j = i - 1; j >= 0; j--) if (marks[j].Level < marks[i].Level) { parent[i] = j; break; }
            }
            List<int>[] kids = new List<int>[marks.Count];
            List<int> top = new List<int>();
            for (int i = 0; i < marks.Count; i++)
            {
                if (parent[i] < 0) top.Add(i);
                else { if (kids[parent[i]] == null) kids[parent[i]] = new List<int>(); kids[parent[i]].Add(i); }
            }
            for (int i = 0; i < marks.Count; i++)
            {
                List<int> sibs = parent[i] < 0 ? top : kids[parent[i]];
                int at = sibs.IndexOf(i);
                StringBuilder sb = new StringBuilder("<< /Title ").Append(Utf16(marks[i].Title)).Append(" /Parent ").Append(parent[i] < 0 ? root : obj[parent[i]]).Append(" 0 R");
                if (at > 0) sb.Append(" /Prev ").Append(obj[sibs[at - 1]]).Append(" 0 R");
                if (at < sibs.Count - 1) sb.Append(" /Next ").Append(obj[sibs[at + 1]]).Append(" 0 R");
                if (kids[i] != null) sb.Append(" /First ").Append(obj[kids[i][0]]).Append(" 0 R /Last ").Append(obj[kids[i][kids[i].Count - 1]]).Append(" 0 R /Count -").Append(Descendants(i, kids));
                int page = Math.Max(0, Math.Min(pageObjs.Count - 1, marks[i].Page));
                sb.Append(" /Dest [").Append(pageObjs[page]).Append(" 0 R /XYZ null ").Append(MdPdfCanvas.Num(height * 0.75)).Append(" null] >>");
                Object(obj[i], sb.ToString());
            }
            Object(root, "<< /Type /Outlines /First " + obj[top[0]] + " 0 R /Last " + obj[top[top.Count - 1]] + " 0 R /Count " + top.Count + " >>");
            return root;
        }

        static int Descendants(int i, List<int>[] kids)
        {
            if (kids[i] == null) return 0;
            int n = 0;
            foreach (int k in kids[i]) n += 1 + Descendants(k, kids);
            return n;
        }
    }

    // One page's drawing as PDF operators: WPF's visuals walked with their transforms, clips and opacity. Shapes become
    // paths, glyph runs become text in the embedded fonts (or their outlines when a font cannot be embedded), pictures
    // become image objects. Every item is drawn on its own (no nested states), in WPF's units under one page transform.
    internal sealed class MdPdfCanvas
    {
        readonly MdPdfFile pdf;
        readonly StringBuilder sb = new StringBuilder(1 << 16);
        static readonly Rect All = new Rect(-1e6, -1e6, 2e6, 2e6);

        public MdPdfCanvas(MdPdfFile pdf, double height)
        {
            this.pdf = pdf;
            // WPF units (1/96 inch, y down) to points (1/72 inch, y up); restored at the end, as a page may have more streams.
            sb.Append("q\n0.75 0 0 -0.75 0 ").Append(Num(height * 0.75)).Append(" cm\n");
        }

        public byte[] Bytes() { return Encoding.ASCII.GetBytes(sb.ToString() + "Q\n"); }

        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            double r = Math.Round(v, 3);
            if (r == 0) return "0";
            return r.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public void Visual(Visual v) { Visual(v, Matrix.Identity, All, 1); }

        void Visual(Visual v, Matrix parent, Rect clip, double opacity)
        {
            UIElement ue = v as UIElement;
            if (ue != null && ue.Visibility != Visibility.Visible) return;
            Matrix m = Matrix.Identity;
            Transform t = VisualTreeHelper.GetTransform(v);
            if (t != null) m = t.Value;
            Vector off = VisualTreeHelper.GetOffset(v);
            m.Translate(off.X, off.Y);
            m = m * parent;
            double op = opacity * VisualTreeHelper.GetOpacity(v);
            if (op <= 0.004) return;
            Geometry vc = VisualTreeHelper.GetClip(v);
            if (vc != null) clip = Clip(clip, vc, m);
            if (clip.IsEmpty) return;
            DrawingGroup dg = VisualTreeHelper.GetDrawing(v);
            if (dg != null) Drawing(dg, m, clip, op);
            int n = VisualTreeHelper.GetChildrenCount(v);
            for (int i = 0; i < n; i++)
            {
                Visual c = VisualTreeHelper.GetChild(v, i) as Visual;
                if (c != null) Visual(c, m, clip, op);
            }
        }

        static Rect Clip(Rect clip, Geometry g, Matrix m)
        {
            Rect b = g.Bounds;
            if (b.IsEmpty) return Rect.Empty;
            b.Transform(m);
            return Rect.Intersect(clip, b);
        }

        void Drawing(Drawing d, Matrix m, Rect clip, double op)
        {
            DrawingGroup g = d as DrawingGroup;
            if (g != null)
            {
                double o = op * g.Opacity;
                if (o <= 0.004) return;
                // A group's clip is in its parent's space; its transform applies to what it holds.
                if (g.ClipGeometry != null) { clip = Clip(clip, g.ClipGeometry, m); if (clip.IsEmpty) return; }
                Matrix mm = g.Transform != null ? g.Transform.Value * m : m;
                foreach (Drawing c in g.Children) Drawing(c, mm, clip, o);
                return;
            }
            GeometryDrawing gd = d as GeometryDrawing;
            if (gd != null) { Shape(gd.Geometry, gd.Brush, gd.Pen, m, clip, op); return; }
            GlyphRunDrawing gr = d as GlyphRunDrawing;
            if (gr != null) { Glyphs(gr.GlyphRun, gr.ForegroundBrush, m, clip, op); return; }
            ImageDrawing id = d as ImageDrawing;
            if (id != null) Picture(id.ImageSource, id.Rect, m, clip, op);
        }

        // A brush as an opaque colour over white; false when it paints nothing (or is not a colour).
        static bool Rgb(Brush b, double op, out double r, out double g, out double bl)
        {
            r = g = bl = 1;
            if (b == null) return false;
            Color c;
            SolidColorBrush s = b as SolidColorBrush;
            GradientBrush gb = b as GradientBrush;
            if (s != null) c = s.Color;
            else if (gb != null && gb.GradientStops.Count > 0) c = gb.GradientStops[0].Color;
            else return false;
            double a = c.A / 255.0 * b.Opacity * op;
            if (a <= 0.004) return false;
            a = Math.Min(1, a);
            r = 1 - a + a * c.R / 255.0;
            g = 1 - a + a * c.G / 255.0;
            bl = 1 - a + a * c.B / 255.0;
            return true;
        }

        void Color(double r, double g, double b, bool stroke)
        {
            sb.Append(Num(r)).Append(' ').Append(Num(g)).Append(' ').Append(Num(b)).Append(stroke ? " RG\n" : " rg\n");
        }

        // Limits what follows to the clip when the item is not wholly inside it (in page units: before any cm).
        void ClipTo(Rect clip, Rect item)
        {
            if (clip.Contains(item)) return;
            sb.Append(Num(clip.X)).Append(' ').Append(Num(clip.Y)).Append(' ').Append(Num(clip.Width)).Append(' ').Append(Num(clip.Height)).Append(" re W n\n");
        }

        void Concat(Matrix m)
        {
            if (m.IsIdentity) return;
            sb.Append(Num(m.M11)).Append(' ').Append(Num(m.M12)).Append(' ').Append(Num(m.M21)).Append(' ').Append(Num(m.M22)).Append(' ').Append(Num(m.OffsetX)).Append(' ').Append(Num(m.OffsetY)).Append(" cm\n");
        }

        void Shape(Geometry geo, Brush brush, Pen pen, Matrix m, Rect clip, double op)
        {
            if (geo == null) return;
            double fr, fg, fb, sr = 0, sg = 0, sbl = 0;
            bool fill = Rgb(brush, op, out fr, out fg, out fb);
            bool stroke = pen != null && pen.Thickness > 0 && Rgb(pen.Brush, op, out sr, out sg, out sbl);
            if (!fill && !stroke) return;
            Rect b = geo.Bounds;
            if (b.IsEmpty) return;
            if (stroke) b.Inflate(pen.Thickness, pen.Thickness);
            b.Transform(m);
            if (!clip.IntersectsWith(b)) return;
            sb.Append("q\n");
            ClipTo(clip, b);
            Concat(m);
            if (fill) Color(fr, fg, fb, false);
            if (stroke)
            {
                Color(sr, sg, sbl, true);
                sb.Append(Num(pen.Thickness)).Append(" w ").Append(pen.StartLineCap == PenLineCap.Round ? 1 : pen.StartLineCap == PenLineCap.Square ? 2 : 0).Append(" J ")
                  .Append(pen.LineJoin == PenLineJoin.Round ? 1 : pen.LineJoin == PenLineJoin.Bevel ? 2 : 0).Append(" j\n");
                if (pen.DashStyle != null && pen.DashStyle.Dashes != null && pen.DashStyle.Dashes.Count > 0)
                {
                    sb.Append('[');
                    foreach (double dsh in pen.DashStyle.Dashes) sb.Append(Num(Math.Max(0, dsh * pen.Thickness))).Append(' ');
                    sb.Append("] ").Append(Num(pen.DashStyle.Offset * pen.Thickness)).Append(" d\n");
                }
            }
            bool evenOdd = Path(geo);
            sb.Append(fill && stroke ? (evenOdd ? "B*" : "B") : fill ? (evenOdd ? "f*" : "f") : "S").Append("\nQ\n");
        }

        // The geometry as a path (lines and curves; arcs flattened). True when it fills even-odd.
        bool Path(Geometry geo)
        {
            RectangleGeometry rg = geo as RectangleGeometry;
            if (rg != null && rg.RadiusX == 0 && rg.RadiusY == 0 && (rg.Transform == null || rg.Transform.Value.IsIdentity))
            {
                Rect r = rg.Rect;
                if (!r.IsEmpty) sb.Append(Num(r.X)).Append(' ').Append(Num(r.Y)).Append(' ').Append(Num(r.Width)).Append(' ').Append(Num(r.Height)).Append(" re\n");
                return false;
            }
            PathGeometry p = PathGeometry.CreateFromGeometry(geo);
            bool arcs = false;
            foreach (PathFigure f in p.Figures) foreach (PathSegment s in f.Segments) if (s is ArcSegment) arcs = true;
            if (arcs) p = geo.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
            Matrix t = p.Transform != null ? p.Transform.Value : Matrix.Identity;
            foreach (PathFigure f in p.Figures)
            {
                Point cur = t.Transform(f.StartPoint);
                Move(cur);
                foreach (PathSegment s in f.Segments)
                {
                    LineSegment ls = s as LineSegment;
                    if (ls != null) { cur = t.Transform(ls.Point); Line(cur); continue; }
                    PolyLineSegment pl = s as PolyLineSegment;
                    if (pl != null) { foreach (Point q in pl.Points) { cur = t.Transform(q); Line(cur); } continue; }
                    BezierSegment bz = s as BezierSegment;
                    if (bz != null) { cur = t.Transform(bz.Point3); Curve(t.Transform(bz.Point1), t.Transform(bz.Point2), cur); continue; }
                    PolyBezierSegment pb = s as PolyBezierSegment;
                    if (pb != null)
                    {
                        for (int i = 0; i + 2 < pb.Points.Count; i += 3) { cur = t.Transform(pb.Points[i + 2]); Curve(t.Transform(pb.Points[i]), t.Transform(pb.Points[i + 1]), cur); }
                        continue;
                    }
                    QuadraticBezierSegment qb = s as QuadraticBezierSegment;
                    if (qb != null) { Point c = t.Transform(qb.Point1), e = t.Transform(qb.Point2); Quad(cur, c, e); cur = e; continue; }
                    PolyQuadraticBezierSegment pq = s as PolyQuadraticBezierSegment;
                    if (pq != null)
                    {
                        for (int i = 0; i + 1 < pq.Points.Count; i += 2) { Point c = t.Transform(pq.Points[i]), e = t.Transform(pq.Points[i + 1]); Quad(cur, c, e); cur = e; }
                    }
                }
                if (f.IsClosed) sb.Append("h\n");
            }
            return p.FillRule == FillRule.EvenOdd;
        }

        void Move(Point p) { sb.Append(Num(p.X)).Append(' ').Append(Num(p.Y)).Append(" m\n"); }
        void Line(Point p) { sb.Append(Num(p.X)).Append(' ').Append(Num(p.Y)).Append(" l\n"); }

        void Curve(Point a, Point b, Point c)
        {
            sb.Append(Num(a.X)).Append(' ').Append(Num(a.Y)).Append(' ').Append(Num(b.X)).Append(' ').Append(Num(b.Y)).Append(' ').Append(Num(c.X)).Append(' ').Append(Num(c.Y)).Append(" c\n");
        }

        void Quad(Point p0, Point c, Point e)
        {
            Curve(new Point(p0.X + 2.0 / 3 * (c.X - p0.X), p0.Y + 2.0 / 3 * (c.Y - p0.Y)), new Point(e.X + 2.0 / 3 * (c.X - e.X), e.Y + 2.0 / 3 * (c.Y - e.Y)), e);
        }

        void Glyphs(GlyphRun run, Brush brush, Matrix m, Rect clip, double op)
        {
            if (run == null || run.GlyphIndices == null || run.GlyphIndices.Count == 0) return;
            double r, g, b;
            if (!Rgb(brush, op, out r, out g, out b)) return;
            IList<ushort> gl = run.GlyphIndices;
            IList<double> adv = run.AdvanceWidths;
            IList<Point> offs = run.GlyphOffsets;
            double em = run.FontRenderingEmSize, wsum = 0;
            for (int i = 0; i < adv.Count; i++) wsum += adv[i];
            bool rtl = (run.BidiLevel & 1) == 1;
            Point o = run.BaselineOrigin;
            Rect bounds = new Rect(rtl ? o.X - wsum : o.X, o.Y - em * 1.3, Math.Max(0.5, wsum), em * 1.8);
            bounds.Transform(m);
            if (!clip.IntersectsWith(bounds)) return;
            MdPdfFile.Font f;
            string name = pdf.FontName(run.GlyphTypeface, out f);
            sb.Append("q\n");
            ClipTo(clip, bounds);
            Concat(m);
            Color(r, g, b, false);
            if (name == null)
            {
                // No font to embed: the glyphs' outlines, so the page still looks right.
                Path(run.BuildGeometry());
                sb.Append("f\nQ\n");
                return;
            }
            Text(run, f);
            bool plain = !rtl;
            if (offs != null) foreach (Point q in offs) if (q.X != 0 || q.Y != 0) { plain = false; break; }
            sb.Append("BT\n/").Append(name).Append(' ').Append(Num(em)).Append(" Tf\n");
            if (plain)
            {
                // One run of glyphs, nudged where WPF placed them differently from the font's own advances.
                sb.Append("1 0 0 -1 ").Append(Num(o.X)).Append(' ').Append(Num(o.Y)).Append(" Tm\n[<");
                for (int i = 0; i < gl.Count; i++)
                {
                    sb.Append(gl[i].ToString("X4"));
                    if (i == gl.Count - 1) break;
                    double k = pdf.GlyphWidth(f, gl[i]) - adv[i] / em * 1000;
                    if (Math.Abs(k) >= 0.05) sb.Append("> ").Append(Num(Math.Round(k, 2))).Append(" <");
                }
                sb.Append(">] TJ\n");
            }
            else
            {
                double x = o.X;
                for (int i = 0; i < gl.Count; i++)
                {
                    double a = i < adv.Count ? adv[i] : 0;
                    Point d = offs != null && i < offs.Count ? offs[i] : new Point();
                    double gx = rtl ? x - a - d.X : x + d.X;
                    sb.Append("1 0 0 -1 ").Append(Num(gx)).Append(' ').Append(Num(o.Y - d.Y)).Append(" Tm <").Append(gl[i].ToString("X4")).Append("> Tj\n");
                    x += rtl ? -a : a;
                }
            }
            sb.Append("ET\nQ\n");
        }

        // Which text each glyph stands for (for copying from the PDF): a cluster's characters go to its first glyph.
        static void Text(GlyphRun run, MdPdfFile.Font f)
        {
            IList<ushort> gl = run.GlyphIndices;
            IList<char> chars = run.Characters;
            IList<ushort> map = run.ClusterMap;
            if (chars == null || chars.Count == 0) { foreach (ushort g in gl) MdPdfFile.Note(f, g, null); return; }
            if (map == null || map.Count != chars.Count)
            {
                for (int i = 0; i < gl.Count; i++) MdPdfFile.Note(f, gl[i], gl.Count == chars.Count ? chars[i].ToString() : null);
                return;
            }
            int ci = 0;
            while (ci < chars.Count)
            {
                int g0 = map[ci], cj = ci + 1;
                while (cj < chars.Count && map[cj] == g0) cj++;
                int g1 = cj < chars.Count ? map[cj] : gl.Count;
                StringBuilder s = new StringBuilder();
                for (int k = ci; k < cj; k++) s.Append(chars[k]);
                if (g0 < gl.Count) MdPdfFile.Note(f, gl[g0], s.ToString());
                for (int k = g0 + 1; k < g1 && k < gl.Count; k++) MdPdfFile.Note(f, gl[k], null);
                ci = cj;
            }
            foreach (ushort g in gl) MdPdfFile.Note(f, g, null);
        }

        void Picture(ImageSource src, Rect rect, Matrix m, Rect clip, double op)
        {
            if (src == null || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;
            DrawingImage di = src as DrawingImage;
            if (di != null)
            {
                Rect db = di.Drawing.Bounds;
                if (db.IsEmpty || db.Width <= 0 || db.Height <= 0) return;
                Matrix k = new Matrix(rect.Width / db.Width, 0, 0, rect.Height / db.Height, rect.X - db.X * rect.Width / db.Width, rect.Y - db.Y * rect.Height / db.Height);
                Drawing(di.Drawing, k * m, clip, op);
                return;
            }
            BitmapSource bmp = src as BitmapSource;
            if (bmp == null) return;
            Rect bb = rect;
            bb.Transform(m);
            if (!clip.IntersectsWith(bb)) return;
            string name = pdf.ImageName(bmp);
            if (name == null) return;
            sb.Append("q\n");
            ClipTo(clip, bb);
            Concat(m);
            sb.Append(Num(rect.Width)).Append(" 0 0 ").Append(Num(-rect.Height)).Append(' ').Append(Num(rect.X)).Append(' ').Append(Num(rect.Y + rect.Height)).Append(" cm /").Append(name).Append(" Do\nQ\n");
        }
    }
}

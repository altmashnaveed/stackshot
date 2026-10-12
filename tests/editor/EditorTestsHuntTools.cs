// hunttools: bug hunt over the image editor (added 2026-10-10): marks at the edges, pixelation after a frame change,
// counters after deletes and undo, undo depth, nudges, crop limits, PDF structure of tall and odd images, and a save
// next to a read-only file.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static readonly BindingFlags AnyInstance = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        // Calls a private or public method of the canvas; exceptions come out unwrapped.
        static void CallCanvas(object o, string name, params object[] args)
        {
            MethodInfo mi = o.GetType().GetMethod(name, AnyInstance);
            Check(mi != null, "no method " + name);
            try { mi.Invoke(o, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        // The live list of marks of a canvas (it is replaced by undo and redo, so read it again each time).
        static List<Shape> MarksOf(Canvas c)
        {
            return (List<Shape>)typeof(Canvas).GetField("shapes", AnyInstance).GetValue(c);
        }

        static Bitmap Gradient(int w, int h)
        {
            int[] px = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = unchecked((int)0xFF000000) | (((x * 7 + y) & 255) << 16) | (((y * 3) & 255) << 8) | ((x + y * 5) & 255);
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(px, 0, bd.Scan0, px.Length); }
            finally { b.UnlockBits(bd); }
            return b;
        }

        static Bitmap Solid(int w, int h, Color c)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        static string Latin(byte[] b) { return Encoding.GetEncoding(28591).GetString(b); }

        // Checks the PDF structure: the xref offsets, the stream lengths, the pages and the rows of every image.
        static void CheckPdf(string path, Bitmap src)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string s = Latin(bytes);
            Match sx = Regex.Match(s, @"startxref\n(\d+)\n%%EOF\n$");
            Check(sx.Success, "no startxref at the end");
            int xref = int.Parse(sx.Groups[1].Value);
            Check(s.Substring(xref, 4) == "xref", "startxref points at " + xref);
            Match hm = Regex.Match(s.Substring(xref), @"^xref\n0 (\d+)\n");
            Check(hm.Success, "xref header");
            int count = int.Parse(hm.Groups[1].Value);
            int pos = xref + hm.Length;
            for (int i = 1; i < count; i++)
            {
                string entry = s.Substring(pos + 20 * i, 20);
                long off = long.Parse(entry.Substring(0, 10));
                string wantHead = i + " 0 obj";
                string gotHead = s.Substring((int)off, wantHead.Length);
                Check(gotHead == wantHead, "xref entry " + i + " points at \"" + gotHead + "\"");
            }
            // Every stream: /Length is the real length.
            foreach (Match m in Regex.Matches(s, @"/Length (\d+) >>\nstream\n"))
            {
                int start = m.Index + m.Length;
                int end = s.IndexOf("\nendstream", start, StringComparison.Ordinal);
                Check(end >= 0 && end - start == int.Parse(m.Groups[1].Value), "stream length " + m.Groups[1].Value + " is " + (end - start));
            }
            int pages = Regex.Matches(s, @"/Type /Page /Parent").Count;
            Check(pages >= 1 && count == 3 + 3 * pages, "xref size " + count + " for " + pages + " pages");
            Check(Regex.Matches(s, @"/Count " + pages + " >>").Count == 1, "page count in the page tree is not " + pages);
            double sumH = 0;
            foreach (Match m in Regex.Matches(s, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]"))
            {
                double ph = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                Check(ph > 0, "a page of height 0");
                sumH += ph * src.Width / 595.28;
            }
            Check(Math.Abs(sumH - src.Height) <= pages * 0.01 * src.Width / 595.28 + 1, "page heights add up to " + sumH.ToString("0.0") + ", image is " + src.Height);
            // Pixels: the images, in order, hold every row of the picture once.
            MemoryStream all = new MemoryStream();
            int imgs = 0, sumRows = 0;
            foreach (Match m in Regex.Matches(s, @"/Type /XObject /Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length (\d+) >>\nstream\n"))
            {
                imgs++;
                Check(int.Parse(m.Groups[1].Value) == src.Width, "image width " + m.Groups[1].Value);
                int rows = int.Parse(m.Groups[2].Value);
                sumRows += rows;
                int len = int.Parse(m.Groups[3].Value);
                int start = m.Index + m.Length;
                using (DeflateStream ds = new DeflateStream(new MemoryStream(bytes, start + 2, len - 6), CompressionMode.Decompress))
                {
                    byte[] buf = new byte[rows * src.Width * 3];
                    int got = 0, r;
                    while ((r = ds.Read(buf, got, buf.Length - got)) > 0) got += r;
                    Check(got == buf.Length, "image of " + rows + " rows inflates to " + got + " bytes");
                    all.Write(buf, 0, buf.Length);
                }
            }
            Check(imgs == pages, "images " + imgs + ", pages " + pages);
            Check(sumRows == src.Height, "rows in the images " + sumRows + ", image is " + src.Height + " high");
            byte[] want = new byte[src.Width * src.Height * 3];
            int o = 0;
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Color c = src.GetPixel(x, y);
                    want[o++] = c.R; want[o++] = c.G; want[o++] = c.B;
                }
            byte[] have = all.ToArray();
            Check(have.Length == want.Length, "pixel bytes " + have.Length + " vs " + want.Length);
            for (int i = 0; i < want.Length; i++)
                if (have[i] != want[i]) throw new TestFailed("pixel byte " + i + " differs (row " + (i / 3 / src.Width) + ")");
        }

        static void HuntToolsSuite()
        {
            Test("hunttools", "marks-at-the-edges-draw-and-render", delegate
            {
                Tool[] tools = { Tool.Rect, Tool.Ellipse, Tool.Arrow, Tool.Line, Tool.Highlight, Tool.Pixelate, Tool.Redact, Tool.Spotlight, Tool.Counter };
                RectangleF[] boxes = {
                    new RectangleF(-30, -30, 45, 45), new RectangleF(185, 185, 75, 75), new RectangleF(-10, 90, 220, 20),
                    new RectangleF(90, -20, 20, 240), new RectangleF(0, 0, 200, 200), new RectangleF(199, 199, 1, 1)
                };
                int drawn = 0;
                foreach (Tool t in tools)
                    foreach (RectangleF r in boxes)
                    {
                        Shape s = Box(t, r.X, r.Y, r.Right, r.Bottom);
                        if (t == Tool.Counter) { s.A = new PointF(r.X, r.Y); s.Number = 1; s.FontPx = 18; }
                        using (Bitmap img = White(200, 200))
                        using (Bitmap b = White(200, 200))
                        {
                            using (Graphics g = Graphics.FromImage(b)) Painter.DrawMarks(g, new List<Shape> { s }, null, null, img, new RectangleF(0, 0, 200, 200));
                            drawn++;
                            s.DropCache();
                        }
                    }
                Add("drawn " + drawn);
                // The same marks on a canvas, rendered whole: the size is the crop size.
                Canvas c = new Canvas(White(200, 200));
                foreach (Tool t in tools)
                {
                    Shape s = Box(t, -20, 150, 230, 260);
                    if (t == Tool.Counter) { s.A = new PointF(0, 0); s.Number = 1; s.FontPx = 18; }
                    MarksOf(c).Add(s);
                }
                using (Bitmap r = c.Render())
                    Check(r.Width == 200 && r.Height == 200, "render size " + r.Width + "x" + r.Height);
            });

            Test("hunttools", "pixelate-after-frame-change", delegate
            {
                // The video editor swaps the frame under the marks; a pixelated area must show the new frame.
                Bitmap a = Solid(200, 200, Color.Black);
                using (Graphics g = Graphics.FromImage(a)) g.FillRectangle(Brushes.White, 100, 0, 100, 200);
                Bitmap b = Solid(200, 200, Color.FromArgb(255, 0, 200, 0));
                Canvas c = new Canvas(a);
                MarksOf(c).Add(Box(Tool.Pixelate, 50, 50, 150, 150));
                using (Bitmap before = c.Render()) Add("before " + before.GetPixel(100, 100));
                c.ReplaceImage(b);
                Color got;
                using (Bitmap after = c.Render()) got = after.GetPixel(100, 100);
                Canvas fresh = new Canvas(Solid(200, 200, Color.FromArgb(255, 0, 200, 0)));
                MarksOf(fresh).Add(Box(Tool.Pixelate, 50, 50, 150, 150));
                Color want;
                using (Bitmap f = fresh.Render()) want = f.GetPixel(100, 100);
                Add("after frame change " + got + ", fresh " + want);
                Check(Math.Abs(got.G - want.G) <= 3 && Math.Abs(got.R - want.R) <= 3, "the pixelated area still shows the old frame");
            });

            Test("hunttools", "counters-delete-undo-numbers", delegate
            {
                Canvas c = new Canvas(White(400, 300));
                for (int i = 0; i < 3; i++) CallCanvas(c, "AddCounter", new PointF(50 + i * 60, 50));
                Check(MarksOf(c)[2].Number == 3, "third counter is " + MarksOf(c)[2].Number);
                c.SelectShape(MarksOf(c)[1]);
                c.DeleteSelected();
                List<Shape> l = MarksOf(c);
                Check(l.Count == 2 && l[0].Number == 1 && l[1].Number == 2, "after delete: " + Nums(l));
                c.Undo();
                l = MarksOf(c);
                Check(l.Count == 3 && Nums(l) == "1,2,3" && l[1].A.X == 110, "undo of delete: " + Nums(l) + " at " + (l.Count > 1 ? l[1].A.X : -1));
                c.Redo();
                l = MarksOf(c);
                Check(l.Count == 2 && Nums(l) == "1,2", "redo of delete: " + Nums(l));
                c.Undo();
                c.SelectShape(MarksOf(c)[0]);
                c.DeleteSelected();
                Check(Nums(MarksOf(c)) == "1,2", "delete first: " + Nums(MarksOf(c)));
                CallCanvas(c, "AddCounter", new PointF(200, 200));
                Check(Nums(MarksOf(c)) == "1,2,3", "next counter after delete: " + Nums(MarksOf(c)));
                c.SelectShape(MarksOf(c)[2]);
                c.DuplicateSelected();
                Check(Nums(MarksOf(c)) == "1,2,3,4", "duplicate counter: " + Nums(MarksOf(c)));
            });

            Test("hunttools", "undo-depth-and-redo-after-many", delegate
            {
                Canvas c = new Canvas(White(200, 200));
                for (int i = 0; i < 120; i++)
                {
                    CallCanvas(c, "Commit");
                    MarksOf(c).Add(Box(Tool.Rect, i % 150, 0, i % 150 + 10, 10));
                }
                Check(MarksOf(c).Count == 120, "marks " + MarksOf(c).Count);
                for (int i = 0; i < 100; i++) c.Undo();
                Check(MarksOf(c).Count == 20, "after 100 undos: " + MarksOf(c).Count + " (undo keeps 100 steps)");
                Check(!c.CanUndo, "still undoable after 100 undos");
                for (int i = 0; i < 100; i++) c.Redo();
                Check(MarksOf(c).Count == 120, "after 100 redos: " + MarksOf(c).Count);
                Check(!c.CanRedo, "still redoable");
                // A new change after an undo drops the redo steps.
                c.Undo();
                CallCanvas(c, "Commit");
                Check(!c.CanRedo, "redo kept after a new change");
            });

            Test("hunttools", "nudge-taps-are-one-step", delegate
            {
                Canvas c = new Canvas(White(200, 200));
                MarksOf(c).Add(Box(Tool.Rect, 20, 20, 60, 60));
                c.SelectShape(MarksOf(c)[0]);
                c.Nudge(1, 0);
                c.Nudge(1, 0);
                c.Nudge(0, 1);
                Shape s = MarksOf(c)[0];
                Check(s.A.X == 22 && s.A.Y == 21, "nudged to " + s.A);
                c.Undo();
                s = MarksOf(c)[0];
                Check(s.A.X == 20 && s.A.Y == 20, "one undo should bring back the start, got " + s.A);
                Check(!c.CanUndo, "the nudges were more than one step");
                c.Redo();
                Check(MarksOf(c)[0].A.X == 22 && MarksOf(c)[0].A.Y == 21, "redo of nudges");
            });

            Test("hunttools", "crop-limits-and-undo", delegate
            {
                Canvas c = new Canvas(White(400, 300));
                CallCanvas(c, "ApplyCrop", new RectangleF(10, 10, 1, 1));
                Check(c.Crop == new RectangleF(0, 0, 400, 300), "a 1x1 crop was applied: " + c.Crop);
                CallCanvas(c, "ApplyCrop", new RectangleF(10, 10, 7, 50));
                Check(c.Crop == new RectangleF(0, 0, 400, 300), "a 7 px wide crop was applied: " + c.Crop);
                CallCanvas(c, "ApplyCrop", new RectangleF(10, 10, 8, 8));
                Check(c.Crop == new RectangleF(10, 10, 8, 8), "8x8 crop: " + c.Crop);
                using (Bitmap r = c.Render()) Check(r.Width == 8 && r.Height == 8, "render of 8x8 crop is " + r.Width + "x" + r.Height);
                c.Undo();
                Check(c.Crop == new RectangleF(0, 0, 400, 300), "undo of the crop: " + c.Crop);
                CallCanvas(c, "ApplyCrop", new RectangleF(390, 290, 50, 50));
                Check(c.Crop == new RectangleF(390, 290, 10, 10), "a crop past the edge: " + c.Crop);
            });

            Test("hunttools", "pixelate-tiny-regions", delegate
            {
                using (Bitmap img = White(200, 200))
                {
                    Check(Painter.Pixelate(img, new Rectangle(0, 0, 1, 1)) == null, "1x1 pixelate gave a bitmap");
                    Check(Painter.Pixelate(img, new Rectangle(199, 199, 5, 5)) == null, "1px pixelate at the corner");
                    Check(Painter.Pixelate(img, new Rectangle(500, 500, 50, 50)) == null, "pixelate outside the image");
                    using (Bitmap p = Painter.Pixelate(img, new Rectangle(190, 190, 50, 50)))
                        Check(p != null && p.Width == 10 && p.Height == 10, "corner pixelate size");
                }
            });

            Test("hunttools", "pdf-tall-and-odd-images", delegate
            {
                int[][] sizes = { new[] { 300, 6000 }, new[] { 5000, 100 }, new[] { 3, 50 }, new[] { 600, 9000 }, new[] { 200, 1 }, new[] { 40, 40 }, new[] { 120, 2500 } };
                foreach (int[] sz in sizes)
                {
                    string dir = WorkDir("pdf");
                    string file = Path.Combine(dir, "captura.pdf");
                    using (Bitmap src = Gradient(sz[0], sz[1]))
                    {
                        Pdf.Save(src, file);
                        CheckPdf(file, src);
                    }
                    Add(sz[0] + "x" + sz[1] + " ok");
                }
            });

            Test("hunttools", "save-next-to-read-only-file", delegate
            {
                string dir = WorkDir("ro");
                string target = Path.Combine(dir, "captura.png");
                using (Bitmap first = Solid(20, 20, Color.Blue)) first.Save(target, ImageFormat.Png);
                byte[] before = File.ReadAllBytes(target);
                File.SetAttributes(target, FileAttributes.ReadOnly);
                try
                {
                    bool threw = false;
                    string kind = "";
                    using (Bitmap red = Solid(30, 30, Color.Red))
                    {
                        try { Editor.Write(red, target); }
                        catch (Exception e) { threw = true; kind = e.GetType().Name; }
                    }
                    Add("threw " + threw + " " + kind);
                    Check(threw, "saving over a read-only file did not report an error");
                    byte[] after = File.ReadAllBytes(target);
                    Check(before.Length == after.Length && Equal(before, after), "the read-only file changed");
                    string[] left = Directory.GetFiles(dir, "*.tmp");
                    Check(left.Length == 0, "temporary files left: " + string.Join(", ", left));
                }
                finally { try { File.SetAttributes(target, FileAttributes.Normal); } catch { } }
            });
        }

        static bool Equal(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string Nums(List<Shape> l)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Shape s in l) if (s.Kind == Tool.Counter) { if (sb.Length > 0) sb.Append(','); sb.Append(s.Number); }
            return sb.ToString();
        }
    }
}

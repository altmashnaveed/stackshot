// Round 6 (Markdown images, PDF, outline, spreadsheet tables), suite "w6md": pictures pasted, dropped and shown in the
// preview, the PDF export (structure, real text, pages, timing, a responsive window), the outline panel and tables pasted
// from spreadsheets. Windows are parked off screen; the clipboard is never touched (PasteSource hands the data in).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static void W6MdSuite()
        {
            Ds.Apply(1);
            Test("w6md", "toolbar-keeps-its-labels-at-the-usual-width", Md6Toolbar);
            Test("w6md", "doc-image-spans-br-and-heading-lines", Md6Doc);
            Test("w6md", "image-links-resolve-and-escape", Md6Links);
            Test("w6md", "html-images-relative-and-safe", Md6Html);
            Test("w6md", "paste-image-into-saved-doc", Md6PasteSaved);
            Test("w6md", "paste-bitmap-and-unique-names", Md6PasteBitmap);
            Test("w6md", "paste-image-unsaved-asks-first", Md6PasteUnsaved);
            Test("w6md", "drop-image-files", Md6Drop);
            Test("w6md", "preview-shows-image-and-placeholder", Md6Preview);
            Test("w6md", "table-tsv-excel-quoting", Md6Tsv);
            Test("w6md", "table-csv-quoting", Md6Csv);
            Test("w6md", "table-html-merged-and-empty", Md6HtmlTable);
            Test("w6md", "table-markdown-escapes-unicode", Md6TableMarkdown);
            Test("w6md", "table-clipboard-detection", Md6Detect);
            Test("w6md", "table-paste-in-editor", Md6TablePasteWindow);
            Test("w6md", "table-paste-big-sheet-off-the-ui-thread", Md6BigSheet);
            Test("w6md", "pictures-follow-save-as-and-stay-small", Md6SaveAsPictures);
            Test("w6md", "outline-extraction", Md6Outline);
            Test("w6md", "outline-panel-toggle-and-updates", Md6OutlinePanel);
            Test("w6md", "outline-jump-and-current-section", Md6OutlineJump);
            Test("w6md", "pdf-structure-text-and-pages", Md6PdfExport);
            Test("w6md", "pdf-headings-not-left-alone", Md6PdfKeep);
            Test("w6md", "pdf-long-document-timing", Md6PdfTiming);
            Test("w6md", "pdf-window-responsive-and-progress", Md6PdfWindow);
            Test("w6md", "pdf-close-stops-and-leaves-nothing", Md6PdfCancel);
            R5Md();
        }

        // ---- Helpers

        static byte[] Md6Png(int w, int h, Color a, Color b)
        {
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(a, b, 0), null, new Rect(0, 0, w, h));
                dc.DrawEllipse(Brushes.White, null, new Point(w / 2.0, h / 2.0), w / 5.0, h / 4.0);
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream()) { e.Save(ms); return ms.ToArray(); }
        }

        static BitmapSource Md6Decode(byte[] png)
        {
            BitmapDecoder d = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return d.Frames[0];
        }

        // A Markdown document on disk, open in a parked window (editing when asked).
        static MarkdownWindow Md6Open(string dir, string name, string md, bool edit)
        {
            MarkdownWindow w = NewWindow();
            Open(w, Put(dir, name, md));
            if (edit) { w.SetEditing(true); Pump(250); }
            return w;
        }

        static void Md6Caret(MarkdownWindow w, int at)
        {
            w.Editor.Box.Select(at, 0);
        }

        static string Md6Status(MarkdownWindow w) { return Field<string>(w, "status") ?? ""; }

        // A key press handed to the editor's own key handler; whether it took it.
        static bool Md6Key(MarkdownEditor ed, System.Windows.Input.Key k, System.Windows.Input.ModifierKeys mods)
        {
            FakeKeys kb;
            if (!keyboards.TryGetValue(mods, out kb)) { kb = new FakeKeys(mods); keyboards[mods] = kb; }
            System.Windows.Input.KeyEventArgs e = new System.Windows.Input.KeyEventArgs(kb, PresentationSource.FromVisual(ed.Box), 0, k) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            Call(ed, "OnKey", ed.Box, e);
            return e.Handled;
        }

        static List<T> Md6All<T>(DependencyObject root) where T : DependencyObject
        {
            List<T> o = new List<T>();
            Collect(root, o);
            return o;
        }

        // Every picture host (the Border inside an InlineUIContainer) in the preview's document.
        static List<Border> Md6Pictures(MarkdownFlow flow)
        {
            List<Border> o = new List<Border>();
            Md6Walk(flow.Box.Document, o);
            return o;
        }

        static void Md6Walk(DependencyObject d, List<Border> o)
        {
            InlineUIContainer c = d as InlineUIContainer;
            if (c != null && c.Child is Border) { o.Add((Border)c.Child); return; }
            foreach (object k in LogicalTreeHelper.GetChildren(d))
            {
                DependencyObject x = k as DependencyObject;
                if (x != null && !(x is FrameworkElement)) Md6Walk(x, o);
            }
        }

        // The outline's button must not cost the other toolbar buttons their labels at the window's usual width (1000).
        static void Md6Toolbar()
        {
            string dir = WorkDir("toolbar");
            MarkdownWindow w = Md6Open(dir, "a.md", "# A\n", false);
            try
            {
                Check(Math.Abs(w.ActualWidth - 1000) < 1, "window width " + w.ActualWidth);
                StackPanel tools = Field<StackPanel>(w, "tools");
                double full = Field<double>(w, "fullWidth");
                Put("toolbar_width", full);
                Check(!Field<bool>(w, "compact"), "the toolbar lost its labels at 1000 px (needs " + full + ")");
                List<OutlineToggle> t = Md6All<OutlineToggle>(tools);
                Check(t.Count == 1 && t[0].ActualWidth <= 30.5 && t[0].ToolTip is string && ((string)t[0].ToolTip).StartsWith("\u00CDndice", StringComparison.Ordinal), "outline button");
                List<MacButton> labelled = Md6All<MacButton>(tools);
                Check(labelled.Count == 4 && labelled.TrueForAll(delegate(MacButton b) { return b.Label.Length > 0; }), "labels");
                Note("toolbar needs " + full + " px");
            }
            finally { Shut(w); }
        }

        // ---- Parser changes

        static void Md6Doc()
        {
            List<MdSpan> s = MarkdownDoc.Inline("antes ![una foto](imagenes/a%20b.png) despu\u00E9s");
            MdSpan img = s.Find(delegate(MdSpan x) { return x.Image; });
            Check(img != null && img.Href == "imagenes/a%20b.png" && img.Text == "\u25A3 una foto" && img.Italic, "image span: " + (img == null ? "none" : img.Href + " | " + img.Text));
            Check(s.Count == 3 && s[0].Text == "antes " && s[2].Text == " despu\u00E9s", "spans around the image: " + s.Count);
            Same("antes \u25A3 una foto despu\u00E9s", MarkdownDoc.Plain("antes ![una foto](imagenes/a%20b.png) despu\u00E9s"), "plain text unchanged");
            // Two images side by side stay two spans (never merged).
            List<MdSpan> two = MarkdownDoc.Inline("![a](x.png)![b](x.png)");
            Check(two.Count == 2 && two[0].Image && two[1].Image, "two images: " + two.Count);
            // <br> is a line break (tables use it); in code or escaped it is text.
            foreach (string br in new string[] { "a<br>b", "a<br/>b", "a<BR />b" })
            {
                List<MdSpan> b = MarkdownDoc.Inline(br);
                Check(b.Count == 3 && b[1].Break && b[0].Text == "a" && b[2].Text == "b", br + " -> " + b.Count + " spans");
            }
            Check(MarkdownDoc.Plain("`a<br>b`") == "a<br>b", "br inside code: " + MarkdownDoc.Plain("`a<br>b`"));
            Check(MarkdownDoc.Plain("a\\<br>b") == "a<br>b", "escaped br: " + MarkdownDoc.Plain("a\\<br>b"));
            Check(MarkdownDoc.Plain("x <brr> y") == "x <brr> y", "not a br");
            // Top-level headings know their line.
            string md = "# Uno\n\ntexto\n\nDos\n===\n\n- # en lista\n\n> ## en cita\n\n  ### Tres\n";
            List<MdBlock> bs = MarkdownDoc.Parse(md);
            List<int> lines = new List<int>();
            foreach (MdBlock b in bs) if (b.Kind == MdKind.Heading) lines.Add(b.Line);
            Same("0,4,11", string.Join(",", lines.ConvertAll(delegate(int i) { return i.ToString(); }).ToArray()), "heading lines");
            MdBlock inList = bs.Find(delegate(MdBlock b) { return b.Kind == MdKind.List; }).Items[0].Blocks[0];
            Check(inList.Kind == MdKind.Heading && inList.Line == -1, "a heading inside a list has no line: " + inList.Line);
        }

        static void Md6Links()
        {
            string dir = WorkDir("links");
            Same(Path.Combine(dir, "imagenes", "a b.png"), MdImages.LocalPath("imagenes/a%20b.png", dir), "relative with %20");
            Same(Path.Combine(dir, "x.png"), MdImages.LocalPath("./x.png?v=2#frag", dir), "query and fragment cut");
            Same(Path.GetFullPath(Path.Combine(dir, "..", "y.png")), MdImages.LocalPath("../y.png", dir), "parent folder");
            Same(@"C:\fotos\z.png", MdImages.LocalPath("file:///C:/fotos/z.png", dir), "file URI");
            Same(@"C:\fotos\z.png", MdImages.LocalPath("C:/fotos/z.png", dir), "drive path");
            Check(MdImages.LocalPath("https://example.com/a.png", dir) == null, "web address is not local");
            Check(MdImages.LocalPath("data:image/png;base64,AAAA", dir) == null, "data URI is not local");
            Check(MdImages.LocalPath("javascript:alert(1)", dir) == null, "script is not local");
            Check(MdImages.LocalPath("imagenes/a.png", null) == null, "relative without a folder");
            Check(MdImages.LocalPath("", dir) == null && MdImages.LocalPath(null, dir) == null, "empty");
            Same("imagenes/mi%20foto%20%281%29.png", MdImages.Escape(@"imagenes\mi foto (1).png"), "escaped link");
            Same("![](a%5Bb%5D%23c.png)", MdImages.Link("a[b]#c.png"), "brackets and hash");
            Same(Path.Combine(dir, "imagenes", "mi foto (1).png"), MdImages.LocalPath(MdImages.Escape(@"imagenes\mi foto (1).png"), dir), "escape then resolve round-trips");
            // The escaped link survives the parser (no space or parenthesis cuts it).
            List<MdSpan> s = MarkdownDoc.Inline(MdImages.Link("imagenes/mi foto (1).png"));
            Check(s.Count == 1 && s[0].Image && s[0].Href == "imagenes/mi%20foto%20%281%29.png", "parsed link: " + (s.Count > 0 ? s[0].Href : "none"));
            Same("sub/a.png", MdImages.Relative(dir, Path.Combine(dir, "sub", "a.png")), "relative inside");
            Check(MdImages.Relative(Path.Combine(dir, "sub"), Path.Combine(dir, "a.png")) == null, "outside is not relative");
            Check(MdImages.IsImageFile("A.PNG") && MdImages.IsImageFile("b.jpeg") && !MdImages.IsImageFile("c.md") && !MdImages.IsImageFile("noext"), "image extensions");
        }

        static void Md6Html()
        {
            string html = MdHtml.Body("![foto](imagenes/a.png) y ![web](https://x.es/b.png) y ![p](javascript:alert(2)) y ![d](data:image/png;base64,AA==)");
            Check(html.Contains("<img src=\"imagenes/a.png\" alt=\"foto\">"), "relative image: " + html);
            Check(html.Contains("<img src=\"https://x.es/b.png\" alt=\"web\">"), "web image: " + html);
            Check(html.Contains("<img src=\"data:image/png;base64,AA==\""), "data image");
            Check(!html.ToLowerInvariant().Contains("src=\"javascript") && !html.ToLowerInvariant().Contains("href=\"javascript"), "script exported: " + html);
            // In the window: next to the document unchanged, elsewhere relative to the page, in the clipboard absolute.
            string root = WorkDir("htmlimg");
            string doc = Path.Combine(root, "doc");
            Directory.CreateDirectory(Path.Combine(doc, "imagenes"));
            File.WriteAllBytes(Path.Combine(doc, "imagenes", "foto.png"), Md6Png(40, 30, Colors.Red, Colors.Blue));
            MarkdownWindow w = Md6Open(doc, "notas.md", "# Notas\n\n![foto](imagenes/foto.png)\n", false);
            try
            {
                string same = Path.Combine(doc, "notas.html");
                MarkdownWindow.PickPath = delegate(string s) { return same; };
                Check(w.ExportHtml(), "export next to the document");
                string a = File.ReadAllText(same, Encoding.UTF8);
                Check(a.Contains("<img src=\"imagenes/foto.png\" alt=\"foto\">"), "same folder: " + a);
                string other = Path.Combine(root, "fuera", "notas.html");
                Directory.CreateDirectory(Path.GetDirectoryName(other));
                MarkdownWindow.PickPath = delegate(string s) { return other; };
                Check(w.ExportHtml(), "export elsewhere");
                string b = File.ReadAllText(other, Encoding.UTF8);
                Check(b.Contains("<img src=\"../doc/imagenes/foto.png\""), "other folder: " + b);
                Func<string, string> clip = (Func<string, string>)Call(w, "HtmlImages", new object[] { null });
                string abs = clip("imagenes/foto.png");
                Check(abs != null && abs.StartsWith("file:///", StringComparison.Ordinal) && abs.EndsWith("/doc/imagenes/foto.png", StringComparison.Ordinal), "clipboard source: " + abs);
                Check(clip("notas.md") == null, "a local file that is not a picture is not exported as one");
                Check(clip("https://x.es/a.png") == "https://x.es/a.png", "web address kept");
            }
            finally { MarkdownWindow.PickPath = null; Shut(w); }
        }

        // ---- Pasting pictures

        static void Md6PasteSaved()
        {
            string dir = WorkDir("pasteimg");
            byte[] png = Md6Png(120, 80, Colors.SteelBlue, Colors.Orange);
            MarkdownWindow w = Md6Open(dir, "notas.md", "# Notas\n\nAntes  despu\u00E9s\n", true);
            try
            {
                string before = w.Editor.Text;
                int at = before.IndexOf("  despu", StringComparison.Ordinal) + 1;
                Md6Caret(w, at);
                DataObject d = new DataObject();
                d.SetData("PNG", new MemoryStream(png));
                MarkdownWindow.PasteSource = delegate { return d; };
                Check(w.EditorPaste(false), "the picture paste was not handled");
                Check(Until(delegate { return w.Editor.Text.Contains("![](imagenes/imagen-"); }, 5000), "no link inserted: " + w.Editor.Text);
                string t = w.Editor.Text;
                Match m = Regex.Match(t, @"!\[\]\((imagenes/imagen-\d{8}-\d{6}\.png)\)");
                Check(m.Success, "link format: " + t);
                Check(t.IndexOf(m.Value, StringComparison.Ordinal) == at, "not at the caret: " + t);
                string file = Path.Combine(dir, m.Groups[1].Value.Replace('/', '\\'));
                Check(File.Exists(file), "file not written: " + file);
                byte[] got = File.ReadAllBytes(file);
                Check(got.Length == png.Length, "the clipboard's PNG was not kept as it was (" + got.Length + " vs " + png.Length + ")");
                Check(Md6Status(w).StartsWith("Imagen pegada: imagenes/imagen-", StringComparison.Ordinal), "status: " + Md6Status(w));
                w.Editor.Box.Undo();
                Same(before, w.Editor.Text, "one undo step");
                // Text on the clipboard is pasted as text (no picture, no table).
                DataObject txt = new DataObject();
                txt.SetData(DataFormats.UnicodeText, "hola");
                txt.SetData("PNG", new MemoryStream(png));
                MarkdownWindow.PasteSource = delegate { return txt; };
                Check(!w.EditorPaste(false), "text with a picture must go to the TextBox as text");
            }
            finally { MarkdownWindow.PasteSource = null; Shut(w); }
        }

        static void Md6PasteBitmap()
        {
            string dir = WorkDir("pastebmp");
            MarkdownWindow w = Md6Open(dir, "notas.md", "texto\n", true);
            try
            {
                BitmapSource bmp = Md6Decode(Md6Png(64, 48, Colors.Green, Colors.Yellow));
                DataObject d = new DataObject();
                d.SetImage(bmp);
                MarkdownWindow.PasteSource = delegate { return d; };
                Md6Caret(w, w.Editor.Text.Length);
                Check(w.EditorPaste(false), "bitmap paste not handled");
                Check(Until(delegate { return Regex.Matches(w.Editor.Text, @"imagenes/imagen-").Count == 1; }, 5000), "first link missing");
                Check(w.EditorPaste(false), "second paste not handled");
                Check(Until(delegate { return Regex.Matches(w.Editor.Text, @"imagenes/imagen-").Count == 2; }, 5000), "second link missing: " + w.Editor.Text);
                string[] files = Directory.GetFiles(Path.Combine(dir, "imagenes"), "*.png");
                Check(files.Length == 2, "files: " + files.Length);
                foreach (string f in files)
                {
                    BitmapSource b = Md6Decode(File.ReadAllBytes(f));
                    Check(b.PixelWidth == 64 && b.PixelHeight == 48, "encoded size " + b.PixelWidth + "x" + b.PixelHeight);
                    Check(w.Editor.Text.Contains("imagenes/" + Path.GetFileName(f)), "link to " + Path.GetFileName(f));
                }
                // Same second: the name gets a number instead of overwriting.
                string err;
                DateTime when = new DateTime(2026, 10, 12, 3, 15, 0);
                string a = MdImages.SavePasted(dir, Md6Png(8, 8, Colors.Red, Colors.Red), null, when, out err);
                string b2 = MdImages.SavePasted(dir, Md6Png(8, 8, Colors.Blue, Colors.Blue), null, when, out err);
                Same("imagenes/imagen-20261012-031500.png", a, "first name");
                Same("imagenes/imagen-20261012-031500-2.png", b2, "second name");
            }
            finally { MarkdownWindow.PasteSource = null; Shut(w); }
        }

        static void Md6PasteUnsaved()
        {
            string dir = WorkDir("pasteunsaved");
            byte[] png = Md6Png(30, 20, Colors.Purple, Colors.Pink);
            MarkdownWindow w = NewWindow();
            int asked = 0;
            string question = null;
            string[] offered = null;
            Func<string, string, string[], int> keep = DocAlert.Answer;
            try
            {
                Call(w, "LoadText", "# Borrador\n\ntexto\n", null, null);
                Pump(150);
                w.SetEditing(true);
                Pump(250);
                DataObject d = new DataObject();
                d.SetData("PNG", new MemoryStream(png));
                MarkdownWindow.PasteSource = delegate { return d; };
                int answer = 1;
                DocAlert.Answer = delegate(string title, string msg, string[] buttons) { asked++; question = title; offered = buttons; return answer; };
                string before = w.Editor.Text;
                Check(w.EditorPaste(false), "not handled");
                Pump(200);
                Check(asked == 1 && offered != null && offered.Length == 2 && offered[0] == "Guardar como\u2026" && offered[1] == "Cancelar", "the question: " + question + " / " + (offered == null ? "" : string.Join("|", offered)));
                Check(question.IndexOf("Guarda el documento", StringComparison.Ordinal) == 0, "title: " + question);
                Same(before, w.Editor.Text, "Cancelar inserted something");
                Check(!Directory.Exists(Path.Combine(dir, "imagenes")), "Cancelar wrote a file");
                answer = 0;
                string target = Path.Combine(dir, "borrador.md");
                MarkdownWindow.PickPath = delegate(string s) { return target; };
                Check(w.EditorPaste(false), "not handled after saving");
                Check(Until(delegate { return w.Editor.Text.Contains("![](imagenes/imagen-"); }, 5000), "no link after Guardar como: " + w.Editor.Text);
                Check(File.Exists(target), "the document was not saved");
                Check(Directory.GetFiles(Path.Combine(dir, "imagenes"), "imagen-*.png").Length == 1, "the picture was not saved next to it");
            }
            finally { DocAlert.Answer = keep; MarkdownWindow.PasteSource = null; MarkdownWindow.PickPath = null; Shut(w); }
        }

        // A drop as WPF raises it (its constructor is internal), at a point over the editor.
        static DragEventArgs Md6DragArgs(IDataObject data, DependencyObject target, Point at)
        {
            ConstructorInfo ci = typeof(DragEventArgs).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                new Type[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) }, null);
            if (ci == null) return null;
            DragEventArgs e = (DragEventArgs)ci.Invoke(new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy, target, at });
            e.RoutedEvent = DragDrop.PreviewDropEvent;
            return e;
        }

        static void Md6Drop()
        {
            string root = WorkDir("dropimg");
            string dir = Path.Combine(root, "doc"), outside = Path.Combine(root, "fuera");
            Directory.CreateDirectory(Path.Combine(dir, "capturas"));
            Directory.CreateDirectory(outside);
            string inside = Path.Combine(dir, "capturas", "dentro.png");
            File.WriteAllBytes(inside, Md6Png(20, 20, Colors.Red, Colors.Blue));
            string far = Path.Combine(outside, "mi foto.png");
            File.WriteAllBytes(far, Md6Png(24, 24, Colors.Green, Colors.Blue));
            MarkdownWindow w = Md6Open(dir, "notas.md", "uno\n\ndos\n", true);
            try
            {
                DataObject d = new DataObject(DataFormats.FileDrop, new string[] { inside });
                DragEventArgs e = Md6DragArgs(d, w.Editor.Box, new Point(30, 25));
                if (e == null) { Call(w, "AddImageFiles", new List<string> { inside }, 0); Note("DragEventArgs not constructible: AddImageFiles called directly"); }
                else { w.RaiseEvent(e); Check(e.Handled, "the drop was not taken"); Note("drop raised through the window"); }
                Check(Until(delegate { return w.Editor.Text.Contains("![](capturas/dentro.png)"); }, 5000), "inside file: " + w.Editor.Text);
                Check(!Directory.Exists(Path.Combine(dir, "imagenes")), "a file inside the folder was copied");
                Check(w.Editor.Text.IndexOf("![](capturas/dentro.png)", StringComparison.Ordinal) <= 4, "not where it was dropped: " + w.Editor.Text);
                // From outside: copied into imagenes; the same file again is reused; another with the same name gets a number.
                Call(w, "AddImageFiles", new List<string> { far }, w.Editor.Text.Length);
                Check(Until(delegate { return w.Editor.Text.Contains("![](imagenes/mi%20foto.png)"); }, 5000), "outside file: " + w.Editor.Text);
                Check(File.Exists(Path.Combine(dir, "imagenes", "mi foto.png")), "not copied");
                Call(w, "AddImageFiles", new List<string> { far }, w.Editor.Text.Length);
                Check(Until(delegate { return Regex.Matches(w.Editor.Text, Regex.Escape("![](imagenes/mi%20foto.png)")).Count == 2; }, 5000), "same file not reused: " + w.Editor.Text);
                File.WriteAllBytes(far, Md6Png(24, 24, Colors.Black, Colors.White));
                Call(w, "AddImageFiles", new List<string> { far, inside }, w.Editor.Text.Length);
                Check(Until(delegate { return w.Editor.Text.Contains("![](imagenes/mi%20foto-2.png)\n\n![](capturas/dentro.png)"); }, 5000), "two files / changed file: " + w.Editor.Text);
                Check(Md6Status(w) == "2 im\u00E1genes a\u00F1adidas.", "status: " + Md6Status(w));
                // Something that is not a picture is left to the window (it opens files).
                DataObject md = new DataObject(DataFormats.FileDrop, new string[] { inside, Path.Combine(dir, "notas.md") });
                Check(((List<string>)typeof(MarkdownWindow).GetMethod("DroppedImageFiles", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { md })).Count == 0, "a drop with a document counted as pictures");
            }
            finally { Shut(w); }
        }

        static void Md6Preview()
        {
            string dir = WorkDir("previewimg");
            Directory.CreateDirectory(Path.Combine(dir, "imagenes"));
            File.WriteAllBytes(Path.Combine(dir, "imagenes", "foto.png"), Md6Png(300, 120, Colors.SteelBlue, Colors.Orange));
            string md = "# Con im\u00E1genes\n\nPrimera l\u00EDnea de texto.\n\n![foto](imagenes/foto.png)\n\n![falta](imagenes/no-existe.png)\n\nTexto final.\n";
            MarkdownWindow w = Md6Open(dir, "notas.md", md, false);
            try
            {
                MarkdownFlow flow = w.PreviewFlow;
                Check(flow != null && flow.BaseDir == dir, "the preview does not know the folder: " + (flow == null ? "no flow" : flow.BaseDir));
                Check(Until(delegate
                {
                    List<Border> ps = Md6Pictures(w.PreviewFlow);
                    return ps.Count == 2 && ps[0].Child is Image && ps[1].Child is StackPanel;
                }, 6000), "picture and placeholder not shown");
                List<Border> pics = Md6Pictures(w.PreviewFlow);
                Image img = (Image)pics[0].Child;
                BitmapSource src = (BitmapSource)img.Source;
                Check(src.PixelWidth == 300 && src.PixelHeight == 120, "picture size " + src.PixelWidth + "x" + src.PixelHeight);
                Pump(150);
                Check(img.ActualWidth > 290 && img.ActualWidth <= 300.5, "shown at its own size: " + img.ActualWidth);
                string note = "";
                foreach (TextBlock tb in Md6All<TextBlock>(pics[1])) note += tb.Text;
                Check(note.StartsWith("No encuentro la imagen", StringComparison.Ordinal) && note.Contains("imagenes/no-existe.png"), "placeholder text: " + note);
                // The line holding the picture grows with it: the text after it starts below the picture.
                Point pic = img.TranslatePoint(new Point(0, img.ActualHeight), w.PreviewFlow.Box);
                Paragraph last = (Paragraph)w.PreviewFlow.Box.Document.Blocks.LastBlock;
                Rect r = last.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Check(r.Top >= pic.Y, "text drawn over the picture: picture ends at " + pic.Y + ", text at " + r.Top);
                SavePng(Snap((FrameworkElement)w.Content), "w6md-preview-images.png");
                // Copying a selection with pictures serializes them as the preview's Copy does (no clipboard here).
                TextRange all = new TextRange(w.PreviewFlow.Box.Document.ContentStart, w.PreviewFlow.Box.Document.ContentEnd);
                foreach (string fmt in new string[] { DataFormats.Xaml, DataFormats.XamlPackage, DataFormats.Rtf, DataFormats.Text })
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        try { all.Save(ms, fmt); }
                        catch (Exception ex) { throw new TestFailed("copy as " + fmt + " failed: " + ex.GetType().Name + ": " + ex.Message); }
                        Check(ms.Length > 0, "copy as " + fmt + " is empty");
                    }
                }
                Check(w.PreviewFlow.FlatText().Contains("Primera l\u00EDnea de texto.") && w.PreviewFlow.FlatText().Contains("Texto final."), "find text around pictures");
                // A big picture fits the column; a picture missing at first appears once the file is there.
                File.WriteAllBytes(Path.Combine(dir, "imagenes", "no-existe.png"), Md6Png(2600, 400, Colors.Red, Colors.Yellow));
                w.SetEditing(true);
                Pump(250);
                w.Editor.Apply(TextEdit.Make(w.Editor.Text.Length, 0, "\nm\u00E1s\n", w.Editor.Text.Length, 0));
                w.SetEditing(false);
                Check(Until(delegate { List<Border> ps = Md6Pictures(w.PreviewFlow); return ps.Count == 2 && ps[1].Child is Image; }, 6000), "the picture added later did not show");
                Pump(200);
                Image big = (Image)Md6Pictures(w.PreviewFlow)[1].Child;
                Check(big.ActualWidth > 100 && big.ActualWidth <= MarkdownView.PageWidth + 1, "a wide picture does not fit the column: " + big.ActualWidth);
                Check(((BitmapSource)big.Source).PixelWidth <= MdImages.MaxWidth, "decoded too big: " + ((BitmapSource)big.Source).PixelWidth);
            }
            finally { Shut(w); }
        }

        // ---- Tables from spreadsheets

        static string Md6Grid(List<List<string>> g)
        {
            if (g == null) return "null";
            List<string> rows = new List<string>();
            foreach (List<string> r in g) rows.Add(string.Join("|", r.ToArray()));
            return string.Join(" / ", rows.ToArray()).Replace("\n", "\\n");
        }

        static void Md6Tsv()
        {
            string excel = "Nombre\tNotas\tImporte\r\nAna\t\"l\u00EDnea 1\nl\u00EDnea 2\"\t3,50 \u20AC\r\nLuis\t\"dice \"\"hola\"\"\"\t10\r\n\t\t\r\n";
            Same("Nombre|Notas|Importe / Ana|l\u00EDnea 1\\nl\u00EDnea 2|3,50 \u20AC / Luis|dice \"hola\"|10 / ||", Md6Grid(MdTablePaste.Tsv(excel)), "Excel text");
            // A quote inside a cell that is not quoted stays as it is.
            Same("a|5\" pulgadas / \"x\"y|z", Md6Grid(MdTablePaste.Tsv("a\t5\" pulgadas\n\"x\"y\tz")), "quotes in plain cells");
            Same("null", Md6Grid(MdTablePaste.Tsv("una sola columna\notra")), "single column");
            Same("null", Md6Grid(MdTablePaste.Tsv("\tif (x) {\n\t\ty();\n\t}\n")), "code with tabs");
            Same("null", Md6Grid(MdTablePaste.Tsv("")), "empty");
            Same("a|b", Md6Grid(MdTablePaste.Tsv("a\tb\r\n")), "one row");
        }

        static void Md6Csv()
        {
            Same("a|b|c / x;y|multi\\nl\u00EDnea|q\"t", Md6Grid(MdTablePaste.Csv("a;b;c\r\n\"x;y\";\"multi\nl\u00EDnea\";\"q\"\"t\"\r\n", 3)), "semicolons with quotes");
            Same("a|b / 1,5|2", Md6Grid(MdTablePaste.Csv("a,b\n\"1,5\",2\n", 2)), "commas with quotes");
            Same("null", Md6Grid(MdTablePaste.Csv("a,b\nc\n", 2)), "ragged rows");
            Same("a|b|c", Md6Grid(MdTablePaste.Csv("a\tb\tc", 3)), "tab separated");
        }

        const string Md6Sheets = "<meta charset=\"utf-8\"><google-sheets-html-origin><style type=\"text/css\"><!--td {border: 1px solid #cccccc;}--></style>" +
            "<table xmlns=\"http://www.w3.org/1999/xhtml\" cellspacing=\"0\" cellpadding=\"0\" dir=\"ltr\" border=\"1\"><colgroup><col width=\"100\"/></colgroup><tbody>" +
            "<tr style=\"height:21px;\"><td>Producto</td><td>Precio</td><td>Notas</td></tr>" +
            "<tr><td colspan=\"2\" rowspan=\"1\">Unido &amp; ancho</td><td>c</td></tr>" +
            "<tr><td rowspan=\"2\">Abajo</td><td>x<br>y</td><td></td></tr>" +
            "<tr><td>z&nbsp;</td><td><div>uno</div><div>dos</div></td></tr></tbody></table></google-sheets-html-origin>";

        static void Md6HtmlTable()
        {
            Same("Producto|Precio|Notas / Unido & ancho||c / Abajo|x\\ny| / |z|uno\\ndos", Md6Grid(MdTablePaste.Html(Md6Sheets)), "Google Sheets HTML");
            string excel = "Version:0.9\r\nStartHTML:0000000105\r\n<html xmlns:o=\"urn:schemas-microsoft-com:office:office\"><head><style><!--.xl65{mso-number-format:\"\\@\";}--></style></head><body>" +
                           "<!--StartFragment--><table border=0 cellpadding=0 cellspacing=0 width=128><tr height=20><td height=20 class=xl65 width=64>A</td><td width=64>B</td></tr>" +
                           "<tr><td class=xl65>l1<br style='mso-data-placement:same-cell;'>l2</td><td align=right>3</td></tr><!--EndFragment--></table></body></html>";
            Same("A|B / l1\\nl2|3", Md6Grid(MdTablePaste.Html(excel)), "Excel HTML");
            Same("null", Md6Grid(MdTablePaste.Html("<div><span>no table</span></div>")), "no table");
            Same("a|b / c|", Md6Grid(MdTablePaste.Html("<table><tr><th>a</th><th>b</th></tr><tr><td>c</td></tr></table>")), "short row padded");
            Same("x|y\\nin", Md6Grid(MdTablePaste.Html("<table><tr><td>x</td><td>y<table><tr><td>in</td></tr></table></td></tr></table><table><tr><td>second</td></tr></table>")), "first table only, nested text kept in the cell");
        }

        static void Md6TableMarkdown()
        {
            List<List<string>> g = new List<List<string>>
            {
                new List<string> { " Nombre ", "Ruta", "Nota" },
                new List<string> { "Ana", "C:\\temp\\a|b", "l\u00EDnea 1\nl\u00EDnea 2" },
                new List<string> { "\u4E2D\u6587", "", "\uD83D\uDE00 ok" },
            };
            string md = MdTablePaste.Markdown(g);
            string[] lines = md.Split('\n');
            Check(lines.Length == 4, "lines: " + md);
            Same("| Nombre | Ruta" + new string(' ', 10) + " | Nota" + new string(' ', 14) + " |", lines[0], "header trimmed and padded");
            Same("| ------ | " + new string('-', 14) + " | " + new string('-', 18) + " |", lines[1], "dashes");
            Same("| Ana    | C:\\\\temp\\\\a\\|b | l\u00EDnea 1<br>l\u00EDnea 2 |", lines[2], "escapes and <br>");
            Same("| \u4E2D\u6587   | " + new string(' ', 14) + " | \uD83D\uDE00 ok" + new string(' ', 13) + " |", lines[3], "wide characters aligned by screen columns");
            // The table reads back with the same cells.
            List<MdBlock> bs = MarkdownDoc.Parse(md);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table && bs[0].Rows.Count == 2, "not read back as a table");
            Same("C:\\temp\\a|b", MarkdownDoc.Plain(bs[0].Rows[0][1]), "cell with backslashes and a pipe");
            List<MdSpan> cell = MarkdownDoc.Inline(bs[0].Rows[0][2]);
            Check(cell.Count == 3 && cell[1].Break, "<br> is a line break in the cell");
        }

        static DataObject Md6Clip(string text, string html, string xls, string csv)
        {
            DataObject d = new DataObject();
            if (text != null) d.SetData(DataFormats.UnicodeText, text);
            if (html != null) d.SetData(DataFormats.Html, html);
            if (xls != null) d.SetData("XML Spreadsheet", new MemoryStream(Encoding.UTF8.GetBytes(xls)));
            if (csv != null) d.SetData("Csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
            return d;
        }

        static void Md6Detect()
        {
            string tsv = "Producto\tPrecio\tNotas\r\nUnido & ancho\t\tc\r\nAbajo\t\"x\ny\"\t\r\n\tz\tuno dos\r\n";
            int r, c;
            string md = MdTablePaste.FromData(Md6Clip(tsv, Md6Sheets, null, null), out r, out c);
            Check(md != null && r == 4 && c == 3, "Google Sheets not converted: " + md);
            Check(md.Contains("| x<br>y "), "line break from the HTML cell: " + md);
            Check(md.Contains("uno<br>dos"), "cell paragraphs from the HTML: " + md);
            // Excel: text, XML Spreadsheet and an HTML table of another shape: the text wins.
            string xls = "<?xml version=\"1.0\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"><Worksheet><Table><Row><Cell><Data>a</Data></Cell></Row></Table></Worksheet></Workbook>";
            md = MdTablePaste.FromData(Md6Clip("a\tb\r\n1\t2\r\n", "<table><tr><td>a</td></tr></table>", xls, null), out r, out c);
            Check(md != null && r == 2 && c == 2 && md.Contains("| 1   | 2   |"), "Excel: " + md);
            // CSV fixes what the text cannot say (a separator inside a quoted cell).
            md = MdTablePaste.FromData(Md6Clip("a\tb\r\n\"x\"\ty\r\n", null, null, "a;b\r\n\"\"\"x\"\"\";y\r\n"), out r, out c);
            Check(md != null && md.Contains("| \"x\" |"), "CSV cells: " + md);
            // Not tables.
            Check(MdTablePaste.FromData(Md6Clip("\tif (x) {\n\t\ty();\n\t}\n", null, null, null), out r, out c) == null, "plain text with tabs converted");
            Check(MdTablePaste.FromData(Md6Clip("a\tb\nc\td\n", "<div><pre>a\tb</pre></div>", null, null), out r, out c) == null, "code from an editor (HTML without a table) converted");
            Check(MdTablePaste.FromData(Md6Clip("a\tb\nc\td\n", null, null, null), out r, out c) == null, "text alone converted");
            Check(MdTablePaste.FromData(Md6Clip("uno\ndos\ntres\n", Md6Sheets, xls, null), out r, out c) == null, "a single column converted");
            Check(MdTablePaste.FromData(Md6Clip("a\tb\nc\n", null, xls, null), out r, out c) == null, "ragged text converted");
            Check(MdTablePaste.FromData(Md6Clip("\tx = 1;\n\ty = 2;\n", "<table><tr><td></td><td>x = 1;</td></tr><tr><td></td><td>y = 2;</td></tr></table>", null, null), out r, out c) == null, "a code listing (empty first column) converted");
            Check(MdTablePaste.FromData(Md6Clip(null, Md6Sheets, null, null), out r, out c) == null, "no text");
        }

        static void Md6TablePasteWindow()
        {
            string dir = WorkDir("tablepaste");
            MarkdownWindow w = Md6Open(dir, "notas.md", "# Datos\n\nAntes de la tabla.\nDespu\u00E9s.\n", true);
            try
            {
                string before = w.Editor.Text;
                int at = before.IndexOf("\nDespu", StringComparison.Ordinal);
                Md6Caret(w, at);
                string tsv = "Mes\tVentas\tNotas\r\nEnero\t10\ta|b\r\nFebrero\t\t\u4E2D\u6587\r\n";
                DataObject d = Md6Clip(tsv, null, "<Workbook/>", null);
                MarkdownWindow.PasteSource = delegate { return d; };
                Check(w.EditorPaste(false), "table paste not handled");
                string t = w.Editor.Text;
                string table = "| Mes     | Ventas | Notas |\n| ------- | ------ | ----- |\n| Enero   | 10     | a\\|b  |\n| Febrero |        | \u4E2D\u6587  |";
                Same("# Datos\n\nAntes de la tabla.\n\n" + table + "\n\nDespu\u00E9s.\n", t, "table with blank lines around it");
                Same("Tabla pegada (3 \u00D7 3).", Md6Status(w), "status");
                Check(w.Editor.Box.CaretIndex == t.IndexOf("\nDespu", StringComparison.Ordinal), "caret after the table: " + w.Editor.Box.CaretIndex);
                w.Editor.Box.Undo();
                Same(before, w.Editor.Text, "one undo step");
                // Ctrl+Shift+V: the text as it is.
                Md6Caret(w, w.Editor.Text.Length);
                Check(w.EditorPaste(true), "plain paste not handled");
                Check(w.Editor.Text.EndsWith("Despu\u00E9s.\n" + tsv.Replace("\r\n", "\n"), StringComparison.Ordinal), "plain paste: " + Show(w.Editor.Text, w.Editor.Text.Length - 20));
                w.Editor.Box.Undo();
                Same(before, w.Editor.Text, "plain paste is one undo step");
                // Through the keys, as the editor gets them (straight to it: through the window, a parked window without
                // the keyboard focus would take Ctrl+V as its own paste, which reads the real clipboard).
                Md6Caret(w, 0);
                Check(Md6Key(w.Editor, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control), "Ctrl+V not taken");
                Check(w.Editor.Text.StartsWith(table + "\n\n# Datos", StringComparison.Ordinal), "Ctrl+V: " + Show(w.Editor.Text, 0));
                w.Editor.Box.Undo();
                Check(Md6Key(w.Editor, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift), "Ctrl+Shift+V not taken");
                Check(w.Editor.Text.StartsWith("Mes\tVentas\tNotas\n", StringComparison.Ordinal), "Ctrl+Shift+V: " + Show(w.Editor.Text, 0));
                w.Editor.Box.Undo();
                Check(Md6Key(w.Editor, System.Windows.Input.Key.Insert, System.Windows.Input.ModifierKeys.Shift), "Shift+Insert not taken");
                w.Editor.Box.Undo();
                Check(!Md6Key(w.Editor, System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Control), "Ctrl+Alt+V taken");
                Same(before, w.Editor.Text, "after the keys");
                // Code with tabs from another app is left to the TextBox.
                DataObject code = Md6Clip("\tif (x)\n\t\ty();\n", null, null, null);
                MarkdownWindow.PasteSource = delegate { return code; };
                Check(!w.EditorPaste(false), "code with tabs was taken as a table");
                // An XML document's editor is not given any of this.
                MarkdownWindow x = Md6Open(dir, "a.xml", "<a>\n  <b/>\n</a>\n", true);
                try
                {
                    Check(Until(delegate { return x.Editor != null; }, 3000), "no XML editor");
                    Check(x.Editor.SmartPaste == null, "the XML editor got the Markdown paste");
                    Check(!x.EditorPaste(false), "XML paste handled as Markdown");
                }
                finally { Shut(x); }
            }
            finally { MarkdownWindow.PasteSource = null; if (w.IsVisible) Shut(w); }
        }

        static void Md6BigSheet()
        {
            string dir = WorkDir("bigsheet");
            // Many short rows: big in bytes, while the editor lays the lines out quickly (long wrapped lines take it seconds).
            StringBuilder tsv = new StringBuilder(), html = new StringBuilder("<table>");
            string filler = "abcdefghij";
            for (int r = 0; r < 1100; r++)
            {
                html.Append("<tr>");
                for (int c = 0; c < 6; c++)
                {
                    string v = r == 0 ? "Col" + c : "v" + r + "." + c + " " + filler;
                    tsv.Append(v).Append(c < 5 ? "\t" : "\r\n");
                    html.Append("<td>").Append(v).Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</table>");
            Check(tsv.Length + html.Length > MarkdownWindow.BigSheet, "sheet not big enough: " + (tsv.Length + html.Length));
            MarkdownWindow w = Md6Open(dir, "datos.md", "# Datos\n", true);
            try
            {
                DataObject d = Md6Clip(tsv.ToString(), html.ToString(), null, null);
                MarkdownWindow.PasteSource = delegate { return d; };
                Md6Caret(w, w.Editor.Text.Length);
                // The preview of a thousand-row table takes WPF seconds: the status must not fade before it is read.
                Field<System.Windows.Threading.DispatcherTimer>(w, "fade").Interval = TimeSpan.FromMinutes(5);
                Stopwatch sw = Stopwatch.StartNew();
                Check(w.EditorPaste(false), "not handled");
                double ms = sw.Elapsed.TotalMilliseconds;
                Check(Md6Status(w) == "Pegando la tabla\u2026", "status while converting: " + Md6Status(w));
                Check(Until(delegate { return w.Editor.Text.Contains("| v1099.5 " + filler + " |"); }, 20000), "the big table never arrived");
                Same("Tabla pegada (1100 \u00D7 6).", Md6Status(w), "status");
                Put("ui_ms", Math.Round(ms, 1));
                Check(ms < 400, "the paste held the UI thread for " + Ms(ms));
                w.Editor.Box.Undo();
                Same("# Datos\n", w.Editor.Text, "one undo step");
                Note("UI thread " + Ms(ms) + " for " + ((tsv.Length + html.Length) / 1024) + " KB of clipboard data");
            }
            finally { MarkdownWindow.PasteSource = null; Shut(w); }
        }

        static void Md6SaveAsPictures()
        {
            string dir = WorkDir("saveaspics");
            Directory.CreateDirectory(Path.Combine(dir, "imagenes"));
            File.WriteAllBytes(Path.Combine(dir, "imagenes", "a.png"), Md6Png(50, 40, Colors.Red, Colors.Blue));
            MarkdownWindow w = NewWindow();
            try
            {
                Call(w, "LoadText", "# Pegado\n\n![a](imagenes/a.png)\n", null, null);
                Pump(300);
                Check(Md6Pictures(w.PreviewFlow).Count == 0, "a relative picture shown for text with no folder");
                MarkdownWindow.PickPath = delegate(string s) { return Path.Combine(dir, "pegado.md"); };
                Check(w.Save(true), "save failed");
                Check(Until(delegate { List<Border> ps = Md6Pictures(w.PreviewFlow); return ps.Count == 1 && ps[0].Child is Image; }, 5000), "the picture did not appear after Guardar como");
                Check(MdImages.Peek(Path.Combine(dir, "imagenes", "a.png")) != null, "picture not cached while open");
            }
            finally { MarkdownWindow.PickPath = null; Shut(w); }
            Check(MdImages.Peek(Path.Combine(dir, "imagenes", "a.png")) == null, "pictures kept in memory after the window closed");
            // A very tall picture is decoded smaller (MaxPixels).
            WriteableBitmap tall = new WriteableBitmap(2200, 8000, 96, 96, PixelFormats.Gray8, null);
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(tall));
            string path = Path.Combine(dir, "alta.png");
            using (FileStream fs = File.Create(path)) e.Save(fs);
            string err;
            BitmapSource b = MdImages.Load(path, out err);
            Check(b != null && b.PixelWidth <= MdImages.MaxWidth && (long)b.PixelWidth * b.PixelHeight <= MdImages.MaxPixels, "tall picture " + (b == null ? err : b.PixelWidth + "x" + b.PixelHeight));
            Check(Math.Abs(b.PixelHeight / (double)b.PixelWidth - 8000 / 2200.0) < 0.02, "proportions kept");
            Check(MdImages.Load(Path.Combine(dir, "nada.png"), out err) == null && err == "No encuentro la imagen", "missing file: " + err);
            File.WriteAllText(Path.Combine(dir, "roto.png"), "no es una imagen");
            Check(MdImages.Load(Path.Combine(dir, "roto.png"), out err) == null && err == "No puedo mostrar esta imagen", "broken file: " + err);
        }

        // ---- Outline

        static void Md6Outline()
        {
            string md = "# Uno\n\ntexto\n\nDos\n===\n\nTres\n---\n\n```\n# no es t\u00EDtulo\n```\n\n## **Negrita** y `c\u00F3digo` [enlace](http://x.es)\n\n#### Cuatro\n\n- # en lista\n\n> # en cita\n\n### ![logo](a.png) Cinco\n\n### ![solo](b.png)\n\n#\n";
            List<MdHeading> hs = MdOutline.Headings(MarkdownDoc.Parse(md));
            List<string> got = new List<string>();
            foreach (MdHeading h in hs) got.Add(h.Level + ":" + h.Depth + ":" + h.Text + "@" + h.Line);
            string[] lines = md.Split('\n');
            int neg = Array.FindIndex(lines, delegate(string l) { return l.StartsWith("## **Negrita**", StringComparison.Ordinal); });
            int cinco = Array.FindIndex(lines, delegate(string l) { return l.StartsWith("### ![logo]", StringComparison.Ordinal); });
            int solo = Array.FindIndex(lines, delegate(string l) { return l.StartsWith("### ![solo]", StringComparison.Ordinal); });
            Same("1:0:Uno@0 | 1:0:Dos@4 | 2:1:Tres@7 | 2:1:Negrita y c\u00F3digo enlace@" + neg + " | 3:2:Cinco@" + cinco + " | 3:2:solo@" + solo, string.Join(" | ", got.ToArray()), "headings");
            // Depth counts from the document's highest heading.
            List<MdHeading> h2 = MdOutline.Headings(MarkdownDoc.Parse("## A\n\n### B\n\n## C\n"));
            Check(h2.Count == 3 && h2[0].Depth == 0 && h2[1].Depth == 1 && h2[2].Depth == 0, "depth from H2");
            Check(MdOutline.Headings(MarkdownDoc.Parse("solo texto\n\n#### profundo\n")).Count == 0, "no H1-H3");
            // Where each starts, and the section of a place in the text.
            string t = "intro\n# A\ntexto a\n## B\ntexto b\n";
            List<MdHeading> hl = MdOutline.Headings(MarkdownDoc.Parse(t));
            MdOutline.Locate(hl, t);
            Check(hl[0].Offset == t.IndexOf("# A", StringComparison.Ordinal) && hl[1].Offset == t.IndexOf("## B", StringComparison.Ordinal), "offsets " + hl[0].Offset + "," + hl[1].Offset);
            Check(MdOutline.At(hl, 0) == -1 && MdOutline.At(hl, t.IndexOf("texto a", StringComparison.Ordinal)) == 0 && MdOutline.At(hl, t.Length) == 1, "sections");
            Same("Hola mundo", MdOutline.Clean("  Hola  <br>  *mundo*  "), "clean text");
        }

        static void Md6OutlinePanel()
        {
            string dir = WorkDir("outline");
            MarkdownWindow w = Md6Open(dir, "notas.md", "# Uno\n\ntexto\n\n## Dos\n\nm\u00E1s\n", false);
            try
            {
                Check(!w.OutlineOpen && w.OutlineList == null, "the outline is not hidden by default");
                List<OutlineToggle> bt = Md6All<OutlineToggle>((DependencyObject)w.Content);
                Check(bt.Count == 1 && !bt[0].On, "toolbar button: " + bt.Count);
                w.SetOutline(true);
                Pump(200);
                Check(w.OutlineOpen && w.OutlineList != null && w.OutlineList.IsVisible, "panel not shown");
                Check(Md6All<OutlineToggle>((DependencyObject)w.Content)[0].On, "button not pressed");
                Same("Uno|Dos", string.Join("|", w.Outline.ConvertAll(delegate(MdHeading h) { return h.Text; }).ToArray()), "entries");
                Check(w.OutlineList.Items.Count == 2, "list items " + w.OutlineList.Items.Count);
                SavePng(Snap((FrameworkElement)w.Content), "w6md-outline-view.png");
                // Kept when switching modes and themes.
                w.SetEditing(true);
                Pump(300);
                Check(w.OutlineOpen && w.OutlineList != null && w.OutlineList.Items.Count == 2, "lost when editing");
                Ds.Apply(2);
                Pump(200);
                Check(w.OutlineOpen && w.OutlineList != null && w.OutlineList.IsVisible, "lost with the theme");
                SavePng(Snap((FrameworkElement)w.Content), "w6md-outline-edit-dark.png");
                Ds.Apply(1);
                Pump(200);
                // It follows the text after a pause in typing.
                w.Editor.Apply(TextEdit.Make(w.Editor.Text.Length, 0, "\n### Tres `nuevo`\n", w.Editor.Text.Length, 0));
                Check(Until(delegate { return w.Outline.Count == 3 && w.Outline[2].Text == "Tres nuevo"; }, 3000), "not updated after typing: " + w.Outline.Count);
                Check(w.OutlineList.Items.Count == 3, "list not updated");
                // Typing that does not touch a heading keeps the list as it is.
                List<MdHeading> keep = w.Outline;
                w.Editor.Apply(TextEdit.Make(w.Editor.Text.IndexOf("texto", StringComparison.Ordinal), 0, "m\u00E1s ", 0, 0));
                Pump(500);
                Check(w.Outline == keep && w.Outline[1].Line == 4, "rebuilt without need, or lines not outlineFlow");
                // Empty state.
                w.Editor.SetText("Sin t\u00EDtulos.\n");
                Check(Until(delegate { return w.Outline.Count == 0; }, 3000), "headings left after removing them");
                TextBlock empty = Field<TextBlock>(w, "outlineEmpty");
                Check(empty.Visibility == Visibility.Visible && empty.Text.StartsWith("No hay t\u00EDtulos", StringComparison.Ordinal) && w.OutlineList.Visibility != Visibility.Visible, "empty state");
                // Off again: nothing left.
                w.SetOutline(false);
                Pump(100);
                Check(!w.OutlineOpen && w.OutlineList == null && Field<Border>(w, "outlinePane") == null, "panel left after hiding it");
                w.SetOutline(true);
                Pump(100);
                Shut(w);
                // The window remembers it while open: a new document in the same window keeps it.
                w = Md6Open(dir, "otra.md", "# Otra\n", false);
                Check(!w.OutlineOpen, "a new window starts hidden");
                w.SetOutline(true);
                Call(w, "LoadText", "# Pegado\n\n## Segundo\n", null, null);
                Pump(300);
                Check(w.OutlineOpen && w.Outline.Count == 2, "a new document lost the panel");
                // XML documents have no outline.
                Call(w, "LoadText", "<a><b/></a>", "a.xml", null);
                Pump(400);
                Check(Md6All<OutlineToggle>((DependencyObject)w.Content).Count == 0, "XML got the outline button");
                Check(w.OutlineList == null, "XML shows the panel");
            }
            finally { Shut(w); Ds.Apply(1); }
        }

        static void Md6OutlineJump()
        {
            string dir = WorkDir("outlinejump");
            StringBuilder sb = new StringBuilder("# Documento largo\n\n");
            for (int i = 0; i < 12; i++)
            {
                sb.Append("## Secci\u00F3n " + i + "\n\n");
                for (int p = 0; p < 6; p++) sb.Append("Texto de la secci\u00F3n " + i + ", p\u00E1rrafo " + p + ", con palabras suficientes para ocupar una l\u00EDnea.\n\n");
            }
            MarkdownWindow w = Md6Open(dir, "largo.md", sb.ToString(), false);
            try
            {
                w.SetOutline(true);
                Pump(300);
                Check(w.Outline.Count == 13, "entries " + w.Outline.Count);
                ScrollViewer sv = w.PreviewFlow.Scroller;
                Check(sv.ScrollableHeight > 1500, "document too short to scroll: " + sv.ScrollableHeight);
                // A click on an entry: the heading at the top of the preview.
                MdHeading target = w.Outline[5];
                w.JumpToHeading(target);
                Pump(200);
                double top = w.PreviewFlow.Top(target.Block) - sv.VerticalOffset;
                Check(top >= 0 && top <= 30, "heading not at the top after the jump: " + top);
                Check(w.OutlineCurrent == 5 && w.OutlineList.SelectedIndex == 5, "current entry after the jump: " + w.OutlineCurrent);
                // Scrolling marks the section being read.
                double y7 = w.PreviewFlow.Top(w.Outline[7].Block);
                sv.ScrollToVerticalOffset(y7 + 40);
                Pump(200);
                Check(w.OutlineCurrent == 7, "section while scrolling: " + w.OutlineCurrent);
                sv.ScrollToVerticalOffset(0);
                Pump(200);
                Check(w.OutlineCurrent == 0, "back at the top: " + w.OutlineCurrent);
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
                Pump(200);
                Check(w.OutlineCurrent == 12, "at the end: " + w.OutlineCurrent);
                // While editing the caret goes to the heading's line too, and its section is the current one.
                w.SetEditing(true);
                Pump(400);
                MdHeading nine = w.Outline[9];
                w.JumpToHeading(nine);
                Pump(300);
                Check(w.Editor.Text.Substring(w.Editor.Box.CaretIndex).StartsWith("## Secci\u00F3n 8\n", StringComparison.Ordinal), "caret not on the heading's line: " + Show(w.Editor.Text, w.Editor.Box.CaretIndex));
                Check(w.OutlineCurrent == 9, "current while editing: " + w.OutlineCurrent);
                double top9 = w.PreviewFlow.Top(nine.Block) - w.PreviewFlow.Scroller.VerticalOffset;
                Check(top9 >= 0 && top9 <= 30, "preview not at the heading while editing: " + top9);
                // The caret moving into another section marks it.
                w.Editor.Box.Select(w.Editor.Text.IndexOf("Texto de la secci\u00F3n 2,", StringComparison.Ordinal), 0);
                Call(w, "OutlineFollowCaret");
                Check(w.OutlineCurrent == 3, "section of the caret: " + w.OutlineCurrent);
            }
            finally { Shut(w); }
        }

        // ---- PDF

        sealed class Md6PdfFile
        {
            public string Raw;
            public readonly Dictionary<int, string> Dicts = new Dictionary<int, string>();
            public readonly Dictionary<int, byte[]> Streams = new Dictionary<int, byte[]>();
            public int Pages, Glyphs, Mapped;
            public string Text = "";
        }

        static byte[] Md6Inflate(byte[] z)
        {
            using (MemoryStream src = new MemoryStream(z, 2, z.Length - 6))
            using (DeflateStream ds = new DeflateStream(src, CompressionMode.Decompress))
            using (MemoryStream o = new MemoryStream())
            {
                ds.CopyTo(o);
                return o.ToArray();
            }
        }

        // Reads a PDF the way a reader would start: the cross-reference table must point at every object; then the streams,
        // the pages and the text through each font's ToUnicode map.
        static Md6PdfFile Md6ReadPdf(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            Md6PdfFile f = new Md6PdfFile();
            string raw = Encoding.GetEncoding(28591).GetString(b);
            f.Raw = raw;
            Check(raw.StartsWith("%PDF-1.4\n", StringComparison.Ordinal), "header");
            Check(raw.EndsWith("%%EOF\n", StringComparison.Ordinal), "no %%EOF at the end");
            int sx = raw.LastIndexOf("startxref\n", StringComparison.Ordinal);
            int xref = int.Parse(raw.Substring(sx + 10, raw.IndexOf('\n', sx + 10) - sx - 10));
            Check(raw.Substring(xref, 5) == "xref\n", "startxref does not point at the table");
            string[] lines = raw.Substring(xref).Split('\n');
            int n = int.Parse(lines[1].Split(' ')[1]);
            Check(lines[2] == "0000000000 65535 f ", "first entry");
            for (int i = 1; i < n; i++)
            {
                string e = lines[2 + i];
                Check(e.Length == 19 && e.EndsWith(" 00000 n ", StringComparison.Ordinal), "entry " + i + ": " + e);
                int off = int.Parse(e.Substring(0, 10));
                string head = i + " 0 obj\n";
                Check(raw.Substring(off, head.Length) == head, "object " + i + " is not where the table says");
                int body = off + head.Length;
                int st = raw.IndexOf(">>\nstream\n", body, StringComparison.Ordinal), end = raw.IndexOf("\nendobj\n", body, StringComparison.Ordinal);
                if (st >= 0 && (end < 0 || st < end))
                {
                    string dict = raw.Substring(body, st + 2 - body);
                    Match lm = Regex.Match(dict, @"/Length (\d+)");
                    Check(lm.Success, "stream " + i + " without length");
                    int len = int.Parse(lm.Groups[1].Value);
                    byte[] data = new byte[len];
                    Array.Copy(b, st + 10, data, 0, len);
                    Check(raw.Substring(st + 10 + len, 18) == "\nendstream\nendobj\n", "stream " + i + " length is wrong");
                    f.Dicts[i] = dict;
                    f.Streams[i] = dict.Contains("/FlateDecode") ? Md6Inflate(data) : data;
                }
                else f.Dicts[i] = raw.Substring(body, end - body);
            }
            Match tr = Regex.Match(raw.Substring(xref), @"trailer\n<< /Size (\d+) /Root (\d+) 0 R /Info (\d+) 0 R >>");
            Check(tr.Success && int.Parse(tr.Groups[1].Value) == n, "trailer");
            Check(f.Dicts[int.Parse(tr.Groups[2].Value)].Contains("/Type /Catalog"), "root is not the catalog");
            // Fonts: resource name -> glyph -> text.
            Dictionary<string, Dictionary<int, string>> maps = new Dictionary<string, Dictionary<int, string>>();
            foreach (KeyValuePair<int, string> kv in f.Dicts)
            {
                if (!kv.Value.Contains("/ProcSet")) continue;
                foreach (Match fm in Regex.Matches(kv.Value, @"/(F\d+) (\d+) 0 R"))
                {
                    string t0 = f.Dicts[int.Parse(fm.Groups[2].Value)];
                    Check(t0.Contains("/Subtype /Type0") && t0.Contains("/Identity-H"), "font " + fm.Groups[1].Value + ": " + t0);
                    int tu = int.Parse(Regex.Match(t0, @"/ToUnicode (\d+) 0 R").Groups[1].Value);
                    int cid = int.Parse(Regex.Match(t0, @"/DescendantFonts \[(\d+) 0 R\]").Groups[1].Value);
                    int desc = int.Parse(Regex.Match(f.Dicts[cid], @"/FontDescriptor (\d+) 0 R").Groups[1].Value);
                    int file = int.Parse(Regex.Match(f.Dicts[desc], @"/FontFile2 (\d+) 0 R").Groups[1].Value);
                    byte[] font = f.Streams[file];
                    Check(font.Length > 100 && font[0] == 0 && font[1] == 1 && font[2] == 0 && font[3] == 0, "embedded font is not TrueType");
                    Dictionary<int, string> map = new Dictionary<int, string>();
                    foreach (Match bm in Regex.Matches(Encoding.ASCII.GetString(f.Streams[tu]), @"<([0-9A-F]{4})> <([0-9A-F]+)>"))
                    {
                        StringBuilder s = new StringBuilder();
                        string hex = bm.Groups[2].Value;
                        for (int k = 0; k + 4 <= hex.Length; k += 4) s.Append((char)Convert.ToInt32(hex.Substring(k, 4), 16));
                        map[Convert.ToInt32(bm.Groups[1].Value, 16)] = s.ToString();
                    }
                    maps[fm.Groups[1].Value] = map;
                }
            }
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<int, string> kv in f.Dicts)
            {
                if (!kv.Value.StartsWith("<< /Type /Page ", StringComparison.Ordinal)) continue;
                f.Pages++;
                Check(kv.Value.Contains("/MediaBox [0 0 595.28 841.89]"), "page is not A4: " + kv.Value);
                foreach (Match cm in Regex.Matches(Regex.Match(kv.Value, @"/Contents (\[[^\]]*\]|\d+ 0 R)").Groups[1].Value, @"(\d+) 0 R"))
                {
                    string ops = Encoding.ASCII.GetString(f.Streams[int.Parse(cm.Groups[1].Value)]);
                    Check(ops.StartsWith("q\n", StringComparison.Ordinal) && ops.EndsWith("Q\n", StringComparison.Ordinal), "content stream not wrapped in q/Q");
                    Dictionary<int, string> cur = null;
                    foreach (Match om in Regex.Matches(ops, @"/(F\d+) [\d.]+ Tf|\[([^\]]*)\] TJ|<([0-9A-F]+)> Tj|\bET\b"))
                    {
                        if (om.Groups[1].Success) { cur = maps[om.Groups[1].Value]; continue; }
                        if (om.Value == "ET") { text.Append('\n'); continue; }
                        string hexes = om.Groups[2].Success ? om.Groups[2].Value : "<" + om.Groups[3].Value + ">";
                        foreach (Match hm in Regex.Matches(hexes, @"<([0-9A-F]+)>"))
                            for (int k = 0; k + 4 <= hm.Groups[1].Value.Length; k += 4)
                            {
                                int g = Convert.ToInt32(hm.Groups[1].Value.Substring(k, 4), 16);
                                f.Glyphs++;
                                string s;
                                if (cur != null && cur.TryGetValue(g, out s)) { f.Mapped++; text.Append(s); }
                            }
                    }
                }
            }
            f.Text = text.ToString();
            return f;
        }

        static string Md6Squash(string s) { return Regex.Replace(s, @"\s+", ""); }

        static string Md6LongDoc(int sections, bool picture)
        {
            StringBuilder sb = new StringBuilder("# Informe de pruebas\n\nTexto con **negrita**, *cursiva*, `c\u00F3digo` y un [enlace](https://example.com). Acentos: acci\u00F3n, ni\u00F1o, \u00BFqu\u00E9? \u00A1s\u00ED! Emoji \uD83D\uDE00 y \u4E2D\u6587.\n\n");
            sb.Append("- uno\n- [x] hecho\n- [ ] pendiente\n\n1. primero\n2. segundo\n\n> Una cita.\n\n```\nvar x = 1;\n```\n\n| Tecla | Acci\u00F3n |\n|---|---:|\n| Ctrl+B | Negrita |\n\n---\n\n");
            if (picture) sb.Append("![foto](imagenes/foto.png)\n\n![falta](imagenes/no-existe.png)\n\n");
            for (int i = 0; i < sections; i++)
            {
                sb.Append("## Secci\u00F3n " + i + "\n\n");
                for (int p = 0; p < 4; p++) sb.Append("P\u00E1rrafo " + p + " de la secci\u00F3n " + i + " con texto de relleno para comprobar la paginaci\u00F3n de un documento largo.\n\n");
                sb.Append("### Detalle " + i + "\n\n- punto a\n- punto b\n\n");
            }
            return sb.ToString();
        }

        // Runs the export on a thread of its own with its own dispatcher, as the window does.
        static int Md6Export(string md, string dir, string path, out double ms)
        {
            int pages = 0;
            Exception fail = null;
            Stopwatch sw = Stopwatch.StartNew();
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                try { pages = MdPdf.Export(md, dir, path, "Informe de pruebas", null, null); }
                catch (Exception e) { fail = e; }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            while (!t.Join(20)) System.Windows.Forms.Application.DoEvents();
            ms = sw.Elapsed.TotalMilliseconds;
            if (fail != null) throw new TestFailed("export failed: " + fail);
            return pages;
        }

        static void Md6PdfExport()
        {
            string dir = WorkDir("pdf");
            Directory.CreateDirectory(Path.Combine(dir, "imagenes"));
            File.WriteAllBytes(Path.Combine(dir, "imagenes", "foto.png"), Md6Png(900, 300, Colors.SteelBlue, Colors.Orange));
            string target = Path.Combine(dir, "informe.pdf");
            double ms;
            int pages = Md6Export(Md6LongDoc(6, true), dir, target, out ms);
            Md6PdfFile f = Md6ReadPdf(target);
            Put("pages", pages);
            Put("bytes", new FileInfo(target).Length);
            Check(f.Pages == pages && pages >= 3, "pages " + f.Pages + " vs " + pages);
            Check(Regex.IsMatch(f.Raw, @"/Type /Pages /Kids \[[^\]]+\] /Count " + pages + " >>"), "page tree count");
            string all = Md6Squash(f.Text);
            foreach (string want in new string[] { "Informe de pruebas", "Texto con negrita, cursiva,", "acci\u00F3n, ni\u00F1o, \u00BFqu\u00E9? \u00A1s\u00ED!", "\u4E2D\u6587", "\uD83D\uDE00", "Secci\u00F3n 5", "P\u00E1rrafo 3 de la secci\u00F3n 5", "Ctrl+B", "Negrita", "var x = 1;", "hecho", "No encuentro la imagen", "1 / " + pages })
                Check(all.Contains(Md6Squash(want)), "text missing from the PDF: " + want);
            double mapped = f.Glyphs == 0 ? 0 : f.Mapped / (double)f.Glyphs;
            Put("glyphs", f.Glyphs);
            Check(f.Glyphs > 2000 && mapped > 0.98, "glyphs " + f.Glyphs + ", with text " + (mapped * 100).ToString("0.0") + "%");
            Check(Regex.IsMatch(f.Raw, @"/Subtype /Image /Width 900 /Height 300 "), "the picture is not in the PDF");
            Check(f.Raw.Contains("/Outlines ") && Regex.Matches(f.Raw, @"/Title <FEFF").Count >= 1 + 6 * 2, "bookmarks");
            Check(f.Raw.Contains("/Title <FEFF0049006E0066006F0072006D0065"), "document title");
            Check(!f.Raw.Contains("/ca ") && !f.Raw.Contains("/SMask"), "transparency in the PDF");
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "temporary file left");
            // The light look whatever the app's theme: the same drawing in dark mode.
            string dark = Path.Combine(dir, "oscuro.pdf");
            Ds.Apply(2);
            try { Md6Export(Md6LongDoc(6, true), dir, dark, out ms); }
            finally { Ds.Apply(1); }
            Md6PdfFile g = Md6ReadPdf(dark);
            Same(f.Text, g.Text, "text in dark mode");
            foreach (KeyValuePair<int, string> kv in f.Dicts)
                if (kv.Value.StartsWith("<< /Type /Page ", StringComparison.Ordinal))
                {
                    int c = int.Parse(Regex.Match(kv.Value, @"/Contents \[(\d+) 0 R").Groups[1].Value);
                    Same(Encoding.ASCII.GetString(f.Streams[c]), Encoding.ASCII.GetString(g.Streams[c]), "page drawing in dark mode");
                    break;
                }
            Note(pages + " pages, " + f.Glyphs + " glyphs, " + (new FileInfo(target).Length / 1024) + " KB, " + Ms(ms));
        }

        static void Md6PdfKeep()
        {
            // Many headings at varied places: with KeepWithNext none ends a page; without it some would.
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                sb.Append("## T\u00EDtulo " + i + "\n\n");
                for (int p = 0; p <= i % 5; p++) sb.Append("Texto " + i + "." + p + " con una l\u00EDnea y algo m\u00E1s de texto para que ocupe dos l\u00EDneas en la p\u00E1gina impresa del documento.\n\n");
            }
            List<MdBlock> blocks = MarkdownDoc.Parse(sb.ToString());
            int alone = Md6Alone(blocks, true), before = Md6Alone(blocks, false);
            Check(before > 0, "the document never leaves a heading alone without the rule (" + before + ")");
            Check(alone == 0, alone + " headings left alone at the bottom of a page");
            Note("alone without KeepWithNext: " + before + ", with it: " + alone);
        }

        static int Md6Alone(List<MdBlock> blocks, bool keep)
        {
            List<Block> made = new List<Block>();
            FlowDocument doc = MarkdownFlow.Printable(blocks, null, 900, made);
            doc.PageWidth = MdPdf.PageWidth;
            doc.PageHeight = MdPdf.PageHeight;
            doc.PagePadding = new Thickness(75.6, 75.6, 75.6, 94.5);
            doc.ColumnWidth = MdPdf.PageWidth;
            if (!keep) foreach (Block b in made) { Paragraph p = b as Paragraph; if (p != null) p.KeepWithNext = false; }
            DynamicDocumentPaginator pg = (DynamicDocumentPaginator)((IDocumentPaginatorSource)doc).DocumentPaginator;
            pg.PageSize = new Size(MdPdf.PageWidth, MdPdf.PageHeight);
            for (int i = 0; i < 500; i++) { DocumentPage page = pg.GetPage(i); if (page == DocumentPage.Missing || page.Visual == null) break; }
            int n = 0;
            for (int i = 0; i + 1 < blocks.Count; i++)
            {
                if (blocks[i].Kind != MdKind.Heading || made[i] == null || made[i + 1] == null) continue;
                if (pg.GetPageNumber(made[i].ContentStart) != pg.GetPageNumber(made[i + 1].ContentStart)) n++;
            }
            return n;
        }

        static void Md6PdfTiming()
        {
            string dir = WorkDir("pdflong");
            string target = Path.Combine(dir, "largo.pdf");
            double ms;
            int pages = Md6Export(Md6LongDoc(260, false), dir, target, out ms);
            Put("pages", pages);
            Check(pages >= 100, "only " + pages + " pages");
            double per100 = ms * 100 / pages;
            Budget("export of " + pages + " pages", ms, 10000.0 * pages / 100);
            Md6PdfFile f = Md6ReadPdf(target);
            Check(f.Pages == pages && Md6Squash(f.Text).Contains(Md6Squash("Secci\u00F3n 259")), "long PDF incomplete");
            Note(pages + " pages in " + Ms(ms) + " (" + Ms(per100) + " per 100), " + (new FileInfo(target).Length / 1024) + " KB");
        }

        static void Md6PdfWindow()
        {
            string dir = WorkDir("pdfwin");
            MarkdownWindow w = Md6Open(dir, "informe.md", Md6LongDoc(150, false), false);
            string target = Path.Combine(dir, "informe.pdf");
            string suggested = null;
            MarkdownWindow.PickPath = delegate(string s) { suggested = s; return target; };
            try
            {
                Check(w.ExportPdf(), "export not started");
                Same("informe.pdf", suggested, "suggested name");
                Check(w.PdfBusy, "not busy");
                Check(!w.ExportPdf() && Md6Status(w).StartsWith("Ya estoy exportando", StringComparison.Ordinal), "a second export started");
                // The window keeps answering while the PDF is written.
                Stopwatch sw = Stopwatch.StartNew();
                double worst = 0;
                bool progress = false;
                long last = 0;
                System.Windows.Threading.DispatcherTimer tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
                tick.Tick += delegate
                {
                    long now = sw.ElapsedMilliseconds;
                    if (last > 0) worst = Math.Max(worst, now - last);
                    last = now;
                    if (Md6Status(w).StartsWith("Exportando PDF\u2026 p\u00E1gina ", StringComparison.Ordinal)) progress = true;
                };
                tick.Start();
                Check(Until(delegate { return !w.PdfBusy; }, 60000), "export never ended");
                tick.Stop();
                Put("ui_gap_ms", worst);
                Check(progress, "no page count in the status bar");
                Check(worst < 300, "the window stopped answering for " + worst + " ms");
                Check(w.PdfError == null && w.PdfPages > 50, "export: " + w.PdfError + " / " + w.PdfPages);
                Check(Md6Status(w) == "PDF exportado: informe.pdf (" + w.PdfPages.ToString("N0") + " p\u00E1ginas).", "status: " + Md6Status(w));
                Check(Md6ReadPdf(target).Pages == w.PdfPages, "pages on disk");
                // The menu entry exists for Markdown only.
                Note(w.PdfPages + " pages; longest pause of the window " + worst + " ms (" + Ms(sw.Elapsed.TotalMilliseconds) + ")");
            }
            finally { MarkdownWindow.PickPath = null; Shut(w); }
            MarkdownWindow x = Md6Open(dir, "a.xml", "<a/>\n", false);
            try { Check(!x.ExportPdf(), "an XML document exported to PDF"); }
            finally { Shut(x); }
        }

        static void Md6PdfCancel()
        {
            string dir = WorkDir("pdfcancel");
            MarkdownWindow w = Md6Open(dir, "largo.md", Md6LongDoc(300, false), false);
            string target = Path.Combine(dir, "largo.pdf");
            MarkdownWindow.PickPath = delegate(string s) { return target; };
            try
            {
                Check(w.ExportPdf(), "export not started");
                Pump(300);
                Check(w.PdfBusy, "finished too soon to test closing");
                Shut(w);
                Check(Until(delegate { return Directory.GetFiles(dir, "*.tmp").Length == 0 && !File.Exists(target) && w.PdfError != null; }, 20000), "closing left a file or did not stop (" + w.PdfError + ")");
                Same("Cancelado.", w.PdfError, "outcome");
            }
            finally { MarkdownWindow.PickPath = null; if (w.IsVisible) Shut(w); }
        }

        // ---- Review of the Markdown additions (wave 2): what the review found, fixed and kept tested

        static void R5Md()
        {
            Test("w6md", "r5-pictures-on-other-computers-stay-text", R5RemotePictures);
            Test("w6md", "r5-table-paste-looks-at-code-quickly", R5TablePasteQuick);
            Test("w6md", "r5-pdf-strict-reader", R5PdfStrict);
            Test("w6md", "r5-outline-cost-on-a-big-document", R5OutlineCost);
        }

        // A picture on another computer is not loaded for a document that is not on that computer: opening a document (a
        // README, a text attachment of an XML) must not make Windows connect to a server it names and offer it the user's
        // credentials. Such pictures stay as text, like web addresses. Only device paths are used in the window and the
        // PDF, so nothing could leave the machine even if a picture were loaded.
        static void R5RemotePictures()
        {
            Check(MdImages.Remote(@"\\192.0.2.1\s\x.png", @"C:\docs"), "a server named by a document in a local folder");
            Check(MdImages.Remote(@"\\servidor\s\x.png", null), "text with no folder");
            Check(MdImages.Remote(@"\\otro\docs\x.png", @"\\servidor\docs"), "another server");
            Check(!MdImages.Remote(@"\\servidor\docs\imagenes\x.png", @"\\servidor\docs"), "a document on that server");
            Check(!MdImages.Remote(@"\\SERVIDOR\otra\x.png", @"\\servidor\docs"), "server names ignore case");
            Check(!MdImages.Remote(@"C:\docs\imagenes\x.png", @"C:\docs") && !MdImages.Remote(@"Z:\fotos\x.png", @"C:\docs"), "local and mapped drives");
            Check(MdImages.Remote(@"\\?\UNC\otro\s\x.png", @"C:\docs"), "long UNC form");
            Check(MdImages.Remote(MdImages.LocalPath("file://otro/s/x.png", @"C:\docs"), @"C:\docs"), "a file address with a host");
            Check(MdImages.Remote(MdImages.LocalPath(@"\\otro\s\x.png", @"C:\docs"), @"C:\docs"), "a UNC link");
            string dir = WorkDir("r5remote");
            Directory.CreateDirectory(Path.Combine(dir, "imagenes"));
            File.WriteAllBytes(Path.Combine(dir, "imagenes", "a.png"), Md6Png(40, 30, Colors.Red, Colors.Blue));
            string remote = @"\\.\stackshot-r5\x.png";
            Check(MdImages.LocalPath(remote, dir) != null && MdImages.Remote(MdImages.LocalPath(remote, dir), dir), "the device path does not stand for a remote picture");
            string md = "# Remota\n\n![remota](" + remote + ")\n\n![local](imagenes/a.png)\n\nTexto.\n";
            MarkdownWindow w = Md6Open(dir, "remota.md", md, false);
            try
            {
                Check(Until(delegate { List<Border> ps = Md6Pictures(w.PreviewFlow); return ps.Count == 1 && ps[0].Child is Image; }, 6000), "the local picture did not show (or the remote one got a place): " + Md6Pictures(w.PreviewFlow).Count);
                Check(w.PreviewFlow.FlatText().Contains("\u25A3 remota"), "the remote picture is not shown as its text");
            }
            finally { Shut(w); }
            List<Block> made = new List<Block>();
            FlowDocument printed = MarkdownFlow.Printable(MarkdownDoc.Parse(md), dir, 900, made);
            List<Border> hosts = new List<Border>();
            Md6Walk(printed, hosts);
            SameV(1, hosts.Count, "pictures in the PDF");
        }

        // Every Ctrl+V with text in the Markdown editor asks whether it is a sheet. Code with tabs (2 MB, varied
        // indentation, an editor's HTML) must be told apart without splitting all of it into cells on the UI thread: code
        // indented by one tab on every line took 90-105 ms there (the whole split plus a regex over the HTML).
        static void R5TablePasteQuick()
        {
            StringBuilder code = new StringBuilder(), html = new StringBuilder("<html><body><!--StartFragment--><div style=\"font-family: Consolas;\">");
            int i = 0;
            while (code.Length < 2 * 1024 * 1024)
            {
                string line = new string('\t', i % 4) + "valor" + i + " = calcular(" + i + ", \"texto\");";
                code.Append(line).Append("\r\n");
                html.Append("<div><span style=\"color: #9cdcfe;\">").Append(line.Replace("\t", "&nbsp;&nbsp;&nbsp;&nbsp;")).Append("</span></div>");
                i++;
            }
            html.Append("</div><!--EndFragment--></body></html>");
            DataObject d = Md6Clip(code.ToString(), html.ToString(), null, null);
            MdTablePaste.Read(Md6Clip("a\tb\r\n", "<div/>", null, null));
            List<double> ms = new List<double>();
            for (int k = 0; k < 5; k++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                MdTablePaste.Sheet s = MdTablePaste.Read(d);
                ms.Add(sw.Elapsed.TotalMilliseconds);
                Check(s == null, "code taken as a table");
            }
            Budget("2 MB of code with tabs looked at (median)", Median(ms), 16);
            // The worst case: every line indented by exactly one tab (two "cells" each, all the way down).
            StringBuilder flat = new StringBuilder(), flatHtml = new StringBuilder("<html><body><!--StartFragment--><div style=\"font-family: Consolas;\">");
            for (int k = 0; flat.Length < 2 * 1024 * 1024; k++)
            {
                flat.Append("\tvalor").Append(k).Append(" = calcular(").Append(k).Append(", \"texto\");\r\n");
                flatHtml.Append("<div>&nbsp;&nbsp;&nbsp;&nbsp;<span style=\"color: #9cdcfe;\">valor").Append(k).Append("</span> = calcular(").Append(k).Append(");</div>");
            }
            flatHtml.Append("</div><!--EndFragment--></body></html>");
            DataObject same = Md6Clip(flat.ToString(), flatHtml.ToString(), null, null);
            List<double> worst = new List<double>();
            for (int k = 0; k < 5; k++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                Check(MdTablePaste.Read(same) == null, "code indented by one tab taken as a table");
                worst.Add(sw.Elapsed.TotalMilliseconds);
            }
            Budget("2 MB of code all indented by one tab (median)", Median(worst), 16);
            // The quick look agrees with the full split on any text: same cells on every row, and a blank first column.
            MethodInfo shape = typeof(MdTablePaste).GetMethod("Shape", BindingFlags.NonPublic | BindingFlags.Static);
            Random rnd = new Random(6);
            string alphabet = "ab \t\t\n\r\"\"x";
            for (int k = 0; k < 20000; k++)
            {
                char[] cs = new char[rnd.Next(0, 14)];
                for (int c = 0; c < cs.Length; c++) cs[c] = alphabet[rnd.Next(alphabet.Length)];
                string s = new string(cs);
                object[] args = { s, false };
                bool quick = (bool)shape.Invoke(null, args);
                List<List<string>> full = MdTablePaste.Tsv(s);
                bool blank = full != null && full.TrueForAll(delegate(List<string> r) { return r[0].Trim().Length == 0; });
                if (quick != (full != null) || (quick && (bool)args[1] != blank))
                {
                    Check(false, "the quick look and the split disagree on " + OneLine(s.Replace("\t", "\\t").Replace("\r", "\\r")) + ": " + quick + "/" + args[1] + " vs " + (full != null) + "/" + blank);
                    break;
                }
            }
            // A real sheet still is one, and the same shape check catches a ragged last row.
            DataObject sheet = Md6Clip("a\tb\r\n1\t2\r\n3\t4\r\n", null, "<Workbook/>", null);
            Check(MdTablePaste.Read(sheet) != null, "a sheet is no longer seen");
            Check(MdTablePaste.Read(Md6Clip("a\tb\r\n1\t2\r\n3\r\n", null, "<Workbook/>", null)) == null, "a ragged sheet is taken");
            Check(MdTablePaste.Read(Md6Clip("a\tb\r\n\"x\ny\"\t2\r\n", null, "<Workbook/>", null)) != null, "a quoted cell with a line break");
        }

        // ---- A strict PDF reader: what a demanding reader checks before it shows a page

        sealed class R5Obj
        {
            public int Num;
            public long Start, End;
            public string Dict;     // the object's text (for a stream: its dictionary)
            public byte[] Data;     // a stream's decoded bytes
        }

        static uint R5U32(byte[] b, int at) { return (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]); }
        static int R5U16(byte[] b, int at) { return b[at] << 8 | b[at + 1]; }
        static int R5S16(byte[] b, int at) { return (short)(b[at] << 8 | b[at + 1]); }

        static uint R5Adler(byte[] d)
        {
            uint a = 1, b = 0;
            foreach (byte x in d) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        static byte[] R5Flate(byte[] z, int obj)
        {
            Check(z.Length >= 6, "stream " + obj + " too short for zlib");
            Check(z[0] == 0x78 && ((z[0] << 8) | z[1]) % 31 == 0 && (z[1] & 0x20) == 0, "stream " + obj + ": bad zlib header");
            byte[] o;
            using (MemoryStream src = new MemoryStream(z, 2, z.Length - 6))
            using (DeflateStream ds = new DeflateStream(src, CompressionMode.Decompress))
            using (MemoryStream m = new MemoryStream())
            {
                ds.CopyTo(m);
                o = m.ToArray();
            }
            Check(R5U32(z, z.Length - 4) == R5Adler(o), "stream " + obj + ": Adler-32 does not match");
            return o;
        }

        static uint R5Sum(byte[] b, int off, int len)
        {
            uint s = 0;
            for (int i = 0; i < len; i += 4)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < len ? b[off + i + k] : (byte)0);
                s = unchecked(s + v);
            }
            return s;
        }

        // The embedded TrueType subset: directory, checksums, the tables a renderer reads, and every glyph the pages use
        // drawn (also each part of a composite). Returns the advance widths in thousandths of the em.
        static int[] R5Font(byte[] f, string name, Dictionary<int, string> text, HashSet<int> used)
        {
            Check(R5U32(f, 0) == 0x00010000, name + ": not TrueType");
            int n = R5U16(f, 4), sr = R5U16(f, 6), es = R5U16(f, 8), rs = R5U16(f, 10);
            int pow = 1, log = 0;
            while (pow * 2 <= n) { pow *= 2; log++; }
            Check(sr == pow * 16 && es == log && rs == n * 16 - pow * 16, name + ": table directory header");
            Dictionary<string, int[]> t = new Dictionary<string, int[]>();
            string prev = "";
            for (int i = 0; i < n; i++)
            {
                int rec = 12 + 16 * i;
                string tag = Encoding.ASCII.GetString(f, rec, 4);
                Check(string.CompareOrdinal(tag, prev) > 0, name + ": tables not sorted (" + tag + ")");
                prev = tag;
                int off = (int)R5U32(f, rec + 8), len = (int)R5U32(f, rec + 12);
                Check(off % 4 == 0 && off + len <= f.Length, name + ": table " + tag + " out of the file");
                uint sum = R5Sum(f, off, len);
                if (tag == "head") sum = unchecked(sum - R5U32(f, off + 8));
                Check(sum == R5U32(f, rec + 4), name + ": checksum of " + tag);
                t[tag] = new int[] { off, len };
            }
            Check(R5Sum(f, 0, f.Length) == 0xB1B0AFBA, name + ": whole-font checksum (head.checkSumAdjustment)");
            foreach (string need in new string[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "post" }) Check(t.ContainsKey(need), name + ": no " + need);
            int head = t["head"][0];
            Check(R5U32(f, head + 12) == 0x5F0F3CF5, name + ": head magic");
            int upem = R5U16(f, head + 18);
            Check(upem >= 16 && upem <= 16384, name + ": units per em " + upem);
            bool longLoca = R5S16(f, head + 50) == 1;
            int glyphs = R5U16(f, t["maxp"][0] + 4);
            int locaAt = t["loca"][0], glyf = t["glyf"][0], glyfLen = t["glyf"][1];
            Check(t["loca"][1] >= (glyphs + 1) * (longLoca ? 4 : 2), name + ": loca too short");
            Func<int, int> loca = delegate(int g) { return longLoca ? (int)R5U32(f, locaAt + 4 * g) : R5U16(f, locaAt + 2 * g) * 2; };
            for (int g = 0; g < glyphs; g++) Check(loca(g) <= loca(g + 1), name + ": loca goes back at glyph " + g);
            Check(loca(glyphs) <= glyfLen, name + ": loca past glyf");
            int hm = R5U16(f, t["hhea"][0] + 34);
            Check(hm >= 1 && hm <= glyphs && t["hmtx"][1] >= hm * 4 + (glyphs - hm) * 2, name + ": horizontal metrics");
            Queue<int> todo = new Queue<int>(used);
            HashSet<int> seen = new HashSet<int>();
            while (todo.Count > 0)
            {
                int g = todo.Dequeue();
                if (!seen.Add(g)) continue;
                Check(g < glyphs, name + ": glyph " + g + " used but the font has " + glyphs);
                int len = loca(g + 1) - loca(g);
                string s = null;
                bool visible = used.Contains(g) && text.TryGetValue(g, out s) && s.Trim().Length > 0;
                if (len == 0) { Check(!visible, name + ": glyph " + g + " (\"" + s + "\") is empty in the subset"); continue; }
                int at = glyf + loca(g);
                Check(len >= 10, name + ": glyph " + g + " header");
                if (R5S16(f, at) >= 0) continue;
                // Composite: every part must be there too.
                int p = at + 10, end = at + len;
                while (p + 4 <= end)
                {
                    int flags = R5U16(f, p), part = R5U16(f, p + 2);
                    Check(part < glyphs && loca(part + 1) > loca(part), name + ": part " + part + " of glyph " + g + " missing");
                    todo.Enqueue(part);
                    p += 4 + ((flags & 1) != 0 ? 4 : 2) + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
                    if ((flags & 0x20) == 0) break;
                }
            }
            int[] w = new int[glyphs];
            int hmtx = t["hmtx"][0];
            for (int g = 0; g < glyphs; g++) w[g] = (int)Math.Round(R5U16(f, hmtx + 4 * Math.Min(g, hm - 1)) * 1000.0 / upem);
            return w;
        }

        static Dictionary<int, string> R5ToUnicode(string cmap)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            Func<string, string> utf16 = delegate(string hex)
            {
                StringBuilder s = new StringBuilder();
                Check(hex.Length % 4 == 0, "ToUnicode value of odd length: " + hex);
                for (int k = 0; k < hex.Length; k += 4) s.Append((char)Convert.ToInt32(hex.Substring(k, 4), 16));
                return s.ToString();
            };
            foreach (Match m in Regex.Matches(cmap, @"beginbfchar\s*(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]+)>"))
                    map[Convert.ToInt32(e.Groups[1].Value, 16)] = utf16(e.Groups[2].Value);
            foreach (Match m in Regex.Matches(cmap, @"beginbfrange\s*(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match e in Regex.Matches(m.Groups[1].Value, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]+)>"))
                {
                    int a = Convert.ToInt32(e.Groups[1].Value, 16), b = Convert.ToInt32(e.Groups[2].Value, 16);
                    string v = utf16(e.Groups[3].Value);
                    for (int g = a; g <= b; g++) map[g] = v.Substring(0, v.Length - 1) + (char)(v[v.Length - 1] + g - a);
                }
            return map;
        }

        // Reads the whole file strictly: cross-reference table, every object, stream lengths and zlib checksums, references,
        // the page tree, fonts and their subsets, widths, and the text through the ToUnicode maps. The text by page.
        static List<string> R5ReadPdf(string path, int pagesExpected)
        {
            byte[] b = File.ReadAllBytes(path);
            string raw = Encoding.GetEncoding(28591).GetString(b);
            Check(raw.StartsWith("%PDF-1.", StringComparison.Ordinal), "header");
            int nl = raw.IndexOf('\n');
            Check(nl > 0 && b[nl + 1] == '%' && b[nl + 2] >= 128 && b[nl + 3] >= 128 && b[nl + 4] >= 128 && b[nl + 5] >= 128, "no binary comment after the header");
            Check(raw.EndsWith("%%EOF\n", StringComparison.Ordinal), "end of file marker");
            Check(!raw.Contains("/ObjStm") && !raw.Contains("/XRefStm") && !raw.Contains("/Type /XRef"), "object or cross-reference streams in a PDF 1.4 file");
            Match sx = Regex.Match(raw, @"startxref\n(\d+)\n%%EOF\n$");
            Check(sx.Success, "startxref");
            int xref = int.Parse(sx.Groups[1].Value);
            Check(string.CompareOrdinal(raw, xref, "xref\n0 ", 0, 7) == 0, "startxref does not point at the table");
            int lineEnd = raw.IndexOf('\n', xref + 5);
            int size = int.Parse(raw.Substring(xref + 7, lineEnd - xref - 7));
            int entries = lineEnd + 1;
            Check(raw.Substring(entries, 20) == "0000000000 65535 f \n", "entry 0");
            Dictionary<int, R5Obj> objs = new Dictionary<int, R5Obj>();
            List<R5Obj> byPlace = new List<R5Obj>();
            for (int i = 1; i < size; i++)
            {
                string e = raw.Substring(entries + 20 * i, 20);
                Check(Regex.IsMatch(e, @"^\d{10} 00000 n \n$"), "entry " + i + " is not 20 bytes: " + OneLine(e));
                int off = int.Parse(e.Substring(0, 10));
                string headText = i + " 0 obj\n";
                Check(off < xref && string.CompareOrdinal(raw, off, headText, 0, headText.Length) == 0, "object " + i + " is not where the table says");
                R5Obj o = new R5Obj { Num = i, Start = off };
                int body = off + headText.Length;
                int st = raw.IndexOf("stream\n", body, StringComparison.Ordinal), end = raw.IndexOf("\nendobj\n", body, StringComparison.Ordinal);
                Check(end > 0, "object " + i + " has no endobj");
                if (st >= 0 && st < end && raw.Substring(body, st - body).TrimEnd().EndsWith(">>", StringComparison.Ordinal))
                {
                    o.Dict = raw.Substring(body, st - body);
                    Match lm = Regex.Match(o.Dict, @"/Length (\d+)( 0 R)?");
                    Check(lm.Success && !lm.Groups[2].Success, "stream " + i + " without a direct length");
                    int len = int.Parse(lm.Groups[1].Value), data = st + 7;
                    Check(data + len + 18 <= b.Length && raw.Substring(data + len, 18) == "\nendstream\nendobj\n", "stream " + i + ": the length is wrong");
                    byte[] z = new byte[len];
                    Array.Copy(b, data, z, 0, len);
                    o.Data = o.Dict.Contains("/FlateDecode") ? R5Flate(z, i) : z;
                    o.End = data + len + 18;
                }
                else
                {
                    o.Dict = raw.Substring(body, end - body);
                    o.End = end + 8;
                }
                objs[i] = o;
                byPlace.Add(o);
            }
            byPlace.Sort(delegate(R5Obj x, R5Obj y) { return x.Start.CompareTo(y.Start); });
            for (int i = 1; i < byPlace.Count; i++) Check(byPlace[i - 1].End <= byPlace[i].Start, "objects " + byPlace[i - 1].Num + " and " + byPlace[i].Num + " overlap");
            Check(byPlace[byPlace.Count - 1].End <= xref, "the last object runs into the table");
            Match tr = Regex.Match(raw.Substring(xref), @"trailer\n<< /Size (\d+) /Root (\d+) 0 R /Info (\d+) 0 R >>\nstartxref\n");
            Check(tr.Success && int.Parse(tr.Groups[1].Value) == size, "trailer");
            foreach (R5Obj o in objs.Values)
                foreach (Match r in Regex.Matches(o.Dict, @"\b(\d+) 0 R\b"))
                {
                    int to = int.Parse(r.Groups[1].Value);
                    Check(to >= 1 && to < size, "object " + o.Num + " points at " + to + ", outside the table");
                }
            Func<string, string, int> refOf = delegate(string dict, string key)
            {
                Match m = Regex.Match(dict, "/" + key + @" (\d+) 0 R");
                Check(m.Success, "no " + key + " in " + OneLine(dict));
                return int.Parse(m.Groups[1].Value);
            };
            string catalog = objs[int.Parse(tr.Groups[2].Value)].Dict;
            Check(catalog.Contains("/Type /Catalog"), "root is not a catalog");
            int pagesObj = refOf(catalog, "Pages");
            string pages = objs[pagesObj].Dict;
            Check(pages.Contains("/Type /Pages"), "page tree");
            List<int> kids = new List<int>();
            foreach (Match k in Regex.Matches(Regex.Match(pages, @"/Kids \[([^\]]*)\]").Groups[1].Value, @"(\d+) 0 R")) kids.Add(int.Parse(k.Groups[1].Value));
            Check(Regex.IsMatch(pages, @"/Count " + kids.Count + @"\b") && kids.Count == pagesExpected, "page count: " + kids.Count + " kids, " + pagesExpected + " exported, " + OneLine(pages));
            // Fonts: resource name -> (ToUnicode map, widths, glyphs used).
            Dictionary<string, Dictionary<int, string>> maps = new Dictionary<string, Dictionary<int, string>>();
            Dictionary<string, Dictionary<int, int>> widths = new Dictionary<string, Dictionary<int, int>>();
            Dictionary<string, HashSet<int>> usedBy = new Dictionary<string, HashSet<int>>();
            Dictionary<string, int> fontFile = new Dictionary<string, int>();
            HashSet<string> images = new HashSet<string>();
            List<string> byPage = new List<string>();
            int resObj = -1;
            foreach (int p in kids)
            {
                string pd = objs[p].Dict;
                Check(pd.Contains("/Type /Page ") && refOf(pd, "Parent") == pagesObj, "page " + p + ": type or parent");
                Check(pd.Contains("/MediaBox [0 0 595.28 841.89]"), "page " + p + " is not A4");
                int res = refOf(pd, "Resources");
                if (resObj < 0)
                {
                    resObj = res;
                    string rd = objs[res].Dict;
                    foreach (Match fm in Regex.Matches(Regex.Match(rd, @"/Font <<([^>]*)>>").Groups[1].Value, @"/(\w+) (\d+) 0 R"))
                    {
                        string fname = fm.Groups[1].Value;
                        string t0 = objs[int.Parse(fm.Groups[2].Value)].Dict;
                        Check(t0.Contains("/Type /Font") && t0.Contains("/Subtype /Type0") && t0.Contains("/Encoding /Identity-H"), "font " + fname + ": " + OneLine(t0));
                        string baseFont = Regex.Match(t0, @"/BaseFont /(\S+)").Groups[1].Value;
                        Check(Regex.IsMatch(baseFont, @"^[A-Z]{6}\+\S+$"), "font " + fname + ": subset name " + baseFont);
                        int cid = int.Parse(Regex.Match(t0, @"/DescendantFonts \[(\d+) 0 R\]").Groups[1].Value);
                        string cd = objs[cid].Dict;
                        Check(cd.Contains("/Subtype /CIDFontType2") && cd.Contains("/CIDToGIDMap /Identity") && cd.Contains("/BaseFont /" + baseFont + " "), "CID font of " + fname + ": " + OneLine(cd));
                        string desc = objs[refOf(cd, "FontDescriptor")].Dict;
                        Check(desc.Contains("/Type /FontDescriptor") && desc.Contains("/FontName /" + baseFont + " "), "descriptor of " + fname);
                        fontFile[fname] = refOf(desc, "FontFile2");
                        maps[fname] = R5ToUnicode(Encoding.ASCII.GetString(objs[refOf(t0, "ToUnicode")].Data));
                        Dictionary<int, int> wd = new Dictionary<int, int>();
                        string warr = Regex.Match(cd, @"/W \[(.*)\] /CIDToGIDMap").Groups[1].Value;
                        foreach (Match wm in Regex.Matches(warr, @"(\d+) \[([\d ]+)\]"))
                        {
                            int g0 = int.Parse(wm.Groups[1].Value);
                            string[] ws = wm.Groups[2].Value.Trim().Split(' ');
                            for (int k = 0; k < ws.Length; k++) wd[g0 + k] = int.Parse(ws[k]);
                        }
                        widths[fname] = wd;
                        usedBy[fname] = new HashSet<int>();
                    }
                    foreach (Match im in Regex.Matches(Regex.Match(rd, @"/XObject <<([^>]*)>>").Groups[1].Value, @"/(\w+) (\d+) 0 R"))
                    {
                        string id = objs[int.Parse(im.Groups[2].Value)].Dict;
                        Match sz = Regex.Match(id, @"/Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace /DeviceRGB /BitsPerComponent 8");
                        Check(sz.Success, "image " + im.Groups[1].Value + ": " + OneLine(id));
                        Check(objs[int.Parse(im.Groups[2].Value)].Data.Length == int.Parse(sz.Groups[1].Value) * int.Parse(sz.Groups[2].Value) * 3, "image " + im.Groups[1].Value + ": pixel data size");
                        images.Add(im.Groups[1].Value);
                    }
                }
                else Check(res == resObj, "pages with different resources");
                StringBuilder text = new StringBuilder();
                foreach (Match cm in Regex.Matches(Regex.Match(pd, @"/Contents (\[[^\]]*\]|\d+ 0 R)").Groups[1].Value, @"(\d+) 0 R"))
                {
                    string ops = Encoding.ASCII.GetString(objs[int.Parse(cm.Groups[1].Value)].Data);
                    int q = Regex.Matches(ops, @"(?m)^q$").Count, Q = Regex.Matches(ops, @"(?m)^Q$").Count;
                    Check(q == Q && q > 0, "q/Q not balanced (" + q + "/" + Q + ")");
                    Check(Regex.Matches(ops, @"\bBT\b").Count == Regex.Matches(ops, @"\bET\b").Count, "BT/ET not balanced");
                    foreach (Match dm in Regex.Matches(ops, @"/(\w+) Do\b")) Check(images.Contains(dm.Groups[1].Value), "image " + dm.Groups[1].Value + " is not in the resources");
                    string cur = null;
                    foreach (Match om in Regex.Matches(ops, @"/(\w+) [\d.]+ Tf|\[([^\]]*)\] TJ|<([0-9A-F]+)> Tj|\bET\b"))
                    {
                        if (om.Groups[1].Success) { cur = om.Groups[1].Value; Check(maps.ContainsKey(cur), "font " + cur + " is not in the resources"); continue; }
                        if (om.Value == "ET") { text.Append('\n'); continue; }
                        Check(cur != null, "text before any font");
                        string hexes = om.Groups[2].Success ? om.Groups[2].Value : "<" + om.Groups[3].Value + ">";
                        Check(Regex.IsMatch(hexes, @"^(\s*(<([0-9A-F]{4})+>|-?[\d.]+))*\s*$"), "TJ array: " + hexes);
                        foreach (Match hm in Regex.Matches(hexes, @"<([0-9A-F]+)>"))
                            for (int k = 0; k < hm.Groups[1].Value.Length; k += 4)
                            {
                                int g = Convert.ToInt32(hm.Groups[1].Value.Substring(k, 4), 16);
                                usedBy[cur].Add(g);
                                int wv;
                                Check(widths[cur].TryGetValue(g, out wv), "glyph " + g + " of " + cur + " has no width");
                                string s;
                                if (maps[cur].TryGetValue(g, out s)) text.Append(s);
                            }
                    }
                }
                byPage.Add(text.ToString());
            }
            foreach (string fname in maps.Keys)
            {
                int[] fw = R5Font(objs[fontFile[fname]].Data, fname, maps[fname], usedBy[fname]);
                foreach (KeyValuePair<int, int> kv in widths[fname])
                    Check(kv.Key < fw.Length && fw[kv.Key] == kv.Value, fname + ": width of glyph " + kv.Key + " is " + kv.Value + " in /W, " + (kv.Key < fw.Length ? fw[kv.Key].ToString() : "none") + " in the font");
            }
            // Bookmarks: a well-formed list under the outline root, each opening a page of the document.
            Match ol = Regex.Match(catalog, @"/Outlines (\d+) 0 R");
            if (ol.Success)
            {
                string root = objs[int.Parse(ol.Groups[1].Value)].Dict;
                int first = refOf(root, "First"), last = refOf(root, "Last"), n = 0, at = first, before = -1;
                while (true)
                {
                    string it = objs[at].Dict;
                    Check(refOf(it, "Parent") == int.Parse(ol.Groups[1].Value), "bookmark " + at + ": parent");
                    Match prevM = Regex.Match(it, @"/Prev (\d+) 0 R");
                    Check(before < 0 ? !prevM.Success : prevM.Success && int.Parse(prevM.Groups[1].Value) == before, "bookmark " + at + ": previous");
                    Check(kids.Contains(int.Parse(Regex.Match(it, @"/Dest \[(\d+) 0 R").Groups[1].Value)), "bookmark " + at + " opens no page");
                    n++;
                    Match next = Regex.Match(it, @"/Next (\d+) 0 R");
                    if (!next.Success) break;
                    before = at;
                    at = int.Parse(next.Groups[1].Value);
                }
                Check(at == last && Regex.IsMatch(root, @"/Count " + n + @"\b"), "bookmark list: " + n + " items, last " + at + " vs " + last);
            }
            return byPage;
        }

        static void R5PdfStrict()
        {
            string dir = WorkDir("r5pdf");
            Directory.CreateDirectory(Path.Combine(dir, "imagenes"));
            File.WriteAllBytes(Path.Combine(dir, "imagenes", "foto.png"), Md6Png(640, 200, Colors.SteelBlue, Colors.Orange));
            string extra = "## Letras\n\n\u00D1and\u00FA ping\u00FCino, \u00E7a va, \u20AC 12,50; \u00BFQu\u00E9 tal? \u00A1Ol\u00E9! **Negrita \u00E1\u00E9\u00ED\u00F3\u00FA** y *cursiva \u00C1\u00C9\u00CD\u00D3\u00DA*.\n\n" +
                           "Emoji \uD83D\uDE00 \uD83D\uDC4D y \u65E5\u672C\u8A9E en una l\u00EDnea.\n\n```\nc\u00F3digo = \"\u00F1\";\n```\n\n";
            string target = Path.Combine(dir, "estricto.pdf");
            double ms;
            int pages = Md6Export(extra + Md6LongDoc(8, true), dir, target, out ms);
            List<string> text = R5ReadPdf(target, pages);
            string all = Md6Squash(string.Join("\n", text.ToArray()));
            foreach (string want in new string[] { "\u00D1and\u00FA ping\u00FCino, \u00E7a va, \u20AC 12,50; \u00BFQu\u00E9 tal? \u00A1Ol\u00E9!", "Negrita \u00E1\u00E9\u00ED\u00F3\u00FA", "cursiva \u00C1\u00C9\u00CD\u00D3\u00DA",
                                                    "\uD83D\uDE00", "\uD83D\uDC4D", "\u65E5\u672C\u8A9E", "c\u00F3digo = \"\u00F1\";", "Secci\u00F3n 7", "1 / " + pages, pages + " / " + pages })
                Check(all.Contains(Md6Squash(want)), "text missing or out of order: " + want);
            // Accents come out as one character each (what search and copy expect), never a letter plus a mark.
            Check(all.IndexOf('\u0301') < 0 && all.IndexOf('\u0303') < 0, "accents as combining marks");
            Check(text[0].Contains("Letras") && text[text.Count - 1].Contains(pages + " / " + pages), "pages in order");
            Note(pages + " pages read strictly, " + (new FileInfo(target).Length / 1024) + " KB");
        }

        // The outline follows the preview while scrolling: finding the section on screen must stay cheap with thousands of
        // headings, and refreshing the list after a pause in typing too.
        static void R5OutlineCost()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 3000; i++) sb.Append("## Apartado " + i + "\n\nTexto del apartado " + i + ".\n\n");
            string dir = WorkDir("r5outline");
            MarkdownWindow w = Md6Open(dir, "largo.md", sb.ToString(), false);
            try
            {
                w.SetOutline(true);
                Pump(300);
                Check(w.Outline.Count == 3000, "headings: " + w.Outline.Count);
                List<double> refresh = new List<double>();
                for (int k = 0; k < 5; k++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Call(w, "RefreshOutline");
                    refresh.Add(sw.Elapsed.TotalMilliseconds);
                }
                ScrollViewer sv = w.PreviewFlow.Scroller;
                List<double> follow = new List<double>();
                for (int k = 1; k <= 10; k++)
                {
                    sv.ScrollToVerticalOffset(sv.ScrollableHeight * k / 11.0);
                    Idle();
                    Stopwatch sw = Stopwatch.StartNew();
                    Call(w, "OutlineFollowPreview");
                    follow.Add(sw.Elapsed.TotalMilliseconds);
                    Check(w.OutlineCurrent > 0, "no section on screen at " + k);
                }
                List<MdBlock> blocks = MarkdownDoc.Parse(sb.ToString());
                Dictionary<string, string> memo = new Dictionary<string, string>(StringComparer.Ordinal);
                Stopwatch sp = Stopwatch.StartNew();
                List<MdHeading> hs = MdOutline.Headings(blocks, memo);
                double headings = sp.Elapsed.TotalMilliseconds;
                sp.Restart();
                List<MdHeading> again = MdOutline.Headings(blocks, memo);
                double remembered = sp.Elapsed.TotalMilliseconds;
                Check(again.Count == hs.Count && again[2999].Text == "Apartado 2999" && memo.Count == 3000, "remembered headings");
                sp.Restart();
                MdOutline.Locate(hs, sb.ToString());
                double lines = sp.Elapsed.TotalMilliseconds;
                sp.Restart();
                Call(w, "OutlineFollowNow");
                Add("headings " + Ms(headings) + " (again " + Ms(remembered) + "), lines " + Ms(lines) + ", follow " + Ms(sp.Elapsed.TotalMilliseconds));
                Budget("outline refresh with 3,000 headings (median)", Median(refresh), 16);
                Budget("section on screen with 3,000 headings (median)", Median(follow), 16);
            }
            finally { Shut(w); }
        }
    }
}

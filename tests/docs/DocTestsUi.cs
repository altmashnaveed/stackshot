// render, editor, window, perf: the window and the editor parked far off screen (never on the desktop), drawn to bitmaps;
// alerts and the save dialog answered by the test.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        static object Call(object o, string method, params object[] args)
        {
            foreach (MethodInfo m in o.GetType().GetMethods(NP | BindingFlags.Public))
                if (m.Name == method && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(o, args); }
                    catch (TargetInvocationException e) { throw e.InnerException; }
                }
            throw new TestFailed("no method " + method + "/" + args.Length);
        }

        static T Field<T>(object o, string name)
        {
            return (T)o.GetType().GetField(name, NP).GetValue(o);
        }

        // A keyboard whose modifier keys are whatever the test says: the real one cannot be held down headless.
        sealed class FakeKeys : System.Windows.Input.KeyboardDevice
        {
            readonly System.Windows.Input.ModifierKeys mods;
            public FakeKeys(System.Windows.Input.ModifierKeys m) : base(System.Windows.Input.InputManager.Current) { mods = m; }
            protected override System.Windows.Input.KeyStates GetKeyStatesFromSystem(System.Windows.Input.Key key)
            {
                System.Windows.Input.ModifierKeys need = key == System.Windows.Input.Key.LeftCtrl ? System.Windows.Input.ModifierKeys.Control
                    : key == System.Windows.Input.Key.LeftShift ? System.Windows.Input.ModifierKeys.Shift
                    : key == System.Windows.Input.Key.LeftAlt ? System.Windows.Input.ModifierKeys.Alt : System.Windows.Input.ModifierKeys.None;
                return need != System.Windows.Input.ModifierKeys.None && (mods & need) != 0 ? System.Windows.Input.KeyStates.Down : System.Windows.Input.KeyStates.None;
            }
        }

        static readonly Dictionary<System.Windows.Input.ModifierKeys, FakeKeys> keyboards = new Dictionary<System.Windows.Input.ModifierKeys, FakeKeys>();

        // A key press as WPF routes it: PreviewKeyDown from the window down to target, then KeyDown back up.
        static void Press(UIElement target, System.Windows.Input.Key k, System.Windows.Input.ModifierKeys mods)
        {
            FakeKeys kb;
            if (!keyboards.TryGetValue(mods, out kb)) { kb = new FakeKeys(mods); keyboards[mods] = kb; }
            Check(kb.Modifiers == mods, "fake keyboard modifiers " + kb.Modifiers);
            PresentationSource src = PresentationSource.FromVisual(target);
            System.Windows.Input.KeyEventArgs e = new System.Windows.Input.KeyEventArgs(kb, src, 0, k) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e);
            if (e.Handled) return;
            e = new System.Windows.Input.KeyEventArgs(kb, src, 0, k) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
            target.RaiseEvent(e);
        }

        static void Collect<T>(DependencyObject o, List<T> into) where T : DependencyObject
        {
            if (o is T) into.Add((T)o);
            int n = VisualTreeHelper.GetChildrenCount(o);
            for (int i = 0; i < n; i++) Collect(VisualTreeHelper.GetChild(o, i), into);
        }

        static void Park(Window w)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -32000;
            w.Top = -32000;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Show();
        }

        // A document window off screen, registered as the open one (as Explorer or the tray would open it).
        static MarkdownWindow NewWindow()
        {
            ConstructorInfo ci = typeof(MarkdownWindow).GetConstructors(NP)[0];
            MarkdownWindow w = (MarkdownWindow)ci.Invoke(new object[] { null });
            typeof(MarkdownWindow).GetField("open", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, w);
            Park(w);
            Pump(200);
            return w;
        }

        static void Shut(MarkdownWindow w)
        {
            Func<string, string, string[], int> keep = DocAlert.Answer;
            DocAlert.Answer = delegate { return 1; };
            try { w.Close(); } catch { }
            DocAlert.Answer = keep;
            Pump(50);
        }

        static void Open(MarkdownWindow w, string path)
        {
            Call(w, "LoadFile", path);
            Check(Until(delegate { return w.Text != null && Path.GetFileName(path) == (w.Doc != null && w.Doc.Path != null ? Path.GetFileName(w.Doc.Path) : null); }, 8000), "file did not load: " + path);
            Pump(150);
        }

        static BitmapSource Snap(FrameworkElement root)
        {
            root.UpdateLayout();
            double ww = root.ActualWidth, wh = root.ActualHeight;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Ds.Brush(Ds.Dark ? Ds.Rgb(32, 32, 34) : Ds.Rgb(238, 238, 242)), null, new Rect(0, 0, ww, wh));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, ww, wh));
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)ww, (int)wh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        static void SavePng(BitmapSource b, string name)
        {
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(b));
            using (FileStream fs = File.Create(Path.Combine(resultDir, name))) e.Save(fs);
        }

        // How many distinct colours (coarsely): a blank or broken render has very few.
        static int Variety(BitmapSource b)
        {
            int w = b.PixelWidth, h = b.PixelHeight;
            byte[] px = new byte[w * h * 4];
            b.CopyPixels(px, w * 4, 0);
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < px.Length; i += 16) seen.Add((px[i] >> 4) | ((px[i + 1] >> 4) << 4) | ((px[i + 2] >> 4) << 8));
            return seen.Count;
        }

        static string Put(string dir, string name, string content)
        {
            string p = Path.Combine(dir, name);
            File.WriteAllText(p, content, new UTF8Encoding(false));
            return p;
        }

        const string Md = "# Notas de versi\u00F3n\n\nStackshot **2.1** trae un *editor* de verdad.\n\n## Novedades\n\n- Guardar con `Ctrl+S`\n- Buscar y reemplazar\n- [x] Formato\n\n| Tecla | Acci\u00F3n |\n|---|---|\n| Ctrl+B | Negrita |\n\n> Una cita.\n\n```\ncodigo();\n```\n";
        const string Xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<pedido id=\"7\" xmlns:x=\"urn:x\">\n  <!-- cliente -->\n  <cliente nombre=\"Pe\u00F1a\">Ana &amp; Luis</cliente>\n  <lineas>\n    <linea sku=\"A-1\" cantidad=\"2\"/>\n    <linea sku=\"B-2\" cantidad=\"1\"><![CDATA[nota]]></linea>\n  </lineas>\n</pedido>\n";

        // ---- render

        static void RenderSuite()
        {
            foreach (int theme in new int[] { 1, 2 })
            {
                int th = theme;
                string label = th == 1 ? "light" : "dark";
                Test("render", "window-" + label, delegate
                {
                    Ds.Apply(th);
                    string dir = WorkDir("render");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        BitmapSource zone = Snap((FrameworkElement)w.Content);
                        SavePng(zone, "zone-" + label + ".png");
                        Open(w, Put(dir, "notas.md", Md));
                        BitmapSource view = Snap((FrameworkElement)w.Content);
                        SavePng(view, "md-view-" + label + ".png");
                        w.SetEditing(true);
                        Pump(400);
                        Call(w, "OpenFind", true);
                        Pump(300);
                        BitmapSource edit = Snap((FrameworkElement)w.Content);
                        SavePng(edit, "md-edit-" + label + ".png");
                        Check(w.Editor != null && w.Editor.PaintedLines > 5, "editor painted " + (w.Editor == null ? -1 : w.Editor.PaintedLines) + " lines");
                        Shut(w);
                        w = NewWindow();
                        Open(w, Put(dir, "pedido.xml", Xml));
                        Check(Until(delegate { return w.Tree != null && w.Tree.Rows.Count > 0; }, 5000), "tree not shown");
                        BitmapSource tree = Snap((FrameworkElement)w.Content);
                        SavePng(tree, "xml-view-" + label + ".png");
                        w.SetEditing(true);
                        Pump(500);
                        BitmapSource xe = Snap((FrameworkElement)w.Content);
                        SavePng(xe, "xml-edit-" + label + ".png");
                        string[] names = { "zone", "md-view", "md-edit", "xml-view", "xml-edit" };
                        BitmapSource[] shots = { zone, view, edit, tree, xe };
                        for (int i = 0; i < shots.Length; i++)
                        {
                            BitmapSource b = shots[i];
                            Check(b.PixelWidth >= 700 && b.PixelHeight >= 450, names[i] + " size " + b.PixelWidth + "x" + b.PixelHeight);
                            // The empty start page is plain by design; documents have text in several colours.
                            Check(Variety(b) > (i == 0 ? 6 : 20), names[i] + " looks blank (" + Variety(b) + " colours)");
                        }
                        Note("PNGs in " + resultDir);
                    }
                    finally { Shut(w); Ds.Apply(1); }
                });
                Test("render", "alert-" + label, delegate
                {
                    Ds.Apply(th);
                    ConstructorInfo ci = typeof(DocAlert).GetConstructors(NP)[0];
                    DocAlert a = (DocAlert)ci.Invoke(new object[] { "\u00BFGuardar los cambios en \u00ABnotas.md\u00BB?", "Si no los guardas, se perder\u00E1n.", new string[] { "Guardar", "No guardar", "Cancelar" }, 0, 2 });
                    try
                    {
                        Park(a);
                        Pump(300);
                        BitmapSource b = Snap((FrameworkElement)a.Content);
                        SavePng(b, "alert-" + label + ".png");
                        Check(Variety(b) > 10, "blank alert");
                    }
                    finally { a.Close(); Ds.Apply(1); }
                });
            }
        }

        // ---- editor

        static Window Host(MarkdownEditor ed, double width, double height)
        {
            Window w = new Window { Width = width, Height = height, WindowStyle = WindowStyle.None, Content = ed };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            Park(w);
            Pump(150);
            return w;
        }

        static bool[] Mask(FrameworkElement e)
        {
            e.UpdateLayout();
            int w = (int)e.ActualWidth, h = (int)e.ActualHeight;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(e), null, new Rect(0, 0, w, h));
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            byte[] px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);
            bool[] m = new bool[w * h];
            for (int i = 0; i < m.Length; i++) m[i] = Math.Min(px[i * 4], Math.Min(px[i * 4 + 1], px[i * 4 + 2])) < 170;
            return m;
        }

        static void EditorSuite()
        {
            Ds.Apply(1);
            Test("editor", "colours-sit-on-the-text", delegate
            {
                // MX-05: the coloured text is drawn where the TextBox itself would draw it, at every width (wrapping too).
                StringBuilder sb = new StringBuilder("# T\u00EDtulo con acentos\n");
                for (int i = 0; i < 6; i++) sb.Append("Un p\u00E1rrafo largo con `c\u00F3digo`, **negrita** y\ttabuladores que se parte en varias l\u00EDneas visuales, n\u00FAmero " + i + ".\n");
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, sb.ToString(), false);
                ed.Plain = true;
                Window w = Host(ed, 420, 500);
                try
                {
                    double worst = 0;
                    for (int width = 420; width <= 900; width += 80)
                    {
                        w.Width = width;
                        ed.Box.Foreground = Brushes.Transparent;
                        ((Grid)ed.Child).Children[0].Visibility = Visibility.Visible;
                        ed.Repaint();
                        Pump(60);
                        bool[] tint = Mask(ed);
                        ed.Box.Foreground = Brushes.Black;
                        ((Grid)ed.Child).Children[0].Visibility = Visibility.Hidden;
                        Pump(60);
                        bool[] box = Mask(ed);
                        int both = 0, only = 0;
                        for (int i = 0; i < tint.Length && i < box.Length; i++) { if (tint[i] && box[i]) both++; else if (tint[i] || box[i]) only++; }
                        double r = both == 0 ? 1 : only / (double)(both + only);
                        worst = Math.Max(worst, r);
                        if (both <= 1000 || only / (double)Math.Max(1, both + only) >= 0.06)
                        {
                            SavePng(Snap(ed), "align-box-" + width + ".png");
                            ed.Box.Foreground = Brushes.Transparent;
                            ((Grid)ed.Child).Children[0].Visibility = Visibility.Visible;
                            ed.Repaint();
                            Pump(100);
                            SavePng(Snap(ed), "align-tint-" + width + ".png");
                            Add("painted " + ed.PaintedLines + " lines");
                        }
                        Check(both > 1000, "no ink at width " + width);
                        Check(r < 0.06, "colours off the text at width " + width + ": " + (r * 100).ToString("0.0") + "% differs");
                    }
                    Note("worst " + (worst * 100).ToString("0.00") + "% of ink pixels differ");
                }
                finally { w.Close(); }
            });
            Test("editor", "goto-error-on-wrapped-text", delegate
            {
                string t = "<a b=\"" + new string('x', 700) + "\">\n  <c>\n</a>";
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, t, true);
                Window w = Host(ed, 600, 400);
                try
                {
                    ed.GoTo(2, 4);
                    Check(ed.Box.CaretIndex == t.IndexOf("\n  <c>") + 4, "caret at " + ed.Box.CaretIndex + ", expected " + (t.IndexOf("\n  <c>") + 4));
                    ed.SetError(EditCommands.Offset(t, 2, 4));
                    Pump(50);
                    Check(ed.ErrorOffset == t.IndexOf("<c>") + 1, "error offset " + ed.ErrorOffset);
                }
                finally { w.Close(); }
            });
            Test("editor", "one-undo-step-per-command", delegate
            {
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, "uno dos tres", false);
                Window w = Host(ed, 600, 300);
                try
                {
                    // Loading the text is not an edit: Ctrl+Z must never empty the editor.
                    Check(!ed.Box.CanUndo, "the loaded text can be undone");
                    ed.Box.Select(4, 3);
                    ed.Apply(EditCommands.Wrap(ed.Text, 4, 3, "**", "texto"));
                    Same("uno **dos** tres", ed.Text, "bold");
                    ed.SetText("UNO **dos** tres!");
                    Same("UNO **dos** tres!", ed.Text, "set text");
                    ed.Box.Undo();
                    Same("uno **dos** tres", ed.Text, "undo of SetText");
                    ed.Box.Undo();
                    Same("uno dos tres", ed.Text, "undo of bold");
                    Check(ed.Box.CanRedo, "no redo");
                }
                finally { w.Close(); }
            });
            Test("editor", "long-lines-always-wrap", delegate
            {
                // An unwrapped 600 KB line took WPF minutes: long lines force wrapping, also when they arrive later.
                StringBuilder sb = new StringBuilder();
                while (sb.Length < 600 * 1024) sb.Append("<a b=\"1\">x</a>\n");
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, sb.ToString(), true);
                Check(!ed.Wraps, "short lines of a big document should not wrap");
                string one = new string('y', 5000);
                ed.Apply(TextEdit.Make(0, 0, one, 0, 0));
                Check(ed.Wraps, "a 5,000-character line did not turn wrapping on");
                MarkdownEditor ed2 = new MarkdownEditor(Ds.Brushes, "<r>" + new string('z', 700 * 1024) + "</r>", true);
                Check(ed2.Wraps, "a long single line opened unwrapped");
            });
            Test("editor", "big-documents-stay-light", delegate
            {
                StringBuilder sb = new StringBuilder();
                while (sb.Length < 700 * 1024) sb.Append("Una l\u00EDnea de texto de prueba con algo de **formato** y `c\u00F3digo`.\n");
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, sb.ToString(), false);
                Check(ed.Box.TextWrapping == TextWrapping.NoWrap, "a 700 KB document wraps");
                Window w = Host(ed, 900, 600);
                try
                {
                    Pump(200);
                    Check(ed.PaintedLines > 10 && ed.PaintedLines < 60, "painted " + ed.PaintedLines + " lines");
                    ed.Select(sb.Length - 10, 0);
                    Pump(100);
                    List<int[]> lines = ed.VisibleLines();
                    Check(lines.Count > 0 && lines[lines.Count - 1][0] + lines[lines.Count - 1][1] >= sb.Length - 80, "end not on screen");
                }
                finally { w.Close(); }
            });
        }

        // ---- window

        static void WindowSuite()
        {
            Ds.Apply(1);
            int asked = 0;
            int answer = 2;
            DocAlert.Answer = delegate(string title, string msg, string[] buttons) { asked++; return answer; };
            try
            {
                Test("window", "edit-save-dirty-title", delegate
                {
                    string dir = WorkDir("win");
                    string p = Path.Combine(dir, "notas.md");
                    File.WriteAllBytes(p, Encoding.UTF8.GetBytes("# Hola\r\n\r\nTexto.\r\n"));
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, p);
                        Check(!MarkdownWindow.AnyUnsaved && !w.Title.StartsWith("\u25CF"), "dirty right after opening");
                        w.SetEditing(true);
                        Pump(200);
                        w.Editor.Apply(TextEdit.Make(w.Editor.Text.Length, 0, "M\u00E1s.\n", 0, 0));
                        Pump(100);
                        Check(MarkdownWindow.AnyUnsaved && w.Title.StartsWith("\u25CF "), "not dirty after typing: " + w.Title);
                        Check(w.Save(false), "save failed");
                        Same("# Hola\r\n\r\nTexto.\r\nM\u00E1s.\r\n", Encoding.UTF8.GetString(File.ReadAllBytes(p)), "file on disk");
                        Check(!MarkdownWindow.AnyUnsaved && !w.Title.StartsWith("\u25CF"), "still dirty after saving");
                        // Undo back to the saved text clears the dot again only when equal.
                        w.Editor.Box.Undo();
                        Pump(50);
                        Check(MarkdownWindow.AnyUnsaved, "undo after save not dirty");
                    }
                    finally { Shut(w); }
                });
                Test("window", "close-asks-and-can-cancel", delegate
                {
                    string dir = WorkDir("close");
                    string p = Put(dir, "a.md", "texto\n");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, p);
                        w.SetEditing(true);
                        Pump(150);
                        w.Editor.Apply(TextEdit.Make(0, 0, "nuevo ", 6, 0));
                        asked = 0; answer = 2;
                        w.Close();
                        Pump(50);
                        Check(asked == 1 && w.IsVisible, "Cancelar did not keep it open (asked " + asked + ")");
                        Check(!MarkdownWindow.ConfirmCloseAll(), "ConfirmCloseAll ignored Cancelar");
                        answer = 1;
                        Check(MarkdownWindow.ConfirmCloseAll(), "ConfirmCloseAll after No guardar");
                        asked = 0;
                        w.Close();
                        Pump(50);
                        Check(asked == 0, "asked again after ConfirmCloseAll");
                        Check(!w.IsVisible && !MarkdownWindow.IsOpen, "did not close");
                        Same("texto\n", File.ReadAllText(p), "file changed although not saved");
                    }
                    finally { Shut(w); }
                });
                Test("window", "guardar-from-the-alert", delegate
                {
                    string dir = WorkDir("alertsave");
                    string p = Put(dir, "a.md", "uno\n");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, p);
                        w.SetEditing(true);
                        Pump(150);
                        w.Editor.Apply(TextEdit.Make(0, 0, "cero ", 5, 0));
                        answer = 0;
                        Check(MarkdownWindow.ConfirmCloseAll(), "Guardar did not allow closing");
                        Same("cero uno\n", File.ReadAllText(p), "saved text");
                    }
                    finally { Shut(w); }
                });
                Test("window", "open-new-and-paste-ask-first", delegate
                {
                    string dir = WorkDir("ask");
                    string a = Put(dir, "a.md", "primero\n"), b = Put(dir, "b.md", "segundo\n");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, a);
                        w.SetEditing(true);
                        Pump(150);
                        w.Editor.Apply(TextEdit.Make(0, 0, "x", 1, 0));
                        asked = 0; answer = 2;
                        Call(w, "LoadFile", b);
                        Pump(400);
                        Check(asked == 1 && w.Text.StartsWith("xprimero"), "opening another file did not ask, or replaced the text");
                        Call(w, "New");
                        Check(asked == 2 && w.Text != null, "Nuevo did not ask");
                        Call(w, "LoadText", "pegado", null, null);
                        Pump(100);
                        Check(w.Text == "pegado", "LoadText runs only after asking (callers ask)");
                    }
                    finally { Shut(w); }
                });
                Test("window", "pasted-text-saves-as", delegate
                {
                    string dir = WorkDir("pasted");
                    string target = Path.Combine(dir, "pegado.md");
                    string suggested = null;
                    MarkdownWindow.PickPath = delegate(string s) { suggested = s; return target; };
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Call(w, "LoadText", "# Mi t\u00EDtulo\r\n\r\nl\u00EDnea\r\n", null, null);
                        Pump(200);
                        Check(!MarkdownWindow.AnyUnsaved, "pasted text counts as unsaved");
                        Check(w.Save(false), "save failed");
                        Same("Mi t\u00EDtulo.md", suggested, "suggested name");
                        Same("# Mi t\u00EDtulo\r\n\r\nl\u00EDnea\r\n", Encoding.UTF8.GetString(File.ReadAllBytes(target)), "bytes (pasted line endings, UTF-8 without BOM)");
                        Check(File.ReadAllBytes(target)[0] == (byte)'#', "a BOM was written");
                        Check(w.Title.StartsWith("pegado.md"), "title " + w.Title);
                    }
                    finally { MarkdownWindow.PickPath = null; Shut(w); }
                });
                Test("window", "export-html", delegate
                {
                    string dir = WorkDir("html");
                    string target = Path.Combine(dir, "notas.html");
                    MarkdownWindow.PickPath = delegate(string s) { Check(s == "notas.html", "suggested " + s); return target; };
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, Put(dir, "notas.md", Md));
                        Check(w.ExportHtml(), "export failed");
                        string html = File.ReadAllText(target, Encoding.UTF8);
                        Check(html.StartsWith("<!DOCTYPE html>") && html.Contains("<title>Notas de versi\u00F3n</title>") && html.Contains("<table>"), "page: " + html.Substring(0, Math.Min(200, html.Length)));
                    }
                    finally { MarkdownWindow.PickPath = null; Shut(w); }
                });
                Test("window", "outside-changes", delegate
                {
                    string dir = WorkDir("outside");
                    string p = Put(dir, "a.md", "uno\n");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, p);
                        File.WriteAllText(p, "uno cambiado fuera\n");
                        Check(Until(delegate { return w.Text == "uno cambiado fuera\n"; }, 4000), "not reloaded: " + w.Text);
                        w.SetEditing(true);
                        Pump(150);
                        w.Editor.Apply(TextEdit.Make(0, 0, "mio ", 4, 0));
                        File.WriteAllText(p, "otra vez fuera\n");
                        Check(Until(delegate { return w.Banner == 1; }, 4000), "no banner for a change while dirty");
                        Check(w.Text.StartsWith("mio "), "my text was replaced");
                        // Saving over it asks first.
                        asked = 0; answer = 1;
                        Check(!w.Save(false) && asked == 1, "saved over the newer file without asking");
                        File.Delete(p);
                        Check(Until(delegate { return w.Banner == 2; }, 4000), "no banner for a deleted file");
                        Check(w.Save(false) && File.ReadAllText(p).StartsWith("mio "), "could not save the deleted file again");
                    }
                    finally { Shut(w); }
                });
                Test("window", "undo-survives-modes-and-theme", delegate
                {
                    // MX-15
                    string dir = WorkDir("undo");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, Put(dir, "a.md", "texto\n"));
                        w.SetEditing(true);
                        Pump(150);
                        MarkdownEditor ed = w.Editor;
                        ed.Apply(TextEdit.Make(0, 0, "nuevo ", 6, 0));
                        w.SetEditing(false);
                        Pump(100);
                        w.SetEditing(true);
                        Pump(100);
                        Check(w.Editor == ed, "a new editor after Vista/Editar");
                        Ds.Apply(2);
                        Pump(150);
                        Ds.Apply(1);
                        Pump(150);
                        Check(w.Editor == ed && ed.Box.CanUndo, "undo lost after the theme changed");
                        Call(w, "Tidy");
                        ed.Box.Undo();
                        ed.Box.Undo();
                        Same("texto\n", ed.Text, "undo back to the start");
                    }
                    finally { Shut(w); Ds.Apply(1); }
                });
                Test("window", "find-and-replace", delegate
                {
                    string dir = WorkDir("find");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, Put(dir, "a.md", "gato perro gato\nGato\n"));
                        Call(w, "OpenFind", false);
                        Pump(150);
                        FindBar bar = Field<FindBar>(w, "findBar");
                        bar.Query.Text = "gato";
                        bar.Flush();
                        Pump(50);
                        Check(Field<List<Hit>>(w, "hits").Count == 3, "view hits " + Field<List<Hit>>(w, "hits").Count);
                        Call(w, "OpenFind", true);
                        Pump(400);
                        bar = Field<FindBar>(w, "findBar");
                        Check(w.Editing && bar.Replacing, "Ctrl+H did not open replace in the editor");
                        bar.Query.Text = "gato";
                        bar.With.Text = "lince";
                        bar.Flush();
                        Call(w, "ReplaceAll");
                        Same("lince perro lince\nlince\n", w.Text, "replaced");
                        w.Editor.Box.Undo();
                        Same("gato perro gato\nGato\n", w.Editor.Text, "one undo step");
                    }
                    finally { Shut(w); }
                });
                Test("window", "ctrl-enter-in-replace-field", delegate
                {
                    // Ctrl+Enter in "Reemplazar por" is Reemplazar todo; the window used to take it as Listo.
                    string dir = WorkDir("ctrlenter");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, Put(dir, "a.md", "gato perro gato\n"));
                        Call(w, "OpenFind", true);
                        Pump(400);
                        FindBar bar = Field<FindBar>(w, "findBar");
                        bar.Query.Text = "gato";
                        bar.With.Text = "lince";
                        bar.Flush();
                        asked = 0; answer = 2;
                        Press(bar.With.Box, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control);
                        Pump(100);
                        Same("lince perro lince\n", w.Text, "Ctrl+Enter did not replace all");
                        Check(w.IsVisible && asked == 0, "Ctrl+Enter ran Listo");
                        Check(Field<string>(w, "status") != "No he podido crear la captura.", "Listo ran");
                        // Plain Enter there replaces one.
                        bar.Query.Text = "perro";
                        bar.With.Text = "oso";
                        bar.Flush();
                        Press(bar.With.Box, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None);
                        Pump(100);
                        Same("lince oso lince\n", w.Text, "Enter did not replace");
                    }
                    finally { Shut(w); }
                });
                Test("window", "listo-asks-before-the-capture", delegate
                {
                    // Cancelar in the alert must leave no capture behind (the test window has no stack: reaching it fails).
                    string dir = WorkDir("listo");
                    string p = Put(dir, "a.md", "texto\n");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        Open(w, p);
                        w.SetEditing(true);
                        Pump(150);
                        w.Editor.Apply(TextEdit.Make(0, 0, "nuevo ", 6, 0));
                        asked = 0; answer = 2;
                        Call(w, "Done");
                        Pump(50);
                        Check(asked == 1, "Listo did not ask (asked " + asked + ")");
                        Check(w.IsVisible, "closed after Cancelar");
                        Check(Field<string>(w, "status") != "No he podido crear la captura.", "the capture was made before asking");
                        Same("texto\n", File.ReadAllText(p), "file changed");
                    }
                    finally { Shut(w); }
                });
                Test("window", "alert-enter-uses-focused-button", delegate
                {
                    ConstructorInfo ci = typeof(DocAlert).GetConstructors(NP)[0];
                    string[] labels = { "Guardar", "No guardar", "Cancelar" };
                    int[] focus = { -1, 1, 2 };
                    int[] expect = { 0, 1, 2 };
                    for (int c = 0; c < focus.Length; c++)
                    {
                        DocAlert a = (DocAlert)ci.Invoke(new object[] { "\u00BFGuardar los cambios?", "", labels, 0, 2 });
                        try
                        {
                            Park(a);
                            Pump(100);
                            List<MacButton> bs = new List<MacButton>();
                            Collect(a, bs);
                            Check(bs.Count == 3, "buttons " + bs.Count);
                            if (focus[c] >= 0)
                            {
                                MacButton b = bs.First(x => x.Label == labels[focus[c]]);
                                System.Windows.Input.FocusManager.SetFocusedElement(a, b);
                            }
                            Press(a, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None);
                            Pump(30);
                            int got = Field<int>(a, "result");
                            Check(got == expect[c], "focus on " + (focus[c] < 0 ? "nothing" : labels[focus[c]]) + ": Enter answered " + labels[got]);
                            Check(!a.IsVisible, "not closed");
                        }
                        finally { if (a.IsVisible) a.Close(); }
                    }
                });
                Test("window", "xml-error-and-tree-sync", delegate
                {
                    string dir = WorkDir("xml");
                    MarkdownWindow w = NewWindow();
                    try
                    {
                        string bad = "<a>\n  <b>\n</a>\n";
                        Open(w, Put(dir, "roto.xml", bad));
                        Check(Until(delegate { return w.Editing && w.Editor != null && w.Editor.ErrorOffset >= 0; }, 5000), "a broken XML did not open in the editor with the error marked");
                        Note("error at " + w.Editor.ErrorOffset);
                        Shut(w);
                        w = NewWindow();
                        Open(w, Put(dir, "ok.xml", Xml));
                        Check(Until(delegate { return w.Tree != null && w.Tree.Model != null; }, 5000), "no tree");
                        XItem linea = null;
                        foreach (XItem t in w.Tree.Model.Top) if (t.Kind == XKind.Element) linea = t.Kids.First(k => k.Name == "lineas").Kids[1];
                        int line, col;
                        EditCommands.LineCol(Xml, Xml.IndexOf("<linea sku=\"B-2\"") + 3, out line, out col);
                        Check(XmlTreeView.At(w.Tree.Model, line, col) == linea, "position -> node");
                        w.Tree.Reveal(linea);
                        Pump(50);
                        Check(w.Tree.List.SelectedItem is XRow && ((XRow)w.Tree.List.SelectedItem).Node == linea, "Reveal did not select the row");
                    }
                    finally { Shut(w); }
                });
            }
            finally { DocAlert.Answer = null; }
        }

        // ---- perf

        static double Median(List<double> v)
        {
            List<double> s = new List<double>(v);
            s.Sort();
            return s[s.Count / 2];
        }

        static double P95(List<double> v)
        {
            List<double> s = new List<double>(v);
            s.Sort();
            return s[Math.Min(s.Count - 1, (int)Math.Ceiling(s.Count * 0.95) - 1)];
        }

        static void PerfSuite()
        {
            Ds.Apply(1);
            Test("perf", "colour-1mb", delegate
            {
                StringBuilder sb = new StringBuilder();
                int i = 0;
                while (sb.Length < 1024 * 1024) { sb.Append("## Secci\u00F3n " + i + "\n\nTexto con **negrita**, *cursiva*, `c\u00F3digo` y [enlace](https://x.es/" + i + ").\n- elemento\n> cita\n\n"); i++; }
                string t = sb.ToString();
                Syntax s = Syntax.For(false);
                Stopwatch sw = Stopwatch.StartNew();
                s.Reset(t);
                int n = s.All().Count;
                Budget("tokenize 1 MB of Markdown", sw.Elapsed.TotalMilliseconds, 2500);
                Check(n > 50000, "spans " + n);
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, t, false);
                sw.Restart();
                Window w = Host(ed, 1000, 720);
                try
                {
                    Idle();
                    Budget("open 1 MB in the editor", sw.Elapsed.TotalMilliseconds, 4000);
                    List<double> keys = new List<double>();
                    for (int k = 0; k < 30; k++)
                    {
                        sw.Restart();
                        ed.Apply(TextEdit.Make(200 + k, 0, "x", 201 + k, 0));
                        Idle();
                        keys.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Budget("keystroke p95 at 1 MB", P95(keys), 100);
                    Check(ed.PaintedLines < 60, "painted " + ed.PaintedLines + " lines: not proportional to the screen");
                }
                finally { w.Close(); }
            });
            Test("perf", "keystroke-small-doc", delegate
            {
                // MX-01: about 1 s per key at 10 KB before.
                StringBuilder sb = new StringBuilder();
                while (sb.Length < 40 * 1024) sb.Append("Texto con **negrita** y `c\u00F3digo` en una l\u00EDnea normal de un README.\n");
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, sb.ToString(), false);
                Window w = Host(ed, 900, 700);
                try
                {
                    List<double> keys = new List<double>();
                    Stopwatch sw = new Stopwatch();
                    // The first few edits after opening warm WPF's text formatting up (~40 ms each); then typing on
                    // different lines of the screen.
                    for (int k = 0; k < 5; k++) { ed.Apply(TextEdit.Make(20 + k, 0, "w", 21 + k, 0)); Idle(); }
                    foreach (int line in new int[] { 1, 8, 15, 23 })
                        for (int k = 0; k < 8; k++)
                        {
                            int at = EditCommands.Offset(ed.Text, line, 10 + k);
                            sw.Restart();
                            ed.Apply(TextEdit.Make(at, 0, "y", at + 1, 0));
                            Idle();
                            keys.Add(sw.Elapsed.TotalMilliseconds);
                        }
                    Budget("keystroke median at 40 KB", Median(keys), 30);
                    Budget("keystroke p95 at 40 KB", P95(keys), 120);
                }
                finally { w.Close(); }
            });
            Test("perf", "preview-5k-lines", delegate
            {
                // MX-02: 79 s for 500 KB before.
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 1250; i++) sb.Append("## T\u00EDtulo " + i + "\n\nP\u00E1rrafo con **negrita** y `c\u00F3digo`.\n- uno\n");
                string t = sb.ToString();
                MarkdownFlow flow = new MarkdownFlow(40);
                Window w = new Window { Width = 1000, Height = 720, WindowStyle = WindowStyle.None, Content = flow.Box };
                Park(w);
                Pump(100);
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    flow.Set(MarkdownDoc.Parse(t));
                    Idle();
                    Budget("first preview of 5,000 lines", sw.Elapsed.TotalMilliseconds, 5000);
                    Check(flow.FlatText().Contains("T\u00EDtulo 1249"), "content missing");
                    // While typing only the changed block is rebuilt.
                    List<double> edits = new List<double>();
                    for (int k = 0; k < 10; k++)
                    {
                        t = t.Replace("T\u00EDtulo " + (600 + k) + "\n", "T\u00EDtulo editado " + k + "\n");
                        sw.Restart();
                        flow.Set(MarkdownDoc.Parse(t));
                        Idle();
                        edits.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Budget("preview update after an edit (p95)", P95(edits), 400);
                    Check(flow.FlatText().Contains("T\u00EDtulo editado 9") && flow.FlatText().Contains("T\u00EDtulo 1249"), "update lost content");
                    Check(flow.Scroll("titulo-editado-9") || flow.Scroll("t\u00EDtulo-editado-9"), "anchor of an edited heading");
                }
                finally { w.Close(); }
            });
            Test("perf", "copy-50k-rows", delegate
            {
                // MX-07: minutes before.
                StringBuilder sb = new StringBuilder("<r>\n");
                for (int i = 0; i < 50000; i++) sb.Append("  <f n=\"" + i + "\"/>\n");
                sb.Append("</r>\n");
                XmlModel m; string err; int l, c;
                Check(XmlDoc.Parse(sb.ToString(), out m, out err, out l, out c), err);
                XmlTreeView tree = new XmlTreeView(Ds.Brushes);
                Window w = new Window { Width = 900, Height = 600, WindowStyle = WindowStyle.None, Content = tree };
                Park(w);
                try
                {
                    tree.Load(m);
                    tree.ExpandAll();
                    Pump(100);
                    tree.List.SelectAll();
                    Idle();
                    Stopwatch sw = Stopwatch.StartNew();
                    string text = tree.SelectionText();
                    Budget("copy of " + tree.Rows.Count.ToString("N0") + " rows", sw.Elapsed.TotalMilliseconds, 1500);
                    Check(text.Split('\n').Length == tree.Rows.Count, "lines " + text.Split('\n').Length + " vs rows " + tree.Rows.Count);
                    Check(text.IndexOf("n=\"10\"") < text.IndexOf("n=\"20000\""), "rows out of order");
                }
                finally { w.Close(); }
            });
            Test("perf", "editor-5mb-xml", delegate
            {
                // MX-12: must not freeze.
                StringBuilder sb = new StringBuilder("<?xml version=\"1.0\"?>\n<pedidos>\n");
                int i = 0;
                while (sb.Length < 5 * 1024 * 1024) { sb.Append("  <pedido id=\"" + i + "\" cliente=\"Cliente n\u00FAmero " + i + "\"><linea sku=\"A-" + (i * 7) + "\" cantidad=\"3\">Producto &amp; c\u00EDa</linea></pedido>\n"); i++; }
                sb.Append("</pedidos>\n");
                string t = sb.ToString();
                Stopwatch sw = Stopwatch.StartNew();
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, t, true);
                Window w = Host(ed, 1000, 720);
                try
                {
                    Idle();
                    Budget("open 5 MB of XML in the editor", sw.Elapsed.TotalMilliseconds, 5000);
                    List<double> keys = new List<double>();
                    for (int k = 0; k < 20; k++)
                    {
                        sw.Restart();
                        ed.Apply(TextEdit.Make(1000 + k, 0, "x", 1001 + k, 0));
                        Idle();
                        keys.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Budget("keystroke p95 at 5 MB", P95(keys), 150);
                    sw.Restart();
                    ed.GoTo(30000, 5);
                    Idle();
                    Budget("jump to line 30,000", sw.Elapsed.TotalMilliseconds, 1500);
                    Check(ed.PaintedLines > 0 && ed.PaintedLines < 60, "painted " + ed.PaintedLines);
                    XmlModel m; string err; int l, c;
                    sw.Restart();
                    Check(XmlDoc.Parse(ed.Text, out m, out err, out l, out c), err);
                    Add("parse " + Ms(sw.Elapsed.TotalMilliseconds) + " (background)");
                }
                finally { w.Close(); }
            });
            Test("perf", "editor-5mb-one-line-xml", delegate
            {
                // Minified XML: one 5 MB line.
                StringBuilder sb = new StringBuilder("<r>");
                int i = 0;
                while (sb.Length < 5 * 1024 * 1024) { sb.Append("<p id=\"" + i + "\">valor " + i + "</p>"); i++; }
                sb.Append("</r>");
                Stopwatch sw = Stopwatch.StartNew();
                MarkdownEditor ed = new MarkdownEditor(Ds.Brushes, sb.ToString(), true);
                Window w = Host(ed, 1000, 720);
                try
                {
                    Idle();
                    Budget("open a 5 MB line in the editor", sw.Elapsed.TotalMilliseconds, 5000);
                    Check(ed.Wraps, "not wrapped");
                    List<double> keys = new List<double>();
                    for (int k = 0; k < 15; k++)
                    {
                        sw.Restart();
                        ed.Apply(TextEdit.Make(100 + k, 0, "x", 101 + k, 0));
                        Idle();
                        keys.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Budget("keystroke p95 on a 5 MB line", P95(keys), 150);
                    Check(ed.PaintedLines > 10 && ed.PaintedLines < 60, "painted " + ed.PaintedLines);
                }
                finally { w.Close(); }
            });
            Test("perf", "tree-filter-250k-nodes", delegate
            {
                StringBuilder sb = new StringBuilder("<r>");
                for (int i = 0; i < 120000; i++) sb.Append("<p n=\"" + i + "\">valor " + i + "</p>");
                sb.Append("</r>");
                XmlModel m; string err; int l, c;
                Check(XmlDoc.Parse(sb.ToString(), out m, out err, out l, out c), err);
                XmlTreeView tree = new XmlTreeView(Ds.Brushes);
                Window w = new Window { Width = 900, Height = 600, WindowStyle = WindowStyle.None, Content = tree };
                Park(w);
                try
                {
                    tree.Load(m);
                    Idle();
                    Stopwatch sw = Stopwatch.StartNew();
                    int n = tree.Filter("valor 11999");
                    Idle();
                    Budget("filter " + m.Count.ToString("N0") + " nodes", sw.Elapsed.TotalMilliseconds, 1500);
                    Check(n == 11, "matches " + n);
                }
                finally { w.Close(); }
            });
        }
    }
}

// UX tests (tester h3-ux): the Texto hotkey in settings.ini (missing, empty, invalid, the same combo as another action),
// what the keys page and the tray say about it, and the home styles, the keys page and the tray panel rendered off-screen
// to PNG for a look. Nothing is shown, no screen is read, the clipboard is never touched, files go only to STACKSHOT_TEST_DATA.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot.Tests
{
    static class AppTestsH3Ux
    {
        const string DefaultText = "Ctrl+Shift+Alt+T";
        // A combo that nothing uses on a normal PC; it is registered for a few milliseconds and released.
        const string FreeCombo = "Ctrl+Shift+Alt+F23";

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static string DataDir(string name)
        {
            string root = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string d = Path.Combine(root, "h3ux", name);
            Directory.CreateDirectory(d);
            return d;
        }

        static string ShotsDir()
        {
            string d = Environment.GetEnvironmentVariable("STACKSHOT_UI_SHOTS");
            if (string.IsNullOrEmpty(d)) d = DataDir("shots");
            Directory.CreateDirectory(d);
            return d;
        }

        static string WriteSettings(string name, string text)
        {
            string path = Path.Combine(DataDir(name), "settings.ini");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        // ---- Helpers for the pages (as in AppTestsHuntUi: the window is built and laid out, never shown)

        static HomeWindow Make(int appearance, Settings s)
        {
            Ds.Apply(appearance);
            s.Appearance = appearance;
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            return new HomeWindow(owner, s);
        }

        static void SetPage(HomeWindow hw, string id)
        {
            MethodInfo m = typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(hw, new object[] { id, false });
        }

        static void Collect(object o, List<string> acc)
        {
            TextBlock tb = o as TextBlock;
            if (tb != null && tb.Text != null) acc.Add(tb.Text);
            DependencyObject d = o as DependencyObject;
            if (d == null) return;
            foreach (object c in LogicalTreeHelper.GetChildren(d)) Collect(c, acc);
        }

        static List<string> TextsOf(HomeWindow hw)
        {
            List<string> acc = new List<string>();
            Collect(hw.Content, acc);
            return acc;
        }

        static bool Has(List<string> texts, string part)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // The window's own background is painted under the content (a window is not part of its Content): without it the
        // translucent parts of the sidebar show the empty picture.
        static void SavePng(UIElement root, Brush background, int w, int h, string file)
        {
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(Path.Combine(ShotsDir(), file))) enc.Save(fs);
        }

        // ---- Settings file

        // An older build's file: there is no Text line at all, so the default applies and the rest reads as before.
        static void OldFileWithoutTextKeepsDefault()
        {
            string path = WriteSettings("old", "# Ajustes de Stackshot (versi\u00F3n anterior)\r\nSaveFolder = C:\\Capturas\r\nSound = 1\r\n" +
                "Region = PrintScreen\r\nScreen = Ctrl+PrintScreen\r\nWindow = Alt+PrintScreen\r\nScroll = Ctrl+Alt+PrintScreen\r\n" +
                "Video = Shift+PrintScreen\r\nGif = Ctrl+Shift+PrintScreen\r\nMarkdown = Ctrl+Shift+Alt+M\r\nVideoFps = 30\r\n");
            Settings s = Settings.Load(path);
            Check(s.HotText == DefaultText, "Text missing: <" + s.HotText + "> instead of " + DefaultText);
            Check(s.HotMarkdown == "Ctrl+Shift+Alt+M", "Markdown read as <" + s.HotMarkdown + ">");
            Check(s.SaveFolder == "C:\\Capturas", "SaveFolder read as <" + s.SaveFolder + ">");
        }

        // The Text line written by Settings.Write comes back the same.
        static void TextLineSurvivesWrite()
        {
            Settings s = new Settings();
            s.HotText = "Ctrl+Alt+F22";
            string path = Path.Combine(DataDir("roundtrip"), "settings.ini");
            MethodInfo write = typeof(Settings).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance);
            write.Invoke(s, new object[] { path });
            string text = File.ReadAllText(path);
            Check(text.IndexOf("Text = Ctrl+Alt+F22", StringComparison.Ordinal) >= 0, "no Text line written");
            Check(Settings.Load(path).HotText == "Ctrl+Alt+F22", "Text not read back");
        }

        // A hand-edited empty value: the mode has no combo; the tray and the keys page must cope.
        static void EmptyTextLeavesRowWithoutKeycaps()
        {
            Settings s = Settings.Load(WriteSettings("empty", "Text = \r\nRegion = PrintScreen\r\n"));
            Check(s.HotText.Length == 0, "Text empty read as <" + s.HotText + ">");
            Check(Hotkeys.Display(s.HotText) == "Sin atajo", "display of an empty combo: <" + Hotkeys.Display(s.HotText) + ">");
            object row = TrayRow(new TrayPanel(s), "text");
            Check(row != null, "tray has no text row");
            Check(CapsOf(row) == null, "tray text row shows keycaps for an empty combo");
        }

        // The default combo is accepted by the rules of the keys page (and the tray shows it as keycaps ending in T).
        static void DefaultComboIsAllowedAndShown()
        {
            System.Windows.Forms.Keys mods = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt;
            string why = Hotkeys.Problem(System.Windows.Forms.Keys.T, mods, false);
            Check(why == null, "Ctrl+Shift+Alt+T refused: " + why);
            Check(Hotkeys.Problem(System.Windows.Forms.Keys.M, mods, false) == null, "Ctrl+Shift+Alt+M refused");
            Console.WriteLine("    AltGr character of Ctrl+Alt+Shift+T on this layout: <" + Hotkeys.AltGrChar(System.Windows.Forms.Keys.T, true) + ">");
            string[] caps = CapsOf(TrayRow(new TrayPanel(new Settings()), "text"));
            Check(caps != null && caps.Length > 0 && caps[caps.Length - 1] == "T", "tray keycaps of the default: " + (caps == null ? "none" : string.Join(" | ", caps)));
        }

        // A value that is not a combo at all: Windows refuses it, the keys page shows it as typed, nothing throws.
        static void InvalidTextIsRefusedWithoutThrowing()
        {
            Settings s = Settings.Load(WriteSettings("invalid", "Text = Hola mundo\r\n"));
            using (Hotkeys hk = new Hotkeys())
            {
                List<string> failed = hk.Register("text", s.HotText);
                Check(failed.Count == 1, "invalid combo not reported as failed");
                Check(!Hotkeys.IsBusy("text", s.HotText), "invalid combo is marked as used by another program");
            }
        }

        // Two actions on one combo (a hand-edited file): Windows refuses the second one.
        static void SecondActionOnOneComboIsRefused()
        {
            using (Hotkeys hk = new Hotkeys())
            {
                List<string> first = hk.Register("markdown", FreeCombo);
                Check(first.Count == 0, "the free test combo could not be registered: " + string.Join(",", first.ToArray()));
                List<string> second = hk.Register("text", FreeCombo);
                Check(second.Count == 1, "same combo on a second action was not refused (failed=" + second.Count + ")");
                Check(Hotkeys.IsBusy("text", FreeCombo), "the text action is not marked as busy");
            }
        }

        // The same clash, seen on the keys page: the Texto row must not blame another program, since the other
        // holder is Stackshot's own Markdown row. Expected to FAIL while the wording is the generic one.
        static void KeysPageDoesNotBlameAnotherProgramForOwnClash()
        {
            using (Hotkeys hk = new Hotkeys())
            {
                hk.Register("markdown", FreeCombo);
                hk.Register("text", FreeCombo);
                Settings s = new Settings();
                s.HotMarkdown = FreeCombo;
                s.HotText = FreeCombo;
                HomeWindow hw = Make(1, s);
                SetPage(hw, "keys");
                List<string> texts = TextsOf(hw);
                SavePng((UIElement)hw.Content, hw.Background, 980, 2400, "keys-clash-light.png");
                Check(!Has(texts, "lo est\u00E1 usando otro programa"),
                      "the Texto row says \"lo est\u00E1 usando otro programa\" for a combo that Markdown of Stackshot holds");
            }
        }

        // ---- Mascot tips and the tray

        static void MascotTipsMentionTexto()
        {
            Settings s = new Settings();
            int found = 0;
            for (int i = 0; i < 400; i++)
                if (MascotTalk.Tip(s).IndexOf("texto", StringComparison.OrdinalIgnoreCase) >= 0) found++;
            Check(found > 0, "no tip of the mascot mentions the Texto mode (0 of 400 draws)");
        }

        static void BuildTray(TrayPanel tp, bool recording, bool scrolling)
        {
            MethodInfo build = typeof(TrayPanel).GetMethod("BuildItems", BindingFlags.NonPublic | BindingFlags.Instance);
            build.Invoke(tp, new object[] { recording, scrolling, false });
        }

        static object TrayRow(TrayPanel tp, string id)
        {
            BuildTray(tp, false, false);
            FieldInfo f = typeof(TrayPanel).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (object it in (System.Collections.IEnumerable)f.GetValue(tp))
                if (string.Equals((string)it.GetType().GetField("Id").GetValue(it), id, StringComparison.Ordinal)) return it;
            return null;
        }

        static string[] CapsOf(object row)
        {
            return row == null ? null : (string[])row.GetType().GetField("Caps").GetValue(row);
        }

        static void TrayPaints(int appearance, string file)
        {
            Settings s = new Settings();
            s.Appearance = appearance;
            Ds.Apply(appearance);
            TrayPanel tp = new TrayPanel(s);
            MethodInfo rebuild = typeof(TrayPanel).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuild.Invoke(tp, new object[] { 1f, 0 });
            // FloatWindow is a WinForms window: its body is a System.Drawing.Size.
            System.Drawing.Size body = (System.Drawing.Size)typeof(FloatWindow).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tp);
            int pad = (int)typeof(FloatWindow).GetField("Pad", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tp);
            int w = body.Width + 2 * pad, h = body.Height + 2 * pad;
            MethodInfo paint = typeof(TrayPanel).GetMethod("PaintSurface", BindingFlags.NonPublic | BindingFlags.Instance);
            DrawingVisual v = new DrawingVisual();
            using (DrawingContext dc = v.RenderOpen()) paint.Invoke(tp, new object[] { dc, w, h });
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(Path.Combine(ShotsDir(), file))) enc.Save(fs);
            Console.WriteLine("    tray picture " + w + "x" + h + " -> " + file);
        }

        // ---- Home styles and pages

        static void HomeStyleShowsTexto(int style, int appearance, int w, int h, string file)
        {
            Settings s = new Settings();
            s.HomeStyle = style;
            HomeWindow hw = Make(appearance, s);
            SetPage(hw, "home");
            List<string> texts = TextsOf(hw);
            SavePng((UIElement)hw.Content, hw.Background, w, h, file);
            Check(Has(texts, "Texto") || Has(texts, "Copiar texto") || ModeBarHasTexto(hw.Content as DependencyObject), "home style " + style + ": no label Texto");
        }

        // The Minimalista style draws its mode bar as ink, not as text blocks: its slots are read from the bar.
        static bool ModeBarHasTexto(DependencyObject root)
        {
            if (root == null) return false;
            if (root.GetType().Name == "HomeModeBar")
            {
                string[] icons = (string[])root.GetType().GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(root);
                Console.WriteLine("    mode bar: " + icons.Length + " slots, slot 4 icon " + icons[4] + ", bar width " + ((FrameworkElement)root).Width);
                return icons.Length == 8 && icons[4] == "textscan";
            }
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                if (ModeBarHasTexto(VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }

        static void KeysPageShowsTextoRow(int appearance, string file)
        {
            Settings s = new Settings();
            HomeWindow hw = Make(appearance, s);
            SetPage(hw, "keys");
            List<string> texts = TextsOf(hw);
            SavePng((UIElement)hw.Content, hw.Background, 980, 2400, file);
            Check(Has(texts, "Copiar texto de la pantalla"), "keys page: no row Copiar texto de la pantalla");
        }

        // The editor's bar: the text annotation tool and the copy-text button must not both be called "Texto".
        static void EditorNamesTextTwice()
        {
            System.Drawing.Bitmap img = new System.Drawing.Bitmap(640, 360);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(img)) g.Clear(System.Drawing.Color.White);
            Editor ed = new Editor(null, Path.Combine(DataDir("editor"), "prueba.png"), img);
            try
            {
                Bar bar = (Bar)typeof(Editor).GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ed);
                Dictionary<Tool, Bar.Item> tools = (Dictionary<Tool, Bar.Item>)typeof(Editor).GetField("toolItems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ed);
                Bar.Item textTool = tools[Tool.Text];
                Bar.Item copyText = null;
                foreach (Bar.Item it in bar.Items) if (it.Label == "Texto") copyText = it;
                Console.WriteLine("    text tool tip: " + textTool.Tip + " | copy button: " + (copyText == null ? "none" : copyText.Label + " (" + copyText.Tip + ")"));
                bar.Size = new System.Drawing.Size(1600, 56);
                bar.LayoutItems();
                System.Drawing.Bitmap pic = new System.Drawing.Bitmap(1600, 56);
                bar.DrawToBitmap(pic, new System.Drawing.Rectangle(0, 0, 1600, 56));
                pic.Save(Path.Combine(ShotsDir(), "editor-bar.png"), System.Drawing.Imaging.ImageFormat.Png);
                pic.Dispose();
                Check(copyText == null || !textTool.Tip.StartsWith("Texto", StringComparison.Ordinal),
                      "the copy-text button is labelled \"Texto\", the same name as the text annotation tool (" + textTool.Tip + ")");
            }
            finally { ed.Dispose(); }
        }

        public static void Suite()
        {
            AppTests.Test("h3ux", "settings file without Text keeps the default combo", OldFileWithoutTextKeepsDefault);
            AppTests.Test("h3ux", "Text line written by Settings.Write is read back", TextLineSurvivesWrite);
            AppTests.Test("h3ux", "empty Text: tray row without keycaps, no throw", EmptyTextLeavesRowWithoutKeycaps);
            AppTests.Test("h3ux", "default Ctrl+Shift+Alt+T passes Hotkeys.Problem and shows keycaps", DefaultComboIsAllowedAndShown);
            AppTests.Test("h3ux", "invalid Text value is refused by Register and not marked busy", InvalidTextIsRefusedWithoutThrowing);
            AppTests.Test("h3ux", "two actions on one combo: the second is refused", SecondActionOnOneComboIsRefused);
            AppTests.Test("h3ux", "keys page does not blame another program for a clash inside Stackshot", KeysPageDoesNotBlameAnotherProgramForOwnClash);
            AppTests.Test("h3ux", "mascot tips mention Texto (discoverability)", MascotTipsMentionTexto);
            AppTests.Test("h3ux", "editor bar: the text tool and the copy-text button are not both called Texto", EditorNamesTextTwice);
            AppTests.Test("h3ux", "home style 0 (Minimalista) shows Texto, light, 980x660", delegate { HomeStyleShowsTexto(0, 1, 980, 660, "home0-light.png"); });
            AppTests.Test("h3ux", "home style 0 (Minimalista) shows Texto, dark, 980x660", delegate { HomeStyleShowsTexto(0, 2, 980, 660, "home0-dark.png"); });
            AppTests.Test("h3ux", "home style 1 (Widgets) shows Texto, light, 980x660", delegate { HomeStyleShowsTexto(1, 1, 980, 660, "home1-light.png"); });
            AppTests.Test("h3ux", "home style 1 (Widgets) shows Texto, dark, 980x660", delegate { HomeStyleShowsTexto(1, 2, 980, 660, "home1-dark.png"); });
            AppTests.Test("h3ux", "home style 2 (Escena) shows Texto, light, 980x660", delegate { HomeStyleShowsTexto(2, 1, 980, 660, "home2-light.png"); });
            AppTests.Test("h3ux", "home style 2 (Escena) shows Texto, dark, 980x660", delegate { HomeStyleShowsTexto(2, 2, 980, 660, "home2-dark.png"); });
            AppTests.Test("h3ux", "home style 0 at 760x520 (smaller window), light", delegate { HomeStyleShowsTexto(0, 1, 760, 520, "home0-small-light.png"); });
            AppTests.Test("h3ux", "home style 1 at 760x520 (smaller window), dark", delegate { HomeStyleShowsTexto(1, 2, 760, 520, "home1-small-dark.png"); });
            AppTests.Test("h3ux", "keys page has the Texto row, light", delegate { KeysPageShowsTextoRow(1, "keys-light.png"); });
            AppTests.Test("h3ux", "keys page has the Texto row, dark", delegate { KeysPageShowsTextoRow(2, "keys-dark.png"); });
            AppTests.Test("h3ux", "tray panel paints the Texto row, light", delegate { TrayPaints(1, "tray-light.png"); });
            AppTests.Test("h3ux", "tray panel paints the Texto row, dark", delegate { TrayPaints(2, "tray-dark.png"); });
        }
    }
}

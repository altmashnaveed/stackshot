// Hunt tests (Haiku tester): the Home pages build headless in light and dark, the Recording page shows the widgets of the
// new options, and a layout pass runs. The window is never shown, no timers start, nothing is written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;

namespace Stackshot.Tests
{
    static class AppTestsHuntUi
    {
        static readonly string[] Pages = { "home", "keys", "general", "editor", "record", "mascot", "about" };

        // The owner is not created (it starts the tray and the hotkeys): only what the pages read is faked as empty.
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
            try { m.Invoke(hw, new object[] { id, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("page " + id + ": " + ex.InnerException, ex.InnerException); }
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

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static void Layout(HomeWindow hw)
        {
            UIElement root = hw.Content as UIElement;
            Check(root != null, "window has a root");
            root.Measure(new Size(980, 660));
            root.Arrange(new Rect(0, 0, 980, 660));
        }

        static void Page(string id, int appearance, Settings s)
        {
            HomeWindow hw = Make(appearance, s);
            SetPage(hw, id);
            Layout(hw);
        }

        static void HasText(List<string> texts, string part, string what)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return;
            throw new InvalidOperationException(what + ": no text containing <" + part + ">");
        }

        // General > Archivos offers .mkv beside .md and .xml (the switch only reads the registry here). With
        // STACKSHOT_UI_SHOTS set to a folder, the general and record pages are saved there as PNG to be looked at.
        static void GeneralOffersMkv(int appearance)
        {
            HomeWindow hw = Make(appearance, new Settings());
            SetPage(hw, "general");
            Layout(hw);
            List<string> texts = TextsOf(hw);
            HasText(texts, "Abrir .md con Stackshot", "md switch");
            HasText(texts, "Abrir .mkv con Stackshot", "mkv switch");
            HasText(texts, ".mkv", "mkv note");
            string shots = Environment.GetEnvironmentVariable("STACKSHOT_UI_SHOTS");
            if (string.IsNullOrEmpty(shots)) return;
            Directory.CreateDirectory(shots);
            // Home in its three styles and the shortcuts page too (they list every capture mode), at the window's usual size.
            string[] ids = { "general", "record", "keys", "home:0", "home:1", "home:2" };
            foreach (string spec in ids)
            {
                string id = spec.Split(':')[0];
                Settings st = new Settings();
                if (spec.Contains(":")) st.HomeStyle = int.Parse(spec.Split(':')[1]);
                HomeWindow w = Make(appearance, st);
                SetPage(w, id);
                int h = id == "home" ? 720 : 3200;
                UIElement root = (UIElement)w.Content;
                root.Measure(new Size(980, h));
                root.Arrange(new Rect(0, 0, 980, h));
                root.UpdateLayout();
                RenderTargetBitmap rtb = new RenderTargetBitmap(980, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = File.Create(Path.Combine(shots, spec.Replace(':', '-') + "-" + appearance + ".png"))) enc.Save(fs);
            }
        }

        static void RecordWidgets(int appearance)
        {
            Settings s = new Settings();
            s.RecordSetup = true;
            s.RecordCountdown = 3;
            s.PlayerOpensVideos = false;
            s.CopyToClipboard = true;
            HomeWindow hw = Make(appearance, s);
            SetPage(hw, "record");
            Layout(hw);
            List<string> texts = TextsOf(hw);
            HasText(texts, "Antes de grabar", "section title");
            HasText(texts, "Mostrar las opciones antes de grabar", "options switch");
            HasText(texts, "Cuenta atr\u00E1s", "countdown row");
            HasText(texts, "Despu\u00E9s de grabar", "after section");
            HasText(texts, "Copiar el archivo al terminar", "copy switch");
            HasText(texts, "Abrir los v\u00EDdeos en Stackshot", "open videos switch");
            HasText(texts, "Elegir el fondo de \u00ABPresentar\u00BB", "backdrop link");
            HasText(texts, "Abrir un v\u00EDdeo o un m\u00E1ster Cine", "open a video link");
            HasText(texts, "Motor de v\u00EDdeo", "engine section");
        }

        // Switches off, widgets still built, both themes.
        static void RecordWidgetsOff(int appearance)
        {
            Settings s = new Settings();
            s.RecordSetup = false;
            s.RecordCountdown = 0;
            s.PlayerOpensVideos = true;
            s.CopyToClipboard = false;
            HomeWindow hw = Make(appearance, s);
            SetPage(hw, "record");
            Layout(hw);
            HasText(TextsOf(hw), "Antes de grabar", "section title with switches off");
        }

        static TrayPanel NewTray()
        {
            Settings s = new Settings();
            return new TrayPanel(s);
        }

        static List<object> TrayItems(TrayPanel tp, bool recording, bool scrolling)
        {
            MethodInfo build = typeof(TrayPanel).GetMethod("BuildItems", BindingFlags.NonPublic | BindingFlags.Instance);
            build.Invoke(tp, new object[] { recording, scrolling, false });
            FieldInfo f = typeof(TrayPanel).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
            List<object> r = new List<object>();
            foreach (object o in (System.Collections.IEnumerable)f.GetValue(tp)) r.Add(o);
            return r;
        }

        static string Field(object item, string name)
        {
            object v = item.GetType().GetField(name).GetValue(item);
            return v == null ? null : v.ToString();
        }

        static bool HasRow(List<object> items, string id, string text)
        {
            foreach (object it in items)
                if (Field(it, "Id") == id && Field(it, "Text") == text) return true;
            return false;
        }

        // The tray menu rows, with the wording in Spanish. Printed so the texts can be read.
        public static void TrayRows()
        {
            TrayPanel tp = NewTray();
            List<object> items = TrayItems(tp, false, false);
            foreach (object it in items)
            {
                string text = Field(it, "Text");
                if (text != null) Console.WriteLine("    tray row: [" + Field(it, "Id") + "] " + text + (Field(it, "Enabled") == "False" ? " (disabled)" : ""));
            }
            if (!HasRow(items, "openvideo", "Abrir v\u00EDdeo\u2026")) throw new InvalidOperationException("row Abrir v\u00EDdeo\u2026 missing");
            if (!HasRow(items, "folder", "Abrir capturas guardadas")) throw new InvalidOperationException("row folder missing");
        }

        public static void TrayRowsRecording()
        {
            TrayPanel tp = NewTray();
            List<object> items = TrayItems(tp, true, false);
            if (!HasRow(items, "video", "Detener grabaci\u00F3n")) throw new InvalidOperationException("recording: stop row missing");
            foreach (object it in items)
                if (Field(it, "Id") == "gif") throw new InvalidOperationException("recording: GIF row still shown");
            List<object> scroll = TrayItems(tp, false, true);
            if (!HasRow(scroll, "scroll", "Terminar desplazamiento")) throw new InvalidOperationException("scrolling: finish row missing");
        }

        public static void TrayWhen()
        {
            MethodInfo when = typeof(TrayPanel).GetMethod("When", BindingFlags.NonPublic | BindingFlags.Static);
            DateTime today = DateTime.Today.AddHours(14).AddMinutes(5);
            string a = (string)when.Invoke(null, new object[] { today });
            if (a != "Hoy, 14:05") throw new InvalidOperationException("today: <" + a + ">");
            string c = (string)when.Invoke(null, new object[] { today.AddDays(-1) });
            if (c != "Ayer, 14:05") throw new InvalidOperationException("yesterday: <" + c + ">");
            string d = (string)when.Invoke(null, new object[] { today.AddDays(-9) });
            if (d.EndsWith(".") || d.IndexOf(", ", StringComparison.Ordinal) < 0 || d.EndsWith(", 14:05") == false)
                throw new InvalidOperationException("older day: <" + d + ">");
            Console.WriteLine("    when: " + a + " | " + c + " | " + d);
        }

        // Recent entries of every kind (and a locked one) on the home page, in each home style and theme.
        static void HomeWithRecents(int style, int appearance, bool missingFolder)
        {
            string root = Path.Combine(Path.GetTempPath(), "stackshot-app-hunt", Guid.NewGuid().ToString("N"));
            string save = Path.Combine(root, "Capturas");
            string temp = Path.Combine(root, "temp");
            Directory.CreateDirectory(save);
            Directory.CreateDirectory(temp);
            DateTime now = DateTime.Now;
            File.WriteAllBytes(Path.Combine(save, "captura.png"), new byte[64]);
            File.WriteAllBytes(Path.Combine(save, "clip.mkv"), new byte[64]);
            File.WriteAllBytes(Path.Combine(temp, "suelto.ts"), new byte[64]);
            File.WriteAllBytes(Path.Combine(temp, "pelicula.wmv"), new byte[64]);
            File.WriteAllBytes(Path.Combine(temp, "bloqueado.mp4"), new byte[64]);
            File.WriteAllBytes(Path.Combine(temp, "gif.gif"), new byte[64]);
            File.SetLastWriteTime(Path.Combine(temp, "bloqueado.mp4"), now);
            Recents.TempFolder = temp;
            Recents.Forget();
            Settings s = new Settings();
            s.SaveFolder = missingFolder ? Path.Combine(root, "no-existe") : save;
            s.ShowRecent = true;
            s.HomeStyle = style;
            s.Appearance = appearance;
            using (FileStream hold = new FileStream(Path.Combine(temp, "bloqueado.mp4"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Page("home", appearance, s);
            }
        }

        public static void HomeRecentsStyles()
        {
            for (int style = 0; style <= 2; style++)
                for (int look = 1; look <= 2; look++)
                    HomeWithRecents(style, look, false);
        }

        public static void HomeRecentsMissingFolder()
        {
            HomeWithRecents(1, 2, true);
            HomeWithRecents(2, 1, true);
        }

        // Runs the dispatcher until done() or ms have passed (Recents answers on the UI thread).
        static void Pump(Func<bool> done, int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (!done() && DateTime.Now < end)
            {
                DispatcherFrame f = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { f.Continue = false; });
                Dispatcher.PushFrame(f);
                Thread.Sleep(15);
            }
        }

        static string ThumbDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "stackshot-app-hunt", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            Recents.TempFolder = d;
            return d;
        }

        static void SavePng(string path)
        {
            using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(640, 360)) b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        public static void ThumbReal()
        {
            string dir = ThumbDir();
            string png = Path.Combine(dir, "ok.png");
            SavePng(png);
            BitmapSource got = null;
            DateTime t0 = DateTime.Now;
            System.Drawing.Size orig;
            using (System.Drawing.Bitmap probe = ShotStack.LoadPreview(png, 200, out orig)) { }
            Console.WriteLine("    LoadPreview direct: " + (DateTime.Now - t0).TotalMilliseconds + " ms");
            t0 = DateTime.Now;
            Recents.Thumb(png, 200, delegate(BitmapSource src) { got = src; });
            Pump(delegate { return got != null; }, 8000);
            Console.WriteLine("    thumb first: " + (DateTime.Now - t0).TotalMilliseconds + " ms");
            string png2 = Path.Combine(dir, "ok2.png");
            SavePng(png2);
            BitmapSource got2 = null;
            t0 = DateTime.Now;
            Recents.Thumb(png2, 200, delegate(BitmapSource src) { got2 = src; });
            Pump(delegate { return got2 != null; }, 8000);
            Console.WriteLine("    thumb second: " + (DateTime.Now - t0).TotalMilliseconds + " ms");
            if (got == null) throw new InvalidOperationException("thumbnail of a good PNG never arrived");
            if (got.PixelWidth <= 0 || Math.Max(got.PixelWidth, got.PixelHeight) > 200)
                throw new InvalidOperationException("thumbnail size " + got.PixelWidth + "x" + got.PixelHeight + " is not within 200 px");
        }

        public static void ThumbBrokenFile()
        {
            string dir = ThumbDir();
            string bad = Path.Combine(dir, "roto.png");
            File.WriteAllText(bad, "no es una imagen");
            bool called = false;
            Recents.Thumb(bad, 200, delegate { called = true; });
            Pump(delegate { return false; }, 1500);
            if (called) throw new InvalidOperationException("a broken PNG gave a thumbnail");
        }

        // A file locked by another program when first asked (a scanner, a copy in progress), then released: the thumbnail
        // should appear once the file can be read.
        public static void ThumbLockedThenReleased()
        {
            string dir = ThumbDir();
            string png = Path.Combine(dir, "bloqueado.png");
            SavePng(png);
            BitmapSource got = null;
            FieldInfo fw = typeof(Recents).GetField("waiting", BindingFlags.NonPublic | BindingFlags.Static);
            System.Collections.IDictionary pending = (System.Collections.IDictionary)fw.GetValue(null);
            FieldInfo fu = typeof(Recents).GetField("unreadable", BindingFlags.NonPublic | BindingFlags.Static);
            HashSet<string> failed = (HashSet<string>)fu.GetValue(null);
            using (FileStream hold = new FileStream(png, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Recents.Thumb(png, 200, delegate(BitmapSource src) { got = src; });
                // The attempt is over when nobody waits for it any more.
                Pump(delegate { return pending.Count == 0; }, 60000);
                if (pending.Count != 0) throw new InvalidOperationException("the attempt while locked did not finish");
                if (got != null) throw new InvalidOperationException("a locked file gave a thumbnail");
                if (failed.Contains(png + "|" + File.GetLastWriteTimeUtc(png).Ticks + "|200")) throw new InvalidOperationException("a file that was only locked was cached as unreadable");
            }
            string copy = Path.Combine(dir, "copia.png");
            File.Copy(png, copy);
            BitmapSource control = null;
            Recents.Thumb(copy, 200, delegate(BitmapSource src) { control = src; });
            Pump(delegate { return control != null; }, 8000);
            if (control == null) throw new InvalidOperationException("control: a copy of the picture gives no thumbnail either");
            Recents.Thumb(png, 200, delegate(BitmapSource src) { got = src; });
            Pump(delegate { return got != null; }, 8000);
            if (got == null) throw new InvalidOperationException("after the lock ended the thumbnail is still missing for this session (failed result is cached by path and write time)");
        }

        public static void Suite()
        {
            foreach (string id in Pages)
            {
                string page = id;
                AppTests.Test("hunt-ui", "page " + page + " builds and lays out, light", delegate { Page(page, 1, new Settings()); });
                AppTests.Test("hunt-ui", "page " + page + " builds and lays out, dark", delegate { Page(page, 2, new Settings()); });
            }
            AppTests.Test("hunt-ui", "record page widgets, light", delegate { RecordWidgets(1); });
            AppTests.Test("hunt-ui", "record page widgets, dark", delegate { RecordWidgets(2); });
            AppTests.Test("hunt-ui", "record page with options off, dark", delegate { RecordWidgetsOff(2); });
            AppTests.Test("hunt-ui", "record page with options off, light", delegate { RecordWidgetsOff(1); });
            AppTests.Test("hunt-ui", "general page offers .mkv, light", delegate { GeneralOffersMkv(1); });
            AppTests.Test("hunt-ui", "general page offers .mkv, dark", delegate { GeneralOffersMkv(2); });
            AppTests.Test("hunt-ui", "tray rows", TrayRows);
            AppTests.Test("hunt-ui", "tray rows while recording or scrolling", TrayRowsRecording);
            AppTests.Test("hunt-ui", "tray date labels", TrayWhen);
            AppTests.Test("hunt-ui", "home with recents of every kind, all styles", HomeRecentsStyles);
            AppTests.Test("hunt-ui", "home with the save folder missing", HomeRecentsMissingFolder);
            AppTests.Test("hunt-thumbs", "thumbnail of a good PNG", ThumbReal);
            AppTests.Test("hunt-thumbs", "thumbnail of a broken PNG gives nothing and does not throw", ThumbBrokenFile);
            AppTests.Test("hunt-thumbs", "thumbnail of a file locked then released", ThumbLockedThenReleased);
        }
    }
}

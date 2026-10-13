// Stackshot - Plates for the trailer: the real main window (its three home views), the Markdown viewer and the XML
// viewer, rendered offscreen at 2x over synthetic content so no real capture ever shows. Built by tools\make-film.ps1.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using W = System.Windows;
using M = System.Windows.Media;

namespace Stackshot
{
    public static partial class Plates
    {
        static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Make(string dir, Bitmap desktop)
        {
            Settings.ReadOnly = true;
            ShotStack.Test = true;
            Backdrop.NoWallpaper = true;
            Ds.Apply(2);
            // Synthetic captures for the recents strip and the widgets.
            string shots = Path.Combine(Path.GetTempPath(), "stackshot-film-shots"), temp = Path.Combine(Path.GetTempPath(), "stackshot-film-temp");
            if (Directory.Exists(shots)) Directory.Delete(shots, true);
            Directory.CreateDirectory(shots);
            Directory.CreateDirectory(temp);
            Rectangle[] parts = { new Rectangle(330, 180, 420, 520), new Rectangle(520, 600, 760, 330), new Rectangle(1240, 230, 420, 300), new Rectangle(600, 215, 1070, 565) };
            for (int i = 0; i < parts.Length; i++)
            {
                string f = Path.Combine(shots, "Captura " + (i + 1) + ".png");
                using (Bitmap b = desktop.Clone(parts[i], PixelFormat.Format32bppArgb)) b.Save(f, ImageFormat.Png);
                File.SetLastWriteTime(f, DateTime.Now.AddMinutes(-10 * (parts.Length - i)));
            }
            Recents.TempFolder = temp;

            for (int style = 0; style < 3; style++)
            {
                Settings st = Fresh(shots);
                st.HomeStyle = style;
                ShotStack stack = new ShotStack(st, false, false);
                HomeWindow w = new HomeWindow(stack, st);
                Park(w);
                Pump(2600);
                Save(Snap(w), Path.Combine(dir, "home-" + style + ".png"));
                if (style == 0)
                {
                    foreach (string page in new string[] { "general", "record", "mascot" })
                    {
                        typeof(HomeWindow).GetMethod("SetPage", NP).Invoke(w, new object[] { page, false });
                        Pump(1600);
                        Save(Snap(w), Path.Combine(dir, "page-" + page + ".png"));
                    }
                }
                w.Shutdown();
                stack.Quit();
            }

            string md = Path.Combine(temp, "notas-de-version.md"), xml = Path.Combine(temp, "pedido.xml");
            File.WriteAllText(md, Sample.Markdown);
            File.WriteAllText(xml, Sample.Xml);
            foreach (string f in new string[] { md, xml })
            {
                ConstructorInfo ci = typeof(MarkdownWindow).GetConstructors(NP)[0];
                MarkdownWindow v = (MarkdownWindow)ci.Invoke(new object[] { null });
                typeof(MarkdownWindow).GetField("open", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, v);
                Park(v);
                Pump(600);
                typeof(MarkdownWindow).GetMethod("LoadFile", NP).Invoke(v, new object[] { f });
                Pump(5000);
                Save(Snap(v), Path.Combine(dir, f == md ? "markdown.png" : "xml.png"));
                v.Close();
            }

            // The welcome window, as a first run shows it.
            {
                Settings st = new Settings();
                st.Appearance = 2;
                SetupWindow sw = (SetupWindow)typeof(SetupWindow).GetConstructors(NP)[0].Invoke(new object[] { st });
                Park(sw);
                Pump(1500);
                Save(Snap(sw), Path.Combine(dir, "welcome.png"));
                sw.Close();
            }

            Try(dir, delegate { Tray(dir, shots); });
            Try(dir, delegate { Updates(dir); });
            Try(dir, delegate { Picker(dir, desktop, true); });
            Try(dir, delegate { Picker(dir, desktop, false); });
            Try(dir, delegate { InvoicePlates(dir); EditorPlates(dir, temp); });

            // Minimal home in the light appearance.
            Ds.Apply(1);
            {
                Settings st = Fresh(shots);
                st.Appearance = 1;
                ShotStack stack = new ShotStack(st, false, false);
                HomeWindow w = new HomeWindow(stack, st);
                Park(w);
                Pump(2600);
                Save(Snap(w), Path.Combine(dir, "home-0-light.png"));
                w.Shutdown();
                stack.Quit();
            }
            Ds.Apply(2);
        }

        // The tray panel at 2x, drawn by its own Compose, plus where each row sits (for the moving highlight).
        static void Tray(string dir, string shots)
        {
            Settings st = Fresh(shots);
            TrayPanel tp = new TrayPanel(st);
            tp.Prewarm();
            Pump(1800);
            typeof(TrayPanel).GetMethod("Rebuild", NP).Invoke(tp, new object[] { 2f, 4000 });
            Pump(800);
            typeof(TrayPanel).GetMethod("Rebuild", NP).Invoke(tp, new object[] { 2f, 4000 });
            Size body = (Size)typeof(FloatWindow).GetField("body", NP).GetValue(tp);
            int pad = (int)typeof(FloatWindow).GetField("Pad", NP).GetValue(tp);
            int w = body.Width + 2 * pad, h = body.Height + 2 * pad;
            Save(Render(w, h, delegate(M.DrawingContext dc) { typeof(TrayPanel).GetMethod("Compose", NP).Invoke(tp, new object[] { dc, w, h }); }), Path.Combine(dir, "tray.png"));
            System.Collections.IList items = (System.Collections.IList)typeof(TrayPanel).GetField("items", NP).GetValue(tp);
            using (StreamWriter sw = new StreamWriter(Path.Combine(dir, "tray-rows.txt")))
                foreach (object it in items)
                {
                    Type t = it.GetType();
                    if ((int)t.GetField("Kind").GetValue(it) != 0) continue;
                    W.Rect r = (W.Rect)t.GetField("R").GetValue(it);
                    sw.WriteLine(t.GetField("Id").GetValue(it) + " " + (int)(r.X + pad) + " " + (int)(r.Y + pad) + " " + (int)r.Width + " " + (int)r.Height);
                }
            tp.Dispose();
        }

        // The update notice at 2x in each step of an update: downloading, verifying and installing.
        static void Updates(string dir)
        {
            Updater.Release r = new Updater.Release();
            r.Version = new Version(2, 1, 0);
            r.Tag = "v2.1.0";
            Updater.Available = r;
            UpdateNotice n = new UpdateNotice(null, r);
            typeof(UpdateNotice).GetField("s", NP).SetValue(n, 2f);
            typeof(FloatWindow).GetField("Pad", NP).SetValue(n, 60);
            string[] names = { "update-download", "update-verify", "update-install" };
            Updater.Step[] steps = { Updater.Step.Downloading, Updater.Step.Verifying, Updater.Step.Installing };
            for (int i = 0; i < steps.Length; i++)
            {
                Updater.Doing = steps[i];
                Updater.Fraction = 0.64;
                typeof(UpdateNotice).GetMethod("Arrange", NP).Invoke(n, null);
                Size body = (Size)typeof(FloatWindow).GetField("body", NP).GetValue(n);
                int w = body.Width + 120, h = body.Height + 120;
                Save(Render(w, h, delegate(M.DrawingContext dc) { typeof(UpdateNotice).GetMethod("PaintSurface", NP).Invoke(n, new object[] { dc, w, h }); }), Path.Combine(dir, names[i] + ".png"));
            }
            Updater.Doing = Updater.Step.None;
            Updater.Fraction = -1;
            Updater.Available = null;
            n.Dispose();
        }

        // The region picker over the fake desktop: hovering the chat window, or adjusting an area in all-in-one mode.
        static void Picker(string dir, Bitmap desktop, bool allInOne)
        {
            Rectangle vs = new Rectangle(0, 0, desktop.Width, desktop.Height), chat = new Rectangle(320, 150, 1360, 800);
            Dib frozen = new Dib(vs.Width, vs.Height);
            using (Graphics g = frozen.Graphics()) g.DrawImageUnscaled(desktop, 0, 0);
            Grabber.Win win = new Grabber.Win();
            win.Bounds = chat;
            win.Name = "Chat";
            ConstructorInfo ci = null;
            foreach (ConstructorInfo c in typeof(RegionPicker).GetConstructors(NP)) if (c.GetParameters().Length == 5) ci = c;
            RegionPicker p = (RegionPicker)ci.Invoke(new object[] { frozen, vs, new System.Collections.Generic.List<Grabber.Win> { win }, RegionPicker.Mode.Image, allInOne });
            Type t = typeof(RegionPicker);
            if (allInOne)
            {
                t.GetField("cur", NP).SetValue(p, new Point(1180, 590));
                t.GetMethod("TrackMonitor", NP).Invoke(p, null);
                t.GetMethod("Adjust", NP).Invoke(p, new object[] { new Rectangle(600, 215, 1070, 565) });
            }
            else
            {
                t.GetField("cur", NP).SetValue(p, new Point(900, 420));
                t.GetMethod("TrackMonitor", NP).Invoke(p, null);
                t.GetField("hover", NP).SetValue(p, chat);
            }
            using (Dib outD = new Dib(vs.Width, vs.Height))
            {
                t.GetMethod("Compose", NP).Invoke(p, new object[] { outD, vs });
                using (Bitmap b = outD.ToBitmap()) b.Save(Path.Combine(dir, allInOne ? "picker-aio.png" : "picker-window.png"), ImageFormat.Png);
            }
            p.Dispose();
            frozen.Dispose();
        }

        static void Try(string dir, Action a)
        {
            try { a(); }
            catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "errors.txt"), ex + Environment.NewLine); }
        }

        static M.Imaging.BitmapSource Render(int w, int h, Action<M.DrawingContext> draw)
        {
            M.DrawingVisual dv = new M.DrawingVisual();
            using (M.DrawingContext dc = dv.RenderOpen()) draw(dc);
            M.Imaging.RenderTargetBitmap rtb = new M.Imaging.RenderTargetBitmap(w, h, 96, 96, M.PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        static Settings Fresh(string shots)
        {
            Settings st = new Settings();
            st.ShowIntro = false;
            st.MascotLove = 42;
            st.Appearance = 2;
            st.SaveFolder = shots;
            st.ShowRecent = true;
            st.MascotTalks = false;
            return st;
        }

        static void Park(W.Window w)
        {
            w.WindowStartupLocation = W.WindowStartupLocation.Manual;
            w.Left = -6000;
            w.Top = 0;
            w.ShowActivated = false;
            w.Show();
        }

        static void Pump(int ms)
        {
            double end = Anim.Now + ms;
            while (Anim.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
        }

        // The window's content at twice its size over its own background (offscreen windows have no Mica).
        static M.Imaging.BitmapSource Snap(W.Window w)
        {
            w.UpdateLayout();
            W.FrameworkElement root = (W.FrameworkElement)w.Content;
            double ww = root.ActualWidth, wh = root.ActualHeight;
            M.DrawingVisual dv = new M.DrawingVisual();
            using (M.DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Ds.Brush(Ds.Dark ? Ds.Rgb(32, 32, 34) : Ds.Rgb(238, 238, 242)), null, new W.Rect(0, 0, ww, wh));
                dc.DrawRectangle(new M.VisualBrush(root), null, new W.Rect(0, 0, ww, wh));
            }
            M.Imaging.RenderTargetBitmap rtb = new M.Imaging.RenderTargetBitmap((int)(ww * 2), (int)(wh * 2), 192, 192, M.PixelFormats.Pbgra32);
            rtb.Render(dv);
            return rtb;
        }

        static void Save(M.Imaging.BitmapSource b, string path)
        {
            M.Imaging.PngBitmapEncoder e = new M.Imaging.PngBitmapEncoder();
            e.Frames.Add(M.Imaging.BitmapFrame.Create(b));
            using (FileStream fs = File.Create(path)) e.Save(fs);
        }
    }

    static class Sample
    {
        public const string Markdown =
"# Stackshot 2.0\n\n" +
"Un **redise\u00F1o completo** al estilo de macOS, con las mismas funciones y todo m\u00E1s r\u00E1pido.\n\n" +
"## Novedades\n\n" +
"- Ventana principal con *tres vistas* de inicio\n" +
"- Selector de \u00E1rea m\u00E1s r\u00E1pido: `1,6 ms` por movimiento\n" +
"- Visor de **Markdown** y **XML** integrado\n" +
"- Actualizaciones verificadas con SHA-256 y firma\n\n" +
"> Captura, marca y comparte en segundos.\n\n" +
"| Modo | Atajo |\n|---|---|\n| \u00C1rea | Impr Pant |\n| V\u00EDdeo | May\u00FAs + Impr Pant |\n| GIF | Ctrl + May\u00FAs + Impr Pant |\n\n" +
"```powershell\nStackshot.exe --install --startup\n```\n";

        public const string Xml =
"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
"<pedido id=\"PED-2026-0412\" fecha=\"2026-10-09\" estado=\"enviado\">\n" +
"  <cliente tipo=\"empresa\">\n    <nombre>Estudio Aurora S.L.</nombre>\n    <email>hola@aurora.example</email>\n" +
"    <direccion ciudad=\"Valencia\" cp=\"46001\">Calle de la Paz, 12</direccion>\n  </cliente>\n" +
"  <lineas>\n" +
"    <linea sku=\"CAM-01\" cantidad=\"2\" precio=\"49.90\">C\u00E1mara web 4K</linea>\n" +
"    <linea sku=\"MIC-07\" cantidad=\"1\" precio=\"89.00\">Micr\u00F3fono de estudio</linea>\n" +
"    <linea sku=\"LUZ-03\" cantidad=\"3\" precio=\"24.50\">Luz LED regulable</linea>\n" +
"  </lineas>\n" +
"  <envio metodo=\"urgente\" transportista=\"R\u00E1pido\">\n    <seguimiento>RX-88412-ES</seguimiento>\n  </envio>\n" +
"  <total moneda=\"EUR\">262.30</total>\n" +
"</pedido>\n";
    }
}

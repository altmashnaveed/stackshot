// Stackshot - Trailer parts for the README and the media kit: every scene of the film at its own pace, plus installing, the
// tray panel, verified updates, light and dark, the desktop mascot, all-in-one mode, pinning and the newest features. Each one
// renders on its own, without fades, so it loops cleanly.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot
{
    public static partial class Film
    {
        static BitmapSource welcome, tray, homeLight, pickerAio, pickerWindow;
        static BitmapSource[] updates;
        static Dictionary<string, Rect> trayRows = new Dictionary<string, Rect>();

        static void LoadParts()
        {
            welcome = Load(Path.Combine(plates, "welcome.png"));
            tray = Load(Path.Combine(plates, "tray.png"));
            homeLight = Load(Path.Combine(plates, "home-0-light.png"));
            pickerAio = Load(Path.Combine(plates, "picker-aio.png"));
            pickerWindow = Load(Path.Combine(plates, "picker-window.png"));
            updates = new BitmapSource[] { Load(Path.Combine(plates, "update-download.png")), Load(Path.Combine(plates, "update-verify.png")), Load(Path.Combine(plates, "update-install.png")) };
            foreach (string line in File.ReadAllLines(Path.Combine(plates, "tray-rows.txt")))
            {
                string[] f = line.Split(' ');
                trayRows[f[0]] = new Rect(double.Parse(f[1]), double.Parse(f[2]), double.Parse(f[3]), double.Parse(f[4]));
            }
        }

        static void AddPart(string name)
        {
            Dictionary<string, KeyValuePair<double, SceneFn>> parts = new Dictionary<string, KeyValuePair<double, SceneFn>>
            {
                { "modes", new KeyValuePair<double, SceneFn>(10.6, Modes) },
                { "capture", new KeyValuePair<double, SceneFn>(11.2, Capture) },
                { "editor", new KeyValuePair<double, SceneFn>(10.0, EditorScene) },
                { "scroll", new KeyValuePair<double, SceneFn>(7.4, Scroll) },
                { "record", new KeyValuePair<double, SceneFn>(9.0, Record) },
                { "viewers", new KeyValuePair<double, SceneFn>(6.2, Viewers) },
                { "home", new KeyValuePair<double, SceneFn>(7.6, Home) },
                { "mascot", new KeyValuePair<double, SceneFn>(9.2, MascotScene) },
                { "install", new KeyValuePair<double, SceneFn>(7.4, Install) },
                { "tray", new KeyValuePair<double, SceneFn>(7.6, TrayScene) },
                { "updates", new KeyValuePair<double, SceneFn>(7.6, UpdatesScene) },
                { "theme", new KeyValuePair<double, SceneFn>(6.6, Theme2) },
                { "pet", new KeyValuePair<double, SceneFn>(10.4, Pet) },
                { "aio", new KeyValuePair<double, SceneFn>(7.2, AllInOne) },
                { "pin", new KeyValuePair<double, SceneFn>(7.4, Pin) },
                { "history", new KeyValuePair<double, SceneFn>(6.8, History) },
                { "sensitive", new KeyValuePair<double, SceneFn>(7.4, Sensitive) },
                { "record-plus", new KeyValuePair<double, SceneFn>(7.4, RecordPlus) },
                { "docs-pro", new KeyValuePair<double, SceneFn>(7.4, DocsPro) },
            };
            double at = scenes.Count == 0 ? 0 : scenes[scenes.Count - 1].Start + scenes[scenes.Count - 1].Dur - Overlap;
            scenes.Add(new Scene { Name = name, Start = at, Dur = parts[name].Key, Draw = parts[name].Value });
        }

        // ---- Shared pieces

        // A key combination as pills: "Ctrl" "+" "Impr Pant".
        static double Keys(DrawingContext dc, string[] keys, double x, double cy, double h, double k, bool measureOnly = false)
        {
            double start = x;
            for (int i = 0; i < keys.Length; i++)
            {
                FormattedText ft = FT(keys[i], h * 0.4, FontWeights.SemiBold, B(Fg), body);
                double w = Math.Max(h, ft.Width + h * 0.6);
                if (!measureOnly && k > 0)
                {
                    double kk = OutBack(P(k, i * 0.12, i * 0.12 + 0.6));
                    Rect r = new Rect(x, cy - h / 2 + (1 - kk) * 14, w, h);
                    dc.PushOpacity(Clamp(kk));
                    dc.DrawRoundedRectangle(B(Color.FromRgb(12, 12, 14)), null, new Rect(r.X, r.Y + h * 0.07, r.Width, r.Height), h * 0.22, h * 0.22);
                    dc.DrawRoundedRectangle(Grad(Color.FromRgb(62, 62, 68), Color.FromRgb(34, 34, 38), 90), new Pen(B(Colors.White, 0.16), 1.2), r, h * 0.22, h * 0.22);
                    dc.DrawText(ft, new Point(r.X + (w - ft.Width) / 2, r.Y + (h - ft.Height) / 2));
                    dc.Pop();
                }
                x += w;
                if (i < keys.Length - 1)
                {
                    FormattedText plus = FT("+", h * 0.42, FontWeights.Normal, B(Gray), body);
                    if (!measureOnly && k > 0) { dc.PushOpacity(Clamp(k * 2)); dc.DrawText(plus, new Point(x + 10, cy - plus.Height / 2)); dc.Pop(); }
                    x += plus.Width + 20;
                }
            }
            return x - start;
        }

        // A Windows 11 style taskbar along the bottom of the fake desktop, with Stackshot in the tray.
        static Point Taskbar(DrawingContext dc, double hot)
        {
            Rect bar = new Rect(0, 1032, W, 48);
            dc.DrawRectangle(B(Color.FromArgb(235, 26, 26, 30)), null, bar);
            dc.DrawLine(new Pen(B(Colors.White, 0.08), 1), new Point(0, 1032.5), new Point(W, 1032.5));
            Color[] apps = { Color.FromRgb(10, 132, 255), Color.FromRgb(255, 189, 46), Color.FromRgb(52, 199, 89), Color.FromRgb(191, 90, 242), Color.FromRgb(255, 69, 58), Color.FromRgb(100, 210, 255) };
            for (int i = 0; i < apps.Length; i++)
                dc.DrawRoundedRectangle(Grad(apps[i], Mix(apps[i], Colors.Black, 0.35), 90), null, new Rect(W / 2.0 - apps.Length * 22 + i * 44 + 8, 1042, 28, 28), 7, 7);
            Point icon = new Point(1700, 1056);
            if (hot > 0) dc.DrawRoundedRectangle(B(Colors.White, 0.12 * hot), null, new Rect(icon.X - 20, icon.Y - 18, 40, 36), 6, 6);
            dc.DrawImage(logo, new Rect(icon.X - 13, icon.Y - 13, 26, 26));
            Pen p = new Pen(B(Fg, 0.85), 1.8);
            dc.DrawEllipse(null, p, new Point(1752, 1058), 6, 6);
            dc.DrawRoundedRectangle(null, p, new Rect(1772, 1050, 20, 12), 3, 3);
            Text(dc, "15:42", 14, FontWeights.Normal, B(Fg), 1850, 1038, 0, 1, body);
            Text(dc, "09/10/2026", 12, FontWeights.Normal, B(Fg, 0.8), 1850, 1056, 0, 1, body);
            return icon;
        }

        // Pixel standing with its feet at (x, feetY), size in desktop pixels.
        static void Pixel(DrawingContext dc, double x, double feetY, double size, double squash = 0)
        {
            dc.DrawEllipse(B(Colors.Black, 0.3), null, new Point(x, feetY + 2), size * 0.18, size * 0.035);
            dc.PushTransform(new ScaleTransform(1 + squash * 0.12, 1 - squash * 0.12, x, feetY));
            dc.DrawImage(costumes[0], new Rect(x - size / 2, feetY - size * 0.87, size, size));
            dc.Pop();
        }

        static void Wheel(DrawingContext dc, Point c, double t)
        {
            dc.DrawRoundedRectangle(B(Color.FromArgb(230, 28, 28, 32)), new Pen(B(Colors.White, 0.5), 1.5), new Rect(c.X - 14, c.Y - 22, 28, 44), 14, 14);
            double y = (t * 2.5) % 1;
            dc.DrawRoundedRectangle(B(Colors.White, 1 - y), null, new Rect(c.X - 2, c.Y - 12 + y * 10, 4, 9), 2, 2);
        }

        // ================= Parts =================

        static void Modes(DrawingContext dc, double t, double dur)
        {
            Headline(dc, L("Seven ways to capture.", "Siete formas de capturar."), 72, B(Fg), W / 2.0, 58, t, 0.1);
            Text(dc, L("One shortcut each. All of them one key away.", "Un atajo para cada una. Todas a una tecla."), 29, FontWeights.Normal, B(Gray), W / 2.0, 156, 0, P(t, 0.4, 1.0), body);
            string[] names = es ? new string[] { "\u00C1rea", "Pantalla", "Ventana", "Desplazar", "V\u00EDdeo", "GIF", "Markdown" }
                                : new string[] { "Area", "Screen", "Window", "Scrolling", "Video", "GIF", "Markdown" };
            string[] what = es ? new string[] { "Arrastra sobre lo que quieras.", "La pantalla en la que est\u00E1 el rat\u00F3n.", "Cualquier ventana, con sus esquinas.", "La p\u00E1gina entera, en una imagen.", "MP4 con sonido y tu c\u00E1mara.", "Animado y listo para compartir.", "Lee y ordena .md y .xml." }
                               : new string[] { "Drag over anything.", "The screen under your mouse.", "Any window, corners and all.", "The whole page, in one image.", "MP4 with sound and your webcam.", "Animated and ready to share.", "Read and tidy .md and .xml." };
            string prt = L("PrtSc", "Impr Pant"), shift = L("Shift", "May\u00FAs");
            string[][] keys = { new string[] { prt }, new string[] { "Ctrl", prt }, new string[] { "Alt", prt }, new string[] { "Ctrl", "Alt", prt },
                                new string[] { shift, prt }, new string[] { "Ctrl", shift, prt }, new string[] { "Ctrl", shift, "Alt", "M" } };
            Color[] col = { Blue, Teal, Indigo, Green, Red, Orange, Purple };
            double step = 1.3, start = 0.75;
            Rect card = new Rect(930, 360, 880, 495);
            for (int i = 0; i < 7; i++)
            {
                double lt = t - (start + i * step);
                if (lt < -0.05 || lt > step + 0.3) continue;
                double kin = OutExpo(P(lt, 0, 0.45)), kout = i == 6 ? 0 : InOut(P(lt, step - 0.05, step + 0.25));
                double a = kin * (1 - kout);
                if (a <= 0) continue;
                // Left: number, name, keys, line
                dc.PushOpacity(a);
                dc.PushTransform(new TranslateTransform(0, (1 - kin) * 40 - kout * 40));
                Text(dc, (i + 1).ToString("00") + "  /  07", 22, FontWeights.SemiBold, B(col[i]), 120, 400, -1, 1, body);
                Text(dc, names[i], 128, FontWeights.Bold, Grad(Mix(col[i], Colors.White, 0.35), col[i], 0), 112, 440, -1);
                Keys(dc, keys[i], 120, 650, 62, P(lt, 0.05, 0.7) * 1.6);
                Text(dc, what[i], 32, FontWeights.Normal, B(Gray), 120, 720, -1, 1, body);
                dc.Pop();
                dc.Pop();
                // Right: a tiny demo of the mode
                Glow(dc, card.X + card.Width / 2, card.Y + card.Height / 2, 1200, 800, col[i], 0.22 * a);
                dc.PushOpacity(a);
                double sx = (1 - kin) * 60 - kout * 60;
                Rect c = new Rect(card.X + sx, card.Y, card.Width, card.Height);
                ModeDemo(dc, i, c, lt);
                dc.Pop();
            }
            // The flash of the shortcut being pressed, at each change
            for (int i = 0; i < 7; i++)
            {
                double f = P(t, start + i * step, start + i * step + 0.25);
                if (f > 0 && f < 1) dc.DrawRectangle(B(col[i], 0.08 * (1 - f)), null, new Rect(0, 0, W, H));
            }
        }

        static void ModeDemo(DrawingContext dc, int mode, Rect c, double u)
        {
            if (mode == 2) { PushScreen(dc, c, 1, new Point()); dc.DrawImage(u < 0.15 ? desk : pickerWindow, new Rect(0, 0, W, H)); }
            else if (mode == 6) { Plate(dc, md, Fit(md, c.X + c.Width / 2, c.Y + c.Height / 2, c.Width * 0.92), 14, 1.0); return; }
            else if (mode == 5)
            {
                Shadow(dc, c, 22, 1.0, 50, 24);
                dc.DrawRoundedRectangle(Grad(Color.FromRgb(70, 80, 170), Color.FromRgb(150, 90, 210), 35), new Pen(B(Colors.White, 0.16), 1.2), c, 22, 22);
                double hop = Math.Abs(Math.Sin(u * 7));
                Pixel(dc, c.X + c.Width / 2, c.Y + c.Height * 0.8 - hop * 70, 330, hop < 0.15 ? 1 - hop / 0.15 : 0);
                Capsule(dc, "GIF", c.X + 60, c.Y + 40, 20, Color.FromArgb(220, 20, 20, 24), Fg, 1, 16, 40);
                Capsule(dc, "\u221E", c.Right - 52, c.Y + 40, 22, Color.FromArgb(220, 20, 20, 24), Fg, 1, 14, 40);
                return;
            }
            else if (mode == 3)
            {
                Shadow(dc, c, 22, 1.0, 50, 24);
                dc.DrawRoundedRectangle(B(Color.FromRgb(24, 24, 28)), null, c, 22, 22);
                Rect v = new Rect(c.X + c.Width / 2 - 300, c.Y + 30, 600, c.Height - 60);
                dc.PushClip(new RectangleGeometry(v, 8, 8));
                double sc = 600 / 720.0;
                dc.DrawImage(longDoc, new Rect(v.X, v.Y - u * 520, 600, longDoc.PixelHeight * sc));
                dc.Pop();
                Rect f = v;
                f.Inflate(6, 6);
                CornerMarks(dc, f, 22, 4);
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.16), 1.2), c, 22, 22);
                return;
            }
            else { PushScreen(dc, c, 1, new Point()); dc.DrawImage(desk, new Rect(0, 0, W, H)); }

            if (mode == 0)
            {
                double k = InOut(P(u, 0.1, 0.8));
                Rect sel = new Rect(600, 215, Math.Max(1, 1070 * k), Math.Max(1, 565 * k));
                dc.DrawGeometry(B(Colors.Black, 0.42), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(sel)));
                dc.DrawRectangle(null, new Pen(B(Colors.White), 2.5), sel);
                Capsule(dc, ((int)sel.Width) + " \u00D7 " + ((int)sel.Height), sel.X + sel.Width / 2, sel.Bottom + 34, 26, Color.FromArgb(225, 28, 28, 30), Fg);
                Pointer(dc, new Point(sel.Right, sel.Bottom), 3, 1);
            }
            else if (mode == 1)
            {
                double f = P(u, 0.3, 0.55), s = InOut(P(u, 0.55, 1.05));
                if (s > 0)
                {
                    dc.DrawRectangle(B(Color.FromRgb(16, 16, 20)), null, new Rect(0, 0, W, H));
                    Rect r = Lerp(new Rect(0, 0, W, H), new Rect(40, 1080 - 40 - 300, 534, 300), s);
                    Shadow(dc, r, 24 * s, 1.0, 30, 10);
                    dc.PushClip(new RectangleGeometry(r, 24 * s, 24 * s));
                    dc.DrawImage(desk, r);
                    dc.Pop();
                }
                if (f > 0 && f < 1) dc.DrawRectangle(B(Colors.White, 0.7 * (1 - f)), null, new Rect(0, 0, W, H));
            }
            else if (mode == 2)
            {
                double f = P(u, 0.7, 0.95);
                if (f > 0 && f < 1) dc.DrawRectangle(B(Colors.White, 0.5 * (1 - f)), null, new Rect(320, 150, 1360, 800));
                Pointer(dc, new Point(900, 420), 3, u > 0.62 && u < 0.75 ? 1 : 0);
            }
            else if (mode == 4)
            {
                Rect area = new Rect(520, 200, 820, 460);
                dc.DrawRectangle(null, new Pen(B(Colors.White, 0.6), 2), area);
                CornerMarks(dc, area, 44, 8);
                Rect bar = new Rect(area.X + area.Width / 2 - 170, area.Bottom + 36, 340, 84);
                Pill(dc, bar, B(Color.FromRgb(28, 28, 32)));
                dc.DrawEllipse(B(Red, 0.55 + 0.45 * Math.Abs(Math.Cos(u * 5))), null, new Point(bar.X + 46, bar.Y + 42), 13, 13);
                Text(dc, "00:0" + (int)(u * 3), 34, FontWeights.SemiBold, B(Fg), bar.X + 76, bar.Y + 20, -1, 1, mono);
                Capsule(dc, "Detener", bar.Right - 90, bar.Y + 42, 26, Red, Colors.White, 1, 22, 58);
                Point wc = new Point(area.Right - 130, area.Bottom - 130);
                dc.PushClip(new EllipseGeometry(wc, 100, 100));
                dc.DrawRectangle(Grad(Color.FromRgb(70, 80, 160), Color.FromRgb(140, 90, 200), 60), null, new Rect(wc.X - 100, wc.Y - 100, 200, 200));
                dc.DrawImage(costumes[0], new Rect(wc.X - 160, wc.Y - 165, 320, 320));
                dc.Pop();
                dc.DrawEllipse(null, new Pen(B(Colors.White, 0.9), 5), wc, 100, 100);
            }
            PopScreen(dc, c);
        }

        static void Install(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Installed in one click.", "Instalada con un clic."),
                L("No admin rights and nothing else to install. Just 2.2 MB.", "Sin permisos de administrador ni nada m\u00E1s que instalar. Solo 2,2 MB."));
            // The download, as a browser shows it
            double chipK = OutBack(P(t, 0.3, 0.8)) * (1 - P(t, 1.5, 1.8));
            Rect chip = new Rect(W - 520, 250, 420, 92);
            if (chipK > 0)
            {
                dc.PushOpacity(Clamp(chipK));
                dc.PushTransform(new TranslateTransform((1 - chipK) * 40, 0));
                Shadow(dc, chip, 16, 0.9, 30, 12);
                dc.DrawRoundedRectangle(B(Color.FromRgb(36, 36, 40)), new Pen(B(Colors.White, 0.1), 1), chip, 16, 16);
                dc.DrawImage(logo, new Rect(chip.X + 20, chip.Y + 18, 56, 56));
                Text(dc, "Stackshot.exe", 22, FontWeights.SemiBold, B(Fg), chip.X + 92, chip.Y + 20, -1, 1, body);
                Text(dc, L("2.2 MB \u00B7 Done", "2,2 MB \u00B7 Completado"), 17, FontWeights.Normal, B(Gray), chip.X + 92, chip.Y + 52, -1, 1, body);
                dc.Pop();
                dc.Pop();
            }
            // The welcome window
            double s = 0.54;
            Rect wr = new Rect(W / 2.0 - welcome.PixelWidth * s / 2, 232, welcome.PixelWidth * s, welcome.PixelHeight * s);
            double wk = Spring(P(t, 1.5, 2.6) * 1.6), gone = InOut(P(t, 4.3, 4.9));
            if (wk > 0 && gone < 1)
            {
                double z = Lerp(0.9, 1, wk) * Lerp(1, 0.85, gone);
                dc.PushOpacity(Clamp(wk * 2) * (1 - gone));
                dc.PushTransform(new ScaleTransform(z, z, wr.X + wr.Width / 2, wr.Y + wr.Height / 2));
                Plate(dc, welcome, wr, 16, 1.2);
                double press = t > 3.95 && t < 4.15 ? 1 : 0;
                if (press > 0) dc.DrawRoundedRectangle(B(Colors.Black, 0.18), null, new Rect(wr.X + 660 * s, wr.Y + 1358 * s, 308 * s, 80 * s), 40 * s, 40 * s);
                dc.Pop();
                dc.Pop();
            }
            // The pointer: to the download, then to "Instalar y empezar"
            Point bt = new Point(wr.X + 814 * s, wr.Y + 1398 * s);
            Point cur = t < 1.3 ? LerpP(new Point(1200, 700), new Point(chip.X + 200, chip.Y + 50), InOut(P(t, 0.5, 1.2)))
                      : LerpP(new Point(chip.X + 200, chip.Y + 50), bt, InOut(P(t, 2.6, 3.8)));
            if (t < 4.4) Pointer(dc, cur, 1.6, (t > 1.2 && t < 1.4) || (t > 3.95 && t < 4.15) ? 1 : 0);
            // Ready: the key
            double rk = OutBack(P(t, 4.8, 5.4));
            if (rk > 0)
            {
                Point c = new Point(W / 2.0, 560);
                double press = t > 5.9 && t < 6.05 ? 1 : 0;
                dc.PushOpacity(Clamp(rk));
                dc.PushTransform(new ScaleTransform(Lerp(0.8, 1, rk), Lerp(0.8, 1, rk), c.X, c.Y));
                Glow(dc, c.X, c.Y + 30, 700, 420, Brand1, 0.3);
                Keycap(dc, L("PrtSc", "Impr Pant"), c, 220, press);
                dc.Pop();
                dc.Pop();
                Text(dc, L("Ready. Press it.", "Listo. P\u00FAlsala."), 56, FontWeights.Bold, B(Fg), W / 2.0, 740, 0, P(t, 5.1, 5.6));
                double burst = P(t, 6.05, 7.0);
                if (burst > 0 && burst < 1)
                {
                    double rr = 140 + 700 * OutExpo(burst);
                    dc.DrawEllipse(null, new Pen(B(Colors.White, 0.5 * (1 - burst)), 2.5), c, rr, rr);
                }
            }
        }

        static void TrayScene(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Always one click away.", "Siempre a un clic."),
                L("Every mode with its shortcut and your latest captures, right from the tray.", "Todos los modos con su atajo y tus \u00FAltimas capturas, desde la bandeja."));
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            double zoom = 1 + 0.45 * InOut(P(t, 1.5, 2.3));
            PushScreen(dc, sr, zoom, new Point(1910, 1075));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            Point icon = Taskbar(dc, P(t, 0.9, 1.0));
            double open = Spring(P(t, 1.25, 2.1) * 1.6);
            Rect pr = new Rect(1910 - tray.PixelWidth / 2.0, 1032 - 6 - tray.PixelHeight / 2.0, tray.PixelWidth / 2.0, tray.PixelHeight / 2.0);
            if (open > 0)
            {
                dc.PushOpacity(Clamp(open * 2));
                dc.PushTransform(new ScaleTransform(Lerp(0.92, 1, open), Lerp(0.92, 1, open), pr.Right - 30, pr.Bottom));
                dc.PushTransform(new TranslateTransform(0, (1 - open) * 20));
                dc.DrawImage(tray, pr);
                // The highlight glides between the capture modes
                string[] path = { "region", "screen", "window", "scroll", "video", "gif" };
                double hp = Math.Max(0, Math.Min(path.Length - 1, (t - 2.6) / 0.5));
                int a = Math.Min(path.Length - 1, (int)hp), b = Math.Min(path.Length - 1, a + 1);
                double f = InOut(hp - a);
                if (t > 2.5 && t < 6.2)
                {
                    Rect ra = trayRows[path[a]], rb = trayRows[path[b]];
                    Rect hr = Lerp(ra, rb, f);
                    Rect h = new Rect(pr.X + hr.X / 2, pr.Y + hr.Y / 2, hr.Width / 2, hr.Height / 2);
                    dc.DrawRoundedRectangle(B(Colors.White, 0.09 * P(t, 2.5, 2.7)), null, h, 6, 6);
                }
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }
            Point cur;
            if (t < 1.4) cur = LerpP(new Point(1300, 760), new Point(icon.X + 4, icon.Y + 2), InOut(P(t, 0.3, 1.1)));
            else
            {
                string[] path = { "region", "screen", "window", "scroll", "video", "gif" };
                double hp = Math.Max(0, Math.Min(path.Length - 1, (t - 2.6) / 0.5));
                int a = Math.Min(path.Length - 1, (int)hp), b = Math.Min(path.Length - 1, a + 1);
                Rect ra = trayRows[path[a]], rb = trayRows[path[b]];
                Point pa = new Point(pr.X + (ra.X + ra.Width * 0.3) / 2, pr.Y + (ra.Y + ra.Height / 2) / 2), pb = new Point(pr.X + (rb.X + rb.Width * 0.3) / 2, pr.Y + (rb.Y + rb.Height / 2) / 2);
                Point row = LerpP(pa, pb, InOut(hp - a));
                cur = LerpP(new Point(icon.X + 4, icon.Y + 2), row, InOut(P(t, 2.1, 2.6)));
            }
            Pointer(dc, cur, 1.3, t > 0.95 && t < 1.1 ? 1 : 0);
            PopScreen(dc, sr);
        }

        static void UpdatesScene(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Updates that check themselves.", "Actualizaciones que se verifican solas."),
                L("Every download is checked against its SHA-256 and the maintainer's signature before it installs.", "Cada descarga se comprueba con su SHA-256 y la firma del autor antes de instalarse."));
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            PushScreen(dc, sr, 1 + 1.1 * InOut(P(t, 0.5, 1.3)), new Point(1910, 1030));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            Taskbar(dc, 0);
            double[] at = { 1.0, 3.0, 4.9 };
            for (int i = 0; i < 3; i++)
            {
                double k = i == 0 ? OutExpo(P(t, at[0], at[0] + 0.5)) : P(t, at[i], at[i] + 0.3);
                double next = i < 2 ? P(t, at[i + 1], at[i + 1] + 0.3) : 0;
                if (k <= 0 || next >= 1) continue;
                BitmapSource img = updates[i];
                Rect r = new Rect(1904 - img.PixelWidth / 2.0 + 30, 1032 - 16 - img.PixelHeight / 2.0 + 30, img.PixelWidth / 2.0, img.PixelHeight / 2.0);
                dc.PushOpacity(Clamp(k) * (1 - next));
                dc.PushTransform(new TranslateTransform(i == 0 ? (1 - k) * 40 : 0, 0));
                dc.DrawImage(img, r);
                if (i == 0)
                {
                    // The real bar runs from 64 %; let it move a little while it downloads.
                    double prog = Lerp(0.2, 1, P(t, 1.0, 2.9));
                    Rect bar = new Rect(r.X + 193 / 2.0, r.Y + 179 / 2.0, (751 - 193) / 2.0, 4);
                    dc.DrawRoundedRectangle(B(Color.FromRgb(58, 58, 62)), null, new Rect(bar.X + bar.Width * prog, bar.Y, bar.Width * (1 - prog), bar.Height), 2, 2);
                }
                dc.Pop();
                dc.Pop();
            }
            PopScreen(dc, sr);
            string[] checks = es ? new string[] { "Suma SHA-256", "Firma del autor", "Se vuelve a abrir sola", "Nunca corta una grabaci\u00F3n" }
                                 : new string[] { "SHA-256 checksum", "Maintainer's signature", "Reopens by itself", "Never cuts a recording" };
            double total = 0;
            double[] ws = new double[checks.Length];
            for (int i = 0; i < checks.Length; i++) { ws[i] = FT("\u2713  " + checks[i], 18, FontWeights.SemiBold, B(Fg), body).Width + 40; total += ws[i] + 14; }
            double x = W / 2.0 - (total - 14) / 2;
            double[] when = { 3.2, 3.5, 5.1, 5.4 };
            for (int i = 0; i < checks.Length; i++)
            {
                Capsule(dc, "\u2713  " + checks[i], x + ws[i] / 2, sr.Bottom + 56, 18, Color.FromRgb(30, 52, 38), Color.FromRgb(140, 235, 160), OutCubic(P(t, when[i], when[i] + 0.4)), 20, 44);
                x += ws[i] + 14;
            }
        }

        static void Theme2(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Light or dark. Your call.", "Claro u oscuro. T\u00FA decides."),
                L("It follows Windows, or pick one. Everything switches live.", "Sigue a Windows o elige uno. Todo cambia al momento."));
            double rise = OutCubic(P(t, 0.1, 0.9));
            Rect r = Fit(homeA, W / 2.0, 610 + (1 - rise) * 60, 1120);
            double wipe = InOut(P(t, 1.2, 2.4)) * (1 - InOut(P(t, 4.0, 5.2)));
            dc.PushOpacity(rise);
            Shadow(dc, r, 14, 1.2, 50, 26);
            dc.PushClip(new RectangleGeometry(r, 14, 14));
            dc.DrawImage(homeA, r);
            double edge = r.X + r.Width * wipe;
            if (wipe > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y, edge - r.X, r.Height)));
                dc.DrawImage(homeLight, r);
                dc.Pop();
                if (wipe < 1)
                {
                    LinearGradientBrush g = new LinearGradientBrush();
                    g.StartPoint = new Point(0, 0.5);
                    g.EndPoint = new Point(1, 0.5);
                    g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                    g.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5));
                    g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                    dc.DrawRectangle(g, null, new Rect(edge - 30, r.Y, 60, r.Height));
                }
            }
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.14), 1.2), r, 14, 14);
            dc.Pop();
            string[] seg = es ? new string[] { "Como Windows", "Claro", "Oscuro" } : new string[] { "Like Windows", "Light", "Dark" };
            int on = t < 1.2 ? 2 : t < 4.0 ? 1 : 2;
            double x = W / 2.0 - 300;
            Rect track = new Rect(x, 1000, 600, 48);
            dc.DrawRoundedRectangle(B(Color.FromRgb(38, 38, 44)), null, track, 24, 24);
            double slide = Lerp(2, 1, InOut(P(t, 1.2, 1.6))) + InOut(P(t, 4.0, 4.4));
            dc.DrawRoundedRectangle(B(Color.FromRgb(72, 72, 80)), null, new Rect(x + 4 + slide * 198, 1004, 194, 40), 20, 20);
            for (int i = 0; i < 3; i++) Text(dc, seg[i], 18, FontWeights.SemiBold, B(i == on ? Fg : Gray), x + 100 + i * 200, 1012, 0, 1, body);
        }

        static void Pet(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Pixel lives on your desktop.", "Pixel vive en tu escritorio."),
                L("It walks above the taskbar, hops onto your windows and naps when you're away.", "Pasea sobre la barra de tareas, salta a tus ventanas y se echa la siesta si no est\u00E1s."),
                L("And never in your captures.", "Y nunca sale en tus capturas."),
                L("It hides from screenshots, recordings and screen sharing.", "Se esconde de las capturas, las grabaciones y al compartir pantalla."), 6.4);
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            PushScreen(dc, sr, 1, new Point());
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            Taskbar(dc, 0);
            double size = 250, x, feet, squash = 0;
            if (t < 2.9) { x = Lerp(160, 760, P(t, 0.5, 2.9)); feet = 1032 - Math.Abs(Math.Sin(t * 9)) * 10; }
            else if (t < 3.6)
            {
                double k = P(t, 2.9, 3.6);
                x = Lerp(760, 900, k);
                feet = Lerp(1032, 150, k) - Math.Sin(k * Math.PI) * 220;
                squash = t < 3.0 ? 1 - P(t, 2.9, 3.0) : 0;
            }
            else if (t < 4.9) { x = Lerp(900, 1240, P(t, 3.7, 4.9)); feet = 150 - Math.Abs(Math.Sin(t * 9)) * 10; squash = t < 3.75 ? 1 - P(t, 3.6, 3.75) : 0; }
            else { x = 1240; feet = 150; squash = -0.1 * Math.Sin(t * 2.2); }
            {
                Pixel(dc, x, feet, size, squash);
                if (t > 5.0)
                    for (int i = 0; i < 3; i++)
                    {
                        double z = ((t - 5.0) * 0.6 + i / 3.0) % 1;
                        dc.PushOpacity(Math.Sin(z * Math.PI));
                        Text(dc, "z", 26 + i * 6, FontWeights.Bold, B(Fg), x + 60 + z * 60, feet - size * 0.75 - z * 110, -1);
                        dc.Pop();
                    }
            }
            // A capture of that corner: the mascot is not in it.
            Rect sel = new Rect(700, 0, 900, 580);
            double dim = P(t, 6.5, 6.7) * (1 - P(t, 7.5, 7.6));
            Point cur = t < 7.4 ? LerpP(new Point(sel.X, sel.Y), new Point(sel.Right, sel.Bottom), InOut(P(t, 6.6, 7.4))) : new Point(sel.Right, sel.Bottom);
            if (dim > 0)
            {
                Rect cs = new Rect(new Point(sel.X, sel.Y), cur);
                dc.DrawGeometry(B(Colors.Black, 0.42 * dim), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(cs)));
                dc.DrawRectangle(null, new Pen(B(Colors.White, dim), 1.5), cs);
            }
            double flash = P(t, 7.5, 7.85);
            if (flash > 0 && flash < 1) dc.DrawRectangle(B(Colors.White, 0.55 * (1 - flash)), null, sel);
            if (t > 7.55)
            {
                double fly = InOut(P(t, 7.6, 8.4));
                Rect card = new Rect(28, 1032 - 28 - 214, 344, 214);
                Rect r = Lerp(sel, card, fly);
                Shadow(dc, r, 16 * fly, 0.4 + 0.6 * fly, 30, 12);
                dc.PushClip(new RectangleGeometry(r, 16 * fly, 16 * fly));
                dc.DrawImage(DeskCrop((int)sel.X, (int)sel.Y, (int)sel.Width, (int)sel.Height), r);
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.22 * fly), 1.5), r, 16 * fly, 16 * fly);
            }
            if (t > 6.5 && t < 7.6) Pointer(dc, cur, 1.6, t > 6.6 ? 1 : 0);
            PopScreen(dc, sr);
        }

        static void AllInOne(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Select first. Decide after.", "Primero seleccionas. Luego decides."),
                L("All-in-one mode: adjust the area, then capture, record a video or a GIF, or scroll.", "Modo todo en uno: ajusta el \u00E1rea y elige capturar, grabar un v\u00EDdeo o un GIF, o desplazar."));
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            PushScreen(dc, sr, 1 + 0.35 * InOut(P(t, 1.2, 2.0)) * (1 - InOut(P(t, 5.0, 5.6))), new Point(1135, 700));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            double plate = P(t, 0.6, 0.9) * (1 - P(t, 4.9, 5.1));
            if (plate > 0) { dc.PushOpacity(plate); dc.DrawImage(pickerAio, new Rect(0, 0, W, H)); dc.Pop(); }
            Point[] items = { new Point(982, 813), new Point(1075, 813), new Point(1155, 813), new Point(1247, 813) };
            double[] wide = { 98, 72, 60, 96 };
            double[] when = { 1.6, 2.3, 3.0, 3.7 };
            int hot = -1;
            for (int i = 0; i < 4; i++) if (t >= when[i]) hot = i;
            if (t > 4.2) hot = 1;
            Point cur = new Point(1400, 900);
            if (t > 1.0)
            {
                Point from = new Point(1400, 900);
                for (int i = 0; i < 4; i++) if (t >= when[i] - 0.45) { cur = LerpP(from, new Point(items[i].X + 8, items[i].Y + 6), InOut(P(t, when[i] - 0.45, when[i]))); from = new Point(items[i].X + 8, items[i].Y + 6); }
                if (t > 4.0) cur = LerpP(from, new Point(items[1].X + 8, items[1].Y + 6), InOut(P(t, 4.0, 4.4)));
            }
            if (hot > 0 && plate > 0) dc.DrawRoundedRectangle(B(Colors.White, 0.18 * plate), null, new Rect(items[hot].X - wide[hot] / 2, 797, wide[hot], 32), 16, 16);
            // "Vídeo" chosen: the recording starts on that same area.
            double rec = P(t, 5.0, 5.3);
            if (rec > 0)
            {
                Rect area = new Rect(600, 215, 1070, 565);
                dc.PushOpacity(rec);
                dc.DrawRectangle(null, new Pen(B(Colors.White, 0.55), 1.2), area);
                CornerMarks(dc, area, 22, 4);
                Rect bar = new Rect(area.X + area.Width / 2 - 130, area.Bottom + 22, 260, 52);
                Pill(dc, bar, B(Color.FromRgb(28, 28, 32)), new Pen(B(Colors.White, 0.1), 1));
                dc.DrawEllipse(B(Red, 0.55 + 0.45 * Math.Abs(Math.Cos(t * 3.2))), null, new Point(bar.X + 26, bar.Y + 26), 6.5, 6.5);
                Text(dc, "00:0" + (int)Math.Max(0, t - 5.3), 18, FontWeights.SemiBold, B(Fg), bar.X + 42, bar.Y + 13, -1, 1, mono);
                Capsule(dc, "Detener", bar.Right - 64, bar.Y + 26, 16, Red, Colors.White, 1, 18, 36);
                dc.Pop();
            }
            if (t < 5.0) Pointer(dc, cur, 1.6, t > 4.5 && t < 4.7 ? 1 : 0);
            PopScreen(dc, sr);
        }

        static void Pin(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Pin it above everything.", "F\u00EDjala encima de todo."),
                L("Keep a capture floating over every window. Wheel to zoom, Ctrl + wheel to fade it.", "Una captura flotando sobre todas las ventanas. Rueda para el zoom, Ctrl + rueda para la transparencia."));
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            PushScreen(dc, sr, 1, new Point());
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            BitmapSource code = DeskCrop(1270, 460, 380, 300);
            Rect card = new Rect(28, 1080 - 28 - 190, 240, 190);
            Rect pinned = new Rect(300, 260, 456, 360);
            double hover = P(t, 1.0, 1.2), fly = InOut(P(t, 1.9, 2.6));
            double zoom = 1 + 0.45 * InOut(P(t, 3.0, 4.0)), fade = 1 - 0.55 * InOut(P(t, 4.6, 5.6));
            Point pinBtn = new Point(card.Right - 22, card.Y + 22);
            if (fly < 1)
            {
                Rect r = Lerp(card, pinned, fly);
                Shadow(dc, r, 16, 1.0, 30, 12);
                dc.PushClip(new RectangleGeometry(r, 16, 16));
                dc.DrawImage(code, r);
                if (fly == 0 && hover > 0) dc.DrawRectangle(B(Colors.Black, 0.45 * hover), null, r);
                dc.Pop();
                if (fly == 0 && hover > 0) CardButtons(dc, r, hover);
            }
            else
            {
                Rect r = new Rect(pinned.X, pinned.Y, pinned.Width * zoom, pinned.Height * zoom);
                dc.PushOpacity(fade);
                Shadow(dc, r, 12, 1.2, 36, 16);
                dc.PushClip(new RectangleGeometry(r, 12, 12));
                dc.DrawImage(code, r);
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.2), 1.2), r, 12, 12);
                dc.Pop();
            }
            Point inPin = new Point(pinned.X + 300, pinned.Y + 200);
            Point cur = t < 1.8 ? LerpP(new Point(900, 700), pinBtn, InOut(P(t, 0.4, 1.4))) : LerpP(pinBtn, inPin, InOut(P(t, 2.2, 2.9)));
            Pointer(dc, cur, 1.6, t > 1.6 && t < 1.8 ? 1 : 0);
            if (t > 2.9 && t < 5.9)
            {
                Wheel(dc, new Point(cur.X + 70, cur.Y + 10), t);
                string hint = t < 4.3 ? L("Wheel: zoom", "Rueda: zoom") : L("Ctrl + wheel: opacity", "Ctrl + rueda: transparencia");
                Capsule(dc, hint, cur.X + 70, cur.Y + 78, 22, Color.FromArgb(230, 28, 28, 32), Fg, P(t, 2.9, 3.2) * (1 - P(t, 5.6, 5.9)), 20, 50);
            }
            PopScreen(dc, sr);
        }
    }
}

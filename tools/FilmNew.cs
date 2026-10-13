// Stackshot - Trailer scenes for the newest features: text search in the history, covering sensitive data, pause, clicks,
// keys and the microphone meter in recordings, the XML and Markdown viewers' new tools, and a wall of small details. They
// are built from pictures of the real windows (the gallery's, copied into the plates folder by tools\make-film.ps1) and from the
// real controls where they can be drawn live.
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
        static readonly Dictionary<string, BitmapSource> plateCache = new Dictionary<string, BitmapSource>();

        // A picture from the plates folder.
        static BitmapSource Pl(string file)
        {
            BitmapSource b;
            if (!plateCache.TryGetValue(file, out b))
            {
                string path = Path.Combine(plates, file + ".png");
                if (!File.Exists(path)) throw new FileNotFoundException("Missing plate " + file + " (run tools\\make-film.ps1 so the gallery pictures are copied).", path);
                b = Load(path);
                plateCache[file] = b;
            }
            return b;
        }

        // A picture of the gallery, dark theme.
        static BitmapSource Gal(string name) { return Pl("gal-" + name); }

        static Color PixelAt(BitmapSource s, int x, int y)
        {
            byte[] px = new byte[4];
            new FormatConvertedBitmap(new CroppedBitmap(s, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0).CopyPixels(px, 4, 0);
            return Color.FromRgb(px[2], px[1], px[0]);
        }

        static int[] Ints(string file)
        {
            string[] f = File.ReadAllText(Path.Combine(plates, file)).Trim().Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int[] r = new int[f.Length];
            for (int i = 0; i < f.Length; i++) r[i] = int.Parse(f[i], CultureInfo.InvariantCulture);
            return r;
        }

        // Crossfades through several pictures drawn into r: picture i takes over from picture i - 1 at at[i].
        static void Cycle(DrawingContext dc, BitmapSource[] imgs, double[] at, double t, Rect r)
        {
            for (int i = 0; i < imgs.Length; i++)
            {
                double k = i == 0 ? 1 : Smooth(P(t, at[i], at[i] + 0.4));
                if (k <= 0) continue;
                dc.PushOpacity(k);
                dc.DrawImage(imgs[i], r);
                dc.Pop();
            }
        }

        // A row of capsules centered on the screen, each fading in at its own time.
        static void ChipRow(DrawingContext dc, string[] labels, double[] when, double t, double cy, Color bg, Color fg, double size = 20)
        {
            double total = 0;
            double[] ws = new double[labels.Length];
            for (int i = 0; i < labels.Length; i++) { ws[i] = FT(labels[i], size, FontWeights.SemiBold, B(fg), body).Width + 44; total += ws[i] + 14; }
            double x = W / 2.0 - (total - 14) / 2;
            for (int i = 0; i < labels.Length; i++)
            {
                Capsule(dc, labels[i], x + ws[i] / 2, cy, size, bg, fg, OutCubic(P(t, when[i], when[i] + 0.4)), 22, size * 2.3);
                x += ws[i] + 14;
            }
        }

        // ================= History =================

        static void History(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Find it by what it said.", "Enc\u00FAentrala por lo que dec\u00EDa."),
                L("Search the text inside your captures. Stackshot reads them with Windows' own OCR, on your PC.", "Busca el texto dentro de tus capturas. Stackshot las lee con el OCR de Windows, en tu equipo."));
            BitmapSource a = Gal("04-historial"), b = Gal("06-historial-busqueda");
            double rise = OutCubic(P(t, 0.1, 0.9));
            Rect r = Fit(a, W / 2.0, 585 + (1 - rise) * 60, 1060);
            double k = r.Width / a.PixelWidth;
            double zoom = 1 + 0.035 * P(t, 0, dur);
            dc.PushOpacity(rise);
            dc.PushTransform(new ScaleTransform(zoom, zoom, W / 2.0, 560));
            Shadow(dc, r, 14, 1.2, 50, 26);
            dc.PushClip(new RectangleGeometry(r, 14, 14));
            dc.DrawImage(a, r);
            // Typing "factura": the field's own pixels, letter by letter, then the results.
            int n = (int)Math.Max(0, Math.Min(7, Math.Floor((t - 1.75) / 0.11) + 1));
            if (n >= 1)
            {
                Rect field = new Rect(r.X + 612 * k, r.Y + 289 * k, 1235 * k, 58 * k);
                dc.DrawRectangle(B(PixelAt(a, 1200, 335)), null, field);
                double end = 612 + 13 + n * 11.4;
                dc.PushClip(new RectangleGeometry(new Rect(field.X, field.Y, (end - 612) * k, field.Height)));
                dc.DrawImage(b, r);
                dc.Pop();
                dc.PushClip(new RectangleGeometry(new Rect(r.X + 1775 * k, field.Y, 80 * k, field.Height)));
                dc.DrawImage(b, r);
                dc.Pop();
            }
            double lower = Smooth(P(t, 2.85, 3.25));
            if (lower > 0)
            {
                double y0 = 360 * k;
                dc.PushOpacity(lower);
                dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y + y0, r.Width, r.Height - y0)));
                dc.DrawImage(b, r);
                dc.Pop();
                dc.Pop();
            }
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r, 14, 14);
            Point onField = new Point(r.X + 760 * k, r.Y + 322 * k), onHit = new Point(r.X + 760 * k, r.Y + 650 * k), start = new Point(r.Right - 140 * k, r.Bottom - 120 * k);
            Point cur = t < 1.6 ? LerpP(start, onField, InOut(P(t, 0.6, 1.5))) : LerpP(onField, onHit, InOut(P(t, 3.1, 3.9)));
            Pointer(dc, cur, 1.5, t > 1.5 && t < 1.65 ? 1 : 0);
            dc.Pop();
            dc.Pop();
            if (t > 3.5)
                Capsule(dc, L("Found in the text, not just the name", "Lo encuentra en el texto, no solo en el nombre"), r.X + 1400 * k, r.Y + 1010 * k, 24, Color.FromRgb(38, 38, 44), Fg, OutBack(P(t, 3.5, 4.0)), 24, 56);
            string[] chips = { L("Windows OCR", "OCR de Windows"), L("On your PC", "En tu equipo"), L("Captures, videos and GIFs", "Capturas, v\u00EDdeos y GIF") };
            ChipRow(dc, chips, new double[] { 4.3, 4.5, 4.7 }, t, 1022, Color.FromRgb(38, 38, 44), Fg);
        }

        // ================= Sensitive data =================

        static void Sensitive(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Hide it before you share it.", "T\u00E1palo antes de compartir."),
                L("One click finds IDs, IBANs, cards, e-mails and phone numbers, and covers them.", "Un clic encuentra DNI, IBAN, tarjetas, correos y tel\u00E9fonos, y los tapa."),
                L("Or let it happen by itself.", "O que pase solo."),
                L("Turn it on once and every capture is checked first. It all stays on your PC.", "Act\u00EDvalo una vez y cada captura se revisa antes. Todo se queda en tu equipo."), 4.3);
            BitmapSource inv = Pl(es ? "invoice-es" : "invoice-en");
            int[] bi = Ints("invoice-boxes.txt");
            Rect[] boxes = new Rect[bi.Length / 4];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = new Rect(bi[i * 4], bi[i * 4 + 1], bi[i * 4 + 2], bi[i * 4 + 3]);
            double[] cover = new double[boxes.Length];
            for (int i = 0; i < cover.Length; i++) cover[i] = P(t, 2.45 + i * 0.22, 3.15 + i * 0.22);

            // First half: the editor, with its own toolbar and hint strip.
            double edIn = OutCubic(P(t, 0, 0.8)), edOut = P(t, 4.0, 4.5);
            if (edOut < 1)
            {
                BitmapSource[] bars = { Pl("sensitive-bar-0"), Pl("sensitive-bar-1"), Pl("sensitive-bar-2") };
                int[] bs = Ints("sensitive-bar.txt");
                double ew = 1660, k = ew / bs[0];
                double barH = bs[1] * k, hintH = Pl("sensitive-hint-1").PixelHeight * k, canvasH = 600;
                Rect win = new Rect((W - ew) / 2, 236 + (1 - edIn) * 50, ew, barH + canvasH + hintH);
                int state = t < 1.75 ? 0 : t < 1.97 ? 1 : t < 3.5 ? 2 : 0;
                BitmapSource hintImg = t < 1.97 ? Pl("sensitive-hint-2") : t < 3.5 ? Pl("sensitive-hint-0") : Pl("sensitive-hint-1");
                double z = 1 - 0.04 * edOut;
                dc.PushOpacity(edIn * (1 - edOut));
                dc.PushTransform(new ScaleTransform(z, z, W / 2.0, 600));
                Shadow(dc, win, 12, 1.2, 60, 30);
                dc.PushClip(new RectangleGeometry(win, 12, 12));
                Rect canvas = new Rect(win.X, win.Y + barH, ew, canvasH);
                dc.DrawRectangle(B(Color.FromRgb(22, 22, 24)), null, canvas);
                double si = Math.Min((ew - 140) / 1280.0, (canvasH - 70) / 800.0);
                Rect img = new Rect(canvas.X + (ew - 1280 * si) / 2, canvas.Y + (canvasH - 800 * si) / 2, 1280 * si, 800 * si);
                Shadow(dc, img, 3, 0.7, 26, 10);
                dc.DrawImage(inv, img);
                double kk = img.Width / inv.PixelWidth;
                for (int i = 0; i < boxes.Length; i++)
                {
                    double c = cover[i];
                    if (c <= 0) continue;
                    Rect b = new Rect(img.X + boxes[i].X * kk, img.Y + boxes[i].Y * kk, boxes[i].Width * kk, boxes[i].Height * kk);
                    double o = P(c, 0, 0.3), f = OutCubic(P(c, 0.3, 1));
                    if (f < 1)
                    {
                        Rect g = b;
                        g.Inflate(5 * o, 4 * o);
                        dc.DrawRoundedRectangle(null, new Pen(B(Blue, 1 - f), 2.5), g, 4, 4);
                    }
                    dc.DrawRectangle(B(Color.FromRgb(22, 22, 26)), null, new Rect(b.X, b.Y, b.Width * f, b.Height));
                }
                dc.DrawImage(bars[state], new Rect(win.X, win.Y, ew, barH));
                dc.DrawImage(hintImg, new Rect(win.X, win.Bottom - hintH, ew, hintH));
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.14), 1), win, 12, 12);
                Point button = new Point(win.X + (bs[2] + bs[4] * 0.5) * k, win.Y + (bs[3] + bs[5] * 0.5) * k);
                Point first = new Point(img.X + (boxes[0].X + 40) * kk, img.Y + (boxes[0].Y + 14) * kk);
                Point cur = t < 1.8 ? LerpP(new Point(1560, 880), button, InOut(P(t, 0.8, 1.7))) : LerpP(button, first, InOut(P(t, 2.1, 2.9)));
                Pointer(dc, cur, 1.5, t > 1.72 && t < 1.92 ? 1 : 0);
                dc.Pop();
                dc.Pop();
            }

            // Second half: the setting that does it on every capture, and a capture being checked.
            double s2 = OutCubic(P(t, 4.5, 5.2));
            if (s2 > 0)
            {
                BitmapSource set = Gal("13-general-al-capturar");
                Rect src = new Rect(553, 1000, 1302, 146);
                double ks = 1100 / src.Width;
                Rect card = new Rect((W - 1100) / 2.0, 290 + (1 - s2) * 40, 1100, src.Height * ks);
                dc.PushOpacity(s2);
                Shadow(dc, card, 18, 0.7, 30, 12);
                dc.PushClip(new RectangleGeometry(card, 16, 16));
                dc.PushTransform(new MatrixTransform(ks, 0, 0, ks, card.X - src.X * ks, card.Y - src.Y * ks));
                dc.DrawImage(set, new Rect(0, 0, set.PixelWidth, set.PixelHeight));
                double on = Smooth(P(t, 5.55, 5.8));
                if (on > 0)
                {
                    dc.PushOpacity(on);
                    dc.PushClip(new RectangleGeometry(new Rect(1738, 1040, 98, 62)));
                    dc.PushTransform(new TranslateTransform(0, 1048 - 468));
                    dc.DrawImage(set, new Rect(0, 0, set.PixelWidth, set.PixelHeight));
                    dc.Pop();
                    dc.Pop();
                    dc.Pop();
                }
                dc.Pop();
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), card, 16, 16);
                Point sw = new Point(card.X + (1790 - src.X) * ks, card.Y + (1072 - src.Y) * ks);
                Point cur = LerpP(new Point(sw.X + 220, sw.Y + 260), sw, InOut(P(t, 4.9, 5.5)));
                if (t > 5.9) cur = LerpP(sw, new Point(sw.X + 120, sw.Y + 330), InOut(P(t, 5.9, 6.5)));
                if (t < 6.7) Pointer(dc, cur, 1.5, t > 5.5 && t < 5.65 ? 1 : 0);
                dc.Pop();

                // The capture that gets checked before it shows: a card with the "Revisando datos..." pill, then covered.
                double ck = Spring(P(t, 5.7, 6.5) * 1.6);
                if (ck > 0)
                {
                    Rect tc = new Rect(W / 2.0 - 300, 460, 600, 600 * 800 / 1280.0);
                    double check = 1 - Smooth(P(t, 6.3, 6.6));
                    double[] done = new double[cover.Length];
                    for (int i = 0; i < done.Length; i++) done[i] = P(t, 5.9 + i * 0.08, 6.5 + i * 0.08);
                    dc.PushOpacity(Clamp(ck * 1.6));
                    dc.PushTransform(new ScaleTransform(Lerp(0.88, 1, ck), Lerp(0.88, 1, ck), tc.X + tc.Width / 2, tc.Y + tc.Height / 2));
                    Shadow(dc, tc, 16, 0.9, 30, 12);
                    dc.PushClip(new RectangleGeometry(tc, 16, 16));
                    dc.DrawImage(inv, tc);
                    double kc = tc.Width / inv.PixelWidth;
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        double f = OutCubic(done[i]);
                        Rect b = new Rect(tc.X + boxes[i].X * kc, tc.Y + boxes[i].Y * kc, boxes[i].Width * kc, boxes[i].Height * kc);
                        dc.DrawRectangle(B(Color.FromRgb(22, 22, 26)), null, new Rect(b.X - 1, b.Y - 0.5, b.Width * f + 2 * f, b.Height + 1));
                    }
                    dc.Pop();
                    dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.2), 1.5), tc, 16, 16);
                    if (check > 0)
                    {
                        string say = "Revisando datos\u2026";
                        FormattedText ft = FT(say, 22, FontWeights.SemiBold, B(Colors.White), body);
                        Rect pill = new Rect(tc.X + 18, tc.Bottom - 18 - 44, ft.Width + 74, 44);
                        dc.PushOpacity(check);
                        Pill(dc, pill, B(Color.FromArgb(220, 34, 34, 38)));
                        Pen sh = new Pen(B(Colors.White), 2.2);
                        sh.LineJoin = PenLineJoin.Round;
                        StreamGeometry shield = new StreamGeometry();
                        using (StreamGeometryContext g = shield.Open())
                        {
                            Point c = new Point(pill.X + 28, pill.Y + 22);
                            g.BeginFigure(new Point(c.X, c.Y - 11), false, true);
                            g.LineTo(new Point(c.X + 9, c.Y - 7), true, true);
                            g.LineTo(new Point(c.X + 9, c.Y + 2), true, true);
                            g.LineTo(new Point(c.X, c.Y + 11), true, true);
                            g.LineTo(new Point(c.X - 9, c.Y + 2), true, true);
                            g.LineTo(new Point(c.X - 9, c.Y - 7), true, true);
                        }
                        dc.DrawGeometry(null, sh, shield);
                        dc.DrawText(ft, new Point(pill.X + 50, pill.Y + (pill.Height - ft.Height) / 2));
                        dc.Pop();
                    }
                    dc.Pop();
                    dc.Pop();
                }
                string[] kinds = es ? new string[] { "DNI", "NIE", "Seguridad Social", "IBAN", "Tarjetas", "Correos", "Tel\u00E9fonos" }
                                    : new string[] { "DNI", "NIE", "Social Security", "IBAN", "Cards", "E-mails", "Phones" };
                double[] when = new double[kinds.Length];
                for (int i = 0; i < when.Length; i++) when[i] = 5.9 + i * 0.12;
                ChipRow(dc, kinds, when, t, 925, Color.FromRgb(38, 38, 44), Fg, 18);
            }
        }

        // ================= Recording: pause, clicks, keys, microphone =================

        // A speaking voice, in dB: the loudness over time, and the loudest peak held for a moment.
        static double Voice(double u)
        {
            double env = Math.Abs(Math.Sin(u * 2.3) * Math.Sin(u * 0.9 + 1.1));
            double syl = 0.5 + 0.5 * Math.Sin(u * 11.0 + Math.Sin(u * 3.0) * 2.0);
            return -46 + 30 * env * (0.45 + 0.55 * syl);
        }

        static void MeterAt(DrawingContext dc, Rect r, double u)
        {
            double rms = Voice(u), peak = rms + 5 + 2 * Math.Abs(Math.Sin(u * 17)), hold = -60;
            for (int j = 0; j < 40; j++)
            {
                double v = Voice(u - j * 0.04) + 5 - 14 * (j * 0.04);
                if (v > hold) hold = v;
            }
            MicBar bar = new MicBar();
            bar.Width = 176;
            bar.Height = 8;
            bar.Show(Math.Min(-4, peak), rms, Math.Min(-4, hold), false);
            bar.Measure(new Size(176, 8));
            bar.Arrange(new Rect(0, 0, 176, 8));
            bar.UpdateLayout();
            dc.DrawRectangle(new VisualBrush(bar), null, r);
        }

        // The click ring the recorder lays over the frames: left blue, right orange, growing and fading.
        static void ClickRing(DrawingContext dc, Point p, double age, bool right, double scale)
        {
            double life = 0.55;
            if (age < 0 || age >= life) return;
            double f = age / life, k = 1 - f, grow = 1 - k * k * k;
            double r = 6 * scale + (22 * scale - 6 * scale) * grow, stroke = (3 - 2 * f) * scale;
            Color c = right ? Orange : Blue;
            dc.DrawEllipse(B(c, 0.28 * k), null, p, r, r);
            dc.DrawEllipse(null, new Pen(B(c, 0.95 * (1 - f * f)), stroke), p, r, r);
        }

        // The shortcut caption: a dark capsule with white text, at the bottom center of the recorded area.
        static void KeyCaption(DrawingContext dc, string text, Rect area, double a, double scale)
        {
            if (a <= 0) return;
            double px = 17 * scale;
            FormattedText ft = FT(text, px, FontWeights.SemiBold, B(Colors.White), display);
            double w = ft.Width + 1.6 * px, h = px * 1.9;
            Rect r = new Rect(area.X + (area.Width - w) / 2, area.Bottom - 22 * scale - h, w, h);
            dc.PushOpacity(Clamp(a));
            dc.DrawRoundedRectangle(B(Color.FromArgb(224, 22, 22, 24)), new Pen(B(Colors.White, 0.18), Math.Max(1, scale)), r, h / 2, h / 2);
            dc.DrawText(ft, new Point(r.X + (w - ft.Width) / 2, r.Y + (h - ft.Height) / 2));
            dc.Pop();
        }

        static void RecordPlus(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Record like a pro.", "Graba como un profesional."),
                L("Pause and resume, show clicks and shortcuts, and test your mic before you start.", "Pausa y reanuda, muestra clics y teclas, y prueba el micr\u00F3fono antes de empezar."));
            double rise = OutCubic(P(t, 0, 0.8));

            // The options before recording, with the live input level.
            double aOut = Smooth(P(t, 3.1, 3.6));
            if (aOut < 1)
            {
                BitmapSource o = Gal("42-grabar-opciones");
                Rect r = Fit(o, W / 2.0, 565 + (1 - rise) * 60 - aOut * 24, 760);
                double k = r.Width / o.PixelWidth;
                dc.PushOpacity(rise * (1 - aOut));
                Shadow(dc, r, 14, 1.2, 50, 26);
                dc.PushClip(new RectangleGeometry(r, 14, 14));
                dc.DrawImage(o, r);
                // The meter row: its background from just above, then the real meter bar over it.
                Rect row = new Rect(r.X + 700 * k, r.Y + 506 * k, 376 * k, 36 * k);
                dc.DrawImage(new CroppedBitmap(o, new Int32Rect(700, 497, 376, 3)), row);
                MeterAt(dc, new Rect(r.X + 712 * k, r.Y + 514 * k, 352 * k, 16 * k), t - 0.4);
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r, 14, 14);
                Point mic = new Point(r.X + 560 * k, r.Y + 522 * k), rec = new Point(r.X + 990 * k, r.Y + 776 * k);
                Point cur = t < 1.7 ? LerpP(new Point(r.Right + 40, r.Y + 330 * k), mic, InOut(P(t, 0.5, 1.4))) : LerpP(mic, rec, InOut(P(t, 1.9, 2.8)));
                if (t > 2.8 && t < 3.0) dc.DrawRoundedRectangle(B(Colors.Black, 0.2), null, new Rect(r.X + 892 * k, r.Y + 742 * k, 172 * k, 64 * k), 12 * k, 12 * k);
                Pointer(dc, cur, 1.5, t > 2.8 && t < 3.0 ? 1 : 0);
                dc.Pop();
            }

            // The recording: clicks, shortcuts and a pause.
            double bIn = OutCubic(P(t, 3.1, 3.9));
            if (bIn > 0)
            {
                Rect sr = ScreenR;
                sr.Offset(0, (1 - bIn) * 40 - 8);
                dc.PushOpacity(bIn);
                PushScreen(dc, sr, 1 + 0.22 * InOut(P(t, 3.4, 4.2)), new Point(930, 500));
                dc.DrawImage(desk, new Rect(0, 0, W, H));
                Rect area = new Rect(520, 200, 820, 460);
                bool paused = t > 5.95;
                dc.DrawRectangle(null, new Pen(B(Colors.White, 0.55), 1.2), area);
                CornerMarks(dc, area, 22, 4);
                // Clicks: (time, position, right button).
                double[] ct = { 4.1, 4.55, 5.0 };
                Point[] cp = { new Point(700, 330), new Point(980, 460), new Point(1150, 330) };
                bool[] cr = { false, false, true };
                for (int i = 0; i < ct.Length; i++) ClickRing(dc, cp[i], t - ct[i], cr[i], 1.6);
                KeyCaption(dc, t < 5.3 ? "Ctrl + C" : "Ctrl + V", area, (t > 4.8 && t < 5.9 ? Math.Min(1, (t - 4.8) / 0.12) * (1 - P(t, 5.65, 5.9)) : 0), 1.6);
                // The recording bar (its own picture), with a running time and the pause button.
                BitmapSource barRec = Gal("bar-recording"), barPaused = Gal("bar-paused");
                double bw = 470, bk = bw / barRec.PixelWidth;
                Rect bar = new Rect(area.X + area.Width / 2 - bw / 2, area.Bottom + 4, bw, barRec.PixelHeight * bk);
                double pk = Smooth(P(t, 5.95, 6.2));
                dc.DrawImage(barRec, bar);
                int secs = (int)Math.Max(0, Math.Min(Math.Floor(t - 3.7), 2));
                Color face = PixelAt(barRec, 205, 70);
                dc.DrawRectangle(B(face), null, new Rect(bar.X + 104 * bk, bar.Y + 70 * bk, 96 * bk, 44 * bk));
                FormattedText tm = FT("00:0" + secs, 31 * bk, FontWeights.SemiBold, B(Fg), display);
                dc.DrawText(tm, new Point(bar.X + 112 * bk, bar.Y + 92 * bk - tm.Height / 2));
                if (pk > 0)
                {
                    dc.PushOpacity(pk);
                    dc.DrawImage(barPaused, bar);
                    dc.Pop();
                }
                if (paused) dc.DrawRectangle(B(Colors.Black, 0.22 * pk), null, area);
                Point pauseBtn = new Point(bar.X + 255 * bk, bar.Y + 92 * bk), away = new Point(1250, 560);
                Point wander = new Point(820 + 120 * Math.Sin(t * 1.3), 420 + 60 * Math.Cos(t * 1.7));
                Point pc;
                if (t < 4.1) pc = LerpP(new Point(900, 520), cp[0], InOut(P(t, 3.7, 4.1)));
                else if (t < 4.55) pc = LerpP(cp[0], cp[1], InOut(P(t, 4.15, 4.5)));
                else if (t < 5.0) pc = LerpP(cp[1], cp[2], InOut(P(t, 4.6, 4.95)));
                else if (t < 5.8) pc = LerpP(cp[2], pauseBtn, InOut(P(t, 5.1, 5.8)));
                else pc = LerpP(pauseBtn, away, InOut(P(t, 6.4, 7.0)));
                Pointer(dc, pc, 1.6, t > 5.82 && t < 5.97 ? 1 : 0);
                PopScreen(dc, sr);
                if (paused)
                    Capsule(dc, L("Paused: nothing is recorded", "En pausa: no se graba"), W / 2.0, sr.Y + 80, 22, Color.FromArgb(235, 255, 159, 10), Color.FromRgb(30, 22, 6), pk, 24, 52);
                dc.Pop();
            }
            string[] chips = { L("Mic level, live", "Nivel del micr\u00F3fono, en vivo"), L("Clicks", "Clics"), L("Shortcuts, never typing", "Atajos, nunca lo que escribes"), L("Pause and resume", "Pausa y reanuda") };
            ChipRow(dc, chips, new double[] { 1.0, 4.1, 4.8, 5.95 }, t, 1022, Color.FromRgb(38, 38, 44), Fg);
        }

        // ================= XML and Markdown =================

        static void DocsPro(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("XML and Markdown, upgraded.", "XML y Markdown, a otro nivel."),
                L("Compare two XMLs, read SOAP at a glance, copy as JSON. Export Markdown to PDF.", "Compara dos XML, lee SOAP de un vistazo y c\u00F3pialos como JSON. Exporta Markdown a PDF."));
            BitmapSource[] xmls = { Gal("27-xml-soap-ok"), Gal("29-xml-comparar"), Gal("31-xml-menu-nodo") };
            BitmapSource[] mds = { Gal("22-markdown-indice"), Gal("24-markdown-tabla-pegada"), Gal("25-markdown-mas-opciones") };
            double[] atX = { 0, 2.5, 4.8 }, atM = { 0, 2.9, 5.2 };
            double a = OutExpo(P(t, 0.2, 1.1)), b = OutExpo(P(t, 0.5, 1.4));
            double ww = 880, wh = ww * xmls[0].PixelHeight / xmls[0].PixelWidth;
            Rect r1 = new Rect(50 - (1 - a) * 200, 262, ww, wh), r2 = new Rect(W - 50 - ww + (1 - b) * 200, 262, ww, wh);
            dc.PushOpacity(a);
            Shadow(dc, r1, 14, 1.2, 50, 26);
            dc.PushClip(new RectangleGeometry(r1, 14, 14));
            Cycle(dc, xmls, atX, t, r1);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r1, 14, 14);
            dc.Pop();
            dc.PushOpacity(b);
            Shadow(dc, r2, 14, 1.2, 50, 26);
            dc.PushClip(new RectangleGeometry(r2, 14, 14));
            Cycle(dc, mds, atM, t, r2);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r2, 14, 14);
            // The PDF that Exportar PDF writes, laid over the document.
            double pk = Spring(P(t, 6.0, 6.8) * 1.6);
            if (pk > 0)
            {
                BitmapSource pdf = Gal("26-markdown-pdf");
                Rect pr = Fit(pdf, r2.Right - 430, r2.Y + 265, 330);
                dc.PushOpacity(Clamp(pk * 1.6));
                dc.PushTransform(new RotateTransform(-3 * (1 - pk * 0.5), pr.X + pr.Width / 2, pr.Y + pr.Height / 2));
                dc.PushTransform(new ScaleTransform(Lerp(0.85, 1, pk), Lerp(0.85, 1, pk), pr.X + pr.Width / 2, pr.Y + pr.Height / 2));
                dc.DrawImage(pdf, pr);
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }
            dc.Pop();
            string[] lx = { L("SOAP at a glance", "SOAP de un vistazo"), L("Compare two XMLs", "Compara dos XML"), L("Attachments and JSON", "Adjuntos y JSON") };
            string[] lm = { L("Outline panel", "Panel de \u00CDndice"), L("Paste Excel tables", "Pega tablas de Excel"), L("Export to PDF", "Exporta a PDF") };
            for (int pass = 0; pass < 2; pass++)
            {
                string[] lab = pass == 0 ? lx : lm;
                double[] at = pass == 0 ? atX : atM;
                double cx = pass == 0 ? r1.X + ww / 2 : r2.X + ww / 2;
                for (int i = 0; i < 3; i++)
                {
                    double from = i == 0 ? 0.9 : at[i] + 0.2, to = i == 2 ? 99 : at[i + 1] - 0.05;
                    double k = OutCubic(P(t, from, from + 0.35)) * (1 - OutCubic(P(t, to, to + 0.3)));
                    if (k > 0) Capsule(dc, lab[i], cx, r1.Bottom + 56, 26, pass == 0 ? Color.FromRgb(30, 52, 80) : Color.FromRgb(52, 40, 80), pass == 0 ? Color.FromRgb(150, 200, 255) : Color.FromRgb(210, 180, 255), k, 26, 58);
                }
            }
        }

        // ================= A wall of small details =================

        static void More(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("And so much more.", "Y mucho m\u00E1s."), L("The small things, all in the same 2.2 MB.", "Los peque\u00F1os detalles, todos en los mismos 2,2 MB."));
            string[] left = es ? new string[] { "El puntero en las capturas", "Nombre de archivo a tu manera", "Subcarpetas por mes", "Repetir la \u00FAltima \u00E1rea", "Tema de contraste alto" }
                               : new string[] { "Pointer in captures", "File names your way", "Folders by month", "Repeat the last area", "High-contrast themes" };
            string[] right = es ? new string[] { "Buscar entre los ajustes", "L\u00EDnea de comandos: --capture", "Ajustes para TI", "Avisos propios", "Listo para lectores de pantalla" }
                                : new string[] { "Search every setting", "Command line: --capture", "Settings for IT", "Its own notices", "Screen-reader ready" };
            BitmapSource[] pics = { Gal("14-general-capturas-guardadas"), Gal("19b-buscar-micro"), Gal("11-atajos") };
            double[] at = { 0, 1.9, 3.8 };
            // chip index highlighted with each picture (left, right)
            int[] hotL = { 1, -1, 3 }, hotR = { -1, 0, -1 };
            double a = OutExpo(P(t, 0.2, 1.0));
            double ww = 880, wh = ww * pics[0].PixelHeight / pics[0].PixelWidth;
            Rect r = new Rect((W - ww) / 2, 330 + (1 - a) * 50, ww, wh);
            dc.PushOpacity(a);
            Shadow(dc, r, 14, 1.2, 50, 26);
            dc.PushClip(new RectangleGeometry(r, 14, 14));
            Cycle(dc, pics, at, t, r);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r, 14, 14);
            dc.Pop();
            int cur = t < at[1] ? 0 : t < at[2] ? 1 : 2;
            Color[] tint = { Blue, Purple, Orange, Green, Teal };
            for (int side = 0; side < 2; side++)
            {
                string[] lab = side == 0 ? left : right;
                for (int i = 0; i < lab.Length; i++)
                {
                    double k = OutBack(P(t, 0.5 + i * 0.16 + side * 0.08, 1.0 + i * 0.16 + side * 0.08));
                    if (k <= 0) continue;
                    bool hot = (side == 0 ? hotL[cur] : hotR[cur]) == i;
                    FormattedText ft = FT(lab[i], 25, FontWeights.SemiBold, B(hot ? Colors.White : Fg), body);
                    double cw = ft.Width + 70, ch = 64;
                    double x = side == 0 ? r.X - 36 - cw : r.Right + 36;
                    double y = 372 + i * 104;
                    Rect c = new Rect(x, y, cw, ch);
                    double slide = (1 - Clamp(k)) * (side == 0 ? -60 : 60);
                    dc.PushOpacity(Clamp(k));
                    dc.PushTransform(new TranslateTransform(slide, 0));
                    if (hot) Glow(dc, c.X + cw / 2, c.Y + ch / 2, cw * 1.5, 150, tint[i], 0.35);
                    Pill(dc, c, B(hot ? Mix(tint[i], Colors.Black, 0.35) : Color.FromRgb(36, 36, 42)), new Pen(B(Colors.White, hot ? 0.3 : 0.08), 1.2));
                    dc.DrawEllipse(B(tint[i]), null, new Point(c.X + 30, c.Y + ch / 2), 8, 8);
                    dc.DrawText(ft, new Point(c.X + 50, c.Y + (ch - ft.Height) / 2));
                    dc.Pop();
                    dc.Pop();
                }
            }
        }
    }
}

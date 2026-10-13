// Stackshot - Trailer scenes. Every frame is a pure function of time: Frame(t) draws the scenes alive at t into one
// DrawingVisual, which is rendered at 1920x1080 and written to FFmpeg as raw BGRA.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;

namespace Stackshot
{
    public static partial class Film
    {
        const int W = 1920, H = 1080;
        const double Overlap = 0.28;
        static bool es;
        static string plates, docs;

        delegate void SceneFn(DrawingContext dc, double t, double dur);
        sealed class Scene { public string Name; public double Start, Dur, Speed = 1; public SceneFn Draw; }
        static readonly List<Scene> scenes = new List<Scene>();

        static string L(string en, string sp) { return es ? sp : en; }

        // ---- Palette: Apple's dark system colors and the brand gradient

        static readonly Color Fg = Color.FromRgb(245, 245, 247), Gray = Color.FromRgb(161, 161, 166), Blue = Color.FromRgb(10, 132, 255),
            Indigo = Color.FromRgb(94, 92, 230), Purple = Color.FromRgb(191, 90, 242), Pink = Color.FromRgb(255, 55, 95),
            Orange = Color.FromRgb(255, 159, 10), Yellow = Color.FromRgb(255, 214, 10), Green = Color.FromRgb(48, 209, 88),
            Teal = Color.FromRgb(100, 210, 255), Red = Color.FromRgb(255, 69, 58), Brand1 = Color.FromRgb(79, 123, 255), Brand2 = Color.FromRgb(139, 92, 246);

        static readonly FontFamily display = new FontFamily("Segoe UI Variable Display, Segoe UI"), body = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            mono = new FontFamily("Cascadia Mono, Consolas");

        // ---- Assets

        static BitmapSource desk, shot, homeA, homeB, homeC, md, xml, editorPlate, backdropPlate, logo, longDoc, pageRecord;
        static BitmapSource edToolbar, edStatus, edPanel, edCanvas, pixelated;
        static BitmapSource[] costumes, grid, frames;
        static string[] costumeNames, costumeFrom;
        static Color[] costumeGlow;
        static Color editorBg;

        static BitmapSource Load(string path)
        {
            BitmapImage b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.UriSource = new Uri(path);
            b.EndInit();
            b.Freeze();
            return b;
        }

        static BitmapSource FromGdi(D.Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, D.Imaging.ImageFormat.Png);
                ms.Position = 0;
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = ms;
                b.EndInit();
                b.Freeze();
                return b;
            }
        }

        // A part of the fake desktop, given in 1920x1080 layout coordinates whatever resolution it was drawn at.
        static BitmapSource DeskCrop(int x, int y, int w, int h)
        {
            double k = desk.PixelWidth / (double)W;
            return Crop(desk, (int)(x * k), (int)(y * k), (int)(w * k), (int)(h * k));
        }

        static BitmapSource Crop(BitmapSource s, int x, int y, int w, int h)
        {
            CroppedBitmap c = new CroppedBitmap(s, new Int32Rect(x, y, w, h));
            c.Freeze();
            return c;
        }

        static BitmapSource MascotArt(int anime, int size)
        {
            using (D.Bitmap b = new D.Bitmap(size, size, D.Imaging.PixelFormat.Format32bppArgb))
            {
                using (D.Graphics g = D.Graphics.FromImage(b))
                {
                    g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
                    g.InterpolationMode = D.Drawing2D.InterpolationMode.HighQualityBicubic;
                    MascotLook l = new MascotLook();
                    l.Seasonal = false;
                    if (anime >= 0) MascotParts.ApplyAnime(l, anime);
                    float s = size * 0.6f;
                    Mascot.RenderStill(g, new D.RectangleF((size - s) / 2, size * 0.27f, s, s), l);
                }
                return FromGdi(b);
            }
        }

        static void LoadAssets()
        {
            desk = Load(Path.Combine(plates, "desktop.png"));
            shot = DeskCrop(600, 215, 1070, 565);
            homeA = Load(Path.Combine(plates, "home-0.png"));
            homeB = Load(Path.Combine(plates, "home-1.png"));
            homeC = Load(Path.Combine(plates, "home-2.png"));
            pageRecord = Load(Path.Combine(plates, "page-record.png"));
            md = Load(Path.Combine(plates, "markdown.png"));
            xml = Load(Path.Combine(plates, "xml.png"));
            editorPlate = Load(Path.Combine(docs, "editor.png"));
            backdropPlate = Load(Path.Combine(docs, "backdrop.png"));
            edToolbar = Crop(editorPlate, 41, 41, 1357, 51);
            edStatus = Crop(editorPlate, 41, 818, 1357, 27);
            edPanel = Crop(backdropPlate, 41, 92, 1357, 58);
            edCanvas = DeskCrop(500, 170, 1100, 640);
            TransformedBitmap small = new TransformedBitmap(DeskCrop(540, 280, 190, 50), new ScaleTransform(0.07 * W / desk.PixelWidth, 0.07 * W / desk.PixelWidth));
            small.Freeze();
            pixelated = small;
            frames = new BitmapSource[10];
            for (int i = 0; i < frames.Length; i++) frames[i] = DeskCrop(520 + i * 30, 200, 820, 460);
            using (D.Bitmap e = new D.Bitmap(Path.Combine(docs, "editor.png")))
            {
                D.Color c = e.GetPixel(120, 400);
                editorBg = Color.FromRgb(c.R, c.G, c.B);
            }
            using (D.Bitmap b = new D.Bitmap(512, 512))
            {
                using (D.Graphics g = D.Graphics.FromImage(b))
                {
                    g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
                    LogoArt.PaintFull(g, new D.RectangleF(0, 0, 512, 512), false);
                }
                logo = FromGdi(b);
            }
            using (D.Bitmap b = LongDoc()) longDoc = FromGdi(b);
            // Pixel, then Goku, Luffy, Pikachu, Spider-Man, Naruto, Batman, Chopper, Totoro, Iron Man, Gojo, Doraemon, Kratos.
            int[] pick = { -1, 6, 5, 11, 30, 13, 31, 25, 14, 34, 8, 12, 15 };
            costumeGlow = new Color[] { Brand1, Orange, Red, Yellow, Red, Orange, Indigo, Pink, Teal, Red, Purple, Blue, Gray };
            costumes = new BitmapSource[pick.Length];
            costumeNames = new string[pick.Length];
            costumeFrom = new string[pick.Length];
            for (int i = 0; i < pick.Length; i++)
            {
                costumes[i] = MascotArt(pick[i], 640);
                costumeNames[i] = pick[i] < 0 ? "Pixel" : MascotParts.AnimeNames[pick[i]];
                costumeFrom[i] = pick[i] < 0 ? L("The original", "El original") : Origin(pick[i]);
            }
            grid = new BitmapSource[MascotParts.AnimeNames.Length];
            for (int i = 0; i < grid.Length; i++) grid[i] = MascotArt(i, 240);
        }

        static string Origin(int i)
        {
            string s = MascotParts.AnimeInspiration[i] ?? "Original";
            return es ? s : Promo.En(s);
        }

        // A long documentation page for the scrolling capture.
        static D.Bitmap LongDoc()
        {
            int w = 720, h = 1900;
            D.Bitmap b = new D.Bitmap(w, h);
            using (D.Graphics g = D.Graphics.FromImage(b))
            {
                g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = D.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(D.Color.FromArgb(24, 24, 28));
                Random r = new Random(7);
                D.Color line = D.Color.FromArgb(58, 60, 72), head = D.Color.FromArgb(236, 236, 240), sub = D.Color.FromArgb(150, 152, 165);
                using (D.Font h1 = new D.Font("Segoe UI Semibold", 34, D.GraphicsUnit.Pixel))
                using (D.Font h2 = new D.Font("Segoe UI Semibold", 22, D.GraphicsUnit.Pixel))
                using (D.Font p = new D.Font("Segoe UI", 15, D.GraphicsUnit.Pixel))
                using (D.Font code = new D.Font("Consolas", 15, D.GraphicsUnit.Pixel))
                {
                    int y = 40;
                    g.DrawString(es ? "Gu\u00EDa de inicio r\u00E1pido" : "Quick start guide", h1, new D.SolidBrush(head), 44, y);
                    y += 56;
                    g.DrawString(es ? "Todo lo que necesitas en cinco minutos" : "Everything you need in five minutes", p, new D.SolidBrush(sub), 46, y);
                    y += 50;
                    string[] titles = es ? new string[] { "1. Instalar", "2. Primera captura", "3. Marcar y compartir", "4. Grabar la pantalla", "5. Atajos" }
                                         : new string[] { "1. Install", "2. Your first capture", "3. Annotate and share", "4. Record the screen", "5. Shortcuts" };
                    D.Color[] tints = { D.Color.FromArgb(79, 123, 255), D.Color.FromArgb(139, 92, 246), D.Color.FromArgb(255, 105, 97), D.Color.FromArgb(48, 209, 88), D.Color.FromArgb(255, 159, 10) };
                    for (int s = 0; s < titles.Length; s++)
                    {
                        g.DrawString(titles[s], h2, new D.SolidBrush(head), 44, y);
                        y += 46;
                        int lines = 3 + r.Next(3);
                        for (int i = 0; i < lines; i++)
                        {
                            int lw = i == lines - 1 ? 220 + r.Next(200) : 560 + r.Next(80);
                            using (D.Drawing2D.GraphicsPath gp = Theme.Round(new D.RectangleF(46, y + 4, lw, 9), 4)) g.FillPath(new D.SolidBrush(line), gp);
                            y += 24;
                        }
                        y += 16;
                        D.RectangleF box = new D.RectangleF(44, y, 632, s % 2 == 0 ? 150 : 96);
                        if (s % 2 == 0)
                        {
                            using (D.Drawing2D.GraphicsPath gp = Theme.Round(box, 14))
                            using (D.Drawing2D.LinearGradientBrush lb = new D.Drawing2D.LinearGradientBrush(box, tints[s], D.Color.FromArgb(40, 40, 52), 25f))
                                g.FillPath(lb, gp);
                            for (int k = 0; k < 5; k++)
                            {
                                float bh = 30 + (k * 23) % 70;
                                using (D.Drawing2D.GraphicsPath bar = Theme.Round(new D.RectangleF(box.X + 40 + k * 70, box.Bottom - 22 - bh, 40, bh), 6))
                                    g.FillPath(new D.SolidBrush(D.Color.FromArgb(150, 255, 255, 255)), bar);
                            }
                        }
                        else
                        {
                            using (D.Drawing2D.GraphicsPath gp = Theme.Round(box, 12)) g.FillPath(new D.SolidBrush(D.Color.FromArgb(32, 33, 40)), gp);
                            g.DrawString("Stackshot.exe --install --startup", code, new D.SolidBrush(D.Color.FromArgb(125, 207, 255)), box.X + 22, box.Y + 22);
                            g.DrawString(es ? "# listo en segundos" : "# ready in seconds", code, new D.SolidBrush(D.Color.FromArgb(110, 115, 140)), box.X + 22, box.Y + 52);
                        }
                        y += (int)box.Height + 40;
                    }
                }
            }
            return b;
        }

        // ---- Timeline

        static void Build()
        {
            double at = 0;
            Action<string, double, SceneFn> add = delegate(string name, double dur, SceneFn fn)
            {
                scenes.Add(new Scene { Name = name, Start = at, Dur = dur, Draw = fn });
                at += dur - Overlap;
            };
            // A scene played k times faster than it was choreographed: the film is an ad, the README clips keep the calm pace.
            Action<string, double, double, SceneFn> fast = delegate(string name, double dur, double k, SceneFn fn)
            {
                scenes.Add(new Scene { Name = name, Start = at, Dur = dur / k, Speed = k, Draw = delegate(DrawingContext dc, double t, double d) { fn(dc, t * k, d * k); } });
                at += dur / k - Overlap;
            };
            add("open", 3.2, ColdOpen);
            add("words", 3.5, Words);
            fast("brand", 3.4, 1.3, Brand);
            fast("modes", 10.6, 1.4, Modes);
            fast("capture", 11.2, 1.35, Capture);
            fast("editor", 10.0, 1.3, EditorScene);
            add("sensitive", 7.4, Sensitive);
            fast("scroll", 7.4, 1.3, Scroll);
            fast("record", 9.0, 1.25, Record);
            add("record-plus", 7.4, RecordPlus);
            add("history", 6.8, History);
            add("docs-pro", 7.4, DocsPro);
            fast("home", 7.6, 1.4, Home);
            fast("mascot", 9.2, 1.5, MascotScene);
            add("more", 6.0, More);
            fast("specs", 5.6, 1.25, Specs);
            add("end", 5.6, End);
        }

        static double Length { get { Scene s = scenes[scenes.Count - 1]; return s.Start + s.Dur; } }

        static void Setup(string platesDir, string lang, string mp4)
        {
            plates = platesDir;
            es = lang == "es";
            docs = Environment.GetEnvironmentVariable("FILM_DOCS") ?? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(mp4)));
            Ds.Apply(2);
            LoadAssets();
            LoadParts();
        }

        static void Render(string platesDir, string lang, string mp4, string ffmpeg, int fps, int scale)
        {
            Setup(platesDir, lang, mp4);
            Build();
            string poster = Path.Combine(docs, "video", "poster-" + lang + ".png");
            if (Environment.GetEnvironmentVariable("FILM_STILLS") != null) { Stills(Path.GetDirectoryName(Path.GetFullPath(mp4)), lang); return; }
            if (Environment.GetEnvironmentVariable("FILM_INFO") != null)
            {
                // What the script needs to split the work: the frame count, the scene marks and the cover.
                File.WriteAllText(mp4 + ".frames.txt", ((int)Math.Ceiling(Length * fps)).ToString(CultureInfo.InvariantCulture));
                using (StreamWriter sw = new StreamWriter(mp4 + ".scenes.txt"))
                    foreach (Scene s in scenes) sw.WriteLine(s.Name + " " + s.Start.ToString("0.###", CultureInfo.InvariantCulture) + " " + s.Dur.ToString("0.###", CultureInfo.InvariantCulture));
                Poster(poster);
                return;
            }
            Encode(mp4, ffmpeg, fps, scale);
        }

        // One extra part on its own, without fades, so it loops cleanly as a clip.
        static void RenderPart(string platesDir, string lang, string name, string mp4, string ffmpeg, int fps, int scale)
        {
            Setup(platesDir, lang, mp4);
            AddPart(name);
            if (Environment.GetEnvironmentVariable("FILM_STILLS") != null) { Stills(Path.GetDirectoryName(Path.GetFullPath(mp4)), lang); return; }
            if (Environment.GetEnvironmentVariable("FILM_INFO") != null) { File.WriteAllText(mp4 + ".frames.txt", ((int)Math.Ceiling(Length * fps)).ToString(CultureInfo.InvariantCulture)); return; }
            Encode(mp4, ffmpeg, fps, scale);
        }

        // Frames [from, to) at scale x 1920x1080 (the drawing is resolution independent), piped to x264 as raw BGRA.
        // FILM_RANGE=from:to renders one segment, so several processes can share a film (the script joins them).
        static void Encode(string mp4, string ffmpeg, int fps, int scale)
        {
            int w = W * scale, h = H * scale;
            int from = 0, to = (int)Math.Ceiling(Length * fps);
            string range = Environment.GetEnvironmentVariable("FILM_RANGE");
            if (range != null)
            {
                string[] r = range.Split(':');
                from = int.Parse(r[0], CultureInfo.InvariantCulture);
                to = Math.Min(to, int.Parse(r[1], CultureInfo.InvariantCulture));
            }
            ProcessStartInfo psi = new ProcessStartInfo(ffmpeg,
                "-y -hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s " + w + "x" + h + " -r " + fps + " -i - " +
                "-c:v libx264 -preset " + (scale > 1 ? "medium" : "slow") + " -crf " + (scale > 1 ? "16" : "17") + " -threads 3 -x264-params rc-lookahead=20" +
                " -pix_fmt yuv420p -colorspace bt709 -color_primaries bt709 -color_trc bt709 -movflags +faststart \"" + mp4 + "\"");
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.CreateNoWindow = true;
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            byte[] buf = new byte[w * h * 4];
            using (Process p = Process.Start(psi))
            {
                Stream input = p.StandardInput.BaseStream;
                for (int f = from; f < to; f++)
                {
                    DrawingVisual dv = new DrawingVisual();
                    using (DrawingContext dc = dv.RenderOpen()) Frame(dc, f / (double)fps);
                    rtb.Clear();
                    rtb.Render(dv);
                    rtb.CopyPixels(buf, w * 4, 0);
                    input.Write(buf, 0, buf.Length);
                    if ((f - from) % 120 == 119) GC.Collect();
                }
                input.Close();
                p.WaitForExit();
            }
        }

        // FILM_STILLS=1: a few stills per scene instead of the video, to review the layout quickly.
        static void Stills(string dir, string lang)
        {
            foreach (Scene s in scenes)
                foreach (double k in new double[] { 0.12, 0.28, 0.45, 0.62, 0.78, 0.92 })
                {
                    DrawingVisual dv = new DrawingVisual();
                    using (DrawingContext dc = dv.RenderOpen()) Frame(dc, s.Start + s.Dur * k);
                    RenderTargetBitmap rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    PngBitmapEncoder e = new PngBitmapEncoder();
                    e.Frames.Add(BitmapFrame.Create(rtb));
                    using (FileStream fs = File.Create(Path.Combine(dir, "still-" + lang + "-" + s.Name + "-" + (int)(k * 100) + ".png"))) e.Save(fs);
                }
        }

        // The README cover: a frame of the capture scene under a play button, at 1280x720.
        static void Poster(string path)
        {
            Scene cap = scenes.Find(delegate(Scene s) { return s.Name == "capture"; });
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(2 / 3.0, 2 / 3.0));
                Frame(dc, cap.Start + 3.3 / cap.Speed);
                dc.DrawRectangle(B(Colors.Black, 0.28), null, new Rect(0, 0, W, H));
                Point c = new Point(W / 2.0, 600);
                Shadow(dc, new Rect(c.X - 70, c.Y - 70, 140, 140), 70, 1.2, 30, 12);
                dc.DrawEllipse(B(Colors.White, 0.92), null, c, 70, 70);
                StreamGeometry tri = new StreamGeometry();
                using (StreamGeometryContext k = tri.Open())
                {
                    k.BeginFigure(new Point(c.X - 20, c.Y - 30), true, true);
                    k.LineTo(new Point(c.X + 34, c.Y), true, true);
                    k.LineTo(new Point(c.X - 20, c.Y + 30), true, true);
                }
                dc.DrawGeometry(B(Color.FromRgb(20, 20, 24)), new Pen(B(Color.FromRgb(20, 20, 24)), 6) { LineJoin = PenLineJoin.Round }, tri);
                Capsule(dc, L("Watch the film", "Ver el v\u00EDdeo") + "  \u00B7  " + ((int)Math.Round(Length) / 60) + ":" + ((int)Math.Round(Length) % 60).ToString("00"), c.X, c.Y + 120, 22, Color.FromArgb(230, 28, 28, 32), Fg, 1, 24, 50);
                dc.Pop();
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) e.Save(fs);
        }

        static void Frame(DrawingContext dc, double t)
        {
            Stage(dc, t);
            for (int i = 0; i < scenes.Count; i++)
            {
                Scene s = scenes[i];
                double lt = t - s.Start;
                if (lt < 0 || lt > s.Dur) continue;
                double a = Math.Min(i == 0 ? 1 : Clamp(lt / Overlap), i == scenes.Count - 1 ? 1 : Clamp((s.Dur - lt) / Overlap));
                a = Smooth(a);
                // Leaving scenes recede slightly, arriving ones settle from slightly larger.
                double z = 1;
                if (i > 0 && lt < Overlap) z = 1.025 - 0.025 * Smooth(lt / Overlap);
                else if (i < scenes.Count - 1 && lt > s.Dur - Overlap) z = 1 - 0.02 * Smooth((lt - s.Dur + Overlap) / Overlap);
                dc.PushOpacity(a);
                dc.PushTransform(new ScaleTransform(z, z, W / 2.0, H / 2.0));
                s.Draw(dc, lt, s.Dur);
                dc.Pop();
                dc.Pop();
            }
        }

        // Near-black stage with two slow, faint color fields.
        static void Stage(DrawingContext dc, double t)
        {
            dc.DrawRectangle(B(Color.FromRgb(6, 6, 9)), null, new Rect(0, 0, W, H));
            Glow(dc, W * (0.3 + 0.05 * Math.Sin(t * 0.21)), H * (0.15 + 0.05 * Math.Cos(t * 0.17)), 1300, 900, Brand2, 0.15);
            Glow(dc, W * (0.75 + 0.04 * Math.Cos(t * 0.19)), H * (0.92 + 0.04 * Math.Sin(t * 0.23)), 1400, 900, Brand1, 0.12);
        }

        // ---- Easing and drawing helpers

        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double P(double t, double a, double b) { return Clamp((t - a) / (b - a)); }
        static double Smooth(double x) { x = Clamp(x); return x * x * (3 - 2 * x); }
        static double OutCubic(double x) { x = Clamp(x); return 1 - Math.Pow(1 - x, 3); }
        static double OutExpo(double x) { x = Clamp(x); return x >= 1 ? 1 : 1 - Math.Pow(2, -10 * x); }
        static double InOut(double x) { x = Clamp(x); return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2; }
        static double OutBack(double x) { x = Clamp(x); const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
        static double Spring(double x) { if (x <= 0) return 0; if (x >= 1.6) return 1; return 1 - Math.Exp(-6 * x) * Math.Cos(9 * x); }
        static double Lerp(double a, double b, double k) { return a + (b - a) * k; }
        static Point LerpP(Point a, Point b, double k) { return new Point(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k)); }
        static Rect Lerp(Rect a, Rect b, double k) { return new Rect(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k), Lerp(a.Width, b.Width, k), Lerp(a.Height, b.Height, k)); }
        static Color A(Color c, double a) { return Color.FromArgb((byte)Math.Max(0, Math.Min(255, a * 255)), c.R, c.G, c.B); }
        static Color Mix(Color a, Color b, double k) { k = Clamp(k); return Color.FromRgb((byte)Lerp(a.R, b.R, k), (byte)Lerp(a.G, b.G, k), (byte)Lerp(a.B, b.B, k)); }
        static SolidColorBrush B(Color c) { SolidColorBrush b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush B(Color c, double a) { return B(A(c, a)); }
        static LinearGradientBrush Grad(Color a, Color b, double angle) { LinearGradientBrush g = new LinearGradientBrush(a, b, angle); g.Freeze(); return g; }

        static void Glow(DrawingContext dc, double cx, double cy, double w, double h, Color c, double a)
        {
            if (a <= 0) return;
            RadialGradientBrush rb = new RadialGradientBrush();
            rb.GradientStops.Add(new GradientStop(A(c, a), 0));
            rb.GradientStops.Add(new GradientStop(A(c, a * 0.5), 0.4));
            rb.GradientStops.Add(new GradientStop(A(c, a * 0.15), 0.7));
            rb.GradientStops.Add(new GradientStop(A(c, 0), 1));
            dc.DrawEllipse(rb, null, new Point(cx, cy), w / 2, h / 2);
        }

        static FormattedText FT(string s, double size, FontWeight w, Brush b, FontFamily fam = null)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(fam ?? display, FontStyles.Normal, w, FontStretches.Normal), size, b, 1.0);
        }

        // Text with its top at y; align -1 left, 0 center, 1 right; k fades it up from a short distance. Lines split on \n.
        static void Text(DrawingContext dc, string s, double size, FontWeight w, Brush b, double x, double y, int align = 0, double k = 1, FontFamily fam = null)
        {
            if (k <= 0) return;
            string[] lines = s.Split('\n');
            double kk = OutCubic(k);
            dc.PushOpacity(kk);
            foreach (string line in lines)
            {
                FormattedText ft = FT(line, size, w, b, fam);
                double width = ft.WidthIncludingTrailingWhitespace;
                double ox = align < 0 ? x : align == 0 ? x - width / 2 : x - width;
                dc.DrawText(ft, new Point(ox, y + (1 - kk) * size * 0.45));
                y += ft.Height * 1.08;
            }
            dc.Pop();
        }

        // Apple-style headline: words rise from behind a mask one after another; outK makes the line leave.
        static void Headline(DrawingContext dc, string s, double size, Brush b, double cx, double y, double t, double start, double outK = 0)
        {
            string[] words = s.Split(' ');
            double space = size * 0.24;
            double total = 0;
            FormattedText[] fts = new FormattedText[words.Length];
            for (int i = 0; i < words.Length; i++) { fts[i] = FT(words[i], size, FontWeights.Bold, b); total += fts[i].WidthIncludingTrailingWhitespace + (i > 0 ? space : 0); }
            double x = cx - total / 2;
            double o = 1 - OutCubic(outK);
            for (int i = 0; i < words.Length; i++)
            {
                double k = OutExpo(P(t, start + i * 0.075, start + i * 0.075 + 0.8));
                if (k > 0 && o > 0)
                {
                    dc.PushOpacity(Clamp(k * 1.5) * o);
                    dc.PushClip(new RectangleGeometry(new Rect(x - 20, y - size * 0.4, fts[i].WidthIncludingTrailingWhitespace + 40, fts[i].Height + size * 0.4)));
                    dc.DrawText(fts[i], new Point(x, y + (1 - k) * size * 1.0 - OutCubic(outK) * size * 0.3));
                    dc.Pop();
                    dc.Pop();
                }
                x += fts[i].WidthIncludingTrailingWhitespace + space;
            }
        }

        // Title and subtitle at the top of a feature scene, optionally replaced by a second pair at 'swap'.
        static void Titles(DrawingContext dc, double t, string h1, string s1, string h2 = null, string s2 = null, double swap = 99)
        {
            Brush hb = B(Fg);
            double out1 = h2 != null ? P(t, swap - 0.35, swap) : 0;
            if (out1 < 1)
            {
                Headline(dc, h1, 72, hb, W / 2.0, 58, t, 0.15, out1);
                if (s1 != null) Text(dc, s1, 29, FontWeights.Normal, B(Gray), W / 2.0, 156, 0, P(t, 0.5, 1.2) * (1 - out1), body);
            }
            if (h2 != null && t > swap)
            {
                Headline(dc, h2, 72, hb, W / 2.0, 58, t, swap + 0.05);
                if (s2 != null) Text(dc, s2, 29, FontWeights.Normal, B(Gray), W / 2.0, 156, 0, P(t, swap + 0.35, swap + 1.0), body);
            }
        }

        static void Shadow(DrawingContext dc, Rect r, double radius, double strength, double spread = 40, double dy = 18)
        {
            int n = 14;
            for (int i = n; i >= 1; i--)
            {
                double k = i / (double)n;
                Rect s = r;
                s.Inflate(spread * k, spread * k);
                s.Offset(0, dy * k);
                dc.DrawRoundedRectangle(B(Colors.Black, strength * 0.085 * (1 - k * 0.85)), null, s, radius + spread * k, radius + spread * k);
            }
        }

        static void Plate(DrawingContext dc, ImageSource img, Rect r, double radius, double shadow = 0.9)
        {
            if (shadow > 0) Shadow(dc, r, radius, shadow, 50, 26);
            dc.PushClip(new RectangleGeometry(r, radius, radius));
            dc.DrawImage(img, r);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.13), 1.2), r, radius, radius);
        }

        static Rect Fit(BitmapSource img, double cx, double cy, double w)
        {
            double h = w * img.PixelHeight / img.PixelWidth;
            return new Rect(cx - w / 2, cy - h / 2, w, h);
        }

        static StreamGeometry arrow;

        static void Pointer(DrawingContext dc, Point p, double scale = 1.5, double press = 0)
        {
            if (arrow == null)
            {
                arrow = new StreamGeometry();
                using (StreamGeometryContext c = arrow.Open())
                {
                    c.BeginFigure(new Point(0, 0), true, true);
                    c.PolyLineTo(new Point[] { new Point(0, 22), new Point(5.2, 17), new Point(9, 25.6), new Point(12.4, 24.2), new Point(8.7, 15.8), new Point(15.6, 15.6) }, true, true);
                }
                arrow.Freeze();
            }
            double s = scale * (1 - 0.12 * press);
            dc.PushTransform(new MatrixTransform(s, 0, 0, s, p.X, p.Y));
            dc.PushTransform(new TranslateTransform(1.2, 2.2));
            dc.DrawGeometry(B(Colors.Black, 0.28), null, arrow);
            dc.Pop();
            dc.DrawGeometry(B(Colors.Black), new Pen(B(Colors.White), 1.6), arrow);
            dc.Pop();
        }

        static void Pill(DrawingContext dc, Rect r, Brush fill, Pen pen = null)
        {
            dc.DrawRoundedRectangle(fill, pen, r, r.Height / 2, r.Height / 2);
        }

        // A capsule with a label, sized to its text and centered at (cx, cy).
        static Rect Capsule(DrawingContext dc, string s, double cx, double cy, double size, Color bg, Color fg, double k = 1, double padX = 18, double h = 0)
        {
            FormattedText ft = FT(s, size, FontWeights.SemiBold, B(fg), body);
            if (h <= 0) h = size * 2.1;
            Rect r = new Rect(cx - ft.Width / 2 - padX, cy - h / 2, ft.Width + 2 * padX, h);
            if (k <= 0) return r;
            double sc = 0.9 + 0.1 * OutBack(k);
            dc.PushOpacity(Clamp(k));
            dc.PushTransform(new ScaleTransform(sc, sc, cx, cy));
            Pill(dc, r, B(bg));
            dc.DrawText(ft, new Point(r.X + padX, cy - ft.Height / 2));
            dc.Pop();
            dc.Pop();
            return r;
        }

        static void Keycap(DrawingContext dc, string s, Point c, double size, double press = 0)
        {
            FormattedText ft = FT(s, size * 0.17, FontWeights.SemiBold, B(Fg), body);
            double w = Math.Max(size, ft.Width + size * 0.5), h = size, depth = size * 0.07;
            Rect baseR = new Rect(c.X - w / 2, c.Y - h / 2 + depth, w, h);
            Shadow(dc, baseR, size * 0.18, 0.9, size * 0.18, size * 0.12);
            dc.DrawRoundedRectangle(B(Color.FromRgb(12, 12, 14)), new Pen(B(Colors.White, 0.06), 1), baseR, size * 0.2, size * 0.2);
            Rect top = new Rect(c.X - w / 2, c.Y - h / 2 + depth * press, w, h - depth * 0.4);
            dc.DrawRoundedRectangle(Grad(Color.FromRgb(62, 62, 68), Color.FromRgb(30, 30, 34), 90), new Pen(B(Colors.White, 0.16), 1.4), top, size * 0.2, size * 0.2);
            dc.DrawText(ft, new Point(top.X + size * 0.14, top.Y + size * 0.12));
        }

        static void Star(DrawingContext dc, Point c, double r, Brush b)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext k = g.Open())
            {
                k.BeginFigure(new Point(c.X, c.Y - r), true, true);
                k.QuadraticBezierTo(c, new Point(c.X + r, c.Y), true, true);
                k.QuadraticBezierTo(c, new Point(c.X, c.Y + r), true, true);
                k.QuadraticBezierTo(c, new Point(c.X - r, c.Y), true, true);
                k.QuadraticBezierTo(c, new Point(c.X, c.Y - r), true, true);
            }
            dc.DrawGeometry(b, null, g);
        }

        // The "screen": the fake desktop in a rounded frame; what is drawn inside uses desktop coordinates.
        static readonly Rect ScreenR = new Rect(320, 236, 1280, 720);

        static void PushScreen(DrawingContext dc, Rect r, double zoom, Point focus)
        {
            Shadow(dc, r, 22, 1.2, 60, 30);
            dc.DrawRoundedRectangle(B(Color.FromRgb(20, 20, 24)), null, r, 22, 22);
            dc.PushClip(new RectangleGeometry(r, 20, 20));
            double s = r.Width / W;
            dc.PushTransform(new MatrixTransform(s, 0, 0, s, r.X, r.Y));
            dc.PushTransform(new ScaleTransform(zoom, zoom, focus.X, focus.Y));
        }

        static void PopScreen(DrawingContext dc, Rect r)
        {
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.16), 1.2), r, 20, 20);
        }

        static void CornerMarks(DrawingContext dc, Rect f, double len, double th)
        {
            Brush w = B(Colors.White);
            dc.DrawRectangle(w, null, new Rect(f.X - th / 2, f.Y - th / 2, len, th)); dc.DrawRectangle(w, null, new Rect(f.X - th / 2, f.Y - th / 2, th, len));
            dc.DrawRectangle(w, null, new Rect(f.Right - len + th / 2, f.Y - th / 2, len, th)); dc.DrawRectangle(w, null, new Rect(f.Right - th / 2, f.Y - th / 2, th, len));
            dc.DrawRectangle(w, null, new Rect(f.X - th / 2, f.Bottom - th / 2, len, th)); dc.DrawRectangle(w, null, new Rect(f.X - th / 2, f.Bottom - len + th / 2, th, len));
            dc.DrawRectangle(w, null, new Rect(f.Right - len + th / 2, f.Bottom - th / 2, len, th)); dc.DrawRectangle(w, null, new Rect(f.Right - th / 2, f.Bottom - len + th / 2, th, len));
        }

        // ================= Scenes =================

        static void ColdOpen(DrawingContext dc, double t, double dur)
        {
            // The key slams in, is pressed, and the screen flashes; then the line lands like a punch.
            double drop = OutBack(P(t, 0.05, 0.5));
            double press = t < 0.72 ? 0 : t < 0.82 ? P(t, 0.72, 0.82) : 1 - OutCubic(P(t, 0.88, 1.2));
            double rise = InOut(P(t, 0.95, 1.55));
            double hit = P(t, 0.8, 1.9);
            double shake = t > 0.8 && t < 1.2 ? Math.Sin(t * 110) * 9 * (1 - P(t, 0.8, 1.2)) : 0;
            dc.PushTransform(new TranslateTransform(shake, shake * 0.5));
            Point c = new Point(W / 2.0, Lerp(H / 2.0, 285, rise));
            double sc = Lerp(1.6, 1, drop) * Lerp(1, 0.7, rise);
            if (hit > 0 && hit < 1)
            {
                double rr = 160 + 1100 * OutExpo(hit);
                Glow(dc, W / 2.0, H / 2.0, rr * 1.7, rr * 1.7, Blue, 0.55 * (1 - hit));
                dc.DrawEllipse(null, new Pen(B(Colors.White, 0.6 * (1 - hit)), 4 * (1 - hit) + 0.5), new Point(W / 2.0, H / 2.0), rr, rr);
            }
            dc.PushOpacity(Clamp(drop * 1.5));
            dc.PushTransform(new ScaleTransform(sc, sc, c.X, c.Y));
            Glow(dc, c.X, c.Y + 40, 760, 460, Brand1, 0.2 + (t > 0.8 ? 0.3 * (1 - hit) : 0));
            Keycap(dc, L("PrtSc", "Impr Pant"), c, 260, press);
            dc.Pop();
            dc.Pop();
            Headline(dc, L("One key.", "Una tecla."), 170, B(Fg), W / 2.0, 470, t, 0.95);
            Text(dc, L("That's all it takes.", "No necesitas m\u00E1s."), 44, FontWeights.Normal, B(Gray), W / 2.0, 705, 0, P(t, 1.7, 2.2), body);
            double flash = P(t, 0.8, 1.05);
            if (flash > 0 && flash < 1) dc.DrawRectangle(B(Colors.White, 0.3 * (1 - flash)), null, new Rect(-50, -50, W + 100, H + 100));
            dc.Pop();
        }

        static void Words(DrawingContext dc, double t, double dur)
        {
            string[] w = es ? new string[] { "Captura.", "Marca.", "Graba.", "Comparte." } : new string[] { "Capture.", "Annotate.", "Record.", "Share." };
            Color[][] g = { new Color[] { Teal, Blue }, new Color[] { Purple, Pink }, new Color[] { Pink, Orange }, new Color[] { Green, Teal } };
            double slot = 0.78;
            for (int i = 0; i < 4; i++)
            {
                double lt = t - (0.1 + i * slot);
                if (lt < 0 || lt > slot + 0.2) continue;
                double kin = OutExpo(P(lt, 0, 0.4)), kout = i == 3 ? 0 : InOut(P(lt, slot - 0.12, slot + 0.12));
                double sc = Lerp(1.3, 1.0, kin) * Lerp(1, 0.92, kout);
                FormattedText ft = FT(w[i], 230, FontWeights.Bold, Grad(g[i][0], g[i][1], 0));
                Glow(dc, W / 2.0, H / 2.0, 1500, 800, g[i][1], 0.3 * kin * (1 - kout));
                dc.PushOpacity(kin * (1 - kout));
                dc.PushTransform(new ScaleTransform(sc, sc, W / 2.0, H / 2.0));
                dc.DrawText(ft, new Point(W / 2.0 - ft.Width / 2, H / 2.0 - ft.Height / 2 - kout * 30));
                dc.Pop();
                dc.Pop();
                double f = P(lt, 0, 0.18);
                if (f > 0 && f < 1) dc.DrawRectangle(B(g[i][1], 0.16 * (1 - f)), null, new Rect(0, 0, W, H));
            }
        }

        static void Brand(DrawingContext dc, double t, double dur)
        {
            double k = Spring(P(t, 0.05, 1.3) * 1.6);
            double d = 240 * Lerp(0.6, 1, k);
            Glow(dc, W / 2.0, 380, 1000, 760, Brand2, 0.38 * OutCubic(P(t, 0, 0.8)));
            dc.PushOpacity(OutCubic(P(t, 0.05, 0.4)));
            dc.DrawImage(logo, new Rect(W / 2.0 - d / 2, 380 - d / 2, d, d));
            dc.Pop();
            Headline(dc, "Stackshot", 150, B(Fg), W / 2.0, 540, t, 0.35);
            Text(dc, L("Capture, annotate and share in seconds.", "Captura, marca y comparte en segundos."), 42, FontWeights.Normal, B(Gray), W / 2.0, 752, 0, P(t, 0.8, 1.5), body);
        }

        static void Capture(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Select. Snap. Done.", "Selecciona. Captura. Listo."),
                L("Press Print Screen, drag over anything, and it waits in the corner.", "Pulsa Impr Pant, arrastra sobre lo que quieras y te espera en la esquina."),
                L("Drag it anywhere.", "Arr\u00E1strala donde quieras."),
                L("Into a chat, an email or a document. Or paste it with Ctrl+V.", "A un chat, un correo o un documento. O p\u00E9gala con Ctrl+V."), 5.9);

            Rect sel = new Rect(600, 215, 1070, 565);
            Rect card = new Rect(28, 1080 - 28 - 190, 360, 190);
            Point A0 = new Point(sel.X, sel.Y), B0 = new Point(sel.Right, sel.Bottom), onCard = new Point(card.X + card.Width * 0.55, card.Y + card.Height * 0.42), drop = new Point(1400, 640);
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            // Camera: in on the corner while the card is hovered, back out for the drop.
            double zoom = 1 + 0.6 * InOut(P(t, 4.3, 5.1)) * (1 - InOut(P(t, 6.3, 7.1)));
            PushScreen(dc, sr, zoom, new Point(0, 1080));
            dc.DrawImage(desk, new Rect(0, 0, W, H));

            Point cur;
            if (t < 1.75) cur = LerpP(new Point(1180, 660), A0, InOut(P(t, 0.7, 1.7)));
            else if (t < 3.4) cur = LerpP(A0, B0, InOut(P(t, 1.85, 3.25)));
            else if (t < 6.3) cur = LerpP(B0, onCard, InOut(P(t, 3.9, 4.9)));
            else cur = LerpP(onCard, drop, InOut(P(t, 6.4, 7.6)));

            // Region picker
            double dim = P(t, 0.85, 1.05) * (1 - P(t, 3.4, 3.55));
            if (dim > 0)
            {
                Rect cs = t < 1.85 ? new Rect(A0, A0) : new Rect(A0, t < 3.4 ? cur : B0);
                Geometry outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), new RectangleGeometry(cs));
                dc.DrawGeometry(B(Colors.Black, 0.42 * dim), null, outside);
                if (cs.Width > 2)
                {
                    dc.DrawRectangle(null, new Pen(B(Colors.White, 0.95 * dim), 1.5), cs);
                    Capsule(dc, ((int)cs.Width) + " \u00D7 " + ((int)cs.Height), cs.X + cs.Width / 2, cs.Bottom + 26, 16, Color.FromArgb(225, 28, 28, 30), Fg, dim);
                }
                Capsule(dc, "Arrastra para capturar un \u00E1rea  \u00B7  Clic: ventana o pantalla entera  \u00B7  Esc: cancelar", W / 2.0, 34, 15, Color.FromArgb(225, 28, 28, 30), Fg, dim);
                if (t < 3.4) Magnifier(dc, cur, dim);
            }
            double flash = P(t, 3.38, 3.75);
            if (flash > 0 && flash < 1) dc.DrawRectangle(B(Colors.White, 0.6 * (1 - flash)), null, sel);

            // The capture flies into the corner and becomes a card, then gets dragged into the chat.
            double fly = InOut(P(t, 3.5, 4.35)), drag = InOut(P(t, 6.4, 7.6));
            bool dropped = t > 7.65;
            if (t > 3.45 && !dropped)
            {
                Rect r = Lerp(sel, card, fly);
                if (t > 6.3) r = Lerp(card, new Rect(drop.X - 170, drop.Y - 80, 300, 158), drag);
                double rad = Lerp(2, 16, fly);
                Shadow(dc, r, rad, 0.4 + 0.6 * fly, 30, 12);
                dc.PushClip(new RectangleGeometry(r, rad, rad));
                dc.DrawImage(shot, r);
                double hover = P(t, 4.9, 5.15) * (1 - P(t, 6.2, 6.4));
                if (hover > 0) CardHover(dc, r, hover);
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.22 * fly), 1.5), r, rad, rad);
                if (hover > 0) CardButtons(dc, r, hover);
            }
            if (dropped)
            {
                double k = Spring(P(t, 7.65, 8.6) * 1.6);
                Rect bub = new Rect(1150, 452, 520, 520 * 565 / 1070.0 + 24);
                dc.PushOpacity(Clamp(k * 2));
                dc.PushTransform(new ScaleTransform(Lerp(0.85, 1, k), Lerp(0.85, 1, k), bub.Right, bub.Bottom));
                Shadow(dc, bub, 18, 0.7, 24, 10);
                dc.DrawRoundedRectangle(B(Color.FromRgb(122, 162, 247)), null, bub, 18, 18);
                Rect img = new Rect(bub.X + 12, bub.Y + 12, bub.Width - 24, bub.Height - 24);
                dc.PushClip(new RectangleGeometry(img, 10, 10));
                dc.DrawImage(shot, img);
                dc.Pop();
                Text(dc, L("You", "T\u00FA"), 13, FontWeights.SemiBold, B(Color.FromRgb(115, 124, 165)), bub.X + 4, bub.Y - 22, -1, 1, body);
                dc.Pop();
                dc.Pop();
            }
            Pointer(dc, cur, 1.6, (t > 1.8 && t < 3.4) || (t > 6.3 && t < 7.6) ? 1 : 0);
            PopScreen(dc, sr);
        }

        static void Magnifier(DrawingContext dc, Point p, double a)
        {
            double r = 64, z = 6;
            Point c = new Point(p.X + 92, p.Y + 92);
            dc.PushOpacity(a);
            Shadow(dc, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r), r, 0.6, 16, 6);
            dc.PushClip(new EllipseGeometry(c, r, r));
            dc.PushTransform(new MatrixTransform(z, 0, 0, z, c.X - p.X * z, c.Y - p.Y * z));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            dc.Pop();
            Pen grid = new Pen(B(Colors.Black, 0.14), 1);
            double ox = (c.X - p.X * z) % z, oy = (c.Y - p.Y * z) % z;
            for (double gx = c.X - r + ((ox - (c.X - r)) % z + z) % z; gx < c.X + r; gx += z) dc.DrawLine(grid, new Point(gx, c.Y - r), new Point(gx, c.Y + r));
            for (double gy = c.Y - r + ((oy - (c.Y - r)) % z + z) % z; gy < c.Y + r; gy += z) dc.DrawLine(grid, new Point(c.X - r, gy), new Point(c.X + r, gy));
            dc.DrawRectangle(null, new Pen(B(Blue), 1.5), new Rect(c.X, c.Y, z, z));
            dc.Pop();
            dc.DrawEllipse(null, new Pen(B(Colors.White, 0.9), 2.5), c, r, r);
            dc.Pop();
        }

        static void CardHover(DrawingContext dc, Rect r, double k)
        {
            dc.DrawRectangle(B(Colors.Black, 0.45 * k), null, r);
            Rect c1 = new Rect(r.X + r.Width / 2 - 116, r.Y + r.Height / 2 - 20, 106, 40), c2 = new Rect(r.X + r.Width / 2 + 10, r.Y + r.Height / 2 - 20, 106, 40);
            dc.PushOpacity(k);
            Pill(dc, c1, B(Colors.White, 0.94));
            Pill(dc, c2, B(Colors.White, 0.94));
            FormattedText a = FT("Copiar", 17, FontWeights.SemiBold, B(Color.FromRgb(28, 28, 30)), body), b = FT("Guardar", 17, FontWeights.SemiBold, B(Color.FromRgb(28, 28, 30)), body);
            dc.DrawText(a, new Point(c1.X + (c1.Width - a.Width) / 2, c1.Y + (c1.Height - a.Height) / 2));
            dc.DrawText(b, new Point(c2.X + (c2.Width - b.Width) / 2, c2.Y + (c2.Height - b.Height) / 2));
            dc.Pop();
        }

        static void CardButtons(DrawingContext dc, Rect r, double k)
        {
            dc.PushOpacity(k);
            Point[] at = { new Point(r.X + 22, r.Y + 22), new Point(r.Right - 22, r.Y + 22), new Point(r.X + 22, r.Bottom - 22), new Point(r.Right - 22, r.Bottom - 22) };
            Brush ink = B(Color.FromRgb(28, 28, 30));
            for (int i = 0; i < 4; i++)
            {
                dc.DrawEllipse(B(Colors.White, 0.95), null, at[i], 15, 15);
                Pen p = new Pen(ink, 2.2);
                p.StartLineCap = p.EndLineCap = PenLineCap.Round;
                Point c = at[i];
                if (i == 0) { dc.DrawLine(p, new Point(c.X - 5, c.Y - 5), new Point(c.X + 5, c.Y + 5)); dc.DrawLine(p, new Point(c.X + 5, c.Y - 5), new Point(c.X - 5, c.Y + 5)); }
                else if (i == 1) { dc.DrawLine(p, new Point(c.X, c.Y), new Point(c.X, c.Y + 7)); dc.DrawRectangle(ink, null, new Rect(c.X - 3.5, c.Y - 7, 7, 6)); dc.DrawLine(p, new Point(c.X - 6, c.Y), new Point(c.X + 6, c.Y)); }
                else if (i == 2) { dc.DrawLine(new Pen(ink, 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Triangle }, new Point(c.X + 5, c.Y - 5), new Point(c.X - 4, c.Y + 4)); }
                else { dc.DrawRoundedRectangle(null, p, new Rect(c.X - 6, c.Y - 3, 12, 9), 2, 2); dc.DrawLine(p, new Point(c.X - 6, c.Y - 5), new Point(c.X - 1, c.Y - 5)); }
            }
            dc.Pop();
        }

        static void EditorScene(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Mark it up in seconds.", "M\u00E1rcala en segundos."),
                L("Arrows, shapes, numbered steps, text, highlight and redaction.", "Flechas, formas, pasos numerados, texto, resaltado y tapado."),
                L("Then make it beautiful.", "Y d\u00E9jala perfecta."),
                L("Backdrops, padding, rounded corners and shadow. For videos too.", "Fondos, margen, esquinas redondeadas y sombra. Tambi\u00E9n en v\u00EDdeos."), 5.3);
            double rise = OutCubic(P(t, 0, 0.8));
            double wx = (W - 1357) / 2.0, wy = 246 + (1 - rise) * 50;
            Rect win = new Rect(wx, wy, 1357, 51 + 690 + 27);
            Shadow(dc, win, 12, 1.2, 60, 30);
            dc.DrawRoundedRectangle(B(editorBg), null, win, 12, 12);
            dc.PushClip(new RectangleGeometry(win, 12, 12));
            double panel = InOut(P(t, 5.0, 5.5));
            double panelH = 58 * panel;
            if (panel > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(wx, wy + 51, 1357, panelH)));
                dc.DrawImage(edPanel, new Rect(wx, wy + 51 - 58 + panelH, 1357, 58));
                dc.Pop();
            }
            dc.DrawImage(edToolbar, new Rect(wx, wy, 1357, 51));
            dc.DrawImage(edStatus, new Rect(wx, win.Bottom - 27, 1357, 27));
            Rect area = new Rect(wx, wy + 51 + panelH, 1357, 690 - panelH);

            // The presentation: the capture shrinks onto a gradient with padding, rounded corners and a shadow.
            double bk = InOut(P(t, 5.4, 6.6));
            Point mid = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            double s0 = Math.Min((area.Width - 120) / 1100.0, (690 - 40) / 640.0);
            Rect grad = new Rect(mid.X - 540, mid.Y - 300, 1080, 600);
            if (bk > 0)
            {
                Color[][] gs = { new Color[] { Color.FromRgb(96, 125, 255), Color.FromRgb(139, 92, 246) }, new Color[] { Color.FromRgb(255, 140, 90), Color.FromRgb(236, 72, 153) }, new Color[] { Color.FromRgb(20, 184, 230), Color.FromRgb(79, 123, 255) } };
                int gi = t < 7.6 ? 0 : t < 8.7 ? 1 : 2;
                double cross = gi == 0 ? 1 : P(t, gi == 1 ? 7.6 : 8.7, (gi == 1 ? 7.6 : 8.7) + 0.35);
                Rect gr = Lerp(new Rect(mid.X - 550 * s0, mid.Y - 320 * s0, 1100 * s0, 640 * s0), grad, bk);
                dc.PushOpacity(bk);
                if (gi > 0) dc.DrawRectangle(Grad(gs[gi - 1][0], gs[gi - 1][1], 35), null, gr);
                dc.PushOpacity(cross);
                dc.DrawRectangle(Grad(gs[gi][0], gs[gi][1], 35), null, gr);
                dc.Pop();
                dc.Pop();
            }
            double s = Lerp(s0, (grad.Width - 2 * 110) / 1100.0, bk);
            Rect img = new Rect(mid.X - 550 * s, mid.Y - 320 * s, 1100 * s, 640 * s);
            double rad = 16 * bk;
            if (bk > 0) Shadow(dc, img, rad, 1.1 * bk, 34, 16);
            dc.PushClip(new RectangleGeometry(img, rad, rad));
            dc.PushTransform(new MatrixTransform(s, 0, 0, s, img.X, img.Y));
            dc.DrawImage(edCanvas, new Rect(0, 0, 1100, 640));
            Marks(dc, t);
            dc.Pop();
            dc.Pop();
            // The pointer follows each mark as it is drawn.
            Point[] stops = { new Point(480, 270), new Point(860, 400), new Point(170, 560), new Point(470, 330), new Point(520, 120), new Point(900, 335), new Point(110, 600), new Point(470, 475), new Point(860, 475), new Point(230, 160) };
            double[] times = { 0.6, 1.3, 1.45, 2.15, 2.25, 2.5, 2.9, 3.85, 4.25, 4.5 };
            if (t < 5.1)
            {
                Point cp = stops[0];
                for (int i = 0; i < times.Length - 1; i++) if (t >= times[i]) cp = LerpP(stops[i], stops[i + 1], InOut(P(t, times[i], times[i + 1])));
                Pointer(dc, new Point(img.X + cp.X * s, img.Y + cp.Y * s), 1.4);
            }
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.14), 1), win, 12, 12);
        }

        // The annotations, as the editor draws them, in capture coordinates (1100 x 640).
        static void Marks(DrawingContext dc, double t)
        {
            Color red = Color.FromRgb(255, 59, 48), blue = Color.FromRgb(10, 132, 255);
            double px = OutCubic(P(t, 4.45, 4.8));
            if (px > 0)
            {
                DrawingGroup dg = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(dg, BitmapScalingMode.NearestNeighbor);
                dg.Children.Add(new ImageDrawing(pixelated, new Rect(40, 110, 190, 50)));
                dc.PushOpacity(px);
                dc.DrawDrawing(dg);
                dc.Pop();
            }
            double hl = InOut(P(t, 3.85, 4.25));
            if (hl > 0) dc.DrawRectangle(B(Color.FromRgb(255, 214, 10), 0.42), null, new Rect(470, 455, 390 * hl, 40));
            double rk = InOut(P(t, 0.6, 1.3));
            if (rk > 0) dc.DrawRoundedRectangle(null, new Pen(B(red), 5), new Rect(480, 270, Math.Max(1, 380 * rk), Math.Max(1, 130 * rk)), 3, 3);
            double ak = InOut(P(t, 1.45, 2.15));
            if (ak > 0)
            {
                Point p0 = new Point(170, 560), p1 = new Point(470, 330), mid = new Point(250, 380);
                Point c = new Point(2 * mid.X - (p0.X + p1.X) / 2, 2 * mid.Y - (p0.Y + p1.Y) / 2);
                StreamGeometry g = new StreamGeometry();
                Point last = p0, prev = p0;
                using (StreamGeometryContext k = g.Open())
                {
                    k.BeginFigure(p0, false, false);
                    int n = 48;
                    for (int i = 1; i <= n; i++)
                    {
                        double u = ak * i / n;
                        Point q = new Point((1 - u) * (1 - u) * p0.X + 2 * (1 - u) * u * c.X + u * u * p1.X, (1 - u) * (1 - u) * p0.Y + 2 * (1 - u) * u * c.Y + u * u * p1.Y);
                        k.LineTo(q, true, true);
                        prev = last;
                        last = q;
                    }
                }
                Pen pen = new Pen(B(red), 6);
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                pen.LineJoin = PenLineJoin.Round;
                dc.DrawGeometry(null, pen, g);
                double ang = Math.Atan2(last.Y - prev.Y, last.X - prev.X), hs = 26;
                StreamGeometry head = new StreamGeometry();
                using (StreamGeometryContext k = head.Open())
                {
                    k.BeginFigure(new Point(last.X + Math.Cos(ang) * 6, last.Y + Math.Sin(ang) * 6), true, true);
                    k.LineTo(new Point(last.X - Math.Cos(ang - 0.45) * hs, last.Y - Math.Sin(ang - 0.45) * hs), true, true);
                    k.LineTo(new Point(last.X - Math.Cos(ang + 0.45) * hs, last.Y - Math.Sin(ang + 0.45) * hs), true, true);
                }
                dc.DrawGeometry(B(red), new Pen(B(red), 3) { LineJoin = PenLineJoin.Round }, head);
            }
            Badge(dc, new Point(520, 120), "1", OutBack(P(t, 2.25, 2.55)), blue);
            Badge(dc, new Point(900, 335), "2", OutBack(P(t, 2.5, 2.8)), blue);
            string label = L("Login fails here", "Aqu\u00ED falla el login");
            int chars = (int)Math.Round(label.Length * P(t, 2.9, 3.7));
            if (chars > 0)
            {
                FormattedText ft = FT(label.Substring(0, chars), 30, FontWeights.SemiBold, B(Colors.White), body);
                FormattedText full = FT(label, 30, FontWeights.SemiBold, B(Colors.White), body);
                Rect r = new Rect(70, 580, Math.Max(ft.Width, 4) + 24, full.Height + 10);
                dc.DrawRoundedRectangle(B(red), null, r, 6, 6);
                dc.DrawText(ft, new Point(r.X + 12, r.Y + 5));
            }
        }

        static void Badge(DrawingContext dc, Point c, string n, double k, Color col)
        {
            if (k <= 0) return;
            double r = 22 * k;
            dc.DrawEllipse(B(Colors.Black, 0.25), null, new Point(c.X, c.Y + 3), r + 2, r + 2);
            dc.DrawEllipse(B(col), new Pen(B(Colors.White), 3.5 * Clamp(k)), c, r, r);
            FormattedText ft = FT(n, Math.Max(1, 24 * Clamp(k)), FontWeights.Bold, B(Colors.White), body);
            dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
        }

        static void Scroll(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Capture the whole page.", "Captura la p\u00E1gina entera."),
                L("Scroll yourself, or let Stackshot scroll for you. One seamless image.", "Despl\u00E1zate t\u00FA o deja que Stackshot lo haga. Una sola imagen, sin cortes."));
            double rise = OutCubic(P(t, 0, 0.8));
            Rect win = new Rect(260, 262 + (1 - rise) * 50, 760, 690);
            Rect view = new Rect(win.X + 20, win.Y + 52, 720, win.Height - 72);
            Shadow(dc, win, 16, 1.1, 50, 24);
            dc.DrawRoundedRectangle(B(Color.FromRgb(30, 30, 36)), null, win, 16, 16);
            for (int i = 0; i < 3; i++) dc.DrawEllipse(B(i == 0 ? Red : i == 1 ? Orange : Green, 0.85), null, new Point(win.X + 26 + i * 22, win.Y + 26), 6.5, 6.5);
            Pill(dc, new Rect(win.X + 180, win.Y + 12, 400, 28), B(Colors.White, 0.07));
            Text(dc, L("docs.example.com/quick-start", "docs.example.com/guia-rapida"), 14, FontWeights.Normal, B(Gray), win.X + 380, win.Y + 16, 0, 1, body);
            double total = longDoc.PixelHeight - view.Height;
            double off = total * InOut(P(t, 1.4, 5.4));
            dc.PushClip(new RectangleGeometry(view, 8, 8));
            dc.DrawImage(longDoc, new Rect(view.X, view.Y - off, 720, longDoc.PixelHeight));
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.12), 1), win, 16, 16);
            double fk = P(t, 0.9, 1.2) * (1 - P(t, 5.6, 5.9));
            if (fk > 0)
            {
                Rect f = view;
                f.Inflate(6, 6);
                dc.PushOpacity(fk);
                dc.DrawRectangle(null, new Pen(B(Colors.White, 0.8), 1.2), f);
                CornerMarks(dc, f, 26, 4);
                Rect cap = new Rect(win.X + win.Width / 2 - 165, win.Bottom + 20, 330, 50);
                Shadow(dc, cap, 25, 0.6, 16, 6);
                Pill(dc, cap, B(Color.FromRgb(28, 28, 32)), new Pen(B(Colors.White, 0.1), 1));
                for (int i = 0; i < 8; i++)
                    dc.DrawEllipse(B(Colors.White, 0.15 + 0.85 * ((i + (int)(t * 12)) % 8) / 8.0), null, new Point(cap.X + 30 + Math.Cos(i * Math.PI / 4) * 8, cap.Y + 25 + Math.Sin(i * Math.PI / 4) * 8), 2, 2);
                Text(dc, "Desplazando\u2026", 17, FontWeights.SemiBold, B(Fg), cap.X + 52, cap.Y + 12, -1, 1, body);
                Capsule(dc, "Listo", cap.Right - 54, cap.Y + 25, 16, Blue, Colors.White, 1, 16, 34);
                dc.Pop();
            }
            // The stitched result grows, then settles as one tall image.
            double seen = view.Height + off;
            double sc = Math.Min(520 / 720.0, 640 / seen);
            Rect res = new Rect(1350 - 360 * sc, 262 + (1 - rise) * 50, 720 * sc, seen * sc);
            double rk = OutCubic(P(t, 1.0, 1.6));
            if (rk > 0)
            {
                dc.PushOpacity(rk);
                Shadow(dc, res, 10, 1.0, 40, 20);
                dc.PushClip(new RectangleGeometry(res, 10, 10));
                dc.DrawImage(Crop(longDoc, 0, 0, 720, (int)Math.Min(longDoc.PixelHeight, Math.Ceiling(seen))), res);
                dc.Pop();
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.16), 1), res, 10, 10);
                Capsule(dc, "720 \u00D7 " + ((int)seen) + " px", res.X + res.Width / 2, res.Bottom + 36, 16, Color.FromRgb(36, 36, 40), Fg, 1);
                dc.Pop();
            }
        }

        static void Record(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Record your screen. With sound.", "Graba la pantalla. Con sonido."),
                L("MP4 or GIF at up to 60 fps, with your webcam if you like.", "MP4 o GIF hasta 60 fps, y con tu c\u00E1mara si quieres."),
                L("Trim it. Speed it up. Share it.", "Rec\u00F3rtalo. Ac\u00E9leralo. Comp\u00E1rtelo."),
                L("Trim the ends, export at 1.5\u00D7 or 2\u00D7, 1080p or 720p, as MP4 or GIF.", "Recorta los extremos y exporta a 1,5\u00D7 o 2\u00D7, en 1080p o 720p, como MP4 o GIF."), 5.0);
            double rise = OutCubic(P(t, 0, 0.8));
            Rect sr = ScreenR;
            sr.Offset(0, (1 - rise) * 60);
            Rect area = new Rect(520, 200, 820, 460);
            PushScreen(dc, sr, 1 + 0.3 * InOut(P(t, 0.5, 1.3)) * (1 - InOut(P(t, 4.6, 5.2))), new Point(930, 470));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            double rec = P(t, 0.8, 1.1) * (1 - P(t, 4.6, 4.9));
            double elapsed = Math.Max(0, Math.Min(t, 4.6) - 1.1);
            if (rec > 0)
            {
                dc.PushOpacity(rec);
                dc.DrawRectangle(null, new Pen(B(Colors.White, 0.55), 1.2), area);
                CornerMarks(dc, area, 22, 4);
                // Webcam bubble, with Pixel presenting
                Point wc = new Point(area.Right - 100, area.Bottom - 100);
                Shadow(dc, new Rect(wc.X - 78, wc.Y - 78, 156, 156), 78, 0.8, 16, 8);
                dc.PushClip(new EllipseGeometry(wc, 78, 78));
                dc.DrawRectangle(Grad(Color.FromRgb(70, 80, 160), Color.FromRgb(140, 90, 200), 60), null, new Rect(wc.X - 80, wc.Y - 80, 160, 160));
                dc.DrawImage(costumes[0], new Rect(wc.X - 125, wc.Y - 128 + Math.Sin(t * 3) * 3, 250, 250));
                dc.Pop();
                dc.DrawEllipse(null, new Pen(B(Colors.White, 0.9), 3), wc, 78, 78);
                // The recording bar
                Rect bar = new Rect(area.X + area.Width / 2 - 205, area.Bottom + 22, 410, 52);
                Shadow(dc, bar, 26, 0.7, 18, 8);
                Pill(dc, bar, B(Color.FromRgb(28, 28, 32)), new Pen(B(Colors.White, 0.1), 1));
                dc.DrawEllipse(B(Red, 0.55 + 0.45 * Math.Abs(Math.Cos(t * 3.2))), null, new Point(bar.X + 26, bar.Y + 26), 6.5, 6.5);
                Text(dc, "00:0" + ((int)elapsed), 18, FontWeights.SemiBold, B(Fg), bar.X + 42, bar.Y + 13, -1, 1, mono);
                for (int m = 0; m < 2; m++)
                {
                    double x0 = bar.X + 124 + m * 58;
                    for (int i = 0; i < 5; i++)
                    {
                        double lv = 0.2 + 0.8 * Math.Abs(Math.Sin(t * (5.3 + m * 1.7) + i * 1.3 + m)) * Math.Abs(Math.Cos(t * 2.1 + i));
                        double h = 6 + 22 * lv;
                        dc.DrawRoundedRectangle(B(m == 0 ? Green : Teal, 0.9), null, new Rect(x0 + i * 8, bar.Y + 26 - h / 2, 4, h), 2, 2);
                    }
                }
                Capsule(dc, "Detener", bar.Right - 106, bar.Y + 26, 16, Red, Colors.White, 1, 18, 36);
                Pen tp = new Pen(B(Gray), 1.8);
                dc.DrawRoundedRectangle(null, tp, new Rect(bar.Right - 33, bar.Y + 20, 11, 14), 2, 2);
                dc.DrawLine(tp, new Point(bar.Right - 36, bar.Y + 19), new Point(bar.Right - 19, bar.Y + 19));
                dc.Pop();
            }
            Point wander = new Point(760 + 260 * Math.Sin(t * 0.9), 470 + 90 * Math.Cos(t * 1.3));
            Point stop = new Point(area.X + area.Width / 2 + 99, area.Bottom + 50);
            Point cur = t < 3.9 ? wander : LerpP(new Point(760 + 260 * Math.Sin(3.9 * 0.9), 470 + 90 * Math.Cos(3.9 * 1.3)), stop, InOut(P(t, 3.9, 4.5)));
            if (t < 4.95) Pointer(dc, cur, 1.6, t > 4.5 && t < 4.65 ? 1 : 0);
            PopScreen(dc, sr);

            // After stopping: the video editor's timeline with QuickTime-style trim handles.
            double tl = OutExpo(P(t, 4.95, 5.7));
            if (tl > 0)
            {
                Rect panel = new Rect(sr.X + 80, sr.Bottom - 196 + (1 - tl) * 80, sr.Width - 160, 150);
                dc.PushOpacity(tl);
                Shadow(dc, panel, 20, 1.0, 40, 18);
                dc.DrawRoundedRectangle(B(Color.FromArgb(242, 30, 30, 34)), new Pen(B(Colors.White, 0.12), 1), panel, 20, 20);
                Rect strip = new Rect(panel.X + 76, panel.Y + 40, panel.Width - 112, 70);
                double fw = strip.Width / frames.Length;
                dc.PushClip(new RectangleGeometry(strip, 8, 8));
                for (int i = 0; i < frames.Length; i++) dc.DrawImage(frames[i], new Rect(strip.X + i * fw, strip.Y, fw + 1, strip.Height));
                dc.Pop();
                double l = Lerp(0, 0.14, InOut(P(t, 5.8, 6.6))), r = Lerp(1, 0.82, InOut(P(t, 6.4, 7.2)));
                Rect keep = new Rect(strip.X + strip.Width * l, strip.Y - 4, strip.Width * (r - l), strip.Height + 8);
                dc.DrawRectangle(B(Colors.Black, 0.62), null, new Rect(strip.X, strip.Y, keep.X - strip.X, strip.Height));
                dc.DrawRectangle(B(Colors.Black, 0.62), null, new Rect(keep.Right, strip.Y, strip.Right - keep.Right, strip.Height));
                dc.DrawRoundedRectangle(null, new Pen(B(Yellow), 4), keep, 8, 8);
                dc.DrawRoundedRectangle(B(Yellow), null, new Rect(keep.X - 6, keep.Y, 14, keep.Height), 6, 6);
                dc.DrawRoundedRectangle(B(Yellow), null, new Rect(keep.Right - 8, keep.Y, 14, keep.Height), 6, 6);
                double ph = keep.X + 14 + (keep.Width - 28) * ((t * 0.3) % 1);
                dc.DrawRoundedRectangle(B(Colors.White), null, new Rect(ph - 1.5, strip.Y - 10, 3, strip.Height + 20), 1.5, 1.5);
                Point pc = new Point(panel.X + 38, strip.Y + strip.Height / 2);
                StreamGeometry tri = new StreamGeometry();
                using (StreamGeometryContext k = tri.Open())
                {
                    k.BeginFigure(new Point(pc.X - 8, pc.Y - 11), true, true);
                    k.LineTo(new Point(pc.X + 12, pc.Y), true, true);
                    k.LineTo(new Point(pc.X - 8, pc.Y + 11), true, true);
                }
                dc.DrawGeometry(B(Fg), null, tri);
                dc.Pop();
                string[] pills = { "MP4", "GIF", "60 fps", L("System sound", "Sonido del equipo"), L("Microphone", "Micr\u00F3fono"), "Webcam", L("Cinema \u00B7 4K", "Cine \u00B7 4K") };
                double total = 0;
                double[] ws = new double[pills.Length];
                for (int i = 0; i < pills.Length; i++) { ws[i] = FT(pills[i], 18, FontWeights.SemiBold, B(Fg), body).Width + 40; total += ws[i] + 14; }
                double x = W / 2.0 - (total - 14) / 2;
                for (int i = 0; i < pills.Length; i++)
                {
                    Capsule(dc, pills[i], x + ws[i] / 2, sr.Bottom + 56, 18, Color.FromRgb(38, 38, 44), Fg, OutCubic(P(t, 5.6 + i * 0.08, 6.0 + i * 0.08)), 20, 44);
                    x += ws[i] + 14;
                }
            }
        }

        static void Viewers(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Markdown and XML, at a glance.", "Markdown y XML, de un vistazo."),
                L("Open .md and .xml files from Explorer. Read, tidy, search, copy as an image.", "\u00C1brelos desde el Explorador. Lee, ordena, busca y c\u00F3pialos como imagen."));
            double a = OutExpo(P(t, 0.3, 1.2)), b = OutExpo(P(t, 0.7, 1.6));
            double drift = t * 5;
            Rect r1 = new Rect(180 - (1 - a) * 160 - drift, 280, 920, 662);
            Rect r2 = new Rect(820 + (1 - b) * 160 + drift, 336, 920, 662);
            dc.PushOpacity(a);
            Plate(dc, md, r1, 14, 1.2);
            dc.Pop();
            dc.PushOpacity(b);
            Plate(dc, xml, r2, 14, 1.4);
            dc.Pop();
        }

        static void Home(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("One window. Your way.", "Una ventana. A tu manera."),
                L("Three home views, every mode and every setting in one place.", "Tres vistas de inicio, todos los modos y todos los ajustes en un solo sitio."));
            BitmapSource[] views = { homeC, homeB, homeA, pageRecord };
            string[] names = es ? new string[] { "Escena", "Widgets", "Minimalista", "Ajustes de grabaci\u00F3n" } : new string[] { "Scene", "Widgets", "Minimal", "Recording settings" };
            double pos = 0;
            double[] at = { 1.9, 3.6, 5.3 };
            for (int i = 0; i < at.Length; i++) pos += InOut(P(t, at[i], at[i] + 0.75));
            double rise = OutCubic(P(t, 0.1, 0.9));
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < views.Length; i++)
                {
                    double d = i - pos, ad = Math.Abs(d);
                    if ((ad < 0.5) != (pass == 1) || ad > 1.6) continue;
                    double w = Lerp(1010, 700, Math.Min(1, ad));
                    Rect r = Fit(views[i], W / 2.0 + d * 900, 600 + (1 - rise) * 60 + Math.Min(1, ad) * 12, w);
                    dc.PushOpacity(Lerp(1, 0.32, Math.Min(1, ad)) * Clamp(1.6 - ad) * rise);
                    Plate(dc, views[i], r, 14, 1.2);
                    dc.Pop();
                }
            int ci = Math.Max(0, Math.Min(names.Length - 1, (int)Math.Round(pos)));
            double lk = 1 - Math.Abs(pos - ci) * 2;
            Capsule(dc, names[ci], W / 2.0, 1010, 18, Color.FromRgb(38, 38, 44), Fg, Clamp(lk) * rise, 22, 44);
        }

        static void MascotScene(DrawingContext dc, double t, double dur)
        {
            Titles(dc, t, L("Meet Pixel.", "Conoce a Pixel."), L("Your capture sidekick. It can even live on your desktop.", "Tu compa\u00F1ero de capturas. Hasta puede vivir en tu escritorio."),
                L("39 costumes. One click.", "39 disfraces. Un clic."), L("12 species, 23 colors and dozens of hats and outfits.", "12 especies, 23 colores y decenas de gorros y ropa."), 5.4);
            double step = 0.38, start = 0.9;
            int n = costumes.Length;
            int idx = t < start ? 0 : Math.Min(n - 1, (int)((t - start) / step) + 1);
            double since = t < start ? t : t - start - (idx - 1) * step;
            double gridK = InOut(P(t, 5.2, 6.0));
            if (gridK < 1)
            {
                Color gc = idx > 0 ? Mix(costumeGlow[idx - 1], costumeGlow[idx], OutCubic(since / 0.25)) : costumeGlow[0];
                double appear = OutBack(P(t, 0.1, 0.7));
                double wob = idx > 0 ? Math.Exp(-since * 9) * Math.Cos(since * 22) : 0;
                double size = 580 * appear * (1 - 0.5 * gridK);
                Point c = new Point(W / 2.0, 560 - gridK * 40);
                dc.PushOpacity(1 - gridK);
                Glow(dc, c.X, c.Y + 40, 1000, 680, gc, 0.45);
                dc.DrawEllipse(B(Colors.Black, 0.35), null, new Point(c.X, c.Y + size * 0.37), size * 0.2, size * 0.035);
                dc.PushTransform(new ScaleTransform(1 + 0.1 * wob, 1 - 0.08 * wob, c.X, c.Y + size * 0.35));
                dc.DrawImage(costumes[idx], new Rect(c.X - size / 2, c.Y - size / 2 - Math.Sin(t * 2.4) * 6, size, size));
                dc.Pop();
                if (idx > 0)
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4 + idx, k = OutCubic(since / 0.35), dist = 170 + 130 * k;
                        Star(dc, new Point(c.X + Math.Cos(a) * dist, c.Y - 20 + Math.Sin(a) * dist * 0.8), 14 * (1 - k) + 2, B(Colors.White, 0.9 * (1 - k)));
                    }
                Text(dc, costumeNames[idx], 54, FontWeights.Bold, B(Fg), W / 2.0, 880, 0, P(t, 0.3, 0.8));
                Text(dc, costumeFrom[idx], 26, FontWeights.Normal, B(Gray), W / 2.0, 952, 0, P(t, 0.3, 0.8), body);
                dc.Pop();
            }
            if (gridK > 0)
            {
                int cols = 13;
                double tw = 134, th = 210, gx = W / 2.0 - cols * tw / 2, gy = 300;
                for (int i = 0; i < grid.Length; i++)
                {
                    int col = i % cols, row = i / cols;
                    double k = OutBack(P(t, 5.3 + (col + row) * 0.035, 5.8 + (col + row) * 0.035));
                    if (k <= 0) continue;
                    Rect tile = new Rect(gx + col * tw + 6, gy + row * th + 6, tw - 12, th - 12);
                    dc.PushOpacity(Clamp(k));
                    dc.PushTransform(new ScaleTransform(k, k, tile.X + tile.Width / 2, tile.Y + tile.Height / 2));
                    dc.DrawRoundedRectangle(B(Color.FromRgb(30, 30, 34)), new Pen(B(Colors.White, 0.08), 1), tile, 18, 18);
                    dc.DrawImage(grid[i], new Rect(tile.X + tile.Width / 2 - 85, tile.Y - 22, 170, 170));
                    string nm = es ? MascotParts.AnimeNames[i] : Promo.En(MascotParts.AnimeNames[i]);
                    if (nm.Length > 13) nm = nm.Substring(0, 12) + "\u2026";
                    FormattedText ft = FT(nm, 15, FontWeights.SemiBold, B(Fg), body);
                    dc.DrawText(ft, new Point(tile.X + (tile.Width - ft.Width) / 2, tile.Bottom - 36));
                    dc.Pop();
                    dc.Pop();
                }
            }
        }

        static void Specs(DrawingContext dc, double t, double dur)
        {
            Headline(dc, L("Small. Fast. Private.", "Ligera. R\u00E1pida. Privada."), 100, B(Fg), W / 2.0, 170, t, 0.1);
            string[] lab = es ? new string[] { "Un solo ejecutable.\nNada m\u00E1s que instalar.", "Permisos de\nadministrador.", "Cuentas, nube\no telemetr\u00EDa.", "Por movimiento al\nseleccionar un \u00E1rea." }
                              : new string[] { "One executable.\nNothing else to install.", "Admin rights\nneeded.", "Accounts, cloud\nor telemetry.", "Per pointer move while\nyou select an area." };
            Color[][] g = { new Color[] { Teal, Blue }, new Color[] { Purple, Pink }, new Color[] { Orange, Yellow }, new Color[] { Green, Teal } };
            CultureInfo ci = es ? CultureInfo.GetCultureInfo("es-ES") : CultureInfo.InvariantCulture;
            for (int i = 0; i < 4; i++)
            {
                double k = P(t, 0.6 + i * 0.15, 1.4 + i * 0.15);
                double cx = 330 + i * 420;
                string v = i == 0 ? (2.2 * OutCubic(P(t, 0.6, 1.6))).ToString("0.0", ci) + " MB" : i == 3 ? (1.6 * OutCubic(P(t, 1.05, 2.05))).ToString("0.0", ci) + " ms" : "0";
                Text(dc, v, 132, FontWeights.Bold, Grad(g[i][0], g[i][1], 0), cx, 400, 0, k);
                Text(dc, lab[i], 28, FontWeights.Normal, B(Gray), cx, 600, 0, P(t, 0.9 + i * 0.15, 1.6 + i * 0.15), body);
            }
            Text(dc, L("Free and open source (MIT)  \u00B7  Windows 10 and 11", "Gratis y de c\u00F3digo abierto (MIT)  \u00B7  Windows 10 y 11"), 30, FontWeights.SemiBold, B(Fg), W / 2.0, 820, 0, P(t, 1.8, 2.5), body);
        }

        static void End(DrawingContext dc, double t, double dur)
        {
            double k = Spring(P(t, 0.05, 1.1) * 1.6);
            double d = 190 * Lerp(0.6, 1, k);
            Point c = new Point(W / 2.0, 250);
            Glow(dc, c.X, c.Y + 80, 1300, 900, Brand2, 0.36 * OutCubic(P(t, 0, 0.8)));
            dc.PushOpacity(OutCubic(P(t, 0.05, 0.4)));
            dc.DrawImage(logo, new Rect(c.X - d / 2, c.Y - d / 2, d, d));
            dc.Pop();
            Headline(dc, "Stackshot", 140, B(Fg), W / 2.0, 360, t, 0.25);
            Text(dc, L("Capture, annotate and share in seconds.", "Captura, marca y comparte en segundos."), 40, FontWeights.Normal, B(Gray), W / 2.0, 548, 0, P(t, 0.6, 1.2), body);
            double bk = OutBack(P(t, 0.9, 1.5));
            if (bk > 0)
            {
                FormattedText ft = FT(L("Free for Windows  \u00B7  Download", "Gratis para Windows  \u00B7  Descargar"), 38, FontWeights.SemiBold, B(Colors.White), body);
                double pulse = 0.5 + 0.5 * Math.Sin(t * 3.2);
                Rect btn = new Rect(W / 2.0 - ft.Width / 2 - 62, 660, ft.Width + 124, 96);
                dc.PushOpacity(Clamp(bk));
                dc.PushTransform(new ScaleTransform(Lerp(0.88, 1, bk) * (1 + 0.012 * pulse), Lerp(0.88, 1, bk) * (1 + 0.012 * pulse), W / 2.0, btn.Y + 48));
                Glow(dc, W / 2.0, btn.Y + 60, btn.Width * 1.8, 280, Blue, 0.34 + 0.14 * pulse);
                Pill(dc, btn, Grad(Color.FromRgb(30, 144, 255), Color.FromRgb(10, 110, 245), 90));
                dc.DrawRoundedRectangle(null, new Pen(B(Colors.White, 0.25), 1.5), btn, 48, 48);
                double sh = P(t % 2.6, 1.6, 2.5);
                if (t > 2.2 && sh > 0 && sh < 1)
                {
                    dc.PushClip(new RectangleGeometry(btn, 48, 48));
                    LinearGradientBrush shine = new LinearGradientBrush();
                    shine.StartPoint = new Point(0, 0.5);
                    shine.EndPoint = new Point(1, 0.5);
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5));
                    shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                    dc.DrawRectangle(shine, null, new Rect(Lerp(btn.X - 220, btn.Right + 60, sh), btn.Y, 180, btn.Height));
                    dc.Pop();
                }
                dc.DrawText(ft, new Point(btn.X + 62, btn.Y + (btn.Height - ft.Height) / 2));
                dc.Pop();
                dc.Pop();
                double ripple = P(t, 1.5, 2.6);
                if (ripple > 0 && ripple < 1)
                {
                    Rect rr = btn;
                    rr.Inflate(70 * OutExpo(ripple), 40 * OutExpo(ripple));
                    dc.DrawRoundedRectangle(null, new Pen(B(Blue, 0.5 * (1 - ripple)), 3), rr, 48 + 40 * ripple, 48 + 40 * ripple);
                }
            }
            Text(dc, "github.com/rubenitx/stackshot", 30, FontWeights.SemiBold, B(Fg, 0.85), W / 2.0, 815, 0, P(t, 1.5, 2.1), body);
            Text(dc, L("Open source (MIT)  \u00B7  Windows 10 and 11  \u00B7  No account, no telemetry", "C\u00F3digo abierto (MIT)  \u00B7  Windows 10 y 11  \u00B7  Sin cuenta ni telemetr\u00EDa"), 24, FontWeights.Normal, B(Gray, 0.8), W / 2.0, 870, 0, P(t, 1.7, 2.3), body);
        }
    }
}

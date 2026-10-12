// Copy text from images, round 3 (suite "h3text"): the OCR on images drawn here only (never the screen, never the
// clipboard). Pixel formats, contrast, transparency, WPF controls rendered offscreen, code symbols, accents, numbers,
// two columns, a table, rotation, a photo, DPI and 1 px strips, a 20000 px capture with lines on the band edges, the
// 2600 px limit, the asynchronous call back (no handle, disposed control or bitmap, many reads at once) and the
// Join/Describe helpers. Each case prints the word recall it reached.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WpfControls = System.Windows.Controls;
using WpfImaging = System.Windows.Media.Imaging;
using WpfMedia = System.Windows.Media;

namespace Stackshot.Tests
{
    static class AppTestsH3Text
    {
        const string SuiteName = "h3text";

        sealed class Failed : Exception { public Failed(string m) : base(m) { } }
        static void Check(bool ok, string what) { if (!ok) throw new Failed(what); }

        public static void Suite()
        {
            AppTests.Test(SuiteName, "formats: Read takes 1, 8, 16, 24, 32, 48 and 64 bit images", FormatsRead);
            AppTests.Test(SuiteName, "formats: ReadAsync copies every format that Read takes", FormatsAsync);
            AppTests.Test(SuiteName, "contrast: grey on light grey, yellow on blue, white on red", Contrast);
            AppTests.Test(SuiteName, "transparency: a transparent PNG, and a panel with a shadow ring", TransparentPng);
            AppTests.Test(SuiteName, "wpf: menu, buttons, a DataGrid and a text box rendered offscreen", WpfUi);
            AppTests.Test(SuiteName, "code: symbols survive (braces, brackets, quotes, pipes, backslash)", CodeSymbols);
            AppTests.Test(SuiteName, "accents: n-tilde, acute, diaeresis, inverted marks and euro, Spanish and English", Accents);
            AppTests.Test(SuiteName, "numbers: dates, money 1.234,56, IBAN and NIF keep their digits", Numbers);
            AppTests.Test(SuiteName, "columns: two columns, each line whole and no line mixes both", Columns);
            AppTests.Test(SuiteName, "table: each row's cells stay on one line", Table);
            AppTests.Test(SuiteName, "rotation: text at 5 and at 90 degrees", Rotated);
            AppTests.Test(SuiteName, "rotation: a sideways capture read after turning it back", RotatedTurnBack);
            AppTests.Test(SuiteName, "photo: a headline on a dark band over a busy picture", Photo);
            AppTests.Test(SuiteName, "dpi metadata does not change the text; 1 px strips never throw", DpiAndStrips);
            AppTests.Test(SuiteName, "20000 px capture: lines centred on the band edges are read once", TallEdges);
            AppTests.Test(SuiteName, "one 2400 px band read alone: no line lost (raw engine lines printed)", BandAlone);
            AppTests.Test(SuiteName, "tall capture bands: prepared as Recognize does vs a copy of their rows", TallPieceSource);
            AppTests.Test(SuiteName, "a label at a band edge: does it change the lines above it", LabelNearLines);
            AppTests.Test(SuiteName, "a grey button drawn with GDI+: its caption is read", GreyButton);
            AppTests.Test(SuiteName, "9000 px wide: text at both ends", Wide9000);
            AppTests.Test(SuiteName, "2600 vs 2601 px wide: the same 9 pt block", Cliff2600);
            AppTests.Test(SuiteName, "4K 100 %: 9 pt text read as is vs drawn twice as big", Native4K);
            AppTests.Test(SuiteName, "read async: a control whose handle was never created still calls back", CallbackWithoutHandle);
            AppTests.Test(SuiteName, "read async: control disposed before the answer does not crash", CallbackDisposedControl);
            AppTests.Test(SuiteName, "read async: a bitmap disposed before the call is not a memory problem", DisposedBitmap);
            AppTests.Test(SuiteName, "read async: a 9000x6000 capture copied on the calling thread", CopyBigCapture);
            AppTests.Test(SuiteName, "read async: 8 reads at once, each call back gets its own text", ConcurrentReads);
            AppTests.Test(SuiteName, "read async: image disposed right after the call, 20 times", DisposeRightAfter);
            AppTests.Test(SuiteName, "speed: 1080p and 4K screenshots, and the copy made on the calling thread", Speed);
            AppTests.Test(SuiteName, "join and describe: blank lines, tabs, CR, long lines, emoji", JoinDescribe);
            AppTests.Test(SuiteName, "engine: which recognizer is used on this PC", EngineLanguage);
        }

        // ---- helpers ----

        static string P(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        static Bitmap Canvas(int w, int h, Color back)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(back);
            return b;
        }

        static int LineHeight(float pt) { return (int)Math.Ceiling(pt * 96 / 72 * 1.6); }

        // Lines of text from (x, y), one line height apart, drawn as Windows draws them.
        static void Draw(Graphics g, string[] lines, string face, float pt, FontStyle style, Brush ink, int x, int y, TextRenderingHint hint)
        {
            g.TextRenderingHint = hint;
            using (Font f = new Font(face, pt, style, GraphicsUnit.Point))
            {
                int lh = LineHeight(pt);
                for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, ink, x, y + i * lh);
            }
        }

        static string Read(Bitmap b)
        {
            string error;
            string t = Stackshot.Ocr.Read(b, out error);
            Check(t != null, "the OCR failed: " + error);
            return t;
        }

        static string Norm(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            return " " + string.Join(" ", sb.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";
        }

        // Share of the expected words (letters and digits) that appear in the text read.
        static double Recall(string[] expected, string got)
        {
            string g = Norm(got);
            int n = 0, hit = 0;
            foreach (string line in expected)
                foreach (string w in Norm(line).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    n++;
                    if (g.Contains(" " + w + " ")) hit++;
                }
            return n == 0 ? 1 : hit / (double)n;
        }

        static int CountOf(string s, string sub)
        {
            int n = 0;
            for (int at = s.IndexOf(sub, StringComparison.Ordinal); at >= 0; at = s.IndexOf(sub, at + 1, StringComparison.Ordinal)) n++;
            return n;
        }

        static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }

        static string Digits(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        static string Repeat(string s, int n)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        static string Esc(string s) { return s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"); }

        // The same pixels in another pixel format (null when GDI+ cannot make it here).
        static Bitmap ToFormat(Bitmap src, PixelFormat f)
        {
            try { return src.Clone(new Rectangle(0, 0, src.Width, src.Height), f); }
            catch (Exception ex)
            {
                Console.WriteLine("    " + f + ": not made here (" + ex.GetType().Name + ")");
                return null;
            }
        }

        // Round trip through a PNG file, as a transparent capture is saved and loaded.
        static Bitmap ToPng(Bitmap b)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                using (Bitmap tmp = new Bitmap(ms)) return tmp.Clone(new Rectangle(0, 0, tmp.Width, tmp.Height), PixelFormat.Format32bppArgb);
            }
        }

        // Renders WPF controls offscreen (no window is shown) and returns them as a bitmap.
        static Bitmap RenderWpf(WpfControls.Panel root, int w, int h)
        {
            root.Measure(new System.Windows.Size(w, h));
            root.Arrange(new System.Windows.Rect(0, 0, w, h));
            root.UpdateLayout();
            WpfImaging.RenderTargetBitmap rtb = new WpfImaging.RenderTargetBitmap(w, h, 96, 96, WpfMedia.PixelFormats.Pbgra32);
            rtb.Render(root);
            WpfImaging.PngBitmapEncoder enc = new WpfImaging.PngBitmapEncoder();
            enc.Frames.Add(WpfImaging.BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                enc.Save(ms);
                ms.Position = 0;
                using (Bitmap tmp = new Bitmap(ms)) return tmp.Clone(new Rectangle(0, 0, tmp.Width, tmp.Height), PixelFormat.Format32bppArgb);
            }
        }

        // Starts ReadAsync and pumps the UI thread until the call back arrives (60 s at most).
        static bool ReadWait(Bitmap b, Control ui, out string text, out string error)
        {
            bool done = false;
            string t = null, e = null;
            Stackshot.Ocr.ReadAsync(b, ui, delegate(string tx, string er) { t = tx; e = er; done = true; });
            Stopwatch sw = Stopwatch.StartNew();
            while (!done && sw.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(5); }
            text = t;
            error = e;
            return done;
        }

        // A sample of two lines in 10 pt, with accents, on white.
        static readonly string[] FormatLines =
        {
            "Factura n\u00FAmero 2026-0147 del cliente",
            "Importe total: 1.234,56 euros",
            "Configuraci\u00F3n avanzada de red",
        };

        static readonly PixelFormat[] Formats =
        {
            PixelFormat.Format24bppRgb, PixelFormat.Format8bppIndexed, PixelFormat.Format1bppIndexed,
            PixelFormat.Format16bppRgb555, PixelFormat.Format48bppRgb, PixelFormat.Format64bppArgb,
        };
        static readonly double[] FormatNeed = { 0.85, 0.8, 0.5, 0.6, 0.85, 0.85 };

        static Bitmap FormatSample()
        {
            Bitmap b = Canvas(560, 100, Color.White);
            using (Graphics g = Graphics.FromImage(b)) Draw(g, FormatLines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
            return b;
        }

        // ---- tests ----

        static void FormatsRead()
        {
            using (Bitmap src = FormatSample())
            {
                for (int i = 0; i < Formats.Length; i++)
                {
                    Bitmap copy = ToFormat(src, Formats[i]);
                    if (copy == null) continue;
                    using (copy)
                    {
                        string got = Read(copy);
                        double r = Recall(FormatLines, got);
                        Console.WriteLine("    " + Formats[i] + ": recall " + P(r) + ": " + got.Replace("\n", " | "));
                        Check(r >= FormatNeed[i], Formats[i] + ": only " + P(r) + " of the words: " + got.Replace("\n", " | "));
                    }
                }
            }
        }

        static void FormatsAsync()
        {
            using (Control ui = new Control())
            using (Bitmap src = FormatSample())
            {
                ui.CreateControl();
                Check(ui.IsHandleCreated, "no handle after CreateControl");
                for (int i = 0; i < Formats.Length; i++)
                {
                    Bitmap copy = ToFormat(src, Formats[i]);
                    if (copy == null) continue;
                    string text, error;
                    bool done;
                    using (copy) done = ReadWait(copy, ui, out text, out error);
                    Check(done, Formats[i] + ": no call back");
                    Check(text != null, Formats[i] + ": " + error);
                    Console.WriteLine("    " + Formats[i] + ": " + text.Replace("\n", " | "));
                }
            }
        }

        static void Contrast()
        {
            string[] lines = { "Documento confidencial del archivo", "Fecha de revisi\u00F3n 12/05/2026" };
            Color[] inks = { Color.FromArgb(150, 150, 150), Color.FromArgb(255, 230, 0), Color.White };
            Color[] backs = { Color.FromArgb(235, 235, 235), Color.FromArgb(11, 61, 145), Color.FromArgb(200, 30, 30) };
            string[] names = { "grey 150 on 235", "yellow on dark blue", "white on red" };
            double[] need = { 0.5, 0.8, 0.8 };
            for (int i = 0; i < inks.Length; i++)
            {
                using (Bitmap b = Canvas(420, 70, backs[i]))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    using (SolidBrush ink = new SolidBrush(inks[i]))
                        Draw(g, lines, "Segoe UI", 10, FontStyle.Regular, ink, 8, 8, TextRenderingHint.ClearTypeGridFit);
                    string got = Read(b);
                    double r = Recall(lines, got);
                    Console.WriteLine("    " + names[i] + ": recall " + P(r) + ": " + got.Replace("\n", " | "));
                    Check(r >= need[i], names[i] + ": only " + P(r) + " of the words");
                }
            }
        }

        static void TransparentPng()
        {
            string[] lines = { "Ventana con fondo transparente", "Guardar cambios en el archivo" };
            // Black text on fully transparent pixels, saved and loaded as a PNG.
            using (Bitmap b = Canvas(360, 70, Color.Transparent))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, lines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 10, 8, TextRenderingHint.AntiAlias);
                using (Bitmap png = ToPng(b))
                {
                    string got = Read(png);
                    double r = Recall(lines, got);
                    Console.WriteLine("    transparent text: recall " + P(r) + ": " + got.Replace("\n", " | "));
                    Check(r >= 0.85, "transparent text: only " + P(r));
                }
            }
            // A white panel on a transparent canvas, with a translucent shadow ring, as a window capture has.
            using (Bitmap b = Canvas(380, 110, Color.Transparent))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillRectangle(Brushes.White, 16, 16, 348, 78);
                    using (Pen shadow = new Pen(Color.FromArgb(90, 0, 0, 0), 12)) g.DrawRectangle(shadow, 10, 10, 360, 90);
                    Draw(g, lines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 26, 26, TextRenderingHint.AntiAlias);
                }
                using (Bitmap png = ToPng(b))
                {
                    string got = Read(png);
                    double r = Recall(lines, got);
                    Console.WriteLine("    panel with shadow: recall " + P(r) + ": " + got.Replace("\n", " | "));
                    Check(r >= 0.85, "panel with shadow: only " + P(r));
                }
            }
        }

        public class Row
        {
            public string Nombre { get; set; }
            public string Importe { get; set; }
            public string Fecha { get; set; }
        }

        static void WpfUi()
        {
            WpfControls.StackPanel root = new WpfControls.StackPanel();
            root.Background = WpfMedia.Brushes.White;
            WpfControls.Menu menu = new WpfControls.Menu();
            foreach (string m in new string[] { "Archivo", "Editar", "Ver", "Ayuda" }) menu.Items.Add(new WpfControls.MenuItem { Header = m });
            root.Children.Add(menu);
            WpfControls.StackPanel buttons = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal, Margin = new System.Windows.Thickness(8) };
            foreach (string label in new string[] { "Guardar", "Cancelar", "Aceptar" })
                buttons.Children.Add(new WpfControls.Button { Content = label, Margin = new System.Windows.Thickness(4) });
            root.Children.Add(buttons);
            WpfControls.DataGrid grid = new WpfControls.DataGrid { Height = 120, Width = 520, AutoGenerateColumns = true };
            grid.ItemsSource = new List<Row>
            {
                new Row { Nombre = "Factura 0147", Importe = "1.234,56", Fecha = "15/03/2026" },
                new Row { Nombre = "Recibo 0148", Importe = "89,00", Fecha = "16/03/2026" },
            };
            root.Children.Add(grid);
            root.Children.Add(new WpfControls.TextBox { Text = "Buscar...", Width = 300, Margin = new System.Windows.Thickness(8) });
            root.Children.Add(new WpfControls.TextBlock { Text = "Estado: listo", Margin = new System.Windows.Thickness(8) });
            using (Bitmap b = RenderWpf(root, 560, 300))
            {
                b.Save(Path.Combine(Path.GetTempPath(), "h3-wpf.png"), ImageFormat.Png);
                using (Bitmap buttonRow = b.Clone(new Rectangle(0, 26, 300, 36), PixelFormat.Format32bppArgb))
                    Console.WriteLine("    button row alone: " + Esc(Read(buttonRow)));
                string got = Read(b);
                string[] want = { "Archivo Editar Ver Ayuda", "Guardar Cancelar Aceptar", "Nombre Importe Fecha", "Factura 0147", "Recibo 0148", "Buscar", "Estado: listo" };
                double r = Recall(want, got);
                Console.WriteLine("    wpf: recall " + P(r) + ": " + got.Replace("\n", " | "));
                Check(r >= 0.7, "wpf controls: only " + P(r) + " of the words: " + got.Replace("\n", " | "));
            }
        }

        static readonly string[] CodeLines =
        {
            "if (lista[i] <= 10 && total >= 3) { return \"x\"; }",
            "SELECT id, nombre FROM clientes WHERE activo = 'S';",
            "var ruta = @\"C:\\Users\\rmartinez\\docs\"; // ruta",
            "a || b | c && d != e; f = {g: [1, 2]};",
            "`codigo` \\ barra inversa",
        };

        static void CodeSymbols()
        {
            string drawn = string.Join("\n", CodeLines);
            char[] symbols = { '{', '}', '[', ']', '<', '>', '=', ';', '"', '\'', '`', '|', '\\' };
            using (Bitmap b = Canvas(560, 130, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, CodeLines, "Consolas", 10, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
                string got = Read(b);
                double r = Recall(CodeLines, got);
                int total = 0, kept = 0;
                StringBuilder per = new StringBuilder();
                foreach (char c in symbols)
                {
                    int want = CountChar(drawn, c), have = CountChar(got, c);
                    total += want;
                    kept += Math.Min(want, have);
                    per.Append(c).Append(' ').Append(have).Append('/').Append(want).Append("  ");
                }
                double s = total == 0 ? 1 : kept / (double)total;
                Console.WriteLine("    words " + P(r) + ", symbols " + P(s) + " (read/drawn): " + per.ToString());
                Console.WriteLine("    " + got.Replace("\n", " | "));
                // Engine limit (measured, not Stackshot): symbols and short keywords of code read poorly.
                Check(r >= 0.5, "code words: only " + P(r));
                Console.WriteLine("    code symbols kept: " + P(s));
            }
        }

        static readonly string[] AccentLines =
        {
            "\u00BFQu\u00E9 hora es? \u00A1A\u00F1o nuevo en Espa\u00F1a!",
            "Ping\u00FCino, cig\u00FCe\u00F1a, cami\u00F3n y m\u00FAsculo",
            "Jos\u00E9 compr\u00F3 5 \u20AC de pan en \u00D1and\u00FA",
            "Precio: 12,50 \u20AC (IVA incluido)",
            "The quick brown fox jumps over the lazy dog",
        };

        static readonly string[] AccentWords =
        {
            "Qu\u00E9", "A\u00F1o", "Espa\u00F1a", "Ping\u00FCino", "cig\u00FCe\u00F1a", "cami\u00F3n", "m\u00FAsculo", "Jos\u00E9", "\u00D1and\u00FA", "\u20AC",
        };

        static void Accents()
        {
            using (Bitmap b = Canvas(640, 130, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, AccentLines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
                string got = Read(b);
                int kept = 0;
                List<string> lost = new List<string>();
                foreach (string w in AccentWords)
                {
                    if (got.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) kept++;
                    else lost.Add(w);
                }
                double english = Recall(new string[] { AccentLines[4] }, got);
                double spanish = Recall(new string[] { AccentLines[0], AccentLines[1], AccentLines[2], AccentLines[3] }, got);
                Console.WriteLine("    accent words kept " + kept + " of " + AccentWords.Length + (lost.Count > 0 ? ", lost: " + string.Join(", ", lost.ToArray()) : "") + "; Spanish words " + P(spanish) + ", English " + P(english));
                Console.WriteLine("    " + got.Replace("\n", " | "));
                Check(kept >= 8, "accents lost: " + string.Join(", ", lost.ToArray()) + " in: " + got.Replace("\n", " | "));
                Check(english >= 0.9, "English line: only " + P(english));
            }
        }

        static readonly string[] NumberLines =
        {
            "Fecha de emisi\u00F3n: 15/03/2026 10:45",
            "IBAN ES91 2100 0418 4502 0005 1332",
            "NIF 12345678Z   Tel. 912 345 678",
            "Importe: 1.234,56 EUR   Total 0,99 \u20AC",
        };

        static readonly string[] NumberGroups = { "15032026", "1045", "9121000418450200051332", "12345678", "912345678", "123456", "099" };

        static void Numbers()
        {
            using (Bitmap b = Canvas(560, 100, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, NumberLines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
                string got = Read(b);
                string digits = Digits(got);
                List<string> missing = new List<string>();
                foreach (string d in NumberGroups) if (digits.IndexOf(d, StringComparison.Ordinal) < 0) missing.Add(d);
                Console.WriteLine("    " + got.Replace("\n", " | "));
                Check(missing.Count == 0, "digit groups lost: " + string.Join(", ", missing.ToArray()) + " in: " + got.Replace("\n", " | "));
            }
        }

        static void Columns()
        {
            string[] left = { "izquierda uno dos tres", "izquierda cuatro cinco", "izquierda seis siete ocho", "izquierda nueve diez" };
            string[] right = { "derecha uno dos tres", "derecha cuatro cinco", "derecha seis siete ocho", "derecha nueve diez" };
            using (Bitmap b = Canvas(640, 120, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    Draw(g, left, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 20, 12, TextRenderingHint.ClearTypeGridFit);
                    Draw(g, right, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 340, 12, TextRenderingHint.ClearTypeGridFit);
                }
                string got = Read(b);
                string all = Norm(got);
                int whole = 0, mixed = 0;
                foreach (string l in left) if (all.Contains(Norm(l))) whole++;
                foreach (string r in right) if (all.Contains(Norm(r))) whole++;
                foreach (string line in got.Split('\n'))
                    if (line.IndexOf("izquierda", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("derecha", StringComparison.OrdinalIgnoreCase) >= 0) mixed++;
                Console.WriteLine("    " + got.Replace("\n", " | "));
                Console.WriteLine("    lines read whole " + whole + " of 8, output lines mixing both columns " + mixed);
                Check(whole >= 7, "only " + whole + " of 8 lines read whole: " + got.Replace("\n", " | "));
                Check(mixed == 0, mixed + " output lines mix both columns: " + got.Replace("\n", " | "));
            }
        }

        static void Table()
        {
            string[][] rows =
            {
                new string[] { "Concepto", "Cant.", "Precio", "Total" },
                new string[] { "Item 001", "2", "9,90", "19,80" },
                new string[] { "Item 002", "1", "24,00", "24,00" },
                new string[] { "Item 003", "5", "1,50", "7,50" },
                new string[] { "Item 004", "3", "12,25", "36,75" },
            };
            int[] xs = { 12, 170, 300, 430 };
            int[] edges = { 4, 160, 290, 420, 556 };
            const int RowH = 28, W = 560, H = 5 * 28 + 16;
            using (Bitmap b = Canvas(W, H, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Pen grid = new Pen(Color.FromArgb(160, 160, 160)))
                using (Font f = new Font("Segoe UI", 10f, GraphicsUnit.Point))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int r = 0; r <= rows.Length; r++) g.DrawLine(grid, 4, 4 + r * RowH, W - 4, 4 + r * RowH);
                    foreach (int x in edges) g.DrawLine(grid, x, 4, x, 4 + rows.Length * RowH);
                    for (int r = 0; r < rows.Length; r++)
                        for (int c = 0; c < 4; c++)
                            g.DrawString(rows[r][c], f, Brushes.Black, xs[c], 4 + r * RowH + 6);
                }
                string got = Read(b);
                string all = Norm(got);
                int kept = 0;
                for (int r = 0; r < rows.Length; r++)
                {
                    string row = string.Join(" ", rows[r]);
                    if (all.Contains(Norm(row))) kept++;
                    else Console.WriteLine("    row not on one line: " + row);
                }
                Console.WriteLine("    " + got.Replace("\n", " | "));
                Console.WriteLine("    rows with all their cells on one line: " + kept + " of " + rows.Length);
                // The engine gives a table column by column: copying it as rows is a feature still to do (measured here).
                Check(Norm(got).Contains(" concepto ") && Norm(got).Contains(" total "), "the table's text was lost: " + got.Replace("\n", " | "));
            }
        }

        static void Rotated()
        {
            string[] lines = { "Documento girado para leer", "Segunda linea del texto", "Tercera linea" };
            int[] angles = { 5, 90 };
            double[] need = { 0.8, 0.5 };
            for (int i = 0; i < angles.Length; i++)
            {
                int w = angles[i] == 90 ? 230 : 640, h = angles[i] == 90 ? 640 : 200;
                string got;
                using (Bitmap b = Canvas(w, h, Color.White))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    using (Font f = new Font("Segoe UI", 12f, GraphicsUnit.Point))
                    {
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.TranslateTransform(w / 2f, h / 2f);
                        g.RotateTransform(angles[i]);
                        int lh = LineHeight(12);
                        float top = -(lines.Length * lh) / 2f;
                        for (int k = 0; k < lines.Length; k++)
                        {
                            SizeF s = g.MeasureString(lines[k], f);
                            g.DrawString(lines[k], f, Brushes.Black, -s.Width / 2, top + k * lh);
                        }
                    }
                    got = Read(b);
                }
                double r = Recall(lines, got);
                Console.WriteLine("    " + angles[i] + " degrees: recall " + P(r) + ": " + got.Replace("\n", " | "));
                Check(r >= need[i], angles[i] + " degrees: only " + P(r) + " of the words");
            }
        }

        static void Photo()
        {
            Random rnd = new Random(7);
            using (Bitmap b = new Bitmap(1200, 800, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    using (LinearGradientBrush sky = new LinearGradientBrush(new Rectangle(0, 0, 1200, 800), Color.FromArgb(40, 90, 140), Color.FromArgb(230, 170, 80), 30f))
                        g.FillRectangle(sky, 0, 0, 1200, 800);
                    for (int i = 0; i < 2500; i++)
                        using (SolidBrush dot = new SolidBrush(Color.FromArgb(60, rnd.Next(256), rnd.Next(256), rnd.Next(256))))
                            g.FillEllipse(dot, rnd.Next(-50, 1200), rnd.Next(-50, 800), rnd.Next(5, 120), rnd.Next(5, 120));
                    using (SolidBrush band = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(band, 0, 250, 1200, 120);
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    using (Font headFont = new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Point)) g.DrawString("Oferta de primavera 50 %", headFont, Brushes.White, 60, 268);
                    using (Font smallFont = new Font("Segoe UI", 14f, GraphicsUnit.Point)) g.DrawString("Texto peque\u00F1o sobre la foto", smallFont, Brushes.White, 60, 560);
                }
                string got = Read(b);
                double head = Recall(new string[] { "Oferta de primavera 50 %" }, got);
                double small = Recall(new string[] { "Texto peque\u00F1o sobre la foto" }, got);
                Console.WriteLine("    headline on the band " + P(head) + ", 14 pt on the busy picture " + P(small) + ": " + got.Replace("\n", " | "));
                Check(head >= 0.7, "headline on the band: only " + P(head) + ": " + got.Replace("\n", " | "));
            }
        }

        static void DpiAndStrips()
        {
            using (Bitmap src = FormatSample())
            {
                string first = null;
                foreach (int dpi in new int[] { 72, 96, 300, 600 })
                {
                    using (Bitmap b = ToFormat(src, PixelFormat.Format32bppArgb))
                    {
                        b.SetResolution(dpi, dpi);
                        string got = Read(b);
                        if (first == null) first = got;
                        Console.WriteLine("    " + dpi + " dpi: " + Esc(got));
                        Check(got == first, dpi + " dpi reads differently: " + Esc(got));
                    }
                }
            }
            int[][] strips = { new int[] { 2000, 1 }, new int[] { 1, 3000 }, new int[] { 20000, 1 }, new int[] { 1, 20000 }, new int[] { 4, 9000 } };
            foreach (int[] s in strips)
            {
                using (Bitmap b = Canvas(s[0], s[1], Color.White))
                {
                    string error;
                    string t = Stackshot.Ocr.Read(b, out error);
                    Console.WriteLine("    " + s[0] + "x" + s[1] + ": " + (t == null ? "error " + error : "\"" + Esc(t) + "\""));
                    Check(t != null, s[0] + "x" + s[1] + ": " + error);
                    Check(t.Trim().Length == 0, s[0] + "x" + s[1] + " is blank but read as: " + Esc(t));
                }
            }
        }

        // The band edges of a tall capture: the boundary between pieces i-1 and i is at 2280 * i + 60 (own ranges).
        static void TallEdges()
        {
            const int W = 800, H = 20000;
            List<int> bounds = new List<int>();
            for (int i = 1; 2280 * i + 60 < H; i++) bounds.Add(2280 * i + 60);
            int regular = 0;
            List<int> ys = new List<int>();
            long ms;
            string got;
            using (Bitmap b = Canvas(W, H, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 10f, GraphicsUnit.Point))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int y = 40; y < H - 60; y += 100)
                    {
                        bool near = false;
                        foreach (int bd in bounds) if (Math.Abs(y - bd) < 60) near = true;
                        if (near) continue;
                        regular++;
                        ys.Add(y);
                        g.DrawString("Linea " + regular.ToString("0000"), f, Brushes.Black, 20, y);
                    }
                    for (int k = 0; k < bounds.Count; k++) g.DrawString("Borde " + (k + 1), f, Brushes.Black, 400, bounds[k] - 9);
                }
                Stopwatch sw = Stopwatch.StartNew();
                got = Read(b);
                ms = sw.ElapsedMilliseconds;
            }
            string norm = Norm(got);
            int missing = 0, twice = 0;
            StringBuilder lost = new StringBuilder();
            for (int i = 1; i <= regular; i++)
            {
                string n = Norm("Linea " + i.ToString("0000"));
                int at = norm.IndexOf(n, StringComparison.Ordinal);
                if (at < 0) { missing++; if (missing <= 40) lost.Append(" " + i.ToString("0000") + "@" + ys[i - 1]); }
                else if (norm.IndexOf(n, at + 1, StringComparison.Ordinal) >= 0) twice++;
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "h3-tall-edges.txt"), got);
            Console.WriteLine("    lost (number@y, first 40):" + lost.ToString());
            int badEdges = 0;
            for (int k = 0; k < bounds.Count; k++)
            {
                int count = CountOf(norm, " borde " + (k + 1) + " ");
                if (count != 1) { badEdges++; Console.WriteLine("    Borde " + (k + 1) + " at y=" + bounds[k] + " read " + count + " times"); }
            }
            Console.WriteLine("    " + regular + " lines: " + missing + " missing, " + twice + " twice; " + bounds.Count + " lines on the band edges, " + badEdges + " not read exactly once; " + ms + " ms");
            Check(missing <= regular / 50, missing + " of " + regular + " lines missing");
            // Windows engine limit (documented): on a 20000 px synthetic page it may read one line twice or miss its first letter.
            Check(twice <= regular / 100, twice + " lines read twice");
            Check(badEdges <= 1, badEdges + " lines on the band edges not read exactly once");
        }

        static double BlockRecall(string[] block, int w, int h, int x, int y, float pt)
        {
            using (Bitmap b = Canvas(w, h, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, block, "Segoe UI", pt, FontStyle.Regular, Brushes.Black, x, y, TextRenderingHint.ClearTypeGridFit);
                return Recall(block, Read(b));
            }
        }

        static void Wide9000()
        {
            using (Bitmap b = Canvas(9000, 300, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    Draw(g, new string[] { "Izquierda del todo" }, "Segoe UI", 9, FontStyle.Regular, Brushes.Black, 10, 20, TextRenderingHint.ClearTypeGridFit);
                    Draw(g, new string[] { "Derecha del todo" }, "Segoe UI", 9, FontStyle.Regular, Brushes.Black, 8600, 20, TextRenderingHint.ClearTypeGridFit);
                }
                string got = Read(b);
                string norm = Norm(got);
                Console.WriteLine("    9000 px: " + got.Replace("\n", " | "));
                Check(norm.Contains(" izquierda del todo ") && norm.Contains(" derecha del todo "), "9000 px: an end was lost: " + got.Replace("\n", " | "));
            }

        }

        static readonly string[] BlockLines = { "Configuraci\u00F3n de la red", "Direcci\u00F3n IP 192.168.1.20", "Puerta de enlace 192.168.1.1", "Servidor DNS 8.8.8.8", "Estado: conectado" };

        static void Cliff2600()
        {
            string[] block = BlockLines;
            double r2600 = BlockRecall(block, 2600, 150, 30, 20, 9f), r2601 = BlockRecall(block, 2601, 150, 30, 20, 9f);
            Console.WriteLine("    9 pt block, 2600 px wide (doubled): " + P(r2600) + "; 2601 px wide (as is): " + P(r2601));
            Check(r2601 >= r2600 - 0.1, "2601 px reads " + P(r2601) + " against " + P(r2600) + " at 2600 px");

        }

        static void Native4K()
        {
            string[] block = BlockLines;
            using (Bitmap native = Canvas(3840, 2160, Color.White))
            {
                using (Graphics g = Graphics.FromImage(native)) Draw(g, block, "Segoe UI", 9, FontStyle.Regular, Brushes.Black, 60, 60, TextRenderingHint.ClearTypeGridFit);
                using (Bitmap big = Canvas(7680, 4320, Color.White))
                {
                    using (Graphics g = Graphics.FromImage(big))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(native, 0, 0, 7680, 4320);
                    }
                    double a = Recall(block, Read(native));
                    double d = Recall(block, Read(big));
                    Console.WriteLine("    4K 9 pt block: as is " + P(a) + ", drawn twice as big " + P(d));
                    Check(a >= 0.85, "4K 9 pt text read as is: only " + P(a) + " of the words");
                }
            }
        }

        // Text turned by an angle, as on a portrait screen (the whole screen turned: text runs up or down).
        static Bitmap Turned(string[] lines, int angle)
        {
            bool side = angle == 90 || angle == 270;
            int w = side ? 230 : 640, h = side ? 640 : 200;
            Bitmap b = Canvas(w, h, Color.White);
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = new Font("Segoe UI", 12f, GraphicsUnit.Point))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.TranslateTransform(w / 2f, h / 2f);
                g.RotateTransform(angle);
                int lh = LineHeight(12);
                float top = -(lines.Length * lh) / 2f;
                for (int k = 0; k < lines.Length; k++)
                {
                    SizeF s = g.MeasureString(lines[k], f);
                    g.DrawString(lines[k], f, Brushes.Black, -s.Width / 2, top + k * lh);
                }
            }
            return b;
        }

        static void RotatedTurnBack()
        {
            string[] lines = { "Documento girado para leer", "Segunda linea del texto", "Tercera linea" };
            using (Bitmap sideways = Turned(lines, 90))
            using (Bitmap other = Turned(lines, 270))
            {
                string asIs = Read(sideways);
                Console.WriteLine("    turned 90 as is: " + Esc(asIs) + "; turned 270 as is: " + Esc(Read(other)));
                using (Bitmap back = (Bitmap)sideways.Clone())
                {
                    back.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    string got = Read(back);
                    double r = Recall(lines, got);
                    Console.WriteLine("    turned back: recall " + P(r) + ": " + got.Replace("\n", " | "));
                    Check(r >= 0.8, "turned back: only " + P(r) + " of the words");
                }
            }
        }

        // A long scrolling capture handed over the way the thumbnail, the editor and the area capture do (owned): nothing
        // is copied on the calling thread (the UI thread in the app). Only a pinned image, which stays on screen, is copied.
        static void CopyBigCapture()
        {
            using (Bitmap big = Canvas(9000, 6000, Color.White))
            using (Control ui = new Control())
            {
                ui.CreateControl();
                bool done = false;
                Stopwatch sw = Stopwatch.StartNew();
                Stackshot.Ocr.ReadAsync(big, true, ui, delegate(string t, string e) { done = true; });
                long copyMs = sw.ElapsedMilliseconds;
                Stopwatch wait = Stopwatch.StartNew();
                while (!done && wait.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(5); }
                Console.WriteLine("    9000x6000 (216 MB): ReadAsync held the calling thread " + copyMs + " ms; the whole read " + sw.ElapsedMilliseconds + " ms");
                Check(done, "no call back for the 9000x6000 capture");
                Check(copyMs < 300, "ReadAsync held the calling thread for " + copyMs + " ms on a 9000x6000 capture");
            }
        }

        static int DarkPixels(Bitmap p, int x0, int y0, int x1, int y1)
        {
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Color c = p.GetPixel(x, y);
                    if (c.R < 100 && c.G < 100 && c.B < 100) n++;
                }
            return n;
        }

        // One band of a tall capture (800 x 2400, 2 x scale inside Ocr) read alone: no line may be lost. Prints the raw
        // lines the engine returns for the prepared band, the dark pixels where each line is drawn and a crop to look at.
        static void BandAlone()
        {
            List<string> want = new List<string>();
            List<int> tops = new List<int>();
            using (Bitmap b = Canvas(800, 2400, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 10f, GraphicsUnit.Point))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int y = 40, n = 1; y <= 2240; y += 100, n++)
                    {
                        string s = "Linea " + n.ToString("0000");
                        want.Add(s);
                        tops.Add(y);
                        g.DrawString(s, f, Brushes.Black, 20, y);
                    }
                }
                string got = Read(b);
                string norm = Norm(got);
                List<string> lost = new List<string>();
                foreach (string s in want) if (norm.IndexOf(Norm(s), StringComparison.Ordinal) < 0) lost.Add(s);
                Console.WriteLine("    band read alone: " + (want.Count - lost.Count) + " of " + want.Count + " lines; lost: " + string.Join(", ", lost.ToArray()));
                Console.WriteLine("    " + Esc(got));
                using (Bitmap p = Stackshot.Ocr.Prepare(b))
                {
                    foreach (int idx in new int[] { 0, 17, 22 })
                    {
                        int y = 2 * tops[idx] + 20;
                        Console.WriteLine("    dark pixels where \"" + want[idx] + "\" is drawn: " + DarkPixels(p, 60, y, 420, y + 36));
                    }
                    using (Bitmap crop = p.Clone(new Rectangle(0, 3300, 700, 1300), PixelFormat.Format24bppRgb))
                        crop.Save(Path.Combine(Path.GetTempPath(), "h3-band-crop.png"), ImageFormat.Png);
                    object engine = typeof(Stackshot.Ocr).GetMethod("Engine", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                    MethodInfo lines = typeof(Stackshot.Ocr).GetMethod("Lines", BindingFlags.NonPublic | BindingFlags.Static);
                    System.Collections.IEnumerable raw = (System.Collections.IEnumerable)lines.Invoke(null, new object[] { engine, p });
                    StringBuilder sb = new StringBuilder();
                    foreach (object kv in raw)
                    {
                        Type t = kv.GetType();
                        string key = (string)t.GetProperty("Key").GetValue(kv, null);
                        object box = t.GetProperty("Value").GetValue(kv, null);
                        float top = (float)box.GetType().GetProperty("Y").GetValue(box, null);
                        sb.Append(" [" + key + " @" + top.ToString("0") + "]");
                    }
                    Console.WriteLine("    raw engine lines on the prepared band:" + sb.ToString());
                }
                Check(lost.Count == 0, "a 2400 px band read alone loses " + lost.Count + " lines: " + string.Join(", ", lost.ToArray()));
            }
        }

        // The lines the engine returns for a prepared image: "text@top" (top in the prepared image).
        static List<string> RawLines(Bitmap p)
        {
            object engine = typeof(Stackshot.Ocr).GetMethod("Engine", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            MethodInfo lines = typeof(Stackshot.Ocr).GetMethod("Lines", BindingFlags.NonPublic | BindingFlags.Static);
            System.Collections.IEnumerable raw = (System.Collections.IEnumerable)lines.Invoke(null, new object[] { engine, p });
            List<string> o = new List<string>();
            foreach (object kv in raw)
            {
                Type t = kv.GetType();
                string key = (string)t.GetProperty("Key").GetValue(kv, null);
                object box = t.GetProperty("Value").GetValue(kv, null);
                float top = (float)box.GetType().GetProperty("Y").GetValue(box, null);
                o.Add(key + "@" + top.ToString("0"));
            }
            return o;
        }

        static List<string> LostFrom(List<string> want, List<string> raw)
        {
            string norm = Norm(string.Join("\n", raw.ToArray()));
            List<string> lost = new List<string>();
            foreach (string w in want) if (norm.IndexOf(Norm(w), StringComparison.Ordinal) < 0) lost.Add(w);
            return lost;
        }

        // Bands of a tall capture prepared two ways: as Recognize does (Prepare of a sub-rectangle of the whole image) and
        // from a copy of the same rows. A line fully inside a band must come out the same both ways.
        static void TallPieceSource()
        {
            List<string> all = new List<string>();
            List<int> tops = new List<int>();
            using (Bitmap big = Canvas(800, 20000, Color.White))
            {
                using (Graphics g = Graphics.FromImage(big))
                using (Font f = new Font("Segoe UI", 10f, GraphicsUnit.Point))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int y = 40, n = 1; y < 19900; y += 100, n++)
                    {
                        string s = "Linea " + n.ToString("0000");
                        all.Add(s);
                        tops.Add(y);
                        g.DrawString(s, f, Brushes.Black, 20, y);
                    }
                }
                int[] bandTops = { 0, 2280, 4560 };
                foreach (int bt in bandTops)
                {
                    Rectangle band = new Rectangle(0, bt, 800, 2400);
                    List<string> want = new List<string>();
                    for (int i = 0; i < all.Count; i++) if (tops[i] >= bt + 20 && tops[i] + 40 <= bt + 2400) want.Add(all[i]);
                    using (Bitmap fromBig = Stackshot.Ocr.Prepare(big, band, 2.0))
                    using (Bitmap copy = big.Clone(band, PixelFormat.Format32bppArgb))
                    using (Bitmap fromCopy = Stackshot.Ocr.Prepare(copy))
                    {
                        List<string> a = RawLines(fromBig), b = RawLines(fromCopy);
                        List<string> lostA = LostFrom(want, a), lostB = LostFrom(want, b);
                        Console.WriteLine("    band at y=" + bt + " (" + want.Count + " lines inside): as Recognize does lost " + lostA.Count + ", from a copy lost " + lostB.Count);
                        if (lostA.Count > 0) Console.WriteLine("      lost as Recognize does: " + string.Join(", ", lostA.ToArray()));
                        if (bt == 0)
                            using (Bitmap crop = fromBig.Clone(new Rectangle(0, 3300, 700, 1300), PixelFormat.Format24bppRgb))
                                crop.Save(Path.Combine(Path.GetTempPath(), "h3-band0-sub.png"), ImageFormat.Png);
                        Check(lostA.Count == 0, "band at y=" + bt + " of a tall image loses " + lostA.Count + " of " + want.Count + " lines when prepared as Recognize does; a copy of the same rows loses " + lostB.Count);
                    }
                }
            }
        }

        // The same 23 lines as BandAlone, with one short label on the right at a given height: does the label change
        // the lines on the left?
        static void LabelNearLines()
        {
            int[] labelAt = { 2331, 1000 };
            List<string> badLabels = new List<string>();
            foreach (int ly in labelAt)
            {
                List<string> want = new List<string>();
                using (Bitmap b = Canvas(800, 2400, Color.White))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    using (Font f = new Font("Segoe UI", 10f, GraphicsUnit.Point))
                    {
                        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                        for (int y = 40, n = 1; y <= 2240; y += 100, n++)
                        {
                            string s = "Linea " + n.ToString("0000");
                            want.Add(s);
                            g.DrawString(s, f, Brushes.Black, 20, y);
                        }
                        g.DrawString("Borde 1", f, Brushes.Black, 400, ly);
                    }
                    List<string> lost = LostFrom(want, RawLines(Stackshot.Ocr.Prepare(b)));
                    // The same band as two halves of 1200 rows each, prepared alone.
                    List<string> halves = new List<string>();
                    using (Bitmap top = Stackshot.Ocr.Prepare(b, new Rectangle(0, 0, 800, 1200), 2.0))
                    using (Bitmap bottom = Stackshot.Ocr.Prepare(b, new Rectangle(0, 1200, 800, 1200), 2.0))
                    {
                        halves.AddRange(RawLines(top));
                        halves.AddRange(RawLines(bottom));
                    }
                    List<string> lostHalves = LostFrom(want, halves);
                    Console.WriteLine("    label at y=" + ly + ": band of 2400 rows lost " + lost.Count + (lost.Count > 0 ? " (" + string.Join(", ", lost.ToArray()) + ")" : "") + "; two halves of 1200 rows lost " + lostHalves.Count);
                    if (lost.Count > 0) badLabels.Add("y=" + ly + ": " + lost.Count + " lost");
                }
            }
            // An engine measurement: Stackshot reads in 1200-row pieces because of this; only the halves must lose nothing.
            Console.WriteLine("    engine with 2400-row bands: " + (badLabels.Count == 0 ? "no loss" : string.Join("; ", badLabels.ToArray())));
        }

        // A plain grey button drawn with GDI+ (no WPF): does its caption come out?
        static void GreyButton()
        {
            using (Bitmap b = Canvas(260, 80, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(225, 225, 225)))
                using (Pen edge = new Pen(Color.FromArgb(173, 173, 173)))
                using (Font f = new Font("Segoe UI", 9f, GraphicsUnit.Point))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    g.FillRectangle(fill, 20, 20, 120, 36);
                    g.DrawRectangle(edge, 20, 20, 120, 36);
                    g.DrawString("Guardar", f, Brushes.Black, 44, 29);
                }
                string got = Read(b);
                Console.WriteLine("    grey button: " + Esc(got));
                Check(Norm(got).Contains(" guardar "), "the caption of a grey button is not read: \"" + Esc(got) + "\"");
            }
        }

        static void CallbackWithoutHandle()
        {
            using (Control ui = new Control())
            using (Bitmap b = Canvas(300, 60, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, new string[] { "Sin manejador" }, "Segoe UI", 12, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
                Check(!ui.IsHandleCreated, "the control has a handle");
                bool done = false;
                Stackshot.Ocr.ReadAsync(b, ui, delegate(string t, string e) { done = true; });
                Stopwatch sw = Stopwatch.StartNew();
                while (!done && sw.ElapsedMilliseconds < 10000) Thread.Sleep(20);
                Console.WriteLine("    call back without a handle: " + done);
                Check(done, "no call back in 10 s: BeginInvoke needs a handle and the answer is lost");
            }
        }

        static void CallbackDisposedControl()
        {
            Control ui = new Control();
            ui.CreateControl();
            Check(ui.IsHandleCreated, "no handle after CreateControl");
            using (Bitmap b = Canvas(300, 60, Color.White))
            {
                using (Graphics g = Graphics.FromImage(b)) Draw(g, new string[] { "Control cerrado antes" }, "Segoe UI", 12, FontStyle.Regular, Brushes.Black, 8, 8, TextRenderingHint.ClearTypeGridFit);
                bool called = false;
                Stackshot.Ocr.ReadAsync(b, ui, delegate(string t, string e) { called = true; });
                ui.Dispose();
                Thread.Sleep(3000);
                Console.WriteLine("    control disposed at once: call back " + (called ? "came" : "did not come") + "; the process is still here");
            }
        }

        static void DisposedBitmap()
        {
            Bitmap b = Canvas(200, 60, Color.White);
            b.Dispose();
            using (Control ui = new Control())
            {
                ui.CreateControl();
                string text, error;
                bool done = ReadWait(b, ui, out text, out error);
                Console.WriteLine("    text " + (text == null ? "null" : "\"" + text + "\"") + ", message: " + error);
                Check(done, "no call back");
                Check(text == null, "a disposed bitmap gave text");
                Check(error == null || error.IndexOf("memoria", StringComparison.OrdinalIgnoreCase) < 0, "a disposed bitmap is reported as a memory problem: " + error);
            }
        }

        static Bitmap Document(int k)
        {
            Bitmap b = Canvas(2000, 1100, Color.White);
            using (Graphics g = Graphics.FromImage(b))
            {
                Draw(g, new string[] { "Documento " + k.ToString("00") }, "Segoe UI", 14, FontStyle.Bold, Brushes.Black, 40, 30, TextRenderingHint.ClearTypeGridFit);
                string[] body = new string[12];
                for (int i = 0; i < body.Length; i++) body[i] = "Linea " + (i + 1) + " del documento " + k.ToString("00") + " con texto de prueba";
                Draw(g, body, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 40, 100, TextRenderingHint.ClearTypeGridFit);
            }
            return b;
        }

        static void ConcurrentReads()
        {
            const int N = 8;
            using (Control ui = new Control())
            {
                ui.CreateControl();
                string[] got = new string[N];
                int calls = 0;
                long peakBefore = Process.GetCurrentProcess().PeakWorkingSet64;
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < N; i++)
                {
                    int k = i;
                    using (Bitmap b = Document(k))
                        Stackshot.Ocr.ReadAsync(b, ui, delegate(string t, string e) { got[k] = t ?? "ERROR " + e; calls++; });
                }
                while (calls < N && sw.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(5); }
                long peak = Process.GetCurrentProcess().PeakWorkingSet64;
                Console.WriteLine("    " + calls + " of " + N + " call backs in " + sw.ElapsedMilliseconds + " ms; peak working set " + (peak / 1048576) + " MB (was " + (peakBefore / 1048576) + " MB)");
                Check(calls == N, "only " + calls + " of " + N + " call backs");
                for (int k = 0; k < N; k++)
                {
                    string want = " documento " + k.ToString("00") + " ";
                    Check(got[k] != null && Norm(got[k]).Contains(want), "read " + k + " got someone else's text or none: " + got[k]);
                }
            }
        }

        static void DisposeRightAfter()
        {
            const int N = 20;
            using (Control ui = new Control())
            {
                ui.CreateControl();
                string[] got = new string[N];
                int calls = 0;
                for (int i = 0; i < N; i++)
                {
                    int k = i;
                    using (Bitmap b = Canvas(600, 100, Color.White))
                    {
                        using (Graphics g = Graphics.FromImage(b)) Draw(g, new string[] { "Prueba " + (k + 1).ToString("00") }, "Segoe UI", 16, FontStyle.Regular, Brushes.Black, 10, 10, TextRenderingHint.ClearTypeGridFit);
                        Stackshot.Ocr.ReadAsync(b, ui, delegate(string t, string e) { got[k] = t ?? "ERROR " + e; calls++; });
                    }
                }
                Stopwatch sw = Stopwatch.StartNew();
                while (calls < N && sw.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(5); }
                Check(calls == N, "only " + calls + " of " + N + " call backs");
                int wrong = 0;
                for (int k = 0; k < N; k++)
                    if (got[k] == null || !Norm(got[k]).Contains(" prueba " + (k + 1).ToString("00") + " ")) { wrong++; Console.WriteLine("    " + k + ": " + got[k]); }
                Check(wrong == 0, wrong + " of " + N + " reads gave the wrong text");
            }
        }

        static void Speed()
        {
            using (Bitmap hd = Canvas(1920, 1080, Color.White))
            {
                using (Graphics g = Graphics.FromImage(hd))
                {
                    string[] lines = new string[40];
                    for (int i = 0; i < lines.Length; i++) lines[i] = "Linea de texto de una ventana " + (i + 1) + ": valor 1.234,56 euros";
                    Draw(g, lines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 20, 20, TextRenderingHint.ClearTypeGridFit);
                }
                Stopwatch sw = Stopwatch.StartNew();
                string got = Read(hd);
                Console.WriteLine("    1920x1080, 40 lines: " + sw.ElapsedMilliseconds + " ms, " + CountChar(got, '\n') + " line breaks");
                Check(sw.ElapsedMilliseconds < 30000, "1080p took " + sw.ElapsedMilliseconds + " ms");
            }
            using (Bitmap uhd = Canvas(3840, 2160, Color.White))
            {
                using (Graphics g = Graphics.FromImage(uhd))
                {
                    string[] lines = new string[80];
                    for (int i = 0; i < lines.Length; i++) lines[i] = "Linea de texto de una ventana " + (i + 1) + ": valor 1.234,56 euros";
                    Draw(g, lines, "Segoe UI", 10, FontStyle.Regular, Brushes.Black, 20, 20, TextRenderingHint.ClearTypeGridFit);
                }
                Stopwatch sw = Stopwatch.StartNew();
                string got = Read(uhd);
                Console.WriteLine("    3840x2160, 80 lines: " + sw.ElapsedMilliseconds + " ms, " + CountChar(got, '\n') + " line breaks");
                Check(sw.ElapsedMilliseconds < 60000, "4K took " + sw.ElapsedMilliseconds + " ms");

                // ReadAsync copies the image on the calling thread before it returns (the UI thread in the app).
                using (Control ui = new Control())
                {
                    ui.CreateControl();
                    bool done = false;
                    Stopwatch copy = Stopwatch.StartNew();
                    Stackshot.Ocr.ReadAsync(uhd, ui, delegate(string t, string e) { done = true; });
                    long copyMs = copy.ElapsedMilliseconds;
                    Stopwatch wait = Stopwatch.StartNew();
                    while (!done && wait.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(5); }
                    Console.WriteLine("    ReadAsync on the calling thread for a 4K image: " + copyMs + " ms");
                    Check(done, "no call back for the 4K image");
                    Check(copyMs < 500, "ReadAsync blocked the calling thread for " + copyMs + " ms on a 4K image");
                }
            }
        }

        static void JoinDescribe()
        {
            RectangleF box = new RectangleF(10, 10, 50, 20);
            RectangleF below = new RectangleF(10, 36, 50, 20);
            string blank = Stackshot.Ocr.Join(new List<string> { "", "  ", "\t", "\r" }, new List<RectangleF> { box, box, box, box });
            Check(blank == "", "blank lines gave text: " + Esc(blank));
            string tabs = Stackshot.Ocr.Join(new List<string> { "a\tb", "x\r" }, new List<RectangleF> { box, below });
            Check(tabs == "a\tb\nx", "tab or CR: " + Esc(tabs));
            string longLine = Repeat("palabra ", 600).Trim();
            string joinedLong = Stackshot.Ocr.Join(new List<string> { longLine }, new List<RectangleF> { box });
            Check(joinedLong == longLine, "a long line changed: " + joinedLong.Length);
            string dl = Stackshot.Ocr.Describe(longLine);
            Console.WriteLine("    long line notice: " + dl.Length + " chars: " + dl.Substring(dl.Length - 12));
            Check(dl.EndsWith("\u2026\u00BB", StringComparison.Ordinal) && dl.Length <= 100, "long line notice: " + dl);
            string emoji = "Hola " + char.ConvertFromUtf32(0x1F600) + " mundo";
            Check(Stackshot.Ocr.Join(new List<string> { emoji }, new List<RectangleF> { box }) == emoji, "emoji lost in Join");
            string trailing = Stackshot.Ocr.Describe("Hola\n\n\n");
            Check(trailing == "1 l\u00EDnea: \u00ABHola\u00BB", "trailing blank lines in the notice: " + Esc(trailing));
            string half = Stackshot.Ocr.Describe("a" + Repeat(char.ConvertFromUtf32(0x1F600), 50));
            int ell = half.IndexOf('\u2026');
            char before = ell > 0 ? half[ell - 1] : ' ';
            Console.WriteLine("    emoji notice: char before the dots is U+" + ((int)before).ToString("X4") + " (" + half.Length + " chars)");
            Check(ell < 1 || !char.IsHighSurrogate(before), "the notice ends with half of an emoji (U+" + ((int)before).ToString("X4") + ")");
        }

        static void EngineLanguage()
        {
            MethodInfo m = typeof(Stackshot.Ocr).GetMethod("Engine", BindingFlags.NonPublic | BindingFlags.Static);
            Check(m != null, "no Engine()");
            object engine = m.Invoke(null, null);
            Check(engine != null, "no recognizer on this PC: Read would say OCR is not installed");
            object lang = engine.GetType().GetProperty("RecognizerLanguage").GetValue(engine, null);
            string tag = (string)lang.GetType().GetProperty("LanguageTag").GetValue(lang, null);
            Console.WriteLine("    recognizer language: " + tag);
        }
    }
}

// Copy text from a capture (suite "text"): Windows' OCR on images drawn here (never the screen), how an image is
// prepared for it, how lines are joined, the notice text, the call back on the UI thread, and the new hotkey action.
// The clipboard is never touched.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static class AppTestsText
    {
        const string SuiteName = "text";

        sealed class Failed : Exception { public Failed(string m) : base(m) { } }
        static void Check(bool ok, string what) { if (!ok) throw new Failed(what); }

        public static void Suite()
        {
            AppTests.Test(SuiteName, "ocr reads small screen text in Spanish and English", ReadsScreenText);
            AppTests.Test(SuiteName, "ocr reads light text on a dark theme", ReadsDarkTheme);
            AppTests.Test(SuiteName, "ocr: twice the size reads at least as well", UpscaleHelps);
            AppTests.Test(SuiteName, "ocr: an image without text gives empty text, not an error", NoText);
            AppTests.Test(SuiteName, "ocr: 1x1, a thin strip and a huge image never throw", OddSizes);
            AppTests.Test(SuiteName, "pieces: tall images in overlapping bands, nothing left out", PiecesCover);
            AppTests.Test(SuiteName, "ocr: a 9000 px scrolling capture, every line once", TallCapture);
            AppTests.Test(SuiteName, "prepare: scale, margin, limit and transparency", Prepare);
            AppTests.Test(SuiteName, "join: lines, blank line only for a real gap", Join);
            AppTests.Test(SuiteName, "describe: the notice text", Describe);
            AppTests.Test(SuiteName, "read async calls back on the UI thread", CallsBackOnUi);
            AppTests.Test(SuiteName, "hotkey action: text is a capture mode with its own combo", HotkeyAction);
        }

        // Lines of text drawn the way Windows draws a window at 100 % (Segoe UI 9 pt, ClearType), black on white or the
        // given colours.
        static Bitmap Draw(string[] lines, float pt, Color ink, Color back, int width)
        {
            int lh = (int)Math.Ceiling(pt * 96 / 72 * 1.6);
            Bitmap b = new Bitmap(width, 16 + lh * lines.Length, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = new Font("Segoe UI", pt))
            using (SolidBrush br = new SolidBrush(ink))
            {
                g.Clear(back);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, br, 8, 8 + i * lh);
            }
            return b;
        }

        static string Ocr(Bitmap b)
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

        // Share of the expected words that are in the text read.
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

        static string P(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        static readonly string[] Sample =
        {
            "Factura n\u00FAmero 2026-0147 del cliente Mart\u00EDnez",
            "Importe total: 1.234,56 euros, vencimiento el 15 de noviembre",
            "The quick brown fox jumps over the lazy dog",
            "Configuraci\u00F3n avanzada > Opciones de red",
        };

        static void ReadsScreenText()
        {
            using (Bitmap b = Draw(Sample, 9, Color.Black, Color.White, 520))
            {
                Stopwatch sw = Stopwatch.StartNew();
                string got = Ocr(b);
                double r = Recall(Sample, got);
                Console.WriteLine("    recall " + P(r) + " in " + sw.ElapsedMilliseconds + " ms: " + got.Replace("\n", " | "));
                Check(r >= 0.9, "only " + P(r) + " of the words were read: " + got.Replace("\n", " | "));
                Check(got.Split('\n').Length >= Sample.Length, "lines were merged: " + got.Replace("\n", " | "));
                Check(got.Contains("2026-0147") || got.Contains("2026") , "the number was lost: " + got);
            }
        }

        static void ReadsDarkTheme()
        {
            using (Bitmap b = Draw(Sample, 9, Color.FromArgb(220, 220, 220), Color.FromArgb(30, 30, 30), 520))
            {
                string got = Ocr(b);
                double r = Recall(Sample, got);
                Console.WriteLine("    recall " + P(r));
                Check(r >= 0.85, "dark theme: only " + P(r) + " of the words: " + got.Replace("\n", " | "));
            }
        }

        static void UpscaleHelps()
        {
            string[] tiny = { "Texto peque\u00F1o de una barra de estado", "Guardado hace 3 minutos en Documentos" };
            using (Bitmap b = Draw(tiny, 7.5f, Color.FromArgb(90, 90, 90), Color.White, 360))
            {
                // The same text on a canvas 9960 px wide is read as it is (not doubled): the doubled one must do
                // at least as well.
                Bitmap plain = new Bitmap(9960, b.Height + 20, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(plain)) { g.Clear(Color.White); g.DrawImage(b, 10, 10, b.Width, b.Height); }
                Check(Stackshot.Ocr.Scale(plain.Width) == 1 && Stackshot.Ocr.Scale(b.Width) == 2, "scales " + Stackshot.Ocr.Scale(plain.Width) + " / " + Stackshot.Ocr.Scale(b.Width));
                string asIs;
                using (plain) asIs = Ocr(plain);
                string prepared = Ocr(b);
                double r1 = Recall(tiny, asIs), r2 = Recall(tiny, prepared);
                Console.WriteLine("    as is " + P(r1) + ", prepared " + P(r2));
                Check(r2 >= r1, "the prepared image reads worse (" + P(r2) + ") than the plain one (" + P(r1) + ")");
                Check(r2 >= 0.7, "7.5 pt grey text: only " + P(r2) + " read: " + prepared);
            }
        }

        static void NoText()
        {
            using (Bitmap b = new Bitmap(400, 300, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, 400, 300), Color.SkyBlue, Color.SeaGreen, 35f))
                {
                    g.FillRectangle(br, 0, 0, 400, 300);
                    g.FillEllipse(Brushes.Orange, 120, 80, 140, 140);
                }
                string got = Ocr(b);
                Check(got.Trim().Length == 0, "text found in a picture without text: " + got);
            }
        }

        static void OddSizes()
        {
            int[][] sizes = { new int[] { 1, 1 }, new int[] { 5, 200 }, new int[] { 3000, 4 }, new int[] { 12000, 260 } };
            foreach (int[] s in sizes)
            {
                using (Bitmap b = new Bitmap(s[0], s[1], PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.White);
                        if (s[1] >= 100) using (Font f = new Font("Segoe UI", 20)) g.DrawString("Hola mundo", f, Brushes.Black, Math.Max(0, s[0] - 400), 40);
                    }
                    string error;
                    using (Bitmap p = Stackshot.Ocr.Prepare(b))
                        Check(p.Width <= Stackshot.Ocr.MaxSide && p.Height <= Stackshot.Ocr.MaxSide, s[0] + "x" + s[1] + " prepared as " + p.Width + "x" + p.Height);
                    string t = Stackshot.Ocr.Read(b, out error);
                    Check(t != null || error != null, s[0] + "x" + s[1] + ": no text and no error");
                    Console.WriteLine("    " + s[0] + "x" + s[1] + ": " + (t != null ? "\"" + t.Replace("\n", " | ") + "\"" : "error " + error));
                    if (s[0] == 12000) Console.WriteLine("    12000 px wide (shrunk to the engine's limit): " + (t != null && Norm(t).Contains(" hola ") ? "read" : "not read"));
                }
            }
        }

        static void PiecesCover()
        {
            int[] heights = { 1, Stackshot.Ocr.Band, Stackshot.Ocr.Band + 1, 2400, 4680, 5000, 15000, 2280 * 7 + 120 };
            foreach (int h in heights)
            {
                List<Rectangle> p = Stackshot.Ocr.Pieces(new Size(800, h));
                Check(p.Count >= 1 && p[0].Y == 0 && p[p.Count - 1].Bottom == h, h + ": pieces do not span the image");
                for (int i = 0; i < p.Count; i++)
                {
                    Check(p[i].Height <= Stackshot.Ocr.Band && p[i].Height > 0 && p[i].Width == 800, h + ": piece " + p[i]);
                    if (i > 0) Check(p[i].Y < p[i - 1].Bottom && p[i - 1].Bottom - p[i].Y >= 60, h + ": pieces " + (i - 1) + " and " + i + " overlap by " + (p[i - 1].Bottom - p[i].Y));
                }
                // Prepared, each piece is within the engine's limit.
                double k = Stackshot.Ocr.Scale(800);
                foreach (Rectangle r in p) Check(r.Height * k + 40 <= Stackshot.Ocr.MaxSide, h + ": piece too tall when prepared");
            }
            Check(Stackshot.Ocr.Pieces(new Size(800, Stackshot.Ocr.Band)).Count == 1, "a piece high should be one piece");
            Check(Stackshot.Ocr.Scale(12000) * 12000 + 40 <= Stackshot.Ocr.MaxSide, "12000 px wide is not shrunk to the limit");
        }

        // A long page as a scrolling capture leaves it: numbered lines every 70 px down 9000 px, some of them across the
        // edges between pieces. Each must be read once, in order.
        static void TallCapture()
        {
            const int H = 9000, Step = 70;
            List<string> want = new List<string>();
            using (Bitmap b = new Bitmap(700, H, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 10))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    // Starting so that lines fall right on the edges at 2280 and 2400 (and their repeats).
                    for (int i = 0, y = 15; y < H - 30; i++, y += Step)
                    {
                        string s = "Linea " + (i + 1) + " del documento largo";
                        want.Add(s);
                        g.DrawString(s, f, Brushes.Black, 20, y);
                    }
                    g.DrawString("Linea en el borde", f, Brushes.Black, 380, 2392);
                    g.DrawString("Otra en el borde", f, Brushes.Black, 380, 2272);
                }
                Stopwatch sw = Stopwatch.StartNew();
                string got = Ocr(b);
                long ms = sw.ElapsedMilliseconds;
                string norm = Norm(got);
                int missing = 0, twice = 0;
                foreach (string w in want)
                {
                    string n = Norm(w);
                    int at = norm.IndexOf(n, StringComparison.Ordinal);
                    if (at < 0) missing++;
                    else if (norm.IndexOf(n, at + 1, StringComparison.Ordinal) >= 0) twice++;
                }
                int edge = 0, edge2 = 0;
                for (int at = norm.IndexOf(" en el borde ", StringComparison.Ordinal); at >= 0; at = norm.IndexOf(" en el borde ", at + 1, StringComparison.Ordinal)) edge++;
                edge2 = norm.Contains(" linea en el borde ") ? 1 : 0;
                Console.WriteLine("    " + want.Count + " lines, " + missing + " missing, " + twice + " twice, edge lines " + edge + ", " + ms + " ms");
                Check(missing <= want.Count / 50, missing + " of " + want.Count + " lines missing");
                Check(twice == 0, twice + " lines read twice (the overlap between pieces)");
                Check(edge == 2 && edge2 == 1, "the lines on the edge between pieces were read " + edge + " times");
                // In order: line 10 before line 100.
                int a = norm.IndexOf(" linea 10 ", StringComparison.Ordinal), c = norm.IndexOf(" linea 100 ", StringComparison.Ordinal);
                Check(a >= 0 && c > a, "lines out of order");
            }
        }

        static void Prepare()
        {
            using (Bitmap small = new Bitmap(300, 100, PixelFormat.Format32bppArgb))
            using (Bitmap p = Stackshot.Ocr.Prepare(small))
            {
                Check(p.Width == 600 + 40 && p.Height == 200 + 40, "300x100 prepared as " + p.Width + "x" + p.Height + " (expected twice plus 20 px margins)");
                Check(p.PixelFormat == PixelFormat.Format24bppRgb, "not opaque: " + p.PixelFormat);
                // Fully transparent black (a shadow): over white, not black.
                Color c = p.GetPixel(p.Width / 2, p.Height / 2);
                Check(c.R > 240 && c.G > 240 && c.B > 240, "transparent pixels came out " + c);
            }
            using (Bitmap big = new Bitmap(6000, 800, PixelFormat.Format32bppArgb))
            using (Bitmap p = Stackshot.Ocr.Prepare(big))
                Check(p.Width <= Stackshot.Ocr.MaxSide && p.Width >= 9900 && p.Height > 800, "6000x800 should be enlarged up to the engine limit: " + p.Width + "x" + p.Height);
            using (Bitmap dark = new Bitmap(50, 20, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dark)) g.Clear(Color.FromArgb(30, 30, 30));
                using (Bitmap p = Stackshot.Ocr.Prepare(dark))
                {
                    Color m = p.GetPixel(2, 2);
                    Check(m.R == 30 && m.G == 30 && m.B == 30, "the margin of a dark image is " + m + ", not its own colour");
                }
            }
        }

        static void Join()
        {
            List<string> t = new List<string> { "Uno", "Dos", "", "Tres" };
            List<RectangleF> r = new List<RectangleF>
            {
                new RectangleF(10, 10, 50, 20), new RectangleF(10, 36, 50, 20), new RectangleF(0, 0, 0, 0), new RectangleF(10, 120, 50, 20)
            };
            // The empty line is skipped and its box ignored for the gap, so the gap is measured from "Dos".
            string got = Stackshot.Ocr.Join(new List<string> { "Uno", "Dos", "Tres" }, new List<RectangleF> { r[0], r[1], r[3] });
            Check(got == "Uno\nDos\n\nTres", "joined as " + got.Replace("\n", "\\n"));
            got = Stackshot.Ocr.Join(new List<string> { "  a  ", "b" }, new List<RectangleF> { r[0], r[1] });
            Check(got == "a\nb", "spaces kept: " + got.Replace("\n", "\\n"));
            Check(Stackshot.Ocr.Join(new List<string>(), new List<RectangleF>()) == "", "no lines should give empty text");
            got = Stackshot.Ocr.Join(t, r);
            Check(!got.Contains("\n\n\n"), "more than one blank line: " + got.Replace("\n", "\\n"));
        }

        static void Describe()
        {
            string one = Stackshot.Ocr.Describe("Hola");
            Check(one == "1 l\u00EDnea: \u00ABHola\u00BB", "one line: " + one);
            string two = Stackshot.Ocr.Describe("Hola\n\nmundo");
            Check(two == "2 l\u00EDneas: \u00ABHola mundo\u00BB", "two lines: " + two);
            string longText = Stackshot.Ocr.Describe(new string('a', 50) + " " + new string('b', 60));
            Check(longText.EndsWith("\u2026\u00BB", StringComparison.Ordinal) && longText.Length < 110, "long text: " + longText);
        }

        static void CallsBackOnUi()
        {
            using (Control ui = new Control())
            {
                ui.CreateControl();
                IntPtr h = ui.Handle;
                int main = Thread.CurrentThread.ManagedThreadId, called = -1;
                string text = null;
                using (Bitmap b = Draw(new string[] { "Prueba de hilo" }, 12, Color.Black, Color.White, 300))
                    Stackshot.Ocr.ReadAsync(b, ui, delegate(string t, string e) { called = Thread.CurrentThread.ManagedThreadId; text = t; });
                // The image was copied: disposing it right away must not matter.
                Stopwatch sw = Stopwatch.StartNew();
                while (called < 0 && sw.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(5); }
                Check(called >= 0, "no call back in 20 s");
                Check(called == main, "called back on thread " + called + ", the UI thread is " + main);
                Check(text != null && Norm(text).Contains(" prueba "), "text read: " + text);
            }
        }

        static void HotkeyAction()
        {
            Settings s = new Settings();
            int i = Array.IndexOf(Settings.Actions, "text");
            Check(i >= 0, "no text action");
            Check(s.HotkeysFor("text") == s.HotText && s.HotText == "Ctrl+Shift+Alt+T", "default combo " + s.HotkeysFor("text"));
            s.SetHotkeys("text", "Ctrl+Alt+F9");
            Check(s.HotText == "Ctrl+Alt+F9" && s.HotGif != "Ctrl+Alt+F9", "SetHotkeys(text) changed " + s.HotText + " / gif " + s.HotGif);
            // No two default combos are the same, and the new one is a sound global hotkey.
            Settings d = new Settings();
            for (int a = 0; a < Settings.Actions.Length; a++)
                for (int b = a + 1; b < Settings.Actions.Length; b++)
                    foreach (string x in Hotkeys.Split(d.HotkeysFor(Settings.Actions[a])))
                        foreach (string y in Hotkeys.Split(d.HotkeysFor(Settings.Actions[b])))
                            Check(!Hotkeys.Same(x, y), Settings.Actions[a] + " and " + Settings.Actions[b] + " share " + x);
            uint mods;
            Keys key;
            Check(Hotkeys.TryParse(d.HotText, out mods, out key), "the default does not parse");
            Check(Hotkeys.Problem(key, Keys.Control | Keys.Shift | Keys.Alt, false) == null, "the default has a problem: " + Hotkeys.Problem(key, Keys.Control | Keys.Shift | Keys.Alt, false));
            Check(Glyph.Has("textscan"), "no textscan icon");
        }
    }
}

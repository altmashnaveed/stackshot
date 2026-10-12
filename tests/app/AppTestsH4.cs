// Round-4 checks (h4app): OCR pieces and scale, identical rows across the band edges, the notice of a read, the owned
// image of ReadAsync, the Texto tip, and the hotkey notices of ShotStack.RegisterHotkeys. Images are drawn here; no
// screen, no clipboard, no real hotkey of the user (the combos are free ones, held by a throwaway Hotkeys object).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static class AppTestsH4
    {
        const string SuiteName = "h4app";
        const string F10 = "Ctrl+Alt+Shift+F10", F11 = "Ctrl+Alt+Shift+F11";

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static Bitmap Canvas(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.White);
            return b;
        }

        static int CountOf(string hay, string needle)
        {
            int n = 0, at = 0;
            while ((at = hay.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        static string Esc(string s) { return s == null ? "null" : s.Replace("\n", " | ").Replace("\r", ""); }

        // ---- OCR geometry

        // Every height from 1 to 20000 (dense below 3000): the pieces span the image, step by Band - Overlap, and the last
        // one is taller than the overlap, so no band is left as a sliver.
        static void PiecesSweep()
        {
            int band = Stackshot.Ocr.Band, overlap = Stackshot.Ocr.Overlap, n = 0;
            for (int h = 1; h <= 20000; h += h < 3000 ? 1 : 37)
            {
                List<Rectangle> p = Stackshot.Ocr.Pieces(new Size(640, h));
                n++;
                Check(p[0].Y == 0 && p[p.Count - 1].Bottom == h, h + " px: pieces do not span the image");
                for (int i = 0; i < p.Count; i++)
                {
                    Check(p[i].Height >= 1 && p[i].Height <= band, h + " px: piece " + i + " is " + p[i].Height + " high");
                    if (i > 0) Check(p[i].Y - p[i - 1].Y == band - overlap, h + " px: step " + (i - 1) + " to " + i + " is " + (p[i].Y - p[i - 1].Y));
                }
                if (p.Count > 1) Check(p[p.Count - 1].Height > overlap, h + " px: last piece only " + p[p.Count - 1].Height + " high");
            }
            Console.WriteLine("    " + n + " heights checked");
        }

        // The prepared width (scaled width plus 20 px margins) stays within the engine's limit for every width, and
        // images up to 4980 px wide are doubled.
        static void ScaleSweep()
        {
            for (int w = 1; w <= 20000; w++)
            {
                double k = Stackshot.Ocr.Scale(w);
                Check(k > 0 && k <= 2, w + " px: scale " + k);
                int prepared = (int)Math.Round(w * k) + 40;
                Check(prepared <= Stackshot.Ocr.MaxSide, w + " px: prepared " + prepared + " px wide");
                if (w <= 4980) Check(k == 2, w + " px: not doubled (" + k + ")");
            }
        }

        // Describe cuts a long notice near 88 characters and never through an emoji: the surrogate pair is tested with
        // the emoji at every place around the cut.
        static void NoticeNeverSplitsAnEmoji()
        {
            string emoji = char.ConvertFromUtf32(0x1F600);
            for (int a = 78; a <= 96; a++)
            {
                string d = Stackshot.Ocr.Describe(new string('a', a) + emoji + new string('b', 40));
                for (int i = 0; i < d.Length; i++)
                {
                    if (char.IsHighSurrogate(d[i]))
                        Check(i + 1 < d.Length && char.IsLowSurrogate(d[i + 1]), "emoji after " + a + " letters: lone high surrogate in " + Esc(d));
                    if (char.IsLowSurrogate(d[i]))
                        Check(i > 0 && char.IsHighSurrogate(d[i - 1]), "emoji after " + a + " letters: lone low surrogate in " + Esc(d));
                }
                Check(d.IndexOf('\u2026') > 0, "no ellipsis after " + a + " letters: " + Esc(d));
            }
        }

        // ---- Reads

        // Ninety identical rows 30 px apart over three bands: each one is read, none is taken for the one next to it.
        static void IdenticalRowsAcrossBands()
        {
            const int W = 520, N = 90, Pitch = 30;
            using (Bitmap b = Canvas(W, 40 + Pitch * N))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 9f, GraphicsUnit.Point))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int i = 0; i < N; i++) g.DrawString("Pendiente", f, Brushes.Black, 20, 20 + i * Pitch);
                }
                string error;
                string text = Stackshot.Ocr.Read(b, out error);
                Check(text != null, "not read: " + error);
                int got = CountOf(text.ToLowerInvariant(), "pendiente");
                Console.WriteLine("    " + N + " identical rows over three bands: " + got + " read");
                Check(got == N, N + " rows drawn, " + got + " read");
            }
        }

        // owned: the image is read in place (no copy on the calling thread) and is disposed before the answer.
        static void OwnedReadIsInPlace()
        {
            Bitmap big = Canvas(9000, 2000);
            using (Graphics g = Graphics.FromImage(big))
            using (Font f = new Font("Segoe UI", 36f, GraphicsUnit.Point))
                g.DrawString("Texto propio", f, Brushes.Black, 40, 40);
            ManualResetEvent answered = new ManualResetEvent(false);
            string text = null, after = null;
            using (Control ui = new Control())
            {
                Stopwatch sw = Stopwatch.StartNew();
                Stackshot.Ocr.ReadAsync(big, true, ui, delegate(string t, string e)
                {
                    text = t ?? "error: " + e;
                    try { after = big.Width > 0 ? "still there" : "odd"; } catch (Exception) { after = "disposed"; }
                    answered.Set();
                });
                long call = sw.ElapsedMilliseconds;
                Check(answered.WaitOne(120000), "no answer in 120 s");
                Console.WriteLine("    owned call returned in " + call + " ms; after the answer the image is " + after);
                Check(call < 50, "ReadAsync held the calling thread " + call + " ms for an owned image (a copy?)");
            }
            Check(text != null && Ocr_Norm(text).Contains("propio"), "read: " + Esc(text));
            Check(after == "disposed", "the owned image was not disposed after the answer: " + after);
        }

        // Not owned: the caller's image is copied and stays the caller's to use afterwards.
        static void NotOwnedKeepsTheCallersImage()
        {
            Bitmap small = Canvas(400, 100);
            using (Graphics g = Graphics.FromImage(small))
            using (Font f = new Font("Segoe UI", 20f, GraphicsUnit.Point))
                g.DrawString("Hola", f, Brushes.Black, 10, 10);
            ManualResetEvent answered = new ManualResetEvent(false);
            string text = null, after = null;
            using (Control ui = new Control())
            {
                Stackshot.Ocr.ReadAsync(small, false, ui, delegate(string t, string e)
                {
                    text = t ?? "error: " + e;
                    try { after = small.Width > 0 ? "alive" : "odd"; } catch (Exception) { after = "disposed"; }
                    answered.Set();
                });
                Check(answered.WaitOne(120000), "no answer in 120 s");
            }
            Check(text != null && Ocr_Norm(text).Contains("hola"), "read: " + Esc(text));
            Check(after == "alive", "the caller's image was disposed by a read that did not own it: " + after);
            small.Dispose();
        }

        static string Ocr_Norm(string s) { return s.ToLowerInvariant().Replace(" ", "").Replace("\n", ""); }

        // Diagnostic (prints only): what the engine reads from small drawn images with one word or two.
        static void SingleWordProbe()
        {
            Probe("Hola", 20f, 300, 100, 30, 30);
            Probe("Hola", 20f, 300, 60, 10, 10);
            Probe("Hola", 20f, 400, 100, 10, 10);
            Probe("Hola", 20f, 300, 200, 10, 10);
            Probe("Hola", 20f, 200, 100, 10, 10);
            Probe("Hola", 24f, 300, 100, 10, 10);
            Probe("Hola", 18f, 300, 100, 10, 10);
            Probe("Hola", 16f, 300, 100, 10, 10);
            Probe("Hola", 20f, 600, 100, 10, 10);
            Probe("Hola", 20f, 320, 100, 10, 10);
            Probe("Hola", 20f, 300, 100, 10, 10);
        }

        static Bitmap OneWord(string word, float pt, int w, int h, int x, int y)
        {
            Bitmap b = Canvas(w, h);
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = new Font("Segoe UI", pt, GraphicsUnit.Point))
                g.DrawString(word, f, Brushes.Black, x, y);
            return b;
        }

        // The engine's own lines for the prepared image (Ocr.Prepare, then Ocr.Lines), with the picture saved for a look.
        static void RawEngineOnOneWord()
        {
            using (Bitmap b = OneWord("Hola", 20f, 300, 100, 10, 10))
            {
                string dir = Environment.GetEnvironmentVariable("STACKSHOT_UI_SHOTS") ?? Path.GetTempPath();
                Directory.CreateDirectory(dir);
                b.Save(Path.Combine(dir, "h4-hola-300x100.png"), ImageFormat.Png);
                object engine = typeof(Stackshot.Ocr).GetMethod("Engine", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Check(engine != null, "no OCR engine on this PC");
                using (Bitmap prepared = Stackshot.Ocr.Prepare(b))
                {
                    prepared.Save(Path.Combine(dir, "h4-hola-300x100-prepared.png"), ImageFormat.Png);
                    MethodInfo lines = typeof(Stackshot.Ocr).GetMethod("Lines", BindingFlags.Static | BindingFlags.NonPublic);
                    System.Collections.IList got = (System.Collections.IList)lines.Invoke(null, new object[] { engine, prepared });
                    Console.WriteLine("    raw engine lines on the prepared 300x100 image: " + got.Count + " (prepared " + prepared.Width + "x" + prepared.Height + ")");
                    foreach (object o in got)
                    {
                        object key = o.GetType().GetProperty("Key").GetValue(o, null);
                        object box = o.GetType().GetProperty("Value").GetValue(o, null);
                        Console.WriteLine("      <" + key + "> at " + box);
                    }
                }
            }
        }

        // Expected to FAIL on this PC: one word, drawn large enough to read, on a 300x100 image.
        static void OneWordIsRead300()
        {
            using (Bitmap b = OneWord("Hola", 20f, 300, 100, 10, 10))
            {
                string error;
                string t = Stackshot.Ocr.Read(b, out error);
                Check(t != null, "not read: " + error);
                Check(t.Trim() == "Hola", "the word \"Hola\" (20 pt, 300x100) is read as <" + Esc(t) + ">");
            }
        }

        // Expected to FAIL on this PC: the same word on a 200x100 image is read as "H Ola".
        static void OneWordIsRead200()
        {
            using (Bitmap b = OneWord("Hola", 20f, 200, 100, 10, 10))
            {
                string error;
                string t = Stackshot.Ocr.Read(b, out error);
                Check(t != null, "not read: " + error);
                Check(t.Trim() == "Hola", "the word \"Hola\" (20 pt, 200x100) is read as <" + Esc(t) + ">");
            }
        }

        static void Probe(string word, float pt, int w, int h, int x, int y)
        {
            using (Bitmap b = Canvas(w, h))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", pt, GraphicsUnit.Point))
                    g.DrawString(word, f, Brushes.Black, x, y);
                string error;
                string t = Stackshot.Ocr.Read(b, out error);
                Console.WriteLine("    probe \"" + word + "\" " + pt + "pt on " + w + "x" + h + " at " + x + "," + y + ": <" + Esc(t) + "> " + error + " scale " + Stackshot.Ocr.Scale(w));
            }
        }

        // ---- Hotkey notices of ShotStack.RegisterHotkeys (report = true) on a ShotStack made without its constructor

        static Settings Blank()
        {
            Settings s = new Settings();
            foreach (string a in Settings.Actions) s.SetHotkeys(a, "");
            return s;
        }

        static void SetField(object o, string field, object value)
        {
            FieldInfo f = typeof(ShotStack).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Check(f != null, "no field " + field + " in ShotStack");
            f.SetValue(o, value);
        }

        // The tray notice RegisterHotkeys writes, for these settings and this Hotkeys object. The icon is never shown.
        static string Notice(Settings s, Hotkeys hk, out string title)
        {
            ShotStack st = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            using (NotifyIcon tray = new NotifyIcon())
            {
                SetField(st, "settings", s);
                SetField(st, "hotkeys", hk);
                SetField(st, "tray", tray);
                ShotStack.ForgetRecorded();
                typeof(ShotStack).GetMethod("RegisterHotkeys", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(st, new object[] { true });
                List<Notice> told = ShotStack.Recorded; // a stack made without its constructor only records its notices
                title = told.Count > 0 ? told[told.Count - 1].Title : "";
                return told.Count > 0 ? told[told.Count - 1].Text : "";
            }
        }

        // A combo another program holds is named as busy, an unreadable one as not valid, and a combo that works is left out.
        static void BusyAndInvalidAreNamed()
        {
            using (Hotkeys other = new Hotkeys())
            using (Hotkeys hk = new Hotkeys())
            {
                Check(other.Register("other", F10).Count == 0, "could not hold " + F10 + " for the test");
                Settings s = Blank();
                s.SetHotkeys("text", F10);
                s.SetHotkeys("region", F11 + ", Ctrl+Nope");
                string title;
                string body = Notice(s, hk, out title);
                Console.WriteLine("    " + title + " | " + Esc(body));
                Check(body.Contains("Ctrl + May\u00FAs + Alt + F10 los usa otro programa"), "busy combo not named: " + Esc(body));
                Check(body.Contains("\u00ABCtrl+Nope\u00BB no es una combinaci\u00F3n de teclas v\u00E1lida"), "invalid combo not named: " + Esc(body));
                Check(!body.Contains("F11"), "a combo that registered is in the notice: " + Esc(body));
                Check(title == "Algunos atajos est\u00E1n ocupados", "title: " + title);
            }
        }

        // One free combo on two actions: the first (in Settings.Actions order) works, the second is named as repeated.
        static void SameComboTwoActions()
        {
            using (Hotkeys hk = new Hotkeys())
            {
                Settings s = Blank();
                s.SetHotkeys("region", F11);
                s.SetHotkeys("text", F11);
                string title;
                string body = Notice(s, hk, out title);
                Console.WriteLine("    " + title + " | " + Esc(body));
                Check(body.Contains("est\u00E1 puesto dos veces en los atajos de Stackshot: solo cuenta la primera."), "repeat not named: " + Esc(body));
                Check(!body.Contains("otro programa"), "a combo of Stackshot is blamed on another program: " + Esc(body));
                Check(title == "Algunos atajos no funcionan", "title: " + title);
            }
        }

        // Expected to FAIL while a combo typed twice in one action is blamed on another program: nothing else holds it.
        static void SameComboOneAction()
        {
            using (Hotkeys hk = new Hotkeys())
            {
                Settings s = Blank();
                s.SetHotkeys("text", F10 + ", Shift+Ctrl+Alt+F10");
                string title;
                string body = Notice(s, hk, out title);
                Console.WriteLine("    " + title + " | " + Esc(body));
                Check(!body.Contains("otro programa"), "a combo this action holds twice is blamed on another program: " + Esc(body));
            }
        }

        // Expected to FAIL while the notice claims the first action works when another program holds the combo for both.
        static void BusyOnTwoActions()
        {
            using (Hotkeys other = new Hotkeys())
            using (Hotkeys hk = new Hotkeys())
            {
                Check(other.Register("other", F10).Count == 0, "could not hold " + F10 + " for the test");
                Settings s = Blank();
                s.SetHotkeys("region", F10);
                s.SetHotkeys("text", F10);
                string title;
                string body = Notice(s, hk, out title);
                Console.WriteLine("    " + title + " | " + Esc(body));
                Check(body.Contains("los usa otro programa"), "busy combo not named: " + Esc(body));
                Check(!body.Contains("solo cuenta la primera"), "the notice says the first one works, but another program holds it: " + Esc(body));
            }
        }

        // A stray comma is not an empty combo: a combo that registers gives no notice at all.
        static void StrayComma()
        {
            using (Hotkeys hk = new Hotkeys())
            {
                Settings s = Blank();
                s.SetHotkeys("text", F11 + ",");
                string title;
                string body = Notice(s, hk, out title);
                Check(body.Length == 0, "a working combo gave a notice: " + Esc(body));
            }
        }

        // ---- The Texto tip of the mascot

        static void TipShowsCombo()
        {
            Settings s = new Settings();
            bool seen = false;
            for (int i = 0; i < 400 && !seen; i++) seen = MascotTalk.Tip(s).Contains("Ctrl + May\u00FAs + Alt + T copia el texto");
            Check(seen, "the Texto tip never showed its combo");
        }

        // Expected to FAIL while a cleared Texto combo reads "Sin atajo copia el texto...".
        static void TipWithoutCombo()
        {
            Settings s = new Settings();
            s.SetHotkeys("text", "");
            for (int i = 0; i < 400; i++)
            {
                string tip = MascotTalk.Tip(s);
                Check(!tip.Contains("Sin atajo"), "tip with the Texto combo cleared reads: " + tip);
            }
        }

        public static void Suite()
        {
            AppTests.Test(SuiteName, "probe: single words on small images (diagnostic)", SingleWordProbe);
            AppTests.Test(SuiteName, "probe: the engine's own lines for one word on 300x100 (diagnostic)", RawEngineOnOneWord);
            AppTests.Test(SuiteName, "ocr: the word Hola (20 pt) on a 300x100 image is read", OneWordIsRead300);
            AppTests.Test(SuiteName, "ocr: the word Hola (20 pt) on a 200x100 image is read", OneWordIsRead200);
            AppTests.Test(SuiteName, "pieces: every height up to 20000 px spans, steps and ends as designed", PiecesSweep);
            AppTests.Test(SuiteName, "scale: every width up to 20000 px fits the engine, up to 4980 px doubled", ScaleSweep);
            AppTests.Test(SuiteName, "describe: no emoji cut in half at any place around the cut", NoticeNeverSplitsAnEmoji);
            AppTests.Test(SuiteName, "ocr: 90 identical rows over three bands are each read", IdenticalRowsAcrossBands);
            AppTests.Test(SuiteName, "read async owned: answered without a copy, image disposed before the answer", OwnedReadIsInPlace);
            AppTests.Test(SuiteName, "read async not owned: the caller's image is still usable", NotOwnedKeepsTheCallersImage);
            AppTests.Test(SuiteName, "hotkeys: busy and invalid combos are named, working ones are not", BusyAndInvalidAreNamed);
            AppTests.Test(SuiteName, "hotkeys: one free combo on two actions: the second is named as repeated", SameComboTwoActions);
            AppTests.Test(SuiteName, "hotkeys: a combo typed twice in one action is not blamed on another program", SameComboOneAction);
            AppTests.Test(SuiteName, "hotkeys: busy combo on two actions: the notice does not say the first one works", BusyOnTwoActions);
            AppTests.Test(SuiteName, "hotkeys: a stray comma gives no notice", StrayComma);
            AppTests.Test(SuiteName, "mascot: the Texto tip shows its combo", TipShowsCombo);
            AppTests.Test(SuiteName, "mascot: a cleared Texto combo gives no tip that reads \"Sin atajo\"", TipWithoutCombo);
        }
    }
}

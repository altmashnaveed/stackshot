// Clipping warning, final check round 4 (suite "h4cine"): AudioCapture.ForTest fed directly (no device, no screen).
// Covers what the round-4 fixes touch: CountClipping after gap silence, merging of moments, ForgetClipsFrom on the
// Finish trim, and the ClipText / Saturation wording.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static void H4CineSuite()
        {
            Test("h4cine", "clip-run-broken-by-gap-silence", H4GapBreaksRun);
            Test("h4cine", "clip-run-continues-without-gap", H4NoGapRun);
            Test("h4cine", "trimmed-straddling-moment-frames", H4StraddleTrim);
            Test("h4cine", "moment-before-cut-kept-whole", H4BeforeCut);
            Test("h4cine", "trim-drops-later-moment-keeps-first", H4MixedTrim);
            Test("h4cine", "merged-moment-straddling-cut-count", H4MergedStraddle);
            Test("h4cine", "moment-after-cut-leaves-no-trace", H4AllTrimmed);
            Test("h4cine", "moments-0.2s-apart-merge", H4Merge02);
            Test("h4cine", "moments-0.3s-apart-stay-two", H4Split03);
            Test("h4cine", "moments-11999-frames-apart-merge", H4MergeJustUnder);
            Test("h4cine", "moments-12000-frames-apart-stay-two", H4MergeEdge);
            Test("h4cine", "threshold-0999-counts-three-in-row", H4Threshold);
            Test("h4cine", "clip-text-held-ms-and-seconds", H4TextHeld);
            Test("h4cine", "clip-text-clock-past-hour", H4TextClock);
            Test("h4cine", "saturation-line-joins-sources", H4SaturationLine);
        }

        // ---- helpers ----

        // Stereo full-scale frames, `len` long from each start; silence elsewhere. `total` frames.
        static float[] H4Bursts(int total, int[] starts, int len)
        {
            float[] s = new float[total * 2];
            foreach (int st in starts)
                for (int i = st; i < st + len && i < total; i++) { s[2 * i] = 1f; s[2 * i + 1] = 1f; }
            return s;
        }

        static float[] H4ReadFloats(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            float[] f = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
            return f;
        }

        // Frames of the file whose left sample is at full scale.
        static int H4LoudFrames(float[] f)
        {
            int n = 0;
            for (int i = 0; i < f.Length / 2; i++) if (Math.Abs(f[2 * i]) >= 0.999f) n++;
            return n;
        }

        static string H4Culture(double d) { return d.ToString("0.0", CultureInfo.CurrentCulture); }

        // ---- the gap and the clip run ----

        // Frames 100 and 101 at full scale end one packet; 0.1 s of silence is inserted; then one full-scale frame.
        // The file has no three in a row, so no moment.
        static void H4GapBreaksRun()
        {
            string dir = WorkDir("h4-gap-run");
            try
            {
                string path = TempAudioPath(dir, "gap-run");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(102, new[] { 100 }, 2), 102, false, 0);
                ac.Feed(H4Bursts(100, new[] { 0 }, 1), 100, false, 1021250);
                ac.Finish(5002);
                int loud = H4LoudFrames(H4ReadFloats(path));
                Note("en el fichero " + loud + " muestras a fondo de escala (100, 101 y 4902), separadas por silencio; ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 0, "el hueco de silencio no corta la racha: el aviso cuenta " + ac.ClipEvents + " momento(s), " + ac.ClipFrames + " muestras; en el fichero no hay tres seguidas");
            }
            finally { DeleteDir(dir); }
        }

        // Control: the same two frames and one more, but contiguous (no gap): three in a row in the file, one moment.
        static void H4NoGapRun()
        {
            string dir = WorkDir("h4-nogap-run");
            try
            {
                string path = TempAudioPath(dir, "nogap-run");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(102, new[] { 100 }, 2), 102, false, 0);
                ac.Feed(H4Bursts(100, new[] { 0 }, 1), 100, false, 21250);
                ac.Finish(202);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", FirstClipSeconds " + Inv(ac.FirstClipSeconds));
                Check(ac.ClipEvents == 1, "sin hueco, tres seguidas: " + ac.ClipEvents + " momento(s), esperado 1");
                Check(Math.Abs(ac.FirstClipSeconds - 102 / 48000.0) < 0.0001, "primer clip a " + Inv(ac.FirstClipSeconds) + " s, esperado 0.002125 s");
            }
            finally { DeleteDir(dir); }
        }

        // ---- the Finish trim ----

        // A held passage of 0.1 s; Finish keeps only its first 0.05 s. The file has 2400 full-scale frames at most.
        static void H4StraddleTrim()
        {
            string dir = WorkDir("h4-straddle");
            try
            {
                string path = TempAudioPath(dir, "straddle");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(4800, new[] { 0 }, 4800), 4800, false, 0);
                ac.Finish(2400);
                int loud = H4LoudFrames(H4ReadFloats(path));
                Note("en el fichero " + loud + " muestras a fondo de escala; ClipFrames " + ac.ClipFrames + " -> " + Session.ClipText(ac));
                Check(ac.ClipFrames <= 2400, "ClipFrames " + ac.ClipFrames + " con 2400 fotogramas en el fichero (" + Session.ClipText(ac) + ")");
            }
            finally { DeleteDir(dir); }
        }

        // Control: a moment that ends before the cut is kept whole (2398 counted, the first two are never counted).
        static void H4BeforeCut()
        {
            string dir = WorkDir("h4-before-cut");
            try
            {
                string path = TempAudioPath(dir, "before-cut");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(4800, new[] { 0 }, 2400), 4800, false, 0);
                ac.Finish(2400);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", TrimmedFrames " + ac.TrimmedFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 2398, "momento antes del corte: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 2398");
            }
            finally { DeleteDir(dir); }
        }

        // A moment at 1000..1499 stays; one at 20000..20499 (far apart, no merge) starts after the cut at 3000 and goes.
        static void H4MixedTrim()
        {
            string dir = WorkDir("h4-mixed-trim");
            try
            {
                string path = TempAudioPath(dir, "mixed-trim");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(21000, new[] { 1000, 20000 }, 500), 21000, false, 0);
                ac.Finish(3000);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", FirstClipSeconds " + Inv(ac.FirstClipSeconds));
                Check(ac.ClipEvents == 1, "tras el corte quedan " + ac.ClipEvents + " momento(s), esperado 1");
                Check(ac.ClipFrames == 498, "muestras tras el corte: " + ac.ClipFrames + ", esperado 498");
                Check(Math.Abs(ac.FirstClipSeconds - 1002 / 48000.0) < 0.0001, "primer clip a " + Inv(ac.FirstClipSeconds) + " s, esperado 0.02087 s");
            }
            finally { DeleteDir(dir); }
        }

        // Moments at 1000..1499 and 4000..4499 are 2500 frames apart, so they merge into one moment that straddles the
        // cut at 3000. The file keeps 498 clipped frames (1002..1499); the count must not include the trimmed ones.
        static void H4MergedStraddle()
        {
            string dir = WorkDir("h4-merged-straddle");
            try
            {
                string path = TempAudioPath(dir, "merged-straddle");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(4800, new[] { 1000, 4000 }, 500), 4800, false, 0);
                ac.Finish(3000);
                int loud = H4LoudFrames(H4ReadFloats(path));
                Note("en el fichero " + loud + " muestras a fondo de escala; ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipFrames <= 498, "momento fusionado cruza el corte: ClipFrames " + ac.ClipFrames + ", en el fichero 498");
            }
            finally { DeleteDir(dir); }
        }

        // The only moment starts after the cut: no moments, no frames, no first time.
        static void H4AllTrimmed()
        {
            string dir = WorkDir("h4-all-trimmed");
            try
            {
                string path = TempAudioPath(dir, "all-trimmed");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(4800, new[] { 2400 }, 2400), 4800, false, 0);
                ac.Finish(2400);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", FirstClipSeconds " + Inv(ac.FirstClipSeconds));
                Check(ac.ClipEvents == 0 && ac.ClipFrames == 0 && ac.FirstClipSeconds < 0, "el momento cortado sigue contando: " + ac.ClipEvents + " / " + ac.ClipFrames + " / " + Inv(ac.FirstClipSeconds));
            }
            finally { DeleteDir(dir); }
        }

        // ---- merging ----

        // Two bursts of 100 frames, 0.2 s apart (9600 frames): one moment of 196 counted frames.
        static void H4Merge02()
        {
            string dir = WorkDir("h4-merge-02");
            try
            {
                string path = TempAudioPath(dir, "merge-02");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(12000, new[] { 0, 9600 }, 100), 12000, false, 0);
                ac.Finish(12000);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 196, "0.2 s aparte: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 196");
            }
            finally { DeleteDir(dir); }
        }

        // Two bursts 0.3 s apart (14400 frames): two moments.
        static void H4Split03()
        {
            string dir = WorkDir("h4-split-03");
            try
            {
                string path = TempAudioPath(dir, "split-03");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(18000, new[] { 0, 14400 }, 100), 18000, false, 0);
                ac.Finish(18000);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", FirstClipSeconds " + Inv(ac.FirstClipSeconds));
                Check(ac.ClipEvents == 2, "0.3 s aparte: " + ac.ClipEvents + " momento(s), esperado 2");
            }
            finally { DeleteDir(dir); }
        }

        // Last clipped frame of the first burst is 99, first counted frame of the second is start + 2: a distance of
        // 11999 frames (just under a quarter second) merges.
        static void H4MergeJustUnder()
        {
            string dir = WorkDir("h4-merge-under");
            try
            {
                string path = TempAudioPath(dir, "merge-under");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(12200, new[] { 0, 12096 }, 100), 12200, false, 0);
                ac.Finish(12200);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1, "11999 fotogramas aparte: " + ac.ClipEvents + " momentos, esperado 1");
            }
            finally { DeleteDir(dir); }
        }

        // The same, 12000 frames apart (exactly a quarter second): the comment in Audio.cs says "closer than" merges,
        // so a distance of exactly a quarter second should not merge.
        static void H4MergeEdge()
        {
            string dir = WorkDir("h4-merge-edge");
            try
            {
                string path = TempAudioPath(dir, "merge-edge");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H4Bursts(12300, new[] { 0, 12097 }, 100), 12300, false, 0);
                ac.Finish(12300);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 2, "12000 fotogramas aparte (un cuarto de segundo exacto): " + ac.ClipEvents + " momento(s), esperado 2");
            }
            finally { DeleteDir(dir); }
        }

        // ---- threshold ----

        // Three samples at 0.9989 in a row: nothing. Three at 0.999 (the threshold): one moment.
        static void H4Threshold()
        {
            string dir = WorkDir("h4-threshold");
            try
            {
                string path = TempAudioPath(dir, "threshold");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                float[] s = new float[20 * 2];
                for (int i = 0; i < 3; i++) { s[2 * i] = 0.9989f; s[2 * i + 1] = 0.9989f; }
                for (int i = 10; i < 13; i++) { s[2 * i] = 0.999f; s[2 * i + 1] = 0.999f; }
                ac.Feed(s, 20, false, 0);
                ac.Finish(20);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 1, "umbral 0.999: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 1 (solo el de 0.999)");
            }
            finally { DeleteDir(dir); }
        }

        // ---- ClipText wording ----

        // Held time: ms under a second (rounded, at least 1 ms), seconds from 0.9995 on.
        static void H4TextHeld()
        {
            long[] frames = { 1, 2400, 19, 24000, 47974, 47977, 480000 };
            string[] want = { "1 ms", "50 ms", "1 ms", "500 ms", "999 ms", H4Culture(1.0) + " s", H4Culture(10.0) + " s" };
            string dir = WorkDir("h4-text-held");
            try
            {
                List<string> bad = new List<string>();
                for (int k = 0; k < frames.Length; k++)
                {
                    AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "held" + k), true);
                    ac.Finish(0);
                    ac.ClipEvents = 1;
                    ac.ClipFrames = frames[k];
                    ac.FirstClipSeconds = 5;
                    string t = Session.ClipText(ac);
                    if (!t.Contains(want[k] + " a fondo de escala)")) bad.Add(frames[k] + " -> '" + t + "' (esperado '" + want[k] + "')");
                }
                Note(bad.Count == 0 ? "7 casos de duracion correctos" : string.Join(" ; ", bad.ToArray()));
                Check(bad.Count == 0, "textos de duracion: " + string.Join(" ; ", bad.ToArray()));
            }
            finally { DeleteDir(dir); }
        }

        // The clock of the first moment: m:ss under an hour, h:mm:ss from one hour on.
        static void H4TextClock()
        {
            double[] at = { 0, 59.9, 61, 3599.9, 3600, 3723.4, 36000, 90000 };
            string[] want = { "0:00", "0:59", "1:01", "59:59", "1:00:00", "1:02:03", "10:00:00", "25:00:00" };
            string dir = WorkDir("h4-text-clock");
            try
            {
                List<string> bad = new List<string>();
                for (int k = 0; k < at.Length; k++)
                {
                    AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "clock" + k), true);
                    ac.Finish(0);
                    ac.ClipEvents = 1;
                    ac.ClipFrames = 48;
                    ac.FirstClipSeconds = at[k];
                    string t = Session.ClipText(ac);
                    if (!t.Contains("(a " + want[k] + ", ")) bad.Add(Inv(at[k]) + " -> '" + t + "' (esperado '" + want[k] + "')");
                }
                Note(bad.Count == 0 ? "8 casos de reloj correctos" : string.Join(" ; ", bad.ToArray()));
                Check(bad.Count == 0, "relojes: " + string.Join(" ; ", bad.ToArray()));
            }
            finally { DeleteDir(dir); }
        }

        // The notice line: one source, both sources, none.
        static void H4SaturationLine()
        {
            string dir = WorkDir("h4-saturation-line");
            try
            {
                AudioCapture sys = AudioCapture.ForTest(TempAudioPath(dir, "sys"), true);
                sys.Finish(0);
                sys.ClipEvents = 1;
                sys.ClipFrames = 4800;
                sys.FirstClipSeconds = 5;
                AudioCapture mic = AudioCapture.ForTest(TempAudioPath(dir, "mic"), true);
                mic.Finish(0);
                mic.ClipEvents = 2;
                mic.ClipFrames = 9600;
                mic.FirstClipSeconds = 61;

                string one = Session.Saturation(sys, null);
                string both = Session.Saturation(sys, mic);
                string none = Session.Saturation(null, null);
                string wantOne = "Ojo: el sonido del equipo " + Session.ClipText(sys) + ".";
                string wantBoth = "Ojo: el sonido del equipo " + Session.ClipText(sys) + " y el micr\u00F3fono " + Session.ClipText(mic) + ".";
                Note("una fuente: " + one + " | las dos: " + both);
                Check(one == wantOne, "una fuente: '" + one + "', esperado '" + wantOne + "'");
                Check(both == wantBoth, "las dos: '" + both + "', esperado '" + wantBoth + "'");
                Check(none == null, "sin clip el aviso no es null: '" + none + "'");
            }
            finally { DeleteDir(dir); }
        }
    }
}

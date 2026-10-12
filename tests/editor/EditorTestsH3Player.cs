// h3-player (suite "h3player"): the player's resume store, pieces (E/S), frame grab (C), quality menu and bar, headless.
// No window is shown, the screen and the clipboard are never touched, and the resume store is a private file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static string h3Store;

        static void H3pSuite()
        {
            string oldStore = PlayerKit.ResumeFile;
            h3Store = Path.Combine(WorkDir("h3-store"), "posiciones.txt");
            PlayerKit.ResumeFile = h3Store;
            try
            {
                Test("h3player", "resume-barely-started-erases-saved-spot", H3pResumeBarely);
                Test("h3player", "resume-home-then-close-after-3s-keeps-spot", H3pResumeHomeWindow);
                Test("h3player", "resume-two-windows-either-close-order", H3pResumeTwoWindows);
                Test("h3player", "resume-path-case-variant-finds-spot", H3pResumeCase);
                Test("h3player", "resume-path-257-chars-accents-emoji", H3pResumeLongPath);
                Test("h3player", "resume-store-locked-or-not-a-file-no-throw", H3pResumeLocked);
                Test("h3player", "resume-moved-then-back", H3pResumeMoved);
                Test("h3player", "resume-webm-without-duration-window", H3pResumeWebm);
                Test("h3player", "export-webm-without-duration-whole-and-piece", H3pExportWebm);
                Test("h3player", "piece-gop-bframes-exact-first-and-last-frame", H3pPieceGop);
                Test("h3player", "piece-E-at-start-S-at-last-frame-keys", H3pPieceFirstLastKeys);
                Test("h3player", "marks-S-before-E-and-E-after-S", H3pMarksOrder);
                Test("h3player", "marks-cleared-by-opening-another-file", H3pMarksClearOnOpen);
                Test("h3player", "marks-survive-frame-steps", H3pMarksSurviveSteps);
                Test("h3player", "piece-mov", H3pPieceMov);
                Test("h3player", "piece-webm", H3pPieceWebm);
                Test("h3player", "piece-avi", H3pPieceAvi);
                Test("h3player", "piece-ts", H3pPieceTs);
                Test("h3player", "piece-of-source-without-sound", H3pPieceNoSound);
                Test("h3player", "piece-two-frames-0.067s", H3pPieceTwoFrames);
                Test("h3player", "piece-16-mb-quality", H3pPieceSixteenMb);
                Test("h3player", "piece-name-when-tramo-exists", H3pPieceNameTaken);
                Test("h3player", "grab-while-playing-pauses-and-saves", H3pGrabWhilePlaying);
                Test("h3player", "grab-on-last-frame", H3pGrabLastFrame);
                Test("h3player", "grab-rotated-phone-clip", H3pGrabRotated);
                Test("h3player", "grab-rotated-anamorphic-display-size", H3pGrabRotatedAnamorphic);
                Test("h3player", "grab-10bit-422-444-match-ffmpeg", H3pGrabDepths);
                Test("h3player", "grab-1x1-and-odd-size", H3pGrabTinyAndOdd);
                Test("h3player", "grab-two-quick-presses-one-picture", H3pGrabTwoPresses);
                Test("h3player", "grab-target-name-blocked-reports-no-crash", H3pGrabBlocked);
                Test("h3player", "grab-window-closed-during-grab-five-times", H3pGrabClosedDuring);
                Test("h3player", "quality-can-export-table-by-extension", H3pQualityTable);
                Test("h3player", "bar-renders-every-mark-state-offscreen", H3pRenderStates);
                Test("h3player", "end-key-with-unknown-duration", H3pEndUnknownDuration);
                Test("h3player", "export-unknown-duration-not-called-too-long", H3pExportUnknownDuration);
                Test("h3player", "video-export-run-throws-when-ffmpeg-missing", H3pRunMissingFfmpeg);
            }
            finally
            {
                PlayerKit.ResumeFile = oldStore;
                PlayerWindow.Quiet = false;
            }
        }

        // ---- helpers ----

        static object H3Get(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "no field " + name);
            return f.GetValue(o);
        }

        static void H3SetField(object o, string name, object v)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "no field " + name);
            f.SetValue(o, v);
        }

        static object H3Call(object o, string name, params object[] args)
        {
            MethodInfo m = o.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Check(m != null, "no method " + name);
            try { return m.Invoke(o, args); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }

        static bool H3CanExport(PlayerWindow w)
        {
            PropertyInfo p = typeof(PlayerWindow).GetProperty("CanExport", BindingFlags.NonPublic | BindingFlags.Instance);
            return (bool)p.GetValue(w, null);
        }

        static bool H3Wait(Func<bool> done, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!done() && sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
            return done();
        }

        static double H3Pos(PlayerWindow w) { return w.Engine != null ? w.Engine.Position : -1; }

        static string H3Fmt(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        static PlayerWindow H3Window(string path, Settings st)
        {
            PlayerWindow w = PwWindow(path, new PlFakeSound(), st);
            w.Handle.ToString();
            Check(w.Media != null && w.Media.CanPlay, "the window cannot play " + Path.GetFileName(path));
            return w;
        }

        // Starts from the saved spot (BeginPlay, as the form does on show) and waits until the picture is at least at minPos.
        static void H3Play(PlayerWindow w, double minPos)
        {
            H3Call(w, "BeginPlay");
            Check(H3Wait(delegate { return H3Pos(w) >= minPos; }, 8000), "the picture is at " + H3Fmt(H3Pos(w)) + " s, not " + H3Fmt(minPos) + " s");
        }

        static void H3SeekTo(PlayerWindow w, double t)
        {
            H3Call(w, "SeekTo", t);
            bool ok = H3Wait(delegate { return Math.Abs(H3Pos(w) - t) < 0.05; }, 5000);
            Check(ok, "the position is " + H3Fmt(H3Pos(w)) + " s, not " + H3Fmt(t));
        }

        // The window's own export (the bar and the menu call ExportMp4), quiet, waiting for its end.
        static void H3Export(PlayerWindow w)
        {
            PlayerWindow.Quiet = true;
            H2Export(w);
        }

        // Exports the piece [a, b] of a window with its marks set directly, and returns the one file it left, or fails.
        static string H3PieceExport(PlayerWindow w, double a, double b, string suffix)
        {
            H3SetField(w, "markIn", a);
            H3SetField(w, "markOut", b);
            H3Export(w);
            string[] outs = Directory.GetFiles(Path.GetDirectoryName(w.FilePath), "* " + suffix + ".mp4");
            Check(outs.Length == 1, "the piece left " + outs.Length + " files; note: " + w.ExportNote);
            Check(w.ExportNote == "ok", "the check said: " + w.ExportNote);
            return outs[0];
        }

        // FFmpeg's stdout into a file. A pipe is not seekable, so the file keeps no duration in its header (as some WebMs).
        static void H3PipeTo(string args, string file)
        {
            using (Process p = new Process())
            {
                p.StartInfo.FileName = ffmpeg;
                p.StartInfo.Arguments = "-nostdin " + args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.ErrorDataReceived += delegate { };
                p.Start();
                ChildJob.Add(p);
                p.BeginErrorReadLine();
                using (FileStream fs = File.Create(file)) p.StandardOutput.BaseStream.CopyTo(fs);
                bool done = p.WaitForExit(120000);
                Check(done && p.ExitCode == 0, "ffmpeg (pipe) ended with " + p.ExitCode);
            }
        }

        // The luma (Y) of the first pixel of every decoded frame.
        static List<int> H3FirstLumas(string file, int w, int h)
        {
            string raw = file + ".y.raw";
            FfOk("-hide_banner -loglevel error -y -i " + Q(file) + " -f rawvideo -pix_fmt yuv420p " + Q(raw), 120000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            int frame = w * h * 3 / 2;
            List<int> r = new List<int>();
            for (int o = 0; o + frame <= b.Length; o += frame) r.Add(b[o]);
            return r;
        }

        static string H3Clip(string dir, string name, string vf, string codecs)
        {
            string path = Path.Combine(dir, name);
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"" + vf + "\" " + codecs + " " + Q(path), 120000);
            return path;
        }

        // Runs fn with the resume store pointed at a file, and puts the suite's store back.
        static void H3WithStore(string file, Action fn)
        {
            string old = PlayerKit.ResumeFile;
            PlayerKit.ResumeFile = file;
            try { fn(); }
            finally { PlayerKit.ResumeFile = old; }
        }

        const string H3X264 = "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac";

        // ---- resume store ----

        static void H3pResumeBarely()
        {
            string dir = WorkDir("h3-barely");
            string v = Path.Combine(dir, "pelicula larga.mkv");
            File.WriteAllBytes(v, new byte[1000]);
            // Watched to 25:00 once; a later look that starts from the beginning (Inicio) and stops after 5 s.
            PlayerKit.ResumeSave(v, 1500, 3000);
            PlayerKit.ResumeSave(v, 5, 3000);
            double at = PlayerKit.ResumeAt(v, 3000);
            Check(Math.Abs(at - 1500) < 0.001, "the 25:00 spot is gone (resumes at " + at + " s); docs/cine-playback.md says a view under 10 s is not saved");
        }

        static void H3pResumeHomeWindow()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-home");
            string clip = H3Clip(dir, "larga.mkv", "testsrc2=s=160x90:r=5:d=130", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            double dur = MediaInfo.Probe(ffmpeg, clip).Duration;
            PlayerKit.ResumeSave(clip, 61, dur);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3Play(w, 60.5);
                PwKey(w, Keys.Home);
                H3Wait(delegate { return false; }, 3000);   // plays on from 0 for about 3 s
                H3Call(w, "RememberPosition");                // what closing the window does
                double at = PlayerKit.ResumeAt(clip, dur);
                Check(Math.Abs(at - 61) < 0.001, "after Inicio and a close at " + H3Fmt(H3Pos(w)) + " s it resumes at " + at + " s, not 61 s");
            }
            finally { w.Dispose(); }
        }

        static void H3pResumeTwoWindows()
        {
            string dir = WorkDir("h3-two");
            string v = Path.Combine(dir, "doble.mkv");
            File.WriteAllBytes(v, new byte[1000]);
            // Two windows of one video: both opened before anything was saved (each starts at 0).
            // Order 1: A closes at 5:00, then B closes after 5 s. Order 2: B first, then A.
            File.Delete(h3Store);
            PlayerKit.ResumeSave(v, 300, 600);
            PlayerKit.ResumeSave(v, 5, 600);
            double first = PlayerKit.ResumeAt(v, 600);
            File.Delete(h3Store);
            PlayerKit.ResumeSave(v, 5, 600);
            PlayerKit.ResumeSave(v, 300, 600);
            double second = PlayerKit.ResumeAt(v, 600);
            Check(Math.Abs(first - 300) < 0.001 && Math.Abs(second - 300) < 0.001,
                  "A closes at 300 s then B at 5 s: resumes at " + first + " s; B then A: " + second + " s (300 expected both ways)");
        }

        static void H3pResumeCase()
        {
            string dir = WorkDir("h3-case");
            string v = Path.Combine(dir, "Clip Mixto.mkv");
            File.WriteAllBytes(v, new byte[1000]);
            string upper = Path.Combine(dir, "CLIP MIXTO.MKV");
            Check(File.Exists(upper), "the folder is case-sensitive: the test does not apply");
            PlayerKit.ResumeSave(v, 300, 600);
            double at = PlayerKit.ResumeAt(upper, 600);
            Check(Math.Abs(at - 300) < 0.001, "the same file under another spelling resumes at " + at + " s (saved as 'Clip Mixto.mkv')");
        }

        static void H3pResumeLongPath()
        {
            string dir = WorkDir("h3-long");
            // The whole path is 257 characters: accents and an emoji (a surrogate pair) in the file name.
            string head = Path.Combine(dir, "");
            int n = 257 - head.Length - ".mkv".Length - "pel\u00EDcula \u00F1 \uD83C\uDFAC ".Length;
            string name = "pel\u00EDcula \u00F1 \uD83C\uDFAC " + new string('x', n) + ".mkv";
            string v = Path.Combine(dir, name);
            File.WriteAllBytes(v, new byte[1000]);
            Add("path " + v.Length + " characters");
            PlayerKit.ResumeSave(v, 300, 600);
            double at = PlayerKit.ResumeAt(v, 600);
            Check(Math.Abs(at - 300) < 0.001, "a 257-character path resumes at " + at + " s");
        }

        static void H3pResumeLocked()
        {
            string dir = WorkDir("h3-locked");
            string v = Path.Combine(dir, "x.mkv");
            File.WriteAllBytes(v, new byte[1000]);
            PlayerKit.ResumeSave(v, 300, 600);
            string store = PlayerKit.ResumeFile;
            string before = File.ReadAllText(store);
            using (FileStream held = new FileStream(store, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                PlayerKit.ResumeAt(v, 600);
                PlayerKit.ResumeSave(v, 100, 600);
            }
            Check(File.ReadAllText(store) == before, "the store changed while another process held it");
            string folderStore = Path.Combine(dir, "como-carpeta.txt");
            Directory.CreateDirectory(folderStore);
            H3WithStore(folderStore, delegate
            {
                PlayerKit.ResumeSave(v, 100, 600);
                PlayerKit.ResumeAt(v, 600);
            });
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "a temporary store file was left");
        }

        static void H3pResumeMoved()
        {
            string dir = WorkDir("h3-moved");
            string v = Path.Combine(dir, "mov.mkv");
            File.WriteAllBytes(v, new byte[1000]);
            PlayerKit.ResumeSave(v, 300, 600);
            string moved = Path.Combine(dir, "sub", "mov.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(moved));
            File.Move(v, moved);
            Check(PlayerKit.ResumeAt(moved, 600) == 0, "a moved file resumes at the old spot");
            File.Move(moved, v);
            double at = PlayerKit.ResumeAt(v, 600);
            Check(Math.Abs(at - 300) < 0.001, "back in its place it resumes at " + at + " s, not 300");
        }

        static void H3pResumeWebm()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-webm");
            string webm = Path.Combine(dir, "sin duracion.webm");
            H3PipeTo("-hide_banner -loglevel error -f lavfi -i \"testsrc2=s=160x90:r=10:d=130\" -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 200k -f webm -", webm);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, webm);
            Check(mi.CanPlay, "the webm does not play");
            Check(mi.Duration > 120, "the duration from the scan is " + mi.Duration + " s (the header has none, about 130 s expected)");
            PlayerKit.ResumeSave(webm, 61, mi.Duration);
            PlayerWindow w = H3Window(webm, new Settings());
            try
            {
                H3Play(w, 60.5);
                Add("window duration " + H3Fmt(w.Media.Duration) + " s, started at " + H3Fmt(H3Pos(w)) + " s");
            }
            finally { w.Dispose(); }
        }

        static void H3pExportWebm()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-webm-exp");
            string webm = Path.Combine(dir, "sin duracion.webm");
            H3PipeTo("-hide_banner -loglevel error -f lavfi -i \"testsrc2=s=160x90:r=10:d=130\" -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 200k -f webm -", webm);
            PlayerWindow w = H3Window(webm, new Settings());
            try
            {
                H3Export(w);
                string[] outs = Directory.GetFiles(dir, "* (para compartir).mp4");
                Check(outs.Length == 1, "the whole export left " + outs.Length + " files; note: " + w.ExportNote);
                Check(w.ExportNote == "ok", "the whole export's check said: " + w.ExportNote);
                double d = DurationOf(Probe(outs[0]));
                Check(Math.Abs(d - 130) < 0.6, "the whole MP4 lasts " + d + " s, the webm about 130 s");
                string piece = H3PieceExport(w, 10, 12, "(tramo)");
                double dp = DurationOf(Probe(piece));
                Check(Math.Abs(dp - 2) < 0.1, "the piece lasts " + dp + " s, marked 2 s");
            }
            finally { w.Dispose(); }
        }

        // ---- pieces ----

        static void H3pPieceGop()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-gop");
            // Frame k has luma 16 + k (mod 200), lossless, with B-frames and open GOPs: the piece [1 s, 2 s] starts at frame 30.
            string src = H3Clip(dir, "gop.mkv", "color=c=black:s=160x90:r=30:d=4,geq=lum='16+mod(N,200)':cb=128:cr=128,format=yuv420p",
                                "-c:v libx264 -preset ultrafast -crf 0 -bf 3 -x264-params open-gop=1:keyint=60");
            List<int> srcY = H3FirstLumas(src, 160, 90);
            Check(srcY.Count == 120 && Math.Abs(srcY[30] - 46) <= 2, "the fixture is not what it should be: " + srcY.Count + " frames, frame 30 luma " + srcY[30]);
            PlayerWindow w = H3Window(src, new Settings());
            try
            {
                string piece = H3PieceExport(w, 1.0, 2.0, "(tramo)");
                List<int> ys = H3FirstLumas(piece, 160, 90);
                Add("piece frames " + ys.Count + ", first luma " + ys[0] + ", last " + ys[ys.Count - 1]);
                Check(ys.Count >= 29 && ys.Count <= 31, "the piece has " + ys.Count + " frames, 30 expected");
                Check(Math.Abs(ys[0] - 46) <= 3, "the first frame of the piece has luma " + ys[0] + ", the source's frame 30 has 46");
                Check(Math.Abs(ys[ys.Count - 1] - 75) <= 3, "the last frame of the piece has luma " + ys[ys.Count - 1] + ", frame 59 has 75");
            }
            finally { w.Dispose(); }
        }

        static void H3pPieceFirstLastKeys()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                PwKey(w, Keys.Home);
                Check(H3Wait(delegate { return Math.Abs(H3Pos(w)) < 0.05; }, 5000), "Inicio did not go to 0");
                PwKey(w, Keys.E);
                double a = (double)H3Get(w, "markIn");
                Check(Math.Abs(a) < 0.001, "E at the very start marked " + H3Fmt(a));
                PwKey(w, Keys.End);
                double last = 119 / 30.0;
                H3SeekTo(w, last);
                PwKey(w, Keys.S);
                double b = (double)H3Get(w, "markOut");
                Check(b > 3.95, "S on the last frame marked " + H3Fmt(b) + " s, the video is about 4 s");
                string piece = H3PieceExport(w, a, b, "(tramo)");
                double d = DurationOf(Probe(piece));
                Check(Math.Abs(d - b) < 0.1, "the piece lasts " + d + " s, marked " + H3Fmt(b));
                Add("marks " + H3Fmt(a) + " to " + H3Fmt(b));
            }
            finally { w.Dispose(); }
        }

        static void H3pMarksOrder()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3SeekTo(w, 2.0);
                PwKey(w, Keys.E);
                Check(Math.Abs((double)H3Get(w, "markIn") - 2.0) < 0.05, "E at 2 s marked " + H3Fmt((double)H3Get(w, "markIn")));
                H3SeekTo(w, 1.0);
                PwKey(w, Keys.S);
                Check((double)H3Get(w, "markOut") < 0, "S at 1 s before E at 2 s was accepted (markOut " + H3Fmt((double)H3Get(w, "markOut")) + ")");
                string flash = (string)H3Get(w, "flash");
                Check(flash != null && flash.StartsWith("El final tiene que ir despu", StringComparison.Ordinal), "no warning: " + flash);
                H3SeekTo(w, 3.0);
                PwKey(w, Keys.S);
                Check(Math.Abs((double)H3Get(w, "markOut") - 3.0333) < 0.05, "S at 3 s marked " + H3Fmt((double)H3Get(w, "markOut")));
                H3SeekTo(w, 3.5);
                PwKey(w, Keys.E);
                Check((double)H3Get(w, "markOut") < 0, "E at 3.5 s after the end at 3.03 s kept the old end: " + H3Fmt((double)H3Get(w, "markOut")));
            }
            finally { w.Dispose(); }
        }

        static void H3pMarksClearOnOpen()
        {
            NeedFfmpeg();
            string clipA = PwClip(".mkv", H3X264, 4, false, null);
            string clipB = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clipA, new Settings());
            try
            {
                H3SetField(w, "markIn", 1.0);
                H3SetField(w, "markOut", 2.0);
                H3Call(w, "Open", clipB);
                Check((double)H3Get(w, "markIn") < 0 && (double)H3Get(w, "markOut") < 0, "the marks survived opening another file");
                Check(w.FilePath == clipB, "the window did not open the other file");
            }
            finally { w.Dispose(); }
        }

        static void H3pMarksSurviveSteps()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3SetField(w, "markIn", 1.0);
                H3Call(w, "Step", 1);
                PwKey(w, Keys.OemPeriod);
                PwKey(w, Keys.Oemcomma);
                Check(Math.Abs((double)H3Get(w, "markIn") - 1.0) < 0.001, "a frame step moved or cleared the start mark: " + H3Fmt((double)H3Get(w, "markIn")));
            }
            finally { w.Dispose(); }
        }

        static void H3PieceOf(string ext, string codecs, string mux)
        {
            NeedFfmpeg();
            string clip = PwClip(ext, codecs, 4, false, mux);
            if (clip == null) Skip("this FFmpeg cannot write " + ext);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                string piece = H3PieceExport(w, 1.0, 2.0, "(tramo)");
                MediaInfo mi = MediaInfo.Probe(ffmpeg, piece);
                double d = DurationOf(Probe(piece));
                Check(Math.Abs(d - 1.0) < 0.1, ext + ": the piece lasts " + d + " s, marked 1 s");
                Check(mi.Audio.Count == 1, ext + ": the piece has " + mi.Audio.Count + " sound tracks");
            }
            finally { w.Dispose(); }
        }

        static void H3pPieceMov() { H3PieceOf(".mov", H3X264, null); }
        static void H3pPieceWebm() { H3PieceOf(".webm", "-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 300k -c:a libopus", null); }
        static void H3pPieceAvi() { H3PieceOf(".avi", "-c:v mpeg4 -q:v 5 -c:a libmp3lame -b:a 128k", null); }
        static void H3pPieceTs() { H3PieceOf(".ts", H3X264, "-f mpegts"); }

        static void H3pPieceNoSound()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p", 4, true, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                string piece = H3PieceExport(w, 1.0, 2.0, "(tramo)");
                MediaInfo mi = MediaInfo.Probe(ffmpeg, piece);
                Check(mi.Audio.Count == 0, "a piece of a silent source has " + mi.Audio.Count + " sound tracks");
                double d = DurationOf(Probe(piece));
                Check(Math.Abs(d - 1.0) < 0.1, "the piece lasts " + d + " s, marked 1 s");
            }
            finally { w.Dispose(); }
        }

        static void H3pPieceTwoFrames()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                // Two frames at 30 fps: 1/15 s, the shortest piece the bar accepts.
                string piece = H3PieceExport(w, 1.0, 1.0 + 2 / 30.0, "(tramo)");
                double d = DurationOf(Probe(piece));
                Check(d > 0.03 && d < 0.1, "the two-frame piece lasts " + d + " s");
            }
            finally { w.Dispose(); }
        }

        static void H3pPieceSixteenMb()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            Settings st = new Settings();
            st.ExportQuality = 3;
            PlayerWindow w = H3Window(clip, st);
            try
            {
                string piece = H3PieceExport(w, 1.0, 2.0, "(tramo)");
                long bytes = new FileInfo(piece).Length;
                Check(bytes <= 16000000, "a 1 s piece for 16 MB is " + bytes + " bytes");
                double d = DurationOf(Probe(piece));
                Check(Math.Abs(d - 1.0) < 0.1, "the piece lasts " + d + " s");
                Add(bytes + " bytes");
            }
            finally { w.Dispose(); }
        }

        static void H3pPieceNameTaken()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            string dir = Path.GetDirectoryName(clip);
            string taken = Path.Combine(dir, "clip (tramo).mp4");
            File.WriteAllBytes(taken, new byte[] { 1, 2, 3 });
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3SetField(w, "markIn", 1.0);
                H3SetField(w, "markOut", 2.0);
                H3Export(w);
                Check(w.ExportNote == "ok", "the check said: " + w.ExportNote);
                Check(File.ReadAllBytes(taken).Length == 3, "the existing (tramo).mp4 was overwritten");
                Check(File.Exists(Path.Combine(dir, "clip (tramo) (2).mp4")), "the new piece has no free name");
            }
            finally { w.Dispose(); }
        }

        // ---- frame grab ----

        static void H3pGrabWhilePlaying()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            string dir = Path.GetDirectoryName(clip);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3Play(w, 0.2);
                Check(w.Engine.State == PlayState.Playing, "the window is not playing before the grab");
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), "the grab did not end in 30 s");
                Check(w.Engine.State != PlayState.Playing, "playback went on after the grab");
                string[] pngs = Directory.GetFiles(dir, "clip fotograma *.png");
                Check(pngs.Length == 1, "the grab left " + pngs.Length + " pictures");
                using (Bitmap b = new Bitmap(pngs[0]))
                    Check(b.Width == 320 && b.Height == 180, "the picture is " + b.Width + "x" + b.Height);
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabLastFrame()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            string dir = Path.GetDirectoryName(clip);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                PwKey(w, Keys.End);
                Check(H3Wait(delegate { return Math.Abs(H3Pos(w) - 119 / 30.0) < 0.05; }, 8000), "End did not reach the last frame: " + H3Fmt(H3Pos(w)));
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), "the grab did not end in 30 s");
                string flash = (string)H3Get(w, "flash");
                Check(flash == null || !flash.StartsWith("No se pudo", StringComparison.Ordinal), "the last frame was not grabbed: " + flash);
                Check(Directory.GetFiles(dir, "clip fotograma *.png").Length == 1, "no picture of the last frame");
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabRotated()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-rot");
            string plain = H3Clip(dir, "plain.mp4", "testsrc2=s=320x180:r=30:d=2", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            string rot = Path.Combine(dir, "phone.mp4");
            // The rotation is an input option of the remux (a copy), so the MP4 carries a display matrix of 90 degrees.
            FfOk("-hide_banner -loglevel error -display_rotation:v:0 90 -i " + Q(plain) + " -c copy " + Q(rot), 120000);
            PlayerWindow w = H3Window(rot, new Settings());
            try
            {
                Size d = w.Media.Display;
                Check(d.Width == 180 && d.Height == 320, "the rotated clip shows at " + d.Width + "x" + d.Height + ", 180x320 expected");
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), "the grab did not end in 30 s");
                string[] pngs = Directory.GetFiles(dir, "phone fotograma *.png");
                Check(pngs.Length == 1, "the grab left " + pngs.Length + " pictures; flash: " + H3Get(w, "flash"));
                using (Bitmap b = new Bitmap(pngs[0]))
                    Check(b.Width == 180 && b.Height == 320, "the picture is " + b.Width + "x" + b.Height + ", 180x320 expected");
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabRotatedAnamorphic()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-rot-sar");
            // 1440x1080 with 4:3 pixels is shown at 1920x1080; turned sideways it must show at 1080x1920.
            string plain = H3Clip(dir, "hdv.mp4", "testsrc2=s=1440x1080:r=30:d=1", "-vf setsar=4/3 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            string rot = Path.Combine(dir, "hdv-rot.mp4");
            FfOk("-hide_banner -loglevel error -display_rotation:v:0 90 -i " + Q(plain) + " -c copy " + Q(rot), 120000);
            PlayerWindow w = H3Window(rot, new Settings());
            try
            {
                Size d = w.Media.Display;
                Check(d.Width == 1080 && d.Height == 1920, "the anamorphic clip turned sideways shows at " + d.Width + "x" + d.Height + ", 1080x1920 expected (stored " + w.Media.Width + "x" + w.Media.Height + ", SAR " + w.Media.SarNum + ":" + w.Media.SarDen + ")");
            }
            finally { w.Dispose(); }
        }

        static void H3GrabCompare(string clip, string label, string dir)
        {
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), label + ": the grab did not end");
                string[] pngs = Directory.GetFiles(dir, "* fotograma *.png");
                Check(pngs.Length == 1, label + ": " + pngs.Length + " pictures; flash: " + H3Get(w, "flash"));
                string refPng = Path.Combine(dir, label + ".ref.png");
                FfOk("-hide_banner -loglevel error -y -i " + Q(clip) + " -frames:v 1 " + Q(refPng), 60000);
                using (Bitmap b = new Bitmap(pngs[0]))
                using (Bitmap r = new Bitmap(refPng))
                {
                    Check(b.Width == r.Width && b.Height == r.Height, label + ": " + b.Width + "x" + b.Height + " against " + r.Width + "x" + r.Height);
                    double sum = 0;
                    int n = 0;
                    for (int y = 0; y < b.Height; y += 2)
                        for (int x = 0; x < b.Width; x += 2)
                        {
                            Color p = b.GetPixel(x, y), q = r.GetPixel(x, y);
                            sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                            n++;
                        }
                    double mean = sum / (n * 3.0);
                    Check(mean < 4, label + ": differs from ffmpeg's frame by " + mean.ToString("0.00", CultureInfo.InvariantCulture) + " per channel");
                    Add(label + " " + mean.ToString("0.00", CultureInfo.InvariantCulture));
                }
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabDepths()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-depth");
            string[] formats = { "yuv420p10le", "yuv422p", "yuv444p" };
            foreach (string pf in formats)
            {
                string codec = "-c:v libx264 -preset ultrafast -crf 0 -pix_fmt " + pf;
                string clip = H3Clip(dir, pf + ".mkv", "testsrc2=s=160x90:r=30:d=1", codec);
                string sub = Path.Combine(dir, pf);
                Directory.CreateDirectory(sub);
                File.Move(clip, Path.Combine(sub, "clip.mkv"));
                H3GrabCompare(Path.Combine(sub, "clip.mkv"), pf, sub);
            }
        }

        static void H3pGrabTinyAndOdd()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-tiny");
            string tiny = H3Clip(dir, "tiny.mkv", "color=c=red:s=1x1:r=30:d=1", "-c:v ffv1");
            string odd = H3Clip(dir, "odd.mkv", "testsrc2=s=161x91:r=30:d=1,format=yuv444p", "-c:v ffv1");
            foreach (string clip in new string[] { tiny, odd })
            {
                PlayerWindow w = H3Window(clip, new Settings());
                try
                {
                    H3Call(w, "GrabFrame");
                    Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), Path.GetFileName(clip) + ": the grab did not end");
                    string[] pngs = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(clip) + " fotograma *.png");
                    Check(pngs.Length == 1, Path.GetFileName(clip) + ": " + pngs.Length + " pictures; flash: " + H3Get(w, "flash"));
                    using (Bitmap b = new Bitmap(pngs[0]))
                    {
                        int ww = clip == tiny ? 1 : 161, hh = clip == tiny ? 1 : 91;
                        Check(b.Width == ww && b.Height == hh, Path.GetFileName(clip) + ": the picture is " + b.Width + "x" + b.Height + ", " + ww + "x" + hh + " expected");
                    }
                }
                finally { w.Dispose(); }
            }
        }

        static void H3pGrabTwoPresses()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            string dir = Path.GetDirectoryName(clip);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3Call(w, "GrabFrame");
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), "the grab did not end");
                Check(Directory.GetFiles(dir, "clip fotograma *.png").Length == 1, "two quick presses left " + Directory.GetFiles(dir, "clip fotograma *.png").Length + " pictures");
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabBlocked()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            string dir = Path.GetDirectoryName(clip);
            // The name the grab at 0:00 would take is a folder: the picture cannot be written there.
            Directory.CreateDirectory(Path.Combine(dir, "clip fotograma 0.00.png"));
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3Call(w, "GrabFrame");
                Check(H3Wait(delegate { return !(bool)H3Get(w, "grabbing"); }, 30000), "the grab did not end");
                string flash = (string)H3Get(w, "flash");
                Check(flash != null && flash.StartsWith("No se pudo guardar", StringComparison.Ordinal), "the message was: " + flash);
            }
            finally { w.Dispose(); }
        }

        static void H3pGrabClosedDuring()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            for (int i = 0; i < 5; i++)
            {
                PlayerWindow w = H3Window(clip, new Settings());
                H3Call(w, "GrabFrame");
                w.Dispose();
                H3Wait(delegate { return false; }, 400);
            }
            Add("5 windows closed during a grab");
        }

        // ---- quality menu ----

        static void H3pQualityTable()
        {
            NeedFfmpeg();
            string mp4 = PwClip(".mp4", H3X264, 2, false, null);
            string dir = Path.GetDirectoryName(mp4);
            string upper = Path.Combine(dir, "CLIP-UP.MP4");
            File.Copy(mp4, upper);
            string mov = H3Clip(dir, "clip2.mov", "testsrc2=s=320x180:r=30:d=2", H3X264);
            string mkv = H3Clip(dir, "clip2.mkv", "testsrc2=s=320x180:r=30:d=2", H3X264);
            string[] clips = { mp4, upper, mov, mkv };
            string[] names = { "mp4", "MP4", "mov", "mkv" };
            // Whole files: a .mp4 is only offered at 16 and 25 MB (at Maxima it would be a copy); other containers always.
            string[] want = { "FFFTT", "FFFTT", "TTTTT", "TTTTT" };
            Settings st = new Settings();
            List<string> got = new List<string>();
            for (int c = 0; c < clips.Length; c++)
            {
                PlayerWindow w = H3Window(clips[c], st);
                try
                {
                    string row = "";
                    for (int q = 0; q <= 4; q++)
                    {
                        st.ExportQuality = q;
                        row += H3CanExport(w) ? "T" : "F";
                    }
                    got.Add(names[c] + "=" + row);
                    Check(row == want[c], names[c] + " at qualities 0..4 can export " + row + ", " + want[c] + " expected");
                }
                finally { w.Dispose(); }
            }
            Add(string.Join(" ", got.ToArray()));
        }

        // ---- bar and keys ----

        static void H3pRenderStates()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                object info = H3Get(w, "info");
                object[][] states = new object[][]
                {
                    new object[] { "none", -1.0, -1.0 },
                    new object[] { "in only", 1.0, -1.0 },
                    new object[] { "out only", -1.0, 2.0 },
                    new object[] { "both", 1.0, 2.0 },
                    new object[] { "beyond the end", 9.0, 12.0 },
                    new object[] { "reversed", 3.0, 1.0 },
                };
                foreach (object[] s in states)
                {
                    H3SetField(w, "markIn", s[1]);
                    H3SetField(w, "markOut", s[2]);
                    H3Paint((string)s[0], w);
                }
                H3SetField(w, "markIn", 1.0);
                H3SetField(w, "markOut", 2.0);
                H3SetField(w, "grabbing", true);
                H3SetField(w, "exportText", "Creando el MP4\u2026 50 %");
                H3Paint("grabbing and exporting", w);
                H3SetField(w, "grabbing", false);
                H3SetField(w, "exportText", null);
                H3SetField(info, "Duration", 0.0);
                H3Paint("unknown duration", w);
            }
            finally { w.Dispose(); }
        }

        static void H3Paint(string label, PlayerWindow w)
        {
            using (Bitmap bmp = new Bitmap(Math.Max(1, w.Width), Math.Max(1, w.Height)))
            {
                w.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                int lit = 0;
                for (int y = bmp.Height - 100; y < bmp.Height; y += 4)
                    for (int x = 0; x < bmp.Width; x += 4)
                        if (bmp.GetPixel(x, y).R > 40) lit++;
                Check(lit > 0, label + ": the bar area is black");
            }
        }

        static void H3pEndUnknownDuration()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            PlayerWindow w = H3Window(clip, new Settings());
            try
            {
                H3SetField(w.Media, "Duration", 0.0);
                PwKey(w, Keys.End);
                H3Wait(delegate { return false; }, 2000);
                double p = H3Pos(w);
                Check(p < 100, "End with an unknown duration went to " + H3Fmt(p) + " s");
            }
            finally { w.Dispose(); }
        }

        static void H3pExportUnknownDuration()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", H3X264, 4, false, null);
            Settings st = new Settings();
            st.ExportQuality = 3;
            PlayerWindow w = H3Window(clip, st);
            try
            {
                H3SetField(w.Media, "Duration", 0.0);
                PlayerWindow.Quiet = true;
                H3Call(w, "ExportMp4");
                H3Wait(delegate { return H3Get(w, "exportThread") == null; }, 60000);
                string note = w.ExportNote ?? "";
                Check(note.IndexOf("demasiado largo", StringComparison.Ordinal) < 0, "with the length unknown it says: " + note);
            }
            finally { w.Dispose(); }
        }

        static void H3pRunMissingFfmpeg()
        {
            string bogus = Path.Combine(WorkDir("h3-noff"), "no-existe", "ffmpeg.exe");
            string err;
            bool threw = false;
            string what = "";
            try { VideoExport.Run(bogus, "-version", 5000, out err); }
            catch (Exception e) { threw = true; what = e.GetType().Name + ": " + e.Message; }
            // The export thread (PlayerWindow.ExportMp4) calls Run with no catch: an exception there ends the process.
            Check(!threw, "Run throws when FFmpeg cannot start (" + what + ")");
        }
    }
}

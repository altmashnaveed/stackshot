// Final check of the export and player fixes of round 4 (tester h4-editor, suite "h4editor"): Refit and RunPasses, the
// editor's unedited MP4 at 16 MB, the size-limit texts, a missing FFmpeg, the seek past a long keyframe gap, the resume
// limits, the frame grab on the last frame and the shape of a turned anamorphic clip against FFmpeg's own reading.
// Real FFmpeg runs on small synthetic sources in this suite's folder. Headless: no window is shown, nothing is captured.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static string h4Dir;

        static void H4EditorSuite()
        {
            h4Dir = WorkDir("h4editor");
            Test("h4editor", "refit-pure-rules", H4RefitPure);
            Test("h4editor", "passes-refit-fits-after-overshoot", H4RefitFits);
            Test("h4editor", "passes-refit-stops-after-two-retries", H4RefitBound);
            Test("h4editor", "missing-ffmpeg-never-throws", H4Missing);
            Test("h4editor", "size-limit-texts-and-floor", H4MaxLength);
            Test("h4editor", "player-unknown-and-long-duration-notes", H4PlayerNotes);
            Test("h4editor", "editor-unknown-duration-unedited-mp4-16mb", H4EditorNoDuration);
            Test("h4editor", "ts-gop-36s-learned-seek-control", H4LateGop);
            Test("h4editor", "ts-gop-after-four-minutes-not-learned", H4LateGopBeyond);
            Test("h4editor", "mpeg-ps-gop-10s-seek-control", H4PsGop);
            Test("h4editor", "resume-limits-exact", H4ResumeEdges);
            Test("h4editor", "frame-grab-last-frame-saves", H4GrabLast);
            Test("h4editor", "rotated-anamorphic-shape-vs-ffmpeg", H4Rotations);
        }

        static ExportSpec H4Spec(string src, string output, int w, int h, double seconds, int level)
        {
            ExportSpec s = H3Spec(output, w, h, 30, level, seconds, true);
            s.Source = src;
            return s;
        }

        static string H4Clip4s()
        {
            return HuntOnce(h4Dir, "h4-4s-640.mp4", "-f lavfi -i \"nullsrc=s=640x360:r=30:d=4\" -f lavfi -i \"sine=f=440:d=4\" -map 0:v -map 1:a -vf noise=alls=100:allf=t+u " +
                                                  "-c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -c:a aac -shortest");
        }

        static void H4Size(string file, long bytes)
        {
            using (FileStream fs = File.Create(file)) fs.SetLength(bytes);
        }

        // Refit: a file over its limit lowers Fit by the ratio and 3 %, never below 0.3; a file within its limit changes nothing.
        static void H4RefitPure()
        {
            string dir = WorkDir("h4-refit");
            string file = Path.Combine(dir, "over.mp4");
            ExportSpec s = H4Spec("in.mp4", file, 640, 360, 10, 3);
            H4Size(file, 20000000);
            Check(VideoExport.Refit(s, file), "a 20 MB file at 16 MB was not refitted");
            double want = 15500000.0 / 20000000.0 * 0.97;
            Check(Math.Abs(s.Fit - want) < 0.0001, "Fit " + s.Fit + ", expected " + want);
            H4Size(file, 15500000);
            double kept = s.Fit;
            Check(!VideoExport.Refit(s, file), "a file of exactly the limit was refitted");
            Check(s.Fit == kept, "Fit moved for a file within its limit");
            long bytes = 15500000;
            int kbps = VideoExport.TargetKbps(s);
            Check(kbps == (int)Math.Floor((bytes * 8 * 0.98 / 10 - 128000) * kept / 1000), "TargetKbps " + kbps + " does not follow Fit " + kept);
            H4Size(file, 60000000);
            ExportSpec low = H4Spec("in.mp4", file, 640, 360, 10, 3);
            Check(VideoExport.Refit(low, file) && low.Fit == 0.3, "the refit went under the 0.3 floor: " + low.Fit);
            Check(!VideoExport.Refit(H4Spec("in.mp4", file, 640, 360, 10, 0), file), "maximum quality was refitted");
            ExportSpec gif = H4Spec("in.mp4", file, 640, 360, 10, 3);
            gif.ToGif = true;
            Check(!VideoExport.Refit(gif, file), "a GIF was refitted");
            Check(!VideoExport.Refit(H4Spec("in.mp4", file, 640, 360, 10, 3), Path.Combine(dir, "missing.mp4")), "a missing file was refitted");
        }

        // RunPasses with a second pass that comes out 40 % too big: the real second pass is run again, lower, and the file fits.
        static void H4RefitFits()
        {
            NeedFfmpeg();
            string src = H4Clip4s();
            string dir = WorkDir("h4-fit");
            ExportSpec s = H4Spec(src, Path.Combine(dir, "fit.mp4"), 640, 360, 4, 3);
            string log = Path.Combine(dir, "pass");
            List<string> passes = VideoExport.Passes(s, log);
            Check(passes.Count == 2, "a size level takes " + passes.Count + " passes");
            string plain = VideoExport.Arguments(s, 2, log);
            string over = Regex.Replace(plain, " -b:v \\d+k", " -b:v " + (int)(VideoExport.TargetKbps(s) * 1.4) + "k");
            Check(over != plain, "the overshoot was not built");
            passes[1] = over;
            string err;
            int code = VideoExport.RunPasses(ffmpeg, s, passes, log, 240000, null, out err);
            VideoExport.DeletePassLog(log);
            Check(code == 0 && File.Exists(s.Output), "RunPasses " + code + ": " + Tail(err));
            long size = new FileInfo(s.Output).Length;
            long limit = VideoExport.TargetBytes(3);
            Add("size " + (size / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB, Fit " + s.Fit.ToString("0.000", CultureInfo.InvariantCulture));
            Check(size <= limit, "the refitted file is " + size + " bytes, over " + limit);
            Check(size >= limit * 0.7, "the refitted file is only " + size + " bytes: too much room wasted");
            Check(s.Fit > 0.5 && s.Fit < 0.99, "Fit " + s.Fit + " after a 40 % overshoot");
        }

        // A clip claimed to last one second asks for a bitrate that makes four seconds far too big: two passes and exactly
        // two refits (four FFmpeg runs, each ending with a progress=end line), then it stops.
        static void H4RefitBound()
        {
            NeedFfmpeg();
            string src = H4Clip4s();
            string dir = WorkDir("h4-bound");
            ExportSpec s = H4Spec(src, Path.Combine(dir, "bound.mp4"), 640, 360, 1, 3);
            string log = Path.Combine(dir, "pass");
            List<string> passes = VideoExport.Passes(s, log);
            int runs = 0;
            string err;
            int code = VideoExport.RunPasses(ffmpeg, s, passes, log, 240000, delegate(int pass, int count, string line)
            {
                if (line == "progress=end") runs++;
            }, out err);
            VideoExport.DeletePassLog(log);
            Add("runs " + runs + ", Fit " + s.Fit.ToString("0.000", CultureInfo.InvariantCulture));
            Check(code == 0 && File.Exists(s.Output), "RunPasses " + code + ": " + Tail(err));
            Check(runs == 4, "FFmpeg ran " + runs + " times: two passes and two refits were expected");
            Check(s.Fit >= 0.3 && s.Fit < 1, "Fit " + s.Fit);
            Check(new FileInfo(s.Output).Length > VideoExport.TargetBytes(3), "the bound test needs a file over the limit");
        }

        // No FFmpeg at the path: every runner reports a failure, none throws.
        static void H4Missing()
        {
            string dir = WorkDir("h4-missing");
            string nowhere = Path.Combine(dir, "no-hay", "ffmpeg.exe");
            string file = Path.Combine(dir, "clip.ts");
            File.WriteAllBytes(file, new byte[4096]);
            string err;
            int code = VideoExport.Run(nowhere, "-version", 5000, out err);
            Check(code == -1 && err.StartsWith("No se pudo iniciar FFmpeg", StringComparison.Ordinal), "Run: " + code + " | " + err);
            ExportSpec s = H4Spec(file, Path.Combine(dir, "out.mp4"), 640, 360, 10, 3);
            string log = Path.Combine(dir, "pass");
            code = VideoExport.RunPasses(nowhere, s, VideoExport.Passes(s, log), log, 5000, null, out err);
            Check(code == -1 && s.Fit == 1 && !File.Exists(s.Output), "RunPasses: " + code + ", Fit " + s.Fit);
            Check(!MediaInfo.Probe(nowhere, file).HasVideo, "Probe read a video with no FFmpeg");
            Check(PlayerKit.RunBytes(nowhere, "-version", 4, 5000) == null, "RunBytes returned bytes with no FFmpeg");
            Check(!ShareCheck.Validate(nowhere, file, new ShareExpect()).Passed, "Validate passed with no FFmpeg");
            Check(MediaInfo.ScanDuration(nowhere, file) == 0, "ScanDuration found a length with no FFmpeg");
            PlayerArgs.LearnKeyGap(nowhere, file);
            Check(PlayerArgs.Early(file, 100) == PlayerArgs.Preroll, "a failed key scan changed the early seek: " + PlayerArgs.Early(file, 100));
        }

        // The texts of the size limits: the minutes that fit at the floor bitrate, with and without sound.
        static void H4MaxLength()
        {
            Check(VideoExport.MaxLength(3, true) == "unos 7 min", "16 MB with sound: " + VideoExport.MaxLength(3, true));
            Check(VideoExport.MaxLength(3, false) == "unos 13 min", "16 MB silent: " + VideoExport.MaxLength(3, false));
            Check(VideoExport.MaxLength(4, true) == "unos 11 min", "25 MB with sound: " + VideoExport.MaxLength(4, true));
            Check(VideoExport.MaxLength(4, false) == "unos 20 min", "25 MB silent: " + VideoExport.MaxLength(4, false));
            Check(VideoExport.MaxLength(0, true) == "", "maximum has a limit text");
            ExportSpec s = H4Spec("in.mp4", "out.mp4", 1280, 720, 7 * 60, 3);
            Check(VideoExport.TargetKbps(s) >= VideoExport.TargetMinKbps, "7 min at 16 MB is refused: " + VideoExport.TargetKbps(s));
            s.Seconds = 8 * 60;
            Check(VideoExport.TargetKbps(s) < VideoExport.TargetMinKbps, "8 min at 16 MB is accepted: " + VideoExport.TargetKbps(s));
        }

        // The player's notes: a long clip names the minutes that fit, an unknown length asks for a mark or another quality.
        static void H4PlayerNotes()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-notes");
            string clip = Path.Combine(dir, "clip.mp4");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=2\" -f lavfi -i \"sine=f=440:d=2\" -map 0:v -map 1:a " +
                 "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest " + Q(clip), 120000);
            Settings st = new Settings();
            st.ExportQuality = 3;
            string oldStore = PlayerKit.ResumeFile;
            bool oldQuiet = PlayerWindow.Quiet;
            PlayerKit.ResumeFile = Path.Combine(dir, "posiciones.txt");
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                w.Handle.ToString();
                MediaInfo info = (MediaInfo)PpField(w, "info");
                Check(info.Audio.Count > 0 && info.CanPlay, "the clip does not play");
                info.Duration = 1200;
                PpCall(w, "ExportMp4");
                Check(w.ExportNote != null && w.ExportNote.Contains("caben unos 7 min"), "20 min at 16 MB: " + w.ExportNote);
                st.ExportQuality = 4;
                PpCall(w, "ExportMp4");
                Check(w.ExportNote != null && w.ExportNote.Contains("caben unos 11 min"), "20 min at 25 MB: " + w.ExportNote);
                info.Duration = 0;
                PpCall(w, "ExportMp4");
                Check(w.ExportNote != null && w.ExportNote.StartsWith("No se sabe cu", StringComparison.Ordinal), "unknown length at 25 MB: " + w.ExportNote);
                Check(Directory.GetFiles(dir, "*.mp4").Length == 1, "an export left a file");
            }
            finally
            {
                w.Dispose();
                PlayerKit.ResumeFile = oldStore;
                PlayerWindow.Quiet = oldQuiet;
            }
        }

        // The editor with no timeline (no duration known): an unedited MP4 at 16 MB must be exported or refused, not closed.
        static void H4EditorNoDuration()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-editor-dur");
            string copy = Path.Combine(dir, "clip.mp4");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=2\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(copy), 120000);
            BindingFlags np = BindingFlags.NonPublic | BindingFlags.Instance;
            ConstructorInfo ctor = typeof(Editor).GetConstructor(np, null,
                new Type[] { typeof(ShotStack), typeof(string), typeof(Bitmap), typeof(string), typeof(double) }, null);
            Check(ctor != null, "no editor constructor for a video was found");
            Settings st = new Settings();
            st.ExportQuality = 3;
            Bitmap img = White(320, 180);
            Editor ed = (Editor)ctor.Invoke(new object[] { null, copy, img, ffmpeg, 0.0 });
            try
            {
                typeof(Editor).GetField("bgs", np).SetValue(ed, st);
                ((Canvas)typeof(Editor).GetField("canvas", np).GetValue(ed)).BgOn = false;
                Check(typeof(Editor).GetField("timeline", np).GetValue(ed) == null, "the editor has a timeline with no duration");
                Editor.Quiet = true;
                try { typeof(Editor).GetMethod("ExportVideo", np).Invoke(ed, null); }
                catch (TargetInvocationException tie) { throw new Exception("ExportVideo threw " + tie.InnerException, tie.InnerException); }
                bool closed = (bool)typeof(Editor).GetField("closeWithoutAsking", np).GetValue(ed);
                object started = typeof(Editor).GetField("export", np).GetValue(ed);
                Add("closed " + closed + ", export started " + (started != null) + ", note: " + ed.ExportNote);
                Check(!closed, "the editor closed with nothing exported: an unedited MP4 at 16 MB and no known length");
                Check(ed.ExportNote != null && ed.ExportNote.StartsWith("No se sabe cu\u00E1nto dura", StringComparison.Ordinal), "no message saying the length is unknown: " + ed.ExportNote);
            }
            finally
            {
                Editor.Quiet = false;
                typeof(Editor).GetMethod("CancelExport", np).Invoke(ed, null);
                ed.Dispose();
                img.Dispose();
            }
        }

        static byte[] H4Accurate(string src, double t)
        {
            return PlayerKit.RunBytes(ffmpeg, "-hide_banner -loglevel error -nostdin -i " + Q(src) + " -ss " + t.ToString("0.000", CultureInfo.InvariantCulture) +
                                              " -map 0:v:0 -an -sn -dn -frames:v 1 -f rawvideo -pix_fmt bgra -", 160 * 90 * 4, 60000);
        }

        static bool H4Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // A TS whose keyframes are 5 s apart in the first two minutes and 36 s apart after them. The gap is learned from those
        // first two minutes only (PlayerArgs.LearnKeyGap, -t 120), so a seek after them starts too late for its decoder.
        static void H4LateGop()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-gop");
            string src = Path.Combine(dir, "late-gop.ts");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=25:d=180\" -c:v libx264 -preset ultrafast -crf 35 " +
                 "-g 900 -sc_threshold 0 -force_key_frames \"expr:if(lt(t,120),gte(t,n_forced*5),0)\" -an -f mpegts " + Q(src), 180000);
            HuntInfo(src);
            double early = PlayerArgs.Early(src, 140);
            Add("early seek at 140 s: " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(early >= 36, "the 36 s GOP is not learned: early seek " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            byte[] control = PlayerKit.RunBytes(ffmpeg, PlayerArgs.Still(src, 60, 0, 0, 160, 90, 0), 160 * 90 * 4, 30000);
            Check(H4Same(control, H4Accurate(src, 60)), "control at 60 s (inside the learned part) does not match the accurate frame");
            byte[] still = PlayerKit.RunBytes(ffmpeg, PlayerArgs.Still(src, 140, 0, 0, 160, 90, 0), 160 * 90 * 4, 30000);
            if (H4Same(still, H4Accurate(src, 140))) return;
            string what = still == null ? "no picture" : H4Same(still, H4Accurate(src, 151)) ? "the frame at 151 s, the next keyframe" : "another frame";
            Check(false, "the still at 140 s is " + what + " (learned gap " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
        }

        // Keyframes 5 s apart before 240 s and 36 s apart after it. LearnKeyGap reads only up to the first keyframe past two
        // minutes, so the 36 s gap after four minutes is not learned and a seek into it should land on a wrong frame.
        static void H4LateGopBeyond()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-gop4");
            string src = Path.Combine(dir, "gop-after-4min.ts");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=25:d=330\" -c:v libx264 -preset ultrafast -crf 35 " +
                 "-g 900 -sc_threshold 0 -force_key_frames \"expr:if(lt(t,240),gte(t,n_forced*5),0)\" -an -f mpegts " + Q(src), 180000);
            HuntInfo(src);
            double early = PlayerArgs.Early(src, 280);
            Add("early seek at 280 s: " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(early < 10, "premise: the learned gap is " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s, expected the 5 s of the first two minutes");
            byte[] still = PlayerKit.RunBytes(ffmpeg, PlayerArgs.Still(src, 280, 0, 0, 160, 90, 0), 160 * 90 * 4, 30000);
            if (H4Same(still, H4Accurate(src, 280))) return;
            string what = still == null ? "no picture" : H4Same(still, H4Accurate(src, 307)) ? "the frame at 307 s, the next keyframe after the 36 s gap" : "another frame";
            // A documented limit (docs/cine-share-export.md): the gap is measured on the first two minutes, as encoders keep
            // one GOP for the whole file. Measured here, not failed.
            Add("known limit: the still at 280 s is " + what + " (learned gap " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
        }

        // Control for MPEG-PS: a 10 s GOP inside the first two minutes is learned, and seeks land on the right frame.
        static void H4PsGop()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-ps");
            string src = Path.Combine(dir, "gop10.mpg");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=25:d=30\" -c:v mpeg2video -g 250 -b:v 500k -an -f mpeg " + Q(src), 120000);
            HuntInfo(src);
            double early = PlayerArgs.Early(src, 25);
            Add("early seek at 25 s: " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(early >= 10, "the gap learned for a 10 s GOP is " + early.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            foreach (double t in new double[] { 14.3, 25, 27.7 })
            {
                byte[] still = PlayerKit.RunBytes(ffmpeg, PlayerArgs.Still(src, t, 0, 0, 160, 90, 0), 160 * 90 * 4, 30000);
                Check(H4Same(still, H4Accurate(src, t)), "the still at " + t.ToString("0.0", CultureInfo.InvariantCulture) + " s of the MPEG-PS does not match the accurate frame");
            }
        }

        // Resume: a glance under 10 s keeps the spot, the limits (10 s, 15 s from the end, 120 s long) are exact, and the
        // case of the path does not matter.
        static void H4ResumeEdges()
        {
            string dir = WorkDir("h4-resume");
            string video = Path.Combine(dir, "Pelicula.MKV");
            File.WriteAllBytes(video, new byte[1000]);
            string upper = Path.Combine(dir, "PELICULA.mkv");
            string other = Path.Combine(dir, "otra.mkv");
            File.WriteAllBytes(other, new byte[1000]);
            PpWithStore(Path.Combine(dir, "posiciones.txt"), delegate
            {
                PlayerKit.ResumeSave(video, 300, 600);
                PlayerKit.ResumeSave(video, 9.99, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 300, "a glance under 10 s moved the spot: " + PlayerKit.ResumeAt(video, 600));
                Check(Math.Abs(PlayerKit.ResumeAt(upper, 600) - 300) < 0.001, "the path in other case does not find the spot");
                PlayerKit.ResumeSave(upper, 10, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 10, "10 s is not kept: " + PlayerKit.ResumeAt(video, 600));
                PlayerKit.ResumeSave(video, 585, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 585, "585 of 600 (15 s from the end) is not kept: " + PlayerKit.ResumeAt(video, 600));
                PlayerKit.ResumeSave(video, 585.5, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 0, "585.5 of 600 is still remembered");
                PlayerKit.ResumeSave(other, 50, 119.9);
                Check(PlayerKit.ResumeAt(other, 600) == 0, "a 119.9 s video was remembered");
                PlayerKit.ResumeSave(other, 50, 120);
                Check(PlayerKit.ResumeAt(other, 120) == 50, "a 120 s video at 50 s is not kept: " + PlayerKit.ResumeAt(other, 120));
                PlayerKit.ResumeSave(other, 105, 120);
                Check(PlayerKit.ResumeAt(other, 120) == 105, "105 of 120 is not kept");
                PlayerKit.ResumeSave(other, 105.5, 120);
                Check(PlayerKit.ResumeAt(other, 120) == 0, "105.5 of 120 is still remembered");
            });
        }

        // The frame grab on the last frame of a clip: a picture is saved next to the video, at the video's size.
        static void H4GrabLast()
        {
            NeedFfmpeg();
            string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 2, false, null);
            if (clip == null) Skip("this FFmpeg cannot write mp4");
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            string oldStore = PlayerKit.ResumeFile;
            PlayerKit.ResumeFile = Path.Combine(dir, "posiciones.txt");
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                w.Handle.ToString();
                Check(w.Media != null && w.Media.CanPlay, "the window cannot play the clip");
                H3Play(w, 0);
                H3SeekTo(w, 1.99);
                H3Call(w, "GrabFrame");
                bool saved = H3Wait(delegate { return Directory.GetFiles(dir, "*fotograma*.png").Length > 0; }, 15000);
                Check(saved, "no picture was saved at the last frame");
                string[] pics = Directory.GetFiles(dir, "*fotograma*.png");
                using (Bitmap b = new Bitmap(pics[0]))
                {
                    Check(b.Width == 320 && b.Height == 180, "the picture is " + b.Width + "x" + b.Height);
                }
            }
            finally
            {
                w.Dispose();
                PlayerKit.ResumeFile = oldStore;
            }
        }

        // A turned clip: the shape the player shows (MediaInfo.Display) against the aspect FFmpeg reads from its own frame
        // (showinfo: size and sar after the automatic rotation).
        static void H4Rotations()
        {
            NeedFfmpeg();
            string dir = WorkDir("h4-rot");
            // stored width, stored height, stored sar, display rotation, seen width, seen height
            string[][] cases = {
                new string[] { "1080", "1440", "3/4", "90", "1920", "1080" },
                new string[] { "1440", "1080", "4/3", "270", "1080", "1920" },
                new string[] { "1440", "1080", "4/3", "180", "1920", "1080" } };
            for (int i = 0; i < cases.Length; i++)
            {
                string[] c = cases[i];
                string plain = Path.Combine(dir, "plain" + i + ".mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=" + c[0] + "x" + c[1] + ":r=30:d=1\" -vf setsar=" + c[2] +
                     " -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(plain), 120000);
                string rot = Path.Combine(dir, "rot" + i + ".mp4");
                FfOk("-hide_banner -loglevel error -y -display_rotation:v:0 " + c[3] + " -i " + Q(plain) + " -c copy " + Q(rot), 120000);
                Size seen = MediaInfo.Probe(ffmpeg, rot).Display;
                Check(seen.Width == int.Parse(c[4], CultureInfo.InvariantCulture) && seen.Height == int.Parse(c[5], CultureInfo.InvariantCulture),
                      "case " + i + " is seen at " + seen.Width + "x" + seen.Height + ", expected " + c[4] + "x" + c[5]);
                string err;
                Ff("-hide_banner -i " + Q(rot) + " -frames:v 1 -vf showinfo -f null -", out err, 30000);
                Match sz = Regex.Match(err, @"s:(\d+)x(\d+)");
                Match sar = Regex.Match(err, @"sar:(\d+)/(\d+)");
                Check(sz.Success && sar.Success, "case " + i + ": showinfo gave no size or sar: " + Tail(err));
                double ffAspect = (double)int.Parse(sz.Groups[1].Value, CultureInfo.InvariantCulture) * int.Parse(sar.Groups[1].Value, CultureInfo.InvariantCulture) /
                                  int.Parse(sar.Groups[2].Value, CultureInfo.InvariantCulture) / int.Parse(sz.Groups[2].Value, CultureInfo.InvariantCulture);
                double ourAspect = (double)seen.Width / seen.Height;
                Add("case " + i + ": FFmpeg " + sz.Value + " " + sar.Value + ", shown " + seen.Width + "x" + seen.Height);
                Check(Math.Abs(ffAspect - ourAspect) / ourAspect < 0.01,
                      "case " + i + ": FFmpeg shows aspect " + ffAspect.ToString("0.000", CultureInfo.InvariantCulture) + ", MediaInfo " + ourAspect.ToString("0.000", CultureInfo.InvariantCulture));
            }
        }
    }
}

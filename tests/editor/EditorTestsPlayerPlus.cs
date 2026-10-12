// What the player gained in October 2026 (suite "playerplus"): it remembers where a long video was left, exports only
// the piece between two marks (an .mp4 too), fits an export into 16 or 25 MB with two passes, and saves the frame on
// screen as a picture. Headless: no window is shown and the screen is never captured.
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
        static void PlayerPlusSuite()
        {
            Test("playerplus", "resume-store-rules", PpResumeRules);
            Test("playerplus", "resume-store-limits-and-bad-lines", PpResumeLimits);
            Test("playerplus", "resume-never-writes-the-real-file-read-only", PpResumeReadOnly);
            Test("playerplus", "window-starts-where-it-was-left", PpResumeWindow);
            Test("playerplus", "target-size-passes-and-bitrate", PpTargetArgs);
            Test("playerplus", "export-rules-mp4-range-and-size", PpExportRules);
            Test("playerplus", "range-export-from-an-mp4", PpRangeMp4);
            Test("playerplus", "size-target-two-pass-fits-16-mb", PpFits16);
            Test("playerplus", "too-long-for-16-mb-is-refused", PpTooLong);
            Test("playerplus", "frame-grab-real-size-and-pixels", PpGrab);
            Test("playerplus", "frame-grab-anamorphic-square-pixels", PpGrabSar);
        }

        static object PpField(PlayerWindow w, string name)
        {
            return typeof(PlayerWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w);
        }

        static void PpSet(PlayerWindow w, string name, object v)
        {
            typeof(PlayerWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(w, v);
        }

        static object PpCall(PlayerWindow w, string name)
        {
            return typeof(PlayerWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
        }

        static bool PpCanExport(PlayerWindow w)
        {
            return (bool)typeof(PlayerWindow).GetProperty("CanExport", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w, null);
        }

        // Runs fn with the resume store pointed at a private file, and puts it back.
        static void PpWithStore(string file, Action fn)
        {
            string old = PlayerKit.ResumeFile;
            PlayerKit.ResumeFile = file;
            try { fn(); }
            finally { PlayerKit.ResumeFile = old; }
        }

        static void PpResumeRules()
        {
            string dir = WorkDir("pp-resume");
            string video = Path.Combine(dir, "pel\u00EDcula larga \u00F1.mkv");
            File.WriteAllBytes(video, new byte[1000]);
            PpWithStore(Path.Combine(dir, "posiciones.txt"), delegate
            {
                PlayerKit.ResumeSave(video, 300, 100);
                Check(PlayerKit.ResumeAt(video, 100) == 0, "a 100 s video resumes");
                PlayerKit.ResumeSave(video, 5, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 0, "barely started (5 s) is remembered");
                PlayerKit.ResumeSave(video, 300.25, 600);
                Check(Math.Abs(PlayerKit.ResumeAt(video, 600) - 300.25) < 0.001, "300.25 s read back as " + PlayerKit.ResumeAt(video, 600));
                // Near the end: forgotten.
                PlayerKit.ResumeSave(video, 590, 600);
                Check(PlayerKit.ResumeAt(video, 600) == 0, "watched to the end is still remembered");
                PlayerKit.ResumeSave(video, 200, 600);
                // The file changes (re-recorded, edited): its old position does not apply.
                File.WriteAllBytes(video, new byte[2000]);
                Check(PlayerKit.ResumeAt(video, 600) == 0, "a changed file resumes at the old spot");
                PlayerKit.ResumeSave(video, 100, 600);
                int lines = 0;
                foreach (string l in File.ReadAllLines(PlayerKit.ResumeFile)) if (l.EndsWith(video, StringComparison.OrdinalIgnoreCase)) lines++;
                Check(lines == 1, "the same path has " + lines + " lines");
                Check(Math.Abs(PlayerKit.ResumeAt(video, 600) - 100) < 0.001, "after the change: " + PlayerKit.ResumeAt(video, 600));
                // A shorter duration than the spot (the file was cut): not past the end.
                Check(PlayerKit.ResumeAt(video, 110) == 0, "resumes past the end of a shorter video");
                // Missing file: nothing, no error.
                Check(PlayerKit.ResumeAt(Path.Combine(dir, "no existe.mkv"), 600) == 0, "a missing file resumes");
                PlayerKit.ResumeSave(Path.Combine(dir, "no existe.mkv"), 100, 600);
            });
        }

        static void PpResumeLimits()
        {
            string dir = WorkDir("pp-resume-limits");
            string store = Path.Combine(dir, "posiciones.txt");
            PpWithStore(store, delegate
            {
                // Garbage first: ignored, then overwritten by a clean file.
                File.WriteAllText(store, "basura\nsin\ttabuladores\tsuficientes\n\t\t\t\t\n12\tx\ty\tz\tC:\\no\\existe.mkv\n");
                string v0 = Path.Combine(dir, "v0.mkv");
                File.WriteAllBytes(v0, new byte[10]);
                PlayerKit.ResumeSave(v0, 50, 600);
                Check(Math.Abs(PlayerKit.ResumeAt(v0, 600) - 50) < 0.001, "bad lines broke the store");
                // 250 videos: the 200 most recent stay.
                List<string> vids = new List<string>();
                for (int i = 0; i < 250; i++)
                {
                    string v = Path.Combine(dir, "v" + (i + 1) + ".mkv");
                    File.WriteAllBytes(v, new byte[10 + i]);
                    vids.Add(v);
                    PlayerKit.ResumeSave(v, 60 + i, 600);
                    Thread.Sleep(0);
                }
                int count = File.ReadAllLines(store).Length;
                Check(count <= 200, "the store holds " + count + " lines");
                Check(Math.Abs(PlayerKit.ResumeAt(vids[249], 600) - 309) < 0.001, "the newest was dropped");
                Check(PlayerKit.ResumeAt(vids[0], 600) == 0, "the oldest is still there");
                Add(count + " lines kept");
            });
        }

        static void PpResumeReadOnly()
        {
            Check(Settings.ReadOnly, "the tests should run read-only");
            string real = Path.Combine(Settings.DataDir, "posiciones.txt");
            Check(PlayerKit.ResumeFile == real, "the store is not the default one: " + PlayerKit.ResumeFile);
            bool existed = File.Exists(real);
            DateTime before = existed ? File.GetLastWriteTimeUtc(real) : DateTime.MinValue;
            string dir = WorkDir("pp-resume-ro");
            string v = Path.Combine(dir, "v.mkv");
            File.WriteAllBytes(v, new byte[10]);
            PlayerKit.ResumeSave(v, 100, 600);
            Check(File.Exists(real) == existed, "the real store was created");
            if (existed) Check(File.GetLastWriteTimeUtc(real) == before, "the real store was written");
        }

        static void PpResumeWindow()
        {
            NeedFfmpeg();
            string dir = WorkDir("pp-resume-win");
            string clip = Path.Combine(dir, "larga.mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=5:d=130\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(clip), 120000);
            PpWithStore(Path.Combine(dir, "posiciones.txt"), delegate
            {
                MediaInfo mi = MediaInfo.Probe(ffmpeg, clip);
                PlayerKit.ResumeSave(clip, 61, mi.Duration);
                PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    w.BeginPlay();
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 3000 && (w.Engine == null || w.Engine.Position < 60)) { Application.DoEvents(); Thread.Sleep(10); }
                    Check(w.Engine != null && w.Engine.Position >= 60 && w.Engine.Position < 66, "started at " + (w.Engine != null ? w.Engine.Position : -1) + " s, it was left at 61 s");
                    string flash = (string)PpField(w, "flash");
                    Check(flash != null && flash.StartsWith("Sigue donde lo dejaste (1:01)", StringComparison.Ordinal), "the notice said: " + flash);
                }
                finally { w.Dispose(); }
            });
        }

        static ExportSpec PpSpec(int level, double seconds, bool audio)
        {
            ExportSpec s = new ExportSpec();
            s.Source = "C:\\v\\in.mkv";
            s.Output = "C:\\v\\out.mp4";
            s.Crop = new Rectangle(0, 0, 1280, 720);
            s.Frame = new Size(1280, 720);
            s.Inner = new Rectangle(0, 0, 1280, 720);
            s.HasAudio = audio;
            s.Level = level;
            s.Fps = 30;
            s.Seconds = seconds;
            return s;
        }

        static void PpTargetArgs()
        {
            Check(VideoExport.TargetBytes(0) == 0 && VideoExport.TargetBytes(1) == 0 && VideoExport.TargetBytes(2) == 0, "quality levels with a size");
            Check(VideoExport.TargetBytes(3) <= 16000000 && VideoExport.TargetBytes(4) <= 25000000, "targets " + VideoExport.TargetBytes(3) + " / " + VideoExport.TargetBytes(4));
            List<string> one = VideoExport.Passes(PpSpec(1, 60, true), "C:\\t\\log");
            Check(one.Count == 1 && !one[0].Contains("-pass"), "Alta made " + one.Count + " passes");
            List<string> two = VideoExport.Passes(PpSpec(3, 60, true), "C:\\t\\log");
            Check(two.Count == 2, "16 MB made " + two.Count + " passes");
            Check(two[0].Contains("-pass 1") && two[0].Contains("-an") && two[0].Contains("-f null NUL") && !two[0].Contains("out.mp4"), "pass 1: " + two[0]);
            Check(two[1].Contains("-pass 2") && two[1].Contains("out.mp4") && two[1].Contains("-b:v ") && two[1].Contains("-maxrate ") && two[1].Contains("-c:a aac"), "pass 2: " + two[1]);
            Check(two[0].Contains("-passlogfile \"C:\\t\\log\"") && two[1].Contains("-passlogfile \"C:\\t\\log\""), "the log is not shared: " + two[0]);
            int kbps = VideoExport.TargetKbps(PpSpec(3, 60, true));
            int expect = (int)((15500000.0 * 8 * 0.98 / 60 - 128000) / 1000);
            Check(Math.Abs(kbps - expect) <= 2, "60 s in 16 MB: " + kbps + " kb/s, expected about " + expect);
            Check(VideoExport.TargetKbps(PpSpec(3, 60, false)) > kbps + 100, "no sound leaves no more room for the picture");
            Check(two[1].Contains("-b:v " + kbps + "k"), "the bitrate in pass 2 is not " + kbps + "k: " + two[1]);
            // A 1 s clip could afford 121 Mb/s: it gets the 720p30 level's ceiling (4.1, 62.5 Mb/s) instead.
            string shortClip = VideoExport.Passes(PpSpec(3, 1, true), "C:\\t\\log")[1];
            Check(shortClip.Contains("-b:v 62500k") && shortClip.Contains("-maxrate 62500k"), "1 s in 16 MB: " + shortClip);
            // Long enough to fall under the floor.
            Check(VideoExport.TargetKbps(PpSpec(3, 1200, true)) < VideoExport.TargetMinKbps, "20 min in 16 MB stays above the floor");
            Check(VideoExport.TargetKbps(PpSpec(4, 600, true)) >= VideoExport.TargetMinKbps, "10 min in 25 MB falls under the floor");
            Add("60 s in 16 MB: " + kbps + " kb/s");
        }

        static void PpExportRules()
        {
            NeedFfmpeg();
            string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 2, false, null);
            Settings st = new Settings();
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                st.ExportQuality = 0;
                Check(!PpCanExport(w), "a whole .mp4 at Maxima can be exported (it would only copy it)");
                st.ExportQuality = 3;
                Check(PpCanExport(w), "a whole .mp4 cannot be fitted into 16 MB");
                st.ExportQuality = 0;
                PpSet(w, "markIn", 0.5);
                PpSet(w, "markOut", 1.5);
                Check(PpCanExport(w), "a piece of an .mp4 cannot be exported");
                // Esc clears the piece.
                PwKey(w, Keys.Escape);
                Check((double)PpField(w, "markIn") < 0 && (double)PpField(w, "markOut") < 0, "Esc left the marks");
                Check(!PpCanExport(w), "after clearing, the whole .mp4 can be exported again");
                // E then S at the start: the end must be after the start.
                PwKey(w, Keys.E);
                PwKey(w, Keys.S);
                Check((double)PpField(w, "markOut") < 0, "a piece of one frame at 0 was accepted");
                string flash = (string)PpField(w, "flash");
                Check(flash != null && flash.StartsWith("El final tiene que ir despu", StringComparison.Ordinal), "flash: " + flash);
            }
            finally { w.Dispose(); }
        }

        static void PpRangeMp4()
        {
            NeedFfmpeg();
            string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 3, false, null);
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                PpSet(w, "markIn", 1.0);
                PpSet(w, "markOut", 2.0);
                H2Export(w);
                string[] outs = Directory.GetFiles(dir, "* (tramo).mp4");
                Check(outs.Length == 1, "the piece left " + outs.Length + " files | note " + w.ExportNote);
                Check(w.ExportNote == "ok", "the check said: " + w.ExportNote);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, outs[0]);
                Check(Math.Abs(mi.Duration - 1.0) < 0.1, "the piece lasts " + mi.Duration + " s, marked 1 s");
                Check(mi.Audio.Count == 1, "the piece has " + mi.Audio.Count + " sound tracks");
                // The original .mp4 is untouched and nothing else was left.
                Check(Directory.GetFiles(dir, "*.mp4").Length == 2, "files: " + string.Join(", ", Directory.GetFiles(dir)));
                Add("piece " + mi.Duration.ToString("0.000", CultureInfo.InvariantCulture) + " s");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void PpFits16()
        {
            NeedFfmpeg();
            string dir = WorkDir("pp-16mb");
            string src = Path.Combine(dir, "ruido.mkv");
            // Noise is the hardest picture to compress: at the usual quality it would be far over 16 MB.
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=1280x720:r=30:d=8\" -f lavfi -i \"sine=f=440:d=8\" " +
                 "-vf noise=alls=60:allf=t -c:v libx264 -preset ultrafast -qp 0 -pix_fmt yuv420p -c:a pcm_s16le -shortest " + Q(src), 240000);
            Check(new FileInfo(src).Length > 40000000, "the noisy source is only " + new FileInfo(src).Length + " bytes");
            string temp = Path.GetTempPath();
            Settings st = new Settings();
            st.ExportQuality = 3;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(src, new PlFakeSound(), st);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                IntPtr h = w.Handle;
                H2Export(w);
                string[] outs = H2Outputs(dir);
                Check(outs.Length == 1, "export left " + H2Names(outs) + " | note " + w.ExportNote);
                long size = new FileInfo(outs[0]).Length;
                Check(size <= VideoExport.TargetBytes(3), "the MP4 is " + size + " bytes, over the 16 MB target " + VideoExport.TargetBytes(3));
                Check(size >= VideoExport.TargetBytes(3) * 0.6, "the MP4 is only " + size + " bytes: the room was wasted");
                Check(w.ExportNote == "ok", "the check said: " + w.ExportNote);
                Check(Directory.GetFiles(temp, "stackshot-2pass-*").Length == 0, "two-pass logs left in TEMP: " + string.Join(", ", Directory.GetFiles(temp, "stackshot-2pass-*")));
                Add((size / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture) + " MiB in " + sw.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void PpTooLong()
        {
            NeedFfmpeg();
            string dir = WorkDir("pp-too-long");
            string src = Path.Combine(dir, "larguisimo.mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=2:d=480\" -f lavfi -i \"anullsrc=r=48000:cl=stereo\" " +
                 "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest " + Q(src), 180000);
            Settings st = new Settings();
            st.ExportQuality = 3;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(src, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                PpCall(w, "ExportMp4");
                Check(PpField(w, "exportThread") == null, "an export started");
                Check(w.ExportNote != null && w.ExportNote.StartsWith("Es demasiado largo para caber en 16 MB", StringComparison.Ordinal), "note: " + w.ExportNote);
                Check(H2Outputs(dir).Length == 0, "a file was left");
                // With a 60 s piece marked, it fits.
                PpSet(w, "markIn", 10.0);
                PpSet(w, "markOut", 70.0);
                H2Export(w);
                string[] outs = Directory.GetFiles(dir, "* (tramo).mp4");
                Check(outs.Length == 1 && new FileInfo(outs[0]).Length <= VideoExport.TargetBytes(3), "the piece: " + outs.Length + " files | " + w.ExportNote);
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // The frame on screen saved next to the video (no stack in the tests): its size and its pixels.
        static void PpGrabWait(PlayerWindow w)
        {
            PpCall(w, "GrabFrame");
            Stopwatch sw = Stopwatch.StartNew();
            while ((bool)PpField(w, "grabbing") && sw.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(10); }
            Check(!(bool)PpField(w, "grabbing"), "the grab did not end in 30 s");
        }

        static void PpGrab()
        {
            NeedFfmpeg();
            string clip = PwClip(".mkv", "-c:v libx264 -preset ultrafast -pix_fmt yuv444p -crf 0 -c:a aac", 2, false, null);
            string dir = Path.GetDirectoryName(clip);
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
            try
            {
                IntPtr h = w.Handle;
                PpGrabWait(w);
                string[] pngs = Directory.GetFiles(dir, "clip fotograma *.png");
                Check(pngs.Length == 1, "the grab left " + pngs.Length + " pictures | " + PpField(w, "flash"));
                using (Bitmap b = new Bitmap(pngs[0]))
                {
                    Check(b.Width == 320 && b.Height == 180, "the picture is " + b.Width + "x" + b.Height);
                    // Against FFmpeg's own decode of the first frame.
                    string refPng = Path.Combine(dir, "ref.png");
                    FfOk("-hide_banner -loglevel error -y -i " + Q(clip) + " -frames:v 1 " + Q(refPng), 60000);
                    using (Bitmap r = new Bitmap(refPng))
                    {
                        double sum = 0;
                        for (int y = 0; y < 180; y += 3)
                            for (int x = 0; x < 320; x += 3)
                            {
                                Color a = b.GetPixel(x, y), c = r.GetPixel(x, y);
                                sum += Math.Abs(a.R - c.R) + Math.Abs(a.G - c.G) + Math.Abs(a.B - c.B);
                            }
                        double mean = sum / (60 * 107 * 3);
                        Check(mean < 4, "the picture differs from the frame by " + mean.ToString("0.0", CultureInfo.InvariantCulture) + " per channel");
                        Add("mean difference " + mean.ToString("0.00", CultureInfo.InvariantCulture));
                    }
                }
                // A second grab of the same frame gets its own name.
                PpGrabWait(w);
                Check(Directory.GetFiles(dir, "clip fotograma *.png").Length == 2, "the second grab replaced the first");
            }
            finally { w.Dispose(); }
        }

        static void PpGrabSar()
        {
            NeedFfmpeg();
            string dir = WorkDir("pp-grab-sar");
            string clip = Path.Combine(dir, "hdv.mov");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=1440x1080:r=30:d=1\" -vf setsar=4/3 -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(clip), 120000);
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
            try
            {
                IntPtr h = w.Handle;
                PpGrabWait(w);
                string[] pngs = Directory.GetFiles(dir, "hdv fotograma *.png");
                Check(pngs.Length == 1, "the grab left " + pngs.Length + " pictures");
                using (Bitmap b = new Bitmap(pngs[0]))
                    Check(b.Width == 1920 && b.Height == 1080, "an anamorphic 1440x1080 (SAR 4:3) frame saved as " + b.Width + "x" + b.Height + ", it shows at 1920x1080");
            }
            finally { w.Dispose(); }
        }
    }
}

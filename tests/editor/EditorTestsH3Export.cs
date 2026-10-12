// Export at the 16 MB and 25 MB levels (two passes), in the player and in the editor (suite "h3export"): sizes and bitrates
// against the limits, the x264 level caps, cancels and pass logs, TEMP and output paths with accents, GIF, RGB and VFR
// sources, the editor's cuts and joined clips, and what the editor does with an unedited MP4 at a size level. Real FFmpeg
// runs on small synthetic sources; the sizes are in the detail of each test. Headless: no window is shown.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static string h3Dir;

        static void H3ExportSuite()
        {
            h3Dir = WorkDir("h3export");
            Test("h3export", "size-30s-640-16mb-two-pass", H3Size30);
            Test("h3export", "size-10s-720p-25mb-level-4", H3Size25);
            Test("h3export", "short-0.3s-and-1s-16mb", H3Short);
            Test("h3export", "floor-430s-fits-and-440s-refused", H3Floor);
            Test("h3export", "mono-and-silent-sources-16mb", H3MonoSilent);
            Test("h3export", "maxrate-never-above-level-args", H3LevelArgs);
            Test("h3export", "level-4k60-and-8k-encoded", H3LevelEncode);
            Test("h3export", "vfr-phone-clip-16mb", H3Vfr);
            Test("h3export", "gif-source-16mb", H3Gif);
            Test("h3export", "cine-rgb-gbrp-16mb", H3CineRgb);
            Test("h3export", "cancel-during-pass-1", H3CancelPass1);
            Test("h3export", "cancel-during-pass-2", H3CancelPass2);
            Test("h3export", "two-exports-at-once-own-logs", H3TwoAtOnce);
            Test("h3export", "temp-with-accents-and-spaces", H3TempAccents);
            Test("h3export", "output-folder-with-accents", H3OutAccents);
            Test("h3export", "check-rejects-over-limit", H3RejectOver);
            Test("h3export", "editor-and-player-same-args", H3SameArgs);
            Test("h3export", "editor-cuts-speed-two-clips-16mb", H3EditorJoined);
            Test("h3export", "editor-cuts-in-ts-16mb", H3EditorTs);
            Test("h3export", "cut-frames-ts-and-mp4-level-0-and-16mb", H3TsFrames);
            Test("h3export", "cut-ts-default-gop-vs-gop-30", H3TsGop);
            Test("h3export", "static-motion-noise-sizes", H3Content);
            Test("h3export", "editor-unedited-mp4-at-16mb-exports", H3EditorSilent);
        }

        // ---- sources, made once in this suite's folder ----

        static string H3Src(string name, string args)
        {
            return HuntOnce(h3Dir, name, args);
        }

        static string H3AvSrc(string name, string size, string rate, string secs)
        {
            return H3Src(name, "-f lavfi -i \"testsrc2=s=" + size + ":r=" + rate + ":d=" + secs + "\" -f lavfi -i \"sine=f=440:d=" + secs +
                               "\" -map 0:v -map 1:a -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -c:a aac -shortest");
        }

        static string H3Src30()
        {
            return H3AvSrc("h3-30s-640.mp4", "640x360", "30", "30");
        }

        static string H3Src10()
        {
            return H3AvSrc("h3-10s-640.mp4", "640x360", "30", "10");
        }

        static string H3TsSrc()
        {
            return H3Src("h3-10s.ts", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=10\" -f lavfi -i \"sine=f=440:d=10\" -map 0:v -map 1:a " +
                                      "-c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -c:a aac -shortest -f mpegts");
        }

        // ---- helpers ----

        // The player's whole-video spec (PlayerWindow.ExportMp4 with no piece), at a quality level.
        static ExportSpec H3PlayerSpec(MediaInfo info, string src, string output, int level)
        {
            Rectangle crop = new Rectangle(0, 0, Math.Max(2, info.Width & ~1), Math.Max(2, info.Height & ~1));
            ExportSpec s = new ExportSpec();
            s.Source = src;
            s.Output = output;
            s.Crop = crop;
            s.Frame = crop.Size;
            s.Inner = new Rectangle(Point.Empty, crop.Size);
            s.SourceIsGif = string.Equals(Path.GetExtension(src), ".gif", StringComparison.OrdinalIgnoreCase);
            s.HasAudio = info.Audio.Count > 0;
            s.RgbSource = VideoExport.IsRgb(src, info.Raw);
            s.Level = level;
            s.Fps = info.Fps;
            int track = -1;
            foreach (AudioTrackInfo a in info.Audio) if (a.Default) { track = a.Index; break; }
            s.AudioTrack = track >= 0 && track < info.Audio.Count ? track : 0;
            s.Seconds = info.Duration;
            return s;
        }

        // The editor's spec of a video with no edits (Editor.ExportVideo): canvas crop, whole length, same options.
        static ExportSpec H3EditorSpec(string src, string output, int level)
        {
            MediaInfo info = HuntInfo(src);
            VideoClip clip = HuntClip(src);
            Rectangle crop = new Rectangle(0, 0, info.Width & ~1, info.Height & ~1);
            ExportSpec s = new ExportSpec();
            s.Source = src;
            s.Output = output;
            s.Crop = crop;
            s.Frame = crop.Size;
            s.Inner = new Rectangle(Point.Empty, crop.Size);
            s.HasAudio = info.Audio.Count > 0;
            s.RgbSource = VideoExport.IsRgb(src, info.Raw);
            s.Level = level;
            s.Fps = info.Fps;
            s.AudioTrack = VideoExport.ClipTrack(clip, 0);
            s.Seconds = info.Duration;
            return s;
        }

        // A spec without a file, for the arithmetic of the size limit.
        static ExportSpec H3Spec(string output, int w, int h, double fps, int level, double seconds, bool audio)
        {
            ExportSpec s = new ExportSpec();
            s.Source = "C:\\h3\\in.mkv";
            s.Output = output;
            s.Crop = new Rectangle(0, 0, w, h);
            s.Frame = new Size(w, h);
            s.Inner = new Rectangle(0, 0, w, h);
            s.HasAudio = audio;
            s.Level = level;
            s.Fps = fps;
            s.Seconds = seconds;
            return s;
        }

        static ShareExpect H3PlayerExpect(MediaInfo info, ExportSpec s)
        {
            ShareExpect e = PlayerKit.ExportExpect(info, s);
            e.MaxBytes = VideoExport.TargetBytes(s.Level);
            return e;
        }

        // The player's runner (PlayerWindow.ExportMp4 calls VideoExport.RunPasses): one FFmpeg run per pass, the second
        // pass redone while the file is over its size, the pass log deleted whatever happened. Returns the exit code of
        // the last run (-1 when it was killed) and the log used.
        static int H3Run(ExportSpec s, out string log, out string err)
        {
            log = Path.Combine(Path.GetTempPath(), "stackshot-2pass-" + Guid.NewGuid().ToString("N"));
            List<string> passes = VideoExport.Passes(s, log);
            ExportAbort ab = VideoExport.Abort;
            err = "";
            int code = ab != null && ab.Set ? -1 : VideoExport.RunPasses(ffmpeg, s, passes, log, 600000, null, out err);
            VideoExport.DeletePassLog(log);
            return code;
        }

        // Pass-log files of one export that are still on disk.
        static string H3LogsLeft(string log)
        {
            List<string> left = new List<string>();
            foreach (string suffix in new string[] { "-0.log", "-0.log.mbtree", "-0.log.temp", "-0.log.mbtree.temp" })
                if (File.Exists(log + suffix)) left.Add(Path.GetFileName(log + suffix));
            return string.Join(", ", left.ToArray());
        }

        static string[] H3TempLogs(string folder)
        {
            return Directory.GetFiles(folder, "stackshot-2pass-*");
        }

        static string H3Fail(ShareReport rep)
        {
            return rep.Summary + " | " + rep.ToString().Replace("\n", " | ");
        }

        static string H3Size(string file, double seconds)
        {
            long n = new FileInfo(file).Length;
            return (n / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB, " +
                   (n * 8 / seconds / 1000).ToString("0", CultureInfo.InvariantCulture) + " kb/s on average";
        }

        // Kills an export after ms milliseconds (sets its abort flag, as the Esc key does).
        static Thread H3Killer(ExportAbort ab, int ms)
        {
            Thread t = new Thread(delegate() { Thread.Sleep(ms); ab.Set = true; });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        // ---- the tests ----

        // A 30 s clip at 16 MB: two passes, the file fits and uses most of the room; no pass log is left.
        static void H3Size30()
        {
            NeedFfmpeg();
            string src = H3Src30();
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-30s"), "para compartir.mp4"), 3);
            Check(VideoExport.Passes(s, "C:\\h3\\log").Count == 2, "16 MB does not take two passes");
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            long size = new FileInfo(s.Output).Length;
            long limit = VideoExport.TargetBytes(3);
            Add(H3Size(s.Output, info.Duration) + ", limit " + (limit / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB, video target " + VideoExport.TargetKbps(s) + " kb/s");
            Check(rep.Passed, H3Fail(rep));
            Add("uses " + (100.0 * size / limit).ToString("0.0", CultureInfo.InvariantCulture) + " % of the limit");
            Check(H3LogsLeft(log).Length == 0, "pass logs left: " + H3LogsLeft(log));
        }

        // 10 s of 720p at 25 MB (level 4): the level 4 limit, two passes.
        static void H3Size25()
        {
            NeedFfmpeg();
            string src = H3AvSrc("h3-10s-720.mp4", "1280x720", "30", "10");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-10s-720"), "para compartir.mp4"), 4);
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            long size = new FileInfo(s.Output).Length;
            long limit = VideoExport.TargetBytes(4);
            Add(H3Size(s.Output, info.Duration) + ", limit " + (limit / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB");
            Check(rep.Passed, H3Fail(rep));
            Add("uses " + (100.0 * size / limit).ToString("0.0", CultureInfo.InvariantCulture) + " % of the limit");
            Check(H3LogsLeft(log).Length == 0, "pass logs left: " + H3LogsLeft(log));
        }

        // Very short clips: the level's ceiling applies, the file is far below the limit and must still pass the check.
        static void H3Short()
        {
            NeedFfmpeg();
            long limit = VideoExport.TargetBytes(3);
            string[] names = { "h3-03s.mp4", "h3-1s.mp4" };
            string[] secs = { "0.3", "1" };
            for (int i = 0; i < names.Length; i++)
            {
                string src = H3AvSrc(names[i], "640x360", "30", secs[i]);
                MediaInfo info = HuntInfo(src);
                ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-short"), "corto.mp4"), 3);
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                Check(code == 0 && File.Exists(s.Output), names[i] + ": ffmpeg " + code + ": " + Tail(err));
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
                Add(names[i] + " " + H3Size(s.Output, info.Duration));
                Check(rep.Passed, names[i] + ": " + H3Fail(rep));
                Check(new FileInfo(s.Output).Length <= limit, names[i] + " is over the limit");
            }
        }

        // The floor: 430 s in 16 MB is still over 150 kb/s and must fit; 440 s is under it (the editor and the player refuse).
        static void H3Floor()
        {
            NeedFfmpeg();
            int kb430 = VideoExport.TargetKbps(H3Spec("x.mp4", 1280, 720, 30, 3, 430, true));
            int kb440 = VideoExport.TargetKbps(H3Spec("x.mp4", 1280, 720, 30, 3, 440, true));
            Check(kb430 >= VideoExport.TargetMinKbps && kb440 < VideoExport.TargetMinKbps, "the floor: 430 s gives " + kb430 + " kb/s, 440 s gives " + kb440);
            Add("video " + kb430 + " kb/s at 430 s, " + kb440 + " kb/s at 440 s");
            string src = H3AvSrc("h3-430s.mp4", "160x90", "10", "430");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-430s"), "largo.mp4"), 3);
            Check(VideoExport.TargetKbps(s) >= VideoExport.TargetMinKbps, "the player would refuse the 430 s clip: " + VideoExport.TargetKbps(s));
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            Add(H3Size(s.Output, info.Duration) + ", limit 15.5 MB");
            Check(rep.Passed, H3Fail(rep));
            Check(new FileInfo(s.Output).Length <= VideoExport.TargetBytes(3), "the 430 s file is " + new FileInfo(s.Output).Length + " bytes, over the limit");
        }

        // A mono source (output stereo, 128 kb/s reserved) and a silent one (no reserve): both fit, with the right sound.
        static void H3MonoSilent()
        {
            NeedFfmpeg();
            long limit = VideoExport.TargetBytes(3);
            string mono = H3Src("h3-20s-mono.mp4", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=20\" -f lavfi -i \"sine=f=440:d=20\" -map 0:v -map 1:a -ac 1 " +
                                                    "-c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -c:a aac -shortest");
            string silent = H3Src("h3-20s-silent.mp4", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=20\" -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -an");
            string[] srcs = { mono, silent };
            string[] names = { "mono", "silent" };
            List<string> bad = new List<string>();
            for (int i = 0; i < srcs.Length; i++)
            {
                MediaInfo info = HuntInfo(srcs[i]);
                ExportSpec s = H3PlayerSpec(info, srcs[i], Path.Combine(WorkDir("h3-" + names[i]), "para compartir.mp4"), 3);
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                if (code != 0 || !File.Exists(s.Output)) { bad.Add(names[i] + ": ffmpeg " + code + ": " + Tail(err)); continue; }
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
                long size = new FileInfo(s.Output).Length;
                Add(names[i] + ": " + H3Size(s.Output, info.Duration) + " = " + (100.0 * size / limit).ToString("0", CultureInfo.InvariantCulture) + " % of the limit, video target " + VideoExport.TargetKbps(s) + " kb/s");
                if (!rep.Passed) bad.Add(names[i] + ": " + H3Fail(rep));
                if (size > limit) bad.Add(names[i] + ": " + size + " bytes against the limit " + limit);
            }
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // The ceiling of each clip's bitrate in the two-pass arguments, for the frames the editor and the player export:
        // the level's High profile maximum (4.1/4.2 62.5 Mb/s; 5.x and 6.x capped at 160 Mb/s), and VBV buffer at most 2x.
        static void H3LevelArgs()
        {
            // width, height, frames per second, level expected, the most bitrate that level allows (kb/s)
            int[][] cases = {
                new int[] { 1280, 720, 30, 41, 62500 },
                new int[] { 1920, 1080, 30, 41, 62500 },
                new int[] { 1920, 1080, 60, 42, 62500 },
                new int[] { 3840, 2160, 30, 51, 160000 },
                new int[] { 3840, 2160, 60, 52, 160000 },
                new int[] { 7680, 4320, 30, 60, 160000 } };
            List<string> seen = new List<string>();
            foreach (int[] c in cases)
            {
                // 0.5 s: the bitrate asked for is far above any ceiling, so the ceiling is what the arguments carry.
                ExportSpec s = H3Spec("C:\\h3\\out.mp4", c[0], c[1], c[2], 3, 0.5, true);
                string pass2 = VideoExport.Passes(s, "C:\\h3\\log")[1];
                Match lv = Regex.Match(pass2, @"-level:v (\d+)\.(\d+)");
                Match mr = Regex.Match(pass2, @"-maxrate (\d+)k");
                Match bf = Regex.Match(pass2, @"-bufsize (\d+)k");
                string label = c[0] + "x" + c[1] + " " + c[2] + " fps";
                Check(lv.Success && mr.Success && bf.Success, label + ": no level, maxrate or bufsize in " + pass2);
                int level = int.Parse(lv.Groups[1].Value, CultureInfo.InvariantCulture) * 10 + int.Parse(lv.Groups[2].Value, CultureInfo.InvariantCulture);
                int maxrate = int.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture);
                int bufsize = int.Parse(bf.Groups[1].Value, CultureInfo.InvariantCulture);
                Check(level == c[3], label + ": level " + level + ", expected " + c[3]);
                Check(maxrate <= c[4], label + ": -maxrate " + maxrate + " is over the level's " + c[4]);
                Check(bufsize <= 2 * maxrate, label + ": -bufsize " + bufsize + " is over twice -maxrate " + maxrate);
                seen.Add(label + " level " + level + " maxrate " + maxrate);
            }
            Add(string.Join("; ", seen.ToArray()));
        }

        // Encoded for real: a 4K60 clip (level 5.2, medium preset) and an 8K clip (level 6.0): the stream's own level is
        // what the check reads back, and the sizes stay under 16 MB.
        static void H3LevelEncode()
        {
            NeedFfmpeg();
            string[] names = { "h3-4k60.mp4", "h3-8k30.mp4" };
            string[] args = {
                "-f lavfi -i \"testsrc2=s=3840x2160:r=60:d=2\" -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -an",
                "-f lavfi -i \"testsrc2=s=7680x4320:r=30:d=0.2\" -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p -an" };
            List<string> bad = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                string src = H3Src(names[i], args[i]);
                MediaInfo info = HuntInfo(src);
                ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-" + names[i]), "para compartir.mp4"), 3);
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                if (code != 0 || !File.Exists(s.Output)) { bad.Add(names[i] + ": ffmpeg " + code + ": " + Tail(err)); continue; }
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
                long size = new FileInfo(s.Output).Length;
                Add(names[i] + " " + info.Width + "x" + info.Height + " " + info.Fps.ToString("0", CultureInfo.InvariantCulture) + " fps, " + H3Size(s.Output, info.Duration) +
                    " = " + (100.0 * size / VideoExport.TargetBytes(3)).ToString("0.0", CultureInfo.InvariantCulture) + " % of 15.5 MB");
                if (!rep.Passed) bad.Add(names[i] + ": " + H3Fail(rep));
                if (size > VideoExport.TargetBytes(3)) bad.Add(names[i] + " is " + size + " bytes, over the limit " + VideoExport.TargetBytes(3));
                // The decoder's model (VBV): at most the ceiling of 160 Mb/s over the clip plus the 300 Mb buffer, which a
                // clip shorter than two seconds may spend at once (a valid stream for the level).
                double kbits = size * 8.0 / 1000, most = 160000 * info.Duration + 300000;
                if (kbits > most) bad.Add(names[i] + " has " + kbits.ToString("0", CultureInfo.InvariantCulture) + " kbit, over the " + most.ToString("0", CultureInfo.InvariantCulture) + " kbit the level's buffer model allows");
            }
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // The cut of an MPEG-TS source (early seek) and of an MP4 with the same picture, at level 0 and at 16 MB: the kept
        // 4 s at 30 fps are 120 frames in every case.
        static void H3TsFrames()
        {
            NeedFfmpeg();
            string ts = H3TsSrc();
            string mp4 = H3Src10();
            List<double[]> keep = Ranges(1.0, 3.0, 5.0, 7.0);
            string[] srcs = { ts, mp4 };
            string[] names = { "ts", "mp4" };
            int[] levels = { 0, 3 };
            List<string> got = new List<string>();
            List<string> bad = new List<string>();
            for (int i = 0; i < srcs.Length; i++)
            {
                foreach (int level in levels)
                {
                    MediaInfo info = HuntInfo(srcs[i]);
                    ExportSpec s = HuntOne(srcs[i], Path.Combine(WorkDir("h3-tsframes"), names[i] + " level " + level + ".mp4"), new Rectangle(0, 0, 640, 360), keep, 1.0, 0, info);
                    s.Level = level;
                    string log;
                    string err;
                    int code = H3Run(s, out log, out err);
                    if (code != 0 || !File.Exists(s.Output)) { bad.Add(names[i] + " level " + level + ": ffmpeg " + code + ": " + Tail(err)); continue; }
                    int frames = H2Frames(s.Output);
                    MediaInfo om = HuntInfo(s.Output);
                    got.Add(names[i] + " level " + level + ": " + frames + " frames, " + om.Duration.ToString("0.00", CultureInfo.InvariantCulture) + " s, " + om.Fps.ToString("0.00", CultureInfo.InvariantCulture) + " fps");
                    if (frames != 120) bad.Add(names[i] + " level " + level + ": " + frames + " frames, expected 120");
                }
            }
            Add(string.Join("; ", got.ToArray()));
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // The same TS cut from a source with x264's default GOP (250 frames, about 8 s at 30 fps) and from one with a 1 s GOP:
        // the early seek (PlayerArgs.Early, a 3 s preroll) only reaches a keyframe when the GOP is short.
        static void H3TsGop()
        {
            NeedFfmpeg();
            string longGop = H3TsSrc();
            string shortGop = H3Src("h3-10s-gop30.ts", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=10\" -f lavfi -i \"sine=f=440:d=10\" -map 0:v -map 1:a " +
                                                       "-c:v libx264 -preset ultrafast -crf 18 -g 30 -pix_fmt yuv420p -c:a aac -shortest -f mpegts");
            string[] srcs = { longGop, shortGop };
            string[] names = { "GOP 250", "GOP 30" };
            List<double[]> keep = Ranges(1.0, 3.0, 5.0, 7.0);
            List<string> got = new List<string>();
            List<string> bad = new List<string>();
            for (int i = 0; i < srcs.Length; i++)
            {
                MediaInfo info = HuntInfo(srcs[i]);
                ExportSpec s = HuntOne(srcs[i], Path.Combine(WorkDir("h3-tsgop"), "cortes " + names[i] + ".mp4"), new Rectangle(0, 0, 640, 360), keep, 1.0, 0, info);
                s.Level = 0;
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                if (code != 0 || !File.Exists(s.Output)) { bad.Add(names[i] + ": ffmpeg " + code + ": " + Tail(err)); continue; }
                int frames = H2Frames(s.Output);
                got.Add(names[i] + ": " + frames + " frames");
                if (frames != 120) bad.Add(names[i] + ": " + frames + " frames, expected 120");
            }
            Add(string.Join("; ", got.ToArray()));
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // A phone-like clip (frames alternate 1/60 s and 1/20 s apart) at 16 MB: the duration and the sound hold.
        static void H3Vfr()
        {
            NeedFfmpeg();
            string src = H3Src("h3-vfr.mp4", "-f lavfi -i \"testsrc2=size=640x360:rate=60\" -f lavfi -i \"sine=f=440:d=3\" -map 0:v -map 1:a " +
                                             "-vf \"setpts='(floor(N/2)*(1/60+1/20)+mod(N,2)/60)/TB'\" -frames:v 90 -fps_mode passthrough " +
                                             "-c:v libx264 -preset medium -crf 18 -video_track_timescale 90000 -c:a aac -shortest");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-vfr"), "para compartir.mp4"), 3);
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            Add("source " + info.Fps.ToString("0.00", CultureInfo.InvariantCulture) + " fps average, " + H3Size(s.Output, info.Duration));
            Check(rep.Passed, H3Fail(rep));
        }

        // A GIF (no duration in its header on some builds) at 16 MB: it is converted to MP4 in two passes and checked.
        static void H3Gif()
        {
            NeedFfmpeg();
            string src = H3Src("h3-10s.gif", "-f lavfi -i \"testsrc2=s=320x180:r=15:d=10\"");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-gif"), "para compartir.mp4"), 3);
            Check(s.RgbSource && !s.HasAudio, "a GIF should be RGB without sound");
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            Add("GIF of " + info.Duration.ToString("0.00", CultureInfo.InvariantCulture) + " s, " + H3Size(s.Output, info.Duration));
            Check(rep.Passed, H3Fail(rep));
        }

        // A lossless RGB master (gbrp): converted to BT.709 YUV, tagged, two passes, under 16 MB.
        static void H3CineRgb()
        {
            NeedFfmpeg();
            string src = H3Src("h3-cine.mkv", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=3\" -c:v ffv1 -pix_fmt gbrp");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-cine"), "para compartir.mp4"), 3);
            Check(s.RgbSource, "a gbrp master is not read as RGB: " + PixelFormatOf(info));
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            Add(H3Size(s.Output, info.Duration) + " from a " + new FileInfo(src).Length / 1000 + " KB master");
            Check(rep.Passed, H3Fail(rep));
            Check(new FileInfo(s.Output).Length <= VideoExport.TargetBytes(3), "the master export is over 16 MB");
        }

        static string PixelFormatOf(MediaInfo info)
        {
            return VideoExport.PixelFormat(info.Raw) ?? "unknown";
        }

        // Cancelled during pass 1: FFmpeg is killed, no output, no pass log in TEMP.
        static void H3CancelPass1()
        {
            NeedFfmpeg();
            string src = H3Src30();
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-cancel1"), "cancelado.mp4"), 3);
            string log = Path.Combine(Path.GetTempPath(), "stackshot-2pass-" + Guid.NewGuid().ToString("N"));
            List<string> passes = VideoExport.Passes(s, log);
            ExportAbort ab = new ExportAbort();
            string err = "";
            int code = 0;
            VideoExport.Abort = ab;
            Thread killer = H3Killer(ab, 1000);
            try { code = VideoExport.Run(ffmpeg, passes[0], 600000, out err); }
            finally { VideoExport.Abort = null; }
            killer.Join();
            VideoExport.DeletePassLog(log);
            Check(code == -1, "pass 1 ended with " + code + " before the cancel (it is too fast to cancel here): " + Tail(err));
            Check(!File.Exists(s.Output), "an output was left after the cancel");
            Check(H3LogsLeft(log).Length == 0, "pass logs left: " + H3LogsLeft(log));
            Check(H3TempLogs(Path.GetTempPath()).Length == 0, "logs left in TEMP");
            Add("pass 1 killed after 1 s");
        }

        // Cancelled during pass 2: the output is partial and the player removes it; the pass log goes too.
        static void H3CancelPass2()
        {
            NeedFfmpeg();
            string src = H3Src30();
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-cancel2"), "cancelado.mp4"), 3);
            string log = Path.Combine(Path.GetTempPath(), "stackshot-2pass-" + Guid.NewGuid().ToString("N"));
            List<string> passes = VideoExport.Passes(s, log);
            string err;
            int first = VideoExport.Run(ffmpeg, passes[0], 600000, out err);
            Check(first == 0, "pass 1 failed: " + first + " " + Tail(err));
            ExportAbort ab = new ExportAbort();
            VideoExport.Abort = ab;
            Thread killer = H3Killer(ab, 1500);
            int second;
            try { second = VideoExport.Run(ffmpeg, passes[1], 600000, out err); }
            finally { VideoExport.Abort = null; }
            killer.Join();
            long partial = File.Exists(s.Output) ? new FileInfo(s.Output).Length : -1;
            if (File.Exists(s.Output)) File.Delete(s.Output);
            VideoExport.DeletePassLog(log);
            Check(second == -1, "pass 2 ended with " + second + " before the cancel (it is too fast to cancel here): " + Tail(err));
            Check(!File.Exists(s.Output), "the output was left after the cancel");
            Check(H3LogsLeft(log).Length == 0, "pass logs left: " + H3LogsLeft(log));
            Check(H3TempLogs(Path.GetTempPath()).Length == 0, "logs left in TEMP");
            Add("pass 2 had written " + partial + " bytes before the cancel");
        }

        // Two exports with size limits at the same time: each has its own log, both finish and pass the check.
        static void H3TwoAtOnce()
        {
            NeedFfmpeg();
            string src = H3Src10();
            MediaInfo info = HuntInfo(src);
            string dirA = WorkDir("h3-two-a");
            string dirB = WorkDir("h3-two-b");
            ExportSpec a = H3PlayerSpec(info, src, Path.Combine(dirA, "a (para compartir).mp4"), 3);
            ExportSpec b = H3PlayerSpec(info, src, Path.Combine(dirB, "b (para compartir).mp4"), 4);
            string logA = null, logB = null, errA = "", errB = "";
            int codeA = -99, codeB = -99;
            Thread ta = new Thread(delegate() { codeA = H3Run(a, out logA, out errA); });
            Thread tb = new Thread(delegate() { codeB = H3Run(b, out logB, out errB); });
            ta.Start();
            tb.Start();
            ta.Join();
            tb.Join();
            Check(codeA == 0 && codeB == 0, "exit codes " + codeA + " and " + codeB + ": " + Tail(errA) + " / " + Tail(errB));
            Check(logA != logB, "both exports got the same pass log: " + logA);
            ShareReport repA = ShareCheck.Validate(ffmpeg, a.Output, H3PlayerExpect(info, a));
            ShareReport repB = ShareCheck.Validate(ffmpeg, b.Output, H3PlayerExpect(info, b));
            Check(repA.Passed, "16 MB: " + H3Fail(repA));
            Check(repB.Passed, "25 MB: " + H3Fail(repB));
            Add("16 MB " + H3Size(a.Output, info.Duration) + "; 25 MB " + H3Size(b.Output, info.Duration));
            Check(H3LogsLeft(logA).Length == 0 && H3LogsLeft(logB).Length == 0, "pass logs left");
        }

        // TEMP in a folder with accents and spaces (a user whose name has accents): the logs go there and are removed.
        static void H3TempAccents()
        {
            NeedFfmpeg();
            string src = H3Src10();
            MediaInfo info = HuntInfo(src);
            string accent = Path.Combine(h3Dir, "Carpeta \u00F1and\u00FA con espacios");
            Directory.CreateDirectory(accent);
            string oldTemp = Environment.GetEnvironmentVariable("TEMP");
            string oldTmp = Environment.GetEnvironmentVariable("TMP");
            try
            {
                Environment.SetEnvironmentVariable("TEMP", accent);
                Environment.SetEnvironmentVariable("TMP", accent);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                Check(string.Equals(temp, accent, StringComparison.OrdinalIgnoreCase), "TEMP is " + temp);
                ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-accent-temp"), "para compartir.mp4"), 3);
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + " with TEMP in an accented folder: " + Tail(err));
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
                Check(rep.Passed, H3Fail(rep));
                Check(H3TempLogs(accent).Length == 0, "logs left in the accented TEMP: " + string.Join(", ", H3TempLogs(accent)));
                Add(H3Size(s.Output, info.Duration));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMP", oldTemp);
                Environment.SetEnvironmentVariable("TMP", oldTmp);
            }
        }

        // The output in a folder with accents and a name with accents; TEMP as usual.
        static void H3OutAccents()
        {
            NeedFfmpeg();
            string src = H3Src10();
            MediaInfo info = HuntInfo(src);
            string dir = Path.Combine(h3Dir, "V\u00EDdeos \u00F1 (prueba)");
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(dir, "salida \u00F1 (para compartir).mp4"), 3);
            string log;
            string err;
            Directory.CreateDirectory(dir);
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
            Check(rep.Passed, H3Fail(rep));
            Add(H3Size(s.Output, info.Duration));
        }

        // The check: a file one byte over its limit fails "L\u00EDmite de tama\u00F1o" and is not passed; at its size it passes that line.
        static void H3RejectOver()
        {
            NeedFfmpeg();
            string src = H3AvSrc("h3-1s.mp4", "640x360", "30", "1");
            MediaInfo info = HuntInfo(src);
            ExportSpec s = H3PlayerSpec(info, src, Path.Combine(WorkDir("h3-reject"), "corto.mp4"), 0);
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            long size = new FileInfo(s.Output).Length;
            ShareExpect over = PlayerKit.ExportExpect(info, s);
            over.MaxBytes = size - 1;
            ShareReport repOver = ShareCheck.Validate(ffmpeg, s.Output, over);
            Check(!repOver.Passed && repOver.Failed(ShareCheck.NameFileSize), "one byte over the limit still passes: " + H3Fail(repOver));
            ShareExpect exact = PlayerKit.ExportExpect(info, s);
            exact.MaxBytes = size;
            ShareReport repExact = ShareCheck.Validate(ffmpeg, s.Output, exact);
            Check(!repExact.Failed(ShareCheck.NameFileSize), "a file at its own size fails the limit: " + H3Fail(repExact));
            Add("size " + size + " bytes; the summary: " + repOver.Summary);
        }

        // The editor and the player build the same FFmpeg runs and expect the same things for the same source and level.
        static void H3SameArgs()
        {
            NeedFfmpeg();
            string src = H3Src10();
            MediaInfo info = HuntInfo(src);
            string output = Path.Combine(WorkDir("h3-same"), "same.mp4");
            int[] levels = { 0, 3, 4 };
            foreach (int level in levels)
            {
                ExportSpec p = H3PlayerSpec(info, src, output, level);
                ExportSpec ed = H3EditorSpec(src, output, level);
                List<string> pa = VideoExport.Passes(p, "C:\\h3\\log");
                List<string> ea = VideoExport.Passes(ed, "C:\\h3\\log");
                Check(pa.Count == ea.Count, "level " + level + ": " + pa.Count + " runs in the player, " + ea.Count + " in the editor");
                for (int i = 0; i < pa.Count; i++)
                    Check(pa[i] == ea[i], "level " + level + " run " + (i + 1) + " differs: player [" + pa[i] + "] editor [" + ea[i] + "]");
                ShareExpect pe = PlayerKit.ExportExpect(info, p);
                ShareExpect ee = Editor.ExpectFor(ed, info.Duration, info.Fps, true);
                bool same = pe.Width == ee.Width && pe.Height == ee.Height && pe.Level == ee.Level && pe.HasAudio == ee.HasAudio &&
                            pe.RgbSource == ee.RgbSource && pe.CentredChroma == ee.CentredChroma && Math.Abs(pe.Seconds - ee.Seconds) < 1e-9;
                Check(same, "level " + level + ": the expectations differ: player " + pe.Width + "x" + pe.Height + " level " + pe.Level + " " + pe.Seconds +
                            " s, editor " + ee.Width + "x" + ee.Height + " level " + ee.Level + " " + ee.Seconds + " s");
            }
            Add("levels 0, 3 and 4 give the same runs in both");
        }

        // The editor's cuts with a speed change and two joined clips, at 16 MB: one size for the whole, the check passes.
        static void H3EditorJoined()
        {
            NeedFfmpeg();
            string a = H3AvSrc("h3-6s-a.mp4", "640x360", "30", "6");
            string b = H3AvSrc("h3-6s-b.mp4", "640x360", "30", "6");
            List<VideoClip> clips = new List<VideoClip> { HuntClip(a), HuntClip(b) };
            List<double[]> keep = Ranges(0.5, 3.0, 7.0, 10.5);
            ExportSpec s = HuntJoin(clips, Path.Combine(WorkDir("h3-joined"), "unidos (editado).mp4"), new Rectangle(0, 0, 640, 360), keep, 1.5, 0, false);
            s.Level = 3;
            ShareExpect e = Editor.ExpectFor(s, TotalOf(clips), clips[0].Fps, true);
            e.MaxBytes = VideoExport.TargetBytes(3);
            Check(Math.Abs(e.Seconds - 4.0) < 0.01, "the expected length is " + e.Seconds + " s, 4 s was kept at 1.5x");
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, e);
            long size = new FileInfo(s.Output).Length;
            Add(H3Size(s.Output, e.Seconds) + " for 4 s");
            Check(rep.Passed, H3Fail(rep));
            Check(size <= VideoExport.TargetBytes(3), "the joined export is " + size + " bytes");
        }

        // Cuts in an MPEG-TS source (early seeks, concat) with a size limit: two passes through the same concat graph.
        static void H3EditorTs()
        {
            NeedFfmpeg();
            string src = H3TsSrc();
            MediaInfo info = HuntInfo(src);
            List<double[]> keep = Ranges(1.0, 3.0, 5.0, 7.0);
            ExportSpec s = HuntOne(src, Path.Combine(WorkDir("h3-ts"), "cortes (editado).mp4"), new Rectangle(0, 0, 640, 360), keep, 1.0, 0, info);
            s.Level = 3;
            ShareExpect e = Editor.ExpectFor(s, info.Duration, info.Fps, true);
            e.MaxBytes = VideoExport.TargetBytes(3);
            string log;
            string err;
            int code = H3Run(s, out log, out err);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, e);
            Add(H3Size(s.Output, e.Seconds) + " for " + e.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Check(rep.Passed, H3Fail(rep));
            Check(H3LogsLeft(log).Length == 0, "pass logs left: " + H3LogsLeft(log));
        }

        // Static, motion and noise pictures of 10 s at 16 MB: how close each one lands to the limit (measured, not assumed).
        static void H3Content()
        {
            NeedFfmpeg();
            string stat = H3Src("h3-static.mp4", "-f lavfi -i \"color=c=0x204060:s=640x360:r=30:d=10\" -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -an");
            string motion = H3Src("h3-motion.mp4", "-f lavfi -i \"testsrc2=s=640x360:r=30:d=10\" -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -an");
            string noise = H3Src("h3-noise.mp4", "-f lavfi -i \"nullsrc=s=640x360:r=30:d=10\" -vf noise=alls=100:allf=t+u -c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p -an");
            string[] srcs = { stat, motion, noise };
            string[] names = { "static", "motion", "noise" };
            long limit = VideoExport.TargetBytes(3);
            List<string> bad = new List<string>();
            for (int i = 0; i < srcs.Length; i++)
            {
                MediaInfo info = HuntInfo(srcs[i]);
                ExportSpec s = H3PlayerSpec(info, srcs[i], Path.Combine(WorkDir("h3-" + names[i]), "para compartir.mp4"), 3);
                string log;
                string err;
                int code = H3Run(s, out log, out err);
                if (code != 0 || !File.Exists(s.Output)) { bad.Add(names[i] + ": ffmpeg " + code + ": " + Tail(err)); continue; }
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, H3PlayerExpect(info, s));
                long size = new FileInfo(s.Output).Length;
                Add(names[i] + " " + (size / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB = " +
                    (100.0 * size / limit).ToString("0", CultureInfo.InvariantCulture) + " % of the limit; ");
                if (!rep.Passed) bad.Add(names[i] + ": " + H3Fail(rep));
                if (size > limit) bad.Add(names[i] + " is over the limit: " + size);
            }
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // An unedited MP4 with the 16 MB quality chosen in the editor: the export must start (and the file must come).
        // Editor.ExportVideo closes the window at once when nothing needs transcoding (no edit, MP4, no size or speed change).
        static void H3EditorSilent()
        {
            NeedFfmpeg();
            string src = H3Src10();
            MediaInfo info = HuntInfo(src);
            string dir = WorkDir("h3-editor");
            string copy = Path.Combine(dir, "clip.mp4");
            File.Copy(src, copy);
            BindingFlags np = BindingFlags.NonPublic | BindingFlags.Instance;
            ConstructorInfo ctor = typeof(Editor).GetConstructor(np, null,
                new Type[] { typeof(ShotStack), typeof(string), typeof(Bitmap), typeof(string), typeof(double) }, null);
            Check(ctor != null, "no editor constructor for a video was found");
            Bitmap img = White(640, 360);
            Editor ed = (Editor)ctor.Invoke(new object[] { null, copy, img, ffmpeg, info.Duration });
            try
            {
                ((Timeline)typeof(Editor).GetField("timeline", np).GetValue(ed)).Quality = 3;
                ((Canvas)typeof(Editor).GetField("canvas", np).GetValue(ed)).BgOn = false;
                typeof(Editor).GetMethod("ExportVideo", np).Invoke(ed, null);
                bool closed = (bool)typeof(Editor).GetField("closeWithoutAsking", np).GetValue(ed);
                object started = typeof(Editor).GetField("export", np).GetValue(ed);
                Add("closeWithoutAsking " + closed + ", export started " + (started != null));
                Check(!closed && started != null, "the editor closed without exporting: 16 MB chosen, an unedited MP4, no file is made");
            }
            finally
            {
                typeof(Editor).GetMethod("CancelExport", np).Invoke(ed, null);
                ed.Dispose();
                img.Dispose();
            }
        }
    }
}

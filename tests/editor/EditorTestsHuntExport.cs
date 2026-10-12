// Bug hunt of the MP4/GIF export (suite "huntexport"): real FFmpeg runs on synthetic sources, each result checked by
// ShareCheck against what Editor.ExpectFor says it must be. The sources are hashed (SHA-256) and must not change.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static readonly Dictionary<string, string> huntSources = new Dictionary<string, string>();
        static string huntDir;

        static string HuntSha(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(fs));
        }

        // A synthetic source made once by FFmpeg from the given arguments; its hash is kept for the final check.
        static string HuntOnce(string dir, string name, string args)
        {
            string p = Path.Combine(dir, name);
            if (!File.Exists(p))
            {
                FfOk("-hide_banner -loglevel error -y " + args + " " + Q(p), 240000);
                huntSources[p] = HuntSha(p);
            }
            return p;
        }

        static string HuntD(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static MediaInfo HuntInfo(string file)
        {
            MediaInfo mi = MediaInfo.Probe(ffmpeg, file);
            Check(mi.CanPlay, "no video or frame rate read from " + Path.GetFileName(file) + ": " + Tail(mi.Raw));
            return mi;
        }

        // The clip as the editor's AddClip builds it from the probe.
        static VideoClip HuntClip(string file)
        {
            MediaInfo mi = HuntInfo(file);
            VideoClip c = new VideoClip();
            c.Path = file;
            c.Duration = mi.Duration;
            c.Fps = mi.Fps;
            c.Width = mi.Width;
            c.Height = mi.Height;
            c.HasAudio = mi.Audio.Count > 0;
            c.Rgb = VideoExport.IsRgbFormat(file, mi.PixelFormat);
            c.TakeColour(mi);
            c.Info = mi;
            return c;
        }

        static ExportSpec HuntOne(string src, string output, Rectangle crop, List<double[]> keep, double speed, int maxH, MediaInfo mi)
        {
            ExportSpec s = new ExportSpec();
            s.Source = src;
            s.Output = output;
            s.Crop = crop;
            s.Keep = keep;
            s.Speed = speed;
            s.MaxHeight = maxH;
            s.HasAudio = mi.Audio.Count > 0;
            s.RgbSource = VideoExport.IsRgb(src, mi.Raw);
            s.SourceIsGif = string.Equals(Path.GetExtension(src), ".gif", StringComparison.OrdinalIgnoreCase);
            s.Level = 1;
            s.Fps = mi.Fps;
            return s;
        }

        // Joined clips as the editor builds them: canvas of the first clip, keep on the joined timeline.
        static ExportSpec HuntJoin(List<VideoClip> clips, string output, Rectangle crop, List<double[]> keep, double speed, int maxH, bool gif)
        {
            ExportSpec s = new ExportSpec();
            s.Source = clips[0].Path;
            s.Output = output;
            s.Clips = clips;
            s.Canvas = new Size(clips[0].Width, clips[0].Height);
            s.Crop = crop;
            s.Keep = keep;
            s.Speed = speed;
            s.MaxHeight = maxH;
            s.ToGif = gif;
            s.HasAudio = ClipSeq.AnyAudio(clips);
            s.RgbSource = clips[0].Rgb;
            s.Level = 1;
            s.Fps = clips[0].Fps;
            return s;
        }

        static double TotalOf(List<VideoClip> clips)
        {
            double t = 0;
            foreach (VideoClip c in clips) t += c.Duration;
            return t;
        }

        static List<double[]> Ranges(params double[] pairs)
        {
            List<double[]> r = new List<double[]>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) r.Add(new double[] { pairs[i], pairs[i + 1] });
            return r;
        }

        // Runs an export the way the editor does (VideoExport.Arguments, FFmpeg, then ShareCheck against ExpectFor).
        static void HuntRun(ExportSpec s, double duration, double fps, bool fpsKnown)
        {
            string args = VideoExport.Arguments(s);
            ShareExpect e = Editor.ExpectFor(s, duration, fps, fpsKnown);
            Add("expect " + e.Width + "x" + e.Height + " " + HuntD(e.Fps) + " fps " + HuntD(e.Seconds) + " s slack " + HuntD(e.Slack) + (e.Gif ? " gif" : "") + (e.HasAudio ? " audio" : ""));
            string err;
            int code = Ff(args, out err, 600000);
            Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err) + " | args: " + args);
            ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, e);
            if (!rep.Passed) throw new TestFailed(rep.Summary + " | " + rep.ToString().Replace("\n", " | ") + " | args: " + args);
        }

        // Rewrites the Matroska Segment Duration (ID 0x4489, a float in TimecodeScale units, 1 ms by default) to 10 s.
        static byte[] HuntLongMkv(byte[] src)
        {
            byte[] b = (byte[])src.Clone();
            for (int i = 0; i + 11 <= b.Length; i++)
            {
                if (b[i] != 0x44 || b[i + 1] != 0x89 || b[i + 2] != 0x88) continue;
                byte[] ten = BitConverter.GetBytes(10000.0);
                Check(BitConverter.IsLittleEndian, "big-endian host");
                for (int k = 0; k < 8; k++) b[i + 3 + k] = ten[7 - k];
                return b;
            }
            throw new TestFailed("no Duration element in the Matroska header");
        }

        // A PNG overlay of the editor's shape: transparent, with one opaque mark.
        static string HuntPng(string dir, string name, int w, int h)
        {
            string p = Path.Combine(dir, name);
            using (Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    using (SolidBrush ink = new SolidBrush(Color.FromArgb(255, 200, 40, 40))) g.FillRectangle(ink, w / 5, h / 5, w / 4, h / 4);
                }
                b.Save(p, System.Drawing.Imaging.ImageFormat.Png);
            }
            return p;
        }

        static void HuntSuite()
        {
            huntDir = WorkDir("hunt");
            string s1080 = HuntOnce(huntDir, "s1080.mp4", "-f lavfi -i testsrc2=s=1920x1080:r=30:d=4 -f lavfi -i sine=f=440:r=44100:d=4 -map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ac 1 -shortest");
            string s720 = HuntOnce(huntDir, "s720b.mp4", "-f lavfi -i testsrc=s=1280x720:r=25:d=3 -f lavfi -i sine=f=660:r=22050:d=3 -map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ac 2 -shortest");
            string s59 = HuntOnce(huntDir, "s59.mkv", "-f lavfi -i testsrc=s=640x360:r=60000/1001:d=2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            string s2398 = HuntOnce(huntDir, "s2398.mp4", "-f lavfi -i testsrc=s=640x360:r=24000/1001:d=2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            string odd = HuntOnce(huntDir, "odd.mp4", "-f lavfi -i testsrc=s=333x251:r=25:d=2 -c:v mpeg4 -q:v 3 -pix_fmt yuv420p");
            string s51 = HuntOnce(huntDir, "s51.mp4", "-f lavfi -i testsrc=s=640x360:r=30:d=2 -f lavfi -i anullsrc=r=48000:cl=5.1:d=2 -map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ac 6 -shortest");
            string s8k = HuntOnce(huntDir, "s8k.mp4", "-f lavfi -i testsrc=s=320x240:r=30:d=2 -f lavfi -i sine=f=220:r=8000:d=2 -map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ac 1 -shortest");
            string cine = HuntOnce(huntDir, "Cine 3pistas.mkv", "-f lavfi -i color=c=0x20C040:s=320x240:r=30:d=2,format=gbrp -f lavfi -i sine=f=440:d=2 -f lavfi -i sine=f=880:d=2 -map 0:v -map 1:a -map 2:a -c:v libx264rgb -preset ultrafast -qp 0 -c:a flac");
            string gif = HuntOnce(huntDir, "g.gif", "-f lavfi -i testsrc=s=320x240:r=10:d=2 -loop 0");
            string ts = HuntOnce(huntDir, "ts6.ts", "-f lavfi -i testsrc=s=640x360:r=25:d=6 -f lavfi -i sine=f=440:r=44100:d=6 -map 0:v -map 1:a -c:v mpeg2video -b:v 2M -c:a mp2 -b:a 128k -ac 2 -shortest -f mpegts");
            string s2997 = HuntOnce(huntDir, "s2997.mp4", "-f lavfi -i testsrc=s=640x360:r=30000/1001:d=2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
            string longv = HuntOnce(huntDir, "long10.mp4", "-f lavfi -i testsrc=s=160x120:r=5:d=600 -c:v libx264 -preset ultrafast -crf 35 -pix_fmt yuv420p");

            Test("huntexport", "odd-source-even-crop", delegate
            {
                NeedFfmpeg();
                // A 333x251 source is cropped to the even 330x248 the editor would use.
                MediaInfo mi = HuntInfo(odd);
                ExportSpec s = HuntOne(odd, Path.Combine(huntDir, "odd-a.mp4"), new Rectangle(0, 0, 330, 248), null, 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
                ExportSpec t = HuntOne(odd, Path.Combine(huntDir, "odd-b.mp4"), new Rectangle(2, 2, 320, 240), null, 1, 0, mi);
                HuntRun(t, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "odd-source-shrink-720-to-120-odd-width", delegate
            {
                NeedFfmpeg();
                // 332x250 scaled to 120 px tall gives 158.4 px wide: the output must be an even 158.
                MediaInfo mi = HuntInfo(odd);
                ExportSpec s = HuntOne(odd, Path.Combine(huntDir, "odd-shrink.mp4"), new Rectangle(0, 0, 332, 250), null, 1, 120, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "1080-single-trim-speed-1.5-to-720", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s1080);
                ExportSpec s = HuntOne(s1080, Path.Combine(huntDir, "a1080-trim.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0.5, 3.0), 1.5, 720, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "two-clips-1080-720-to-720p", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                double total = TotalOf(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined-720.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0, total), 1, 720, false);
                HuntRun(s, total, clips[0].Fps, true);
            });
            Test("huntexport", "two-clips-cut-speed-1.5", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined-cut-15.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0.5, 2.5, 3.5, 6.5), 1.5, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "two-clips-speed-2-keep-touching-boundary", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined-b2.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(2, 6), 2, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
                // Two whole clips, the second joined exactly at the boundary (0-4 and 4-7).
                ExportSpec t = HuntJoin(clips, Path.Combine(huntDir, "joined-b3.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0, 4, 4, 7), 1, 0, false);
                HuntRun(t, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "cut-removes-whole-middle-clip", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s59));
                clips.Add(HuntClip(s720));
                clips.Add(HuntClip(odd));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined-drop-middle.mp4"), new Rectangle(0, 0, 640, 360), Ranges(0, 2, 5, 7), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "gif-of-joined-clips", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(gif));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined.gif"), new Rectangle(0, 0, 1920, 1080), Ranges(0, TotalOf(clips)), 1, 0, true);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
                ExportSpec t = HuntJoin(clips, Path.Combine(huntDir, "joined-crop.gif"), new Rectangle(100, 100, 640, 360), Ranges(0.25, 5), 1, 0, true);
                HuntRun(t, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "gif-source-joined-into-mp4", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(gif));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "joined-gifsrc.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0, TotalOf(clips)), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "unicode-apostrophe-ampersand-percent-caret-names", delegate
            {
                NeedFfmpeg();
                string dir = Path.Combine(huntDir, "carpeta \u00F1 & % ^ '");
                Directory.CreateDirectory(dir);
                string copy = Path.Combine(dir, "fuente \u00F1 & % ^ '.mp4");
                File.Copy(s720, copy);
                Check(HuntSha(copy) == huntSources[s720], "the copy differs from its source");
                MediaInfo mi = HuntInfo(copy);
                string outName = Path.Combine(dir, "Cine \u00F1 'x' & 100% ^ (editado) \u65E5\u672C.mp4");
                ExportSpec s = HuntOne(copy, outName, new Rectangle(0, 0, 1280, 720), Ranges(0.5, 2.5), 2, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
                ExportSpec g = HuntOne(copy, Path.Combine(dir, "gif & % ^ ' \u00F1.gif"), new Rectangle(0, 0, 640, 360), null, 1, 0, mi);
                g.ToGif = true;
                HuntRun(g, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "output-name-already-taken-gets-new-name", delegate
            {
                NeedFfmpeg();
                string taken = Path.Combine(huntDir, "taken.mp4");
                File.WriteAllText(taken, "not a video");
                string before = HuntSha(taken);
                string unique = ShotStack.Unique(taken);
                Check(unique != taken, "Unique returned the taken name");
                Add("unique " + Path.GetFileName(unique));
                MediaInfo mi = HuntInfo(s720);
                ExportSpec s = HuntOne(s720, unique, new Rectangle(0, 0, 1280, 720), null, 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
                Check(HuntSha(taken) == before, "the existing file was changed");
            });
            Test("huntexport", "read-only-existing-output-not-corrupted", delegate
            {
                NeedFfmpeg();
                string ro = Path.Combine(huntDir, "readonly.mp4");
                File.WriteAllText(ro, "keep me");
                File.SetAttributes(ro, FileAttributes.ReadOnly);
                try
                {
                    MediaInfo mi = HuntInfo(s720);
                    ExportSpec s = HuntOne(s720, ro, new Rectangle(0, 0, 1280, 720), null, 1, 0, mi);
                    string err;
                    int code = Ff(VideoExport.Arguments(s), out err, 120000);
                    Add("ffmpeg exit " + code);
                    if (code == 0) Check(File.ReadAllText(ro).Length > 0 && ShareCheck.Validate(ffmpeg, ro, Editor.ExpectFor(s, mi.Duration, mi.Fps, true)).Passed, "FFmpeg overwrote a read-only file and left it broken");
                    else Check(File.ReadAllText(ro) == "keep me", "a failed export changed the read-only file");
                }
                finally { File.SetAttributes(ro, FileAttributes.Normal); }
            });
            Test("huntexport", "abort-stops-running-export", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(longv);
                string outp = Path.Combine(huntDir, "abort.mp4");
                ExportSpec s = HuntOne(longv, outp, new Rectangle(0, 0, 160, 120), null, 1, 0, mi);
                s.Level = 0;
                string args = VideoExport.Arguments(s);
                ExportAbort ab = new ExportAbort();
                int code = 0;
                string stderr = null;
                Thread t = new Thread(delegate()
                {
                    VideoExport.Abort = ab;
                    code = VideoExport.Run(ffmpeg, args, 300000, out stderr);
                });
                t.IsBackground = true;
                Stopwatch clock = Stopwatch.StartNew();
                t.Start();
                Thread.Sleep(2500);
                ab.Set = true;
                bool joined = t.Join(15000);
                clock.Stop();
                Add("returned " + code + " in " + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; partial file " + File.Exists(outp));
                Check(joined, "Run did not return after the abort");
                Check(code == -1, "an aborted run should return -1, got " + code);
            });
            Test("huntexport", "header-duration-too-long-flagged", delegate
            {
                NeedFfmpeg();
                // The header claims 10 s for a 2 s file (the Matroska Duration is rewritten); the picture is fine.
                string bad = Path.Combine(huntDir, "header-long.mkv");
                File.WriteAllBytes(bad, HuntLongMkv(File.ReadAllBytes(s59)));
                huntSources[bad] = HuntSha(bad);
                MediaInfo mi = HuntInfo(bad);
                Add("header " + HuntD(mi.Duration) + " s (picture about 2 s)");
                Check(mi.Duration > 9, "FFmpeg does not read the rewritten header: " + HuntD(mi.Duration) + " s");
                ExportSpec s = HuntOne(bad, Path.Combine(huntDir, "header-long-out.mp4"), new Rectangle(0, 0, 640, 360), null, 1, 0, mi);
                // A header that lies is a damaged source: the editor itself shows the header length, and a file that is much
                // shorter than expected is exactly what a truncated export looks like, so the check must say so instead of
                // passing it. The export itself is fine.
                string err;
                int code = Ff(VideoExport.Arguments(s), out err, 600000);
                Check(code == 0 && File.Exists(s.Output), "ffmpeg " + code + ": " + Tail(err));
                MediaInfo outInfo = HuntInfo(s.Output);
                Check(outInfo.Duration > 1.8 && outInfo.Duration < 2.3, "the export is as long as the picture: " + HuntD(outInfo.Duration) + " s");
                ShareReport rep = ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, mi.Duration, mi.Fps, mi.Fps > 0));
                Check(!rep.Passed, "a file far shorter than the header says must not pass the check");
            });
            Test("huntexport", "mpegts-early-seek-single", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(ts);
                ExportSpec s = HuntOne(ts, Path.Combine(huntDir, "ts-single.mp4"), new Rectangle(0, 0, 640, 360), Ranges(2.5, 4.5), 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "mpegts-early-seek-joined-pieces", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(ts);
                ExportSpec s = HuntOne(ts, Path.Combine(huntDir, "ts-pieces.mp4"), new Rectangle(0, 0, 640, 360), Ranges(1.0, 1.5, 2.7, 3.9), 1.5, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "touching-keep-ranges-single-source", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s1080);
                ExportSpec s = HuntOne(s1080, Path.Combine(huntDir, "touch.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(1, 2, 2, 3), 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "zero-length-keep-piece", delegate
            {
                NeedFfmpeg();
                // The editor drops pieces under 0.05 s (Timeline.Keep); a direct call gets a clean error, not an FFmpeg failure.
                MediaInfo mi = HuntInfo(s1080);
                ExportSpec s = HuntOne(s1080, Path.Combine(huntDir, "zero.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(1, 1, 2, 3), 1, 0, mi);
                bool refused = false;
                try { VideoExport.Arguments(s); } catch (ArgumentException) { refused = true; }
                Check(refused, "an empty piece must be refused");
            });
            Test("huntexport", "reversed-keep-range", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s1080);
                ExportSpec s = HuntOne(s1080, Path.Combine(huntDir, "reversed.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(3, 2, 1, 2), 1, 0, mi);
                bool refused = false;
                try { VideoExport.Arguments(s); } catch (ArgumentException) { refused = true; }
                Check(refused, "a reversed piece must be refused");
            });
            Test("huntexport", "59.94-source-speed-1.5", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s59);
                ExportSpec s = HuntOne(s59, Path.Combine(huntDir, "s59-15.mp4"), new Rectangle(0, 0, 640, 360), Ranges(0.5, 1.5), 1.5, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "23.976-source-speed-2", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s2398);
                ExportSpec s = HuntOne(s2398, Path.Combine(huntDir, "s2398-2.mp4"), new Rectangle(0, 0, 640, 360), null, 2, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
                ExportSpec t = HuntOne(s2398, Path.Combine(huntDir, "s2398-15.mp4"), new Rectangle(0, 0, 640, 360), null, 1.5, 0, mi);
                HuntRun(t, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "cine-3-tracks-single", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(cine);
                Check(VideoExport.IsRgb(cine, mi.Raw), "the master should read as RGB");
                ExportSpec s = HuntOne(cine, Path.Combine(huntDir, "cine-single.mp4"), new Rectangle(0, 0, 320, 240), Ranges(0.5, 1.5), 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "cine-3-tracks-joined-with-720-clip", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(cine));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "cine-joined.mp4"), new Rectangle(0, 0, 320, 240), Ranges(0.25, 3.5), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "mixed-rates-channels-to-stereo", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s8k));
                clips.Add(HuntClip(s51));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "mixed-audio.mp4"), new Rectangle(0, 0, 320, 240), Ranges(0, TotalOf(clips)), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "ten-minute-source-whole-and-trimmed", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(longv);
                Check(mi.Duration > 599, "the 10 min source reads " + HuntD(mi.Duration) + " s");
                ExportSpec s = HuntOne(longv, Path.Combine(huntDir, "long-whole.mp4"), new Rectangle(0, 0, 160, 120), null, 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
                ExportSpec t = HuntOne(longv, Path.Combine(huntDir, "long-trim.mp4"), new Rectangle(0, 0, 160, 120), Ranges(300.2, 420.4), 1, 0, mi);
                HuntRun(t, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "overlay-marks-two-clips-cut", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                string top = HuntPng(huntDir, "marks-1920.png", 1920, 1080);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "marks-cut.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(0.5, 3.5, 4.5, 6.5), 1, 0, false);
                s.Overlay = top;
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "backdrop-overlay-gif-joined-speed-2", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s1080));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                string top = HuntPng(huntDir, "bg-1100x700.png", 1100, 700);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "bg-joined.gif"), new Rectangle(100, 100, 1000, 600), Ranges(0, 3, 4, 7), 2, 0, true);
                s.Backdrop = true;
                s.Frame = new Size(1100, 700);
                s.Inner = new Rectangle(50, 50, 1000, 600);
                s.Overlay = top;
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "mpegts-backdrop-trim-to-gif-and-mp4-720", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(ts);
                // The source is 640x360: the crop is 480x320 inside it, padded into a frame with the video at (60,70).
                string top = HuntPng(huntDir, "bg-ts-600x460.png", 600, 460);
                ExportSpec g = HuntOne(ts, Path.Combine(huntDir, "ts-bg.gif"), new Rectangle(40, 20, 480, 320), Ranges(2.5, 4.5), 1, 0, mi);
                g.Backdrop = true;
                g.Frame = new Size(600, 460);
                g.Inner = new Rectangle(60, 70, 480, 320);
                g.Overlay = top;
                g.ToGif = true;
                HuntRun(g, mi.Duration, mi.Fps, mi.Fps > 0);
                ExportSpec m = HuntOne(ts, Path.Combine(huntDir, "ts-bg.mp4"), new Rectangle(40, 20, 480, 320), Ranges(1.0, 1.5, 2.7, 3.9), 1.5, 0, mi);
                m.Backdrop = true;
                m.Frame = new Size(600, 700);
                m.Inner = new Rectangle(60, 190, 480, 320);
                m.Overlay = HuntPng(huntDir, "bg-ts-600x700.png", 600, 700);
                m.MaxHeight = 360;
                HuntRun(m, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "odd-canvas-first-clip-joined", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(odd));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "odd-canvas.mp4"), new Rectangle(0, 0, 330, 248), Ranges(0, TotalOf(clips)), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "29.97-and-59.94-clips-speed-1.5", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s2997));
                clips.Add(HuntClip(s59));
                ClipSeq.Layout(clips);
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "ntsc-joined.mp4"), new Rectangle(0, 0, 640, 360), Ranges(0.2, 3.8), 1.5, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "keep-just-above-editor-minimum", delegate
            {
                NeedFfmpeg();
                MediaInfo mi = HuntInfo(s1080);
                ExportSpec s = HuntOne(s1080, Path.Combine(huntDir, "tiny.mp4"), new Rectangle(0, 0, 1920, 1080), Ranges(1.0, 1.06), 1, 0, mi);
                HuntRun(s, mi.Duration, mi.Fps, mi.Fps > 0);
            });
            Test("huntexport", "joined-sliver-at-clip-boundary", delegate
            {
                NeedFfmpeg();
                List<VideoClip> clips = new List<VideoClip>();
                clips.Add(HuntClip(s720));
                clips.Add(HuntClip(s720));
                ClipSeq.Layout(clips);
                // The 0.015 s piece at the start of the second clip is below ClipSeq.Sliver and is dropped on purpose.
                ExportSpec s = HuntJoin(clips, Path.Combine(huntDir, "sliver.mp4"), new Rectangle(0, 0, 1280, 720), Ranges(0.5, 2.99, 3.0, 3.015, 3.5, 5.2), 1, 0, false);
                HuntRun(s, TotalOf(clips), clips[0].Fps, true);
            });
            Test("huntexport", "sources-unchanged-sha256", delegate
            {
                foreach (KeyValuePair<string, string> kv in huntSources)
                {
                    Check(File.Exists(kv.Key), "source gone: " + kv.Key);
                    Check(HuntSha(kv.Key) == kv.Value, "source changed: " + Path.GetFileName(kv.Key));
                }
                Add(huntSources.Count + " sources hashed");
            });
        }
    }
}

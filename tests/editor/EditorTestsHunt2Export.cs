// Bug hunt of the MP4/GIF export, round 2 (suite "hunt2export"): real FFmpeg runs on synthetic sources. Each result is
// checked by ShareCheck against what Editor.ExpectFor (editor) or PlayerKit.ExportExpect (player) says it must be.
// Helpers of EditorTestsHuntExport.cs (HuntOne, HuntRun, HuntJoin, HuntOnce) and EditorTestsShare.cs are reused.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static readonly List<string> h2Made = new List<string>();
        static string h2Dir;

        // A synthetic source, made once; HuntOnce keeps its hash, checked at the end.
        static string H2Src(string name, string args)
        {
            string p = HuntOnce(h2Dir, name, args);
            if (!h2Made.Contains(p)) h2Made.Add(p);
            return p;
        }

        static string H2Base()
        {
            return H2Src("base.mp4", "-f lavfi -i testsrc2=s=640x360:r=25:d=3 -f lavfi -i sine=f=440:d=3 -map 0:v -map 1:a " +
                                     "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest");
        }

        static string H2Fps(string tag, string rate)
        {
            return H2Src("fps-" + tag + ".mp4", "-f lavfi -i testsrc2=s=640x360:r=" + rate + ":d=2 -f lavfi -i sine=f=440:d=2 -map 0:v -map 1:a " +
                                                "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest");
        }

        // The first part of a message (where FFmpeg's own error and ShareCheck's verdict start), not its tail.
        static string Head(string t)
        {
            t = (t ?? "").Trim();
            return t.Length > 900 ? t.Substring(0, 900) : t;
        }

        // Runs one export through HuntRun (ShareCheck against ExpectFor); returns "" when it passes, else what failed.
        static string H2Try(ExportSpec s, double duration, double fps, bool known)
        {
            try { HuntRun(s, duration, fps, known); return ""; }
            catch (TestFailed f) { return Path.GetFileName(s.Output) + ": " + Head(f.Message); }
            catch (Exception ex) { return Path.GetFileName(s.Output) + ": " + ex.GetType().Name + " " + Head(ex.Message); }
        }

        // The export the player makes of a whole video (PlayerWindow): even-sized crop, checked against PlayerKit.ExportExpect.
        static string H2Player(string src, string output)
        {
            MediaInfo info = HuntInfo(src);
            Rectangle crop = new Rectangle(0, 0, Math.Max(16, info.Width & ~1), Math.Max(16, info.Height & ~1));
            ExportSpec spec = new ExportSpec();
            spec.Source = src;
            spec.Output = output;
            spec.Crop = crop;
            spec.Frame = crop.Size;
            spec.Inner = new Rectangle(Point.Empty, crop.Size);
            spec.HasAudio = info.Audio.Count > 0;
            spec.RgbSource = VideoExport.IsRgb(src, info.Raw);
            spec.Level = 1;
            spec.Fps = info.Fps;
            int track = -1;
            foreach (AudioTrackInfo a in info.Audio) if (a.Default) { track = a.Index; break; }
            spec.AudioTrack = track >= 0 && track < info.Audio.Count ? track : 0;
            string err;
            int code = Ff(VideoExport.Arguments(spec), out err, 600000);
            if (code != 0 || !File.Exists(output)) return Path.GetFileName(output) + ": ffmpeg " + code + ": " + Tail(err);
            ShareReport rep = ShareCheck.Validate(ffmpeg, output, PlayerKit.ExportExpect(info, spec));
            return rep.Passed ? "" : Path.GetFileName(output) + ": " + rep.Summary + " | " + rep.ToString().Replace("\n", " | ");
        }

        // Video frames of a file (listed by framecrc, passthrough so nothing is dropped or repeated).
        static int H2Frames(string file)
        {
            int[] n = { 0 };
            string err;
            VideoExport.Run(ffmpeg, "-hide_banner -v error -i " + Q(file) + " -map 0:v:0 -fps_mode passthrough -f framecrc -", 300000,
                delegate(string line) { if (line.Length > 0 && line[0] != '#') lock (n) n[0]++; }, out err);
            lock (n) return n[0];
        }

        // The stereo 48 kHz float samples of one sound track of a file.
        static float[] H2Track(string file, int track)
        {
            string raw = file + ".t" + track + ".f32";
            FfOk("-hide_banner -loglevel error -y -i " + Q(file) + " -map 0:a:" + track + " -vn -ac 2 -ar 48000 -f f32le " + Q(raw), 120000);
            float[] s = ShareReadF32(raw);
            File.Delete(raw);
            return s;
        }

        // One decoded RGB frame, its 4:2:0 chroma read as sited at siting ("center" or "left"): what a player does with the flag or without it.
        static byte[] H2Frame(string file, double t, string siting)
        {
            string raw = file + "." + siting + ".rgb";
            string vf = "scale=in_chroma_loc=" + siting + ",format=yuv444p,format=rgb24";
            FfOk("-hide_banner -loglevel error -y -ss " + HuntD(t) + " -i " + Q(file) + " -frames:v 1 -vf \"" + vf + "\" -f rawvideo " + Q(raw), 120000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            return b;
        }

        static double H2Mean(byte[] a, byte[] b)
        {
            Check(a.Length == b.Length && a.Length > 0, "frames differ in size: " + a.Length + " / " + b.Length);
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
            return sum / a.Length;
        }

        // chroma_sample_loc_type of the first SPS (-1 when the stream does not say).
        static int H2ChromaLoc(string file)
        {
            string trace;
            VideoExport.Run(ffmpeg, "-hide_banner -i " + Q(file) + " -map 0:v:0 -c copy -bsf:v trace_headers -frames:v 1 -f null -", 60000, out trace);
            return ShareCheck.ParseSps(trace).ChromaLoc;
        }

        static int H2Level(string file)
        {
            string trace;
            VideoExport.Run(ffmpeg, "-hide_banner -i " + Q(file) + " -map 0:v:0 -c copy -bsf:v trace_headers -frames:v 1 -f null -", 60000, out trace);
            return ShareCheck.ParseSps(trace).Level;
        }

        static int H2Default(MediaInfo mi)
        {
            foreach (AudioTrackInfo a in mi.Audio) if (a.Default) return a.Index;
            return -1;
        }

        static void Hunt2ExportSuite()
        {
            h2Dir = WorkDir("hunt2export");

            Test("hunt2export", "rotated-90-whole-editor-and-player", delegate
            {
                NeedFfmpeg();
                string src = H2Src("rot90.mp4", "-display_rotation 90 -i " + Q(H2Base()) + " -c copy");
                MediaInfo mi = HuntInfo(src);
                Add("read " + mi.Width + "x" + mi.Height + " (coded 640x360)");
                Check(mi.Width == 360 && mi.Height == 640, "MediaInfo reads " + mi.Width + "x" + mi.Height + ", the display size is 360x640");
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "rot90 whole.mp4"), new Rectangle(0, 0, mi.Width, mi.Height), null, 1, 0, mi);
                string e1 = H2Try(s, mi.Duration, f, known);
                string e2 = H2Player(src, Path.Combine(h2Dir, "rot90 player.mp4"));
                Check(e1.Length == 0 && e2.Length == 0, "editor: [" + e1 + "] player: [" + e2 + "]");
            });

            Test("hunt2export", "rotated-270-cut-speed-1.5-and-joined", delegate
            {
                NeedFfmpeg();
                string src = H2Src("rot270.mp4", "-display_rotation 270 -i " + Q(H2Base()) + " -c copy");
                MediaInfo mi = HuntInfo(src);
                Check(mi.Width == 360 && mi.Height == 640, "MediaInfo reads " + mi.Width + "x" + mi.Height + ", the display size is 360x640");
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "rot270 cut.mp4"), new Rectangle(0, 0, 360, 640), Ranges(0.5, 2.0, 2.5, 2.9), 1.5, 0, mi);
                string e1 = H2Try(s, mi.Duration, f, known);
                VideoClip a = HuntClip(src), b = HuntClip(H2Base());
                List<VideoClip> clips = new List<VideoClip> { a, b };
                ExportSpec j = HuntJoin(clips, Path.Combine(h2Dir, "rot270 joined.mp4"), new Rectangle(0, 0, 360, 640), Ranges(0.5, 2.5, 3.2, 5.0), 1, 0, false);
                string e2 = H2Try(j, TotalOf(clips), a.Fps, true);
                Check(e1.Length == 0 && e2.Length == 0, "cut: [" + e1 + "] joined: [" + e2 + "]");
            });

            Test("hunt2export", "rotated-90-gif-speed-2-cut", delegate
            {
                NeedFfmpeg();
                string src = H2Src("rot90.mp4", "-display_rotation 90 -i " + Q(H2Base()) + " -c copy");
                MediaInfo mi = HuntInfo(src);
                ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "rot90 speed2.gif"), new Rectangle(0, 0, mi.Width, mi.Height), Ranges(0.2, 2.2), 2, 0, mi);
                s.ToGif = true;
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                string e = H2Try(s, mi.Duration, f, known);
                Check(e.Length == 0, e);
            });

            Test("hunt2export", "frame-rates-whole-cut-speed-0.5-2", delegate
            {
                NeedFfmpeg();
                string[][] rates = { new[] { "24", "24" }, new[] { "25", "25" }, new[] { "50", "50" },
                                     new[] { "59.94", "60000/1001" }, new[] { "120", "120" } };
                StringBuilder bad = new StringBuilder();
                foreach (string[] r in rates)
                {
                    string src = H2Fps(r[0], r[1]);
                    MediaInfo mi = HuntInfo(src);
                    double f;
                    bool known = Editor.ConstantRate(mi.Raw, out f);
                    Rectangle full = new Rectangle(0, 0, mi.Width, mi.Height);
                    List<double[]> cut = Ranges(0.2, 0.9, 1.1, 1.8);
                    double[] speeds = { 1, 0.5, 2 };
                    foreach (double sp in speeds)
                    {
                        string name = r[0] + "-speed" + HuntD(sp) + ".mp4";
                        ExportSpec s = HuntOne(src, Path.Combine(h2Dir, name), full, sp == 1 ? null : cut, sp, 0, mi);
                        string e = H2Try(s, mi.Duration, f, known);
                        if (e.Length > 0) bad.Append(r[0] + " speed " + HuntD(sp) + ": " + e + "; ");
                    }
                }
                Check(bad.Length == 0, bad.ToString());
            });

            Test("hunt2export", "h264-level-edges-whole", delegate
            {
                NeedFfmpeg();
                string[][] cases = {
                    new[] { "lv2048x1024-30", "2048x1024", "30" }, new[] { "lv2064x1024-30", "2064x1024", "30" },
                    new[] { "lv1080p60", "1920x1080", "60" }, new[] { "lv1080p120", "1920x1080", "120" },
                    new[] { "lv4096x200-30", "4096x200", "30" }, new[] { "lv16x16-30", "16x16", "30" },
                    new[] { "lv720x4096-30", "720x4096", "30" }, new[] { "lv4k60", "3840x2160", "60" } };
                StringBuilder bad = new StringBuilder();
                foreach (string[] c in cases)
                {
                    string src = H2Src(c[0] + ".mp4", "-f lavfi -i testsrc2=s=" + c[1] + ":r=" + c[2] + ":d=2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
                    MediaInfo mi = HuntInfo(src);
                    double f;
                    bool known = Editor.ConstantRate(mi.Raw, out f);
                    ExportSpec s = HuntOne(src, Path.Combine(h2Dir, c[0] + " export.mp4"), new Rectangle(0, 0, mi.Width, mi.Height), null, 1, 0, mi);
                    string e = H2Try(s, mi.Duration, f, known);
                    if (e.Length > 0) bad.Append(c[0] + ": " + e + "; ");
                }
                Check(bad.Length == 0, bad.ToString());
            });

            Test("hunt2export", "sound-51-96k-22k-mono", delegate
            {
                NeedFfmpeg();
                string s51 = H2Src("snd51.mp4", "-f lavfi -i testsrc2=s=640x360:r=25:d=2 -f lavfi -i sine=f=440:d=2,aformat=channel_layouts=5.1 " +
                                              "-map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest");
                string s96 = H2Src("snd96.mp4", "-f lavfi -i testsrc2=s=640x360:r=25:d=2 -f lavfi -i sine=f=440:d=2:sample_rate=96000 " +
                                              "-map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest");
                string s22 = H2Src("snd22mono.mp4", "-f lavfi -i testsrc2=s=640x360:r=25:d=2 -f lavfi -i sine=f=440:d=2:sample_rate=22050 " +
                                                  "-map 0:v -map 1:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -ac 1 -shortest");
                StringBuilder bad = new StringBuilder();
                string[] files = { s51, s96, s22 };
                double[] speeds = { 1, 1.5, 0.5 };
                for (int i = 0; i < files.Length; i++)
                {
                    MediaInfo mi = HuntInfo(files[i]);
                    Add(Path.GetFileName(files[i]) + " " + mi.Audio.Count + " track(s), " + (mi.Audio.Count > 0 ? mi.Audio[0].Title : ""));
                    double f;
                    bool known = Editor.ConstantRate(mi.Raw, out f);
                    ExportSpec s = HuntOne(files[i], Path.Combine(h2Dir, "sound" + i + " " + HuntD(speeds[i]) + ".mp4"), new Rectangle(0, 0, mi.Width, mi.Height), Ranges(0.3, 1.7), speeds[i], 0, mi);
                    string e = H2Try(s, mi.Duration, f, known);
                    if (e.Length > 0) bad.Append(Path.GetFileName(files[i]) + ": " + e + "; ");
                }
                Check(bad.Length == 0, bad.ToString());
            });

            Test("hunt2export", "eight-tracks-default-not-first-chosen", delegate
            {
                NeedFfmpeg();
                StringBuilder args = new StringBuilder("-f lavfi -i testsrc2=s=640x360:r=25:d=2 ");
                for (int i = 0; i < 8; i++) args.Append("-f lavfi -i sine=f=" + (300 + 100 * i) + ":d=2 ");
                args.Append("-map 0:v ");
                for (int i = 0; i < 8; i++) args.Append("-map " + (i + 1) + ":a ");
                args.Append("-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -disposition:a:0 0 -disposition:a:5 default -shortest");
                string src = H2Src("snd8.mp4", args.ToString());
                MediaInfo mi = HuntInfo(src);
                Check(mi.Audio.Count == 8, "tracks read: " + mi.Audio.Count);
                Check(mi.Audio[5].Default && !mi.Audio[0].Default, "default track read as " + H2Default(mi));
                VideoClip clip = HuntClip(src);
                Check(VideoExport.ClipTrack(clip, 9) == 5, "ClipTrack(9) gave " + VideoExport.ClipTrack(clip, 9) + ", expected the default 5");
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "snd8 default.mp4"), new Rectangle(0, 0, mi.Width, mi.Height), Ranges(0.2, 1.8), 1.5, 0, mi);
                s.AudioTrack = VideoExport.ClipTrack(clip, H2Default(mi));
                string e = H2Try(s, mi.Duration, f, known);
                Check(e.Length == 0, e);
                float[] au = H2Track(s.Output, 0);
                double t800 = EdgeTone(au, 0.1, 0.9, 800), t300 = EdgeTone(au, 0.1, 0.9, 300);
                Add("tone 800 " + HuntD(t800) + " / 300 " + HuntD(t300));
                Check(t800 > 10 * t300 && t800 > 0, "export carries track 0 (300 Hz) instead of the default 800 Hz");

                ExportSpec last = HuntOne(src, Path.Combine(h2Dir, "snd8 track7.mp4"), new Rectangle(0, 0, mi.Width, mi.Height), null, 1, 0, mi);
                last.AudioTrack = VideoExport.ClipTrack(clip, 7);
                string e2 = H2Try(last, mi.Duration, f, known);
                Check(e2.Length == 0, e2);
                float[] au7 = H2Track(last.Output, 0);
                double t1000 = EdgeTone(au7, 0.5, 1.5, 1000), t800b = EdgeTone(au7, 0.5, 1.5, 800);
                Add("tone 1000 " + HuntD(t1000) + " / 800 " + HuntD(t800b));
                Check(t1000 > 10 * t800b && t1000 > 0, "track 7 (1000 Hz) not carried: 1000 Hz " + HuntD(t1000) + ", 800 Hz " + HuntD(t800b));
            });

            Test("hunt2export", "variable-rate-cuts-and-speeds", delegate
            {
                NeedFfmpeg();
                string src = H2Src("vfr.mp4", "-f lavfi -i testsrc2=s=640x360:r=60:d=3 -f lavfi -i sine=f=660:d=4.97 -map 0:v -map 1:a " +
                                              "-vf \"setpts='if(lt(N\\,60)\\,N/60\\,1+(N-60)/30)/TB'\" -fps_mode passthrough " +
                                              "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest");
                MediaInfo mi = HuntInfo(src);
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                Add("fps " + HuntD(mi.Fps) + " constant " + known);
                Check(!known, "fixture reads as constant rate: " + HuntD(mi.Fps) + " fps");
                StringBuilder bad = new StringBuilder();
                List<List<double[]>> keeps = new List<List<double[]>> { Ranges(0.2, 1.5, 2.0, 3.5), null };
                double[] speeds = { 1, 0.5, 2 };
                foreach (List<double[]> keep in keeps)
                {
                    foreach (double sp in speeds)
                    {
                        if (keep == null && sp == 1) continue;
                        string out1 = Path.Combine(h2Dir, "vfr " + (keep == null ? "whole" : "cut") + " " + HuntD(sp) + ".mp4");
                        ExportSpec s = HuntOne(src, out1, new Rectangle(0, 0, mi.Width, mi.Height), keep, sp, 0, mi);
                        string e = H2Try(s, mi.Duration, f, known);
                        if (e.Length > 0) bad.Append(Path.GetFileName(out1) + ": " + e + "; ");
                        double kept = 0;
                        if (keep == null) kept = mi.Duration;
                        else foreach (double[] k in keep) kept += k[1] - k[0];
                        double want = Math.Round(kept / sp * VideoExport.OutFps(mi.Fps, sp));
                        int got = H2Frames(out1);
                        int tol = 2 + 2 * (keep == null ? 1 : keep.Count);
                        Add(HuntD(sp) + "x: " + got + " frames, want " + HuntD(want));
                        if (Math.Abs(got - want) > tol) bad.Append(Path.GetFileName(out1) + ": " + got + " frames, want " + HuntD(want) + "; ");
                    }
                }
                Check(bad.Length == 0, bad.ToString());
            });

            Test("hunt2export", "gif-to-mp4-and-mp4-to-gif-cut-speed", delegate
            {
                NeedFfmpeg();
                string gif = H2Src("src.gif", "-i " + Q(H2Base()) + " -vf fps=10,scale=320:-2 -loop 0");
                MediaInfo gi = HuntInfo(gif);
                ExportSpec a = HuntOne(gif, Path.Combine(h2Dir, "gif to mp4.mp4"), new Rectangle(0, 0, gi.Width, gi.Height), Ranges(0.2, 1.0), 1.5, 0, gi);
                string e1 = H2Try(a, gi.Duration, gi.Fps, false);
                MediaInfo mi = HuntInfo(H2Base());
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                ExportSpec b = HuntOne(H2Base(), Path.Combine(h2Dir, "mp4 to gif.gif"), new Rectangle(0, 0, mi.Width, mi.Height), Ranges(0.3, 1.8), 0.5, 0, mi);
                b.ToGif = true;
                string e2 = H2Try(b, mi.Duration, f, known);
                Check(e1.Length == 0 && e2.Length == 0, "gif->mp4: [" + e1 + "] mp4->gif: [" + e2 + "]");
            });

            Test("hunt2export", "tiny-pieces-below-a-frame", delegate
            {
                NeedFfmpeg();
                string[][] rates = { new[] { "24", "24" }, new[] { "59.94", "60000/1001" }, new[] { "120", "120" } };
                StringBuilder bad = new StringBuilder();
                foreach (string[] r in rates)
                {
                    string src = H2Fps(r[0], r[1]);
                    MediaInfo mi = HuntInfo(src);
                    double f;
                    bool known = Editor.ConstantRate(mi.Raw, out f);
                    foreach (double sp in new double[] { 1, 2 })
                    {
                        ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "tiny " + r[0] + " " + HuntD(sp) + ".mp4"), new Rectangle(0, 0, mi.Width, mi.Height), Ranges(1.0, 1.05), sp, 0, mi);
                        string e = H2Try(s, mi.Duration, f, known);
                        if (e.Length > 0) bad.Append(r[0] + " speed " + HuntD(sp) + ": " + e + "; ");
                    }
                }
                Check(bad.Length == 0, bad.ToString());
            });

            Test("hunt2export", "player-whole-video-rotated-and-odd", delegate
            {
                NeedFfmpeg();
                string src = H2Src("rot90.mp4", "-display_rotation 90 -i " + Q(H2Base()) + " -c copy");
                string e1 = H2Player(src, Path.Combine(h2Dir, "player rot90.mp4"));
                string odd = H2Src("odd641x361.mkv", "-f lavfi -i testsrc2=s=641x361:r=25:d=2 -c:v ffv1 -pix_fmt yuv420p");
                string e2 = H2Player(odd, Path.Combine(h2Dir, "player odd.mp4"));
                Check(e1.Length == 0 && e2.Length == 0, "rotated: [" + e1 + "] odd: [" + e2 + "]");
            });

            Test("hunt2export", "joined-rgb-with-yuv-keeps-centred-chroma", delegate
            {
                NeedFfmpeg();
                string rgb = H2Src("chroma-rgb.mkv", "-f lavfi -i testsrc2=s=320x240:r=25:d=2 -vf format=gbrp -c:v libx264rgb -preset ultrafast -qp 0");
                string yuv = H2Src("chroma-yuv.mp4", "-f lavfi -i testsrc2=s=320x240:r=25:d=2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
                VideoClip a = HuntClip(rgb), b = HuntClip(yuv);
                List<VideoClip> clips = new List<VideoClip> { a, b };
                ExportSpec single = HuntOne(rgb, Path.Combine(h2Dir, "chroma single.mp4"), new Rectangle(0, 0, 320, 240), null, 1, 0, a.Info);
                string e1 = H2Try(single, a.Duration, a.Fps, true);
                ExportSpec mixed = HuntJoin(clips, Path.Combine(h2Dir, "chroma mixed.mp4"), new Rectangle(0, 0, 320, 240), Ranges(0.0, 2.0), 1, 0, false);
                string e2 = H2Try(mixed, TotalOf(clips), a.Fps, true);
                Check(e1.Length == 0 && e2.Length == 0, "single: [" + e1 + "] mixed: [" + e2 + "]");
                byte[] sC = H2Frame(single.Output, 1.0, "center");
                byte[] mC = H2Frame(mixed.Output, 1.0, "center");
                byte[] mL = H2Frame(mixed.Output, 1.0, "left");
                double same = H2Mean(sC, mC), shifted = H2Mean(sC, mL);
                Add("mean difference single(center) vs mixed(center) " + HuntD(Math.Round(same, 3)) +
                    ", single(center) vs mixed read as left " + HuntD(Math.Round(shifted, 3)));
                Add("chroma_sample_loc_type single " + H2ChromaLoc(single.Output) + ", mixed " + H2ChromaLoc(mixed.Output));
                // Decided (VideoExport.ChromaMadeHere): a join of RGB and YUV clips writes no chroma location. Either label
                // is wrong for one kind of clip (the phone video keeps its own, implicitly "left"); the measured cost for the
                // RGB part, read as left-sited, is reported above. A single RGB export does say "centred".
                Check(H2ChromaLoc(single.Output) == 1 && H2ChromaLoc(mixed.Output) == -1, "single " + H2ChromaLoc(single.Output) + ", mixed " + H2ChromaLoc(mixed.Output));
                Check(same < 0.5, "the mixed join changed the RGB clip's pixels: " + HuntD(Math.Round(same, 3)));
            });

            Test("hunt2export", "h264-above-level-5.2-declares-a-real-level", delegate
            {
                NeedFfmpeg();
                string src = H2Src("lv4096x2304-60.mp4", "-f lavfi -i testsrc2=s=4096x2304:r=60:d=1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p");
                MediaInfo mi = HuntInfo(src);
                double f;
                bool known = Editor.ConstantRate(mi.Raw, out f);
                ExportSpec s = HuntOne(src, Path.Combine(h2Dir, "lv4096 export.mp4"), new Rectangle(0, 0, mi.Width, mi.Height), null, 1, 0, mi);
                string e = H2Try(s, mi.Duration, f, known);
                Check(e.Length == 0, e);
                int want = Editor.ExpectFor(s, mi.Duration, f, known).Level;
                int got = H2Level(s.Output);
                Add("ExpectFor level " + want + " (0: not checked); stream level_idc " + got + " = " + ShareCheck.LevelName(got));
                // Above 5.2 the frame needs level 6.0 (H.264 has had 6.0, 6.1 and 6.2 since 2016): now in the table, so the
                // export asks for it and the check verifies it.
                Check(want == 60 && got == 60, "frame " + mi.Width + "x" + mi.Height + " at " + HuntD(f) + " fps: expected level 6.0, ExpectFor " + want + ", stream " + got);
            });

            Test("hunt2export", "sources-unchanged-sha256", delegate
            {
                StringBuilder bad = new StringBuilder();
                foreach (string p in h2Made)
                {
                    string want;
                    if (!huntSources.TryGetValue(p, out want)) continue;
                    if (HuntSha(p) != want) bad.Append(Path.GetFileName(p) + " changed; ");
                }
                Add(h2Made.Count + " sources");
                Check(bad.Length == 0, bad.ToString());
            });
        }
    }
}

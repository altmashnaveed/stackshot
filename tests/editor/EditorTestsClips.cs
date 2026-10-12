// Tests of the multi-clip editor and of the export quality levels: ShareCodec, the joined export of several videos
// brought to the canvas of the first one, the clip list operations and the timeline layout that follows them.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static VideoClip ClipsOf(string path, double dur, double fps, int w, int h, bool audio, bool rgb)
        {
            VideoClip c = new VideoClip();
            c.Path = path;
            c.Duration = dur;
            c.Fps = fps;
            c.Width = w;
            c.Height = h;
            c.HasAudio = audio;
            c.Rgb = rgb;
            return c;
        }

        // A flat-colour yuv420p MP4 (optionally with a sine track), the kind of clip a phone or a screen recorder makes.
        static VideoClip ClipsColor(string dir, string name, string color, int w, int h, int fps, double dur, double hz)
        {
            string mp4 = Path.Combine(dir, name + ".mp4");
            string a = hz > 0 ? " -f lavfi -i \"sine=f=" + hz.ToString(CultureInfo.InvariantCulture) + ":sample_rate=48000:d=" + dur.ToString(CultureInfo.InvariantCulture) + "\" -c:a aac -b:a 128k -shortest" : "";
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=" + color + ":s=" + w + "x" + h + ":r=" + fps + ":d=" + dur.ToString(CultureInfo.InvariantCulture) +
                 ",format=yuv420p\"" + a + " -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(mp4), 60000);
            return ClipsOf(mp4, dur, fps, w, h, hz > 0, false);
        }

        static ExportSpec ClipsSpec(List<VideoClip> list, string output, List<double[]> keep)
        {
            ExportSpec s = new ExportSpec();
            s.Source = list[0].Path;
            s.Clips = list;
            s.Canvas = new Size(list[0].Width, list[0].Height);
            s.Output = output;
            s.Crop = new Rectangle(0, 0, list[0].Width, list[0].Height);
            s.Keep = keep;
            s.HasAudio = ClipSeq.AnyAudio(list);
            s.RgbSource = list[0].Rgb;
            s.Fps = list[0].Fps;
            s.Level = 1;
            return s;
        }

        // The colour at output time t of a file, decoded the way players decode tagged HD video.
        static int[] ClipsPixel(string file, double t, int x, int y)
        {
            string raw = file + "." + x + "x" + y + "." + t.ToString("0.00", CultureInfo.InvariantCulture) + ".raw";
            FfOk("-hide_banner -loglevel error -y -ss " + t.ToString("0.000", CultureInfo.InvariantCulture) + " -i " + Q(file) + " -vf \"crop=2:2:" + (x & ~1) + ":" + (y & ~1) +
                 ",scale=in_color_matrix=bt709:in_range=tv:flags=accurate_rnd+full_chroma_int,format=rgb24\" -frames:v 1 -f rawvideo " + Q(raw), 60000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            Check(b.Length >= 3, "no pixel decoded at " + t + " s");
            return new int[] { b[0], b[1], b[2] };
        }

        // R, G, B for a clearly red, green or blue pixel, K for black, ? otherwise.
        static char ClipsHue(int[] c)
        {
            if (c[0] < 25 && c[1] < 25 && c[2] < 25) return 'K';
            if (c[0] > 150 && c[1] < 110 && c[2] < 110) return 'R';
            if (c[1] > 150 && c[0] < 110 && c[2] < 110) return 'G';
            if (c[2] > 150 && c[0] < 110 && c[1] < 110) return 'B';
            return '?';
        }

        static void ClipsHueIs(string file, double t, int x, int y, char want)
        {
            int[] c = ClipsPixel(file, t, x, y);
            Check(ClipsHue(c) == want, "at " + t.ToString("0.00", CultureInfo.InvariantCulture) + " s (" + x + "," + y + ") expected " + want + " but got " + Rgb(c));
        }

        // The sound of a file as stereo float at 48 kHz.
        static float[] ClipsAudio(string file)
        {
            string raw = file + ".all.f32";
            FfOk("-hide_banner -loglevel error -y -i " + Q(file) + " -map 0:a:0 -vn -ac 2 -ar 48000 -f f32le " + Q(raw), 120000);
            float[] s = ShareReadF32(raw);
            File.Delete(raw);
            return s;
        }

        static double ClipsPeak(float[] s, double from, double to)
        {
            double peak = 0;
            for (long i = (long)(from * 48000); i < (long)(to * 48000) && i * 2 < s.Length; i++) peak = Math.Max(peak, Math.Abs(s[i * 2]));
            return peak;
        }

        static void ClipsExportChecked(ExportSpec s, double seconds)
        {
            ExportOk(s);
            ShareExpect e = Editor.ExpectFor(s, seconds, s.Fps, true);
            ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, e));
        }

        // The two-clip fixture: a Cine-like master 1280x720 at 30 fps with 3 sound tracks, then a 640x480 yuv420p MP4 at 60 fps
        // with no sound. Built once.
        static string clipsDir, clipsMasterA, clipsMp4B, clipsOut;
        static ExportSpec clipsFixSpec;
        static string clipsMasterStamp;

        static void ClipsFixture()
        {
            if (clipsOut != null) return;
            string dir = WorkDir("clips-fixture");
            const int w = 1280, h = 720, fps = 30, frames = 60;
            long af = ShareAudioFrames(frames, fps);
            Action<byte[], int, int, int> paint = delegate(byte[] buf, int pw, int ph, int f) { ShareRect(buf, pw, 0, 0, pw, ph, new int[] { 200, 40, 40 }); };
            string master = ShareMaster(dir, "Cine A", w, h, fps, frames, paint, ShareTone(af, 440, 0.3), ShareTone(af, 880, 0.3));
            VideoClip a = ClipsOf(master, frames / (double)fps, fps, w, h, true, true);
            VideoClip b = ClipsColor(dir, "Movil B", "0x20C040", 640, 480, 60, 1.5, 0);
            clipsMasterStamp = ShareStamp(master);
            List<VideoClip> list = new List<VideoClip> { a, b };
            ClipSeq.Layout(list);
            ExportSpec s = ClipsSpec(list, Path.Combine(dir, "Cine A (editado).mp4"), null);
            ExportOk(s);
            clipsDir = dir;
            clipsMasterA = master;
            clipsMp4B = b.Path;
            clipsFixSpec = s;
            clipsOut = s.Output;
        }

        static void ClipsSuite()
        {
            Test("clips", "sharecodec-exact-strings", delegate
            {
                // The lowest H.264 level that holds the frame (never below 4.1), and that level's High-profile ceiling, at
                // most 160 Mb/s with a buffer of at most twice that: 4.1/4.2 62.5/78.125, 5.0 160/168.75, 5.1/5.2 160/300.
                string[] crf = { "10", "14", "20" };
                Size hd = new Size(1920, 1080), uhd = new Size(3840, 2160);
                string l41 = " -maxrate 62500k -bufsize 78125k", l50 = " -maxrate 160000k -bufsize 168750k", l51 = " -maxrate 160000k -bufsize 300000k";
                for (int level = 0; level < 3; level++)
                {
                    string head = "-c:v libx264 -preset slow -crf " + crf[level] + " -profile:v high", headM = "-c:v libx264 -preset medium -crf " + crf[level] + " -profile:v high";
                    Check(VideoExport.ShareCodec(level, hd, 30) == head + " -level:v 4.1 -g 60" + l41, VideoExport.ShareCodec(level, hd, 30));
                    Check(VideoExport.ShareCodec(level, new Size(1280, 720), 60) == head + " -level:v 4.1 -g 120" + l41, VideoExport.ShareCodec(level, new Size(1280, 720), 60));
                    Check(VideoExport.ShareCodec(level, hd, 60) == head + " -level:v 4.2 -g 120" + l41, VideoExport.ShareCodec(level, hd, 60));
                    Check(VideoExport.ShareCodec(level, new Size(1920, 1200), 30) == head + " -level:v 5.0 -g 60" + l50, VideoExport.ShareCodec(level, new Size(1920, 1200), 30));
                    Check(VideoExport.ShareCodec(level, uhd, 30) == headM + " -level:v 5.1 -g 60" + l51, VideoExport.ShareCodec(level, uhd, 30));
                    Check(VideoExport.ShareCodec(level, uhd, 60) == headM + " -level:v 5.2 -g 120" + l51, VideoExport.ShareCodec(level, uhd, 60));
                    // 4K above 60 fps needs 6.0 (in H.264 since 2016); still at most 160 Mb/s.
                    Check(VideoExport.ShareCodec(level, uhd, 120) == headM + " -level:v 6.0 -g 240" + l51, VideoExport.ShareCodec(level, uhd, 120));
                }
                // The levels by frame and rate, with their edges.
                Check(VideoExport.H264Level(new Size(320, 240), 30) == 41 && VideoExport.H264Level(new Size(1888, 968), 30) == 41 && VideoExport.H264Level(new Size(1730, 786), 60) == 42, "small and Cine-like areas");
                Check(VideoExport.H264Level(hd, 30000.0 / 1001) == 41 && VideoExport.H264Level(hd, 60000.0 / 1001) == 42 && VideoExport.H264Level(new Size(1920, 1088), 30) == 41, "1080p at NTSC rates; 1088 has the same macroblocks");
                Check(VideoExport.H264Level(new Size(2048, 1024), 30) == 41 && VideoExport.H264Level(new Size(2064, 1024), 30) == 42, "8192 macroblocks is the 4.1 frame limit");
                Check(VideoExport.H264Level(new Size(2176, 1024), 30) == 42 && VideoExport.H264Level(new Size(2192, 1024), 30) == 50, "8704 macroblocks is the 4.2 frame limit");
                Check(VideoExport.H264Level(new Size(4128, 200), 30) == 42 && VideoExport.H264Level(new Size(4096, 200), 30) == 41, "a very wide frame passes the width limit of 4.1 (sqrt(8 x 8192) = 256 macroblocks)");
                Check(VideoExport.H264Level(new Size(2560, 1600), 60) == 51 && VideoExport.H264Level(new Size(2560, 1440), 30) == 50, "1440p/1600p");
                Check(VideoExport.H264Level(new Size(7680, 4320), 30) == 60 && VideoExport.H264Level(uhd, 120) == 60 && VideoExport.H264Level(new Size(7680, 4320), 60) == 61 &&
                      VideoExport.H264Level(new Size(7680, 4320), 120) == 62, "8K and 4K above 60 fps: levels 6.x");
                Check(VideoExport.H264Level(new Size(7680, 4320), 240) == 0 && VideoExport.H264Level(new Size(16384, 16384), 30) == 0, "beyond 6.2 x264 decides");
                Check(VideoExport.H264Level(hd, 0) == 42 && VideoExport.ShareCodec(0, hd, 0).Contains(" -g 120 "), "an unknown rate is taken as 60");
                Check(VideoExport.ShareCodec(1, new Size(1730, 786), 30).Contains("-preset slow") && VideoExport.ShareCodec(1, new Size(2000, 1200), 30).Contains("-preset slow") &&
                      VideoExport.ShareCodec(1, new Size(2001, 1200), 30).Contains("-preset medium"), "slow up to 2.4 MP, medium above");
                for (int level = 0; level < 3; level++) Check(!VideoExport.ShareCodec(level, uhd, 120).Contains("veryfast"), "never veryfast");
                Check(VideoExport.ShareAudioCodec(0) == "-c:a aac -b:a 320k -ar 48000" && VideoExport.ShareAudioCodec(1) == "-c:a aac -b:a 256k -ar 48000" &&
                      VideoExport.ShareAudioCodec(2) == "-c:a aac -b:a 160k -ar 48000", "audio bitrates by level");
                Check(VideoExport.ShareCodec(-5, hd, 30).Contains("-crf 10") && VideoExport.ShareCodec(9, hd, 30).Contains("-crf 20"), "out of range levels clamp");
                Add("3 quality levels x 7 sizes exact; level edges");
            });

            Test("clips", "sharecodec-accepted-by-ffmpeg", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("clips-codec");
                // The first string asks for the lowest ceiling and the last for the highest: both must be valid x264 options.
                string[] codecs = { VideoExport.ShareCodec(0, new Size(320, 240), 30), VideoExport.ShareCodec(2, new Size(3840, 2160), 60), VideoExport.ShareCodec(1, new Size(3840, 2160), 120) };
                for (int i = 0; i < codecs.Length; i++)
                    FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=320x240:r=30:d=0.5 -pix_fmt yuv420p " + codecs[i] + " -movflags +faststart " + Q(Path.Combine(dir, "c" + i + ".mp4")), 60000);
                Add("ffmpeg accepts the strings");
            });

            Test("clips", "export-args-use-quality-level", delegate
            {
                ExportSpec s = new ExportSpec();
                s.Source = @"C:\v\a.mp4";
                s.Output = @"C:\v\a (editado).mp4";
                s.Crop = new Rectangle(0, 0, 1920, 1080);
                s.HasAudio = true;
                s.Fps = 30;
                string[] crf = { "-crf 10 ", "-crf 14 ", "-crf 20 " }, ab = { "-b:a 320k", "-b:a 256k", "-b:a 160k" };
                Check(s.Level == 0, "Maxima is the default level of a spec");
                for (int level = 0; level < 3; level++)
                {
                    s.Level = level;
                    string a = VideoExport.Arguments(s);
                    Check(a.Contains(crf[level]) && a.Contains(ab[level]) && a.Contains("-profile:v high") && a.Contains("format=yuv420p") && a.Contains("-ac 2") && a.Contains("-movflags +faststart"), "level " + level + ": " + a);
                    Check(a.Contains(VideoExport.ShareCodec(level, new Size(1920, 1080), 30)), "Arguments uses ShareCodec");
                }
                s.ToGif = true;
                string g = VideoExport.Arguments(s);
                Check(!g.Contains("libx264") && !g.Contains("-crf") && !g.Contains("maxrate"), "GIF is unaffected by the quality level: " + g);
            });

            Test("clips", "quality-option-persisted", delegate
            {
                Check(Timeline.Qualities.Length == 5 && Timeline.Qualities[0] == "M\u00E1xima" && Timeline.Qualities[1] == "Alta" && Timeline.Qualities[2] == "Ligera" &&
                      Timeline.Qualities[3] == "16 MB" && Timeline.Qualities[4] == "25 MB", "the five levels");
                Check(new Timeline().Quality == 0, "a timeline starts at Maxima");
                Settings st = new Settings();
                Check(st.ExportQuality == 0, "the setting defaults to Maxima");
                Editor.RememberQuality(st, 2);
                Check(st.ExportQuality == 2, "remembered on change");
                Editor.RememberQuality(st, 4);
                Check(st.ExportQuality == 4, "25 MB remembered");
                Editor.RememberQuality(st, 2);
                Editor.RememberQuality(st, 7);
                Editor.RememberQuality(st, -1);
                Check(st.ExportQuality == 2, "out of range ignored");
                // Written and read back through the real settings file format (a private file under the work folder).
                string dir = WorkDir("clips-settings");
                string file = Path.Combine(dir, "settings.ini");
                MethodInfo write = typeof(Settings).GetMethod("Write", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(string) }, null);
                Check(write != null, "Settings.Write not found");
                write.Invoke(st, new object[] { file });
                Check(File.ReadAllText(file).Contains("ExportQuality = 2"), "the file holds ExportQuality = 2");
                Check(Settings.Load(file).ExportQuality == 2, "read back as 2");
                File.WriteAllText(file, "ExportQuality = 9\r\n");
                int bad = Settings.Load(file).ExportQuality;
                Check(bad >= 0 && bad <= 4, "an invalid value stays in range: " + bad);
            });

            Test("clips", "clip-list-locate-segments-fit", delegate
            {
                List<VideoClip> list = new List<VideoClip> { ClipsOf("a.mp4", 2, 30, 1280, 720, true, false), ClipsOf("b.mp4", 3, 60, 640, 480, false, false), ClipsOf("c.mp4", 4, 30, 1280, 720, true, false) };
                Check(Math.Abs(ClipSeq.Layout(list) - 9) < 1e-9 && list[1].Offset == 2 && list[2].Offset == 5 && list[2].End == 9, "layout");
                double l;
                Check(ClipSeq.Locate(list, 0, out l) == 0 && l == 0, "start");
                Check(ClipSeq.Locate(list, 1.9, out l) == 0 && Math.Abs(l - 1.9) < 1e-9, "inside the first");
                Check(ClipSeq.Locate(list, 2.0, out l) == 1 && l == 0, "a boundary belongs to the clip that starts there");
                Check(ClipSeq.Locate(list, 6.5, out l) == 2 && Math.Abs(l - 1.5) < 1e-9, "local time in the third");
                Check(ClipSeq.Locate(list, 9, out l) == 2 && l == 4 && ClipSeq.Locate(list, 99, out l) == 2 && l == 4, "the end and past it: the last clip, clamped");
                Check(ClipSeq.Locate(new List<VideoClip>(), 1, out l) == -1, "no clips");
                // A cut spanning the boundary between two clips: keep [0,0.6] and [1.4,3.0] over clips of 1 s.
                List<VideoClip> three = new List<VideoClip> { ClipsOf("a.mp4", 1, 25, 320, 240, true, false), ClipsOf("b.mp4", 1, 30, 640, 360, false, false), ClipsOf("c.mp4", 1, 25, 320, 240, true, false) };
                ClipSeq.Layout(three);
                List<ClipSegment> segs = ClipSeq.Segments(three, new List<double[]> { new double[] { 0, 0.6 }, new double[] { 1.4, 3.0 } });
                Check(segs.Count == 3, "three segments: " + segs.Count);
                Check(segs[0].Clip == three[0] && segs[0].A == 0 && Math.Abs(segs[0].B - 0.6) < 1e-9 && !segs[0].Whole, "first piece 0..0.6");
                Check(segs[1].Clip == three[1] && Math.Abs(segs[1].A - 0.4) < 1e-9 && Math.Abs(segs[1].B - 1.0) < 1e-9 && !segs[1].Whole, "second piece 0.4..1.0 of the middle clip");
                Check(segs[2].Clip == three[2] && segs[2].Whole, "third clip whole");
                Check(ClipSeq.Segments(three, null).Count == 3 && ClipSeq.Segments(three, null)[1].Whole, "no keep list: every clip whole");
                // A range joining two clips is cut at the boundary; a sliver of a few ms at a boundary is dropped.
                Check(ClipSeq.Segments(three, new List<double[]> { new double[] { 0.5, 1.5 } }).Count == 2, "a range over a boundary becomes two pieces");
                Check(ClipSeq.Segments(three, new List<double[]> { new double[] { 0.5, 1.005 } }).Count == 1, "a 5 ms sliver is dropped");
                // Fit: as large as fits, centred.
                Check(ClipSeq.Fit(new Size(640, 480), new Size(1280, 720)) == new Rectangle(160, 0, 960, 720), "4:3 in 16:9: " + ClipSeq.Fit(new Size(640, 480), new Size(1280, 720)));
                Check(ClipSeq.Fit(new Size(1920, 1080), new Size(1280, 720)) == new Rectangle(0, 0, 1280, 720), "same shape fills");
                Check(ClipSeq.Fit(new Size(640, 360), new Size(320, 240)) == new Rectangle(0, 30, 320, 180), "16:9 in 4:3");
                // Reorder and remove.
                Check(ClipSeq.Move(three, 2, -1) && three[1].Path == "c.mp4" && three[2].Path == "b.mp4", "move left");
                Check(!ClipSeq.Move(three, 0, -1) && !ClipSeq.Move(three, 2, 1), "cannot move past the ends");
            });

            Test("clips", "args-two-clip-graph", delegate
            {
                List<VideoClip> list = new List<VideoClip> { ClipsOf(@"C:\v\Cine A.mkv", 2, 30, 1280, 720, true, true), ClipsOf(@"C:\v\B.mp4", 1.5, 60, 640, 480, false, false) };
                ClipSeq.Layout(list);
                ExportSpec s = ClipsSpec(list, @"C:\v\Cine A (editado).mp4", null);
                string a = VideoExport.Arguments(s);
                string fit = "scale=1280:720:force_original_aspect_ratio=decrease:flags=lanczos,pad=1280:720:(ow-iw)/2:(oh-ih)/2:black,setsar=1,fps=30";
                string expected = "-hide_banner -loglevel error -nostats -progress pipe:1 -y " + Recorder.SafeInput + "-i \"C:\\v\\Cine A.mkv\" " + Recorder.SafeInput + "-i \"C:\\v\\B.mp4\"  -filter_complex \"" +
                    "[0:v:0]" + VideoExport.RgbToYuv + ",format=yuv444p," + fit + ",format=yuv444p[v0];[0:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a0];" +
                    "[1:v:0]" + fit + ",format=yuv444p[v1];anullsrc=r=48000:cl=stereo:d=1.500,aformat=sample_fmts=fltp:channel_layouts=stereo[a1];" +
                    "[v0][a0][v1][a1]concat=n=2:v=1:a=1[cv][ca];" +
                    "[cv]crop=1280:720:0:0,fps=30," + Recorder.Tags + ",format=yuv420p[o]\" -map \"[o]\" -fps_mode passthrough " + VideoExport.ShareCodec(1, new Size(1280, 720), 30) + " -map \"[ca]\" " + VideoExport.ShareAudioCodec(1) +
                    " -ac 2 -metadata comment=\"" + VideoExport.ShareComment + "\" -metadata:s:a:0 title= -movflags +faststart \"C:\\v\\Cine A (editado).mp4\"";
                // The only RGB conversion is the first clip's; the second is already YUV, so its chroma location is left as it
                // was (no out_chroma_loc). Compared without the doubled space after the last input.
                Check(a.Replace("  -filter_complex", " -filter_complex") == expected.Replace("  -filter_complex", " -filter_complex"), "got: " + a + " | expected: " + expected);
                // A cut in the middle of the second clip, and a speed change: -ss/-to per piece, concat output at the faster rate, atempo on the joined sound.
                s.Keep = new List<double[]> { new double[] { 0, 1 }, new double[] { 2.5, 3.5 } };
                s.Speed = 2;
                string b = VideoExport.Arguments(s);
                Check(b.Contains(" -to 1.000 ") && !b.Contains("-ss 0.000") && b.Contains("-ss 0.500 -to 1.500 ") && b.Contains("concat=n=2:v=1:a=1[cv][ca]") && b.Contains(",setpts=PTS/2,fps=60,") && b.Contains("[ca]atempo=2[ao]"), b);
                // GIF from joined clips: no YUV conversion, rgb24 per piece, no audio.
                s.Speed = 1;
                s.ToGif = true;
                string g = VideoExport.Arguments(s);
                Check(g.Contains("format=rgb24[v0]") && !g.Contains("yuv444p") && !g.Contains("anullsrc") && g.Contains("concat=n=2:v=1:a=0[cv];") && g.Contains("palettegen"), g);
                // The expectation of the joined export.
                s.ToGif = false;
                s.Keep = null;
                s.Speed = 1;
                ShareExpect e = Editor.ExpectFor(s, 2, 30, true);
                Check(e.Width == 1280 && e.Height == 720 && e.Fps == 30 && Math.Abs(e.Seconds - 3.5) < 1e-9 && e.HasAudio && e.RgbSource && e.Slack > 0, "expect: " + e.Width + "x" + e.Height + " " + e.Fps + " " + e.Seconds);
                s.MaxHeight = 720 / 2;
                Check(Editor.ExpectFor(s, 2, 30, true).Height == 360, "the Tamano option applies to the canvas");
                s.MaxHeight = 0;
                list[0].HasAudio = list[1].HasAudio = false;
                s.HasAudio = false;
                string n = VideoExport.Arguments(s);
                Check(!n.Contains("anullsrc") && n.Contains("concat=n=2:v=1:a=0[cv];") && n.Contains(" -an ") && !Editor.ExpectFor(s, 2, 30, true).HasAudio, "no sound anywhere: no audio at all: " + n);
            });

            Test("clips", "two-clip-export-canvas-letterbox-audio", delegate
            {
                NeedFfmpeg();
                ClipsFixture();
                ShareProbe p = ShareCheck.ParseProbe(Probe(clipsOut));
                Check(p.Width == 1280 && p.Height == 720 && Math.Abs(p.Fps - 30) < 0.01, "canvas of the first clip: " + p.Width + "x" + p.Height + " @ " + p.Fps);
                Check(p.Audio.Count == 1 && p.Audio[0].Codec == "aac" && p.Audio[0].Rate == 48000, "one AAC 48 kHz track: " + p.Audio.Count);
                double expected = 60 / 30.0 + 1.5;
                Check(Math.Abs(p.Seconds - expected) <= 1 / 30.0 + 0.05, "duration " + p.Seconds + " s, expected " + expected + " +- 1 frame");
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, clipsOut, Editor.ExpectFor(clipsFixSpec, 2, 30, true)));
                float[] au = ClipsAudio(clipsOut);
                double asecs = au.Length / 2 / 48000.0;
                Check(Math.Abs(asecs - expected) < 0.06, "audio length " + asecs.ToString("0.000", CultureInfo.InvariantCulture) + " s, expected " + expected);
                double tone = ClipsPeak(au, 0.3, 1.8), silence = ClipsPeak(au, 2.15, 3.4);
                Check(tone > 0.1 && silence < 0.005, "sound of the first clip, then silence for the one without: tone peak " + tone.ToString("0.000", CultureInfo.InvariantCulture) + ", silence peak " + silence.ToString("0.0000", CultureInfo.InvariantCulture));
                // First clip: red over the whole canvas. Second clip 640x480 (4:3) in 1280x720: black bars 160 px wide, green in the middle.
                ClipsHueIs(clipsOut, 1.0, 640, 360, 'R');
                ClipsHueIs(clipsOut, 1.0, 20, 20, 'R');
                ClipsHueIs(clipsOut, 2.75, 640, 360, 'G');
                ClipsHueIs(clipsOut, 2.75, 40, 360, 'K');
                ClipsHueIs(clipsOut, 2.75, 150, 360, 'K');
                ClipsHueIs(clipsOut, 2.75, 1130, 360, 'K');
                ClipsHueIs(clipsOut, 2.75, 1240, 20, 'K');
                ClipsHueIs(clipsOut, 2.75, 175, 360, 'G');
                ClipsHueIs(clipsOut, 2.75, 1100, 700, 'G');
                // Measured on a decoded frame: the bar is exactly the fitted rectangle's margin.
                string raw = clipsOut + ".row.raw";
                FfOk("-hide_banner -loglevel error -y -ss 2.75 -i " + Q(clipsOut) + " -vf \"crop=1280:2:0:360,scale=in_color_matrix=bt709:in_range=tv:flags=accurate_rnd+full_chroma_int,format=rgb24\" -frames:v 1 -f rawvideo " + Q(raw), 60000);
                byte[] row = File.ReadAllBytes(raw);
                File.Delete(raw);
                int left = 0, right = 0;
                while (left < 1280 && row[left * 3 + 1] < 40) left++;
                while (right < 1280 && row[(1279 - right) * 3 + 1] < 40) right++;
                Check(Math.Abs(left - 160) <= 2 && Math.Abs(right - 160) <= 2, "black bars measured " + left + " and " + right + " px, expected 160");
                Add("1280x720@30, " + p.Seconds.ToString("0.000", CultureInfo.InvariantCulture) + " s, audio " + asecs.ToString("0.000", CultureInfo.InvariantCulture) + " s, bars " + left + "/" + right + " px");
            });

            Test("clips", "two-clip-export-sources-untouched", delegate
            {
                NeedFfmpeg();
                ClipsFixture();
                Check(ShareStamp(clipsMasterA) == clipsMasterStamp, "the master changed during the export");
                Check(File.Exists(clipsMp4B) && new FileInfo(clipsMp4B).Length > 1000, "the second source is intact");
                Check(clipsOut != clipsMasterA && clipsOut != clipsMp4B && Path.GetFileName(clipsOut) == "Cine A (editado).mp4", "a new file named after the first clip");
            });

            Test("clips", "three-clips-cut-spanning-a-boundary", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("clips-three");
                VideoClip a = ClipsColor(dir, "rojo", "red", 320, 240, 25, 1.0, 440);
                VideoClip b = ClipsColor(dir, "verde", "0x00C000", 640, 360, 30, 1.0, 0);
                VideoClip c = ClipsColor(dir, "azul", "blue", 320, 240, 25, 1.0, 660);
                List<VideoClip> list = new List<VideoClip> { a, b, c };
                ClipSeq.Layout(list);
                // 0.6..1.4 removed: it covers the end of the first clip and the start of the second.
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "rojo (editado).mp4"), new List<double[]> { new double[] { 0, 0.6 }, new double[] { 1.4, 3.0 } });
                string args = VideoExport.Arguments(s);
                Check(args.Contains(" -to 0.600 ") && !args.Contains("-ss 0.000") && args.Contains("-ss 0.400 -to 1.000 ") && args.Contains("concat=n=3"), args);
                ClipsExportChecked(s, 2.2);
                ShareProbe p = ShareCheck.ParseProbe(Probe(s.Output));
                Check(p.Width == 320 && p.Height == 240 && Math.Abs(p.Fps - 25) < 0.01 && Math.Abs(p.Seconds - 2.2) < 0.1, p.Width + "x" + p.Height + " " + p.Fps + " " + p.Seconds + " s");
                ClipsHueIs(s.Output, 0.3, 160, 120, 'R');
                ClipsHueIs(s.Output, 0.9, 160, 120, 'G');
                ClipsHueIs(s.Output, 0.9, 160, 5, 'K');
                ClipsHueIs(s.Output, 0.9, 160, 235, 'K');
                ClipsHueIs(s.Output, 1.6, 160, 120, 'B');
                float[] au = ClipsAudio(s.Output);
                Check(Math.Abs(au.Length / 2 / 48000.0 - 2.2) < 0.06, "audio length " + (au.Length / 2 / 48000.0));
                Check(ClipsPeak(au, 0.1, 0.5) > 0.05 && ClipsPeak(au, 0.75, 1.05) < 0.01 && ClipsPeak(au, 1.4, 2.1) > 0.05, "tone, silence for the clip with no sound, tone");
                Add("2.2 s from 3 clips with a cut across the first boundary");
            });

            Test("clips", "other-containers-thumbnail-and-join", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("clips-containers");
                VideoClip a = ClipsColor(dir, "rojo", "red", 320, 240, 25, 1.0, 440);
                string[][] made = {
                    new string[] { "verde.ts", "0x00C000", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", "G" },
                    new string[] { "azul.wmv", "blue", "-c:v wmv2 -b:v 2M -c:a wmav2", "B" },
                    new string[] { "rojo.flv", "red", "-c:v flv1 -b:v 2M -c:a libmp3lame -ar 44100", "R" },
                    new string[] { "verde.mpg", "0x00C000", "-c:v mpeg2video -b:v 4M -c:a mp2", "G" } };
                List<VideoClip> list = new List<VideoClip> { a };
                foreach (string[] m in made)
                {
                    string f = Path.Combine(dir, m[0]);
                    FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=" + m[1] + ":s=320x240:r=25:d=1,format=yuv420p\" -f lavfi -i \"sine=f=660:sample_rate=48000:d=1\" " +
                         m[2] + " -shortest " + Q(f), 60000);
                    MediaInfo mi = MediaInfo.Parse(Probe(f));
                    Check(mi.CanPlay, m[0] + " not playable");
                    // A thumbnail of the strip and the editor's first frame (no seek at 0).
                    string png = f + ".png";
                    FfOk(FrameStrip.Arguments(f, 0.5, 60, png, true), 30000);
                    Check(File.Exists(png) && new FileInfo(png).Length > 100, m[0] + " gave no thumbnail");
                    FfOk("-hide_banner -loglevel error -y " + PlayerArgs.Seek(0) + Recorder.SafeInput + "-i " + Q(f) + " -frames:v 1 " + Q(f + ".first.png"), 30000);
                    Check(File.Exists(f + ".first.png"), m[0] + " gave no first frame");
                    VideoClip c = ClipsOf(f, mi.Duration, mi.Fps, mi.Width, mi.Height, mi.Audio.Count > 0, false);
                    c.TakeColour(mi);
                    list.Add(c);
                }
                ClipSeq.Layout(list);
                double total = 0;
                foreach (VideoClip c in list) total += c.Duration;
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "rojo (editado).mp4"), null);
                ClipsExportChecked(s, total);
                double at = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    char want = i == 0 ? 'R' : made[i - 1][3][0];
                    ClipsHueIs(s.Output, at + list[i].Duration / 2, 160, 120, want);
                    at += list[i].Duration;
                }
                MfResult r = MfDecode(s.Output);
                Check(r.Error == null && r.VideoFrames > 0, "Windows could not decode the joined MP4: " + r.Error);
                Add("mp4 + ts + wmv + flv + mpg joined into one MP4 of " + total.ToString("0.00", CultureInfo.InvariantCulture) + " s; " + r);
            });

            Test("clips", "mpeg-ts-and-ps-seek-exactly", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("clips-noindex");
                foreach (string name in new string[] { "gop.ts", "gop.mpg", "gop.m2ts" })
                {
                    // One keyframe a second: a plain seek would start up to 25 frames late.
                    string f = Path.Combine(dir, name);
                    string codec = name.EndsWith(".mpg") ? "-c:v mpeg2video -b:v 4M -c:a mp2" : "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac" + (name.EndsWith(".m2ts") ? " -f mpegts" : "");
                    FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x240:r=25:d=5,format=yuv420p\" -f lavfi -i \"sine=f=440:sample_rate=48000:d=5\" " +
                         codec + " -g 25 -shortest " + Q(f), 60000);
                    string refMd5 = Path.Combine(dir, name + ".ref.txt");
                    FfOk("-hide_banner -loglevel error -y -i " + Q(f) + " -map 0:v:0 -pix_fmt bgra -f framemd5 " + Q(refMd5), 60000);
                    List<string> all = new List<string>();
                    foreach (string line in File.ReadAllLines(refMd5)) if (!line.StartsWith("#")) all.Add(line.Substring(line.LastIndexOf(',') + 1).Trim());
                    foreach (double t in new double[] { 1.3, 2.2, 3.5, 4.1 })
                    {
                        string outMd5 = Path.Combine(dir, name + ".still.txt");
                        string a = PlayerArgs.Still(f, t, 320, 240, 320, 240, 0).Replace(" -f rawvideo -pix_fmt bgra -", " -pix_fmt bgra -f framemd5 " + Q(outMd5));
                        FfOk("-y " + a, 30000);
                        string got = null;
                        foreach (string line in File.ReadAllLines(outMd5)) if (!line.StartsWith("#")) got = line.Substring(line.LastIndexOf(',') + 1).Trim();
                        int idx = all.IndexOf(got);
                        int want = (int)Math.Round(t * 25);
                        Check(idx >= 0 && Math.Abs(idx - want) <= 1, name + " at " + t + " s showed frame " + idx + ", expected " + want);
                    }
                    // A trimmed piece and two pieces of the same file, exported.
                    VideoClip c = ClipsOf(f, 5, 25, 320, 240, true, false);
                    c.TakeColour(MediaInfo.Parse(Probe(f)));
                    List<VideoClip> one = new List<VideoClip> { c };
                    ExportSpec s1 = ClipsSpec(one, Path.Combine(dir, name + " uno.mp4"), new List<double[]> { new double[] { 1.3, 2.6 } });
                    ClipsExportChecked(s1, 1.3);
                    ExportSpec s2 = ClipsSpec(one, Path.Combine(dir, name + " dos.mp4"), new List<double[]> { new double[] { 1.3, 2.2 }, new double[] { 3.5, 4.3 } });
                    ClipsExportChecked(s2, 1.7);
                }
                // A trimmed TS after another clip.
                VideoClip red = ClipsColor(dir, "rojo", "red", 320, 240, 25, 1.0, 440);
                VideoClip ts = ClipsOf(Path.Combine(dir, "gop.ts"), 5, 25, 320, 240, true, false);
                ts.TakeColour(MediaInfo.Parse(Probe(ts.Path)));
                List<VideoClip> two = new List<VideoClip> { red, ts };
                ClipSeq.Layout(two);
                ExportSpec s3 = ClipsSpec(two, Path.Combine(dir, "rojo (editado).mp4"), new List<double[]> { new double[] { 0, 1.0 }, new double[] { 2.3, 6.0 } });
                ClipsExportChecked(s3, 4.7);
                Add("ts, mpg and m2ts: stills within a frame at 1.3/2.2/3.5/4.1 s, trimmed exports pass the check");
            });

            Test("clips", "reorder-and-remove-change-the-export", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("clips-order");
                VideoClip r = ClipsColor(dir, "r", "red", 320, 240, 25, 1.0, 0);
                VideoClip g = ClipsColor(dir, "g", "0x00C000", 320, 240, 25, 1.0, 0);
                VideoClip b = ClipsColor(dir, "b", "blue", 320, 240, 25, 1.0, 0);
                // Original order red, green, blue.
                List<VideoClip> list = new List<VideoClip> { r, g, b };
                ClipSeq.Layout(list);
                ExportSpec s0 = ClipsSpec(list, Path.Combine(dir, "orden0.mp4"), null);
                ClipsExportChecked(s0, 3.0);
                ClipsHueIs(s0.Output, 0.4, 160, 120, 'R');
                ClipsHueIs(s0.Output, 1.4, 160, 120, 'G');
                ClipsHueIs(s0.Output, 2.4, 160, 120, 'B');
                // Blue moved left: red, blue, green.
                List<VideoClip> moved = new List<VideoClip>(list);
                Check(ClipSeq.Move(moved, 2, -1), "move");
                ClipSeq.Layout(moved);
                ExportSpec s1 = ClipsSpec(moved, Path.Combine(dir, "orden1.mp4"), null);
                ClipsExportChecked(s1, 3.0);
                ClipsHueIs(s1.Output, 0.4, 160, 120, 'R');
                ClipsHueIs(s1.Output, 1.4, 160, 120, 'B');
                ClipsHueIs(s1.Output, 2.4, 160, 120, 'G');
                // Green moved to the front: it is now the first clip and the first frame.
                List<VideoClip> front = new List<VideoClip>(list);
                Check(ClipSeq.Move(front, 1, -1), "move to the front");
                ClipSeq.Layout(front);
                ExportSpec s2 = ClipsSpec(front, Path.Combine(dir, "orden2.mp4"), null);
                ClipsExportChecked(s2, 3.0);
                ClipsHueIs(s2.Output, 0.1, 160, 120, 'G');
                ClipsHueIs(s2.Output, 1.4, 160, 120, 'R');
                // Green removed: red then blue, two seconds.
                List<VideoClip> removed = new List<VideoClip>(list);
                removed.RemoveAt(1);
                ClipSeq.Layout(removed);
                ExportSpec s3 = ClipsSpec(removed, Path.Combine(dir, "orden3.mp4"), null);
                ClipsExportChecked(s3, 2.0);
                ClipsHueIs(s3.Output, 0.4, 160, 120, 'R');
                ClipsHueIs(s3.Output, 1.4, 160, 120, 'B');
                Check(Math.Abs(ShareCheck.ParseProbe(Probe(s3.Output)).Seconds - 2.0) < 0.1, "two clips, two seconds");
                Add("order and removal reflected in the first frame of each piece");
            });

            Test("clips", "windows-decodes-the-multi-clip-mp4", delegate
            {
                NeedFfmpeg();
                ClipsFixture();
                MfResult r = MfDecode(clipsOut);
                Add(r.ToString());
                Check(r.Error == null, "Media Foundation: " + r.Error);
                Check(Math.Abs(r.VideoFrames - 105) <= 2, "video frames decoded by Windows: " + r.VideoFrames + " (expected 105)");
                Check(Math.Abs(r.AudioSeconds - 3.5) < 0.08, "audio decoded by Windows: " + r.AudioSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Check(r.Width == 1280 && r.Height == 720, "size " + r.Width + "x" + r.Height);
            });

            Test("clips", "timeline-extend-keeps-the-trim-as-a-cut", delegate
            {
                Timeline tl = new Timeline();
                List<VideoClip> list = new List<VideoClip> { ClipsOf("a.mp4", 2, 30, 640, 360, true, false) };
                ClipSeq.Layout(list);
                tl.Clips = list;
                tl.Init(2);
                // Not trimmed: the new clip is simply appended and the handles cover everything.
                list.Add(ClipsOf("b.mp4", 3, 30, 640, 360, false, false));
                tl.Extend(ClipSeq.Layout(list));
                Check(tl.Duration == 5 && tl.Out == 5 && tl.Pieces.Count == 2 && tl.Pieces[1].A == 2 && tl.Pieces[1].B == 5, "appended as a piece of its own");
                // Trimmed at the end (Out 4 of 5) and split at 3, then a third clip is appended.
                tl.Out = 4;
                tl.Playhead = 3;
                tl.SplitAtPlayhead();
                list.Add(ClipsOf("c.mp4", 4, 30, 640, 360, true, false));
                tl.Extend(ClipSeq.Layout(list));
                Check(tl.Duration == 9 && tl.Out == 9, "the new clip is inside the handles: " + tl.Out);
                List<double[]> keep = tl.Keep();
                Check(keep.Count == 2 && keep[0][0] == 0 && Math.Abs(keep[0][1] - 4) < 1e-9 && Math.Abs(keep[1][0] - 5) < 1e-9 && keep[1][1] == 9, "what the old end trim left out (4..5) is now a cut: " + KeepText(keep));
                Check(tl.HasCuts && !tl.IsTrimmed, "a cut, not a trim");
                // Removing the second clip: its pieces (and the cut) go with it and the third clip moves up.
                double start = list[1].Offset, len = list[1].Duration;
                list.RemoveAt(1);
                tl.Relayout(ClipSeq.RemovedMap(start, len), ClipSeq.Layout(list));
                keep = tl.Keep();
                Check(tl.Duration == 6 && tl.Out == 6 && keep.Count == 1 && keep[0][0] == 0 && keep[0][1] == 6 && !tl.HasCuts, "after removing the middle clip: " + KeepText(keep) + " of " + tl.Duration);
            });

            Test("clips", "timeline-relayout-follows-a-moved-clip", delegate
            {
                Timeline tl = new Timeline();
                List<VideoClip> list = new List<VideoClip> { ClipsOf("a.mp4", 2, 30, 640, 360, true, false) };
                ClipSeq.Layout(list);
                tl.Clips = list;
                tl.Init(2);
                list.Add(ClipsOf("b.mp4", 3, 30, 640, 360, true, false));
                tl.Extend(ClipSeq.Layout(list));
                list.Add(ClipsOf("c.mp4", 4, 30, 640, 360, true, false));
                tl.Extend(ClipSeq.Layout(list));
                // Cut 2.5..3.5 inside b (2..5); then move c to the middle.
                tl.Playhead = 2.5;
                tl.SplitAtPlayhead();
                tl.Playhead = 3.5;
                tl.SplitAtPlayhead();
                tl.Playhead = 3.0;
                tl.ToggleCut();
                Check(tl.HasCuts, "a cut in b");
                List<VideoClip> before = new List<VideoClip>(list);
                double[] oldOff = ClipSeq.Offsets(before);
                ClipSeq.Move(list, 2, -1);
                tl.Relayout(ClipSeq.MovedMap(before, oldOff), ClipSeq.Layout(list));
                // Order is now a (0..2), c (2..6), b (6..9): the cut of b is at 6.5..7.5.
                Check(list[1].Path == "c.mp4" && list[1].Offset == 2 && list[2].Offset == 6, "new layout");
                List<double[]> keep = tl.Keep();
                Check(keep.Count == 2 && keep[0][0] == 0 && Math.Abs(keep[0][1] - 6.5) < 1e-9 && Math.Abs(keep[1][0] - 7.5) < 1e-9 && keep[1][1] == 9, "the cut moved with b: " + KeepText(keep));
                Check(tl.Duration == 9 && tl.Out == 9 && tl.In == 0, "handles still cover all");
                for (int i = 1; i < tl.Pieces.Count; i++) Check(Math.Abs(tl.Pieces[i].A - tl.Pieces[i - 1].B) < 1e-9, "pieces still tile the timeline");
                // The playback skip logic follows: inside the cut, playback continues at its end.
                Check(Math.Abs(tl.PlayableFrom(7.0) - 7.5) < 1e-9 && tl.PlayableFrom(6.2) == 6.2, "PlayableFrom skips the cut");
            });

            Test("clips", "timeline-paints-clips-boundaries-and-quality", delegate
            {
                // Paints the header and strip of three clips to a bitmap (no window is shown) at a wide, a medium and a narrow width.
                string outDir = Environment.GetEnvironmentVariable("STACKSHOT_CLIPS_PNG");
                foreach (int width in new int[] { 1300, 1000, 760 })
                {
                    Timeline tl = new Timeline();
                    List<VideoClip> list = new List<VideoClip> { ClipsOf("Cine 2026-10-10.mkv", 12, 30, 1280, 720, true, true), ClipsOf("Movil B.mp4", 6, 60, 640, 480, false, false), ClipsOf("c.mp4", 3, 30, 1280, 720, true, false) };
                    double total = ClipSeq.Layout(list);
                    for (int k = 0; k < list.Count; k++)
                    {
                        list[k].Thumbs = new Bitmap[6];
                        for (int i = 0; i < 6; i++)
                        {
                            Bitmap t = new Bitmap(96, 54);
                            using (Graphics g = Graphics.FromImage(t)) g.Clear(Color.FromArgb(60 + 40 * k, 90 + 20 * i, 160 - 30 * k));
                            list[k].Thumbs[i] = t;
                        }
                    }
                    tl.Clips = list;
                    tl.Init(list[0].Duration);
                    tl.Extend(list[0].Duration + list[1].Duration);
                    tl.Extend(total);
                    tl.CanAddClips = true;
                    tl.CanPlay = true;
                    tl.HasSound = true;
                    tl.Quality = 1;
                    tl.Playhead = 5;
                    tl.SetBounds(0, 0, width, 110);
                    using (Bitmap bmp = new Bitmap(width, 110))
                    {
                        tl.DrawToBitmap(bmp, new Rectangle(0, 0, width, 110));
                        // Not blank: the strip has the thumbnail colours and the header has text.
                        int different = 0;
                        for (int x = 0; x < width; x += 7)
                            for (int y = 0; y < 110; y += 5)
                                if (bmp.GetPixel(x, y).ToArgb() != bmp.GetPixel(0, 1).ToArgb()) different++;
                        Check(different > 300, "the timeline painted almost nothing (" + different + " different samples)");
                        if (!string.IsNullOrEmpty(outDir)) bmp.Save(Path.Combine(outDir, "timeline-" + width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    foreach (VideoClip c in list) foreach (Bitmap t in c.Thumbs) t.Dispose();
                    tl.Dispose();
                }
            });

            Test("clips", "frame-of-another-clip-is-fitted-on-the-canvas", delegate
            {
                using (Bitmap src = new Bitmap(640, 480))
                {
                    using (Graphics g = Graphics.FromImage(src)) g.Clear(Color.FromArgb(220, 30, 30));
                    using (Bitmap r = Editor.OnCanvas(src, new Size(1280, 720)))
                    {
                        Check(r.Width == 1280 && r.Height == 720, "canvas size");
                        Check(r.GetPixel(20, 360).ToArgb() == Color.Black.ToArgb() && r.GetPixel(1260, 360).ToArgb() == Color.Black.ToArgb(), "black bars at the sides");
                        Color c = r.GetPixel(640, 360), top = r.GetPixel(640, 2);
                        Check(c.R > 200 && c.G < 50 && top.R > 200, "the picture fills the height");
                        Check(r.GetPixel(170, 360).R > 200 && r.GetPixel(150, 360).R < 10, "the picture starts at x = 160");
                    }
                }
            });

            EdgeSuite();
        }

        static string KeepText(List<double[]> keep)
        {
            StringBuilder sb = new StringBuilder();
            foreach (double[] k in keep) sb.Append(sb.Length == 0 ? "" : " ").Append("[").Append(k[0].ToString("0.###", CultureInfo.InvariantCulture)).Append(",").Append(k[1].ToString("0.###", CultureInfo.InvariantCulture)).Append("]");
            return sb.ToString();
        }
    }
}

// Tests of the MP4 made to share from a Cine master: arguments, colour, validation and the master left untouched.
// The masters are built through the real Cine path (CineSink, Cine.LiveArgs, CineMaster.Build), never by hand.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        sealed class SharePatch
        {
            public string Name;
            public int X, Y;
            public int[] Rgb;
        }

        static SharePatch SharePatchAt(string name, int x, int y, int r, int g, int b)
        {
            SharePatch p = new SharePatch();
            p.Name = name;
            p.X = x;
            p.Y = y;
            p.Rgb = new int[] { r, g, b };
            return p;
        }

        // Flat UI-like colours, each 60x80 px; the centre of every patch is what the tests read back.
        static readonly SharePatch[] SharePatches = {
            SharePatchAt("rojo", 46, 60, 255, 0, 0), SharePatchAt("verde", 114, 60, 0, 255, 0), SharePatchAt("azul", 182, 60, 0, 0, 255),
            SharePatchAt("acento", 250, 60, 10, 132, 255), SharePatchAt("blanco", 318, 60, 255, 255, 255), SharePatchAt("negro", 386, 60, 0, 0, 0),
            SharePatchAt("gris64", 454, 60, 64, 64, 64), SharePatchAt("gris128", 522, 60, 128, 128, 128), SharePatchAt("gris192", 590, 60, 192, 192, 192),
            SharePatchAt("fondo", 600, 340, 24, 24, 28) };

        const int ShareRampX = 64, ShareRampY0 = 120, ShareRampY1 = 160, ShareLineY = 200;

        static void SharePx(byte[] buf, int w, int x, int y, int r, int g, int b)
        {
            int i = (y * w + x) * 4;
            buf[i] = (byte)b;
            buf[i + 1] = (byte)g;
            buf[i + 2] = (byte)r;
            buf[i + 3] = 0;
        }

        static void ShareRect(byte[] buf, int w, int x0, int y0, int rw, int rh, int[] c)
        {
            for (int y = y0; y < y0 + rh; y++)
                for (int x = x0; x < x0 + rw; x++) SharePx(buf, w, x, y, c[0], c[1], c[2]);
        }

        static ulong ShareCell(int gx, int gy)
        {
            unchecked
            {
                ulong v = (ulong)(uint)(gx * 73856093) ^ ((ulong)(uint)(gy * 19349663) << 17);
                v ^= v >> 33;
                v *= 0xFF51AFD7ED558CCDUL;
                v ^= v >> 33;
                v *= 0xC4CEB9FE1A85EC53UL;
                v ^= v >> 33;
                return v;
            }
        }

        static readonly int[][] ShareSyntax = {
            new[] { 255, 85, 85 }, new[] { 80, 250, 123 }, new[] { 139, 233, 253 }, new[] { 255, 121, 198 },
            new[] { 241, 250, 140 }, new[] { 189, 147, 249 }, new[] { 255, 184, 108 }, new[] { 220, 220, 220 } };

        // Small 5x7 glyph cells on a 6x10 grid, in syntax colours, over the region x0..x0+rw, y0..y0+rh; scrolls with the frame.
        static void ShareText(byte[] buf, int w, int x0, int y0, int rw, int rh, int frame)
        {
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    int sx = x + frame * 2, gx = sx / 6, px = sx % 6, gy = y / 10, py = y % 10;
                    int r = 24, g = 24, b = 28;
                    if (px < 5 && py < 7)
                    {
                        ulong bits = ShareCell(gx, gy);
                        if (((bits >> (py * 5 + px)) & 1) != 0)
                        {
                            int[] c = ShareSyntax[(int)((bits >> 40) & 7)];
                            r = c[0]; g = c[1]; b = c[2];
                        }
                    }
                    SharePx(buf, w, x0 + x, y0 + y, r, g, b);
                }
        }

        // The colour test card: patches, a 0..255 gray ramp, 1 px lines and small text on the UI dark background.
        static void ShareCard(byte[] buf, int w, int h, int frame)
        {
            ShareRect(buf, w, 0, 0, w, h, new int[] { 24, 24, 28 });
            foreach (SharePatch p in SharePatches)
                if (p.Name != "fondo") ShareRect(buf, w, p.X - 30, p.Y - 40, 60, 80, p.Rgb);
            for (int y = ShareRampY0; y < ShareRampY1; y++)
                for (int v = 0; v < 256; v++) SharePx(buf, w, ShareRampX + v, y, v, v, v);
            for (int x = 16; x < 300; x++) { SharePx(buf, w, x, ShareLineY, 255, 255, 255); SharePx(buf, w, x, ShareLineY + 10, 255, 0, 0); }
            for (int y = 180; y < 260; y++) SharePx(buf, w, 330, y, 255, 255, 255);
            ShareText(buf, w, 16, 240, 400, 100, 0);
        }

        // Scrolling text over the whole frame: lots of edges, the kind of content that stresses the encoder.
        static void ShareScreen(byte[] buf, int w, int h, int frame)
        {
            ShareText(buf, w, 0, 0, w, h, frame);
        }

        static float[] ShareSilence(long audioFrames) { return new float[audioFrames * 2]; }

        static float[] ShareTone(long audioFrames, double hz, double amp)
        {
            float[] s = new float[audioFrames * 2];
            for (long i = 0; i < audioFrames; i++)
                s[i * 2] = s[i * 2 + 1] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / 48000.0));
            return s;
        }

        static void ShareWriteF32(string path, float[] s)
        {
            byte[] b = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, b, 0, b.Length);
            File.WriteAllBytes(path, b);
        }

        static float[] ShareReadF32(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            float[] s = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, s, 0, s.Length * 4);
            return s;
        }

        // A real Cine master: frames through CineSink with Cine.LiveArgs, sound files through CineMaster.Build (a mix
        // track plus both originals when both are given).
        static string ShareMaster(string dir, string name, int w, int h, int fps, int frames, Action<byte[], int, int, int> paint, float[] sys, float[] mic)
        {
            string nut = Path.Combine(dir, name + ".nut"), mkv = Path.Combine(dir, name + ".mkv");
            int bytes = w * h * 4;
            byte[] managed = new byte[bytes];
            IntPtr unmanaged = Marshal.AllocHGlobal(bytes);
            CineSink sink = new CineSink(ffmpeg, w, h, fps, 1, nut);
            try
            {
                sink.Start();
                for (int f = 0; f < frames; f++)
                {
                    paint(managed, w, h, f);
                    Marshal.Copy(managed, 0, unmanaged, bytes);
                    if (!sink.Write(unmanaged)) throw new TestFailed("Write failed at frame " + f + ": " + sink.ErrorTail);
                }
                int code;
                if (!sink.Close(120000, out code)) throw new TestFailed("ffmpeg did not finish: " + sink.ErrorTail);
                if (code != 0) throw new TestFailed("ffmpeg exited with " + code + ": " + sink.ErrorTail);
            }
            finally
            {
                sink.Dispose();
                Marshal.FreeHGlobal(unmanaged);
            }
            string sysPath = null, micPath = null;
            if (sys != null) { sysPath = Path.Combine(dir, name + "-equipo.f32"); ShareWriteF32(sysPath, sys); }
            if (mic != null) { micPath = Path.Combine(dir, name + "-micro.f32"); ShareWriteF32(micPath, mic); }
            string err = CineMaster.Build(ffmpeg, nut, sysPath, micPath, "", mkv, frames / (double)fps, null);
            if (err != null) throw new TestFailed("CineMaster.Build: " + err);
            return mkv;
        }

        static long ShareAudioFrames(int frames, int fps) { return Cine.AudioFramesFor(frames, fps, 1); }

        static string ShareStamp(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "") + " " + File.GetLastWriteTimeUtc(path).Ticks;
        }

        // What the editor builds for a master: the Cine share profile, source size and fps, converted with the BT.709 chain.
        static ExportSpec ShareSpec(string master, string output, int cropW, int cropH, int fps, bool audio)
        {
            ExportSpec s = new ExportSpec();
            s.Source = master;
            s.Output = output;
            s.Crop = new Rectangle(0, 0, cropW, cropH);
            s.RgbSource = true;
            s.HasAudio = audio;
            s.Level = 0;
            s.Fps = fps;
            return s;
        }

        static ShareExpect ShareExpectOf(ExportSpec s, double seconds)
        {
            ShareExpect e = new ShareExpect();
            e.Width = s.Crop.Width;
            e.Height = s.Crop.Height;
            e.Fps = s.Fps;
            e.Seconds = seconds;
            e.HasAudio = s.HasAudio;
            e.RgbSource = s.RgbSource;
            return e;
        }

        // Exports with the real arguments and requires that the master is byte-identical afterwards.
        static void ShareExport(ExportSpec s)
        {
            string before = ShareStamp(s.Source);
            ExportOk(s);
            Check(ShareStamp(s.Source) == before, "the master changed during the export");
        }

        static void ShareRequirePassed(ShareReport r)
        {
            Check(r.Passed, r.Summary + " | " + r.ToString().Replace("\n", " | "));
        }

        // Every frame of the MP4 as RGB, decoded the way players decode tagged HD video (or with another matrix).
        static byte[] ShareDecodeRgb(string mp4, string matrix, int w, int h)
        {
            string raw = mp4 + "." + matrix + ".rgb";
            FfOk("-hide_banner -loglevel error -y -i " + Q(mp4) + " -map 0:v:0 -an -fps_mode passthrough -vf \"scale=in_color_matrix=" + matrix +
                 ":in_range=tv:flags=accurate_rnd+full_chroma_int,format=rgb24\" -f rawvideo " + Q(raw), 120000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            Check(b.Length > 0 && b.Length % (w * h * 3) == 0, "decoded " + b.Length + " bytes, not whole " + w + "x" + h + " frames");
            return b;
        }

        static int[] ShareRgbAt(byte[] rgb, int w, int h, int frame, int x, int y)
        {
            int i = frame * w * h * 3 + (y * w + x) * 3;
            return new int[] { rgb[i], rgb[i + 1], rgb[i + 2] };
        }

        // First frame whose mean luma passes 128, or -1.
        static int ShareFlashFrame(string video, int w, int h)
        {
            string raw = video + ".gray";
            FfOk("-hide_banner -loglevel error -y -i " + Q(video) + " -map 0:v:0 -an -fps_mode passthrough -vf format=gray -f rawvideo " + Q(raw), 120000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            int frames = b.Length / (w * h);
            for (int f = 0; f < frames; f++)
            {
                long sum = 0;
                for (int i = f * w * h; i < (f + 1) * w * h; i++) sum += b[i];
                if (sum / (double)(w * h) > 128) return f;
            }
            return -1;
        }

        // Seconds of the first audio sample above 0.1 in track k of the file, or -1.
        static double ShareClickTime(string file, int k)
        {
            string raw = file + ".a" + k + ".f32";
            FfOk("-hide_banner -loglevel error -y -i " + Q(file) + " -map 0:a:" + k + " -vn -ac 2 -ar 48000 -f f32le " + Q(raw), 120000);
            float[] s = ShareReadF32(raw);
            File.Delete(raw);
            for (int i = 0; i < s.Length; i += 2)
                if (Math.Abs(s[i]) > 0.1f) return (i / 2) / 48000.0;
            return -1;
        }

        // Power of one frequency in the left channel (a single-bin DFT), normalised so a sine of amplitude a gives a*a/4.
        static double ShareTonePower(float[] s, long from, long count, double hz)
        {
            double re = 0, im = 0;
            for (long i = 0; i < count; i++)
            {
                double x = s[(from + i) * 2], w = 2 * Math.PI * hz * i / 48000.0;
                re += x * Math.Cos(w);
                im -= x * Math.Sin(w);
            }
            return (re * re + im * im) / ((double)count * count);
        }

        static string ShareFailing(ShareReport r)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ShareReport.Check c in r.Checks)
                if (!c.Passed) sb.Append(sb.Length == 0 ? "" : ", ").Append(c.Name);
            return sb.ToString();
        }

        // Requires that exactly the named checks fail and every other one passes.
        static void ShareOnlyFails(ShareReport r, params string[] names)
        {
            Check(!r.Passed, "the report passed but " + string.Join("/", names) + " should fail");
            foreach (string n in names) Check(r.Failed(n), "'" + n + "' should fail; failing: " + ShareFailing(r) + " | " + r.ToString().Replace("\n", " | "));
            foreach (ShareReport.Check c in r.Checks)
            {
                bool expected = Array.IndexOf(names, c.Name) >= 0;
                Check(c.Passed != expected, "'" + c.Name + "' " + (c.Passed ? "passed" : "failed") + ": " + c.Detail);
            }
            Check(r.Summary.Contains(names[0]), "the summary does not name '" + names[0] + "': " + r.Summary);
        }

        // PSNR (average over R, G, B) and SSIM (all planes) of the export against the master, both in RGB, frame by frame
        // (both inputs are restamped by frame index so no pairing depends on the container's timestamps).
        static void ShareQualityNotes(string master, string mp4, int fps)
        {
            string err;
            string re = "setpts=N/(" + fps + "*TB)";
            string back = "scale=in_color_matrix=bt709:in_range=tv:flags=accurate_rnd+full_chroma_int,format=gbrp";
            // The third pair is the ceiling of any 4:2:0 output: the master converted to yuv420p and back with no encoding.
            string chain = "[0:v]" + back + "," + re + ",split[a1][a2];[1:v]format=gbrp," + re + ",split=4[b1][b2][b3][b4];[a1][b1]psnr[p];[a2][b2]ssim[s];" +
                           "[b3]" + VideoExport.RgbToYuv + ",format=yuv444p,format=yuv420p," + back + "[c];[c][b4]psnr[pc]";
            int code = Ff("-hide_banner -i " + Q(mp4) + " -i " + Q(master) + " -filter_complex \"" + chain + "\" -map \"[p]\" -f null - -map \"[s]\" -f null - -map \"[pc]\" -f null -", out err, 300000);
            Check(code == 0, "psnr/ssim run failed (" + code + "): " + Tail(err));
            MatchCollection ps = Regex.Matches(err, @"PSNR\s+r:([\d.]+).*?\bg:([\d.]+).*?\bb:([\d.]+).*?average:([\d.]+)");
            Match s = Regex.Match(err, @"SSIM\s+R:([\d.]+).*?\bG:([\d.]+).*?\bB:([\d.]+).*?All:([\d.]+)");
            Check(ps.Count == 2 && s.Success, "no psnr/ssim in the output: " + Tail(err));
            Match p = ps[0], c = ps[1];
            double psnr = double.Parse(p.Groups[4].Value, CultureInfo.InvariantCulture), ceiling = double.Parse(c.Groups[4].Value, CultureInfo.InvariantCulture);
            double ssim = double.Parse(s.Groups[4].Value, CultureInfo.InvariantCulture);
            Add("PSNR " + psnr.ToString("0.00", CultureInfo.InvariantCulture) + " dB (r " + p.Groups[1].Value + " g " + p.Groups[2].Value + " b " + p.Groups[3].Value + "), SSIM " +
                ssim.ToString("0.0000", CultureInfo.InvariantCulture) + ", 4:2:0 ceiling without encoding " + ceiling.ToString("0.00", CultureInfo.InvariantCulture) + " dB");
            // Chroma subsampling dominates the loss on saturated text; the encoder may only add a little on top of it.
            Check(psnr > ceiling - 3 && ssim > 0.9, "the export is far below what 4:2:0 allows (PSNR " + psnr + " vs ceiling " + ceiling + ", SSIM " + ssim + ")");
        }

        // One 3-track master (mix, equipo 440 Hz, micro 880 Hz) and its share MP4, built once for several tests.
        static string shareFixDir, shareFixMaster, shareFixMp4;
        static ExportSpec shareFixSpec;

        static void ShareFixture()
        {
            if (shareFixMp4 != null) return;
            string dir = WorkDir("share-fixture");
            const int w = 640, h = 360, fps = 30, frames = 60;
            long af = ShareAudioFrames(frames, fps);
            string master = ShareMaster(dir, "Cine 3 pistas", w, h, fps, frames, ShareScreen, ShareTone(af, 440, 0.3), ShareTone(af, 880, 0.3));
            ExportSpec s = ShareSpec(master, Path.Combine(dir, "Cine 3 pistas (para compartir).mp4"), w, h, fps, true);
            ShareExport(s);
            shareFixDir = dir;
            shareFixMaster = master;
            shareFixSpec = s;
            shareFixMp4 = s.Output;
        }

        static double ShareFixSeconds() { return 60 / 30.0; }

        static void ShareSuite()
        {
            Test("share", "args-cine-share-profile", delegate
            {
                ExportSpec s = ShareSpec(@"C:\v\Cine 2026.mkv", @"C:\v\Cine 2026 (para compartir).mp4", 1730, 786, 30, true);
                string a = VideoExport.Arguments(s);
                string expected = "-hide_banner -loglevel error -nostats -progress pipe:1 -y " + Recorder.SafeInput + "-i \"C:\\v\\Cine 2026.mkv\"" +
                                  " -filter_complex \"[0:v]" + VideoExport.RgbToYuv + ",format=yuv444p,crop=1730:786:0:0," + Recorder.Tags + ",scale=out_chroma_loc=center,format=yuv420p[o]\"" +
                                  " -map \"[o]\" -fps_mode passthrough -c:v libx264 -preset slow -crf 10 -profile:v high -level:v 4.1 -g 60 -maxrate 62500k -bufsize 78125k -map 0:a:0 -c:a aac -b:a 320k -ar 48000 -ac 2" +
                                  " -metadata comment=\"Stackshot: MP4 para compartir (H.264 y AAC, con p\u00E9rdida)\" -metadata:s:a:0 title= -movflags +faststart \"C:\\v\\Cine 2026 (para compartir).mp4\"";
                Check(a == expected, "got: " + a + " | expected: " + expected);
                Has(a, "-crf 10");
                Has(a, "format=yuv420p[o]");
                Has(a, "out_color_matrix=bt709:out_range=tv");
                Has(a, "color_primaries=bt709:color_trc=iec61966-2-1:colorspace=bt709:range=tv");
                Has(a, "-movflags +faststart");
                Has(a, "-map 0:a:0");
                Has(a, "-b:a 320k -ar 48000");
                HasNot(a, " -r ");
                HasNot(a, "lanczos");
                // Every frame passes with its own time; only a filter (speed, joins) may change the rate.
                Has(a, " -fps_mode passthrough ");
                HasNot(a, "-vsync");
                Check(a.IndexOf("crop=1730:786:0:0") > a.IndexOf("format=yuv444p"), "crop after the full-chroma conversion");
                // Size and cadence of the source decide the rest.
                string b = VideoExport.Arguments(ShareSpec(@"C:\v\m.mkv", @"C:\v\m.mp4", 1280, 720, 60, false));
                Has(b, "-preset slow -crf 10 -profile:v high -level:v 4.1 -g 120 -maxrate 62500k -bufsize 78125k");
                HasNot(b, "-map 0:a");
                HasNot(b, "-metadata:s:a:0");
                Has(b, "-an");
                string c = VideoExport.Arguments(ShareSpec(@"C:\v\m.mkv", @"C:\v\m.mp4", 3840, 2160, 60, true));
                Has(c, "-preset medium -crf 10 -profile:v high -level:v 5.2 -g 120 -maxrate 160000k -bufsize 300000k");
                // The track being listened to, not always the first.
                s.AudioTrack = 2;
                Has(VideoExport.Arguments(s), " -map 0:a:2 ");
                Add("crf 10 (maximum), slow up to 2.4 MP and medium above, the level the frame needs and its bitrate ceiling");
            });

            Test("share", "cinema-profile-values", delegate
            {
                Check(Recorder.FinalCodec(Recorder.Cinema, 1920L * 1080, 30) == "-c:v libx264 -preset slow -crf 14 -profile:v high -g 60", Recorder.FinalCodec(Recorder.Cinema, 1920L * 1080, 30));
                Check(Recorder.FinalCodec(Recorder.Cinema, 2400001, 60) == "-c:v libx264 -preset medium -crf 14 -profile:v high -g 120", Recorder.FinalCodec(Recorder.Cinema, 2400001, 60));
                Check(Recorder.FinalCodec(Recorder.Maximum, 1920L * 1080, 30) == "-c:v libx264 -preset slow -crf 14 -profile:v high -g 60", "Maximum changed");
                Check(Recorder.FinalCodec(Recorder.High, 1920L * 1080, 30) == "-c:v libx264 -preset medium -crf 16 -profile:v high -g 60", "High changed");
                Check(Recorder.AudioCodec(Recorder.Cinema) == "-c:a aac -b:a 320k -ar 48000", Recorder.AudioCodec(Recorder.Cinema));
            });

            Test("share", "parsers", delegate
            {
                const string mp4 = "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'x.mp4':\n  Duration: 00:01:12.40, start: 0.000000, bitrate: 5165 kb/s\n" +
                    "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709/bt709/iec61966-2-1, progressive), 1730x786 [SAR 1:1 DAR 865:393], 4999 kb/s, 30 fps, 30 tbr, 15360 tbn (default)\n" +
                    "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 317 kb/s (default)\n";
                ShareProbe p = ShareCheck.ParseProbe(mp4);
                Check(p.Seconds == 72.4 && p.VideoStreams == 1 && p.VideoCodec == "h264" && p.Profile == "High" && p.PixFmt == "yuv420p" && p.Width == 1730 && p.Height == 786 && p.Fps == 30,
                      "video: " + p.Seconds + " " + p.VideoCodec + " " + p.Profile + " " + p.PixFmt + " " + p.Width + "x" + p.Height + " " + p.Fps);
                Check(p.Color == "tv, bt709/bt709/iec61966-2-1, progressive", "color: " + p.Color);
                Check(p.Audio.Count == 1 && p.Audio[0].Codec == "aac" && p.Audio[0].Profile == "LC" && p.Audio[0].Rate == 48000 && p.Audio[0].Layout == "stereo", "audio");
                ShareProbe g = ShareCheck.ParseProbe("  Stream #0:0: Video: gif, bgra, 320x240 [SAR 64:64 DAR 4:3], 15 fps, 15 tbr, 100 tbn\n");
                Check(g.VideoCodec == "gif" && g.Profile == null && g.Width == 320 && g.Height == 240 && g.Fps == 15 && g.Audio.Count == 0 && g.Seconds < 0, "gif");
                ShareProbe m = ShareCheck.ParseProbe("  Stream #0:0(eng): Video: h264 (High 4:4:4 Predictive), gbrp(pc, gbr/unknown/unknown, progressive), 1730x786, 30 fps\n" +
                    "  Stream #0:1: Audio: pcm_f32le ([3][0][0][0] / 0x0003), 48000 Hz, stereo, flt, 3072 kb/s\n  Stream #0:2: Audio: pcm_f32le, 48000 Hz, stereo, flt\n");
                Check(m.Profile == "High 4:4:4 Predictive" && m.PixFmt == "gbrp" && m.Audio.Count == 2, "master: " + m.Profile + " " + m.PixFmt + " " + m.Audio.Count);
                Check(ShareCheck.CountFrameLines(new string[] { "#tb 0: 1/30", "0, 0, 0, 1, 10, 0x1", "", "0, 1, 1, 1, 10, 0x2" }) == 2, "frame lines");

                // Box walk: ftyp + moov + mdat covering the stream exactly; a cut one; a bad size; trailing bytes.
                byte[] good = ShareBoxes(new string[] { "ftyp", "moov", "mdat" }, new int[] { 24, 100, 500 });
                Mp4Boxes b = ShareCheck.WalkBoxes(new MemoryStream(good));
                Check(b.Error == null && string.Join(",", b.Types.ToArray()) == "ftyp,moov,mdat", "good: " + b.Error);
                byte[] cut = new byte[(int)(good.Length * 0.6)];
                Array.Copy(good, cut, cut.Length);
                Check(ShareCheck.WalkBoxes(new MemoryStream(cut)).Error != null, "cut stream not detected");
                byte[] tail = new byte[good.Length + 3];
                Array.Copy(good, tail, good.Length);
                Check(ShareCheck.WalkBoxes(new MemoryStream(tail)).Error != null, "trailing bytes not detected");
                byte[] bad = (byte[])good.Clone();
                bad[0] = bad[1] = bad[2] = 0;
                bad[3] = 4;
                Check(ShareCheck.WalkBoxes(new MemoryStream(bad)).Error != null, "impossible size not detected");
            });

            Test("share", "colour-fidelity", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-colour");
                const int w = 640, h = 360, fps = 30, frames = 30;
                string master = ShareMaster(dir, "Cine tarjeta", w, h, fps, frames, ShareCard, null, null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "Cine tarjeta (para compartir).mp4"), w, h, fps, false);
                ShareExport(s);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, ShareExpectOf(s, frames / (double)fps)));
                byte[] rgb = ShareDecodeRgb(s.Output, "bt709", w, h);
                StringBuilder worst = new StringBuilder();
                int top = 0;
                foreach (SharePatch p in SharePatches)
                    foreach (int f in new int[] { 0, frames - 1 })
                    {
                        int[] got = ShareRgbAt(rgb, w, h, f, p.X, p.Y);
                        int d = Diff(got, p.Rgb);
                        if (d > top) top = d;
                        if (f == 0) worst.Append(p.Name).Append(' ').Append(Rgb(got)).Append(d > 0 ? " (" + d + ")" : "").Append(' ');
                        Check(d <= 3, "patch " + p.Name + " frame " + f + " is " + Rgb(got) + " instead of " + Rgb(p.Rgb));
                    }
                Add("patches: " + worst.ToString().Trim() + "; max error " + top);

                // Gray ramp: every level 0..255, no gamma shift (the signed mean stays near zero as well).
                double abs = 0, signed = 0;
                int n = 0;
                foreach (int f in new int[] { 0, frames / 2, frames - 1 })
                    for (int y = ShareRampY0 + 4; y < ShareRampY1 - 4; y++)
                        for (int v = 8; v < 248; v++)
                        {
                            int[] c = ShareRgbAt(rgb, w, h, f, ShareRampX + v, y);
                            for (int k = 0; k < 3; k++) { abs += Math.Abs(c[k] - v); signed += c[k] - v; n++; }
                        }
                abs /= n;
                signed /= n;
                Add("ramp mean error " + abs.ToString("0.00", CultureInfo.InvariantCulture) + " levels (signed " + signed.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                Check(abs < 1.0, "gray ramp mean error " + abs);
                Check(Math.Abs(signed) < 0.5, "gray ramp is shifted by " + signed);

                // 1 px lines stay sharp in luma.
                int[] line = ShareRgbAt(rgb, w, h, 0, 160, ShareLineY), above = ShareRgbAt(rgb, w, h, 0, 160, ShareLineY - 2);
                Add("white 1 px line " + Rgb(line) + " over " + Rgb(above));
                Check(Math.Min(line[0], Math.Min(line[1], line[2])) >= 200 && Math.Max(above[0], Math.Max(above[1], above[2])) <= 60, "the 1 px line is blurred: " + Rgb(line) + " over " + Rgb(above));
                int[] vline = ShareRgbAt(rgb, w, h, 0, 330, 220);
                Check(Math.Min(vline[0], Math.Min(vline[1], vline[2])) >= 200, "the vertical 1 px line is blurred: " + Rgb(vline));
                ShareQualityNotes(master, s.Output, fps);

                // Negative control: the same file read as BT.601 must be visibly wrong, so the test above has teeth.
                byte[] wrong = ShareDecodeRgb(s.Output, "bt601", w, h);
                int shift = 0;
                string which = "";
                foreach (SharePatch p in SharePatches)
                {
                    if (p.Name != "rojo" && p.Name != "verde" && p.Name != "azul" && p.Name != "acento") continue;
                    int d = Diff(ShareRgbAt(wrong, w, h, 0, p.X, p.Y), p.Rgb);
                    if (d > shift) { shift = d; which = p.Name; }
                }
                Add("BT.601 control: up to " + shift + " levels (" + which + ")");
                Check(shift > 5, "reading it as BT.601 only shifts " + shift + " levels");
            });

            Test("share", "size-and-fps-kept", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-sizes");
                // 1281x721 is odd: like the editor, the export crops to the even size 1280x720 (the last column and row go).
                int[][] cases = { new[] { 1730, 786, 30, 1730, 786 }, new[] { 1280, 720, 60, 1280, 720 }, new[] { 1281, 721, 30, 1280, 720 } };
                StringBuilder notes = new StringBuilder();
                foreach (int[] k in cases)
                {
                    int fps = k[2], frames = fps;
                    string tag = k[0] + "x" + k[1] + "@" + fps;
                    string master = ShareMaster(dir, "Cine " + tag, k[0], k[1], fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                    ExportSpec s = ShareSpec(master, Path.Combine(dir, "Cine " + tag + " (para compartir).mp4"), k[3], k[4], fps, true);
                    ShareExport(s);
                    ShareReport r = ShareCheck.Validate(ffmpeg, s.Output, ShareExpectOf(s, 1.0));
                    Check(r.Passed, tag + ": " + r.Summary + " | " + r.ToString().Replace("\n", " | "));
                    string probe = Probe(s.Output);
                    Check(probe.Contains(k[3] + "x" + k[4]) && probe.Contains(" " + fps + " fps"), tag + " not kept: " + Tail(probe));
                    long bytes = new FileInfo(s.Output).Length, masterBytes = new FileInfo(master).Length;
                    notes.Append(tag + " -> " + k[3] + "x" + k[4] + " ok, " + (bytes >> 10) + " KiB (master " + (masterBytes >> 10) + " KiB); ");
                    if (k[0] == 1730) { Add(notes.ToString().Trim()); notes.Length = 0; ShareQualityNotes(master, s.Output, fps); }
                }
                Add(notes.ToString().Trim());
            });

            Test("share", "three-tracks-one-aac", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string probe = Probe(shareFixMaster);
                Check(Regex.Matches(probe, "Audio: pcm_f32le").Count == 3, "the synthetic master should have 3 audio tracks: " + Tail(probe));
                string outProbe = Probe(shareFixMp4);
                Check(Regex.Matches(outProbe, "Audio:").Count == 1 && Regex.IsMatch(outProbe, @"Audio: aac \(LC\).*48000 Hz, stereo"), "the export must have exactly one AAC LC 48 kHz stereo track: " + Tail(outProbe));
                double dm = DurationOf(probe), de = DurationOf(outProbe);
                Add("duration master " + dm.ToString("0.000", CultureInfo.InvariantCulture) + " s, export " + de.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Check(Math.Abs(dm - de) <= 0.05, "duration drifted: " + dm + " vs " + de);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, shareFixMp4, ShareExpectOf(shareFixSpec, ShareFixSeconds())));

                // The one track is the mix (a:0): both tones are in it, and nothing at a frequency that was not played.
                string raw = Path.Combine(shareFixDir, "mix.f32");
                FfOk("-hide_banner -loglevel error -y -i " + Q(shareFixMp4) + " -map 0:a:0 -ac 2 -ar 48000 -f f32le " + Q(raw), 60000);
                float[] s = ShareReadF32(raw);
                long from = 12000, count = 48000;
                double p440 = ShareTonePower(s, from, count, 440), p880 = ShareTonePower(s, from, count, 880), p1500 = ShareTonePower(s, from, count, 1500);
                Add("mix tones 440 Hz " + p440.ToString("0.0000", CultureInfo.InvariantCulture) + ", 880 Hz " + p880.ToString("0.0000", CultureInfo.InvariantCulture) + ", 1500 Hz " + p1500.ToString("0.000000", CultureInfo.InvariantCulture));
                Check(p440 > 100 * p1500 && p880 > 100 * p1500 && p440 > 1e-3 && p880 > 1e-3, "the exported sound is not the mix of both sources");
            });

            Test("share", "audio-video-sync", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-sync");
                const int w = 320, h = 180, fps = 30, frames = 90, flash = 45;
                long af = ShareAudioFrames(frames, fps);
                float[] click = ShareSilence(af);
                long at = (long)flash * 48000 / fps;
                for (long i = at; i < at + 240; i++) click[i * 2] = click[i * 2 + 1] = 0.7f;
                Action<byte[], int, int, int> paint = delegate(byte[] buf, int pw, int ph, int f)
                {
                    int v = f == flash ? 255 : 0;
                    ShareRect(buf, pw, 0, 0, pw, ph, new int[] { v, v, v });
                };
                string master = ShareMaster(dir, "Cine sync", w, h, fps, frames, paint, click, (float[])click.Clone());
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "Cine sync (para compartir).mp4"), w, h, fps, true);
                ShareExport(s);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, ShareExpectOf(s, frames / (double)fps)));
                int fm = ShareFlashFrame(master, w, h), fe = ShareFlashFrame(s.Output, w, h);
                Check(fm == flash && fe == flash, "the flash frame is " + fm + " in the master and " + fe + " in the export, expected " + flash);
                double tm = ShareClickTime(master, 0), te = ShareClickTime(s.Output, 0);
                Check(tm >= 0 && te >= 0, "no click found (master " + tm + ", export " + te + ")");
                double flashT = flash / (double)fps;
                double offMaster = (tm - flashT) * 1000, offExport = (te - flashT) * 1000;
                Add("click-flash offset: master " + offMaster.ToString("0.0", CultureInfo.InvariantCulture) + " ms, export " + offExport.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                double limit = 1000.0 / fps + 25;
                Check(Math.Abs(offExport) <= limit, "the sound is off by " + offExport + " ms (limit " + limit + ")");
            });

            Test("share", "master-untouched", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-untouched");
                const int w = 640, h = 360, fps = 30, frames = 30;
                long af = ShareAudioFrames(frames, fps);
                string master = ShareMaster(dir, "Cine intacto", w, h, fps, frames, ShareScreen, ShareTone(af, 440, 0.3), ShareTone(af, 660, 0.3));
                string before = ShareStamp(master);
                string[] files = Directory.GetFiles(dir);
                Array.Sort(files);
                ExportSpec s = ShareSpec(master, Path.Combine(Directory.CreateDirectory(Path.Combine(dir, "salida")).FullName, "Cine intacto (para compartir).mp4"), w, h, fps, true);
                ExportOk(s);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, ShareExpectOf(s, frames / (double)fps)));
                string[] after = Directory.GetFiles(dir);
                Array.Sort(after);
                Check(ShareStamp(master) == before, "master SHA-256 or modification time changed");
                Check(string.Join("|", files) == string.Join("|", after), "files appeared or vanished next to the master");
                Add("master " + before.Substring(0, 16) + "... unchanged");
            });

            Test("share", "check-passes-and-names-what-fails", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                ShareExpect e = ShareExpectOf(shareFixSpec, ShareFixSeconds());
                ShareReport good = ShareCheck.Validate(ffmpeg, shareFixMp4, e);
                ShareRequirePassed(good);
                Check(good.Checks.Count >= 10 && good.Summary.Contains("comprobado"), "the good report is thin: " + good.Checks.Count + " checks, " + good.Summary);
                Add(good.Checks.Count + " checks pass on a good export");

                // Wrong expectations: each one fails alone and is named.
                ShareExpect size = ShareExpectOf(shareFixSpec, ShareFixSeconds());
                size.Width = 642;
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, shareFixMp4, size), ShareCheck.NameSize);
                ShareExpect audio = ShareExpectOf(shareFixSpec, ShareFixSeconds());
                audio.HasAudio = false;
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, shareFixMp4, audio), ShareCheck.NameAudio);
                ShareExpect dur = ShareExpectOf(shareFixSpec, 3.5);
                dur.Fps = 0;
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, shareFixMp4, dur), ShareCheck.NameDuration);
                ShareExpect fps = ShareExpectOf(shareFixSpec, ShareFixSeconds());
                fps.Fps = 29.5;
                Check(ShareCheck.Validate(ffmpeg, shareFixMp4, fps).Failed(ShareCheck.NameFps), "a wrong frame rate is not named");
                ShareExpect asGif = ShareExpectOf(shareFixSpec, ShareFixSeconds());
                asGif.Gif = true;
                Check(ShareCheck.Validate(ffmpeg, shareFixMp4, asGif).Failed(ShareCheck.NameVideo), "an MP4 passed as a GIF");

                // Missing file.
                ShareReport missing = ShareCheck.Validate(ffmpeg, Path.Combine(shareFixDir, "no existe.mp4"), e);
                ShareOnlyFails(missing, ShareCheck.NameFile);
                Check(missing.Checks.Count == 1, "a missing file should stop at the first check");
                // Not a video at all.
                string text = Path.Combine(shareFixDir, "texto.mp4");
                File.WriteAllText(text, new string('x', 5000));
                Check(ShareCheck.Validate(ffmpeg, text, e).Failed(ShareCheck.NameStructure), "a text file named .mp4 passed");
                // Empty.
                string empty = Path.Combine(shareFixDir, "vacio.mp4");
                File.WriteAllBytes(empty, new byte[10]);
                Check(ShareCheck.Validate(ffmpeg, empty, e).Failed(ShareCheck.NameFile), "a 10 byte file passed");
            });

            Test("share", "check-truncated-copy", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                byte[] all = File.ReadAllBytes(shareFixMp4);
                string cut = Path.Combine(shareFixDir, "cortado.mp4");
                byte[] part = new byte[(int)(all.Length * 0.6)];
                Array.Copy(all, part, part.Length);
                File.WriteAllBytes(cut, part);
                ShareReport r = ShareCheck.Validate(ffmpeg, cut, ShareExpectOf(shareFixSpec, ShareFixSeconds()));
                Check(!r.Passed && r.Failed(ShareCheck.NameStructure), "a 60% copy must fail the structure check: " + r.ToString().Replace("\n", " | "));
                Add("failing: " + ShareFailing(r));
            });

            Test("share", "check-not-faststart", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                ExportSpec s = ShareSpec(shareFixMaster, Path.Combine(shareFixDir, "sin faststart.mp4"), 640, 360, 30, true);
                string args = VideoExport.Arguments(s);
                Check(args.Contains(" -movflags +faststart"), "the export args lost +faststart");
                string err;
                int code = Ff(args.Replace(" -movflags +faststart", ""), out err, 120000);
                Check(code == 0, "export without faststart failed: " + Tail(err));
                ShareReport r = ShareCheck.Validate(ffmpeg, s.Output, ShareExpectOf(s, ShareFixSeconds()));
                ShareOnlyFails(r, ShareCheck.NameFaststart);
            });

            Test("share", "check-corrupt-data", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                byte[] all = File.ReadAllBytes(shareFixMp4);
                Random rnd = new Random(7);
                int from = all.Length / 2;
                byte[] junk = new byte[16384];
                rnd.NextBytes(junk);
                Array.Copy(junk, 0, all, from, junk.Length);
                string bad = Path.Combine(shareFixDir, "danado.mp4");
                File.WriteAllBytes(bad, all);
                ShareReport r = ShareCheck.Validate(ffmpeg, bad, ShareExpectOf(shareFixSpec, ShareFixSeconds()));
                Check(r.Failed(ShareCheck.NameDecode), "16 KB of garbage inside the data went unnoticed: " + r.ToString().Replace("\n", " | "));
                Add("failing: " + ShareFailing(r));
            });

            Test("share", "check-gif", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-gif");
                string gif = Path.Combine(dir, "clip.gif");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x240:r=15:d=2\" -vf \"split[a][b];[a]palettegen[p];[b][p]paletteuse\" " + Q(gif), 60000);
                ShareExpect e = new ShareExpect();
                e.Gif = true;
                e.Width = 320;
                e.Height = 240;
                ShareReport good = ShareCheck.Validate(ffmpeg, gif, e);
                ShareRequirePassed(good);
                e.Width = 322;
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, gif, e), ShareCheck.NameSize);
                byte[] all = File.ReadAllBytes(gif);
                string cut = Path.Combine(dir, "cortado.gif");
                byte[] part = new byte[all.Length / 2];
                Array.Copy(all, part, part.Length);
                File.WriteAllBytes(cut, part);
                e.Width = 320;
                ShareReport r = ShareCheck.Validate(ffmpeg, cut, e);
                Add("half a GIF: " + (r.Passed ? "passed" : "failing " + ShareFailing(r)));
                Check(!r.Passed, "half a GIF passed the check");
            });

            Test("share", "editor-expectation-pure", delegate
            {
                ExportSpec s = ShareSpec("x.mkv", "y.mp4", 1730, 786, 30, true);
                ShareExpect e = Editor.ExpectFor(s, 72.4, 30, true);
                Check(e.Width == 1730 && e.Height == 786 && e.Fps == 30 && Math.Abs(e.Seconds - 72.4) < 1e-9 && e.HasAudio && e.RgbSource && !e.Gif, "whole master");
                s.Keep = new List<double[]>();
                s.Keep.Add(new double[] { 1, 3 });
                s.Speed = 2;
                Check(Math.Abs(Editor.ExpectFor(s, 72.4, 30, true).Seconds - 1.0) < 1e-9, "trim 1..3 at 2x is 1 s");
                Check(Editor.ExpectFor(s, 72.4, 30, true).Fps == 60 && Editor.ExpectFor(s, 72.4, 30, true).Slack == 0, "2x keeps every frame (30 fps plays at 60); one piece needs no slack");
                Check(VideoExport.OutFps(60, 2) == 60 && VideoExport.OutFps(30, 1.5) == 45 && VideoExport.OutFps(30, 0.5) == 30 && VideoExport.OutFps(120, 2) == 120, "OutFps: capped at 60 or the source rate, slow-downs keep the source rate");
                Check(VideoExport.Rate(30000.0 / 1001) == "30000/1001" && VideoExport.Rate(29.97) == "30000/1001" && VideoExport.Rate(25) == "25", "Rate: NTSC exact");
                s.Keep.Clear();
                s.Keep.Add(new double[] { 0.1, 1.23 });
                s.Keep.Add(new double[] { 2.0, 2.77 });
                s.Speed = 1;
                Check(Math.Abs(Editor.ExpectFor(s, 72.4, 30, true).Seconds - ((37 - 3) + (84 - 60)) / 30.0) < 1e-9, "pieces counted in whole frames: " + Editor.ExpectFor(s, 72.4, 30, true).Seconds);
                Check(Editor.ExpectFor(s, 72.4, 30, false).Fps == 0, "an unread frame rate is not checked");
                s.SourceIsGif = true;
                Check(Editor.ExpectFor(s, 72.4, 15, true).Fps == 0, "a GIF source has no reliable frame rate");
                s.SourceIsGif = false;
                Check(Math.Abs(Editor.ExactFps(29.97) - 30000.0 / 1001) < 1e-9 && Math.Abs(Editor.ExactFps(23.98) - 24000.0 / 1001) < 1e-9 && Math.Abs(Editor.ExactFps(59.94) - 60000.0 / 1001) < 1e-9, "NTSC rates");
                Check(Editor.ExactFps(25) == 25 && Editor.ExactFps(29.9) == 29.9, "other rates are left alone");
                s.Keep = null;
                s.MaxHeight = 360;
                ShareExpect small = Editor.ExpectFor(s, 10, 30, true);
                Check(small.Height == 360 && small.Width == 792, "shrunk size: " + small.Width + "x" + small.Height);
                s.ToGif = true;
                s.MaxHeight = 0;
                ShareExpect g = Editor.ExpectFor(s, 10, 30, true);
                Check(g.Gif && g.Width == 960 && g.Height == 436, "gif size: " + g.Width + "x" + g.Height);
                s.Crop = new Rectangle(0, 0, 640, 360);
                g = Editor.ExpectFor(s, 10, 30, true);
                Check(g.Width == 640 && g.Height == 360, "small gif size: " + g.Width + "x" + g.Height);
            });

            Test("share", "editor-expectation-matches-real-exports", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-expect");
                const int w = 320, h = 180, fps = 30, frames = 150;
                string master = ShareMaster(dir, "Cine expect", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                double[][][] cases = new double[][][]
                {
                    null,
                    new double[][] { new double[] { 0.37, 2.91 } },
                    new double[][] { new double[] { 0.1, 1.23 }, new double[] { 2.0, 2.77 }, new double[] { 3.3, 4.61 } },
                    new double[][] { new double[] { 0.13, 1.57 }, new double[] { 1.91, 2.43 }, new double[] { 2.77, 3.29 }, new double[] { 3.61, 4.97 } },
                    new double[][] { new double[] { 0.2, 0.51 }, new double[] { 0.65, 0.96 }, new double[] { 1.1, 1.41 }, new double[] { 1.55, 1.86 }, new double[] { 2.0, 2.31 },
                                     new double[] { 2.45, 2.76 }, new double[] { 2.9, 3.21 }, new double[] { 3.35, 3.66 }, new double[] { 3.8, 4.11 }, new double[] { 4.25, 4.56 } }
                };
                int[] which = new int[] { 0, 1, 2, 3, 2, 4, 4 };
                double[] speeds = new double[] { 1, 1, 1, 2, 0.5, 1, 1.5 };
                int n = 0, bad = 0; string notes = "";
                foreach (double sp in speeds)
                {
                    double[][] keep = cases[which[n]];
                    ExportSpec s = ShareSpec(master, Path.Combine(dir, "salida " + n + ".mp4"), w, h, fps, true);
                    s.Speed = sp;
                    if (keep != null) { s.Keep = new List<double[]>(); foreach (double[] k in keep) s.Keep.Add(k); }
                    ShareExport(s);
                    ShareReport r = ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, frames / (double)fps, fps, true));
                    ShareExpect ex = Editor.ExpectFor(s, frames / (double)fps, fps, true);
                    notes += "case " + n + " speed " + sp + ": expected " + ex.Seconds.ToString("0.000") + " s; " + (r.Passed ? "passed" : "FAILED: " + r.Summary) + " // ";
                    if (!r.Passed) bad++;
                    n++;
                }
                Add(notes); Check(bad == 0, bad + " of " + n + " exports did not match the expectation computed by the editor: " + notes);

                ExportSpec gs = ShareSpec(master, Path.Combine(dir, "salida.gif"), w, h, fps, false);
                gs.ToGif = true;
                gs.HasAudio = false;
                ExportOk(gs);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, gs.Output, Editor.ExpectFor(gs, 5, fps, true)));
            });

            Test("share", "editor-expectation-gif-wide", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("share-expect-gif");
                const int w = 1730, h = 786, fps = 30, frames = 15;
                string master = ShareMaster(dir, "Cine ancho", w, h, fps, frames, ShareScreen, null, null);
                ExportSpec gs = ShareSpec(master, Path.Combine(dir, "ancho.gif"), w, h, fps, false);
                gs.ToGif = true;
                ExportOk(gs);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, gs.Output, Editor.ExpectFor(gs, 0.5, fps, true)));
            });

            ShareEdgeSuite();
            ShareAuditSuite();
        }

        // Top-level MP4 boxes with the given types and total sizes (zero-filled bodies).
        static byte[] ShareBoxes(string[] types, int[] sizes)
        {
            List<byte> o = new List<byte>();
            for (int i = 0; i < types.Length; i++)
            {
                int n = sizes[i];
                o.Add((byte)(n >> 24)); o.Add((byte)(n >> 16)); o.Add((byte)(n >> 8)); o.Add((byte)n);
                o.AddRange(Encoding.ASCII.GetBytes(types[i]));
                for (int k = 8; k < n; k++) o.Add(0);
            }
            return o.ToArray();
        }
    }
}

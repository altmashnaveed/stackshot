// Roundtrip (zero-loss proof) and negative (the verifier is not vacuous) suites, plus the frame and audio generators.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static readonly string[] Patterns = { "text", "lines", "gradient", "bars", "black", "white", "noise", "moving", "garbagex" };

        sealed class Plan
        {
            public int W, H, FpsNum, FpsDen, Frames, Seed, RepeatEvery;
            public string Pattern, Audio, Gain = "";
            public bool CrossCheck;
        }

        sealed class Roundtrip
        {
            public Plan Plan;
            public string Dir, Nut, Master, Raw, Sys, Mic;
            public List<ulong> Hashes;
            public List<CineAudioTrack> Tracks;
            public CineVerification Verification;
            public long AudioFrames, FileBytes;
            public double WriteSeconds, BuildSeconds, VerifySeconds;
        }

        static Plan MakePlan(int w, int h, int fpsNum, int fpsDen, int frames, string pattern, string audio, bool crossCheck)
        {
            Plan p = new Plan();
            p.W = w;
            p.H = h;
            p.FpsNum = fpsNum;
            p.FpsDen = fpsDen;
            p.Frames = frames;
            p.Seed = 11 + w % 97 + h % 89;
            p.RepeatEvery = frames >= 20 ? 10 : 0;
            p.Pattern = pattern;
            p.Audio = audio;
            p.Gain = audio == "mic" || audio == "both" ? ",volume=3.0dB" : "";
            p.CrossCheck = crossCheck;
            return p;
        }

        // ---- frame generators (bgr0 layout: B, G, R, X per pixel) ----

        static void Px(byte[] buf, int w, int x, int y, int r, int g, int b)
        {
            int i = (y * w + x) * 4;
            buf[i] = (byte)b;
            buf[i + 1] = (byte)g;
            buf[i + 2] = (byte)r;
            buf[i + 3] = 0;
        }

        static void Paint(string pattern, byte[] buf, int w, int h, int frame, int seed)
        {
            switch (pattern)
            {
                case "black": Array.Clear(buf, 0, buf.Length); break;
                case "white": for (int i = 0; i < buf.Length; i++) buf[i] = 255; break;
                case "noise": new Random(seed * 1000003 + frame).NextBytes(buf); break;
                case "gradient": PaintGradient(buf, w, h, frame, false, seed); break;
                case "garbagex": PaintGradient(buf, w, h, frame, true, seed); break;
                case "bars": PaintBars(buf, w, h, frame); break;
                case "lines": PaintLines(buf, w, h, frame); break;
                case "text": PaintText(buf, w, h, frame, seed, 0); break;
                case "moving":
                    PaintText(buf, w, h, frame, seed, 3);
                    PaintRect(buf, w, h, frame);
                    break;
                default: throw new ArgumentException("patron desconocido " + pattern);
            }
        }

        // Every channel takes all 256 values; with garbage the X byte is random too.
        static void PaintGradient(byte[] buf, int w, int h, int frame, bool garbage, int seed)
        {
            Random xr = new Random(seed + frame);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    Px(buf, w, x, y, (idx + frame) & 255, (idx * 7 + frame * 3) & 255, (idx ^ (idx >> 3)) & 255);
                    if (garbage) buf[(y * w + x) * 4 + 3] = (byte)xr.Next(256);
                }
        }

        static void PaintBars(byte[] buf, int w, int h, int frame)
        {
            int[][] bars = { new[] { 255, 255, 255 }, new[] { 255, 255, 0 }, new[] { 0, 255, 255 }, new[] { 0, 255, 0 },
                             new[] { 255, 0, 255 }, new[] { 255, 0, 0 }, new[] { 0, 0, 255 }, new[] { 0, 0, 0 } };
            int top = h * 6 / 10;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (y < top)
                    {
                        int[] c = bars[Math.Min(7, x * 8 / w)];
                        Px(buf, w, x, y, c[0], c[1], c[2]);
                    }
                    else
                    {
                        bool white = ((x / 2 + y / 2 + frame) & 1) == 0;
                        int v = white ? 255 : 0;
                        Px(buf, w, x, y, v, v, v);
                    }
                }
        }

        static void PaintLines(byte[] buf, int w, int h, int frame)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = (y & 1) == 0 ? 255 : 0, g = 0, b = (y & 1) == 0 ? 0 : 255;
                    if ((x + frame) % 4 == 0)
                    {
                        bool a = ((x / 4) & 1) == 0;
                        r = a ? 255 : 0; g = 255; b = a ? 0 : 255;
                    }
                    Px(buf, w, x, y, r, g, b);
                }
        }

        static readonly int[][] Syntax = {
            new[] { 255, 85, 85 }, new[] { 80, 250, 123 }, new[] { 139, 233, 253 }, new[] { 255, 121, 198 },
            new[] { 241, 250, 140 }, new[] { 189, 147, 249 }, new[] { 255, 184, 108 }, new[] { 0, 255, 255 } };

        // Glyph grid: 6x10 cells, 5x7 glyphs in syntax colors, scrolling by frame.
        static void PaintText(byte[] buf, int w, int h, int frame, int seed, int shiftY)
        {
            int ox = frame * 2, oy = frame * shiftY;
            for (int y = 0; y < h; y++)
            {
                int gy = (y + oy) / 10, py = (y + oy) % 10;
                for (int x = 0; x < w; x++)
                {
                    int gx = (x + ox) / 6, px = (x + ox) % 6;
                    int r = 24, g = 24, b = 28;
                    if (px < 5 && py < 7)
                    {
                        ulong bits = CellBits(gx, gy, seed);
                        if (((bits >> (py * 5 + px)) & 1) != 0)
                        {
                            int[] c = Syntax[(int)((bits >> 40) & 7)];
                            r = c[0]; g = c[1]; b = c[2];
                        }
                    }
                    Px(buf, w, x, y, r, g, b);
                }
            }
        }

        static ulong CellBits(int gx, int gy, int seed)
        {
            unchecked
            {
                ulong v = (ulong)(uint)(gx * 73856093) ^ ((ulong)(uint)(gy * 19349663) << 17) ^ ((ulong)(uint)(seed * 83492791) << 31);
                v ^= v >> 33;
                v *= 0xFF51AFD7ED558CCDUL;
                v ^= v >> 33;
                v *= 0xC4CEB9FE1A85EC53UL;
                v ^= v >> 33;
                return v;
            }
        }

        static void PaintRect(byte[] buf, int w, int h, int frame)
        {
            int rw = Math.Min(200, w / 2), rh = Math.Min(120, h / 2);
            int rx = (frame * 11) % Math.Max(1, w - rw), ry = (frame * 5) % Math.Max(1, h - rh);
            for (int y = ry; y < ry + rh; y++)
                for (int x = rx; x < rx + rw; x++)
                    Px(buf, w, x, y, (frame * 37) & 255, 255 - ((frame * 13) & 255), 128);
        }

        // ---- audio generators: float32 stereo at 48 kHz ----

        static float FromBits(uint bits) { return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0); }

        // Sine plus seeded noise, with exact edge values (1.0, -1.0, +-0, denormals) every 1009 samples.
        static float[] AudioSamples(long frames, int seed)
        {
            Random rnd = new Random(seed);
            float[] edges = { 1.0f, -1.0f, 0f, FromBits(0x80000000u), FromBits(1u), FromBits(0x807FFFFFu), FromBits(0x00400000u),
                              0.99999994f, -0.99999994f, 1e-38f };
            long n = frames * 2;
            float[] s = new float[n];
            for (long i = 0; i < n; i++)
            {
                double t = (i / 2) / 48000.0;
                double tone = (i % 2 == 0) ? 0.4 * Math.Sin(2 * Math.PI * 440 * t) : 0.3 * Math.Sin(2 * Math.PI * 660 * t);
                s[i] = (float)(tone + 0.05 * (rnd.NextDouble() * 2 - 1));
                if (i % 1009 == 0) s[i] = edges[(i / 1009) % edges.Length];
            }
            return s;
        }

        static void WriteAudio(string path, long frames, int seed)
        {
            float[] s = AudioSamples(frames, seed);
            byte[] bytes = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(path, bytes);
        }

        // ---- roundtrip: frames -> CineSink -> CineMaster -> CineVerifier ----

        static Roundtrip RunRoundtrip(Plan p, string dir)
        {
            Roundtrip rt = new Roundtrip();
            rt.Plan = p;
            rt.Dir = dir;
            rt.Nut = Path.Combine(dir, "video.nut");
            rt.Master = Path.Combine(dir, "master.mkv");
            rt.Raw = Path.Combine(dir, "frames.bgr0");
            int bytes = p.W * p.H * 4;
            byte[] managed = new byte[bytes];
            IntPtr unmanaged = Marshal.AllocHGlobal(bytes);
            FileStream raw = p.CrossCheck ? new FileStream(rt.Raw, FileMode.Create, FileAccess.Write) : null;
            CineSink sink = new CineSink(ffmpeg, p.W, p.H, p.FpsNum, p.FpsDen, rt.Nut);
            try
            {
                sink.Start();
                int repeats = 0;
                Stopwatch clock = Stopwatch.StartNew();
                for (int f = 0; f < p.Frames; f++)
                {
                    Paint(p.Pattern, managed, p.W, p.H, f, p.Seed);
                    Marshal.Copy(managed, 0, unmanaged, bytes);
                    if (!sink.Write(unmanaged)) throw new TestFailed("Write fallo en el fotograma " + f + ": " + sink.ErrorTail);
                    if (raw != null) raw.Write(managed, 0, bytes);
                    if (p.RepeatEvery > 0 && f % p.RepeatEvery == p.RepeatEvery - 1)
                    {
                        if (!sink.Repeat()) throw new TestFailed("Repeat fallo en el fotograma " + f);
                        repeats++;
                        if (raw != null) raw.Write(managed, 0, bytes);
                    }
                }
                rt.WriteSeconds = clock.Elapsed.TotalSeconds;
                int code;
                if (!sink.Close(120000, out code)) throw new TestFailed("ffmpeg no termino: " + sink.ErrorTail);
                if (code != 0) throw new TestFailed("ffmpeg salio con " + code + ": " + sink.ErrorTail);
                Check(sink.Frames == p.Frames + repeats, "fotogramas escritos " + sink.Frames + ", esperados " + (p.Frames + repeats));
                Check(sink.Hashes.Count == sink.Frames, "hashes " + sink.Hashes.Count + " != fotogramas " + sink.Frames);
                rt.Hashes = new List<ulong>(sink.Hashes);
            }
            finally
            {
                sink.Dispose();
                Marshal.FreeHGlobal(unmanaged);
                if (raw != null) raw.Dispose();
            }

            long frames = rt.Hashes.Count;
            rt.AudioFrames = Cine.AudioFramesFor(frames, p.FpsNum, p.FpsDen);
            bool sys = p.Audio == "sys" || p.Audio == "both", mic = p.Audio == "mic" || p.Audio == "both";
            if (sys) { rt.Sys = Path.Combine(dir, "equipo.f32"); WriteAudio(rt.Sys, rt.AudioFrames, p.Seed + 1); }
            if (mic) { rt.Mic = Path.Combine(dir, "micro.f32"); WriteAudio(rt.Mic, rt.AudioFrames, p.Seed + 2); }
            rt.Tracks = CineMaster.Tracks(rt.Sys, rt.Mic, rt.AudioFrames);
            int expectedTracks = (sys ? 1 : 0) + (mic ? 1 : 0) + (sys && mic ? 1 : 0);
            Check(rt.Tracks.Count == expectedTracks, "pistas " + rt.Tracks.Count + ", esperadas " + expectedTracks);
            foreach (CineAudioTrack t in rt.Tracks)
                Check(t.Frames == rt.AudioFrames, "pista '" + t.Title + "' con " + t.Frames + " muestras, esperadas " + rt.AudioFrames);

            Stopwatch build = Stopwatch.StartNew();
            double seconds = frames * (double)p.FpsDen / p.FpsNum;
            string err = CineMaster.Build(ffmpeg, rt.Nut, rt.Sys, rt.Mic, p.Gain, rt.Master, seconds, null);
            rt.BuildSeconds = build.Elapsed.TotalSeconds;
            if (err != null) throw new TestFailed("Build: " + err);
            rt.FileBytes = new FileInfo(rt.Master).Length;

            CineExpect e = new CineExpect();
            e.Width = p.W;
            e.Height = p.H;
            e.FpsNum = p.FpsNum;
            e.FpsDen = p.FpsDen;
            e.FrameHashes = new List<ulong>(rt.Hashes);
            e.Audio = rt.Tracks;
            Stopwatch verify = Stopwatch.StartNew();
            rt.Verification = CineVerifier.Verify(ffmpeg, rt.Master, e, null);
            rt.VerifySeconds = verify.Elapsed.TotalSeconds;
            return rt;
        }

        static string FailingChecks(CineVerification v)
        {
            StringBuilder sb = new StringBuilder();
            foreach (CineCheck c in v.Checks)
                if (!c.Passed) sb.Append(sb.Length == 0 ? "" : "; ").Append(c.Name).Append(": ").Append(c.Detail);
            return sb.ToString();
        }

        // null when the master verifies completely (every check, every frame).
        static string Verdict(Roundtrip rt)
        {
            CineVerification v = rt.Verification;
            long frames = rt.Hashes.Count;
            string failing = FailingChecks(v);
            if (v.Passed && failing.Length == 0 && v.FramesDecoded == frames && v.FramesMatched == frames && v.FirstMismatch == -1) return null;
            return "no verificado: passed=" + v.Passed + " decodificados=" + v.FramesDecoded + " coinciden=" + v.FramesMatched +
                   " primer fallo=" + v.FirstMismatch + " fallos: " + failing;
        }

        static void RequirePassed(Roundtrip rt)
        {
            string verdict = Verdict(rt);
            Check(verdict == null, verdict);
        }

        static bool IsDurationCheck(CineCheck c) { return c.Name != null && c.Name.StartsWith("Duraci", StringComparison.Ordinal); }

        // Failures other than the audio duration check (see the duration finding in the report).
        static string FailingNonDuration(CineVerification v)
        {
            StringBuilder sb = new StringBuilder();
            foreach (CineCheck c in v.Checks)
                if (!c.Passed && !IsDurationCheck(c)) sb.Append(sb.Length == 0 ? "" : "; ").Append(c.Name).Append(": ").Append(c.Detail);
            return sb.ToString();
        }

        // The base master of a negative test may fail only the duration check; otherwise the mutations prove nothing.
        static void RequireBaseOk(Roundtrip rt)
        {
            CineVerification v = rt.Verification;
            long frames = rt.Hashes.Count;
            Check(v.FramesDecoded == frames && v.FramesMatched == frames && v.FirstMismatch == -1 && FailingNonDuration(v).Length == 0,
                  "la base no verifica (sin contar la duracion del sonido): " + FailingChecks(v));
        }

        // Independent cross-check, without CineHash: framemd5 of the raw frames against framemd5 of the decoded master,
        // and SHA-256 of each original audio file against ffmpeg's decode of its track.
        static void CrossCheck(Roundtrip rt)
        {
            Plan p = rt.Plan;
            long frames = rt.Hashes.Count;
            string rate = p.FpsNum + "/" + p.FpsDen;
            string err;
            int exit;
            List<string> expected = Md5Column(FfmpegLines("-hide_banner -loglevel error -f rawvideo -pix_fmt bgr0 -video_size " + p.W + "x" + p.H +
                                                          " -framerate " + rate + " -i " + Q(rt.Raw) +
                                                          " -pix_fmt gbrp -fps_mode passthrough -f framemd5 -", out err, out exit));
            Check(exit == 0, "framemd5 del raw salio con " + exit + ": " + err);
            List<string> actual = Md5Column(FfmpegLines("-hide_banner -loglevel error -i " + Q(rt.Master) +
                                                        " -map 0:v:0 -pix_fmt gbrp -fps_mode passthrough -f framemd5 -", out err, out exit));
            Check(exit == 0, "framemd5 del master salio con " + exit + ": " + err);
            Check(expected.Count == frames, "framemd5 del raw: " + expected.Count + " fotogramas");
            Check(actual.Count == frames, "framemd5 del master: " + actual.Count + " fotogramas, esperados " + frames);
            for (int i = 0; i < frames; i++)
                Check(expected[i] == actual[i], "fotograma " + i + ": md5 raw " + expected[i] + " master " + actual[i]);
            Put("framemd5Frames", frames);

            int compared = 0;
            for (int k = 0; k < rt.Tracks.Count; k++)
            {
                CineAudioTrack t = rt.Tracks[k];
                if (!t.Original) { Add("mezcla sin comparar por separado"); continue; }
                string mine = Sha256File(t.Path);
                string decoded = null;
                long bytes = -1;
                int code = RunProcess(ffmpeg, "-hide_banner -loglevel error -i " + Q(rt.Master) + " -map 0:a:" + k +
                                      " -c:a pcm_f32le -f f32le -", s => { decoded = Sha256Stream(s, out bytes); }, out err);
                Check(code == 0, "ffmpeg audio " + k + " salio con " + code + ": " + err);
                Check(decoded == mine, "audio '" + t.Title + "': sha256 del fichero " + mine + " distinto del decodificado " + decoded);
                Check(decoded == t.Sha256, "audio '" + t.Title + "': CineMaster.Sha256 distinto");
                Check(bytes == rt.AudioFrames * 8, "audio '" + t.Title + "': " + bytes + " bytes, esperados " + rt.AudioFrames * 8);
                compared++;
            }
            Put("audioTracksCompared", compared);
        }

        static List<string> Md5Column(List<string> lines)
        {
            List<string> md5 = new List<string>();
            foreach (string line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                md5.Add(line.Substring(line.LastIndexOf(',') + 1).Trim());
            }
            return md5;
        }

        static void RoundtripTest(string name, Plan p) { RoundtripTest("roundtrip", name, p); }

        static void RoundtripTest(string suite, string name, Plan p)
        {
            Test(suite, name, () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("rt-" + name);
                try
                {
                    Roundtrip rt = RunRoundtrip(p, dir);
                    string verdict = Verdict(rt);
                    if (p.CrossCheck) CrossCheck(rt);   // independent evidence, also when the verifier complains
                    int n = rt.Hashes.Count;
                    Put("size", p.W + "x" + p.H);
                    Put("rate", p.FpsNum + "/" + p.FpsDen);
                    Put("pattern", p.Pattern);
                    Put("audio", p.Audio);
                    Put("frames", n);
                    Put("fileBytes", rt.FileBytes);
                    Put("firstHash", rt.Hashes[0].ToString("x16"));
                    Put("lastHash", rt.Hashes[n - 1].ToString("x16"));
                    Put("encodeSeconds", Math.Round(rt.WriteSeconds, 2));
                    Put("buildSeconds", Math.Round(rt.BuildSeconds, 2));
                    Put("verifySeconds", Math.Round(rt.VerifySeconds, 2));
                    Note(p.W + "x" + p.H + " " + p.Pattern + " " + n + " fotogramas, " + (rt.FileBytes >> 10) + " KiB, verificado en " +
                         rt.VerifySeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    Check(verdict == null, verdict);
                }
                finally { DeleteDir(dir); }
            });
        }

        static void RoundtripSuite()
        {
            foreach (string pattern in Patterns)
                RoundtripTest("pattern-" + pattern + "-1280x720", MakePlan(1280, 720, 60, 1, 30, pattern, "both", true));
            RoundtripTest("size-1920x1080", MakePlan(1920, 1080, 60, 1, 30, "text", "both", true));
            RoundtripTest("size-2560x1440", MakePlan(2560, 1440, 60, 1, 20, "gradient", "sys", true));
            RoundtripTest("size-3840x2160", MakePlan(3840, 2160, 60, 1, 5, "text", "none", true));
            RoundtripTest("size-1000x2000", MakePlan(1000, 2000, 60, 1, 30, "lines", "both", true));
            RoundtripTest("size-16x16", MakePlan(16, 16, 60, 1, 60, "gradient", "none", true));
            RoundtripTest("rate-60000-1001", MakePlan(1280, 720, 60000, 1001, 60, "text", "both", true));
            RoundtripTest("rate-30", MakePlan(1280, 720, 30, 1, 30, "text", "sys", true));
            RoundtripTest("audio-none", MakePlan(1280, 720, 60, 1, 30, "text", "none", true));
            RoundtripTest("audio-sys", MakePlan(1280, 720, 60, 1, 30, "text", "sys", true));
            RoundtripTest("audio-mic", MakePlan(1280, 720, 60, 1, 30, "text", "mic", true));
            RoundtripTest("audio-both", MakePlan(1280, 720, 60, 1, 30, "text", "both", true));
        }

        // ---- negative: each broken master must fail verification ----

        static CineExpect ExpectOf(Roundtrip rt, List<ulong> hashes, List<CineAudioTrack> audio)
        {
            CineExpect e = new CineExpect();
            e.Width = rt.Plan.W;
            e.Height = rt.Plan.H;
            e.FpsNum = rt.Plan.FpsNum;
            e.FpsDen = rt.Plan.FpsDen;
            e.FrameHashes = hashes;
            e.Audio = audio;
            return e;
        }

        // Starts from a good master; the test then breaks one thing. The base must pass first, or the test is void.
        static void NegativeTest(string name, Action<Roundtrip, string> body)
        {
            Test("negative", name, () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("neg-" + name);
                try
                {
                    Plan p = MakePlan(640, 360, 30, 1, 30, "text", "both", true);
                    Roundtrip rt = RunRoundtrip(p, dir);
                    RequireBaseOk(rt);
                    CrossCheck(rt);
                    body(rt, dir);
                }
                finally { DeleteDir(dir); }
            });
        }

        // The mutation must be caught by a check other than the duration one, or the verifier is not proven.
        static void NotPassed(CineVerification v, string what)
        {
            Check(v != null && !v.Passed, what + " deberia fallar la verificacion");
            string other = FailingNonDuration(v);
            Check(other.Length > 0, what + ": solo fallan comprobaciones de duracion, la alteracion no se detecta");
            Add("fallos: " + other);
        }

        static void NegativeSuite()
        {
            NegativeTest("altered-hash-at-index", (rt, dir) =>
            {
                List<ulong> hashes = new List<ulong>(rt.Hashes);
                hashes[7] ^= 1UL;
                CineVerification v = CineVerifier.Verify(ffmpeg, rt.Master, ExpectOf(rt, hashes, rt.Tracks), null);
                NotPassed(v, "hash alterado en 7");
                Check(v.FirstMismatch == 7, "primer desajuste en " + v.FirstMismatch + ", esperado 7");
            });

            NegativeTest("removed-hash", (rt, dir) =>
            {
                List<ulong> hashes = new List<ulong>(rt.Hashes);
                hashes.RemoveAt(5);
                CineVerification v = CineVerifier.Verify(ffmpeg, rt.Master, ExpectOf(rt, hashes, rt.Tracks), null);
                NotPassed(v, "hash quitado en 5");
                Check(v.FirstMismatch == 5, "primer desajuste en " + v.FirstMismatch + ", esperado 5");
            });

            NegativeTest("truncated-master-70", (rt, dir) =>
            {
                string cut = Path.Combine(dir, "cut.mkv");
                long keep = (long)(new FileInfo(rt.Master).Length * 0.7);
                using (FileStream src = File.OpenRead(rt.Master))
                using (FileStream dst = File.Create(cut))
                {
                    byte[] buf = new byte[1 << 16];
                    long left = keep;
                    while (left > 0)
                    {
                        int n = src.Read(buf, 0, (int)Math.Min(buf.Length, left));
                        if (n <= 0) break;
                        dst.Write(buf, 0, n);
                        left -= n;
                    }
                }
                NotPassed(CineVerifier.Verify(ffmpeg, cut, ExpectOf(rt, rt.Hashes, rt.Tracks), null), "master cortado al 70 %");
            });

            NegativeTest("lossy-yuv444-crf0", (rt, dir) =>
            {
                string lossy = Path.Combine(dir, "lossy444.mkv");
                EncodeRawLossy(rt, lossy, "-c:v libx264 -preset ultrafast -crf 0 -pix_fmt yuv444p");
                NotPassed(CineVerifier.Verify(ffmpeg, lossy, ExpectOf(rt, rt.Hashes, new List<CineAudioTrack>()), null),
                          "yuv444p crf 0 (no es RGB)");
            });

            NegativeTest("lossy-yuv420-crf10", (rt, dir) =>
            {
                string lossy = Path.Combine(dir, "lossy420.mkv");
                EncodeRawLossy(rt, lossy, "-c:v libx264 -preset ultrafast -crf 10 -pix_fmt yuv420p");
                NotPassed(CineVerifier.Verify(ffmpeg, lossy, ExpectOf(rt, rt.Hashes, new List<CineAudioTrack>()), null),
                          "yuv420p crf 10");
            });

            NegativeTest("ffv1-same-pixels", (rt, dir) =>
            {
                string ffv1 = Path.Combine(dir, "ffv1.mkv");
                EncodeRawLossy(rt, ffv1, "-c:v ffv1 -pix_fmt gbrp");
                CineVerification v = CineVerifier.Verify(ffmpeg, ffv1, ExpectOf(rt, rt.Hashes, new List<CineAudioTrack>()), null);
                NotPassed(v, "FFV1 (pixeles iguales, codec distinto)");
                Check(v.FramesMatched == rt.Hashes.Count, "FFV1 deberia coincidir en pixeles: " + v.FramesMatched + " de " + rt.Hashes.Count);
            });

            NegativeTest("wrong-expected-width", (rt, dir) =>
            {
                CineExpect e = ExpectOf(rt, rt.Hashes, rt.Tracks);
                e.Width = rt.Plan.W + 2;
                NotPassed(CineVerifier.Verify(ffmpeg, rt.Master, e, null), "ancho esperado erroneo");
            });

            NegativeTest("one-audio-sample-changed-in-reference", (rt, dir) =>
            {
                string changed = Path.Combine(dir, "equipo-changed.f32");
                byte[] bytes = File.ReadAllBytes(rt.Sys);
                int at = 4 * 1000;
                bytes[at] ^= 0x01; // one bit of one sample
                File.WriteAllBytes(changed, bytes);
                List<CineAudioTrack> tracks = CineMaster.Tracks(changed, rt.Mic, rt.AudioFrames);
                NotPassed(CineVerifier.Verify(ffmpeg, rt.Master, ExpectOf(rt, rt.Hashes, tracks), null), "una muestra cambiada en la referencia");
            });

            NegativeTest("missing-master-file", (rt, dir) =>
            {
                string missing = Path.Combine(dir, "no-existe.mkv");
                NotPassed(CineVerifier.Verify(ffmpeg, missing, ExpectOf(rt, rt.Hashes, rt.Tracks), null), "master inexistente");
            });
        }

        // Re-encodes the raw frames of rt (video only) with the given output options.
        static void EncodeRawLossy(Roundtrip rt, string output, string options)
        {
            Plan p = rt.Plan;
            string err;
            int code = Ffmpeg("-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgr0 -video_size " + p.W + "x" + p.H +
                              " -framerate " + p.FpsNum + "/" + p.FpsDen + " -i " + Q(rt.Raw) + " " + options + " -f matroska " + Q(output), out err);
            if (code != 0) throw new TestFailed("no se pudo codificar la referencia (" + options + "): " + err);
        }
    }
}

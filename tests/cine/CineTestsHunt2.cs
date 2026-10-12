// Hunt round 2 (h2-cine): the Cine pipeline after capture and its safety nets. Synthetic frames and sound only, through
// the real encoder, master builder, verifier, session end (CompleteCine) and rescue code. No screen is used.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        const BindingFlags H2Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static int h2Stamps;

        static void Hunt2CineSuite()
        {
            // These recordings save their reports in <DataDir>\cine: never in the real Stackshot data folder.
            string real = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Stackshot");
            if (string.Equals(Path.GetFullPath(Settings.DataDir).TrimEnd('\\'), Path.GetFullPath(real).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Test("hunt2cine", "private-data-folder", () => Skip("set STACKSHOT_TEST_DATA to a private folder: these tests write reports to <DataDir>\\cine"));
                return;
            }
            string savedRoot = Recorder.RescueRoot;
            try
            {
                Hunt2Sizes();
                Hunt2Audio();
                Hunt2Hash();
                Hunt2Failures();
                Hunt2Report();
                Hunt2Disk();
            }
            finally
            {
                Recorder.RescueRoot = savedRoot;
            }
        }

        // ---- helpers (all names prefixed H2 to stay apart from the other suites) ----

        // Encodes n frames through a live CineSink and leaves it open (the caller closes or hands it to a session).
        static CineSink H2Feed(string nut, int w, int h, int fn, int fd, int n, string pattern, bool garbageX, int seed)
        {
            CineSink sink = new CineSink(ffmpeg, w, h, fn, fd, nut);
            try
            {
                sink.Start();
                int bytes = w * h * 4;
                byte[] managed = new byte[bytes];
                IntPtr un = Marshal.AllocHGlobal(bytes);
                try
                {
                    Random xr = new Random(seed);
                    for (int f = 0; f < n; f++)
                    {
                        Paint(pattern, managed, w, h, f, seed);
                        // The fourth byte of bgr0 is ignored by everything downstream: garbage there must change nothing.
                        if (garbageX) for (int i = 3; i < bytes; i += 4) managed[i] = (byte)xr.Next(256);
                        Marshal.Copy(managed, 0, un, bytes);
                        if (!sink.Write(un)) throw new TestFailed("Write fallo en el fotograma " + f + ": " + sink.ErrorTail);
                    }
                }
                finally { Marshal.FreeHGlobal(un); }
                return sink;
            }
            catch
            {
                sink.Dispose();
                throw;
            }
        }

        // Encodes n frames to nut, closes the encoder and returns the hashes the sink kept.
        static List<ulong> H2Encode(string nut, int w, int h, int fn, int fd, int n, string pattern, bool garbageX, int seed)
        {
            CineSink sink = H2Feed(nut, w, h, fn, fd, n, pattern, garbageX, seed);
            try
            {
                int code;
                Check(sink.Close(120000, out code), "ffmpeg no termino: " + sink.ErrorTail);
                Check(code == 0, "ffmpeg salio con " + code + ": " + sink.ErrorTail);
                return new List<ulong>(sink.Hashes);
            }
            finally { sink.Dispose(); }
        }

        static void H2WriteFloats(string path, float[] s)
        {
            byte[] b = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, b, 0, b.Length);
            File.WriteAllBytes(path, b);
        }

        static float[] H2DecodeFloats(string master, int k)
        {
            MemoryStream ms = new MemoryStream();
            string err;
            int code = RunProcess(ffmpeg, "-hide_banner -nostdin -loglevel error -i " + Q(master) + " -map 0:a:" + k + " -c:a pcm_f32le -f f32le -",
                                  s => s.CopyTo(ms), out err);
            Check(code == 0, "ffmpeg (audio " + k + ") salio con " + code + ": " + err);
            byte[] b = ms.ToArray();
            float[] f = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
            return f;
        }

        // Builds the master from the nut and the given sound files (null for none), then verifies it against the hashes and
        // the expected sample count. buildError is set when Build refuses; the verification is then null.
        static CineVerification H2Master(string nut, string sys, string mic, List<ulong> hashes, int w, int h, int fn, int fd, long samples,
                                         string master, out string buildError)
        {
            List<CineAudioTrack> tracks = CineMaster.Tracks(sys, mic, samples);
            double seconds = hashes.Count * (double)fd / fn;
            buildError = CineMaster.Build(ffmpeg, nut, sys, mic, sys != null && mic != null ? ",volume=3.0dB" : "", master, seconds, null);
            if (buildError != null) return null;
            CineExpect e = new CineExpect();
            e.Width = w;
            e.Height = h;
            e.FpsNum = fn;
            e.FpsDen = fd;
            e.FrameHashes = hashes;
            e.Audio = tracks;
            return CineVerifier.Verify(ffmpeg, master, e, null);
        }

        static byte[] H2Head(byte[] all, int n)
        {
            byte[] b = new byte[n];
            Buffer.BlockCopy(all, 0, b, 0, n);
            return b;
        }

        static void H2Set(object o, string field, object value)
        {
            FieldInfo f = o.GetType().GetField(field, H2Any);
            if (f == null) throw new TestFailed("no encuentro el campo " + field);
            if (value != null && !f.FieldType.IsInstanceOfType(value)) value = Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture);
            f.SetValue(o, value);
        }

        static object H2Get(object o, string field)
        {
            FieldInfo f = o.GetType().GetField(field, H2Any);
            if (f == null) throw new TestFailed("no encuentro el campo " + field);
            return f.GetValue(o);
        }

        // A recording that stopped: the live encoder is open with its frames written, the sound files are on disk, and a
        // Session holds them as Stop would leave it. H2Finish then runs the real end of recording (CompleteCine).
        sealed class H2Rec
        {
            public Session S;
            public CineSink Sink;
            public string Dir, Stamp, Nut, Sys, Mic, Output, Recovered, SysSha, MicSha;
            public int W, H, Fn, Fd;
            public long N, Samples;
        }

        static object H2Source()
        {
            object s = FormatterServices.GetUninitializedObject(typeof(ScreenSource));
            H2Set(s, "RefreshNum", 60u);
            H2Set(s, "RefreshDen", 1u);
            H2Set(s, "Description", "prueba");
            return s;
        }

        static H2Rec H2Recording(string name, int w, int h, int fn, int fd, int n, string pattern, bool sound, bool garbageX)
        {
            H2Rec r = new H2Rec();
            r.Dir = WorkDir(name);
            r.W = w;
            r.H = h;
            r.Fn = fn;
            r.Fd = fd;
            r.N = n;
            DateTime t = new DateTime(2026, 10, 11, 10, 0, 0).AddSeconds(h2Stamps++);
            r.Stamp = t.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Settings.TempDir);
            r.Recovered = Path.Combine(r.Dir, "recuperado");
            Recorder.RescueRoot = r.Recovered;
            r.Nut = Path.Combine(r.Dir, "~grabando " + r.Stamp + ".nut");
            r.Sink = H2Feed(r.Nut, w, h, fn, fd, n, pattern, garbageX, 21);
            r.Output = Path.Combine(r.Dir, "Cine " + r.Stamp + ".mkv");
            r.Samples = Cine.AudioFramesFor(n, fn, fd);
            AudioCapture sysCap = null, micCap = null;
            if (sound)
            {
                r.Sys = Path.Combine(r.Dir, "~grabando " + r.Stamp + " equipo.f32");
                r.Mic = Path.Combine(r.Dir, "~grabando " + r.Stamp + " micro.f32");
                H2WriteFloats(r.Sys, AudioSamples(r.Samples, 31));
                H2WriteFloats(r.Mic, AudioSamples(r.Samples, 32));
                r.SysSha = Sha256File(r.Sys);
                r.MicSha = Sha256File(r.Mic);
                sysCap = new AudioCapture(true, null, r.Sys);
                micCap = new AudioCapture(false, null, r.Mic);
            }
            Settings st = new Settings();
            st.VideoQuality = Recorder.Cinema;
            st.Webcam = 0;
            r.S = new Session(null, st, ffmpeg, new Rectangle(0, 0, w, h), false, 60, Recorder.Cinema);
            H2Set(r.S, "video", r.Nut);
            H2Set(r.S, "sysPath", sound ? r.Sys : null);
            H2Set(r.S, "micPath", sound ? r.Mic : null);
            H2Set(r.S, "output", r.Output);
            H2Set(r.S, "stamp", r.Stamp);
            H2Set(r.S, "fpsNum", fn);
            H2Set(r.S, "fpsDen", fd);
            H2Set(r.S, "frames", (long)n);
            H2Set(r.S, "startedAt", t);
            H2Set(r.S, "enc", "libx264rgb");
            H2Set(r.S, "sink", r.Sink);
            H2Set(r.S, "sys", sysCap);
            H2Set(r.S, "mic", micCap);
            H2Set(r.S, "source", H2Source());
            H2Set(r.S, "pool", new FramePool(w * h * 4, 4));
            return r;
        }

        // The real end of a recording: closes the encoder, builds and verifies the master, writes the report, rescues.
        static void H2Finish(H2Rec r)
        {
            MethodInfo m = typeof(Session).GetMethod("CompleteCine", H2Any);
            Check(m != null, "no encuentro Session.CompleteCine");
            try { m.Invoke(r.S, null); }
            catch (TargetInvocationException ex) { throw new TestFailed("CompleteCine: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
        }

        static string H2Output(H2Rec r) { return (string)H2Get(r.S, "output"); }

        // The report SaveReport writes next to the other reports: <DataDir>\cine\<name of the output>.json.
        static string H2ReportPath(string output)
        {
            return Path.Combine(Settings.DataDir, "cine", Path.GetFileNameWithoutExtension(output) + ".json");
        }

        static Dictionary<string, object> H2ReadReport(string output)
        {
            string path = H2ReportPath(output);
            Check(File.Exists(path), "no hay informe en " + path);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return (Dictionary<string, object>)js.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
        }

        static void H2Cleanup(H2Rec r)
        {
            if (r == null) return;
            try
            {
                string output = r.S != null ? H2Output(r) : r.Output;
                string json = H2ReportPath(output);
                if (File.Exists(json)) File.Delete(json);
            }
            catch { }
            DeleteDir(r.Dir);
        }

        // The files a recording leaves behind when its master could not be made: they must be in recuperado\<stamp>,
        // byte for byte as they were (sound files checked by SHA-256).
        static void H2CheckKept(H2Rec r, bool sound)
        {
            string dir = Path.Combine(r.Recovered, r.Stamp);
            string nutKept = Path.Combine(dir, Path.GetFileName(r.Nut));
            Check(File.Exists(nutKept), "el video en vivo no se ha conservado en " + dir);
            if (sound)
            {
                string sysKept = Path.Combine(dir, Path.GetFileName(r.Sys)), micKept = Path.Combine(dir, Path.GetFileName(r.Mic));
                Check(File.Exists(sysKept) && Sha256File(sysKept) == r.SysSha, "el sonido del equipo no se ha conservado igual");
                Check(File.Exists(micKept) && Sha256File(micKept) == r.MicSha, "el micro no se ha conservado igual");
            }
            Check(!File.Exists(H2Output(r)), "queda un master en la carpeta de salida tras un fallo");
        }

        static float[] H2Constant(long frames, float value)
        {
            float[] s = new float[(int)(frames * 2)];
            for (int i = 0; i < s.Length; i++) s[i] = value;
            return s;
        }

        // Starts with NaN (quiet and signalling, with payloads), infinities, a negative zero and a denormal.
        static float[] H2NonFinite(long frames)
        {
            uint[] pattern = { 0x7FC00000u, 0xFFC00123u, 0x7F800000u, 0xFF800000u, 0x7FA00001u, 0x00000001u, 0x80000001u, 0x3F800000u };
            float[] s = new float[(int)(frames * 2)];
            for (int i = 0; i < s.Length; i++) s[i] = FromBits(pattern[i % pattern.Length]);
            return s;
        }

        static void H2Peaks(float[] s, out int nonFinite, out float peak)
        {
            nonFinite = 0;
            peak = 0;
            foreach (float x in s)
            {
                if (float.IsNaN(x) || float.IsInfinity(x)) { nonFinite++; continue; }
                float a = Math.Abs(x);
                if (a > peak) peak = a;
            }
        }

        // ---- sizes, frame counts and timestamps (the real pipeline, synthetic frames) ----

        static void Hunt2Sizes()
        {
            RoundtripTest("hunt2cine", "size-2x2-both-audio", MakePlan(2, 2, 60, 1, 30, "gradient", "both", true));
            RoundtripTest("hunt2cine", "size-1x1-sys-audio", MakePlan(1, 1, 60, 1, 30, "gradient", "sys", true));
            RoundtripTest("hunt2cine", "size-4097x3-odd-pixels", MakePlan(4097, 3, 60, 1, 20, "bars", "both", true));
            RoundtripTest("hunt2cine", "size-16x8192-tall", MakePlan(16, 8192, 60, 1, 5, "lines", "none", true));
            RoundtripTest("hunt2cine", "frames-1-both-audio", MakePlan(64, 48, 60, 1, 1, "text", "both", true));
            RoundtripTest("hunt2cine", "frames-2-ntsc-both-audio", MakePlan(64, 48, 30000, 1001, 2, "text", "both", true));
            RoundtripTest("hunt2cine", "frames-3-ntsc-sys-audio", MakePlan(64, 48, 30000, 1001, 3, "moving", "sys", true));
            RoundtripTest("hunt2cine", "ten-minutes-16x16-60fps", MakePlan(16, 16, 60, 1, 36000, "moving", "none", false));
            RoundtripTest("hunt2cine", "two-minutes-ntsc-16x16-both-audio", MakePlan(16, 16, 30000, 1001, 3603, "moving", "both", false));
            RoundtripTest("hunt2cine", "ten-minutes-ntsc-16x16-no-audio", MakePlan(16, 16, 30000, 1001, 17982, "moving", "none", false));

            Test("hunt2cine", "audio-frames-exact-ntsc", () =>
            {
                long a = Cine.AudioFramesFor(60000, 60000, 1001);
                Check(a == 48048000, "60000 fotogramas a 60000/1001: " + a + " muestras, esperadas 48048000");
                long b = Cine.AudioFramesFor(1, 60, 1);
                Check(b == 800, "1 fotograma a 60 fps: " + b + " muestras, esperadas 800");
                int checkedCount = 0;
                for (long k = 1; k <= 200000; k += 997)
                {
                    double exact = k * 1001.0 / 60000.0 * 48000.0;
                    long got = Cine.AudioFramesFor(k, 60000, 1001);
                    Check(Math.Abs(got - exact) <= 0.5 + 1e-6, "fotogramas " + k + ": " + got + " muestras, exactas " + exact.ToString("0.###", CultureInfo.InvariantCulture));
                    checkedCount++;
                }
                Put("points", checkedCount);
            });
        }

        // ---- sound: one track, two, lengths, silence, clipping, non-finite values ----

        static void Hunt2Audio()
        {
            Test("hunt2cine", "audio-silence-both-tracks", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-silence");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 3);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32");
                    H2WriteFloats(sys, H2Constant(s, 0f));
                    H2WriteFloats(mic, H2Constant(s, 0f));
                    CineVerification v = H2Master(nut, sys, mic, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(v.Passed, "no verifica: " + FailingChecks(v));
                    float[] mix = H2DecodeFloats(master, 0);
                    int nonZero = 0;
                    foreach (float x in mix) if (x != 0f) nonZero++;
                    Check(nonZero == 0, nonZero + " muestras distintas de cero en la mezcla de silencio");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-nonfinite-originals-bit-exact", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-nan-orig");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 4);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32");
                    H2WriteFloats(sys, H2NonFinite(s));
                    H2WriteFloats(mic, AudioSamples(s, 5));
                    CineVerification v = H2Master(nut, sys, mic, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(v.Passed, "no verifica (los originales deben ser bit a bit): " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-nonfinite-input-mix-is-finite", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-nan-mix");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 4);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32");
                    H2WriteFloats(sys, H2NonFinite(s));
                    H2WriteFloats(mic, AudioSamples(s, 5));
                    CineVerification v = H2Master(nut, sys, mic, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Put("verifierPassed", v.Passed);
                    float[] mix = H2DecodeFloats(master, 0);
                    int nonFinite;
                    float peak;
                    H2Peaks(mix, out nonFinite, out peak);
                    Put("mixNonFinite", nonFinite);
                    Put("mixPeak", Math.Round(peak, 4));
                    Check(nonFinite == 0, nonFinite + " de " + mix.Length + " muestras de la mezcla son NaN o infinito (el verificador " +
                                          (v.Passed ? "lo da por bueno" : "lo rechaza") + ")");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-clipping-mix-within-range", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-clip");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 6);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    float[] sysS = new float[(int)(s * 2)], micS = H2Constant(s, 1.2f);
                    for (long i = 0; i < s; i++)
                    {
                        float x = i < s / 3 ? 1.5f : (i < 2 * s / 3 ? -2.0f : 0.5f);
                        sysS[2 * i] = x;
                        sysS[2 * i + 1] = -x;
                    }
                    string sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32");
                    H2WriteFloats(sys, sysS);
                    H2WriteFloats(mic, micS);
                    CineVerification v = H2Master(nut, sys, mic, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(v.Passed, "no verifica: " + FailingChecks(v));
                    float[] mix = H2DecodeFloats(master, 0);
                    int nonFinite;
                    float peak;
                    H2Peaks(mix, out nonFinite, out peak);
                    Put("mixPeak", Math.Round(peak, 4));
                    Check(nonFinite == 0 && peak <= 1.0f, "la mezcla llega a " + peak.ToString("0.000", CultureInfo.InvariantCulture) + " (sin recorte esperado, <= 1)");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-one-sample-track-detected", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-one-sample");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 8);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32");
                    H2WriteFloats(sys, AudioSamples(1, 8));
                    CineVerification v = H2Master(nut, sys, null, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(!v.Passed, "una pista de 1 muestra (esperadas " + s + ") se da por buena");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-one-sample-short-detected", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-short");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 8);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32");
                    H2WriteFloats(sys, AudioSamples(s - 1, 8));
                    CineVerification v = H2Master(nut, sys, null, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(!v.Passed, "una muestra menos de las grabadas se da por buena");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-one-sample-long-detected", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-long");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 8);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32");
                    H2WriteFloats(sys, AudioSamples(s + 1, 8));
                    CineVerification v = H2Master(nut, sys, null, h, 16, 16, 60, 1, s, master, out err);
                    Check(err == null, "Build: " + err);
                    Check(!v.Passed, "una muestra de mas de las grabadas se da por buena");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "audio-tracks-different-lengths-detected", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-lengths");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 16, 16, 60, 1, 60, "moving", false, 9);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    string sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32");
                    H2WriteFloats(sys, AudioSamples(s, 9));
                    H2WriteFloats(mic, AudioSamples(s / 2, 10));
                    CineVerification v = H2Master(nut, sys, mic, h, 16, 16, 60, 1, s, master, out err);
                    if (err != null) { Note("Build se niega: " + OneLine(err)); return; }
                    Check(!v.Passed, "dos pistas de distinta longitud se dan por buenas");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });
        }

        // ---- the frame hash: bgr0 pixels, the fourth byte ignored, odd pixel counts ----

        static void Hunt2Hash()
        {
            Test("hunt2cine", "hash-odd-pixels-ignore-x-byte", () =>
            {
                int[][] sizes = { new[] { 1, 1 }, new[] { 3, 3 }, new[] { 5, 1 }, new[] { 4097, 3 }, new[] { 2, 2 } };
                Random rnd = new Random(77);
                foreach (int[] sz in sizes)
                {
                    int w = sz[0], h = sz[1], n = w * h, bytes = n * 4;
                    string tag = w + "x" + h;
                    byte[] a = new byte[bytes];
                    rnd.NextBytes(a);
                    byte[] b = (byte[])a.Clone();
                    for (int i = 3; i < bytes; i += 4) b[i] = (byte)rnd.Next(256);
                    ulong ha = CineHash.Bgr0(a, w, h);
                    Check(CineHash.Bgr0(b, w, h) == ha, tag + ": el cuarto byte cambia el hash (byte[])");
                    IntPtr pa = Marshal.AllocHGlobal(bytes), pb = Marshal.AllocHGlobal(bytes);
                    try
                    {
                        Marshal.Copy(a, 0, pa, bytes);
                        Marshal.Copy(b, 0, pb, bytes);
                        long[] sa = null, sb = null;
                        ulong pha = CineHash.Bgr0(pa, w, h, ref sa), phb = CineHash.Bgr0(pb, w, h, ref sb);
                        Check(pha == phb, tag + ": el cuarto byte cambia el hash (IntPtr)");
                        Check(pha == ha, tag + ": IntPtr y byte[] dan hashes distintos");
                        byte[] planes = new byte[3 * n];
                        for (int i = 0; i < n; i++)
                        {
                            planes[i] = a[4 * i + 1];
                            planes[n + i] = a[4 * i];
                            planes[2 * n + i] = a[4 * i + 2];
                        }
                        Check(CineHash.Gbrp(planes, w, h) == ha, tag + ": el hash de gbrp no coincide con el de bgr0");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pa);
                        Marshal.FreeHGlobal(pb);
                    }
                    byte[] c = (byte[])a.Clone();
                    c[bytes - 2] ^= 0x01;   // red of the last pixel: must count
                    Check(CineHash.Bgr0(c, w, h) != ha, tag + ": un cambio en el rojo del ultimo pixel no cambia el hash");
                }
            });

            Test("hunt2cine", "pipeline-garbage-x-byte-verifies", () =>
            {
                NeedFfmpeg();
                int[][] sizes = { new[] { 16, 16 }, new[] { 3, 3 } };
                foreach (int[] sz in sizes)
                {
                    int w = sz[0], h = sz[1];
                    string dir = WorkDir("h2-garbage-" + w + "x" + h);
                    try
                    {
                        List<ulong> same = H2Encode(Path.Combine(dir, "two.nut"), w, h, 60, 1, 2, "white", true, 12);
                        Check(same.Count == 2 && same[0] == same[1], w + "x" + h + ": dos fotogramas con el mismo RGB y distinto cuarto byte dan hashes distintos");
                        string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), err;
                        List<ulong> hashes = H2Encode(nut, w, h, 60, 1, 30, "gradient", true, 12);
                        CineVerification v = H2Master(nut, null, null, hashes, w, h, 60, 1, 0, master, out err);
                        Check(err == null, w + "x" + h + " Build: " + err);
                        Check(v.Passed, w + "x" + h + " no verifica: " + FailingChecks(v));
                    }
                    finally { DeleteDir(dir); }
                }
            });

            Test("hunt2cine", "hash-random-frames-and-bit-flips", () =>
            {
                Random rnd = new Random(2026);
                int w = 16, h = 16, n = w * h;
                byte[] a = new byte[n * 4];
                HashSet<ulong> seen = new HashSet<ulong>();
                int duplicates = 0;
                for (int t = 0; t < 4000; t++)
                {
                    rnd.NextBytes(a);
                    ulong hv = CineHash.Bgr0(a, w, h);
                    if (!seen.Add(hv)) duplicates++;
                    int px = rnd.Next(n), ch = rnd.Next(3);   // blue, green or red only
                    byte[] b = (byte[])a.Clone();
                    b[4 * px + ch] ^= (byte)(1 << rnd.Next(8));
                    Check(CineHash.Bgr0(b, w, h) != hv, "un bit de color no cambia el hash (fotograma " + t + ")");
                }
                Check(duplicates == 0, duplicates + " colisiones entre 4000 fotogramas aleatorios");
                Put("frames", 4000);
            });
        }

        // ---- failures: nothing recorded is lost, and a broken master never verifies ----

        static void Hunt2Failures()
        {
            Test("hunt2cine", "truncated-nut-never-verifies", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-trunc-nut");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), cut = Path.Combine(dir, "cut.nut"), master = Path.Combine(dir, "m.mkv"), err;
                    List<ulong> h = H2Encode(nut, 64, 48, 60, 1, 120, "moving", false, 4);
                    byte[] all = File.ReadAllBytes(nut);
                    File.WriteAllBytes(cut, H2Head(all, all.Length / 2));
                    CineVerification v = H2Master(cut, null, null, h, 64, 48, 60, 1, 0, master, out err);
                    if (err != null) { Note("Build se niega: " + OneLine(err)); return; }
                    Check(!v.Passed, "un video cortado a la mitad se verifica como bueno");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "truncated-master-never-verifies", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-trunc-master");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), sys = Path.Combine(dir, "sys.f32"), mic = Path.Combine(dir, "mic.f32"), err;
                    List<ulong> h = H2Encode(nut, 64, 48, 60, 1, 120, "moving", false, 5);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    H2WriteFloats(sys, AudioSamples(s, 41));
                    H2WriteFloats(mic, AudioSamples(s, 42));
                    CineVerification good = H2Master(nut, sys, mic, h, 64, 48, 60, 1, s, master, out err);
                    Check(err == null && good.Passed, "la base no verifica: " + (err ?? FailingChecks(good)));
                    byte[] all = File.ReadAllBytes(master);
                    int[] percents = { 99, 90, 50 };
                    foreach (int pct in percents)
                    {
                        string cutFile = Path.Combine(dir, "cut" + pct + ".mkv");
                        File.WriteAllBytes(cutFile, H2Head(all, (int)((long)all.Length * pct / 100)));
                        CineVerification v = new CineVerification();
                        CineExpect e = new CineExpect();
                        e.Width = 64;
                        e.Height = 48;
                        e.FpsNum = 60;
                        e.FpsDen = 1;
                        e.FrameHashes = h;
                        e.Audio = CineMaster.Tracks(sys, mic, s);
                        v = CineVerifier.Verify(ffmpeg, cutFile, e, null);
                        Check(!v.Passed, "un master cortado al " + pct + " % se verifica como bueno");
                    }
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "read-only-master-keeps-recording", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-ro", 64, 48, 60, 1, 60, "text", true, false);
                string temp = Path.Combine(Settings.TempDir, "~cine " + r.Stamp + Cine.Extension);
                try
                {
                    File.WriteAllBytes(temp, new byte[16]);
                    File.SetAttributes(temp, FileAttributes.ReadOnly);
                    H2Finish(r);
                    string err = r.S.ErrorText ?? "";
                    H2CheckKept(r, true);
                    Check(err.Contains("Lo grabado no se ha borrado") && err.Contains(Path.Combine(r.Recovered, r.Stamp)),
                          "el mensaje no dice donde esta lo grabado: " + OneLine(err));
                    Note(OneLine(err));
                }
                finally
                {
                    try { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); } catch { }
                    H2Cleanup(r);
                }
            });

            Test("hunt2cine", "killed-encoder-keeps-the-video", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-kill", 320, 180, 60, 1, 200, "moving", true, false);
                try
                {
                    r.Sink.Kill();
                    H2Finish(r);
                    string err = r.S.ErrorText ?? "";
                    Check(err.Length > 0, "no hay aviso tras matar el codificador");
                    H2CheckKept(r, true);
                    Note(OneLine(err));
                }
                finally { H2Cleanup(r); }
            });

            Test("hunt2cine", "cancelled-final-pass-keeps-everything", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-cancel", 64, 48, 60, 1, 60, "text", true, false);
                try
                {
                    r.S.CancelSave();
                    H2Finish(r);
                    H2CheckKept(r, true);
                    string err = r.S.ErrorText ?? "";
                    Check(err.Contains(PassWatch.CancelledText), "no dice que el guardado se cancelo: " + OneLine(err));
                }
                finally { H2Cleanup(r); }
            });

            Test("hunt2cine", "hash-mismatch-keeps-unverified-master", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-mismatch", 64, 48, 60, 1, 60, "text", true, false);
                try
                {
                    // One frame's hash no longer matches what was piped: the master must be kept as unverified, with the sources.
                    r.Sink.Hashes[5] = r.Sink.Hashes[5] ^ 1UL;
                    H2Finish(r);
                    string output = H2Output(r);
                    Check(output.Contains("(sin verificar)") && File.Exists(output), "el master no se guardo como sin verificar: " + output);
                    string kept = Path.Combine(r.Recovered, r.Stamp);
                    Check(File.Exists(Path.Combine(kept, Path.GetFileName(r.Nut))), "el video en vivo no se ha conservado");
                    Check(File.Exists(Path.Combine(kept, Path.GetFileName(r.Sys))) && File.Exists(Path.Combine(kept, Path.GetFileName(r.Mic))), "el sonido no se ha conservado");
                    string warning = (string)H2Get(r.S, "warning") ?? "";
                    Check(warning.Contains("diferencias"), "el aviso no dice que hay diferencias: " + OneLine(warning));
                    Dictionary<string, object> j = H2ReadReport(output);
                    Check(!Convert.ToBoolean(j["passed"]), "el informe dice que ha pasado");
                    Dictionary<string, object> v = (Dictionary<string, object>)j["verification"];
                    Check(Convert.ToInt64(v["firstMismatch"]) == 5, "primer fotograma distinto: " + v["firstMismatch"]);
                    bool named = false;
                    foreach (Dictionary<string, object> c in (System.Collections.IList)v["checks"])
                        if (!Convert.ToBoolean(c["passed"]) && ((string)c["name"]).StartsWith("Fotogramas", StringComparison.Ordinal)) named = true;
                    Check(named, "el informe no nombra el fallo de fotogramas");
                }
                finally { H2Cleanup(r); }
            });

            Test("hunt2cine", "build-cancelled-deletes-only-the-master", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("h2-build-cancel");
                try
                {
                    string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv"), sys = Path.Combine(dir, "sys.f32");
                    List<ulong> h = H2Encode(nut, 64, 48, 60, 1, 120, "moving", false, 4);
                    long s = Cine.AudioFramesFor(h.Count, 60, 1);
                    H2WriteFloats(sys, AudioSamples(s, 51));
                    string before = Sha256File(nut), beforeSys = Sha256File(sys);
                    using (PassWatch watch = new PassWatch(PassWatch.DefaultStallMs))
                    {
                        watch.Cancel();
                        string err = CineMaster.Build(ffmpeg, nut, sys, null, "", master, 2.0, null, watch);
                        Check(err == PassWatch.CancelledText, "Build cancelado devolvio: " + err);
                    }
                    Check(!File.Exists(master), "queda un master parcial tras cancelar");
                    Check(Sha256File(nut) == before && Sha256File(sys) == beforeSys, "cancelar el Build altero los archivos de trabajo");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "live-pipe-with-bom-preamble-verifies", () =>
            {
                NeedFfmpeg();
                Encoding saved = null;
                try
                {
                    saved = Console.InputEncoding;
                    Console.InputEncoding = new UTF8Encoding(true);
                }
                catch (Exception e) { Skip("no puedo cambiar la codificacion de consola: " + e.Message); }
                string dir = WorkDir("h2-bom");
                try
                {
                    Roundtrip rt = RunRoundtrip(MakePlan(64, 48, 60, 1, 30, "text", "none", false), dir);
                    RequirePassed(rt);
                    Note("preambulo de consola: " + Console.InputEncoding.GetPreamble().Length + " bytes");
                }
                finally
                {
                    try { if (saved != null) Console.InputEncoding = saved; } catch { }
                    DeleteDir(dir);
                }
            });

            Test("hunt2cine", "startup-rescue-moves-interrupted-master", () =>
            {
                string dir = WorkDir("h2-startup");
                try
                {
                    string tmp = Path.Combine(dir, "temp"), rec = Path.Combine(dir, "recuperado");
                    Directory.CreateDirectory(tmp);
                    string stamp = "2026-10-11 10.20.30";
                    string[] names = { "~grabando " + stamp + ".nut", "~grabando " + stamp + " equipo.f32", "~grabando " + stamp + " micro.f32", "~cine " + stamp + ".mkv" };
                    foreach (string nm in names)
                    {
                        string p = Path.Combine(tmp, nm);
                        File.WriteAllBytes(p, new byte[3000]);
                        File.SetLastWriteTime(p, DateTime.Now.AddHours(-1));
                    }
                    string fresh = Path.Combine(tmp, "~grabando 2026-10-11 11.00.00.nut");
                    File.WriteAllBytes(fresh, new byte[100]);
                    List<WorkFiles.Rescued> moved = WorkFiles.Rescue(tmp, rec, TimeSpan.FromSeconds(10), true);
                    Check(moved.Count == 1 && moved[0].Stamp == stamp, "rescates: " + moved.Count);
                    Check(moved[0].Files.Count == names.Length, "se han recuperado " + moved[0].Files.Count + " de " + names.Length + " archivos");
                    Check(moved[0].Started == new DateTime(2026, 10, 11, 10, 20, 30), "hora de la grabacion: " + moved[0].Started);
                    foreach (string nm in names)
                        Check(File.Exists(Path.Combine(rec, stamp, nm)), "no esta en recuperado: " + nm);
                    Check(File.Exists(fresh), "una grabacion que acaba de escribirse se ha movido");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2cine", "startup-rescue-name-collision-keeps-both", () =>
            {
                string dir = WorkDir("h2-collide");
                try
                {
                    string tmp = Path.Combine(dir, "temp"), rec = Path.Combine(dir, "recuperado");
                    Directory.CreateDirectory(tmp);
                    string stamp = "2026-10-11 10.20.30";
                    string master = "~cine " + stamp + ".mkv";
                    File.WriteAllBytes(Path.Combine(tmp, master), new byte[3000]);
                    File.SetLastWriteTime(Path.Combine(tmp, master), DateTime.Now.AddHours(-1));
                    string kept = Path.Combine(rec, stamp);
                    Directory.CreateDirectory(kept);
                    byte[] old = Encoding.ASCII.GetBytes("ya estaba");
                    File.WriteAllBytes(Path.Combine(kept, master), old);
                    WorkFiles.Rescue(tmp, rec, TimeSpan.FromSeconds(10), true);
                    Check(File.ReadAllBytes(Path.Combine(kept, master)).Length == old.Length, "el archivo que ya estaba en recuperado se ha sobrescrito");
                    Check(Directory.GetFiles(kept).Length == 2, "se esperaban 2 archivos en recuperado y hay " + Directory.GetFiles(kept).Length);
                }
                finally { DeleteDir(dir); }
            });
        }

        // ---- the report JSON: its fields against the file ----

        static void Hunt2Report()
        {
            Test("hunt2cine", "report-json-matches-the-file", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-report", 64, 48, 60, 1, 120, "text", true, false);
                try
                {
                    H2Finish(r);
                    string output = H2Output(r);
                    Check(File.Exists(output), "no hay master: " + OneLine(r.S.ErrorText ?? ""));
                    Dictionary<string, object> j = H2ReadReport(output);
                    Check(Convert.ToBoolean(j["passed"]), "el informe dice que no ha pasado");
                    Check(string.Equals((string)j["file"], output, StringComparison.OrdinalIgnoreCase), "file del informe: " + j["file"] + ", fichero: " + output);
                    Check(Convert.ToInt64(j["fileBytes"]) == new FileInfo(output).Length, "fileBytes " + j["fileBytes"] + " != " + new FileInfo(output).Length);
                    Check(Convert.ToInt64(j["framesWritten"]) == 120, "framesWritten " + j["framesWritten"]);
                    Check(Convert.ToInt64(j["fpsNum"]) == 60 && Convert.ToInt64(j["fpsDen"]) == 1, "fps en el informe");
                    Check(Convert.ToInt64(j["width"]) == 64 && Convert.ToInt64(j["height"]) == 48, "tamano en el informe");
                    double rs = Convert.ToDouble(j["recordSeconds"], CultureInfo.InvariantCulture);
                    Check(Math.Abs(rs - 2.0) < 1e-6, "recordSeconds " + rs);
                    Check(j["stopReason"] == null, "stopReason en una grabacion normal: " + j["stopReason"]);
                    Check(Convert.ToBoolean(j["captureClean"]), "captureClean es falso");

                    Dictionary<string, object> v = (Dictionary<string, object>)j["verification"];
                    Check(Convert.ToBoolean(v["passed"]), "verification.passed es falso");
                    Check(Convert.ToInt64(v["framesDecoded"]) == 120 && Convert.ToInt64(v["framesMatched"]) == 120, "verification frames " + v["framesDecoded"] + "/" + v["framesMatched"]);
                    double vs = Convert.ToDouble(v["seconds"], CultureInfo.InvariantCulture);
                    Check(Math.Abs(vs - rs) <= 1.0 / 60 + 0.002, "verification.seconds " + vs + " frente a recordSeconds " + rs);

                    System.Collections.IList tracks = (System.Collections.IList)j["audio"];
                    Check(tracks.Count == 3, "pistas en el informe: " + tracks.Count);
                    long want = Cine.AudioFramesFor(120, 60, 1);
                    Dictionary<string, object> mix = (Dictionary<string, object>)tracks[0];
                    Dictionary<string, object> sys = (Dictionary<string, object>)tracks[1];
                    Dictionary<string, object> mic = (Dictionary<string, object>)tracks[2];
                    Check(!Convert.ToBoolean(mix["original"]) && Convert.ToBoolean(sys["original"]) && Convert.ToBoolean(mic["original"]), "marcas de originales");
                    foreach (Dictionary<string, object> t in tracks)
                        Check(Convert.ToInt64(t["frames"]) == want, "pista " + t["title"] + " con " + t["frames"] + " muestras, esperadas " + want);
                    Check((string)sys["sha256"] == r.SysSha, "sha256 del equipo en el informe distinto del fichero");
                    Check((string)mic["sha256"] == r.MicSha, "sha256 del micro en el informe distinto del fichero");
                    Put("bytes", new FileInfo(output).Length);
                }
                finally { H2Cleanup(r); }
            });

            Test("hunt2cine", "report-says-why-the-master-failed", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-why", 64, 48, 60, 1, 60, "text", true, false);
                string temp = Path.Combine(Settings.TempDir, "~cine " + r.Stamp + Cine.Extension);
                try
                {
                    File.WriteAllBytes(temp, new byte[16]);
                    File.SetAttributes(temp, FileAttributes.ReadOnly);
                    H2Finish(r);
                    string json = File.ReadAllText(H2ReportPath(H2Output(r)), Encoding.UTF8);
                    Check(json.Contains("pudo crear el"), "el informe no dice por que no hay master (solo el aviso en pantalla y el log)");
                }
                finally
                {
                    try { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); } catch { }
                    H2Cleanup(r);
                }
            });

            Test("hunt2cine", "report-points-to-the-file-when-failed", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-pointer", 64, 48, 60, 1, 60, "text", true, false);
                string temp = Path.Combine(Settings.TempDir, "~cine " + r.Stamp + Cine.Extension);
                try
                {
                    File.WriteAllBytes(temp, new byte[16]);
                    File.SetAttributes(temp, FileAttributes.ReadOnly);
                    H2Finish(r);
                    Dictionary<string, object> j = H2ReadReport(H2Output(r));
                    string file = (string)j["file"];
                    string json = File.ReadAllText(H2ReportPath(H2Output(r)), Encoding.UTF8);
                    string kept = r.Recovered.Replace("\\", "\\\\");   // as JSON writes a backslash
                    Check(File.Exists(file) || json.Contains(kept),
                          "el informe apunta a " + file + ", que no existe, y no dice donde estan los archivos rescatados");
                }
                finally
                {
                    try { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); } catch { }
                    H2Cleanup(r);
                }
            });

            Test("hunt2cine", "report-frames-match-the-rescued-video", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-killcount", 320, 180, 60, 1, 200, "moving", false, false);
                try
                {
                    r.Sink.Kill();
                    H2Finish(r);
                    Dictionary<string, object> j = H2ReadReport(H2Output(r));
                    long written = Convert.ToInt64(j["framesWritten"]);
                    string kept = Path.Combine(r.Recovered, r.Stamp, Path.GetFileName(r.Nut));
                    Check(File.Exists(kept), "el video no se ha conservado");
                    string err;
                    int code;
                    List<string> lines = FfmpegLines("-hide_banner -nostdin -loglevel error -i " + Q(kept) + " -map 0:v:0 -c copy -f framecrc -", out err, out code);
                    long inFile = 0;
                    foreach (string line in lines) if (line.Length > 0 && line[0] != '#') inFile++;
                    Put("framesWritten", written);
                    Put("framesInFile", inFile);
                    // framesWritten counts what reached the encoder; an encoder killed mid-way keeps fewer in its file
                    // (decided: not counted again on the failure path). What the report must say is that it failed and
                    // where the recording is.
                    Check(inFile > 0 && inFile <= written, "el video rescatado tiene " + inFile + " fotogramas de " + written);
                    Check(j["error"] != null && Convert.ToString(j["rescued"]).Length > 0, "el informe no dice el error ni d\u00F3nde est\u00E1 lo grabado");
                }
                finally { H2Cleanup(r); }
            });
        }

        // ---- disk: the reserve kept while recording against what the final pass writes ----

        static void Hunt2Disk()
        {
            Test("hunt2cine", "disk-estimate-audio-part-matches-files", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-est", 16, 16, 60, 1, 180, "moving", true, false);
                try
                {
                    int code;
                    Check(r.Sink.Close(120000, out code) && code == 0, "ffmpeg no cerro el video");
                    long v = new FileInfo(r.Nut).Length, a = new FileInfo(r.Sys).Length + new FileInfo(r.Mic).Length;
                    H2Finish(r);
                    string output = H2Output(r);
                    long m = new FileInfo(output).Length;
                    double seconds = r.N / 60.0;
                    double measuredAudio = (a + (m - v)) / seconds;   // work sound files plus the sound copied into the master
                    DiskBudget.Estimate e = DiskBudget.For(Recorder.Cinema, false, 16, 16, 60, 2, 1L << 40);
                    double pixels = 16.0 * 16 * 60;
                    double estimatedAudio = e.TypicalPerSecond - pixels * 0.06 * 2.05;
                    Put("measuredAudioPerSecond", Math.Round(measuredAudio));
                    Put("estimatedAudioPerSecond", Math.Round(estimatedAudio));
                    Check(Math.Abs(measuredAudio - estimatedAudio) / estimatedAudio < 0.01,
                          "sonido por segundo medido " + measuredAudio.ToString("0", CultureInfo.InvariantCulture) + " frente a estimado " + estimatedAudio.ToString("0", CultureInfo.InvariantCulture));
                }
                finally { H2Cleanup(r); }
            });

            Test("hunt2cine", "disk-reserve-covers-final-pass-two-tracks", () =>
            {
                NeedFfmpeg();
                H2Rec r = H2Recording("h2-reserve", 16, 16, 60, 1, 180, "moving", true, false);
                try
                {
                    int code;
                    Check(r.Sink.Close(120000, out code) && code == 0, "ffmpeg no cerro el video");
                    long sysB = new FileInfo(r.Sys).Length, micB = new FileInfo(r.Mic).Length;
                    long v = new FileInfo(r.Nut).Length, a = sysB + micB, mix = Math.Max(sysB, micB);
                    H2Finish(r);
                    string output = H2Output(r);
                    Check(File.Exists(output), "no hay master: " + OneLine(r.S.ErrorText ?? ""));
                    long m = new FileInfo(output).Length;
                    double seconds = r.N / 60.0;
                    double extraPerSecond = (m - v - a) / seconds;   // what the final pass writes beyond the work files
                    long need = DiskBudget.Need(true, false, false, v, a, mix);   // as Recorder.CheckHealth asks, with the mix the master adds
                    long old = DiskBudget.Need(true, false, false, v, a);
                    Put("extraPerSecond", Math.Round(extraPerSecond));
                    // The master is a new file written while the work files are still there: the free space kept must hold
                    // all of it. Every term of need grows with the recording as fast as the master does (video x1.05, both
                    // originals, the mix), so holding it now holds it at any length; before, the mix was missing and only
                    // the 512 MiB reserve covered it (about 23 minutes of stereo float).
                    double oldMinutes = (old - (v + a)) / extraPerSecond / 60.0;
                    Put("oldCoveredMinutes", Math.Round(oldMinutes, 1));
                    Check(need >= m, "la reserva (" + need + " bytes) no cubre el m\u00E1ster (" + m + " bytes)");
                    Check(need - old == mix && mix > 0, "la reserva no cuenta la pista de mezcla");
                }
                finally { H2Cleanup(r); }
            });
        }
    }
}

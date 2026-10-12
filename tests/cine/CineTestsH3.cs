// Clipping warning, hunt round 3 (suite "h3cine"): what is counted and when it is timed, fed through AudioCapture.ForTest
// (no device, no screen). The device conversion is reached through the private Packet (ToStereo + Feed), as Drain does.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static void H3CineSuite()
        {
            Test("h3cine", "gap-before-clip-timed-on-timeline", H3GapTime);
            Test("h3cine", "late-first-packet-timed-on-timeline", H3LateStart);
            Test("h3cine", "clip-before-start-not-counted", H3PreStart);
            Test("h3cine", "overlap-dropped-not-counted-again", H3Overlap);
            Test("h3cine", "clip-after-end-trimmed-not-counted", H3TailTrim);
            Test("h3cine", "hovering-near-full-scale-is-one-moment", H3Hover);
            Test("h3cine", "nan-inf-through-limiter-stay-finite", H3NanLimiter);
            Test("h3cine", "inf-written-as-is-and-counted", H3Inf);
            Test("h3cine", "int16-stereo-full-scale-counted", H3Int16);
            Test("h3cine", "int16-mono-full-scale-counted", H3Int16Mono);
            Test("h3cine", "int24-full-scale-counted", H3Int24);
            Test("h3cine", "muted-device-packets-not-counted", H3Muted);
            Test("h3cine", "dc-offset-observation", H3Dc);
            Test("h3cine", "clip-time-text-over-an-hour", H3Hours);
            Test("h3cine", "count-clipping-speed", H3Speed);
        }

        // ---- helpers ----

        static float[] H3Flat(int frames, float value)
        {
            float[] s = new float[frames * 2];
            for (int i = 0; i < s.Length; i++) s[i] = value;
            return s;
        }

        static MethodInfo H3Method(string name)
        {
            MethodInfo m = typeof(AudioCapture).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Check(m != null, "no existe AudioCapture." + name);
            return m;
        }

        static void H3SetFormat(AudioCapture ac, int channels, int bits, bool isFloat)
        {
            H3Field(ac, "channels").SetValue(ac, channels);
            H3Field(ac, "srcRate").SetValue(ac, 48000);
            H3Field(ac, "bits").SetValue(ac, bits);
            H3Field(ac, "isFloat").SetValue(ac, isFloat);
        }

        static FieldInfo H3Field(AudioCapture ac, string name)
        {
            FieldInfo f = typeof(AudioCapture).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "no existe AudioCapture." + name);
            return f;
        }

        // Raw device bytes through the private Packet: conversion to 48 kHz stereo float, then Feed.
        static void H3Packet(AudioCapture ac, byte[] raw, int frames, long when)
        {
            GCHandle h = GCHandle.Alloc(raw, GCHandleType.Pinned);
            try { H3Method("Packet").Invoke(ac, new object[] { h.AddrOfPinnedObject(), frames, 0, when }); }
            catch (TargetInvocationException e) { throw e.InnerException; }
            finally { h.Free(); }
        }

        static byte[] H3FloatBytes(float[] s)
        {
            byte[] b = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, b, 0, b.Length);
            return b;
        }

        static float[] H3ReadFloats(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            float[] f = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
            return f;
        }

        // First stereo frame of the file whose left sample is at full scale (0.999 or more), or -1.
        static long H3FirstLoud(float[] f)
        {
            int frames = f.Length / 2;
            for (int i = 0; i < frames; i++) if (Math.Abs(f[2 * i]) >= 0.999f) return i;
            return -1;
        }

        static int H3NonFinite(float[] f, int fromFrame)
        {
            int n = 0;
            for (int i = fromFrame * 2; i < f.Length; i++) if (float.IsNaN(f[i]) || float.IsInfinity(f[i])) n++;
            return n;
        }

        // ---- timeline placement of the warning ----

        // 0.1 s of silence, then a full-scale 10 ms packet 0.4 s later (a gap is inserted before it).
        static void H3GapTime()
        {
            string dir = WorkDir("h3-gap");
            try
            {
                string path = TempAudioPath(dir, "gap");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H3Flat(4800, 0f), 4800, false, 0);
                ac.Feed(H3Flat(480, 1f), 480, false, 5000000);
                ac.Finish(24480);
                long first = H3FirstLoud(H3ReadFloats(path));
                double inFile = first / 48000.0;
                Note("en el fichero el clip empieza a " + Inv(inFile) + " s; FirstClipSeconds = " + Inv(ac.FirstClipSeconds) + " s");
                Check(first >= 0, "el fichero no tiene el clip");
                Check(Math.Abs(ac.FirstClipSeconds - inFile) < 0.002,
                      "primer clip a " + Inv(ac.FirstClipSeconds) + " s, en el fichero esta a " + Inv(inFile) + " s (el silencio insertado no cuenta)");
            }
            finally { DeleteDir(dir); }
        }

        // The first sound of the recording starts at 2.0 s: the silence before it is written after the count.
        static void H3LateStart()
        {
            string dir = WorkDir("h3-late");
            try
            {
                string path = TempAudioPath(dir, "late");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H3Flat(480, 1f), 480, false, 20000000);
                ac.Finish(96480);
                long first = H3FirstLoud(H3ReadFloats(path));
                double inFile = first / 48000.0;
                Note("en el fichero " + Inv(inFile) + " s; FirstClipSeconds = " + Inv(ac.FirstClipSeconds) + " s");
                Check(Math.Abs(ac.FirstClipSeconds - inFile) < 0.002,
                      "primer clip a " + Inv(ac.FirstClipSeconds) + " s, en el fichero a " + Inv(inFile) + " s");
            }
            finally { DeleteDir(dir); }
        }

        // The first 10 ms of a packet are before the recording starts (skipped): a clip only there is not in the file.
        static void H3PreStart()
        {
            string dir = WorkDir("h3-prestart");
            try
            {
                string path = TempAudioPath(dir, "prestart");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                float[] s = new float[960 * 2];
                for (int i = 100; i < 200; i++) { s[2 * i] = 1f; s[2 * i + 1] = 1f; }
                ac.Feed(s, 960, false, -100000);
                ac.Finish(480);
                long loud = H3FirstLoud(H3ReadFloats(path));
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + "; en el fichero: " + (loud < 0 ? "ningun clip" : "clip"));
                Check(loud < 0, "el fichero tiene un clip que no debia estar");
                Check(ac.ClipEvents == 0, "el fichero no tiene clip y el aviso cuenta " + ac.ClipEvents + " momento(s), " + ac.ClipFrames + " muestras");
            }
            finally { DeleteDir(dir); }
        }

        // The same 0.1 s packet twice: the second copy is dropped as an overlap, its clipping must not be counted again.
        static void H3Overlap()
        {
            string dir = WorkDir("h3-overlap");
            try
            {
                string path = TempAudioPath(dir, "overlap");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                float[] full = H3Flat(4800, 1f);
                ac.Feed(full, 4800, false, 0);
                ac.Feed(full, 4800, false, 0);
                ac.Finish(4800);
                Note("OverlapFrames " + ac.OverlapFrames + ", ClipFrames " + ac.ClipFrames + " con 4800 fotogramas en el fichero");
                Check(ac.OverlapFrames == 4800, "la segunda copia no se descarto como solapamiento: " + ac.OverlapFrames);
                Check(ac.ClipFrames <= 4800, "ClipFrames " + ac.ClipFrames + " con 4800 fotogramas en el fichero");
            }
            finally { DeleteDir(dir); }
        }

        // A clip in the last packet, which Finish trims away (the recording ends before it): not in the file, not counted.
        static void H3TailTrim()
        {
            string dir = WorkDir("h3-tail");
            try
            {
                string path = TempAudioPath(dir, "tail");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H3Flat(4800, 0f), 4800, false, 0);
                ac.Feed(H3Flat(480, 1f), 480, false, 1000000);
                ac.Finish(4800);
                Note("TrimmedFrames " + ac.TrimmedFrames + ", ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 0, "un clip recortado al final de la grabacion cuenta " + ac.ClipEvents + " momento(s)");
            }
            finally { DeleteDir(dir); }
        }

        // A passage of 0.2 s that stays at 0.998..1.0 (a limiter ceiling, a loud master): a single moment, in principle.
        static void H3Hover()
        {
            string dir = WorkDir("h3-hover");
            try
            {
                string path = TempAudioPath(dir, "hover");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                Random rnd = new Random(7);
                float[] s = new float[9600 * 2];
                for (int i = 0; i < 9600; i++)
                {
                    float v = (float)(0.998 + 0.002 * rnd.NextDouble());
                    s[2 * i] = v;
                    s[2 * i + 1] = v;
                }
                ac.Feed(s, 9600, false, 0);
                ac.Finish(9600);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + " en 0.2 s");
                Check(ac.ClipEvents <= 3, "0.2 s entre 0.998 y 1.0 cuentan " + ac.ClipEvents + " momentos (cada hueco bajo 0.999 abre uno nuevo)");
            }
            finally { DeleteDir(dir); }
        }

        // NaN and Infinity through the real master graph: the limiter of the mix (Recorder.FinishArgs, Cine MasterArgs).
        static void H3NanLimiter()
        {
            NeedFfmpeg();
            string dir = WorkDir("h3-nan");
            try
            {
                const int n = 96000;   // 2 s
                string[] kinds = { "nan", "inf" };
                foreach (string kind in kinds)
                {
                    float bad = kind == "nan" ? float.NaN : float.PositiveInfinity;
                    byte[] raw = new byte[n * 8];
                    for (int i = 0; i < n; i++)
                    {
                        float v = i == 24000 ? bad : (float)(0.1 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
                        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, raw, i * 8, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, raw, i * 8 + 4, 4);
                    }
                    string inFile = Path.Combine(dir, kind + "-in.f32");
                    string outFile = Path.Combine(dir, kind + "-out.f32");
                    File.WriteAllBytes(inFile, raw);
                    string err;
                    int code = Ffmpeg("-hide_banner -loglevel error -y -f f32le -ar 48000 -ch_layout stereo -i " + Q(inFile) +
                                      " -af \"alimiter=limit=0.891:attack=5:release=60:level=0:latency=1\"" +
                                      " -f f32le -ar 48000 -ch_layout stereo " + Q(outFile), out err);
                    Check(code == 0 && File.Exists(outFile), kind + ": ffmpeg " + code + " " + err);
                    float[] o = H3ReadFloats(outFile);
                    int after = H3NonFinite(o, 24001);
                    Note(kind + ": muestras no finitas tras el defecto en el limitador: " + after + " de " + (n - 24001) * 2);
                    Check(after == 0, kind + ": el limitador deja " + after + " muestras no finitas despues de una sola muestra " + kind + " de la tarjeta");
                }
            }
            finally { DeleteDir(dir); }
        }

        // Infinity is written as it came and counted as clipping; NaN is neither (it breaks the run).
        static void H3Inf()
        {
            string dir = WorkDir("h3-inf");
            try
            {
                string path = TempAudioPath(dir, "inf");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Feed(H3Flat(300, float.PositiveInfinity), 300, false, 0);
                ac.Finish(300);
                int nonFinite = H3NonFinite(H3ReadFloats(path), 0);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + ", no finitas en el fichero " + nonFinite);
                Check(nonFinite == 600, "la muestra infinita no llega igual al fichero: " + nonFinite + " de 600");
            }
            finally { DeleteDir(dir); }
        }

        // ---- device formats through the conversion ----

        static void H3Int16()
        {
            string dir = WorkDir("h3-int16");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "int16"), true);
                H3SetFormat(ac, 2, 16, false);
                byte[] raw = new byte[300 * 4];
                for (int i = 0; i < 300; i++)
                {
                    BitConverter.GetBytes((short)32767).CopyTo(raw, i * 4);
                    BitConverter.GetBytes((short)-32768).CopyTo(raw, i * 4 + 2);
                }
                H3Packet(ac, raw, 300, 0);
                ac.Finish(300);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 298, "32767 / -32768 en 16 bits: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 298");
            }
            finally { DeleteDir(dir); }
        }

        static void H3Int16Mono()
        {
            string dir = WorkDir("h3-mono");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "mono"), true);
                H3SetFormat(ac, 1, 16, false);
                byte[] raw = new byte[300 * 2];
                for (int i = 0; i < 300; i++) BitConverter.GetBytes((short)32767).CopyTo(raw, i * 2);
                H3Packet(ac, raw, 300, 0);
                ac.Finish(300);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 298, "mono 16 bits a fondo de escala: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 298");
            }
            finally { DeleteDir(dir); }
        }

        static void H3Int24()
        {
            string dir = WorkDir("h3-int24");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "int24"), true);
                H3SetFormat(ac, 2, 24, false);
                // Left at 0x7FFFFF (full scale), right at 0x7F0000 (0.992): only the left one counts.
                byte[] raw = new byte[300 * 6];
                for (int i = 0; i < 300; i++)
                {
                    int o = i * 6;
                    raw[o] = 0xFF; raw[o + 1] = 0xFF; raw[o + 2] = 0x7F;
                    raw[o + 3] = 0x00; raw[o + 4] = 0x00; raw[o + 5] = 0x7F;
                }
                H3Packet(ac, raw, 300, 0);
                ac.Finish(300);
                Note("ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames);
                Check(ac.ClipEvents == 1 && ac.ClipFrames == 298, "24 bits a fondo de escala: " + ac.ClipEvents + " / " + ac.ClipFrames + ", esperado 1 / 298");
            }
            finally { DeleteDir(dir); }
        }

        static void H3Muted()
        {
            string dir = WorkDir("h3-muted");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "muted"), true);
                H3SetFormat(ac, 2, 32, true);
                ac.Muted = true;
                byte[] raw = H3FloatBytes(H3Flat(480, 1f));
                for (int p = 0; p < 20; p++) H3Packet(ac, raw, 480, p * PacketTicks);
                ac.Finish(20L * 480);
                Check(ac.ClipEvents == 0 && ac.ClipFrames == 0, "un equipo silenciado cuenta " + ac.ClipEvents + " / " + ac.ClipFrames);
            }
            finally { DeleteDir(dir); }
        }

        // ---- observations (documented, not failures) ----

        // A constant DC offset of 0.9995 is held at full scale by the rule (no dips): one moment of 1 s.
        static void H3Dc()
        {
            string dir = WorkDir("h3-dc");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "dc"), true);
                ac.Feed(H3Flat(48000, 0.9995f), 48000, false, 0);
                ac.Finish(48000);
                Note("DC 0.9995 durante 1 s: ClipEvents " + ac.ClipEvents + ", ClipFrames " + ac.ClipFrames + " -> " + Session.ClipText(ac));
                Check(ac.ClipEvents == 1, "DC: " + ac.ClipEvents + " momentos");
            }
            finally { DeleteDir(dir); }
        }

        // ---- text and speed ----

        // 1 h 5 min: the notice prints minutes past 60 ("65:00"), not "1:05:00".
        static void H3Hours()
        {
            string dir = WorkDir("h3-hours");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "hours"), true);
                ac.Finish(0);
                ac.ClipEvents = 1;
                ac.ClipFrames = 48000;
                ac.FirstClipSeconds = 3900;
                string t = Session.ClipText(ac);
                Note(t);
                Check(t.Contains("1:05:00"), "el primero a 65:00 en vez de 1:05:00: " + t);
            }
            finally { DeleteDir(dir); }
        }

        static void H3Speed()
        {
            string dir = WorkDir("h3-speed");
            try
            {
                AudioCapture ac = AudioCapture.ForTest(TempAudioPath(dir, "speed"), true);
                Random rnd = new Random(3);
                float[] buf = new float[48000 * 2];
                for (int i = 0; i < buf.Length; i++) buf[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.5f;
                const int seconds = 600;
                Stopwatch sw = Stopwatch.StartNew();
                for (int k = 0; k < seconds; k++) ac.CountClipping(buf, 0, 48000);
                sw.Stop();
                double ns = sw.Elapsed.TotalMilliseconds * 1e6 / (seconds * 48000.0);
                ac.Finish(0);
                Put("nsPerFrame", ns);
                Note(ns.ToString("0.00", CultureInfo.InvariantCulture) + " ns por fotograma estereo, 10 min de audio");
                Check(ns < 20, "CountClipping tarda " + ns.ToString("0.0", CultureInfo.InvariantCulture) + " ns por fotograma");
            }
            finally { DeleteDir(dir); }
        }
    }
}

// Audio capture suite: exact clock handling (frame-level), drift, gaps, overlaps and Finish padding/trimming.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        const int PacketFrames = 480;          // 10 ms at 48 kHz
        const long PacketTicks = 100000;       // 10 ms in 100 ns units

        // Sample value that encodes its feed position: 0.5 + mantissa(seq). seq is even for a left sample, its
        // right sample is seq + 1. Zero bits mean silence.
        static float SeqValue(long seq) { return FromBits(0x3F000000u | (uint)(seq & 0x7FFFFF)); }

        sealed class Feeder
        {
            public readonly AudioCapture Capture;
            public long Samples;               // stereo samples fed so far (sequence numbers used)

            public Feeder(AudioCapture capture) { Capture = capture; }

            public void Packet(long when)
            {
                float[] s = new float[PacketFrames * 2];
                for (int i = 0; i < s.Length; i++) s[i] = SeqValue(Samples + i);
                Samples += s.Length;
                Capture.Feed(s, PacketFrames, false, when);
            }
        }

        // Frame-level classification of an output file: each frame must be a whole input frame (L, R) = (seq, seq+1)
        // with seq even, or silence. Whole input frames must appear in non-decreasing input order.
        sealed class FrameReport
        {
            public long Frames, Silent, Whole, NotWhole, OutOfOrder;
            public long FirstNotWhole = -1, FirstOutOfOrder = -1, FirstSilent = -1, LastSilent = -1;

            public bool Clean { get { return NotWhole == 0 && OutOfOrder == 0 && Whole + Silent == Frames; } }

            public string Describe()
            {
                return Frames + " fotogramas: " + Whole + " de entrada, " + Silent + " silencio, " + NotWhole +
                       " que no son un fotograma de entrada (primero " + FirstNotWhole + "), " + OutOfOrder +
                       " fuera de orden (primero " + FirstOutOfOrder + ")";
            }
        }

        static bool IsZeroBits(uint b) { return b == 0 || b == 0x80000000u; }

        static FrameReport ClassifyFrames(uint[] bits, long fedSamples)
        {
            FrameReport r = new FrameReport();
            r.Frames = bits.Length / 2;
            long lastIndex = -1;
            for (long f = 0; f < r.Frames; f++)
            {
                uint l = bits[2 * f], rt = bits[2 * f + 1];
                if (IsZeroBits(l) && IsZeroBits(rt))
                {
                    r.Silent++;
                    if (r.FirstSilent < 0) r.FirstSilent = f;
                    r.LastSilent = f;
                    continue;
                }
                uint ml = l & 0x7FFFFF, mr = rt & 0x7FFFFF;
                bool whole = (l & 0xFF800000u) == 0x3F000000u && (rt & 0xFF800000u) == 0x3F000000u &&
                             (ml % 2 == 0) && mr == ml + 1 && (long)ml + 1 < fedSamples;
                if (!whole)
                {
                    r.NotWhole++;
                    if (r.FirstNotWhole < 0) r.FirstNotWhole = f;
                    continue;
                }
                r.Whole++;
                long index = ml / 2;
                if (index < lastIndex)
                {
                    r.OutOfOrder++;
                    if (r.FirstOutOfOrder < 0) r.FirstOutOfOrder = f;
                }
                lastIndex = index;
            }
            return r;
        }

        static uint[] ReadBits(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Check(bytes.Length % 8 == 0, "el fichero de audio mide " + bytes.Length + " bytes, no es multiplo de 8");
            uint[] bits = new uint[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, bits, 0, bits.Length * 4);
            return bits;
        }

        static string TempAudioPath(string dir, string name) { return Path.Combine(dir, name + ".f32"); }

        static void AudioSuite()
        {
            Test("audio", "clean-continuous", () =>
            {
                string dir = WorkDir("audio-clean");
                try
                {
                    string path = TempAudioPath(dir, "clean");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 200; p++) f.Packet(p * PacketTicks);
                    long total = 200L * PacketFrames;
                    ac.Finish(total);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == total * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + total * 8);
                    FrameReport fr = ClassifyFrames(bits, f.Samples);
                    Check(fr.Clean, fr.Describe());
                    CheckCounters(ac, 0, 0, 0, 0);
                    Note("200 paquetes, sin correcciones; " + fr.Describe());
                }
                finally { DeleteDir(dir); }
            });

            Test("audio", "gap-100ms", () =>
            {
                string dir = WorkDir("audio-gap");
                try
                {
                    string path = TempAudioPath(dir, "gap");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 200; p++)
                    {
                        if (p >= 100 && p < 110) continue;   // 100 ms that the device never delivered
                        f.Packet(p * PacketTicks);
                    }
                    long total = 200L * PacketFrames;
                    ac.Finish(total);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == total * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + total * 8);
                    long silence = ac.SilenceFrames;
                    Check(silence >= 4700 && silence <= 4900, "SilenceFrames = " + silence + ", esperados ~4800");
                    for (long fr = 48002; fr < 52798; fr++)
                        Check(bits[fr * 2] == 0 && bits[fr * 2 + 1] == 0, "el hueco no es silencio en el fotograma " + fr);
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Check(rep.Clean, rep.Describe());
                    Note("SilenceFrames " + silence + "; " + rep.Describe());
                }
                finally { DeleteDir(dir); }
            });

            // A packet stamped 50 ms early lands entirely on already written time: dropped whole (480 frames).
            Test("audio", "overlap-50ms", () =>
            {
                string dir = WorkDir("audio-overlap");
                try
                {
                    string path = TempAudioPath(dir, "overlap");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 200; p++)
                        f.Packet(p == 100 ? p * PacketTicks - 500000 : p * PacketTicks);   // 50 ms early, once
                    long total = 200L * PacketFrames;
                    ac.Finish(total);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == total * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + total * 8);
                    Check(ac.OverlapFrames == PacketFrames, "OverlapFrames = " + ac.OverlapFrames + ", esperados 480 (paquete descartado entero)");
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Check(rep.Clean, rep.Describe());
                    Note("OverlapFrames " + ac.OverlapFrames + ", SilenceFrames " + ac.SilenceFrames + " (reportado, sin modelo propio); " + rep.Describe());
                }
                finally { DeleteDir(dir); }
            });

            Test("audio", "leading-silence", () =>
            {
                string dir = WorkDir("audio-lead");
                try
                {
                    string path = TempAudioPath(dir, "lead");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 100; p++) f.Packet((p + 1) * PacketTicks);   // starts 10 ms after t0
                    long total = 100L * PacketFrames + PacketFrames;
                    ac.Finish(total);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == total * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + total * 8);
                    long silence = ac.SilenceFrames;
                    Check(silence >= 470 && silence <= 490, "SilenceFrames = " + silence + ", esperados ~480 de silencio inicial");
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Check(rep.Clean, rep.Describe());
                }
                finally { DeleteDir(dir); }
            });

            Test("audio", "finish-pads-with-silence", () =>
            {
                string dir = WorkDir("audio-pad");
                try
                {
                    string path = TempAudioPath(dir, "pad");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 100; p++) f.Packet(p * PacketTicks);
                    ac.Finish(48100);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == 48100 * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + 48100 * 8);
                    Check(ac.SilenceFrames == 100, "SilenceFrames = " + ac.SilenceFrames + ", esperados 100");
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Check(rep.Clean, rep.Describe());
                    Check(rep.Silent == 100 && rep.FirstSilent == 48000, "el relleno no esta al final: " + rep.Describe());
                }
                finally { DeleteDir(dir); }
            });

            Test("audio", "finish-trims-the-excess", () =>
            {
                string dir = WorkDir("audio-trim");
                try
                {
                    string path = TempAudioPath(dir, "trim");
                    AudioCapture ac = AudioCapture.ForTest(path, true);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 100; p++) f.Packet(p * PacketTicks);
                    ac.Finish(47900);
                    uint[] bits = ReadBits(path);
                    Check(bits.Length == 47900 * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + 47900 * 8);
                    Check(ac.TrimmedFrames == 100, "TrimmedFrames = " + ac.TrimmedFrames + ", esperados 100");
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Check(rep.Clean, rep.Describe());
                }
                finally { DeleteDir(dir); }
            });

            // Drift: 6000 packets of 10 ms, stamped at (1 +/- pct) times the nominal clock. Each packet absorbs at most
            // one frame of error (max about 0.21 %), so 0.1 % (2880 frames over 60 s) is within range; 0.5 % is beyond it.
            Test("audio", "drift-0.1pct-exact-fast", () => RunDrift(0.1, true));
            Test("audio", "drift-0.1pct-exact-slow", () => RunDrift(0.1, false));
            Test("audio", "drift-0.5pct-beyond-range-fast", () => RunDrift(0.5, true));
            Test("audio", "drift-0.5pct-beyond-range-slow", () => RunDrift(0.5, false));
            Test("audio", "drift-fast-legacy-report", () =>
            {
                string dir = WorkDir("audio-legacy");
                try
                {
                    string path = TempAudioPath(dir, "legacy");
                    AudioCapture ac = AudioCapture.ForTest(path, false);
                    Feeder f = new Feeder(ac);
                    for (int p = 0; p < 6000; p++) f.Packet((long)(p * PacketTicks * 1.005));
                    long total = 6000L * PacketFrames;
                    ac.Finish(total);
                    uint[] bits = ReadBits(path);
                    FrameReport rep = ClassifyFrames(bits, f.Samples);
                    Put("legacyNotWholeFrames", rep.NotWhole);
                    Note("legacy (interpolacion, 0.5 %): " + rep.NotWhole + " de " + rep.Frames + " fotogramas no son un fotograma de entrada; " +
                         rep.Silent + " silencio, " + rep.OutOfOrder + " fuera de orden; Duplicated " + ac.Duplicated + ", Removed " + ac.Removed +
                         " (informe, no fallo)");
                }
                finally { DeleteDir(dir); }
            });
        }

        static void CheckCounters(AudioCapture ac, long silence, long overlap, long duplicated, long removed)
        {
            Check(ac.SilenceFrames == silence, "SilenceFrames = " + ac.SilenceFrames + ", esperados " + silence);
            Check(ac.OverlapFrames == overlap, "OverlapFrames = " + ac.OverlapFrames + ", esperados " + overlap);
            Check(ac.Duplicated == duplicated, "Duplicated = " + ac.Duplicated + ", esperados " + duplicated);
            Check(ac.Removed == removed, "Removed = " + ac.Removed + ", esperados " + removed);
        }

        static bool WithinTenPercent(long value, long expected)
        {
            return value >= expected * 0.9 && value <= expected * 1.1;
        }

        // fast: stamps run ahead of the samples (the writer falls behind and duplicates); slow: the reverse (removes).
        static void RunDrift(double pct, bool fast)
        {
            string name = "drift-" + pct.ToString(CultureInfo.InvariantCulture) + "pct-" + (fast ? "fast" : "slow");
            string dir = WorkDir("audio-" + name);
            try
            {
                double rate = fast ? 1 + pct / 100 : 1 - pct / 100;
                string path = TempAudioPath(dir, name);
                AudioCapture ac = AudioCapture.ForTest(path, true);
                Feeder f = new Feeder(ac);
                const int packets = 6000;
                long lastWhen = 0;
                for (int p = 0; p < packets; p++)
                {
                    lastWhen = (long)Math.Round(p * PacketTicks * rate);
                    f.Packet(lastWhen);
                }
                long nominal = (long)packets * PacketFrames;                                    // 2 880 000 frames of input
                long total = (long)Math.Round((lastWhen + PacketTicks) * 48000.0 / 10000000.0);   // device timeline by the stamps
                ac.Finish(total);
                uint[] bits = ReadBits(path);
                Check(bits.Length == total * 2, "tamano " + bits.Length * 4 + " bytes, esperados " + total * 8);
                FrameReport rep = ClassifyFrames(bits, f.Samples);
                Check(rep.Clean, rep.Describe());

                long dup = ac.Duplicated, rem = ac.Removed, sil = ac.SilenceFrames, ov = ac.OverlapFrames, trim = ac.TrimmedFrames;
                long net = total - nominal;   // frames the stamps say the device produced beyond the samples fed
                Put("duplicated", dup);
                Put("removed", rem);
                Put("silence", sil);
                Put("overlap", ov);
                Put("trimmed", trim);
                Put("expectedNet", net);
                Put("frames", rep.Frames);

                if (pct <= 0.1)
                {
                    long expected = (long)Math.Round(pct / 100.0 * 60 * 48000);   // 2880
                    long right = fast ? dup : rem, wrong = fast ? rem : dup;
                    Check(WithinTenPercent(right, expected), (fast ? "Duplicated" : "Removed") + " = " + right + ", esperados ~" + expected);
                    Check(wrong == 0, "correcciones en sentido contrario: " + wrong);
                    Check(ov == 0, "OverlapFrames = " + ov + " con deriva dentro de rango");
                    // No gap rule at this drift: only Finish padding or trimming may remain.
                    long wpre = nominal + dup - rem;
                    long pad = Math.Max(0, total - wpre), trimModel = Math.Max(0, wpre - total);
                    Check(Math.Abs(sil - pad) <= 2, "SilenceFrames " + sil + ", relleno de Finish esperado " + pad);
                    Check(Math.Abs(trim - trimModel) <= 2, "TrimmedFrames " + trim + ", recorte de Finish esperado " + trimModel);
                    Note("Duplicated " + dup + ", Removed " + rem + " (esperado ~" + expected + "), SilenceFrames " + sil + " (relleno " + pad +
                         "), Overlap " + ov + "; " + rep.Describe());
                }
                else
                {
                    // Beyond range the timeline resyncs through the gap/overlap rule. Every frame is still whole or silence,
                    // and the counters must account for every frame: dup + sil - (rem + ov + trim) == device minus input.
                    long balance = dup + sil - (rem + ov + trim);
                    Put("balance", balance);
                    Check(Math.Abs(balance - net) <= 50, "balance de contadores " + balance + ", esperado " + net + " (desde las marcas de tiempo)");
                    Note("fuera de rango: Duplicated " + dup + ", Removed " + rem + ", SilenceFrames " + sil + ", Overlap " + ov + ", Trimmed " +
                         trim + "; balance " + balance + " vs esperado " + net + "; " + rep.Describe() +
                         (rep.Silent > 0 ? "; silencio entre " + rep.FirstSilent + " y " + rep.LastSilent : ""));
                }
            }
            finally { DeleteDir(dir); }
        }
    }
}

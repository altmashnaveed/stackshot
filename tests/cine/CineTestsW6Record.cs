// Round 6 (pause, clicks and keys in recordings), suite "w6record". Pausing: the capture loop's frame times (Session.Cut)
// and the sound (AudioCapture.Feed) cut exactly the same moments, Cine masters still verify, a GIF's length skips the
// pauses and the safety nets do not fire. Clicks and keys: the overlay drawn into BGRA and NV12 frames at 100 and 150 %,
// the shortcut captions and the privacy filter. Synthetic frames, sound and input events only: no screen, no audio
// device, and no hook is ever installed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.Win32;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static void W6RecordSuite()
        {
            int hooksBefore = InputHooks.Live;
            string savedRoot = Recorder.RescueRoot;
            try
            {
                Test("w6record", "timeline-maps-both-ways", W6TimelineMaps);
                Test("w6record", "timeline-pause-lengths-keep-refresh-phase", W6TimelineQuantum);
                Test("w6record", "frames-continuous-across-pauses-59.94-locked", () => W6FramesContinuous(60000, 1001, true, 60000 / 1001.0));
                Test("w6record", "frames-continuous-across-pauses-60-on-144hz", () => W6FramesContinuous(60, 1, true, 144));
                Test("w6record", "frames-continuous-across-pauses-30-gdi", () => W6FramesContinuous(30, 1, false, 0));
                Test("w6record", "frames-many-random-pauses", W6FramesManyPauses);
                Test("w6record", "frames-stop-while-paused", W6FramesStopPaused);
                Test("w6record", "frames-no-late-frame-after-long-pause", W6FramesLongPause);
                Test("w6record", "sound-cuts-the-same-moments-and-stays-in-sync", W6SoundSync);
                Test("w6record", "sound-dated-ahead-is-held-until-a-pause-is-known", W6SoundAhead);
                Test("w6record", "sound-join-has-no-click", W6SoundJoin);
                Test("w6record", "sound-without-pauses-is-unchanged", W6SoundUnchanged);
                Test("w6record", "sound-hours-after-a-pause-is-untouched", W6SoundHoursLater);
                Test("w6record", "cine-verified-with-pauses", () => W6Cine(false));
                Test("w6record", "cine-verified-stopped-while-paused", () => W6Cine(true));
                Test("w6record", "gif-length-skips-the-pauses", W6Gif);
                Test("w6record", "session-pause-resume-and-elapsed", W6SessionToggle);
                Test("w6record", "session-pause-is-instant", W6PauseInstant);
                Test("w6record", "safety-disk-pace-kept-while-paused", W6DiskPace);
                Test("w6record", "safety-lock-and-suspend-while-paused-stop", W6LockWhilePaused);
                Test("w6record", "tray-pause-row", W6TrayRow);
                Test("w6record", "bar-pausing-never-moves-the-buttons", W6BarLayout);
                Test("w6record", "bar-paints-paused-and-recording", W6BarPaint);
                Test("w6record", "keys-caption-texts", W6KeyTexts);
                Test("w6record", "keys-privacy-never-shows-typing", W6KeyPrivacy);
                Test("w6record", "overlay-ring-100-and-150", W6Rings);
                Test("w6record", "overlay-ring-gone-after-400ms", W6RingGone);
                Test("w6record", "overlay-caption-100-and-150", W6Caption);
                Test("w6record", "overlay-caption-repeats-and-fades", W6CaptionRepeat);
                Test("w6record", "overlay-nv12-matches-bgra", W6Nv12);
                Test("w6record", "overlay-nothing-from-or-across-pauses", W6OverlayPause);
                Test("w6record", "overlay-hidden-windows-and-far-clicks", W6OverlayHidden);
                Test("w6record", "overlay-queue-bounded", W6Queue);
                Test("w6record", "overlay-frame-times-follow-the-reads", W6OverlayPlumbing);
                Test("w6record", "overlay-only-when-asked-and-never-hooks-in-tests", W6SessionInput);
                Test("w6record", "r4-hooks-thread-ends-whenever-disposed", R4HooksThreadEnds);
                Test("w6record", "r4-every-exit-path-ends-the-hooks", R4HookExitPaths);
                Test("w6record", "r4-overlay-cost-per-frame-at-4k", R4Overlay4k);
                Test("w6record", "r4-pause-in-countdown-or-before-first-frame", R4PauseBeforeFirstFrame);
                Test("w6record", "r4-cine-tells-what-was-altered", R4CineAltered);
                W6PageShot();
            }
            finally
            {
                Recorder.RescueRoot = savedRoot;
                Recorder.SetCurrentForTest(null);
            }
            Test("w6record", "hooks-never-installed", () => Check(hooksBefore == 0 && InputHooks.Live == 0, "hay ganchos de teclado o rat\u00F3n instalados: " + InputHooks.Live));
        }

        // ---- helpers (all prefixed W6) ----

        sealed class W6Clock
        {
            public long Now;
            public long Get() { return Now; }
        }

        static long W6Ms(double ms) { return (long)Math.Round(ms * Stopwatch.Frequency / 1000.0); }
        static double W6ToMs(double ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
        static string W6N(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        static Session W6Session(Settings st, int quality)
        {
            if (st == null) st = new Settings();
            st.Webcam = 0;
            return new Session(null, st, ffmpeg, new Rectangle(0, 0, 64, 48), false, 60, quality);
        }

        // A session whose recording threads run (as after Go): it can be paused.
        static Session W6Running()
        {
            Session s = W6Session(null, Recorder.Standard);
            H2Set(s, "writer", new Thread(delegate() { }));
            return s;
        }

        sealed class W6Frame
        {
            public long N, At;
            public double Cut;
            public bool Last;
        }

        sealed class W6Run
        {
            public readonly W6Clock Clock = new W6Clock();
            public PauseTimeline Timeline;
            public Session S;
            public double Period, Display, CutPhase, Margin;
            public bool Locked;
            public int FpsNum, FpsDen;
            public long Stop;
            public readonly List<W6Frame> Frames = new List<W6Frame>();
            public readonly List<long[]> Asked = new List<long[]>();   // the virtual instants each pause was pressed and released
        }

        // The capture loop's timing on a virtual clock (1 ms steps), as Session.CaptureFrames runs it: frame n is due at
        // Session.Cut(n) (through the pauses) and made once the clock passes it by the margin; at the stop, the last loop
        // makes the frames up to the stop's recording time. pauses: (press, release) in ms, release < 0: still paused at the
        // stop. each: every millisecond, before that instant's frames (to feed sound). frame: every frame made.
        static W6Run W6Schedule(int fpsNum, int fpsDen, bool locked, double displayHz, double phase, double[][] pauses, double stopMs,
                                Action<W6Run> each, Action<W6Run, W6Frame> frame)
        {
            W6Run r = new W6Run();
            r.FpsNum = fpsNum;
            r.FpsDen = fpsDen;
            r.Locked = locked;
            r.Timeline = new PauseTimeline(r.Clock.Get);
            r.S = W6Session(null, Recorder.Standard);
            r.S.PausesForTest = r.Timeline;
            r.S.T0ForTest = 0;
            long freq = Stopwatch.Frequency, step = W6Ms(1);
            r.Period = freq * (double)fpsDen / fpsNum;
            r.Display = locked ? freq / displayHz : r.Period;
            r.CutPhase = locked ? r.Display * phase : 0;
            r.Margin = freq * 3 / 1000 + (locked ? (long)r.Display : 0);
            r.Timeline.Quantum = locked ? r.Display : 0;
            r.Stop = W6Ms(stopMs);
            List<long[]> events = new List<long[]>();
            foreach (double[] p in pauses)
            {
                long press = (W6Ms(p[0]) + step - 1) / step * step;
                long release = p[1] < 0 ? -1 : (W6Ms(p[1]) + step - 1) / step * step;
                events.Add(new long[] { press, 1 });
                if (release >= 0) events.Add(new long[] { release, 0 });
                r.Asked.Add(new long[] { press, release });
            }
            events.Sort(delegate(long[] a, long[] b) { return a[0].CompareTo(b[0]); });
            int ev = 0;
            long n = 0;
            for (r.Clock.Now = 0; r.Clock.Now < r.Stop; r.Clock.Now += step)
            {
                while (ev < events.Count && events[ev][0] <= r.Clock.Now)
                {
                    Check(events[ev][1] == 1 ? r.Timeline.Pause() : r.Timeline.Resume(), "la pausa " + ev + " no cambi\u00F3 el estado");
                    ev++;
                }
                if (each != null) each(r);
                PauseTimeline.View v;
                long now = r.Timeline.Now(out v);
                while (true)
                {
                    double cut = r.S.CutForTest(n, r.Period, r.CutPhase, locked, v);
                    if (!(cut + r.Margin <= now)) break;
                    W6Frame f = new W6Frame();
                    f.N = n++;
                    f.Cut = cut;
                    f.At = now;
                    r.Frames.Add(f);
                    if (frame != null) frame(r, f);
                }
            }
            long t1 = r.Clock.Now;
            PauseTimeline.View fv = r.Timeline.Current;
            while (n * r.Period < fv.RtOf(t1))
            {
                W6Frame f = new W6Frame();
                f.Cut = fv.Clamp(r.S.CutForTest(n, r.Period, r.CutPhase, locked, fv));
                f.N = n++;
                f.At = t1;
                f.Last = true;
                r.Frames.Add(f);
                if (frame != null) frame(r, f);
            }
            if (each != null) each(r);
            return r;
        }

        // Every frame is the next one on recording time (no gap, nothing frozen), none is taken inside a pause, none is late,
        // and there are exactly as many as the recording time holds.
        static void W6CheckFrames(W6Run r, string what)
        {
            PauseTimeline.View v = r.Timeline.Current;
            double worstStep = 0, worstLate = 0;
            for (int i = 0; i < r.Frames.Count; i++)
            {
                W6Frame f = r.Frames[i];
                Check(f.N == i, what + ": fotograma " + i + " con \u00EDndice " + f.N);
                for (int k = 0; k < v.Count; k++)
                {
                    long end = v.Length(k) < 0 ? long.MaxValue : v.Start(k) + v.Length(k);
                    Check(!(f.Cut > v.Start(k) && f.Cut < end), what + ": el fotograma " + i + " (" + W6N(W6ToMs(f.Cut)) + " ms) es de dentro de la pausa " + k);
                }
                if (f.Last && v.Open && f.Cut >= v.OpenStart) continue;
                double want = r.Locked ? r.CutPhase + i * r.Period : (i + 0.5) * r.Period;
                worstStep = Math.Max(worstStep, Math.Abs(v.RtOf((long)Math.Round(f.Cut)) - want));
                if (!f.Last) worstLate = Math.Max(worstLate, f.At - f.Cut);
            }
            Check(worstStep <= 3, what + ": un fotograma se sale de su instante en " + W6N(worstStep) + " ticks");
            Check(worstLate <= r.Margin + W6Ms(1) + 2, what + ": un fotograma sale " + W6N(W6ToMs(worstLate)) + " ms tarde (margen " + W6N(W6ToMs(r.Margin)) + " ms)");
            long expected = (long)Math.Ceiling(v.RtOf(r.Stop) / r.Period);
            Check(r.Frames.Count == expected, what + ": " + r.Frames.Count + " fotogramas, el tiempo grabado da " + expected);
        }

        static IntPtr W6Buffer(int bytes, byte value)
        {
            byte[] b = new byte[bytes];
            for (int i = 0; i < bytes; i++) b[i] = value;
            IntPtr p = Marshal.AllocHGlobal(bytes);
            Marshal.Copy(b, 0, p, bytes);
            return p;
        }

        static byte[] W6Bytes(IntPtr p, int bytes)
        {
            byte[] b = new byte[bytes];
            Marshal.Copy(p, b, 0, bytes);
            return b;
        }

        static int W6Changed(byte[] b, byte value, int tolerance)
        {
            int n = 0;
            foreach (byte x in b) if (Math.Abs(x - value) > tolerance) n++;
            return n;
        }

        // A synthetic screen: every millisecond of the clock looks different.
        static void W6Paint(byte[] px, int w, int h, double cut)
        {
            long ms = (long)Math.Floor(W6ToMs(cut));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    px[o] = (byte)(ms * 7 + x);
                    px[o + 1] = (byte)((ms >> 2) * 13 + y * 3);
                    px[o + 2] = (byte)((ms >> 5) ^ (x + y));
                    px[o + 3] = 255;
                }
        }

        // 10 ms packets of sound (480 frames each, the first at 0); the value of each frame says which it is (SeqValue), or a
        // sine. A packet is handed over when the clock reaches its end minus aheadMs: 0 like a microphone, more like loopback
        // (dated where it will be heard, which is still ahead of the clock).
        sealed class W6Feeder
        {
            public readonly AudioCapture A;
            public long Next;
            readonly double aheadMs;
            readonly bool sine;
            readonly long offset;

            public W6Feeder(AudioCapture a, double aheadMs, bool sine, long offset)
            {
                A = a;
                this.aheadMs = aheadMs;
                this.sine = sine;
                this.offset = offset;
            }

            public long Frames { get { return Next * PacketFrames; } }

            public void Upto(long ticks)
            {
                long now = PauseTimeline.To100(ticks), ahead = (long)Math.Round(aheadMs * 10000);
                while ((Next + 1) * PacketTicks - ahead <= now)
                {
                    float[] s = new float[PacketFrames * 2];
                    for (int i = 0; i < PacketFrames; i++)
                    {
                        long f = Next * PacketFrames + i;
                        if (sine)
                        {
                            float v = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * f / AudioCapture.Rate));
                            s[2 * i] = v;
                            s[2 * i + 1] = v;
                        }
                        else
                        {
                            s[2 * i] = SeqValue(2 * (f + offset));
                            s[2 * i + 1] = SeqValue(2 * (f + offset) + 1);
                        }
                    }
                    A.Feed(s, PacketFrames, false, Next * PacketTicks);
                    Next++;
                }
            }
        }

        // The input frame a pair came from: -1 silence, -2 not a whole input frame.
        static long W6Index(uint l, uint r, long offset)
        {
            if (IsZeroBits(l) && IsZeroBits(r)) return -1;
            uint ml = l & 0x7FFFFF, mr = r & 0x7FFFFF;
            if ((l & 0xFF800000u) != 0x3F000000u || (r & 0xFF800000u) != 0x3F000000u || ml % 2 != 0 || mr != ml + 1) return -2;
            return ml / 2 - offset;
        }

        // Where the sound at recording time rt (100 ns) comes from: rt plus every pause before it; +infinity past the start of
        // a pause still going on. The instant a pause ends is the first one after it.
        static double W6Source100(PauseTimeline.View v, double rt)
        {
            long[] s = v.Start100, e = v.End100;
            double removed = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (rt < s[i] - removed) break;
                if (e[i] == long.MaxValue) return double.PositiveInfinity;
                removed += e[i] - s[i];
            }
            return rt + removed;
        }

        // Output frames where a pause's sound fades into the next (the joins), with a frame of slack on each side.
        static List<long[]> W6Joins(PauseTimeline.View v)
        {
            List<long[]> joins = new List<long[]>();
            long[] s = v.Start100, e = v.End100;
            double step = 10000000.0 / AudioCapture.Rate, removed = 0;
            for (int i = 0; i < s.Length && e[i] != long.MaxValue; i++)
            {
                long at = (long)Math.Floor((s[i] - removed) / step);
                joins.Add(new long[] { at - 1, at + AudioCapture.JoinFrames + 2 });
                removed += e[i] - s[i];
            }
            return joins;
        }

        static bool W6InJoin(List<long[]> joins, long f)
        {
            foreach (long[] j in joins) if (f >= j[0] && f <= j[1]) return true;
            return false;
        }

        // Every frame of the sound file (outside the joins) is the input frame of its recording time; past what was fed,
        // or past the start of a pause still going on, only silence.
        static int W6CheckMapping(uint[] bits, PauseTimeline.View v, long fed, long offset, string what)
        {
            double step = 10000000.0 / AudioCapture.Rate;
            List<long[]> joins = W6Joins(v);
            long frames = bits.Length / 2;
            int bad = 0, count = 0;
            string first = null;
            for (long f = 0; f < frames; f++)
            {
                if (W6InJoin(joins, f)) continue;
                long idx = W6Index(bits[2 * f], bits[2 * f + 1], offset);
                double src = W6Source100(v, f * step);
                if (double.IsInfinity(src) || src / step > fed - 2)
                {
                    if (idx != -1 && !double.IsInfinity(src) && src / step > fed + 2) { bad++; if (first == null) first = "fotograma " + f + " deber\u00EDa ser silencio y es " + idx; }
                    if (double.IsInfinity(src) && idx != -1) { bad++; if (first == null) first = "fotograma " + f + " (tras una pausa sin terminar) es " + idx; }
                    continue;
                }
                long want = (long)Math.Round(src / step);
                count++;
                if (idx < 0 || Math.Abs(idx - want) > 1)
                {
                    bad++;
                    if (first == null) first = "fotograma " + f + " es " + idx + " y deber\u00EDa ser " + want;
                }
            }
            Check(bad == 0, what + ": " + bad + " fotogramas de sonido fuera de su sitio (" + first + ")");
            return count;
        }

        // Sound and image in sync: the sound at a frame's recording time came from the instant its image was taken.
        static double W6CheckSync(W6Run r, uint[] bits, long offset, string what)
        {
            PauseTimeline.View v = r.Timeline.Current;
            List<long[]> joins = W6Joins(v);
            double perSample = Stopwatch.Frequency / (double)AudioCapture.Rate, worst = 0;
            int compared = 0;
            foreach (W6Frame f in r.Frames)
            {
                if (f.Last && v.Open && f.Cut >= v.OpenStart) continue;
                double rt = r.Locked ? r.CutPhase + f.N * r.Period : (f.N + 0.5) * r.Period;
                long j = (long)Math.Round(rt / perSample);
                if (j < 0 || j >= bits.Length / 2 || W6InJoin(joins, j)) continue;
                long idx = W6Index(bits[2 * j], bits[2 * j + 1], offset);
                if (idx < 0) continue;
                worst = Math.Max(worst, Math.Abs(f.Cut - idx * perSample));
                compared++;
            }
            Check(compared > r.Frames.Count / 2, what + ": solo se han podido comparar " + compared + " fotogramas");
            Check(worst <= 1.5 * perSample + 2, what + ": imagen y sonido se separan " + W6N(W6ToMs(worst)) + " ms");
            return worst;
        }

        // STACKSHOT_W6_SHOTS=<folder>: the pictures these tests draw are saved there to look at (nothing otherwise).
        static void W6Shot(byte[] bgra, int w, int h, string name)
        {
            string dir = Environment.GetEnvironmentVariable("STACKSHOT_W6_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            using (Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                System.Drawing.Imaging.BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] copy = (byte[])bgra.Clone();
                for (int i = 3; i < copy.Length; i += 4) copy[i] = 255;
                for (int y = 0; y < h; y++) Marshal.Copy(copy, y * w * 4, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), w * 4);
                b.UnlockBits(d);
                b.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        static InputOverlay W6Overlay(int w, int h, float scale, W6Clock c, out PauseTimeline tl, out InputQueue q)
        {
            tl = new PauseTimeline(c.Get);
            q = new InputQueue();
            return new InputOverlay(new Rectangle(1000, 500, w, h), scale, true, true, tl, q);
        }

        // ---- the timeline ----

        static void W6TimelineMaps()
        {
            W6Clock c = new W6Clock();
            PauseTimeline tl = new PauseTimeline(c.Get);
            Check(!tl.Paused && tl.Count == 0 && tl.Current.QpcOf(12345) == 12345 && tl.Current.RtOf(777) == 777, "sin pausas el tiempo grabado es el del reloj");
            Check(!tl.Resume(), "reanudar sin pausa no debe hacer nada");
            c.Now = 1000;
            Check(tl.Pause() && !tl.Pause(), "pausar dos veces seguidas");
            PauseTimeline.View open = tl.Current;
            Check(open.Open && open.OpenStart == 1000, "pausa abierta en 1000");
            Check(open.QpcOf(999) == 999 && open.QpcOf(1000) == 1000, "lo de antes de la pausa (y su instante) no se mueve");
            Check(double.IsPositiveInfinity(open.QpcOf(1000.5)), "un fotograma de despu\u00E9s de una pausa abierta no tiene instante");
            Check(open.Clamp(double.PositiveInfinity) == 1000 && open.Clamp(500) == 500, "Clamp lleva al inicio de la pausa");
            bool inPause;
            int seg;
            Check(open.RtOf(1500, out inPause, out seg) == 1000 && inPause && seg == 0, "un instante de la pausa abierta");
            c.Now = 1300;
            Check(tl.Resume(), "reanudar");
            c.Now = 2000;
            tl.Pause();
            c.Now = 2050;
            tl.Resume();
            PauseTimeline.View v = tl.Current;
            Check(v.Count == 2 && !v.Open && v.Removed == 350, "dos pausas de 300 y 50: quitado " + v.Removed);
            long[] qpcs = { 0, 999, 1000, 1299, 1300, 1301, 1999, 2000, 2049, 2050, 5000 };
            long[] rts = { 0, 999, 1000, 1000, 1000, 1001, 1699, 1700, 1700, 1700, 4650 };
            bool[] paused = { false, false, true, true, false, false, false, true, true, false, false };
            int[] segs = { 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2 };
            for (int i = 0; i < qpcs.Length; i++)
            {
                long rt = v.RtOf(qpcs[i], out inPause, out seg);
                Check(rt == rts[i] && inPause == paused[i] && seg == segs[i], "RtOf(" + qpcs[i] + ") = " + rt + "/" + inPause + "/" + seg);
                if (!inPause) Check(v.QpcOf(rt) == qpcs[i] || rt == 1000 || rt == 1700, "QpcOf(RtOf(" + qpcs[i] + ")) = " + v.QpcOf(rt));
            }
            Check(v.QpcOf(1000) == 1000 && v.QpcOf(1000.5) == 1300.5 && v.QpcOf(1700) == 2000 && v.QpcOf(1701) == 2051, "QpcOf alrededor de las pausas");
            long[] s100 = v.Start100, e100 = v.End100;
            Check(s100.Length == 2 && s100[0] == PauseTimeline.To100(1000) && e100[1] == PauseTimeline.To100(2050), "las pausas en unidades de 100 ns");
            Note("RtOf y QpcOf coinciden en " + qpcs.Length + " instantes, dos pausas cerradas y una abierta");
        }

        static void W6TimelineQuantum()
        {
            W6Clock c = new W6Clock();
            PauseTimeline tl = new PauseTimeline(c.Get);
            double display = Stopwatch.Frequency * 1001 / 60000.0;
            tl.Quantum = display;
            Random rnd = new Random(11);
            long t = 5000;
            double removed = 0, worstPhase = 0;
            for (int i = 0; i < 40; i++)
            {
                t += W6Ms(10 + rnd.Next(400));
                c.Now = t;
                tl.Pause();
                long asked = W6Ms(1 + rnd.NextDouble() * 900);
                t += asked;
                c.Now = t;
                tl.Resume();
                PauseTimeline.View v = tl.Current;
                long len = v.Length(i);
                Check(len >= asked && len < asked + display + 1, "pausa " + i + ": pedida " + asked + ", cortada " + len);
                removed += len;
                // Everything cut so far is whole refresh periods: a frame after it keeps its place in the refresh cycle.
                Check(v.Removed == (long)removed, "quitado " + v.Removed + ", sumado " + removed);
                double k = removed / display;
                worstPhase = Math.Max(worstPhase, Math.Abs(k - Math.Round(k)) * display);
                Check(worstPhase <= 0.5 * (i + 1) + 1, "pausa " + i + ": la fase se mueve " + W6N(worstPhase) + " ticks");
            }
            Check(worstPhase <= 40 * 0.5 + 1, "40 pausas desplazan la fase " + W6N(worstPhase) + " ticks");
            Note("40 pausas redondeadas al refresco de 59,94 Hz; desfase acumulado " + W6N(worstPhase) + " ticks");
        }

        // ---- the capture loop's timing ----

        static void W6FramesContinuous(int fn, int fd, bool locked, double hz)
        {
            double[][] pauses = { new double[] { 700.3, 1450.9 }, new double[] { 2100.2, 2111.7 }, new double[] { 2600.5, 3900.1 } };
            W6Run r = W6Schedule(fn, fd, locked, hz, 0.6, pauses, 5000, null, null);
            PauseTimeline.View v = r.Timeline.Current;
            Check(v.Count == 3 && !v.Open, "pausas: " + v.Count);
            W6CheckFrames(r, fn + "/" + fd);
            for (int k = 0; k < 3; k++)
            {
                long asked = r.Asked[k][1] - r.Asked[k][0], len = v.Length(k);
                Check(len >= asked && len <= asked + (locked ? r.Display + 1 : 0), "pausa " + k + ": pedida " + asked + ", cortada " + len);
            }
            if (locked && Math.Abs(r.Period - r.Display) < 1)
                foreach (W6Frame f in r.Frames)
                {
                    double ph = ((f.Cut - r.CutPhase) % r.Display + r.Display) % r.Display;
                    Check(Math.Min(ph, r.Display - ph) <= 3, "el fotograma " + f.N + " cambia de sitio en el refresco: " + W6N(ph) + " ticks");
                }
            Note(r.Frames.Count + " fotogramas en " + W6N(W6ToMs(v.RtOf(r.Stop))) + " ms grabados de 5000; tres pausas (una de 11 ms)");
        }

        static void W6FramesManyPauses()
        {
            foreach (bool locked in new bool[] { false, true })
            {
                Random rnd = new Random(locked ? 21 : 22);
                List<double[]> list = new List<double[]>();
                double t = 150;
                for (int i = 0; i < 25; i++)
                {
                    t += 15 + rnd.NextDouble() * 280;
                    double len = 2 + rnd.NextDouble() * 400;
                    list.Add(new double[] { t, t + len });
                    t += len;
                }
                W6Run r = W6Schedule(locked ? 60 : 30, 1, locked, 60, 0.4, list.ToArray(), t + 500, null, null);
                Check(r.Timeline.Count == 25, "pausas " + r.Timeline.Count);
                W6CheckFrames(r, locked ? "60 fps" : "30 fps");
                Add((locked ? "60" : "30") + " fps: " + r.Frames.Count + " fotogramas tras 25 pausas");
            }
        }

        static void W6FramesStopPaused()
        {
            foreach (bool locked in new bool[] { false, true })
            {
                double[][] pauses = { new double[] { 400.4, 900.2 }, new double[] { 1500.4, -1 } };
                W6Run r = W6Schedule(locked ? 60000 : 30, locked ? 1001 : 1, locked, 60000 / 1001.0, 0.6, pauses, 2600, null, null);
                PauseTimeline.View v = r.Timeline.Current;
                Check(v.Open, "la segunda pausa sigue abierta");
                W6CheckFrames(r, locked ? "59,94" : "30");
                foreach (W6Frame f in r.Frames) Check(f.Cut <= v.OpenStart, "el fotograma " + f.N + " es de despu\u00E9s de pausar");
                Add((locked ? "59,94" : "30") + " fps: " + r.Frames.Count + " fotogramas hasta la pausa");
            }
        }

        static void W6FramesLongPause()
        {
            W6Run r = W6Schedule(60, 1, true, 60, 0.6, new double[][] { new double[] { 1000.5, 61000.2 } }, 62000, null, null);
            W6CheckFrames(r, "pausa de un minuto");
            double lateMs = W6ToMs(r.Margin) + Math.Max(100, W6ToMs(r.Period)), worst = 0;
            foreach (W6Frame f in r.Frames) if (!f.Last) worst = Math.Max(worst, W6ToMs(f.At - f.Cut));
            Check(worst < lateMs && worst < 1000, "tras la pausa un fotograma sale " + W6N(worst) + " ms tarde (cuenta como tarde a partir de " + W6N(lateMs) + ")");
            Note("tras 60 s en pausa ning\u00FAn fotograma sale tarde (peor " + W6N(worst) + " ms; Cine se detiene a los 1000)");
        }

        // ---- the sound ----

        static void W6SoundSync()
        {
            string dir = WorkDir("w6-sound");
            try
            {
                string path = Path.Combine(dir, "micro.f32");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                W6Feeder fd = null;
                double[][] pauses = { new double[] { 700.3, 1450.9 }, new double[] { 2100.2, 2600.5 }, new double[] { 2900.7, 2905.1 } };
                W6Run r = W6Schedule(30, 1, false, 0, 0, pauses, 3600, delegate(W6Run run)
                {
                    if (fd == null) { ac.Pauses = run.Timeline; fd = new W6Feeder(ac, 0, false, 0); }
                    fd.Upto(run.Clock.Now);
                }, null);
                long samples = Cine.AudioFramesFor(r.Frames.Count, 30, 1);
                ac.Finish(samples);
                uint[] bits = ReadBits(path);
                Check(bits.Length == samples * 2, "el sonido tiene " + bits.Length / 2 + " fotogramas y el v\u00EDdeo pide " + samples);
                PauseTimeline.View v = r.Timeline.Current;
                int n = W6CheckMapping(bits, v, fd.Frames, 0, "micro");
                double worst = W6CheckSync(r, bits, 0, "micro");
                Check(ac.Joins == 3, "fundidos " + ac.Joins + " (tres pausas)");
                long paused = 0;
                for (int i = 0; i < v.Count; i++) paused += (long)Math.Round(v.Length(i) * (double)AudioCapture.Rate / Stopwatch.Frequency);
                Check(Math.Abs(ac.PausedFrames - paused) <= 6, "descartado " + ac.PausedFrames + " de " + paused + " en pausa");
                // Only the end may be padded (the video's last frame can reach past the last packet); never a gap inside.
                Check(ac.OverlapFrames == 0 && ac.Duplicated == 0 && ac.Removed == 0 && ac.SilenceFrames <= AudioCapture.Rate / 30 + 2,
                      "el reloj del sonido tuvo que corregir: solapado " + ac.OverlapFrames + ", duplicado " + ac.Duplicated + ", quitado " + ac.Removed + ", silencio " + ac.SilenceFrames);
                Note(n + " fotogramas de sonido en su sitio tras 3 pausas; imagen y sonido a " + W6N(W6ToMs(worst)) + " ms como mucho");
            }
            finally { DeleteDir(dir); }
        }

        static void W6SoundAhead()
        {
            string dir = WorkDir("w6-ahead");
            try
            {
                string path = Path.Combine(dir, "equipo.f32");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                W6Feeder fd = null;
                double[][] pauses = { new double[] { 1000.4, 1600.2 }, new double[] { 2000.1, 2003.3 } };
                W6Run r = W6Schedule(30, 1, false, 0, 0, pauses, 2500, delegate(W6Run run)
                {
                    if (fd == null) { ac.Pauses = run.Timeline; fd = new W6Feeder(ac, 120, false, 0); }
                    fd.Upto(run.Clock.Now);
                }, null);
                long samples = Cine.AudioFramesFor(r.Frames.Count, 30, 1);
                ac.Finish(samples);
                uint[] bits = ReadBits(path);
                PauseTimeline.View v = r.Timeline.Current;
                long s0 = (long)Math.Ceiling(v.Start100[0] / (10000000.0 / AudioCapture.Rate)), e0 = (long)Math.Floor(v.End100[0] / (10000000.0 / AudioCapture.Rate));
                List<long[]> joins = W6Joins(v);
                int leaked = 0;
                for (long f = 0; f < bits.Length / 2; f++)
                {
                    if (W6InJoin(joins, f)) continue;
                    long idx = W6Index(bits[2 * f], bits[2 * f + 1], 0);
                    if (idx > s0 + 1 && idx < e0 - 1) leaked++;
                }
                Check(leaked == 0, leaked + " fotogramas de sonido de dentro de la pausa (llegaron antes de pausar, fechados por delante)");
                int n = W6CheckMapping(bits, v, fd.Frames, 0, "equipo con 120 ms de adelanto");
                W6CheckSync(r, bits, 0, "equipo con 120 ms de adelanto");
                Note("con 120 ms de adelanto nada de la pausa entra; " + n + " fotogramas en su sitio");
            }
            finally { DeleteDir(dir); }
        }

        static void W6SoundJoin()
        {
            string dir = WorkDir("w6-join");
            try
            {
                string path = Path.Combine(dir, "equipo.f32");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                W6Feeder fd = null;
                double[][] pauses = { new double[] { 1000, 1455.7 }, new double[] { 2000.3, 2333.9 } };
                W6Run r = W6Schedule(30, 1, false, 0, 0, pauses, 3000, delegate(W6Run run)
                {
                    if (fd == null) { ac.Pauses = run.Timeline; fd = new W6Feeder(ac, 30, true, 0); }
                    fd.Upto(run.Clock.Now);
                }, null);
                long samples = Cine.AudioFramesFor(r.Frames.Count, 30, 1);
                ac.Finish(samples);
                uint[] bits = ReadBits(path);
                float[] x = new float[bits.Length];
                Buffer.BlockCopy(bits, 0, x, 0, bits.Length * 4);
                long fed = fd.Frames;
                double natural = 2 * Math.PI * 440 / AudioCapture.Rate * 0.5, worst = 0, peak = 0;
                long last = Math.Min(samples, fed - 600);
                for (long f = 1; f < last; f++)
                {
                    worst = Math.Max(worst, Math.Abs(x[2 * f] - x[2 * f - 2]));
                    peak = Math.Max(peak, Math.Abs(x[2 * f]));
                }
                // Without the fade: the jump from the last sample before each pause to the first after it.
                PauseTimeline.View v = r.Timeline.Current;
                double hard = 0, step = 10000000.0 / AudioCapture.Rate;
                for (int i = 0; i < v.Count; i++)
                {
                    long a = (long)Math.Ceiling(v.Start100[i] / step) - 1, b = (long)Math.Ceiling(v.End100[i] / step);
                    hard = Math.Max(hard, Math.Abs(0.5 * Math.Sin(2 * Math.PI * 440 * a / AudioCapture.Rate) - 0.5 * Math.Sin(2 * Math.PI * 440 * b / AudioCapture.Rate)));
                }
                Check(hard > 0.2, "la prueba no corta donde se oir\u00EDa un salto (" + W6N(hard) + ")");
                Check(worst <= natural * 1.4, "salto de " + W6N(worst) + " entre dos muestras (la onda sola salta " + W6N(natural) + ", un corte seco " + W6N(hard) + ")");
                Check(peak <= 0.5 + 1e-6, "el fundido supera la amplitud: " + W6N(peak));
                Check(ac.Joins == 2, "fundidos " + ac.Joins);
                Note("salto m\u00E1ximo " + W6N(worst) + " (onda " + W6N(natural) + ", corte seco " + W6N(hard) + "), pico " + W6N(peak));
            }
            finally { DeleteDir(dir); }
        }

        static void W6SoundUnchanged()
        {
            string dir = WorkDir("w6-same");
            try
            {
                foreach (double ahead in new double[] { 0, 150 })
                {
                    string a = Path.Combine(dir, "a" + ahead + ".f32"), b = Path.Combine(dir, "b" + ahead + ".f32");
                    AudioCapture plain = AudioCapture.ForTest(a, false), timed = AudioCapture.ForTest(b, false);
                    W6Clock c = new W6Clock();
                    timed.Pauses = new PauseTimeline(c.Get);
                    W6Feeder fa = new W6Feeder(plain, ahead, false, 0), fb = new W6Feeder(timed, ahead, false, 0);
                    for (c.Now = 0; c.Now < W6Ms(2000); c.Now += W6Ms(1))
                    {
                        fa.Upto(c.Now);
                        fb.Upto(c.Now);
                    }
                    long total = 2000 * 48 - 1234;
                    plain.Finish(total);
                    timed.Finish(total);
                    byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
                    Check(x.Length == y.Length && x.Length == total * 8, "tama\u00F1os " + x.Length + " y " + y.Length);
                    for (int i = 0; i < x.Length; i++) Check(x[i] == y[i], "con " + ahead + " ms de adelanto el byte " + i + " cambia");
                    Check(timed.Joins == 0 && timed.PausedFrames == 0, "sin pausas no hay fundidos");
                }
                Note("sin pausas el archivo es id\u00E9ntico byte a byte, con y sin adelanto");
            }
            finally { DeleteDir(dir); }
        }

        // 13 hours after a pause (sample offsets past the range of an int): every sample as it came, no fade.
        static void W6SoundHoursLater()
        {
            string dir = WorkDir("w6-hours");
            try
            {
                string path = Path.Combine(dir, "equipo.f32");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                W6Clock c = new W6Clock();
                PauseTimeline tl = new PauseTimeline(c.Get);
                c.Now = 0;
                tl.Pause();
                c.Now = Stopwatch.Frequency;
                tl.Resume();
                ac.Pauses = tl;
                long q0 = 13L * 3600 * 10000000, removed = tl.Current.End100[0] - tl.Current.Start100[0];
                H2Set(ac, "t0", q0 - removed);   // the recording time of the first packet is its start
                c.Now = W6Ms(14.0 * 3600 * 1000);
                const int packets = 20;
                for (int k = 0; k < packets; k++)
                {
                    float[] s = new float[PacketFrames * 2];
                    for (int i = 0; i < s.Length; i++) s[i] = SeqValue((long)k * PacketFrames * 2 + i);
                    ac.Feed(s, PacketFrames, false, q0 + k * PacketTicks);
                }
                ac.Finish(packets * PacketFrames);
                uint[] bits = ReadBits(path);
                Check(bits.Length == packets * PacketFrames * 2, "tama\u00F1o " + bits.Length);
                int bad = 0;
                for (long f = 0; f < packets * PacketFrames; f++) if (W6Index(bits[2 * f], bits[2 * f + 1], 0) != f) bad++;
                Check(bad == 0 && ac.Joins == 0, bad + " muestras cambiadas 13 horas despu\u00E9s de la pausa (fundidos " + ac.Joins + ")");
                Note(packets * PacketFrames + " muestras id\u00E9nticas 13 horas despu\u00E9s de una pausa");
            }
            finally { DeleteDir(dir); }
        }

        // ---- whole recordings ----

        static void W6Cine(bool stopPaused)
        {
            NeedFfmpeg();
            string real = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Stackshot");
            if (string.Equals(Path.GetFullPath(Settings.DataDir).TrimEnd('\\'), Path.GetFullPath(real).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Skip("STACKSHOT_TEST_DATA debe apuntar a una carpeta propia: esta prueba escribe informes en <DataDir>\\cine");
            int w = 64, h = 48, fn = 30, fd = 1;
            string dir = WorkDir(stopPaused ? "w6-cine-paused" : "w6-cine");
            string stamp = "2026-10-12 03.00.0" + (stopPaused ? "2" : "1");
            string nut = Path.Combine(dir, "~grabando " + stamp + ".nut"), sysPath = Path.Combine(dir, "~grabando " + stamp + " equipo.f32");
            string micPath = Path.Combine(dir, "~grabando " + stamp + " micro.f32"), output = Path.Combine(dir, "Cine " + stamp + ".mkv");
            Recorder.RescueRoot = Path.Combine(dir, "recuperado");
            Directory.CreateDirectory(Settings.TempDir);
            CineSink sink = new CineSink(ffmpeg, w, h, fn, fd, nut);
            Session s = null;
            try
            {
                sink.Start();
                AudioCapture sys = AudioCapture.ForTest(sysPath, true), mic = AudioCapture.ForTest(micPath, true);
                W6Feeder fs = null, fm = null;
                byte[] px = new byte[w * h * 4];
                IntPtr buf = Marshal.AllocHGlobal(px.Length);
                W6Run r;
                double[][] pauses = stopPaused ? new double[][] { new double[] { 600.2, 1300.7 }, new double[] { 1900.4, -1 } }
                                               : new double[][] { new double[] { 600.2, 1300.7 }, new double[] { 1900.4, 2300.9 } };
                try
                {
                    r = W6Schedule(fn, fd, false, 0, 0, pauses, 3000, delegate(W6Run run)
                    {
                        if (fs == null)
                        {
                            sys.Pauses = mic.Pauses = run.Timeline;
                            fs = new W6Feeder(sys, 40, false, 0);
                            fm = new W6Feeder(mic, 0, false, 1000000);
                        }
                        fs.Upto(run.Clock.Now);
                        fm.Upto(run.Clock.Now);
                    }, delegate(W6Run run, W6Frame f)
                    {
                        W6Paint(px, w, h, f.Cut);
                        Marshal.Copy(px, 0, buf, px.Length);
                        if (!sink.Write(buf)) throw new TestFailed("Write fall\u00F3 en el fotograma " + f.N + ": " + sink.ErrorTail);
                    });
                }
                finally { Marshal.FreeHGlobal(buf); }
                Settings st = new Settings();
                st.VideoQuality = Recorder.Cinema;
                st.Webcam = 0;
                s = new Session(null, st, ffmpeg, new Rectangle(0, 0, w, h), false, 60, Recorder.Cinema);
                H2Set(s, "video", nut);
                H2Set(s, "sysPath", sysPath);
                H2Set(s, "micPath", micPath);
                H2Set(s, "output", output);
                H2Set(s, "stamp", stamp);
                H2Set(s, "fpsNum", fn);
                H2Set(s, "fpsDen", fd);
                H2Set(s, "frames", (long)r.Frames.Count);
                H2Set(s, "startedAt", new DateTime(2026, 10, 12, 3, 0, 0));
                H2Set(s, "enc", "libx264rgb");
                H2Set(s, "sink", sink);
                H2Set(s, "sys", sys);
                H2Set(s, "mic", mic);
                H2Set(s, "source", H2Source());
                H2Set(s, "pool", new FramePool(w * h * 4, 4));
                s.PausesForTest = r.Timeline;
                sink = null; // the session owns it now (CompleteCine disposes it)
                MethodInfo m = typeof(Session).GetMethod("CompleteCine", H2Any);
                try { m.Invoke(s, null); }
                catch (TargetInvocationException ex) { throw new TestFailed("CompleteCine: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
                CineReport rep = (CineReport)H2Get(s, "report");
                Check(s.ErrorText == null, "error: " + s.ErrorText);
                Check(rep != null && rep.Passed, "la comprobaci\u00F3n ha fallado: " + (rep != null ? OneLine(rep.Summary()) : "sin informe"));
                Check(rep.FramesWritten == r.Frames.Count && rep.Verification.FramesMatched == r.Frames.Count, "fotogramas " + rep.FramesWritten + "/" + rep.Verification.FramesMatched + " de " + r.Frames.Count);
                Check(rep.Pauses == 2, "pausas en el informe: " + rep.Pauses);
                Check(rep.Audio.Count == 3, "pistas: " + rep.Audio.Count);
                bool noted = false;
                foreach (string note in rep.AudioNotes) if (note.Contains("se funde")) noted = true;
                Check(noted, "el informe no dice que el sonido se funde al reanudar: " + string.Join(" | ", rep.AudioNotes.ToArray()));
                Check(rep.Summary().Contains("Se paus\u00F3 2 veces"), "el resumen no cuenta las pausas: " + OneLine(rep.Summary()));
                // R4: the fade changed samples the device gave, so the outcome tells it in full instead of "sin perdidas".
                Check(!rep.CaptureClean, "con el sonido fundido al reanudar el informe se da por limpio y el aviso dice \u00ABsin p\u00E9rdidas\u00BB");
                Check(rep.SoundJoins >= 1 && rep.OverlayFrames == 0, "uniones " + rep.SoundJoins + ", fotogramas con clics " + rep.OverlayFrames);
                string master = s.OutputPath;
                Check(master != null && File.Exists(master), "no hay m\u00E1ster");
                Dictionary<string, object> json = H2ReadReport(master);
                Check(Convert.ToInt32(json["pauses"]) == 2, "pauses en el JSON: " + json["pauses"]);
                Check(Convert.ToInt64(json["soundJoins"]) == rep.SoundJoins && !(bool)json["captureClean"], "soundJoins/captureClean en el JSON: " + json["soundJoins"] + "/" + json["captureClean"]);
                // The originals in the master (identical to the work files, the verification says so): every sample in its place.
                PauseTimeline.View v = r.Timeline.Current;
                long samples = Cine.AudioFramesFor(r.Frames.Count, fn, fd);
                int checkedFrames = 0;
                for (int k = 1; k <= 2; k++)
                {
                    float[] f = H2DecodeFloats(master, k);
                    Check(f.Length == samples * 2, "pista " + k + ": " + f.Length / 2 + " fotogramas, se esperaban " + samples);
                    uint[] bits = new uint[f.Length];
                    Buffer.BlockCopy(f, 0, bits, 0, f.Length * 4);
                    long offset = k == 1 ? 0 : 1000000;
                    checkedFrames += W6CheckMapping(bits, v, (k == 1 ? fs : fm).Frames, offset, k == 1 ? "equipo" : "micro");
                    W6CheckSync(r, bits, offset, k == 1 ? "equipo" : "micro");
                }
                Note(r.Frames.Count + " fotogramas id\u00E9nticos, tres pistas verificadas, " + checkedFrames + " fotogramas de sonido en su sitio" +
                     (stopPaused ? "; detenida en pausa" : ""));
            }
            finally
            {
                if (sink != null) sink.Dispose();
                try
                {
                    string o = s != null ? s.OutputPath : output;
                    if (o != null && File.Exists(H2ReportPath(o))) File.Delete(H2ReportPath(o));
                }
                catch { }
                DeleteDir(dir);
            }
        }

        static void W6Gif()
        {
            NeedFfmpeg();
            string dir = WorkDir("w6-gif");
            try
            {
                int w = 64, h = 48;
                Settings st = new Settings();
                st.GifFps = 10;
                int target = Recorder.TargetFps(st, true, 60);
                string raw = Path.Combine(dir, "frames.bgra"), temp = Path.Combine(dir, "~grabando"), gif = Path.Combine(dir, "GIF.gif");
                byte[] px = new byte[w * h * 4];
                W6Run r;
                using (FileStream fs = File.Create(raw))
                    r = W6Schedule(target, 1, false, 0, 0, new double[][] { new double[] { 500.3, 1500.6 } }, 2500, null, delegate(W6Run run, W6Frame f)
                    {
                        W6Paint(px, w, h, f.Cut);
                        fs.Write(px, 0, px.Length);
                    });
                LivePlan plan = Recorder.PlanLive(Recorder.Standard, true, st.VideoFps, st.GifFps, w, h, false, false, null, false, target, 1, gif, temp);
                Check(plan.Lossless && plan.Video == temp + ".nut" && plan.Args.Contains(" -i - "), "plan del GIF: " + plan.Args);
                string err;
                int code = Ffmpeg(plan.Args.Replace(" -i - ", " -i " + Q(raw) + " "), out err);
                Check(code == 0, "ffmpeg (en vivo) sali\u00F3 con " + code + ": " + err);
                code = Ffmpeg("-hide_banner -loglevel error -y " + Recorder.GifArgs(w, st.GifFps, plan.Video, gif), out err);
                Check(code == 0, "ffmpeg (GIF) sali\u00F3 con " + code + ": " + err);
                int exit;
                List<string> lines = FfmpegLines("-hide_banner -nostdin -loglevel error -i " + Q(gif) + " -f framecrc -", out err, out exit);
                List<string> crcs = new List<string>();
                foreach (string l in lines)
                {
                    if (l.Length == 0 || l[0] == '#') continue;
                    string[] parts = l.Split(',');
                    crcs.Add(parts[parts.Length - 1].Trim());
                }
                double seconds = r.Frames.Count / (double)target;
                int expected = (int)Math.Round(seconds * st.GifFps);
                Check(Math.Abs(crcs.Count - expected) <= 1, "el GIF tiene " + crcs.Count + " fotogramas; " + W6N(seconds) + " s grabados dan " + expected);
                Check(crcs.Count < 2.5 * st.GifFps - 3, "el GIF dura lo del reloj (con la pausa): " + crcs.Count);
                for (int i = 1; i < crcs.Count; i++) Check(crcs[i] != crcs[i - 1], "los fotogramas " + (i - 1) + " y " + i + " del GIF son iguales (imagen congelada)");
                Note(r.Frames.Count + " fotogramas a " + target + " fps -> GIF de " + crcs.Count + " a " + st.GifFps + " fps (" + W6N(seconds) + " s de 2,5 con la pausa)");
            }
            finally { DeleteDir(dir); }
        }

        // ---- the session, safety nets, tray and bar ----

        static void W6SessionToggle()
        {
            Check(!Recorder.CanPause && !Recorder.Paused, "sin grabaci\u00F3n no se puede pausar");
            Recorder.TogglePause();
            Session s = W6Session(null, Recorder.Standard);
            Check(!s.CanPause, "antes de grabar no se puede pausar");
            s.TogglePause();
            Check(!s.Paused && s.PausesForTest.Count == 0, "pausar antes de grabar cambi\u00F3 algo");
            H2Set(s, "writer", new Thread(delegate() { }));
            Check(s.CanPause, "grabando se tiene que poder pausar");
            ((Stopwatch)H2Get(s, "clock")).Start();
            Thread.Sleep(30);
            s.TogglePause();
            Check(s.Paused && s.PausesForTest.Paused, "no se ha pausado");
            TimeSpan a = s.Elapsed;
            Thread.Sleep(60);
            Check(s.Elapsed == a, "el tiempo sigue corriendo en pausa");
            s.TogglePause();
            Check(!s.Paused && !s.PausesForTest.Paused && s.PausesForTest.Count == 1, "no se ha reanudado");
            Thread.Sleep(30);
            Check(s.Elapsed > a, "el tiempo no vuelve a correr");
            Recorder.SetCurrentForTest(s);
            try
            {
                Check(Recorder.CanPause && !Recorder.Paused, "Recorder no ve la grabaci\u00F3n");
                Recorder.TogglePause();
                Check(Recorder.Paused && s.Paused, "Recorder.TogglePause no pausa");
                s.Stop();
                Check(s.StoppingForTest && !s.CanPause && !Recorder.CanPause && !Recorder.Paused, "tras detener todav\u00EDa se puede pausar");
                s.TogglePause();
                Check(s.PausesForTest.Count == 2 && s.PausesForTest.Paused, "pausar tras detener cambi\u00F3 la l\u00EDnea de tiempo");
            }
            finally { Recorder.SetCurrentForTest(null); }
            Note("pausar y reanudar paran y siguen el tiempo; detener en pausa deja de admitir pausas");
        }

        static void W6PauseInstant()
        {
            Session s = W6Running();
            double worst = 0;
            Stopwatch sw = new Stopwatch();
            for (int i = 0; i < 20; i++)
            {
                sw.Restart();
                s.TogglePause();
                sw.Stop();
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            }
            Check(worst < 16, "pausar tarda " + W6N(worst) + " ms");
            PauseTimeline tl = new PauseTimeline();
            sw.Restart();
            for (int i = 0; i < 200; i++) { tl.Pause(); tl.Resume(); }
            sw.Stop();
            double each = sw.Elapsed.TotalMilliseconds / 400;
            PauseTimeline.View v = tl.Current;
            sw.Restart();
            double sum = 0;
            for (int i = 0; i < 20000; i++) sum += v.QpcOf(i * 1000.0);
            sw.Stop();
            double cut = sw.Elapsed.TotalMilliseconds * 1000 / 20000;
            Check(each < 0.1, "cada pausa de la l\u00EDnea de tiempo tarda " + W6N(each) + " ms");
            Check(cut < 20 && sum > 0, "con 200 pausas cada instante de fotograma cuesta " + W6N(cut) + " \u00B5s");
            Note("pausar en la sesi\u00F3n: " + W6N(worst) + " ms como mucho; l\u00EDnea de tiempo " + W6N(each * 1000) + " \u00B5s; con 200 pausas, " + W6N(cut) + " \u00B5s por fotograma");
        }

        static void W6DiskPace()
        {
            string dir = WorkDir("w6-disk");
            try
            {
                Session s = W6Session(null, Recorder.Standard);
                string video = Path.Combine(dir, "v.nut");
                File.WriteAllBytes(video, new byte[300000]);
                H2Set(s, "video", video);
                H2Set(s, "source", H2Source());
                MethodInfo check = typeof(Session).GetMethod("CheckHealth", H2Any);
                long freq = Stopwatch.Frequency;
                foreach (bool paused in new bool[] { true, false })
                {
                    long before = Stopwatch.GetTimestamp() - freq;
                    H2Set(s, "paused", paused);
                    H2Set(s, "diskRate", 5e6);
                    H2Set(s, "lastTotal", 0L);
                    H2Set(s, "lastTotalAt", before);
                    H2Set(s, "nextDiskCheck", 0L);
                    check.Invoke(s, null);
                    double rate = (double)H2Get(s, "diskRate");
                    Check(!(bool)H2Get(s, "halted"), "se ha detenido sola (" + H2Get(s, "stopReason") + ")");
                    Check((long)H2Get(s, "nextDiskCheck") > before + freq, "el disco no se ha comprobado");
                    Check((long)H2Get(s, "lastTotalAt") > before, "no se ha tomado nota de lo escrito");
                    if (paused) Check(rate == 5e6, "en pausa el ritmo de escritura cambia a " + rate + " (la estimaci\u00F3n de minutos saltar\u00EDa)");
                    else Check(rate != 5e6, "grabando el ritmo de escritura no se actualiza");
                }
                Note("en pausa el disco se sigue comprobando y el ritmo se conserva; grabando se actualiza");
            }
            finally { DeleteDir(dir); }
        }

        static void W6LockWhilePaused()
        {
            Session s = W6Running();
            s.TogglePause();
            Check(s.Paused, "no se ha pausado");
            typeof(Session).GetMethod("OnSessionSwitch", H2Any).Invoke(s, new object[] { null, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock) });
            Check((bool)H2Get(s, "halted") && (string)H2Get(s, "stopReason") == Recorder.LockedReason, "bloquear en pausa no detiene: " + H2Get(s, "stopReason"));
            Session t = W6Running();
            t.TogglePause();
            typeof(Session).GetMethod("OnPowerMode", H2Any).Invoke(t, new object[] { null, new PowerModeChangedEventArgs(PowerModes.Suspend) });
            Check((bool)H2Get(t, "halted") && (string)H2Get(t, "stopReason") == Recorder.SuspendReason, "suspender en pausa no detiene: " + H2Get(t, "stopReason"));
            Check(!s.CanPause && !t.CanPause, "detenida se puede seguir pausando");
            Note("bloquear o suspender en pausa detienen y guardan como siempre");
        }

        static List<object> W6TrayItems(TrayPanel tp, bool recording)
        {
            typeof(TrayPanel).GetMethod("BuildItems", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tp, new object[] { recording, false, false });
            List<object> r = new List<object>();
            foreach (object o in (System.Collections.IEnumerable)typeof(TrayPanel).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tp)) r.Add(o);
            return r;
        }

        static object W6Row(List<object> items, string id)
        {
            foreach (object it in items) if ((string)it.GetType().GetField("Id").GetValue(it) == id) return it;
            return null;
        }

        static void W6TrayRow()
        {
            string dir = WorkDir("w6-tray");
            Session s = W6Running();
            Recorder.SetCurrentForTest(s);
            TrayPanel tp = null;
            try
            {
                Settings st = new Settings();
                st.SaveFolder = dir;
                tp = new TrayPanel(st);
                object row = W6Row(W6TrayItems(tp, true), "pause");
                Check(row != null && (string)row.GetType().GetField("Text").GetValue(row) == "Pausar grabaci\u00F3n" && (string)row.GetType().GetField("Icon").GetValue(row) == "pause",
                      "falta \u00ABPausar grabaci\u00F3n\u00BB");
                Recorder.TogglePause();
                List<object> items = W6TrayItems(tp, true);
                row = W6Row(items, "pause");
                Check(row != null && (string)row.GetType().GetField("Text").GetValue(row) == "Reanudar grabaci\u00F3n" && (string)row.GetType().GetField("Icon").GetValue(row) == "play",
                      "falta \u00ABReanudar grabaci\u00F3n\u00BB");
                Check((string)typeof(TrayPanel).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tp) == "Grabaci\u00F3n en pausa", "el estado no dice que est\u00E1 en pausa");
                Check(W6Row(items, "video") != null && W6Row(items, "gif") == null, "las dem\u00E1s filas de grabaci\u00F3n cambiaron");
                Check(W6Row(W6TrayItems(tp, false), "pause") == null, "sin grabar sale la pausa");
                s.Stop();
                Check(W6Row(W6TrayItems(tp, true), "pause") == null, "guardando sale la pausa");
                Note("bandeja: Pausar y Reanudar grabaci\u00F3n, y el estado \u00ABGrabaci\u00F3n en pausa\u00BB");
            }
            finally
            {
                Recorder.SetCurrentForTest(null);
                if (tp != null) tp.Dispose();
                DeleteDir(dir);
            }
        }

        // A recording bar laid out and painted without its window (the real one opens on screen next to the area).
        static RecordBar W6Bar(float scale, bool sys, bool mic, string disk)
        {
            RecordBar b = (RecordBar)FormatterServices.GetUninitializedObject(typeof(RecordBar));
            GC.SuppressFinalize(b); // no constructor ran: the form's finalizer must not try to dispose it
            H2Set(b, "s", scale);
            H2Set(b, "hasSys", sys);
            H2Set(b, "hasMic", mic);
            H2Set(b, "diskText", disk);
            H2Set(b, "diskLevel", disk != null ? 1 : 0);
            H2Set(b, "meter", new float[2]);
            H2Set(b, "cap", new Tween(0));
            H2Set(b, "fade", new Tween(1));
            H2Set(b, "session", W6Session(null, Recorder.Standard));
            H2Set(b, "armedAt", -1.0);
            H2Set(b, "nudgeAt", -1.0);
            H2Set(b, "progress", -1.0);
            H2Set(b, "faceProgress", -1.0);
            H2Set(b, "hot", -1);
            H2Set(b, "pressed", -1);
            H2Set(b, "Pad", (int)Math.Round(22 * scale));
            typeof(RecordBar).GetMethod("Plan", H2Any).Invoke(b, null);
            int recW = (int)H2Get(b, "recW");
            H2Set(b, "body", new Size(recW, (int)Math.Round(46 * scale)));
            ((Tween)H2Get(b, "cap")).Set(recW);
            return b;
        }

        static void W6BarLayout()
        {
            int cases = 0;
            foreach (float scale in new float[] { 1f, 1.25f, 1.5f, 2f })
                foreach (int sources in new int[] { 0, 1, 2, 3 })
                    foreach (string disk in new string[] { null, DiskBudget.Chip(3.4), DiskBudget.Chip(42) })
                    {
                        RecordBar b = W6Bar(scale, (sources & 1) != 0, (sources & 2) != 0, disk);
                        string[] fields = { "pauseX", "stopX", "stopW", "discardX", "recW", "timeX" };
                        int[] before = new int[fields.Length];
                        for (int i = 0; i < fields.Length; i++) before[i] = (int)H2Get(b, fields[i]);
                        H2Set(b, "paused", true);
                        typeof(RecordBar).GetMethod("Plan", H2Any).Invoke(b, null);
                        for (int i = 0; i < fields.Length; i++) Check((int)H2Get(b, fields[i]) == before[i], "pausar mueve " + fields[i] + " a " + scale + "x");
                        int P30 = (int)Math.Round(30 * scale), P8 = (int)Math.Round(8 * scale), P10 = (int)Math.Round(10 * scale);
                        int pauseX = before[0], stopX = before[1], stopW = before[2], discardX = before[3], timeX = before[5];
                        Check(pauseX + P30 <= stopX && stopX + stopW <= discardX, "los botones se pisan a " + scale + "x");
                        double label = Ink.Px("En pausa", Ds.Semibold, (int)Math.Round(12 * scale), Palette.HudLabel).WidthIncludingTrailingWhitespace;
                        double chip = disk == null ? 0 : Math.Ceiling(Ink.Px(disk, Ds.Semibold, (int)Math.Round(12 * scale), Palette.HudLabel).WidthIncludingTrailingWhitespace) + P10;
                        Check(timeX + Math.Ceiling(label) + P8 + chip <= pauseX, "\u00ABEn pausa\u00BB" + (disk != null ? " y el aviso del disco" : "") + " no caben antes del bot\u00F3n a " + scale + "x");
                        Rectangle pr = (Rectangle)typeof(RecordBar).GetMethod("PauseRect", H2Any).Invoke(b, null);
                        Point mid = new Point(pr.X + pr.Width / 2 + (int)H2Get(b, "Pad"), pr.Y + pr.Height / 2 + (int)H2Get(b, "Pad"));
                        H2Set(b, "paused", false);
                        int hit = (int)typeof(RecordBar).GetMethod("HitTest", H2Any).Invoke(b, new object[] { new Point(mid.X - (int)H2Get(b, "Pad"), mid.Y - (int)H2Get(b, "Pad")) });
                        Check(hit == 5, "el centro del bot\u00F3n de pausa da " + hit);
                        cases++;
                    }
            Note(cases + " combinaciones (escala, fuentes de sonido, aviso de disco): pausar no mueve nada y todo cabe");
        }

        static int W6Count(byte[] px, Func<int, int, int, bool> match)
        {
            int n = 0;
            for (int i = 0; i + 3 < px.Length; i += 4) if (px[i + 3] > 200 && match(px[i + 2], px[i + 1], px[i])) n++;
            return n;
        }

        static byte[] W6PaintBar(RecordBar b, out int w, out int h)
        {
            Size body = (Size)H2Get(b, "body");
            int pad = (int)H2Get(b, "Pad");
            w = body.Width + 2 * pad;
            h = body.Height + 2 * pad;
            WM.DrawingVisual dv = new WM.DrawingVisual();
            using (WM.DrawingContext dc = dv.RenderOpen())
                typeof(RecordBar).GetMethod("PaintSurface", H2Any).Invoke(b, new object[] { dc, w, h });
            WMI.RenderTargetBitmap rtb = new WMI.RenderTargetBitmap(w, h, 96, 96, WM.PixelFormats.Pbgra32);
            rtb.Render(dv);
            byte[] px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);
            return px;
        }

        static void W6BarPaint()
        {
            int w, h;
            RecordBar b = W6Bar(1.5f, false, false, null);
            byte[] rec = W6PaintBar(b, out w, out h);
            string sigRec = (string)H2Get(b, "shown");
            H2Set(b, "paused", true);
            byte[] paused = W6PaintBar(b, out w, out h);
            string sigPaused = (string)H2Get(b, "shown");
            Func<int, int, int, bool> amber = delegate(int r, int g, int bl) { return r > 225 && g > 145 && g < 205 && bl < 120; };
            W6Shot(rec, w, h, "bar-recording");
            W6Shot(paused, w, h, "bar-paused");
            int amberRec = W6Count(rec, amber), amberPaused = W6Count(paused, amber);
            Check(amberRec == 0, "grabando hay " + amberRec + " p\u00EDxeles \u00E1mbar");
            Check(amberPaused > 40, "en pausa no se ve \u00ABEn pausa\u00BB (" + amberPaused + " p\u00EDxeles \u00E1mbar)");
            Check(sigRec != sigPaused, "pausar no cambia la firma del dibujo (no se repintar\u00EDa)");
            // The pause button: two light bars while recording, a play sign on a lit circle while paused.
            Rectangle pr = (Rectangle)typeof(RecordBar).GetMethod("PauseRect", H2Any).Invoke(b, null);
            int pad = (int)H2Get(b, "Pad");
            int lightRec = 0, lightPaused = 0;
            for (int y = pr.Y + pad; y < pr.Bottom + pad; y++)
                for (int x = pr.X + pad; x < pr.Right + pad; x++)
                {
                    int o = (y * w + x) * 4;
                    if (rec[o] + rec[o + 1] + rec[o + 2] > 480) lightRec++;
                    if (paused[o] + paused[o + 1] + paused[o + 2] > 480) lightPaused++;
                }
            Check(lightRec > 10 && lightPaused > 10, "el bot\u00F3n de pausa no se ve (" + lightRec + ", " + lightPaused + ")");
            Note("\u00E1mbar en pausa " + amberPaused + " p\u00EDxeles, grabando 0; bot\u00F3n " + lightRec + "/" + lightPaused + " p\u00EDxeles claros");
        }

        // ---- keys ----

        static void W6KeyTexts()
        {
            int C = KeyCaption.Ctrl, A = KeyCaption.Alt, S = KeyCaption.Shift, Wn = KeyCaption.Win;
            object[][] cases =
            {
                new object[] { 0x43, C, "Ctrl + C" }, new object[] { 0x09, A, "Alt + Tab" }, new object[] { 0x53, Wn | S, "Win + May\u00FAs + S" },
                new object[] { 0x0D, 0, "Intro" }, new object[] { 0x1B, 0, "Esc" }, new object[] { 0x09, 0, "Tab" }, new object[] { 0x25, 0, "\u2190" },
                new object[] { 0x26, 0, "\u2191" }, new object[] { 0x27, 0, "\u2192" }, new object[] { 0x28, 0, "\u2193" }, new object[] { 0x74, 0, "F5" },
                new object[] { 0x7B, S, "May\u00FAs + F12" }, new object[] { 0x2E, 0, "Supr" }, new object[] { 0x2E, C | A, "Ctrl + Alt + Supr" },
                new object[] { 0x09, S, "May\u00FAs + Tab" }, new object[] { 0x1B, C | S, "Ctrl + May\u00FAs + Esc" }, new object[] { 0x20, C, "Ctrl + Espacio" },
                new object[] { 0x44, Wn, "Win + D" }, new object[] { 0x73, A, "Alt + F4" }, new object[] { 0x08, 0, "Retroceso" }, new object[] { 0x5A, C | S, "Ctrl + May\u00FAs + Z" },
                new object[] { 0x24, 0, "Inicio" }, new object[] { 0x23, 0, "Fin" }, new object[] { 0x2C, 0, "Impr Pant" }, new object[] { 0x4C, Wn, "Win + L" },
            };
            foreach (object[] c in cases)
            {
                string t = KeyCaption.Text((int)c[0], (int)c[1], false);
                Check(t == (string)c[2], "tecla 0x" + ((int)c[0]).ToString("X2") + " con " + c[1] + ": \u00AB" + t + "\u00BB, se esperaba \u00AB" + c[2] + "\u00BB");
            }
            string emoji = KeyCaption.Text(0xBE, Wn, false);
            Check(emoji != null && emoji.StartsWith("Win + ", StringComparison.Ordinal), "Win + punto: " + emoji);
            Note(cases.Length + " combinaciones con su texto (Win + May\u00FAs + S, Ctrl + Alt + Supr, flechas, F, Supr\u2026)");
        }

        static void W6KeyPrivacy()
        {
            int C = KeyCaption.Ctrl, A = KeyCaption.Alt, S = KeyCaption.Shift, Wn = KeyCaption.Win, G = KeyCaption.AltGr;
            int typing = 0, shown = 0;
            for (int vk = 0; vk < 256; vk++)
                foreach (bool ext in new bool[] { false, true })
                    foreach (int mods in new int[] { 0, S })
                    {
                        string t = KeyCaption.Text(vk, mods, ext);
                        if (KeyCaption.Types(vk)) { typing++; Check(t == null, "se ve lo que se escribe: 0x" + vk.ToString("X2") + " con " + mods + " -> " + t); }
                        else if (t != null) shown++;
                    }
            List<int> chars = new List<int>();
            for (int vk = 0x30; vk <= 0x39; vk++) chars.Add(vk);
            for (int vk = 0x41; vk <= 0x5A; vk++) chars.Add(vk);
            for (int vk = 0xBA; vk <= 0xC0; vk++) chars.Add(vk);
            for (int vk = 0xDB; vk <= 0xDF; vk++) chars.Add(vk);
            chars.Add(0xE2);
            chars.Add(0x20);
            foreach (int vk in chars)
                foreach (int mods in new int[] { G, G | C, G | S, C | A, C | A | S, G | C | A })
                    Check(KeyCaption.Text(vk, mods, false) == null, "AltGr o Ctrl + Alt escriben @, #, \u20AC...: 0x" + vk.ToString("X2") + " con " + mods + " se ve");
            for (int vk = 0x60; vk <= 0x69; vk++) Check(KeyCaption.Text(vk, A, false) == null && KeyCaption.Text(vk, A | S, false) == null, "Alt + teclado num\u00E9rico (c\u00F3digos de car\u00E1cter) se ve");
            foreach (int vk in new int[] { 0x2D, 0x23, 0x28, 0x22, 0x25, 0x0C, 0x27, 0x24, 0x26, 0x21, 0x2E })
                Check(KeyCaption.Text(vk, A, false) == null, "Alt + 0x" + vk.ToString("X2") + " del teclado num\u00E9rico (sin Bloq Num) se ve");
            Check(KeyCaption.Text(0x23, A, true) == "Alt + Fin", "Alt + Fin (la tecla Fin de verdad): " + KeyCaption.Text(0x23, A, true));
            foreach (int vk in new int[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                foreach (int mods in new int[] { 0, C, A, S, Wn })
                    Check(KeyCaption.Text(vk, mods, false) == null, "una tecla modificadora sola sale: 0x" + vk.ToString("X2"));
            foreach (int vk in new int[] { 0xE5, 0xE7, 0x14, 0x90, 0x91, 0xAD, 0xAE, 0xAF, 0xB3 })
                Check(KeyCaption.Text(vk, 0, false) == null && KeyCaption.Text(vk, S, false) == null, "0x" + vk.ToString("X2") + " sale sin atajo");
            Check(KeyCaption.Text(0x41, C, false) == "Ctrl + A" && KeyCaption.Text(0x41, A, false) == "Alt + A" && KeyCaption.Text(0x41, Wn | G, false) != null,
                  "los atajos de verdad deben verse");
            // The overlay filters again whatever reaches it.
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o = W6Overlay(320, 200, 1, c, out tl, out q);
            IntPtr buf = W6Buffer(320 * 200 * 4, 128);
            try
            {
                q.Post(InputEvent.Press(W6Ms(10), 0x41, 0, false, false));
                q.Post(InputEvent.Press(W6Ms(20), 0x32, G | C, false, false));
                o.Draw(buf, false, W6Ms(200));
                Check(o.Caption == null && W6Changed(W6Bytes(buf, 320 * 200 * 4), 128, 0) == 0, "el dibujo muestra lo que se escribe: " + o.Caption);
            }
            finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
            Note(typing + " pulsaciones de escritura (solas o con May\u00FAs) nunca salen; " + shown + " teclas sin escritura s\u00ED");
        }

        // ---- the overlay ----

        // How far the drawing reaches to the right of x0 on row y (last pixel that changed).
        static int W6Reach(byte[] px, int w, int x0, int y, byte value)
        {
            int last = -1;
            for (int x = x0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (Math.Abs(px[o] - value) > 8 || Math.Abs(px[o + 1] - value) > 8 || Math.Abs(px[o + 2] - value) > 8) last = x;
            }
            return last - x0;
        }

        static void W6Rings()
        {
            int w = 400, h = 300;
            double[] reach = new double[2];
            string notes = "";
            for (int k = 0; k < 2; k++)
            {
                float scale = k == 0 ? 1f : 1.5f;
                foreach (bool right in new bool[] { false, true })
                {
                    W6Clock c = new W6Clock();
                    PauseTimeline tl;
                    InputQueue q;
                    InputOverlay o = W6Overlay(w, h, scale, c, out tl, out q);
                    IntPtr buf = W6Buffer(w * h * 4, 128);
                    try
                    {
                        q.Post(InputEvent.Click(W6Ms(1000), 1200, 650, right));
                        o.Draw(buf, false, W6Ms(1200));
                        byte[] px = W6Bytes(buf, w * h * 4);
                        int ext = W6Reach(px, w, 200, 150, 128);
                        double r = 6 * scale + 16 * scale * (1 - 0.125), half = (3 - 0.5) * scale / 2;
                        Check(ext >= r - 1 && ext <= r + half + 1.5, (scale * 100) + " %: el anillo llega a " + ext + " px, se esperaba " + W6N(r) + "-" + W6N(r + half + 1.5));
                        int ro = (150 * w + 200 + (int)Math.Round(r)) * 4;
                        if (right) Check(px[ro + 2] > px[ro] + 30, "el clic derecho no es naranja: " + px[ro + 2] + "/" + px[ro + 1] + "/" + px[ro]);
                        else Check(px[ro] > px[ro + 2] + 30, "el clic izquierdo no es azul: " + px[ro + 2] + "/" + px[ro + 1] + "/" + px[ro]);
                        int co = (150 * w + 200) * 4;
                        Check(px[co] != 128 || px[co + 2] != 128, "el centro del clic no tiene relleno");
                        Check(px[(10 * w + 10) * 4] == 128 && px[co + 3] == 128, "el dibujo toca lo que no debe (o el byte de alfa)");
                        Check(o.Rings == 1, "anillos: " + o.Rings);
                        if (!right) reach[k] = ext;
                        notes += (scale * 100) + " % " + (right ? "der." : "izq.") + " " + ext + " px; ";
                    }
                    finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
                }
            }
            double ratio = reach[1] / reach[0];
            Check(ratio > 1.38 && ratio < 1.62, "a 150 % el anillo deber\u00EDa ser 1,5 veces mayor: " + W6N(ratio));
            Note(notes + "proporci\u00F3n " + W6N(ratio));
        }

        static void W6RingGone()
        {
            int w = 200, h = 120;
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o = W6Overlay(w, h, 1, c, out tl, out q);
            IntPtr buf = W6Buffer(w * h * 4, 90);
            try
            {
                q.Post(InputEvent.Click(W6Ms(500), 1100, 560, false));
                o.Draw(buf, false, W6Ms(500));
                Check(W6Changed(W6Bytes(buf, w * h * 4), 90, 2) > 20, "el clic no se ve en su primer fotograma");
                IntPtr b2 = W6Buffer(w * h * 4, 90);
                try
                {
                    o.Draw(b2, false, W6Ms(500 + InputOverlay.ClickMs + 1));
                    Check(W6Changed(W6Bytes(b2, w * h * 4), 90, 0) == 0 && o.Rings == 0, "el anillo sigue pasados 400 ms");
                }
                finally { Marshal.FreeHGlobal(b2); }
            }
            finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
            Note("el anillo se ve al hacer clic y no queda nada a los 401 ms");
        }

        static void W6Caption()
        {
            int w = 400, h = 300;
            int[] heights = new int[2];
            string notes = "";
            for (int k = 0; k < 2; k++)
            {
                float scale = k == 0 ? 1f : 1.5f;
                W6Clock c = new W6Clock();
                PauseTimeline tl;
                InputQueue q;
                InputOverlay o = W6Overlay(w, h, scale, c, out tl, out q);
                IntPtr buf = W6Buffer(w * h * 4, 128);
                try
                {
                    q.Post(InputEvent.Press(W6Ms(1000), 0x43, KeyCaption.Ctrl, false, false));
                    o.Draw(buf, false, W6Ms(1300));
                    Check(o.Caption == "Ctrl + C", "texto: " + o.Caption);
                    int pw = o.PillWidth, ph = o.PillHeight;
                    Check(ph == (int)Math.Ceiling(17 * scale * 1.9), (scale * 100) + " %: alto " + ph);
                    int margin = Math.Max(4, (int)Math.Round(Math.Min(36 * scale, h * 0.08)));
                    int left = (w - pw) / 2, top = h - margin - ph;
                    byte[] px = W6Bytes(buf, w * h * 4);
                    int dark = (( top + ph / 2) * w + left + (int)(ph * 0.35)) * 4;
                    Check(px[dark] < 60 && px[dark + 1] < 60 && px[dark + 2] < 60, "el fondo de la etiqueta no es oscuro: " + px[dark]);
                    int bright = 0;
                    for (int y = top; y < top + ph; y++)
                        for (int x = left; x < left + pw; x++)
                            if (px[(y * w + x) * 4] > 200) bright++;
                    Check(bright > 25, "no hay texto en la etiqueta (" + bright + " p\u00EDxeles claros)");
                    Check(W6Changed(px, 128, 0) == W6ChangedIn(px, w, left - 1, top - 1, pw + 2, ph + 2, 128), "se ha dibujado fuera de la etiqueta");
                    Check(Math.Abs(left + pw / 2.0 - w / 2.0) <= 1, "la etiqueta no est\u00E1 centrada");
                    heights[k] = ph;
                    notes += (scale * 100) + " %: " + pw + "x" + ph + " a " + margin + " px del borde; ";
                }
                finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
            }
            double ratio = heights[1] / (double)heights[0];
            Check(ratio > 1.4 && ratio < 1.6, "a 150 % la etiqueta deber\u00EDa ser 1,5 veces mayor: " + W6N(ratio));
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STACKSHOT_W6_SHOTS")))
                foreach (float scale in new float[] { 1f, 1.5f }) W6Demo(scale);
            Note(notes);
        }

        // With STACKSHOT_W6_SHOTS: the Grabación page (light and dark), laid out tall enough to show it whole. The window is
        // built but never shown.
        static void W6PageShot()
        {
            string dir = Environment.GetEnvironmentVariable("STACKSHOT_W6_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            foreach (int appearance in new int[] { 1, 2 })
            {
                Settings s = new Settings();
                s.SaveFolder = Path.Combine(dir, "guardado");
                s.RecordKeys = true;
                s.Appearance = appearance;
                Ds.Apply(appearance);
                ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
                GC.SuppressFinalize(owner);
                HomeWindow hw = new HomeWindow(owner, s);
                typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, new object[] { "record", false });
                System.Windows.UIElement root = (System.Windows.UIElement)hw.Content;
                int w = 980, h = 2900;
                root.Measure(new System.Windows.Size(w, h));
                root.Arrange(new System.Windows.Rect(0, 0, w, h));
                root.UpdateLayout();
                WM.DrawingVisual dv = new WM.DrawingVisual();
                using (WM.DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(hw.Background, null, new System.Windows.Rect(0, 0, w, h));
                    dc.DrawRectangle(new WM.VisualBrush(root), null, new System.Windows.Rect(0, 0, w, h));
                }
                WMI.RenderTargetBitmap rtb = new WMI.RenderTargetBitmap(w, h, 96, 96, WM.PixelFormats.Pbgra32);
                rtb.Render(dv);
                WMI.PngBitmapEncoder enc = new WMI.PngBitmapEncoder();
                enc.Frames.Add(WMI.BitmapFrame.Create(rtb));
                using (FileStream fs = File.Create(Path.Combine(dir, "record-page-" + (appearance == 1 ? "light" : "dark") + ".png"))) enc.Save(fs);
            }
            Ds.Apply(0);
        }

        // A frame as a viewer would see it: light and dark halves, two clicks and a shortcut.
        static void W6Demo(float scale)
        {
            int w = (int)(640 * scale), h = (int)(360 * scale), bytes = w * h * 4;
            byte[] bg = new byte[bytes];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    byte v = (byte)(x < w / 2 ? 236 - y * 30 / h : 40 + y * 20 / h);
                    bg[o] = (byte)(v + 6); bg[o + 1] = v; bg[o + 2] = v; bg[o + 3] = 255;
                }
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o2 = W6Overlay(w, h, scale, c, out tl, out q);
            IntPtr buf = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.Copy(bg, 0, buf, bytes);
                q.Post(InputEvent.Click(W6Ms(880), 1000 + w / 4, 500 + h / 3, false));
                q.Post(InputEvent.Click(W6Ms(750), 1000 + w * 3 / 4, 500 + h / 3, true));
                q.Post(InputEvent.Press(W6Ms(700), 0x53, KeyCaption.Win | KeyCaption.Shift, false, false));
                o2.Draw(buf, false, W6Ms(1000));
                W6Shot(W6Bytes(buf, bytes), w, h, "overlay-demo-" + (int)(scale * 100));
            }
            finally { Marshal.FreeHGlobal(buf); o2.Dispose(); }
        }

        static int W6ChangedIn(byte[] px, int w, int x0, int y0, int cw, int ch, byte value)
        {
            int n = 0;
            for (int y = Math.Max(0, y0); y < y0 + ch && y * w * 4 < px.Length; y++)
                for (int x = Math.Max(0, x0); x < x0 + cw && x < w; x++)
                    for (int i = 0; i < 4; i++)
                        if (Math.Abs(px[(y * w + x) * 4 + i] - value) > 0) n++;
            return n;
        }

        static void W6CaptionRepeat()
        {
            int w = 320, h = 200, bytes = w * h * 4;
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o = W6Overlay(w, h, 1, c, out tl, out q);
            try
            {
                int Z = 0x5A, C = KeyCaption.Ctrl;
                q.Post(InputEvent.Press(W6Ms(0), Z, C, false, false));
                q.Post(InputEvent.Press(W6Ms(300), Z, C, false, false));
                q.Post(InputEvent.Press(W6Ms(600), Z, C, false, false));
                q.Post(InputEvent.Press(W6Ms(700), Z, C, false, true));
                IntPtr buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(800));
                Marshal.FreeHGlobal(buf);
                Check(o.Caption == "Ctrl + Z \u00D73", "tres pulsaciones (y una repetici\u00F3n de la tecla mantenida): " + o.Caption);
                // Half way through the fade (the hold counts from the last press, the held key's repeat).
                buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(700 + InputOverlay.HoldMs + InputOverlay.FadeMs / 2));
                byte[] px = W6Bytes(buf, bytes);
                Marshal.FreeHGlobal(buf);
                int pw = o.PillWidth, ph = o.PillHeight, margin = Math.Max(4, (int)Math.Round(Math.Min(36.0, h * 0.08)));
                int at = ((h - margin - ph + ph / 2) * w + (w - pw) / 2 + (int)(ph * 0.35)) * 4;
                Check(px[at] > 55 && px[at] < 110, "a mitad del fundido el fondo vale " + px[at]);
                buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(700 + InputOverlay.HoldMs + InputOverlay.FadeMs + 1));
                Check(W6Changed(W6Bytes(buf, bytes), 128, 0) == 0 && o.Caption == null, "la etiqueta sigue al terminar el fundido");
                Marshal.FreeHGlobal(buf);
                q.Post(InputEvent.Press(W6Ms(3000), 0x53, C, false, false));
                q.Post(InputEvent.Press(W6Ms(3100), 0x43, C, false, false));
                buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(3110));
                px = W6Bytes(buf, bytes);
                Marshal.FreeHGlobal(buf);
                Check(o.Caption == "Ctrl + C", "otra combinaci\u00F3n sustituye a la anterior: " + o.Caption);
                at = ((h - margin - o.PillHeight + o.PillHeight / 2) * w + (w - o.PillWidth) / 2 + (int)(o.PillHeight * 0.35)) * 4;
                Check(px[at] < 60, "al cambiar de combinaci\u00F3n la etiqueta parpadea (vuelve a aparecer poco a poco): " + px[at]);
            }
            finally { o.Dispose(); }
            Note("\u00D73 al repetir, fundido al final, la siguiente combinaci\u00F3n sustituye a la anterior");
        }

        static void W6Nv12()
        {
            int w = 400, h = 300, nvBytes = w * h * 3 / 2, rgbBytes = w * h * 4;
            W6Clock c = new W6Clock();
            PauseTimeline t1, t2;
            InputQueue q1, q2;
            InputOverlay nv = W6Overlay(w, h, 1, c, out t1, out q1), rgb = W6Overlay(w, h, 1, c, out t2, out q2);
            // The same grey in both: BGRA 128 is luma 126 (BT.709, limited range), chroma neutral.
            IntPtr nb = W6Buffer(nvBytes, 128), rb = W6Buffer(rgbBytes, 128);
            byte[] luma = new byte[w * h];
            for (int i = 0; i < luma.Length; i++) luma[i] = 126;
            Marshal.Copy(luma, 0, nb, luma.Length);
            try
            {
                foreach (InputQueue q in new InputQueue[] { q1, q2 })
                {
                    q.Post(InputEvent.Click(W6Ms(100), 1100, 600, false));
                    q.Post(InputEvent.Click(W6Ms(100), 1300, 600, true));
                    q.Post(InputEvent.Press(W6Ms(100), 0x09, KeyCaption.Alt, false, false));
                }
                nv.Draw(nb, true, W6Ms(250));
                rgb.Draw(rb, false, W6Ms(250));
                byte[] y = W6Bytes(nb, nvBytes), p = W6Bytes(rb, rgbBytes);
                // Luma of the BGRA result against the NV12 one, pixel by pixel.
                int worst = 0, changed = 0;
                for (int i = 0; i < w * h; i++)
                {
                    double l = 16 + (0.2126 * p[4 * i + 2] + 0.7152 * p[4 * i + 1] + 0.0722 * p[4 * i]) * 219 / 255;
                    worst = Math.Max(worst, (int)Math.Round(Math.Abs(l - y[i])));
                    if (y[i] != 126) changed++;
                }
                Check(changed > 200, "NV12: casi nada dibujado (" + changed + ")");
                Check(worst <= 2, "NV12 y BGRA no coinciden: diferencia de luma " + worst);
                // Chroma at each ring (row 100 of the area, 18 px right of the click): blue raises Cb, orange raises Cr.
                int lr = (int)Math.Round(6 + 16 * (1 - Math.Pow(1 - 150.0 / 400, 3)));
                int uvl = w * h + 50 * w + (100 + lr) / 2 * 2, uvr = w * h + 50 * w + (300 + lr) / 2 * 2;
                Check(y[uvl] > 140 && y[uvl + 1] < 128, "el clic izquierdo no es azul (Cb " + y[uvl] + ", Cr " + y[uvl + 1] + ")");
                Check(y[uvr + 1] > 140 && y[uvr] < 128, "el clic derecho no es naranja (Cb " + y[uvr] + ", Cr " + y[uvr + 1] + ")");
                int margin = Math.Max(4, (int)Math.Round(Math.Min(36.0, h * 0.08)));
                int pillY = y[(h - margin - nv.PillHeight / 2) * w + (w - nv.PillWidth) / 2 + (int)(nv.PillHeight * 0.35)];
                Check(pillY < 70, "NV12: el fondo de la etiqueta no es oscuro (Y " + pillY + ")");
                Note("NV12 coincide con BGRA (luma \u00B1" + worst + "); Cb " + y[uvl] + " en el clic izquierdo, Cr " + y[uvr + 1] + " en el derecho");
            }
            finally { Marshal.FreeHGlobal(nb); Marshal.FreeHGlobal(rb); nv.Dispose(); rgb.Dispose(); }
        }

        static void W6OverlayPause()
        {
            int w = 320, h = 200, bytes = w * h * 4;
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o = W6Overlay(w, h, 1, c, out tl, out q);
            try
            {
                q.Post(InputEvent.Press(W6Ms(10), 0x43, KeyCaption.Ctrl, false, false));
                c.Now = W6Ms(50);
                tl.Pause();
                q.Post(InputEvent.Click(W6Ms(100), 1160, 600, false));
                q.Post(InputEvent.Press(W6Ms(200), 0x56, KeyCaption.Ctrl, false, false));
                IntPtr buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(45));
                int before = W6Changed(W6Bytes(buf, bytes), 128, 2);
                Marshal.FreeHGlobal(buf);
                Check(before > 20 && o.Caption == "Ctrl + C", "lo de antes de la pausa no se ve antes de ella (" + before + ", " + o.Caption + ")");
                c.Now = W6Ms(500);
                tl.Resume();
                buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(600));
                int after = W6Changed(W6Bytes(buf, bytes), 128, 0);
                Marshal.FreeHGlobal(buf);
                Check(after == 0 && o.Rings == 0 && o.Caption == null, "tras reanudar se ve lo de la pausa o lo de antes (" + after + " bytes, " + o.Rings + " anillos, " + o.Caption + ")");
                q.Post(InputEvent.Click(W6Ms(620), 1160, 600, false));
                buf = W6Buffer(bytes, 128);
                o.Draw(buf, false, W6Ms(700));
                int later = W6Changed(W6Bytes(buf, bytes), 128, 2);
                Marshal.FreeHGlobal(buf);
                Check(later > 20, "un clic tras reanudar no se ve");
            }
            finally { o.Dispose(); }
            Note("nada de lo pulsado en pausa se dibuja, ni lo de antes de una pausa despu\u00E9s de ella");
        }

        static void W6OverlayHidden()
        {
            int w = 320, h = 200, bytes = w * h * 4;
            W6Clock c = new W6Clock();
            PauseTimeline tl;
            InputQueue q;
            InputOverlay o = W6Overlay(w, h, 1, c, out tl, out q);
            o.Hidden = delegate(int x, int y) { return x < 1100; };
            IntPtr buf = W6Buffer(bytes, 128);
            try
            {
                q.Post(InputEvent.Click(W6Ms(10), 1050, 600, false));     // on a window hidden from captures
                q.Post(InputEvent.Click(W6Ms(10), 9000, 9000, false));    // far from the area
                q.Post(InputEvent.Click(W6Ms(10), 1200, 600, true));
                o.Draw(buf, false, W6Ms(100));
                Check(o.Rings == 1, "anillos: " + o.Rings + " (solo deber\u00EDa verse el clic visible)");
                byte[] px = W6Bytes(buf, bytes);
                Check(W6ChangedIn(px, w, 0, 0, 90, h, 128) == 0, "se ha dibujado el clic sobre la ventana oculta");
            }
            finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
            Note("clics en ventanas ocultas a la captura o lejos del \u00E1rea: sin anillo");
        }

        static void W6Queue()
        {
            InputQueue q = new InputQueue();
            for (int i = 0; i < 1000; i++) q.Post(InputEvent.Click(i, i, i, false));
            Check(q.Count == InputQueue.Max, "la cola tiene " + q.Count);
            InputEvent e;
            int n = 0;
            long last = -1, first = -1;
            while (q.TryTake(out e))
            {
                Check(e.Time > last, "orden");
                if (first < 0) first = e.Time;
                last = e.Time;
                n++;
            }
            Check(n == InputQueue.Max && q.Count == 0, "salen " + n);
            Check(first == 1000 - InputQueue.Max && last == 999, "se quedan los m\u00E1s antiguos (" + first + "-" + last + "): lo de despu\u00E9s de una pausa larga no saldr\u00EDa");
            q.Post(InputEvent.Click(5, 5, 5, false));
            Check(q.Count == 1, "tras vaciarla no admite m\u00E1s");
            Note("una avalancha de " + 1000 + " se queda en los " + InputQueue.Max + " m\u00E1s recientes, sin esperar");
        }

        static void W6OverlayPlumbing()
        {
            Settings st = new Settings();
            st.RecordClicks = true;
            Session s = W6Session(st, Recorder.Standard);
            s.StartInputForTest();
            InputOverlay o = s.OverlayForTest;
            Check(o != null, "sin dibujo");
            H2Set(s, "source", H2Source());
            MethodInfo note = typeof(Session).GetMethod("NoteCut", H2Any), over = typeof(Session).GetMethod("Overlaid", H2Any);
            int bytes = 64 * 48 * 4;
            IntPtr a = W6Buffer(bytes, 128), b = W6Buffer(bytes, 128);
            try
            {
                s.InputsForTest.Post(InputEvent.Click(W6Ms(1000), 32, 24, false));
                note.Invoke(s, new object[] { (double)W6Ms(900) });
                note.Invoke(s, new object[] { (double)W6Ms(1100) });
                bool r1 = (bool)over.Invoke(s, new object[] { a, false });   // the screen could not be read: nothing drawn, its time used up
                bool r2 = (bool)over.Invoke(s, new object[] { b, true });
                Check(!r1 && r2, "Overlaid cambia lo le\u00EDdo");
                Check(W6Changed(W6Bytes(a, bytes), 128, 0) == 0, "se ha dibujado en un fotograma que no se ley\u00F3");
                Check(W6Changed(W6Bytes(b, bytes), 128, 2) > 20, "el fotograma de las 1100 ms no lleva el clic de las 1000 (toma la hora de otro)");
            }
            finally { Marshal.FreeHGlobal(a); Marshal.FreeHGlobal(b); }
            typeof(Session).GetMethod("Release", H2Any).Invoke(s, null);
            IntPtr c2 = W6Buffer(bytes, 128);
            try
            {
                s.InputsForTest.Post(InputEvent.Click(W6Ms(2000), 32, 24, false));
                o.Draw(c2, false, W6Ms(2050));
                Check(W6Changed(W6Bytes(c2, bytes), 128, 0) == 0, "tras terminar se sigue dibujando");
            }
            finally { Marshal.FreeHGlobal(c2); }
            Note("cada fotograma le\u00EDdo usa la hora del suyo; al terminar, el dibujo se suelta");
        }

        static void W6SessionInput()
        {
            Settings off = new Settings();
            Check(!off.RecordClicks && !off.RecordKeys, "vienen encendidos");
            Session s = W6Session(off, Recorder.Standard);
            s.StartInputForTest();
            Check(s.OverlayForTest == null && s.InputsForTest == null, "con las dos opciones apagadas se prepara el dibujo");
            Check((bool)typeof(Session).GetMethod("Overlaid", H2Any).Invoke(s, new object[] { IntPtr.Zero, true }), "sin dibujo, Overlaid debe dejar pasar el fotograma");
            foreach (int which in new int[] { 1, 2, 3 })
            {
                Settings st = new Settings();
                st.RecordClicks = (which & 1) != 0;
                st.RecordKeys = (which & 2) != 0;
                Session t = W6Session(st, Recorder.Standard);
                t.StartInputForTest();
                Check(t.OverlayForTest != null && t.InputsForTest != null, "con la opci\u00F3n " + which + " no hay dibujo");
                Check(InputHooks.Live == 0 && H2Get(t, "hooks") == null, "en una prueba se han instalado ganchos");
                typeof(Session).GetMethod("Release", H2Any).Invoke(t, null);
            }
            Note("apagado no se prepara nada; encendido hay dibujo y cola, nunca ganchos en pruebas");
        }

        // ---- review (R4): hooks' thread lifecycle, every exit path, the overlay's cost at 4K, pausing before the first frame

        static bool R4Until(Func<bool> cond, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(2);
            }
            return cond();
        }

        static Thread R4HookThread(InputHooks h)
        {
            return (Thread)typeof(InputHooks).GetField("thread", H2Any).GetValue(h);
        }

        // The hooks' thread with neither hook asked for (so nothing is ever installed): it must end whenever it is disposed,
        // before it has its message queue or while it waits in GetMessage, and a second Dispose is harmless.
        static void R4HooksThreadEnds()
        {
            Check(InputHooks.Live == 0, "hay ganchos antes de empezar: " + InputHooks.Live);
            FieldInfo tid = typeof(InputHooks).GetField("tid", H2Any);
            List<Thread> threads = new List<Thread>();
            int waited = 0;
            for (int i = 0; i < 40; i++)
            {
                InputHooks h = InputHooks.Start(new InputQueue(), false, false);
                if (i % 2 == 1)
                {
                    InputHooks hh = h;
                    Check(R4Until(delegate { return (uint)tid.GetValue(hh) != 0; }, 3000), "el hilo de los ganchos no lleg\u00F3 a esperar mensajes");
                    waited++;
                }
                h.Dispose();
                if (i % 5 == 0) h.Dispose();
                threads.Add(R4HookThread(h));
            }
            int alive = 0;
            foreach (Thread t in threads) if (!t.Join(3000)) alive++;
            Check(alive == 0, alive + " de " + threads.Count + " hilos de los ganchos siguen vivos tras Dispose");
            Check(InputHooks.Live == 0, "ganchos instalados: " + InputHooks.Live);
            Note(threads.Count + " hilos terminan al soltarlos (" + waited + " ya esperando mensajes, el resto al instante); ning\u00FAn gancho instalado");
        }

        // Stop, a start abandoned (failed, or a quit during the countdown) and the end of the session (Done, Shutdown: Release)
        // all let go of the hooks. The hooks here are the real class with neither hook asked for.
        static void R4HookExitPaths()
        {
            string[] paths = { "Detener", "abandonar", "terminar" };
            for (int k = 0; k < paths.Length; k++)
            {
                Session s = k == 1 ? W6Session(null, Recorder.Standard) : W6Running();
                InputHooks h = InputHooks.Start(new InputQueue(), false, false);
                H2Set(s, "hooks", h);
                if (k == 0) s.Stop();
                else if (k == 1) Check(s.Abandon(), "Abandon no se hizo cargo de una grabaci\u00F3n sin empezar");
                else typeof(Session).GetMethod("Release", H2Any).Invoke(s, null);
                Check(H2Get(s, "hooks") == null, paths[k] + " deja los ganchos en la sesi\u00F3n");
                Check(R4HookThread(h).Join(3000), paths[k] + " no termina el hilo de los ganchos");
            }
            Check(InputHooks.Live == 0, "ganchos instalados: " + InputHooks.Live);
            Note("Detener, abandonar y terminar sueltan los ganchos y su hilo acaba");
        }

        // The overlay on 3840 x 2160 frames at 200 %, BGRA and NV12: idle (on, nothing pressed) it must cost next to nothing,
        // and with clicks every 100 ms and a shortcut on screen it must stay a small part of a 60 fps frame (16.7 ms) on
        // the capture thread.
        static void R4Overlay4k()
        {
            int w = 3840, h = 2160;
            string notes = "";
            foreach (bool nv12 in new bool[] { false, true })
            {
                int bytes = nv12 ? w * h * 3 / 2 : w * h * 4;
                W6Clock c = new W6Clock();
                PauseTimeline tl = new PauseTimeline(c.Get);
                InputQueue q = new InputQueue();
                InputOverlay o = new InputOverlay(new Rectangle(0, 0, w, h), 2f, true, true, tl, q);
                IntPtr buf = W6Buffer(bytes, 100);
                try
                {
                    double frame = 1000.0 / 60;
                    Stopwatch sw = new Stopwatch();
                    double idleSum = 0, idleWorst = 0;
                    for (int i = 0; i < 120; i++)
                    {
                        sw.Restart();
                        o.Draw(buf, nv12, W6Ms(1000 + i * frame));
                        sw.Stop();
                        idleSum += sw.Elapsed.TotalMilliseconds;
                        idleWorst = Math.Max(idleWorst, sw.Elapsed.TotalMilliseconds);
                    }
                    Check(W6Changed(W6Bytes(buf, bytes), 100, 0) == 0, "sin pulsar nada se ha dibujado algo");
                    // Two seconds: a click every 100 ms (left and right, across the frame) and Ctrl + C every 300 ms.
                    for (int i = 0; i < 20; i++)
                        q.Post(InputEvent.Click(W6Ms(3000 + i * 100), 400 + i * 150, 300 + (i % 7) * 220, i % 2 == 1));
                    for (int i = 0; i < 7; i++) q.Post(InputEvent.Press(W6Ms(3000 + i * 300), 0x43, KeyCaption.Ctrl, false, false));
                    double busySum = 0, busyWorst = 0, firstCaption = -1;
                    int frames = 0, maxRings = 0;
                    for (int i = 0; i < 120; i++)
                    {
                        sw.Restart();
                        o.Draw(buf, nv12, W6Ms(3000 + i * frame));
                        sw.Stop();
                        double ms = sw.Elapsed.TotalMilliseconds;
                        if (firstCaption < 0) { firstCaption = ms; continue; }   // the caption's picture is made once per text
                        busySum += ms;
                        busyWorst = Math.Max(busyWorst, ms);
                        maxRings = Math.Max(maxRings, o.Rings);
                        frames++;
                    }
                    Check(maxRings >= 3 && o.Caption != null, "no hab\u00EDa anillos o r\u00F3tulo que dibujar: " + maxRings + " anillos, r\u00F3tulo " + o.Caption);
                    double idle = idleSum / 120, busy = busySum / frames;
                    string what = nv12 ? "NV12" : "BGRA";
                    Check(idle < 0.1, what + ": sin pulsar nada cada fotograma cuesta " + W6N(idle) + " ms");
                    Check(busy < 8, what + ": con clics y r\u00F3tulo cada fotograma cuesta " + W6N(busy) + " ms de media (peor " + W6N(busyWorst) + ")");
                    Check(firstCaption < 50, what + ": hacer el r\u00F3tulo cuesta " + W6N(firstCaption) + " ms");
                    notes += what + ": en reposo " + W6N(idle) + " ms (peor " + W6N(idleWorst) + "), con " + maxRings + " anillos y r\u00F3tulo " + W6N(busy) + " ms (peor " +
                             W6N(busyWorst) + "), r\u00F3tulo nuevo " + W6N(firstCaption) + " ms; ";
                }
                finally { Marshal.FreeHGlobal(buf); o.Dispose(); }
            }
            Note(notes.TrimEnd(' ', ';'));
        }

        // During the countdown nothing can be paused. Right after Go, t0 is still 50 ms ahead: a pause pressed in that moment
        // must keep image and sound together (both start at the same instant after it) and nothing of the pause may get in.
        static void R4PauseBeforeFirstFrame()
        {
            Session p = W6Running();
            H2Set(p, "preparing", true);
            Check(!p.CanPause, "durante la cuenta atr\u00E1s se puede pausar");
            p.TogglePause();
            Check(!p.Paused && p.PausesForTest.Count == 0, "pausar en la cuenta atr\u00E1s cambi\u00F3 algo");

            string dir = WorkDir("r4-early");
            try
            {
                W6Clock c = new W6Clock();
                PauseTimeline tl = new PauseTimeline(c.Get);
                Session s = W6Session(null, Recorder.Standard);
                s.PausesForTest = tl;
                long t0 = W6Ms(50);
                s.T0ForTest = t0;
                string path = Path.Combine(dir, "micro.f32");
                AudioCapture ac = AudioCapture.ForTest(path, true);
                ac.Begin(t0);
                ac.Pauses = tl;
                W6Feeder fd = new W6Feeder(ac, 0, false, 0);
                for (int ms = 0; ms <= 1500; ms++)
                {
                    c.Now = W6Ms(ms);
                    if (ms == 20) Check(tl.Pause(), "no se paus\u00F3");
                    if (ms == 320) Check(tl.Resume(), "no se reanud\u00F3");
                    fd.Upto(c.Now);
                }
                PauseTimeline.View v = tl.Current;
                long removed = v.Removed;
                Check(removed == W6Ms(320) - W6Ms(20), "la pausa quit\u00F3 " + W6N(W6ToMs(removed)) + " ms");
                double period = Stopwatch.Frequency / 30.0;
                // The first frame comes after the pause, exactly t0 + its length later: what the clock showed at 350 ms + half a frame.
                double first = s.CutForTest(0, period, 0, false, v);
                Check(Math.Abs(first - (t0 + removed + 0.5 * period)) <= 1, "el primer fotograma es de las " + W6N(W6ToMs(first)) + " ms");
                long frames = 0;
                while (t0 + frames * period < v.RtOf(c.Now)) frames++;
                for (long n = 0; n < frames; n++)
                {
                    double cut = s.CutForTest(n, period, 0, false, v);
                    Check(cut >= W6Ms(320), "el fotograma " + n + " es de dentro de la pausa (" + W6N(W6ToMs(cut)) + " ms)");
                }
                long samples = (long)Math.Round(frames / 30.0 * AudioCapture.Rate);
                ac.Finish(samples);
                uint[] bits = ReadBits(path);
                Check(bits.Length == samples * 2, "el sonido tiene " + bits.Length / 2 + " fotogramas y el v\u00EDdeo pide " + samples);
                // Each sound frame f comes from the clock at t0 + the pause + f: the same shift as the image (past what was fed,
                // the end of the last frame, silence).
                long shift = (long)Math.Round(PauseTimeline.To100(t0 + removed) * (AudioCapture.Rate / 1e7));
                int bad = 0;
                string firstBad = null;
                for (long f = 0; f < samples; f++)
                {
                    long idx = W6Index(bits[2 * f], bits[2 * f + 1], 0);
                    if (idx >= 0 && Math.Abs(idx - (shift + f)) <= 1) continue;
                    if (idx == -1 && shift + f >= fd.Frames - 1) continue;
                    bad++;
                    if (firstBad == null) firstBad = "fotograma " + f + " es " + idx + " y deber\u00EDa ser " + (shift + f);
                }
                Check(bad == 0, bad + " fotogramas de sonido fuera de su sitio (" + firstBad + ")");
                // Image and sound of the same frame come from the same instant.
                double perSample = Stopwatch.Frequency / (double)AudioCapture.Rate, worst = 0;
                for (long n = 0; n < frames; n++)
                {
                    long j = (long)Math.Round((n + 0.5) * period / perSample);
                    if (j >= samples) break;
                    long idx = W6Index(bits[2 * j], bits[2 * j + 1], 0);
                    if (idx < 0) continue;
                    worst = Math.Max(worst, Math.Abs(s.CutForTest(n, period, 0, false, v) - idx * perSample));
                }
                Check(worst <= perSample * 2, "imagen y sonido separados " + W6N(W6ToMs(worst)) + " ms");
                Note("en la cuenta atr\u00E1s no se pausa; una pausa antes del primer fotograma deja " + frames + " fotogramas e imagen y sonido a " + W6N(W6ToMs(worst)) + " ms");
            }
            finally { DeleteDir(dir); }
        }

        // Cine tells in full whatever makes the frames differ from the screen or the sound from what the device gave
        // (CineReport.CaptureClean decides between the "sin perdidas" notice and the whole report): the fade at each resume
        // and the clicks and keys drawn into the frames are such changes. A pause on its own (time skipped, every frame
        // still the screen) and the overlay switched on with nothing drawn are not.
        static CineReport R4Report(PauseTimeline tl, AudioCapture sys, InputOverlay overlay)
        {
            Settings st = new Settings();
            st.Webcam = 0;
            Session s = new Session(null, st, ffmpeg, new Rectangle(0, 0, 64, 48), false, 60, Recorder.Cinema);
            H2Set(s, "source", H2Source());
            H2Set(s, "pool", new FramePool(64 * 48 * 4, 4));
            H2Set(s, "fpsNum", 30);
            H2Set(s, "fpsDen", 1);
            H2Set(s, "frames", 90L);
            if (tl != null) s.PausesForTest = tl;
            if (sys != null) H2Set(s, "sys", sys);
            if (overlay != null) H2Set(s, "overlay", overlay);
            return (CineReport)typeof(Session).GetMethod("BuildReport", H2Any).Invoke(s, null);
        }

        static void R4CineAltered()
        {
            CineReport plain = R4Report(null, null, null);
            Check(plain.CaptureClean, "una grabaci\u00F3n sin nada raro no sale limpia: " + OneLine(plain.Summary()));
            // Two pauses, no sound: nothing in the file differs from the screen.
            W6Clock c = new W6Clock();
            PauseTimeline tl = new PauseTimeline(c.Get);
            c.Now = W6Ms(500); tl.Pause();
            c.Now = W6Ms(900); tl.Resume();
            c.Now = W6Ms(1500); tl.Pause();
            c.Now = W6Ms(1700); tl.Resume();
            CineReport paused = R4Report(tl, null, null);
            Check(paused.Pauses == 2 && paused.Summary().Contains("Se paus\u00F3 2 veces"), "las pausas: " + OneLine(paused.Summary()));
            Check(paused.CaptureClean, "dos pausas sin sonido ya no salen limpias");
            // The sound faded across a resume: told in full.
            string dir = WorkDir("r4-clean");
            try
            {
                AudioCapture sys = AudioCapture.ForTest(Path.Combine(dir, "equipo.f32"), true);
                sys.Joins = 1;
                CineReport faded = R4Report(tl, sys, null);
                sys.Finish(0);
                Check(!faded.CaptureClean, "con el sonido fundido al reanudar sale limpia (aviso \u00ABsin p\u00E9rdidas\u00BB)");
                Check(faded.SoundJoins == 1, "uniones en el informe: " + faded.SoundJoins);
                Check(faded.Summary().Contains("se funde durante 10 ms"), "el resumen no lo dice: " + OneLine(faded.Summary()));
            }
            finally { DeleteDir(dir); }
            // The overlay on but nothing pressed: every frame is still the screen.
            int w = 64, h = 48;
            IntPtr buf = W6Buffer(w * h * 4, 100);
            PauseTimeline otl;
            InputQueue q;
            InputOverlay idle = W6Overlay(w, h, 1f, c, out otl, out q);
            InputOverlay busy = W6Overlay(w, h, 1f, c, out otl, out q);
            try
            {
                for (int i = 0; i < 8; i++) idle.Draw(buf, false, W6Ms(1000 + i * 100));
                CineReport quiet = R4Report(null, null, idle);
                Check(quiet.CaptureClean && quiet.OverlayFrames == 0, "con los clics activados pero sin pulsar nada no sale limpia: " + quiet.OverlayFrames);
                // A click: its ring is in the frames of the next 400 ms (here 4 of them), and the report says so.
                q.Post(InputEvent.Click(W6Ms(1000), 1032, 524, false));
                for (int i = 0; i < 8; i++) busy.Draw(buf, false, W6Ms(1000 + i * 100));
                Check(W6Changed(W6Bytes(buf, w * h * 4), 100, 0) > 0, "el clic no se dibuj\u00F3");
                CineReport drawn = R4Report(null, null, busy);
                Check(!drawn.CaptureClean, "con clics dibujados en los fotogramas sale limpia (aviso \u00ABsin p\u00E9rdidas\u00BB)");
                Check(drawn.OverlayFrames == 4 && busy.DrawnFrames == 4, "fotogramas con el clic: " + drawn.OverlayFrames);
                Check(drawn.ToJson().Contains("\"overlayFrames\":4"), "el JSON no lo cuenta");
                Check(drawn.Summary().Contains("4 fotogramas llevan dibujados los clics o las teclas pulsadas"), "el resumen no lo dice: " + OneLine(drawn.Summary()));
                Note("sin nada raro, con pausas solas o con clics activados sin pulsar: limpia; con el sonido fundido o 4 fotogramas con clics: se cuenta todo");
            }
            finally { Marshal.FreeHGlobal(buf); idle.Dispose(); busy.Dispose(); }
        }
    }
}

// Stackshot - Pausing a recording: the same moments are cut from the video and from the sound.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Stackshot
{
    // The pauses of one recording on the Stopwatch clock. What is recorded runs on "recording time": the clock with every
    // pause cut out. Frames are cut at recording-time instants mapped back to the clock, and each sound sample lands at
    // the recording time of its moment, so video and sound skip exactly the same span and stay in sync after any number
    // of pauses. Readers take an immutable snapshot (View); Pause, Resume and Now share one lock, so a reader that saw
    // no pause at `now` can never meet one that started before it.
    internal sealed class PauseTimeline
    {
        readonly object sync = new object();
        readonly Func<long> clock;
        volatile View view = View.None;
        double quantum;

        public PauseTimeline() : this(null) { }

        // clock: Stopwatch ticks (tests give their own).
        internal PauseTimeline(Func<long> clock)
        {
            this.clock = clock ?? new Func<long>(Stopwatch.GetTimestamp);
        }

        public View Current { get { return view; } }
        public bool Paused { get { return view.Open; } }
        public int Count { get { return view.Count; } }

        // Ticks a pause's length is rounded up to: the monitor's refresh period when frames follow it, so every cut after
        // a pause keeps its place in the refresh cycle. 0: exact.
        public double Quantum
        {
            get { lock (sync) return quantum; }
            set { lock (sync) quantum = Math.Max(0, value); }
        }

        // The clock now, and the pauses as they are at that instant.
        public long Now(out View v)
        {
            lock (sync)
            {
                v = view;
                return clock();
            }
        }

        // The same in 100 ns units (the sound's timeline).
        public long Now100(out View v)
        {
            return To100(Now(out v));
        }

        internal static long To100(long ticks)
        {
            return (long)(ticks * (10000000.0 / Stopwatch.Frequency));
        }

        public bool Pause()
        {
            lock (sync)
            {
                if (view.Open) return false;
                view = view.Opened(clock());
                return true;
            }
        }

        public bool Resume()
        {
            lock (sync)
            {
                if (!view.Open) return false;
                long length = Math.Max(0, clock() - view.OpenStart);
                if (quantum > 0) length = (long)Math.Round(Math.Ceiling(length / quantum) * quantum);
                view = view.Closed(length);
                return true;
            }
        }

        internal sealed class View
        {
            public static readonly View None = new View(new long[0], new long[0]);
            readonly long[] start, length;       // Stopwatch ticks; the last length is -1 while that pause lasts
            long[] start100;
            volatile long[] end100;              // set last: both sound threads may make them at once

            View(long[] start, long[] length)
            {
                this.start = start;
                this.length = length;
            }

            public int Count { get { return start.Length; } }
            public bool Open { get { return length.Length > 0 && length[length.Length - 1] < 0; } }
            public long OpenStart { get { return Open ? start[start.Length - 1] : long.MaxValue; } }
            public long Start(int i) { return start[i]; }
            public long Length(int i) { return length[i]; }

            // Ticks cut out by the pauses that have ended.
            public long Removed
            {
                get
                {
                    long r = 0;
                    foreach (long l in length) if (l > 0) r += l;
                    return r;
                }
            }

            internal View Opened(long at)
            {
                long[] s = new long[start.Length + 1], l = new long[length.Length + 1];
                Array.Copy(start, s, start.Length);
                Array.Copy(length, l, length.Length);
                s[s.Length - 1] = Math.Max(at, start.Length > 0 ? start[start.Length - 1] + length[length.Length - 1] : at);
                l[l.Length - 1] = -1;
                return new View(s, l);
            }

            internal View Closed(long len)
            {
                long[] l = (long[])length.Clone();
                l[l.Length - 1] = len;
                return new View(start, l);
            }

            // The clock instant of a recording-time instant; +infinity once it lies past the start of the pause going on.
            // The instant a pause starts belongs before it.
            public double QpcOf(double rt)
            {
                double removed = 0;
                for (int i = 0; i < start.Length; i++)
                {
                    if (rt <= start[i] - removed) break;
                    if (length[i] < 0) return double.PositiveInfinity;
                    removed += length[i];
                }
                return rt + removed;
            }

            // The last loop of a recording stopped during a pause: frames past its start show the screen as it was then.
            public double Clamp(double qpc)
            {
                return double.IsPositiveInfinity(qpc) ? OpenStart : qpc;
            }

            public long RtOf(long qpc)
            {
                bool paused;
                int segment;
                return RtOf(qpc, out paused, out segment);
            }

            // The recording time of a clock instant. paused: it lies in a pause (the time is then where that pause starts).
            // segment: pauses started before it, so what happened before a pause is never shown after it.
            public long RtOf(long qpc, out bool paused, out int segment)
            {
                long removed = 0;
                for (int i = 0; i < start.Length; i++)
                {
                    if (qpc < start[i])
                    {
                        paused = false;
                        segment = i;
                        return qpc - removed;
                    }
                    if (length[i] < 0 || qpc < start[i] + length[i])
                    {
                        paused = true;
                        segment = i;
                        return start[i] - removed;
                    }
                    removed += length[i];
                }
                paused = false;
                segment = start.Length;
                return qpc - removed;
            }

            // The pauses in 100 ns units, as the sound's timeline counts (end: long.MaxValue while it lasts).
            public long[] Start100
            {
                get { Make100(); return start100; }
            }

            public long[] End100
            {
                get { Make100(); return end100; }
            }

            void Make100()
            {
                if (end100 != null) return;
                long[] s = new long[start.Length], e = new long[start.Length];
                for (int i = 0; i < s.Length; i++)
                {
                    s[i] = To100(start[i]);
                    e[i] = length[i] < 0 ? long.MaxValue : To100(start[i] + length[i]);
                }
                start100 = s;
                end100 = e;
            }
        }
    }

    public static partial class Recorder
    {
        // A recording is running (not preparing, not saving) and can be paused or resumed.
        public static bool CanPause
        {
            get { Session s = current; return s != null && s.CanPause; }
        }

        public static bool Paused
        {
            get { Session s = current; return s != null && s.CanPause && s.Paused; }
        }

        // Pausar / Reanudar from the bar, the tray or the command line.
        public static void TogglePause()
        {
            Session s = current;
            if (s != null) s.TogglePause();
        }
    }

    partial class Session
    {
        PauseTimeline pauses = new PauseTimeline();
        PauseTimeline.View pv = PauseTimeline.View.None;   // the capture thread's snapshot, taken with `now`
        volatile bool paused;

        public bool Paused { get { return paused; } }

        internal bool CanPause
        {
            get { return writer != null && !stopping && !halted && !preparing && !asking && !cancelled; }
        }

        // Tests: a timeline on their own clock, and the capture thread's frame times through the real Cut.
        internal PauseTimeline PausesForTest { get { return pauses; } set { pauses = value; } }
        internal long T0ForTest { get { return t0; } set { t0 = value; } }
        internal double CutForTest(long n, double period, double cutPhase, bool locked, PauseTimeline.View v)
        {
            pv = v;
            return Cut(n, period, cutPhase, locked);
        }

        // While paused nothing is written: the capture thread makes no frames (their cuts lie past the pause), the sound
        // drops what it hears, the time on the bar stops and the frame around the area dims. Locking the PC, the disk
        // running out or Stop still end the recording and save it.
        public void TogglePause()
        {
            if (!CanPause) return;
            bool on = !paused;
            if (on ? !pauses.Pause() : !pauses.Resume()) return;
            paused = on;
            if (on) clock.Stop();
            else clock.Start();
            if (bar != null && !bar.IsDisposed) bar.Paused(on);
            if (edges != null) foreach (FrameEdge e in edges) e.Dim(on);
            ShotStack.Log(on ? "Grabaci\u00F3n en pausa" : "Grabaci\u00F3n reanudada (" + pauses.Count + (pauses.Count == 1 ? " pausa)" : " pausas)"));
        }
    }

    sealed partial class AudioCapture
    {
        // 10 ms: how long the sound before a pause fades into the sound after it, so the cut is never heard as a click.
        public const int JoinFrames = 480;
        const long MaxAhead = 20000000;   // 2 s (100 ns): a packet dated further ahead is placed at once
        const int MaxHeld = 400;

        // The recording's pauses (null outside a recording). With them, sound is placed only once its moment has passed:
        // loopback is dated up to the device's delay ahead, and a pause pressed now must still find every later sample
        // unwritten.
        internal PauseTimeline Pauses;
        public long PausedFrames, Joins;

        sealed class Held { public float[] Data; public int Frames; public long When; }
        readonly Queue<Held> held = new Queue<Held>();
        readonly Stack<float[]> spare = new Stack<float[]>();
        readonly float[] tail = new float[JoinFrames * 2];   // the first samples of the pause tailOf (dropped, kept to fade)
        int tailOf = -1, joinedOf = -1;

        // Feed with a pause timeline: the packet in `work` waits until its last sample is in the past.
        void Hold(int frames, long when, long start)
        {
            float[] d = spare.Count > 0 ? spare.Pop() : null;
            if (d == null || d.Length < frames * 2) d = new float[Math.Max(frames, JoinFrames) * 2];
            Array.Copy(work, d, frames * 2);
            Held h = new Held();
            h.Data = d;
            h.Frames = frames;
            h.When = when;
            held.Enqueue(h);
            PauseTimeline.View v;
            long now = Pauses.Now100(out v);
            Commit(v, now, start);
        }

        // Places the packets whose last sample is before horizon (100 ns), cutting out the pauses.
        void Commit(PauseTimeline.View v, long horizon, long start)
        {
            while (held.Count > 0)
            {
                Held h = held.Peek();
                long end = h.When + (long)Math.Round(h.Frames * (10000000.0 / Rate));
                if (end > horizon && end - horizon < MaxAhead && held.Count <= MaxHeld) break;
                held.Dequeue();
                Split(v, h.Data, h.Frames, h.When, start);
                if (spare.Count < 8) spare.Push(h.Data);
            }
        }

        // Finish: whatever still waits goes in now (a pause still going on drops its part).
        void CommitHeld()
        {
            if (Pauses == null || held.Count == 0) return;
            long start = Interlocked.Read(ref t0);
            if (start == long.MinValue || file == null) { held.Clear(); return; }
            Commit(Pauses.Current, long.MaxValue, start);
        }

        // One packet (n frames of d, the first at q): the parts outside the pauses go onto the timeline at their
        // recording time; the first samples of each pause are kept to fade into the sound after it.
        void Split(PauseTimeline.View v, float[] d, int n, long q, long start)
        {
            long[] s100 = v.Start100, e100 = v.End100;
            double step = 10000000.0 / Rate;
            long removed = 0;
            int pos = 0;
            for (int i = 0; i < s100.Length && pos < n; i++)
            {
                long s = s100[i], e = e100[i];
                if (e != long.MaxValue && e <= q + pos * step)
                {
                    removed += e - s;
                    continue;
                }
                int ks = Index(Math.Ceiling((s - q) / step), pos, n);
                if (ks > pos) Run(v, d, pos, ks, q, start, removed, i - 1);
                if (ks >= n) { pos = n; break; }
                int ke = e == long.MaxValue ? n : Index(Math.Ceiling((e - q) / step), ks, n);
                Tail(i, d, ks, ke, q, s);
                pos = ke;
                if (e != long.MaxValue) removed += e - s;
            }
            if (pos < n) Run(v, d, pos, n, q, start, removed, s100.Length - 1);
        }

        // A sample index clamped while still a double: pauses hours away must not overflow the cast.
        static int Index(double v, int lo, int hi)
        {
            return v <= lo ? lo : v >= hi ? hi : (int)v;
        }

        void Run(PauseTimeline.View v, float[] d, int from, int to, long q, long start, long removed, int junction)
        {
            int count = to - from;
            double t = q + from * (10000000.0 / Rate);
            Ensure(count * 2);
            Array.Copy(d, from * 2, work, 0, count * 2);
            if (junction >= 0) Join(v, junction, t, count);
            Place(count, (long)Math.Round(t - removed), start);
        }

        void Tail(int p, float[] d, int from, int to, long q, long s)
        {
            if (to <= from) return;
            PausedFrames += to - from;
            if (tailOf != p)
            {
                Array.Clear(tail, 0, tail.Length);
                tailOf = p;
            }
            double step = 10000000.0 / Rate;
            for (int j = from; j < to; j++)
            {
                long o = (long)Math.Round((q + j * step - s) / step);
                if (o < 0) continue;
                if (o >= JoinFrames) break;
                tail[2 * o] = d[2 * j];
                tail[2 * o + 1] = d[2 * j + 1];
            }
        }

        // The first samples after pause p (count in `work`, the first at t) fade in while the ones the pause cut fade out.
        // The weights add up to one, so the join never peaks above either side.
        void Join(PauseTimeline.View v, int p, double t, int count)
        {
            long s = v.Start100[p], e = v.End100[p];
            double step = 10000000.0 / Rate;
            int len = (int)Math.Min(JoinFrames, Math.Round((e - s) / step));
            if (len < 2) return;
            double after = Math.Round((t - e) / step);   // compared before the cast: a run hours later is far past the fade
            if (after >= len) return;
            int o0 = (int)Math.Max(0, after);
            bool have = tailOf == p;
            for (int j = 0; j < count && o0 + j < len; j++)
            {
                int o = o0 + j;
                float w = (float)(0.5 - 0.5 * Math.Cos(Math.PI * (o + 0.5) / len));
                float a = have ? tail[2 * o] : 0f, b = have ? tail[2 * o + 1] : 0f;
                work[2 * j] = a + (work[2 * j] - a) * w;
                work[2 * j + 1] = b + (work[2 * j + 1] - b) * w;
            }
            if (joinedOf != p)
            {
                joinedOf = p;
                Joins++;
            }
        }
    }
}

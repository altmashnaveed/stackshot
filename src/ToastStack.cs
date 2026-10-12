// Stackshot - The notices on screen: up to three toasts stacked in a corner, each leaving on its own.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Stackshot
{
    // What the stack needs from a toast on screen (the tests use their own).
    public interface IToastView
    {
        Size Measure(float scale);                                      // body size in pixels at that scale
        void Place(Rectangle body, float scale, int enterFrom, bool animate); // the first call shows it; enterFrom: +1 right, -1 left
        void Renew();                                                   // its notice changed (same title and text again)
        void Depart(bool animate);                                      // fades out and closes
        void Announce();                                                // tells screen readers
    }

    // Newest at the bottom, at most three; the same title and text again refreshes the one shown. Each leaves after a few
    // seconds (longer for a warning with a button), never while the mouse is on it. While Windows asks for quiet (full
    // screen, presenting) new ones wait, and appear if that ends within a minute. Nothing runs while none is shown or waiting.
    public sealed class ToastStack
    {
        public const int Max = 3;
        public const double LifeMs = 8000, LongLifeMs = 12000, ResumeMs = 3000, WaitMs = 60000, RecheckMs = 2000;

        public sealed class Entry
        {
            public Notice Notice;
            public IToastView View;
            public double Deadline;     // when it leaves, while the mouse is not on it
            public double Left;         // the time it had left when the mouse came onto it
            public bool Hovered, Placed;
            public Rectangle Slot;
            public float Scale;
            internal Entry(Notice n) { Notice = n; }
        }

        // Replaceable by the tests: the clock (ms), why notices must wait now on a monitor (null: they need not), the monitor
        // they go to (by device name, null for a new choice), what they keep clear of there, the window for one, whether to
        // animate, running something later on the UI thread, and asking for a Tick in so many ms (-1: none needed).
        public Func<double> Clock = delegate { return Anim.Now; };
        public Func<Rectangle, string> Quiet = ToastQuiet.Reason;
        public Func<string, ToastArea> Area;
        public Func<List<Rectangle>> Occupied = delegate { return new List<Rectangle>(); };
        public Func<Entry, IToastView> MakeView;
        public Func<bool> Animate = ToastQuiet.AnimationsOn;
        public Action<Action> Post = delegate(Action a) { a(); };
        public Action<double> Wake;

        readonly List<Entry> shown = new List<Entry>();
        readonly List<Notice> waiting = new List<Notice>();
        ToastArea area;
        bool later;
        System.Windows.Forms.Timer timer;

        public ToastStack() { Wake = WakeTimer; }

        public IList<Entry> Shown { get { return shown.AsReadOnly(); } }
        public IList<Notice> Waiting { get { return waiting.AsReadOnly(); } }
        public ToastArea CurrentArea { get { return area; } }

        public static double LifeOf(Notice n) { return n.Kind == NoticeKind.Warning && n.Action != null ? LongLifeMs : LifeMs; }

        public void Show(Notice n)
        {
            double now = Clock();
            n.At = now;
            Entry same = shown.Find(delegate(Entry e) { return e.Notice.Key == n.Key; });
            if (same != null)
            {
                same.Notice = n;
                Restart(same, now);
                same.View.Renew();
                Arrange(Animate());
                same.View.Announce();
                Schedule();
                return;
            }
            waiting.RemoveAll(delegate(Notice w) { return w.Key == n.Key; });
            ToastArea a = shown.Count > 0 && area != null ? area : PickArea(null);
            if (a == null) return;
            string quiet = Quiet(a.Bounds);
            if (quiet != null) { Wait(n, quiet); return; }
            area = a;
            bool animate = Animate();
            while (shown.Count >= Max) Drop(Oldest(), animate);
            Entry entry = new Entry(n);
            entry.View = MakeView(entry);
            shown.Add(entry);
            Restart(entry, now);
            Arrange(animate);
            entry.View.Announce();
            Schedule();
        }

        // Expiry, and the waiting notices once Windows no longer asks for quiet.
        public void Tick()
        {
            double now = Clock();
            bool gone = false, animate = Animate();
            foreach (Entry e in shown.ToArray())
                if (!e.Hovered && now >= e.Deadline) { Drop(e, animate); gone = true; }
            if (gone) Arrange(animate);
            if (waiting.Count > 0) Recheck(now);
            Schedule();
        }

        // The mouse came onto a toast (its time stops) or left it (it stays a little longer).
        public void Hover(Entry e, bool on)
        {
            if (!shown.Contains(e) || e.Hovered == on) return;
            double now = Clock();
            e.Hovered = on;
            if (on) e.Left = Math.Max(0, e.Deadline - now);
            else e.Deadline = now + Math.Max(e.Left, ResumeMs);
            Schedule();
        }

        // A click: on ×, close only; anywhere else, close and run its action.
        public void Click(Entry e, bool close)
        {
            if (!shown.Contains(e)) return;
            bool animate = Animate();
            Drop(e, animate);
            Arrange(animate);
            Schedule();
            Action act = close ? null : e.Notice.Action;
            if (act == null) return;
            Post(delegate
            {
                try { act(); }
                catch (Exception ex) { ShotStack.Log("Aviso \u00AB" + e.Notice.Title + "\u00BB: " + ex.Message); }
            });
        }

        // Something else moved (the captures, the displays): placed again on the next turn, once.
        public void Later()
        {
            if (shown.Count == 0 || later) return;
            later = true;
            Post(delegate { later = false; Arrange(Animate()); });
        }

        public void CloseAll()
        {
            foreach (Entry e in shown.ToArray()) e.View.Depart(false);
            shown.Clear();
            waiting.Clear();
            if (timer != null) { timer.Dispose(); timer = null; }
        }

        void Restart(Entry e, double now)
        {
            double life = LifeOf(e.Notice);
            if (e.Hovered) e.Left = life;
            else e.Deadline = now + life;
        }

        // The one to make room: the oldest the mouse is not on.
        Entry Oldest()
        {
            foreach (Entry e in shown) if (!e.Hovered) return e;
            return shown[0];
        }

        void Drop(Entry e, bool animate)
        {
            shown.Remove(e);
            e.View.Depart(animate);
            if (shown.Count == 0) area = null;
        }

        ToastArea PickArea(string device) { return Area != null ? Area(device) : null; }

        void Wait(Notice n, string reason)
        {
            waiting.Add(n);
            while (waiting.Count > Max)
            {
                ShotStack.Log("Aviso descartado (hay otros en espera): " + waiting[0].Title);
                waiting.RemoveAt(0);
            }
            ShotStack.Log("Aviso en espera (" + reason + "): " + n.Title);
            Schedule();
        }

        void Recheck(double now)
        {
            foreach (Notice n in waiting.ToArray())
                if (now - n.At > WaitMs)
                {
                    waiting.Remove(n);
                    ShotStack.Log("Aviso descartado (ha pasado m\u00E1s de un minuto): " + n.Title);
                }
            if (waiting.Count == 0) return;
            ToastArea a = shown.Count > 0 && area != null ? area : PickArea(null);
            if (a == null || Quiet(a.Bounds) != null) return;
            List<Notice> back = new List<Notice>(waiting);
            waiting.Clear();
            foreach (Notice n in back) Show(n);
        }

        void Arrange(bool animate)
        {
            if (shown.Count == 0) return;
            ToastArea a = PickArea(area != null ? area.Device : null);
            if (a != null) area = a;
            if (area == null) return;
            Size[] sizes = new Size[shown.Count];
            for (int i = 0; i < shown.Count; i++) sizes[i] = shown[i].View.Measure(area.Scale);
            int margin = (int)Math.Round(16 * area.Scale), gap = (int)Math.Round(10 * area.Scale);
            Rectangle[] slots = ToastPlan.Place(area.Work, area.Top, false, sizes, margin, gap, Occupied());
            int middle = area.Work.Left + area.Work.Width / 2;
            for (int i = 0; i < shown.Count; i++)
            {
                Entry e = shown[i];
                if (e.Placed && e.Slot == slots[i] && e.Scale == area.Scale) continue;
                e.Slot = slots[i];
                e.Scale = area.Scale;
                e.Placed = true;
                e.View.Place(slots[i], area.Scale, slots[i].Left + slots[i].Width / 2 >= middle ? 1 : -1, animate);
            }
        }

        void Schedule()
        {
            double now = Clock(), next = double.MaxValue;
            foreach (Entry e in shown) if (!e.Hovered) next = Math.Min(next, e.Deadline - now);
            if (waiting.Count > 0) next = Math.Min(next, RecheckMs);
            Wake(next == double.MaxValue ? -1 : Math.Max(15, next));
        }

        void WakeTimer(double ms)
        {
            if (ms < 0) { if (timer != null) timer.Stop(); return; }
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Tick += delegate { timer.Stop(); Tick(); };
            }
            timer.Stop();
            timer.Interval = (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(ms)));
            timer.Start();
        }
    }
}

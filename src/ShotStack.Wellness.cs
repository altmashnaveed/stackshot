// Stackshot - Healthy breaks: when to remind, and where (the desktop mascot, or a notice if asked for).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        readonly Wellness wellness = new Wellness();
        Timer wellTimer;
        double callCheckedAt = -1e9;
        volatile bool inCall, callChecking;

        // Every 20 s, a few cheap checks; nothing at all while the breaks are off.
        void StartWellness()
        {
            if (Test) return;
            wellTimer = new Timer { Interval = 20000 };
            wellTimer.Tick += delegate
            {
                try { WellTick(); }
                catch (Exception ex) { Log("Pausas saludables: " + ex.Message); }
            };
            wellTimer.Start();
        }

        static int IdleMs() { return Native.IdleMs(); }

        void WellTick()
        {
            if (exiting || !settings.WellOn) return;
            double now = Anim.Now;
            // Whether a call is on is read off this thread every 30 s, so it is ready when a reminder is due.
            if (now - callCheckedAt > 30000 && !callChecking)
            {
                callCheckedAt = now;
                callChecking = true;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { inCall = Wellness.InCall(); } finally { callChecking = false; } });
            }
            wellness.Tick(now, IdleMs());
            int kind = wellness.Due(now, Wellness.MinutesOf(settings));
            // Nobody at the computer right now (lunch with the screen on): it waits until you're back.
            if (kind < 0 || IdleMs() >= Wellness.ActiveIdleMs) return;
            // With the mascot on the desktop it always asks itself, once it is free (not walking, dragged or in a menu).
            bool toPet = pet != null;
            if (toPet ? !pet.CanRemind : !settings.WellToasts) return;
            if (WellQuiet()) return;
            string text = MascotTalk.Remind(settings, kind, Wellness.MinutesOf(settings)[kind]);
            wellness.Shown(now);
            // Answered or not, it comes back in a while; an answer replaces this.
            wellness.Later(kind, now, Wellness.IgnoredMs);
            if (toPet) pet.Remind(kind, text);
            else
            {
                int k = kind;
                Notify(settings.MascotName + ": " + WellKind.Names[kind].ToLowerInvariant(), text, NoticeKind.Info, "Hecho", delegate
                {
                    string up;
                    WellDone(k, out up);
                    if (up != null) Notify(settings.MascotName, up);
                });
            }
        }

        // Not now: Windows asks for quiet (full screen, presenting, quiet time, locked), an area is being picked, a
        // recording is on, or the microphone or camera is in use (a call).
        bool WellQuiet()
        {
            if (Recorder.Recording) return true;
            if (ToastQuietNow(Screen.PrimaryScreen.Bounds) != null) return true;
            return inCall;
        }

        // A break done: counted for today, the timer starts over and it is one more friendship point (levelUp: what to
        // say if that reached a new level, or null).
        public int WellDone(int kind, out string levelUp)
        {
            wellness.Done(kind, Anim.Now);
            int today = Wellness.Count(settings, kind);
            levelUp = LoveUp();
            return today;
        }

        // Later: after ms, or (ms < 0) not again today.
        public void WellLater(int kind, double ms)
        {
            if (ms < 0) ms = Math.Max(600000, (DateTime.Today.AddDays(1) - DateTime.Now).TotalMilliseconds);
            wellness.Later(kind, Anim.Now, ms);
        }

        // The next "Moverse" break: the back stretch or the legs, one after the other.
        public bool MoveIsStretch { get { return wellness.MoveIsStretch; } }
    }
}

// Stackshot - Healthy breaks: when to remind, and where (the desktop mascot, or a notice if asked for).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        readonly Wellness wellness = new Wellness();
        Timer wellTimer;
        double callCheckedAt = -1e9;
        bool inCall;

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public int cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

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

        static int IdleMs()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return Math.Max(0, unchecked(Environment.TickCount - (int)li.dwTime));
        }

        void WellTick()
        {
            if (exiting || !settings.WellOn) return;
            double now = Anim.Now;
            wellness.Tick(now, IdleMs());
            int kind = wellness.Due(now, Wellness.MinutesOf(settings));
            if (kind < 0) return;
            bool toPet = pet != null && pet.CanRemind;
            if (!toPet && !settings.WellToasts) return;
            if (WellQuiet(now)) return;
            string text = MascotTalk.Remind(settings, kind, Wellness.MinutesOf(settings)[kind]);
            wellness.Shown(now);
            // Answered or not, it comes back in a while; an answer replaces this.
            wellness.Later(kind, now, Wellness.IgnoredMs);
            if (toPet) pet.Remind(kind, text);
            else
            {
                int k = kind;
                Notify(settings.MascotName + ": " + WellKind.Names[kind].ToLowerInvariant(), text, NoticeKind.Info, "Hecho", delegate { WellDone(k); });
            }
        }

        // Not now: Windows asks for quiet (full screen, presenting, quiet time, locked), an area is being picked, a
        // recording is on, or the microphone or camera is in use (a call).
        bool WellQuiet(double now)
        {
            if (Recorder.Recording) return true;
            if (ToastQuietNow(Screen.PrimaryScreen.Bounds) != null) return true;
            if (now - callCheckedAt > 30000) { callCheckedAt = now; inCall = Wellness.InCall(); }
            return inCall;
        }

        // A break done: counted for today, the timer starts over and it is one more friendship point.
        public int WellDone(int kind)
        {
            wellness.Done(kind, Anim.Now);
            int today = Wellness.Count(settings, kind);
            settings.MascotLove++;
            settings.Save();
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

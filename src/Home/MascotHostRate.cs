// Stackshot - How often the mascot's hosts (the desktop pet and the main window) redraw it, and when it naps because
// nobody is there. Pure decisions, so they can be tested without a window.
// MIT License - https://github.com/rubenitx/stackshot
using System;

namespace Stackshot
{
    public static class MascotRate
    {
        // WinForms and WPF timers fire on whole 15.625 ms system ticks whatever timeBeginPeriod says (measured: 33 ms
        // fires every 47, 63 every 75), so every tier is a tick multiple: 31 = 32 fps, 62 = 16 fps, 125 = 8 fps.
        public const int Anim = 16;         // something animates (main window: rides the render loop)
        public const int Look = 31;         // following the mouse, a bubble or the menu; backs up the pet's vsync pacer
        public const int Idle = 62;         // floating and breathing; also a nap (its "z"s drift slowly)
        public const int Background = 125;  // main window in the background with nothing going on for a while
        public const int Hidden = 500;      // nobody can see it (screen off, locked, cloaked): only checks for a change

        public const int AwayMs = 300000;   // no keyboard or mouse for 5 minutes: the user is away
        public const int DeepAwayMs = 600000; // 10 minutes: long gone, whether or not the screen went off
        public const int BackMs = 1500;     // input this recent: the user is back
        public const int NearMs = 600;      // the mouse moved near the mascot this recently: it follows it smoothly
        public const int SettleMs = 15000;  // main window: nothing happening for this long slows its clock down

        // The desktop pet naps while the user is away: only when it may doze off ("Se duerme sola"), and only at rest,
        // never in the middle of a walk, a fall, a drag, a slide, its menu or the way home.
        public static bool AwayNap(int idleMs, bool sleepIdle, bool atRest, bool away)
        {
            return sleepIdle && atRest && !away && idleMs > AwayMs;
        }

        // Long away and napping (or the main window not in front): the picture stays as it is and the clock only checks,
        // twice a second, for the user's return, so a screen that never switches off doesn't run the nap all night.
        public static bool Frozen(int idleMs, bool napping)
        {
            return napping && idleMs > DeepAwayMs;
        }

        // Checked every frame: any input, or the mouse moving near it, wakes it from that nap at once.
        public static bool AwayWake(bool away, int idleMs, bool nearMoved)
        {
            return away && (nearMoved || (idleMs >= 0 && idleMs < BackMs));
        }

        // The desktop pet's timer interval. moving: walking, falling, dragged or sliding (the vsync pacer drives those
        // frames and the timer only backs it up). Asleep it ticks at the idle rate: Mascot.Lively ignores the nap's "z"s.
        public static int Pet(bool screenOff, bool moving, bool menu, bool lively, bool looking)
        {
            if (screenOff) return Hidden;
            if (moving || menu || lively || looking) return Look;
            return Idle;
        }

        // The main window's clock; 0 stops it. quietMs: time since anything last happened (the mouse, an animation).
        public static int Home(bool shown, bool cloaked, bool hasMascot, bool lively, bool entering, bool looking, bool sleeping,
                               bool active, double quietMs)
        {
            if (!shown) return 0;
            if (cloaked) return Hidden;
            // Behind another window an animation runs at half rate: nobody follows it closely.
            if (lively) return entering || !active ? Look : Anim;
            if (!hasMascot) return 0;
            if (looking) return Look;
            bool settled = quietMs > SettleMs;
            if (sleeping) return active && !settled ? Look : Idle;
            if (active) return settled ? Idle : Look;
            return settled ? Background : Idle;
        }

        // Screen state reported by MascotScreen (this session's display power and lock notifications).
        static volatile bool displayOff, locked;
        static int offAt;                   // Environment.TickCount when it stopped being seen
        public const int OffGraceMs = 1000; // input this close to the report is the key that locked it, not a return
        public static bool ScreenOff { get { return displayOff || locked; } }
        public static bool Locked { get { return locked; } }
        public static void SetDisplayOff(bool off) { if (off) MarkOff(); displayOff = off; }
        public static void SetLocked(bool on) { if (on) MarkOff(); locked = on; }
        public static void ScreenSeen() { displayOff = false; locked = false; }
        static void MarkOff() { if (!displayOff && !locked) offAt = Environment.TickCount; }

        // A missed "on" or "unlock" notification must not freeze the mascots: input made after the report means
        // someone is there. lastInput and offAt are GetTickCount values (they wrap).
        public static bool BackFromOff(bool screenOff, int offAt, int lastInput)
        {
            return screenOff && unchecked(lastInput - offAt) > OffGraceMs;
        }

        public static bool InputSinceOff(int lastInput) { return BackFromOff(ScreenOff, offAt, lastInput); }
    }
}

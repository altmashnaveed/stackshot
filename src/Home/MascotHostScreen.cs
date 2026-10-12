// Stackshot - Tells the mascot's hosts (the desktop pet and the main window) when nobody can see them: this session's
// display is off, or the session is locked or disconnected. One hidden window for the whole app, never shown.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stackshot
{
    sealed class MascotScreen : NativeWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public int cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid setting, int flags);
        [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
        [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(int flags, bool inherit, uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desk);

        // GUID_SESSION_DISPLAY_STATUS, not the console one: over Remote Desktop the office monitor going off must not
        // freeze the mascots the remote user is looking at.
        static Guid DisplayStatus = new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
        static MascotScreen watch;

        // Called on the UI thread by each host; the window lives as long as the process.
        public static void Ensure()
        {
            if (watch != null) return;
            watch = new MascotScreen();
            try
            {
                CreateParams cp = new CreateParams();
                cp.Caption = "Stackshot screen";
                cp.Style = unchecked((int)0x80000000);   // WS_POPUP, never made visible
                cp.ExStyle = 0x00000080;                  // WS_EX_TOOLWINDOW
                cp.X = -32000; cp.Y = -32000; cp.Width = 1; cp.Height = 1;
                watch.CreateHandle(cp);
                RegisterPowerSettingNotification(watch.Handle, ref DisplayStatus, 0);   // DEVICE_NOTIFY_WINDOW_HANDLE
                WTSRegisterSessionNotification(watch.Handle, 0);                         // NOTIFY_FOR_THIS_SESSION
            }
            catch (Exception ex) { ShotStack.Log("Mascota: sin avisos de pantalla (" + ex.Message + ")"); }
        }

        public static bool Watching { get { return watch != null && watch.Handle != IntPtr.Zero; } }

        // From the hosts' slow "nobody sees it" check: recovers from a missed notification once the user is back.
        public static void Check()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!MascotRate.ScreenOff || !GetLastInputInfo(ref li) || !MascotRate.InputSinceOff(unchecked((int)li.dwTime))) return;
            // Typing the password on the lock screen is input too: still locked while the input desktop is not ours.
            if (MascotRate.Locked && !InputDesktopOurs()) return;
            MascotRate.ScreenSeen();
        }

        static bool InputDesktopOurs()
        {
            IntPtr d = OpenInputDesktop(0, false, 0x0100);   // DESKTOP_SWITCHDESKTOP: refused while Winlogon's desktop is up
            if (d == IntPtr.Zero) return false;
            CloseDesktop(d);
            return true;
        }

        protected override void WndProc(ref Message msg)
        {
            // PBT_POWERSETTINGCHANGE: POWERBROADCAST_SETTING data at offset 20 is 0 off, 1 on, 2 dimmed.
            if (msg.Msg == 0x0218 && (long)msg.WParam == 0x8013 && msg.LParam != IntPtr.Zero)
            {
                try { MascotRate.SetDisplayOff(Marshal.ReadInt32(msg.LParam, 20) == 0); } catch { }
            }
            // Session locked or disconnected (lock 7, console or remote disconnect 2/4) and back (unlock 8, connect 1/3).
            if (msg.Msg == 0x02B1)
            {
                int ev = (int)(long)msg.WParam;
                if (ev == 7 || ev == 2 || ev == 4) MascotRate.SetLocked(true);
                else if (ev == 8 || ev == 1 || ev == 3) MascotRate.SetLocked(false);
            }
            base.WndProc(ref msg);
        }
    }
}

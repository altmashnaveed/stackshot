// Stackshot - Hidden window that handles close requests from Windows.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stackshot
{
    // Stackshot has no visible top-level window. Windows Installer, the Restart Manager and sign-out send WM_CLOSE /
    // WM_QUERYENDSESSION / WM_ENDSESSION to top-level windows; this one receives them. While a recording is saving or
    // something is unsaved, Windows is asked to wait (with the reason on its sign-out screen) and the app settles it.
    public sealed class CloseListener : NativeWindow, IDisposable
    {
        readonly Func<string> blockReason;
        readonly Action askQuit, ending;
        readonly Timer release = new Timer();
        bool blocked, ended;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);
        [DllImport("user32.dll")] static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);

        // blockReason: why Windows should wait, or null. askQuit: settle it and close (asking what to do with unsaved
        // work). ending: the session ends now, no questions.
        public CloseListener(Func<string> blockReason, Action askQuit, Action ending)
        {
            this.blockReason = blockReason;
            this.askQuit = askQuit;
            this.ending = ending;
            CreateParams cp = new CreateParams();
            cp.Caption = "Stackshot";
            cp.ClassName = null;
            cp.ExStyle = 0x80; // WS_EX_TOOLWINDOW: no Alt+Tab or taskbar entry
            cp.Style = 0;      // not WS_VISIBLE
            CreateHandle(cp);
            // The reason stays on Windows' screen only while it is true.
            release.Interval = 1000;
            release.Tick += delegate { if (!blocked || Reason() == null) Unblock(); };
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0010: // WM_CLOSE
                    ShotStack.Log("Cierre pedido por Windows");
                    askQuit();
                    return;
                case 0x0011: // WM_QUERYENDSESSION
                    m.Result = QueryEnd() ? (IntPtr)1 : IntPtr.Zero;
                    return;
                case 0x0016: // WM_ENDSESSION
                    if (m.WParam != IntPtr.Zero && !ended)
                    {
                        ended = true;
                        ShotStack.Log("Windows cierra la sesi\u00F3n");
                        // Synchronous: once this returns Windows may end the process.
                        ending();
                    }
                    else if (m.WParam == IntPtr.Zero) Unblock(); // the sign-out was cancelled
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        string Reason()
        {
            try { return blockReason(); }
            catch (Exception ex) { ShotStack.Log("Cierre de sesi\u00F3n: " + ex.Message); return null; }
        }

        // True when nothing would be lost. Otherwise Windows shows the reason and waits; meanwhile the recording is
        // saved and the questions are asked, and Stackshot closes itself once that is done.
        bool QueryEnd()
        {
            string why = Reason();
            if (why == null) return true;
            ShotStack.Log("Windows quiere cerrar la sesi\u00F3n: se le pide esperar (" + why + ")");
            try { blocked = ShutdownBlockReasonCreate(Handle, why); }
            catch (Exception ex) { ShotStack.Log("Motivo de espera: " + ex.Message); }
            release.Start();
            askQuit();
            return false;
        }

        void Unblock()
        {
            release.Stop();
            if (!blocked) return;
            blocked = false;
            try { ShutdownBlockReasonDestroy(Handle); } catch { }
        }

        public void Dispose()
        {
            release.Stop();
            release.Dispose();
            if (blocked) { try { ShutdownBlockReasonDestroy(Handle); } catch { } blocked = false; }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}

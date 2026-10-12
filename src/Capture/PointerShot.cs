// Stackshot - The mouse pointer in screenshots: its shape and place when the capture started, drawn into the result.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Stackshot
{
    // The shape is copied at once: an area capture shows a crosshair while the area is chosen, and an app may drop its own
    // pointer meanwhile. Drawing it later touches only the patch under it, which goes through a small DIB where GDI draws
    // it exactly as Windows does on screen (alpha, the inverting I-beam...), as the recorder does with its frames.
    public sealed class PointerShot : IDisposable
    {
        IntPtr shape;                       // our own copy, destroyed with this
        public readonly Point Position;     // the hot spot on the screen, in physical pixels
        public readonly Point Hotspot;      // the hot spot inside the shape, at the size it is drawn
        public readonly Size Size;          // the size it is drawn at (see SizeFor)

        PointerShot(IntPtr shape, Point position, Point hotspot, Size size)
        {
            this.shape = shape;
            Position = position;
            Hotspot = hotspot;
            Size = size;
        }

        // The pointer as it is now, or null when it is hidden (typing, a touch screen) or can't be read.
        public static PointerShot Take()
        {
            Native.CURSORINFO ci = Grabber.CursorNow();
            if ((ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return null; // CURSOR_SHOWING
            Point at = new Point(ci.ptScreenPos.X, ci.ptScreenPos.Y);
            int monitorDpi, systemDpi;
            Dpis(at, out monitorDpi, out systemDpi);
            return From(ci.hCursor, at, monitorDpi, systemDpi, BaseSize());
        }

        // A copy of a pointer shape whose hot spot is at screen point `at`; the DPIs and the pointer size chosen in Windows
        // decide the size it is drawn at. Null if the shape can't be read or copied.
        internal static PointerShot From(IntPtr cursor, Point at, int monitorDpi, int systemDpi, int baseSize)
        {
            int w, h, hx, hy;
            if (cursor == IntPtr.Zero || !Measure(cursor, out w, out h, out hx, out hy)) return null;
            int tw = SizeFor(w, monitorDpi, systemDpi, baseSize);
            int th = tw == w ? h : Math.Max(1, (int)Math.Round(h * (double)tw / w));
            Point hot = tw == w ? new Point(hx, hy) : new Point((int)Math.Round(hx * (double)tw / w), (int)Math.Round(hy * (double)th / h));
            IntPtr copy = IntPtr.Zero;
            if (tw != w)
            {
                // A crisp copy from the pointer's own file at that size when it has one; otherwise it is stretched as drawn.
                copy = CopyImage(cursor, 2, tw, th, 0x4000); // IMAGE_CURSOR, LR_COPYFROMRESOURCE
                int cw, ch, cx, cy;
                if (copy != IntPtr.Zero && (!Measure(copy, out cw, out ch, out cx, out cy) || cw != tw || ch != th))
                {
                    Native.DestroyIcon(copy);
                    copy = IntPtr.Zero;
                }
            }
            if (copy == IntPtr.Zero) copy = CopyIcon(cursor);
            if (copy == IntPtr.Zero) return null;
            return new PointerShot(copy, at, hot, new Size(tw, th));
        }

        // The size a shape is drawn at. Windows may hand out a shape at its size for the system DPI while showing it on the
        // pointer's monitor at that monitor's size: a shape of the nominal size for the system DPI (32 px at 100 %, or the
        // pointer size chosen in Accessibility) is then drawn at the nominal size for the monitor. Any other size (already
        // the monitor's, an app's own pointer) is drawn as it is.
        internal static int SizeFor(int shape, int monitorDpi, int systemDpi, int baseSize)
        {
            if (shape <= 0 || monitorDpi <= 0 || systemDpi <= 0 || monitorDpi == systemDpi) return shape;
            int[] nominals = baseSize > 0 && baseSize != 32 ? new int[] { baseSize, 32 } : new int[] { 32 };
            // Already the monitor's size for any of them wins over looking like the system's size for another.
            foreach (int n in nominals) if (shape == Scaled(n, monitorDpi)) return shape;
            foreach (int n in nominals) if (shape == Scaled(n, systemDpi)) return Scaled(n, monitorDpi);
            return shape;
        }

        static int Scaled(int px, int dpi) { return (int)Math.Round(px * dpi / 96.0); }

        // Draws the pointer into image, whose top-left pixel is at screen point origin, when its hot spot falls inside the
        // image; what sticks out is left out. The pixels it covers stay opaque. True when it was drawn.
        public bool DrawInto(Bitmap image, Point origin)
        {
            if (shape == IntPtr.Zero || image == null) return false;
            if (!new Rectangle(origin, image.Size).Contains(Position)) return false;
            Rectangle at = new Rectangle(Position.X - Hotspot.X - origin.X, Position.Y - Hotspot.Y - origin.Y, Size.Width, Size.Height);
            Rectangle r = Rectangle.Intersect(at, new Rectangle(Point.Empty, image.Size));
            if (r.Width <= 0 || r.Height <= 0) return false;
            int ox = r.X - at.X, oy = r.Y - at.Y;
            using (Dib patch = new Dib(Size.Width, Size.Height))
            {
                BitmapData bd = image.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < r.Height; y++)
                        Native.CopyMemory(Row(patch, ox, oy + y), new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), new UIntPtr((uint)(r.Width * 4)));
                    Native.DrawIconEx(patch.Dc, 0, 0, shape, Size.Width, Size.Height, 0, IntPtr.Zero, 3); // DI_NORMAL
                    Native.GdiFlush();
                    // GDI leaves the alpha byte undefined where it draws: a capture stays opaque under the pointer.
                    int[] line = new int[r.Width];
                    for (int y = 0; y < r.Height; y++)
                    {
                        Marshal.Copy(Row(patch, ox, oy + y), line, 0, r.Width);
                        for (int x = 0; x < line.Length; x++) line[x] |= unchecked((int)0xFF000000);
                        Marshal.Copy(line, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), r.Width);
                    }
                }
                finally { image.UnlockBits(bd); }
            }
            return true;
        }

        static IntPtr Row(Dib d, int x, int y) { return new IntPtr(d.Bits.ToInt64() + ((long)y * d.Width + x) * 4); }

        // Size and hot spot of a shape.
        static bool Measure(IntPtr cursor, out int w, out int h, out int hx, out int hy)
        {
            w = h = hx = hy = 0;
            Native.ICONINFO ii;
            if (!Native.GetIconInfo(cursor, out ii)) return false;
            try
            {
                Native.BITMAP bm;
                IntPtr src = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
                if (src == IntPtr.Zero || Native.GetObject(src, Marshal.SizeOf(typeof(Native.BITMAP)), out bm) == 0) return false;
                w = bm.bmWidth;
                h = ii.hbmColor != IntPtr.Zero ? bm.bmHeight : bm.bmHeight / 2; // a monochrome mask holds AND and XOR halves
                hx = ii.xHotspot;
                hy = ii.yHotspot;
                return w > 0 && h > 0 && w <= 1024 && h <= 1024;
            }
            finally
            {
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
            }
        }

        // The DPI of the pointer's monitor and the system's (the one shapes may come at); equal when unknown.
        static void Dpis(Point at, out int monitorDpi, out int systemDpi)
        {
            monitorDpi = systemDpi = 96;
            try
            {
                Native.POINT pt;
                pt.X = at.X;
                pt.Y = at.Y;
                uint dx, dy;
                if (Native.GetDpiForMonitor(Native.MonitorFromPoint(pt, 2), 0, out dx, out dy) == 0 && dx > 0) monitorDpi = (int)dx;
                systemDpi = monitorDpi;
                uint sys = GetDpiForSystem();
                if (sys > 0) systemDpi = (int)sys;
            }
            catch (Exception) { systemDpi = monitorDpi; } // GetDpiForSystem needs Windows 10 1607
        }

        // The pointer size chosen in Windows (Accessibility > Mouse pointer), 32 by default.
        static int BaseSize()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
                {
                    object v = k == null ? null : k.GetValue("CursorBaseSize");
                    if (v is int && (int)v >= 16 && (int)v <= 512) return (int)v;
                }
            }
            catch (Exception) { }
            return 32;
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        // A copy nobody disposed (an unexpected exception on the way) is still given back to Windows.
        ~PointerShot() { Release(); }

        void Release()
        {
            IntPtr h = shape;
            shape = IntPtr.Zero;
            if (h != IntPtr.Zero) Native.DestroyIcon(h);
        }

        [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] static extern IntPtr CopyImage(IntPtr h, uint type, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    }
}

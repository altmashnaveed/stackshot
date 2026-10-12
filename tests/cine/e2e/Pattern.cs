// Known test pattern for real-capture checks: one image per DWM composition, frame id in black/white blocks.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Stackshot.E2E
{
    class PatternWindow : Form
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int SetDIBitsToDevice(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int start, int lines, byte[] bits, ref BITMAPINFOHEADER bmi, int usage);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

        public readonly int W, H;
        volatile bool running = true;
        Thread thread;
        public long ShownCount;

        public PatternWindow(Rectangle r)
        {
            W = r.Width; H = r.Height;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            TopMost = true;
            BackColor = Color.Black;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8; return cp; }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void Begin()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
        }

        public void End() { running = false; if (thread != null) thread.Join(2000); }

        void Loop()
        {
            byte[] buf = new byte[W * H * 4];
            BITMAPINFOHEADER bmi = new BITMAPINFOHEADER();
            bmi.biSize = 40; bmi.biWidth = W; bmi.biHeight = -H; bmi.biPlanes = 1; bmi.biBitCount = 32;
            IntPtr dc = GetDC(Handle);
            try
            {
                for (int k = 0; running; k++)
                {
                    Pattern.Draw(k, W, H, buf);
                    SetDIBitsToDevice(dc, 0, 0, W, H, 0, 0, 0, H, buf, ref bmi, 0);
                    ShownCount = k + 1;
                    DwmFlush(); // one image per composition
                }
            }
            finally { ReleaseDC(Handle, dc); }
        }
    }

    static class Pattern
    {
        public const int Bits = 16, Block = 16;
        static byte[] baseImg;
        static int bw, bh;

        // A fixed screen-like background: saturated "syntax" text blocks, 1-px lines, full-range gradients.
        static void Base(int w, int h)
        {
            if (baseImg != null && bw == w && bh == h) return;
            bw = w; bh = h;
            baseImg = new byte[w * h * 4];
            Random rnd = new Random(1234);
            int[][] syntax = { new[] { 86, 156, 214 }, new[] { 206, 145, 120 }, new[] { 106, 153, 85 }, new[] { 197, 134, 192 }, new[] { 220, 220, 170 }, new[] { 212, 212, 212 } };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte b = 30, g = 30, r = 30;
                    if (y >= 40 && y < 104) { r = (byte)(x * 255 / Math.Max(1, w - 1)); g = (byte)((y - 40) * 4); b = (byte)(255 - r); }
                    else if (y >= 104 && y < 120) { bool on = (x + y) % 2 == 0; r = g = b = (byte)(on ? 255 : 0); }
                    else if (y >= 120 && y < 136) { int c = x % 3; r = (byte)(c == 0 ? 255 : 0); g = (byte)(c == 1 ? 255 : 0); b = (byte)(c == 2 ? 255 : 0); }
                    else if (y % 14 < 10 && y >= 140)
                    {
                        int word = (x / 7 + (y / 14) * 13) % 11;
                        if (word < 7 && x % 7 < 5)
                        {
                            int[] c = syntax[(x / 50 + y / 14) % syntax.Length];
                            r = (byte)c[0]; g = (byte)c[1]; b = (byte)c[2];
                        }
                    }
                    baseImg[i] = b; baseImg[i + 1] = g; baseImg[i + 2] = r; baseImg[i + 3] = 0;
                }
        }

        public static void Draw(int k, int w, int h, byte[] buf)
        {
            Base(w, h);
            Buffer.BlockCopy(baseImg, 0, buf, 0, buf.Length);
            // id blocks along the top
            for (int bit = 0; bit < Bits; bit++)
            {
                byte v = (byte)(((k >> bit) & 1) != 0 ? 255 : 0);
                for (int y = 4; y < 4 + Block; y++)
                    for (int x = 4 + bit * (Block + 4); x < 4 + bit * (Block + 4) + Block; x++)
                    {
                        int i = (y * w + x) * 4;
                        buf[i] = v; buf[i + 1] = v; buf[i + 2] = v;
                    }
            }
            // a moving block and a band that scrolls one pixel per image
            int mx = (k * 6) % Math.Max(1, w - 48), my = Math.Min(h - 48, 150);
            for (int y = my; y < my + 40; y++)
                for (int x = mx; x < mx + 40; x++)
                {
                    int i = (y * w + x) * 4;
                    buf[i] = (byte)(k * 3); buf[i + 1] = (byte)(255 - k); buf[i + 2] = (byte)(k * 7);
                }
            int band = Math.Min(h - 20, 220);
            for (int y = band; y < band + 16; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4, s = (x + k) % 64;
                    buf[i] = (byte)(s * 4); buf[i + 1] = (byte)(s < 32 ? 255 : 0); buf[i + 2] = (byte)(255 - s * 4);
                }
        }

        static int ReadLossy(byte[] b, byte[] g, byte[] r, int w)
        {
            int id = 0;
            for (int bit = 0; bit < Bits; bit++)
            {
                int x = 4 + bit * (Block + 4) + Block / 2, y = 4 + Block / 2, i = y * w + x, v = (r[i] + g[i] + b[i]) / 3;
                if (v > 200) id |= 1 << bit; else if (v > 55) return -1;
            }
            return id;
        }

        // id from a captured B,G,R image; -1 when a block is not cleanly black or white (a transition).
        public static bool Lossy;
        public static int Read(byte[] b, byte[] g, byte[] r, int w)
        {
            if (Lossy) return ReadLossy(b, g, r, w);
            int id = 0;
            for (int bit = 0; bit < Bits; bit++)
            {
                int x = 4 + bit * (Block + 4) + Block / 2, y = 4 + Block / 2, i = y * w + x;
                if (r[i] == 255 && g[i] == 255 && b[i] == 255) id |= 1 << bit;
                else if (r[i] != 0 || g[i] != 0 || b[i] != 0) return -1;
            }
            return id;
        }
    }

    // Same pattern through UpdateLayeredWindow: each image replaces the window content atomically (no tearing).
    class LayeredPattern : Form
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BMI bmi, uint usage, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Point pt, ref Size sz, IntPtr src, ref Point sp, int key, ref BLEND b, int flags);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        [StructLayout(LayoutKind.Sequential)] struct BMI { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, x, y, c, i; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

        public readonly int W, H;
        readonly Rectangle rect;
        volatile bool running = true;
        Thread thread;

        public LayeredPattern(Rectangle r)
        {
            W = r.Width; H = r.Height; rect = r;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x80000; return cp; }
        }

        public void Begin()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();
        }

        public void End() { running = false; if (thread != null) thread.Join(2000); }

        void Loop()
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
            BMI bmi = new BMI();
            bmi.biSize = 40; bmi.biWidth = W; bmi.biHeight = -H; bmi.biPlanes = 1; bmi.biBitCount = 32;
            SelectObject(mem, CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0));
            byte[] buf = new byte[W * H * 4];
            BLEND bl = new BLEND();
            bl.Alpha = 255; bl.Format = 1;
            Point pt = rect.Location, sp = Point.Empty;
            Size sz = rect.Size;
            for (int k = 0; running; k++)
            {
                Pattern.Draw(k, W, H, buf);
                for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
                Marshal.Copy(buf, 0, bits, buf.Length);
                UpdateLayeredWindow(Handle, screen, ref pt, ref sz, mem, ref sp, 0, ref bl, 2);
                DwmFlush();
            }
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

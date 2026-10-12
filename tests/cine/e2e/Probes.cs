// Probes used to find where frames went out of order: ScreenSource as Session drives it, a minimal DXGI loop that
// shares nothing with Stackshot, and GDI. Each reads the pattern's frame id from every captured image and counts
// images older than the previous one. Each class is its own entry point (/main).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.E2E
{
    static class Probe
    {
        public static Rectangle Area()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(wa.Right - 680, wa.Bottom - 450, 640, 360);
        }

        public static LayeredPattern Show(Rectangle area)
        {
            LayeredPattern win = new LayeredPattern(area);
            win.Show();
            win.Begin();
            Application.DoEvents();
            Thread.Sleep(500);
            return win;
        }

        // Frame id of a BGRX image, -1 if unreadable.
        public static int Id(byte[] raw, int w, int h, byte[] b, byte[] g, byte[] r)
        {
            int n = w * h;
            for (int i = 0; i < n; i++) { b[i] = raw[i * 4]; g[i] = raw[i * 4 + 1]; r[i] = raw[i * 4 + 2]; }
            return Pattern.Read(b, g, r, w);
        }

        public static bool Backwards(int prev, int id) { return id >= 0 && prev >= 0 && ((id - prev) & 0xFFFF) >= 0x8000; }
    }

    // ScreenSource with an acquire thread (Wait/Take) like Session. "newest" reads the latest image each time; otherwise
    // the image on screen 10 ms ago, as a video frame would.
    static class OrderProbe
    {
        [STAThread]
        static int Main(string[] args)
        {
            Stackshot.Program.Init();
            ShotStack.Test = true;
            bool newest = args.Length > 0 && args[0] == "newest";
            Rectangle area = Probe.Area();
            LayeredPattern win = Probe.Show(area);
            ScreenSource s = ScreenSource.Open(area, false, false);
            bool stop = false;
            Thread t = new Thread(delegate() { long p; while (!stop) if (s.Wait(16, out p)) s.Take(); });
            t.Priority = ThreadPriority.Highest;
            t.Start();
            int w = area.Width, h = area.Height, n = w * h;
            IntPtr buf = Marshal.AllocHGlobal(n * 4);
            byte[] raw = new byte[n * 4], b = new byte[n], g = new byte[n], r = new byte[n];
            int prev = -1, back = 0, frames = 0, failed = 0;
            List<string> log = new List<string>();
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000)
            {
                double at = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 0.010;
                int v = newest ? s.Pick(double.MaxValue) : s.Pick(at);
                s.Submit(new Native.CURSORINFO(), v);
                if (!s.Read(buf)) { failed++; continue; }
                Marshal.Copy(buf, raw, 0, raw.Length);
                int id = Probe.Id(raw, w, h, b, g, r);
                frames++;
                if (Probe.Backwards(prev, id)) { back++; if (log.Count < 6) log.Add("version " + v + ": id " + prev + " -> " + id); }
                if (id >= 0) prev = id;
                Application.DoEvents();
                Thread.Sleep(16);
            }
            stop = true;
            t.Join();
            win.End();
            Console.WriteLine("ScreenSource " + (newest ? "newest" : "pick(now-10ms)") + ": frames " + frames + ", backwards " + back + ", read failures " + failed +
                              ", misses " + s.Misses + ", unused " + s.Unused);
            foreach (string l in log) Console.WriteLine("  " + l);
            s.Dispose();
            Marshal.FreeHGlobal(buf);
            return back;
        }
    }

    // Acquire, copy, read and release in sequence on one thread: what DXGI itself delivers.
    static class RawDxgiProbe
    {
        [STAThread]
        static int Main(string[] args)
        {
            Stackshot.Program.Init();
            ShotStack.Test = true;
            Rectangle area = Probe.Area();
            LayeredPattern win = Probe.Show(area);
            IntPtr factory, ad, output, device, context, dupl, staging;
            int level;
            Guid fg = Dx.Factory1;
            Dx.Check(Dx.CreateDXGIFactory1(ref fg, out factory), "CreateDXGIFactory1");
            Dx.Fn<Dx.EnumFn>(factory, 12)(factory, 0, out ad);
            Dx.Fn<Dx.EnumFn>(ad, 7)(ad, 0, out output);
            Dx.OutputDesc d;
            Dx.Fn<Dx.OutputDescFn>(output, 7)(output, out d);
            IntPtr out1 = Dx.Query(output, Dx.Output1);
            Dx.Check(Dx.D3D11CreateDevice(ad, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context), "D3D11CreateDevice");
            Dx.Check(Dx.Fn<Dx.DuplicateFn>(out1, 22)(out1, device, out dupl), "DuplicateOutput");
            Dx.TexDesc td = new Dx.TexDesc();
            td.Width = (uint)area.Width; td.Height = (uint)area.Height; td.MipLevels = 1; td.ArraySize = 1; td.Format = Dx.Bgra;
            td.SampleCount = 1; td.Usage = 3; td.CpuAccess = 0x20000;
            Dx.Check(Dx.Fn<Dx.CreateTexFn>(device, 5)(device, ref td, IntPtr.Zero, out staging), "CreateTexture2D");
            int w = area.Width, h = area.Height, n = w * h;
            byte[] raw = new byte[n * 4], b = new byte[n], g = new byte[n], r = new byte[n];
            int prev = -1, back = 0, frames = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8000)
            {
                Dx.FrameInfo info;
                IntPtr res;
                if (Dx.Fn<Dx.AcquireFn>(dupl, 8)(dupl, 50, out info, out res) < 0) continue;
                if (info.LastPresentTime != 0 && info.AccumulatedFrames > 0)
                {
                    IntPtr tex = Dx.Query(res, Dx.Texture2D);
                    Dx.Box box = new Dx.Box();
                    box.Left = (uint)(area.X - d.Coords.Left); box.Top = (uint)(area.Y - d.Coords.Top);
                    box.Right = box.Left + (uint)w; box.Bottom = box.Top + (uint)h; box.Back = 1;
                    Dx.Fn<Dx.CopyRegionFn>(context, 46)(context, staging, 0, 0, 0, 0, tex, 0, ref box);
                    Dx.Mapped m;
                    Dx.Fn<Dx.MapFn>(context, 14)(context, staging, 0, 1, 0, out m);
                    for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(m.Data.ToInt64() + (long)y * m.RowPitch), raw, y * w * 4, w * 4);
                    Dx.Fn<Dx.UnmapFn>(context, 15)(context, staging, 0);
                    Dx.Release(ref tex);
                    int id = Probe.Id(raw, w, h, b, g, r);
                    frames++;
                    if (Probe.Backwards(prev, id)) back++;
                    if (id >= 0) prev = id;
                }
                Dx.Release(ref res);
                Dx.Fn<Dx.CallFn>(dupl, 14)(dupl);
                Application.DoEvents();
            }
            win.End();
            Console.WriteLine("raw DXGI: frames " + frames + ", backwards " + back);
            return back;
        }
    }

    // BitBlt of the composed desktop.
    static class GdiProbe
    {
        [STAThread]
        static int Main(string[] args)
        {
            Stackshot.Program.Init();
            ShotStack.Test = true;
            Rectangle area = Probe.Area();
            LayeredPattern win = Probe.Show(area);
            int w = area.Width, h = area.Height, n = w * h;
            Dib dib = new Dib(w, h);
            byte[] raw = new byte[n * 4], b = new byte[n], g = new byte[n], r = new byte[n];
            int prev = -1, back = 0, frames = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000)
            {
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                Native.BitBlt(dib.Dc, 0, 0, w, h, screen, area.X, area.Y, Native.SRCCOPY | Native.CAPTUREBLT);
                Native.ReleaseDC(IntPtr.Zero, screen);
                Native.GdiFlush();
                Marshal.Copy(dib.Bits, raw, 0, raw.Length);
                int id = Probe.Id(raw, w, h, b, g, r);
                frames++;
                if (Probe.Backwards(prev, id)) back++;
                if (id >= 0) prev = id;
                Application.DoEvents();
                Thread.Sleep(3);
            }
            win.End();
            Console.WriteLine("GDI: samples " + frames + ", backwards " + back);
            return back;
        }
    }
}

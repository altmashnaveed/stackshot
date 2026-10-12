// Real capture: windows with a frame-id pattern are captured with ScreenSource and recorded through CineSink.
// Named explicitly only (realcapture). Never captures anything outside the window rectangle.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    // Borderless, topmost, never activated; paints the bitmap it is given, nothing else.
    sealed class CapturePattern : Form
    {
        public Bitmap Canvas;
        public bool Layered;   // presented with UpdateLayeredWindow (whole frames) instead of WM_PAINT

        public CapturePattern(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Bounds = bounds;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                if (Layered) cp.ExStyle |= 0x00080000;                 // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Canvas != null) e.Graphics.DrawImageUnscaled(Canvas, 0, 0);
        }
    }

    static partial class CineTests
    {
        const int CapW = 640, CapH = 360;
        const int BlockTop = 8, BlockBottom = CapH - 20;   // the id blocks, at the top and at the bottom
        static volatile bool capturing;
        static readonly int[] BlockOne = { 20, 240, 250 };   // B, G, R
        static readonly int[] BlockZero = { 200, 40, 10 };

        // Frame n of the pattern, bgr0 layout (A = 255). Frame id n is 16 blocks (bit i = block i) at the top and
        // at the bottom, drawn last, so a tear between the two copies shows as a disagreement.
        static byte[] RealPattern(int n)
        {
            byte[] b = new byte[CapW * CapH * 4];
            for (int y = 0; y < CapH; y++)
                for (int x = 0; x < CapW; x++)
                {
                    int i = (y * CapW + x) * 4;
                    b[i] = (byte)((x + y + n) & 255);
                    b[i + 1] = (byte)(y & 255);
                    b[i + 2] = (byte)(x & 255);
                    b[i + 3] = 255;
                }
            int hy = (3 * n + 100) % CapH;
            for (int x = 0; x < CapW; x++) SetPix(b, x, hy, 255, 255, 255);
            int vx = (5 * n) % CapW;
            for (int y = 0; y < CapH; y++) SetPix(b, vx, y, 0, 255, 0);
            int mx = (7 * n) % (CapW - 80), my = 40 + (4 * n) % 200;   // inside the window, between the blocks
            for (int y = my; y < my + 80; y++)
                for (int x = mx; x < mx + 80; x++) SetPix(b, x, y, 255, 0, 255);
            for (int i = 0; i < 16; i++)
            {
                int[] c = ((n >> i) & 1) == 1 ? BlockOne : BlockZero;
                foreach (int top in new[] { BlockTop, BlockBottom })
                    for (int y = top; y < top + 8; y++)
                        for (int x = 8 + i * 36; x < 16 + i * 36; x++) SetPix(b, x, y, c[2], c[1], c[0]);
            }
            return b;
        }

        static void SetPix(byte[] b, int x, int y, int r, int g, int bl)
        {
            int i = (y * CapW + x) * 4;
            b[i] = (byte)bl;
            b[i + 1] = (byte)g;
            b[i + 2] = (byte)r;
            b[i + 3] = 255;
        }

        // Reads the frame id from the blocks at row y0; -1 when a block is not a clean color (tear or transition).
        static int DecodeBlocks(byte[] f, int y0)
        {
            int id = 0;
            for (int i = 0; i < 16; i++)
            {
                int cx = 8 + i * 36;
                int bit = -1;
                for (int y = y0; y < y0 + 8 && bit != -2; y++)
                    for (int x = cx; x < cx + 8; x++)
                    {
                        int k = (y * CapW + x) * 4;
                        int v = -1;
                        if (f[k] == BlockOne[0] && f[k + 1] == BlockOne[1] && f[k + 2] == BlockOne[2]) v = 1;
                        else if (f[k] == BlockZero[0] && f[k + 1] == BlockZero[1] && f[k + 2] == BlockZero[2]) v = 0;
                        if (x == cx && y == y0) bit = v;
                        else if (v != bit) { bit = -2; break; }
                    }
                if (bit < 0) return -1;
                id |= bit << i;
            }
            return id;
        }

        // Receives captured frames: decodes them, compares each with the pattern of its id and writes it to the sink.
        sealed class CaptureRun
        {
            public ScreenSource Source;
            public FramePool Pool;
            public CineSink Sink;
            public int Written, Repeats, WriteFailures, ReadFailures;
            public int Exact, Torn, Unreadable, Unexplained, Duplicates, Skipped, Backwards, BlocksDisagree;
            public long FromPrevious, FromNext, FromNeither;   // mismatched pixels, by the frame they equal
            public int FirstUnexplainedId = -1, FirstUnexplainedRow = -1;
            public string Sample = "";
            byte[] frame;
            int lastId = -1;

            public void ReadOne()
            {
                IntPtr buf = Pool.Take(() => false);
                try
                {
                    if (Source.Read(buf))
                    {
                        if (frame == null) frame = new byte[CapW * CapH * 4];
                        Marshal.Copy(buf, frame, 0, frame.Length);
                        Analyze(frame);
                        if (Sink.Write(buf)) Written++;
                        else WriteFailures++;
                    }
                    else
                    {
                        ReadFailures++;
                        if (Sink.Repeat()) Repeats++;
                        else WriteFailures++;
                    }
                }
                finally { Pool.Give(buf); }
            }

            void Analyze(byte[] f)
            {
                int top = DecodeBlocks(f, BlockTop), bottom = DecodeBlocks(f, BlockBottom);
                if (top < 0 || bottom < 0)
                {
                    if (Unreadable == 0)
                    {
                        int k = (BlockTop + 4) * CapW * 4 + 12 * 4;
                        Sample = "bloque0 superior bgr=" + f[k] + "," + f[k + 1] + "," + f[k + 2] + " (1=" + BlockOne[0] + "," + BlockOne[1] + "," +
                                 BlockOne[2] + " 0=" + BlockZero[0] + "," + BlockZero[1] + "," + BlockZero[2] + ")";
                    }
                    Unreadable++;
                    return;
                }
                int id = top;
                byte[] expected = RealPattern(id);
                int diff = 0;
                for (int p = 0; p < expected.Length; p += 4)
                    if (expected[p] != f[p] || expected[p + 1] != f[p + 1] || expected[p + 2] != f[p + 2]) diff++;
                if (top != bottom)
                {
                    BlocksDisagree++;
                    if (BlocksDisagree == 1) Sample = "primer desacuerdo: arriba " + top + ", abajo " + bottom + ", pixeles distintos de arriba " + diff;
                }
                if (diff == 0) Exact++;
                else ClassifyMismatch(f, id);
                if (lastId >= 0)
                {
                    if (id == lastId) Duplicates++;
                    else if (id < lastId) Backwards++;
                    else if (id > lastId + 1) Skipped += id - lastId - 1;
                }
                lastId = id;
            }

            // A mismatch is a tear when every differing pixel equals the frame before or after; anything else is a bug.
            void ClassifyMismatch(byte[] f, int id)
            {
                byte[] prev = RealPattern(Math.Max(0, id - 1)), next = RealPattern(id + 1), expected = RealPattern(id);
                long neitherBefore = FromNeither;
                int firstRow = -1;
                for (int p = 0; p < expected.Length; p += 4)
                {
                    if (expected[p] == f[p] && expected[p + 1] == f[p + 1] && expected[p + 2] == f[p + 2]) continue;
                    if (firstRow < 0) firstRow = p / 4 / CapW;
                    if (prev[p] == f[p] && prev[p + 1] == f[p + 1] && prev[p + 2] == f[p + 2]) FromPrevious++;
                    else if (next[p] == f[p] && next[p + 1] == f[p + 1] && next[p + 2] == f[p + 2]) FromNext++;
                    else FromNeither++;
                }
                if (FromNeither == neitherBefore) Torn++;
                else
                {
                    Unexplained++;
                    if (FirstUnexplainedId < 0) { FirstUnexplainedId = id; FirstUnexplainedRow = firstRow; }
                }
            }
        }

        static void RealCaptureSuite()
        {
            // The layered window is the default pattern source (whole frames). The plain GDI window tears by design
            // (WM_PAINT into the DWM redirection surface): reported, never failing.
            Test("realcapture", "window-layered-60fps-5s", () => { NeedFfmpeg(); RealCapture(true); });
            Test("realcapture", "info-window-gdi-60fps-5s", () =>
            {
                NeedFfmpeg();
                try { RealCapture(false); }
                catch (TestFailed f) { Add("informativo, no cuenta: " + f.Message); }
            });
            Test("realcapture", "order-session-5x6s", () => OrderSuite("session"));
            Test("realcapture", "order-newest-5x6s", () => OrderSuite("newest"));
        }

        static Rectangle CaptureRect()
        {
            Rectangle wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(wa.Right - CapW - 40, wa.Bottom - CapH - 40, CapW, CapH);
        }

        static CapturePattern NewWindow(Rectangle rect, bool layered)
        {
            CapturePattern win = new CapturePattern(rect);
            win.Layered = layered;
            win.Canvas = new Bitmap(CapW, CapH, PixelFormat.Format32bppArgb);
            return win;
        }

        static void ShowWindow(CapturePattern win, Rectangle rect)
        {
            win.Show();
            for (int i = 0; i < 10; i++) System.Windows.Forms.Application.DoEvents();
            Check(win.Bounds == rect, "la ventana no esta donde debe: " + win.Bounds + " en vez de " + rect);
        }

        [StructLayout(LayoutKind.Sequential)] struct PointN { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SizeN { public int Cx, Cy; }
        [StructLayout(LayoutKind.Sequential)] struct BlendN { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PointN dst, ref SizeN size, IntPtr hdcSrc, ref PointN src,
                                               int key, ref BlendN blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        static void PresentLayered(CapturePattern win, Bitmap canvas)
        {
            IntPtr hbmp = canvas.GetHbitmap(Color.FromArgb(0));
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr old = SelectObject(mem, hbmp);
            try
            {
                PointN dst = new PointN();
                dst.X = win.Left;
                dst.Y = win.Top;
                SizeN size = new SizeN();
                size.Cx = canvas.Width;
                size.Cy = canvas.Height;
                PointN src = new PointN();
                BlendN blend = new BlendN();
                blend.Alpha = 255;
                blend.Format = 1;   // AC_SRC_ALPHA
                UpdateLayeredWindow(win.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);   // ULW_ALPHA
            }
            finally
            {
                SelectObject(mem, old);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
                DeleteObject(hbmp);
            }
        }

        // Presents the pattern once per DWM composition (UpdateLayeredWindow, then DwmFlush), like the coordinator's
        // probe: a 60 fps timer would repeat or skip images on a 144 Hz screen.
        sealed class DwmDrawer
        {
            readonly CapturePattern win;
            readonly Rectangle rect;
            volatile bool running;
            Thread thread;
            public volatile int Shown;
            public string Error = "";

            public DwmDrawer(CapturePattern win, Rectangle rect)
            {
                this.win = win;
                this.rect = rect;
            }

            public void Start()
            {
                running = true;
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.AboveNormal;
                thread.Start();
            }

            public void Stop()
            {
                running = false;
                if (thread != null) thread.Join(3000);
            }

            void Loop()
            {
                IntPtr screen = GetDC(IntPtr.Zero);
                IntPtr mem = CreateCompatibleDC(screen);
                IntPtr dib = IntPtr.Zero, old = IntPtr.Zero;
                try
                {
                    BitmapInfoHeader bmi = new BitmapInfoHeader();
                    bmi.biSize = Marshal.SizeOf(typeof(BitmapInfoHeader));
                    bmi.biWidth = CapW;
                    bmi.biHeight = -CapH;
                    bmi.biPlanes = 1;
                    bmi.biBitCount = 32;
                    IntPtr bits;
                    dib = CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
                    old = SelectObject(mem, dib);
                    PointN dst = new PointN();
                    dst.X = rect.X;
                    dst.Y = rect.Y;
                    SizeN size = new SizeN();
                    size.Cx = CapW;
                    size.Cy = CapH;
                    PointN src = new PointN();
                    BlendN blend = new BlendN();
                    blend.Alpha = 255;
                    blend.Format = 1;
                    for (int k = 0; running; k++)
                    {
                        byte[] px = RealPattern(k);
                        Marshal.Copy(px, 0, bits, px.Length);
                        UpdateLayeredWindow(win.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
                        DwmFlush();
                        Shown = k + 1;
                    }
                }
                catch (Exception e) { Error = e.Message; }
                finally
                {
                    if (old != IntPtr.Zero) SelectObject(mem, old);
                    if (dib != IntPtr.Zero) DeleteObject(dib);
                    DeleteDC(mem);
                    ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        // Ordering: frames read must never show an older image than the previous frame.
        sealed class OrderStats
        {
            public string Mode = "";
            public int Frames, Readable, Unreadable, Backwards, Duplicates, Skipped, MaxBackward, ReadFailures;
            public int LastId = -1;
            public int Shown;
            public long Presents, Misses, Unused;
        }

        // mode "session": Pick(now - 10 ms), Submit, read one behind (Session.Emit); mode "newest": Pick(newest),
        // Submit and read at once. Both paced at 60 ticks per second, with the pattern presented once per composition.
        static void OrderSuite(string mode)
        {
            Program.Init();
            int runs = 5;
            double seconds = 6;
            long frames = 0, readable = 0, backwards = 0, duplicates = 0, skipped = 0;
            int maxBack = 0;
            List<string> perRun = new List<string>();
            for (int i = 0; i < runs; i++)
            {
                OrderStats st = CaptureOrder(mode, seconds);
                frames += st.Frames;
                readable += st.Readable;
                backwards += st.Backwards;
                duplicates += st.Duplicates;
                skipped += st.Skipped;
                maxBack = Math.Max(maxBack, st.MaxBackward);
                perRun.Add(st.Frames + "/" + st.Backwards + "/" + st.MaxBackward + " (dibujados " + st.Shown + ", misses " + st.Misses +
                           ", unused " + st.Unused + ")");
            }
            Put("mode", mode);
            Put("frames", frames);
            Put("readable", readable);
            Put("backwards", backwards);
            Put("maxBackward", maxBack);
            Note(mode + ": " + runs + " x " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s, " + frames + " fotogramas (" + readable +
                 " legibles), " + backwards + " hacia atras (mayor salto " + maxBack + " ids), " + duplicates + " repetidos, " + skipped +
                 " saltados; por pasada fotogramas/atras/mayor salto: " + string.Join("; ", perRun.ToArray()));
            Check(backwards == 0, mode + ": " + backwards + " fotogramas con id menor que el anterior (mayor salto " + maxBack + " ids)");
            Check(readable > 0, mode + ": ningun fotograma legible");
        }

        // One run: the window is presented per composition, the acquire thread does Wait/Take as in Session.
        static OrderStats CaptureOrder(string mode, double seconds)
        {
            Rectangle rect = CaptureRect();
            CapturePattern win = NewWindow(rect, true);
            DwmDrawer drawer = null;
            ScreenSource source = null;
            FramePool pool = null;
            try
            {
                ShowWindow(win, rect);
                drawer = new DwmDrawer(win, rect);
                drawer.Start();
                Thread.Sleep(200);
                source = ScreenSource.Open(rect, false, false);
                pool = new FramePool(source.FrameBytes, 6);
                OrderStats st = new OrderStats();
                st.Mode = mode;
                byte[] frame = new byte[CapW * CapH * 4];
                capturing = true;
                ScreenSource grabber = source;
                Thread acquire = new Thread(() =>
                {
                    while (capturing)
                    {
                        try
                        {
                            long present;
                            if (grabber.Wait(16, out present)) grabber.Take();
                        }
                        catch (Exception) { break; }
                    }
                });
                acquire.Priority = ThreadPriority.Highest;
                acquire.Start();

                bool newest = mode == "newest";
                long freq = Stopwatch.Frequency;
                double period = freq / 60.0;
                long t0 = Stopwatch.GetTimestamp();
                Stopwatch clock = Stopwatch.StartNew();
                int ticks = 0;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    int v = newest ? source.Pick(double.MaxValue) : source.Pick(Stopwatch.GetTimestamp() - freq / 100.0);
                    source.Submit(new Native.CURSORINFO(), v);
                    if (newest) ReadOrder(source, pool, frame, st);
                    else if (source.Pending >= 2) ReadOrder(source, pool, frame, st);
                    ticks++;
                    long next = t0 + (long)(ticks * period);
                    while (Stopwatch.GetTimestamp() < next)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }
                }
                while (source.Pending > 0) ReadOrder(source, pool, frame, st);
                capturing = false;
                acquire.Join(2000);
                drawer.Stop();
                if (drawer.Error.Length > 0) throw new TestFailed("dibujo de la ventana: " + drawer.Error);
                st.Shown = drawer.Shown;
                st.Presents = source.Presents;
                st.Misses = source.Misses;
                st.Unused = source.Unused;
                return st;
            }
            finally
            {
                capturing = false;
                if (drawer != null) drawer.Stop();
                if (source != null) { try { source.Dispose(); } catch { } }
                if (pool != null) pool.Dispose();
                try { win.Close(); } catch { }
                win.Dispose();
            }
        }

        static void ReadOrder(ScreenSource source, FramePool pool, byte[] frame, OrderStats st)
        {
            IntPtr buf = pool.Take(() => false);
            try
            {
                if (!source.Read(buf))
                {
                    st.ReadFailures++;
                    return;
                }
                Marshal.Copy(buf, frame, 0, frame.Length);
                st.Frames++;
                int id = DecodeBlocks(frame, BlockTop);
                if (id < 0)
                {
                    st.Unreadable++;
                    return;
                }
                st.Readable++;
                if (st.LastId >= 0)
                {
                    if (id < st.LastId)
                    {
                        st.Backwards++;
                        st.MaxBackward = Math.Max(st.MaxBackward, st.LastId - id);
                    }
                    else if (id == st.LastId) st.Duplicates++;
                    else if (id > st.LastId + 1) st.Skipped += id - st.LastId - 1;
                }
                st.LastId = id;
            }
            finally { pool.Give(buf); }
        }

        static void RealCapture(bool layered)
        {
            Program.Init();   // per-monitor DPI aware: the rectangle below is in physical pixels
            Rectangle rect = CaptureRect();
            string dir = WorkDir("realcapture");
            CapturePattern win = NewWindow(rect, layered);
            ScreenSource source = null;
            CineSink sink = null;
            FramePool pool = null;
            try
            {
                ShowWindow(win, rect);

                source = ScreenSource.Open(rect, false, false);
                pool = new FramePool(source.FrameBytes, 6);
                string nut = Path.Combine(dir, "capture.nut"), master = Path.Combine(dir, "master.mkv");
                sink = new CineSink(ffmpeg, CapW, CapH, 60, 1, nut);
                sink.Start();
                CaptureRun run = new CaptureRun();
                run.Source = source;
                run.Pool = pool;
                run.Sink = sink;

                capturing = true;
                ScreenSource grabber = source;
                Thread acquire = new Thread(() =>
                {
                    while (capturing)
                    {
                        try
                        {
                            long present;
                            if (grabber.Wait(16, out present)) grabber.Take();
                        }
                        catch (Exception) { break; }
                    }
                });
                acquire.Start();

                // Same rhythm as Session.Emit: submit one frame per tick, read one frame behind.
                double period = Stopwatch.Frequency / 60.0;
                long t0 = Stopwatch.GetTimestamp();
                Stopwatch clock = Stopwatch.StartNew();
                int drawn = 0;
                while (clock.Elapsed.TotalSeconds < 5)
                {
                    DrawPattern(win, drawn);
                    source.Submit(new Native.CURSORINFO(), source.Pick(Stopwatch.GetTimestamp()));
                    if (source.Pending >= 2) run.ReadOne();
                    drawn++;
                    long next = t0 + (long)(drawn * period);
                    while (Stopwatch.GetTimestamp() < next)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }
                }
                while (source.Pending > 0) run.ReadOne();
                double seconds = clock.Elapsed.TotalSeconds;
                capturing = false;
                acquire.Join(2000);

                long presents = source.Presents;
                Put("screenPresents", presents);
                Put("screenSourcePresents", source.SourcePresents);
                Put("screenCoalesced", source.Coalesced);
                Put("screenUnused", source.Unused);
                Put("screenMisses", source.Misses);
                Put("screenHdr", source.Hdr);
                Put("screenDescription", source.Description);
                Put("screenRefresh", source.RefreshNum + "/" + source.RefreshDen);
                Put("screenBroken", source.Broken);
                source.Dispose();
                source = null;

                int code;
                if (!sink.Close(120000, out code) || code != 0) throw new TestFailed("ffmpeg no cerro bien: " + sink.ErrorTail);
                int frames = sink.Frames;
                string err = CineMaster.Build(ffmpeg, nut, null, null, "", master, frames / 60.0, null);
                if (err != null) throw new TestFailed("Build: " + err);
                CineExpect e = new CineExpect();
                e.Width = CapW; e.Height = CapH; e.FpsNum = 60; e.FpsDen = 1;
                e.FrameHashes = new List<ulong>(sink.Hashes);
                CineVerification v = CineVerifier.Verify(ffmpeg, master, e, null);

                Put("drawn", drawn);
                Put("framesWritten", frames);
                Put("exact", run.Exact);
                Put("torn", run.Torn);
                Put("blocksDisagree", run.BlocksDisagree);
                Put("unreadable", run.Unreadable);
                Put("unexplained", run.Unexplained);
                Put("duplicates", run.Duplicates);
                Put("skipped", run.Skipped);
                Put("backwards", run.Backwards);
                Put("repeats", run.Repeats);
                Put("readFailures", run.ReadFailures);
                Put("verified", v.Passed);
                Note("ventana " + rect + (layered ? " (en capas)" : " (GDI)") + ", " + drawn + " dibujados, " + frames + " capturados en " +
                     seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s (" + (frames / seconds).ToString("0.0", CultureInfo.InvariantCulture) +
                     " fps); exactos " + run.Exact +
                     ", desgarros " + run.Torn + " (pixeles de un fotograma vecino), bloques arriba/abajo distintos " + run.BlocksDisagree +
                     ", ilegibles " + run.Unreadable +
                     ", no explicados " + run.Unexplained + " (primero id " + run.FirstUnexplainedId + ", fila " + run.FirstUnexplainedRow +
                     "); pixeles de id-1 " + run.FromPrevious + ", de id+1 " + run.FromNext + ", de ninguno " + run.FromNeither +
                     "; repetidos " + run.Duplicates + ", saltados " + run.Skipped + ", atras " + run.Backwards +
                     ", lecturas fallidas " + run.ReadFailures + " [" + run.Sample + "]; ScreenSource presents " + presents +
                     "; verificado " + v.Passed + (v.Passed ? "" : " (" + FailingChecks(v) + ")"));
                Check(v.Passed && v.FramesMatched == frames && v.FramesDecoded == frames, "master no verificado: " + FailingChecks(v));
                Check(run.WriteFailures == 0, run.WriteFailures + " fallos de escritura");
                Check(run.Unexplained == 0, run.Unexplained + " fotogramas con pixeles que no son de ningun fotograma del patron (primero id " +
                      run.FirstUnexplainedId + ", fila " + run.FirstUnexplainedRow + ")");
                Check(run.Exact > 0, "ningun fotograma capturado coincide con el patron");
            }
            finally
            {
                capturing = false;
                if (source != null) { try { source.Dispose(); } catch { } }
                if (sink != null) sink.Dispose();
                if (pool != null) pool.Dispose();
                try { win.Close(); } catch { }
                win.Dispose();
                DeleteDir(dir);
            }
        }

        static void DrawPattern(CapturePattern win, int n)
        {
            byte[] px = RealPattern(n);
            BitmapData d = win.Canvas.LockBits(new Rectangle(0, 0, CapW, CapH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(px, 0, d.Scan0, px.Length); }
            finally { win.Canvas.UnlockBits(d); }
            if (win.Layered) PresentLayered(win, win.Canvas);
            else
            {
                win.Invalidate();
                win.Update();
            }
        }
    }
}

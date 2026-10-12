// Stackshot - Clicks and keys drawn into a recording: hooks, the shortcut captions and the overlay on the frames.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot
{
    // One mouse click or one key combination, stamped on the Stopwatch clock when it happened.
    internal struct InputEvent
    {
        public const int Left = 1, Right = 2, Key = 3;
        public int Kind, X, Y, Vk, Mods;
        public bool Extended, Repeat;
        public long Time;
        internal long Rt;          // recording time, set when the overlay takes it
        internal int Segment;      // pauses before it

        public static InputEvent Click(long time, int x, int y, bool right)
        {
            InputEvent e = new InputEvent();
            e.Kind = right ? Right : Left;
            e.Time = time;
            e.X = x;
            e.Y = y;
            return e;
        }

        public static InputEvent Press(long time, int vk, int mods, bool extended, bool repeat)
        {
            InputEvent e = new InputEvent();
            e.Kind = Key;
            e.Time = time;
            e.Vk = vk;
            e.Mods = mods;
            e.Extended = extended;
            e.Repeat = repeat;
            return e;
        }
    }

    // From the hooks' thread to the frames' thread without locks. Bounded, never waited for: when full the oldest event
    // goes (nothing takes them during a pause, and what happens after it must still show).
    internal sealed class InputQueue
    {
        public const int Max = 256;
        readonly ConcurrentQueue<InputEvent> q = new ConcurrentQueue<InputEvent>();
        int count;

        public int Count { get { return Volatile.Read(ref count); } }

        public void Post(InputEvent e)
        {
            q.Enqueue(e);
            if (Interlocked.Increment(ref count) <= Max) return;
            InputEvent old;
            if (q.TryDequeue(out old)) Interlocked.Decrement(ref count);
        }

        public bool TryTake(out InputEvent e)
        {
            if (!q.TryDequeue(out e)) return false;
            Interlocked.Decrement(ref count);
            return true;
        }
    }

    // Which key presses may be shown, and how. Only shortcuts and keys that type nothing: a letter, digit, space or
    // punctuation is never shown on its own or with Shift (that is typing, maybe a password), nor with AltGr or
    // Ctrl + Alt (they type characters such as @ or # on many layouts), nor Alt with the numeric keypad (Alt + 64 types
    // @).
    internal static class KeyCaption
    {
        public const int Ctrl = 1, Shift = 2, Alt = 4, Win = 8, AltGr = 16;

        public static bool IsModifier(int vk)
        {
            return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        // Keys that type something (or a character code): letters, digits, space, punctuation, the numeric keypad.
        public static bool Types(int vk)
        {
            return vk == 0x20 || (vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x60 && vk <= 0x6F) || vk == 0x0C ||
                   (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDF) || vk == 0xE2;
        }

        // The keypad: its digits, and with Num Lock off its arrows, Inicio, Fin... (not extended keys).
        static bool Keypad(int vk, bool extended)
        {
            if (vk >= 0x60 && vk <= 0x69) return true;
            if (extended) return false;
            return vk == 0x2D || vk == 0x2E || vk == 0x0C || (vk >= 0x21 && vk <= 0x28);
        }

        // Keys shown even without a modifier: they type nothing.
        static bool Named(int vk)
        {
            return vk == 0x08 || vk == 0x09 || vk == 0x0D || vk == 0x13 || vk == 0x03 || vk == 0x1B || (vk >= 0x21 && vk <= 0x28) ||
                   vk == 0x2C || vk == 0x2D || vk == 0x2E || vk == 0x5D || (vk >= 0x70 && vk <= 0x87);
        }

        public static bool Shown(int vk, int mods, bool extended)
        {
            if (vk <= 0 || vk >= 0xFF || IsModifier(vk)) return false;
            bool ctrl = (mods & Ctrl) != 0, alt = (mods & (Alt | AltGr)) != 0, win = (mods & Win) != 0, altGr = (mods & AltGr) != 0;
            if (alt && !ctrl && !win && Keypad(vk, extended)) return false;
            if (Types(vk))
            {
                if (win) return true;
                if (altGr || (ctrl && alt)) return false;
                return ctrl || alt;
            }
            return Named(vk);
        }

        // "Ctrl + C", "Win + Mayús + S", "Intro"; null when it must not be shown.
        public static string Text(int vk, int mods, bool extended)
        {
            if (!Shown(vk, mods, extended)) return null;
            string key = Hotkeys.KeyName((Keys)vk);
            string m = Hotkeys.DisplayMods((mods & Ctrl) != 0, (mods & Shift) != 0, (mods & (Alt | AltGr)) != 0, (mods & Win) != 0);
            return m.Length > 0 ? m + " + " + key : key;
        }
    }

    // Low-level mouse and keyboard hooks, installed only while a recording that shows clicks or keys runs, on a thread
    // of their own with its message loop (the UI thread may be busy). The callbacks only queue what happened and return;
    // plain typing is filtered out right there, so it never even reaches the queue.
    internal sealed class InputHooks : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y, priv; }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        // Hooks installed right now, in the whole process (the tests check it stays at zero).
        static int live;
        internal static int Live { get { return Volatile.Read(ref live); } }

        readonly InputQueue queue;
        readonly bool mouse, keys;
        readonly HookProc mouseProc, keyProc;    // kept alive as long as Windows may call them
        readonly Thread thread;
        volatile uint tid;
        volatile bool stop;
        IntPtr mouseHook, keyHook;
        int downVk;

        InputHooks(InputQueue queue, bool mouse, bool keys)
        {
            this.queue = queue;
            this.mouse = mouse;
            this.keys = keys;
            mouseProc = OnMouse;
            keyProc = OnKey;
            thread = new Thread(Run);
            thread.IsBackground = true;
            // Windows waits for these callbacks before it delivers each click and key press: never starved.
            thread.Priority = ThreadPriority.Highest;
            thread.Name = "Stackshot: clics y teclas";
        }

        // Never waits: the thread installs the hooks by itself.
        public static InputHooks Start(InputQueue queue, bool mouse, bool keys)
        {
            InputHooks h = new InputHooks(queue, mouse, keys);
            h.thread.Start();
            return h;
        }

        void Run()
        {
            try
            {
                Power.OptOutThisThread();
                IntPtr module = GetModuleHandle(null);
                if (mouse) mouseHook = SetWindowsHookEx(14, mouseProc, module, 0);   // WH_MOUSE_LL
                if (keys) keyHook = SetWindowsHookEx(13, keyProc, module, 0);        // WH_KEYBOARD_LL
                if ((mouse && mouseHook == IntPtr.Zero) || (keys && keyHook == IntPtr.Zero))
                    ShotStack.Log("Grabaci\u00F3n: no se pueden mostrar los clics o las teclas (" + Marshal.GetLastWin32Error() + ")");
                Interlocked.Add(ref live, (mouseHook != IntPtr.Zero ? 1 : 0) + (keyHook != IntPtr.Zero ? 1 : 0));
                MSG m;
                PeekMessage(out m, IntPtr.Zero, 0, 0, 0); // the thread's message queue, so Dispose can post to it
                tid = Native.GetCurrentThreadId();
                Thread.MemoryBarrier();
                while (!stop && GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
            }
            catch (Exception ex) { ShotStack.Log("Grabaci\u00F3n (clics y teclas): " + ex.Message); }
            finally
            {
                if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; Interlocked.Decrement(ref live); }
                if (keyHook != IntPtr.Zero) { UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; Interlocked.Decrement(ref live); }
            }
        }

        // Called by Windows from native code: nothing may escape (it would end the process), and the input always goes on.
        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                long msg = wParam.ToInt64();
                if (code >= 0 && (msg == 0x201 || msg == 0x204)) // WM_LBUTTONDOWN, WM_RBUTTONDOWN
                    queue.Post(InputEvent.Click(Stopwatch.GetTimestamp(), Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4), msg == 0x204));
            }
            catch { }
            return CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0)
                {
                    long msg = wParam.ToInt64();
                    int vk = Marshal.ReadInt32(lParam, 0);
                    if ((msg == 0x100 || msg == 0x104) && !KeyCaption.IsModifier(vk)) // WM_KEYDOWN, WM_SYSKEYDOWN
                    {
                        bool extended = (Marshal.ReadInt32(lParam, 8) & 1) != 0, repeat = vk == downVk;
                        downVk = vk;
                        int mods = Mods();
                        if (KeyCaption.Shown(vk, mods, extended)) queue.Post(InputEvent.Press(Stopwatch.GetTimestamp(), vk, mods, extended, repeat));
                    }
                    else if ((msg == 0x101 || msg == 0x105) && vk == downVk) downVk = 0;
                }
            }
            catch { }
            return CallNextHookEx(keyHook, code, wParam, lParam);
        }

        // The modifiers as Windows has them (never a state of our own that a missed key-up could leave stuck).
        static int Mods()
        {
            int m = 0;
            if (Down(0xA2) || Down(0xA3)) m |= KeyCaption.Ctrl;
            if (Down(0xA0) || Down(0xA1)) m |= KeyCaption.Shift;
            if (Down(0xA4)) m |= KeyCaption.Alt;
            if (Down(0xA5)) m |= KeyCaption.AltGr;
            if (Down(0x5B) || Down(0x5C)) m |= KeyCaption.Win;
            return m;
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public void Dispose()
        {
            stop = true;
            Thread.MemoryBarrier();
            uint t = tid;
            if (t != 0) PostThreadMessage(t, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        }
    }

    // Draws the clicks (a ring that grows and fades, left and right in different colors) and the last shortcut (a caption
    // at the bottom center) into each frame before it is encoded, like the cursor, for every quality. Sizes follow the
    // monitor's scale. Times run on recording time, and nothing from before a pause shows after it.
    internal sealed class InputOverlay : IDisposable
    {
        public const double ClickMs = 400, HoldMs = 1500, FadeMs = 250, InMs = 80;
        static readonly Color LeftColor = Color.FromArgb(10, 132, 255), RightColor = Color.FromArgb(255, 159, 10);
        const int MaxRings = 24;

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT p);
        [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr h, out uint affinity);

        readonly object sync = new object();
        readonly int width, height, originX, originY;
        readonly float scale;
        readonly bool clicks, keys;
        readonly PauseTimeline pauses;
        readonly InputQueue queue;
        readonly long clickTicks, holdTicks, fadeTicks, inTicks;
        readonly List<InputEvent> waiting = new List<InputEvent>();
        readonly List<InputEvent> rings = new List<InputEvent>();
        // A click on a window hidden from captures (the recording bar, the tray panel): the video shows what is under it.
        public Func<int, int, bool> Hidden;
        string caption, drawn;
        int count, captionSegment;
        long captionSince, captionLast;
        byte[] pill = new byte[0], layer = new byte[0], row = new byte[0], row2 = new byte[0];
        int pillW, pillH;
        Font font;
        float fontPx;
        bool disposed, failed;
        bool touched;       // the frame being drawn got something laid over it
        long drawnFrames;   // frames that did (a Cine report tells them)

        // area: the recorded rectangle (screen pixels); scale: its monitor's (1 = 100 %).
        public InputOverlay(Rectangle area, float scale, bool clicks, bool keys, PauseTimeline pauses, InputQueue queue)
        {
            width = area.Width;
            height = area.Height;
            originX = area.X;
            originY = area.Y;
            this.scale = Math.Max(0.5f, scale);
            this.clicks = clicks;
            this.keys = keys;
            this.pauses = pauses;
            this.queue = queue;
            double f = Stopwatch.Frequency / 1000.0;
            clickTicks = (long)(ClickMs * f);
            holdTicks = (long)(HoldMs * f);
            fadeTicks = (long)(FadeMs * f);
            inTicks = (long)(InMs * f);
        }

        // What the caption says now (null: none), for tests and the log.
        internal string Caption { get { lock (sync) return caption == null ? null : Display(); } }
        internal int Rings { get { lock (sync) return rings.Count; } }
        internal int PillWidth { get { lock (sync) return pillW; } }
        internal int PillHeight { get { lock (sync) return pillH; } }
        internal long DrawnFrames { get { lock (sync) return drawnFrames; } }

        // A window the click landed on is hidden from captures (display affinity set).
        internal static bool CaptureHides(int x, int y)
        {
            try
            {
                Native.POINT p;
                p.X = x;
                p.Y = y;
                IntPtr h = WindowFromPoint(p);
                if (h == IntPtr.Zero) return false;
                IntPtr root = Native.GetAncestor(h, 2); // GA_ROOT
                uint a;
                return GetWindowDisplayAffinity(root != IntPtr.Zero ? root : h, out a) && a != 0;
            }
            catch { return false; }
        }

        // One frame (BGRA or NV12, width x height) whose image was on screen at qpc.
        public void Draw(IntPtr frame, bool nv12, long qpc)
        {
            lock (sync)
            {
                if (disposed || failed || frame == IntPtr.Zero) return;
                try
                {
                    PauseTimeline.View v = pauses.Current;
                    bool inPause;
                    int segment;
                    long rt = v.RtOf(qpc, out inPause, out segment);
                    Take(v);
                    Apply(rt);
                    touched = false;
                    if (rings.Count > 0) DrawRings(frame, nv12, rt, segment);
                    if (caption != null) DrawCaption(frame, nv12, rt, segment);
                    if (touched) drawnFrames++;
                }
                catch (Exception ex)
                {
                    // Never in the way of the recording: the overlay just stops.
                    failed = true;
                    ShotStack.Log("Grabaci\u00F3n: clics y teclas: " + ex.Message);
                }
            }
        }

        // New events from the hooks, each on recording time; those in a pause or on a hidden window are dropped.
        void Take(PauseTimeline.View v)
        {
            InputEvent e;
            while (queue.TryTake(out e))
            {
                if (e.Kind == InputEvent.Key ? !keys : !clicks) continue;
                bool inPause;
                e.Rt = v.RtOf(e.Time, out inPause, out e.Segment);
                if (inPause) continue;
                if (e.Kind != InputEvent.Key)
                {
                    int r = (int)Math.Ceiling(RingMax() + 4);
                    if (e.X < originX - r || e.Y < originY - r || e.X > originX + width + r || e.Y > originY + height + r) continue;
                    if (Hidden != null && Hidden(e.X, e.Y)) continue;
                }
                waiting.Add(e);
            }
        }

        // Events up to this frame's moment start their ring or update the caption; later ones wait for later frames.
        void Apply(long rt)
        {
            for (int i = 0; i < waiting.Count; i++)
            {
                InputEvent e = waiting[i];
                if (e.Rt > rt) continue;
                waiting.RemoveAt(i--);
                if (e.Kind != InputEvent.Key)
                {
                    rings.Add(e);
                    if (rings.Count > MaxRings) rings.RemoveAt(0);
                    continue;
                }
                string text = KeyCaption.Text(e.Vk, e.Mods, e.Extended);
                if (text == null) continue;
                bool showing = caption != null && e.Segment == captionSegment && e.Rt - captionLast < holdTicks + fadeTicks;
                if (showing && text == caption)
                {
                    if (!e.Repeat) count++;
                    captionLast = e.Rt;
                    continue;
                }
                // A new combination: it replaces the one showing at once (no new fade-in), or fades in on its own.
                caption = text;
                count = 1;
                captionSince = showing ? e.Rt - inTicks : e.Rt;
                captionLast = e.Rt;
                captionSegment = e.Segment;
            }
        }

        double RingMax() { return 22 * scale; }

        void DrawRings(IntPtr frame, bool nv12, long rt, int segment)
        {
            for (int i = 0; i < rings.Count; i++)
            {
                InputEvent e = rings[i];
                long age = rt - e.Rt;
                if (e.Segment != segment || age >= clickTicks)
                {
                    rings.RemoveAt(i--);
                    continue;
                }
                double f = age / (double)clickTicks, k = 1 - f;
                double grow = 1 - k * k * k;
                double r = 6 * scale + (RingMax() - 6 * scale) * grow;
                double stroke = (3 - f) * scale;
                // Clear for most of its life, then gone.
                Ring(frame, nv12, e.X - originX + 0.5, e.Y - originY + 0.5, r, stroke, e.Kind == InputEvent.Right ? RightColor : LeftColor,
                     0.95 * (1 - f * f), 0.28 * k);
            }
        }

        // An antialiased ring with a light fill, drawn into `layer` (premultiplied BGRA) and laid over the frame.
        void Ring(IntPtr frame, bool nv12, double cx, double cy, double r, double stroke, Color c, double ringAlpha, double fillAlpha)
        {
            int pad = (int)Math.Ceiling(r + stroke) + 2;
            int x0 = (int)Math.Floor(cx) - pad, y0 = (int)Math.Floor(cy) - pad, size = 2 * pad + 1;
            if (x0 + size <= 0 || y0 + size <= 0 || x0 >= width || y0 >= height) return;
            if (layer.Length < size * size * 4) layer = new byte[size * size * 4];
            double half = stroke / 2;
            for (int y = 0; y < size; y++)
            {
                double dy = y0 + y + 0.5 - cy;
                for (int x = 0; x < size; x++)
                {
                    double dx = x0 + x + 0.5 - cx, d = Math.Sqrt(dx * dx + dy * dy);
                    double fill = Clamp(r - d + 0.5) * fillAlpha, ring = Clamp(half - Math.Abs(d - r) + 0.5) * ringAlpha;
                    double a = ring + fill * (1 - ring);
                    int o = (y * size + x) * 4;
                    layer[o] = (byte)Math.Round(c.B * a);
                    layer[o + 1] = (byte)Math.Round(c.G * a);
                    layer[o + 2] = (byte)Math.Round(c.R * a);
                    layer[o + 3] = (byte)Math.Round(255 * a);
                }
            }
            Compose(frame, nv12, layer, size, size, x0, y0, 1);
        }

        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        string Display() { return count > 1 ? caption + " \u00D7" + count : caption; }

        void DrawCaption(IntPtr frame, bool nv12, long rt, int segment)
        {
            long age = rt - captionLast;
            if (captionSegment != segment || age >= holdTicks + fadeTicks)
            {
                caption = null;
                return;
            }
            double alpha = Math.Min(1, (rt - captionSince + 1) / (double)Math.Max(1, inTicks));
            if (age > holdTicks) alpha *= 1 - (age - holdTicks) / (double)fadeTicks;
            if (alpha <= 0) return;
            string text = Display();
            if (text != drawn) RenderPill(text);
            if (pillW <= 0) return;
            int margin = Math.Max(4, (int)Math.Round(Math.Min(36 * scale, height * 0.08)));
            Compose(frame, nv12, pill, pillW, pillH, (width - pillW) / 2, height - margin - pillH, alpha);
        }

        // The caption as a dark capsule with white text (premultiplied BGRA), narrower than the area.
        void RenderPill(string text)
        {
            drawn = text;
            float px = 17 * scale;
            int maxW = Math.Max(8, width - 2 * (int)Math.Round(12 * scale));
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics m = Graphics.FromImage(probe))
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                SizeF size = Measure(m, text, px, sf);
                // Padding grows with the text: a small area gets a smaller caption, the same shape.
                if (size.Width + 1.6 * px > maxW)
                {
                    px = (float)Math.Max(8, px * maxW / (size.Width + 1.6 * px));
                    size = Measure(m, text, px, sf);
                }
                int w = Math.Min(maxW, (int)Math.Ceiling(size.Width + 1.6 * px)), h = (int)Math.Ceiling(px * 1.9);
                if (w <= 2 || h <= 2 || h > height) { pillW = pillH = 0; return; }
                using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);
                        using (GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, w - 1, h - 1), (h - 1) / 2f))
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(224, 22, 22, 24)))
                        using (Pen edge = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1, scale)))
                        {
                            g.FillPath(bg, path);
                            g.DrawPath(edge, path);
                        }
                        using (SolidBrush ink = new SolidBrush(Color.White))
                        using (StringFormat center = (StringFormat)sf.Clone())
                        {
                            center.Alignment = StringAlignment.Center;
                            center.LineAlignment = StringAlignment.Center;
                            g.DrawString(text, font, ink, new RectangleF(0, 0, w, h), center);
                        }
                    }
                    BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        if (pill.Length < w * h * 4) pill = new byte[w * h * 4];
                        for (int y = 0; y < h; y++) Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), pill, y * w * 4, w * 4);
                    }
                    finally { b.UnlockBits(bd); }
                    pillW = w;
                    pillH = h;
                }
            }
        }

        SizeF Measure(Graphics g, string text, float px, StringFormat sf)
        {
            if (font == null || Math.Abs(fontPx - px) > 0.01f)
            {
                if (font != null) font.Dispose();
                font = new Font(Fonts.DisplaySemibold, px, GraphicsUnit.Pixel);
                fontPx = px;
            }
            return g.MeasureString(text, font, PointF.Empty, sf);
        }

        // Lays premultiplied BGRA (sw x sh at dx, dy, times alpha) over the frame, clipped to it. NV12 frames are blended in
        // BT.709 limited-range Y'CbCr, which is the same as blending the R'G'B' values; chroma by 2 x 2 blocks.
        void Compose(IntPtr frame, bool nv12, byte[] src, int sw, int sh, int dx, int dy, double alpha)
        {
            int x0 = Math.Max(0, dx), y0 = Math.Max(0, dy), x1 = Math.Min(width, dx + sw), y1 = Math.Min(height, dy + sh);
            if (x0 >= x1 || y0 >= y1) return;
            int k = (int)Math.Round(Clamp(alpha) * 256);
            if (k <= 0) return;
            if (!nv12)
            {
                int n = (x1 - x0) * 4;
                if (row.Length < n) row = new byte[n];
                for (int y = y0; y < y1; y++)
                {
                    IntPtr p = new IntPtr(frame.ToInt64() + ((long)y * width + x0) * 4);
                    Marshal.Copy(p, row, 0, n);
                    int so = ((y - dy) * sw + (x0 - dx)) * 4;
                    bool any = false;
                    for (int i = 0; i < n; i += 4)
                    {
                        int a = src[so + i + 3] * k >> 8;
                        if (a == 0) continue;
                        any = true;
                        int keep = 255 - a;
                        row[i] = (byte)Math.Min(255, (src[so + i] * k >> 8) + (row[i] * keep + 127) / 255);
                        row[i + 1] = (byte)Math.Min(255, (src[so + i + 1] * k >> 8) + (row[i + 1] * keep + 127) / 255);
                        row[i + 2] = (byte)Math.Min(255, (src[so + i + 2] * k >> 8) + (row[i + 2] * keep + 127) / 255);
                    }
                    if (any) { Marshal.Copy(row, 0, p, n); touched = true; }
                }
                return;
            }
            double ka = k / 256.0;
            int ny = x1 - x0;
            if (row.Length < ny) row = new byte[ny];
            for (int y = y0; y < y1; y++)
            {
                IntPtr p = new IntPtr(frame.ToInt64() + (long)y * width + x0);
                Marshal.Copy(p, row, 0, ny);
                int so = ((y - dy) * sw + (x0 - dx)) * 4;
                for (int i = 0; i < ny; i++)
                {
                    int o = so + i * 4;
                    double a = src[o + 3] / 255.0 * ka;
                    if (a <= 0) continue;
                    double luma = 16 * a + (0.2126 * src[o + 2] + 0.7152 * src[o + 1] + 0.0722 * src[o]) * ka * 219 / 255;
                    row[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(luma + row[i] * (1 - a))));
                }
                Marshal.Copy(row, 0, p, ny);
            }
            // Chroma: one U, V pair per 2 x 2 pixels, in the plane after the luma.
            int bx0 = x0 / 2, bx1 = (x1 + 1) / 2, by0 = y0 / 2, by1 = (y1 + 1) / 2;
            int nc = (bx1 - bx0) * 2;
            if (row2.Length < nc) row2 = new byte[nc];
            long chroma = frame.ToInt64() + (long)width * height;
            for (int by = by0; by < by1 && by < height / 2; by++)
            {
                IntPtr p = new IntPtr(chroma + (long)by * width + bx0 * 2);
                Marshal.Copy(p, row2, 0, nc);
                bool any = false;
                for (int bx = bx0; bx < bx1 && bx < width / 2; bx++)
                {
                    double sa = 0, su = 0, sv = 0;
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            int x = bx * 2 + i, y = by * 2 + j;
                            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
                            int o = ((y - dy) * sw + (x - dx)) * 4;
                            double a = src[o + 3] / 255.0 * ka;
                            if (a <= 0) continue;
                            double r = src[o + 2] * ka, g = src[o + 1] * ka, b = src[o] * ka;
                            sa += a;
                            su += 128 * a + (-0.1146 * r - 0.3854 * g + 0.5 * b) * 224 / 255;
                            sv += 128 * a + (0.5 * r - 0.4542 * g - 0.0458 * b) * 224 / 255;
                        }
                    if (sa <= 0) continue;
                    any = true;
                    sa /= 4;
                    int c = (bx - bx0) * 2;
                    row2[c] = (byte)Math.Max(0, Math.Min(255, Math.Round(su / 4 + row2[c] * (1 - sa))));
                    row2[c + 1] = (byte)Math.Max(0, Math.Min(255, Math.Round(sv / 4 + row2[c + 1] * (1 - sa))));
                }
                if (any) { Marshal.Copy(row2, 0, p, nc); touched = true; }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (font != null) { font.Dispose(); font = null; }
                waiting.Clear();
                rings.Clear();
            }
        }
    }

    partial class Session
    {
        InputQueue inputs;
        InputHooks hooks;
        InputOverlay overlay;
        readonly double[] overlayCut = new double[8];   // the cut of each frame submitted and not yet read
        long overlaySubmitted, overlayRead;

        internal InputOverlay OverlayForTest { get { return overlay; } }
        internal InputQueue InputsForTest { get { return inputs; } }

        // A recording that shows clicks or keys: the overlay, and the hooks that feed it (never in tests, where the
        // session has no owner and the events are made up).
        void StartInput()
        {
            if (!settings.RecordClicks && !settings.RecordKeys) return;
            inputs = new InputQueue();
            float scale = owner != null ? ShotStack.ScaleFor(Screen.FromRectangle(area)) : 1f;
            overlay = new InputOverlay(area, scale, settings.RecordClicks, settings.RecordKeys, pauses, inputs);
            if (owner == null) return;
            overlay.Hidden = InputOverlay.CaptureHides;
            hooks = InputHooks.Start(inputs, settings.RecordClicks, settings.RecordKeys);
        }

        internal void StartInputForTest() { StartInput(); }

        // The hooks go as soon as the recording stops; the last frames may still be drawn.
        void StopInput()
        {
            InputHooks h = hooks;
            hooks = null;
            if (h != null) h.Dispose();
        }

        // Done, Abandon, Shutdown: nothing of it is left.
        void EndInput()
        {
            StopInput();
            InputOverlay o = overlay;
            if (o != null) o.Dispose();
        }

        // Emit: the cut of the frame being submitted, for when it is read (frames are read in order, one behind).
        void NoteCut(double cut)
        {
            if (overlay != null) overlayCut[overlaySubmitted++ & 7] = cut;
        }

        // Drain: every frame read gets the clicks and keys of its moment before it goes to the encoder.
        bool Overlaid(IntPtr buf, bool read)
        {
            if (overlay == null) return read;
            double cut = overlayCut[overlayRead++ & 7];
            if (read) overlay.Draw(buf, source.Nv12, (long)cut);
            return read;
        }
    }
}

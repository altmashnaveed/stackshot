// Stackshot - Clipboard image that reports when it is pasted.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Stackshot
{
    public class TrackedData : DataObject
    {
        // Follows a rename of the capture, so a paste still finds its card.
        public string FilePath { get; internal set; }
        readonly DateTime since = DateTime.Now;
        readonly Action<TrackedData> onUsed;
        readonly Bitmap image;
        readonly MemoryStream png = new MemoryStream();
        readonly object pngLock = new object();
        bool used, pngReady, released;

        // pngFromFile: the image is the file's PNG, so its bytes are used as-is without re-encoding.
        public TrackedData(string path, Bitmap image, bool fromCapture, bool pngFromFile, Action<TrackedData> onUsed)
        {
            FilePath = path;
            this.image = image;
            this.onUsed = onUsed;
            // The PNG (slow for large captures) is prepared on a worker thread, either read from the file once written
            // or encoded from a deep copy (a same-format Clone may share pixels and GDI+ objects aren't thread-safe).
            // Readers block until it's ready.
            Bitmap copy = pngFromFile ? null : DeepCopy(image);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (TestPngDelayMs > 0) System.Threading.Thread.Sleep(TestPngDelayMs);
                    bool done = false;
                    if (copy == null)
                    {
                        ShotStack.WaitWritten(path);
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(path);
                            png.Write(bytes, 0, bytes.Length);
                            done = true;
                        }
                        catch (Exception ex) { ShotStack.Log("PNG del portapapeles (fichero): " + ex.Message); }
                    }
                    // If the file couldn't be read, the PNG stays empty and apps fall back to the bitmap.
                    if (!done && copy != null) copy.Save(png, ImageFormat.Png);
                    png.Position = 0;
                }
                catch (Exception ex) { ShotStack.Log("PNG del portapapeles: " + ex.Message); }
                finally
                {
                    if (copy != null) copy.Dispose();
                    lock (pngLock)
                    {
                        pngReady = true;
                        System.Threading.Monitor.PulseAll(pngLock);
                        if (released) png.Dispose(); // released while it was being prepared
                    }
                }
            });
            SetData(DataFormats.Bitmap, true, image);
            SetData("PNG", false, png);
            // Screenshots often contain private data: never sync them to other devices through the cloud clipboard.
            SetData("CanUploadToCloudClipboard", false, new MemoryStream(BitConverter.GetBytes(0)));
            // Already in the Win+V history: keep this copy out of it.
            if (fromCapture) SetData("CanIncludeInClipboardHistory", false, new MemoryStream(BitConverter.GetBytes(0)));
        }

        public override object GetData(string format, bool autoConvert)
        {
            // When the request arrived, not when the PNG was ready: a clipboard manager that reads every new copy at once
            // and waits for a large PNG is not a paste.
            double askedAfter = (DateTime.Now - since).TotalMilliseconds;
            // Asked before waiting: the app reading must still have the clipboard open.
            bool front = !used && askedAfter <= 1000 && format != null && !format.StartsWith("Can", StringComparison.Ordinal) && ReaderInFront();
            if (format == "PNG") lock (pngLock) { while (!pngReady) System.Threading.Monitor.Wait(pngLock, 10000); }
            object o = base.GetData(format, autoConvert);
            if (!used && o != null && CountsAsPaste(format, askedAfter, front))
            {
                used = true;
                if (onUsed != null) onUsed(this);
            }
            return o;
        }

        // Reads in the first second are the system and clipboard tools looking at a new copy; the Can* flags are
        // Windows asking about history and cloud sync.
        internal static bool CountsAsPaste(string format, double askedAfterMs)
        {
            return CountsAsPaste(format, askedAfterMs, false);
        }

        // readerInFront: the app reading is the one the user is in, so even an instant Ctrl+V right after the capture
        // counts. Missing it matters: browsers and Electron apps read through the Win32 clipboard, which keeps the first
        // copy it gets, so a missed paste never comes back.
        internal static bool CountsAsPaste(string format, double askedAfterMs, bool readerInFront)
        {
            return format != null && !format.StartsWith("Can", StringComparison.Ordinal) && (askedAfterMs > 1000 || readerInFront);
        }

        // The window that has the clipboard open belongs to the foreground app, or to its parent or child process
        // (WebView2 apps such as Teams read from a child process). Clipboard history and other tools run elsewhere.
        static bool ReaderInFront()
        {
            try
            {
                IntPtr reader = Native.GetOpenClipboardWindow(), front = Native.GetForegroundWindow();
                if (reader == IntPtr.Zero || front == IntPtr.Zero) return false;
                uint a, b;
                Native.GetWindowPid(reader, out a);
                Native.GetWindowPid(front, out b);
                if (a == 0 || b == 0 || a == OwnPid) return false;
                if (a == b) return true;
                return Native.ParentPid(a) == b || Native.ParentPid(b) == a;
            }
            catch { return false; }
        }

        static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        // Tests make the PNG slow to prepare.
        internal static int TestPngDelayMs { get; set; }

        // Deep pixel copy (memory to memory, a few ms even for huge captures).
        internal static Bitmap DeepCopy(Bitmap src)
        {
            PixelFormat f = src.PixelFormat;
            if (f != PixelFormat.Format32bppArgb && f != PixelFormat.Format32bppPArgb && f != PixelFormat.Format32bppRgb)
            {
                Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b)) g.DrawImageUnscaled(src, 0, 0);
                return b;
            }
            Rectangle r = new Rectangle(0, 0, src.Width, src.Height);
            Bitmap dst = new Bitmap(src.Width, src.Height, f);
            BitmapData s = src.LockBits(r, ImageLockMode.ReadOnly, f);
            BitmapData d = dst.LockBits(r, ImageLockMode.WriteOnly, f);
            try
            {
                for (int y = 0; y < r.Height; y++)
                    Native.CopyMemory(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), new IntPtr(s.Scan0.ToInt64() + (long)y * s.Stride), new UIntPtr((uint)(r.Width * 4)));
            }
            finally
            {
                src.UnlockBits(s);
                dst.UnlockBits(d);
            }
            return dst;
        }

        // Doesn't wait for the PNG: if it's still being prepared, the worker releases it.
        public void Release()
        {
            lock (pngLock)
            {
                released = true;
                if (pngReady) png.Dispose();
            }
            image.Dispose();
        }
    }

}

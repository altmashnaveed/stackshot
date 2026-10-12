// Stackshot - Frame strip of the video timeline: a few thumbnails, each with its own fast seek.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Stackshot
{
    internal static class FrameStrip
    {
        // Per thumbnail: a seek costs the same at minute 1 or minute 30, unlike decoding the whole file.
        internal const int TimeoutMs = 15000;

        // The middle of each of n equal slots.
        internal static double[] Times(double duration, int n)
        {
            double[] t = new double[n];
            for (int i = 0; i < n; i++) t[i] = Math.Max(0, duration) * (i + 0.5) / n;
            return t;
        }

        internal static string Arguments(string src, double t, int height, string png, bool accurate)
        {
            // Without accurate seeking FFmpeg shows the keyframe at or before t, which is plenty for a strip and avoids
            // decoding up to t.
            return "-hide_banner -loglevel error -y " + (accurate ? "" : "-noaccurate_seek ") + PlayerArgs.Input(src, t) + " -an -sn -frames:v 1 -vf scale=-2:" + height + " " + Recorder.Quote(png);
        }

        // Extracts n thumbnails, a few FFmpegs at a time, and hands each to 'got' (on a worker thread) as soon as it
        // exists; a slot that fails stays empty. Returns when every slot is done or 'stop' says so.
        internal static void Extract(string ff, string src, double duration, int n, int height, Action<int, Bitmap> got, Func<bool> stop)
        {
            double[] times = Times(duration, n);
            string dir = Path.Combine(Path.GetTempPath(), "stackshot-strip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            int next = -1;
            int workers = Math.Max(2, Math.Min(4, Environment.ProcessorCount / 2));
            Thread[] threads = new Thread[Math.Min(workers, n)];
            try
            {
                for (int w = 0; w < threads.Length; w++)
                {
                    threads[w] = new Thread(delegate()
                    {
                        Power.OptOutThisThread();
                        while (true)
                        {
                            int i = Interlocked.Increment(ref next);
                            if (i >= n || (stop != null && stop())) return;
                            Bitmap b = null;
                            try { b = One(ff, src, times[i], height, Path.Combine(dir, "t" + i.ToString("00") + ".png")); }
                            catch (Exception ex) { ShotStack.Log("Tira de fotogramas: " + ex.Message); }
                            if (b != null) got(i, b);
                        }
                    });
                    threads[w].IsBackground = true;
                    threads[w].Start();
                }
                foreach (Thread t in threads) t.Join();
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        static Bitmap One(string ff, string src, double t, int height, string png)
        {
            string err;
            VideoExport.Run(ff, Arguments(src, t, height, png, false), TimeoutMs, out err);
            // Some files can't seek to a keyframe there (or have none after it): decode up to the time instead.
            if (!File.Exists(png)) VideoExport.Run(ff, Arguments(src, t, height, png, true), TimeoutMs, out err);
            if (!File.Exists(png)) return null;
            try { return ShotStack.LoadFull(png); }
            finally { try { File.Delete(png); } catch { } }
        }
    }
}

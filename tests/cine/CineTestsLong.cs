// Stress (random roundtrips and a 3 minute 1080p run; named explicitly) and bench (encoder throughput; named explicitly).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static void StressSuite()
        {
            Random rnd = new Random(20261009);
            int[][] rates = { new[] { 60, 1 }, new[] { 60000, 1001 }, new[] { 30, 1 } };
            string[] audios = { "none", "sys", "mic", "both" };
            for (int i = 1; i <= 20; i++)
            {
                int w = 2 * rnd.Next(8, 1281), h = 2 * rnd.Next(8, 1281);
                string pattern = Patterns[rnd.Next(Patterns.Length)];
                int[] rate = rates[rnd.Next(rates.Length)];
                string audio = audios[rnd.Next(audios.Length)];
                int frames = (int)Math.Min(40, Math.Max(3, 200000000L / ((long)w * h * 4)));
                string name = "random-" + i.ToString("00") + "-" + w + "x" + h + "-" + pattern + "-" + rate[0] + "-" + audio;
                RoundtripTest("stress", name, MakePlan(w, h, rate[0], rate[1], frames, pattern, audio, false));
            }

            Test("stress", "long-1080p60-3min", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("stress-long");
                try
                {
                    if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace < 25L << 30)
                        Skip("menos de 25 GB libres en la unidad de pruebas");
                    int w = 1920, h = 1080, bytes = w * h * 4;
                    byte[] managed = new byte[bytes];
                    IntPtr frame = Marshal.AllocHGlobal(bytes);
                    string nut = Path.Combine(dir, "video.nut"), master = Path.Combine(dir, "master.mkv");
                    CineSink sink = new CineSink(ffmpeg, w, h, 60, 1, nut);
                    Process me = Process.GetCurrentProcess();
                    List<long> privateMb = new List<long>();
                    int written = 0;
                    try
                    {
                        sink.Start();
                        Stopwatch clock = Stopwatch.StartNew();
                        long nextSample = 0;
                        while (clock.Elapsed.TotalSeconds < 180)
                        {
                            Paint("moving", managed, w, h, written, 7);
                            Marshal.Copy(managed, 0, frame, bytes);
                            if (!sink.Write(frame)) throw new TestFailed("Write fallo tras " + written + " fotogramas: " + sink.ErrorTail);
                            written++;
                            if (clock.ElapsedMilliseconds >= nextSample)
                            {
                                me.Refresh();
                                privateMb.Add(me.PrivateMemorySize64 >> 20);
                                nextSample += 10000;
                            }
                        }
                        int code;
                        if (!sink.Close(600000, out code) || code != 0) throw new TestFailed("ffmpeg no cerro bien: " + sink.ErrorTail);
                        Check(sink.Frames == written, "fotogramas " + sink.Frames + " de " + written);
                        Put("frames", written);
                        Put("privateMB", privateMb);
                        long first = privateMb.Count > 1 ? privateMb[1] : privateMb[0];
                        long peak = 0;
                        foreach (long mb in privateMb) peak = Math.Max(peak, mb);
                        Check(peak - first <= 200, "memoria privada crecio " + (peak - first) + " MB (inicio " + first + " MB, pico " + peak + " MB)");

                        List<CineAudioTrack> none = new List<CineAudioTrack>();
                        string err = CineMaster.Build(ffmpeg, nut, null, null, "", master, written / 60.0, null);
                        if (err != null) throw new TestFailed("Build: " + err);
                        CineExpect e = new CineExpect();
                        e.Width = w; e.Height = h; e.FpsNum = 60; e.FpsDen = 1;
                        e.FrameHashes = new List<ulong>(sink.Hashes);
                        e.Audio = none;
                        CineVerification v = CineVerifier.Verify(ffmpeg, master, e, null);
                        Check(v.Passed && v.FramesMatched == written && v.FramesDecoded == written,
                              "verificacion del master de 3 minutos: " + FailingChecks(v) + " coinciden " + v.FramesMatched + " de " + written);
                        Note(written + " fotogramas, memoria privada inicio " + first + " MB, pico " + peak + " MB");
                    }
                    finally
                    {
                        sink.Dispose();
                        Marshal.FreeHGlobal(frame);
                    }
                }
                finally { DeleteDir(dir); }
            });
        }

        // ---- bench ----

        static Bitmap uiStrip;

        // The docs screenshots stacked into one tall strip, to be scrolled.
        static Bitmap UiStrip()
        {
            if (uiStrip != null) return uiStrip;
            string[] names = { "region.png", "editor.png", "settings.png", "welcome.png", "stack.png" };
            List<Image> images = new List<Image>();
            foreach (string name in names)
            {
                string path = Path.Combine(repoDir, "docs", name);
                if (File.Exists(path)) images.Add(Image.FromFile(path));
            }
            if (images.Count == 0) Skip("no encuentro las capturas de docs\\ para el contenido de interfaz");
            try
            {
                int width = 0;
                foreach (Image img in images) width = Math.Max(width, img.Width);
                Bitmap strip = new Bitmap(width, 4400, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.Clear(Color.Black);
                    int y = 0;
                    while (y < 4400)
                        foreach (Image img in images)
                        {
                            g.DrawImage(img, 0, y, img.Width, img.Height);
                            y += img.Height;
                        }
                }
                uiStrip = strip;
                return strip;
            }
            finally
            {
                foreach (Image img in images) img.Dispose();
            }
        }

        // One bgr0 frame of the strip at vertical offset sy.
        static byte[] UiFrame(Bitmap strip, int w, int h, int sy)
        {
            using (Bitmap target = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(target))
                {
                    g.Clear(Color.Black);
                    int sw = Math.Min(w, strip.Width);
                    g.DrawImage(strip, new Rectangle(0, 0, sw, h), new Rectangle(0, sy, sw, h), GraphicsUnit.Pixel);
                }
                byte[] bytes = new byte[w * h * 4];
                BitmapData data = target.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * w * 4, w * 4);
                }
                finally { target.UnlockBits(data); }
                return bytes;
            }
        }

        static void BenchSuite()
        {
            int[][] sizes = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } };
            foreach (int[] sz in sizes)
            {
                int frames = sz[0] == 3840 ? 120 : 240;
                foreach (string content in new[] { "ui", "static", "noise" })
                    BenchTest(content, sz[0], sz[1], frames);
            }
        }

        static volatile bool benchStop;

        static void BenchTest(string content, int w, int h, int frames)
        {
            Test("bench", content + "-" + w + "x" + h, () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("bench");
                int bytes = w * h * 4;
                int ringSize = content == "static" ? 1 : 8;
                IntPtr[] ring = new IntPtr[ringSize];
                try
                {
                    Random rnd = new Random(w + h);
                    Bitmap strip = content == "ui" ? UiStrip() : null;
                    for (int i = 0; i < ringSize; i++)
                    {
                        byte[] data = content == "ui" ? UiFrame(strip, w, h, (i * 37) % (strip.Height - h)) : RandomBytes(rnd, bytes);
                        ring[i] = Marshal.AllocHGlobal(bytes);
                        Marshal.Copy(data, 0, ring[i], bytes);
                    }
                    string nut = Path.Combine(dir, "bench.nut");
                    CineSink sink = new CineSink(ffmpeg, w, h, 60, 1, nut);
                    double cpuSeconds = 0;
                    long peakMb = 0;
                    benchStop = false;
                    int me = Process.GetCurrentProcess().Id;
                    Thread sampler = new Thread(() =>
                    {
                        while (!benchStop)
                        {
                            List<int> kids = ChildPids(me, "ffmpeg.exe");
                            if (kids.Count > 0)
                            {
                                try
                                {
                                    using (Process p = Process.GetProcessById(kids[0]))
                                    {
                                        cpuSeconds = p.TotalProcessorTime.TotalSeconds;
                                        peakMb = Math.Max(peakMb, p.PeakWorkingSet64 >> 20);
                                    }
                                }
                                catch (Exception) { }
                            }
                            Thread.Sleep(300);
                        }
                    });
                    sink.Start();
                    sampler.Start();
                    Stopwatch clock = Stopwatch.StartNew();
                    try
                    {
                        for (int i = 0; i < frames; i++)
                            if (!sink.Write(ring[i % ringSize])) throw new TestFailed("Write fallo en " + i + ": " + sink.ErrorTail);
                        int code;
                        if (!sink.Close(900000, out code) || code != 0) throw new TestFailed("ffmpeg no cerro bien: " + sink.ErrorTail);
                    }
                    finally
                    {
                        benchStop = true;
                        sampler.Join(2000);
                        sink.Dispose();
                    }
                    double wall = clock.Elapsed.TotalSeconds;
                    double fps = frames / wall;
                    double mb = new FileInfo(nut).Length / 1048576.0;
                    Put("fps", Math.Round(fps, 1));
                    Put("atLeast60", fps >= 60);
                    Put("atLeast30", fps >= 30);
                    Put("fileMB", Math.Round(mb, 1));
                    Put("mbPerSecond", Math.Round(mb / wall, 1));
                    Put("ffmpegCpuSeconds", Math.Round(cpuSeconds, 1));
                    Put("ffmpegPeakWorkingSetMB", peakMb);
                    Note("encode " + fps.ToString("0.0", CultureInfo.InvariantCulture) + " fps (>=60: " + (fps >= 60 ? "si" : "no") +
                         ", >=30: " + (fps >= 30 ? "si" : "no") + "), " + mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB, " +
                         (mb / wall).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s, ffmpeg CPU " +
                         cpuSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, pico " + peakMb + " MB");
                }
                finally
                {
                    foreach (IntPtr p in ring) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                    DeleteDir(dir);
                }
            });
        }
    }
}

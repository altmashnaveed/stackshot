// Real FFmpeg runs on synthetic media: exports of RGB sources to MP4 and the frame strip of a long video.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static readonly int[] Green = { 32, 192, 64 };

        // A lossless RGB master like Cine writes (gbrp, libx264rgb -qp 0) of a known colour, with sound.
        static string MakeCineLike(string dir, double seconds)
        {
            string mkv = Path.Combine(dir, "Cine 1.mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x20C040:s=320x240:r=30:d=" + seconds + ",format=gbrp\" -f lavfi -i \"sine=f=440:d=" + seconds +
                 "\" -map 0:v -map 1:a -c:v libx264rgb -preset ultrafast -qp 0 -c:a flac -shortest " + Q(mkv), 60000);
            return mkv;
        }

        static ExportSpec FullFrame(string src, string output, bool rgb)
        {
            ExportSpec s = new ExportSpec();
            s.Source = src;
            s.Output = output;
            s.Crop = new Rectangle(0, 0, 320, 240);
            s.RgbSource = rgb;
            s.Level = 0;
            s.Fps = 30;
            return s;
        }

        static void ExportOk(ExportSpec s)
        {
            string err;
            int code = Ff(VideoExport.Arguments(s), out err, 120000);
            Check(code == 0 && File.Exists(s.Output), "export failed (" + code + "): " + Tail(err));
        }

        static void CheckTaggedAndDecodes(string mp4)
        {
            string probe = Probe(mp4);
            Check(probe.Contains("yuv420p(tv, bt709/bt709/iec61966-2-1"), "not tagged BT.709/sRGB: " + Tail(probe));
            FfOk("-hide_banner -loglevel error -i " + Q(mp4) + " -f null -", 60000);
        }

        static void ExportSuite()
        {
            Test("export", "cine-master-unedited-to-mp4", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("cine");
                string src = MakeCineLike(dir, 2);
                string probe = Probe(src);
                Check(VideoExport.IsRgb(src, probe), "the synthetic master must read as RGB: " + VideoExport.PixelFormat(probe));
                Check(VideoExport.NeedsTranscode(src, false), "an unedited master must export");
                ExportSpec s = FullFrame(src, Path.Combine(dir, "Cine 1 (editado).mp4"), true);
                s.HasAudio = true;
                ExportOk(s);
                CheckTaggedAndDecodes(s.Output);
                int[] c = PixelAt(s.Output, 160, 120, true);
                Add("color " + Rgb(c));
                Check(Diff(c, Green) <= 3, "colour " + Rgb(c) + " instead of " + Rgb(Green));
                Check(Probe(s.Output).Contains("Audio: aac"), "the sound is lost");
            });
            Test("export", "old-chain-would-shift-colour", delegate
            {
                // The control: without the conversion the same master decodes visibly wrong, so the test above means something.
                NeedFfmpeg();
                string dir = WorkDir("control");
                string src = MakeCineLike(dir, 1);
                ExportSpec s = FullFrame(src, Path.Combine(dir, "old.mp4"), false);
                ExportOk(s);
                int[] c = PixelAt(s.Output, 160, 120, true);
                Add("old chain " + Rgb(c));
                Check(Diff(c, Green) > 8, "expected a visible shift with BT.601, got " + Rgb(c));
            });
            Test("export", "backdrop-overlay-crop", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("overlay");
                string src = MakeCineLike(dir, 1);
                // Crop 200x160 at (40,40), padded into 280x220 with the video at (40,30); the overlay paints the margin and
                // one mark over the video.
                string top = Path.Combine(dir, "top.png");
                using (Bitmap b = new Bitmap(280, 220, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.FromArgb(255, 200, 50, 100));
                        g.SetClip(new Rectangle(40, 30, 200, 160));
                        g.Clear(Color.Transparent);
                        g.ResetClip();
                        using (SolidBrush ink = new SolidBrush(Color.FromArgb(255, 30, 60, 220))) g.FillRectangle(ink, 60, 50, 40, 40);
                    }
                    b.Save(top, ImageFormat.Png);
                }
                ExportSpec s = FullFrame(src, Path.Combine(dir, "overlay.mp4"), true);
                s.Crop = new Rectangle(40, 40, 200, 160);
                s.Backdrop = true;
                s.Frame = new Size(280, 220);
                s.Inner = new Rectangle(40, 30, 200, 160);
                s.Overlay = top;
                ExportOk(s);
                CheckTaggedAndDecodes(s.Output);
                int[] video = PixelAt(s.Output, 180, 140, true), margin = PixelAt(s.Output, 10, 10, true), mark = PixelAt(s.Output, 80, 70, true);
                Add("video " + Rgb(video) + " margin " + Rgb(margin) + " mark " + Rgb(mark));
                Check(Diff(video, Green) <= 3, "video colour " + Rgb(video));
                Check(Diff(margin, new int[] { 200, 50, 100 }) <= 4, "backdrop colour " + Rgb(margin));
                Check(Diff(mark, new int[] { 30, 60, 220 }) <= 4, "mark colour " + Rgb(mark));
                Check(Probe(s.Output).Contains("280x220"), "output size");
            });
            Test("export", "gif-to-mp4", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("gif");
                string gif = Path.Combine(dir, "clip.gif");
                // Its own palette and no dithering, so the frame is one flat colour (dither would make 4:2:0 average it).
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x20C040:s=320x240:r=15:d=1\" -vf \"split[a][b];[a]palettegen[p];[b][p]paletteuse=dither=none\" " +
                     Q(gif), 60000);
                int[] want = PixelAt(gif, 160, 120, false);
                Check(VideoExport.IsRgb(gif, Probe(gif)), "GIF is RGB");
                ExportSpec s = FullFrame(gif, Path.Combine(dir, "clip.mp4"), true);
                s.SourceIsGif = true;
                ExportOk(s);
                CheckTaggedAndDecodes(s.Output);
                int[] c = PixelAt(s.Output, 160, 120, true);
                Add("gif " + Rgb(want) + " -> mp4 " + Rgb(c));
                Check(Diff(c, want) <= 3, "colour " + Rgb(c) + " instead of " + Rgb(want));
            });
            Test("export", "trim-cuts-speed-scale", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("trim");
                string src = MakeCineLike(dir, 4);
                ExportSpec s = FullFrame(src, Path.Combine(dir, "trim.mp4"), true);
                s.HasAudio = true;
                s.Keep = new List<double[]> { new double[] { 0.5, 2.5 }, new double[] { 3, 4 } };
                s.Speed = 2;
                s.MaxHeight = 120;
                ExportOk(s);
                CheckTaggedAndDecodes(s.Output);
                string probe = Probe(s.Output);
                double d = DurationOf(probe);
                Add("duration " + d.ToString("0.00"));
                Check(Math.Abs(d - 1.5) < 0.2, "3 s kept at 2x should last 1.5 s, got " + d);
                Check(probe.Contains("160x120"), "scaled to 120 px tall: " + Tail(probe));
                Check(probe.Contains("Audio: aac"), "sound kept");
                int[] c = PixelAt(s.Output, 80, 60, true);
                Check(Diff(c, Green) <= 3, "colour after scale " + Rgb(c));
            });
            Test("export", "mp4-to-gif-colour", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("togif");
                string mp4 = Path.Combine(dir, "rec.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x20C040:s=320x240:r=30:d=1,format=gbrp\" -vf \"" + VideoExport.RgbToYuv + ",format=yuv420p," +
                     Recorder.Tags + "\" -c:v libx264 -crf 12 " + Q(mp4), 60000);
                ExportSpec s = FullFrame(mp4, Path.Combine(dir, "rec.gif"), false);
                s.ToGif = true;
                ExportOk(s);
                int[] c = PixelAt(s.Output, 160, 120, false);
                Add("gif " + Rgb(c));
                Check(Diff(c, Green) <= 4, "GIF colour " + Rgb(c));
            });
        }

        static void StripSuite()
        {
            Test("strip", "twenty-minutes-quickly", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("strip");
                string src = Path.Combine(dir, "long.mp4");
                const double minutes = 20;
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=192x108:r=10:d=" + (minutes * 60) + "\" -c:v libx264 -preset ultrafast -g 50 " + Q(src), 300000);
                double dur = DurationOf(Probe(src));
                Check(dur > minutes * 60 - 2, "synthetic video too short: " + dur);
                Bitmap[] thumbs = new Bitmap[14];
                int got = 0;
                Stopwatch sw = Stopwatch.StartNew();
                FrameStrip.Extract(ffmpeg, src, dur, 14, 72, delegate(int i, Bitmap b) { lock (thumbs) { thumbs[i] = b; got++; } }, null);
                sw.Stop();
                try
                {
                    Add(got + "/14 in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
                    Check(got == 14, "only " + got + " thumbnails");
                    foreach (Bitmap b in thumbs) Check(b != null && b.Height == 72, "thumbnail size");
                    Check(sw.Elapsed.TotalSeconds < 20, "too slow: " + sw.Elapsed.TotalSeconds + " s");
                    // testsrc2 moves over time: the first and last thumbnails must differ (each one really seeked).
                    int differ = 0;
                    for (int y = 0; y < 72; y += 4)
                        for (int x = 0; x < thumbs[0].Width; x += 4)
                            if (thumbs[0].GetPixel(x, y).ToArgb() != thumbs[13].GetPixel(x, y).ToArgb()) differ++;
                    Check(differ > 20, "first and last thumbnails look the same");
                }
                finally { foreach (Bitmap b in thumbs) if (b != null) b.Dispose(); }
            });
            Test("strip", "stops-when-asked", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("stop");
                string src = Path.Combine(dir, "short.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=192x108:r=10:d=20\" -c:v libx264 -preset ultrafast " + Q(src), 60000);
                int got = 0;
                FrameStrip.Extract(ffmpeg, src, 20, 14, 72, delegate(int i, Bitmap b) { Interlocked.Increment(ref got); b.Dispose(); }, delegate { return true; });
                Check(got == 0, "closed editor still extracted " + got);
            });
        }
    }
}

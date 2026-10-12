// Adversarial tests of the built-in player: seeks at the edges, lifecycle races, odd files, colour, process counts
// and a 10 s 1080p60 run. Headless, with the fake sound sink. Invoked from PlayerSuite.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        // Sound that refuses every block: the audio decoder ends at once and the clock runs on the Stopwatch.
        // OpenAfterDispose counts a device opened after Dispose, which would be left open.
        sealed class EdgeSink : ISoundOut
        {
            readonly object gate = new object();
            public bool Disposed;
            public int Opens, OpenAfterDispose, Writes;

            public bool Open(int rate, int channels)
            {
                lock (gate)
                {
                    Opens++;
                    if (Disposed) OpenAfterDispose++;
                }
                return true;
            }

            public bool Write(float[] samples, int count)
            {
                lock (gate) Writes++;
                return false;
            }

            public long Position { get { return 0; } }
            public void Pause() { }
            public void Resume() { }
            public void Reset() { }
            public void Dispose() { lock (gate) Disposed = true; }
        }

        sealed class EdgePixel
        {
            public int[] Rgb;
            public string Error;
        }

        // A testsrc2 MP4 with a video of videoSeconds and a sine of audioSeconds (no -shortest: the output keeps the longer).
        static string EdgeMp4(string name, double videoSeconds, double audioSeconds)
        {
            string path = Path.Combine(WorkDir("edge-mp4"), name);
            string v = videoSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            string a = audioSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=" + v + "\" -f lavfi -i \"sine=f=440:d=" + a +
                 "\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 128k " + Q(path), 60000);
            return path;
        }

        // A 0.2 s yuv420p patch of one colour. mode 0: BT.709 matrix and BT.709 tags; mode 1: BT.601 matrix and BT.601
        // tags; mode 2: BT.709 matrix with no tags at all.
        static string EdgeColour(string name, string hex, int mode)
        {
            string path = Path.Combine(WorkDir("edge-colour"), name + ".mp4");
            string matrix = mode == 1 ? "bt601" : "bt709";
            string tags = mode == 0 ? " -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv"
                        : mode == 1 ? " -colorspace bt470bg -color_primaries smpte170m -color_trc smpte170m -color_range tv" : "";
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x" + hex + ":s=320x180:r=30:d=0.2\" -vf \"scale=out_color_matrix=" +
                 matrix + ":out_range=tv,format=yuv420p\" -c:v libx264 -preset ultrafast -crf 12 -pix_fmt yuv420p" + tags + " " + Q(path), 60000);
            return path;
        }

        // The centre pixel of the first decoded frame, as RGB.
        static int[] EdgeFirstPixel(string path)
        {
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay && mi.PixelFormat == "yuv420p", "not a playable yuv420p file: " + mi.PixelFormat);
            EdgePixel px = new EdgePixel();
            Playback p = new Playback(ffmpeg, path, mi, new PlFakeSound());
            try
            {
                p.FrameReady += delegate(PlayerFrame f)
                {
                    lock (px)
                    {
                        if (px.Rgb != null) return;
                        int i = ((f.Height / 2) * f.Width + f.Width / 2) * 4;
                        // The frame is BGRA.
                        px.Rgb = new int[] { f.Data[i + 2], f.Data[i + 1], f.Data[i] };
                    }
                };
                p.Error += delegate(string msg) { lock (px) px.Error = msg; };
                p.Start(0, 0);
                PlWait(delegate { lock (px) return px.Rgb != null || px.Error != null; }, 8000, "a first frame");
                lock (px) Check(px.Error == null, "player error: " + px.Error);
                return px.Rgb;
            }
            finally { p.Dispose(); }
        }

        static int[] EdgeWant(string hex)
        {
            return new int[] { Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16) };
        }

        static string EdgeRgb(int[] c) { return "(" + c[0] + "," + c[1] + "," + c[2] + ")"; }

        static int EdgeFfmpegCount()
        {
            Process[] ps = Process.GetProcessesByName("ffmpeg");
            foreach (Process pr in ps) pr.Dispose();
            return ps.Length;
        }

        // Waits for count frames after mark and checks that they show first, first + 1, ...
        static void EdgeFrom(PlLog log, int mark, int first, int count, string what)
        {
            PlWait(delegate { return log.Count >= mark + count || log.Errors.Count > 0; }, 8000, what);
            PlOk(log);
            Check(log.Count >= mark + count, what + ": " + (log.Count - mark) + " frames");
            for (int i = 0; i < count; i++)
                Check(log.Index[mark + i] == first + i, what + ": frame " + i + " shows " + log.Index[mark + i] + ", want " + (first + i));
        }

        static void PlayerEdgeSuite()
        {
            Test("player", "edge-seek-zero-negative-duration-nan", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 3; }, 8000, "first frames");
                    p.Seek(0);
                    EdgeFrom(log, log.Count, 0, 3, "seek to 0");
                    p.Seek(-3);
                    int mark = log.Count;
                    EdgeFrom(log, mark, 0, 3, "seek to -3");
                    // Exactly the duration while playing: the end, with no picture after it.
                    int ended = log.EndedCount;
                    p.Seek(p.Info.Duration);
                    PlWait(delegate { return log.EndedCount > ended; }, 8000, "the end at the duration");
                    int at = log.Count;
                    Thread.Sleep(150);
                    PlOk(log);
                    Check(log.Count == at, "frames after the end at the duration: " + (log.Count - at));
                    Check(p.State == PlayState.Ended, "state after the duration: " + p.State);
                    Add("seek 0, -3 and duration ok");
                }
                finally { p.Dispose(); }

                // Start at the duration, and Seek(+infinity), end at once without a picture.
                PlLog el = new PlLog();
                Playback e = PlNew(m.Path, new PlFakeSound(), el);
                try
                {
                    e.Start(m.Seconds, 0);
                    PlWait(delegate { return el.EndedCount > 0 || el.Errors.Count > 0; }, 8000, "the end when starting at the duration");
                    PlOk(el);
                    Check(el.Count == 0, "frames when starting at the duration: " + el.Count);
                }
                finally { e.Dispose(); }

                // NaN: must not hang, must not report a player error, must not leave the position NaN.
                List<string> nanFails = new List<string>();
                foreach (int mode in new int[] { 0, 1 })
                {
                    string what = mode == 0 ? "Seek(NaN)" : "Start(NaN)";
                    PlLog nl = new PlLog();
                    Playback n = PlNew(m.Path, new PlFakeSound(), nl);
                    try
                    {
                        if (mode == 0)
                        {
                            n.Start(0, 0);
                            PlWait(delegate { return nl.Count >= 3; }, 8000, "frames before NaN");
                            n.Seek(double.NaN);
                        }
                        else n.Start(double.NaN, 0);
                        int nm = nl.Count;
                        PlWait(delegate { return nl.Count >= nm + 3 || nl.Errors.Count > 0; }, 8000, "frames after NaN");
                        if (nl.Errors.Count > 0) nanFails.Add(what + " player error: " + nl.Errors[0].Replace("\n", " "));
                        else if (nl.Index[nm] != 0) nanFails.Add(what + " shows frame " + nl.Index[nm] + ", want 0");
                        if (double.IsNaN(n.Position)) nanFails.Add(what + " left the position NaN");
                    }
                    catch (TestFailed f) { nanFails.Add(what + ": " + f.Message); }
                    finally { n.Dispose(); }
                }
                Check(nanFails.Count == 0, string.Join(" | ", nanFails));
            });

            Test("player", "edge-fifty-rapid-seeks", delegate
            {
                PlMaster m = PlSharedMaster();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, new PlFakeSound(), log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 2; }, 8000, "first frames");
                    for (int i = 0; i < 49; i++) p.Seek(((i * 37) % 90) / 30.0);
                    p.Seek(1.2);
                    int mark = log.Count;
                    EdgeFrom(log, mark, 36, 10, "after 50 seeks");
                    Add("ffmpeg started for the 50 seeks: " + p.ProcessIds().Length);
                }
                finally { p.Dispose(); }
            });

            Test("player", "edge-dispose-while-starting", delegate
            {
                PlMaster m = PlSharedMaster();
                MediaInfo mi = MediaInfo.Probe(ffmpeg, m.Path);
                int[] delays = { 0, 1, 2, 4, 8, 16, 30 };
                int cases = 0, opens = 0;
                foreach (int d in delays)
                {
                    for (int rep = 0; rep < 3; rep++)
                    {
                        EdgeSink sink = new EdgeSink();
                        PlLog log = new PlLog();
                        Playback p = new Playback(ffmpeg, m.Path, mi, sink);
                        p.FrameReady += delegate { lock (log) log.Index.Add(0); };
                        p.Start(0, 0);
                        if (d > 0) Thread.Sleep(d);
                        Stopwatch sw = Stopwatch.StartNew();
                        p.Dispose();
                        long ms = sw.ElapsedMilliseconds;
                        Check(ms < 2000, "Dispose after " + d + " ms took " + ms + " ms");
                        PlDead(p, 1500);
                        int c = log.Count;
                        Thread.Sleep(100);
                        Check(log.Count == c, "frame after Dispose (delay " + d + " ms)");
                        Check(sink.Disposed, "sound not disposed (delay " + d + " ms)");
                        Check(sink.OpenAfterDispose == 0, "sound opened after Dispose (delay " + d + " ms)");
                        cases++;
                        opens += sink.Opens;
                    }
                }
                Add(cases + " cases; sound opened " + opens + " times in all");
            });

            Test("player", "edge-dispose-twice-and-use-after", delegate
            {
                PlMaster m = PlSharedMaster();
                MediaInfo mi = MediaInfo.Probe(ffmpeg, m.Path);
                Playback never = new Playback(ffmpeg, m.Path, mi, new PlFakeSound());
                never.Dispose();
                never.Dispose();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, new PlFakeSound(), log);
                p.Start(0, 0);
                PlWait(delegate { return log.Count >= 2; }, 8000, "frames");
                p.Dispose();
                Stopwatch sw = Stopwatch.StartNew();
                p.Dispose();
                long ms = sw.ElapsedMilliseconds;
                Check(ms < 100, "second Dispose took " + ms + " ms");
                int procs = p.ProcessIds().Length, c = log.Count;
                p.Start(0, 0);
                p.Seek(1);
                p.Resume();
                p.Pause();
                p.Stop();
                p.Track = 1;
                p.Volume = 0.5;
                p.SetDecodeSize(100, 100);
                p.EndTime = 2;
                Thread.Sleep(200);
                Check(p.ProcessIds().Length == procs, "ffmpeg started after Dispose: " + (p.ProcessIds().Length - procs));
                Check(log.Count == c, "frames after Dispose: " + (log.Count - c));
            });

            Test("player", "edge-pause-before-first-frame", delegate
            {
                PlMaster m = PlSharedMaster();
                int[] delays = { 0, 1, 5, 15, 40 };
                foreach (int d in delays)
                {
                    PlLog log = new PlLog();
                    Playback p = PlNew(m.Path, new PlFakeSound(), log);
                    try
                    {
                        p.Start(0, 0);
                        if (d > 0) Thread.Sleep(d);
                        p.Pause();
                        Thread.Sleep(30);
                        int mark = log.Count;
                        Thread.Sleep(600);
                        PlOk(log);
                        Check(log.Count == mark, "delay " + d + " ms: " + (log.Count - mark) + " frames while paused");
                        Check(p.State == PlayState.Paused, "delay " + d + " ms: state " + p.State);
                        double pos = p.Position;
                        Check(pos >= 0 && pos <= 0.1, "delay " + d + " ms: paused at " + pos);
                        p.Resume();
                        PlWait(delegate { return log.Count >= mark + 5 || log.Errors.Count > 0; }, 8000, "frames after resume");
                        PlOk(log);
                        Check(log.Index[mark] <= 1, "delay " + d + " ms: first frame after resume shows " + log.Index[mark]);
                    }
                    finally { p.Dispose(); }
                }
                Add("pause at 0, 1, 5, 15, 40 ms ok");
            });

            Test("player", "edge-track-out-of-range", delegate
            {
                PlMaster m = PlSharedMaster();
                int[] tracks = { -1, 3, 99 };
                foreach (int tr in tracks)
                {
                    PlFakeSound snd = new PlFakeSound();
                    PlLog log = new PlLog();
                    Playback p = PlNew(m.Path, snd, log);
                    try
                    {
                        p.Track = tr;
                        p.Start(0, 0);
                        PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end with track " + tr);
                        PlOk(log);
                        Check(log.Count == m.Frames, "track " + tr + ": " + log.Count + " frames of " + m.Frames);
                        Check(snd.WriteCount == 0, "track " + tr + " wrote sound: " + snd.WriteCount);
                    }
                    finally { p.Dispose(); }
                }
                Add("tracks -1, 3, 99: silent and complete");
            });

            Test("player", "edge-audio-and-video-lengths-differ", delegate
            {
                NeedFfmpeg();
                string shortSound = EdgeMp4("short-sound.mp4", 3, 1);
                string longSound = EdgeMp4("long-sound.mp4", 1, 3);
                PlLog log = new PlLog();
                PlFakeSound snd = new PlFakeSound();
                Playback p = PlNew(shortSound, snd, log);
                try
                {
                    Check(p.Info.Audio.Count == 1, "audio tracks " + p.Info.Audio.Count);
                    Check(Math.Abs(p.Info.Duration - 3.0) < 0.1, "duration " + p.Info.Duration);
                    Stopwatch sw = Stopwatch.StartNew();
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 20000, "the end of the 3 s picture");
                    PlOk(log);
                    long ms = sw.ElapsedMilliseconds;
                    Check(log.Count == 90, "3 s picture with 1 s sound: " + log.Count + " frames");
                    Check(snd.WriteCount > 0, "no sound written");
                    Check(ms > 2600 && ms < 6000, "the 3 s picture took " + ms + " ms");
                    Check(Math.Abs(p.Position - 3.0) < 0.15, "position at the end " + p.Position);
                    Add("sound ends at 1 s: " + log.Count + " frames in " + ms + " ms");
                }
                finally { p.Dispose(); }

                PlLog l2 = new PlLog();
                Playback q = PlNew(longSound, new PlFakeSound(), l2);
                try
                {
                    q.Start(0, 0);
                    PlWait(delegate { return l2.EndedCount > 0 || l2.Errors.Count > 0; }, 20000, "the end of the 1 s picture");
                    PlOk(l2);
                    Check(l2.Count == 30, "1 s picture with 3 s sound: " + l2.Count + " frames");
                    Add("sound lasts 3 s: picture ends at " + q.Position.ToString("0.00", CultureInfo.InvariantCulture) + " s");
                }
                finally { q.Dispose(); }
            });

            Test("player", "edge-one-frame-file", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-one");
                string silent = Path.Combine(dir, "one-silent.mp4");
                string sound = Path.Combine(dir, "one-sound.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30\" -frames:v 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an " +
                     Q(silent), 60000);
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30\" -f lavfi -i \"sine=f=440:d=0.05\" -frames:v 1 -c:v libx264 " +
                     "-preset ultrafast -pix_fmt yuv420p -c:a aac " + Q(sound), 60000);
                foreach (string f in new string[] { silent, sound })
                {
                    string name = Path.GetFileName(f);
                    MediaInfo mi = MediaInfo.Probe(ffmpeg, f);
                    Check(mi.CanPlay && mi.Width == 320, name + " is not playable: " + mi.Width + "x" + mi.Height + " " + mi.Fps);
                    PlLog log = new PlLog();
                    Playback p = PlNew(f, new PlFakeSound(), log);
                    try
                    {
                        p.Start(0, 0);
                        PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 8000, "the end of " + name);
                        PlOk(log);
                        // testsrc2 carries no frame index, so only the count is checked here.
                        Check(log.Count == 1, name + ": " + log.Count + " frames");
                        Check(p.FramesDecoded == 1, name + ": decoded " + p.FramesDecoded);
                    }
                    finally { p.Dispose(); }

                    // Starting past the only frame ends at once with no picture.
                    PlLog l2 = new PlLog();
                    Playback q = PlNew(f, new PlFakeSound(), l2);
                    try
                    {
                        q.Start(0.5, 0);
                        PlWait(delegate { return l2.EndedCount > 0 || l2.Errors.Count > 0; }, 8000, "the end when starting past " + name);
                        PlOk(l2);
                        Check(l2.Count == 0, name + " past its only frame: " + l2.Count + " frames");
                    }
                    finally { q.Dispose(); }
                }
            });

            Test("player", "edge-yuv420p-bt709-colour-patches", delegate
            {
                NeedFfmpeg();
                string[] hex = { "FF0000", "00FF00", "0000FF", "1E90FF", "FF8000", "C87840", "808080", "FFFFFF", "000000" };
                // mode 0 (BT.709 tagged) and mode 1 (BT.601 tagged) must decode within 4 levels; mode 2 is only measured.
                int[] modes = { 0, 1, 2 };
                List<string> bad = new List<string>();
                foreach (int mode in modes)
                {
                    int worst = 0;
                    string worstHex = "";
                    foreach (string h in hex)
                    {
                        int[] got = EdgeFirstPixel(EdgeColour("m" + mode + "-" + h, h, mode));
                        int[] want = EdgeWant(h);
                        int dev = 0;
                        for (int c = 0; c < 3; c++) dev = Math.Max(dev, Math.Abs(got[c] - want[c]));
                        if (dev > worst) { worst = dev; worstHex = h; }
                        if (dev > 4 && mode < 2) bad.Add("mode " + mode + " " + h + " got " + EdgeRgb(got) + " want " + EdgeRgb(want));
                    }
                    Add("mode " + mode + " worst " + worst + " at " + worstHex);
                }
                Check(bad.Count == 0, "patches off by more than 4 levels: " + string.Join("; ", bad));
            });

            Test("player", "edge-ffmpeg-count-no-leaks", delegate
            {
                PlMaster m = PlSharedMaster();
                int before = EdgeFfmpegCount();
                List<int> all = new List<int>();
                for (int k = 0; k < 10; k++)
                {
                    PlLog log = new PlLog();
                    Playback p = PlNew(m.Path, new PlFakeSound(), log);
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 2; }, 8000, "frames");
                    p.Seek(1.0);
                    p.Stop();
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 4; }, 8000, "frames again");
                    p.Dispose();
                    PlDead(p, 1500);
                    all.AddRange(p.ProcessIds());
                }
                Thread.Sleep(300);
                int after = EdgeFfmpegCount();
                Check(after <= before, "ffmpeg.exe processes before " + before + ", after " + after);
                Add(all.Count + " ffmpeg started in 10 cycles, all dead; ffmpeg.exe processes before " + before + ", after " + after);
            });

            Test("player", "edge-cut-ends-off-the-frame-grid", delegate
            {
                PlMaster m = PlSharedMaster();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, new PlFakeSound(), log);
                try
                {
                    // A removed piece [1.0, 1.505) ends between frame 45 (1.500) and frame 46 (1.533).
                    p.Skip = delegate(double t) { return t >= 1.0 && t < 1.505 ? 1.505 : t; };
                    Stopwatch sw = Stopwatch.StartNew();
                    p.Start(0, 0);
                    while (sw.ElapsedMilliseconds < 15000 && log.EndedCount == 0 && log.Errors.Count == 0) Thread.Sleep(20);
                    StringBuilder seen = new StringBuilder();
                    lock (log) foreach (int i in log.Index) seen.Append(i).Append(' ');
                    Add("state " + p.State + ", position " + p.Position.ToString("0.000", CultureInfo.InvariantCulture) + ", shown " + p.FramesShown +
                        ", decoded " + p.FramesDecoded + ", ffmpeg started " + p.ProcessIds().Length + ", indexes " + seen.ToString().Trim());
                    PlOk(log);
                    Check(log.EndedCount > 0, "no end after 15 s");
                    StringBuilder inside = new StringBuilder();
                    int n = 0;
                    foreach (int i in log.Index)
                    {
                        if (i >= 30 && i <= 45) { n++; inside.Append(i).Append(' '); }
                    }
                    Check(n == 0, n + " frames from the removed piece shown: " + inside.ToString().Trim());
                    Add("shown " + log.Count + " frames, ffmpeg started " + p.ProcessIds().Length + ", " + sw.ElapsedMilliseconds + " ms");
                }
                finally { p.Dispose(); }
            });

            Test("player", "edge-trim-in-off-grid-plays-on", delegate
            {
                PlMaster m = PlSharedMaster();
                // Control first: a trimmed In on the frame grid (1.500 s = frame 45), then one off it (1.505 s).
                foreach (double start in new double[] { 1.5, 1.505 })
                {
                    double s0 = start;
                    PlLog log = new PlLog();
                    Playback p = PlNew(m.Path, new PlFakeSound(), log);
                    try
                    {
                        // Nothing before In is played (what Editor.cs does through Timeline.PlayableFrom).
                        p.Skip = delegate(double t) { return t < s0 ? s0 : t; };
                        p.Start(s0, 0);
                        Stopwatch sw = Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 10000 && log.EndedCount == 0 && log.Errors.Count == 0) Thread.Sleep(20);
                        PlOk(log);
                        Add("In " + s0.ToString("0.000", CultureInfo.InvariantCulture) + ": state " + p.State + ", position " +
                            p.Position.ToString("0.000", CultureInfo.InvariantCulture) + ", shown " + log.Count + ", ffmpeg started " + p.ProcessIds().Length);
                        Check(log.EndedCount > 0, "In at " + s0.ToString("0.000", CultureInfo.InvariantCulture) + ": no end after 10 s, still " + p.State +
                              " at " + p.Position.ToString("0.000", CultureInfo.InvariantCulture));
                    }
                    finally { p.Dispose(); }
                }
            });

            Test("player", "edge-1080p60-ten-seconds-fake-sink", delegate
            {
                NeedFfmpeg();
                PlMaster big = PlBuild(1920, 1080, 60, 600, true);
                PlLog log = new PlLog();
                Playback p = PlNew(big.Path, new PlFakeSound(), log);
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 60000, "the end of 10 s");
                    PlOk(log);
                    long ms = sw.ElapsedMilliseconds;
                    for (int i = 1; i < log.Count; i++)
                        Check(log.Index[i] > log.Index[i - 1], "frame " + i + " shows " + log.Index[i] + " after " + log.Index[i - 1]);
                    long shown = p.FramesShown, dropped = p.FramesDropped, decoded = p.FramesDecoded;
                    Check(decoded == 600, "decoded " + decoded + " of 600");
                    Check(log.Count == shown, "callbacks " + log.Count + " vs shown " + shown);
                    Check(shown >= 540, "shown " + shown + " of 600 (dropped " + dropped + ")");
                    Add("decoded " + decoded + ", shown " + shown + ", dropped " + dropped + ", played in " + ms + " ms");
                }
                finally { p.Dispose(); }
            });
        }
    }
}

// Tests of the player window and of opening any video: playback speed, loop, frame steps, the extension list and
// the probe fallback, the scaling flag, the window headless and the routing of Open. Headless, fake sound sink.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        sealed class PwStills
        {
            public readonly List<int> Index = new List<int>(), Width = new List<int>(), Height = new List<int>();
            public int Count { get { lock (this) return Index.Count; } }
        }

        // A short clip in a given container, made from lavfi: 320x180, 30 fps, d seconds, with a sine unless noAudio.
        // Returns null when this FFmpeg has no encoder for it.
        static string PwClip(string ext, string codecs, double d, bool noAudio, string mux)
        {
            string path = Path.Combine(WorkDir("pw-clip"), "clip" + ext);
            string secs = d.ToString("0.###", CultureInfo.InvariantCulture);
            string err;
            int code = Ff("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=" + secs + "\"" +
                          (noAudio ? "" : " -f lavfi -i \"sine=f=440:d=" + secs + "\"") + " " + codecs + (noAudio ? " -an" : " -shortest") + " " + (mux ?? "") + " " + Q(path), out err, 60000);
            if (code != 0) { Add("no encoder for " + ext + ": " + Tail(err)); return null; }
            return path;
        }

        // Plays a file to its end on the real clock and returns how many frames the decoder produced.
        static int PwPlayAll(string path, PlLog log, out Playback outPlayback)
        {
            PlFakeSound snd = new PlFakeSound();
            Playback p = PlNew(path, snd, log);
            outPlayback = p;
            p.Start(0, 0);
            PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 20000, "the end of " + Path.GetFileName(path));
            PlOk(log);
            return (int)p.FramesDecoded;
        }

        // The media time between picture i and the one before is a whole number (at least 1) of steps.
        static bool PwStepOk(PlLog log, int i, double step)
        {
            double r = (log.Time[i] - log.Time[i - 1]) / step;
            return Math.Round(r) >= 1 && Math.Abs(r - Math.Round(r)) < 1e-4;
        }

        static PlayerWindow PwWindow(string path, PlFakeSound snd, Settings st)
        {
            return new PlayerWindow(null, st, ffmpeg, path, delegate { return snd; });
        }

        static bool PwKey(PlayerWindow w, Keys k)
        {
            MethodInfo mi = typeof(PlayerWindow).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance);
            Message m = new Message();
            object[] args = new object[] { m, k };
            return (bool)mi.Invoke(w, args);
        }

        static void PwFormat(string name, string ext, string codecs, string mux, bool noAudio, int expectFrames)
        {
            Test("playerwindow", "format-" + name, delegate
            {
                NeedFfmpeg();
                string clip = PwClip(ext, codecs, 1.2, noAudio, mux);
                if (clip == null) Skip("this FFmpeg cannot write " + ext);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, clip);
                Check(mi.CanPlay, ext + " does not read as playable");
                Check(mi.Width == 320 && mi.Height == 180, ext + " size " + mi.Width + "x" + mi.Height);
                Check(ShotStack.IsVideoFile(clip), ext + " is not in the extension list");
                PlLog log = new PlLog();
                Playback p;
                int frames = PwPlayAll(clip, log, out p);
                try
                {
                    Check(Math.Abs(frames - expectFrames) <= 1, ext + " decoded " + frames + " frames, expected " + expectFrames);
                    Check(p.State == PlayState.Ended, "state " + p.State);
                    Check(log.Count >= expectFrames - 3, ext + " showed only " + log.Count + " frames");
                    Add(frames + " frames, " + mi.Audio.Count + " audio, probe fps " + mi.Fps);
                }
                finally { p.Dispose(); }
            });
        }

        static void PlayerWindowSuite()
        {
            Test("playerwindow", "speed-args", delegate
            {
                Dictionary<double, string> chain = new Dictionary<double, string>();
                chain[0.25] = "atempo=0.5,atempo=0.5";
                chain[0.5] = "atempo=0.5";
                chain[0.75] = "atempo=0.75";
                chain[1] = "";
                chain[1.25] = "atempo=1.25";
                chain[1.5] = "atempo=1.5";
                chain[2] = "atempo=2";
                chain[4] = "atempo=2,atempo=2";
                foreach (KeyValuePair<double, string> kv in chain)
                    Check(PlayerArgs.Tempo(kv.Key) == kv.Value, "tempo " + kv.Key + " = '" + PlayerArgs.Tempo(kv.Key) + "', expected '" + kv.Value + "'");
                string wl = PlayerArgs.Whitelist;
                string a = PlayerArgs.Audio("C:\\a b\\x.mkv", 2.25, 1, 0.25);
                Check(a == "-hide_banner -loglevel error -nostdin -ss 2.250 " + wl + "-i \"C:\\a b\\x.mkv\" -map 0:a:1 -vn -sn -dn -af atempo=0.5,atempo=0.5 -ac 2 -ar 48000 -f f32le -", "audio 0.25: " + a);
                Check(PlayerArgs.Audio("x.mp4", 0, 0, 1).Contains("-vn -sn -dn -ac 2"), "speed 1 adds no filter");
                string v = PlayerArgs.Video("x.mp4", 1, 320, 180, 30, 320, 180, 2, 1);
                Check(v == "-hide_banner -loglevel error -nostdin -ss 1.000 " + wl + "-i \"x.mp4\" -map 0:v:0 -an -sn -dn -vf setpts=PTS/2 -fps_mode cfr -r 30 -f rawvideo -pix_fmt bgra -", "video 2x: " + v);
                string v2 = PlayerArgs.Video("x.mp4", 0, 160, 90, 30000.0 / 1001, 320, 180, 0.25, 0);
                Check(v2.Contains("-vf scale=160:90:flags=lanczos,setpts=PTS/0.25 -fps_mode cfr -r 29.97003 "), "video 0.25 scaled: " + v2);
                Check(!PlayerArgs.Video("x.mp4", 0, 0, 0, 25, 320, 180, 1, 1).Contains("-vf"), "native speed 1 has no filter");
            });

            Test("playerwindow", "scaling-flag-args", delegate
            {
                string best = PlayerArgs.Video("x.mp4", 0, 160, 90, 30, 320, 180, 1, 0), fast = PlayerArgs.Video("x.mp4", 0, 160, 90, 30, 320, 180, 1, 1);
                Check(best.Contains("scale=160:90:flags=lanczos") && !best.Contains("bilinear"), "best: " + best);
                Check(fast.Contains("scale=160:90:flags=bilinear") && !fast.Contains("lanczos"), "fast: " + fast);
                Check(!PlayerArgs.Video("x.mp4", 0, 320, 180, 30, 320, 180, 1, 0).Contains("scale"), "native size is never scaled");
                string still = PlayerArgs.Still("x.mp4", 1.5, 160, 90, 320, 180, 0);
                Check(still == "-hide_banner -loglevel error -nostdin -ss 1.500 " + PlayerArgs.Whitelist + "-i \"x.mp4\" -map 0:v:0 -an -sn -dn -vf scale=160:90:flags=lanczos -frames:v 1 -f rawvideo -pix_fmt bgra -", "still: " + still);
                // The editor's own call keeps its exact old line.
                Check(PlayerArgs.Video("x.mp4", 0, 160, 90, 30, 320, 180).Contains(Recorder.SafeInput + "-i") && PlayerArgs.Video("x.mp4", 0, 160, 90, 30, 320, 180).Contains("flags=bilinear"), "the 7-argument overload changed");
            });

            Test("playerwindow", "scaling-decode-size-never-upscales", delegate
            {
                PlMaster m = PlSharedMaster();
                int[][] sizes = { new int[] { 160, 90 }, new int[] { 640, 360 }, new int[] { 161, 91 } };
                int[] want = { 160, 320, 160 };
                for (int k = 0; k < sizes.Length; k++)
                    foreach (int scaling in new int[] { 0, 1 })
                    {
                        PlLog log = new PlLog();
                        PlFakeSound snd = new PlFakeSound();
                        Playback p = PlNew(m.Path, snd, log);
                        PwStills st = new PwStills();
                        p.Scaling = scaling;
                        p.SetDecodeSize(sizes[k][0], sizes[k][1]);
                        p.StillReady += delegate(PlayerFrame f) { lock (st) { st.Index.Add(PlIndex(f, 320)); st.Width.Add(f.Width); st.Height.Add(f.Height); } };
                        try
                        {
                            p.Still(10 / 30.0);
                            PlWait(delegate { return st.Count > 0; }, 8000, "a still at " + sizes[k][0] + "x" + sizes[k][1]);
                            Check(st.Width[0] == want[k], "asked " + sizes[k][0] + "x" + sizes[k][1] + " scaling " + scaling + ", got width " + st.Width[0]);
                            Check(st.Width[0] % 2 == 0 && st.Height[0] % 2 == 0, "odd size " + st.Width[0] + "x" + st.Height[0]);
                            Check(st.Index[0] == 10, "the still shows frame " + st.Index[0] + " (scaling " + scaling + ")");
                        }
                        finally { p.Dispose(); }
                    }
            });

            Test("playerwindow", "clock-2x-with-sound", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Speed = 2;
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end at 2x");
                    PlOk(log);
                    int n = log.Count;
                    double span = (log.Ms[n - 1] - log.Ms[0]) / 1000.0;
                    // 90 frames of media at 2x: 45 pictures in about 1.5 s.
                    Check(Math.Abs(n - m.Frames / 2) <= 2, "shown " + n + " frames, expected " + (m.Frames / 2) + " +-2");
                    Check(Math.Abs(p.FramesDecoded - m.Frames / 2) <= 2, "decoded " + p.FramesDecoded);
                    Check(span > 1.0 && span < 2.1, "the pictures spread over " + PlF(span) + " s, expected about 1.4");
                    // A picture may be skipped when late, so a step is a whole number of 2-frame steps.
                    for (int i = 1; i < n; i++) Check(PwStepOk(log, i, 2 / 30.0), "media time step at " + i + ": " + (log.Time[i] - log.Time[i - 1]));
                    // The sound was sped up too: half as many samples as the media has.
                    Check(Math.Abs(p.AudioFramesQueued - 48000 * m.Seconds / 2) < 4800, "audio frames queued " + p.AudioFramesQueued);
                    Check(Math.Abs(p.Position - m.Seconds) < 0.25, "position at the end " + p.Position);
                    Add(n + " pictures in " + PlF(span) + " s, audio frames " + p.AudioFramesQueued);
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "clock-2x-ideal-clock", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                snd.Manual = true;
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Speed = 2;
                    p.Start(0, 0);
                    // The test's clock: 800 sample frames per output frame, so each step is 2 media frames.
                    for (int i = 0; i < m.Frames / 2; i++)
                    {
                        int step = i;
                        snd.SetManual(step * 1600L);
                        PlWait(delegate { return log.Count >= step + 1 || log.Errors.Count > 0; }, 8000, "picture " + step);
                        PlOk(log);
                        Thread.Sleep(5);
                        Check(log.Count == step + 1, "at step " + step + " there are " + log.Count + " pictures");
                        Check(Math.Abs(log.Time[step] - step * 2 / 30.0) < 1e-6, "picture " + step + " is media time " + log.Time[step]);
                    }
                    Check(Math.Abs(p.Position - (m.Frames / 2 - 1) * 1600 / 48000.0 * 2) < 0.05, "position " + p.Position);
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "clock-half-speed-no-sound", delegate
            {
                NeedFfmpeg();
                string mp4 = PlMp4("half.mp4", false, 1);
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(mp4, snd, log);
                try
                {
                    p.Speed = 0.5;
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end at 0.5x");
                    PlOk(log);
                    int n = log.Count;
                    double span = (log.Ms[n - 1] - log.Ms[0]) / 1000.0;
                    Check(Math.Abs(n - 60) <= 2, "shown " + n + " pictures, expected 60 +-2");
                    Check(span > 1.5 && span < 2.4, "spread over " + PlF(span) + " s, expected about 2");
                    Add(n + " pictures in " + PlF(span) + " s on the Stopwatch clock");
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "speed-quarter-sound-stretches", delegate
            {
                NeedFfmpeg();
                string mp4 = PlMp4("quarter.mp4", true, 1);
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(mp4, snd, log);
                try
                {
                    p.Speed = 0.25;
                    p.Start(0, 0);
                    // The sound is kept when the end resets the sink, so a copy is taken while it plays.
                    PlWait(delegate { return snd.SampleCount >= 2 * 24000 || log.Errors.Count > 0; }, 25000, "sound at 0.25x");
                    float[] s = snd.Samples();
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 25000, "the end at 0.25x");
                    PlOk(log);
                    // One second of sound at a quarter speed is four seconds of samples.
                    Check(Math.Abs(p.AudioFramesQueued - 4 * 48000) < 9600, "audio frames queued " + p.AudioFramesQueued);
                    Check(Math.Abs(log.Count - 120) <= 3, "pictures " + log.Count + ", expected 120");
                    // atempo keeps the pitch: the 440 Hz tone is still 440 Hz.
                    Check(s.Length >= 2 * 24000, "samples " + s.Length);
                    double a440 = PlTonePower(s, 4800, 9600, 440), a330 = PlTonePower(s, 4800, 9600, 330);
                    Check(a440 > 0.04 && a440 > 3 * a330, "tone power at 440 Hz " + PlF(a440) + ", at 330 Hz " + PlF(a330));
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "speed-change-while-playing", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 15; }, 8000, "half a second");
                    double before = p.Position;
                    int mark = log.Count;
                    p.Speed = 2;
                    PlWait(delegate { return log.Count >= mark + 6; }, 8000, "pictures at 2x");
                    PlOk(log);
                    // It carried on from where it was (no jump back to 0) and the media step is now two frames.
                    Check(log.Time[mark] >= before - 0.2 && log.Time[mark] < before + 0.6, "first picture after the change is at " + PlF(log.Time[mark]) + ", was " + PlF(before));
                    Check(PwStepOk(log, mark + 3, 2 / 30.0), "step after the change " + (log.Time[mark + 3] - log.Time[mark + 2]));
                    p.Pause();
                    p.Speed = 0.5;
                    Check(p.State == PlayState.Paused, "a speed change must not resume: " + p.State);
                    p.Resume();
                    int mark2 = log.Count;
                    PlWait(delegate { return log.Count >= mark2 + 4; }, 8000, "pictures at 0.5x");
                    Check(PwStepOk(log, mark2 + 3, 0.5 / 30.0), "step at 0.5x " + (log.Time[mark2 + 3] - log.Time[mark2 + 2]));
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "loop-restarts", delegate
            {
                NeedFfmpeg();
                string mp4 = PlMp4("loop.mp4", true, 1);
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(mp4, snd, log);
                try
                {
                    p.Repeat = true;
                    p.Start(0, 0);
                    PlWait(delegate { return p.Loops >= 3 || log.Errors.Count > 0; }, 20000, "three loops");
                    PlOk(log);
                    Check(log.EndedCount == 0 && p.State == PlayState.Playing, "ended " + log.EndedCount + " state " + p.State);
                    int restarts = 0, n = log.Count;
                    for (int i = 1; i < n; i++) if (log.Number[i] == 0 && log.Time[i] < 0.1 && log.Time[i - 1] > 0.5) restarts++;
                    Check(restarts >= 2, "the pictures restarted " + restarts + " times");
                    p.Repeat = false;
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end once loop is off");
                    Check(p.State == PlayState.Ended, "state " + p.State);
                    Add(p.Loops + " loops, " + restarts + " restarts seen");
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "loop-start-and-end-time", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Repeat = true;
                    p.LoopStart = 1.0;
                    p.Start(2.0, 2.4);
                    PlWait(delegate { return p.Loops >= 1 || log.Errors.Count > 0; }, 15000, "one loop");
                    PlOk(log);
                    PlWait(delegate { return log.Count >= 20; }, 8000, "pictures after the loop");
                    bool back = false;
                    for (int i = 1; i < log.Count; i++) if (log.Index[i] < log.Index[i - 1]) { back = true; Check(log.Index[i] == 30, "the loop restarts at frame " + log.Index[i] + ", expected 30"); break; }
                    Check(back, "the pictures never went back");
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "frame-step-lands-on-neighbour", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                PwStills st = new PwStills();
                p.StillReady += delegate(PlayerFrame f) { lock (st) { st.Index.Add(PlIndex(f, 320)); st.Width.Add(f.Width); st.Height.Add(f.Height); } };
                try
                {
                    double pos = 10 / 30.0;
                    p.Seek(pos);
                    int[] dirs = { 0, 1, 1, -1, -1, -1, 1 };
                    int n = 10;
                    for (int k = 0; k < dirs.Length; k++)
                    {
                        if (dirs[k] != 0) { pos = PlayerKit.StepTime(p.Position, 30, m.Seconds, dirs[k]); n += dirs[k]; p.Seek(pos); }
                        int mark = st.Count;
                        p.Still(pos);
                        PlWait(delegate { return st.Count > mark; }, 8000, "the still of step " + k);
                        Check(st.Index[mark] == n, "step " + k + " (" + dirs[k] + "): the picture is frame " + st.Index[mark] + ", expected " + n);
                        Check(Math.Abs(p.Position - n / 30.0) < 1e-6, "the position is " + p.Position + ", expected " + n / 30.0);
                    }
                    Check(log.Count == 0, "a still must not play: " + log.Count + " pictures");
                    // The first and the last frame: a step off either end stays where it is.
                    Check(PlayerKit.StepTime(0, 30, m.Seconds, -1) == 0, "back from 0");
                    double last = PlayerKit.StepTime(89 / 30.0, 30, m.Seconds, 1);
                    Check(Math.Abs(last - 89 / 30.0) < 1e-9, "forward from the last frame " + last);
                    int mk = st.Count;
                    p.Still(last);
                    PlWait(delegate { return st.Count > mk; }, 8000, "the last frame");
                    Check(st.Index[mk] == 89, "the last frame shows " + st.Index[mk]);
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "still-latest-request-wins-and-dispose-kills", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                PwStills st = new PwStills();
                p.StillReady += delegate(PlayerFrame f) { lock (st) { st.Index.Add(PlIndex(f, 320)); st.Width.Add(f.Width); st.Height.Add(f.Height); } };
                try
                {
                    for (int k = 0; k < 12; k++) p.Still(k / 30.0 + 0.5);
                    p.Still(60 / 30.0);
                    PlWait(delegate { lock (st) return st.Index.Contains(60); }, 8000, "the last still");
                    Thread.Sleep(300);
                    lock (st) Check(st.Index[st.Index.Count - 1] == 60, "the last answer is frame " + st.Index[st.Index.Count - 1]);
                    p.Still(5);
                    p.Dispose();
                    int before = st.Count;
                    Thread.Sleep(500);
                    Check(st.Count == before, "a still arrived after Dispose");
                    PlDead(p, 3000);
                }
                finally { p.Dispose(); }
            });

            // Every container FFmpeg is asked to write here must open, report its size and play to the end.
            PwFormat("mov", ".mov", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", null, false, 36);
            PwFormat("mkv", ".mkv", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", null, false, 36);
            PwFormat("webm", ".webm", "-c:v libvpx -b:v 1M -deadline realtime -c:a libvorbis", null, false, 36);
            PwFormat("avi", ".avi", "-c:v mpeg4 -q:v 4 -c:a pcm_s16le", null, false, 36);
            PwFormat("wmv", ".wmv", "-c:v wmv2 -c:a wmav2", null, false, 36);
            PwFormat("flv", ".flv", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", null, false, 36);
            PwFormat("mpg", ".mpg", "-c:v mpeg2video -c:a mp2", null, false, 36);
            PwFormat("ts", ".ts", "-c:v mpeg2video -c:a mp2", null, false, 36);
            PwFormat("mts", ".mts", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", "-f mpegts", false, 36);
            PwFormat("m2ts", ".m2ts", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", "-f mpegts", false, 36);
            PwFormat("3gp", ".3gp", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", null, false, 36);
            PwFormat("m4v", ".m4v", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p", null, true, 36);
            PwFormat("ogv", ".ogv", "-c:v libtheora -q:v 6 -c:a libvorbis", null, false, 36);
            PwFormat("gif", ".gif", "-vf fps=25", null, true, 30);

            Test("playerwindow", "unknown-extension-probe-fallback", delegate
            {
                NeedFfmpeg();
                string mp4 = PlMp4("source.mp4", true, 1);
                string odd = Path.Combine(WorkDir("pw-odd"), "movie.xyz");
                File.Copy(mp4, odd);
                Check(!ShotStack.IsVideoFile(odd), ".xyz must not be in the list");
                MediaInfo mi = MediaInfo.Probe(ffmpeg, odd);
                Check(mi.CanPlay && mi.Width == 320, "the probe must read it by content: " + mi.Width);
                PlLog log = new PlLog();
                Playback p;
                int frames = PwPlayAll(odd, log, out p);
                try { Check(Math.Abs(frames - 30) <= 1, "decoded " + frames); }
                finally { p.Dispose(); }
                string text = Path.Combine(WorkDir("pw-odd"), "notes.xyz");
                File.WriteAllText(text, "this is not a video\r\n");
                Check(!MediaInfo.Probe(ffmpeg, text).CanPlay, "a text file must not read as playable");
                Check(!MediaInfo.Probe(ffmpeg, Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".mp4")).CanPlay, "a missing file");
            });

            Test("playerwindow", "extension-list-and-whitelist", delegate
            {
                string[] must = { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".3gp", ".ogv", ".gif" };
                foreach (string ext in must)
                {
                    Check(Array.IndexOf(ShotStack.VideoExtensions, ext) >= 0, ext + " is missing from ShotStack.VideoExtensions");
                    Check(ShotStack.IsVideoFile("C:\\v\\clip" + ext.ToUpperInvariant()), ext + " in capitals");
                }
                Check(!ShotStack.IsVideoFile("a.png") && !ShotStack.IsVideoFile("a.txt") && !ShotStack.IsVideoFile("a") && !ShotStack.IsVideoFile(null), "not videos");
                string f = ShotStack.VideoFilter;
                Check(f.StartsWith("V\u00EDdeos|*.mp4") && f.EndsWith("|Todos los archivos|*.*") && f.Contains("*.wmv") && f.Contains("*.m2ts"), "filter " + f);
                string wl = PlayerArgs.Whitelist;
                Check(wl.StartsWith("-format_whitelist ") && wl.EndsWith(" "), "whitelist shape");
                foreach (string d in new string[] { "mov", "mp4", "matroska", "webm", "gif", "avi", "asf", "flv", "mpeg", "mpegts", "ogg" })
                    Check(wl.Contains("," + d + ",") || wl.Contains(" " + d + ",") || wl.Contains("," + d + " "), "the whitelist lacks " + d);
                // Playlists and anything that names other files or addresses stay out.
                foreach (string bad in new string[] { "hls", "concat", "ffconcat", "dash", "tee", "sdp", "rtsp", "lavfi", "image2", "data", "http" })
                    Check(!wl.Contains(bad), "the whitelist must not allow " + bad);
                Check(Recorder.SafeInput == wl, "the player and the editor read the same containers");
            });

            Test("playerwindow", "rotated-phone-video-swaps-size", delegate
            {
                MediaInfo r = MediaInfo.Parse("Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'IMG_0001.MOV':\n  Duration: 00:00:03.00, start: 0.000000, bitrate: 9000 kb/s\n" +
                                              "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709, progressive), 1920x1080, 9000 kb/s, 30 fps, 30 tbr, 600 tbn (default)\n" +
                                              "    Side data:\n      displaymatrix: rotation of -90.00 degrees\n" +
                                              "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 100 kb/s (default)\n");
                Check(r.Width == 1080 && r.Height == 1920, "rotated size " + r.Width + "x" + r.Height);
                MediaInfo flat = MediaInfo.Parse("  Stream #0:0: Video: h264, yuv420p, 1920x1080, 30 fps\n    Side data:\n      displaymatrix: rotation of 180.00 degrees\n");
                Check(flat.Width == 1920 && flat.Height == 1080, "180 degrees keeps the size");
                MediaInfo other = MediaInfo.Parse("  Stream #0:0: Audio: aac (LC), 44100 Hz, stereo\n    Side data:\n      displaymatrix: rotation of 90.00 degrees\n  Stream #0:1: Video: h264, yuv420p, 640x360, 30 fps\n");
                Check(other.Width == 640 && other.Height == 360, "a rotation of another stream must not turn this one");
                NeedFfmpeg();
                string src = PlMp4("upright.mp4", false, 1);
                string rot = Path.Combine(WorkDir("pw-rot"), "rotated.mp4");
                string err;
                if (Ff("-hide_banner -loglevel error -y -display_rotation:v:0 90 -i " + Q(src) + " -c copy " + Q(rot), out err, 30000) != 0) Skip("this FFmpeg cannot set a display rotation: " + Tail(err));
                MediaInfo real = MediaInfo.Probe(ffmpeg, rot);
                Check(real.CanPlay, "the rotated file does not read as playable");
                Check(real.Width == 180 && real.Height == 320, "the probe says " + real.Width + "x" + real.Height + " for a 320x180 turned 90 degrees");
                PlFakeSound snd = new PlFakeSound();
                Playback p = new Playback(ffmpeg, rot, real, snd);
                PwStills st = new PwStills();
                p.StillReady += delegate(PlayerFrame f) { lock (st) { st.Index.Add(0); st.Width.Add(f.Width); st.Height.Add(f.Height); } };
                try
                {
                    p.Still(0.2);
                    PlWait(delegate { return st.Count > 0; }, 8000, "a still of the rotated file");
                    Check(st.Width[0] == 180 && st.Height[0] == 320, "the decoded picture is " + st.Width[0] + "x" + st.Height[0]);
                }
                finally { p.Dispose(); }
            });

            Test("playerwindow", "kit-pure", delegate
            {
                Check(PlayerKit.Clock(0) == "0:00" && PlayerKit.Clock(72.4) == "1:12" && PlayerKit.Clock(3723) == "1:02:03" && PlayerKit.Clock(-5) == "0:00" && PlayerKit.Clock(59.99) == "0:59", "clock");
                Check(PlayerKit.SpeedText(0.25) == "0,25\u00D7" && PlayerKit.SpeedText(1) == "1\u00D7" && PlayerKit.SpeedText(1.5) == "1,5\u00D7", "speed text");
                Check(PlayerKit.FrameAt(10 / 30.0, 30) == 10 && PlayerKit.FrameAt(10.9 / 30.0, 30) == 10 && PlayerKit.FrameAt(0, 30) == 0, "frame at");
                Check(PlayerKit.LastFrame(3.0, 30) == 89 && PlayerKit.LastFrame(72.4, 30) == 2171, "last frame " + PlayerKit.LastFrame(72.4, 30));
                Check(Math.Abs(PlayerKit.StepTime(10 / 30.0, 30, 3, 1) - 11 / 30.0) < 1e-12 && Math.Abs(PlayerKit.StepTime(10 / 30.0, 30, 3, -1) - 9 / 30.0) < 1e-12, "step");
                Check(PlayerKit.StepTime(0, 30, 3, -1) == 0, "step back from the start");
                Rectangle lb = PlayerKit.Letterbox(new Size(1730, 786), new Size(1000, 1000));
                Check(lb.Width == 1000 && lb.Height == 454 && lb.X == 0 && lb.Y == 273, "letterbox " + lb);
                Check(PlayerKit.Letterbox(new Size(100, 200), new Size(1000, 500)) == new Rectangle(375, 0, 250, 500), "pillarbox " + PlayerKit.Letterbox(new Size(100, 200), new Size(1000, 500)));
                Check(PlayerKit.DecodeSize(new Size(1730, 786), new Size(865, 393)) == new Size(864, 392), "decode size " + PlayerKit.DecodeSize(new Size(1730, 786), new Size(865, 393)));
                Check(PlayerKit.DecodeSize(new Size(320, 180), new Size(1000, 1000)) == Size.Empty, "never above native");
                // The window: the video at its real size when it fits with the frame (16 x 39 px here), else the largest that fits.
                Size frame = new Size(16, 39);
                Rectangle laptop = new Rectangle(0, 0, 1920, 1032);
                Check(PlayerKit.WindowFor(new Size(1888, 968), laptop, 560, frame) == new Size(1888, 968), "a 1888x968 master at real size on a 1080p screen: " + PlayerKit.WindowFor(new Size(1888, 968), laptop, 560, frame));
                Check(PlayerKit.WindowFor(new Size(1730, 786), laptop, 560, frame) == new Size(1730, 786), "1730x786 at real size");
                Size w = PlayerKit.WindowFor(new Size(1920, 1080), laptop, 560, frame);
                Check(w.Width <= 1904 && w.Height <= 993 && w.Height >= 990 && Math.Abs(w.Width / (double)w.Height - 16 / 9.0) < 0.01, "a full-screen recording fills what fits: " + w);
                Size small = PlayerKit.WindowFor(new Size(320, 180), new Rectangle(0, 0, 1920, 1040), 560);
                Check(small.Width == 560 && small.Height == 315, "small video window " + small);
                Size tall = PlayerKit.WindowFor(new Size(1080, 1920), new Rectangle(0, 0, 1920, 1040), 560, frame);
                Check(tall.Height <= 1001 && tall.Height >= 999 && Math.Abs(tall.Width / (double)tall.Height - 1080 / 1920.0) < 0.01, "phone video window " + tall);
                // The picture: never enlarged unless "llenar la ventana" is on; scaled down when the window is smaller.
                Check(PlayerKit.Picture(new Size(1888, 968), new Size(1920, 1080), false) == new Rectangle(16, 56, 1888, 968), "real size, centred, in full screen: " + PlayerKit.Picture(new Size(1888, 968), new Size(1920, 1080), false));
                Check(PlayerKit.Picture(new Size(1888, 968), new Size(1920, 1080), true) == PlayerKit.Letterbox(new Size(1888, 968), new Size(1920, 1080)), "fill enlarges like before");
                Check(PlayerKit.Picture(new Size(1888, 968), new Size(1536, 787), false) == PlayerKit.Letterbox(new Size(1888, 968), new Size(1536, 787)), "smaller window: scaled down");
                Check(PlayerKit.Picture(new Size(1888, 968), new Size(1888, 968), false) == new Rectangle(0, 0, 1888, 968), "exact fit");
                Check(PlayerKit.Picture(new Size(1888, 968), new Size(1889, 968), false) == new Rectangle(0, 0, 1888, 968), "one pixel more is not a stretch");
                Check(PlayerKit.ScaleNote(new Size(1888, 968), new Rectangle(16, 56, 1888, 968), true).StartsWith("100 % (tama\u00F1o real"), "note at real size");
                Check(PlayerKit.ScaleNote(new Size(1888, 968), PlayerKit.Letterbox(new Size(1888, 968), new Size(1536, 787)), true).StartsWith("81 % (la ventana"), "note when smaller: " + PlayerKit.ScaleNote(new Size(1888, 968), PlayerKit.Letterbox(new Size(1888, 968), new Size(1536, 787)), true));
                Check(PlayerKit.ScaleNote(new Size(3840, 2160), new Rectangle(0, 0, 1920, 1080), false).StartsWith("50 % (el v\u00EDdeo es m\u00E1s grande que la pantalla"), "note in full screen with a 4K video");
                Check(PlayerKit.ScaleNote(new Size(640, 360), new Rectangle(0, 0, 1920, 1080), true).StartsWith("300 % (ampliado"), "note when filling");
                Check(PlayerKit.ScaleNote(new Size(1888, 968), new Rectangle(0, 0, 1887, 967), true).StartsWith("99 %"), "never rounds up to 100");
                string cine = Path.Combine(Path.GetTempPath(), "cine");
                PlayerKit.CineDir = cine;
                try
                {
                    Check(PlayerKit.CineReportPath("D:\\v\\Cine 2026-10-10 12.53.58.mkv") == Path.Combine(cine, "Cine 2026-10-10 12.53.58.json"), "report path " + PlayerKit.CineReportPath("D:\\v\\Cine 2026-10-10 12.53.58.mkv"));
                    Check(PlayerKit.ReadCineReport(Path.Combine("D:\\v", "Cine no report " + Guid.NewGuid().ToString("N") + ".mkv")) == null, "a missing report is null");
                }
                finally { PlayerKit.CineDir = Path.Combine(Settings.DataDir, "cine"); }
            });

            Test("playerwindow", "info-panel-rows", delegate
            {
                MediaInfo m = MediaInfo.Parse(PlUserMasterProbe);
                List<string[]> rows = PlayerKit.Describe(m, "C:\\v\\Cine 2026-10-10 12.53.58.mkv", 366L * 1048576, "{\"passed\":true,\"verification\":{\"passed\":true,\"framesMatched\":2172}}");
                Func<string, string> val = delegate(string label)
                {
                    foreach (string[] r in rows) if (r[0] == label) return r[1];
                    return null;
                };
                Check(val("Archivo") == "Cine 2026-10-10 12.53.58.mkv", "file " + val("Archivo"));
                Check(val("Contenedor") == "Matroska / WebM", "container " + val("Contenedor"));
                Check(val("C\u00F3dec de v\u00EDdeo") == "h264 (High 4:4:4 Predictive)", "codec " + val("C\u00F3dec de v\u00EDdeo"));
                Check(val("Formato de p\u00EDxel") == "gbrp" && val("Tama\u00F1o") == "1730 \u00D7 786" && val("Fotogramas por segundo") == "30" && val("Duraci\u00F3n") == "1:12", "video rows");
                Check(val("Audio 1") == Cine.TitleMix + " \u00B7 pcm_f32le \u00B7 est\u00E9reo \u00B7 48000 Hz", "audio 1: " + val("Audio 1"));
                Check(val("Audio 2") != null && val("Audio 3") != null && val("Audio 4") == null, "three audio tracks");
                Check(val("Tama\u00F1o del archivo") == "366 MB", "size " + val("Tama\u00F1o del archivo"));
                Check(val("M\u00E1ster sin p\u00E9rdidas") != null, "a Cine master says so");
                Check(val("Comprobaci\u00F3n") == "Correcta: 2172 fotogramas id\u00E9nticos", "verification " + val("Comprobaci\u00F3n"));
                List<string[]> failed = PlayerKit.Describe(m, "x.mkv", 0, "{\"passed\":false}");
                bool bad = false;
                foreach (string[] r in failed) if (r[0] == "Comprobaci\u00F3n" && r[1] == "No superada") bad = true;
                Check(bad, "a failed report is shown as failed");
                List<string[]> noReport = PlayerKit.Describe(m, "x.mkv", 0, null);
                foreach (string[] r in noReport) Check(r[0] != "Comprobaci\u00F3n", "no report, no verification row");
                List<string[]> mp4 = PlayerKit.Describe(MediaInfo.Parse(PlMp4Probe), "x (para compartir).mp4", 1234, null);
                foreach (string[] r in mp4) Check(r[0] != "M\u00E1ster sin p\u00E9rdidas", "an MP4 is no master");
                bool aac = false;
                foreach (string[] r in mp4) if (r[0] == "Audio" && r[1].Contains("aac") && r[1].Contains("48000 Hz")) aac = true;
                Check(aac, "the mp4 audio row");
                List<string[]> silent = PlayerKit.Describe(MediaInfo.Parse("  Duration: 00:00:02.00\n  Stream #0:0: Video: h264, yuv420p, 320x180, 30 fps\n"), "s.mp4", 0, null);
                bool none = false;
                foreach (string[] r in silent) if (r[0] == "Audio" && r[1] == "Sin sonido") none = true;
                Check(none, "a silent file says so");
                Add(rows.Count + " rows for the master");
            });

            Test("playerwindow", "opening-routes", delegate
            {
                // A card, a recent or the tray: videos open in the player when it is on, Cine masters always.
                Check(ShotStack.RouteFor("a.png", true) == ShotStack.OpenRoute.Editor && ShotStack.RouteFor("a.png", false) == ShotStack.OpenRoute.Editor, "images go to the editor");
                Check(ShotStack.RouteFor("a.jpg", false) == ShotStack.OpenRoute.Editor && ShotStack.RouteFor("a.txt", true) == ShotStack.OpenRoute.Editor && ShotStack.RouteFor(null, true) == ShotStack.OpenRoute.Editor, "other files go to the editor");
                foreach (string ext in new string[] { ".mp4", ".mov", ".webm", ".avi", ".gif", ".wmv", ".flv", ".mpg", ".ts", ".3gp" })
                {
                    Check(ShotStack.RouteFor("clip" + ext, true) == ShotStack.OpenRoute.Player, ext + " with the player on");
                    // Only a Cine master (.mkv) is forced into the player.
                    Check(ShotStack.RouteFor("clip" + ext, false) == ShotStack.OpenRoute.Windows, ext + " with the player off");
                }
                Check(ShotStack.RouteFor("Cine 2026-10-10 12.53.58.mkv", true) == ShotStack.OpenRoute.Player, "Cine on");
                Check(ShotStack.RouteFor("Cine 2026-10-10 12.53.58.mkv", false) == ShotStack.OpenRoute.Player, "Cine off: Windows cannot decode it");
                Check(ShotStack.RouteFor("C:\\v\\CINE 2026-10-10 12.53.58.MKV", false) == ShotStack.OpenRoute.Player, "capitals");
                Check(ShotStack.RouteFor("C:\\v\\CLIP.MKV", false) == ShotStack.OpenRoute.Windows && ShotStack.RouteFor("C:\\v\\CLIP.MKV", true) == ShotStack.OpenRoute.Player, "a plain .mkv follows the switch");
                Settings st = new Settings();
                Check(st.PlayerOpensVideos && st.PlayerScaling == 0 && st.PlayerVolume == 100 && !st.PlayerLoop, "defaults of the player settings");
            });

            Test("playerwindow", "window-headless-plays-and-disposes", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                Settings st = new Settings();
                PlayerWindow w = PwWindow(m.Path, snd, st);
                Playback engine = null;
                try
                {
                    Check(w.Message == null, "message " + w.Message);
                    Check(w.Media != null && w.Media.CanPlay && w.Media.Width == 320, "media");
                    engine = w.Engine;
                    Check(engine != null, "no engine");
                    PwStills seen = new PwStills();
                    w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(PlIndex(f, 320)); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                    w.BeginPlay();
                    PlWait(delegate { return seen.Count >= 6; }, 8000, "pictures in the window");
                    Check(engine.State == PlayState.Playing, "state " + engine.State);
                    for (int i = 1; i < 6; i++) Check(seen.Index[i] == seen.Index[i - 1] + 1, "out of order at " + i);
                    Check(engine.Track == 0 && engine.Repeat == st.PlayerLoop, "engine settings");
                }
                finally
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    w.Dispose();
                    long took = sw.ElapsedMilliseconds;
                    Check(took < 2500, "disposing took " + took + " ms");
                    Add("window disposed in " + took + " ms");
                }
                PlDead(engine, 3000);
            });

            Test("playerwindow", "window-keys-volume-loop-remembered", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                Settings st = new Settings();
                st.PlayerVolume = 60;
                PlayerWindow w = PwWindow(m.Path, snd, st);
                try
                {
                    Check(Math.Abs(w.Engine.Volume - 0.6) < 1e-9, "the volume comes from the settings: " + w.Engine.Volume);
                    PwKey(w, Keys.Up);
                    Check(st.PlayerVolume == 65 && Math.Abs(w.Engine.Volume - 0.65) < 1e-9, "up: " + st.PlayerVolume + " engine " + w.Engine.Volume);
                    PwKey(w, Keys.Down); PwKey(w, Keys.Down);
                    Check(st.PlayerVolume == 55, "down twice: " + st.PlayerVolume);
                    PwKey(w, Keys.M);
                    Check(w.Engine.Volume == 0 && st.PlayerVolume == 55, "mute keeps the remembered volume: engine " + w.Engine.Volume + ", settings " + st.PlayerVolume);
                    PwKey(w, Keys.M);
                    Check(Math.Abs(w.Engine.Volume - 0.55) < 1e-9, "unmute " + w.Engine.Volume);
                    for (int i = 0; i < 30; i++) PwKey(w, Keys.Up);
                    Check(st.PlayerVolume == 100 && w.Engine.Volume == 1, "volume stops at 100: " + st.PlayerVolume);
                    for (int i = 0; i < 30; i++) PwKey(w, Keys.Down);
                    Check(st.PlayerVolume == 0 && w.Engine.Volume == 0, "volume stops at 0: " + st.PlayerVolume);
                    MethodInfo loop = typeof(PlayerWindow).GetMethod("ToggleLoop", BindingFlags.NonPublic | BindingFlags.Instance);
                    loop.Invoke(w, null);
                    Check(st.PlayerLoop && w.Engine.Repeat, "loop on");
                    loop.Invoke(w, null);
                    Check(!st.PlayerLoop && !w.Engine.Repeat, "loop off");
                    MethodInfo scaling = typeof(PlayerWindow).GetMethod("SetScaling", BindingFlags.NonPublic | BindingFlags.Instance);
                    scaling.Invoke(w, new object[] { 1 });
                    Check(st.PlayerScaling == 1 && w.Engine.Scaling == 1, "scaling fast");
                    scaling.Invoke(w, new object[] { 0 });
                    Check(st.PlayerScaling == 0 && w.Engine.Scaling == 0, "scaling best");
                }
                finally { w.Dispose(); }
            });

            Test("playerwindow", "window-keys-play-pause-and-frame-step", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlayerWindow w = PwWindow(m.Path, snd, new Settings());
                Playback engine = w.Engine;
                try
                {
                    PwStills seen = new PwStills();
                    w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(PlIndex(f, 320)); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                    w.BeginPlay();
                    PlWait(delegate { return seen.Count >= 4; }, 8000, "playing");
                    PwKey(w, Keys.Space);
                    Check(engine.State == PlayState.Paused, "Space pauses: " + engine.State);
                    PwKey(w, Keys.Space);
                    Check(engine.State == PlayState.Playing, "Space plays: " + engine.State);
                    PwKey(w, Keys.K);
                    Check(engine.State == PlayState.Paused, "K pauses: " + engine.State);
                    // Home: the first frame, then one step at a time.
                    int mark = seen.Count;
                    PwKey(w, Keys.Home);
                    PlWait(delegate { return seen.Count > mark; }, 8000, "the first frame");
                    Check(seen.Index[seen.Count - 1] == 0, "Home shows frame " + seen.Index[seen.Count - 1]);
                    int[] expect = { 1, 2, 1, 0, 0, 1 };
                    Keys[] keys = { Keys.OemPeriod, Keys.OemPeriod, Keys.Oemcomma, Keys.Oemcomma, Keys.Oemcomma, Keys.OemPeriod };
                    for (int k = 0; k < keys.Length; k++)
                    {
                        int before = seen.Count;
                        PwKey(w, keys[k]);
                        PlWait(delegate { return seen.Count > before; }, 8000, "the picture of step " + k);
                        Check(seen.Index[seen.Count - 1] == expect[k], "step " + k + " (" + keys[k] + ") shows frame " + seen.Index[seen.Count - 1] + ", expected " + expect[k]);
                        Check(engine.State == PlayState.Paused, "stepping keeps it paused: " + engine.State);
                    }
                    // End: the last frame, paused.
                    int b2 = seen.Count;
                    PwKey(w, Keys.End);
                    PlWait(delegate { return seen.Count > b2; }, 8000, "the last frame");
                    Check(seen.Index[seen.Count - 1] == 89, "End shows frame " + seen.Index[seen.Count - 1]);
                    // Seeks by keys: 5 s and 10 s are clamped inside the file.
                    int b3 = seen.Count;
                    PwKey(w, Keys.Left);
                    PlWait(delegate { return seen.Count > b3; }, 8000, "a picture after Left");
                    Check(seen.Index[seen.Count - 1] == 0, "Left from 89 clamps to frame " + seen.Index[seen.Count - 1] + " (5 s is more than the 3 s file)");
                }
                finally { w.Dispose(); }
                PlDead(engine, 3000);
            });

            Test("playerwindow", "window-resize-decodes-at-the-new-size", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlayerWindow w = PwWindow(m.Path, snd, new Settings());
                Playback engine = w.Engine;
                try
                {
                    Check(w.DecodeNow == Size.Empty || w.DecodeNow.Width <= 320, "initial decode " + w.DecodeNow);
                    PwStills seen = new PwStills();
                    w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(PlIndex(f, 320)); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                    w.BeginPlay();
                    PlWait(delegate { return seen.Count >= 3; }, 8000, "playing");
                    PwKey(w, Keys.Space);
                    w.MinimumSize = Size.Empty;
                    w.ClientSize = new Size(200, 150);
                    MethodInfo re = typeof(PlayerWindow).GetMethod("Redecode", BindingFlags.NonPublic | BindingFlags.Instance);
                    re.Invoke(w, new object[] { true });
                    Check(w.DecodeNow.Width == 200 && w.DecodeNow.Height == 112, "decode size for a 200x150 box is " + w.DecodeNow);
                    PlWait(delegate { lock (seen) return seen.Width[seen.Count - 1] == 200; }, 8000, "a picture at the new size");
                    Check(seen.Height[seen.Count - 1] == 112, "its height " + seen.Height[seen.Count - 1]);
                    Check(engine.State == PlayState.Paused, "a resize keeps it paused");
                    // Playing: the pictures come at the new size too.
                    PwKey(w, Keys.Space);
                    w.ClientSize = new Size(120, 100);
                    re.Invoke(w, new object[] { true });
                    PlWait(delegate { lock (seen) return seen.Width[seen.Count - 1] == 120; }, 8000, "playing pictures at 120 wide");
                    Check(engine.State == PlayState.Playing, "state " + engine.State);
                }
                finally { w.Dispose(); }
                PlDead(engine, 3000);
            });

            Test("playerwindow", "window-draws-the-picture", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlayerWindow w = PwWindow(m.Path, snd, new Settings());
                try
                {
                    w.MinimumSize = Size.Empty;
                    w.ClientSize = new Size(640, 360);
                    IntPtr h = w.Handle;
                    Check(h != IntPtr.Zero, "no handle");
                    PwStills seen = new PwStills();
                    w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(0); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                    w.BeginPlay();
                    PlWait(delegate { return seen.Count >= 3; }, 8000, "pictures");
                    PwKey(w, Keys.Space);
                    long sum = 0;
                    int lit = 0;
                    using (Bitmap shot = new Bitmap(640, 360))
                    {
                        w.DrawToBitmap(shot, new Rectangle(0, 0, 640, 360));
                        for (int y = 40; y < 200; y += 4)
                            for (int x = 40; x < 600; x += 4) { Color c = shot.GetPixel(x, y); sum += c.R + c.G + c.B; if (c.R + c.G + c.B > 60) lit++; }
                        // A look at the window for whoever runs the tests by hand: STACKSHOT_PW_SHOT=<folder> saves PNGs there.
                        string look = Environment.GetEnvironmentVariable("STACKSHOT_PW_SHOT");
                        if (!string.IsNullOrEmpty(look) && Directory.Exists(look))
                        {
                            shot.Save(Path.Combine(look, "player-bar.png"), System.Drawing.Imaging.ImageFormat.Png);
                            PwKey(w, Keys.I);
                            w.DrawToBitmap(shot, new Rectangle(0, 0, 640, 360));
                            shot.Save(Path.Combine(look, "player-info.png"), System.Drawing.Imaging.ImageFormat.Png);
                            PwKey(w, Keys.I);
                        }
                    }
                    Check(lit > 500, "the window is mostly black: " + lit + " lit samples");
                    Add("lit samples " + lit + ", sum " + sum);
                }
                finally { w.Dispose(); }
            });

            Test("playerwindow", "window-unreadable-file-shows-a-message", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("pw-bad");
                string bad = Path.Combine(dir, "broken.mp4");
                File.WriteAllBytes(bad, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });
                PlayerWindow w = PwWindow(bad, new PlFakeSound(), new Settings());
                try
                {
                    Check(w.Message != null && w.Message.StartsWith("No se puede reproducir"), "message: " + w.Message);
                    Check(w.Engine == null, "an unreadable file has no engine");
                    PwKey(w, Keys.Space);
                    PwKey(w, Keys.OemPeriod);
                    PwKey(w, Keys.Left);
                    PwKey(w, Keys.I);
                    w.BeginPlay();
                    Check(w.Message != null, "still the message");
                }
                finally { w.Dispose(); }
                PlayerWindow gone = PwWindow(Path.Combine(dir, "missing.mp4"), new PlFakeSound(), new Settings());
                try { Check(gone.Message == "No se encuentra el archivo.", "missing file: " + gone.Message); }
                finally { gone.Dispose(); }
            });

            Test("playerwindow", "window-opens-another-file-and-leaks-nothing", delegate
            {
                PlMaster m = PlSharedMaster();
                string mp4 = PlMp4("second.mp4", true, 1);
                PlFakeSound snd = new PlFakeSound();
                PlayerWindow w = PwWindow(m.Path, snd, new Settings());
                Playback first = w.Engine;
                Playback second = null;
                try
                {
                    PwStills seen = new PwStills();
                    w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(0); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                    w.BeginPlay();
                    PlWait(delegate { return seen.Count >= 3; }, 8000, "the first file");
                    // A file not read yet is probed off the UI thread and opened from a posted message, which this test
                    // doesn't pump: probed first, it switches at once, as it does for any file already read.
                    MediaInfo.ProbeCached(ffmpeg, mp4);
                    MethodInfo open = typeof(PlayerWindow).GetMethod("OpenAndPlay", BindingFlags.NonPublic | BindingFlags.Instance);
                    open.Invoke(w, new object[] { mp4 });
                    second = w.Engine;
                    Check(second != null && second != first && w.FilePath == mp4, "the window did not switch to the dropped file");
                    int mark = seen.Count;
                    PlWait(delegate { return seen.Count >= mark + 3; }, 8000, "pictures of the second file");
                    Check(w.Media.Audio.Count == 1 && Math.Abs(w.Media.Duration - 1.0) < 0.2, "media of the second file");
                    PlDead(first, 3000);
                }
                finally { w.Dispose(); }
                if (second != null) PlDead(second, 3000);
            });

            Test("playerwindow", "window-real-size-and-fill", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("pw-real");
                string mkv = Path.Combine(dir, "Cine 1888x968.mkv");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=1888x968:r=30:d=1 -pix_fmt gbrp -c:v libx264rgb -qp 0 -preset ultrafast " + Q(mkv), 60000);
                Settings st = new Settings();
                PlayerWindow w = PwWindow(mkv, new PlFakeSound(), st);
                try
                {
                    IntPtr h = w.Handle;
                    Size video = new Size(1888, 968);
                    // Opened at its real size whenever it fits this screen with the frame (on a 1080p laptop it does).
                    Rectangle work = Screen.FromControl(w).WorkingArea;
                    Size frame = new Size(w.Width - w.ClientSize.Width, w.Height - w.ClientSize.Height);
                    bool fits = video.Width + frame.Width <= work.Width && video.Height + frame.Height <= work.Height;
                    if (fits) Check(w.ClientSize == video, "opened at " + w.ClientSize + ", not at the real size " + video + " (work area " + work.Size + ")");
                    else Check(w.ClientSize.Width + frame.Width <= work.Width && w.ClientSize.Height + frame.Height <= work.Height, "larger than the screen: " + w.ClientSize);
                    // A window larger than the video: the picture stays at its real size, centred; "llenar" enlarges it.
                    w.ClientSize = new Size(Math.Min(1900, work.Width - frame.Width), Math.Min(1000, work.Height - frame.Height));
                    Size box = w.ClientSize;
                    Rectangle real = w.PictureRect;
                    if (box.Width >= video.Width && box.Height >= video.Height)
                        Check(real == new Rectangle((box.Width - 1888) / 2, (box.Height - 968) / 2, 1888, 968), "picture " + real + " in " + box);
                    else Check(real.Width <= video.Width && real.Height <= video.Height, "picture " + real + " enlarged in " + box);
                    typeof(PlayerWindow).GetMethod("SetFill", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { true });
                    Check(st.PlayerFill && w.PictureRect == PlayerKit.Letterbox(video, box), "fill: " + w.PictureRect);
                    typeof(PlayerWindow).GetMethod("SetFill", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { false });
                    Check(!st.PlayerFill && w.PictureRect == real, "back to the real size");
                    Add("work area " + work.Size + ", frame " + frame + ", opened at " + (fits ? "real size" : "fit") + "; picture " + real + " in " + box);
                }
                finally { w.Dispose(); }
            });

            Test("playerwindow", "window-exports-a-validated-mp4", delegate
            {
                NeedFfmpeg();
                string clip = PwClip(".avi", "-c:v mpeg4 -q:v 3 -c:a pcm_s16le", 1.5, false, null);
                if (clip == null) Skip("this FFmpeg cannot write .avi");
                PlayerWindow.Quiet = true;
                PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    Check(h != IntPtr.Zero, "no handle");
                    typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
                    Stopwatch sw = Stopwatch.StartNew();
                    while (w.ExportNote == null && sw.ElapsedMilliseconds < 90000) { Application.DoEvents(); Thread.Sleep(10); }
                    Check(w.ExportNote == "ok", "export note: " + w.ExportNote);
                    string outFile = Path.Combine(Path.GetDirectoryName(clip), "clip (para compartir).mp4");
                    Check(File.Exists(outFile), "no file at " + outFile);
                    ShareExpect e = new ShareExpect();
                    e.Width = 320; e.Height = 180; e.Seconds = 1.5; e.HasAudio = true;
                    ShareReport rep = ShareCheck.Validate(ffmpeg, outFile, e);
                    Check(rep.Passed, "the export does not pass ShareCheck: " + rep);
                    string probe = Probe(outFile);
                    Check(probe.Contains("Video: h264 (High)") && probe.Contains("yuv420p") && probe.Contains("aac (LC)") && probe.Contains("48000 Hz"), "profile: " + Tail(probe));
                    Add("exported in " + sw.ElapsedMilliseconds + " ms; " + rep.Checks.Count + " checks");
                }
                finally { w.Dispose(); PlayerWindow.Quiet = false; }
            });

            Test("playerwindow", "window-export-escape-cancels-and-removes-the-file", delegate
            {
                NeedFfmpeg();
                string clip = PwClip(".avi", "-c:v mpeg4 -q:v 3 -c:a pcm_s16le", 20, false, null);
                if (clip == null) Skip("this FFmpeg cannot write .avi");
                PlayerWindow.Quiet = true;
                PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    Check(h != IntPtr.Zero, "no handle");
                    typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
                    string outFile = Path.Combine(Path.GetDirectoryName(clip), "clip (para compartir).mp4");
                    Stopwatch sw = Stopwatch.StartNew();
                    while (!File.Exists(outFile) && sw.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(10); }
                    Check(File.Exists(outFile), "the export never started");
                    Check(PwKey(w, Keys.Escape), "Esc was not taken");
                    Check(!File.Exists(outFile), "the partial file is still there");
                    Thread.Sleep(300);
                    Application.DoEvents();
                    Check(w.ExportNote == null, "a cancelled export must say nothing: " + w.ExportNote);
                    Check(!File.Exists(outFile), "the file came back");
                }
                finally { w.Dispose(); PlayerWindow.Quiet = false; }
            });

            Test("playerwindow", "window-shown-offscreen-bar-hides-and-returns", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlayerWindow w = PwWindow(m.Path, snd, new Settings());
                Playback engine = w.Engine;
                try
                {
                    w.MinimumSize = Size.Empty;
                    w.ClientSize = new Size(480, 270);
                    w.Location = new Point(-6000, -6000);
                    // Loop, so there is something playing for as long as the test needs.
                    typeof(PlayerWindow).GetMethod("ToggleLoop", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
                    FieldInfo alpha = typeof(PlayerWindow).GetField("alpha", BindingFlags.NonPublic | BindingFlags.Instance);
                    w.Show();
                    Stopwatch sw = Stopwatch.StartNew();
                    while (engine.State != PlayState.Playing && sw.ElapsedMilliseconds < 8000) { Application.DoEvents(); Thread.Sleep(5); }
                    Check(engine.State == PlayState.Playing, "the window did not start playing when shown: " + engine.State);
                    Check((float)alpha.GetValue(w) > 0.9f, "the bar must show at the start");
                    sw.Restart();
                    while (sw.ElapsedMilliseconds < 3600) { Application.DoEvents(); Thread.Sleep(5); }
                    float hidden = (float)alpha.GetValue(w);
                    Check(hidden < 0.05f, "the bar is still visible after 3.6 s without moving: " + hidden);
                    Check(engine.State == PlayState.Playing, "still playing: " + engine.State);
                    MethodInfo move = typeof(PlayerWindow).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance);
                    move.Invoke(w, new object[] { new MouseEventArgs(MouseButtons.None, 0, 50, 50, 0) });
                    sw.Restart();
                    while (sw.ElapsedMilliseconds < 600) { Application.DoEvents(); Thread.Sleep(5); }
                    float back = (float)alpha.GetValue(w);
                    Check(back > 0.9f, "the bar did not come back on a mouse move: " + back);
                    // Paused: the bar stays whatever the time.
                    PwKey(w, Keys.Space);
                    sw.Restart();
                    while (sw.ElapsedMilliseconds < 3200) { Application.DoEvents(); Thread.Sleep(5); }
                    Check((float)alpha.GetValue(w) > 0.9f, "the bar hid while paused");
                    w.Close();
                    Check(w.IsDisposed, "closing the window did not dispose it");
                    Add("alpha hidden " + hidden.ToString("0.00", CultureInfo.InvariantCulture) + ", back " + back.ToString("0.00", CultureInfo.InvariantCulture));
                }
                finally { w.Dispose(); }
                PlDead(engine, 3000);
            });
            PlayerWindowEdgeSuite();
        }
    }
}

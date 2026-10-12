// Adversarial tests of the player window and of opening any video: every speed with sound, a speed change while playing
// or paused, loops of very short and half-second files, frame steps at both ends, more formats, unplayable files, a 4K60
// file at a small size, twenty window lifetimes and the routing of every kind of file. Headless, fake sound sink.
// Invoked from PlayerWindowSuite.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        // A file made by FFmpeg with these arguments; null (with a note) when this FFmpeg cannot write it.
        static string PweMake(string name, string args, int timeoutMs)
        {
            string path = Path.Combine(WorkDir("pwe"), name);
            string err;
            int code = Ff("-hide_banner -loglevel error -y " + args + " " + Q(path), out err, timeoutMs);
            if (code != 0) { Add("no encoder for " + name + ": " + Tail(err)); return null; }
            return path;
        }

        // Lets the window's timers and messages run for a while.
        static void PweSpin(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
        }

        // A sound output that keeps a copy of everything written (the fake sink forgets it on every Reset).
        sealed class PweRecord : ISoundOut
        {
            public readonly PlFakeSound Inner = new PlFakeSound();
            readonly List<float> all = new List<float>();
            public bool Open(int rate, int channels) { return Inner.Open(rate, channels); }
            public bool Write(float[] samples, int count)
            {
                bool ok = Inner.Write(samples, count);
                if (ok) lock (all) for (int i = 0; i < count; i++) all.Add(samples[i]);
                return ok;
            }
            public long Position { get { return Inner.Position; } }
            public void Pause() { Inner.Pause(); }
            public void Resume() { Inner.Resume(); }
            public void Reset() { Inner.Reset(); }
            public void Dispose() { Inner.Dispose(); }
            public float[] Samples() { lock (all) return all.ToArray(); }
            public int Count { get { lock (all) return all.Count; } }
        }

        static Playback PweNew(string path, ISoundOut sink, PlLog log)
        {
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "the file does not read as playable: " + path);
            Playback p = new Playback(ffmpeg, path, mi, sink);
            p.FrameReady += delegate(PlayerFrame f) { lock (log) log.Index.Add(PlIndex(f, mi.Width)); lock (log) log.Time.Add(f.Time); };
            p.Ended += delegate { lock (log) log.Ended++; };
            p.Error += delegate(string m) { lock (log) log.Errors.Add(m); };
            return p;
        }

        // Plays one second with sound at a speed: wall time, audio frames against the media length, and the pitch.
        static void PweSpeed(double speed)
        {
            Test("playerwindow", "edge-speed-" + speed.ToString("0.00", CultureInfo.InvariantCulture) + "-with-sound", delegate
            {
                NeedFfmpeg();
                string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 1.0, false, null);
                if (clip == null) Skip("this FFmpeg cannot write mp4");
                PlLog log = new PlLog();
                PweRecord snd = new PweRecord();
                Playback p = PweNew(clip, snd, log);
                try
                {
                    double dur = p.Info.Duration;
                    Check(dur > 0.9 && dur < 1.3, "duration " + PlF(dur));
                    p.Speed = speed;
                    Check(Math.Abs(p.Speed - speed) < 1e-9, "speed reads " + p.Speed);
                    Stopwatch sw = Stopwatch.StartNew();
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, (int)(dur / speed * 2000 + 15000), "the end at " + speed + "x");
                    double wall = sw.Elapsed.TotalSeconds;
                    PlOk(log);
                    Check(p.State == PlayState.Ended, "state " + p.State);
                    double want = dur / speed;
                    // The start-up of FFmpeg and the priming come on top and grow with the load of the machine (1.0 s idle, 2.0 s seen busy).
                    Check(wall >= want * 0.9 && wall <= want + 3.5,"took " + PlF(wall) + " s, a " + speed + "x run of " + PlF(dur) + " s should take about " + PlF(want) + " s");
                    double frames = dur / speed * 48000;
                    long queued = p.AudioFramesQueued;
                    // atempo drops a few tens of milliseconds at the very end, so the tolerance is wide on the short side.
                    Check(Math.Abs(queued - frames) <= frames * 0.06 + 2400, "audio frames " + queued + ", expected about " + frames.ToString("0", CultureInfo.InvariantCulture) + " (" + PlF((queued - frames) / 48000.0) + " s)");
                    Check(Math.Abs(snd.Count / 2 - queued) <= 2400, "the sink holds " + snd.Count / 2 + " frames, the engine counted " + queued);
                    float[] s = snd.Samples();
                    int n = s.Length / 2, start = n / 5, len = n * 3 / 5;
                    double at = PlTonePower(s, start, len, 440), up = PlTonePower(s, start, len, 880), down = PlTonePower(s, start, len, 220);
                    Check(at > 0.02, "no 440 Hz tone in the sound: " + PlF(at));
                    Check(at > 4 * Math.Max(up, down), "the pitch moved: 440 Hz " + PlF(at) + ", 880 Hz " + PlF(up) + ", 220 Hz " + PlF(down));
                    Add("wall " + PlF(wall) + " s, audio " + queued + " frames, 440 Hz " + PlF(at) + ", 880 Hz " + PlF(up) + ", 220 Hz " + PlF(down));
                }
                finally { p.Dispose(); }
                PlDead(p, 3000);
            });
        }

        // The atempo chain of every speed multiplies back to the speed, and the video keeps its frame rate.
        static void PweSpeedArgs()
        {
            Test("playerwindow", "edge-speed-chain-every-speed", delegate
            {
                foreach (double v in PlayerKit.Speeds)
                {
                    string tempo = PlayerArgs.Tempo(v);
                    double prod = 1;
                    if (tempo.Length > 0)
                        foreach (string stage in tempo.Split(','))
                        {
                            Check(stage.StartsWith("atempo=", StringComparison.Ordinal), "not an atempo stage: " + stage);
                            double k = double.Parse(stage.Substring(7), CultureInfo.InvariantCulture);
                            Check(k >= 0.5 - 1e-9 && k <= 2 + 1e-9, "stage out of atempo range: " + stage);
                            prod *= k;
                        }
                    Check(Math.Abs(prod - v) < 1e-6, v + " -> '" + tempo + "' multiplies to " + prod);
                    string a = PlayerArgs.Audio("x.mp4", 0, 0, v);
                    Check(a.EndsWith(" -ac 2 -ar 48000 -f f32le -", StringComparison.Ordinal), "audio tail at " + v + ": " + a);
                    Check(tempo.Length == 0 ? !a.Contains(" -af ") : a.Contains(" -af " + tempo + " -ac 2"), "audio chain at " + v + ": " + a);
                    string vid = PlayerArgs.Video("x.mp4", 0, 320, 180, 30, 320, 180, v, 1);
                    string setpts = "setpts=PTS/" + v.ToString("0.######", CultureInfo.InvariantCulture);
                    Check(v == 1 ? !vid.Contains("setpts") : vid.Contains("-vf " + setpts + " -fps_mode cfr -r 30 "), "video at " + v + ": " + vid);
                }
            });
        }

        // A speed change while playing: the pictures go on from the same place at the new rate, the position never goes back.
        static void PweSpeedWhilePlaying()
        {
            Test("playerwindow", "edge-speed-change-while-playing", delegate
            {
                NeedFfmpeg();
                string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 2.0, false, null);
                if (clip == null) Skip("this FFmpeg cannot write mp4");
                PlLog log = new PlLog();
                Playback p = PlNew(clip, new PlFakeSound(), log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 10; }, 10000, "the first pictures");
                    double before = p.Position;
                    p.Speed = 0.5;
                    int mark;
                    lock (log) mark = log.Count;
                    Check(p.State == PlayState.Playing, "state after a speed change: " + p.State);
                    double prev = -1, worst = 0;
                    Stopwatch sw = Stopwatch.StartNew();
                    while (log.EndedCount == 0 && log.Errors.Count == 0 && sw.ElapsedMilliseconds < 20000)
                    {
                        double pos = p.Position;
                        if (prev >= 0 && pos < prev) worst = Math.Max(worst, prev - pos);
                        prev = Math.Max(prev, pos);
                        Thread.Sleep(10);
                    }
                    PlOk(log);
                    Check(log.EndedCount == 1, "no end within 20 s at 0.5x: state " + p.State);
                    Check(worst <= 1.0 / 30 + 1e-6, "the position went back by " + PlF(worst) + " s");
                    lock (log)
                    {
                        Check(log.Count - mark >= 40, "only " + (log.Count - mark) + " pictures after the change");
                        Check(log.Time[mark] >= before - 0.1 && log.Time[mark] <= before + 0.2, "the new run starts at " + PlF(log.Time[mark]) + ", the change was at " + PlF(before));
                        for (int i = mark + 1; i < log.Count; i++)
                            Check(PwStepOk(log, i, 0.5 / 30), "picture " + i + " is not a whole step of 0.5x after the change");
                    }
                    Add("change at " + PlF(before) + " s, " + (log.Count - mark) + " pictures at 0.5x, back-step " + PlF(worst) + " s");
                }
                finally { p.Dispose(); }
                PlDead(p, 3000);
            });
        }

        // A speed change while paused keeps the picture where it is; the next play runs at the new speed.
        static void PweSpeedWhilePaused()
        {
            Test("playerwindow", "edge-speed-change-while-paused", delegate
            {
                NeedFfmpeg();
                string clip = PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 2.0, false, null);
                if (clip == null) Skip("this FFmpeg cannot write mp4");
                PlLog log = new PlLog();
                Playback p = PlNew(clip, new PlFakeSound(), log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 10; }, 10000, "the first pictures");
                    p.Pause();
                    Check(p.State == PlayState.Paused, "state after pause: " + p.State);
                    Thread.Sleep(150);
                    double at = p.Position;
                    int count0 = log.Count;
                    p.Speed = 2;
                    Thread.Sleep(250);
                    Check(p.State == PlayState.Paused, "state after a speed change while paused: " + p.State);
                    double after = p.Position;
                    Check(Math.Abs(after - at) <= 1.0 / 30 + 0.02, "the position moved while paused: " + PlF(at) + " -> " + PlF(after));
                    Check(log.Count == count0, "pictures came while paused: " + (log.Count - count0));
                    p.Resume();
                    int mark = log.Count;
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 20000, "the end after resuming at 2x");
                    PlOk(log);
                    lock (log)
                    {
                        Check(log.Count - mark >= 12, "only " + (log.Count - mark) + " pictures at 2x after resuming");
                        Check(log.Time[mark] >= after - 0.1 && log.Time[mark] <= after + 0.2, "resumed at " + PlF(log.Time[mark]) + ", paused at " + PlF(after));
                        for (int i = mark + 1; i < log.Count; i++)
                            Check(PwStepOk(log, i, 2.0 / 30), "picture " + i + " is not a whole step of 2x after resuming");
                    }
                    Add("paused at " + PlF(at) + " s, " + (log.Count - mark) + " pictures at 2x");
                }
                finally { p.Dispose(); }
                PlDead(p, 3000);
            });
        }

        // Repeat on: playback goes on for the whole run and loops at least minLoops times, with the picture starting again each time.
        static void PweLoop(string label, Func<string> make, double runSeconds, int minLoops)
        {
            Test("playerwindow", label, delegate
            {
                NeedFfmpeg();
                string clip = make();
                if (clip == null) Skip("this FFmpeg cannot write the clip");
                PlLog log = new PlLog();
                Playback p = PlNew(clip, new PlFakeSound(), log);
                try
                {
                    p.Repeat = true;
                    p.Start(0, 0);
                    Thread.Sleep((int)(runSeconds * 1000));
                    PlOk(log);
                    int restarts = 0;
                    lock (log)
                        for (int i = 1; i < log.Time.Count; i++)
                            if (log.Time[i] < log.Time[i - 1] - 0.2) restarts++;
                    Add("duration " + PlF(p.Info.Duration) + " s, state " + p.State + ", loops " + p.Loops + ", pictures " + log.Count + ", restarts seen " + restarts);
                    Check(p.State == PlayState.Playing, "the loop stopped after " + p.Loops + " loops: state " + p.State + " at " + runSeconds + " s");
                    Check(p.Loops >= minLoops, "only " + p.Loops + " loops in " + runSeconds + " s, expected at least " + minLoops);
                }
                finally { p.Dispose(); }
                PlDead(p, 3000);
            });
        }

        // The picture shown after one frame step or key: the index of the last picture, once it has settled.
        static int PweShown(List<int> seen, Action act)
        {
            int before;
            lock (seen) before = seen.Count;
            act();
            PlWait(delegate { bool more; lock (seen) more = seen.Count > before; return more; }, 8000, "a picture after a frame step");
            Thread.Sleep(150);
            lock (seen) return seen[seen.Count - 1];
        }

        // Frame steps at the first and last frame of the 90-frame master, then after the end.
        static void PweFrameSteps()
        {
            Test("playerwindow", "edge-frame-steps-at-the-first-and-last-frame", delegate
            {
                NeedFfmpeg();
                PlMaster m = PlSharedMaster();
                PlayerWindow w = PwWindow(m.Path, new PlFakeSound(), new Settings());
                Playback eng = w.Engine;
                try
                {
                    Check(w.Message == null, "message " + w.Message);
                    List<int> seen = new List<int>();
                    w.FrameHook = delegate(PlayerFrame f) { int idx = PlIndex(f, 320); lock (seen) seen.Add(idx); };
                    Check(PweShown(seen, delegate { w.Step(-1); }) == 0, "a step back at the first frame must stay on frame 0");
                    Check(PweShown(seen, delegate { w.Step(1); }) == 1, "a step forward from frame 0 must show frame 1");
                    Check(PweShown(seen, delegate { w.Step(-1); }) == 0, "a step back to frame 0");
                    Check(PweShown(seen, delegate { PwKey(w, Keys.End); }) == 89, "End must show the last frame (89)");
                    Check(Math.Abs(w.Engine.Position - 89.0 / 30) < 0.01, "position at the end: " + w.Engine.Position);
                    Check(PweShown(seen, delegate { w.Step(1); }) == 89, "a step forward at the last frame must stay on it");
                    Check(PweShown(seen, delegate { w.Step(1); }) == 89, "a second step forward at the last frame must stay on it");
                    Check(PweShown(seen, delegate { w.Step(-1); }) == 88, "a step back from the last frame must show 88");
                    Add("steps 0,1,0,0 then End 89, 89, 89, 88");
                }
                finally { w.Dispose(); }
                PlDead(eng, 3000);
            });

            Test("playerwindow", "edge-frame-step-after-the-end", delegate
            {
                NeedFfmpeg();
                PlMaster m = PlSharedMaster();
                PlayerWindow w = PwWindow(m.Path, new PlFakeSound(), new Settings());
                Playback eng = w.Engine;
                try
                {
                    List<int> seen = new List<int>();
                    w.FrameHook = delegate(PlayerFrame f) { int idx = PlIndex(f, 320); lock (seen) seen.Add(idx); };
                    w.BeginPlay();
                    PlWait(delegate { return w.Engine.State == PlayState.Ended || w.Message != null; }, 20000, "the end of the master");
                    Check(w.Message == null, "message " + w.Message);
                    Check(w.Engine.State == PlayState.Ended, "state " + w.Engine.State);
                    Check(PweShown(seen, delegate { w.Step(-1); }) == 89, "a step back right after the end must show the last frame (89)");
                    Check(PweShown(seen, delegate { w.Step(1); }) == 89, "a step forward after the end must stay on the last frame");
                }
                finally { w.Dispose(); }
                PlDead(eng, 3000);
            });
        }

        // Windows made and closed twenty times, some while playing and some at once: no FFmpeg left behind.
        static void PweLifetimes()
        {
            Test("playerwindow", "edge-window-twenty-lifetimes-leave-no-ffmpeg", delegate
            {
                NeedFfmpeg();
                PlMaster m = PlSharedMaster();
                int started = 0;
                for (int i = 0; i < 20; i++)
                {
                    PlayerWindow w = PwWindow(m.Path, new PlFakeSound(), new Settings());
                    Playback e = w.Engine;
                    try
                    {
                        Check(e != null, "no engine in round " + i + ": " + w.Message);
                        w.BeginPlay();
                        if (i % 2 == 1)
                        {
                            Thread.Sleep(100 + 60 * (i % 4));
                            PwKey(w, Keys.Space);
                            PwKey(w, Keys.OemPeriod);
                            PwKey(w, Keys.Right);
                            PwKey(w, Keys.Space);
                        }
                    }
                    finally { w.Dispose(); }
                    started += e.ProcessIds().Length;
                    PlDead(e, 3000);
                }
                Add(started + " FFmpeg processes started over 20 windows, all gone");
            });
        }

        // A file that cannot be played: a message in the window, no engine, keys that do nothing, no crash.
        static void PweUnplayable(string label, Func<string> make, string expect)
        {
            Test("playerwindow", label, delegate
            {
                NeedFfmpeg();
                string path = make();
                if (path == null) Skip("this FFmpeg cannot write the file");
                PlayerWindow w = PwWindow(path, new PlFakeSound(), new Settings());
                try
                {
                    Check(w.Engine == null, "an engine was made for a file that cannot play");
                    Check(w.Message != null && w.Message.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0, "message: '" + w.Message + "'");
                    PwKey(w, Keys.Space);
                    PwKey(w, Keys.Right);
                    PwKey(w, Keys.OemPeriod);
                    PwKey(w, Keys.End);
                    PwKey(w, Keys.M);
                    PwKey(w, Keys.Up);
                    w.BeginPlay();
                    w.Step(1);
                    Check(w.Engine == null, "an engine appeared after the keys");
                    Add("message '" + w.Message + "'");
                }
                finally { w.Dispose(); }
            });
        }

        // A 4K60 file in a small window: the pictures come at the small size, late ones are dropped, nothing stalls.
        static void PweUhd()
        {
            Test("playerwindow", "edge-4k60-small-window-drops-without-stalling", delegate
            {
                NeedFfmpeg();
                string clip = PweMake("uhd60.mp4", "-f lavfi -i \"testsrc2=s=3840x2160:r=60:d=2\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an", 300000);
                if (clip == null) Skip("this FFmpeg cannot write 4K");
                PlayerWindow w = PwWindow(clip, new PlFakeSound(), new Settings());
                Playback e = w.Engine;
                try
                {
                    Check(e != null, "no engine: " + w.Message);
                    Check(w.Media.Width == 3840 && w.Media.Height == 2160 && Math.Abs(w.Media.Fps - 60) < 0.01, "4K60 reads as " + w.Media.Width + "x" + w.Media.Height + " at " + w.Media.Fps);
                    List<int> numbers = new List<int>(), widths = new List<int>();
                    w.FrameHook = delegate(PlayerFrame f) { lock (numbers) { numbers.Add(f.Number); widths.Add(f.Width); } };
                    w.ClientSize = new Size(480, 270);
                    PweSpin(800);
                    Check(w.DecodeNow.Width > 0 && w.DecodeNow.Width <= 480, "decode size after the resize: " + w.DecodeNow);
                    // The resize asked for one still picture (number 0, not part of the run): it is not counted.
                    lock (numbers) { numbers.Clear(); widths.Clear(); }
                    Stopwatch sw = Stopwatch.StartNew();
                    w.BeginPlay();
                    PlWait(delegate { return e.State == PlayState.Ended || w.Message != null; }, 90000, "the end of the 4K file");
                    double wall = sw.Elapsed.TotalSeconds;
                    Check(w.Message == null, "message: " + w.Message);
                    Check(e.State == PlayState.Ended, "state " + e.State);
                    Check(wall < 60, "the 2 s file took " + PlF(wall) + " s");
                    lock (numbers)
                    {
                        for (int i = 0; i < widths.Count; i++) Check(widths[i] <= 480, "picture " + i + " is " + widths[i] + " px wide");
                        for (int i = 1; i < numbers.Count; i++) Check(numbers[i] > numbers[i - 1], "pictures out of order at " + i);
                        Check(numbers.Count == e.FramesShown, "shown " + e.FramesShown + ", counted " + numbers.Count);
                    }
                    Check(e.FramesShown + e.FramesDropped <= e.FramesDecoded, "shown " + e.FramesShown + " + dropped " + e.FramesDropped + " > decoded " + e.FramesDecoded);
                    Add("decoded " + e.FramesDecoded + ", shown " + e.FramesShown + ", dropped " + e.FramesDropped + " in " + PlF(wall) + " s");
                }
                finally { w.Dispose(); }
                PlDead(e, 3000);
            });
        }

        // Each of these opens with a message and no crash.
        static void PweUnplayables()
        {
            PweUnplayable("edge-audio-only-m4a-clear-message", delegate
            {
                return PweMake("solo-audio.m4a", "-f lavfi -i \"sine=f=440:d=1\" -c:a aac", 60000);
            }, "No se puede reproducir este archivo");

            PweUnplayable("edge-cover-art-mp3-is-not-a-video", delegate
            {
                return PweMake("con-portada.mp3", "-f lavfi -i \"sine=f=440:d=1\" -f lavfi -i \"testsrc2=s=64x64\" -map 0:a -map 1:v -c:a libmp3lame -c:v mjpeg -frames:v 1 -disposition:v:0 attached_pic -t 1", 60000);
            }, "No se puede reproducir este archivo");

            PweUnplayable("edge-renamed-text-file", delegate
            {
                string path = Path.Combine(WorkDir("pwe-text"), "falso.mp4");
                File.WriteAllText(path, "Esto no es un v\u00EDdeo, es texto.\r\n", Encoding.UTF8);
                return path;
            }, "No se puede reproducir este archivo");

            PweUnplayable("edge-zero-byte-file", delegate
            {
                string path = Path.Combine(WorkDir("pwe-empty"), "vacio.mp4");
                File.WriteAllBytes(path, new byte[0]);
                return path;
            }, "No se puede reproducir este archivo");

            PweUnplayable("edge-missing-file", delegate
            {
                return Path.Combine(WorkDir("pwe-missing"), "no-existe.mp4");
            }, "No se encuentra el archivo");
        }

        // A format of the list the other suites do not play: with its picture count and its sound.
        static void PweFormat(string name, string ext, string codecs, string mux, int expectFrames)
        {
            Test("playerwindow", "edge-format-" + name, delegate
            {
                NeedFfmpeg();
                string clip = PwClip(ext, codecs, 1.2, false, mux);
                if (clip == null) Skip("this FFmpeg cannot write " + ext);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, clip);
                Check(mi.CanPlay && mi.Width == 320 && mi.Height == 180, ext + " reads as " + mi.Width + "x" + mi.Height + ", playable " + mi.CanPlay);
                Check(mi.Audio.Count > 0, ext + " shows no audio track");
                PlLog log = new PlLog();
                Playback p = PlNew(clip, new PlFakeSound(), log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 30000, "the end of " + ext);
                    PlOk(log);
                    Check(p.State == PlayState.Ended, ext + " state " + p.State);
                    Check(Math.Abs(p.FramesDecoded - expectFrames) <= 1, ext + " decoded " + p.FramesDecoded + " frames, expected " + expectFrames);
                    Check(p.AudioFramesQueued >= 0.85 * 1.2 * 48000, ext + " sound: only " + p.AudioFramesQueued + " frames of about " + (int)(1.2 * 48000));
                    Check(p.AudioFramesQueued <= 1.15 * 1.2 * 48000 + 9600, ext + " sound too long: " + p.AudioFramesQueued + " frames");
                    Add(p.FramesDecoded + " frames, sound " + p.AudioFramesQueued + " frames, " + mi.Audio.Count + " track(s)");
                }
                finally { p.Dispose(); }
                PlDead(p, 3000);
            });
        }

        // Routing: which window or program a file opens in, for every kind of name.
        static readonly string[] PweKnownVideos =
        {
            "C:\\v\\clip.mp4", "C:\\v\\CLIP.MP4", "C:\\v\\clip.m4v", "C:\\v\\clip.mov", "C:\\v\\clip.webm", "C:\\v\\clip.avi",
            "C:\\v\\clip.wmv", "C:\\v\\clip.flv", "C:\\v\\clip.mpg", "C:\\v\\clip.ts", "C:\\v\\clip.mts", "C:\\v\\clip.m2ts",
            "C:\\v\\clip.3gp", "C:\\v\\clip.ogv", "C:\\v\\Cine 2026-10-10 12.53.58.mkv", "C:\\v\\CINE 2026-10-10 12.53.58.MKV"
        };

        static readonly string[] PweImages = { "C:\\v\\foto.png", "C:\\v\\foto.JPG", "C:\\v\\captura.bmp", null, "" };

        static void PweRouting()
        {
            Test("playerwindow", "edge-route-every-known-video-extension", delegate
            {
                StringBuilder bad = new StringBuilder();
                int n = 0;
                foreach (string path in PweKnownVideos)
                    foreach (bool on in new bool[] { true, false })
                    {
                        n++;
                        bool cine = path.IndexOf("Cine ", StringComparison.OrdinalIgnoreCase) >= 0;
                        ShotStack.OpenRoute want = on || cine ? ShotStack.OpenRoute.Player : ShotStack.OpenRoute.Windows;
                        ShotStack.OpenRoute got = ShotStack.RouteFor(path, on);
                        if (got != want) bad.Append(" [" + path + ", switch " + on + " -> " + got + "]");
                    }
                foreach (string path in PweImages)
                    foreach (bool on in new bool[] { true, false })
                    {
                        n++;
                        if (ShotStack.RouteFor(path, on) != ShotStack.OpenRoute.Editor) bad.Append(" [image " + path + ", switch " + on + "]");
                    }
                Check(bad.Length == 0, "wrong routes:" + bad);
                Add(n + " combinations");
            });

            // Expected by the spec: a plain .mkv is not a Cine master, so with the switch off it goes to Windows.
            Test("playerwindow", "edge-route-plain-mkv-follows-the-switch", delegate
            {
                ShotStack.OpenRoute off = ShotStack.RouteFor("C:\\v\\pelicula.mkv", false);
                Check(off == ShotStack.OpenRoute.Windows, "a plain .mkv with the switch off goes to " + off + ", not Windows");
                Check(ShotStack.RouteFor("C:\\v\\pelicula.mkv", true) == ShotStack.OpenRoute.Player, "a plain .mkv with the switch on is not in the player");
            });

            // Expected by the spec: an extension FFmpeg reads is probed, so it is never sent to the editor, which cannot open video.
            Test("playerwindow", "edge-route-unknown-video-extension-not-editor", delegate
            {
                string[] unknown = { "C:\\v\\clip.m2t", "C:\\v\\clip.m2v", "C:\\v\\clip.qt", "C:\\v\\clip.mpv" };
                StringBuilder bad = new StringBuilder();
                foreach (string path in unknown)
                    foreach (bool on in new bool[] { true, false })
                        if (ShotStack.RouteFor(path, on) == ShotStack.OpenRoute.Editor) bad.Append(" [" + path + ", switch " + on + "]");
                Check(bad.Length == 0, "video the editor cannot open goes to the editor:" + bad);
            });

            Test("playerwindow", "edge-video-filter-and-editor-formats", delegate
            {
                string f = ShotStack.VideoFilter;
                foreach (string ext in ShotStack.VideoExtensions) Check(f.Contains("*" + ext), "the dialog filter misses " + ext);
                foreach (string ext in PlayerKit.EditorFormats) Check(Array.IndexOf(ShotStack.VideoExtensions, ext) >= 0, "editor format not in the video list: " + ext);
                Check(ShotStack.IsVideoFile("C:\\x\\CLIP.MTS") && ShotStack.IsVideoFile("C:\\x\\clip.Mp4"), "upper or mixed case is not a video");
                Check(!ShotStack.IsVideoFile("C:\\x\\nota.txt") && !ShotStack.IsVideoFile(null) && !ShotStack.IsVideoFile(""), "a text file is a video");
                Check(f.EndsWith("|Todos los archivos|*.*", StringComparison.Ordinal), "the filter lost 'all files': " + f);
            });
        }

        static void PlayerWindowEdgeSuite()
        {
            PweSpeedArgs();
            foreach (double v in PlayerKit.Speeds) PweSpeed(v);
            PweSpeedWhilePlaying();
            PweSpeedWhilePaused();

            PweLoop("edge-loop-one-frame-file", delegate
            {
                return PweMake("um-cuadro.mp4", "-f lavfi -i \"testsrc2=s=320x180:r=30\" -frames:v 1 -c:v libx264 -pix_fmt yuv420p -an", 60000);
            }, 1.5, 3);
            PweLoop("edge-loop-half-second-silent", delegate
            {
                return PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p", 0.5, true, null);
            }, 3.0, 3);
            PweLoop("edge-loop-half-second-with-sound", delegate
            {
                return PwClip(".mp4", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", 0.5, false, null);
            }, 5.0, 3);   // each loop restarts two FFmpeg processes: 3 s was too tight on a busy machine

            PweFrameSteps();

            PweFormat("webm-vp9-opus", ".webm", "-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -b:v 300k -c:a libopus", null, 36);
            PweFormat("avi-mpeg4-mp3", ".avi", "-c:v mpeg4 -q:v 4 -c:a libmp3lame", null, 36);
            PweFormat("mov-prores-pcm", ".mov", "-c:v prores_ks -profile:v 1 -pix_fmt yuv422p10le -c:a pcm_s16le", null, 36);
            PweFormat("wmv-msmpeg4-wmav2", ".wmv", "-c:v msmpeg4 -b:v 1M -c:a wmav2", null, 36);
            PweFormat("flv-flv1-mp3", ".flv", "-c:v flv1 -pix_fmt yuv420p -c:a libmp3lame", null, 36);
            PweFormat("ts-h264-aac", ".ts", "-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac", null, 36);
            PwFormat("gif-10fps-animated", ".gif", "-vf fps=10", null, true, 12);

            PweUnplayables();
            PweUhd();
            PweLifetimes();
            PweRouting();
        }
    }
}

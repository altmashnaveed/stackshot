// Tests of the built-in player (decoding, clock, seeking, sound, cleanup). Headless: the sound goes to a fake sink
// except in one optional test that opens the real waveOut device and plays silence.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        // A sink that counts time like a sound card: real time (times Speed) while resumed and never past what was
        // written, or, in Manual mode, exactly what the test sets. Writes block while 200 ms are queued.
        sealed class PlFakeSound : ISoundOut
        {
            public bool Manual, FailOpen;
            public double Speed = 1;
            public volatile bool Opened, Disposed, Paused = true;
            public int ResetCount, WriteCount;
            readonly object gate = new object();
            readonly List<float> data = new List<float>();
            long written, manualPos;
            double playedD;
            readonly Stopwatch sw = Stopwatch.StartNew();
            long lastTicks;
            int epoch;

            public bool Open(int rate, int channels)
            {
                if (FailOpen) return false;
                Opened = true;
                return true;
            }

            void Advance()
            {
                long now = sw.ElapsedTicks;
                if (!Paused && !Manual)
                {
                    double dt = (now - lastTicks) / (double)Stopwatch.Frequency;
                    playedD = Math.Min(written, playedD + dt * 48000 * Speed);
                }
                lastTicks = now;
            }

            long PosLocked()
            {
                Advance();
                return Manual ? manualPos : (long)playedD;
            }

            public long Position { get { lock (gate) return PosLocked(); } }
            public void SetManual(long frames) { lock (gate) manualPos = frames; }

            public bool Write(float[] samples, int count)
            {
                int ep;
                lock (gate) ep = epoch;
                while (true)
                {
                    lock (gate)
                    {
                        if (epoch != ep || Disposed) return false;
                        if (written - PosLocked() <= 9600)
                        {
                            for (int i = 0; i < count; i++) data.Add(samples[i]);
                            written += count / 2;
                            WriteCount++;
                            return true;
                        }
                    }
                    Thread.Sleep(2);
                }
            }

            public void Pause() { lock (gate) { Advance(); Paused = true; } }
            public void Resume() { lock (gate) { Advance(); Paused = false; } }

            public void Reset()
            {
                lock (gate)
                {
                    epoch++;
                    data.Clear();
                    written = 0;
                    playedD = 0;
                    manualPos = 0;
                    Paused = true;
                    ResetCount++;
                    lastTicks = sw.ElapsedTicks;
                }
            }

            public void Dispose() { lock (gate) { Disposed = true; epoch++; } }
            public float[] Samples() { lock (gate) return data.ToArray(); }
            public int SampleCount { get { lock (gate) return data.Count; } }
        }

        sealed class PlLog
        {
            public readonly List<int> Index = new List<int>(), Number = new List<int>();
            public readonly List<double> Time = new List<double>();
            public readonly List<long> Ms = new List<long>();
            public readonly List<string> Errors = new List<string>(), Notices = new List<string>();
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public int Ended;
            public int Count { get { lock (this) return Index.Count; } }
            public int EndedCount { get { lock (this) return Ended; } }
        }

        sealed class PlMaster
        {
            public string Path, Sha;
            public DateTime Mtime;
            public int W, H, Frames, FpsNum;
            public double Fps, Seconds;
        }

        // Frame index in the picture: 16 blocks of 8 x 8 px across the top-left, white = 1, over a dark gradient.
        static void PlPaint(byte[] buf, int w, int h, int index)
        {
            for (int y = 0; y < h; y++)
            {
                byte v = (byte)(40 + y * 60 / h);
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    buf[i] = v; buf[i + 1] = v; buf[i + 2] = v; buf[i + 3] = 0;
                }
            }
            for (int b = 0; b < 16; b++)
            {
                byte val = (byte)(((index >> b) & 1) != 0 ? 255 : 0);
                for (int y = 0; y < 8; y++)
                    for (int x = b * 8; x < b * 8 + 8; x++)
                    {
                        int i = (y * w + x) * 4;
                        buf[i] = val; buf[i + 1] = val; buf[i + 2] = val;
                    }
            }
        }

        static int PlIndex(PlayerFrame f, int nativeW)
        {
            double sc = f.Width / (double)nativeW;
            int v = 0;
            for (int b = 0; b < 16; b++)
            {
                int x = (int)((b * 8 + 4) * sc), y = (int)(4 * sc);
                if (f.Data[(y * f.Width + x) * 4 + 2] > 128) v |= 1 << b;
            }
            return v;
        }

        static void PlTone(string path, long frames, double hz)
        {
            float[] s = new float[frames * 2];
            for (long i = 0; i < frames; i++) s[2 * i] = s[2 * i + 1] = (float)(0.4 * Math.Sin(2 * Math.PI * hz * i / 48000.0));
            byte[] b = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, b, 0, b.Length);
            File.WriteAllBytes(path, b);
        }

        // A lossless master built through the real Cine path (CineSink, CineMaster.Build): 440 Hz on the system track,
        // 880 Hz on the microphone, the mix first.
        static PlMaster PlBuild(int w, int h, int fpsNum, int frames, bool audio)
        {
            string dir = WorkDir("plmaster");
            PlMaster m = new PlMaster();
            m.W = w; m.H = h; m.Frames = frames; m.FpsNum = fpsNum; m.Fps = fpsNum; m.Seconds = frames / (double)fpsNum;
            string nut = Path.Combine(dir, "video.nut");
            m.Path = Path.Combine(dir, "Cine test.mkv");
            int bytes = w * h * 4;
            byte[] managed = new byte[bytes];
            IntPtr mem = Marshal.AllocHGlobal(bytes);
            CineSink sink = new CineSink(ffmpeg, w, h, fpsNum, 1, nut);
            try
            {
                sink.Start();
                for (int f = 0; f < frames; f++)
                {
                    PlPaint(managed, w, h, f);
                    Marshal.Copy(managed, 0, mem, bytes);
                    if (!sink.Write(mem)) throw new TestFailed("CineSink.Write failed at " + f + ": " + sink.ErrorTail);
                }
                int code;
                if (!sink.Close(120000, out code) || code != 0) throw new TestFailed("CineSink.Close: " + code + " " + sink.ErrorTail);
            }
            finally
            {
                sink.Dispose();
                Marshal.FreeHGlobal(mem);
            }
            string sys = null, mic = null;
            if (audio)
            {
                long af = Cine.AudioFramesFor(frames, fpsNum, 1);
                sys = Path.Combine(dir, "sys.f32");
                mic = Path.Combine(dir, "mic.f32");
                PlTone(sys, af, 440);
                PlTone(mic, af, 880);
            }
            string err = CineMaster.Build(ffmpeg, nut, sys, mic, "", m.Path, m.Seconds, null);
            if (err != null) throw new TestFailed("CineMaster.Build: " + err);
            m.Sha = CineMaster.Sha256File(m.Path);
            m.Mtime = File.GetLastWriteTimeUtc(m.Path);
            return m;
        }

        static PlMaster plShared;

        static PlMaster PlSharedMaster()
        {
            NeedFfmpeg();
            if (plShared == null) plShared = PlBuild(320, 180, 30, 90, true);
            return plShared;
        }

        static string PlMp4(string name, bool audio, double seconds)
        {
            string dir = WorkDir("plmp4");
            string mp4 = Path.Combine(dir, name);
            string secs = seconds.ToString(CultureInfo.InvariantCulture);
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=" + secs + "\"" +
                 (audio ? " -f lavfi -i \"sine=f=440:d=" + secs + "\"" : "") +
                 " -c:v libx264 -preset ultrafast -pix_fmt yuv420p" + (audio ? " -c:a aac -shortest" : " -an") + " " + Q(mp4), 60000);
            return mp4;
        }

        static void PlWait(Func<bool> cond, int ms, string what)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > ms) throw new TestFailed("timed out waiting for " + what);
                Thread.Sleep(2);
            }
        }

        static Playback PlNew(string path, PlFakeSound snd, PlLog log)
        {
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "the file does not read as playable: " + path);
            Playback p = new Playback(ffmpeg, path, mi, snd);
            p.FrameReady += delegate(PlayerFrame f)
            {
                int idx = PlIndex(f, mi.Width);
                lock (log)
                {
                    log.Index.Add(idx);
                    log.Number.Add(f.Number);
                    log.Time.Add(f.Time);
                    log.Ms.Add(log.Clock.ElapsedMilliseconds);
                }
            };
            p.Ended += delegate { lock (log) log.Ended++; };
            p.Error += delegate(string m) { lock (log) log.Errors.Add(m); };
            p.Notice += delegate(string m) { lock (log) log.Notices.Add(m); };
            return p;
        }

        static double PlTonePower(float[] x, int startFrame, int frames, double hz)
        {
            double w = 2 * Math.PI * hz / 48000.0, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = 0; i < frames; i++)
            {
                double s0 = x[(startFrame + i) * 2] + c * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            return Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - c * s1 * s2)) / (frames / 2.0);
        }

        static string PlF(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        static void PlDead(Playback p, int waitMs)
        {
            foreach (int pid in p.ProcessIds())
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (true)
                {
                    bool alive = false;
                    try { using (Process pr = Process.GetProcessById(pid)) alive = !pr.HasExited; }
                    catch (ArgumentException) { alive = false; }
                    catch (InvalidOperationException) { alive = false; }
                    if (!alive) break;
                    if (sw.ElapsedMilliseconds > waitMs) throw new TestFailed("ffmpeg " + pid + " is still alive");
                    Thread.Sleep(20);
                }
            }
        }

        static void PlOk(PlLog log) { Check(log.Errors.Count == 0, "player error: " + (log.Errors.Count > 0 ? log.Errors[0] : "")); }

        static readonly string PlUserMasterProbe =
            "Input #0, matroska,webm, from 'C:\\Users\\x\\Videos\\Cine 2026-10-10 12.53.58.mkv':\n" +
            "  Metadata:\n" +
            "    comment         : Stackshot Cine: v\u00EDdeo H.264 RGB sin p\u00E9rdidas y sonido PCM de 32 bits\n" +
            "    encoder         : Lavf62.3.100\n" +
            "  Duration: 00:01:12.40, start: 0.000000, bitrate: 41570 kb/s\n" +
            "  Stream #0:0: Video: h264 (High 4:4:4 Predictive), gbrp(pc, gbr/bt709/iec61966-2-1, progressive), 1730x786, 30 fps, 30 tbr, 1k tbn (default)\n" +
            "    Metadata:\n" +
            "      ENCODER         : Lavc62.11.100 libx264rgb\n" +
            "      DURATION        : 00:01:12.400000000\n" +
            "  Stream #0:1: Audio: pcm_f32le, 48000 Hz, stereo, flt, 3072 kb/s (default)\n" +
            "    Metadata:\n" +
            "      title           : Mezcla (equipo + micr\u00F3fono)\n" +
            "      ENCODER         : Lavc62.11.100 pcm_f32le\n" +
            "  Stream #0:2: Audio: pcm_f32le, 48000 Hz, stereo, flt, 3072 kb/s\n" +
            "    Metadata:\n" +
            "      title           : Sonido del equipo (original)\n" +
            "  Stream #0:3: Audio: pcm_f32le, 48000 Hz, stereo, flt, 3072 kb/s\n" +
            "    Metadata:\n" +
            "      title           : Micr\u00F3fono (original)\n" +
            "At least one output file must be specified\n";

        static readonly string PlMp4Probe =
            "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'Cine 2026-10-10 12.53.58 (para compartir).mp4':\n" +
            "  Metadata:\n" +
            "    major_brand     : isom\n" +
            "    encoder         : Lavf62.3.100\n" +
            "  Duration: 00:01:12.40, start: 0.000000, bitrate: 5163 kb/s\n" +
            "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709/bt709/iec61966-2-1, progressive), 1730x786 [SAR 1:1 DAR 865:393], 4830 kb/s, 30 fps, 30 tbr, 15360 tbn (default)\n" +
            "    Metadata:\n" +
            "      handler_name    : VideoHandler\n" +
            "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 320 kb/s (default)\n" +
            "    Metadata:\n" +
            "      handler_name    : SoundHandler\n" +
            "At least one output file must be specified\n";

        static void PlayerSuite()
        {
            Test("player", "parse-user-master-text", delegate
            {
                MediaInfo m = MediaInfo.Parse(PlUserMasterProbe);
                Check(m.CanPlay, "not playable");
                Check(Math.Abs(m.Duration - 72.40) < 1e-9, "duration " + m.Duration);
                Check(m.Width == 1730 && m.Height == 786, "size " + m.Width + "x" + m.Height);
                Check(m.Fps == 30, "fps " + m.Fps);
                Check(m.PixelFormat == "gbrp", "pix_fmt " + m.PixelFormat);
                Check(m.Audio.Count == 3, "audio tracks " + m.Audio.Count);
                Check(m.Audio[0].Title == Cine.TitleMix && m.Audio[1].Title == Cine.TitleSystem && m.Audio[2].Title == Cine.TitleMic,
                      "titles: " + m.Audio[0].Title + " | " + m.Audio[1].Title + " | " + m.Audio[2].Title);
                Check(m.Audio[0].Default && !m.Audio[1].Default && !m.Audio[2].Default, "default flags");
                Check(m.Audio[0].Index == 0 && m.Audio[1].Index == 1 && m.Audio[2].Index == 2, "indices");
            });

            Test("player", "parse-mp4-text", delegate
            {
                MediaInfo m = MediaInfo.Parse(PlMp4Probe);
                Check(m.CanPlay && m.Width == 1730 && m.Height == 786 && m.Fps == 30 && m.PixelFormat == "yuv420p", "video " + m.Width + "x" + m.Height + " " + m.Fps + " " + m.PixelFormat);
                Check(Math.Abs(m.Duration - 72.40) < 1e-9, "duration " + m.Duration);
                Check(m.Audio.Count == 1 && m.Audio[0].Default && m.Audio[0].Title == null, "audio");
            });

            Test("player", "parse-odd-inputs", delegate
            {
                Check(!MediaInfo.Parse(null).CanPlay && !MediaInfo.Parse("").CanPlay, "null or empty");
                MediaInfo none = MediaInfo.Parse("C:\\x\\missing.mp4: No such file or directory\n");
                Check(!none.CanPlay && none.Audio.Count == 0 && none.Duration == 0, "missing file");
                MediaInfo gif = MediaInfo.Parse("  Duration: 00:00:03.20, start: 0.000000, bitrate: 1200 kb/s\n  Stream #0:0: Video: gif, bgra, 800x600, 12.50 fps, 12.50 tbr, 100 tbn\n");
                Check(gif.CanPlay && gif.Width == 800 && gif.Height == 600 && gif.Fps == 12.5 && gif.PixelFormat == "bgra" && gif.Audio.Count == 0, "gif " + gif.Fps + " " + gif.PixelFormat);
                MediaInfo tbr = MediaInfo.Parse("  Stream #0:0: Video: vp9, yuv420p(tv, progressive), 640x360, 25 tbr, 1k tbn\n");
                Check(tbr.CanPlay && tbr.Fps == 25, "tbr fallback " + tbr.Fps);
                MediaInfo cover = MediaInfo.Parse("  Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 128 kb/s\n  Stream #0:1: Video: mjpeg (Baseline), yuvj420p, 500x500, 90k tbr, 90k tbn (attached pic)\n");
                Check(!cover.CanPlay && cover.Audio.Count == 1, "attached picture is not a video");
                // The output side of a command that also writes a file repeats the stream lines.
                MediaInfo both = MediaInfo.Parse("Input #0, x\n  Duration: 00:00:02.00, start: 0\n  Stream #0:0: Video: h264, yuv420p, 320x180, 30 fps\nOutput #0, image2, to 'o.png':\n  Stream #0:0: Video: png, rgb24, 1x1, 0.03 fps\n");
                Check(both.Width == 320 && both.Fps == 30, "output side leaked: " + both.Width + " " + both.Fps);
            });

            Test("player", "parse-real-ffmpeg-output", delegate
            {
                PlMaster m = PlSharedMaster();
                MediaInfo mi = MediaInfo.Probe(ffmpeg, m.Path);
                Check(mi.CanPlay && mi.Width == 320 && mi.Height == 180 && mi.Fps == 30 && mi.PixelFormat == "gbrp", "master video " + mi.Width + "x" + mi.Height + " " + mi.Fps + " " + mi.PixelFormat);
                Check(Math.Abs(mi.Duration - 3.0) < 0.06, "master duration " + mi.Duration);
                Check(mi.Audio.Count == 3, "master tracks " + mi.Audio.Count);
                Check(mi.Audio[0].Title == Cine.TitleMix && mi.Audio[1].Title == Cine.TitleSystem && mi.Audio[2].Title == Cine.TitleMic,
                      "master titles: " + mi.Audio[0].Title + " | " + mi.Audio[1].Title + " | " + mi.Audio[2].Title + " in " + Tail(Probe(m.Path)));
                Check(mi.Audio[0].Default && !mi.Audio[1].Default && !mi.Audio[2].Default, "master default flags");
                string mp4 = PlMp4("with-audio.mp4", true, 2);
                MediaInfo a = MediaInfo.Probe(ffmpeg, mp4);
                Check(a.CanPlay && a.Width == 320 && a.Height == 180 && a.Fps == 30 && a.PixelFormat == "yuv420p" && a.Audio.Count == 1, "mp4 " + a.Width + " " + a.Fps + " " + a.PixelFormat + " audio " + a.Audio.Count);
                Check(Math.Abs(a.Duration - 2.0) < 0.1, "mp4 duration " + a.Duration);
                string silent = PlMp4("silent.mp4", false, 2);
                MediaInfo s = MediaInfo.Probe(ffmpeg, silent);
                Check(s.CanPlay && s.Audio.Count == 0, "silent mp4 audio " + s.Audio.Count);
            });

            Test("player", "args-exact", delegate
            {
                string safe = Recorder.SafeInput;
                string src = "C:\\a b\\x.mkv";
                string head = "-hide_banner -loglevel error -nostdin -ss ";
                Check(PlayerArgs.Video(src, 1.5, 320, 180, 30, 320, 180) ==
                      head + "1.500 " + safe + "-i \"C:\\a b\\x.mkv\" -map 0:v:0 -an -sn -dn -fps_mode cfr -r 30 -f rawvideo -pix_fmt bgra -", "native: " + PlayerArgs.Video(src, 1.5, 320, 180, 30, 320, 180));
                Check(PlayerArgs.Video(src, 0, 160, 90, 30000.0 / 1001, 320, 180) ==
                      "-hide_banner -loglevel error -nostdin " + safe + "-i \"C:\\a b\\x.mkv\" -map 0:v:0 -an -sn -dn -vf scale=160:90:flags=bilinear -fps_mode cfr -r 29.97003 -f rawvideo -pix_fmt bgra -",
                      "scaled: " + PlayerArgs.Video(src, 0, 160, 90, 30000.0 / 1001, 320, 180));
                // No -ss at 0: the FLV demuxer cannot seek there and writes nothing.
                Check(!PlayerArgs.Video(src, -3, 0, 0, 24, 1730, 786).Contains("-ss") && !PlayerArgs.Video(src, -3, 0, 0, 24, 1730, 786).Contains("scale"), "negative time and no size");
                Check(PlayerArgs.Audio(src, 2.25, 2) == head + "2.250 " + safe + "-i \"C:\\a b\\x.mkv\" -map 0:a:2 -vn -sn -dn -ac 2 -ar 48000 -f f32le -", "audio: " + PlayerArgs.Audio(src, 2.25, 2));
            });

            Test("player", "timeline-playable-from", delegate
            {
                Timeline tl = new Timeline();
                tl.Init(10);
                tl.Playhead = 2; tl.SplitAtPlayhead();
                tl.Playhead = 5; tl.SplitAtPlayhead();
                tl.Playhead = 3; tl.ToggleCut();
                Check(tl.PlayableFrom(1) == 1, "inside a kept piece");
                Check(tl.PlayableFrom(2.5) == 5, "inside the removed piece: " + tl.PlayableFrom(2.5));
                Check(tl.PlayableFrom(5) == 5 && tl.PlayableFrom(9.9) == 9.9, "after it");
                Check(double.IsPositiveInfinity(tl.PlayableFrom(10)), "at the end");
                tl.In = 1; tl.Out = 8;
                Check(tl.PlayableFrom(0) == 1 && tl.PlayableFrom(6) == 6 && double.IsPositiveInfinity(tl.PlayableFrom(8)), "trimmed");
                tl.Playhead = 3; tl.ToggleCut();
                Check(tl.PlayableFrom(2.5) == 2.5, "restored piece plays");
            });

            Test("player", "canvas-live-frame", delegate
            {
                using (Bitmap img = new Bitmap(320, 180, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(img)) g.Clear(Color.FromArgb(200, 30, 30));
                    Canvas c = new Canvas(img);
                    try
                    {
                        c.Size = new Size(480, 300);
                        c.Ui = 1f;
                        IntPtr forced = c.Handle;
                        Check(forced != IntPtr.Zero, "no handle");
                        Func<Color> centre = delegate
                        {
                            using (Bitmap shot = new Bitmap(480, 300))
                            {
                                c.DrawToBitmap(shot, new Rectangle(0, 0, 480, 300));
                                return shot.GetPixel(240, 150);
                            }
                        };
                        Color before = centre();
                        Check(before.R > 150 && before.G < 80, "the image: " + before);
                        // 160 x 90 green frame in BGRA, decoded below native size.
                        byte[] green = new byte[160 * 90 * 4];
                        for (int i = 0; i < green.Length; i += 4) { green[i] = 40; green[i + 1] = 220; green[i + 2] = 60; green[i + 3] = 255; }
                        c.ShowPlayFrame(green, 160, 90);
                        Color live = centre();
                        Check(live.G > 180 && live.R < 90, "the live frame must replace the image: " + live);
                        c.EndPlayFrame();
                        Color after = centre();
                        Check(after.R > 150 && after.G < 80, "the image comes back: " + after);
                        Add("centre " + before.R + "," + before.G + "," + before.B + " -> " + live.R + "," + live.G + "," + live.B);
                    }
                    finally { c.Release(); c.Dispose(); }
                }
            });

            Test("player", "canvas-live-frame-crop-and-marks", delegate
            {
                using (Bitmap img = new Bitmap(320, 180, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(img)) g.Clear(Color.FromArgb(30, 30, 200));
                    Canvas c = new Canvas(img);
                    try
                    {
                        c.Size = new Size(480, 300);
                        c.Ui = 1f;
                        Check(c.Handle != IntPtr.Zero, "no handle");
                        // Right half of the picture only: the live frame has red on the left and green on the right.
                        c.Crop = new RectangleF(160, 0, 160, 180);
                        byte[] frame = new byte[160 * 90 * 4];
                        for (int y = 0; y < 90; y++)
                            for (int x = 0; x < 160; x++)
                            {
                                int i = (y * 160 + x) * 4;
                                bool right = x >= 80;
                                frame[i] = (byte)(right ? 60 : 30); frame[i + 1] = (byte)(right ? 220 : 30); frame[i + 2] = (byte)(right ? 40 : 220); frame[i + 3] = 255;
                            }
                        c.ShowPlayFrame(frame, 160, 90);
                        Func<int> reds = delegate
                        {
                            int n = 0;
                            using (Bitmap shot = new Bitmap(480, 300))
                            {
                                c.DrawToBitmap(shot, new Rectangle(0, 0, 480, 300));
                                for (int y = 0; y < 300; y++)
                                    for (int x = 0; x < 480; x++) { Color p = shot.GetPixel(x, y); if (p.R > 180 && p.G < 90 && p.B < 90) n++; }
                            }
                            return n;
                        };
                        Check(reds() == 0, "the cropped-away half of the live frame is visible");
                        System.Reflection.FieldInfo fi = typeof(Canvas).GetField("shapes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        ((List<Shape>)fi.GetValue(c)).Add(Box(Tool.Rect, 200, 60, 280, 120));
                        int marked = reds();
                        Check(marked > 100, "the mark is not drawn over the live frame: " + marked + " red pixels");
                        Add("mark pixels over the live frame: " + marked);
                    }
                    finally { c.Release(); c.Dispose(); }
                }
            });

            Test("player", "play-to-end-ideal-clock", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                snd.Manual = true;
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    // The clock is the test's: one frame (1600 sample frames) at a time, and exactly that frame must appear.
                    for (int i = 0; i < m.Frames; i++)
                    {
                        int step = i;
                        snd.SetManual(step * 1600L);
                        PlWait(delegate { return log.Count >= step + 1 || log.Errors.Count > 0; }, 8000, "frame " + step + " (have " + log.Count + ")");
                        PlOk(log);
                        Thread.Sleep(6);
                        Check(log.Count == step + 1, "at step " + step + " there are " + log.Count + " frames");
                    }
                    snd.SetManual(m.Frames * 1600L);
                    PlWait(delegate { return log.EndedCount > 0; }, 5000, "the end");
                    PlOk(log);
                    for (int i = 0; i < m.Frames; i++) Check(log.Index[i] == i && log.Number[i] == i, "frame " + i + " shows index " + log.Index[i] + " number " + log.Number[i]);
                    Check(p.FramesDecoded == m.Frames, "decoded " + p.FramesDecoded);
                    Check(p.FramesShown == m.Frames, "shown " + p.FramesShown);
                    Check(p.FramesDropped == 0, "dropped " + p.FramesDropped);
                    Check(log.EndedCount == 1 && p.State == PlayState.Ended, "ended " + log.EndedCount + " state " + p.State);
                    Check(Math.Abs(p.Position - m.Seconds) < 0.2, "position " + p.Position);
                    Check(p.AudioFramesQueued > 100000, "audio frames queued " + p.AudioFramesQueued);
                    Add("decoded " + p.FramesDecoded + ", shown " + p.FramesShown + ", dropped " + p.FramesDropped + ", audio frames " + p.AudioFramesQueued);
                }
                finally { p.Dispose(); }
            });

            Test("player", "play-real-time", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end");
                    PlOk(log);
                    int n = log.Count;
                    for (int i = 1; i < n; i++) Check(log.Number[i] > log.Number[i - 1], "frames out of order at " + i);
                    double span = (log.Ms[n - 1] - log.Ms[0]) / 1000.0;
                    Check(Math.Abs(span - (m.Seconds - 1.0 / 30)) < 0.5, "frames spread over " + span + " s");
                    double worst = 0;
                    for (int i = 0; i < n; i++) worst = Math.Max(worst, Math.Abs((log.Ms[i] - log.Ms[0]) / 1000.0 - log.Number[i] / 30.0));
                    Check(n >= m.Frames - 3, "only " + n + " of " + m.Frames + " frames shown");
                    Check(p.FramesShown + p.FramesDropped == p.FramesDecoded, "shown " + p.FramesShown + " + dropped " + p.FramesDropped + " != decoded " + p.FramesDecoded);
                    Add(n + " frames, " + p.FramesDropped + " dropped, " + PlF(span) + " s, worst lateness " + worst.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                }
                finally { p.Dispose(); }
            });

            Test("player", "seek-lands-on-frame", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 3; }, 8000, "first frames");
                    double[] targets = { 1.0, 2.0, 0.5, 52 / 30.0, 1.016, 2.9 };
                    foreach (double t in targets)
                    {
                        p.Seek(t);
                        int mark = log.Count;
                        int want = (int)Math.Round(t * 30);
                        PlWait(delegate { return log.Count > mark; }, 8000, "a frame after seeking to " + t);
                        Check(log.Index[mark] == want && log.Number[mark] == 0, "seek to " + t + ": first frame is index " + log.Index[mark] + " number " + log.Number[mark] + ", expected " + want);
                        Check(Math.Abs(log.Time[mark] - want / 30.0) < 1e-6, "its time is " + log.Time[mark]);
                    }
                    PlOk(log);
                }
                finally { p.Dispose(); }
            });

            Test("player", "seek-near-and-past-the-end", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 2; }, 8000, "first frames");
                    p.Seek(2.9);
                    int mark = log.Count;
                    PlWait(delegate { return log.EndedCount > 0; }, 8000, "the end after seeking near it");
                    PlOk(log);
                    Check(log.Count - mark == 3 && log.Index[mark] == 87 && log.Index[mark + 1] == 88 && log.Index[mark + 2] == 89,
                          "near the end gave " + (log.Count - mark) + " frames starting at " + (log.Count > mark ? log.Index[mark] : -1));
                    // Past the end: nothing to show, it ends at once and the position is the end.
                    p.Start(0, 0);
                    int before = log.Count;
                    PlWait(delegate { return log.Count >= before + 5; }, 8000, "playing again");
                    int ended = log.EndedCount;
                    p.Seek(99);
                    int mark2 = log.Count;
                    PlWait(delegate { return log.EndedCount > ended; }, 8000, "the end after seeking past it");
                    Thread.Sleep(100);
                    Check(log.Count == mark2, "frames shown after seeking past the end: " + (log.Count - mark2));
                    Check(Math.Abs(p.Position - m.Seconds) < 0.1, "position " + p.Position);
                    PlOk(log);
                }
                finally { p.Dispose(); }
            });

            Test("player", "rapid-seeks-last-one-wins", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 3; }, 8000, "first frames");
                    Stopwatch sw = Stopwatch.StartNew();
                    for (int k = 0; k < 19; k++) p.Seek(((k * 7) % 25) / 10.0);
                    p.Seek(2.2);
                    long took = sw.ElapsedMilliseconds;
                    int mark = log.Count;
                    PlWait(delegate { return log.Count >= mark + 6; }, 8000, "frames after the last seek");
                    PlOk(log);
                    Check(log.Index[mark] == 66, "first frame after the last seek is " + log.Index[mark] + ", expected 66");
                    for (int i = mark; i < log.Count; i++) Check(log.Index[i] == 66 + (i - mark), "frame " + (i - mark) + " after the last seek shows " + log.Index[i]);
                    Check(took < 1000, "twenty seeks took " + took + " ms");
                    Add("20 seeks issued in " + took + " ms");
                }
                finally { p.Dispose(); }
            });

            Test("player", "pause-resume-keeps-position", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 12; }, 8000, "twelve frames");
                    p.Pause();
                    int c1 = log.Count;
                    double pos1 = p.Position;
                    Check(p.State == PlayState.Paused, "state " + p.State);
                    Thread.Sleep(700);
                    Check(log.Count == c1, "frames shown while paused: " + (log.Count - c1));
                    Check(Math.Abs(p.Position - pos1) < 0.002, "position moved while paused: " + pos1 + " -> " + p.Position);
                    Check(snd.Paused, "the sound was not paused");
                    p.Resume();
                    PlWait(delegate { return log.Count >= c1 + 6; }, 8000, "frames after resuming");
                    PlOk(log);
                    Check(Math.Abs(log.Index[c1] - c1) <= 1, "first frame after resuming is " + log.Index[c1] + ", paused after " + (c1 - 1));
                    for (int i = 1; i < log.Count; i++) Check(log.Number[i] == log.Number[i - 1] + 1, "a seam at " + i);
                    Check(p.Position > pos1 + 0.1, "position after resuming " + p.Position);
                }
                finally { p.Dispose(); }
            });

            Test("player", "track-switch-restarts-sound", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return snd.SampleCount >= 2 * 9000; }, 8000, "sound from the mix");
                    float[] s = snd.Samples();
                    double a440 = PlTonePower(s, 2000, 4800, 440), a880 = PlTonePower(s, 2000, 4800, 880);
                    Check(a440 > 0.2 && a880 > 0.2, "the mix has both tones: " + PlF(a440) + " " + PlF(a880));
                    int[] order = { 1, 2 };
                    foreach (int track in order)
                    {
                        double before = p.Position;
                        int resets = snd.ResetCount;
                        int framesBefore = log.Count;
                        p.Track = track;
                        PlWait(delegate { return snd.ResetCount > resets && snd.SampleCount >= 2 * 14400; }, 8000, "sound from track " + track);
                        s = snd.Samples();
                        double hi = PlTonePower(s, 4000, 4800, track == 1 ? 440 : 880), lo = PlTonePower(s, 4000, 4800, track == 1 ? 880 : 440);
                        Check(hi > 0.3 && lo < 0.05, "track " + track + ": wanted tone " + PlF(hi) + ", other " + PlF(lo));
                        Check(p.Position >= before - 0.001, "the clock went back from " + before + " to " + p.Position);
                        PlWait(delegate { return log.Count > framesBefore + 3 || log.EndedCount > 0; }, 8000, "video after the switch");
                    }
                    PlOk(log);
                    for (int i = 1; i < log.Count; i++) Check(log.Number[i] == log.Number[i - 1] + 1, "the picture restarted at " + i);
                }
                finally { p.Dispose(); }
            });

            Test("player", "volume-half-halves-samples", delegate
            {
                PlMaster m = PlSharedMaster();
                float[][] runs = new float[2][];
                double[] vols = { 1, 0.5 };
                for (int r = 0; r < 2; r++)
                {
                    PlFakeSound snd = new PlFakeSound();
                    PlLog log = new PlLog();
                    Playback p = PlNew(m.Path, snd, log);
                    try
                    {
                        p.Volume = vols[r];
                        p.Start(0, 0);
                        PlWait(delegate { return snd.SampleCount >= 2 * 14400; }, 8000, "sound at volume " + vols[r]);
                        float[] all = snd.Samples();
                        runs[r] = new float[2 * 14400];
                        Array.Copy(all, runs[r], runs[r].Length);
                    }
                    finally { p.Dispose(); }
                }
                double peak = 0;
                for (int i = 0; i < runs[0].Length; i++)
                {
                    Check(runs[1][i] == runs[0][i] * 0.5f, "sample " + i + ": " + runs[1][i] + " vs half of " + runs[0][i]);
                    peak = Math.Max(peak, Math.Abs(runs[0][i]));
                }
                Check(peak > 0.3, "the reference run is silent");
            });

            Test("player", "file-without-audio-uses-stopwatch", delegate
            {
                NeedFfmpeg();
                string mp4 = PlMp4("silent-clock.mp4", false, 2);
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(mp4, snd, log);
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 10000, "the end");
                    double took = sw.Elapsed.TotalSeconds;
                    PlOk(log);
                    Check(snd.WriteCount == 0 && !snd.Opened, "the sound was used: " + snd.WriteCount + " writes");
                    Check(took > 1.7 && took < 3.5, "2 s of video played in " + took + " s");
                    Check(log.Count >= 55, "frames shown " + log.Count);
                    Add("2 s file took " + PlF(took) + " s, " + log.Count + " frames");
                }
                finally { p.Dispose(); }
            });

            Test("player", "silence-and-unmute-keep-the-clock", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Volume = 0;
                    p.Start(0, 0);
                    PlWait(delegate { return log.Count >= 15; }, 8000, "frames while silent");
                    Check(p.ProcessIds().Length == 1 && snd.WriteCount == 0, "silent: " + p.ProcessIds().Length + " ffmpeg, " + snd.WriteCount + " writes");
                    Check(!snd.Opened, "the device was opened for nothing");
                    double last = p.Position;
                    p.Volume = 1;
                    Stopwatch sw = Stopwatch.StartNew();
                    int c0 = log.Count;
                    while (sw.ElapsedMilliseconds < 1200 && log.EndedCount == 0)
                    {
                        double now = p.Position;
                        Check(now >= last - 0.001, "the clock went back from " + last + " to " + now);
                        last = now;
                        Thread.Sleep(10);
                    }
                    Check(p.ProcessIds().Length == 2 && snd.WriteCount > 0, "after unmuting: " + p.ProcessIds().Length + " ffmpeg, " + snd.WriteCount + " writes");
                    Check(log.Count > c0 + 10, "frames after unmuting " + (log.Count - c0));
                    PlOk(log);
                    for (int i = 1; i < log.Count; i++) Check(log.Number[i] == log.Number[i - 1] + 1, "the picture restarted at " + i);
                }
                finally { p.Dispose(); }
            });

            Test("player", "no-output-device-plays-silent", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                snd.FailOpen = true;
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0, 1.0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 8000, "the end");
                    PlOk(log);
                    Check(log.Notices.Count == 1 && log.Notices[0] == "Sin salida de sonido", "notices: " + log.Notices.Count);
                    Check(log.Count >= 28, "frames shown " + log.Count);
                    // And a second run does not repeat the notice.
                    int ended = log.EndedCount;
                    p.Start(0, 0.5);
                    PlWait(delegate { return log.EndedCount > ended; }, 8000, "the second end");
                    Check(log.Notices.Count == 1, "the notice came again");
                }
                finally { p.Dispose(); }
            });

            Test("player", "end-time-stops-playback", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Start(0.5, 1.5);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 8000, "the end");
                    PlOk(log);
                    Check(log.Count == 30 && log.Index[0] == 15 && log.Index[29] == 44, "frames " + log.Count + " from " + log.Index[0] + " to " + log.Index[log.Count - 1]);
                }
                finally { p.Dispose(); }
            });

            Test("player", "skip-filter-jumps-over-removed-piece", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                try
                {
                    p.Skip = delegate(double t) { return t >= 1.0 && t < 2.0 ? 2.0 : t; };
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 15000, "the end");
                    PlOk(log);
                    int inside = 0, before = 0, after = 0;
                    foreach (int i in log.Index) { if (i >= 30 && i < 60) inside++; else if (i < 30) before++; else after++; }
                    Check(inside == 0, inside + " frames from the removed second");
                    Check(before >= 28 && after >= 28, "kept frames " + before + " + " + after);
                    // Skipping everything ends playback.
                    int ended = log.EndedCount;
                    p.Skip = delegate(double t) { return t >= 0.5 ? double.PositiveInfinity : t; };
                    p.Start(0, 0);
                    PlWait(delegate { return log.EndedCount > ended; }, 8000, "the end by skipping");
                }
                finally { p.Dispose(); }
            });

            Test("player", "decode-size-scales-the-picture", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                int w = 0, h = 0;
                p.FrameReady += delegate(PlayerFrame f) { w = f.Width; h = f.Height; };
                try
                {
                    p.SetDecodeSize(161, 91);
                    p.Start(0, 1.0);
                    PlWait(delegate { return log.EndedCount > 0 || log.Errors.Count > 0; }, 8000, "the end");
                    PlOk(log);
                    Check(w == 160 && h == 90, "frame size " + w + "x" + h);
                    Check(log.Count == 30, "frames " + log.Count);
                    for (int i = 0; i < log.Count; i++) Check(log.Index[i] == i, "scaled frame " + i + " reads as " + log.Index[i]);
                    // Larger than native is native.
                    p.SetDecodeSize(4000, 2000);
                    int ended = log.EndedCount;
                    p.Start(0, 0.5);
                    PlWait(delegate { return log.EndedCount > ended; }, 8000, "the second end");
                    Check(w == 320 && h == 180, "native size " + w + "x" + h);
                }
                finally { p.Dispose(); }
            });

            Test("player", "unreadable-file-reports-an-error", delegate
            {
                PlMaster m = PlSharedMaster();
                string bad = Path.Combine(WorkDir("plbad"), "broken.mkv");
                byte[] head = new byte[4096];
                using (FileStream fs = File.OpenRead(m.Path)) fs.Read(head, 0, head.Length);
                File.WriteAllBytes(bad, head);
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                MediaInfo mi = MediaInfo.Probe(ffmpeg, m.Path);
                Playback p = new Playback(ffmpeg, bad, mi, snd);
                p.Error += delegate(string e) { lock (log) log.Errors.Add(e); };
                p.Ended += delegate { lock (log) log.Ended++; };
                try
                {
                    p.Start(0, 0);
                    PlWait(delegate { return log.Errors.Count > 0 || log.EndedCount > 0; }, 15000, "an error or the end");
                    Check(log.Errors.Count == 1 || log.EndedCount == 1, "neither an error nor an end");
                    Add(log.Errors.Count > 0 ? "error: " + log.Errors[0].Replace("\n", " ").Substring(0, Math.Min(60, log.Errors[0].Length)) : "ended with no frames");
                }
                finally { p.Dispose(); }
            });

            Test("player", "stop-and-dispose-leave-no-ffmpeg", delegate
            {
                PlMaster m = PlSharedMaster();
                PlFakeSound snd = new PlFakeSound();
                PlLog log = new PlLog();
                Playback p = PlNew(m.Path, snd, log);
                p.Start(0, 0);
                PlWait(delegate { return log.Count >= 8; }, 8000, "frames");
                p.Seek(1.0);
                p.Track = 2;
                Thread.Sleep(150);
                Stopwatch sw = Stopwatch.StartNew();
                p.Stop();
                long stopMs = sw.ElapsedMilliseconds;
                Check(p.State == PlayState.Stopped, "state " + p.State);
                PlDead(p, 500);
                int c = log.Count;
                Thread.Sleep(200);
                Check(log.Count == c, "a frame after Stop");
                p.Start(0, 0);
                PlWait(delegate { return log.Count >= c + 5; }, 8000, "frames after starting again");
                sw.Restart();
                p.Dispose();
                long disposeMs = sw.ElapsedMilliseconds;
                Check(disposeMs < 2000, "Dispose took " + disposeMs + " ms");
                PlDead(p, 1000);
                Check(snd.Disposed, "the sound was not closed");
                int after = log.Count, started = p.ProcessIds().Length;
                p.Start(0, 0);
                p.Seek(1);
                Thread.Sleep(200);
                Check(log.Count == after && p.ProcessIds().Length == started, "a disposed player played");
                Add("Stop " + stopMs + " ms, Dispose " + disposeMs + " ms, " + started + " ffmpeg started in all");
            });

            Test("player", "real-wave-out-device", delegate
            {
                WaveOutSink w = new WaveOutSink();
                try
                {
                    if (!w.Open(48000, 2)) Skip("no hay dispositivo de salida de sonido (waveOutOpen fall\u00F3)");
                    float[] silence = new float[2 * 2400];
                    for (int i = 0; i < 4; i++) Check(w.Write(silence, silence.Length), "write " + i);
                    Check(w.Position == 0, "position before starting: " + w.Position);
                    w.Resume();
                    Thread.Sleep(120);
                    long p1 = w.Position;
                    Check(p1 > 1000 && p1 <= 4 * 2400, "position after 120 ms: " + p1);
                    w.Pause();
                    long held = w.Position;
                    Thread.Sleep(150);
                    Check(w.Position == held, "position moved while paused: " + held + " -> " + w.Position);
                    w.Reset();
                    Check(w.Position == 0, "position after Reset: " + w.Position);
                    // After a reset it writes and plays again, still paused until resumed.
                    Check(w.Write(silence, silence.Length), "write after reset");
                    Thread.Sleep(60);
                    Check(w.Position == 0, "paused after reset: " + w.Position);
                    w.Resume();
                    Thread.Sleep(60);
                    Check(w.Position > 0, "no position after resuming again");
                    Add("position after 120 ms: " + p1 + " frames");
                }
                finally { w.Dispose(); }
                Check(w.Position == 0, "closed device reports a position");
            });

            Test("player", "decode-throughput", delegate
            {
                NeedFfmpeg();
                int[][] sizes = { new[] { 1920, 1080, 45 }, new[] { 3840, 2160, 12 } };
                foreach (int[] sz in sizes)
                {
                    PlMaster big = PlBuild(sz[0], sz[1], 30, sz[2], false);
                    double[] scales = { 1.0, 0.5 };
                    foreach (double sc in scales)
                    {
                        int w = ((int)(sz[0] * sc)) & ~1, h = ((int)(sz[1] * sc)) & ~1;
                        string args = PlayerArgs.Video(big.Path, 0, w, h, 30, sz[0], sz[1]);
                        PlayerChild c = PlayerChild.Start(ffmpeg, args);
                        byte[] buf = new byte[w * h * 4];
                        int frames = 0;
                        Stopwatch sw = null;
                        try
                        {
                            while (true)
                            {
                                int n = 0;
                                while (n < buf.Length) { int r = c.Out.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
                                if (n < buf.Length) break;
                                if (frames == 0) sw = Stopwatch.StartNew();
                                frames++;
                            }
                        }
                        finally { c.Kill(); c.Dispose(); }
                        double secs = sw != null ? sw.Elapsed.TotalSeconds : 0;
                        double fps = frames > 1 && secs > 0 ? (frames - 1) / secs : 0;
                        Add(sz[0] + "x" + sz[1] + " at " + w + "x" + h + ": " + frames + " frames, " + fps.ToString("0", CultureInfo.InvariantCulture) + " fps");
                        Check(frames == sz[2], sz[0] + "x" + sz[1] + " decoded " + frames + " of " + sz[2] + " frames");
                        if (sz[0] == 1920 && sc == 1.0) Check(fps >= 15, "1080p native decodes at " + fps + " fps");
                    }
                }
            });

            Test("player", "master-untouched-by-playback", delegate
            {
                PlMaster m = PlSharedMaster();
                Check(CineMaster.Sha256File(m.Path) == m.Sha, "the master changed (SHA-256)");
                Check(File.GetLastWriteTimeUtc(m.Path) == m.Mtime, "the master's modification time changed");
                Add("SHA-256 " + m.Sha.Substring(0, 12));
            });
            PlayerEdgeSuite();
        }
    }
}

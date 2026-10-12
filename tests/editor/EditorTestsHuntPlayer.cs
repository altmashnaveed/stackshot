// Bug hunt of the built-in player (Haiku independent tester, 2026-10-10): odd paths and names, tiny and odd sizes,
// 0.1 s clips, variable frame rate, MPEG-TS and MPEG-PS seeks checked by picture, truncated and deleted files, a random
// storm of commands, many audio tracks, 8K, audio-only and video-only files, handles after many opens, long paths.
// Invoked from HuntPlayerSuite (suite "huntplayer"). Headless: no window is shown and the screen is never captured.
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
        sealed class HpShot
        {
            public double Time;
            public int W, H, R, G, B;
        }

        sealed class HpRun
        {
            public readonly List<HpShot> Shots = new List<HpShot>();
            public readonly List<string> Errors = new List<string>();
            public int Ended;
            public Playback P;
            public int Count { get { lock (Shots) return Shots.Count; } }
            public int EndedCount { get { lock (Shots) return Ended; } }
            public int ErrorCount { get { lock (Errors) return Errors.Count; } }
            public string FirstError { get { lock (Errors) return Errors.Count > 0 ? Errors[0] : ""; } }
        }

        static void HpSuite()
        {
            Test("huntplayer", "unicode-and-symbol-paths", HpUnicodePaths);
            Test("huntplayer", "tiny-and-odd-sizes", HpTinyAndOddSizes);
            Test("huntplayer", "tenth-of-a-second-clips", HpShortClips);
            Test("huntplayer", "variable-frame-rate-plays-to-end", HpVariableRate);
            Test("huntplayer", "ts-and-mpg-seeks-show-the-right-second", HpMpegSeeks);
            Test("huntplayer", "truncated-while-playing", HpTruncated);
            Test("huntplayer", "deleted-while-paused", HpDeleted);
            Test("huntplayer", "random-command-storm", HpStorm);
            Test("huntplayer", "eight-audio-tracks-mkv", HpManyTracks);
            Test("huntplayer", "handles-after-many-opens", HpHandles);
            Test("huntplayer", "long-path-probe", HpLongPath);
            Test("huntplayer", "eight-k-native-and-scaled", HpEightK);
            Test("huntplayer", "audio-only-and-video-only", HpAudioOnly);
            Test("huntplayer", "wheel-volume-clamps", HpWheel);
            Test("huntplayer", "window-key-and-resize-storm", HpWindowStorm);
            Test("huntplayer", "geometry-invariants", HpGeometry);
            Test("huntplayer", "no-duration-in-header", HpNoDuration);
        }

        // Browser WebM recordings and interrupted MKVs say "Duration: N/A": the length is found from the packets.
        static void HpNoDuration()
        {
            NeedFfmpeg();
            string dir = WorkDir("hp-nodur");
            string webm = Path.Combine(dir, "navegador.webm"), mkv = Path.Combine(dir, "cortado.mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x240:r=30:d=4\" -f lavfi -i \"sine=d=4\" -c:v libvpx-vp9 -deadline realtime -c:a libopus -live 1 -f webm " + Q(webm), 60000);
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x240:r=30:d=4\" -c:v libx264 -preset ultrafast -live 1 -f matroska " + Q(mkv), 60000);
            foreach (string f in new string[] { webm, mkv })
            {
                Check(Probe(f).Contains("Duration: N/A"), Path.GetFileName(f) + " should have no duration in its header");
                MediaInfo mi = MediaInfo.Probe(ffmpeg, f);
                Check(mi.CanPlay && Math.Abs(mi.Duration - 4) < 0.05, Path.GetFileName(f) + " duration " + mi.Duration);
                ExportSpec s = new ExportSpec();
                s.Source = f;
                s.Output = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + " (editado).mp4");
                s.Crop = new Rectangle(0, 0, 320, 240);
                s.HasAudio = mi.Audio.Count > 0;
                s.Fps = mi.Fps;
                s.Level = 1;
                s.Keep = new List<double[]> { new double[] { 1, 3 } };
                ExportOk(s);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, mi.Duration, mi.Fps, true)));
            }
            Add("webm and mkv without a header duration: 4.00 s found, a 2 s trim exports and passes the check");
        }

        static HpShot HpCapture(PlayerFrame f)
        {
            HpShot s = new HpShot();
            s.Time = f.Time;
            s.W = f.Width;
            s.H = f.Height;
            int i = ((f.Height / 2) * f.Width + f.Width / 2) * 4;
            s.B = f.Data[i];
            s.G = f.Data[i + 1];
            s.R = f.Data[i + 2];
            return s;
        }

        static HpRun HpNew(string path, ISoundOut snd)
        {
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "not playable: " + Path.GetFileName(path) + " " + mi.Width + "x" + mi.Height + " fps " + mi.Fps);
            HpRun r = new HpRun();
            Playback p = new Playback(ffmpeg, path, mi, snd);
            p.FrameReady += delegate(PlayerFrame f) { HpShot s = HpCapture(f); lock (r.Shots) r.Shots.Add(s); };
            p.Ended += delegate { lock (r.Shots) r.Ended++; };
            p.Error += delegate(string m) { lock (r.Errors) r.Errors.Add(m); };
            r.P = p;
            return r;
        }

        static void HpWait(HpRun r, int ms, string what)
        {
            PlWait(delegate { return r.EndedCount > 0 || r.ErrorCount > 0; }, ms, what);
        }

        // Disposes and returns how long Dispose took; afterwards no FFmpeg of that player may be alive.
        static double HpDispose(HpRun r)
        {
            Stopwatch sw = Stopwatch.StartNew();
            r.P.Dispose();
            double ms = sw.Elapsed.TotalMilliseconds;
            PlDead(r.P, 3000);
            return ms;
        }

        static void HpFfmpeg(string args, int timeoutMs)
        {
            FfOk("-hide_banner -loglevel error -y " + args, timeoutMs);
        }

        static void HpUnicodePaths()
        {
            NeedFfmpeg();
            string dir = WorkDir("hp-uni");
            string[] names =
            {
                "v\u00EDdeo \u00F1and\u00FA.mp4", "it's & 100% ^ done !#;$@.mp4", "copia (2) [final] {x}.mp4",
                "\u65E5\u672C \u30C6\u30B9\u30C8.mp4", "a%20b %PATH% x.mp4", "trailing dot.mp4", "con\u00A0nbsp.mp4"
            };
            List<string> bad = new List<string>();
            foreach (string name in names)
            {
                try
                {
                    string path = Path.Combine(dir, name);
                    HpFfmpeg("-f lavfi -i \"testsrc2=s=320x180:r=30:d=1\" -f lavfi -i \"sine=f=440:d=1\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest " + Q(path), 60000);
                    HpRun r = HpNew(path, new PlFakeSound());
                    r.P.Start(0, 0);
                    HpWait(r, 20000, "the end of " + name);
                    if (r.ErrorCount > 0) { bad.Add(name + ": error " + r.FirstError); }
                    else if (r.EndedCount == 0) { bad.Add(name + ": no end"); }
                    else if (r.Count < 25) { bad.Add(name + ": " + r.Count + " frames"); }
                    HpDispose(r);
                }
                catch (TestFailed f) { bad.Add(name + ": " + f.Message); }
            }
            Check(bad.Count == 0, string.Join(" | ", bad));
            Add(names.Length + " names played");
        }

        static void HpTinyAndOddSizes()
        {
            NeedFfmpeg();
            string[] sizes = { "16x16", "17x9", "321x179", "2x2", "1x1" };
            List<string> bad = new List<string>();
            foreach (string size in sizes)
            {
                string[] wh = size.Split('x');
                int w = int.Parse(wh[0], CultureInfo.InvariantCulture), h = int.Parse(wh[1], CultureInfo.InvariantCulture);
                string path = Path.Combine(WorkDir("hp-size"), "s" + size + ".mkv");
                try
                {
                    HpFfmpeg("-f lavfi -i \"color=c=0xFF0000:s=" + size + ":r=30:d=0.5\" -c:v ffv1 -pix_fmt yuv444p " + Q(path), 60000);
                }
                catch (TestFailed f) { bad.Add(size + ": could not make the clip: " + f.Message); continue; }
                HpRun r = HpNew(path, new PlFakeSound());
                r.P.Start(0, 0);
                HpWait(r, 20000, "the end of " + size);
                if (r.ErrorCount > 0) bad.Add(size + ": error " + r.FirstError);
                else if (r.EndedCount == 0) bad.Add(size + ": no end");
                else if (r.Count < 14) bad.Add(size + ": " + r.Count + " frames of 15");
                else
                {
                    HpShot s;
                    lock (r.Shots) s = r.Shots[r.Shots.Count / 2];
                    if (s.W != w || s.H != h) bad.Add(size + ": picture " + s.W + "x" + s.H);
                    else if (s.R < 200 || s.G > 60 || s.B > 60) bad.Add(size + ": centre colour " + s.R + "," + s.G + "," + s.B + " (want red)");
                }
                HpDispose(r);
            }
            Check(bad.Count == 0, string.Join(" | ", bad));
            Add(sizes.Length + " sizes");
        }

        static void HpShortClips()
        {
            NeedFfmpeg();
            string[] durs = { "0.1", "0.05" };
            List<string> bad = new List<string>();
            foreach (string d in durs)
            {
                string path = Path.Combine(WorkDir("hp-short"), "short" + d + ".mp4");
                HpFfmpeg("-f lavfi -i \"testsrc2=s=320x180:r=30:d=" + d + "\" -f lavfi -i \"sine=f=440:d=" + d + "\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest " + Q(path), 60000);
                PlFakeSound snd = new PlFakeSound();
                HpRun r = HpNew(path, snd);
                r.P.Start(0, 0);
                HpWait(r, 10000, "the end of " + d + " s");
                if (r.ErrorCount > 0) bad.Add(d + " s: error " + r.FirstError);
                else if (r.EndedCount == 0) bad.Add(d + " s: no end, state " + r.P.State);
                else if (r.Count < 1) bad.Add(d + " s: no picture");
                else Add(d + " s: " + r.Count + " pictures");
                HpDispose(r);
            }
            Check(bad.Count == 0, string.Join(" | ", bad));
        }

        static void HpVariableRate()
        {
            NeedFfmpeg();
            string path = Path.Combine(WorkDir("hp-vfr"), "vfr.mp4");
            // 30 frames a second for one second, then 30 frames at 90 a second: a variable frame rate.
            HpFfmpeg("-f lavfi -i \"testsrc2=s=320x180:r=30:d=2\" -vf \"setpts='if(lt(N\\,30)\\,N/30\\,1+(N-30)/90)/TB'\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an " + Q(path), 60000);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "VFR file not playable: fps " + mi.Fps + " " + mi.Width + "x" + mi.Height);
            Add("container " + mi.Duration.ToString("0.000", CultureInfo.InvariantCulture) + " s at " + mi.Fps + " fps");
            HpRun r = HpNew(path, new PlFakeSound());
            r.P.Start(0, 0);
            HpWait(r, 20000, "the end of the VFR file");
            if (r.ErrorCount > 0) { HpDispose(r); throw new TestFailed("error: " + r.FirstError); }
            Check(r.EndedCount > 0, "no end, state " + r.P.State);
            List<HpShot> s;
            lock (r.Shots) s = new List<HpShot>(r.Shots);
            double expect = mi.Duration * mi.Fps;
            Check(Math.Abs(s.Count - expect) <= 3, "pictures " + s.Count + ", expected about " + expect.ToString("0.0", CultureInfo.InvariantCulture));
            for (int i = 1; i < s.Count; i++)
                Check(s[i].Time > s[i - 1].Time, "time goes back at picture " + i + ": " + s[i - 1].Time + " -> " + s[i].Time);
            HpDispose(r);
        }

        // Colour-coded seconds: second k is a flat colour with red = 20k + 10, so the picture says which second it shows.
        static string HpSecondsClip(string ext, string codecs)
        {
            string path = Path.Combine(WorkDir("hp-secs"), "secs" + ext);
            StringBuilder fg = new StringBuilder();
            StringBuilder labels = new StringBuilder();
            for (int k = 0; k < 12; k++)
            {
                string hex = (20 * k + 10).ToString("X2", CultureInfo.InvariantCulture) + "8080";
                fg.Append("color=c=0x" + hex + ":s=160x90:r=30:d=1[c" + k + "];");
                labels.Append("[c" + k + "]");
            }
            fg.Append(labels + "concat=n=12:v=1:a=0[v]");
            HpFfmpeg("-filter_complex \"" + fg + "\" -map \"[v]\" " + codecs + " " + Q(path), 120000);
            return path;
        }

        static void HpMpegSeeks()
        {
            NeedFfmpeg();
            string[][] kinds =
            {
                new string[] { ".ts", "-c:v libx264 -preset ultrafast -g 30 -pix_fmt yuv420p -f mpegts" },
                new string[] { ".mpg", "-c:v mpeg2video -b:v 2M -g 12 -f mpeg" }
            };
            double[] targets = { 0.5, 3.5, 7.3, 10.7, 11.5 };
            List<string> bad = new List<string>();
            foreach (string[] kind in kinds)
            {
                string path = HpSecondsClip(kind[0], kind[1]);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
                Check(mi.CanPlay, kind[0] + " not playable");
                foreach (double t in targets)
                {
                    int want = (int)Math.Floor(t);
                    HpRun r = HpNew(path, new PlFakeSound());
                    r.P.Start(t, 0);
                    PlWait(delegate { return r.Count > 0 || r.ErrorCount > 0; }, 20000, "a picture of " + kind[0] + " at " + t);
                    if (r.ErrorCount > 0) { bad.Add(kind[0] + " at " + t + ": error " + r.FirstError); HpDispose(r); continue; }
                    HpShot s;
                    lock (r.Shots) s = r.Shots[0];
                    double got = (s.R - 10) / 20.0;
                    if (Math.Abs(got - want) > 0.5) bad.Add(kind[0] + " from " + t + " s shows second " + got.ToString("0.0", CultureInfo.InvariantCulture) + " (red " + s.R + "), want " + want);
                    if (Math.Abs(s.Time - t) > 0.05) bad.Add(kind[0] + " from " + t + " s: first picture time " + s.Time);
                    HpDispose(r);
                }
                // A seek while it runs: the first picture after the seek is the new second.
                HpRun live = HpNew(path, new PlFakeSound());
                live.P.Start(0.5, 0);
                PlWait(delegate { return live.Count >= 3; }, 20000, "three pictures of " + kind[0]);
                live.P.Seek(7.3);
                int mark = live.Count;
                PlWait(delegate { return live.Count > mark + 2; }, 20000, "pictures after the seek in " + kind[0]);
                HpShot after;
                lock (live.Shots) after = live.Shots[mark];
                double got2 = (after.R - 10) / 20.0;
                if (Math.Abs(got2 - 7) > 0.5) bad.Add(kind[0] + " seek to 7.3 while playing shows second " + got2.ToString("0.0", CultureInfo.InvariantCulture));
                HpDispose(live);
            }
            Check(bad.Count == 0, string.Join(" | ", bad));
            Add("ts and mpg, 5 seeks each, plus a seek while playing");
        }

        static void HpTruncated()
        {
            NeedFfmpeg();
            string clip = PlMp4("hp-trunc.mp4", true, 3);
            string path = Path.Combine(WorkDir("hp-trunc"), "t.mp4");
            File.Copy(clip, path);
            HpRun r = HpNew(path, new PlFakeSound());
            r.P.Start(0, 0);
            Thread.Sleep(300);
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                fs.SetLength(fs.Length * 4 / 10);
            HpWait(r, 15000, "the end or an error after truncation");
            int shown = r.Count;
            double ms = HpDispose(r);
            Check(ms < 2000, "Dispose after truncation took " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms");
            Add("ended " + r.EndedCount + ", errors " + r.ErrorCount + (r.ErrorCount > 0 ? " (" + Tail(r.FirstError) + ")" : "") + ", pictures " + shown);
        }

        static void HpDeleted()
        {
            NeedFfmpeg();
            string path = Path.Combine(WorkDir("hp-del"), "d.mp4");
            File.Copy(PlMp4("hp-del.mp4", true, 3), path);
            HpRun r = HpNew(path, new PlFakeSound());
            r.P.Start(0, 0);
            PlWait(delegate { return r.Count > 2; }, 20000, "pictures before the delete");
            r.P.Pause();
            Thread.Sleep(100);
            bool deletedWhilePaused = false;
            try { File.Delete(path); deletedWhilePaused = true; } catch (IOException) { }
            Add("deleted while paused: " + deletedWhilePaused);
            // A seek stops the decoders (they release the file); the delete then has to work if it did not before.
            r.P.Seek(0.5);
            Thread.Sleep(300);
            for (int i = 0; i < 40 && File.Exists(path); i++)
            {
                try { File.Delete(path); } catch (IOException) { }
                Thread.Sleep(50);
            }
            Check(!File.Exists(path), "could not delete the file to test the rest");
            r.P.Resume();
            HpWait(r, 15000, "an end or an error after the file vanished");
            Check(r.ErrorCount > 0 || r.EndedCount > 0, "neither an end nor an error after the file vanished, state " + r.P.State);
            Add("error " + (r.ErrorCount > 0 ? Tail(r.FirstError) : "none") + ", ended " + r.EndedCount);
            HpDispose(r);
        }

        static void HpStorm()
        {
            NeedFfmpeg();
            string path = PlMp4("hp-storm.mp4", true, 4);
            Random rnd = new Random(20261010);
            PlFakeSound snd = new PlFakeSound();
            HpRun r = HpNew(path, snd);
            double dur = r.P.Info.Duration;
            double[] speeds = { 0.25, 0.5, 1, 1.5, 2, 4 };
            r.P.Start(0, 0);
            for (int i = 0; i < 400; i++)
            {
                switch (rnd.Next(10))
                {
                    case 0: r.P.Seek(rnd.NextDouble() * (dur + 1)); break;
                    case 1: r.P.Pause(); break;
                    case 2: r.P.Resume(); break;
                    case 3: r.P.Speed = speeds[rnd.Next(speeds.Length)]; break;
                    case 4: r.P.Volume = rnd.NextDouble(); break;
                    case 5: r.P.Track = rnd.Next(-1, 3); break;
                    case 6: r.P.Stop(); break;
                    case 7: r.P.Start(rnd.NextDouble() * dur, 0); break;
                    case 8: r.P.Repeat = rnd.Next(2) == 0; break;
                    default: r.P.SetDecodeSize(rnd.Next(0, 400), rnd.Next(0, 300)); break;
                }
                Thread.Sleep(rnd.Next(0, 12));
            }
            r.P.Track = 0;
            r.P.Volume = 1;
            r.P.Repeat = false;
            r.P.Speed = 1;
            r.P.Start(0, 0);
            HpWait(r, 15000, "the end after the storm");
            Check(r.ErrorCount == 0, "errors in the storm: " + Tail(r.FirstError));
            Check(r.EndedCount > 0, "no end after the storm, state " + r.P.State);
            int before = r.Count;
            double ms = HpDispose(r);
            Thread.Sleep(300);
            Check(r.Count == before, "pictures after Dispose: " + (r.Count - before));
            Check(ms < 2000, "Dispose after the storm took " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms");
            Add("400 commands, " + before + " pictures, dispose " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms");
        }

        static void HpManyTracks()
        {
            NeedFfmpeg();
            string[] titles = { "Mezcla \u00F1and\u00FA & 100%", "Pista ^2", "Sistema (ruido) #3", "Micro; \u00BFhola?", "Track 5", "Pista $6", "Canal 7 'bis'", "\u65E5\u672C 8" };
            string path = Path.Combine(WorkDir("hp-tracks"), "tracks.mkv");
            StringBuilder args = new StringBuilder("-f lavfi -i \"testsrc2=s=320x180:r=30:d=1\"");
            for (int j = 0; j < 8; j++) args.Append(" -f lavfi -i \"sine=f=" + (300 + 100 * j) + ":d=1\"");
            args.Append(" -map 0:v");
            for (int j = 0; j < 8; j++) args.Append(" -map " + (j + 1) + ":a");
            args.Append(" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac");
            for (int j = 0; j < 8; j++) args.Append(" -metadata:s:a:" + j + " title=" + Q(titles[j]));
            args.Append(" " + Q(path));
            HpFfmpeg(args.ToString(), 120000);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "not playable");
            Check(mi.Audio.Count == 8, "audio tracks " + mi.Audio.Count + " of 8");
            List<string> bad = new List<string>();
            for (int j = 0; j < 8; j++)
                if (mi.Audio[j].Title != titles[j]) bad.Add("track " + j + " title '" + mi.Audio[j].Title + "' want '" + titles[j] + "'");
            Check(bad.Count == 0, string.Join(" | ", bad));
            foreach (int tr in new int[] { 0, 7, 3 })
            {
                PlFakeSound snd = new PlFakeSound();
                HpRun r = HpNew(path, snd);
                r.P.Track = tr;
                r.P.Start(0, 0);
                HpWait(r, 20000, "the end on track " + tr);
                Check(r.ErrorCount == 0, "track " + tr + " error: " + Tail(r.FirstError));
                Check(r.EndedCount > 0, "track " + tr + " no end");
                Check(snd.WriteCount > 0, "track " + tr + " wrote no sound");
                HpDispose(r);
            }
            Add("8 titles read back, tracks 0, 7 and 3 played");
        }

        // Make and play a clip many times: handles of this process must not pile up.
        static void HpCycle(string clip)
        {
            HpRun r = HpNew(clip, new PlFakeSound());
            r.P.Start(0, 0);
            r.P.Seek(0.5);
            r.P.Pause();
            r.P.Resume();
            HpWait(r, 20000, "the end of a cycle");
            HpDispose(r);
        }

        static void HpHandles()
        {
            NeedFfmpeg();
            string clip = PlMp4("hp-handles.mp4", true, 1);
            for (int i = 0; i < 3; i++) HpCycle(clip);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int h0;
            using (Process me = Process.GetCurrentProcess()) h0 = me.HandleCount;
            for (int i = 0; i < 30; i++) { MediaInfo.Probe(ffmpeg, clip); HpCycle(clip); }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int h1;
            using (Process me = Process.GetCurrentProcess()) h1 = me.HandleCount;
            Check(h1 - h0 < 60, "handles of the process grew by " + (h1 - h0) + " over 30 probes and plays (" + h0 + " -> " + h1 + ")");
            Add("handles " + h0 + " -> " + h1);
        }

        static void HpLongPath()
        {
            NeedFfmpeg();
            string clip = PlMp4("hp-long.mp4", false, 1);
            string root = WorkDir("hp-long");
            string dir = root;
            while (dir.Length < 290) dir = Path.Combine(dir, new string('d', 30));
            string path = Path.Combine(dir, "largo.mp4");
            try
            {
                Directory.CreateDirectory("\\\\?\\" + dir);
                File.Copy("\\\\?\\" + clip, "\\\\?\\" + path);
            }
            catch (Exception ex) { Skip("this .NET cannot make a path of " + path.Length + " characters: " + ex.Message); }
            Add("path " + path.Length + " characters");
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay, "a valid video of " + path.Length + " characters does not read as playable (ffmpeg has no long path support here)");
        }

        static void HpEightK()
        {
            NeedFfmpeg();
            string path = Path.Combine(WorkDir("hp-8k"), "8k.mp4");
            HpFfmpeg("-f lavfi -i \"color=c=0x336699:s=7680x4320:r=30:d=0.4\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(path), 180000);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay && mi.Width == 7680 && mi.Height == 4320, "8K reads as " + mi.Width + "x" + mi.Height);
            HpRun r = HpNew(path, new PlFakeSound());
            r.P.Start(0, 0);
            HpWait(r, 60000, "the end of the 8K clip");
            Check(r.ErrorCount == 0, "8K error: " + Tail(r.FirstError));
            // 8K decodes at about 13 fps here, so a 0.4 s clip cannot play in real time: some pictures are dropped by design.
            Check(r.Count >= 1, "8K pictures " + r.Count);
            HpShot s;
            lock (r.Shots) s = r.Shots[0];
            Check(s.W == 7680 && s.H == 4320, "8K picture " + s.W + "x" + s.H);
            Add("8K native: " + r.Count + " of 12 pictures in real time");
            HpDispose(r);
            HpRun small = HpNew(path, new PlFakeSound());
            small.P.SetDecodeSize(1280, 720);
            small.P.Start(0, 0);
            HpWait(small, 60000, "the end of the scaled 8K clip");
            Check(small.ErrorCount == 0, "scaled 8K error: " + Tail(small.FirstError));
            Check(small.Count >= 1, "scaled 8K pictures " + small.Count);
            lock (small.Shots) s = small.Shots[0];
            Check(s.W == 1280 && s.H == 720, "scaled 8K picture " + s.W + "x" + s.H);
            Add("scaled to 1280x720: " + small.Count + " pictures");
            HpDispose(small);
        }

        static void HpAudioOnly()
        {
            NeedFfmpeg();
            string dir = WorkDir("hp-audio");
            string m4a = Path.Combine(dir, "solo.m4a");
            HpFfmpeg("-f lavfi -i \"sine=f=440:d=1\" -c:a aac " + Q(m4a), 60000);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, m4a);
            Check(!mi.CanPlay, "audio-only file reads as playable: " + mi.Width + "x" + mi.Height + " fps " + mi.Fps);
            Check(mi.Audio.Count == 1, "audio tracks " + mi.Audio.Count);
            Check(ShotStack.RouteFor(m4a, true) == ShotStack.OpenRoute.Editor, "m4a routed to " + ShotStack.RouteFor(m4a, true));
            Check(!ShotStack.IsVideoFile(m4a), "m4a counted as video");
            string silent = PlMp4("hp-novoice.mp4", false, 1);
            MediaInfo vi = MediaInfo.Probe(ffmpeg, silent);
            Check(vi.CanPlay && vi.Audio.Count == 0, "video-only file: playable " + vi.CanPlay + ", audio " + vi.Audio.Count);
            PlFakeSound snd = new PlFakeSound();
            HpRun r = HpNew(silent, snd);
            r.P.Start(0, 0);
            HpWait(r, 20000, "the end of the video-only file");
            Check(r.ErrorCount == 0 && r.EndedCount > 0, "video-only file error " + Tail(r.FirstError));
            Check(snd.WriteCount == 0, "sound written for a file with no audio: " + snd.WriteCount);
            HpDispose(r);
            Add("audio-only refused, video-only plays silent");
        }

        static void HpWheel()
        {
            NeedFfmpeg();
            string path = PlMp4("hp-wheel.mp4", true, 1);
            Settings st = new Settings();
            PlFakeSound snd = new PlFakeSound();
            PlayerWindow w = PwWindow(path, snd, st);
            try
            {
                FieldInfo vf = typeof(PlayerWindow).GetField("volume", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo wheel = typeof(Control).GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
                Check(vf != null && wheel != null, "volume field or wheel handler not found");
                for (int i = 0; i < 30; i++) wheel.Invoke(w, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120) });
                double up = (double)vf.GetValue(w);
                Check(up <= 1 && up >= 0.99, "volume after 30 notches up: " + up);
                for (int i = 0; i < 30; i++) wheel.Invoke(w, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, -120) });
                double down = (double)vf.GetValue(w);
                Check(down >= 0 && down <= 0.01, "volume after 30 notches down: " + down);
                wheel.Invoke(w, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120) });
                double one = (double)vf.GetValue(w);
                Check(Math.Abs(one - 0.05) < 1e-9, "one notch up from 0 gives " + one);
                Add("volume clamps at 0 and 1");
            }
            finally { w.Dispose(); }
        }

        // A window shown off-screen takes 400 random keys, wheel turns and resizes; it must show no error and leave no FFmpeg.
        static void HpWindowStorm()
        {
            NeedFfmpeg();
            string path = PlMp4("hp-wstorm.mp4", true, 4);
            PlayerWindow w = PwWindow(path, new PlFakeSound(), new Settings());
            Playback engine = null;
            List<int> live = new List<int>();
            List<DateTime> started = new List<DateTime>();
            try
            {
                w.MinimumSize = Size.Empty;
                w.ClientSize = new Size(480, 270);
                w.Location = new Point(-6000, -6000);
                w.Show();
                engine = w.Engine;
                Random rnd = new Random(7);
                Keys[] keys = { Keys.Space, Keys.K, Keys.Left, Keys.Right, Keys.J, Keys.L, Keys.Oemcomma, Keys.OemPeriod, Keys.Up, Keys.Down, Keys.M, Keys.I, Keys.Home, Keys.End, Keys.Escape };
                MethodInfo wheel = typeof(Control).GetMethod("OnMouseWheel", BindingFlags.NonPublic | BindingFlags.Instance);
                int keysSent = 0;
                for (int i = 0; i < 400; i++)
                {
                    int op = rnd.Next(10);
                    if (op < 7) { PwKey(w, keys[rnd.Next(keys.Length)]); keysSent++; }
                    else if (op == 7) w.ClientSize = new Size(rnd.Next(200, 1000), rnd.Next(150, 700));
                    else wheel.Invoke(w, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, rnd.Next(2) == 0 ? 120 : -120) });
                    Application.DoEvents();
                    Thread.Sleep(rnd.Next(0, 10));
                }
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(5); }
                FieldInfo mf = typeof(PlayerWindow).GetField("message", BindingFlags.NonPublic | BindingFlags.Instance);
                string msg = (string)mf.GetValue(w);
                Check(msg == null, "the window shows a message after the storm: " + msg);
                Check(!w.IsDisposed, "the window was closed by a key");
                Add(keysSent + " keys, state " + engine.State + ", " + engine.FramesShown + " pictures shown");
                // Only the FFmpeg running now is followed: a PID that already ended can be reused by another program.
                foreach (int pid in engine.ProcessIds())
                {
                    try
                    {
                        using (Process pr = Process.GetProcessById(pid))
                        {
                            if (pr.HasExited) continue;
                            live.Add(pid);
                            started.Add(pr.StartTime);
                        }
                    }
                    catch (Exception) { }
                }
            }
            finally { w.Dispose(); }
            Add(live.Count + " FFmpeg running at the close");
            for (int k = 0; k < live.Count; k++)
            {
                int pid = live[k];
                Stopwatch dead = Stopwatch.StartNew();
                while (true)
                {
                    bool alive = false;
                    try { using (Process pr = Process.GetProcessById(pid)) alive = !pr.HasExited && pr.StartTime == started[k]; }
                    catch (ArgumentException) { alive = false; }
                    catch (InvalidOperationException) { alive = false; }
                    if (!alive) break;
                    if (dead.ElapsedMilliseconds > 3000) throw new TestFailed("ffmpeg " + pid + " still alive after the window closed");
                    Thread.Sleep(20);
                }
            }
        }

        // Pure geometry of the player window and of the frame and step arithmetic, over random sizes and times.
        static void HpGeometry()
        {
            Random rnd = new Random(42);
            List<string> bad = new List<string>();
            for (int i = 0; i < 3000; i++)
            {
                Size video = new Size(rnd.Next(1, 8000), rnd.Next(1, 4500));
                if (i % 3 == 0) video = new Size(rnd.Next(1, 40), rnd.Next(1, 40));
                Rectangle work = new Rectangle(0, 0, rnd.Next(300, 3840), rnd.Next(200, 2160));
                Size frame = new Size(rnd.Next(0, 40), rnd.Next(0, 60));
                Size win = PlayerKit.WindowFor(video, work, 560, frame);
                if (win.Width + frame.Width > Math.Max(work.Width, frame.Width + 1) || win.Height + frame.Height > Math.Max(work.Height, frame.Height + 1))
                    bad.Add("window " + win + " with frame " + frame + " past the work area " + work.Size + " for video " + video);
                if (video.Width + frame.Width <= work.Width && video.Height + frame.Height <= work.Height && video.Width >= 560 && win != video)
                    bad.Add("window " + win + " is not the real size " + video + " although it fits " + work.Size);
                Rectangle pic = PlayerKit.Picture(video, win, false);
                if (pic.Width > video.Width || pic.Height > video.Height) bad.Add("picture " + pic + " enlarged past " + video);
                if (video.Width >= 16 && video.Height >= 16 && win.Width >= 16 && win.Height >= 16)
                {
                    double a = win.Width / (double)win.Height, b = video.Width / (double)video.Height;
                    if (Math.Abs(a - b) / b > 0.04) bad.Add("window " + win + " distorts video " + video);
                }
                Size box = new Size(rnd.Next(50, 2000), rnd.Next(50, 1200));
                Size d = PlayerKit.DecodeSize(video, box);
                if (!d.IsEmpty)
                {
                    if (d.Width % 2 != 0 || d.Height % 2 != 0) bad.Add("odd decode size " + d + " for " + video + " in " + box);
                    if (d.Width > video.Width || d.Height > video.Height) bad.Add("decode " + d + " above native " + video);
                    if (d.Width > box.Width + 1 || d.Height > box.Height + 1) bad.Add("decode " + d + " bigger than box " + box);
                }
                double fps = 1 + rnd.NextDouble() * 120, dur = rnd.NextDouble() * 100;
                int lf = PlayerKit.LastFrame(dur, fps);
                if (lf > 0 && !(lf / fps < dur && (lf + 1) / fps >= dur - 1.1e-3 / fps))
                    bad.Add("last frame " + lf + " at " + fps + " fps for duration " + dur);
                double pos = rnd.NextDouble() * dur;
                double next = PlayerKit.StepTime(pos, fps, dur, 1), prev = PlayerKit.StepTime(pos, fps, dur, -1);
                if (next < prev - 1e-9 || next > dur + 1.0 / fps) bad.Add("step " + prev + " .. " + next + " from " + pos + " in " + dur);
                if (PlayerKit.StepTime(0, fps, dur, -1) != 0) bad.Add("step back from 0 is " + PlayerKit.StepTime(0, fps, dur, -1));
            }
            Check(PlayerKit.Clock(double.NaN) == "0:00", "Clock(NaN) = " + PlayerKit.Clock(double.NaN));
            Check(PlayerKit.Clock(-0.5) == "0:00", "Clock(-0.5) = " + PlayerKit.Clock(-0.5));
            Check(PlayerKit.Clock(59.99) == "0:59", "Clock(59.99) = " + PlayerKit.Clock(59.99));
            Check(PlayerKit.Clock(3600) == "1:00:00", "Clock(3600) = " + PlayerKit.Clock(3600));
            Check(bad.Count == 0, bad.Count + " bad: " + string.Join(" | ", bad.GetRange(0, Math.Min(5, bad.Count))));
            Add("3000 random sizes, times and steps");
        }
    }
}

// Adversarial tests of the joined export (suite clips): other frame rates and sizes, odd sizes, sound that is mono, 44.1 kHz
// or missing, a GIF, a three-track Cine master, trims and cuts across clips, speed, the quality levels, a BT.601 source
// and Windows' own decoder. Every source must keep its SHA-256 and mtime.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static string EdgeT(double t) { return t.ToString("0.000", CultureInfo.InvariantCulture); }

        // The colour at time t as the file's own tags say it is (no matrix forced).
        static int[] EdgeAuto(string file, double t, int x, int y)
        {
            string raw = file + "." + x + "x" + y + "." + EdgeT(t) + ".auto.raw";
            FfOk("-hide_banner -loglevel error -y -ss " + EdgeT(t) + " -i " + Q(file) + " -vf \"crop=2:2:" + (x & ~1) + ":" + (y & ~1) +
                 ",scale=in_color_matrix=auto:in_range=auto,format=rgb24\" -frames:v 1 -f rawvideo " + Q(raw), 60000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            Check(b.Length >= 3, "no pixel decoded at " + EdgeT(t));
            return new int[] { b[0], b[1], b[2] };
        }

        static List<VideoClip> EdgeList(params VideoClip[] clips)
        {
            List<VideoClip> l = new List<VideoClip>(clips);
            ClipSeq.Layout(l);
            return l;
        }

        static string EdgeStamps(List<VideoClip> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (VideoClip c in list) sb.Append(ShareStamp(c.Path)).Append(" | ");
            return sb.ToString();
        }

        static void EdgeUntouched(List<VideoClip> list, string before)
        {
            Check(EdgeStamps(list) == before, "a source changed during the export");
        }

        // Exports with the editor's own arguments, checks the file with ShareCheck and returns what FFmpeg wrote.
        static ShareProbe EdgeExport(ExportSpec s, double seconds)
        {
            ExportOk(s);
            ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, seconds, s.Fps, true)));
            return ShareCheck.ParseProbe(Probe(s.Output));
        }

        static void EdgeMf(string file, int w, int h, double seconds, double fps, bool audio)
        {
            MfResult r = MfDecode(file);
            Add("windows: " + r);
            Check(r.Error == null, "Media Foundation: " + r.Error);
            Check(r.Width == w && r.Height == h, "Windows sees " + r.Width + "x" + r.Height + ", expected " + w + "x" + h);
            Check(Math.Abs(r.VideoFrames - seconds * fps) <= 2, "Windows decodes " + r.VideoFrames + " frames, expected " + (seconds * fps).ToString("0.0", CultureInfo.InvariantCulture));
            if (audio) Check(Math.Abs(r.AudioSeconds - seconds) < 0.1, "Windows audio " + r.AudioSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s, expected " + seconds);
        }

        // Average of the left channel's tone power between two times (1 = a full-scale sine).
        static double EdgeTone(float[] au, double from, double to, double hz)
        {
            return ShareTonePower(au, (long)(from * 48000), (long)((to - from) * 48000), hz);
        }

        // Odd sizes cannot come from libx264 in yuv420p: an FFV1 Matroska keeps them.
        static VideoClip EdgeOdd(string dir, string name, string color, int w, int h, int fps, double dur)
        {
            string mkv = Path.Combine(dir, name + ".mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=" + color + ":s=" + w + "x" + h + ":r=" + fps + ":d=" + EdgeT(dur) + ",format=yuv420p\" -c:v ffv1 " + Q(mkv), 60000);
            return ClipsOf(mkv, dur, fps, w, h, false, false);
        }

        // Mono 44.1 kHz sound in an MP4 of flat colour.
        static VideoClip EdgeMono441(string dir, string name, string color, double dur, double hz)
        {
            string mp4 = Path.Combine(dir, name + ".mp4");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=" + color + ":s=320x240:r=30:d=" + EdgeT(dur) + ",format=yuv420p\" -f lavfi -i \"sine=f=" +
                 hz.ToString(CultureInfo.InvariantCulture) + ":sample_rate=44100:d=" + EdgeT(dur) + "\" -ac 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -b:a 96k -shortest " + Q(mp4), 60000);
            return ClipsOf(mp4, dur, 30, 320, 240, true, false);
        }

        // A GIF of flat colour (it has no sound).
        static VideoClip EdgeGif(string dir, string name, string color, int w, int h, double dur, int fps)
        {
            string gif = Path.Combine(dir, name + ".gif");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=" + color + ":s=" + w + "x" + h + ":r=" + fps + ":d=" + EdgeT(dur) + "\" -loop 0 " + Q(gif), 60000);
            return ClipsOf(gif, dur, fps, w, h, false, true);
        }

        static void EdgeSuite()
        {
            Test("clips", "edge-mixed-fps-24-60-30-and-sizes", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-mixed");
                VideoClip a = ClipsColor(dir, "a24", "red", 640, 360, 24, 1.0, 0);
                VideoClip b = ClipsColor(dir, "b60", "0x00C000", 320, 240, 60, 1.0, 0);
                VideoClip c = ClipsColor(dir, "c30", "blue", 1280, 720, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b, c);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "mixed (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 3.0);
                Check(p.Width == 640 && p.Height == 360 && Math.Abs(p.Fps - 24) < 0.01, "canvas of the first clip: " + p.Width + "x" + p.Height + " @ " + p.Fps);
                Check(Math.Abs(p.Seconds - 3.0) <= 0.1, "duration " + p.Seconds);
                ClipsHueIs(s.Output, 0.5, 320, 180, 'R');
                ClipsHueIs(s.Output, 1.5, 320, 180, 'G');
                ClipsHueIs(s.Output, 1.5, 40, 180, 'K');
                ClipsHueIs(s.Output, 1.5, 600, 180, 'K');
                ClipsHueIs(s.Output, 2.5, 320, 180, 'B');
                ClipsHueIs(s.Output, 2.5, 5, 5, 'B');
                EdgeMf(s.Output, 640, 360, 3.0, 24, false);
                EdgeUntouched(list, before);
                Add("24 fps canvas, 60 and 30 fps clips brought to it");
            });

            Test("clips", "edge-portrait-clip-after-landscape-canvas", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-portrait");
                VideoClip a = ClipsColor(dir, "land", "red", 1280, 720, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "port", "0x00C000", 720, 1280, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "p (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 2.0);
                Check(p.Width == 1280 && p.Height == 720, "canvas " + p.Width + "x" + p.Height);
                ClipsHueIs(s.Output, 0.5, 100, 360, 'R');
                ClipsHueIs(s.Output, 1.5, 640, 360, 'G');
                ClipsHueIs(s.Output, 1.5, 640, 5, 'G');
                ClipsHueIs(s.Output, 1.5, 100, 360, 'K');
                ClipsHueIs(s.Output, 1.5, 1180, 360, 'K');
                ClipsHueIs(s.Output, 1.5, 460, 360, 'G');
                ClipsHueIs(s.Output, 1.5, 820, 360, 'G');
                ClipsHueIs(s.Output, 1.5, 420, 360, 'K');
                ClipsHueIs(s.Output, 1.5, 860, 360, 'K');
                EdgeMf(s.Output, 1280, 720, 2.0, 30, false);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-landscape-clip-after-portrait-canvas", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-landscape");
                VideoClip a = ClipsColor(dir, "port", "red", 720, 1280, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "land", "0x00C000", 1280, 720, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "l (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 2.0);
                Check(p.Width == 720 && p.Height == 1280, "canvas " + p.Width + "x" + p.Height);
                ClipsHueIs(s.Output, 0.5, 360, 640, 'R');
                ClipsHueIs(s.Output, 1.5, 360, 640, 'G');
                ClipsHueIs(s.Output, 1.5, 360, 20, 'K');
                ClipsHueIs(s.Output, 1.5, 360, 1260, 'K');
                ClipsHueIs(s.Output, 1.5, 360, 460, 'G');
                ClipsHueIs(s.Output, 1.5, 360, 820, 'G');
                ClipsHueIs(s.Output, 1.5, 360, 420, 'K');
                ClipsHueIs(s.Output, 1.5, 360, 860, 'K');
                EdgeMf(s.Output, 720, 1280, 2.0, 30, false);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-odd-size-canvas-1001x601", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-odd-canvas");
                VideoClip a = EdgeOdd(dir, "odd1", "red", 1001, 601, 30, 1.0);
                VideoClip b = ClipsColor(dir, "wide", "0x00C000", 1280, 720, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "odd (editado).mp4"), null);
                // The editor makes the crop even before it reaches the arguments.
                s.Crop = new Rectangle(0, 0, 1000, 600);
                ShareProbe p = EdgeExport(s, 2.0);
                Check(p.Width == 1000 && p.Height == 600, "output " + p.Width + "x" + p.Height);
                ClipsHueIs(s.Output, 0.5, 500, 300, 'R');
                ClipsHueIs(s.Output, 0.5, 995, 5, 'R');
                ClipsHueIs(s.Output, 1.5, 500, 300, 'G');
                ClipsHueIs(s.Output, 1.5, 5, 5, 'K');
                ClipsHueIs(s.Output, 1.5, 500, 595, 'K');
                EdgeMf(s.Output, 1000, 600, 2.0, 30, false);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-odd-size-clip-after-even-canvas", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-odd-clip");
                VideoClip a = ClipsColor(dir, "even", "red", 1280, 720, 30, 1.0, 0);
                VideoClip b = EdgeOdd(dir, "odd2", "0x00C000", 1001, 601, 30, 1.0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "odd2 (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 2.0);
                Check(p.Width == 1280 && p.Height == 720, "canvas " + p.Width + "x" + p.Height);
                ClipsHueIs(s.Output, 0.5, 20, 360, 'R');
                ClipsHueIs(s.Output, 1.5, 640, 360, 'G');
                ClipsHueIs(s.Output, 1.5, 20, 360, 'K');
                ClipsHueIs(s.Output, 1.5, 1260, 360, 'K');
                EdgeMf(s.Output, 1280, 720, 2.0, 30, false);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-mono-441-silent-and-stereo-sound", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-sound");
                // Silent first, then stereo 48 kHz 440 Hz, then mono 44.1 kHz 880 Hz: the silent clip defines the canvas.
                VideoClip a = ClipsColor(dir, "silent", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "stereo", "0x00C000", 320, 240, 30, 2.0, 440);
                VideoClip c = EdgeMono441(dir, "mono", "blue", 1.0, 880);
                List<VideoClip> list = EdgeList(a, b, c);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "sound (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 4.0);
                Check(p.Audio.Count == 1 && p.Audio[0].Rate == 48000, "one 48 kHz track: " + p.Audio.Count);
                Check(Math.Abs(p.Seconds - 4.0) <= 0.1, "duration " + p.Seconds);
                float[] au = ClipsAudio(s.Output);
                Check(Math.Abs(au.Length / 2 / 48000.0 - 4.0) < 0.06, "audio length " + (au.Length / 2 / 48000.0).ToString("0.000", CultureInfo.InvariantCulture));
                Check(ClipsPeak(au, 0.1, 0.9) < 0.005, "the silent first clip has sound: " + ClipsPeak(au, 0.1, 0.9));
                double t440 = EdgeTone(au, 1.2, 2.8, 440), t880 = EdgeTone(au, 3.2, 3.8, 880), leak = EdgeTone(au, 3.2, 3.8, 440);
                Check(t440 > 0.001, "440 Hz stereo clip missing: " + t440.ToString("0.000000", CultureInfo.InvariantCulture));
                Check(t880 > 0.001, "880 Hz mono 44.1 kHz clip missing (or not at 48 kHz): " + t880.ToString("0.000000", CultureInfo.InvariantCulture));
                Check(leak < 0.0001, "440 Hz leaks into the 880 Hz clip: " + leak.ToString("0.000000", CultureInfo.InvariantCulture));
                ClipsHueIs(s.Output, 0.5, 160, 120, 'R');
                ClipsHueIs(s.Output, 2.0, 160, 120, 'G');
                ClipsHueIs(s.Output, 3.5, 160, 120, 'B');
                EdgeMf(s.Output, 320, 240, 4.0, 30, true);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-gif-clip-after-mp4-and-gif-canvas", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-gif");
                VideoClip m = ClipsColor(dir, "mp4", "red", 320, 240, 30, 1.0, 0);
                VideoClip g = EdgeGif(dir, "gif", "0x00C000", 320, 240, 1.0, 10);
                List<VideoClip> list = EdgeList(m, g);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "gif-mp4 (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 2.0);
                Check(Math.Abs(p.Fps - 30) < 0.01 && p.Audio.Count == 0, "30 fps, no sound: " + p.Fps + " " + p.Audio.Count);
                ClipsHueIs(s.Output, 0.5, 160, 120, 'R');
                ClipsHueIs(s.Output, 1.5, 160, 120, 'G');
                EdgeMf(s.Output, 320, 240, 2.0, 30, false);
                EdgeUntouched(list, before);

                // A GIF as the first clip: its 10 fps is the canvas rate and the 30 fps clip is brought down to it.
                List<VideoClip> first = EdgeList(g, m);
                string before2 = EdgeStamps(first);
                ExportSpec s2 = ClipsSpec(first, Path.Combine(dir, "gif-first (editado).mp4"), null);
                ShareProbe p2 = EdgeExport(s2, 2.0);
                Check(Math.Abs(p2.Fps - 10) < 0.01 && Math.Abs(p2.Seconds - 2.0) <= 0.15, "10 fps canvas, 2 s: " + p2.Fps + " / " + p2.Seconds);
                ClipsHueIs(s2.Output, 0.5, 160, 120, 'G');
                ClipsHueIs(s2.Output, 1.5, 160, 120, 'R');
                EdgeMf(s2.Output, 320, 240, 2.0, 10, false);
                EdgeUntouched(first, before2);
            });

            Test("clips", "edge-cine-three-tracks-mix-is-used", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-cine");
                const int w = 640, h = 360, fps = 30, frames = 60;
                long af = ShareAudioFrames(frames, fps);
                Action<byte[], int, int, int> paint = delegate(byte[] buf, int pw, int ph, int f) { ShareRect(buf, pw, 0, 0, pw, ph, new int[] { 200, 40, 40 }); };
                string master = ShareMaster(dir, "Cine T", w, h, fps, frames, paint, ShareTone(af, 440, 0.3), ShareTone(af, 880, 0.3));
                Check(Probe(master).Split(new string[] { "Audio:" }, StringSplitOptions.None).Length - 1 == 3, "the master has three audio tracks");
                VideoClip m = ClipsOf(master, frames / (double)fps, fps, w, h, true, true);
                VideoClip b = ClipsColor(dir, "blue", "blue", 320, 240, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(m, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "cine-mas-b (editado).mp4"), null);
                ShareProbe p = EdgeExport(s, 3.0);
                Check(p.Audio.Count == 1, "one track out of three: " + p.Audio.Count);
                float[] mix = ClipsAudio(master), outAu = ClipsAudio(s.Output);
                double num = 0, den = 0;
                for (long i = 4800; i < 91200; i++) { double d = outAu[i * 2] - mix[i * 2]; num += d * d; den += (double)mix[i * 2] * mix[i * 2]; }
                double ratio = Math.Sqrt(num / den);
                Check(den > 0 && ratio < 0.15, "sound differs from the mix track a:0 by " + ratio.ToString("0.000", CultureInfo.InvariantCulture) + " (relative RMS)");
                double s440 = EdgeTone(outAu, 0.3, 1.7, 440), s880 = EdgeTone(outAu, 0.3, 1.7, 880);
                Check(s440 > 0.005 && s880 > 0.005, "both tones of the mix present: " + s440.ToString("0.0000", CultureInfo.InvariantCulture) + " " + s880.ToString("0.0000", CultureInfo.InvariantCulture));
                Check(ClipsPeak(outAu, 2.2, 2.8) < 0.005, "the clip without sound is silent");
                EdgeMf(s.Output, w, h, 3.0, 30, true);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-trim-in-inside-second-clip-timeline", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-trim");
                VideoClip a = ClipsColor(dir, "t1", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "t2", "0x00C000", 320, 240, 30, 2.0, 0);
                VideoClip c = ClipsColor(dir, "t3", "blue", 320, 240, 30, 1.0, 0);
                List<VideoClip> list = new List<VideoClip> { a };
                ClipSeq.Layout(list);
                Timeline tl = new Timeline();
                tl.Clips = list;
                tl.Init(a.Duration);
                list.Add(b);
                tl.Extend(ClipSeq.Layout(list));
                list.Add(c);
                tl.Extend(ClipSeq.Layout(list));
                string before = EdgeStamps(list);
                // In at 1.5 s: half a second into the second clip, the third one whole.
                tl.In = 1.5;
                List<double[]> keep = tl.Keep();
                Check(keep.Count == 1 && Math.Abs(keep[0][0] - 1.5) < 1e-9 && Math.Abs(keep[0][1] - 4) < 1e-9, "keep " + KeepText(keep));
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "trim (editado).mp4"), keep);
                ShareProbe p = EdgeExport(s, 2.5);
                Check(Math.Abs(p.Seconds - 2.5) <= 0.1, "duration " + p.Seconds);
                ClipsHueIs(s.Output, 0.05, 160, 120, 'G');
                ClipsHueIs(s.Output, 1.0, 160, 120, 'G');
                ClipsHueIs(s.Output, 1.9, 160, 120, 'B');
                EdgeMf(s.Output, 320, 240, 2.5, 30, false);
                EdgeUntouched(list, before);
                tl.Dispose();
            });

            Test("clips", "edge-cut-spanning-three-clips-timeline", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-cut3");
                VideoClip a = ClipsColor(dir, "k1", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "k2", "0x00C000", 320, 240, 30, 1.0, 0);
                VideoClip c = ClipsColor(dir, "k3", "blue", 320, 240, 30, 1.0, 0);
                List<VideoClip> list = new List<VideoClip> { a };
                ClipSeq.Layout(list);
                Timeline tl = new Timeline();
                tl.Clips = list;
                tl.Init(a.Duration);
                list.Add(b);
                tl.Extend(ClipSeq.Layout(list));
                list.Add(c);
                tl.Extend(ClipSeq.Layout(list));
                string before = EdgeStamps(list);
                // Cut 0.6..2.4: the end of the first clip, all of the second and the start of the third.
                tl.Playhead = 0.6; tl.SplitAtPlayhead();
                tl.Playhead = 2.4; tl.SplitAtPlayhead();
                tl.Playhead = 0.8; tl.ToggleCut();
                tl.Playhead = 1.5; tl.ToggleCut();
                tl.Playhead = 2.2; tl.ToggleCut();
                List<double[]> keep = tl.Keep();
                Check(keep.Count == 2 && Math.Abs(keep[0][1] - 0.6) < 1e-9 && Math.Abs(keep[1][0] - 2.4) < 1e-9 && Math.Abs(keep[1][1] - 3.0) < 1e-9, "keep " + KeepText(keep));
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "cut3 (editado).mp4"), keep);
                ShareProbe p = EdgeExport(s, 1.2);
                Check(Math.Abs(p.Seconds - 1.2) <= 0.1, "duration " + p.Seconds);
                ClipsHueIs(s.Output, 0.3, 160, 120, 'R');
                ClipsHueIs(s.Output, 0.9, 160, 120, 'B');
                EdgeMf(s.Output, 320, 240, 1.2, 30, false);
                EdgeUntouched(list, before);
                tl.Dispose();
            });

            Test("clips", "edge-speed-1-5-and-2-over-two-clips", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-speed");
                VideoClip a = ClipsColor(dir, "s1", "red", 320, 240, 30, 1.0, 440);
                VideoClip b = ClipsColor(dir, "s2", "0x00C000", 320, 240, 30, 1.0, 880);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);

                ExportSpec two = ClipsSpec(list, Path.Combine(dir, "x2 (editado).mp4"), null);
                two.Speed = 2;
                ShareProbe p = EdgeExport(two, 2.0);
                Check(Math.Abs(p.Fps - 60) < 0.01 && Math.Abs(p.Seconds - 1.0) <= 0.07, "2x: " + p.Fps + " fps, " + p.Seconds + " s");
                ClipsHueIs(two.Output, 0.2, 160, 120, 'R');
                ClipsHueIs(two.Output, 0.7, 160, 120, 'G');
                float[] au = ClipsAudio(two.Output);
                Check(Math.Abs(au.Length / 2 / 48000.0 - 1.0) < 0.06, "2x audio length " + (au.Length / 2 / 48000.0).ToString("0.000", CultureInfo.InvariantCulture));
                double a440 = EdgeTone(au, 0.05, 0.45, 440), b880 = EdgeTone(au, 0.55, 0.95, 880), x880 = EdgeTone(au, 0.05, 0.45, 880);
                Check(a440 > 0.001 && b880 > 0.001 && x880 < 0.0001, "2x: pitch kept and clips in order (440 " + a440.ToString("0.000000", CultureInfo.InvariantCulture) + ", 880 " + b880.ToString("0.000000", CultureInfo.InvariantCulture) + ", leak " + x880.ToString("0.000000", CultureInfo.InvariantCulture) + ")");

                ExportSpec one5 = ClipsSpec(list, Path.Combine(dir, "x15 (editado).mp4"), null);
                one5.Speed = 1.5;
                ShareProbe q = EdgeExport(one5, 2.0);
                Check(Math.Abs(q.Fps - 45) < 0.01 && Math.Abs(q.Seconds - 2.0 / 1.5) <= 0.07, "1.5x: " + q.Fps + " fps, " + q.Seconds + " s");
                ClipsHueIs(one5.Output, 0.3, 160, 120, 'R');
                ClipsHueIs(one5.Output, 1.0, 160, 120, 'G');
                EdgeMf(one5.Output, 320, 240, 2.0 / 1.5, 45, true);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-many-cut-pieces-sound-stays-in-step", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-pieces");
                VideoClip a = ClipsColor(dir, "long", "red", 320, 240, 30, 4.0, 440);
                List<VideoClip> list = EdgeList(a);
                string before = EdgeStamps(list);
                // Eight pieces of 0.3 s, each separated by 0.2 s that is removed.
                List<double[]> keep = new List<double[]>();
                for (int k = 0; k < 8; k++) keep.Add(new double[] { 0.1 + 0.5 * k, 0.4 + 0.5 * k });
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "pieces (editado).mp4"), keep);
                ShareProbe p = EdgeExport(s, 2.4);
                Check(Math.Abs(p.Seconds - 2.4) <= 0.1, "duration " + p.Seconds);
                float[] au = ClipsAudio(s.Output);
                Check(Math.Abs(au.Length / 2 / 48000.0 - 2.4) < 0.06, "audio length " + (au.Length / 2 / 48000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s, expected 2.4");
                for (int k = 0; k < 8; k++)
                {
                    double from = k * 0.3;
                    Check(EdgeTone(au, from + 0.05, from + 0.25, 440) > 0.001, "no sound in piece " + k + " at " + EdgeT(from));
                    ClipsHueIs(s.Output, from + 0.15, 160, 120, 'R');
                }
                EdgeMf(s.Output, 320, 240, 2.4, 30, true);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-quality-levels-1080p60-joined-and-single", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-quality");
                VideoClip a = ClipsColor(dir, "hd1", "red", 1920, 1080, 60, 1.0, 0);
                VideoClip b = ClipsColor(dir, "hd2", "0x00C000", 1920, 1080, 60, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                long[] sizes = new long[3];
                for (int level = 0; level < 3; level++)
                {
                    ExportSpec s = ClipsSpec(list, Path.Combine(dir, "joined" + level + " (editado).mp4"), null);
                    s.Level = level;
                    string args = VideoExport.Arguments(s);
                    Check(args.Contains("-level:v 4.2 -g 120 -maxrate 62500k -bufsize 78125k") && args.Contains("-crf " + new int[] { 10, 14, 20 }[level] + " "), "level " + level + " args: " + args);
                    EdgeExport(s, 2.0);
                    sizes[level] = new FileInfo(s.Output).Length;
                }
                Check(sizes[0] > sizes[1] && sizes[1] > sizes[2], "joined sizes must fall: " + sizes[0] + " > " + sizes[1] + " > " + sizes[2]);
                for (int level = 0; level < 3; level++)
                {
                    ExportSpec s = new ExportSpec();
                    s.Source = a.Path;
                    s.Output = Path.Combine(dir, "single" + level + " (editado).mp4");
                    s.Crop = new Rectangle(0, 0, 1920, 1080);
                    s.Level = level;
                    s.Fps = 60;
                    ExportOk(s);
                    ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, 1.0, 60, true)));
                    sizes[level] = new FileInfo(s.Output).Length;
                }
                Check(sizes[0] > sizes[1] && sizes[1] > sizes[2], "single-clip sizes must fall: " + sizes[0] + " > " + sizes[1] + " > " + sizes[2]);
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-bt601-second-clip-keeps-its-colours", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-601");
                // A saturated blue, encoded with the BT.601 matrix and tagged as such (an SD video from an old camera or a DVD).
                string src = Path.Combine(dir, "sd601.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x0000FF:s=320x240:r=30:d=1\" -vf format=yuv420p -c:v libx264 -preset ultrafast " +
                     "-colorspace bt470bg -color_primaries bt470bg -color_trc smpte170m -color_range tv " + Q(src), 60000);
                string tags = Probe(src);
                Check(tags.Contains("bt470bg"), "the fixture is not tagged BT.601: " + Tail(tags));
                VideoClip a = ClipsColor(dir, "red709", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsOf(src, 1.0, 30, 320, 240, false, false);
                b.TakeColour(MediaInfo.Parse(Probe(src)));   // what the editor does when a clip is added
                Check(b.Matrix == "smpte170m", "the clip's matrix reads " + b.Matrix);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                int[] want = EdgeAuto(src, 0.5, 160, 120);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "601 (editado).mp4"), null);
                EdgeExport(s, 2.0);
                int[] got = EdgeAuto(s.Output, 1.5, 160, 120);
                // Control: the same source alone keeps its own tags (no joined conversion).
                ExportSpec alone = new ExportSpec();
                alone.Source = src;
                alone.Output = Path.Combine(dir, "601-alone (editado).mp4");
                alone.Crop = new Rectangle(0, 0, 320, 240);
                alone.Level = 1;
                alone.Fps = 30;
                ExportOk(alone);
                int[] control = EdgeAuto(alone.Output, 0.5, 160, 120);
                Add("source " + Rgb(want) + ", joined " + Rgb(got) + ", alone " + Rgb(control));
                Check(Diff(control, want) <= 6, "control export (single clip) changed the colours: " + Rgb(control) + " vs " + Rgb(want));
                Check(Diff(got, want) <= 6, "joined export shifts the colours of a BT.601 clip: " + Rgb(got) + " instead of " + Rgb(want));
                EdgeUntouched(list, before);
            });

            Test("clips", "colour-matrix-tags-and-args", delegate
            {
                MediaInfo sd = MediaInfo.Parse("  Duration: 00:00:01.00, start: 0.0\n  Stream #0:0: Video: h264 (High), yuv420p(tv, bt470bg/bt470bg/smpte170m, progressive), 320x240, 30 fps, 30 tbr, 15360 tbn\n");
                MediaInfo hd = MediaInfo.Parse("  Stream #0:0: Video: h264 (High), yuv420p(tv, bt709, progressive), 1280x720, 30 fps, 30 tbr, 15360 tbn\n");
                MediaInfo none = MediaInfo.Parse("  Stream #0:0: Video: h264 (High), yuv420p(tv, progressive), 1920x1080, 30 fps, 30 tbr, 15360 tbn\n");
                MediaInfo pc = MediaInfo.Parse("  Stream #0:0: Video: mjpeg, yuvj420p(pc, bt470bg/unknown/unknown), 640x480, 30 fps, 30 tbr, 15360 tbn\n");
                Check(sd.ColorMatrix == "bt470bg" && !sd.FullRange && hd.ColorMatrix == "bt709" && none.ColorMatrix == null && pc.FullRange, "tags: " + sd.ColorMatrix + " " + hd.ColorMatrix + " " + none.ColorMatrix + " " + pc.FullRange);
                Check(VideoExport.ClipMatrix("bt470bg", 320, 240) == "smpte170m" && VideoExport.ClipMatrix("bt709", 320, 240) == "bt709", "tagged matrices");
                Check(VideoExport.ClipMatrix(null, 1920, 1080) == "bt709" && VideoExport.ClipMatrix(null, 720, 576) == "smpte170m", "untagged follows the size");
                VideoClip a = ClipsOf("a.mp4", 1, 30, 1280, 720, false, false);
                VideoClip b = ClipsOf("b.mp4", 1, 30, 320, 240, false, false);
                VideoClip c = ClipsOf("c.mp4", 1, 30, 1280, 720, false, false);
                a.TakeColour(hd);
                b.TakeColour(sd);
                ExportSpec s = ClipsSpec(EdgeList(a, b, c), "o.mp4", null);
                string args = VideoExport.Arguments(s);
                Check(args.Contains("[1:v:0]scale=in_color_matrix=smpte170m:in_range=tv:out_color_matrix=bt709:out_range=tv:") && !args.Contains("[0:v:0]scale=in_color_matrix") && !args.Contains("[2:v:0]scale=in_color_matrix"), args);
            });

            Test("clips", "edge-gif-export-of-joined-clips", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-gif-out");
                VideoClip a = ClipsColor(dir, "g1", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "g2", "0x00C000", 320, 240, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "joined (editado).gif"), null);
                s.ToGif = true;
                ExportOk(s);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, 2.0, 30, true)));
                ClipsHueIs(s.Output, 0.5, 160, 120, 'R');
                ClipsHueIs(s.Output, 1.5, 160, 120, 'G');
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-gif-export-single-clip-colour-change", delegate
            {
                // Not joined at all: one file that turns from red to green, exported as a GIF by the existing path.
                NeedFfmpeg();
                string dir = WorkDir("edge-gif-one");
                string src = Path.Combine(dir, "one.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=red:s=320x240:r=30:d=1,format=yuv420p\" -f lavfi -i \"color=c=0x00C000:s=320x240:r=30:d=1,format=yuv420p\" " +
                     "-filter_complex \"[0:v][1:v]concat=n=2:v=1:a=0\" -c:v libx264 -preset ultrafast " + Q(src), 60000);
                VideoClip a = ClipsOf(src, 2.0, 30, 320, 240, false, false);
                List<VideoClip> list = EdgeList(a);
                string before = EdgeStamps(list);
                ExportSpec s = new ExportSpec();
                s.Source = src;
                s.Output = Path.Combine(dir, "one (editado).gif");
                s.Crop = new Rectangle(0, 0, 320, 240);
                s.ToGif = true;
                s.Fps = 30;
                ExportOk(s);
                ClipsHueIs(s.Output, 0.5, 160, 120, 'R');
                ClipsHueIs(s.Output, 1.5, 160, 120, 'G');
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-marks-and-backdrop-on-joined-clips", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-marks");
                VideoClip a = ClipsColor(dir, "m1", "red", 320, 240, 30, 1.0, 0);
                VideoClip b = ClipsColor(dir, "m2", "0x00C000", 320, 240, 30, 1.0, 0);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                // Marks: a blue square over the top-left corner, on a transparent PNG the size of the frame.
                string mark = Path.Combine(dir, "mark.png");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=blue:s=100x100,format=rgba,pad=320:240:0:0:color=black@0\" -frames:v 1 " + Q(mark), 60000);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "marks (editado).mp4"), null);
                s.Overlay = mark;
                EdgeExport(s, 2.0);
                ClipsHueIs(s.Output, 0.5, 50, 50, 'B');
                ClipsHueIs(s.Output, 1.5, 50, 50, 'B');
                ClipsHueIs(s.Output, 0.5, 200, 200, 'R');
                ClipsHueIs(s.Output, 1.5, 200, 200, 'G');
                // Backdrop: a grey frame 40 px wide around the 320x240 video, the marks on top; the video sits at 40,40.
                string frame = Path.Combine(dir, "frame.png");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x202020:s=400x320,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(between(X,40,359)*between(Y,40,279),0,255)'\" -frames:v 1 " + Q(frame), 60000);
                ExportSpec bg = ClipsSpec(list, Path.Combine(dir, "backdrop (editado).mp4"), null);
                bg.Backdrop = true;
                bg.Frame = new Size(400, 320);
                bg.Inner = new Rectangle(40, 40, 320, 240);
                bg.Overlay = frame;
                EdgeExport(bg, 2.0);
                ClipsHueIs(bg.Output, 1.5, 200, 160, 'G');
                ClipsHueIs(bg.Output, 0.5, 200, 160, 'R');
                int[] grey = ClipsPixel(bg.Output, 1.0, 380, 300);
                Check(grey[0] > 20 && grey[0] < 45 && grey[1] > 20 && grey[1] < 45 && grey[2] > 20 && grey[2] < 45, "backdrop colour " + Rgb(grey));
                EdgeUntouched(list, before);
            });

            Test("clips", "edge-rotated-phone-clip-after-landscape", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-rot");
                // Coded 640x360 with a blue quarter, displayed rotated 90 degrees: 360x640 (a phone held upright).
                string coded = Path.Combine(dir, "coded.mp4"), rot = Path.Combine(dir, "phone.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0xFF0000:s=640x360:r=30:d=1,format=yuv420p,drawbox=x=0:y=0:w=320:h=180:color=0x0000FF:t=fill\" " +
                     "-c:v libx264 -preset ultrafast " + Q(coded), 60000);
                // A remux that only sets the display rotation (the way a phone writes it).
                FfOk("-hide_banner -loglevel error -y -display_rotation:v:0 90 -i " + Q(coded) + " -c copy " + Q(rot), 60000);
                Check(Probe(rot).Contains("rotation of"), "the fixture has no display rotation: " + Tail(Probe(rot)));
                VideoClip a = ClipsColor(dir, "wide", "0x202020", 640, 360, 30, 1.0, 0);
                VideoClip b = ClipsOf(rot, 1.0, 30, 360, 640, false, false);
                List<VideoClip> list = EdgeList(a, b);
                string before = EdgeStamps(list);
                ExportSpec s = ClipsSpec(list, Path.Combine(dir, "rot (editado).mp4"), null);
                EdgeExport(s, 2.0);
                // The fitted phone picture: 202x360 at x = 219, scale 0.5625. Compare the hue at four points with the
                // picture the file itself shows (its own rotation applied).
                int[][] pts = { new int[] { 240, 40 }, new int[] { 300, 300 }, new int[] { 380, 150 }, new int[] { 410, 330 } };
                foreach (int[] p in pts)
                {
                    int xr = (int)Math.Round((p[0] - 219) / 0.5625), yr = (int)Math.Round(p[1] / 0.5625);
                    char want = ClipsHue(EdgeAuto(rot, 0.5, xr, yr));
                    ClipsHueIs(s.Output, 1.5, p[0], p[1], want);
                }
                EdgeMf(s.Output, 640, 360, 2.0, 30, false);
                EdgeUntouched(list, before);
            });
        }
    }
}

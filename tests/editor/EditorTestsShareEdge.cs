// Edge cases of the share export and of its check: masters without sound, with only the system sound, at 60 fps or
// half a second long, trims with cuts and speed, the 720p option, GIFs with odd rescaled heights, a mono source, and
// files the check must refuse. Every export runs with the real arguments; the master must stay byte-identical.
// Invoked from ShareSuite.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        // Packet count and end time (seconds) of one stream, read from "-f framecrc" with stream copy.
        static void ShareEdgeStreamInfo(string file, string map, out int count, out double end)
        {
            string txt = file + ".framecrc-" + map.Replace(':', '_') + ".txt";
            string err;
            int code = VideoExport.Run(ffmpeg, "-hide_banner -v error -i " + Q(file) + " -map " + map + " -c copy -f framecrc " + Q(txt), 120000, out err);
            Check(code == 0 && File.Exists(txt), "framecrc of " + map + " failed (" + code + "): " + Tail(err));
            double tb = 0, last = 0;
            count = 0;
            foreach (string line in File.ReadAllLines(txt))
            {
                if (line.StartsWith("#tb ", StringComparison.Ordinal))
                {
                    Match m = Regex.Match(line, @"^#tb \d+: (\d+)/(\d+)");
                    Check(m.Success, "no time base in: " + line);
                    tb = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(',');
                if (f.Length < 4) continue;
                count++;
                double pts = double.Parse(f[2].Trim(), CultureInfo.InvariantCulture), dur = double.Parse(f[3].Trim(), CultureInfo.InvariantCulture);
                last = Math.Max(last, pts + dur);
            }
            Check(tb > 0 && count > 0, "no packets listed for " + map);
            end = last * tb;
        }

        static ShareReport ShareEdgeValidate(string file, ShareExpect e)
        {
            return ShareCheck.Validate(ffmpeg, file, e);
        }

        static string ShareEdgeSize(ShareReport r)
        {
            ShareReport.Check c = r.Find(ShareCheck.NameSize);
            return c == null ? "no size check" : c.Detail;
        }

        static void ShareEdgeSuite()
        {
            Test("share", "edge-master-without-audio", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-silent");
                string master = ShareMaster(dir, "silencioso", 320, 180, 30, 60, ShareScreen, null, null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "sin sonido.mp4"), 320, 180, 30, false);
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 2.0, 30, true);
                ShareRequirePassed(ShareEdgeValidate(s.Output, e));
            });

            Test("share", "edge-master-system-sound-only", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-system");
                const int w = 320, h = 180, fps = 30, frames = 60;
                string master = ShareMaster(dir, "solo equipo", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "equipo.mp4"), w, h, fps, true);
                ShareExport(s);
                ShareRequirePassed(ShareEdgeValidate(s.Output, Editor.ExpectFor(s, 2.0, fps, true)));
                int n; double endA;
                ShareEdgeStreamInfo(s.Output, "0:a:0", out n, out endA);
                Check(Math.Abs(endA - 2.0) <= 0.1, "audio of the export ends at " + endA + " s, expected 2 s");
                Add("audio ends at " + endA.ToString("0.000", CultureInfo.InvariantCulture));
            });

            Test("share", "edge-master-60fps", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-60");
                const int w = 320, h = 180, fps = 60, frames = 60;
                string master = ShareMaster(dir, "60 fps", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "60.mp4"), w, h, fps, true);
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 1.0, fps, true);
                Check(e.Fps == 60, "a 60 fps source must be expected at 60, got " + e.Fps);
                ShareRequirePassed(ShareEdgeValidate(s.Output, e));
            });

            Test("share", "edge-master-half-second-and-short-trim", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-half");
                const int w = 320, h = 180, fps = 30, frames = 15;
                string master = ShareMaster(dir, "medio segundo", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec whole = ShareSpec(master, Path.Combine(dir, "entero.mp4"), w, h, fps, true);
                ShareExport(whole);
                ShareRequirePassed(ShareEdgeValidate(whole.Output, Editor.ExpectFor(whole, 0.5, fps, true)));
                ExportSpec cut = ShareSpec(master, Path.Combine(dir, "corto.mp4"), w, h, fps, true);
                cut.Keep = new List<double[]>();
                cut.Keep.Add(new double[] { 0.1, 0.4 });
                ShareExport(cut);
                ShareRequirePassed(ShareEdgeValidate(cut.Output, Editor.ExpectFor(cut, 0.5, fps, true)));
            });

            Test("share", "edge-trim-cut-speed2-consistent", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-tcs");
                const int w = 320, h = 180, fps = 30, frames = 150;
                string master = ShareMaster(dir, "tcs", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "tcs.mp4"), w, h, fps, true);
                s.Keep = new List<double[]>();
                s.Keep.Add(new double[] { 0.37, 1.91 });
                s.Keep.Add(new double[] { 2.5, 3.7 });
                s.Keep.Add(new double[] { 4.0, 4.9 });
                s.Speed = 2;
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 5.0, fps, true);
                ShareReport r = ShareEdgeValidate(s.Output, e);
                ShareRequirePassed(r);
                int nv, na; double endV, endA;
                ShareEdgeStreamInfo(s.Output, "0:v:0", out nv, out endV);
                ShareEdgeStreamInfo(s.Output, "0:a:0", out na, out endA);
                Add("expected " + e.Seconds.ToString("0.000", CultureInfo.InvariantCulture) + " s; video ends " + endV.ToString("0.000", CultureInfo.InvariantCulture) +
                    " s, audio ends " + endA.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Check(Math.Abs(endV - endA) <= 0.1, "video ends at " + endV + " s but audio at " + endA + " s");
                // Pieces hold 46 + 36 + 27 = 109 frames; a 2x speed-up keeps the frames, so the count must not move.
                Check(Math.Abs(nv - 109) <= 2, "the 2x export holds " + nv + " video packets, expected 109 (not checked by ShareCheck at speed != 1)");
            });

            Test("share", "edge-720p-of-1730x786", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-720");
                const int w = 1730, h = 786, fps = 30, frames = 15;
                string master = ShareMaster(dir, "720", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "720.mp4"), w, h, fps, true);
                s.MaxHeight = Timeline.SizeValues[2];
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, frames / (double)fps, fps, true);
                Check(e.Width % 2 == 0 && e.Height == 720, "expected size " + e.Width + "x" + e.Height);
                double ratio = (double)e.Width / e.Height, src = (double)w / h;
                Check(Math.Abs(ratio - src) / src < 0.001, "aspect " + ratio + " vs source " + src);
                ShareReport r = ShareEdgeValidate(s.Output, e);
                ShareRequirePassed(r);
                Add("720p export " + e.Width + "x" + e.Height);
            });

            Test("share", "edge-gif-of-master", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-gif");
                const int w = 320, h = 180, fps = 30, frames = 60;
                string master = ShareMaster(dir, "gif", w, h, fps, frames, ShareScreen, null, null);
                ExportSpec gs = ShareSpec(master, Path.Combine(dir, "salida.gif"), w, h, fps, false);
                gs.ToGif = true;
                ShareExport(gs);
                ShareRequirePassed(ShareEdgeValidate(gs.Output, Editor.ExpectFor(gs, 2.0, fps, true)));
            });

            Test("share", "edge-gif-odd-rescaled-height", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-gif-odd");
                int[][] sizes = new int[][] { new int[] { 1920, 218 }, new int[] { 1920, 222 } };
                foreach (int[] sz in sizes)
                {
                    int w = sz[0], h = sz[1], fps = 30, frames = 6;
                    string master = ShareMaster(dir, "gif " + w + "x" + h, w, h, fps, frames, ShareScreen, null, null);
                    ExportSpec gs = ShareSpec(master, Path.Combine(dir, "gif " + w + "x" + h + ".gif"), w, h, fps, false);
                    gs.ToGif = true;
                    ShareExport(gs);
                    ShareExpect e = Editor.ExpectFor(gs, 0.2, fps, true);
                    ShareReport r = ShareEdgeValidate(gs.Output, e);
                    Add(w + "x" + h + ": expected " + e.Width + "x" + e.Height + ", got " + ShareEdgeSize(r));
                    ShareRequirePassed(r);
                }
            });

            Test("share", "edge-master-untouched-by-every-export", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-sha");
                const int w = 320, h = 180, fps = 30, frames = 90;
                string master = ShareMaster(dir, "sha", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3),
                                            ShareTone(ShareAudioFrames(frames, fps), 880, 0.3));
                string before = ShareStamp(master);
                ExportSpec whole = ShareSpec(master, Path.Combine(dir, "entero.mp4"), w, h, fps, true);
                ShareExport(whole);
                ShareRequirePassed(ShareEdgeValidate(whole.Output, Editor.ExpectFor(whole, 3.0, fps, true)));
                ExportSpec cut = ShareSpec(master, Path.Combine(dir, "cortado.mp4"), w, h, fps, true);
                cut.Keep = new List<double[]>();
                cut.Keep.Add(new double[] { 0.5, 1.5 });
                cut.Keep.Add(new double[] { 2.0, 2.7 });
                cut.Speed = 1.5;
                ShareExport(cut);
                ShareRequirePassed(ShareEdgeValidate(cut.Output, Editor.ExpectFor(cut, 3.0, fps, true)));
                ExportSpec gs = ShareSpec(master, Path.Combine(dir, "salida.gif"), w, h, fps, false);
                gs.ToGif = true;
                ShareExport(gs);
                ShareRequirePassed(ShareEdgeValidate(gs.Output, Editor.ExpectFor(gs, 3.0, fps, true)));
                Check(ShareStamp(master) == before, "the master changed after the exports");
            });

            Test("share", "edge-check-moov-at-end", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-moov");
                const int w = 320, h = 180, fps = 30, frames = 30;
                string master = ShareMaster(dir, "moov", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "bien.mp4"), w, h, fps, true);
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 1.0, fps, true);
                ShareRequirePassed(ShareEdgeValidate(s.Output, e));
                string late = Path.Combine(dir, "moov al final.mp4");
                FfOk("-hide_banner -v error -i " + Q(s.Output) + " -map 0 -c copy " + Q(late), 120000);
                ShareReport r = ShareEdgeValidate(late, e);
                Add("failing: " + string.Join(", ", ShareFailingNames(r)));
                Check(!r.Passed && r.Failed(ShareCheck.NameFaststart) && !r.Failed(ShareCheck.NameStructure), "a moov-at-end MP4 must fail the faststart check: " + r.ToString().Replace("\n", " | "));
            });

            Test("share", "edge-check-extra-audio-track", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-two-audio");
                const int w = 320, h = 180, fps = 30, frames = 30;
                string master = ShareMaster(dir, "dos sonidos", w, h, fps, frames, ShareScreen, ShareTone(ShareAudioFrames(frames, fps), 440, 0.3), null);
                ExportSpec s = ShareSpec(master, Path.Combine(dir, "uno.mp4"), w, h, fps, true);
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 1.0, fps, true);
                string two = Path.Combine(dir, "dos.mp4");
                FfOk("-hide_banner -v error -i " + Q(s.Output) + " -map 0:v:0 -map 0:a:0 -map 0:a:0 -c copy " + Q(two), 120000);
                ShareReport r = ShareEdgeValidate(two, e);
                Check(!r.Passed && r.Failed(ShareCheck.NameAudio), "two audio tracks must fail the sound check: " + r.ToString().Replace("\n", " | "));
            });

            Test("share", "edge-check-zero-byte-and-junk", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-junk");
                ShareExpect e = new ShareExpect();
                e.Width = 320; e.Height = 180; e.Fps = 30; e.Seconds = 1; e.HasAudio = true; e.RgbSource = true;
                string zero = Path.Combine(dir, "vacio.mp4");
                File.WriteAllBytes(zero, new byte[0]);
                ShareReport rz = ShareEdgeValidate(zero, e);
                Check(!rz.Passed && rz.Failed(ShareCheck.NameFile) && rz.Checks.Count == 1, "zero bytes: " + rz.ToString().Replace("\n", " | "));
                Check(rz.Summary.Contains(ShareCheck.NameFile) && rz.Summary.Contains("bytes"), "zero bytes summary: " + rz.Summary);
                string zeros = Path.Combine(dir, "ceros.mp4");
                File.WriteAllBytes(zeros, new byte[4096]);
                ShareReport rk = ShareEdgeValidate(zeros, e);
                Check(!rk.Passed && rk.Failed(ShareCheck.NameStructure), "4 KB of zeros passed: " + rk.ToString().Replace("\n", " | "));
                ShareReport rm = ShareEdgeValidate(Path.Combine(dir, "no existe.mp4"), e);
                Check(!rm.Passed && rm.Checks.Count == 1 && rm.Failed(ShareCheck.NameFile), "missing file: " + rm.ToString().Replace("\n", " | "));
                Add("zero: " + rz.Summary);
            });

            Test("share", "edge-mono-source-trimmed-passes", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("edge-mono");
                string src = Path.Combine(dir, "mono.mp4");
                FfOk("-hide_banner -v error -f lavfi -i testsrc=size=320x180:rate=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 3 " +
                     "-map 0:v -map 1:a -ac 1 -c:v libx264 -pix_fmt yuv420p -c:a aac -b:a 128k " + Q(src), 120000);
                Check(Regex.IsMatch(Probe(src), @"Audio: aac.*mono"), "the source is not mono: " + Tail(Probe(src)));
                ExportSpec s = ShareSpec(src, Path.Combine(dir, "recortado.mp4"), 320, 180, 30, true);
                s.RgbSource = false;
                s.Level = 0;
                s.Keep = new List<double[]>();
                s.Keep.Add(new double[] { 0.5, 2.0 });
                ShareExport(s);
                ShareExpect e = Editor.ExpectFor(s, 3.0, 30, true);
                ShareReport r = ShareEdgeValidate(s.Output, e);
                Add("report: " + r.Summary);
                ShareRequirePassed(r);
            });
        }

        static List<string> ShareFailingNames(ShareReport r)
        {
            List<string> names = new List<string>();
            foreach (ShareReport.Check c in r.Checks) if (!c.Passed) names.Add(c.Name);
            return names;
        }
    }
}

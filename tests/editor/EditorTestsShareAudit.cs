// Tests from the audit of the share export (docs/cine-share-export.md): the level the stream declares, the master's
// description left behind, the chosen sound track, variable-rate sources keeping every frame, the chroma location and
// the player's own check of a master. Every export runs with the real arguments. Invoked from ShareSuite.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        // The settings every export used before the audit: x264 chose the level itself and asked for 5.0.
        const string AuditOldCodec = "-c:v libx264 -preset slow -crf 10 -profile:v high -g 60 -maxrate 40M -bufsize 80M";

        // The H.264 header of a file as ShareCheck reads it, plus the reference frames it declares.
        static ShareCheck.SpsInfo AuditSps(string file, out int refs)
        {
            string err;
            Ff("-hide_banner -i " + Q(file) + " -map 0:v:0 -c copy -bsf:v trace_headers -frames:v 1 -f null -", out err, 30000);
            Match m = Regex.Match(err, @"\bmax_num_ref_frames\s+[01]+\s*=\s*(\d+)");
            refs = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            return ShareCheck.ParseSps(err);
        }

        // Power of a tone in the first sound track of a file (left channel, from 0.25 s on).
        static double AuditTone(string file, double hz)
        {
            string raw = file + ".tone.f32";
            FfOk("-hide_banner -loglevel error -y -i " + Q(file) + " -map 0:a:0 -ac 2 -ar 48000 -f f32le " + Q(raw), 60000);
            float[] s = ShareReadF32(raw);
            File.Delete(raw);
            long count = Math.Min(48000, s.Length / 2 - 12000);
            Check(count > 4800, "too little sound in " + file);
            return ShareTonePower(s, 12000, count, hz);
        }

        // The time of every decoded frame, to the millisecond.
        static List<string> AuditFrameTimes(string file)
        {
            string err;
            Ff("-hide_banner -i " + Q(file) + " -map 0:v:0 -vf showinfo -f null -", out err, 60000);
            List<string> t = new List<string>();
            foreach (Match m in Regex.Matches(err, @"pts_time:([\d.]+)"))
                t.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("0.000", CultureInfo.InvariantCulture));
            return t;
        }

        // MD5 of every decoded picture, in order.
        static List<string> AuditPictures(string file)
        {
            string err;
            string txt = file + ".framemd5.txt";
            int code = Ff("-hide_banner -v error -y -i " + Q(file) + " -map 0:v:0 -f framemd5 " + Q(txt), out err, 120000);
            Check(code == 0, "framemd5 failed: " + Tail(err));
            List<string> r = new List<string>();
            foreach (string line in File.ReadAllLines(txt))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split(',');
                r.Add(f[f.Length - 1].Trim());
            }
            File.Delete(txt);
            return r;
        }

        // The expectation of an export with everything the stream itself can show: level and chroma location included.
        static ShareExpect AuditExpect(ExportSpec s, double seconds)
        {
            ShareExpect e = ShareExpectOf(s, seconds);
            e.Level = VideoExport.H264Level(VideoExport.OutputSize(s), VideoExport.OutFps(s.Fps, s.Speed));
            e.CentredChroma = VideoExport.ChromaMadeHere(s);
            return e;
        }

        static void ShareAuditSuite()
        {
            Test("share", "audit-level-declared-in-the-stream", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("audit-level");
                // Width, height, rate, level and the decoded picture buffer of that level (macroblocks).
                int[][] cases = { new[] { 640, 360, 30, 41, 32768 }, new[] { 1888, 968, 30, 41, 32768 }, new[] { 1920, 1080, 60, 42, 34816 }, new[] { 1920, 1200, 30, 50, 110400 } };
                List<string> notes = new List<string>();
                foreach (int[] k in cases)
                {
                    Size sz = new Size(k[0], k[1]);
                    string mp4 = Path.Combine(dir, k[0] + "x" + k[1] + "@" + k[2] + ".mp4");
                    FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=" + k[0] + "x" + k[1] + ":r=" + k[2] + ":d=0.2 -pix_fmt yuv420p " + VideoExport.ShareCodec(0, sz, k[2]) + " " + Q(mp4), 120000);
                    int refs;
                    ShareCheck.SpsInfo sps = AuditSps(mp4, out refs);
                    long mbs = (long)((k[0] + 15) / 16) * ((k[1] + 15) / 16);
                    Check(sps.Level == k[3], k[0] + "x" + k[1] + "@" + k[2] + " declares " + sps.Level + ", expected " + k[3]);
                    Check(refs > 0 && refs * mbs <= k[4], k[0] + "x" + k[1] + ": " + refs + " reference frames do not fit level " + k[3]);
                    notes.Add(k[0] + "x" + k[1] + "@" + k[2] + " level " + ShareCheck.LevelName(sps.Level) + ", " + refs + " refs");
                }
                // Before: the reference recording's size with the old settings (5 references, an 80 Mbit buffer) said 5.0.
                string old = Path.Combine(dir, "old.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=1888x968:r=30:d=0.2 -pix_fmt yuv420p " + AuditOldCodec + " " + Q(old), 120000);
                int oldRefs;
                int oldLevel = AuditSps(old, out oldRefs).Level;
                Check(oldLevel == 50, "the old settings were expected to declare 5.0, got " + oldLevel);
                Add(string.Join("; ", notes.ToArray()) + "; old settings 1888x968@30: level 5.0, " + oldRefs + " refs");
            });

            Test("share", "audit-master-description-stays-behind", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string probe = Probe(shareFixMp4);
                Check(probe.IndexOf("Stackshot Cine", StringComparison.Ordinal) < 0 && probe.IndexOf("sin p\u00E9rdidas", StringComparison.Ordinal) < 0,
                      "the MP4 still describes itself as the lossless master: " + Tail(probe));
                Check(probe.Contains(VideoExport.ShareComment), "the MP4 does not say what it is: " + Tail(probe));
                Check(!probe.Contains("Mezcla") && !probe.Contains("(original)"), "a track name of the master came along: " + Tail(probe));
                Check(!probe.Contains("UDTA parsing failed"), "FFmpeg complains when reading the MP4: " + Tail(probe));
                MediaInfo mi = MediaInfo.Probe(ffmpeg, shareFixMp4);
                Check(!PlayerKit.IsMaster(mi), "the player takes the MP4 for a master");
                foreach (string[] row in PlayerKit.Describe(mi, shareFixMp4, 1, null))
                    Check(row[0] != "M\u00E1ster sin p\u00E9rdidas", "the info panel calls the MP4 lossless");
                // An MP4 exported before the audit copied the master's description, which is all the player looked for.
                string old = Path.Combine(shareFixDir, "antes (para compartir).mp4");
                FfOk("-hide_banner -loglevel error -y -i " + Q(shareFixMp4) + " -map 0 -c copy -metadata comment=\"Stackshot Cine: v\u00EDdeo H.264 RGB sin p\u00E9rdidas y sonido PCM de 32 bits\" " + Q(old), 60000);
                MediaInfo om = MediaInfo.Probe(ffmpeg, old);
                Check(om.Raw.Contains("Stackshot Cine") && !PlayerKit.IsMaster(om), "an old export is taken for a master");
                foreach (string[] row in PlayerKit.Describe(om, old, 1, null))
                    Check(row[0] != "M\u00E1ster sin p\u00E9rdidas", "the info panel calls an old export lossless");
                MediaInfo mm = MediaInfo.Probe(ffmpeg, shareFixMaster);
                Check(PlayerKit.IsMaster(mm), "the master itself is no longer recognised");
                Add("comment: " + VideoExport.ShareComment + "; old exports are not masters either");
            });

            Test("share", "audit-chosen-sound-track", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string dir = WorkDir("audit-track");
                List<string> notes = new List<string>();
                // Track 1 is the computer sound (440 Hz), track 2 the microphone (880 Hz): whole, cut in two, and sped up.
                for (int t = 1; t <= 2; t++)
                    for (int form = 0; form < 3; form++)
                    {
                        ExportSpec s = ShareSpec(shareFixMaster, Path.Combine(dir, "pista " + t + " forma " + form + ".mp4"), 640, 360, 30, true);
                        s.AudioTrack = t;
                        if (form == 1) s.Keep = new List<double[]> { new double[] { 0, 0.9 }, new double[] { 1.1, 2.0 } };
                        if (form == 2) s.Speed = 1.5;
                        ShareExport(s);
                        ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, ShareFixSeconds(), 30, true)));
                        double want = AuditTone(s.Output, t == 1 ? 440 : 880), other = AuditTone(s.Output, t == 1 ? 880 : 440);
                        Check(want > 1e-3 && want > 100 * other, "track " + t + " form " + form + ": wanted tone " + want + ", other " + other);
                        if (form == 0) notes.Add("track " + t + ": " + (t == 1 ? "440" : "880") + " Hz only (" + (want / Math.Max(other, 1e-12)).ToString("0", CultureInfo.InvariantCulture) + "x)");
                    }
                Add(string.Join("; ", notes.ToArray()) + "; also through cuts and speed");
            });

            Test("share", "audit-variable-rate-keeps-every-frame", delegate
            {
                NeedFfmpeg();
                string dir = WorkDir("audit-vfr");
                string src = Path.Combine(dir, "movil.mp4");
                // 90 frames that alternate 1/60 s and 1/20 s apart, like a phone camera that changes its rate (with B-frames,
                // as phones write them).
                FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=60 -vf \"setpts='(floor(N/2)*(1/60+1/20)+mod(N,2)/60)/TB'\" -frames:v 90" +
                     " -fps_mode passthrough -c:v libx264 -preset medium -crf 18 -video_track_timescale 90000 " + Q(src), 60000);
                List<string> before = AuditFrameTimes(src);
                Check(before.Count == 90, "the source has " + before.Count + " frames");
                MediaInfo mi = MediaInfo.Probe(ffmpeg, src);
                ExportSpec s = new ExportSpec();
                s.Source = src;
                s.Output = Path.Combine(dir, "movil (editado).mp4");
                s.Crop = new Rectangle(0, 0, 640, 360);
                s.Fps = mi.Fps;
                ExportOk(s);
                List<string> after = AuditFrameTimes(s.Output);
                Check(after.Count == before.Count, after.Count + " frames out of " + before.Count);
                for (int i = 0; i < before.Count; i++) Check(after[i] == before[i], "frame " + i + " moved from " + before[i] + " s to " + after[i] + " s");
                // Both windows' checks accept it: the player's and the editor's, which read the rate as variable (30.34 fps on
                // a 60 tbr base) and so check the length instead of a frame rate the file does not have.
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, PlayerKit.ExportExpect(mi, s)));
                double avg;
                bool constant = Editor.ConstantRate(Probe(src), out avg);
                Check(!constant && avg > 30 && avg < 31, "the phone-like source should read as a variable rate: " + avg);
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, mi.Duration, avg, constant)));
                // Before, the editor took any printed rate as constant: this export would then fail on its own average.
                ShareReport strict = ShareCheck.Validate(ffmpeg, s.Output, Editor.ExpectFor(s, mi.Duration, avg, true));
                // FFmpeg's default for MP4 (no -fps_mode) is what dropped them.
                string dropped = Path.Combine(dir, "sin passthrough.mp4");
                s.Output = dropped;
                string err;
                Check(Ff(VideoExport.Arguments(s).Replace("-fps_mode passthrough ", ""), out err, 120000) == 0, "export without passthrough failed: " + Tail(err));
                int lost = before.Count - AuditFrameTimes(dropped).Count;
                Add("90 of 90 frames at their times (source " + mi.Fps.ToString("0.##", CultureInfo.InvariantCulture) + " fps average); without passthrough " + lost +
                    " were dropped; a constant-rate check would say: " + (strict.Passed ? "passed" : ShareFailing(strict)));
                double f;
                Check(Editor.ConstantRate("  Stream #0:0: Video: h264 (High 4:4:4 Predictive), gbrp(pc), 1888x968, 30 fps, 30 tbr, 1k tbn", out f) && f == 30, "a Cine master is constant");
                Check(Editor.ConstantRate("  Stream #0:0(und): Video: h264 (High), yuv420p(tv), 1920x1080, 8000 kb/s, 29.97 fps, 29.97 tbr, 30k tbn", out f) && f == 29.97, "NTSC constant");
                Check(!Editor.ConstantRate("  Stream #0:0(und): Video: h264 (High), yuv420p, 640x360, 1009 kb/s, 30.34 fps, 60 tbr, 90k tbn", out f) && f == 30.34, "variable");
                Check(Editor.ConstantRate("  Stream #0:0: Video: mpeg4, yuv420p, 320x180, 25 fps", out f) && f == 25, "no tbr printed");
                Check(!Editor.ConstantRate("  Stream #0:0: Audio: aac", out f) && f == 0, "no video");
            });

            Test("share", "audit-chroma-location", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                int refs;
                Check(AuditSps(shareFixMp4, out refs).ChromaLoc == 1, "the master's export does not say its chroma is centred");
                ShareRequirePassed(ShareCheck.Validate(ffmpeg, shareFixMp4, AuditExpect(shareFixSpec, ShareFixSeconds())));
                // Only the label is new: the pictures are the ones the same export made without it.
                string dir = WorkDir("audit-chroma");
                ExportSpec s = ShareSpec(shareFixMaster, Path.Combine(dir, "sin ubicacion.mp4"), 640, 360, 30, true);
                string err;
                Check(Ff(VideoExport.Arguments(s).Replace(VideoExport.CentredChroma, ""), out err, 120000) == 0, "export without the location failed: " + Tail(err));
                Check(AuditSps(s.Output, out refs).ChromaLoc == -1, "without the filter the stream should say nothing");
                List<string> a = AuditPictures(shareFixMp4), b = AuditPictures(s.Output);
                Check(a.Count == b.Count && a.Count > 0, "frames: " + a.Count + " vs " + b.Count);
                for (int i = 0; i < a.Count; i++) Check(a[i] == b[i], "picture " + i + " differs: the location changed the pixels");
                // The check notices a stream that does not say it.
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, s.Output, AuditExpect(s, ShareFixSeconds())), ShareCheck.NameColor);
                // A YUV source keeps the chroma it had and its implicit location.
                string yuv = Path.Combine(dir, "yuv.mp4");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i testsrc2=s=320x240:r=30:d=1 -pix_fmt yuv420p -c:v libx264 -preset ultrafast " + Q(yuv), 60000);
                ExportSpec y = new ExportSpec();
                y.Source = yuv;
                y.Output = Path.Combine(dir, "yuv (editado).mp4");
                y.Crop = new Rectangle(0, 0, 320, 240);
                y.Fps = 30;
                ExportOk(y);
                Check(!VideoExport.Arguments(y).Contains("out_chroma_loc") && AuditSps(y.Output, out refs).ChromaLoc == -1, "a YUV source got a chroma location");
                Add(a.Count + " pictures identical with and without the label; YUV sources unchanged");
            });

            Test("share", "audit-export-folder", delegate
            {
                // An export of a saved video stays next to it (kept); anything else goes with the temporary captures.
                string save = @"C:\Users\x\Pictures\Capturas";
                Check(ShotStack.ExportFolder(save + @"\Cine 2026-10-11 17.03.26.mkv", save) == save, "a saved master's export goes to the save folder");
                Check(ShotStack.ExportFolder(@"c:\users\X\pictures\capturas\a.mkv", save + @"\") == save + @"\", "case and a trailing backslash do not matter");
                Check(ShotStack.ExportFolder(Path.Combine(Settings.TempDir, "Cine.mkv"), save) == Settings.TempDir, "a temporary recording's export stays temporary");
                Check(ShotStack.ExportFolder(save + @"\sub\a.mkv", save) == Settings.TempDir, "a subfolder is not the save folder");
                Check(ShotStack.ExportFolder(@"D:\Descargas\v.mov", save) == Settings.TempDir && ShotStack.ExportFolder(null, save) == Settings.TempDir &&
                      ShotStack.ExportFolder(save + @"\a.mkv", null) == Settings.TempDir, "elsewhere, null source or no save folder");
                Add("saved -> next to it; otherwise the temp folder");
            });

            Test("share", "audit-check-refuses-a-wrong-level", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string dir = WorkDir("audit-level-check");
                ExportSpec s = ShareSpec(shareFixMaster, Path.Combine(dir, "nivel 5.mp4"), 640, 360, 30, true);
                string err;
                string args = VideoExport.Arguments(s).Replace(VideoExport.ShareCodec(0, new Size(640, 360), 30), AuditOldCodec);
                Check(args.Contains(AuditOldCodec), "the old settings were not swapped in: " + args);
                Check(Ff(args, out err, 120000) == 0, "export failed: " + Tail(err));
                ShareOnlyFails(ShareCheck.Validate(ffmpeg, s.Output, AuditExpect(s, ShareFixSeconds())), ShareCheck.NameLevel);
                Check(ShareCheck.ParseSps("x  24          level_idc        00101001 = 41\n chroma_sample_loc_type_top_field  010 = 1").Level == 41 &&
                      ShareCheck.ParseSps("").Level == -1 && ShareCheck.ParseSps("").ChromaLoc == -1, "ParseSps");
                Add("an export that declares 5.0 instead of 4.1 fails only '" + ShareCheck.NameLevel + "'");
            });

            Test("share", "audit-player-export-of-a-master", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string dir = WorkDir("audit-player");
                string master = Path.Combine(dir, "Cine 3 pistas.mkv");
                File.Copy(shareFixMaster, master);
                string stamp = ShareStamp(master);
                PlayerWindow.Quiet = true;
                PlayerWindow w = PwWindow(master, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    Check(h != IntPtr.Zero && w.Engine != null, "no player");
                    w.Engine.Track = 2;
                    typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
                    Stopwatch sw = Stopwatch.StartNew();
                    while (w.ExportNote == null && sw.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(10); }
                    Check(w.ExportNote == "ok", "export note: " + w.ExportNote);
                    string outFile = Path.Combine(dir, "Cine 3 pistas (para compartir).mp4");
                    Check(File.Exists(outFile), "no file at " + outFile);
                    // What the window checked: a master's rate, frames, level and centred chroma too.
                    ExportSpec s = ShareSpec(master, outFile, 640, 360, 30, true);
                    ShareExpect e = PlayerKit.ExportExpect(w.Media, s);
                    Check(e.Fps == 30 && e.Level == 41 && e.CentredChroma && e.RgbSource, "the player's expectation of a master: fps " + e.Fps + ", level " + e.Level);
                    ShareReport rep = ShareCheck.Validate(ffmpeg, outFile, e);
                    ShareRequirePassed(rep);
                    Check(rep.Find(ShareCheck.NameFps) != null && rep.Find(ShareCheck.NameFrames) != null && rep.Find(ShareCheck.NameLevel) != null, "checks: " + rep.ToString().Replace("\n", " | "));
                    double mic = AuditTone(outFile, 880), sys = AuditTone(outFile, 440);
                    Check(mic > 1e-3 && mic > 100 * sys, "the export is not the track being listened to (880 Hz " + mic + ", 440 Hz " + sys + ")");
                    Check(ShareStamp(master) == stamp, "the master changed");
                    Add(rep.Checks.Count + " checks, the microphone track, master untouched");
                }
                finally { w.Dispose(); PlayerWindow.Quiet = false; }
            });

            Test("share", "audit-player-consecutive-incomplete-and-closed", delegate
            {
                NeedFfmpeg();
                ShareFixture();
                string dir = WorkDir("audit-player-robust");
                string master = Path.Combine(dir, "Cine 3 pistas.mkv");
                File.Copy(shareFixMaster, master);
                PlayerWindow.Quiet = true;
                List<string> notes = new List<string>();
                MethodInfo export = typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance);
                try
                {
                    // Two exports in a row: the second gets its own name and both pass.
                    PlayerWindow w = PwWindow(master, new PlFakeSound(), new Settings());
                    try
                    {
                        IntPtr h = w.Handle;
                        for (int k = 0; k < 2; k++)
                        {
                            export.Invoke(w, null);
                            Stopwatch sw = Stopwatch.StartNew();
                            while (w.ExportNote == null && sw.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(10); }
                            Check(w.ExportNote == "ok", "export " + (k + 1) + ": " + w.ExportNote);
                            typeof(PlayerWindow).GetField("exportNote", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(w, null);
                        }
                    }
                    finally { w.Dispose(); }
                    string first = Path.Combine(dir, "Cine 3 pistas (para compartir).mp4"), second = Path.Combine(dir, "Cine 3 pistas (para compartir) (2).mp4");
                    Check(File.Exists(first) && File.Exists(second), "two exports in a row should leave two files");
                    notes.Add("two in a row: both ok, the second as \"(2)\"");

                    // A master cut short after it was opened: the export is never presented as good.
                    string cut = Path.Combine(dir, "Cine cortado.mkv");
                    File.Copy(shareFixMaster, cut);
                    w = PwWindow(cut, new PlFakeSound(), new Settings());
                    try
                    {
                        IntPtr h = w.Handle;
                        using (FileStream fs = new FileStream(cut, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) fs.SetLength(fs.Length / 2);
                        export.Invoke(w, null);
                        Stopwatch sw = Stopwatch.StartNew();
                        while (w.ExportNote == null && sw.ElapsedMilliseconds < 120000) { Application.DoEvents(); Thread.Sleep(10); }
                        Check(w.ExportNote != null && w.ExportNote != "ok", "an export of a cut master was accepted: " + w.ExportNote);
                        Check(!File.Exists(Path.Combine(dir, "Cine cortado (para compartir).mp4")), "the failed export kept the normal name");
                        notes.Add("cut master: " + (File.Exists(Path.Combine(dir, "Cine cortado (para compartir) (sin validar).mp4")) ? "kept as \"(sin validar)\"" : "no file") +
                                  " (" + w.ExportNote.Split('\n')[0] + ")");
                    }
                    finally { w.Dispose(); }

                    // Closing the window while it exports stops FFmpeg and removes the partial file.
                    string longer = ShareMaster(dir, "Cine largo", 640, 360, 30, 900, ShareScreen, ShareTone(ShareAudioFrames(900, 30), 440, 0.3), null);
                    w = PwWindow(longer, new PlFakeSound(), new Settings());
                    string partial = Path.Combine(dir, "Cine largo (para compartir).mp4");
                    try
                    {
                        IntPtr h = w.Handle;
                        export.Invoke(w, null);
                        Stopwatch sw = Stopwatch.StartNew();
                        while (!File.Exists(partial) && sw.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(10); }
                        Check(File.Exists(partial), "the long export never started");
                        w.Close();
                        Application.DoEvents();
                    }
                    finally { w.Dispose(); }
                    Thread.Sleep(500);
                    Check(!File.Exists(partial), "closing the window left the partial file");
                    notes.Add("closed while exporting: partial file removed");
                }
                finally { PlayerWindow.Quiet = false; }
                Add(string.Join("; ", notes.ToArray()));
            });
        }
    }
}

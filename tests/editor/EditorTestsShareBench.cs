// Benchmark of the share export against a real Cine master (not in the default suites): run-tests.ps1 sharebench.
//   STACKSHOT_BENCH_MASTER     the master (.mkv); it is copied first and only the copy is read. Without it, a synthetic
//                              10 s scrolling-text master is built.
//   STACKSHOT_BENCH_REFERENCE  optional: an MP4 already exported from that master, measured as it is (and the old
//                              settings must reproduce its pictures exactly).
//   STACKSHOT_BENCH_REGIONS    optional: "name:x,y,w,h,from,to;..." areas (seconds) measured on their own, e.g. text.
//   STACKSHOT_BENCH_CROPS      optional: "x,y,w,h,t;..." areas saved as PNG at 3x (master and every variant).
//   STACKSHOT_BENCH_OUT        where the table and the crops go (default %TEMP%\stackshot-share-bench).
// Every variant goes through the master with the real VideoExport.Arguments; only the codec part changes. PSNR and SSIM
// compare RGB to RGB: the master's gbrp against the MP4 decoded with BT.709, limited range.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        sealed class BenchRegion
        {
            public string Name;
            public Rectangle R;
            public double From, To;
        }

        sealed class BenchRow
        {
            public string Name, File, Note = "";
            public double Seconds = -1, CpuSeconds = -1, VideoKbps = -1, AudioKbps = -1;
            public long Bytes = -1, PeakMb = -1;
            public int Level = -1, Refs = -1, ChromaLoc = -1;
            public string Check = "", Mf = "";
            public double Psnr = -1, PsnrMin = -1, PsnrLeft = -1, Ssim = -1, R = -1, G = -1, B = -1;
            public Dictionary<string, double> Regions = new Dictionary<string, double>();
        }

        static string BenchN(double v, string f) { return v < 0 ? "-" : double.IsPositiveInfinity(v) ? "inf" : v.ToString(f, CultureInfo.InvariantCulture); }

        static double BenchParse(string s)
        {
            if (s == "inf") return double.PositiveInfinity;
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        static string BenchSha(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // Runs FFmpeg and measures wall time, processor time and the peak working set (sampled while it runs).
        static int BenchRun(string args, BenchRow row, out string stderr)
        {
            StringBuilder err = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo.FileName = ffmpeg;
                p.StartInfo.Arguments = "-nostdin " + args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e) { if (e.Data != null) lock (err) { if (err.Length < 20000) err.AppendLine(e.Data); } };
                p.OutputDataReceived += delegate { };
                Stopwatch sw = Stopwatch.StartNew();
                p.Start();
                ChildJob.Add(p);
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                long peak = 0;
                TimeSpan cpu = TimeSpan.Zero;
                while (!p.WaitForExit(200))
                {
                    try { p.Refresh(); peak = Math.Max(peak, p.PeakWorkingSet64); cpu = p.TotalProcessorTime; } catch { }
                    if (sw.Elapsed.TotalMinutes > 30) { try { p.Kill(); } catch { } break; }
                }
                p.WaitForExit();
                try { cpu = p.TotalProcessorTime; } catch { }
                row.Seconds = sw.Elapsed.TotalSeconds;
                row.CpuSeconds = cpu.TotalSeconds;
                row.PeakMb = peak >> 20;
                lock (err) stderr = err.ToString();
                return p.ExitCode;
            }
        }

        // PSNR (RGB average, worst frame, per channel) and SSIM of a file against the master, plus PSNR per region. pre is
        // the chain that turns input 0 into gbrp: the decode of an MP4 (with a given chroma location), or a conversion.
        static void BenchMetrics(string file, string master, double fps, string pre, List<BenchRegion> regions, BenchRow row, bool regionsAndSsim)
        {
            string re = "setpts=N/(" + fps.ToString("0.###", CultureInfo.InvariantCulture) + "*TB)";
            int n = 2 + (regionsAndSsim ? regions.Count : 0);
            StringBuilder g = new StringBuilder();
            g.Append("[0:v]" + pre + "," + re + ",split=" + n);
            for (int i = 0; i < n; i++) g.Append("[a" + i + "]");
            g.Append(";[1:v]format=gbrp," + re + ",split=" + n);
            for (int i = 0; i < n; i++) g.Append("[b" + i + "]");
            g.Append(";[a0][b0]psnr@full[p0];");
            if (regionsAndSsim) g.Append("[a1][b1]ssim@full[s0];");
            else g.Append("[a1]nullsink;[b1]nullsink;");
            string maps = " -map \"[p0]\" -f null -" + (regionsAndSsim ? " -map \"[s0]\" -f null -" : "");
            if (regionsAndSsim)
                for (int k = 0; k < regions.Count; k++)
                {
                    BenchRegion r = regions[k];
                    int f0 = (int)Math.Round(r.From * fps), f1 = (int)Math.Round(r.To * fps);
                    string cut = "trim=start_frame=" + f0 + ":end_frame=" + f1 + ",crop=" + r.R.Width + ":" + r.R.Height + ":" + r.R.X + ":" + r.R.Y;
                    g.Append("[a" + (2 + k) + "]" + cut + "[ra" + k + "];[b" + (2 + k) + "]" + cut + "[rb" + k + "];[ra" + k + "][rb" + k + "]psnr@r" + k + "[pr" + k + "];");
                    maps += " -map \"[pr" + k + "]\" -f null -";
                }
            string graph = g.ToString().TrimEnd(';');
            string err;
            int code = Ff("-hide_banner -i " + Q(file) + " -i " + Q(master) + " -filter_complex \"" + graph + "\"" + maps, out err, 1800000);
            Check(code == 0, "metrics of " + Path.GetFileName(file) + " failed (" + code + "): " + Tail(err));
            foreach (Match m in Regex.Matches(err, @"\[psnr@(\w+) @[^\]]*\] PSNR r:([\d.inf]+) g:([\d.inf]+) b:([\d.inf]+) average:([\d.inf]+) min:([\d.inf]+)"))
            {
                string who = m.Groups[1].Value;
                if (who == "full")
                {
                    row.R = BenchParse(m.Groups[2].Value); row.G = BenchParse(m.Groups[3].Value); row.B = BenchParse(m.Groups[4].Value);
                    row.Psnr = BenchParse(m.Groups[5].Value); row.PsnrMin = BenchParse(m.Groups[6].Value);
                }
                else row.Regions[regions[int.Parse(who.Substring(1), CultureInfo.InvariantCulture)].Name] = BenchParse(m.Groups[5].Value);
            }
            Match s = Regex.Match(err, @"\[ssim@full @[^\]]*\] SSIM .*?All:([\d.]+)");
            if (s.Success) row.Ssim = BenchParse(s.Groups[1].Value);
            Check(row.Psnr >= 0, "no PSNR for " + Path.GetFileName(file) + ": " + Tail(err));
        }

        static string BenchBack(string loc)
        {
            return "scale=in_color_matrix=bt709:in_range=tv" + (loc != null ? ":in_chroma_loc=" + loc : "") + ":flags=accurate_rnd+full_chroma_int,format=gbrp";
        }

        static void BenchProbe(string file, BenchRow row)
        {
            string probe = Probe(file);
            Match v = Regex.Match(probe, @"Video: .*?, (\d+) kb/s");
            if (v.Success) row.VideoKbps = double.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture);
            Match a = Regex.Match(probe, @"Audio: .*?, (\d+) kb/s");
            if (a.Success) row.AudioKbps = double.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture);
            row.Bytes = new FileInfo(file).Length;
            int refs;
            ShareCheck.SpsInfo sps = AuditSps(file, out refs);
            row.Level = sps.Level;
            row.Refs = refs;
            row.ChromaLoc = sps.ChromaLoc;
        }

        static void BenchCrops(string file, string tag, List<int[]> crops, string outDir, bool yuv)
        {
            for (int i = 0; i < crops.Count; i++)
            {
                int[] c = crops[i];
                string png = Path.Combine(outDir, "recorte" + (i + 1) + "-" + tag + ".png");
                string conv = yuv ? BenchBack(null) + "," : "format=gbrp,";
                FfOk("-hide_banner -loglevel error -y -ss " + c[4] + " -i " + Q(file) + " -frames:v 1 -vf \"" + conv + "crop=" + c[2] + ":" + c[3] + ":" + c[0] + ":" + c[1] +
                     ",scale=iw*3:ih*3:flags=neighbor,format=rgb24\" " + Q(png), 60000);
            }
        }

        static void ShareBenchSuite()
        {
            Test("sharebench", "share-export-benchmark", delegate
            {
                NeedFfmpeg();
                string outDir = Environment.GetEnvironmentVariable("STACKSHOT_BENCH_OUT");
                if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "stackshot-share-bench");
                Directory.CreateDirectory(outDir);
                string dir = WorkDir("bench");
                string given = Environment.GetEnvironmentVariable("STACKSHOT_BENCH_MASTER"), reference = Environment.GetEnvironmentVariable("STACKSHOT_BENCH_REFERENCE");
                string master = Path.Combine(dir, "master.mkv"), givenSha = null;
                if (!string.IsNullOrEmpty(given))
                {
                    givenSha = BenchSha(given);
                    File.Copy(given, master);
                    Check(BenchSha(master) == givenSha, "the copy of the master differs");
                }
                else master = ShareMaster(dir, "master", 1730, 786, 30, 300, ShareScreen, ShareTone(ShareAudioFrames(300, 30), 440, 0.3), null);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, master);
                Check(mi.Width > 0 && mi.Fps > 0 && VideoExport.IsRgbFormat(master, mi.PixelFormat), "not a Cine master: " + mi.PixelFormat);
                double fps = mi.Fps;
                Size size = new Size(mi.Width & ~1, mi.Height & ~1);

                List<BenchRegion> regions = new List<BenchRegion>();
                foreach (string part in (Environment.GetEnvironmentVariable("STACKSHOT_BENCH_REGIONS") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] nv = part.Split(':');
                    string[] v = nv[1].Split(',');
                    BenchRegion r = new BenchRegion();
                    r.Name = nv[0];
                    r.R = new Rectangle(int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]), int.Parse(v[3]));
                    r.From = double.Parse(v[4], CultureInfo.InvariantCulture);
                    r.To = double.Parse(v[5], CultureInfo.InvariantCulture);
                    regions.Add(r);
                }
                List<int[]> crops = new List<int[]>();
                foreach (string part in (Environment.GetEnvironmentVariable("STACKSHOT_BENCH_CROPS") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] v = part.Split(',');
                    crops.Add(new int[] { int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]), int.Parse(v[3]), int.Parse(v[4]) });
                }

                ExportSpec spec = ShareSpec(master, null, size.Width, size.Height, (int)Math.Round(fps), mi.Audio.Count > 0);
                spec.Fps = fps;
                string newCodec = VideoExport.ShareCodec(0, size, fps);
                // name, codec part, whether the chroma location is written, pixel format and profile
                List<string[]> variants = new List<string[]>();
                variants.Add(new[] { "antes (CRF 10, 40M, nivel autom\u00E1tico)", AuditOldCodec.Replace("-g 60", "-g " + Math.Max(2, (int)Math.Round(fps * 2))), "old" });
                variants.Add(new[] { "M\u00E1xima (CRF 10)", newCodec, "new" });
                variants.Add(new[] { "CRF 12", newCodec.Replace("-crf 10 ", "-crf 12 "), "new" });
                variants.Add(new[] { "Alta (CRF 14)", VideoExport.ShareCodec(1, size, fps), "new" });
                variants.Add(new[] { "CRF 16", newCodec.Replace("-crf 10 ", "-crf 16 "), "new" });
                variants.Add(new[] { "CRF 18", newCodec.Replace("-crf 10 ", "-crf 18 "), "new" });
                variants.Add(new[] { "Ligera (CRF 20)", VideoExport.ShareCodec(2, size, fps), "new" });
                variants.Add(new[] { "CRF 10 preset slower", newCodec.Replace("-preset slow ", "-preset slower ").Replace("-preset medium ", "-preset slow "), "new" });
                variants.Add(new[] { "CRF 10 4:4:4 (High 4:4:4)", newCodec.Replace("-profile:v high", "-profile:v high444"), "444" });

                List<BenchRow> rows = new List<BenchRow>();
                StringBuilder log = new StringBuilder();
                if (!string.IsNullOrEmpty(reference))
                {
                    BenchRow rr = new BenchRow();
                    rr.Name = "referencia (" + Path.GetFileName(reference) + ")";
                    rr.File = reference;
                    BenchProbe(reference, rr);
                    BenchMetrics(reference, master, fps, BenchBack(null), regions, rr, true);
                    BenchRow rl = new BenchRow();
                    BenchMetrics(reference, master, fps, BenchBack(rr.ChromaLoc == 1 ? "center" : "left"), regions, rl, false);
                    rr.PsnrLeft = rl.Psnr;
                    rows.Add(rr);
                }
                for (int i = 0; i < variants.Count; i++)
                {
                    string[] v = variants[i];
                    BenchRow row = new BenchRow();
                    row.Name = v[0];
                    row.File = Path.Combine(dir, "v" + i + ".mp4");
                    spec.Output = row.File;
                    string args = VideoExport.Arguments(spec).Replace(newCodec, v[1]);
                    if (v[2] == "old") args = args.Replace(VideoExport.CentredChroma, "").Replace("-fps_mode passthrough ", "");
                    if (v[2] == "444") args = args.Replace(VideoExport.CentredChroma + "format=yuv420p[o]", "format=yuv444p[o]");
                    Check(args.Contains(v[1]), "the codec of '" + v[0] + "' was not swapped in: " + args);
                    string err;
                    int code = BenchRun(args, row, out err);
                    Check(code == 0, v[0] + ": FFmpeg " + code + ": " + Tail(err));
                    BenchProbe(row.File, row);
                    if (v[2] != "444")
                    {
                        ShareExpect e = AuditExpect(spec, mi.Duration);
                        if (v[2] == "old") { e.Level = 0; e.CentredChroma = false; }
                        ShareReport rep = ShareCheck.Validate(ffmpeg, row.File, e);
                        row.Check = rep.Passed ? "ok (" + rep.Checks.Count + ")" : "FALLO: " + ShareFailing(rep);
                    }
                    else
                    {
                        string derr;
                        row.Check = Ff("-hide_banner -v error -i " + Q(row.File) + " -f null -", out derr, 600000) == 0 && derr.Trim().Length == 0 ? "se decodifica (FFmpeg)" : "error al decodificar";
                    }
                    try
                    {
                        MfResult mf = MfDecode(row.File);
                        row.Mf = mf.Error == null && mf.VideoFrames > 0 ? "s\u00ED (" + mf.VideoFrames + " fotogramas)" : "no: " + (mf.Error ?? "sin fotogramas");
                    }
                    catch (Exception ex) { row.Mf = "no comprobado: " + ex.Message; }
                    BenchMetrics(row.File, master, fps, BenchBack(null), regions, row, true);
                    BenchRow left = new BenchRow();
                    BenchMetrics(row.File, master, fps, BenchBack(row.ChromaLoc == 1 ? "center" : "left"), regions, left, false);
                    row.PsnrLeft = left.Psnr;
                    rows.Add(row);
                    if (crops.Count > 0 && (i <= 1 || v[2] == "444" || v[0].StartsWith("CRF 18", StringComparison.Ordinal)))
                        BenchCrops(row.File, "v" + i, crops, outDir, true);
                    // The export as Stackshot makes it now stays next to the table, to be watched.
                    if (i == 1) File.Copy(row.File, Path.Combine(outDir, "nuevo (M\u00E1xima).mp4"), true);
                    log.Append(v[0] + ": " + BenchN(row.Psnr, "0.00") + " dB, " + (row.Bytes >> 10) + " KiB; ");
                    Console.WriteLine("  bench " + v[0] + ": " + BenchN(row.Psnr, "0.00") + " dB, " + BenchN(row.VideoKbps, "0") + " kb/s, " + BenchN(row.Seconds, "0.0") + " s");
                }
                // The ceiling of any 4:2:0 file: the master converted to yuv420p and back, with no encoder at all.
                BenchRow ceil = new BenchRow();
                ceil.Name = "techo 4:2:0 sin codificar";
                BenchMetrics(master, master, fps, VideoExport.RgbToYuv + ",format=yuv444p,format=yuv420p," + BenchBack(null), regions, ceil, true);
                rows.Add(ceil);
                if (crops.Count > 0) BenchCrops(master, "master", crops, outDir, false);

                // The old settings must reproduce the reference export's pictures exactly (same FFmpeg, same master).
                string repro = "";
                if (!string.IsNullOrEmpty(reference))
                {
                    List<string> a = AuditPictures(reference), b = AuditPictures(rows[1].File);
                    bool same = a.Count == b.Count;
                    for (int i = 0; same && i < a.Count; i++) same = a[i] == b[i];
                    repro = same ? "Los ajustes de antes reproducen la referencia: " + a.Count + " im\u00E1genes id\u00E9nticas." : "Los ajustes de antes NO reproducen la referencia (" + a.Count + " / " + b.Count + " im\u00E1genes).";
                }

                // Fast scrolling: a 6 s synthetic master made of four frames of this one, 24 px per frame, through the old and new ceilings.
                StringBuilder stress = new StringBuilder();
                {
                    double d = mi.Duration;
                    string tall = Path.Combine(dir, "tall.png"), scroll = Path.Combine(dir, "scroll.mkv");
                    string ins = "";
                    for (int k = 0; k < 4; k++) ins += "-ss " + (d * (k + 0.5) / 4).ToString("0.000", CultureInfo.InvariantCulture) + " -i " + Q(master) + " ";
                    FfOk("-hide_banner -loglevel error -y " + ins + "-filter_complex \"[0:v][1:v][2:v][3:v]vstack=inputs=4,format=gbrp\" -frames:v 1 " + Q(tall), 120000);
                    int sf = (int)Math.Round(fps);
                    FfOk("-hide_banner -loglevel error -y -loop 1 -framerate " + sf + " -i " + Q(tall) + " -vf \"crop=" + size.Width + ":" + size.Height + ":0:'mod(n*24," + "ih-" + size.Height + ")',format=gbrp\" -t 6 -c:v libx264rgb -qp 0 -preset ultrafast " + Q(scroll), 600000);
                    ExportSpec ss = ShareSpec(scroll, null, size.Width, size.Height, sf, false);
                    string[][] sv = { new[] { "antes (40M/80M)", AuditOldCodec.Replace("-g 60", "-g " + (sf * 2)) }, new[] { "ahora (" + VideoExport.ShareCodec(0, size, sf).Substring(VideoExport.ShareCodec(0, size, sf).IndexOf("-maxrate", StringComparison.Ordinal)) + ")", VideoExport.ShareCodec(0, size, sf) },
                                      new[] { "sin tope", Regex.Replace(VideoExport.ShareCodec(0, size, sf), @" -level:v \S+| -maxrate \S+ -bufsize \S+", "") } };
                    stress.Append("| Tope | V\u00EDdeo | PSNR medio | Peor fotograma | Nivel |\n|---|---|---|---|---|\n");
                    for (int k = 0; k < sv.Length; k++)
                    {
                        BenchRow row = new BenchRow();
                        ss.Output = Path.Combine(dir, "scroll" + k + ".mp4");
                        string err;
                        string args = VideoExport.Arguments(ss).Replace(VideoExport.ShareCodec(0, size, sf), sv[k][1]);
                        Check(BenchRun(args, row, out err) == 0, "scroll " + k + ": " + Tail(err));
                        BenchProbe(ss.Output, row);
                        BenchMetrics(ss.Output, scroll, sf, BenchBack(null), regions, row, false);
                        stress.Append("| " + sv[k][0] + " | " + BenchN(row.VideoKbps / 1000, "0.0") + " Mb/s | " + BenchN(row.Psnr, "0.00") + " dB | " + BenchN(row.PsnrMin, "0.00") + " dB | " + ShareCheck.LevelName(row.Level) + " |\n");
                    }
                }

                StringBuilder md = new StringBuilder();
                md.Append("# Benchmark de la exportaci\u00F3n para compartir\n\n");
                md.Append("M\u00E1ster: " + (given ?? "sint\u00E9tico") + " (" + mi.Width + "x" + mi.Height + ", " + fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps, " + mi.Duration.ToString("0.0", CultureInfo.InvariantCulture) + " s, " + mi.PixelFormat + ")\n\n");
                md.Append("PSNR y SSIM en RGB contra el m\u00E1ster (gbrp), con el MP4 decodificado BT.709 de rango limitado. \u00ABPSNR norma\u00BB: el croma situado como dice el archivo (sin indicaci\u00F3n, H.264 entiende \u00ABizquierda\u00BB). Tiempos en este equipo.\n\n");
                md.Append("| Variante | V\u00EDdeo | Sonido | Archivo | Tiempo | CPU | Memoria | Nivel | Ref. | PSNR | Peor | R/G/B | PSNR norma | SSIM |");
                foreach (BenchRegion r in regions) md.Append(" " + r.Name + " |");
                md.Append(" Comprobaci\u00F3n | Windows (MF) |\n|");
                for (int i = 0; i < 16 + regions.Count; i++) md.Append("---|");
                md.Append("\n");
                foreach (BenchRow r in rows)
                {
                    md.Append("| " + r.Name + " | " + BenchN(r.VideoKbps / 1000, "0.00") + " Mb/s | " + BenchN(r.AudioKbps, "0") + " kb/s | " + (r.Bytes < 0 ? "-" : (r.Bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB") +
                              " | " + BenchN(r.Seconds, "0.0") + " s | " + BenchN(r.CpuSeconds, "0") + " s | " + (r.PeakMb < 0 ? "-" : r.PeakMb + " MB") + " | " + (r.Level < 0 ? "-" : ShareCheck.LevelName(r.Level)) +
                              " | " + (r.Refs < 0 ? "-" : r.Refs.ToString()) + " | " + BenchN(r.Psnr, "0.00") + " | " + BenchN(r.PsnrMin, "0.00") + " | " + BenchN(r.R, "0.0") + "/" + BenchN(r.G, "0.0") + "/" + BenchN(r.B, "0.0") +
                              " | " + BenchN(r.PsnrLeft, "0.00") + " | " + BenchN(r.Ssim, "0.00000") + " |");
                    foreach (BenchRegion g in regions) md.Append(" " + (r.Regions.ContainsKey(g.Name) ? BenchN(r.Regions[g.Name], "0.00") : "-") + " |");
                    md.Append(" " + r.Check + " | " + r.Mf + " |\n");
                }
                md.Append("\n" + repro + "\n\n## Desplazamiento r\u00E1pido (sint\u00E9tico, 6 s, 24 px por fotograma)\n\n" + stress + "\n");
                if (givenSha != null)
                {
                    Check(BenchSha(given) == givenSha, "the master changed during the benchmark");
                    md.Append("M\u00E1ster sin cambios: SHA-256 " + givenSha + " antes y despu\u00E9s.\n");
                }
                File.WriteAllText(Path.Combine(outDir, "share-bench.md"), md.ToString(), new UTF8Encoding(false));
                Add(log.ToString() + repro + " -> " + Path.Combine(outDir, "share-bench.md"));
            });
        }
    }
}

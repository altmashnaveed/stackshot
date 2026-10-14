// Stackshot - MP4/GIF screen recording with sound, through FFmpeg.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WI = System.Windows.Media.Imaging;
using WM = System.Windows.Media;

namespace Stackshot
{
    // Stackshot grabs the frames itself (Desktop Duplication, physical pixels, any DPI, with cursor) and the sound
    // (WASAPI), both on the same clock, and pipes raw frames to FFmpeg, which only encodes. Stackshot windows are excluded
    // from capture.
    public static partial class Recorder
    {
        // Quality values as stored in settings.ini (the menu shows them in another order).
        public const int Standard = 0, Maximum = 1, High = 2, Cinema = 3;

        // Screen content is sRGB: BT.709 video tagged with the sRGB transfer, so players show the original colors.
        public const string Tags = "setparams=color_primaries=bt709:color_trc=iec61966-2-1:colorspace=bt709:range=tv";

        static Session current;

        public static bool Recording { get { return current != null; } }

        // Recording, or saving what was recorded (the session ends only when its file is ready).
        public static bool Busy { get { return current != null; } }

        // Quitting, an update or Windows signing out: a recording in progress stops as if Stop was pressed and this
        // waits (on the UI thread, pumping nothing) until it is saved, then moves it to the save folder, since no card
        // is left to keep it. Past timeoutMs the final pass is cancelled and the work files go to recuperado. True when
        // nothing is left unsaved.
        public static bool Shutdown(int timeoutMs)
        {
            Session s = current;
            if (s == null) return true;
            bool ok = s.Shutdown(timeoutMs);
            if (current == s) current = null;
            return ok;
        }

        // Where work files go when a recording can't be finished; nothing ever sweeps it. Tests point it elsewhere.
        internal static string RescueRoot = Path.Combine(Settings.DataDir, "recuperado");

        // Tests drive a session without the screen.
        internal static void SetCurrentForTest(Session s) { current = s; }

        // Only the user's explicit choice or the verified download: never whatever ffmpeg.exe happens to be on PATH
        // (a user-writable PATH entry could shadow it).
        public static string FindFfmpeg(Settings s)
        {
            if (!string.IsNullOrEmpty(s.Ffmpeg) && File.Exists(s.Ffmpeg)) return s.Ffmpeg;
            string mine = Path.Combine(Settings.FfmpegDir, "ffmpeg.exe");
            return File.Exists(mine) ? mine : null;
        }

        public static void Toggle(ShotStack owner, Settings s, bool gif)
        {
            if (current != null) { current.Stop(); return; }
            string ffmpeg = FindFfmpeg(s);
            if (ffmpeg == null) ffmpeg = FfmpegSetup.Run(s);
            if (ffmpeg == null) return;
            Bitmap frozen;
            Rectangle vsr;
            IntPtr win;
            Rectangle r = RegionPicker.Pick(gif ? RegionPicker.Mode.Gif : RegionPicker.Mode.Video, out frozen, out vsr, out win);
            frozen.Dispose();
            if (r.IsEmpty) return;
            Begin(owner, s, ffmpeg, r, gif);
        }

        // Records an area already chosen (the all-in-one picker).
        public static void Start(ShotStack owner, Settings s, Rectangle r, bool gif)
        {
            if (current != null) { current.Busy(); return; } // one at a time: the bar shakes to say so
            string ffmpeg = FindFfmpeg(s);
            if (ffmpeg == null) ffmpeg = FfmpegSetup.Run(s);
            if (ffmpeg == null) return;
            Begin(owner, s, ffmpeg, r, gif);
        }

        static void Begin(ShotStack owner, Settings s, string ffmpeg, Rectangle r, bool gif)
        {
            // H.264 needs even dimensions.
            r.Width = Math.Max(16, r.Width & ~1);
            r.Height = Math.Max(16, r.Height & ~1);
            // The take's options (quality, frame rate, sound, camera, countdown) and a Grabar button, unless turned off.
            if (s.RecordSetup && !RecordSetup.Ask(s, r, gif)) return;
            if (current != null) { current.Busy(); return; }
            int cineFps = 60;
            if (!gif && s.VideoQuality == Cinema && !Cine.Preflight(owner, s, ffmpeg, r, out cineFps)) return;
            // Too little disk for this quality: a lower one for this take only, or record anyway (it stops by itself
            // before the disk fills and keeps what it has). The shared Settings stay untouched: the camera bubble saves
            // them, and the lower quality must not reach settings.ini.
            int quality;
            if (!DiskPreflight(s, r, gif, cineFps, out quality)) return;
            Session session = new Session(owner, s, ffmpeg, r, gif, cineFps, quality);
            current = session;
            try { session.Start(); }
            catch (Exception ex)
            {
                // A start that failed half way must not leave Stackshot believing it records (every later press would
                // only "stop" it).
                ShotStack.Log("Grabaci\u00F3n: " + ex);
                if (!session.Abandon()) return;
                if (current == session) current = null;
                MessageBox.Show("No se pudo empezar a grabar: " + ex.Message, "Stackshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static void Stop()
        {
            if (current != null) current.Stop();
        }

        internal static readonly string[] QualityNames = { "Est\u00E1ndar", "M\u00E1xima", "Alta", "Cine" };

        // The frame rate a take will record at, as Session picks it.
        internal static int TargetFps(Settings s, bool gif, int cineFps)
        {
            return TargetFps(s, gif ? Standard : s.VideoQuality, gif, cineFps);
        }

        internal static int TargetFps(Settings s, int quality, bool gif, int cineFps)
        {
            int q = gif ? Standard : quality;
            return q == Cinema && !gif ? cineFps : gif ? Math.Max(10, s.GifFps * 2) : q == Standard ? s.VideoFps : 60;
        }

        // quality: what this take records at (the user's, or a lighter one chosen here).
        static bool DiskPreflight(Settings s, Rectangle r, bool gif, int cineFps, out int quality)
        {
            int q = gif ? Standard : s.VideoQuality;
            quality = q;
            long free = Cine.FreeBytes(Settings.TempDir);
            if (free < 0) return true;
            int tracks = gif ? 0 : (s.RecordSystemAudio ? 1 : 0) + (s.RecordMic ? 1 : 0);
            DiskBudget.Estimate e = DiskBudget.For(q, gif, r.Width, r.Height, TargetFps(s, q, gif, cineFps), tracks, free);
            if (e.TypicalMinutes >= DiskBudget.WarnMinutes) return true;
            string name = gif ? "GIF" : QualityNames[q];
            string left = "Quedan " + DiskBudget.Gb(free) + " libres en el disco";
            if (e.TypicalMinutes < 1)
            {
                MessageBox.Show(left + ": no cabe ni un minuto en " + name + ".\n\nLibera espacio o elige una calidad m\u00E1s ligera.", "Stackshot",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string fits = left + ": en " + name + " caben " + DiskBudget.Range(e.HeavyMinutes, e.TypicalMinutes) + ", seg\u00FAn el movimiento.";
            const string anyway = "la grabaci\u00F3n se detiene sola antes de llenar el disco y conserva lo grabado";
            int alt = gif ? -1 : q == Cinema || q == Maximum ? High : q == High ? Standard : -1;
            if (alt < 0)
            {
                DialogResult ok = MessageBox.Show(fits + "\n\nAceptar: grabar igualmente (" + anyway + ").\nCancelar: no grabar.", "Stackshot",
                                                  MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                return ok == DialogResult.OK;
            }
            DiskBudget.Estimate ae = DiskBudget.For(alt, false, r.Width, r.Height, TargetFps(s, alt, false, cineFps), tracks, free);
            DialogResult d = MessageBox.Show(fits + "\n\nS\u00ED: grabar en " + QualityNames[alt] + " (caben " + DiskBudget.Range(ae.HeavyMinutes, ae.TypicalMinutes) + ").\n" +
                                             "No: grabar en " + name + " igualmente (" + anyway + ").\nCancelar: no grabar.", "Stackshot",
                                             MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (d == DialogResult.Cancel) return false;
            if (d == DialogResult.Yes) quality = alt;
            return true;
        }

        // On a 59.94 Hz panel, 59.94 (or 29.97) fps, so the video stays in step with the screen; the same for the rest
        // of the NTSC family (119.88, 143.86, 239.76 Hz), whose refreshes are whole multiples of 1/1001-second steps, so
        // frames keep the same refresh pattern for the whole take instead of slipping one refresh every few seconds.
        // Only the standard rates are used: an odd one (a monitor reporting 60.001 Hz) confuses some editors, and the
        // phase tracking absorbs the tiny difference.
        internal static void ChooseRate(int target, bool gpu, uint refreshNum, uint refreshDen, out int fpsNum, out int fpsDen)
        {
            fpsNum = target;
            fpsDen = 1;
            if (!gpu || (target != 60 && target != 30) || refreshNum == 0 || refreshDen == 0) return;
            double hz = refreshNum / (double)refreshDen;
            foreach (int rate in NtscFamily)
                if (Math.Abs(hz - rate * 1000 / 1001.0) < 0.02 * rate / 60)
                {
                    fpsNum = target * 1000;
                    fpsDen = 1001;
                    return;
                }
        }

        static readonly int[] NtscFamily = { 60, 120, 144, 180, 240, 360 };

        // Stop codes of a stopped recording, shown to the user.
        internal const string LockedReason = "Se ha bloqueado el equipo", SuspendReason = "El equipo se va a suspender", DiskReason = "Queda poco espacio en el disco";

        // Runs one final FFmpeg pass to the end under a watch (cancel, stall), reporting progress (0..1 of `seconds`);
        // stderr goes to `errors`. The exit code, or -1 when it could not start (the reason is then in `errors`).
        internal static int RunPass(string ffmpeg, string args, double seconds, PassWatch watch, Action<double> progress, StringBuilder errors)
        {
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = ffmpeg;
                    p.StartInfo.Arguments = "-hide_banner -nostdin -loglevel error -nostats -progress pipe:1 -y " + args;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.OutputDataReceived += delegate(object o, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        watch.Ping();
                        if (progress == null || !e.Data.StartsWith("out_time_us=", StringComparison.Ordinal) || seconds <= 0) return;
                        long us;
                        if (long.TryParse(e.Data.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out us)) progress(Math.Max(0, Math.Min(1, us / 1e6 / seconds)));
                    };
                    p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (errors) { if (errors.Length < 4000) errors.AppendLine(e.Data); }
                    };
                    p.Start();
                    ChildJob.Add(p);
                    watch.Attach(p, PassWatch.LimitFor(seconds));
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    int code = PassWatch.WaitExit(p, watch);
                    watch.Detach(p);
                    return code;
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.AppendLine(ex.Message);
                return -1;
            }
        }

        internal static void Ended(Session s)
        {
            if (current == s) current = null;
        }

        // Inputs the user opens: only real video containers (MP4, MKV, WMV, FLV, MPEG, TS...), never playlists, concat lists
        // or anything else that names other files or addresses.
        internal const string SafeInput = "-format_whitelist mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,gif,avi,asf,flv,mpeg,mpegts,ogg,mxf,rm,wtv,dv,nut,ivf,gxf ";

        internal static string Quote(string path) { return "\"" + path + "\""; }

        // True when moving a file from path a to path b is a rename, not a copy (same drive or share).
        internal static bool SameVolume(string a, string b)
        {
            try
            {
                string ra = Path.GetPathRoot(Path.GetFullPath(a)), rb = Path.GetPathRoot(Path.GetFullPath(b));
                return !string.IsNullOrEmpty(ra) && string.Equals(ra.TrimEnd('\\', '/'), rb.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // H.264 settings for finished files, by quality: the slower presets only where they pay off.
        public static string FinalCodec(int quality, long pixels, double fps)
        {
            string gop = " -g " + Math.Max(2, (int)Math.Round(fps * 2));
            if (quality == Cinema) return "-c:v libx264 -preset " + (pixels > 2400000 ? "medium" : "slow") + " -crf 14 -profile:v high" + gop;
            if (quality == Maximum) return "-c:v libx264 -preset " + (pixels > 2400000 ? "medium" : "slow") + " -crf 14 -profile:v high" + gop;
            if (quality == High) return "-c:v libx264 -preset medium -crf 16 -profile:v high" + gop;
            return "-c:v libx264 -preset medium -crf 20 -profile:v high" + gop;
        }

        public static string AudioCodec(int quality)
        {
            return "-c:a aac -b:a " + (quality == Cinema ? "320k" : quality == Maximum ? "256k" : quality == High ? "192k" : "160k") + " -ar 48000";
        }

        static string hardware;
        static bool probed;

        // The graphics card's H.264 encoder (NVIDIA, Intel or AMD), probed once, for areas too large to encode on the
        // CPU in real time. Null if there is none.
        internal static string HardwareName(string ffmpeg)
        {
            if (!probed)
            {
                probed = true;
                foreach (string c in new string[] { "h264_nvenc", "h264_qsv", "h264_amf" })
                {
                    string err;
                    if (Run(ffmpeg, "-hide_banner -loglevel error -f lavfi -i color=black:s=640x360:r=30 -frames:v 10 -pix_fmt nv12 -c:v " + c + " -f null -", 15000, out err) == 0)
                    {
                        hardware = c;
                        break;
                    }
                }
                ShotStack.Log("Codificador por hardware: " + (hardware ?? "ninguno"));
            }
            return hardware;
        }

        // level: 0 near lossless, 1 high, 2 standard.
        public static string HardwareEncoder(string ffmpeg, int level)
        {
            return HardwareArgs(HardwareName(ffmpeg), level);
        }

        internal static string HardwareArgs(string name, int level)
        {
            if (name == null) return null;
            int q = level == 0 ? 12 : level == 1 ? 18 : 24;
            if (name == "h264_nvenc") return "-c:v h264_nvenc -preset p5 -rc constqp -qp " + q;
            if (name == "h264_qsv") return "-c:v h264_qsv -preset medium -global_quality " + q + " -look_ahead 0";
            return "-c:v h264_amf -quality quality -rc cqp -qp_i " + q + " -qp_p " + q;
        }

        // Above 1920x1200 at 60 fps the CPU can't encode in real time (see Session.Start).
        internal static bool LargeArea(int w, int h, double fps)
        {
            return (long)w * h * fps > 1920L * 1200 * 60;
        }

        // The live FFmpeg command of the Standard, High and Maximum profiles and GIF. nv12: what the screen source
        // delivers; large: whether the area is too big to encode on the CPU in real time.
        internal static LivePlan PlanLive(int quality, bool gif, int videoFps, int gifFps, int w, int h, bool nv12, bool large, string hardwareName,
                                          bool audio, int fpsNum, int fpsDen, string output, string temp)
        {
            LivePlan plan = new LivePlan();
            plan.Lossless = gif || quality == Maximum || quality == Cinema;
            string rate = fpsNum + "/" + fpsDen;
            string gop = " -g " + Math.Max(2, (int)Math.Round(2.0 * fpsNum / fpsDen));
            string input = "-f rawvideo -pix_fmt " + (nv12 ? "nv12" : "bgra") + " -video_size " + w + "x" + h +
                           " -framerate " + rate + " -i - ";
            if (plan.Lossless && !nv12)
            {
                // Lossless RGB: exactly what was on screen; the color conversion waits for the final encode.
                plan.Enc = "-c:v libx264rgb -preset ultrafast -qp 0";
                plan.Video = temp + ".nut";
            }
            else
            {
                string vf = nv12 ? Tags : "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p," + Tags;
                int level = plan.Lossless ? 0 : quality == High ? 1 : 2;
                string hw = large ? HardwareArgs(hardwareName, level) : null;
                if (hw == null)
                    hw = "-c:v libx264 -preset " + (large ? "ultrafast" : "veryfast") + " -crf " + (level == 0 ? 8 : level == 1 ? 16 : 23);
                plan.Enc = "-vf \"" + vf + "\" " + hw + gop;
                plan.Direct = !plan.Lossless && !audio;
                plan.Video = plan.Direct ? output : temp + ".nut"; // NUT keeps exact frame times (Matroska rounds them to 1 ms)
                if (plan.Direct) plan.Enc += " -movflags +faststart";
            }
            plan.Args = "-hide_banner -loglevel error -y " + input + plan.Enc + " " + Quote(plan.Video);
            return plan;
        }

        // The final pass over the live file: sound added (voice brought to a steady level, mixed without clipping) and,
        // for the lossless qualities, the final H.264 encode with exact BT.709 colors. voiceGain: ",volume=..dB" or "".
        internal static string FinishArgs(int quality, bool lossless, bool nv12, int w, int h, int fpsNum, int fpsDen, string video,
                                          string sysPath, string micPath, string voiceGain, string output)
        {
            double fps = fpsNum / (double)fpsDen;
            string args = "-i " + Quote(video);
            int next = 1, sysIn = -1, micIn = -1;
            if (sysPath != null) { args += " -f f32le -sample_rate 48000 -ch_layout stereo -i " + Quote(sysPath); sysIn = next++; }
            if (micPath != null) { args += " -f f32le -sample_rate 48000 -ch_layout stereo -i " + Quote(micPath); micIn = next++; }
            List<string> graph = new List<string>();
            string vmap = "0:v", vcodec = "-c:v copy", amap = null;
            if (lossless)
            {
                string chain = nv12 ? Tags : "scale=out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int+full_chroma_inp,format=yuv420p," + Tags;
                graph.Add("[0:v]" + chain + "[v]");
                vmap = "[v]";
                vcodec = FinalCodec(quality, (long)w * h, fps);
            }
            string voice = micIn >= 0 ? "[" + micIn + ":a]highpass=f=70" + voiceGain : null;
            const string limiter = "alimiter=limit=0.891:attack=5:release=60:level=0:latency=1";
            if (sysIn >= 0 && micIn >= 0)
            {
                graph.Add(voice + "[m];[" + sysIn + ":a][m]amix=inputs=2:duration=first:dropout_transition=0:normalize=0," + limiter + "[a]");
                amap = "[a]";
            }
            else if (micIn >= 0)
            {
                graph.Add(voice + "," + limiter + "[a]");
                amap = "[a]";
            }
            else if (sysIn >= 0) amap = sysIn + ":a";
            if (graph.Count > 0) args += " -filter_complex \"" + string.Join(";", graph.ToArray()) + "\"";
            args += " -map \"" + vmap + "\" " + vcodec;
            if (amap != null) args += " -map \"" + amap + "\" " + AudioCodec(quality);
            args += " -movflags +faststart " + Quote(output);
            return args;
        }

        // Per-GIF palette with light dithering: accurate colors, small files.
        internal static string GifArgs(int w, int gifFps, string video, string output)
        {
            int gw = Math.Min(w, 960) & ~1;
            return "-i " + Quote(video) + " -vf \"fps=" + gifFps + ",scale=" + gw + ":-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];" +
                   "[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle\" -loop 0 " + Quote(output);
        }

        // Runs FFmpeg to the end (or the timeout); stderr is read asynchronously so a stuck process can't block.
        public static int Run(string ffmpeg, string args, int timeoutMs, out string stderr)
        {
            return Run(ffmpeg, args, timeoutMs, null, out stderr);
        }

        // watch (may be null): a cancelled save ends this pass too.
        internal static int Run(string ffmpeg, string args, int timeoutMs, PassWatch watch, out string stderr)
        {
            StringBuilder sb = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo.FileName = ffmpeg;
                p.StartInfo.Arguments = "-nostdin " + args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardError = true;
                p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (sb) { if (sb.Length < 32000) sb.AppendLine(e.Data); }
                };
                p.Start();
                ChildJob.Add(p);
                if (watch != null) watch.Attach(p, timeoutMs / 1000.0);
                p.BeginErrorReadLine();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited) { try { p.Kill(); } catch { } }
                p.WaitForExit(2000);
                if (watch != null) watch.Detach(p);
                lock (sb) stderr = sb.ToString();
                return exited ? p.ExitCode : -1;
            }
        }
    }

    internal sealed class LivePlan { public string Args, Video, Enc; public bool Direct, Lossless; }

    // Watches one FFmpeg pass at a time: kills it when the user cancels, or when it stops making progress (no CPU time
    // and no output for stallMs) or runs far past its own limit. A stuck pass never blocks the next recording.
    internal sealed class PassWatch : IDisposable
    {
        public const int DefaultStallMs = 90000;
        public const string CancelledText = "Has cancelado el guardado.";
        public const string StalledText = "La conversi\u00F3n se ha quedado parada (FFmpeg dej\u00F3 de avanzar).";

        readonly object sync = new object();
        readonly int stallMs;
        readonly System.Threading.Timer timer;
        Process proc;
        TimeSpan cpu;
        long lastChange, started, limitTicks, killedAt;
        volatile bool cancelled, stalled;

        public PassWatch(int stallMs)
        {
            this.stallMs = Math.Max(500, stallMs);
            // Ticks only while a pass is attached.
            timer = new System.Threading.Timer(delegate { Check(); }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool Cancelled { get { return cancelled; } }
        public bool Stalled { get { return stalled; } }

        // A generous ceiling for a whole pass: a slow but working encode is never cut, a runaway one eventually is.
        public static double LimitFor(double seconds)
        {
            return Math.Max(1200, seconds * 20);
        }

        // limitSeconds <= 0: no overall limit, only the stall rule.
        public void Attach(Process p, double limitSeconds)
        {
            lock (sync)
            {
                proc = p;
                stalled = false;
                killedAt = 0;
                cpu = TimeSpan.Zero;
                started = lastChange = Stopwatch.GetTimestamp();
                limitTicks = limitSeconds > 0 ? (long)(limitSeconds * Stopwatch.Frequency) : 0;
                try { timer.Change(250, 250); } catch (ObjectDisposedException) { }
            }
            if (cancelled) Kill(p);
        }

        public void Detach(Process p)
        {
            lock (sync)
            {
                if (proc != p) return;
                proc = null;
                try { timer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
            }
        }

        // The pass reported something (a progress line, a frame read): it is alive.
        public void Ping()
        {
            lock (sync) lastChange = Stopwatch.GetTimestamp();
        }

        public void Cancel()
        {
            cancelled = true;
            Process p;
            lock (sync) p = proc;
            if (p != null) Kill(p);
        }

        void Check()
        {
            Process p;
            lock (sync) p = proc;
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                TimeSpan c = p.TotalProcessorTime;
                long now = Stopwatch.GetTimestamp();
                bool idle;
                lock (sync)
                {
                    if (proc != p || stalled) return;
                    if (c != cpu) { cpu = c; lastChange = now; }
                    idle = now - lastChange > (long)stallMs * Stopwatch.Frequency / 1000;
                    bool over = limitTicks > 0 && now - started > limitTicks;
                    if (!idle && !over) return;
                    stalled = true;
                }
                ShotStack.Log("FFmpeg: se detiene una pasada que " + (idle ? "no avanzaba" : "pasaba de su tiempo m\u00E1ximo"));
                Kill(p);
            }
            catch { }
        }

        void Kill(Process p)
        {
            lock (sync) if (killedAt == 0) killedAt = Stopwatch.GetTimestamp();
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        // Waits for the pass to end; the watch guarantees it does (a process that can't even be killed is given up on
        // 30 s after the kill). The exit code, or -1.
        public static int WaitExit(Process p, PassWatch w)
        {
            while (!p.WaitForExit(500))
            {
                long k;
                lock (w.sync) k = w.killedAt;
                if (k != 0 && Stopwatch.GetTimestamp() - k > Stopwatch.Frequency * 30) return -1;
            }
            p.WaitForExit(); // the process has exited: this only lets the asynchronous readers finish
            try { return p.ExitCode; } catch { return -1; }
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }

    // Disk use of a recording: a rough forecast before it starts and, while it runs, what must stay free so the final
    // pass can still write its file.
    internal static class DiskBudget
    {
        public const long Reserve = 256L << 20;      // kept free while recording (Cine keeps Cine.ReserveBytes)
        public const double WarnMinutes = 15;         // fewer minutes than this for ordinary content: ask first
        const double AudioTrack = 48000 * 8;          // bytes per second of a work sound file (stereo float)

        public struct Estimate { public double TypicalPerSecond, HeavyPerSecond, TypicalMinutes, HeavyMinutes; }

        // Bytes per pixel and frame of the work file, with ordinary screen content and with heavy motion (a video
        // playing), and what the final pass writes on top, as a share of the work file. Rough figures from the Cine
        // bench (lossless H.264: 0.3-0.8 bits per pixel for interfaces) and usual x264 rates: shown only as ranges.
        static void Profile(int quality, bool gif, bool large, out double typical, out double heavy, out double final)
        {
            if (!gif && quality == Recorder.Cinema) { typical = 0.06; heavy = 0.5; final = 1.05; return; }
            if (gif || quality == Recorder.Maximum)
            {
                final = 0.5;
                if (large && !gif) { typical = 0.02; heavy = 0.2; }
                else { typical = 0.06; heavy = 0.5; }
                return;
            }
            final = 1.05;
            if (quality == Recorder.High) { typical = 0.006; heavy = 0.06; }
            else { typical = 0.003; heavy = 0.03; }
        }

        public static Estimate For(int quality, bool gif, int w, int h, double fps, int tracks, long free)
        {
            bool cine = !gif && quality == Recorder.Cinema;
            bool large = !cine && !gif && Recorder.LargeArea(w, h, fps);
            double typical, heavy, final;
            Profile(quality, gif, large, out typical, out heavy, out final);
            // Without sound, Estandar and Alta write the finished MP4 directly: no final pass.
            if (!cine && !gif && quality != Recorder.Maximum && tracks == 0) final = 0;
            double px = (double)w * h * Math.Max(1, fps);
            // Cine copies each original into the master, plus the mix when there are two.
            double audio = tracks * AudioTrack * (cine ? 2 : 1) + (cine && tracks == 2 ? AudioTrack : 0);
            Estimate e = new Estimate();
            e.TypicalPerSecond = px * typical * (1 + final) + audio;
            e.HeavyPerSecond = px * heavy * (1 + final) + audio;
            double room = Math.Max(0, free - (cine ? Cine.ReserveBytes : Reserve));
            e.TypicalMinutes = room / e.TypicalPerSecond / 60;
            e.HeavyMinutes = room / e.HeavyPerSecond / 60;
            return e;
        }

        // What must stay free now: the reserve plus what the final pass will write from the work files so far.
        public static long Need(bool cine, bool lossless, bool direct, long video, long audio)
        {
            return Need(cine, lossless, direct, video, audio, 0);
        }

        // mix: the track a Cine master adds when it has both computer sound and microphone (as long as the longer one),
        // written on top of both originals; without it a long recording could run out of disk in the final pass.
        public static long Need(bool cine, bool lossless, bool direct, long video, long audio, long mix)
        {
            if (cine) return Cine.ReserveBytes + video * 105 / 100 + audio + mix;
            if (direct) return Reserve;
            return Reserve + (lossless ? video / 2 : video * 105 / 100) + audio / 8;
        }

        // Minutes until the disk can't take more at the current pace (bytes per second written plus needed).
        public static double MinutesLeft(long free, long need, double bytesPerSecond)
        {
            if (bytesPerSecond <= 1) return double.PositiveInfinity;
            return Math.Max(0, free - need) / bytesPerSecond / 60;
        }

        // 0 nothing to say, 1 amber (under 10 min), 2 red (under 3 min); a level shown stays until it clearly recovers.
        public static int Level(double minutes, int shown)
        {
            if (minutes < 3) return 2;
            if (shown == 2 && minutes < 4) return 2;
            if (minutes < 10) return 1;
            if (shown > 0 && minutes < 12) return 1;
            return 0;
        }

        public static string Chip(double minutes)
        {
            return "~" + Math.Max(0, (int)Math.Floor(minutes)) + " min de disco";
        }

        public static string Range(double fromMin, double toMin)
        {
            if (toMin < 1) return "menos de 1 min";
            if (fromMin < 1) return "hasta " + Minutes(toMin);
            string a = Minutes(fromMin), b = Minutes(toMin);
            return a == b ? "unos " + a : "entre " + a + " y " + b;
        }

        static string Minutes(double m)
        {
            if (double.IsInfinity(m) || m > 99 * 60) return "muchas horas";
            return m < 120 ? (int)Math.Floor(m) + " min" : (int)Math.Floor(m / 60) + " h";
        }

        public static string Gb(long bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " GB";
        }
    }

    // Whether a recording kept up with the screen, and what to try when it did not.
    internal sealed class RecordHealth
    {
        public bool Bad;
        public long Affected;
        public double Percent;
        public readonly List<string> Advice = new List<string>();
        public string Text;

        // frames: video frames counted (the first second is left out by the caller); late: made more than a frame
        // after their moment; missed: took a screen image that was already gone; repeated: the screen could not be
        // read and the previous frame was written again; stalls: the encoder made the capture wait.
        public static RecordHealth Assess(long frames, double seconds, long late, long missed, long repeated, long stalls, double maxLateMs,
                                          int quality, bool gif, int fps, int w, int h, bool onBattery)
        {
            RecordHealth r = new RecordHealth();
            if (seconds < 3 || frames <= 0) return r;
            r.Affected = Math.Min(frames, Math.Max(0, late) + Math.Max(0, missed) + Math.Max(0, repeated) + Math.Max(0, stalls));
            r.Percent = 100.0 * r.Affected / frames;
            bool many = r.Affected >= 30 && r.Percent > 2;
            bool spike = maxLateMs > 250;
            if (!many && !spike) return r;
            r.Bad = true;
            if (onBattery) r.Advice.Add("enchufar el port\u00E1til");
            if (fps > (gif ? 20 : 30)) r.Advice.Add(gif ? "bajar los fotogramas del GIF" : "bajar a 30 fps");
            if ((long)w * h > 1920L * 1080) r.Advice.Add("grabar un \u00E1rea m\u00E1s peque\u00F1a");
            if (!gif && (quality == Recorder.Maximum || quality == Recorder.Cinema)) r.Advice.Add("usar la calidad Alta");
            r.Advice.Add("cerrar las apps que no uses");
            string what = many
                ? "Tu equipo no ha podido seguir el ritmo: " + r.Affected + " fotogramas (" + Num(r.Percent) + " %) salieron tarde o repetidos."
                : "Tu equipo se atasc\u00F3 un momento (" + Num(maxLateMs / 1000) + " s) y alg\u00FAn fotograma pudo repetirse.";
            r.Text = what + " Prueba a " + Join(r.Advice, 3) + ".";
            return r;
        }

        static string Num(double d)
        {
            return d.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        static string Join(List<string> items, int max)
        {
            int n = Math.Min(max, items.Count);
            if (n == 1) return items[0];
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(i == n - 1 ? " o " : ", ");
                sb.Append(items[i]);
            }
            return sb.ToString();
        }
    }

    // Frame buffers handed from the capture thread to the writer; allocated on demand up to a limit.
    sealed class FramePool : IDisposable
    {
        readonly int bytes, max;
        readonly Stack<IntPtr> free = new Stack<IntPtr>();
        readonly List<IntPtr> all = new List<IntPtr>();

        public FramePool(int bytes, int max)
        {
            this.bytes = bytes;
            this.max = Math.Max(3, max);
        }

        // Blocks while every buffer is in use; IntPtr.Zero if abort() says so meanwhile.
        public IntPtr Take(Func<bool> abort)
        {
            lock (free)
            {
                while (free.Count == 0 && all.Count >= max)
                {
                    if (abort()) return IntPtr.Zero;
                    Monitor.Wait(free, 50);
                }
                if (free.Count > 0) return free.Pop();
                IntPtr p = Marshal.AllocHGlobal(bytes);
                all.Add(p);
                return p;
            }
        }

        // Never waits: IntPtr.Zero when every buffer is in use, or when Windows has no memory left for another one
        // (OutOfMemory then says so).
        public IntPtr TryTake()
        {
            lock (free)
            {
                if (free.Count > 0) return free.Pop();
                if (all.Count >= max) return IntPtr.Zero;
                IntPtr p;
                try { p = Marshal.AllocHGlobal(bytes); }
                catch (OutOfMemoryException) { OutOfMemory = true; return IntPtr.Zero; }
                all.Add(p);
                return p;
            }
        }

        public volatile bool OutOfMemory;

        public int Capacity { get { return max; } }

        public int InUse { get { lock (free) return all.Count - free.Count; } }

        public void Give(IntPtr p)
        {
            lock (free) { free.Push(p); Monitor.Pulse(free); }
        }

        public void Dispose()
        {
            lock (free)
            {
                foreach (IntPtr p in all) Marshal.FreeHGlobal(p);
                all.Clear();
                free.Clear();
            }
        }
    }

    partial class Session
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll")] static extern bool CancelSynchronousIo(IntPtr thread);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        // Ends a thread's blocking pipe writes (the encoder stopped reading) until it finishes, up to ms. The thread must
        // not start new writes once asked (feedCut). True if it ended.
        internal static bool CancelPendingWrites(Thread t, uint threadId, int ms)
        {
            if (t == null || !t.IsAlive) return true;
            IntPtr h = threadId != 0 ? OpenThread(0x0001, false, threadId) : IntPtr.Zero; // THREAD_TERMINATE
            try
            {
                Stopwatch w = Stopwatch.StartNew();
                do
                {
                    // Nothing pending is fine: the next write sees feedCut and is never made.
                    if (h != IntPtr.Zero) CancelSynchronousIo(h);
                    if (t.Join(20)) return true;
                }
                while (w.ElapsedMilliseconds < ms);
                return !t.IsAlive;
            }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }

        readonly ShotStack owner;
        readonly string ffmpeg;
        readonly Rectangle area;
        readonly Settings settings;
        readonly bool gif, cine;
        readonly int quality, gifFps, cameraShape, cineFps;
        readonly Stopwatch clock = new Stopwatch();
        readonly StringBuilder errors = new StringBuilder();
        readonly List<string> notes = new List<string>();
        int fpsNum = 30, fpsDen = 1;
        string output, video, sysPath, micPath, enc;
        bool lossless, direct;
        ScreenSource source;
        AudioCapture sys, mic;
        Process proc;
        Stream pipeStream;
        SafeFileHandle pipe;
        Thread capture, writer;
        FramePool pool;
        BlockingCollection<IntPtr> queue;
        long t0, t1, frames;
        double maxLate;                   // how far behind the screen frames were made (ms), for the log
        volatile bool stopping, cancelled, writeFailed, halted;
        // Cinema: the live encoder with its frame hashes, what went wrong on the way, and the report.
        CineSink sink;
        CineReport report;
        string stopReason, stamp, masterTemp;
        DateTime startedAt;
        long queued, repeatedFailed, black, queuePeak, discarded, nextDiskCheck, lastDeadline;
        Bitmap poster;
        string warning;
        bool fineTimer;                   // the 1 ms timer resolution is raised for this recording
        string error;
        RecordBar bar;
        WebcamBubble webcam;
        FrameEdge[] edges;
        // Saving: the watch over the final passes, the user's (or a quit's) cancel, and where rescued files went.
        readonly PassWatch watch = new PassWatch(PassWatch.DefaultStallMs);
        readonly ManualResetEvent finished = new ManualResetEvent(false);
        volatile bool saveCancelled, quitting;
        volatile bool feedCut;            // a cancelled save of a direct MP4: no more frames go to the encoder
        volatile uint writerTid;
        int quitKept;
        bool quitSaved, released, eventsOn;
        string rescueDir;
        IDisposable awake;
        // How well the recording kept up (all qualities), counted after the first second.
        long warmFrames, lateFrames, repeats, stalls;
        double maxLateWarm;
        // Disk watch: pace of what the recording writes and will need, and what the bar says about it.
        long lastTotal, lastTotalAt;
        double diskRate;
        int diskLevel, diskMinutes = -1;

        public Session(ShotStack owner, Settings s, string ffmpeg, Rectangle area, bool gif, int cineFps)
            : this(owner, s, ffmpeg, area, gif, cineFps, s.VideoQuality)
        {
        }

        // quality: this take's, which may be lighter than the saved one (the disk check); s is the shared Settings.
        public Session(ShotStack owner, Settings s, string ffmpeg, Rectangle area, bool gif, int cineFps, int quality)
        {
            this.owner = owner;
            this.ffmpeg = ffmpeg;
            this.area = area;
            this.gif = gif;
            this.quality = gif ? Recorder.Standard : quality;
            cine = !gif && quality == Recorder.Cinema;
            this.cineFps = cineFps;
            gifFps = s.GifFps;
            cameraShape = gif ? 0 : s.Webcam;
            settings = s;
            countdownSeconds = Settings.Countdown(s.RecordCountdown);
        }

        public TimeSpan Elapsed { get { return clock.Elapsed; } }
        public bool Gif { get { return gif; } }

        // owner is null in tests.
        void Ui(Action a)
        {
            if (owner != null) owner.Ui(a);
        }
        public AudioCapture SystemAudio { get { return sys; } }
        public AudioCapture Microphone { get { return mic; } }

        public void Start()
        {
            Directory.CreateDirectory(Settings.TempDir);
            startedAt = DateTime.Now;
            stamp = startedAt.ToString("yyyy-MM-dd HH.mm.ss");
            output = ShotStack.Unique(Path.Combine(Settings.TempDir, cine ? "Cine " + stamp + Cine.Extension : (gif ? "GIF " : "V\u00EDdeo ") + stamp + (gif ? ".gif" : ".mp4")));
            // Work files start with "~" and never show up as captures.
            string temp = Path.Combine(Settings.TempDir, "~grabando " + stamp);
            int target = cine ? cineFps : gif ? Math.Max(10, gifFps * 2) : quality == Recorder.Standard ? settings.VideoFps : 60;
            lossless = gif || quality == Recorder.Maximum || quality == Recorder.Cinema;
            // Up to 1920x1200 at 60 fps the CPU encodes losslessly in real time. Larger areas are converted to 4:2:0 on
            // the GPU (2.7x less data) and encoded by the graphics card when it can. Cinema never converts: it stays RGB.
            bool large = !cine && !gif && Recorder.LargeArea(area.Width, area.Height, target);
            // Cinema keeps more screen images on the GPU (up to 16, 256 MB at most), so a frame made late still finds its
            // own image (6 last only ~40 ms on a 144 Hz monitor).
            source = cine ? ScreenSource.Open(area, false, cameraShape > 0, (int)Math.Min(16, (256L << 20) / ((long)area.Width * area.Height * 4)))
                          : ScreenSource.Open(area, large, cameraShape > 0);
            // E_ACCESSDENIED: a secure desktop (lock screen, UAC) is showing; the GDI fallback would only record black.
            if (cine && !source.Gpu && source.Description.Contains("0x80070005"))
                throw new InvalidOperationException("Windows no deja capturar la pantalla ahora mismo (pantalla bloqueada o un aviso de seguridad). Vuelve a intentarlo en el escritorio.");
            if ((cine && !CineSourceAccepted()) || quitting)
            {
                source.Dispose();
                source = null;
                Recorder.Ended(this);
                return;
            }
            Recorder.ChooseRate(target, source.Gpu, source.RefreshNum, source.RefreshDen, out fpsNum, out fpsDen);
            // The camera opens while the rest is prepared; nothing is recorded until it shows live video.
            if (cameraShape > 0)
            {
                webcam = new WebcamBubble(settings, ffmpeg, area);
                webcam.Show();
            }

            if (!gif && settings.RecordSystemAudio) sys = StartAudio(true, settings.SystemAudioDevice, sysPath = temp + " equipo.f32");
            if (!gif && settings.RecordMic) mic = StartAudio(false, settings.MicDevice, micPath = temp + " micro.f32");
            bool audio = sys != null || mic != null;

            LivePlan plan = null;
            if (cine)
            {
                video = temp + ".nut";
                enc = "libx264rgb -qp 0 (bgr0 -> gbrp, sin p\u00E9rdidas)";
            }
            else
            {
                // The probe only runs when the plan can use a hardware encoder.
                string hwName = large && !(lossless && !source.Nv12) ? Recorder.HardwareName(ffmpeg) : null;
                plan = Recorder.PlanLive(quality, gif, settings.VideoFps, gifFps, area.Width, area.Height, source.Nv12, large, hwName, audio,
                                         fpsNum, fpsDen, output, temp);
                enc = plan.Enc;
                video = plan.Video;
                direct = plan.Direct;
            }
            try
            {
                if (cine)
                {
                    sink = new CineSink(ffmpeg, area.Width, area.Height, fpsNum, fpsDen, video);
                    sink.Start();
                }
                else proc = StartFfmpeg(plan.Args, true);
            }
            catch (Exception ex)
            {
                ShotStack.Log("FFmpeg: " + ex.Message);
                MessageBox.Show("No se pudo arrancar FFmpeg: " + ex.Message, "Stackshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (webcam != null) { webcam.Close(); webcam = null; }
                long none = 0;
                if (sys != null) sys.Finish(none);
                if (mic != null) mic.Finish(none);
                TryDelete(sysPath);
                TryDelete(micPath);
                if (sink != null) { sink.Dispose(); sink = null; }
                source.Dispose();
                Recorder.Ended(this);
                return;
            }
            if (cine) pool = new FramePool(source.FrameBytes, Cine.PoolFrames(source.FrameBytes, Cine.PoolBudget(settings.CinePeakMemory, Cine.AvailablePhysical())));
            else
            {
                pipeStream = proc.StandardInput.BaseStream;
                FileStream fs = pipeStream as FileStream;
                pipe = fs != null ? fs.SafeFileHandle : null;
                pool = new FramePool(source.FrameBytes, (int)Math.Max(4, Math.Min(32, (256L << 20) / source.FrameBytes)));
            }
            queue = new BlockingCollection<IntPtr>();

            edges = FrameEdge.Around(area);
            bar = new RecordBar(this, area);
            bar.Prepare(countdownSeconds > 0);
            preparing = true;
            // The countdown runs while the devices finish opening; recording starts when both are done.
            if (countdownSeconds > 0)
            {
                countdown = new CountdownWindow(area, countdownSeconds);
                countdown.Cancelled += delegate { if (preparing) CancelPrep(); };
                countdown.Show();
            }
            prepClock = Stopwatch.StartNew();
            escSeenUp = false;
            prep = new System.Windows.Forms.Timer();
            prep.Interval = 30;
            prep.Tick += delegate { PrepTick(); };
            prep.Start();
        }

        // Cine promises the screen as Windows gives it: say so before recording through the GDI fallback (blind to
        // hardware-accelerated content) or across monitors (frames follow the main one's refresh).
        bool CineSourceAccepted()
        {
            string text = null;
            if (!source.Gpu)
            {
                string why = source.Description.StartsWith("GDI (", StringComparison.Ordinal) ? source.Description.Substring(5).TrimEnd(')') : "sin duplicaci\u00F3n de pantalla";
                text = "Esta zona no se puede grabar con la captura de Windows (" + why + "): se usar\u00EDa la de reserva (GDI), que no ve el contenido " +
                       "acelerado por hardware (v\u00EDdeos, juegos, algunas apps) y no es fiel a la pantalla.\n\n\u00BFGrabar igualmente en Cine?";
            }
            else if (source.OutputCount > 1)
            {
                text = "El \u00E1rea ocupa " + source.OutputCount + " pantallas. Los fotogramas siguen el ritmo de la principal (" +
                       (source.RefreshNum / (double)source.RefreshDen).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + " Hz)" +
                       (source.MixedRefresh ? " y la otra va a otra frecuencia, as\u00ED que su parte puede verse a saltos." : "; si una se mueve a otro ritmo, su parte puede verse a saltos.") +
                       "\n\n\u00BFGrabar igualmente en Cine?";
            }
            if (text == null) return true;
            ShotStack.Log("Cine: aviso antes de grabar: " + source.Description);
            asking = true;
            try { return MessageBox.Show(text, "Stackshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
            finally { asking = false; }
        }

        // The question above is open: Recorder.current is already this session, but nothing runs yet. The record
        // shortcut and a quit must not stop or dispose what Start is about to use.
        bool asking;

        internal void SetAskingForTest(bool on) { asking = on; }
        internal bool StoppingForTest { get { return stopping; } }
        internal int QualityForTest { get { return quality; } }
        internal Settings SettingsForTest { get { return settings; } }

        const int CameraWaitMs = 6000;
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        static bool OurForeground()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }
        bool preparing, escSeenUp;
        int countdownSeconds;
        CountdownWindow countdown;
        Stopwatch prepClock;
        System.Windows.Forms.Timer prep;
        string cameraNote;

        // Waits for every device (today the camera; audio, FFmpeg and the screen are open by now), then starts.
        void PrepTick()
        {
            if (!preparing) return;
            try
            {
                bool esc = (GetAsyncKeyState(0x1B) & 0x8000) != 0 && OurForeground();
                if (!esc) escSeenUp = true;
                else if (escSeenUp) { CancelPrep(); return; }
                if (webcam != null)
                {
                    bool late = prepClock.ElapsedMilliseconds > CameraWaitMs;
                    if (webcam.GaveUp || late)
                    {
                        cameraNote = webcam.GaveUp ? "No se ha podido abrir la c\u00E1mara." : "La c\u00E1mara no ha respondido a tiempo.";
                        ShotStack.Log("C\u00E1mara: " + cameraNote + " Se graba sin ella.");
                        webcam.Dismiss();
                        webcam = null;
                    }
                    else if (!webcam.Live) return;
                }
                if (countdown != null && !countdown.Done) return;
                CloseCountdown();
                preparing = false;
                prep.Stop();
                Go();
            }
            catch (Exception ex)
            {
                preparing = false;
                prep.Stop();
                ShotStack.Log("Grabaci\u00F3n: " + ex);
                if (Abandon()) Recorder.Ended(this);
                MessageBox.Show("No se pudo empezar a grabar: " + ex.Message, "Stackshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void CancelPrep()
        {
            CloseCountdown();
            preparing = false;
            prep.Stop();
            ShotStack.Log("Grabaci\u00F3n cancelada antes de empezar");
            if (Abandon()) Recorder.Ended(this);
        }

        void CloseCountdown()
        {
            CountdownWindow c = countdown;
            countdown = null;
            if (c != null && !c.IsDisposed) c.Close();
        }

        // Everything is ready: from here sound and image share one clock.
        void Go()
        {
            string rate = fpsNum + "/" + fpsDen;
            bar.Ready();
            Native.timeBeginPeriod(1);
            fineTimer = true;
            // Recording and saving keep the PC awake and out of efficiency mode; locking or suspending stops and saves.
            Power.OptOutOfThrottling();
            awake = Power.KeepAwake();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerMode;
            eventsOn = true;
            t0 = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 20;
            if (sys != null) sys.Begin(t0);
            if (mic != null) mic.Begin(t0);
            StartInput();
            clock.Start();
            writer = new Thread(cine ? new ThreadStart(WriteLoopCine) : new ThreadStart(WriteLoop));
            writer.IsBackground = true;
            writer.Priority = ThreadPriority.AboveNormal;
            writer.Start();
            capture = new Thread(CaptureLoop);
            capture.IsBackground = true;
            capture.Priority = ThreadPriority.Highest;
            capture.Start();
            ShotStack.Log("Grabando " + (gif ? "GIF" : "v\u00EDdeo") + " " + area.Width + "x" + area.Height + " a " + rate + " fps \u00B7 " + source.Description +
                          " \u00B7 " + enc + (sys != null ? " \u00B7 sonido: " + sys.DeviceName : "") + (mic != null ? " \u00B7 micr\u00F3fono: " + mic.DeviceName : ""));
            if (notes.Count > 0) owner.Notify("Grabando sin parte del sonido", string.Join("\n", notes.ToArray()));
            if (cameraNote != null) owner.Notify("Grabando sin c\u00E1mara", cameraNote);
        }

        AudioCapture StartAudio(bool computer, string device, string path)
        {
            AudioCapture a = new AudioCapture(computer, device, path, cine);
            a.Pauses = pauses;
            if (a.Start()) return a;
            notes.Add(a.Error);
            ShotStack.Log("Sonido: " + a.Error);
            TryDelete(path);
            return null;
        }

        Process StartFfmpeg(string args, bool input)
        {
            Process p = new Process();
            p.StartInfo.FileName = ffmpeg;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardInput = input;
            p.StartInfo.RedirectStandardError = true;
            p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (errors) { if (errors.Length < 4000) errors.AppendLine(e.Data); }
            };
            p.Start();
            // The live encoder: out of Windows' efficiency mode, but not in the job (see ChildJob).
            Power.OptOutOfThrottling(p);
            p.BeginErrorReadLine();
            return p;
        }

        public void Stop()
        {
            if (asking) return;
            if (preparing) { CancelPrep(); return; }
            if (stopping)
            {
                // Asked again while saving (the record shortcut, say): the bar shakes to say it is still busy.
                Busy();
                return;
            }
            t1 = Stopwatch.GetTimestamp();
            stopping = true;
            clock.Stop();
            StopInput();
            // Discarding has nothing to wait for: the bar goes at once.
            if (bar != null) { if (cancelled) bar.Finish(false); else bar.Saving(); }
            if (webcam != null) { webcam.Dismiss(); webcam = null; }
            // The frame only marks what is being recorded: it goes now, while the bar keeps showing the saving.
            if (edges != null) { foreach (FrameEdge e in edges) e.Dismiss(); edges = null; }
        }

        public void Cancel()
        {
            cancelled = true;
            Stop();
        }

        // Win+L, a remote session closed or the PC going to sleep: what was recorded is saved, and nothing is recorded
        // from a locked or sleeping machine (the microphone included).
        void OnSessionSwitch(object o, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
                StopFor(Recorder.LockedReason);
        }

        void OnPowerMode(object o, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) StopFor(Recorder.SuspendReason);
        }

        void StopFor(string reason)
        {
            if (stopping || halted || writer == null) return;
            Halt(reason);
        }

        // The final pass is cancelled (the bar's Cancelar, or a quit that can't wait): what was recorded goes to
        // recuperado instead of being deleted. A direct MP4 is left to finish: killed, it would not play.
        public void CancelSave()
        {
            if (saveCancelled) return;
            saveCancelled = true;
            ShotStack.Log("Grabaci\u00F3n: guardado cancelado");
            watch.Cancel();
            if (bar != null && !bar.IsDisposed) bar.Cancelling();
        }

        // Part of a Shutdown budget kept for putting the work files away after cancelling the final pass.
        internal static int ShutdownGrace(int timeoutMs)
        {
            return Math.Min(15000, Math.Max(0, timeoutMs) / 3);
        }

        // Recorder.Shutdown, on the UI thread. Never blocks longer than timeoutMs.
        internal bool Shutdown(int timeoutMs)
        {
            quitting = true;
            // Start sees `quitting` when the question closes and gives up; nothing was opened for good yet.
            if (asking) return true;
            if (preparing) { CancelPrep(); return true; }
            if (writer == null)
            {
                if (Abandon()) Recorder.Ended(this);
                return true;
            }
            ShotStack.Log("Grabaci\u00F3n: Stackshot se cierra; se guarda antes lo grabado");
            if (!stopping) Stop();
            int grace = ShutdownGrace(timeoutMs);
            bool done = finished.WaitOne(Math.Max(0, timeoutMs) - grace);
            if (!done)
            {
                CancelSave();
                done = finished.WaitOne(grace);
            }
            if (done) KeepOnQuit();
            Release();
            if (bar != null && !bar.IsDisposed) bar.Close();
            if (webcam != null && !webcam.IsDisposed) webcam.Close();
            if (edges != null) foreach (FrameEdge e in edges) if (!e.IsDisposed) e.Close();
            Recorder.Ended(this);
            ShotStack.Log("Grabaci\u00F3n: " + (done && quitSaved ? "guardada al cerrar" : "no se pudo terminar al cerrar; lo grabado est\u00E1 en " + (rescueDir ?? Recorder.RescueRoot)));
            return done && quitSaved;
        }

        // Quitting: the finished file goes to the save folder (no card is left to keep it, and the temporary folder is
        // swept); if that fails, to recuperado. On the work thread when it finishes, or from Shutdown.
        void KeepOnQuit()
        {
            if (Interlocked.Exchange(ref quitKept, 1) == 1) return;
            if (cancelled || frames == 0) { quitSaved = true; return; }
            if (error != null || output == null || !File.Exists(output)) { quitSaved = false; return; }
            if (!Recorder.SameVolume(output, settings.SaveFolder))
            {
                // Another drive would mean copying gigabytes while Windows or the user waits for the quit: the finished
                // file stays in the temporary folder, where the quit (ShotStack) picks up whatever a recording left.
                quitSaved = true;
                ShotStack.Log("Grabaci\u00F3n terminada al cerrar; la carpeta de guardado est\u00E1 en otra unidad y se deja en " + output);
                return;
            }
            string keepFolder = SaveFolders.Target(settings, startedAt == DateTime.MinValue ? DateTime.Now : startedAt);
            try
            {
                Directory.CreateDirectory(keepFolder);
                string dest = ShotStack.Unique(Path.Combine(keepFolder, Path.GetFileName(output)));
                File.Move(output, dest);
                output = dest;
                quitSaved = true;
                ShotStack.Log("Grabaci\u00F3n guardada al cerrar: " + dest);
            }
            catch (Exception ex)
            {
                ShotStack.Log("Grabaci\u00F3n: no se pudo llevar a " + keepFolder + ": " + ex.Message);
                string partial = output;
                List<string> kept = new List<string>();
                string dir = Path.Combine(Recorder.RescueRoot, stamp);
                Keep(ref partial, dir, kept);
                rescueDir = dir;
                quitSaved = kept.Count > 0 && !kept[0].Equals(output, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---- Tests: a recording that has just stopped, saved without screen, devices or bar.

        internal void PrepareForTest(string video, string sysPath, string micPath, string output, long frames, int fpsNum, int fpsDen, bool direct, string stamp,
                                     string liveError)
        {
            error = liveError;
            this.video = video;
            this.sysPath = sysPath;
            this.micPath = micPath;
            this.output = output;
            this.frames = frames;
            this.fpsNum = fpsNum;
            this.fpsDen = fpsDen;
            this.direct = direct;
            this.stamp = stamp;
            lossless = gif || quality == Recorder.Maximum;
        }

        // The saving part of Finish on its own thread, as after a real stop.
        internal void BeginSavingForTest(double seconds)
        {
            stopping = true;
            writer = new Thread(delegate()
            {
                try { SaveFinal(seconds); }
                catch (Exception ex) { error = ex.Message; }
                if (quitting) KeepOnQuit();
                watch.Dispose();
                finished.Set();
            });
            writer.IsBackground = true;
            writer.Start();
        }

        internal bool WaitSavedForTest(int ms) { return finished.WaitOne(ms); }
        internal string ErrorText { get { return error; } }
        internal string OutputPath { get { return output; } }
        internal string RescueDir { get { return rescueDir; } }

        // Power, system events and the timer resolution, given back once (Done, Abandon or Shutdown).
        void Release()
        {
            if (released) return;
            released = true;
            EndInput();
            EndPeriod();
            if (eventsOn)
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerMode;
                eventsOn = false;
            }
            if (awake != null) { awake.Dispose(); awake = null; }
        }

        // Another recording was asked for while this one runs or is being saved.
        public void Busy()
        {
            if (bar != null && !cancelled) bar.Nudge();
        }

        // Start threw before the recording was running: let go of whatever it had opened. False once the threads run
        // (the recording then goes on and ends as usual).
        internal bool Abandon()
        {
            if (writer != null) return false;
            if (prep != null) { prep.Stop(); prep.Dispose(); prep = null; }
            stopping = cancelled = true;
            CloseCountdown();
            if (proc != null)
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                try { proc.Dispose(); } catch { }
            }
            if (sink != null) { try { sink.Dispose(); } catch { } }
            try { if (sys != null) sys.Finish(0); } catch { }
            try { if (mic != null) mic.Finish(0); } catch { }
            try { if (source != null) source.Dispose(); } catch { }
            if (pool != null) pool.Dispose();
            if (bar != null && !bar.IsDisposed) bar.Close();
            if (webcam != null && !webcam.IsDisposed) webcam.Close();
            if (edges != null) foreach (FrameEdge e in edges) if (!e.IsDisposed) e.Close();
            if (video != null && video != output) TryDelete(video);
            TryDelete(output);
            TryDelete(sysPath);
            TryDelete(micPath);
            Release();
            watch.Dispose();
            finished.Set();
            return true;
        }

        void EndPeriod()
        {
            if (!fineTimer) return;
            fineTimer = false;
            Native.timeEndPeriod(1);
        }

        // Video frame n shows the latest image presented before Cut(n). The compositor stamps each image when it
        // presents it, which scatters several milliseconds (unevenly) after the monitor's refresh. With the monitor's own
        // rate, the cut goes in the quietest part of the refresh interval, learned from the recent present times: each
        // frame then gets exactly one refresh and motion is perfectly even. Without a known refresh, halfway through the
        // frame's interval. Frame times run on recording time (pauses cut out): a frame whose moment lies past the start of
        // a pause going on is due at +infinity, so none is made until it is resumed.
        double Cut(long n, double period, double cutPhase, bool locked)
        {
            return pv.QpcOf(locked ? t0 + cutPhase + n * period : t0 + (n + 0.5) * period);
        }

        static double Wrap(double v, double m) { v %= m; return v < 0 ? v + m : v; }

        // The new cut phase moved by the shortest way from the old one: wrapped into [0, display), a small move across the
        // wrap point would shift the next cut back a whole refresh (one image shown twice, or one skipped). Cut() is
        // additive, so any phase works.
        internal static double NearPhase(double phase, double old, double display)
        {
            if (display <= 0) return phase;
            double d = Wrap(phase - old, display);
            return d > display / 2 ? old + d - display : old + d;
        }

        // Middle of the quietest quarter of the refresh interval (circular histogram of present times).
        internal static double Quietest(double[] hist, double display, double current)
        {
            current = Wrap(current, display);
            int bins = hist.Length, w = bins / 4, best = 0;
            double bestSum = double.MaxValue;
            for (int s = 0; s < bins; s++)
            {
                double sum = 0;
                for (int j = 0; j < w; j++) sum += hist[(s + j) % bins];
                if (sum < bestSum) { bestSum = sum; best = s; }
            }
            // Only move for a clearly quieter place, so the cut doesn't wander between two similar ones.
            int cur = (int)(current / display * bins) - w / 2;
            double curSum = 0;
            for (int j = 0; j < w; j++) curSum += hist[((cur + j) % bins + bins) % bins];
            if (bestSum > curSum * 0.7) return current;
            return Wrap((best + w / 2.0) / bins * display, display);
        }

        // Takes every new screen image as it comes and does nothing else, so none is ever merged with the next. It runs
        // until the last frame is made (`acquired`), which can be a little after the user stops.
        volatile bool acquired;

        void AcquireLoop()
        {
            Power.OptOutThisThread();
            IntPtr mm = Power.JoinMmcss("Capture");
            try
            {
                while (!acquired && !source.Broken)
                {
                    long present;
                    if (source.Wait(16, out present)) source.Take();
                }
            }
            catch (Exception ex) { ShotStack.Log("Grabaci\u00F3n (im\u00E1genes): " + ex.Message); }
            finally { Power.LeaveMmcss(mm); }
        }

        void CaptureLoop()
        {
            Power.OptOutThisThread();
            IntPtr mm = Power.JoinMmcss("Capture");
            try { CaptureFrames(); }
            finally { Power.LeaveMmcss(mm); }
            // Saving runs on this same thread, at its ordinary priority.
            Finish();
        }

        void CaptureFrames()
        {
            long freq = Stopwatch.Frequency;
            double period = freq * (double)fpsDen / fpsNum;
            double display = source.Gpu ? freq * (double)source.RefreshDen / source.RefreshNum : period;
            bool locked = source.Gpu && display <= period * 1.02;
            pauses.Quantum = locked ? display : 0;
            // A frame is made a refresh after its cut: the compositor sometimes hands an image over late, and it must
            // still land in its own frame.
            long margin = freq * 3 / 1000 + (locked ? (long)display : 0);
            double[] hist = new double[32];
            int seen = 0;
            // Until there is a history, assume presents gather just after the refresh of the first image.
            double cutPhase = source.LastPresent != 0 ? Wrap(source.LastPresent - t0 + display * 0.6, display) : display / 2;
            Thread acquire = null;
            if (source.Gpu)
            {
                acquire = new Thread(AcquireLoop);
                acquire.IsBackground = true;
                acquire.Priority = ThreadPriority.Highest;
                acquire.Start();
            }
            long n = 0;
            // The first second (devices settling) is left out of the "kept up" count. A frame made over 100 ms (or a
            // frame) past its moment counts as late: by then the few screen images kept may already be gone.
            warmFrames = (long)Math.Ceiling(fpsNum / (double)fpsDen);
            double lateMs = margin * 1000.0 / freq + Math.Max(100, period * 1000.0 / freq);
            try
            {
                while (!stopping && !halted)
                {
                    SampleCursor();
                    CheckHealth();
                    if (locked)
                        foreach (long present in source.NewPresents())
                        {
                            double ph = Wrap(present - t0, display);
                            for (int i = 0; i < hist.Length; i++) hist[i] *= 0.995; // about the last three seconds
                            hist[Math.Min(hist.Length - 1, (int)(ph / display * hist.Length))] += 1;
                            seen++;
                            if (seen == 1) cutPhase = NearPhase(Wrap(ph + display * 0.6, display), cutPhase, display);
                            else if (seen >= 20 && seen % 5 == 0) cutPhase = NearPhase(Quietest(hist, display, cutPhase), cutPhase, display);
                        }
                    long now = pauses.Now(out pv);
                    while (Cut(n, period, cutPhase, locked) + margin <= now && !stopping && !halted)
                    {
                        double late = (now - Cut(n, period, cutPhase, locked)) * 1000.0 / freq;
                        if (late > maxLate) maxLate = late;
                        if (n >= warmFrames)
                        {
                            if (late > maxLateWarm) maxLateWarm = late;
                            if (late > lateMs) lateFrames++;
                        }
                        // A second behind: the screen images of these frames are long gone, it would only add wrong ones.
                        if (cine && late > 1000) { Halt("El equipo no ha podido seguir el ritmo de la pantalla"); break; }
                        if (!Emit(Cut(n, period, cutPhase, locked), locked ? display / 2 : 0)) break;
                        n++;
                    }
                    // Short sleeps keep the cursor history dense (a few ms apart).
                    int ms = (int)Math.Max(1, Math.Min(4, (Cut(n, period, cutPhase, locked) + margin - Stopwatch.GetTimestamp()) * 1000 / freq));
                    Thread.Sleep(ms);
                }
                // Up to the moment the user stopped (on recording time: stopped while paused, up to the pause).
                pv = pauses.Current;
                while (t0 + n * period < pv.RtOf(t1) && !cancelled && !writeFailed && !halted)
                {
                    if (!Emit(pv.Clamp(Cut(n, period, cutPhase, locked)), locked ? display / 2 : 0)) break;
                    n++;
                }
                acquired = true;
                if (acquire != null) acquire.Join(2000);
                // A stuck encoder must not keep the last frames waiting forever: 10 s, then they count as discarded.
                lastDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
                if (!halted) Drain(source.Pending, true);
                if (cine && !cancelled) discarded = source.Pending;
            }
            catch (Exception ex)
            {
                if (!cancelled && error == null) error = "La captura de pantalla ha fallado: " + ex.Message;
                ShotStack.Log("Grabaci\u00F3n: " + ex);
            }
            acquired = true;
            if (acquire != null) acquire.Join(2000);
            frames = n;
            queue.CompleteAdding();
        }

        // One video frame: the image on screen at its cut, with the cursor of that moment (`back` before the cut, when
        // the image itself appeared).
        bool Emit(double cut, double back)
        {
            NoteCut(cut);
            source.Submit(CursorAt(cut - back), source.Pick(cut));
            // Read one frame behind, so the GPU has finished it and nothing waits.
            return source.Pending < 2 || Drain(1);
        }

        // The cursor of the last moments, so each frame gets the cursor of its own instant (frames are written a little
        // after they were on screen).
        readonly long[] cursorTime = new long[128];
        readonly Native.CURSORINFO[] cursorShot = new Native.CURSORINFO[128];
        int cursorCount;

        void SampleCursor()
        {
            int i = cursorCount % cursorShot.Length;
            cursorShot[i] = Grabber.CursorNow();
            cursorTime[i] = Stopwatch.GetTimestamp();
            cursorCount++;
        }

        // Between two samples the position is interpolated, so movement stays smooth.
        Native.CURSORINFO CursorAt(double when)
        {
            if (!source.Gpu || cursorCount == 0) return Grabber.CursorNow();
            int size = cursorShot.Length, available = Math.Min(cursorCount, size);
            int after = (cursorCount - 1) % size;
            for (int k = 1; k < available; k++)
            {
                int before = (cursorCount - 1 - k) % size;
                if (cursorTime[before] > when) { after = before; continue; }
                Native.CURSORINFO a = cursorShot[before], b = cursorShot[after];
                double span = cursorTime[after] - cursorTime[before];
                if (span <= 0 || a.hCursor != b.hCursor || (a.flags & 1) != (b.flags & 1)) return a;
                double f = Math.Max(0, Math.Min(1, (when - cursorTime[before]) / span));
                a.ptScreenPos.X = (int)Math.Round(a.ptScreenPos.X + (b.ptScreenPos.X - a.ptScreenPos.X) * f);
                a.ptScreenPos.Y = (int)Math.Round(a.ptScreenPos.Y + (b.ptScreenPos.Y - a.ptScreenPos.Y) * f);
                return a;
            }
            return cursorShot[after];
        }

        bool Drain(int count)
        {
            return Drain(count, false);
        }

        // Cinema never waits for a buffer while recording: with the queue full the encoder can't keep up, and waiting
        // would let the screen images be overwritten, so it stops instead. The last frames at the end do wait.
        bool Drain(int count, bool last)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr buf;
                if (cine && !last)
                {
                    buf = pool.TryTake();
                    if (buf == IntPtr.Zero)
                    {
                        Halt(pool.OutOfMemory ? "No queda memoria libre para seguir grabando sin p\u00E9rdidas"
                                              : "El equipo no ha podido codificar sin p\u00E9rdidas al ritmo de la pantalla");
                        return false;
                    }
                    int used = pool.InUse;
                    if (used > queuePeak) queuePeak = used;
                }
                else
                {
                    // The encoder making the capture wait longer than a frame: a stall the user will see.
                    long waitFrom = Stopwatch.GetTimestamp();
                    buf = pool.Take(delegate { return cancelled || writeFailed || saveCancelled || (cine && last && Stopwatch.GetTimestamp() > lastDeadline); });
                    if (!last && queued >= warmFrames && (Stopwatch.GetTimestamp() - waitFrom) * (long)fpsNum > Stopwatch.Frequency * (long)fpsDen) stalls++;
                }
                if (buf == IntPtr.Zero) return false;
                queued++;
                if (Overlaid(buf, source.Read(buf))) queue.Add(buf);
                else
                {
                    pool.Give(buf);
                    queue.Add(IntPtr.Zero); // repeat the previous frame
                    if (!cine && queued > warmFrames) repeats++;
                }
            }
            return true;
        }

        // The recording ends here and keeps what it has (saved as if Stop was pressed; the reason is told at the end).
        void Halt(string reason)
        {
            if (halted) return;
            stopReason = reason;
            halted = true;
            ShotStack.Log("Grabaci\u00F3n: se detiene sola: " + reason);
            Ui(delegate { if (!stopping) Stop(); });
        }

        // Once a second, for every quality: the screen is still the same shape, and the disk still has room for what is
        // being written plus what the final pass will write. When little is left the bar says how many minutes.
        void CheckHealth()
        {
            if (source.GeometryChanged) { Halt("La resoluci\u00F3n o la orientaci\u00F3n de la pantalla ha cambiado"); return; }
            long now = Stopwatch.GetTimestamp();
            if (now < nextDiskCheck) return;
            nextDiskCheck = now + Stopwatch.Frequency;
            TellAudio(mic, ref micTold);
            TellAudio(sys, ref sysTold);
            TellLimits();
            long free = Cine.FreeBytes(Settings.TempDir);
            if (free < 0) return;
            long sysBytes = FileLength(sysPath), micBytes = FileLength(micPath);
            long videoBytes = FileLength(video), audioBytes = sysBytes + micBytes;
            long mixBytes = cine && sysBytes > 0 && micBytes > 0 ? Math.Max(sysBytes, micBytes) : 0;
            long need = DiskBudget.Need(cine, lossless, direct, videoBytes, audioBytes, mixBytes);
            if (free < need) { Halt(Recorder.DiskReason); return; }
            // Pace: what was written plus what the final pass will need, smoothed over a few seconds. A pause writes nothing:
            // the pace of the recording is kept, not averaged down (the checks above keep running).
            long total = videoBytes + audioBytes + need;
            if (lastTotalAt != 0 && !paused)
            {
                double dt = (now - lastTotalAt) / (double)Stopwatch.Frequency;
                double rate = dt > 0 ? Math.Max(0, total - lastTotal) / dt : 0;
                diskRate = diskRate <= 0 ? rate : diskRate * 0.8 + rate * 0.2;
            }
            lastTotal = total;
            lastTotalAt = now;
            double minutes = DiskBudget.MinutesLeft(free, need, diskRate);
            int level = DiskBudget.Level(minutes, diskLevel);
            int shownMinutes = level == 0 ? -1 : (int)Math.Floor(minutes);
            if (level == diskLevel && shownMinutes == diskMinutes) return;
            if (level > 0 && diskLevel == 0) ShotStack.Log("Grabaci\u00F3n: queda poco disco (" + DiskBudget.Gb(free) + ", ~" + shownMinutes + " min)");
            diskLevel = level;
            diskMinutes = shownMinutes;
            string text = level == 0 ? null : DiskBudget.Chip(minutes);
            Ui(delegate { if (bar != null && !bar.IsDisposed) bar.Disk(text, level); });
        }

        int micTold, sysTold;
        bool limitsTold, inputTold;

        // What makes this take differ from what was asked, once (only the log knew before).
        void TellLimits()
        {
            List<string> lines = new List<string>();
            if (!limitsTold)
            {
                limitsTold = true;
                if (FloatWindow.AffinityFailed && !ShotStack.Test)
                    lines.Add("Este equipo no deja ocultar la barra de grabaci\u00F3n: saldr\u00E1 en el v\u00EDdeo si est\u00E1 dentro del \u00E1rea.");
                // Cine asks before recording this way; the other qualities say it here.
                if (!source.Gpu && !cine)
                    lines.Add("Se graba con el m\u00E9todo de reserva: puede ir m\u00E1s lento y no ver v\u00EDdeos, juegos ni algunas apps.");
            }
            if (!inputTold && hooks != null && hooks.Failed)
            {
                inputTold = true;
                lines.Add("Windows no deja seguir el rat\u00F3n y el teclado ahora: el v\u00EDdeo sale sin clics ni teclas.");
            }
            if (lines.Count == 0) return;
            string text = string.Join("\n", lines.ToArray());
            Ui(delegate { owner.Notify("Grabando con limitaciones", text); });
        }

        // A sound device that drops out mid-take is followed silently by AudioCapture; the user is told each change.
        void TellAudio(AudioCapture a, ref int told)
        {
            if (a == null) return;
            int t = a.Trouble, was = told;
            if (t == was) return;
            told = t;
            string what = a.Computer ? "sonido del equipo" : "micr\u00F3fono", title, text;
            if (t == 0)
            {
                title = a.Computer ? "Sonido del equipo recuperado" : "Micr\u00F3fono recuperado";
                text = "Vuelve a grabarse desde \u00AB" + a.DeviceName + "\u00BB.";
            }
            else if (t == 1)
            {
                title = a.Computer ? "La salida de sonido elegida no est\u00E1" : "El micr\u00F3fono elegido se ha desconectado";
                text = "Se graba con \u00AB" + a.DeviceName + "\u00BB, el de Windows, hasta que vuelva.";
            }
            else
            {
                title = a.Computer ? "Sin sonido del equipo" : "Sin micr\u00F3fono";
                text = "No hay ning\u00FAn dispositivo: esta parte se graba sin " + what + ".";
            }
            ShotStack.Log("Grabaci\u00F3n: " + title + (t == 1 ? " (" + a.DeviceName + ")" : ""));
            Ui(delegate { owner.Notify(title, text); });
        }

        // The current size of a file another process may still be writing (the directory entry lags behind).
        static long FileLength(string f)
        {
            if (f == null) return 0;
            try
            {
                using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return fs.Length;
            }
            catch
            {
                try { return File.Exists(f) ? new FileInfo(f).Length : 0; }
                catch { return 0; }
            }
        }

        void WriteLoop()
        {
            Power.OptOutThisThread();
            writerTid = Native.GetCurrentThreadId();
            int size = source.FrameBytes;
            IntPtr held = IntPtr.Zero, black = IntPtr.Zero;
            try
            {
                foreach (IntPtr f in queue.GetConsumingEnumerable())
                {
                    IntPtr data = f;
                    if (data == IntPtr.Zero)
                    {
                        if (held == IntPtr.Zero && black == IntPtr.Zero) black = Black(size);
                        data = held != IntPtr.Zero ? held : black;
                    }
                    if (!writeFailed && !feedCut && !WriteFrame(data, size))
                    {
                        writeFailed = true;
                        if (!cancelled && !feedCut && error == null) error = "FFmpeg se ha cerrado durante la grabaci\u00F3n. " + Tail();
                        // Ends the recording if it still runs; a stop already under way must not read as a second press.
                        Ui(delegate { if (!stopping) Stop(); });
                    }
                    if (f != IntPtr.Zero)
                    {
                        if (held != IntPtr.Zero) pool.Give(held);
                        held = f;
                    }
                }
            }
            catch (Exception ex) { ShotStack.Log("Grabaci\u00F3n: " + ex.Message); }
            finally
            {
                if (held != IntPtr.Zero) pool.Give(held);
                if (black != IntPtr.Zero) Marshal.FreeHGlobal(black);
                try { pipeStream.Close(); } catch { }
            }
        }

        IntPtr Black(int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            byte[] b = new byte[size];
            if (source.Nv12)
            {
                int luma = area.Width * area.Height;
                for (int i = 0; i < size; i++) b[i] = (byte)(i < luma ? 16 : 128);
            }
            Marshal.Copy(b, 0, p, size);
            return p;
        }

        bool WriteFrame(IntPtr data, int size)
        {
            try
            {
                if (pipe == null)
                {
                    byte[] b = new byte[size];
                    Marshal.Copy(data, b, 0, size);
                    pipeStream.Write(b, 0, size);
                    return true;
                }
                int off = 0;
                while (off < size)
                {
                    int done;
                    if (!WriteFile(pipe, new IntPtr(data.ToInt64() + off), size - off, out done, IntPtr.Zero) || done <= 0) return false;
                    off += done;
                }
                return true;
            }
            catch { return false; }
        }

        // Cinema: every frame goes through CineSink, which keeps its own copy, so buffers come back at once.
        void WriteLoopCine()
        {
            Power.OptOutThisThread();
            int size = source.FrameBytes;
            IntPtr blank = IntPtr.Zero;
            try
            {
                foreach (IntPtr f in queue.GetConsumingEnumerable())
                {
                    if (!writeFailed)
                    {
                        bool ok;
                        if (f != IntPtr.Zero) ok = sink.Write(f);
                        else if (sink.Frames > 0) { repeatedFailed++; ok = sink.Repeat(); }
                        else
                        {
                            // Nothing to repeat yet: black, and counted.
                            if (blank == IntPtr.Zero) blank = Black(size);
                            black++;
                            ok = sink.Write(blank);
                        }
                        if (!ok)
                        {
                            writeFailed = true;
                            if (!cancelled && error == null) error = "FFmpeg se ha cerrado durante la grabaci\u00F3n. " + Tail();
                            Ui(delegate { if (!stopping) Stop(); });
                        }
                    }
                    if (f != IntPtr.Zero) pool.Give(f);
                }
            }
            catch (Exception ex) { ShotStack.Log("Grabaci\u00F3n: " + ex.Message); }
            finally
            {
                if (blank != IntPtr.Zero) Marshal.FreeHGlobal(blank);
            }
        }

        // On the capture thread. Whatever goes wrong while saving, the bar and the recording state still end.
        void Finish()
        {
            try { Complete(); }
            catch (Exception ex)
            {
                if (!cancelled && error == null) error = "No se pudo guardar la grabaci\u00F3n: " + ex.Message;
                ShotStack.Log("Grabaci\u00F3n: " + ex);
                // Whatever it had not released yet (each of these can be released twice).
                if (proc != null)
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                }
                if (sink != null) { try { sink.Dispose(); } catch { } }
                try { if (source != null) source.Dispose(); } catch { }
                if (pool != null) pool.Dispose();
                if (!cancelled && frames > 0) RescueOnError();
                DeleteOutput();
                if (video != output) TryDelete(video);
                TryDelete(sysPath);
                TryDelete(micPath);
            }
            try { health = Health(); }
            catch (Exception ex) { ShotStack.Log("Grabaci\u00F3n: " + ex.Message); }
            if (quitting) KeepOnQuit();
            watch.Dispose();
            finished.Set();
            Ui(Done);
        }

        RecordHealth health;

        // Did the recording keep up? Cine counts only real defects (a late Cine frame still finds its own image).
        RecordHealth Health()
        {
            if (cancelled || source == null || frames <= warmFrames) return null;
            double fps = fpsNum / (double)fpsDen, seconds = frames / fps;
            bool battery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            RecordHealth h = cine
                ? RecordHealth.Assess(frames - warmFrames, seconds, 0, source.Misses, repeatedFailed + black, 0, 0, quality, false, (int)Math.Round(fps), area.Width, area.Height, battery)
                : RecordHealth.Assess(frames - warmFrames, seconds, lateFrames, source.Misses, repeats, stalls, maxLateWarm, quality, gif, (int)Math.Round(fps), area.Width, area.Height, battery);
            ShotStack.Log("Grabaci\u00F3n: ritmo: " + lateFrames + " tarde, " + source.Misses + " con imagen perdida, " + (cine ? repeatedFailed : repeats) + " repetidos, " +
                          stalls + " esperas del codificador, retraso m\u00E1ximo " + (int)maxLateWarm + " ms" + (h.Bad ? " (se avisa)" : ""));
            return h;
        }

        static void Kill(Process p)
        {
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
        }

        // Waits for a thread, or less if the save is cancelled meanwhile. True if it ended.
        bool JoinUnlessCancelled(Thread t, int ms)
        {
            Stopwatch w = Stopwatch.StartNew();
            while (!t.Join(250))
                if (saveCancelled || w.ElapsedMilliseconds >= ms) return false;
            return true;
        }

        bool WaitUnlessCancelled(Process p, int ms)
        {
            Stopwatch w = Stopwatch.StartNew();
            while (!p.WaitForExit(250))
                if (saveCancelled || w.ElapsedMilliseconds >= ms) return false;
            return true;
        }

        // Waits for FFmpeg, then makes the final file (GIF, or video with sound and the final encode).
        void Complete()
        {
            if (cine) { CompleteCine(); return; }
            // A cancelled or stuck FFmpeg is killed, so the writer's pending write fails instead of waiting forever.
            if (cancelled) Kill(proc);
            bool finishDirect = direct && !cancelled;
            if (writer != null && !JoinUnlessCancelled(writer, 120000))
            {
                // A cancelled save of a direct MP4: no more frames, and the writer's pending write is cancelled so it
                // closes the encoder's input and the encoder writes the index (killed, the file would not play).
                if (finishDirect && saveCancelled)
                {
                    feedCut = true;
                    CancelPendingWrites(writer, writerTid, 5000);
                }
                if (writer.IsAlive)
                {
                    Kill(proc);
                    writer.Join(5000);
                    finishDirect = false;
                }
                if (error == null && !cancelled && !saveCancelled) error = "FFmpeg ha dejado de responder.";
            }
            if (cancelled)
            {
                try { if (!proc.WaitForExit(3000)) proc.Kill(); } catch { }
            }
            else if (!WaitUnlessCancelled(proc, 120000))
            {
                // A direct MP4 is left to finish by itself: killed, it would have no index and not play. A cancelled
                // save still gives it a moment, so it goes to recuperado whole; past that the encoder (outside the
                // job) finishes the file where it is.
                if (!direct) Kill(proc);
                else if (finishDirect && saveCancelled) { try { proc.WaitForExit(10000); } catch { } }
                if (error == null && !saveCancelled) error = "FFmpeg no termin\u00F3 de guardar el v\u00EDdeo.";
            }
            bool exited;
            try { exited = proc.HasExited; } catch { exited = true; }
            if (error == null && !cancelled && !saveCancelled && exited && proc.ExitCode != 0) error = "FFmpeg no pudo guardar el v\u00EDdeo. " + Tail();
            double seconds = frames * (double)fpsDen / fpsNum;
            long samples = (long)Math.Round(seconds * AudioCapture.Rate);
            if (sys != null) sys.Finish(samples);
            if (mic != null) mic.Finish(samples);
            if (source.Gpu) ShotStack.Log("Grabaci\u00F3n: " + frames + " fotogramas, " + source.Presents + " im\u00E1genes nuevas de la pantalla, retraso m\u00E1ximo " + (int)maxLate + " ms");
            try { source.Dispose(); } catch { }
            pool.Dispose();
            SaveFinal(seconds);
            try { proc.Dispose(); } catch { }
        }

        // The final file from the work files, whatever the quality: nothing recorded is ever deleted when this fails or
        // is cancelled; it goes to recuperado and the message says where.
        internal void SaveFinal(double seconds)
        {
            bool go = error == null && !cancelled && !saveCancelled;
            if (go && gif) MakeGif(seconds);
            else if (go && !direct) MakeVideo(seconds);
            if (saveCancelled && !cancelled && error == null) error = PassWatch.CancelledText;
            if (!cancelled && error != null && frames > 0) RescueOnError();
            if (cancelled || error != null) DeleteOutput();
            if (video != output) TryDelete(video);
            TryDelete(sysPath);
            TryDelete(micPath);
        }

        // Cinema: waits for the live encoder, then builds the master (video copied, sound copied bit for bit) and checks
        // it frame by frame against what was piped. Work files are never deleted if something fails after recording.
        void CompleteCine()
        {
            if (cancelled) sink.Kill();
            if (writer != null && !JoinUnlessCancelled(writer, cancelled ? 5000 : 300000))
            {
                sink.Kill();
                writer.Join(5000);
                if (error == null && !cancelled && !saveCancelled) error = "FFmpeg ha dejado de responder.";
            }
            int code;
            bool closed = sink.Close(cancelled ? 3000 : saveCancelled ? 15000 : 300000, out code);
            if (error == null && !cancelled && !saveCancelled && (!closed || code != 0)) error = "FFmpeg no pudo guardar el v\u00EDdeo. " + Tail();
            frames = sink.Frames;
            double seconds = frames * (double)fpsDen / fpsNum;
            long samples = Cine.AudioFramesFor(frames, fpsNum, fpsDen);
            if (sys != null) sys.Finish(samples);
            if (mic != null) mic.Finish(samples);
            try { source.Dispose(); } catch { }
            pool.Dispose();
            if (!cancelled)
            {
                report = BuildReport();
                if (error == null && frames == 0) error = "No se ha grabado ning\u00FAn fotograma.";
                if (error == null && saveCancelled) error = PassWatch.CancelledText;
                if (error == null) MakeCine(seconds);
                else if (frames > 0) RescueOnError();
                SaveReport();
            }
            sink.Dispose();
            if (cancelled || error != null) DeleteOutput();
            if (video != output) TryDelete(video);
            TryDelete(sysPath);
            TryDelete(micPath);
        }

        CineReport BuildReport()
        {
            CineReport r = new CineReport();
            r.File = output;
            r.Started = startedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            r.StopReason = stopReason;
            r.CaptureApi = source.Description;
            r.Encoder = enc;
            r.Width = area.Width;
            r.Height = area.Height;
            r.FpsNum = fpsNum;
            r.FpsDen = fpsDen;
            r.RefreshHz = source.RefreshNum / (double)source.RefreshDen;
            r.Hdr = source.Hdr;
            r.Gdi = !source.Gpu;
            r.SourcePresents = source.SourcePresents;
            r.Coalesced = source.Coalesced;
            r.ImagesReceived = source.Presents;
            r.ImagesUnused = source.Unused;
            r.HistoryMisses = source.Misses;
            r.Protected = source.Protected;
            r.LostEvents = source.LostEvents;
            r.LostMs = source.LostMs;
            r.MaxLateMs = maxLate;
            r.RecordSeconds = frames * (double)fpsDen / fpsNum;
            r.FramesEmitted = queued;
            r.FramesWritten = frames;
            r.FramesRepeatedFailed = repeatedFailed;
            r.FramesBlack = black;
            r.QueuePeak = queuePeak;
            r.PoolFrames = pool.Capacity;
            r.Discarded = discarded;
            r.Pauses = pauses.Count;
            r.SoundJoins = (sys != null ? sys.Joins : 0) + (mic != null ? mic.Joins : 0);
            r.OverlayFrames = overlay != null ? overlay.DrawnFrames : 0;
            r.AudioConverted = (sys != null && sys.Converted) || (mic != null && mic.Converted);
            r.AudioNotes = new List<string>();
            AudioNote(r.AudioNotes, "Sonido del equipo", sys);
            AudioNote(r.AudioNotes, "Micr\u00F3fono", mic);
            r.Saturation = Saturation(sys, mic);
            return r;
        }

        internal static void AudioNote(List<string> notes, string name, AudioCapture a)
        {
            if (a == null) return;
            if (a.ClipEvents > 0)
                notes.Add(name + ": " + ClipText(a) + ". La grabaci\u00F3n lo guarda tal cual; si se oye distorsionado, baja el volumen de esa fuente antes de grabar.");
            if (a.OverlapFrames > 0 || a.Duplicated > 0 || a.Removed > 0 || a.Converted)
            {
                string t = name + ": ";
                if (a.OverlapFrames > 0) t += a.OverlapFrames + " muestras solapadas descartadas. ";
                if (a.Duplicated + a.Removed > 0) t += "Ajuste del reloj de sonido: " + a.Duplicated + " muestras duplicadas y " + a.Removed + " quitadas. ";
                if (a.Converted) t += "El dispositivo no entrega 48 kHz est\u00E9reo en coma flotante: Stackshot lo ha convertido (no es copia exacta). ";
                notes.Add(t.Trim());
            }
            if (a.SilenceFrames > 0)
                notes.Add(name + ": " + (a.SilenceFrames / (double)AudioCapture.Rate).ToString("0.0", CultureInfo.InvariantCulture) + " s de silencio insertado (huecos, o nada sonando).");
            if (a.Joins > 0)
                notes.Add(name + ": al reanudar tras " + (a.Joins == 1 ? "la pausa" : "cada una de las " + a.Joins + " pausas") + ", el sonido se funde durante " +
                          AudioCapture.JoinFrames * 1000 / AudioCapture.Rate + " ms para que el corte no se oiga.");
        }

        // "saturó en 3 momentos (el primero a 0:12, 40 ms a fondo de escala)"
        internal static string ClipText(AudioCapture a)
        {
            double total = a.ClipFrames / (double)AudioCapture.Rate;
            int first = (int)Math.Max(0, a.FirstClipSeconds);
            string held = total < 0.9995 ? Math.Max(1, (int)Math.Round(total * 1000)) + " ms" : total.ToString("0.0", CultureInfo.CurrentCulture) + " s";
            string when = first >= 3600 ? (first / 3600) + ":" + (first / 60 % 60).ToString("00") + ":" + (first % 60).ToString("00")
                                        : (first / 60) + ":" + (first % 60).ToString("00");
            return "satur\u00F3 " + (a.ClipEvents == 1 ? "una vez" : "en " + a.ClipEvents + " momentos") +
                   " (" + (a.ClipEvents == 1 ? "a " : "el primero a ") + when + ", " + held + " a fondo de escala)";
        }

        // One line for the notice when a source clipped, or null.
        internal static string Saturation(AudioCapture sys, AudioCapture mic)
        {
            List<string> parts = new List<string>();
            if (sys != null && sys.ClipEvents > 0) parts.Add("el sonido del equipo " + ClipText(sys));
            if (mic != null && mic.ClipEvents > 0) parts.Add("el micr\u00F3fono " + ClipText(mic));
            if (parts.Count == 0) return null;
            string t = string.Join(" y ", parts.ToArray());
            return "Ojo: " + t + ".";
        }

        void MakeCine(double seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string sysArg = sys != null ? sysPath : null, micArg = mic != null ? micPath : null;
            string gain = sys != null && mic != null ? VoiceGain() : "";
            List<CineAudioTrack> tracks = CineMaster.Tracks(sysArg, micArg, Cine.AudioFramesFor(frames, fpsNum, fpsDen));
            report.Audio = tracks;
            string temp = Path.Combine(Settings.TempDir, "~cine " + stamp + Cine.Extension);
            TryDelete(temp);
            masterTemp = temp;
            string failure = CineMaster.Build(ffmpeg, video, sysArg, micArg, gain, temp, seconds, CineProgress("Guardando el m\u00E1ster sin p\u00E9rdidas\u2026"), watch);
            report.MuxSeconds = sw.Elapsed.TotalSeconds;
            if (failure != null)
            {
                TryDelete(temp);
                masterTemp = null;
                error = watch.Cancelled ? PassWatch.CancelledText : "No se pudo crear el m\u00E1ster sin p\u00E9rdidas. " + failure;
                RescueOnError();
                return;
            }
            CineExpect expect = new CineExpect();
            expect.Width = area.Width;
            expect.Height = area.Height;
            expect.FpsNum = fpsNum;
            expect.FpsDen = fpsDen;
            expect.FrameHashes = sink.Hashes;
            expect.Audio = tracks;
            Stopwatch vw = Stopwatch.StartNew();
            CineVerification v = CineVerifier.Verify(ffmpeg, temp, expect, CineProgress("Comprobando que no hay p\u00E9rdidas\u2026"), watch);
            report.VerifySeconds = vw.Elapsed.TotalSeconds;
            report.Verification = v;
            poster = v.Poster;
            string dest = output;
            if (!v.Passed)
            {
                dest = ShotStack.Unique(Path.ChangeExtension(output, null) + " (sin verificar)" + Cine.Extension);
                warning = watch.Cancelled ? "Has cancelado la comprobaci\u00F3n: el archivo se ha guardado sin verificar como \u00AB" + Path.GetFileName(dest) + "\u00BB."
                                          : "La comprobaci\u00F3n ha encontrado diferencias: el archivo se ha guardado como \u00AB" + Path.GetFileName(dest) + "\u00BB.";
            }
            else if (File.Exists(dest)) dest = ShotStack.Unique(dest);
            File.Move(temp, dest);
            masterTemp = null;
            output = dest;
            report.File = output;
            if (!v.Passed)
            {
                // The live file may be fine where the master is not: it is kept too.
                string where = Rescue(false);
                if (where != null) warning += "\n\n" + where;
            }
        }

        // Progress of a pass on the bar, as a fraction (the bar is only touched when the percentage changes).
        Action<double> CineProgress(string label)
        {
            int shown = -1;
            Ui(delegate { if (bar != null) bar.Converting(label, 0); });
            return delegate(double f)
            {
                int pct = (int)Math.Max(0, Math.Min(99, f * 100));
                if (pct == shown) return;
                shown = pct;
                Ui(delegate { if (bar != null) bar.Converting(label, pct / 100.0); });
            };
        }

        bool outputKept;    // the unfinished output could not be moved to recuperado: it stays and is never deleted

        void DeleteOutput()
        {
            if (!outputKept) TryDelete(output);
        }

        // What was recorded stays on disk when anything fails after recording, in every quality: the live file, the
        // sound, the master if there is one and (withOutput) a half-written output go to recuperado\<time>, which
        // nothing sweeps. A file that can't be moved stays where it is and is never deleted either. Returns what to
        // tell the user, or null if there was nothing.
        string Rescue(bool withOutput)
        {
            string dir = Path.Combine(Recorder.RescueRoot, stamp);
            string original = output;
            string partial = withOutput && output != null && output != video && output != masterTemp && FileLength(output) > 0 ? output : null;
            List<string> kept = new List<string>();
            Keep(ref video, dir, kept);
            Keep(ref sysPath, dir, kept);
            Keep(ref micPath, dir, kept);
            Keep(ref masterTemp, dir, kept);
            Keep(ref partial, dir, kept);
            if (kept.Count == 0) return null;
            List<string> stuck = new List<string>();
            bool moved = false;
            foreach (string k in kept)
            {
                if (k.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) moved = true;
                else stuck.Add(k);
                if (original != null && string.Equals(k, original, StringComparison.OrdinalIgnoreCase)) outputKept = true;
            }
            if (moved) rescueDir = dir;
            ShotStack.Log("Grabaci\u00F3n: archivos de trabajo conservados: " + string.Join(", ", kept.ToArray()));
            string text = "Lo grabado no se ha borrado.";
            if (moved) text += " Est\u00E1 en:\n" + dir;
            if (stuck.Count > 0) text += "\nNo se ha podido mover y sigue donde estaba:\n" + string.Join("\n", stuck.ToArray());
            return text;
        }

        static void Keep(ref string f, string dir, List<string> kept)
        {
            string file = f;
            f = null;
            if (file == null || !File.Exists(file)) return;
            try
            {
                Directory.CreateDirectory(dir);
                string to = Path.Combine(dir, Path.GetFileName(file));
                File.Move(file, to);
                kept.Add(to);
            }
            catch (Exception ex)
            {
                kept.Add(file);
                ShotStack.Log("Grabaci\u00F3n: no se pudo mover " + file + ": " + ex.Message);
            }
        }

        void RescueOnError()
        {
            string where = Rescue(true);
            if (where != null) error = (error ?? "No se pudo guardar la grabaci\u00F3n.") + "\n\n" + where;
        }

        void SaveReport()
        {
            if (report == null) return;
            try
            {
                if (output != null && File.Exists(output)) report.FileBytes = new FileInfo(output).Length;
                // A recording without its master says why and where what was recorded went.
                report.Error = error;
                report.Rescued = rescueDir;
                report.Save(Path.Combine(Settings.DataDir, "cine"));
                ShotStack.Log("Cine: " + report.Summary().Replace("\r", "").Replace("\n", " | "));
            }
            catch (Exception ex) { ShotStack.Log("Cine: informe: " + ex.Message); }
        }

        void MakeGif(double seconds)
        {
            Encode(Recorder.GifArgs(area.Width, gifFps, video, output), "Creando el GIF\u2026", seconds, "No se pudo crear el GIF.");
        }

        // The finished MP4: sound added (voice brought to a steady level, mixed without clipping) and, for the lossless
        // qualities, the final H.264 encode with exact BT.709 colors.
        void MakeVideo(double seconds)
        {
            string voiceGain = mic != null ? VoiceGain() : "";
            string args = Recorder.FinishArgs(quality, lossless, source != null && source.Nv12, area.Width, area.Height, fpsNum, fpsDen, video,
                                              sys != null ? sysPath : null, mic != null ? micPath : null, voiceGain, output);
            string label = quality == Recorder.Maximum ? "Guardando en m\u00E1xima calidad\u2026" : "A\u00F1adiendo el sonido\u2026";
            Encode(args, label, seconds, "No se pudo guardar el v\u00EDdeo.");
        }

        // A fixed gain that brings the voice to about -16 LUFS (EBU R128), the level of good voice-overs: no compression
        // or pumping, just the right volume. Silence and near silence are left alone.
        string VoiceGain()
        {
            string err;
            Recorder.Run(ffmpeg, "-hide_banner -nostats -f f32le -sample_rate 48000 -ch_layout stereo -i " + Recorder.Quote(micPath) +
                                 " -af highpass=f=70,loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json -f null -", 300000, watch, out err);
            Match m = Regex.Match(err ?? "", "\"input_i\"\\s*:\\s*\"(-?[0-9.]+)\"");
            double i;
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out i) || i < -50 || i > 0) return "";
            double gain = Math.Max(-6, Math.Min(15, -16 - i));
            ShotStack.Log("Micr\u00F3fono: " + i.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS, ganancia " + gain.ToString("0.0", CultureInfo.InvariantCulture) + " dB");
            return Math.Abs(gain) < 0.5 ? "" : ",volume=" + gain.ToString("0.0", CultureInfo.InvariantCulture) + "dB";
        }

        // Runs a final FFmpeg pass at low priority (the computer stays responsive), with its progress on the bar, under
        // the watch: a pass that stops making progress is ended, and the bar's Cancelar ends it at once.
        void Encode(string args, string label, double seconds, string failure)
        {
            lock (errors) errors.Length = 0;
            Ui(delegate { if (bar != null) bar.Converting(label, 0); });
            int shown = -1;
            int code = Recorder.RunPass(ffmpeg, args, seconds, watch, delegate(double f)
            {
                int pct = (int)Math.Max(0, Math.Min(99, f * 100));
                if (pct == shown) return;
                shown = pct;
                Ui(delegate { if (bar != null) bar.Converting(label, pct / 100.0); });
            }, errors);
            if (watch.Cancelled) { if (error == null) error = PassWatch.CancelledText; }
            else if (watch.Stalled) error = failure + " " + PassWatch.StalledText;
            else if (code != 0) error = failure + " " + Tail();
        }

        void Done()
        {
            Release();
            if (quitting)
            {
                // Shutdown already took care of the file; nothing is shown while Stackshot closes.
                if (bar != null && !bar.IsDisposed) bar.Close();
                if (webcam != null && !webcam.IsDisposed) webcam.Close();
                if (edges != null) foreach (FrameEdge e in edges) if (!e.IsDisposed) e.Close();
                Recorder.Ended(this);
                if (poster != null) { poster.Dispose(); poster = null; }
                return;
            }
            bool saved = error == null && !cancelled && output != null && File.Exists(output);
            // A short check when the file is ready; the bar closes itself after its fade.
            if (bar != null) bar.Finish(saved);
            if (webcam != null) { webcam.Close(); webcam = null; }
            if (edges != null) { foreach (FrameEdge e in edges) e.Close(); edges = null; }
            Recorder.Ended(this);
            if (error != null)
            {
                if (poster != null) { poster.Dispose(); poster = null; }
                ShotStack.Log("Grabaci\u00F3n: " + error.Replace("\n", " "));
                ShowError();
                return;
            }
            if (saved)
            {
                ShotStack.Log("Grabaci\u00F3n lista: " + Path.GetFileName(output) + " (" + frames + " fotogramas, " + (new FileInfo(output).Length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB)");
                if (poster != null) ShotStack.SetPoster(output, poster);
                owner.AddRecording(output);
                if (cine && report != null) CineOutcome();
                else Outcome();
            }
            if (poster != null) { poster.Dispose(); poster = null; }
        }

        // The failure, and the folder with what was kept when there is one.
        void ShowError()
        {
            bool mine = saveCancelled && error.StartsWith(PassWatch.CancelledText, StringComparison.Ordinal);
            MessageBoxIcon icon = mine ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            if (rescueDir == null || !Directory.Exists(rescueDir))
            {
                MessageBox.Show(error, "Stackshot", MessageBoxButtons.OK, icon);
                return;
            }
            if (MessageBox.Show(error + "\n\n\u00BFAbrir la carpeta?", "Stackshot", MessageBoxButtons.YesNo, icon) != DialogResult.Yes) return;
            try { Process.Start(Native.Explorer, "\"" + rescueDir + "\""); }
            catch (Exception ex) { ShotStack.Log("Carpeta: " + ex.Message); }
        }

        // Estandar, Alta, Maxima and GIF: why it stopped by itself, and whether it kept up, in one short notice.
        void Outcome()
        {
            string title = null, text = null;
            if (stopReason != null)
            {
                title = "Grabaci\u00F3n detenida";
                text = stopReason + ": se ha guardado lo grabado hasta ese momento.";
            }
            if (health != null && health.Bad)
            {
                if (title == null) title = "La grabaci\u00F3n ha ido a tirones";
                text = text == null ? health.Text : text + " " + health.Text;
            }
            // A source that clipped: the limiter keeps the MP4 from crackling, but the sound itself came in distorted.
            string clipped = Saturation(sys, mic);
            if (clipped != null)
            {
                if (title == null) title = "El sonido ha saturado";
                clipped += " Si se oye distorsionado, baja el volumen de esa fuente antes de grabar.";
                text = text == null ? clipped : text + " " + clipped;
            }
            // Clicking it opens Grabación, where the advice (fps, quality, memory) can be acted on.
            if (title != null) owner.NotifyPage(title, text, "record");
        }

        // Cinema: a short notice when everything checked out; the details when something did not, with what to try
        // when the computer could not keep up.
        void CineOutcome()
        {
            if (report.Passed && report.CaptureClean)
            {
                long n = report.Verification.FramesMatched;
                string done = n + " fotogramas id\u00E9nticos a los capturados. Para compartirlo, \u00E1brelo y exp\u00F3rtalo a MP4 (esa copia s\u00ED comprime).";
                if (report.Saturation != null) done = report.Saturation + " " + done;
                owner.Notify("Cine: sin p\u00E9rdidas, verificado", done);
                return;
            }
            string text = report.Summary();
            if (warning != null) text = warning + "\n\n" + text;
            if (health != null && health.Bad) text += "\n\n" + health.Text;
            else if (stopReason != null && stopReason.StartsWith("El equipo no ha podido", StringComparison.Ordinal))
                text += "\n\nPrueba a grabar a 30 fps, un \u00E1rea m\u00E1s peque\u00F1a o con m\u00E1s memoria para picos de movimiento (Grabaci\u00F3n > Cine).";
            MessageBox.Show(text, "Stackshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        string Tail()
        {
            if (sink != null) return sink.ErrorTail;
            lock (errors)
            {
                string t = errors.ToString().Trim();
                return t.Length > 300 ? t.Substring(t.Length - 300) : t;
            }
        }

        static void TryDelete(string f)
        {
            try { if (f != null && File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    // Floating HUD while recording: time, the sound being recorded (live meters; click a source to mute it), Stop and
    // Discard. On Stop it narrows into a pill with the saving progress, confirms with a check and fades out. A dark
    // capsule with a soft shadow, excluded from the capture; its empty parts drag it out of the way.
    class RecordBar : FloatWindow
    {
        enum Phase { Rec, Busy, Progress, Done, Gone }

        const string DiscardHint = "\u00BFDescartar?";
        const string CancelText = "Cancelar", CancelHint = "\u00BFCancelar?";
        const string PausedText = "En pausa", PauseTip = "Pausar", ResumeTip = "Reanudar";
        static readonly WM.Color Amber = Ds.Rgb(255, 179, 64);
        const double ArmMs = 3000;          // a first click on Discard asks for a second one within this time
        const double QuickDiscardMs = 3000; // shorter recordings are discarded at once
        static readonly WM.Color Red = Ds.Rgb(255, 59, 48), Accent = Ds.Rgb(10, 132, 255), Green = Ds.Rgb(48, 209, 88);

        readonly Session session;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly bool hasSys, hasMic;
        readonly float[] meter = new float[2];
        readonly Tween cap = new Tween(0);     // capsule width; the window may be wider (transparent, click-through)
        readonly Tween fade = new Tween(1);    // opacity of the content shown
        static RecordBar previous;             // the last bar, which may still be saying "Guardado"
        // phase, label and progress say what the recording is doing; the face is what the capsule shows, which follows
        // them through a short fade (and keeps the recording controls while a discarded bar fades out).
        Phase phase = Phase.Rec, face = Phase.Rec;
        string label, faceLabel, shown;
        double progress = -1, faceProgress = -1, armedAt = -1, nudgeAt = -1;
        int hot = -1, pressed = -1, spin, faceW;
        bool dragging, longClock;
        Point dragFrom;
        double dragX, dragY;
        // Recording layout, in capsule coordinates.
        int recW, timeX, srcX, diskX, stopX, stopW, discardX, pauseX;
        bool paused, tipShown;
        // Low disk: "~N min de disco", amber (1) or red (2); null when there is room.
        string diskText;
        int diskLevel;
        WI.BitmapSource shadowL, shadowM, shadowR;
        WM.FormattedText[] clockGlyphs;          // 0-9 and the colon
        int clockFor;                            // the palette they were made with (HighContrast.Generation)
        double digitW;
        int shadowCore;

        public RecordBar(Session session, Rectangle area)
        {
            this.session = session;
            hasSys = session.SystemAudio != null;
            hasMic = session.Microphone != null;
            // A new recording started while the last bar still confirms its file: that one gets out of the way.
            if (previous != null && !previous.IsDisposed && previous.phase == Phase.Done) previous.StepAside();
            previous = this;
            Screen scr = Screen.FromRectangle(area);
            s = ShotStack.ScaleFor(scr);
            Pad = P(22);
            Plan();
            cap.Set(recW);
            Size sz = new Size(recW, P(46));
            Rectangle wa = scr.WorkingArea;
            int x0 = area.X + (area.Width - sz.Width) / 2;
            int y0 = area.Bottom + P(14);
            if (y0 + sz.Height > wa.Bottom) y0 = area.Top - sz.Height - P(14);
            // Full-screen area: inside it (it is excluded from the video anyway), above the taskbar.
            if (y0 < wa.Top) y0 = Math.Min(area.Bottom, wa.Bottom) - sz.Height - P(18);
            x0 = Math.Max(wa.Left + P(8), Math.Min(wa.Right - sz.Width - P(8), x0));
            SetSize(sz);
            JumpTo(x0, y0 + P(10));
            ShowQuiet();
            alpha.Go(1, 200, 0, Ease.OutCubic, null);
            MoveTo(x0, y0, 320, 0.78, 0);
            tick.Tick += delegate { Tick(); };
            Retime();
        }

        protected override bool PerPixel { get { return true; } }

        // Devices are being opened: nothing is recorded yet.
        public void Prepare(bool countdown)
        {
            if (phase == Phase.Rec && !IsDisposed) Become(Phase.Busy, countdown ? "Cuenta atr\u00e1s\u2026" : "Preparando\u2026", -1);
        }

        // Everything is ready and recording starts: back to the recording controls.
        public void Ready()
        {
            if (phase != Phase.Busy || IsDisposed) return;
            phase = Phase.Rec;
            label = null;
            hot = pressed = -1;
            Morph(recW);
            fade.Go(0, fade.Value > 0.01 ? 90 : 1, 0, Ease.OutCubic, Swap);
            Retime();
            Anim.Wake(this);
        }

        // Recording stopped: saving starts.
        public void Saving()
        {
            if (phase == Phase.Rec && !IsDisposed) Become(Phase.Busy, "Guardando\u2026", -1);
        }

        // The save was cancelled: what was recorded is being put away.
        public void Cancelling()
        {
            if (phase != Phase.Done && phase != Phase.Gone && !IsDisposed) Become(Phase.Busy, "Conservando lo grabado\u2026", -1);
        }

        // Little disk left while recording (null: enough again).
        public void Disk(string text, int level)
        {
            if (IsDisposed || (text == diskText && level == diskLevel)) return;
            diskText = text;
            diskLevel = level;
            Plan();
            if (phase == Phase.Rec && face == Phase.Rec) Morph(recW);
            Repaint();
            Anim.Wake(this);
        }

        // Paused or resumed: the time stops, the dot turns into a pause sign, "En pausa" takes the place of the sources and
        // the button becomes Reanudar. The layout stays as it is, so the button stays under the pointer.
        public void Paused(bool on)
        {
            if (IsDisposed || paused == on) return;
            paused = on;
            meter[0] = meter[1] = 0;
            if (hot == 5) Tip(5);
            Retime();
            Repaint();
        }

        // The pause button's tooltip, under it (or over it at the bottom of the screen); excluded from captures too.
        void Tip(int h)
        {
            if (h == 5 && phase == Phase.Rec && face == Phase.Rec && !IsDisposed)
            {
                Rectangle r = PauseRect();
                r.Offset(Pad + CapLeft(), Pad);
                EdTip.Schedule(this, r, paused ? ResumeTip : PauseTip);
                tipShown = true;
            }
            else if (tipShown)
            {
                tipShown = false;
                EdTip.Cancel();
            }
        }

        // A final pass with its progress; fraction < 0: unknown.
        public void Converting(string text, double fraction)
        {
            if (phase == Phase.Done || phase == Phase.Gone || IsDisposed) return;
            if (phase == Phase.Progress && text == label && (fraction < 0) == (progress < 0))
            {
                progress = fraction;
                if (face == Phase.Progress && faceLabel == text) faceProgress = fraction;
                Repaint();
                return;
            }
            Become(Phase.Progress, text, fraction);
        }

        // The end: a short check when the file was saved, then it fades out and closes (at once when discarded or failed).
        public void Finish(bool saved)
        {
            if (phase == Phase.Done || phase == Phase.Gone || IsDisposed) return;
            double wait = 0;
            Tip(-1);
            if (saved) { Become(Phase.Done, "Guardado", -1); wait = 900; }
            else phase = Phase.Gone;
            tick.Stop();
            dragging = false;
            Cursor = Cursors.Default;
            alpha.Go(0, saved ? 240 : 160, wait, Ease.OutCubic, delegate { if (!IsDisposed) Close(); });
            if (!moving) MoveTo(tx, ty + P(6), 260, 1, wait);
            Anim.Wake(this);
        }

        // Out of the way at once (a new recording is starting).
        void StepAside()
        {
            phase = Phase.Gone;
            tick.Stop();
            alpha.Go(0, 120, 0, Ease.OutCubic, delegate { if (!IsDisposed) Close(); });
            Anim.Wake(this);
        }

        // Another recording was asked for while this one runs or is being saved: a short shake says "busy".
        public void Nudge()
        {
            if (phase == Phase.Gone || IsDisposed || dragging) return;
            nudgeAt = Anim.Now;
            Anim.Wake(this);
        }

        void Become(Phase p, string text, double fraction)
        {
            // One pass after another (Cine's master, then its check): a first click on Cancelar still counts.
            if (!KeepsArmed(phase == Phase.Progress, p == Phase.Progress)) armedAt = -1;
            phase = p;
            label = text;
            progress = fraction;
            hot = pressed = -1;
            Tip(-1);
            if (!dragging) Cursor = Cursors.Default;
            Morph(PillWidth());
            // The old content fades out quickly while the capsule changes shape, then the new one fades in.
            fade.Go(0, fade.Value > 0.01 ? 90 : 1, 0, Ease.OutCubic, Swap);
            Retime();
            Anim.Wake(this);
        }

        internal static bool KeepsArmed(bool fromProgress, bool toProgress) { return fromProgress && toProgress; }

        void Swap()
        {
            if (phase == Phase.Gone || IsDisposed) return;
            face = phase;
            faceLabel = label;
            faceProgress = progress;
            faceW = PillWidth();
            fade.Go(1, 200, 0, Ease.OutCubic, null);
        }

        // The timer only runs while something on the bar changes by itself: the meters and the clock, or the spinner.
        void Retime()
        {
            if (IsDisposed) { tick.Stop(); return; }
            bool spinning = phase == Phase.Busy || (phase == Phase.Progress && progress < 0);
            bool asking = phase == Phase.Progress && armedAt >= 0;   // the cancel question expires by itself
            if (phase == Phase.Rec) tick.Interval = Metering() ? 50 : NextSecond();
            else if (spinning) tick.Interval = 90;
            else if (asking) tick.Interval = 200;
            if (phase == Phase.Rec || spinning || asking) tick.Start(); else tick.Stop();
        }

        bool Metering() { return !paused && ((hasSys && !session.SystemAudio.Muted) || (hasMic && !session.Microphone.Muted)); }

        // Without live meters only the clock changes (and the discard question expires): wake just after that.
        int NextSecond()
        {
            double ms = 1000 - session.Elapsed.TotalMilliseconds % 1000 + 15;
            if (armedAt >= 0) ms = Math.Min(ms, ArmMs - (Anim.Now - armedAt) + 15);
            return (int)Math.Max(20, Math.Min(1015, ms));
        }

        void Tick()
        {
            if (IsDisposed) return;
            if (phase == Phase.Rec)
            {
                if (hasSys) meter[0] = Math.Max(session.SystemAudio.Muted ? 0 : session.SystemAudio.Level, meter[0] * 0.8f);
                if (hasMic) meter[1] = Math.Max(session.Microphone.Muted ? 0 : session.Microphone.Level, meter[1] * 0.8f);
                if (armedAt >= 0 && Anim.Now - armedAt > ArmMs) armedAt = -1;
                if (!longClock && session.Elapsed.TotalHours >= 1)
                {
                    longClock = true;
                    Plan();
                    Morph(recW);
                    Anim.Wake(this);
                }
                if (!Metering()) Retime();
            }
            else
            {
                spin++;
                if (armedAt >= 0 && Anim.Now - armedAt > ArmMs) { armedAt = -1; Retime(); }
            }
            Repaint();
        }

        // Repaints only when something visible changed (a still bar costs one paint per second).
        void Repaint()
        {
            if (Signature() != shown) Redraw();
        }

        string Signature()
        {
            if (face == Phase.Rec)
                return "r" + Clock(session.Elapsed) + "|" + Bars(0) + Bars(1) + "|" + hot + "|" + pressed + "|" + (armedAt >= 0) +
                       (hasSys && session.SystemAudio.Muted) + (hasMic && session.Microphone.Muted) + "|" + diskText + diskLevel + "|" + paused;
            return face + "|" + faceLabel + "|" + (int)Math.Round(faceProgress * 100) + "|" + spin + "|" + hot + "|" + pressed + "|" + (armedAt >= 0);
        }

        static string Clock(TimeSpan t)
        {
            int h = (int)t.TotalHours;
            string ms = t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return h > 0 ? h + ":" + ms : ms;
        }

        // Every digit takes the widest one's room, like tabular figures: the time never jitters as it counts.
        WM.FormattedText ClockGlyph(char c)
        {
            if (clockGlyphs == null || clockFor != HighContrast.Generation)
            {
                clockFor = HighContrast.Generation;
                clockGlyphs = new WM.FormattedText[11];
                for (int i = 0; i < 11; i++)
                {
                    clockGlyphs[i] = Ink.Px(i < 10 ? ((char)('0' + i)).ToString() : ":", Ds.Semibold, P(14), Palette.HudLabel);
                    if (i < 10) digitW = Math.Max(digitW, clockGlyphs[i].WidthIncludingTrailingWhitespace);
                }
            }
            return clockGlyphs[c >= '0' && c <= '9' ? c - '0' : 10];
        }

        double ClockCell(char c) { WM.FormattedText g = ClockGlyph(c); return c >= '0' && c <= '9' ? digitW : g.WidthIncludingTrailingWhitespace; }

        double ClockWidth(string text)
        {
            double w = 0;
            foreach (char c in text) w += ClockCell(c);
            return w;
        }

        void DrawClock(WM.DrawingContext dc, string text, double x, double cy)
        {
            foreach (char c in text)
            {
                WM.FormattedText g = ClockGlyph(c);
                double cell = ClockCell(c);
                dc.DrawText(g, new System.Windows.Point(x + (cell - g.WidthIncludingTrailingWhitespace) / 2, Math.Round(cy - g.Height / 2)));
                x += cell;
            }
        }

        // Lit bars of a source's meter (0-3), from -50 dB to 0 dB.
        int Bars(int i)
        {
            if (paused || (i == 0 ? !hasSys || session.SystemAudio.Muted : !hasMic || session.Microphone.Muted)) return 0;
            float level = meter[i];
            double db = level > 0.0001 ? 20 * Math.Log10(level) : -100;
            double v = Math.Max(0, Math.Min(1, (db + 50) / 50));
            int n = 0;
            for (int k = 0; k < 3; k++) if (v > (k + 0.5) / 3.5) n++;
            return n;
        }

        // ---- layout ----

        void Plan()
        {
            double clockW = ClockWidth(longClock ? "0:00:00" : "00:00");
            WM.FormattedText hint = Ink.Px(DiscardHint, Ds.Semibold, P(13), Palette.HudLabel);
            WM.FormattedText stop = Ink.Px("Detener", Ds.Semibold, P(13), Palette.HudLabel);
            timeX = P(34);
            srcX = timeX + (int)Math.Ceiling(clockW) + P(10);
            int left = srcX + (hasSys ? P(44) : 0) + (hasMic ? P(44) : 0);
            diskX = left + P(2);
            if (diskText != null) left = diskX + (int)Math.Ceiling(Ink.Px(diskText, Ds.Semibold, P(12), Palette.HudLabel).WidthIncludingTrailingWhitespace) + P(10);
            // While Discard is armed its question takes the place of the dot, the time and the sources; while paused,
            // "En pausa" does (there is always room for it, so pausing never moves the buttons).
            WM.FormattedText pausedText = Ink.Px(PausedText, Ds.Semibold, P(12), Palette.HudLabel);
            int chipW = diskText != null ? (int)Math.Ceiling(Ink.Px(diskText, Ds.Semibold, P(12), Palette.HudLabel).WidthIncludingTrailingWhitespace) + P(10) : 0;
            pauseX = Math.Max(Math.Max(left, P(14) + (int)Math.Ceiling(hint.WidthIncludingTrailingWhitespace) + P(8)),
                              timeX + (int)Math.Ceiling(pausedText.WidthIncludingTrailingWhitespace) + P(8) + chipW);
            stopX = pauseX + P(30) + P(6);
            stopW = P(30) + (int)Math.Ceiling(stop.WidthIncludingTrailingWhitespace) + P(16);
            discardX = stopX + stopW + P(4);
            recW = discardX + P(30) + P(8);
        }

        Rectangle StopRect() { return new Rectangle(stopX, P(8), stopW, body.Height - P(16)); }
        Rectangle DiscardRect() { return new Rectangle(discardX, (body.Height - P(30)) / 2, P(30), P(30)); }
        Rectangle PauseRect() { return new Rectangle(pauseX, (body.Height - P(30)) / 2, P(30), P(30)); }
        Rectangle SourceRect(int i) { return new Rectangle(srcX + i * P(44), P(7), P(40), body.Height - P(14)); }

        int PillIcon { get { return P(42); } }

        int PillWidth()
        {
            WM.FormattedText t = Ink.Px(label ?? "", Ds.Semibold, P(13), Palette.HudLabel);
            int w = PillIcon + (int)Math.Ceiling(t.WidthIncludingTrailingWhitespace) + P(18);
            if (phase == Phase.Progress && progress >= 0) w += P(12) + PercentWidth();
            if (phase == Phase.Progress) w += P(10) + CancelWidth() - P(8);
            return Math.Max(P(120), w);
        }

        // A final pass can be cancelled from the pill (what was recorded is kept in recuperado).
        int CancelWidth()
        {
            double a = Ink.Px(CancelText, Ds.Semibold, P(12), Palette.HudLabel).WidthIncludingTrailingWhitespace;
            double b = Ink.Px(CancelHint, Ds.Semibold, P(12), Palette.HudLabel).WidthIncludingTrailingWhitespace;
            return (int)Math.Ceiling(Math.Max(a, b)) + P(18);
        }

        // In the pill's content coordinates (content width fw).
        Rectangle CancelRect(int fw)
        {
            int w = CancelWidth(), h = P(28);
            return new Rectangle(fw - P(9) - w, (body.Height - h) / 2, w, h);
        }

        int PercentWidth() { return (int)Math.Ceiling(Ink.Px("100 %", Ds.Medium, P(12), Palette.HudLabel).WidthIncludingTrailingWhitespace); }

        int CapLeft() { return (int)Math.Round((body.Width - Math.Round(cap.Value)) / 2); }

        // The capsule glides to a new width around its center; the window only grows (in one move) when it must.
        void Morph(int width)
        {
            if (width > body.Width && IsHandleCreated)
            {
                int grow = width - body.Width;
                grow += grow & 1;
                body = new Size(body.Width + grow, body.Height);
                // Centered on the old capsule, but never past the edges of the screen.
                Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                double nx = Math.Max(wa.Left + P(8), Math.Min(wa.Right - body.Width - P(8), tx - grow / 2));
                x += nx - tx;
                tx = nx;
                Native.SetWindowPos(Handle, IntPtr.Zero, (int)Math.Round(x) - Pad, (int)Math.Round(y) - Pad, body.Width + 2 * Pad, body.Height + 2 * Pad,
                                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                ApplyPos();
            }
            cap.Go(width, 360, 0, Ease.OutCubic, null);
        }

        protected override bool StepExtra(double now)
        {
            bool repaint = cap.Running || fade.Running;
            cap.Step(now);
            fade.Step(now);
            bool shaking = false;
            if (nudgeAt >= 0)
            {
                double t = (now - nudgeAt) / 420.0;
                if (t >= 1 || moving || dragging) { nudgeAt = -1; if (!moving && !dragging) x = tx; }
                else { x = tx + Math.Sin(t * Math.PI * 6) * (1 - t) * P(7); shaking = true; }
                if (!moving) ApplyPos();
            }
            if (repaint && !IsDisposed) Redraw();
            return cap.Running || fade.Running || shaking;
        }

        // ---- painting ----

        static System.Windows.Rect R(Rectangle r) { return new System.Windows.Rect(r.X, r.Y, r.Width, r.Height); }

        // One soft shadow rendered once; its middle column stretches to any capsule width.
        void Shadow(WM.DrawingContext dc, double left, double width, int h)
        {
            if (shadowL == null)
            {
                shadowCore = P(64);
                int w = shadowCore + 2 * Pad, half = Pad + shadowCore / 2;
                WI.BitmapSource sh = Ink.Shadow(w, h, new System.Windows.Rect(Pad, Pad + P(5), shadowCore, body.Height), P(14), P(16), Ds.Argb(0.45, 0, 0, 0));
                shadowL = Crop(sh, 0, half - 2, h);
                shadowM = Crop(sh, half - 2, 4, h);
                shadowR = Crop(sh, half + 2, w - half - 2, h);
            }
            double lx = left - Pad, rx = left + width - shadowCore / 2 + 2;
            dc.DrawImage(shadowL, new System.Windows.Rect(lx, 0, shadowL.PixelWidth, h));
            dc.DrawImage(shadowR, new System.Windows.Rect(rx, 0, shadowR.PixelWidth, h));
            double mx = lx + shadowL.PixelWidth;
            if (rx > mx) dc.DrawImage(shadowM, new System.Windows.Rect(mx, 0, rx - mx, h));
        }

        static WI.BitmapSource Crop(WI.BitmapSource src, int x0, int w, int h)
        {
            WI.CroppedBitmap c = new WI.CroppedBitmap(src, new System.Windows.Int32Rect(x0, 0, w, h));
            c.Freeze();
            return c;
        }

        protected override void PaintSurface(WM.DrawingContext dc, int w, int h)
        {
            shown = Signature();
            double cw = Math.Round(cap.Value), cx = CapLeft();
            if (!Ds.Brushes.High) Shadow(dc, Pad + cx, cw, h);
            dc.PushTransform(new WM.TranslateTransform(Pad + cx, Pad));
            System.Windows.Rect b = new System.Windows.Rect(0, 0, cw, body.Height);
            Ink.Round(dc, Palette.HudBack(Ds.Argb(0.93, 30, 30, 32)), b, P(14));
            Ink.Hairline(dc, Palette.HudLine, b, P(14));
            dc.PushClip(new WM.RectangleGeometry(b, P(14), P(14)));
            dc.PushOpacity(Math.Max(0, Math.Min(1, fade.Value)));
            // The content keeps its final layout, centered: the capsule's edges reveal or hide it while it changes size.
            int fw = face == Phase.Rec ? recW : faceW;
            dc.PushTransform(new WM.TranslateTransform(Math.Round((cw - fw) / 2), 0));
            if (face == Phase.Rec) PaintRec(dc);
            else PaintPill(dc, fw);
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        void PaintRec(WM.DrawingContext dc)
        {
            double cy = body.Height / 2.0;
            WM.Color white = Palette.HudLabel, dim = Palette.HudLabel2;
            bool armed = armedAt >= 0;
            if (armed)
            {
                WM.FormattedText ht = Ink.Px(DiscardHint, Ds.Semibold, P(13), Palette.HudInk(Ds.Rgb(255, 138, 128)));
                dc.DrawText(ht, new System.Windows.Point(P(14), Math.Round(cy - ht.Height / 2)));
            }
            else if (paused) PaintPaused(dc, cy);
            else
            {
                dc.DrawEllipse(Ds.Brush(Palette.HudInk(Red)), null, new System.Windows.Point(P(20), cy), 4.5 * s, 4.5 * s);
                DrawClock(dc, Clock(session.Elapsed), timeX, cy);
                if (hasSys) PaintSource(dc, SourceRect(0), 0, session.SystemAudio.Muted ? "soundoff" : "sound", session.SystemAudio.Muted, hot == 3);
                if (hasMic) PaintSource(dc, SourceRect(hasSys ? 1 : 0), 1, session.Microphone.Muted ? "micoff" : "mic", session.Microphone.Muted, hot == 2);
                if (diskText != null)
                {
                    WM.FormattedText dt = Ink.Px(diskText, Ds.Semibold, P(12), Palette.HudInk(diskLevel >= 2 ? Ds.Rgb(255, 105, 97) : Ds.Rgb(255, 179, 64)));
                    dc.DrawText(dt, new System.Windows.Point(diskX, Math.Round(cy - dt.Height / 2)));
                }
            }
            PaintPauseButton(dc, cy);
            System.Windows.Rect sr = R(StopRect());
            Ink.Round(dc, Palette.HudFill(hot == 0 && pressed == 0 ? Ds.Rgb(222, 46, 36) : hot == 0 ? Ds.Rgb(255, 92, 82) : Red), sr, sr.Height / 2);
            double sq = 9 * s;
            Ink.Round(dc, Palette.OnHudFill(white), new System.Windows.Rect(Math.Round(sr.X + P(14)), Math.Round(cy - sq / 2), sq, sq), 2 * s);
            WM.FormattedText st = Ink.Px("Detener", Ds.Semibold, P(13), Palette.OnHudFill(white));
            dc.DrawText(st, new System.Windows.Point(Math.Round(sr.X + P(30)), Math.Round(cy - st.Height / 2)));
            System.Windows.Rect dr = R(DiscardRect());
            System.Windows.Point dcp = new System.Windows.Point(dr.X + dr.Width / 2, cy);
            if (armed) dc.DrawEllipse(Ds.Brush(Palette.HudFill(hot == 1 && pressed == 1 ? Ds.Rgb(222, 46, 36) : hot == 1 ? Ds.Rgb(255, 92, 82) : Red)), null, dcp, dr.Width / 2, dr.Width / 2);
            else if (hot == 1) dc.DrawEllipse(Ds.Brush(pressed == 1 ? Palette.HudWash(Ds.Argb(0.26, 255, 255, 255)) : Palette.HudHover), null, dcp, dr.Width / 2, dr.Width / 2);
            double gs = 15 * s;
            Glyph.Draw(dc, "trash", dcp.X - gs / 2, cy - gs / 2, gs, armed ? Palette.OnHudFill(white) : hot == 1 ? white : dim, Math.Max(1.4, 1.6 * s));
        }

        // Paused: an amber pause sign where the dot is and "En pausa"; then the disk warning, or else the stopped time
        // (dimmed) when it fits before the buttons.
        void PaintPaused(WM.DrawingContext dc, double cy)
        {
            double bw = 2.6 * s, bh = 9 * s, gap = 2.4 * s, mid = P(20);
            WM.Color amber = Palette.HudInk(Amber);
            Ink.Round(dc, amber, new System.Windows.Rect(mid - gap / 2 - bw, cy - bh / 2, bw, bh), 1.1 * s);
            Ink.Round(dc, amber, new System.Windows.Rect(mid + gap / 2, cy - bh / 2, bw, bh), 1.1 * s);
            WM.FormattedText pt = Ink.Px(PausedText, Ds.Semibold, P(12), amber);
            dc.DrawText(pt, new System.Windows.Point(timeX, Math.Round(cy - pt.Height / 2)));
            double x = timeX + Math.Ceiling(pt.WidthIncludingTrailingWhitespace) + P(8), end = pauseX - P(8);
            if (diskText != null)
            {
                WM.FormattedText dt = Ink.Px(diskText, Ds.Semibold, P(12), diskLevel >= 2 ? Palette.HudInk(Ds.Rgb(255, 105, 97)) : amber);
                dc.DrawText(dt, new System.Windows.Point(x, Math.Round(cy - dt.Height / 2)));
                return;
            }
            string time = Clock(session.Elapsed);
            if (x + ClockWidth(time) > end) return;
            dc.PushOpacity(0.55);
            DrawClock(dc, time, x, cy);
            dc.Pop();
        }

        // Pausar (two bars) or, while paused, Reanudar (a play sign on a lit circle).
        void PaintPauseButton(WM.DrawingContext dc, double cy)
        {
            System.Windows.Rect pr = R(PauseRect());
            System.Windows.Point pc = new System.Windows.Point(pr.X + pr.Width / 2, cy);
            bool lit = hot == 5;
            if (paused) dc.DrawEllipse(Ds.Brush(Palette.HudWash(lit && pressed == 5 ? Ds.Argb(0.34, 255, 255, 255) : lit ? Ds.Argb(0.28, 255, 255, 255) : Palette.HudHover)), HcPaint.Border(lit ? 2 : 1), pc, pr.Width / 2, pr.Width / 2);
            else if (lit) dc.DrawEllipse(Ds.Brush(pressed == 5 ? Palette.HudWash(Ds.Argb(0.26, 255, 255, 255)) : Palette.HudHover), null, pc, pr.Width / 2, pr.Width / 2);
            WM.Color ink = paused || lit ? Palette.HudLabel : Palette.HudLabel2;
            if (paused)
            {
                double gs = 15 * s;
                Glyph.Draw(dc, "play!", pc.X - gs / 2 + 0.6 * s, cy - gs / 2, gs, ink, 1);
                return;
            }
            double bw = 2.8 * s, bh = 11 * s, gap = 3.2 * s;
            Ink.Round(dc, ink, new System.Windows.Rect(pc.X - gap / 2 - bw, cy - bh / 2, bw, bh), 1.2 * s);
            Ink.Round(dc, ink, new System.Windows.Rect(pc.X + gap / 2, cy - bh / 2, bw, bh), 1.2 * s);
        }

        // An audio source: its icon and three bars that light up with the level; red and silent while muted.
        void PaintSource(WM.DrawingContext dc, Rectangle r, int i, string icon, bool muted, bool hover)
        {
            if (hover) Ink.Round(dc, pressed == (i == 0 ? 3 : 2) ? Palette.HudWash(Ds.Argb(0.26, 255, 255, 255)) : Palette.HudHover, R(r), P(8));
            double gs = 17 * s, cy = r.Y + r.Height / 2.0;
            Glyph.Draw(dc, icon, r.X + P(4), cy - gs / 2, gs, muted ? Palette.HudInk(Ds.Rgb(255, 105, 97)) : Palette.HudLabel, Math.Max(1.4, 1.6 * s));
            int lit = Bars(i);
            for (int k = 0; k < 3; k++)
            {
                double bh = P(4 + k * 4), bx = r.X + P(25) + k * 4.5 * s, by = cy + P(7) - bh;
                Ink.Round(dc, k < lit ? Palette.HudInk(Green) : Palette.HudFaint(Ds.Argb(muted ? 0.12 : 0.22, 255, 255, 255)), new System.Windows.Rect(bx, by, 2.5 * s, bh), 1.2 * s);
            }
        }

        void PaintPill(WM.DrawingContext dc, double cw)
        {
            double cy = body.Height / 2.0, ix = P(23), r = 8.5 * s, th = Math.Max(1.5, 2 * s);
            WM.Color white = Palette.HudLabel;
            bool ring = face == Phase.Progress && faceProgress >= 0;
            if (face == Phase.Done)
            {
                dc.DrawEllipse(Ds.Brush(Palette.HudFill(Green)), null, new System.Windows.Point(ix, cy), r + 1, r + 1);
                double gs = 13 * s;
                Glyph.Draw(dc, "check", ix - gs / 2, cy - gs / 2, gs, Palette.OnHudFill(white), Math.Max(1.8, 2.2 * s));
            }
            else if (ring) Ring(dc, ix, cy, r, th, faceProgress);
            else Spinner(dc, ix, cy, th);
            double right = cw - P(18);
            if (face == Phase.Progress)
            {
                Rectangle cr = CancelRect((int)cw);
                bool asking = armedAt >= 0 && phase == Phase.Progress;
                System.Windows.Rect crr = R(cr);
                if (asking) Ink.Round(dc, Palette.HudFill(hot == 4 && pressed == 4 ? Ds.Rgb(222, 46, 36) : hot == 4 ? Ds.Rgb(255, 92, 82) : Red), crr, crr.Height / 2);
                else if (hot == 4) Ink.Round(dc, pressed == 4 ? Palette.HudWash(Ds.Argb(0.26, 255, 255, 255)) : Palette.HudHover, crr, crr.Height / 2);
                WM.FormattedText ct = Ink.Px(asking ? CancelHint : CancelText, Ds.Semibold, P(12), asking ? Palette.OnHudFill(white) : hot == 4 ? white : Palette.HudLabel2);
                dc.DrawText(ct, new System.Windows.Point(Math.Round(crr.X + (crr.Width - ct.WidthIncludingTrailingWhitespace) / 2), Math.Round(cy - ct.Height / 2)));
                right = cr.X - P(10);
            }
            if (ring)
            {
                WM.FormattedText pt = Ink.Px((int)Math.Round(faceProgress * 100) + " %", Ds.Medium, P(12), Palette.HudLabel2);
                dc.DrawText(pt, new System.Windows.Point(Math.Round(right - pt.WidthIncludingTrailingWhitespace), Math.Round(cy - pt.Height / 2)));
                right -= PercentWidth() + P(12);
            }
            WM.FormattedText lt = Ink.Px(faceLabel, Ds.Semibold, P(13), white);
            lt.MaxTextWidth = Math.Max(1, right - PillIcon);
            lt.MaxLineCount = 1;
            lt.Trimming = System.Windows.TextTrimming.CharacterEllipsis;
            dc.DrawText(lt, new System.Windows.Point(PillIcon, Math.Round(cy - lt.Height / 2)));
        }

        // Determinate progress: a thin ring that fills clockwise from the top.
        static void Ring(WM.DrawingContext dc, double cx, double cy, double r, double th, double f)
        {
            WM.Pen track = new WM.Pen(Ds.Brush(Palette.HudFaint(Ds.Argb(0.2, 255, 255, 255))), th);
            track.Freeze();
            dc.DrawEllipse(null, track, new System.Windows.Point(cx, cy), r, r);
            f = Math.Max(0, Math.Min(1, f));
            if (f <= 0.005) return;
            WM.Pen pen = new WM.Pen(Ds.Brush(Palette.HudInk(Accent)), th);
            pen.StartLineCap = pen.EndLineCap = WM.PenLineCap.Round;
            pen.Freeze();
            if (f >= 0.995) { dc.DrawEllipse(null, pen, new System.Windows.Point(cx, cy), r, r); return; }
            double a = f * 2 * Math.PI;
            WM.StreamGeometry g = new WM.StreamGeometry();
            using (WM.StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new System.Windows.Point(cx, cy - r), false, false);
                c.ArcTo(new System.Windows.Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), new System.Windows.Size(r, r), 0, a > Math.PI,
                        WM.SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        // Unknown progress: the macOS spinner, eight spokes fading behind the leading one.
        void Spinner(WM.DrawingContext dc, double cx, double cy, double th)
        {
            const int n = 8;
            double r0 = 4 * s, r1 = 8.5 * s;
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n - Math.PI / 2;
                int age = ((spin - i) % n + n) % n;
                WM.Pen p = new WM.Pen(Ds.Brush(Ds.WithAlpha(Palette.HudLabel, Math.Max(0.2, 1 - age * 0.13))), th);
                p.StartLineCap = p.EndLineCap = WM.PenLineCap.Round;
                p.Freeze();
                dc.DrawLine(p, new System.Windows.Point(cx + Math.Cos(a) * r0, cy + Math.Sin(a) * r0), new System.Windows.Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1));
            }
        }

        // ---- mouse ----

        Point Local(Point p) { return new Point(p.X - Pad, p.Y - Pad); }

        bool OnCapsule(Point p)
        {
            int cx = CapLeft();
            return p.X >= cx && p.X < cx + Math.Round(cap.Value) && p.Y >= 0 && p.Y < body.Height;
        }

        // 0 Stop, 1 Discard, 2 microphone, 3 computer sound, 4 cancel the save, 5 pause or resume; -1 none.
        int HitTest(Point p)
        {
            if (phase == Phase.Progress && face == Phase.Progress && !cap.Running)
            {
                int cw = (int)Math.Round(cap.Value);
                Point c = new Point(p.X - CapLeft() - (int)Math.Round((cw - faceW) / 2.0), p.Y);
                return CancelRect(faceW).Contains(c) ? 4 : -1;
            }
            if (phase != Phase.Rec || face != Phase.Rec || cap.Running) return -1;
            Point q = new Point(p.X - CapLeft(), p.Y);
            if (StopRect().Contains(q)) return 0;
            if (DiscardRect().Contains(q)) return 1;
            if (PauseRect().Contains(q)) return 5;
            if (armedAt >= 0 || paused) return -1;
            if (hasMic && SourceRect(hasSys ? 1 : 0).Contains(q)) return 2;
            if (hasSys && SourceRect(0).Contains(q)) return 3;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Once it says "Guardado" (or is going) it is only leaving: nothing to press or drag.
            if (e.Button != MouseButtons.Left || phase == Phase.Gone || phase == Phase.Done) return;
            Tip(-1);
            Point p = Local(e.Location);
            pressed = HitTest(p);
            if (pressed >= 0) { Repaint(); return; }
            if (!OnCapsule(p)) return;
            dragging = true;
            dragFrom = PointToScreen(e.Location);
            dragX = moving ? tx : x;
            dragY = moving ? ty : y;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { Drag(PointToScreen(e.Location)); return; }
            int h = HitTest(Local(e.Location));
            if (h != hot)
            {
                hot = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Tip(h);
                Repaint();
            }
        }

        // Moves the bar with the mouse, keeping the capsule inside the screen's working area.
        void Drag(Point c)
        {
            if (phase == Phase.Done || phase == Phase.Gone) return;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int cx = CapLeft(), cw = (int)Math.Round(cap.Value);
            double nx = Math.Max(wa.Left - cx, Math.Min(wa.Right - cx - cw, dragX + c.X - dragFrom.X));
            double ny = Math.Max(wa.Top, Math.Min(wa.Bottom - body.Height, dragY + c.Y - dragFrom.Y));
            x = tx = nx;
            y = ty = ny;
            vx = vy = 0;
            moving = false;
            nudgeAt = -1;
            ApplyPos();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Point p = Local(e.Location);
            if (dragging)
            {
                dragging = false;
                hot = HitTest(p);
                Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
                Repaint();
                return;
            }
            int down = pressed;
            pressed = -1;
            if (down >= 0 && HitTest(p) == down) Act(down);
            if (!IsDisposed) Repaint();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            dragging = false;
            if (pressed >= 0) { pressed = -1; Repaint(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Tip(-1);
            if (hot != -1 && !dragging) { hot = -1; Repaint(); }
        }

        void Act(int what)
        {
            if (what == 4)
            {
                // Like Discard: a first click asks, a second one within a few seconds cancels.
                if (phase != Phase.Progress) return;
                if (armedAt >= 0) { armedAt = -1; session.CancelSave(); }
                else { armedAt = Anim.Now; Retime(); Repaint(); }
                return;
            }
            if (phase != Phase.Rec) return;
            if (what == 5) { armedAt = -1; session.TogglePause(); Retime(); Repaint(); return; }
            if (what == 0) session.Stop();
            else if (what == 1)
            {
                // A long recording is only thrown away on a second click.
                if (armedAt >= 0 || session.Elapsed.TotalMilliseconds < QuickDiscardMs) session.Cancel();
                else { armedAt = Anim.Now; Retime(); Repaint(); }
            }
            else if (what == 2) { session.Microphone.Muted = !session.Microphone.Muted; meter[1] = 0; Retime(); Repaint(); }
            else if (what == 3) { session.SystemAudio.Muted = !session.SystemAudio.Muted; meter[0] = 0; Retime(); Repaint(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Tip(-1);
            tick.Dispose();
            if (previous == this) previous = null;
        }
    }

    // Quiet frame around the recorded area: a fine translucent white line with white corner marks, on click-through
    // strips that never appear in the video. A faint dark edge keeps both visible on white content. A side on the edge
    // of its screen gets no strip: the screen's edge already marks it, and the strip would show on the next monitor.
    class FrameEdge : FloatWindow
    {
        readonly int side;     // 0 top, 1 bottom, 2 left, 3 right
        readonly int t, mark, k;
        readonly int lead, trail; // top and bottom strips: how far they reach past the area on the left and the right

        FrameEdge(Rectangle r, int side, int t, int mark, int k, int lead, int trail)
        {
            this.side = side;
            this.t = t;
            this.mark = mark;
            this.k = k;
            this.lead = lead;
            this.trail = trail;
            SetSize(r.Size);
            JumpTo(r.X, r.Y);
            ShowQuiet();
            alpha.Go(1, 180, 0, Ease.OutCubic, null);
            Anim.Wake(this);
        }

        protected override bool PerPixel { get { return true; } }
        protected override bool Rounded { get { return false; } }

        // Recording stopped: a quick fade, then gone (saving goes on without it).
        public void Dismiss()
        {
            if (IsDisposed) return;
            alpha.Go(0, 120, 0, Ease.OutCubic, delegate { if (!IsDisposed) Close(); });
            Anim.Wake(this);
        }

        // Paused: the frame fades back (nothing is being recorded); resumed: full again.
        public void Dim(bool on)
        {
            if (IsDisposed) return;
            alpha.Go(on ? 0.35 : 1, 200, 0, Ease.OutCubic, null);
            Anim.Wake(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT: clicks pass through
                return cp;
            }
        }

        protected override void PaintSurface(WM.DrawingContext dc, int w, int h)
        {
            WM.Brush line = Ds.Brush(Ds.Argb(0.6, 255, 255, 255)), halo = Ds.Brush(Ds.Argb(0.3, 0, 0, 0));
            WM.Brush white = Ds.Brush(Ds.Rgb(255, 255, 255)), edge = Ds.Brush(Ds.Argb(0.34, 0, 0, 0));
            // The line hugs the recorded area; the strip is t wide so the corner marks (k thick, o from the outside) fit.
            int o = t - k;
            if (side < 2)
            {
                bool top = side == 0;
                double ly = top ? t - 1 : 0, my = top ? o : 0, ey = top ? o - 1 : 0;
                dc.DrawRectangle(halo, null, new System.Windows.Rect(0, top ? ly - 1 : ly + 1, w, 1));
                dc.DrawRectangle(line, null, new System.Windows.Rect(lead, ly, w - lead - trail, 1));
                // Each mark covers the corner square when there is a side strip to meet, else it starts at the area.
                int a0 = Math.Max(0, lead - k), a1 = lead + mark, b0 = w - trail - mark, b1 = Math.Min(w, w - trail + k);
                int e0 = Math.Max(0, a0 - 1), e1 = Math.Min(w, b1 + 1);
                dc.DrawRectangle(edge, null, new System.Windows.Rect(e0, ey, a1 + 1 - e0, k + 1));
                dc.DrawRectangle(edge, null, new System.Windows.Rect(b0 - 1, ey, e1 - b0 + 1, k + 1));
                dc.DrawRectangle(white, null, new System.Windows.Rect(a0, my, a1 - a0, k));
                dc.DrawRectangle(white, null, new System.Windows.Rect(b0, my, b1 - b0, k));
            }
            else
            {
                bool left = side == 2;
                double lx = left ? t - 1 : 0, mx = left ? o : 0, ex = left ? o - 1 : 0;
                dc.DrawRectangle(halo, null, new System.Windows.Rect(left ? lx - 1 : lx + 1, 0, 1, h));
                dc.DrawRectangle(line, null, new System.Windows.Rect(lx, 0, 1, h));
                dc.DrawRectangle(edge, null, new System.Windows.Rect(ex, 0, k + 1, mark + 1));
                dc.DrawRectangle(edge, null, new System.Windows.Rect(ex, h - mark - 1, k + 1, mark + 1));
                dc.DrawRectangle(white, null, new System.Windows.Rect(mx, 0, k, mark));
                dc.DrawRectangle(white, null, new System.Windows.Rect(mx, h - mark, k, mark));
            }
        }

        public static FrameEdge[] Around(Rectangle a)
        {
            float s = ShotStack.ScaleFor(Screen.FromRectangle(a));
            int t = Math.Max(4, (int)Math.Round(4 * s)), mark = (int)Math.Round(18 * s), k = Math.Max(3, Math.Min(t - 1, (int)Math.Round(3 * s)));
            bool top = !OnScreenEdge(a, 0), bottom = !OnScreenEdge(a, 1), left = !OnScreenEdge(a, 2), right = !OnScreenEdge(a, 3);
            int l = left ? t : 0, r = right ? t : 0;
            List<FrameEdge> strips = new List<FrameEdge>();
            if (top) strips.Add(new FrameEdge(new Rectangle(a.X - l, a.Y - t, a.Width + l + r, t), 0, t, mark, k, l, r));
            if (bottom) strips.Add(new FrameEdge(new Rectangle(a.X - l, a.Bottom, a.Width + l + r, t), 1, t, mark, k, l, r));
            if (left) strips.Add(new FrameEdge(new Rectangle(a.X - t, a.Y, t, a.Height), 2, t, mark, k, 0, 0));
            if (right) strips.Add(new FrameEdge(new Rectangle(a.Right, a.Y, t, a.Height), 3, t, mark, k, 0, 0));
            return strips.ToArray();
        }

        // Whether that side of the area lies on (or past) the edge of the screen its middle is on.
        static bool OnScreenEdge(Rectangle a, int side)
        {
            Point p = side == 0 ? new Point(a.X + a.Width / 2, a.Y) : side == 1 ? new Point(a.X + a.Width / 2, a.Bottom - 1)
                    : side == 2 ? new Point(a.X, a.Y + a.Height / 2) : new Point(a.Right - 1, a.Y + a.Height / 2);
            Rectangle b = Screen.FromPoint(p).Bounds;
            if (!b.Contains(p)) return false;
            return side == 0 ? a.Top <= b.Top : side == 1 ? a.Bottom >= b.Bottom : side == 2 ? a.Left <= b.Left : a.Right >= b.Right;
        }
    }
}

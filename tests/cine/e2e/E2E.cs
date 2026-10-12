// End-to-end check of a real Cinema recording: the real ShotStack + Recorder.Start/Stop, over a window that shows
// one known image per display refresh (frame id in blocks). Afterwards the master is decoded and every frame is
// compared with the image that id must have, and the id sequence tells whether refreshes were lost or repeated.
// usage: E2E.exe <name> <w> <h> <seconds> [sys] [throttle] [cancel] [mic]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Stackshot.E2E
{
    static class Program2
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);
        static List<Process> suspended = new List<Process>();

        static bool suspend;
        static bool resumedDone;
        static Stopwatch suspendWatch = new Stopwatch();
        static readonly List<string> dialogs = new List<string>();
        static readonly StringBuilder log = new StringBuilder();
        static void Say(string s) { Console.WriteLine(s); log.AppendLine(s); }

        // Closes (and records) any message box of ours, so a failure never leaves the run waiting.
        static void CloseDialogs()
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != me || !IsWindowVisible(h)) return true;
                StringBuilder c = new StringBuilder(64);
                GetClassName(h, c, 64);
                if (c.ToString() != "#32770") return true;
                StringBuilder all = new StringBuilder();
                EnumChildWindows(h, delegate(IntPtr ch, IntPtr q)
                {
                    StringBuilder t = new StringBuilder(4096);
                    GetWindowText(ch, t, 4096);
                    if (t.Length > 0) all.Append(t.ToString()).Append(" | ");
                    return true;
                }, IntPtr.Zero);
                lock (dialogs) dialogs.Add(all.ToString());
                PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                return true;
            }, IntPtr.Zero);
        }

        static List<Process> FfmpegChildren()
        {
            List<Process> l = new List<Process>();
            int me = Process.GetCurrentProcess().Id;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name='ffmpeg.exe' AND ParentProcessId=" + me))
                foreach (ManagementObject o in s.Get())
                    try { l.Add(Process.GetProcessById(Convert.ToInt32(o["ProcessId"]))); } catch { }
            return l;
        }

        [STAThread]
        static int Main(string[] args)
        {
            // Never against the user's own data folder (run-e2e.ps1 sets a private one).
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA"))) return 99;
            string name = args[0];
            int w = int.Parse(args[1]), h = int.Parse(args[2]);
            double seconds = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            bool sys = Array.IndexOf(args, "sys") >= 0, throttle = Array.IndexOf(args, "throttle") >= 0, cancel = Array.IndexOf(args, "cancel") >= 0;
            bool mic = Array.IndexOf(args, "mic") >= 0;
            suspend = Array.IndexOf(args, "suspend") >= 0;
            string outDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "out-" + name);
            Directory.CreateDirectory(outDir);
            ShotStack.LogPath = Path.Combine(outDir, "stackshot.log");
            Stackshot.Program.Init();
            ShotStack.Test = true;
            Settings.ReadOnly = true;

            Settings s = new Settings();
            s.VideoQuality = Recorder.Cinema;
            foreach (string a in args) if (a.StartsWith("q=")) s.VideoQuality = int.Parse(a.Substring(2));
            Pattern.Lossy = s.VideoQuality != Recorder.Cinema;
            s.RecordSystemAudio = sys;
            s.RecordMic = mic;
            s.Webcam = 0;
            // No options window; the countdown only when asked for (countdown=N).
            s.RecordSetup = false;
            s.RecordCountdown = 0;
            foreach (string a in args) if (a.StartsWith("countdown=")) s.RecordCountdown = int.Parse(a.Substring(10));
            s.CopyToClipboard = false;
            s.MascotOn = false;
            s.ShowIntro = false;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Rectangle area = new Rectangle(wa.Left + 40, wa.Bottom - h - 90, w, h);
            if (area.X < wa.X) area.X = wa.X;
            if (area.Y < wa.Y) area.Y = wa.Y;
            bool full = Array.IndexOf(args, "full") >= 0, desktop = Array.IndexOf(args, "desktop") >= 0;
            // full: the whole primary screen; desktop: whatever is on screen (no pattern, only the verification counts)
            if (full || desktop) area = Screen.PrimaryScreen.Bounds;
            Say("E2E " + name + ": area " + area + " for " + seconds + " s" + (sys ? " +sys" : "") + (mic ? " +mic" : "") + (throttle ? " throttled" : "") + (cancel ? " cancel" : ""));

            LayeredPattern win = new LayeredPattern(area);
            ShotStack owner = new ShotStack(s, false, false);
            DateTime started = DateTime.Now;
            int phase = 0;
            Stopwatch sw = new Stopwatch();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 100;
            t.Tick += delegate
            {
                CloseDialogs();
                if (phase == 0 && sw.ElapsedMilliseconds == 0) { if (!desktop) { win.Show(); win.Begin(); } sw.Start(); return; }
                if (phase == 0 && sw.ElapsedMilliseconds > 1500)
                {
                    phase = 1;
                    started = DateTime.Now;
                    Recorder.Start(owner, s, area, false);
                    sw.Restart();
                    return;
                }
                if (phase == 1 && suspend && suspended.Count == 0 && sw.ElapsedMilliseconds > 1500)
                {
                    suspendWatch.Start(); foreach (Process p in FfmpegChildren()) { NtSuspendProcess(p.Handle); suspended.Add(p); Say("suspended ffmpeg " + p.Id); }
                }
                if (suspended.Count > 0 && !resumedDone && suspendWatch.ElapsedMilliseconds > 14000)
                {
                    foreach (Process p in suspended) { NtResumeProcess(p.Handle); Say("resumed ffmpeg " + p.Id); }
                    resumedDone = true;
                }
                if (phase == 1 && throttle && sw.ElapsedMilliseconds > 1500 && sw.ElapsedMilliseconds < 1700)
                {
                    foreach (Process p in FfmpegChildren())
                    {
                        try { p.ProcessorAffinity = new IntPtr(1); p.PriorityClass = ProcessPriorityClass.Idle; Say("throttled ffmpeg " + p.Id); } catch (Exception ex) { Say("throttle: " + ex.Message); }
                    }
                }
                if (phase == 1 && (sw.ElapsedMilliseconds > seconds * 1000 || !Recorder.Recording))
                {
                    phase = 2;
                    if (Recorder.Recording)
                    {
                        if (cancel)
                        {
                            object cur = typeof(Recorder).GetField("current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                            cur.GetType().GetMethod("Cancel").Invoke(cur, null);
                        }
                        else Recorder.Stop();
                    }
                    sw.Restart();
                    return;
                }
                if (phase == 2 && (!Recorder.Recording || sw.ElapsedMilliseconds > 600000))
                {
                    phase = 3;
                    t.Stop();
                    if (!desktop) { win.End(); win.Close(); }
                    Say("saving took " + sw.ElapsedMilliseconds + " ms; pattern images shown: " + 0);
                    owner.ExitThread();
                }
            };
            t.Start();
            Application.Run(owner);
            CloseDialogs();
            lock (dialogs) foreach (string d in dialogs) Say("DIALOG: " + d);
            Thread.Sleep(500);
            int orphans = FfmpegChildren().Count;
            Say("ffmpeg children left: " + orphans);

            int fails = orphans > 0 ? 1 : 0;
            string master = Newest(Settings.TempDir, Pattern.Lossy ? "V*deo *.mp4" : "Cine *.mkv", started);
            string report = Newest(Path.Combine(Settings.DataDir, "cine"), "*.json", started);
            string[] work = Directory.GetFiles(Settings.TempDir, "~*");
            List<string> fresh = new List<string>();
            foreach (string f in work) if (File.GetLastWriteTime(f) >= started) fresh.Add(Path.GetFileName(f));
            Say("work files left: " + (fresh.Count == 0 ? "none" : string.Join(", ", fresh.ToArray())));
            if (fresh.Count > 0) fails++;
            if (cancel)
            {
                Say(master == null ? "PASS cancel: no master kept" : "FAIL cancel: master kept " + master);
                if (master != null) fails++;
                File.WriteAllText(Path.Combine(outDir, "e2e.txt"), log.ToString());
                return fails;
            }
            if (report != null)
            {
                File.Copy(report, Path.Combine(outDir, "report.json"), true);
                Dictionary<string, object> rep = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(report));
                foreach (KeyValuePair<string, object> kv in rep)
                    if (!(kv.Value is System.Collections.ArrayList) && !(kv.Value is Dictionary<string, object>)) Say("report." + kv.Key + " = " + kv.Value);
                if (!true.Equals(rep["passed"])) { Say("FAIL: verification did not pass"); fails++; }
                bool expectHalt = Array.IndexOf(args, "suspend") >= 0;
                if (!expectHalt && !true.Equals(rep["captureClean"])) { Say("FAIL: capture not clean"); fails++; }
                if (expectHalt && rep["stopReason"] == null) { Say("FAIL: overload did not stop the recording"); fails++; }
                if (s.RecordCountdown > 0)
                {
                    // Recording starts when the countdown ends: the take is that much shorter than the time until Stop.
                    double rec = Convert.ToDouble(rep["recordSeconds"]), expect = seconds - s.RecordCountdown;
                    bool ok = rec > expect - 1.5 && rec < expect + 0.8;
                    Say((ok ? "PASS" : "FAIL") + " countdown " + s.RecordCountdown + " s: recorded " + rec.ToString("0.00") + " s of " + seconds + " s (expected about " + expect + ")");
                    if (!ok) fails++;
                }
            }
            else if (!Pattern.Lossy) { Say("FAIL: no report"); fails++; }
            if (master == null) { Say("FAIL: no master"); return fails + 1; }
            Say("master: " + master + " (" + new FileInfo(master).Length / 1048576.0 + " MB)");
            if (!desktop) fails += Inspect(master, area.Width, area.Height);
            else Say("desktop recording: content not compared with a pattern; the verification above is the check");
            File.WriteAllText(Path.Combine(outDir, "e2e.txt"), log.ToString());
            if (Array.IndexOf(args, "keep") < 0) { try { File.Delete(master); } catch { } }
            return fails;
        }

        static string Newest(string dir, string pattern, DateTime after)
        {
            if (!Directory.Exists(dir)) return null;
            string best = null;
            DateTime bt = DateTime.MinValue;
            foreach (string f in Directory.GetFiles(dir, pattern))
            {
                DateTime t = File.GetLastWriteTime(f);
                if (t >= after && t > bt) { bt = t; best = f; }
            }
            return best;
        }

        // Decodes the master and checks each frame against the pattern of the id it shows.
        static int Inspect(string master, int w, int h)
        {
            string ffmpeg = Path.Combine(Settings.FfmpegDir, "ffmpeg.exe");
            Process p = new Process();
            p.StartInfo.FileName = ffmpeg;
            p.StartInfo.Arguments = "-hide_banner -nostdin -loglevel error -i \"" + master + "\" -map 0:v:0 -f rawvideo -pix_fmt gbrp -";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            Stream o = p.StandardOutput.BaseStream;
            int n = w * h;
            byte[] frame = new byte[n * 3], g = new byte[n], b = new byte[n], r = new byte[n], exp = new byte[n * 4];
            long frames = 0, exact = 0, transitions = 0, step1 = 0, repeats = 0, jumps = 0, back = 0, lostImages = 0, cursorish = 0;
            int prev = -1;
            List<string> odd = new List<string>();
            int boxes = 0;
            while (true)
            {
                int got = 0;
                while (got < frame.Length) { int k = o.Read(frame, got, frame.Length - got); if (k <= 0) break; got += k; }
                if (got < frame.Length) break;
                Buffer.BlockCopy(frame, 0, g, 0, n);
                Buffer.BlockCopy(frame, n, b, 0, n);
                Buffer.BlockCopy(frame, 2 * n, r, 0, n);
                int id = Pattern.Read(b, g, r, w);
                if (id < 0) { transitions++; frames++; continue; }
                Pattern.Draw(id, w, h, exp);
                long diff = 0; int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int i = 0; i < n; i++) if (exp[i * 4] != b[i] || exp[i * 4 + 1] != g[i] || exp[i * 4 + 2] != r[i]) { diff++; int xx = i % w, yy = i / w; if (xx < x0) x0 = xx; if (xx > x1) x1 = xx; if (yy < y0) y0 = yy; if (yy > y1) y1 = yy; }
                if (diff > 0 && boxes < 3 && !Pattern.Lossy) { boxes++; Say("  frame " + frames + ": " + diff + " px differ in box x " + x0 + ".." + x1 + ", y " + y0 + ".." + y1); }
                if (diff == 0) exact++;
                else if (diff < 4096) cursorish++;
                else if (odd.Count < 5) odd.Add("frame " + frames + " id " + id + ": " + diff + " px differ");
                if (prev >= 0)
                {
                    int d = (id - prev) & 0xFFFF;
                    if (d == 1) step1++;
                    else if (d == 0) repeats++;
                    else if (d < 0x8000) { jumps++; lostImages += d - 1; }
                    else { back++; if (odd.Count < 8) odd.Add("backwards at frame " + frames + ": id " + prev + " -> " + id); }
                }
                prev = id;
                frames++;
            }
            p.WaitForExit();
            Say("decoded frames: " + frames + " (exit " + p.ExitCode + ")");
            Say("capture fidelity: " + exact + " frames identical to the drawn image, " + cursorish + " with <4096 px different (cursor), " + transitions + " unreadable ids");
            foreach (string s in odd) Say("  " + s);
            Say("refresh sequence: +1 " + step1 + ", repeated " + repeats + ", jumps " + jumps + " (" + lostImages + " images skipped), backwards " + back);
            int fails = 0;
            if (p.ExitCode != 0 || frames == 0) fails++;
            if (odd.Count > 0 && !Pattern.Lossy) fails++;
            if (back > 0) fails++;
            return fails;
        }
    }
}

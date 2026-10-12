// Reliability suite: nothing recorded is ever lost when saving fails, stalls, is cancelled or Stackshot quits; disk and
// "kept up" estimates; Cine options. Uses a fake FFmpeg compiled here, so it needs no screen and no real FFmpeg (the
// last test, which checks the overlapped verifier, does).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static string fakeFfmpeg;

        // A stand-in for ffmpeg.exe whose behaviour comes from STACKSHOT_FAKE_FFMPEG: ok, fail, partial, hang, sleep:<ms>.
        const string FakeSource = @"
using System;
using System.IO;
using System.Threading;
static class Fake
{
    static int Main(string[] a)
    {
        string mode = Environment.GetEnvironmentVariable(""STACKSHOT_FAKE_FFMPEG"") ?? ""ok"";
        string log = Environment.GetEnvironmentVariable(""STACKSHOT_FAKE_LOG"");
        if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, string.Join("" "", a) + Environment.NewLine);
        string output = a.Length > 0 ? a[a.Length - 1] : ""-"";
        bool file = output != ""-"" && output.IndexOf(':') > 0;
        if (mode.StartsWith(""sleep:"")) { Thread.Sleep(int.Parse(mode.Substring(6))); mode = ""ok""; }
        if (mode == ""hang"") Thread.Sleep(Timeout.Infinite);
        if (mode == ""fail"") { Console.Error.WriteLine(""Simulated failure: No space left on device""); return 1; }
        if (mode == ""partial"")
        {
            if (file) File.WriteAllBytes(output, new byte[1000]);
            Console.Error.WriteLine(""Simulated failure: Error writing trailer"");
            return 1;
        }
        Console.WriteLine(""out_time_us=500000"");
        Console.WriteLine(""progress=end"");
        if (file) File.WriteAllBytes(output, new byte[] { 1, 2, 3, 4 });
        return 0;
    }
}";

        static string FakeFfmpeg()
        {
            if (fakeFfmpeg != null && File.Exists(fakeFfmpeg)) return fakeFfmpeg;
            string dir = Path.Combine(workRoot, "fake");
            Directory.CreateDirectory(dir);
            string src = Path.Combine(dir, "Fake.cs"), exe = Path.Combine(dir, "fake-ffmpeg.exe");
            File.WriteAllText(src, FakeSource);
            string csc = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "csc.exe");
            string err;
            int code = RunProcess(csc, "/nologo /target:exe /out:" + Q(exe) + " " + Q(src), null, out err);
            if (code != 0 || !File.Exists(exe)) throw new TestFailed("no compila el FFmpeg falso: " + err);
            fakeFfmpeg = exe;
            return exe;
        }

        static void Fake(string mode)
        {
            Environment.SetEnvironmentVariable("STACKSHOT_FAKE_FFMPEG", mode);
        }

        static string MakeFile(string dir, string name, int bytes)
        {
            string p = Path.Combine(dir, name);
            File.WriteAllBytes(p, new byte[bytes]);
            return p;
        }

        // A stopped recording ready to be saved: work files in dir, rescue folder under dir.
        sealed class Take
        {
            public Session Session;
            public string Dir, Video, Sys, Mic, Output, Stamp, Rescue;
            public Settings Settings;
        }

        static Take NewTake(string name, int quality, bool gif, bool sound, bool direct, string liveError)
        {
            Take t = new Take();
            t.Dir = WorkDir(name);
            t.Stamp = "test " + name;
            t.Rescue = Path.Combine(t.Dir, "recuperado");
            Recorder.RescueRoot = t.Rescue;
            t.Settings = new Settings();
            t.Settings.VideoQuality = quality;
            t.Settings.Webcam = 0;
            t.Settings.SaveFolder = Path.Combine(t.Dir, "guardado");
            t.Output = Path.Combine(t.Dir, gif ? "GIF " + t.Stamp + ".gif" : "Video " + t.Stamp + ".mp4");
            t.Video = direct ? t.Output : MakeFile(t.Dir, "~grabando " + t.Stamp + ".nut", 4096);
            if (direct) File.WriteAllBytes(t.Output, new byte[2048]);
            if (sound)
            {
                t.Sys = MakeFile(t.Dir, "~grabando " + t.Stamp + " equipo.f32", 8000);
                t.Mic = MakeFile(t.Dir, "~grabando " + t.Stamp + " micro.f32", 8000);
            }
            t.Session = new Session(null, t.Settings, FakeFfmpeg(), new Rectangle(0, 0, 64, 48), gif, 60);
            t.Session.PrepareForTest(t.Video, t.Sys, t.Mic, t.Output, 120, 60, 1, direct, t.Stamp, liveError);
            return t;
        }

        static string RescuedDir(Take t) { return Path.Combine(t.Rescue, t.Stamp); }

        static void CheckRescued(Take t, string[] names)
        {
            string dir = RescuedDir(t);
            foreach (string n in names)
                Check(File.Exists(Path.Combine(dir, n)), "no se ha conservado " + n + " en " + dir);
            string e = t.Session.ErrorText ?? "";
            Check(e.Contains("Lo grabado no se ha borrado") && e.Contains(dir), "el mensaje no dice d\u00F3nde est\u00E1 lo grabado: " + OneLine(e));
            Check(t.Session.RescueDir == dir, "RescueDir = " + t.Session.RescueDir);
        }

        static void CheckNothingLeft(Take t)
        {
            foreach (string f in new[] { t.Video, t.Sys, t.Mic })
                if (f != null && f != t.Output) Check(!File.Exists(f), "queda un archivo de trabajo: " + f);
        }

        static List<int> FakeChildren()
        {
            return ChildPids(Process.GetCurrentProcess().Id, "fake-ffmpeg.exe");
        }

        static void ReliabilitySuite()
        {
            string savedRoot = Recorder.RescueRoot;
            try { ReliabilityTests(); }
            finally
            {
                Recorder.RescueRoot = savedRoot;
                Environment.SetEnvironmentVariable("STACKSHOT_FAKE_FFMPEG", null);
            }
        }

        static void ReliabilityTests()
        {
            Test("reliability", "gif-final-pass-fails-keeps-work-file", () =>
            {
                Fake("fail");
                Take t = NewTake("gif-fail", Recorder.Standard, true, false, false, null);
                t.Session.SaveFinal(2.0);
                string e = t.Session.ErrorText ?? "";
                Check(e.StartsWith("No se pudo crear el GIF."), "error inesperado: " + OneLine(e));
                Check(e.Contains("No space left on device"), "el error no incluye lo que dijo FFmpeg: " + OneLine(e));
                CheckRescued(t, new[] { Path.GetFileName(t.Video) });
                Check(!File.Exists(t.Output), "qued\u00F3 un GIF a medias en temp");
                Note(OneLine(e));
            });

            Test("reliability", "maxima-with-sound-fails-keeps-video-and-sound", () =>
            {
                Fake("fail");
                Take t = NewTake("max-fail", Recorder.Maximum, false, true, false, null);
                t.Session.SaveFinal(2.0);
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Sys), Path.GetFileName(t.Mic) });
                CheckNothingLeft(t);
            });

            Test("reliability", "alta-with-sound-fails-keeps-video-and-sound", () =>
            {
                Fake("fail");
                Take t = NewTake("high-fail", Recorder.High, false, true, false, null);
                t.Session.SaveFinal(2.0);
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Sys), Path.GetFileName(t.Mic) });
            });

            Test("reliability", "partial-output-is-kept-too", () =>
            {
                Fake("partial");
                Take t = NewTake("max-partial", Recorder.Maximum, false, false, false, null);
                t.Session.SaveFinal(2.0);
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Output) });
                Check(!File.Exists(t.Output), "la salida a medias sigue en temp");
            });

            Test("reliability", "direct-mp4-live-failure-keeps-file", () =>
            {
                Fake("ok");
                Take t = NewTake("std-direct", Recorder.Standard, false, false, true, "FFmpeg no pudo guardar el v\u00EDdeo. simulado");
                t.Session.SaveFinal(2.0);
                CheckRescued(t, new[] { Path.GetFileName(t.Output) });
            });

            Test("reliability", "success-saves-and-cleans", () =>
            {
                Fake("ok");
                Take t = NewTake("high-ok", Recorder.High, false, true, false, null);
                t.Session.SaveFinal(2.0);
                Check(t.Session.ErrorText == null, "error: " + t.Session.ErrorText);
                Check(File.Exists(t.Output), "no hay salida");
                CheckNothingLeft(t);
                Check(!Directory.Exists(RescuedDir(t)), "se ha creado recuperado sin motivo");
            });

            Test("reliability", "watchdog-ends-a-stuck-pass", () =>
            {
                Fake("hang");
                string ff = FakeFfmpeg();
                System.Text.StringBuilder errs = new System.Text.StringBuilder();
                Stopwatch clock = Stopwatch.StartNew();
                int code;
                using (PassWatch w = new PassWatch(1500))
                {
                    code = Recorder.RunPass(ff, "-i x -f null -", 10, w, null, errs);
                    Check(w.Stalled, "el vigilante no marc\u00F3 la pasada como parada");
                    Check(!w.Cancelled, "Cancelled sin cancelar");
                }
                Check(code != 0, "una pasada parada devolvi\u00F3 0");
                Check(clock.Elapsed.TotalSeconds < 15, "tard\u00F3 " + clock.Elapsed.TotalSeconds + " s en cortar la pasada");
                Check(FakeChildren().Count == 0, "queda el proceso colgado");
                Note("cortada en " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s");
            });

            Test("reliability", "cancel-ends-a-pass-at-once", () =>
            {
                Fake("hang");
                string ff = FakeFfmpeg();
                System.Text.StringBuilder errs = new System.Text.StringBuilder();
                using (PassWatch w = new PassWatch(PassWatch.DefaultStallMs))
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    Timer cancel = new Timer(delegate { w.Cancel(); }, null, 800, Timeout.Infinite);
                    int code = Recorder.RunPass(ff, "-i x -f null -", 10, w, null, errs);
                    cancel.Dispose();
                    Check(w.Cancelled && !w.Stalled, "Cancelled=" + w.Cancelled + " Stalled=" + w.Stalled);
                    Check(code != 0, "una pasada cancelada devolvi\u00F3 0");
                    Check(clock.Elapsed.TotalSeconds < 8, "tard\u00F3 " + clock.Elapsed.TotalSeconds + " s");
                }
            });

            Test("reliability", "cancel-save-keeps-everything", () =>
            {
                Fake("hang");
                Take t = NewTake("cancel-save", Recorder.Maximum, false, true, false, null);
                t.Session.BeginSavingForTest(2.0);
                Thread.Sleep(1500);
                t.Session.CancelSave();
                Check(t.Session.WaitSavedForTest(20000), "el guardado no termin\u00F3 tras cancelar");
                string e = t.Session.ErrorText ?? "";
                Check(e.StartsWith(PassWatch.CancelledText), "mensaje: " + OneLine(e));
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Sys), Path.GetFileName(t.Mic) });
                Check(FakeChildren().Count == 0, "queda el proceso de la pasada");
            });

            Test("reliability", "shutdown-waits-and-saves", () =>
            {
                Fake("sleep:1500");
                Take t = NewTake("shutdown-ok", Recorder.Maximum, false, false, false, null);
                Recorder.SetCurrentForTest(t.Session);
                t.Session.BeginSavingForTest(2.0);
                Check(Recorder.Busy, "Busy deber\u00EDa ser true mientras guarda");
                Stopwatch clock = Stopwatch.StartNew();
                bool ok = Recorder.Shutdown(30000);
                Check(ok, "Shutdown devolvi\u00F3 false: " + OneLine(t.Session.ErrorText ?? ""));
                Check(clock.ElapsedMilliseconds >= 1000, "no esper\u00F3 al guardado (" + clock.ElapsedMilliseconds + " ms)");
                Check(!Recorder.Busy && !Recorder.Recording, "sigue ocupado tras Shutdown");
                string saved = Path.Combine(t.Settings.SaveFolder, Path.GetFileName(t.Output));
                Check(File.Exists(saved), "no est\u00E1 en la carpeta de guardado: " + saved);
                Check(!File.Exists(t.Output), "sigue en temp");
                CheckNothingLeft(t);
                Note("esper\u00F3 " + clock.ElapsedMilliseconds + " ms");
            });

            Test("reliability", "shutdown-timeout-rescues", () =>
            {
                Fake("hang");
                Take t = NewTake("shutdown-hang", Recorder.Maximum, false, true, false, null);
                Recorder.SetCurrentForTest(t.Session);
                t.Session.BeginSavingForTest(2.0);
                Stopwatch clock = Stopwatch.StartNew();
                bool ok = Recorder.Shutdown(1500);
                Check(!ok, "Shutdown dijo que todo estaba guardado con la pasada colgada");
                Check(clock.Elapsed.TotalSeconds < 20, "Shutdown tard\u00F3 " + clock.Elapsed.TotalSeconds + " s");
                Check(!Recorder.Busy, "sigue ocupado tras Shutdown");
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Sys), Path.GetFileName(t.Mic) });
                Check(FakeChildren().Count == 0, "queda el proceso colgado");
            });

            Test("reliability", "shutdown-with-nothing-running", () =>
            {
                Recorder.SetCurrentForTest(null);
                Check(Recorder.Shutdown(10), "sin grabaci\u00F3n Shutdown debe devolver true");
                Check(!Recorder.Busy, "Busy sin grabaci\u00F3n");
            });

            Test("reliability", "disk-estimate", () =>
            {
                long gib = 1L << 30, free = 20 * gib;
                DiskBudget.Estimate std = DiskBudget.For(Recorder.Standard, false, 1920, 1080, 30, 0, free);
                DiskBudget.Estimate high = DiskBudget.For(Recorder.High, false, 1920, 1080, 60, 1, free);
                DiskBudget.Estimate max = DiskBudget.For(Recorder.Maximum, false, 1920, 1080, 60, 1, free);
                DiskBudget.Estimate cine = DiskBudget.For(Recorder.Cinema, false, 1920, 1080, 60, 2, free);
                DiskBudget.Estimate gif = DiskBudget.For(Recorder.Standard, true, 1280, 720, 30, 0, free);
                Check(std.TypicalPerSecond < high.TypicalPerSecond && high.TypicalPerSecond < max.TypicalPerSecond && max.TypicalPerSecond < cine.TypicalPerSecond,
                      "el orden de tama\u00F1os no es Est\u00E1ndar < Alta < M\u00E1xima < Cine");
                foreach (DiskBudget.Estimate e in new[] { std, high, max, cine, gif })
                {
                    Check(e.HeavyPerSecond > e.TypicalPerSecond, "el peor caso no es mayor que el normal");
                    Check(e.HeavyMinutes < e.TypicalMinutes, "los minutos del peor caso no son menos");
                }
                // Estandar 1080p30 without sound: 0.003 B/px * 62.2 Mpx/s, no final pass.
                double expect = 1920.0 * 1080 * 30 * 0.003;
                Check(Math.Abs(std.TypicalPerSecond - expect) < 1, "Est\u00E1ndar sin sonido: " + std.TypicalPerSecond + " B/s, esperado " + expect);
                Check(Math.Abs(std.TypicalMinutes - (free - DiskBudget.Reserve) / expect / 60) < 0.01, "minutos de Est\u00E1ndar");
                // Cine keeps its own reserve and copies the sound into the master (two originals + the mix).
                double cineExpect = 1920.0 * 1080 * 60 * 0.06 * 2.05 + 48000 * 8 * 5;
                Check(Math.Abs(cine.TypicalPerSecond - cineExpect) < 1, "Cine: " + cine.TypicalPerSecond + " B/s, esperado " + cineExpect);
                Check(DiskBudget.For(Recorder.Cinema, false, 1920, 1080, 60, 0, Cine.ReserveBytes).TypicalMinutes == 0, "sin sitio sobre la reserva: 0 min");
                // While recording: Cine needs exactly what it needed before; direct MP4 only the reserve.
                Check(DiskBudget.Need(true, true, false, 1000000, 5000) == Cine.ReserveBytes + 1050000 + 5000, "Need de Cine cambi\u00F3");
                Check(DiskBudget.Need(false, false, true, 1000000, 0) == DiskBudget.Reserve, "Need de MP4 directo");
                Check(DiskBudget.Need(false, true, false, 1000000, 8000) == DiskBudget.Reserve + 500000 + 1000, "Need de M\u00E1xima");
                Check(DiskBudget.Need(false, false, false, 1000000, 8000) == DiskBudget.Reserve + 1050000 + 1000, "Need de Alta con sonido");
                Check(double.IsPositiveInfinity(DiskBudget.MinutesLeft(gib, 0, 0)), "sin ritmo: infinito");
                Check(Math.Abs(DiskBudget.MinutesLeft(gib + 600 * 1000000L, gib, 1000000) - 10) < 1e-9, "MinutesLeft");
                Check(DiskBudget.MinutesLeft(gib, 2 * gib, 1000) == 0, "MinutesLeft por debajo de lo necesario");
                Check(DiskBudget.Level(30, 0) == 0 && DiskBudget.Level(9, 0) == 1 && DiskBudget.Level(2.5, 1) == 2, "Level b\u00E1sico");
                Check(DiskBudget.Level(11, 1) == 1 && DiskBudget.Level(13, 1) == 0 && DiskBudget.Level(3.5, 2) == 2 && DiskBudget.Level(5, 2) == 1, "Level con hist\u00E9resis");
                Check(DiskBudget.Chip(6.7) == "~6 min de disco", "Chip: " + DiskBudget.Chip(6.7));
                Check(DiskBudget.Range(4.2, 40.9) == "entre 4 min y 40 min", "Range: " + DiskBudget.Range(4.2, 40.9));
                Check(DiskBudget.Range(0.3, 9) == "hasta 9 min" && DiskBudget.Range(0.2, 0.5) == "menos de 1 min" && DiskBudget.Range(200, 300) == "entre 3 h y 5 h", "Range l\u00EDmites");
                Check(DiskBudget.Gb(3L * gib + gib / 10) == "3,1 GB", "Gb: " + DiskBudget.Gb(3L * gib + gib / 10));
            });

            Test("reliability", "health-report", () =>
            {
                RecordHealth ok = RecordHealth.Assess(3600, 60, 0, 0, 0, 0, 30, Recorder.High, false, 60, 1280, 720, false);
                Check(!ok.Bad, "una grabaci\u00F3n limpia dio aviso");
                RecordHealth few = RecordHealth.Assess(3600, 60, 10, 5, 3, 0, 80, Recorder.High, false, 60, 1280, 720, false);
                Check(!few.Bad, "0,5 % dio aviso: " + few.Text);
                RecordHealth bad = RecordHealth.Assess(3600, 60, 60, 30, 20, 0, 120, Recorder.Maximum, false, 60, 2560, 1440, true);
                Check(bad.Bad && bad.Affected == 110, "3 % no dio aviso o cuenta mal: " + bad.Affected);
                Check(bad.Text.Contains("110 fotogramas (3,1 %)"), "texto: " + bad.Text);
                Check(bad.Advice[0] == "enchufar el port\u00E1til", "con bater\u00EDa lo primero es enchufar: " + string.Join(", ", bad.Advice.ToArray()));
                Check(bad.Advice.Contains("bajar a 30 fps") && bad.Advice.Contains("grabar un \u00E1rea m\u00E1s peque\u00F1a") && bad.Advice.Contains("usar la calidad Alta"),
                      "consejos: " + string.Join(", ", bad.Advice.ToArray()));
                Check(bad.Text.EndsWith("enchufar el port\u00E1til, bajar a 30 fps o grabar un \u00E1rea m\u00E1s peque\u00F1a."), "texto: " + bad.Text);
                RecordHealth small = RecordHealth.Assess(1800, 60, 200, 0, 0, 0, 90, Recorder.Standard, false, 30, 1280, 720, false);
                Check(small.Bad && !small.Advice.Contains("bajar a 30 fps") && !small.Advice.Contains("grabar un \u00E1rea m\u00E1s peque\u00F1a") &&
                      !small.Advice.Contains("enchufar el port\u00E1til") && small.Advice.Contains("cerrar las apps que no uses"), "consejos a 30 fps: " + string.Join(", ", small.Advice.ToArray()));
                RecordHealth spike = RecordHealth.Assess(3600, 60, 2, 0, 0, 0, 420, Recorder.High, false, 60, 1280, 720, false);
                Check(spike.Bad && spike.Text.StartsWith("Tu equipo se atasc\u00F3 un momento (0,4 s)"), "pico: " + spike.Text);
                Check(!RecordHealth.Assess(100, 2, 100, 0, 0, 0, 900, Recorder.High, false, 60, 1280, 720, false).Bad, "menos de 3 s no avisa");
                RecordHealth gif = RecordHealth.Assess(900, 30, 100, 0, 0, 0, 50, Recorder.Standard, true, 30, 800, 600, false);
                Check(gif.Bad && gif.Advice.Contains("bajar los fotogramas del GIF"), "GIF: " + string.Join(", ", gif.Advice.ToArray()));
            });

            Test("reliability", "ntsc-frame-rates", () =>
            {
                object[][] cases =
                {
                    new object[] { 60, true, 60u, 1u, 60, 1 },
                    new object[] { 60, true, 60000u, 1001u, 60000, 1001 },
                    new object[] { 30, true, 60000u, 1001u, 30000, 1001 },
                    new object[] { 60, true, 59951u, 1000u, 60000, 1001 },   // a 59.951 Hz laptop panel, as before
                    new object[] { 60, true, 120000u, 1001u, 60000, 1001 },
                    new object[] { 60, true, 120u, 1u, 60, 1 },
                    new object[] { 60, true, 144000u, 1001u, 60000, 1001 },
                    new object[] { 60, true, 143856u, 1000u, 60000, 1001 },
                    new object[] { 60, true, 144u, 1u, 60, 1 },
                    new object[] { 60, true, 240000u, 1001u, 60000, 1001 },
                    new object[] { 60, true, 165u, 1u, 60, 1 },
                    new object[] { 60, true, 50u, 1u, 60, 1 },
                    new object[] { 60, false, 60000u, 1001u, 60, 1 },
                    new object[] { 24, true, 60000u, 1001u, 24, 1 },
                };
                foreach (object[] c in cases)
                {
                    int num, den;
                    Recorder.ChooseRate((int)c[0], (bool)c[1], (uint)c[2], (uint)c[3], out num, out den);
                    Check(num == (int)c[4] && den == (int)c[5], "objetivo " + c[0] + " a " + c[2] + "/" + c[3] + " Hz: " + num + "/" + den + ", esperado " + c[4] + "/" + c[5]);
                }
                Note(cases.Length + " casos");
            });

            Test("reliability", "pool-budget-levels", () =>
            {
                long gib = 1L << 30, fb = 1920L * 1080 * 4;
                Check(Cine.PoolBudget(0, 32 * gib) == 2 * gib && Cine.PoolBudget(0, 4 * gib) == gib, "nivel normal: min(2 GiB, libre/4)");
                Check(Cine.PoolBudget(1, 32 * gib) == 8 * gib && Cine.PoolBudget(1, 6 * gib) == 3 * gib, "nivel alto: min(8 GiB, libre/2)");
                Check(Cine.PoolBudget(1, -5) == 0 && Cine.PoolBudget(0, 0) == 0, "sin memoria: 0");
                Check(Cine.PoolFrames(fb, 8 * gib) == (int)(8 * gib / fb), "1080p con 8 GiB: " + Cine.PoolFrames(fb, 8 * gib));
                Check(Cine.PoolFrames(16, 8 * gib) == 2400, "el tope crece con la memoria: " + Cine.PoolFrames(16, 8 * gib));
                Check(Cine.PoolFrames(16, 3 * gib) == 900 && Cine.PoolFrames(16, gib) == 600, "tope proporcional, nunca menos de 600");
                Check(Cine.PoolFrames(fb, 0) == 8 && Cine.PoolFrames(0, gib) == 8, "m\u00EDnimo 8");
                double s = Cine.PeakSeconds(1, fb, 32 * gib, 60);
                Check(Math.Abs(s - (int)(8 * gib / fb) / 60.0) < 1e-9, "PeakSeconds: " + s);
            });

            Test("reliability", "settings-cine-roundtrip", () =>
            {
                string dir = WorkDir("settings");
                try
                {
                    Settings d = new Settings();
                    Check(d.CineFps == 60 && d.CinePeakMemory == 0, "valores por defecto");
                    string ini = Path.Combine(dir, "settings.ini");
                    File.WriteAllText(ini, "Sound = 1\nCineFps = 30\nCinePeakMemory = 1\n");
                    Settings a = Settings.Load(ini);
                    Check(a.CineFps == 30 && a.CinePeakMemory == 1, "lectura: " + a.CineFps + "/" + a.CinePeakMemory);
                    File.WriteAllText(ini, "Sound = 1\nCineFps = 24\nCinePeakMemory = 7\n");
                    Settings b = Settings.Load(ini);
                    Check(b.CineFps == 30 && b.CinePeakMemory == 1, "valores fuera de rango: " + b.CineFps + "/" + b.CinePeakMemory);
                    File.WriteAllText(ini, "Sound = 1\nCineFps = 59\nCinePeakMemory = x\n");
                    Settings c = Settings.Load(ini);
                    Check(c.CineFps == 60 && c.CinePeakMemory == 0, "valores raros: " + c.CineFps + "/" + c.CinePeakMemory);
                    // Write is private and Save never writes in tests: through reflection, to this folder only.
                    MethodInfo write = typeof(Settings).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance);
                    Check(write != null, "no encuentro Settings.Write");
                    Settings w = new Settings();
                    w.CineFps = 30;
                    w.CinePeakMemory = 1;
                    string out1 = Path.Combine(dir, "out.ini");
                    write.Invoke(w, new object[] { out1 });
                    Settings r = Settings.Load(out1);
                    Check(r.CineFps == 30 && r.CinePeakMemory == 1, "ida y vuelta: " + r.CineFps + "/" + r.CinePeakMemory);
                    w.CineFps = 60;
                    w.CinePeakMemory = 0;
                    write.Invoke(w, new object[] { out1 });
                    r = Settings.Load(out1);
                    Check(r.CineFps == 60 && r.CinePeakMemory == 0, "ida y vuelta por defecto: " + r.CineFps + "/" + r.CinePeakMemory);
                    // Options window and countdown before recording.
                    Check(d.RecordSetup && d.RecordCountdown == 3, "antes de grabar, por defecto: " + d.RecordSetup + "/" + d.RecordCountdown);
                    int[] given = { -4, 0, 1, 2, 3, 4, 5, 7, 8, 10, 60 }, nearest = { 0, 0, 0, 3, 3, 3, 5, 5, 10, 10, 10 };
                    for (int i = 0; i < given.Length; i++)
                        Check(Settings.Countdown(given[i]) == nearest[i], "cuenta atr\u00E1s " + given[i] + " -> " + Settings.Countdown(given[i]));
                    File.WriteAllText(ini, "Sound = 1\nRecordSetup = 0\nRecordCountdown = 7\n");
                    Settings e = Settings.Load(ini);
                    Check(!e.RecordSetup && e.RecordCountdown == 5, "lectura antes de grabar: " + e.RecordSetup + "/" + e.RecordCountdown);
                    w.RecordSetup = false;
                    w.RecordCountdown = 10;
                    write.Invoke(w, new object[] { out1 });
                    r = Settings.Load(out1);
                    Check(!r.RecordSetup && r.RecordCountdown == 10, "ida y vuelta antes de grabar: " + r.RecordSetup + "/" + r.RecordCountdown);
                }
                finally { DeleteDir(dir); }
            });

            Test("reliability", "verifier-overlap-same-result", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("verify-overlap");
                int w = 64, h = 48, n = 50, bytes = w * h * 4;
                IntPtr frame = Marshal.AllocHGlobal(bytes);
                string nut = Path.Combine(dir, "v.nut"), master = Path.Combine(dir, "m.mkv");
                CineSink sink = new CineSink(ffmpeg, w, h, 30, 1, nut);
                try
                {
                    byte[] managed = new byte[bytes];
                    sink.Start();
                    for (int i = 0; i < n; i++)
                    {
                        Paint("gradient", managed, w, h, i, 1);
                        Marshal.Copy(managed, 0, frame, bytes);
                        Check(sink.Write(frame), "Write " + i);
                    }
                    int code;
                    Check(sink.Close(30000, out code) && code == 0, "el nut no se cerr\u00F3: " + sink.ErrorTail);
                    string err = CineMaster.Build(ffmpeg, nut, null, null, "", master, n / 30.0, null);
                    Check(err == null, "Build: " + err);
                    CineExpect e = new CineExpect();
                    e.Width = w; e.Height = h; e.FpsNum = 30; e.FpsDen = 1;
                    e.FrameHashes = new List<ulong>(sink.Hashes);
                    CineVerification v = CineVerifier.Verify(ffmpeg, master, e, null);
                    Check(v.Passed && v.FramesMatched == n && v.FramesDecoded == n, "verificaci\u00F3n: " + FailingChecks(v));
                    e.FrameHashes[37] ^= 1;
                    CineVerification bad = CineVerifier.Verify(ffmpeg, master, e, null);
                    Check(!bad.Passed && bad.FirstMismatch == 37 && bad.FramesMatched == n - 1, "un hash cambiado: primero " + bad.FirstMismatch + ", iguales " + bad.FramesMatched);
                }
                finally
                {
                    sink.Dispose();
                    Marshal.FreeHGlobal(frame);
                    DeleteDir(dir);
                }
            });

            ReviewFixTests();
        }
    }
}

// Review fixes of the recording package: this take's quality never reaches the shared Settings, Shutdown keeps to its
// budget, the Cine question can't be stopped from under Start, cut phases move the short way, secondary-monitor stamps,
// a blocked pipe write can be cancelled, the cancel question survives a change of pass. Part of the reliability suite.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int written, IntPtr overlapped);

        static void ReviewFixTests()
        {
            Test("reliability", "take-quality-leaves-settings", () =>
            {
                Settings s = new Settings();
                s.VideoQuality = Recorder.Cinema;
                s.Webcam = 0;
                Session take = new Session(null, s, FakeFfmpeg(), new Rectangle(0, 0, 64, 48), false, 30, Recorder.High);
                Check(take.QualityForTest == Recorder.High, "calidad de la toma: " + take.QualityForTest);
                Check(object.ReferenceEquals(take.SettingsForTest, s), "la sesi\u00F3n no usa los Settings compartidos (la burbuja guardar\u00EDa una copia)");
                Check(s.VideoQuality == Recorder.Cinema, "la calidad guardada cambi\u00F3 a " + s.VideoQuality);
                Session same = new Session(null, s, FakeFfmpeg(), new Rectangle(0, 0, 64, 48), false, 30);
                Check(same.QualityForTest == Recorder.Cinema, "sin cambio, la calidad guardada: " + same.QualityForTest);
                Session gif = new Session(null, s, FakeFfmpeg(), new Rectangle(0, 0, 64, 48), true, 30, Recorder.High);
                Check(gif.QualityForTest == Recorder.Standard, "GIF: " + gif.QualityForTest);
                Check(Recorder.TargetFps(s, Recorder.High, false, 30) == 60 && Recorder.TargetFps(s, Recorder.Cinema, false, 30) == 30, "TargetFps por calidad");
                Check(Recorder.TargetFps(s, false, 30) == 30, "TargetFps con la calidad guardada");
            });

            Test("reliability", "shutdown-keeps-to-its-budget", () =>
            {
                Check(Session.ShutdownGrace(25000) == 8333 && Session.ShutdownGrace(300000) == 15000 && Session.ShutdownGrace(0) == 0 && Session.ShutdownGrace(-5) == 0,
                      "ShutdownGrace: " + Session.ShutdownGrace(25000) + "/" + Session.ShutdownGrace(300000));
                Fake("hang");
                Take t = NewTake("shutdown-budget", Recorder.Maximum, false, true, false, null);
                Recorder.SetCurrentForTest(t.Session);
                t.Session.BeginSavingForTest(2.0);
                Stopwatch clock = Stopwatch.StartNew();
                bool ok = Recorder.Shutdown(4000);
                long ms = clock.ElapsedMilliseconds;
                Check(!ok, "Shutdown dijo que todo estaba guardado con la pasada colgada");
                Check(ms < 4600, "Shutdown(4000) bloque\u00F3 " + ms + " ms");
                Check(t.Session.WaitSavedForTest(20000), "el guardado no termin\u00F3 tras cancelar");
                CheckRescued(t, new[] { Path.GetFileName(t.Video), Path.GetFileName(t.Sys), Path.GetFileName(t.Mic) });
                Check(FakeChildren().Count == 0, "queda el proceso colgado");
                Note("bloque\u00F3 " + ms + " ms");
            });

            Test("reliability", "shutdown-leaves-cross-drive-move-to-the-quit", () =>
            {
                Fake("sleep:300");
                Take t = NewTake("shutdown-other-drive", Recorder.Maximum, false, false, false, null);
                string root = Path.GetPathRoot(Path.GetFullPath(t.Dir));
                char other = char.ToUpperInvariant(root[0]) == 'Q' ? 'R' : 'Q';
                t.Settings.SaveFolder = other + ":\\Stackshot prueba sin crear";
                Check(!Recorder.SameVolume(t.Output, t.Settings.SaveFolder), "SameVolume con otra unidad");
                Recorder.SetCurrentForTest(t.Session);
                t.Session.BeginSavingForTest(2.0);
                bool ok = Recorder.Shutdown(30000);
                Check(ok, "Shutdown devolvi\u00F3 false: " + OneLine(t.Session.ErrorText ?? ""));
                Check(File.Exists(t.Output), "el archivo terminado no sigue en temp para que lo lleve la salida");
                Check(!Directory.Exists(t.Settings.SaveFolder), "intent\u00F3 copiar a la otra unidad");
                CheckNothingLeft(t);
            });

            Test("reliability", "same-volume", () =>
            {
                Check(Recorder.SameVolume("C:\\a\\b.mp4", "c:\\x\\y"), "misma unidad");
                Check(!Recorder.SameVolume("C:\\a\\b.mp4", "D:\\a"), "otra unidad");
                Check(Recorder.SameVolume("\\\\srv\\share\\a.mp4", "\\\\SRV\\Share\\b"), "mismo recurso compartido");
                Check(!Recorder.SameVolume("\\\\srv\\share\\a.mp4", "\\\\srv\\other\\b"), "otro recurso compartido");
                Check(!Recorder.SameVolume("C:\\a", "C:\\bad|name"), "una ruta inv\u00E1lida no es la misma unidad");
            });

            Test("reliability", "cine-question-blocks-stop-and-quit", () =>
            {
                Settings s = new Settings();
                s.Webcam = 0;
                Session q = new Session(null, s, FakeFfmpeg(), new Rectangle(0, 0, 64, 48), false, 60, Recorder.Cinema);
                Recorder.SetCurrentForTest(q);
                q.SetAskingForTest(true);
                try
                {
                    q.Stop();
                    Check(!q.StoppingForTest, "el atajo de grabar par\u00F3 una toma que a\u00FAn pregunta");
                    Check(Recorder.Busy, "Busy deber\u00EDa ser true mientras pregunta");
                    Stopwatch clock = Stopwatch.StartNew();
                    Check(Recorder.Shutdown(5000), "Shutdown durante la pregunta debe devolver true");
                    Check(clock.ElapsedMilliseconds < 1000, "Shutdown esper\u00F3 " + clock.ElapsedMilliseconds + " ms sin nada que guardar");
                    Check(!Recorder.Busy, "sigue ocupado tras Shutdown");
                    Check(!q.WaitSavedForTest(0), "Shutdown abandon\u00F3 la sesi\u00F3n que Start iba a usar");
                }
                finally
                {
                    q.SetAskingForTest(false);
                    Recorder.SetCurrentForTest(null);
                }
            });

            Test("reliability", "cut-phase-moves-the-short-way", () =>
            {
                double display = 16.6667, period = 16.6667;
                Check(Math.Abs(Session.NearPhase(0.5, 16.0, display) - 17.1667) < 1e-6, "16,0 -> 0,5 debe avanzar 1,17 ms: " + Session.NearPhase(0.5, 16.0, display));
                Check(Math.Abs(Session.NearPhase(16.0, 0.5, display) - (-0.6667)) < 1e-6, "0,5 -> 16,0 debe retroceder 1,17 ms: " + Session.NearPhase(16.0, 0.5, display));
                Check(Math.Abs(Session.NearPhase(5, 4, display) - 5) < 1e-9 && Math.Abs(Session.NearPhase(4, 40.0, display) - 37.3334) < 1e-6, "sin cruzar el l\u00EDmite");
                Check(Session.NearPhase(3, 1, 0) == 3, "sin refresco conocido");
                // Every relocation keeps consecutive cuts within half a refresh of one period apart.
                Random rnd = new Random(7);
                double phase = 8;
                for (int n = 0; n < 2000; n++)
                {
                    double before = phase + n * period;
                    phase = Session.NearPhase(rnd.NextDouble() * display, phase, display);
                    double step = phase + (n + 1) * period - before;
                    Check(step > period - display / 2 - 1e-9 && step < period + display / 2 + 1e-9, "salto de " + step + " ms entre cortes en " + n);
                }
                double[] hist = new double[32];
                for (int i = 0; i < 32; i++) hist[i] = i >= 20 && i < 28 ? 0 : 10;
                double far = Session.Quietest(hist, display, -50.0);
                Check(far >= 0 && far < display, "Quietest con fase negativa: " + far);
                Check(Math.Abs(Session.Quietest(hist, display, display * 0.75) - display * 0.75) < 1e-9, "Quietest se mueve sin motivo");
            });

            Test("reliability", "secondary-monitor-stamp", () =>
            {
                Check(ScreenSource.StampOf(80, 50, 100) == 80, "debe usar el present del monitor que cambi\u00F3");
                Check(ScreenSource.StampOf(0, 50, 100) == 100, "sin present, ahora");
                Check(ScreenSource.StampOf(40, 50, 100) == 50, "nunca antes que la imagen anterior");
                Check(ScreenSource.StampOf(0, 0, 100) == 100, "primera imagen");
            });

            Test("reliability", "blocked-pipe-write-can-be-cancelled", () =>
            {
                SafeFileHandle read, write;
                Check(CreatePipe(out read, out write, IntPtr.Zero, 4096), "CreatePipe");
                int size = 1 << 20;
                IntPtr buf = Marshal.AllocHGlobal(size);
                bool stop = false, failed = false;
                uint tid = 0;
                Thread writer = new Thread(delegate()
                {
                    tid = Native.GetCurrentThreadId();
                    while (!Volatile.Read(ref stop))
                    {
                        int done;
                        if (!WriteFile(write, buf, size, out done, IntPtr.Zero)) { failed = true; break; }
                    }
                });
                writer.IsBackground = true;
                try
                {
                    writer.Start();
                    Check(!writer.Join(400), "la escritura no se bloque\u00F3 (nadie lee el pipe)");
                    Volatile.Write(ref stop, true);
                    Stopwatch clock = Stopwatch.StartNew();
                    bool ended = Session.CancelPendingWrites(writer, tid, 3000);
                    Check(ended, "el hilo sigue bloqueado tras cancelar");
                    Check(failed, "la escritura pendiente no fall\u00F3");
                    Check(clock.ElapsedMilliseconds < 2000, "tard\u00F3 " + clock.ElapsedMilliseconds + " ms");
                    Check(Session.CancelPendingWrites(writer, tid, 100), "un hilo terminado cuenta como terminado");
                }
                finally
                {
                    write.Dispose();
                    read.Dispose();
                    if (!writer.IsAlive) Marshal.FreeHGlobal(buf);
                }
            });

            Test("reliability", "cancel-question-survives-next-pass", () =>
            {
                Check(RecordBar.KeepsArmed(true, true), "de una pasada a otra se olvida el primer clic en Cancelar");
                Check(!RecordBar.KeepsArmed(true, false) && !RecordBar.KeepsArmed(false, true) && !RecordBar.KeepsArmed(false, false), "fuera de las pasadas no debe seguir armado");
            });
        }
    }
}

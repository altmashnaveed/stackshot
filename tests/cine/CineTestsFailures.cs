// Failure-path suite: the recorder must notice and report, never hang or leave processes behind.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        // ---- failures: the recorder must notice and report, never hang or leave processes behind ----

        static void FailuresSuite()
        {
            Test("failures", "missing-ffmpeg-start-throws", () =>
            {
                string dir = WorkDir("fail-missing");
                CineSink sink = new CineSink(@"C:\no-existe\stackshot-ffmpeg-missing.exe", 64, 64, 30, 1, Path.Combine(dir, "v.nut"));
                bool threw = false;
                try { sink.Start(); }
                catch (Exception) { threw = true; }
                finally { sink.Dispose(); DeleteDir(dir); }
                Check(threw, "Start no lanzo excepcion con un ffmpeg inexistente");
            });

            Test("failures", "kill-ffmpeg-midstream", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("fail-kill");
                int w = 640, h = 360, bytes = w * h * 4;
                IntPtr frame = Marshal.AllocHGlobal(bytes);
                CineSink sink = new CineSink(ffmpeg, w, h, 30, 1, Path.Combine(dir, "v.nut"));
                try
                {
                    byte[] managed = RandomBytes(new Random(9), bytes);
                    Marshal.Copy(managed, 0, frame, bytes);
                    sink.Start();
                    for (int i = 0; i < 5; i++) Check(sink.Write(frame), "Write " + i + " fallo antes de matar ffmpeg: " + sink.ErrorTail);
                    List<int> kids = WaitChildren(Process.GetCurrentProcess().Id, 5000);
                    Check(kids.Count > 0, "no encuentro el proceso ffmpeg hijo");
                    foreach (int pid in kids)
                    {
                        using (Process p = Process.GetProcessById(pid))
                        {
                            p.Kill();
                            p.WaitForExit(5000);
                        }
                    }
                    bool failed = false;
                    for (int i = 0; i < 500 && !failed; i++) failed = !sink.Write(frame);
                    Check(failed, "Write siguio devolviendo true tras matar ffmpeg");
                    Check(sink.Failed, "Failed no es true tras el fallo");
                    Note("ErrorTail: " + OneLine(sink.ErrorTail));
                }
                finally
                {
                    sink.Dispose();
                    Marshal.FreeHGlobal(frame);
                    DeleteDir(dir);
                }
            });

            Test("failures", "no-ffmpeg-child-after-dispose", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("fail-dispose");
                int w = 320, h = 180, bytes = w * h * 4;
                IntPtr frame = Marshal.AllocHGlobal(bytes);
                CineSink sink = new CineSink(ffmpeg, w, h, 30, 1, Path.Combine(dir, "v.nut"));
                try
                {
                    sink.Start();
                    for (int i = 0; i < 3; i++) Check(sink.Write(frame), "Write " + i + " fallo");
                    Check(WaitChildren(Process.GetCurrentProcess().Id, 5000).Count > 0, "no aparece el ffmpeg hijo");
                }
                finally
                {
                    sink.Dispose();
                    Marshal.FreeHGlobal(frame);
                }
                List<int> left = new List<int>();
                Stopwatch clock = Stopwatch.StartNew();
                do
                {
                    left = ChildPids(Process.GetCurrentProcess().Id, "ffmpeg.exe");
                    if (left.Count == 0) break;
                    System.Threading.Thread.Sleep(100);
                } while (clock.ElapsedMilliseconds < 5000);
                Check(left.Count == 0, "quedan " + left.Count + " ffmpeg hijos 5 s despues de Dispose");
                DeleteDir(dir);
            });

            Test("failures", "build-missing-nut", () =>
            {
                string dir = WorkDir("fail-build-nut");
                try
                {
                    string output = Path.Combine(dir, "out.mkv");
                    string err = CineMaster.Build(ffmpegOrDefault(), Path.Combine(dir, "missing.nut"), null, null, "", output, 1.0, null);
                    Check(err != null && err.Length > 0, "Build con un .nut inexistente devolvio exito");
                    Check(!File.Exists(output), "Build dejo un fichero de salida tras fallar");
                    Note("error: " + OneLine(err));
                }
                finally { DeleteDir(dir); }
            });

            Test("failures", "build-missing-directory", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("fail-build-dir");
                try
                {
                    string nut = MakeTinyNut(dir);
                    string output = Path.Combine(dir, "no-such-folder", "sub", "out.mkv");
                    string err = CineMaster.Build(ffmpeg, nut, null, null, "", output, 0.1, null);
                    Check(err != null && err.Length > 0, "Build a una carpeta inexistente devolvio exito");
                    Check(!File.Exists(output), "Build creo el fichero de salida");
                    Note("error: " + OneLine(err));
                }
                finally { DeleteDir(dir); }
            });

            Test("failures", "verify-zero-byte-file", () =>
            {
                NeedFfmpeg();
                string dir = WorkDir("fail-zero");
                try
                {
                    string empty = Path.Combine(dir, "empty.mkv");
                    File.WriteAllBytes(empty, new byte[0]);
                    CineExpect e = new CineExpect();
                    e.Width = 16; e.Height = 16; e.FpsNum = 30; e.FpsDen = 1;
                    e.FrameHashes.Add(1UL);
                    CineVerification v = CineVerifier.Verify(ffmpeg, empty, e, null);
                    Check(v != null && !v.Passed, "un fichero de 0 bytes se verifico");
                    Note("fallos: " + FailingChecks(v));
                }
                finally { DeleteDir(dir); }
            });
        }

        static string ffmpegOrDefault()
        {
            return File.Exists(ffmpeg) ? ffmpeg : "ffmpeg.exe";
        }

        static string MakeTinyNut(string dir)
        {
            string nut = Path.Combine(dir, "tiny.nut");
            byte[] managed = new byte[16 * 16 * 4];
            IntPtr frame = Marshal.AllocHGlobal(managed.Length);
            CineSink sink = new CineSink(ffmpeg, 16, 16, 30, 1, nut);
            try
            {
                sink.Start();
                for (int i = 0; i < 4; i++)
                {
                    Paint("gradient", managed, 16, 16, i, 1);
                    Marshal.Copy(managed, 0, frame, managed.Length);
                    Check(sink.Write(frame), "Write del nut de prueba fallo");
                }
                int code;
                Check(sink.Close(30000, out code) && code == 0, "el nut de prueba no se cerro bien: " + sink.ErrorTail);
            }
            finally
            {
                sink.Dispose();
                Marshal.FreeHGlobal(frame);
            }
            return nut;
        }

        static List<int> WaitChildren(int parent, int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                List<int> kids = ChildPids(parent, "ffmpeg.exe");
                if (kids.Count > 0 || clock.ElapsedMilliseconds >= timeoutMs) return kids;
                System.Threading.Thread.Sleep(100);
            }
        }
    }
}

// Saves: a failed write keeps the old file whole and leaves nothing behind.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static byte[] Bytes(int n, byte v)
        {
            byte[] b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(v + i);
            return b;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static void OnlyFile(string dir, string name)
        {
            string[] files = Directory.GetFiles(dir);
            Check(files.Length == 1 && Path.GetFileName(files[0]) == name, "left behind: " + string.Join(", ", files));
        }

        static void AtomicSuite()
        {
            Test("atomic", "failed-write-keeps-old-file", delegate
            {
                string dir = WorkDir("fail");
                string file = Path.Combine(dir, "Captura 1.png");
                byte[] old = Bytes(5000, 7);
                File.WriteAllBytes(file, old);
                bool threw = false;
                try
                {
                    AtomicFile.Write(file, delegate(Stream s)
                    {
                        s.Write(Bytes(100, 1), 0, 100);
                        throw new IOException("disco lleno (simulado)");
                    });
                }
                catch (IOException) { threw = true; }
                Check(threw, "the failure must reach the caller");
                Check(Same(File.ReadAllBytes(file), old), "the old file was damaged");
                OnlyFile(dir, "Captura 1.png");
            });
            Test("atomic", "replaces-content", delegate
            {
                string dir = WorkDir("ok");
                string file = Path.Combine(dir, "Captura 1.png");
                File.WriteAllBytes(file, Bytes(5000, 7));
                DateTime created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                File.SetCreationTimeUtc(file, created);
                byte[] fresh = Bytes(3000, 99);
                AtomicFile.Write(file, fresh);
                Check(Same(File.ReadAllBytes(file), fresh), "new content not written");
                Check(File.GetCreationTimeUtc(file) == created, "File.Replace should keep the original file's identity (creation time)");
                OnlyFile(dir, "Captura 1.png");
            });
            Test("atomic", "new-file", delegate
            {
                string dir = WorkDir("new");
                string file = Path.Combine(dir, "nuevo.pdf");
                AtomicFile.Write(file, Bytes(10, 1));
                Check(Same(File.ReadAllBytes(file), Bytes(10, 1)), "content");
                OnlyFile(dir, "nuevo.pdf");
            });
            Test("atomic", "read-only-target-intact", delegate
            {
                string dir = WorkDir("ro");
                string file = Path.Combine(dir, "Captura 1.png");
                byte[] old = Bytes(800, 3);
                File.WriteAllBytes(file, old);
                File.SetAttributes(file, FileAttributes.ReadOnly);
                bool threw = false;
                try { AtomicFile.Write(file, Bytes(10, 1)); }
                catch (Exception) { threw = true; }
                finally { File.SetAttributes(file, FileAttributes.Normal); }
                Check(threw, "writing over a read-only file must fail");
                Check(Same(File.ReadAllBytes(file), old), "the read-only file changed");
                OnlyFile(dir, "Captura 1.png");
            });
            Test("atomic", "watched-folder-no-delete-event", delegate
            {
                // In the stack's temp folder a capture must only look changed, never deleted or renamed (its card would go).
                string dir = WorkDir("watched");
                string file = Path.Combine(dir, "Captura 1.png");
                File.WriteAllBytes(file, Bytes(4000, 1));
                string saved = AtomicFile.WatchedFolder;
                AtomicFile.WatchedFolder = dir;
                List<string> events = new List<string>();
                try
                {
                    using (FileSystemWatcher w = new FileSystemWatcher(dir))
                    {
                        w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                        w.Deleted += delegate(object o, FileSystemEventArgs e) { lock (events) events.Add("Deleted " + e.Name); };
                        w.Renamed += delegate(object o, RenamedEventArgs e) { lock (events) events.Add("Renamed " + e.OldName + " -> " + e.Name); };
                        w.EnableRaisingEvents = true;
                        AtomicFile.Write(file, Bytes(4100, 9));
                        Thread.Sleep(600);
                    }
                }
                finally { AtomicFile.WatchedFolder = saved; }
                Check(Same(File.ReadAllBytes(file), Bytes(4100, 9)), "content");
                lock (events)
                    foreach (string e in events)
                        Check(e != "Deleted Captura 1.png" && !e.StartsWith("Renamed Captura 1.png ->") && !e.EndsWith("-> Captura 1.png"),
                              "the capture itself was " + e);
                OnlyFile(dir, "Captura 1.png");
            });
            Test("atomic", "editor-write-png-roundtrip", delegate
            {
                string dir = WorkDir("png");
                string file = Path.Combine(dir, "Captura 1.png");
                File.WriteAllBytes(file, Bytes(100, 1));
                using (Bitmap b = new Bitmap(64, 32))
                {
                    using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 32, 192, 64));
                    Editor.Write(b, file);
                }
                using (Bitmap r = ShotStack.LoadFull(file))
                {
                    Check(r.Width == 64 && r.Height == 32, "size");
                    Color c = r.GetPixel(10, 10);
                    Check(c.R == 32 && c.G == 192 && c.B == 64, "pixel " + c);
                }
                OnlyFile(dir, "Captura 1.png");
            });
        }
    }
}

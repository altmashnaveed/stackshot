// Stackshot - Background copies, the steps of quitting, and recordings' work files (rescued, never lost).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Stackshot
{
    // Background copies to the save folder, shared between the UI thread and the copy threads. A synchronous save of
    // a path whose copy is still running must not write the file itself (that copy would rename its older .part over
    // it): it asks the running copy to go once more instead.
    public sealed class CopyRuns
    {
        readonly HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> again = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Begin(string path) { lock (active) active.Add(path); }

        // True when a copy is running and will repeat; false when the caller must copy by itself.
        public bool Repeat(string path)
        {
            lock (active)
            {
                if (!active.Contains(path)) return false;
                again.Add(path);
                return true;
            }
        }

        // Called by the copy thread after each pass: true to copy again, false when it is done (from then on Repeat
        // returns false).
        public bool Again(string path)
        {
            lock (active)
            {
                if (again.Remove(path)) return true;
                active.Remove(path);
                return false;
            }
        }

        public void End(string path) { lock (active) { active.Remove(path); again.Remove(path); } }
    }

    // The order of a safe exit, apart from the app so tests can run it with fakes: every question first (each one can
    // cancel, and nothing has been stopped yet), then the recording is saved and copies still running finish.
    public sealed class QuitSteps
    {
        public bool Interactive = true;    // false: Windows is ending the session, nobody can answer
        public Func<bool> Recording, AskRecording, ConfirmEditors, ConfirmDocuments;
        public Action SaveRecording, WaitCopies;
        public string CancelledBy;

        public bool Run()
        {
            if (Interactive)
            {
                if (Ask(Recording) && !Ask(AskRecording, true)) { CancelledBy = "grabaci\u00F3n"; return false; }
                if (!Ask(ConfirmEditors, true)) { CancelledBy = "editor"; return false; }
                if (!Ask(ConfirmDocuments, true)) { CancelledBy = "documentos"; return false; }
            }
            if (Ask(Recording)) Do(SaveRecording, "grabaci\u00F3n");
            Do(WaitCopies, "copias");
            return true;
        }

        static bool Ask(Func<bool> f) { return Ask(f, false); }

        // A question that fails to show never traps the user in the app.
        static bool Ask(Func<bool> f, bool otherwise)
        {
            if (f == null) return otherwise;
            try { return f(); }
            catch (Exception ex) { ShotStack.Log("Salida: " + ex.Message); return otherwise; }
        }

        static void Do(Action a, string what)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception ex) { ShotStack.Log("Salida (" + what + "): " + ex.Message); }
        }
    }

    // Work files of a recording: "~grabando <time> ..." (live video and sound) and "~cine <time>.mkv" (the master being
    // built). The recorder removes them when it finishes; any found later belong to a recording that never did.
    public static class WorkFiles
    {
        public const string StampFormat = "yyyy-MM-dd HH.mm.ss";
        static readonly string[] Prefixes = { "~grabando ", "~cine " };
        static readonly string[] Outputs = { "V\u00EDdeo ", "GIF ", "Cine " };

        public sealed class Rescued
        {
            public string Stamp, Folder;
            public DateTime Started = DateTime.MinValue;
            public readonly List<string> Files = new List<string>();
            public long Bytes;
        }

        public static bool IsWorkFile(string name)
        {
            foreach (string p in Prefixes) if (name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // The recording's start time in a work file's name, or null.
        public static string StampOf(string name)
        {
            foreach (string p in Prefixes)
            {
                if (!name.StartsWith(p, StringComparison.Ordinal) || name.Length < p.Length + StampFormat.Length) continue;
                string s = name.Substring(p.Length, StampFormat.Length);
                DateTime t;
                if (DateTime.TryParseExact(s, StampFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) return s;
            }
            return null;
        }

        // Moves orphaned work files into recoverRoot\<time>, grouped by recording. Files written in the last `quiet`
        // or still open by someone (a live FFmpeg finishing after a crash) stay for the next time. withOutputs: the
        // recording's own output in the temp folder ("Video <time>.mp4"...) goes with them; only at startup, when no
        // card can be showing it. Nothing is ever deleted.
        public static List<Rescued> Rescue(string tempDir, string recoverRoot, TimeSpan quiet, bool withOutputs)
        {
            List<Rescued> r = new List<Rescued>();
            string[] files;
            try { files = Directory.GetFiles(tempDir); }
            catch (Exception) { return r; }
            Dictionary<string, List<string>> groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            DateTime now = DateTime.Now;
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                if (!IsWorkFile(name)) continue;
                string stamp = StampOf(name);
                try
                {
                    DateTime written = File.GetLastWriteTime(f);
                    if (now - written < quiet) continue;
                    if (stamp == null) stamp = written.ToString(StampFormat);
                }
                catch (Exception) { continue; }
                if (InUse(f)) continue;
                List<string> g;
                if (!groups.TryGetValue(stamp, out g)) groups[stamp] = g = new List<string>();
                g.Add(f);
            }
            if (withOutputs)
                foreach (string f in files)
                {
                    string name = Path.GetFileName(f);
                    foreach (string o in Outputs)
                    {
                        if (!name.StartsWith(o, StringComparison.Ordinal) || name.Length < o.Length + StampFormat.Length) continue;
                        string stamp = name.Substring(o.Length, StampFormat.Length);
                        List<string> g;
                        if (groups.TryGetValue(stamp, out g)) { if (!InUse(f)) g.Add(f); continue; }
                        // A recording without sound is written straight to its MP4, with no work file: one that never
                        // got its index (FFmpeg writes it last) is an interrupted recording too.
                        DateTime t;
                        if (!name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!DateTime.TryParseExact(stamp, StampFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) continue;
                        try { if (now - File.GetLastWriteTime(f) < quiet) continue; }
                        catch (Exception) { continue; }
                        if (InUse(f) || !UnfinishedMp4(f)) continue;
                        g = new List<string>();
                        g.Add(f);
                        groups[stamp] = g;
                    }
                }
            List<string> stamps = new List<string>(groups.Keys);
            stamps.Sort(StringComparer.Ordinal);
            foreach (string stamp in stamps)
            {
                Rescued one = new Rescued();
                one.Stamp = stamp;
                one.Folder = Path.Combine(recoverRoot, stamp);
                DateTime t;
                if (DateTime.TryParseExact(stamp, StampFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) one.Started = t;
                foreach (string f in groups[stamp])
                {
                    try
                    {
                        Directory.CreateDirectory(one.Folder);
                        string to = Free(Path.Combine(one.Folder, Path.GetFileName(f)));
                        long size = new FileInfo(f).Length;
                        File.Move(f, to);
                        one.Files.Add(to);
                        one.Bytes += size;
                    }
                    catch (Exception ex) { ShotStack.Log("Recuperar " + f + ": " + ex.Message); }
                }
                if (one.Files.Count == 0) continue;
                ShotStack.Log("Grabaci\u00F3n interrumpida recuperada en " + one.Folder + " (" + one.Files.Count + " archivos)");
                r.Add(one);
            }
            return r;
        }

        // An MP4 (it starts with an ftyp box) whose top-level boxes end without a moov: its index was never written,
        // so no player opens it as it is. Anything else, or a file that can't be read, counts as finished.
        public static bool UnfinishedMp4(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length, pos = 0;
                    byte[] h = new byte[8];
                    bool mp4 = false;
                    while (pos + 8 <= len)
                    {
                        fs.Position = pos;
                        if (!ReadAll(fs, h)) break;
                        long size = BigEndian(h, 0);
                        string type = System.Text.Encoding.ASCII.GetString(h, 4, 4);
                        if (!mp4 && type != "ftyp") return false;
                        mp4 = true;
                        if (type == "moov") return false;
                        if (size == 1)
                        {
                            if (!ReadAll(fs, h)) break;
                            size = (BigEndian(h, 0) << 32) | BigEndian(h, 4);
                        }
                        if (size < 8) break; // 0: the box runs to the end of the file
                        pos += size;
                    }
                    return mp4;
                }
            }
            catch (Exception) { return false; }
        }

        static long BigEndian(byte[] b, int at)
        {
            return ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];
        }

        static bool ReadAll(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        static bool InUse(string f)
        {
            try
            {
                using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None)) return false;
            }
            catch (Exception) { return true; }
        }

        static string Free(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int n = 2; ; n++)
            {
                string p = Path.Combine(dir, stem + " (" + n + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }

        // Bytes under the recovered folder (0 when there is none).
        public static long Bytes(string recoverRoot)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(recoverRoot)) return 0;
                foreach (string f in Directory.GetFiles(recoverRoot, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch (Exception) { }
                }
            }
            catch (Exception) { }
            return total;
        }
    }
}

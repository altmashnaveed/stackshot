// Stackshot - A folder watched for new XML files, each opened in the viewer as soon as it is complete: for programs that
// leave every SOAP message they send or receive as an XML file in a folder. Off unless turned on in General > Avanzado.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Stackshot
{
    public sealed class XmlRecent
    {
        public string Path, Operation;
        public DateTime Written;
    }

    public sealed class XmlWatch : IDisposable
    {
        const int SettleMs = 250, GiveUpSeconds = 15;

        // The folder an organization sets with the XmlWatchFolder policy, or null; tests replace it.
        internal static Func<string> Preset = delegate { return Policy.XmlFolder; };

        // The folder watch is only offered where it was already set up or the organization sets its folder: nobody else
        // sees it, not even through the settings search.
        public static bool Offered(Settings s)
        {
            return s.XmlWatch || !string.IsNullOrEmpty(s.XmlWatchFolder) || Preset() != null;
        }

        readonly Action<string> open;
        readonly Control sync;
        readonly Timer settle = new Timer { Interval = SettleMs }, retry = new Timer { Interval = 60000 };
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher watcher;
        string folder;

        sealed class Pending { public long Size = -1; public DateTime Since; }

        // open: called on sync's thread with each complete new file.
        public XmlWatch(Control sync, Action<string> open)
        {
            this.sync = sync;
            this.open = open;
            settle.Tick += delegate { Settle(); };
            retry.Tick += delegate { retry.Stop(); Start(); };
        }

        // The folder being watched, or null when off.
        public string Folder { get { return folder; } }
        public bool Watching { get { return watcher != null; } }

        // The folder to watch: the organization's, else the one chosen; null when there is none yet.
        public static string FolderFor(Settings s)
        {
            string f = Preset() ?? s.XmlWatchFolder;
            return string.IsNullOrEmpty(f) ? null : f;
        }

        // Follows the settings: starts, stops or moves to another folder.
        public void Apply(Settings s)
        {
            string want = s.XmlWatch ? FolderFor(s) : null;
            if (string.Equals(want, folder, StringComparison.OrdinalIgnoreCase) && (want == null || watcher != null || retry.Enabled)) return;
            Stop();
            folder = want;
            if (want != null) Start();
        }

        void Start()
        {
            if (folder == null) return;
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            try
            {
                if (!Directory.Exists(folder)) { ShotStack.Log("XML: no existe " + folder + ", otra vez en 1 min"); retry.Start(); return; }
                FileSystemWatcher w = new FileSystemWatcher(folder, "*.xml");
                w.NotifyFilter = NotifyFilters.FileName;
                w.SynchronizingObject = sync;
                w.Created += delegate(object o, FileSystemEventArgs e) { Arrived(e.FullPath); };
                w.Renamed += delegate(object o, RenamedEventArgs e) { Arrived(e.FullPath); };
                w.Error += delegate(object o, ErrorEventArgs e) { ShotStack.Log("XML: vigilante " + e.GetException().Message); retry.Start(); };
                w.EnableRaisingEvents = true;
                watcher = w;
                ShotStack.Log("XML: vigilando " + folder);
            }
            catch (Exception ex) { ShotStack.Log("XML: " + ex.Message); retry.Start(); }
        }

        public void Stop()
        {
            retry.Stop();
            settle.Stop();
            pending.Clear();
            if (watcher != null) { watcher.Dispose(); watcher = null; ShotStack.Log("XML: ya no se vigila " + folder); }
            folder = null;
        }

        void Arrived(string path)
        {
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return;
            pending[path] = new Pending { Since = DateTime.UtcNow };
            settle.Start();
        }

        // A file is complete when it can be opened without sharing writes (the writer has closed it) and its size held
        // for one tick; one that never settles is given up after GiveUpSeconds.
        void Settle()
        {
            List<string> ready = new List<string>();
            foreach (KeyValuePair<string, Pending> kv in new List<KeyValuePair<string, Pending>>(pending))
            {
                long size = -1;
                try { using (FileStream fs = new FileStream(kv.Key, FileMode.Open, FileAccess.Read, FileShare.Read)) size = fs.Length; }
                catch (Exception) { }
                if (size > 0 && size == kv.Value.Size) { pending.Remove(kv.Key); ready.Add(kv.Key); }
                else if ((DateTime.UtcNow - kv.Value.Since).TotalSeconds > GiveUpSeconds) { pending.Remove(kv.Key); ShotStack.Log("XML: no se pudo leer " + kv.Key); }
                else kv.Value.Size = size;
            }
            if (pending.Count == 0) settle.Stop();
            ready.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)); });
            foreach (string p in ready)
            {
                try { open(p); }
                catch (Exception ex) { ShotStack.Log("XML: abrir " + ex.Message); }
            }
        }

        public void Dispose()
        {
            Stop();
            settle.Dispose();
            retry.Dispose();
        }

        // ---- the folder's files

        // The newest XML in the folder, or null.
        public static string Latest(string folder)
        {
            List<XmlRecent> r = Recent(folder, 1);
            return r.Count > 0 ? r[0].Path : null;
        }

        // The newest XML files of the folder, newest first, with the operation each one carries.
        public static List<XmlRecent> Recent(string folder, int max)
        {
            List<XmlRecent> o = new List<XmlRecent>();
            try
            {
                if (folder == null || !Directory.Exists(folder)) return o;
                List<FileInfo> files = new List<FileInfo>(new DirectoryInfo(folder).GetFiles("*.xml"));
                files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 0; i < files.Count && o.Count < max; i++)
                    o.Add(new XmlRecent { Path = files[i].FullName, Written = files[i].LastWriteTime, Operation = Operation(files[i].FullName) });
            }
            catch (Exception ex) { ShotStack.Log("XML: lista " + ex.Message); }
            return o;
        }

        // The XML before (step -1) or after (step 1) this one in its folder, by the time it was written; null at either end.
        public static string Sibling(string path, int step)
        {
            try
            {
                int i;
                List<FileInfo> files = Ordered(path, out i);
                int j = i + step;
                return i >= 0 && j >= 0 && j < files.Count ? files[j].FullName : null;
            }
            catch (Exception) { return null; }
        }

        // Both neighbours from one listing of the folder (it can hold thousands of messages).
        public static void Siblings(string path, out bool prev, out bool next)
        {
            prev = next = false;
            try
            {
                int i;
                List<FileInfo> files = Ordered(path, out i);
                if (i < 0) return;
                prev = i > 0;
                next = i + 1 < files.Count;
            }
            catch (Exception) { }
        }

        // The folder's XML files by the time they were written, and where path is among them (-1: not there).
        static List<FileInfo> Ordered(string path, out int index)
        {
            string full = Path.GetFullPath(path);
            List<FileInfo> files = new List<FileInfo>(new DirectoryInfo(Path.GetDirectoryName(path)).GetFiles("*.xml"));
            files.Sort(delegate(FileInfo a, FileInfo b)
            {
                int c = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            index = files.FindIndex(delegate(FileInfo f) { return string.Equals(f.FullName, full, StringComparison.OrdinalIgnoreCase); });
            return files;
        }

        static readonly Regex BodyRx = new Regex(@"<(?:[\w.\-]+:)?Body[^>]*>\s*<(?:[\w.\-]+:)?([\w.\-]+)", RegexOptions.Compiled);
        static readonly Regex RootRx = new Regex(@"<(?![?!])(?:[\w.\-]+:)?([\w.\-]+)", RegexOptions.Compiled);

        // What a SOAP message does, from its start: the first element inside Body ("SOAP Fault" for a fault), else the
        // root element's name; "" when it can't be read.
        public static string Operation(string path)
        {
            try
            {
                byte[] buf = new byte[4096];
                int n;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = fs.Read(buf, 0, buf.Length);
                string t = Encoding.UTF8.GetString(buf, 0, n);
                Match m = BodyRx.Match(t);
                if (!m.Success) m = RootRx.Match(t);
                if (!m.Success) return "";
                return m.Groups[1].Value == "Fault" ? "SOAP Fault" : m.Groups[1].Value;
            }
            catch (Exception) { return ""; }
        }
    }
}

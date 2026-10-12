// Stackshot - Saves that never leave a file half written.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.IO;

namespace Stackshot
{
    internal static class AtomicFile
    {
        // Folder whose files are swapped by copying over them instead of File.Replace: the stack watches it, and the
        // renames File.Replace makes there read as the capture being deleted. Tests point it elsewhere.
        internal static string WatchedFolder = Settings.TempDir;

        // The whole new content goes to a temporary file next to the target first, so a failed encode, a full disk or
        // a denied write leaves the old file intact; only then does it take the target's place.
        internal static void Write(string file, Action<Stream> write)
        {
            string full = Path.GetFullPath(file);
            string dir = Path.GetDirectoryName(full);
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            string tmp = Path.Combine(dir, Path.GetFileName(full) + ".~" + id + ".tmp");
            // A target near the 259-character path limit: a 9-character name in the same folder (still the same volume,
            // so the swap stays a rename; Windows keeps folders under 248 characters, so it always fits), or the long one
            // would fail before anything is written.
            if (tmp.Length > 259) tmp = Path.Combine(dir, "~" + id);
            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(fs);
                    fs.Flush(true);
                }
                if (!File.Exists(full)) File.Move(tmp, full);
                else if (SameFolder(dir, WatchedFolder)) File.Copy(tmp, full, true);
                else
                {
                    try { File.Replace(tmp, full, null, true); }
                    catch (IOException)
                    {
                        // Held open by a viewer that allows writing but not deleting: overwrite it as before.
                        File.Copy(tmp, full, true);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        internal static void Write(string file, byte[] data)
        {
            Write(file, delegate(Stream s) { s.Write(data, 0, data.Length); });
        }

        internal static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        }
    }
}

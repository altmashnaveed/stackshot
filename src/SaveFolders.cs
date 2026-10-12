// Stackshot - Where kept captures live: the save folder and, with SaveSubfolder, one folder per month (2026-10) or per
// year and month (2026\10) inside it.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Stackshot
{
    public static class SaveFolders
    {
        static readonly Regex MonthRx = new Regex(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
        static readonly Regex YearRx = new Regex(@"^\d{4}$", RegexOptions.Compiled);
        static readonly Regex MonthOnlyRx = new Regex(@"^(0[1-9]|1[0-2])$", RegexOptions.Compiled);

        // The folder a capture kept now goes to (not created here).
        public static string Target(Settings s, DateTime when)
        {
            string root = s.SaveFolder;
            switch (s.SaveSubfolder)
            {
                case 1: return Path.Combine(root, when.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                case 2: return Path.Combine(root, when.ToString("yyyy", CultureInfo.InvariantCulture), when.ToString("MM", CultureInfo.InvariantCulture));
                default: return root;
            }
        }

        // The save folder and every dated subfolder in it (whatever SaveSubfolder says now: it may have changed), newest
        // names first after the save folder itself. Folders that can't be listed are left out.
        public static List<string> All(Settings s)
        {
            List<string> o = new List<string>();
            string root = s.SaveFolder;
            if (string.IsNullOrEmpty(root)) return o;
            o.Add(root);
            try
            {
                if (!Directory.Exists(root)) return o;
                List<string> dated = new List<string>();
                foreach (string d in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(d);
                    if (MonthRx.IsMatch(name)) dated.Add(d);
                    else if (YearRx.IsMatch(name))
                    {
                        try
                        {
                            foreach (string m in Directory.GetDirectories(d))
                                if (MonthOnlyRx.IsMatch(Path.GetFileName(m))) dated.Add(m);
                        }
                        catch (Exception) { }
                    }
                }
                dated.Sort(delegate(string a, string b) { return string.Compare(Key(root, b), Key(root, a), StringComparison.Ordinal); });
                o.AddRange(dated);
            }
            catch (Exception) { }
            return o;
        }

        // "2026-10" for both layouts, to sort them together.
        static string Key(string root, string dir)
        {
            string rel = dir.Substring(Math.Min(dir.Length, root.TrimEnd('\\').Length)).Trim('\\');
            return rel.Replace('\\', '-');
        }

        // True when dir is the save folder or one of its dated subfolders.
        public static bool Inside(Settings s, string dir)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(s.SaveFolder)) return false;
            string root, d;
            try
            {
                root = Path.GetFullPath(s.SaveFolder).TrimEnd('\\');
                d = Path.GetFullPath(dir).TrimEnd('\\');
            }
            catch (Exception) { return false; }
            if (string.Equals(root, d, StringComparison.OrdinalIgnoreCase)) return true;
            if (!d.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = d.Substring(root.Length + 1).Split('\\');
            if (parts.Length == 1) return MonthRx.IsMatch(parts[0]);
            return parts.Length == 2 && YearRx.IsMatch(parts[0]) && MonthOnlyRx.IsMatch(parts[1]);
        }
    }
}

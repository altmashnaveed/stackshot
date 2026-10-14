// Stackshot - What each version brings, in plain Spanish (docs/novedades.md): the one running carries its own, and a
// release on GitHub carries the next one's between <!-- novedades --> markers, so it can be told before updating.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot
{
    public static class News
    {
        public sealed class Item
        {
            public string Title, Text;
        }

        public const string Start = "<!-- novedades -->", End = "<!-- /novedades -->";

        static string embedded;
        static bool loaded;

        // docs/novedades.md, built into the program.
        static string Embedded()
        {
            if (loaded) return embedded;
            loaded = true;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("novedades.md"))
                    if (s != null) using (StreamReader r = new StreamReader(s, Encoding.UTF8)) embedded = r.ReadToEnd();
            }
            catch (Exception ex) { ShotStack.Log("Novedades: " + ex.Message); }
            return embedded;
        }

        // The newest version described in the program and its news (none: null and an empty list).
        public static List<Item> Newest(out string version)
        {
            version = null;
            string md = Embedded();
            if (md == null) return new List<Item>();
            Match m = Regex.Match(md, @"^## (\d+\.\d+\.\d+)\s*$", RegexOptions.Multiline);
            if (!m.Success) return new List<Item>();
            version = m.Groups[1].Value;
            return Section(md, version);
        }

        // The news of exactly this version, as carried by the program (empty if it has none).
        public static List<Item> Of(string version)
        {
            string md = Embedded();
            return md == null ? new List<Item>() : Section(md, version);
        }

        static List<Item> Section(string md, string version)
        {
            List<string> lines = new List<string>();
            bool inside = false;
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## ", StringComparison.Ordinal)) { inside = raw.Substring(3).Trim() == version; continue; }
                if (inside) lines.Add(raw);
            }
            return Bullets(lines);
        }

        // The news a release on GitHub carries for its version (empty if it has none, as releases before 2.2.0).
        public static List<Item> FromRelease(string body)
        {
            if (string.IsNullOrEmpty(body)) return new List<Item>();
            int a = body.IndexOf(Start, StringComparison.Ordinal), b = a < 0 ? -1 : body.IndexOf(End, a, StringComparison.Ordinal);
            if (a < 0 || b < 0) return new List<Item>();
            string part = body.Substring(a + Start.Length, b - a - Start.Length);
            return Bullets(new List<string>(part.Replace("\r", "").Split('\n')));
        }

        // "- **Title.** Text": the title and its explanation, without Markdown.
        static List<Item> Bullets(List<string> lines)
        {
            List<Item> items = new List<Item>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
                line = line.Substring(2).Trim();
                Match m = Regex.Match(line, @"^\*\*(.+?)\*\*\s*(.*)$");
                Item it = m.Success ? new Item { Title = Plain(m.Groups[1].Value), Text = Plain(m.Groups[2].Value) } : new Item { Title = Plain(line), Text = "" };
                if (it.Title.Length > 0) items.Add(it);
                if (items.Count == 20) break;
            }
            return items;
        }

        static string Plain(string s)
        {
            s = Regex.Replace(s, @"<[^>]+>|\*\*|`", "");
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
            return s.Trim();
        }

        // A short line for a notice: the first titles, without their final full stop.
        public static string Headline(List<Item> items, int count)
        {
            List<string> t = new List<string>();
            for (int i = 0; i < items.Count && t.Count < count; i++) t.Add(items[i].Title.TrimEnd('.'));
            return string.Join(" \u00B7 ", t.ToArray());
        }
    }
}

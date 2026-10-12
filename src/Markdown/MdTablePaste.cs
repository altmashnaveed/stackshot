// Stackshot - Tables copied from a spreadsheet (Excel, LibreOffice, Google Sheets) pasted as Markdown tables.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Stackshot
{
    internal static class MdTablePaste
    {
        static readonly Regex TagRx = new Regex(@"<!--.*?-->|<(/?)([A-Za-z][A-Za-z0-9:\-]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex SpanRx = new Regex(@"\b(colspan|rowspan)\s*=\s*[""']?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex SpaceRx = new Regex(@"[ \t\r\n\f]+", RegexOptions.Compiled);
        const int MaxSpan = 1000;

        // A table copied from a spreadsheet, as the clipboard had it.
        internal sealed class Sheet
        {
            public string Text, Html, Csv;
        }

        // What the clipboard holds when it is a table from a spreadsheet, or null when it is not one: the plain text must be
        // tab-separated with the same two or more columns on every row, and a spreadsheet's own format must come with it
        // ("XML Spreadsheet", "Csv" or an HTML table), so text that merely has tabs in it (code) stays text. Quick enough
        // for the UI thread; Convert does the rest.
        internal static Sheet Read(IDataObject d)
        {
            if (d == null) return null;
            string text = Str(Get(d, DataFormats.UnicodeText), false) ?? Str(Get(d, DataFormats.Text), false);
            if (text == null || text.IndexOf('\t') < 0) return null;
            bool xls = Has(d, "XML Spreadsheet"), csv = Has(d, "Csv"), html = Has(d, DataFormats.Html);
            if (!xls && !csv && !html) return null;
            // The shape first, without making the cells: code indented by tabs is told apart before anything is split.
            bool blankFirst;
            if (!Shape(text, out blankFirst)) return null;
            // Only an HTML table speaks for it: a first column empty all the way down is a code listing, not a sheet.
            if (!xls && !csv && blankFirst) return null;
            string h = html ? Str(Get(d, DataFormats.Html), false) : null;
            bool table = h != null && HasTable(h);
            if (!xls && !csv && !table) return null;
            // The cells are split by Convert (off the UI thread for a big sheet).
            return new Sheet { Text = text, Html = table ? h : null, Csv = csv ? Str(Get(d, "Csv"), true) : null };
        }

        // The Markdown table: cells from the HTML table or the CSV when they have the text's shape (they keep the line
        // breaks inside cells and what the text cannot quote), else from the text. Any thread.
        internal static string Convert(Sheet s, out int rows, out int cols)
        {
            rows = cols = 0;
            List<List<string>> tsv = Tsv(s.Text);
            if (tsv == null) return null;
            List<List<string>> grid = null;
            if (s.Html != null)
            {
                List<List<string>> h = Html(s.Html);
                if (h != null && Same(h, tsv)) grid = h;
            }
            if (grid == null && s.Csv != null)
            {
                List<List<string>> g = Csv(s.Csv, tsv[0].Count);
                if (g != null && Same(g, tsv) && Count(s.Csv, '?') <= Count(s.Text, '?')) grid = g;
            }
            if (grid == null) grid = tsv;
            rows = grid.Count;
            cols = grid[0].Count;
            return Markdown(grid);
        }

        // Both at once: the table for the clipboard's data, or null.
        internal static string FromData(IDataObject d, out int rows, out int cols)
        {
            rows = cols = 0;
            Sheet s = Read(d);
            return s == null ? null : Convert(s, out rows, out cols);
        }

        static bool Has(IDataObject d, string f)
        {
            try { return d.GetDataPresent(f); } catch (Exception) { return false; }
        }

        static object Get(IDataObject d, string f)
        {
            try { return d.GetDataPresent(f) ? d.GetData(f) : null; } catch (Exception) { return null; }
        }

        // Clipboard data as text: strings as they are, streams as UTF-8 (ansi: the system code page when they are not
        // valid UTF-8, as Excel writes its CSV).
        static string Str(object o, bool ansi)
        {
            string s = o as string;
            if (s != null) return s;
            Stream st = o as Stream;
            if (st == null) return null;
            byte[] b;
            using (MemoryStream ms = new MemoryStream()) { st.CopyTo(ms); b = ms.ToArray(); }
            int n = b.Length;
            while (n > 0 && b[n - 1] == 0) n--;
            int at = n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            try { return new UTF8Encoding(false, true).GetString(b, at, n - at); }
            catch (DecoderFallbackException) { return ansi ? Encoding.Default.GetString(b, 0, n) : Encoding.UTF8.GetString(b, at, n - at); }
        }

        static int Count(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }

        static bool Same(List<List<string>> a, List<List<string>> b)
        {
            return a.Count == b.Count && a.Count > 0 && a[0].Count == b[0].Count;
        }

        // "<table" followed by a space or ">", any case.
        static bool HasTable(string h)
        {
            for (int at = h.IndexOf("<table", StringComparison.OrdinalIgnoreCase); at >= 0; at = h.IndexOf("<table", at + 6, StringComparison.OrdinalIgnoreCase))
                if (at + 6 < h.Length && (h[at + 6] == '>' || char.IsWhiteSpace(h[at + 6]))) return true;
            return false;
        }

        // Tab-separated text read as Split reads it, without making the cells: true when every row has the same two or more
        // cells; blankFirst when the first cell of every row is blank.
        static bool Shape(string t, out bool blankFirst)
        {
            blankFirst = true;
            if (t == null) return false;
            int n = t.Length;
            while (n > 0 && (t[n - 1] == '\n' || t[n - 1] == '\r')) n--;
            if (n == 0) return false;
            int cols = -1, cells = 0, i = 0;
            while (true)
            {
                bool blank = true, quoted = false;
                if (i < n && t[i] == '"')
                {
                    int j = i + 1;
                    bool closed = false;
                    while (j < n)
                    {
                        if (t[j] == '"')
                        {
                            if (j + 1 < n && t[j + 1] == '"') { blank = false; j += 2; continue; }
                            closed = true;
                            j++;
                            break;
                        }
                        if (blank && !char.IsWhiteSpace(t[j])) blank = false;
                        j++;
                    }
                    if (closed && (j >= n || t[j] == '\t' || t[j] == '\n' || t[j] == '\r')) { quoted = true; i = j; }
                    else blank = true;
                }
                if (!quoted)
                    for (; i < n; i++)
                    {
                        char c = t[i];
                        if (c == '\t' || c == '\n' || c == '\r') break;
                        if (blank && !char.IsWhiteSpace(c)) blank = false;
                    }
                if (cells++ == 0 && !blank) blankFirst = false;
                if (i < n && t[i] == '\t') { i++; continue; }
                if (cols < 0) cols = cells;
                if (cols < 2 || cells != cols) return false;
                if (i >= n) return true;
                i += t[i] == '\r' && i + 1 < n && t[i + 1] == '\n' ? 2 : 1;
                cells = 0;
            }
        }

        // Tab-separated text as rows, or null unless every row has the same two or more cells.
        internal static List<List<string>> Tsv(string text)
        {
            List<List<string>> rows = Split(text, '\t', true);
            if (rows == null || rows[0].Count < 2) return null;
            foreach (List<string> r in rows) if (r.Count != rows[0].Count) return null;
            return rows;
        }

        // CSV as rows, the separator found from the cells (the system's list separator first, as Excel writes it); null
        // when no separator gives every row the same cells (columns: that many, when above zero).
        internal static List<List<string>> Csv(string text, int columns)
        {
            List<char> seps = new List<char>();
            string ls = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            if (ls.Length == 1) seps.Add(ls[0]);
            foreach (char c in new char[] { ',', ';', '\t' }) if (!seps.Contains(c)) seps.Add(c);
            foreach (char sep in seps)
            {
                List<List<string>> rows = Split(text, sep);
                if (rows == null) return null;
                int n = rows[0].Count;
                bool ok = n >= 2 && (columns <= 0 || n == columns);
                foreach (List<string> r in rows) if (r.Count != n) { ok = false; break; }
                if (ok) return rows;
            }
            return null;
        }

        // Fields separated by sep, one row per line. A field that starts with a quote may hold separators, line breaks
        // and doubled quotes, as spreadsheets write them; when its closing quote is not followed by a separator or the end
        // of the line it is plain text after all. The text's last line break ends the last row.
        internal static List<List<string>> Split(string t, char sep)
        {
            return Split(t, sep, false);
        }

        // sheet: null as soon as the first row has fewer than two cells or a row has another number of cells than the
        // first, so text that is no sheet (code with tabs) is not split whole on every paste.
        static List<List<string>> Split(string t, char sep, bool sheet)
        {
            if (t == null) return null;
            int n = t.Length;
            while (n > 0 && (t[n - 1] == '\n' || t[n - 1] == '\r')) n--;
            if (n == 0) return null;
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (true)
            {
                sb.Length = 0;
                bool quoted = false;
                if (i < n && t[i] == '"')
                {
                    int j = i + 1;
                    bool closed = false;
                    while (j < n)
                    {
                        if (t[j] == '"')
                        {
                            if (j + 1 < n && t[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                            closed = true;
                            j++;
                            break;
                        }
                        sb.Append(t[j]);
                        j++;
                    }
                    if (closed && (j >= n || t[j] == sep || t[j] == '\n' || t[j] == '\r')) { quoted = true; i = j; }
                    else sb.Length = 0;
                }
                if (!quoted) while (i < n && t[i] != sep && t[i] != '\n' && t[i] != '\r') sb.Append(t[i++]);
                row.Add(quoted ? sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n') : sb.ToString());
                if (i >= n) { rows.Add(row); break; }
                if (t[i] == sep) { i++; continue; }
                i += t[i] == '\r' && i + 1 < n && t[i + 1] == '\n' ? 2 : 1;
                rows.Add(row);
                if (sheet && (rows[0].Count < 2 || row.Count != rows[0].Count)) return null;
                row = new List<string>();
            }
            return rows;
        }

        // ---- HTML

        // The first table of an HTML fragment as rows of cell text, merged cells spread over the cells they cover (empty);
        // null when there is no table. Line breaks inside a cell (<br>, paragraphs) are kept as '\n'.
        internal static List<List<string>> Html(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            Grid g = null;
            int depth = 0, at = 0;
            StringBuilder cell = null;
            int span = 1, down = 1;
            string skip = null;
            for (Match m = TagRx.Match(html); m.Success; m = m.NextMatch())
            {
                if (g != null && cell != null && skip == null && m.Index > at) Text(cell, html.Substring(at, m.Index - at));
                at = m.Index + m.Length;
                if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
                bool close = m.Groups[1].Length > 0;
                string tag = m.Groups[2].Value.ToLowerInvariant();
                if (skip != null) { if (close && tag == skip) skip = null; continue; }
                if (!close && (tag == "style" || tag == "script" || tag == "title")) { skip = tag; continue; }
                if (tag == "table")
                {
                    if (!close) { if (depth++ == 0) g = new Grid(); else if (cell != null) Break(cell); continue; }
                    if (depth > 0 && --depth == 0) break;
                    continue;
                }
                if (g == null) continue;
                if (depth > 1)
                {
                    // A table inside a cell: its text joins the cell's.
                    if (cell != null && (tag == "td" || tag == "th") && !close) cell.Append(' ');
                    else if (cell != null && (tag == "tr" || tag == "br") && !close) Break(cell);
                    continue;
                }
                if (tag == "tr")
                {
                    if (cell != null) { g.Place(Clean(cell), span, down); cell = null; }
                    if (close) g.EndRow(); else g.StartRow();
                }
                else if (tag == "td" || tag == "th")
                {
                    if (cell != null) { g.Place(Clean(cell), span, down); cell = null; }
                    if (close) continue;
                    if (!g.InRow) g.StartRow();
                    cell = new StringBuilder();
                    span = down = 1;
                    foreach (Match a in SpanRx.Matches(m.Groups[3].Value))
                    {
                        int v;
                        if (!int.TryParse(a.Groups[2].Value, out v)) continue;
                        v = Math.Max(1, Math.Min(MaxSpan, v));
                        if (a.Groups[1].Value.ToLowerInvariant() == "colspan") span = v; else down = v;
                    }
                }
                else if (cell != null && (tag == "br" || tag == "p" || tag == "div" || tag == "li")) Break(cell);
            }
            if (g == null) return null;
            if (cell != null) g.Place(Clean(cell), span, down);
            g.EndRow();
            return g.Rows.Count > 0 ? g.Done() : null;
        }

        static void Text(StringBuilder cell, string raw)
        {
            string t = SpaceRx.Replace(System.Net.WebUtility.HtmlDecode(raw), " ").Replace('\u00A0', ' ');
            cell.Append(t);
        }

        static void Break(StringBuilder cell)
        {
            if (cell.Length > 0 && cell[cell.Length - 1] != '\n') cell.Append('\n');
        }

        // Each line of a cell trimmed, and no empty lines at its start or end.
        static string Clean(StringBuilder cell)
        {
            string[] lines = cell.ToString().Split('\n');
            List<string> o = new List<string>();
            foreach (string l in lines) o.Add(l.Trim());
            while (o.Count > 0 && o[0].Length == 0) o.RemoveAt(0);
            while (o.Count > 0 && o[o.Count - 1].Length == 0) o.RemoveAt(o.Count - 1);
            return string.Join("\n", o.ToArray());
        }

        // Rows being filled, with the cells merged down from rows above.
        sealed class Grid
        {
            public readonly List<List<string>> Rows = new List<List<string>>();
            readonly List<int> below = new List<int>();   // per column: rows still covered by a cell above
            List<string> row;
            bool[] covered;
            int col;

            public bool InRow { get { return row != null; } }

            public void StartRow()
            {
                EndRow();
                row = new List<string>();
                covered = new bool[below.Count];
                for (int c = 0; c < below.Count; c++) covered[c] = below[c] > 0;
                col = 0;
            }

            public void Place(string text, int span, int down)
            {
                if (row == null) StartRow();
                while (col < covered.Length && covered[col]) { Set(col, ""); col++; }
                for (int k = 0; k < span; k++)
                {
                    Set(col + k, k == 0 ? text : "");
                    while (below.Count <= col + k) below.Add(0);
                    if (down > 1) below[col + k] = Math.Max(below[col + k], down - 1);
                }
                col += span;
            }

            void Set(int c, string s)
            {
                while (row.Count <= c) row.Add(null);
                if (row[c] == null) row[c] = s;
            }

            public void EndRow()
            {
                if (row == null) return;
                for (int c = 0; c < covered.Length; c++)
                {
                    if (!covered[c]) continue;
                    Set(c, "");
                    below[c]--;
                }
                if (row.Count > 0) Rows.Add(row);
                row = null;
            }

            public List<List<string>> Done()
            {
                int n = 0;
                foreach (List<string> r in Rows) n = Math.Max(n, r.Count);
                foreach (List<string> r in Rows)
                {
                    for (int c = 0; c < r.Count; c++) if (r[c] == null) r[c] = "";
                    while (r.Count < n) r.Add("");
                }
                return Rows;
            }
        }

        // ---- Markdown

        // The rows as an aligned Markdown table, the first one as its header (as Alinear la tabla writes it).
        internal static string Markdown(List<List<string>> grid)
        {
            List<List<string>> rows = new List<List<string>>();
            foreach (List<string> r in grid)
            {
                List<string> o = new List<string>();
                foreach (string c in r) o.Add(Cell(c));
                rows.Add(o);
            }
            List<string> delim = new List<string>();
            for (int c = 0; c < rows[0].Count; c++) delim.Add("---");
            return string.Join("\n", MarkdownTools.FormatTable(rows, delim).ToArray());
        }

        // A cell trimmed, its line breaks as <br>, pipes and backslashes escaped.
        internal static string Cell(string c)
        {
            string[] lines = (c ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Split('\n');
            List<string> o = new List<string>();
            foreach (string l in lines) o.Add(l.Trim().Replace("\\", "\\\\").Replace("|", "\\|"));
            while (o.Count > 0 && o[0].Length == 0) o.RemoveAt(0);
            while (o.Count > 0 && o[o.Count - 1].Length == 0) o.RemoveAt(o.Count - 1);
            return string.Join("<br>", o.ToArray());
        }
    }
}

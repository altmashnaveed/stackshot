// Round 2 hunt (h2-docs): encodings and saving, find and replace, Markdown parse and tidy, XML format and errors.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static byte[] H2Join(params byte[][] parts)
        {
            MemoryStream ms = new MemoryStream();
            foreach (byte[] p in parts) ms.Write(p, 0, p.Length);
            return ms.ToArray();
        }

        static void H2SameBytes(byte[] exp, byte[] got, string what)
        {
            Same(BitConverter.ToString(exp), BitConverter.ToString(got), what);
        }

        static FindOptions H2Opts(bool regex, bool word, bool cs)
        {
            return new FindOptions { Regex = regex, Word = word, Case = cs };
        }

        static string H2Kinds(List<MdBlock> bs)
        {
            return string.Join(", ", bs.Select(b => b.Kind.ToString()).ToArray());
        }

        static int H2ListDepth(List<MdBlock> bs)
        {
            int best = 0;
            foreach (MdBlock b in bs)
            {
                if (b.Kind != MdKind.List) continue;
                int d = 1;
                foreach (MdItem it in b.Items) d = Math.Max(d, 1 + H2ListDepth(it.Blocks));
                best = Math.Max(best, d);
            }
            return best;
        }

        static string H2TextOf(string xml, string name)
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Descendants(name).First().Value;
        }

        static void Hunt2DocsSuite()
        {
            // ---- Encodings and saving

            Test("hunt2docs", "empty-and-blank-md-open", delegate
            {
                string dir = WorkDir("empty");
                try
                {
                    string p = Path.Combine(dir, "vacio.md");
                    File.WriteAllBytes(p, new byte[0]);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "an empty .md cannot be opened: " + err);
                    File.WriteAllBytes(p, Encoding.UTF8.GetBytes("\r\n  \r\n"));
                    d = DocFile.Load(p, out err);
                    Check(d != null, "a blank .md cannot be opened: " + err);
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "utf16le-bom-edit-bytes", delegate
            {
                string dir = WorkDir("u16le");
                try
                {
                    string p = Path.Combine(dir, "a.md");
                    Encoding le = new UnicodeEncoding(false, false);
                    File.WriteAllBytes(p, H2Join(new byte[] { 0xFF, 0xFE }, le.GetBytes("Hola\r\nmundo\r\n")));
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Text == "Hola\nmundo\n", "text: " + d.Text);
                    Check(d.Save(d.Text.Replace("mundo", "tierra"), null, out err), "save: " + err);
                    H2SameBytes(H2Join(new byte[] { 0xFF, 0xFE }, le.GetBytes("Hola\r\ntierra\r\n")), File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "utf16be-bom-edit-bytes", delegate
            {
                string dir = WorkDir("u16be");
                try
                {
                    string p = Path.Combine(dir, "a.md");
                    Encoding be = new UnicodeEncoding(true, false);
                    File.WriteAllBytes(p, H2Join(new byte[] { 0xFE, 0xFF }, be.GetBytes("Hola\r\nmundo\r\n")));
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save(d.Text.Replace("Hola", "Adi\u00F3s"), null, out err), "save: " + err);
                    H2SameBytes(H2Join(new byte[] { 0xFE, 0xFF }, be.GetBytes("Adi\u00F3s\r\nmundo\r\n")), File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "utf16-nobom-md-opens", delegate
            {
                string dir = WorkDir("u16nb");
                try
                {
                    string p = Path.Combine(dir, "sin-bom.md");
                    File.WriteAllBytes(p, new UnicodeEncoding(false, false).GetBytes("# T\u00EDtulo\r\nun texto\r\n"));
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "UTF-16 LE without BOM refused: " + err);
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "cp1252-undefined-bytes-kept", delegate
            {
                string dir = WorkDir("cp1252");
                try
                {
                    string p = Path.Combine(dir, "w.md");
                    Encoding w = Encoding.GetEncoding(1252);
                    byte[] head = new byte[] { (byte)'a', 0x81, 0x8D, 0x8F, 0x90, 0x9D, (byte)' ', 0xE9, 0x0D, 0x0A };
                    byte[] orig = H2Join(head, w.GetBytes("fin\r\n"));
                    File.WriteAllBytes(p, orig);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save(d.Text, null, out err), "save unchanged: " + err);
                    H2SameBytes(orig, File.ReadAllBytes(p), "unchanged bytes");
                    Check(d.Save(d.Text.Replace("fin", "final"), null, out err), "save edit: " + err);
                    H2SameBytes(H2Join(head, w.GetBytes("final\r\n")), File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "invalid-utf8-bytes-kept-after-edit", delegate
            {
                string dir = WorkDir("badutf8");
                try
                {
                    string p = Path.Combine(dir, "bad.md");
                    byte[] orig = new byte[] { (byte)'a', (byte)'b', 0xFF, 0xFE, (byte)' ', (byte)'c', (byte)'d', 0x0D, 0x0A };
                    File.WriteAllBytes(p, orig);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save(d.Text.Replace("cd", "ce"), null, out err), "save edit: " + err);
                    H2SameBytes(new byte[] { (byte)'a', (byte)'b', 0xFF, 0xFE, (byte)' ', (byte)'c', (byte)'e', 0x0D, 0x0A }, File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "no-final-newline-edit-at-end", delegate
            {
                string dir = WorkDir("nofinal");
                try
                {
                    string p = Path.Combine(dir, "f.md");
                    byte[] orig = Encoding.ASCII.GetBytes("a\r\nb");
                    File.WriteAllBytes(p, orig);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save(d.Text, null, out err), "save unchanged: " + err);
                    H2SameBytes(orig, File.ReadAllBytes(p), "unchanged bytes");
                    Check(d.Save(d.Text + "\n", null, out err), "save with newline: " + err);
                    H2SameBytes(Encoding.ASCII.GetBytes("a\r\nb\r\n"), File.ReadAllBytes(p), "bytes after adding a final line break");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "double-bom-kept-after-edit", delegate
            {
                string dir = WorkDir("bom2");
                try
                {
                    string p = Path.Combine(dir, "b.md");
                    byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
                    File.WriteAllBytes(p, H2Join(bom, bom, Encoding.ASCII.GetBytes("x")));
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save("y", null, out err), "save: " + err);
                    H2SameBytes(H2Join(bom, bom, Encoding.ASCII.GetBytes("y")), File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "xml-declared-latin1-edit-bytes", delegate
            {
                string dir = WorkDir("latin1");
                try
                {
                    string p = Path.Combine(dir, "l.xml");
                    byte[] head = Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\r\n<a>caf");
                    byte[] tail = new byte[] { 0xE9, (byte)'<', (byte)'/', (byte)'a', (byte)'>', 0x0D, 0x0A };
                    File.WriteAllBytes(p, H2Join(head, tail));
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "load: " + err);
                    Check(d.Save(d.Text.Replace("caf\u00E9", "caf\u00E9s"), null, out err), "save: " + err);
                    H2SameBytes(H2Join(head, new byte[] { 0xE9, (byte)'s' }, tail.Skip(1).ToArray()), File.ReadAllBytes(p), "bytes after edit");
                }
                finally { DeleteDir(dir); }
            });

            // ---- Find and replace

            Test("hunt2docs", "regex-caret-replace-all", delegate
            {
                int n; bool to;
                string r = TextSearch.ReplaceAll("a\nb\nc", "^", "> ", H2Opts(true, false, false), out n, out to);
                Put("count", n);
                Same("> a\n> b\n> c", r, "replace ^ (regex) on three lines");
            });

            Test("hunt2docs", "regex-dollar-replace-all", delegate
            {
                int n; bool to;
                string r = TextSearch.ReplaceAll("ab\ncd", "$", "!", H2Opts(true, false, false), out n, out to);
                Put("count", n);
                Same("ab!\ncd!", r, "replace $ (regex)");
            });

            Test("hunt2docs", "regex-word-boundary-replace-all", delegate
            {
                int n; bool to;
                string r = TextSearch.ReplaceAll("ab cd", "\\b", "|", H2Opts(true, false, false), out n, out to);
                Put("count", n);
                Same("|ab| |cd|", r, "replace \\b (regex)");
            });

            Test("hunt2docs", "replacement-groups-and-dollars", delegate
            {
                int n; bool to;
                Same("Smith, John$", TextSearch.ReplaceAll("John Smith", "(\\w+) (\\w+)", "$2, $1$$", H2Opts(true, false, false), out n, out to), "regex groups with $$");
                Same("$1$$ y", TextSearch.ReplaceAll("x y", "x", "$1$$", H2Opts(false, false, true), out n, out to), "plain replacement is literal");
            });

            Test("hunt2docs", "lookbehind-and-word-alternation", delegate
            {
                int n; bool to;
                Same("xb ya", TextSearch.ReplaceAll("xa ya", "(?<=x)a", "b", H2Opts(true, false, false), out n, out to), "lookbehind");
                Same("X catalog X.", TextSearch.ReplaceAll("cat catalog cat.", "cat|dog", "X", H2Opts(true, true, true), out n, out to), "whole word with alternation");
            });

            Test("hunt2docs", "case-insensitive-accents", delegate
            {
                bool to1, to2;
                List<Hit> plain = TextSearch.All("\u00C9COLE \u00E9cole \u00D1AND\u00DA \u00F1and\u00FA", "\u00E9cole", H2Opts(false, false, false), int.MaxValue, out to1);
                Check(plain.Count == 2, "plain accents: " + plain.Count + " hits");
                List<Hit> rx = TextSearch.All("\u00C9COLE \u00E9cole", "\u00C9COLE", H2Opts(true, false, false), int.MaxValue, out to2);
                Check(rx.Count == 2, "regex accents: " + rx.Count + " hits");
            });

            Test("hunt2docs", "replace-all-10mb-plain-and-regex", delegate
            {
                string unit = "lorem ipsum foo dolor sit amet, ";
                StringBuilder sb = new StringBuilder();
                int units = 0;
                while (sb.Length < 10 * 1024 * 1024) { sb.Append(unit); units++; }
                string text = sb.ToString();
                int count; bool to;
                Stopwatch sw = Stopwatch.StartNew();
                string r = TextSearch.ReplaceAll(text, "foo", "bar", H2Opts(false, false, true), out count, out to);
                double ms1 = sw.Elapsed.TotalMilliseconds;
                Check(count == units && r.Length == text.Length, "plain count " + count + " of " + units);
                Budget("plain-10mb", ms1, 3000);
                sw.Restart();
                string r2 = TextSearch.ReplaceAll(text, "fo+", "bar", H2Opts(true, false, false), out count, out to);
                double ms2 = sw.Elapsed.TotalMilliseconds;
                Check(!to, "regex search timed out on 10 MB");
                Check(count == units && r2.Length == text.Length, "regex count " + count + " of " + units);
                Budget("regex-10mb", ms2, 3000);
            });

            // ---- Markdown parse and tidy

            Test("hunt2docs", "setext-h2-with-dashes", delegate
            {
                List<MdBlock> bs = MarkdownDoc.Parse("T\u00EDtulo\n-----\n\ntexto\n");
                Add("blocks: " + H2Kinds(bs));
                Check(bs.Count > 0 && bs[0].Kind == MdKind.Heading && bs[0].Level == 2, "expected an H2 heading, got " + H2Kinds(bs));
            });

            Test("hunt2docs", "setext-h1-with-equals", delegate
            {
                List<MdBlock> bs = MarkdownDoc.Parse("T\u00EDtulo\n=====\n");
                Check(bs.Count == 1 && bs[0].Kind == MdKind.Heading && bs[0].Level == 1, "expected an H1 heading, got " + H2Kinds(bs));
            });

            Test("hunt2docs", "tidy-fence-in-list-code-untouched", delegate
            {
                string src = "1. Paso\n\n    ```bash\n    * uno\n    # dos\n    ```\n";
                string outp = MarkdownTools.Tidy(src);
                Add("tidy output: " + outp.Replace("\n", "\\n"));
                Check(outp.Contains("    * uno"), "a bullet inside an indented code block was rewritten");
            });

            Test("hunt2docs", "tidy-fence-unindented-untouched", delegate
            {
                string src = "```\n* uno\n# dos\n```\n";
                Same(src, MarkdownTools.Tidy(src), "fenced code at column 0");
            });

            Test("hunt2docs", "table-stray-backtick-cells", delegate
            {
                List<MdBlock> bs = MarkdownDoc.Parse("| a | b |\n|---|---|\n| it`s | x |\n");
                Check(bs.Count == 1 && bs[0].Kind == MdKind.Table, "not a table: " + H2Kinds(bs));
                Put("cells", bs[0].Rows[0].Count);
                Check(bs[0].Rows[0].Count == 2, "row has " + bs[0].Rows[0].Count + " cells, expected 2");
                Same("x", bs[0].Rows[0][1], "second cell");
            });

            Test("hunt2docs", "nested-lists-with-tabs", delegate
            {
                List<MdBlock> bs = MarkdownDoc.Parse("- a\n\t- b\n\t\t- c\n");
                Put("depth", H2ListDepth(bs));
                Check(H2ListDepth(bs) == 3, "list depth " + H2ListDepth(bs) + ", expected 3");
            });

            Test("hunt2docs", "ordered-list-start-kept", delegate
            {
                List<MdBlock> bs = MarkdownDoc.Parse("3. a\n4. b\n");
                Check(bs.Count == 1 && bs[0].Kind == MdKind.List && bs[0].Ordered && bs[0].Level == 3 && bs[0].Items.Count == 2, "start/items wrong: " + H2Kinds(bs));
            });

            Test("hunt2docs", "md-one-megabyte-line", delegate
            {
                StringBuilder sb = new StringBuilder();
                while (sb.Length < 1000000) sb.Append("*a _b [c](d) `e` ~~f~~ ");
                string line = sb.ToString();
                Stopwatch sw = Stopwatch.StartNew();
                List<MdBlock> bs = MarkdownDoc.Parse(line);
                Budget("parse-1mb-line", sw.Elapsed.TotalMilliseconds, 3000);
                Check(bs.Count == 1 && bs[0].Kind == MdKind.Paragraph, "blocks: " + H2Kinds(bs));
                sw.Restart();
                List<MdSpan> spans = MarkdownDoc.Inline(line);
                Budget("inline-1mb-line", sw.Elapsed.TotalMilliseconds, 3000);
                Check(spans.Count > 0, "no inline spans");
            });

            // ---- XML format, compact and errors

            Test("hunt2docs", "xml-nbsp-only-element-kept", delegate
            {
                string err;
                string indent = XmlDoc.Format("<a><b>&#160;</b></a>", true, out err);
                Check(indent != null, "format: " + err);
                string compact = XmlDoc.Format("<a><b>&#160;</b></a>", false, out err);
                Check(compact != null, "compact: " + err);
                string where = "indent: " + indent.Replace("\n", " ") + " | compact: " + compact;
                Put("indent", indent.Replace("\n", "\\n"));
                Put("compact", compact);
                Same("\u00A0", H2TextOf(indent, "b"), "text of b after indent format (" + where + ")");
                Same("\u00A0", H2TextOf(compact, "b"), "text of b after compact (" + where + ")");
            });

            Test("hunt2docs", "xml-unicode-space-only-kept", delegate
            {
                string err;
                string o = XmlDoc.Format("<a><b>&#x3000;</b><c>&#x2003;</c></a>", false, out err);
                Check(o != null, "format: " + err);
                Same("\u3000", H2TextOf(o, "b"), "ideographic space in b (output: " + o + ")");
                Same("\u2003", H2TextOf(o, "c"), "em space in c (output: " + o + ")");
            });

            Test("hunt2docs", "xml-tab-in-attribute-kept", delegate
            {
                string err;
                string o = XmlDoc.Format("<a v=\"x&#9;y\"/>", true, out err);
                Check(o != null, "format: " + err);
                Same("x\ty", XDocument.Parse(o).Root.Attribute("v").Value, "attribute value after format (output: " + o + ")");
            });

            Test("hunt2docs", "xml-cdata-comment-pi-kept", delegate
            {
                string src = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- top -->\n<?pi data?>\n<r>\n  <![CDATA[a < b & c]]>\n  <!-- in -->\n  <?p q?>\n</r>\n";
                string err;
                string o = XmlDoc.Format(src, true, out err);
                Check(o != null, "format: " + err);
                Check(o.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>"), "declaration: " + o);
                foreach (string part in new string[] { "<!-- top -->", "<?pi data?>", "<![CDATA[a < b & c]]>", "<!-- in -->", "<?p q?>" })
                    Check(o.Contains(part), "lost " + part + " in: " + o);
            });

            Test("hunt2docs", "xml-doctype-refused-friendly", delegate
            {
                string err;
                string o = XmlDoc.Format("<!DOCTYPE r [<!ENTITY e \"x\">]>\n<r>&e;</r>", true, out err);
                Check(o == null && err != null && err.Contains("DOCTYPE"), "DOCTYPE not reported: " + err);
            });

            Test("hunt2docs", "xml-namespaces-kept", delegate
            {
                string err;
                string o = XmlDoc.Format("<x:a xmlns:x=\"urn:u\" xmlns=\"urn:d\"><x:b>1</x:b><c>2</c></x:a>", true, out err);
                Check(o != null, "format: " + err);
                foreach (string part in new string[] { "xmlns:x=\"urn:u\"", "xmlns=\"urn:d\"", "<x:b>1</x:b>", "<c>2</c>" })
                    Check(o.Contains(part), "lost " + part + " in: " + o);
            });

            Test("hunt2docs", "xml-space-nested-default-in-preserve", delegate
            {
                string src = "<a xml:space=\"preserve\">\n  <b xml:space=\"default\">\n    <c>1</c>\n  </b>\n  <d>  x  </d>\n</a>";
                string err;
                string o = XmlDoc.Format(src, false, out err);
                Check(o != null, "compact: " + err);
                Check(o.StartsWith("<a xml:space=\"preserve\">\n  <b xml:space=\"default\"><c>1</c></b>\n  <d>  x  </d>"), "compact output: " + o);
            });

            Test("hunt2docs", "xml-format-compact-content-stable", delegate
            {
                string src = "<doc>\n  <p>Hola <b>mundo</b> !</p>\n  <![CDATA[x]]>\n  <!-- c -->\n  <v>&lt;&amp;&#x41;&quot;</v>\n  <e xml:space=\"preserve\">  sp  </e>\n</doc>\n";
                string err;
                string compact = XmlDoc.Format(src, false, out err);
                Check(compact != null, "compact: " + err);
                string indent = XmlDoc.Format(src, true, out err);
                Check(indent != null, "indent: " + err);
                string again = XmlDoc.Format(indent, false, out err);
                Check(again != null, "compact again: " + err);
                Same(compact, again, "compact of indented output differs from compact of original");
                Check(compact.Contains("<p>Hola <b>mundo</b> !</p>"), "mixed content changed: " + compact);
                Same("<&A\"", H2TextOf(compact, "v"), "entities value");
                Same("  sp  ", H2TextOf(compact, "e"), "preserved value");
            });

            Test("hunt2docs", "xml-20mb-refused-fast", delegate
            {
                string dir = WorkDir("xml20");
                try
                {
                    string p = Path.Combine(dir, "grande.xml");
                    File.WriteAllText(p, "<r>" + new string('a', 20 * 1024 * 1024) + "</r>", new UTF8Encoding(false));
                    string err;
                    Stopwatch sw = Stopwatch.StartNew();
                    DocFile d = DocFile.Load(p, out err);
                    Budget("load-20mb-xml", sw.Elapsed.TotalMilliseconds, 2000);
                    Check(d == null && err != null && err.Contains("8 MB"), "20 MB XML not refused with the limit: " + err);
                }
                finally { DeleteDir(dir); }
            });

            Test("hunt2docs", "xml-error-line-and-column", delegate
            {
                XmlModel m; string err; int line, col;
                bool ok = XmlDoc.Parse("<a>\n  <b>\n  </a>", out m, out err, out line, out col);
                Put("line", line);
                Put("col", col);
                Check(!ok, "mismatched tags accepted");
                Check(line == 3, "error line " + line + " (" + err + "), expected 3");
                Check(err.Contains("l\u00EDnea 2, columna 4"), "start tag position missing: " + err);
            });

            Test("hunt2docs", "xml-entity-error-line", delegate
            {
                XmlModel m; string err; int line, col;
                bool ok = XmlDoc.Parse("<a>\n\n  &nbsp;\n</a>", out m, out err, out line, out col);
                Check(!ok, "undefined entity accepted");
                Check(line == 3, "error line " + line + " (" + err + "), expected 3");
            });
        }
    }
}

// hunt: bug hunt over the Markdown and XML editor (added 2026-10-10): entities and malformed XML, deep nesting, XML
// formatting that must keep values, whole-word search with accents, edits at the ends of files and line breaks in
// CRLF files, tidy idempotence, HTML export of hostile links, sizes and a change that keeps its time stamp.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static void HuntSuite()
        {
            Test("hunt", "xml-external-entity-not-read", delegate
            {
                string dir = WorkDir("xxe");
                string secret = Path.Combine(dir, "secreto.txt");
                File.WriteAllText(secret, "SECRETO-4471", new UTF8Encoding(false));
                string uri = new Uri(secret).AbsoluteUri;
                string[] docs = {
                    "<?xml version=\"1.0\"?>\n<!DOCTYPE a [<!ENTITY x SYSTEM \"" + uri + "\">]>\n<a>&x;</a>\n",
                    "<?xml version=\"1.0\"?>\n<!DOCTYPE a SYSTEM \"" + uri + "\">\n<a>hola</a>\n",
                    "<?xml version=\"1.0\"?>\n<!DOCTYPE a [<!ENTITY % p SYSTEM \"" + uri + "\"> %p;]>\n<a>hola</a>\n",
                    "<a>&ext;</a>"
                };
                for (int i = 0; i < docs.Length; i++)
                {
                    XmlModel m;
                    string err;
                    int line, col;
                    bool ok = XmlDoc.Parse(docs[i], out m, out err, out line, out col);
                    Check(!ok && m == null, "doc " + i + " was read");
                    Check(err == null || err.IndexOf("SECRETO", StringComparison.Ordinal) < 0, "doc " + i + " error shows the file");
                    string ferr;
                    string fmt = XmlDoc.Format(docs[i], true, out ferr);
                    Check(fmt == null || fmt.IndexOf("SECRETO", StringComparison.Ordinal) < 0, "doc " + i + " formatted with the file content");
                }
                Note("4 documents refused, the file never read");
            });

            Test("hunt", "xml-billion-laughs-refused-fast", delegate
            {
                StringBuilder d = new StringBuilder("<?xml version=\"1.0\"?>\n<!DOCTYPE lolz [\n<!ENTITY lol \"lol\">\n");
                for (int i = 2; i <= 9; i++)
                {
                    d.Append("<!ENTITY lol" + i + " \"");
                    for (int k = 0; k < 10; k++) d.Append("&lol" + (i - 1) + ";");
                    d.Append("\">\n");
                }
                d.Append("]>\n<lolz>&lol9;</lolz>\n");
                XmlModel m;
                string err;
                int line, col;
                Stopwatch sw = Stopwatch.StartNew();
                bool ok = XmlDoc.Parse(d.ToString(), out m, out err, out line, out col);
                sw.Stop();
                Budget("billion-laughs", sw.Elapsed.TotalMilliseconds, 2000);
                Check(!ok && m == null, "billion laughs was parsed");
                Check(err != null, "no error message");
            });

            Test("hunt", "xml-malformed-always-refused", delegate
            {
                string[] bad = {
                    "<a><b></a>", "<a>&bad;</a>", "<a x=1/>", "<a>\u0001</a>", "<a><!-- x </a>", "<a/><b/>", "<a>",
                    "   ", "<?xml version='1.0'?>", "<a x='1' x='2'/>", "<a>\uD800</a>", "<a>&#0;</a>", "<a>&#xD800;</a>",
                    "text only", "<a>]]></a>", "<a></a >x", "<1a/>", "<a>&#x110000;</a>", "<a b=\"<\"/>", "<a>\uFFFE</a>"
                };
                List<string> msgs = new List<string>();
                for (int i = 0; i < bad.Length; i++)
                {
                    XmlModel m;
                    string err;
                    int line, col;
                    bool ok = XmlDoc.Parse(bad[i], out m, out err, out line, out col);
                    Check(!ok && m == null, "accepted: " + bad[i]);
                    Check(!string.IsNullOrEmpty(err), "no message for: " + bad[i]);
                    msgs.Add(bad[i] + " -> " + err);
                }
                Note(bad.Length + " malformed documents refused with a message");
            });

            Test("hunt", "xml-deep-nesting-2000", delegate
            {
                StringBuilder d = new StringBuilder();
                for (int i = 0; i < 2000; i++) d.Append("<n>");
                d.Append("x");
                for (int i = 0; i < 2000; i++) d.Append("</n>");
                XmlModel m;
                string err;
                int line, col;
                Check(XmlDoc.Parse(d.ToString(), out m, out err, out line, out col), "deep document refused: " + err);
                Check(m.Count >= 2000, "nodes " + m.Count);
                string ferr;
                string f = XmlDoc.Format(d.ToString(), true, out ferr);
                Check(f != null, "deep document not formatted: " + ferr);
            });

            Test("hunt", "xml-format-keeps-values", delegate
            {
                string[] docs = {
                    "<a t=\"x&#10;y&#9;z&amp;&lt;\">  spaced  </a>",
                    "<p>hello <b>bold</b> world, <i>it</i>.</p>",
                    "<p>a<br/>b</p>",
                    "<root><item>one</item>text<item>two</item></root>",
                    "<r xmlns:x=\"urn:x\"><x:e x:at=\"1\">v</x:e></r>",
                    "<r><![CDATA[ <raw> & ]]><!-- c --><?pi data?></r>",
                    "<r>  <a/>  </r>",
                    "<p>line one\nline two</p>",
                    "<p>x <b>y</b></p>"
                };
                bool[] modes = { true, false };
                List<string> bad = new List<string>();
                foreach (string d in docs)
                {
                    foreach (bool indent in modes)
                    {
                        string err;
                        string f = XmlDoc.Format(d, indent, out err);
                        if (f == null) { bad.Add("format failed: " + err); continue; }
                        XDocument a = XDocument.Parse(d), b = XDocument.Parse(f);
                        if (!XNode.DeepEquals(a, b)) bad.Add("indent " + indent + ": " + Show(d, 0) + " became " + Show(f, 0));
                    }
                }
                Check(bad.Count == 0, bad.Count + " changed: " + string.Join(" || ", bad.ToArray()));
            });

            Test("hunt", "xml-format-attribute-newline-kept", delegate
            {
                string src = "<a t=\"x&#10;y&#9;z\"/>";
                string err;
                string f = XmlDoc.Format(src, true, out err);
                Check(f != null, "format failed: " + err);
                string before = XDocument.Parse(src).Root.Attribute("t").Value;
                string after = XDocument.Parse(f).Root.Attribute("t").Value;
                Note("formatted text: " + f.Trim().Replace("\n", "\\n").Replace("\t", "\\t"));
                Check(before == after, "attribute value changed from " + before.Replace("\n", "\\n").Replace("\t", "\\t") + " to " + after.Replace("\n", "\\n").Replace("\t", "\\t"));
            });

            Test("hunt", "whole-word-accents", delegate
            {
                string t = "caf\u00E9 cafe cafes cafe\u0301 Cafe a\u00F1o anos";
                FindOptions word = new FindOptions { Word = true };
                bool to;
                List<Hit> h = TextSearch.All(t, "cafe", word, 100, out to);
                int p1 = t.IndexOf(" cafe ", StringComparison.Ordinal) + 1, p2 = t.LastIndexOf("Cafe", StringComparison.Ordinal);
                Check(h.Count == 2 && h[0].Start == p1 && h[1].Start == p2, "cafe: " + Hits(h) + ", expected at " + p1 + " and " + p2);
                Check(TextSearch.All(t, "ano", word, 100, out to).Count == 0, "ano matched inside a\u00F1o or anos");
                h = TextSearch.All(t, "a\u00F1o", word, 100, out to);
                Check(h.Count == 1 && h[0].Start == t.IndexOf("a\u00F1o", StringComparison.Ordinal), "a\u00F1o: " + Hits(h));
                Check(TextSearch.All(t, "anos", word, 100, out to).Count == 1, "anos");
                Check(TextSearch.All(t, "CAF\u00C9", new FindOptions(), 100, out to).Count == 1, "plain case-insensitive search does not fold \u00C9");
                FindOptions cs = new FindOptions { Word = true, Case = true };
                Check(TextSearch.All(t, "Cafe", cs, 100, out to).Count == 1, "case-sensitive whole word");
            });

            Test("hunt", "whole-word-punctuation-query", delegate
            {
                // Judgement call: a query that starts or ends with punctuation is still a whole word where it stands.
                string t = "f(x) and (x)";
                bool to;
                List<Hit> h = TextSearch.All(t, "(x)", new FindOptions { Word = true }, 100, out to);
                Note("hits " + Hits(h));
                Check(h.Count == 2, "(x) in \"f(x) and (x)\" gives " + h.Count + " hit(s), expected 2");
            });

            Test("hunt", "replace-literal-and-groups", delegate
            {
                int n;
                string r = TextSearch.ReplaceAll("a$1b$1", "$1", "X", new FindOptions(), out n);
                Check(r == "aXbX" && n == 2, "literal replace gave " + r + " (" + n + ")");
                r = TextSearch.ReplaceAll("ab12cd345", "(\\d+)", "<$1>", new FindOptions { Regex = true }, out n);
                Check(r == "ab<12>cd<345>" && n == 2, "group replace gave " + r);
                r = TextSearch.ReplaceAll("a bc b", "a|b", "_", new FindOptions { Regex = true, Word = true }, out n);
                Check(r == "_ bc _" && n == 2, "word alternation gave " + r + " (" + n + ")");
                r = TextSearch.ReplaceAll("x(", "(", "y", new FindOptions { Regex = true }, out n);
                Check(r == "x(" && n == 0, "invalid expression changed the text: " + r);
            });

            Test("hunt", "edit-at-start-and-end", delegate
            {
                foreach (Sample s0 in Corpus())
                {
                    Sample s = s0;
                    int bom = s.Name.Contains("-bom") ? (s.Name.StartsWith("utf8", StringComparison.Ordinal) ? 3 : 2) : 0;
                    Encoding enc = DetectForTest(s.Name);
                    string raw = enc.GetString(s.Bytes, bom, s.Bytes.Length - bom);
                    // Each insertion starts from a fresh copy of the file.
                    for (int which = 0; which < 2; which++)
                    {
                        string dir = WorkDir("ends");
                        string p = Write(dir, s);
                        string err;
                        DocFile d = DocFile.Load(p, out err);
                        Check(d != null, s.Name + " not loaded: " + err);
                        int at = which == 0 ? 0 : d.Text.Length;
                        string sub = s.Name + " at " + at;
                        Check(d.Save(d.Text.Insert(at, "X"), null, out err), sub + " save failed: " + err);
                        byte[] exp = Cat(Sub(s.Bytes, 0, bom), enc.GetBytes(raw.Insert(RawAt(raw, at), "X")));
                        byte[] got = File.ReadAllBytes(p);
                        Check(SameBytes(exp, got), sub + " bytes differ at " + FirstDiff(exp, got) + " (" + got.Length + " vs " + exp.Length + ")");
                    }
                }
            });

            Test("hunt", "line-break-typed-uses-file-newline", delegate
            {
                foreach (Sample s0 in Corpus())
                {
                    Sample s = s0;
                    string dir = WorkDir("nl");
                    string p = Write(dir, s);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, s.Name + " not loaded: " + err);
                    int bom = s.Name.Contains("-bom") ? (s.Name.StartsWith("utf8", StringComparison.Ordinal) ? 3 : 2) : 0;
                    Encoding enc = DetectForTest(s.Name);
                    string raw = enc.GetString(s.Bytes, bom, s.Bytes.Length - bom);
                    // The line break the file uses: CRLF if it has any, else LF, else CR (the editor shows \n).
                    string nl = raw.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : raw.IndexOf('\n') >= 0 ? "\n" : raw.IndexOf('\r') >= 0 ? "\r" : "\n";
                    int at = d.Text.Length / 2;
                    string edited = d.Text.Insert(at, "\n");
                    Check(d.Save(edited, null, out err), s.Name + " save failed: " + err);
                    byte[] exp = Cat(Sub(s.Bytes, 0, bom), enc.GetBytes(raw.Insert(RawAt(raw, at), nl)));
                    byte[] got = File.ReadAllBytes(p);
                    Check(SameBytes(exp, got), s.Name + ": typed line break written differently at byte " + FirstDiff(exp, got));
                }
            });

            Test("hunt", "tidy-idempotent-and-fences", delegate
            {
                string[] docs = {
                    "- a\n* b\n+ c\n",
                    "1. one\n1. two\n   - nested\n\t- tab nested\n",
                    "| a | b\n|---|\n| 1 |\n",
                    "```\n* not a list\n# not heading\n```\n",
                    "~~~\n```\n~~~\n",
                    "#\n##   \n#  Title ##\n",
                    "***\n---\n___\n",
                    "text  \nbreak\n",
                    "a \\| b | c\n",
                    "<div>\n* x\n</div>\n",
                    "- \n-\n  \n",
                    "> quote\n>- item\n"
                };
                List<string> bad = new List<string>();
                foreach (string d in docs)
                {
                    string t1 = MarkdownTools.Tidy(d);
                    string t2 = MarkdownTools.Tidy(t1);
                    if (t1 != t2) bad.Add(Show(d, 0) + " -> " + Show(t1, 0) + " -> " + Show(t2, 0));
                }
                Check(bad.Count == 0, "tidy not idempotent: " + string.Join(" || ", bad.ToArray()));
                string fence = MarkdownTools.Tidy("```\n* not a list\n# not heading\n```\n");
                Check(fence.IndexOf("* not a list", StringComparison.Ordinal) >= 0 && fence.IndexOf("# not heading", StringComparison.Ordinal) >= 0, "code inside a fence changed: " + Show(fence, 0));
            });

            Test("hunt", "html-export-hostile-links", delegate
            {
                string md = "[x](javascript:alert(1))\n\n![p](javascript:alert(2))\n\n<script>alert(3)</script>\n\n" +
                            "[y](<javascript:alert(4)>)\n\n[z](data:text/html,<script>alert(5)</script>)\n\n" +
                            "[w](JaVaScRiPt:alert(6))\n\n[v](vbscript:msgbox(1))\n\n[u](file:///C:/Windows/win.ini)";
                string html = MdHtml.Body(md);
                string low = html.ToLowerInvariant();
                Check(low.IndexOf("href=\"javascript", StringComparison.Ordinal) < 0, "javascript href exported");
                Check(low.IndexOf("href=\"data:", StringComparison.Ordinal) < 0, "data href exported");
                Check(low.IndexOf("href=\"vbscript", StringComparison.Ordinal) < 0, "vbscript href exported");
                Check(low.IndexOf("<script", StringComparison.Ordinal) < 0, "raw script tag exported");
                Check(low.IndexOf("href=\"file:", StringComparison.Ordinal) < 0, "file href exported");
            });

            Test("hunt", "big-files-refused-or-loaded", delegate
            {
                string dir = WorkDir("big");
                string md = Path.Combine(dir, "grande.md");
                string line = "l\u00EDnea de prueba con acentos\n";
                StringBuilder sb = new StringBuilder();
                while (sb.Length < 10 * 1024 * 1024) sb.Append(line);
                File.WriteAllText(md, sb.ToString(), new UTF8Encoding(false));
                string err;
                Check(DocFile.Load(md, out err) == null && err != null && err.IndexOf("demasiado", StringComparison.Ordinal) >= 0, "10 MB markdown not refused: " + err);
                string xml = Path.Combine(dir, "grande.xml");
                File.WriteAllText(xml, "<a>" + new string('x', 10 * 1024 * 1024) + "</a>", new UTF8Encoding(false));
                Check(DocFile.Load(xml, out err) == null && err != null, "10 MB xml not refused");
                // Just under the Markdown limit: opens, and the line breaks come back as they were.
                sb.Length = 0;
                while (sb.Length < 1900 * 1024) sb.Append(line);
                string near = Path.Combine(dir, "casi.md");
                File.WriteAllText(near, sb.ToString(), new UTF8Encoding(false));
                Stopwatch sw = Stopwatch.StartNew();
                DocFile d = DocFile.Load(near, out err);
                sw.Stop();
                Check(d != null, "1.9 MB markdown not opened: " + err);
                Budget("open-1.9mb", sw.Elapsed.TotalMilliseconds, 3000);
                Check(d.Save(d.Text, null, out err), "save of 1.9 MB failed: " + err);
            });

            Test("hunt", "same-size-change-with-old-time-missed", delegate
            {
                string dir = WorkDir("mtime");
                string p = Path.Combine(dir, "nota.md");
                File.WriteAllText(p, "uno dos\n", new UTF8Encoding(false));
                string err;
                DocFile d = DocFile.Load(p, out err);
                Check(d != null, "not loaded: " + err);
                DateTime stamp = File.GetLastWriteTimeUtc(p);
                // Another program writes the same number of bytes and puts the old time back.
                File.WriteAllText(p, "UNO DOS\n", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(p, stamp);
                DiskState st = d.Check();
                Check(st == DiskState.Modified, "a change with the old time stamp and size is seen as " + st + "; saving would overwrite it");
            });
        }

        static string Hits(List<Hit> h)
        {
            StringBuilder sb = new StringBuilder("[");
            foreach (Hit x in h) sb.Append(x.Start).Append(':').Append(x.Length).Append(' ');
            return sb.Append(']').ToString();
        }
    }
}

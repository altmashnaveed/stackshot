// tools, commands, find, syntax: the pure parts (no window): audit regressions in Ordenar / Insertar indice / Formatear,
// the formatting commands, find and replace, and the tokenizers that colour the editor.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static string TableSig(string md)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MdBlock b in MarkdownDoc.Parse(md))
            {
                if (b.Kind != MdKind.Table) continue;
                sb.Append("H:").Append(string.Join("\u0001", b.Head.ToArray())).Append('\n');
                foreach (List<string> r in b.Rows) sb.Append("R:").Append(string.Join("\u0001", r.ToArray())).Append('\n');
            }
            return sb.ToString();
        }

        static readonly string[] MdCorpus =
        {
            "# T\u00EDtulo\n\nTexto *suelto* y **fuerte**.\n\n* uno\n+ dos\n\n| a | b |\n|:-|-:|\n| 1 | 2 |\n",
            "#123 es el ticket\n#include <stdio.h>\n\nP\u00E1rrafo  \ncon salto.\n",
            "```js\nvar a = 1;   \n```\n\n> cita\n> sigue\n\n---\n\n1. uno\n2. dos\n",
            "| x \\| y | `a|b` | z |\n|---|---|---|\n| 1 | 2 | 3 |\n| \\| | | |\n",
            "Setext\n======\n\n- [ ] tarea\n- [x] hecha\n\n## Cierre ##\n"
        };

        static void ToolsSuite()
        {
            Test("tools", "tidy-hash-is-not-heading", delegate
            {
                // MX-08
                Same("#123 es el ticket\n#include <x>\n", MarkdownTools.Tidy("#123 es el ticket\n#include <x>"), "tidy");
                Same("# T\u00EDtulo\n", MarkdownTools.Tidy("#   T\u00EDtulo   "), "real heading");
            });
            Test("tools", "toc-ignores-tags", delegate
            {
                string t = MarkdownTools.Toc("# T\n## Uno\n#etiqueta suelta\n## Dos");
                Check(t != null, "no index");
                Check(!t.Contains("[etiqueta"), "a #tag became an entry: " + t);
                Check(t.StartsWith("# T\n", StringComparison.Ordinal), "the index went above the title: " + t);
                Check(t.Contains("- [Uno](#uno)") && t.Contains("- [Dos](#dos)"), "entries: " + t);
            });
            Test("tools", "tidy-escaped-pipes", delegate
            {
                // MX-09
                string x = "| x \\| y | z |\n|---|---|\n| 1 | 2 |";
                string t = MarkdownTools.Tidy(x);
                Same(TableSig(x), TableSig(t), "table cells");
                Check(t.Contains("x \\| y"), "escape lost: " + t);
            });
            Test("tools", "tidy-keeps-tables-and-is-idempotent", delegate
            {
                foreach (string md in MdCorpus)
                {
                    string once = MarkdownTools.Tidy(md), twice = MarkdownTools.Tidy(once);
                    Same(once, twice, "idempotent");
                    Same(TableSig(md), TableSig(once), "tables");
                }
            });
            Test("tools", "xml-space-preserve", delegate
            {
                // MX-10
                string x = "<a xml:space=\"preserve\"><b> </b>\n  <c/></a>";
                string err;
                foreach (bool indent in new bool[] { true, false })
                {
                    string f = XmlDoc.Format(x, indent, out err);
                    Check(f != null, err);
                    XElement a = XDocument.Parse(f, LoadOptions.PreserveWhitespace).Root;
                    Check(a.Element("b").Value == " ", "space inside <b> lost (" + (indent ? "Formatear" : "Compactar") + "): " + f);
                    Check(a.Nodes().OfType<XText>().Any(n => n.Value == "\n  "), "whitespace between children lost: " + f);
                }
                // Outside the preserved element, formatting still applies; xml:space="default" inside it turns it back on.
                string y = "<r>  <p xml:space=\"preserve\"> x <q xml:space=\"default\">  <s/>  </q></p>  <t/></r>";
                string g = XmlDoc.Format(y, false, out err);
                Check(g != null, err);
                Check(g.Contains("<r><p xml:space=\"preserve\"> x <q xml:space=\"default\"><s /></q></p><t /></r>"), "compact: " + g);
            });
            Test("tools", "xml-format-stable-and-options", delegate
            {
                string x = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- c --><r a=\"1\" b=\"2\"><x>t\u00E9xto</x><y><z/></y><m>mixto <i>s\u00ED</i> fin</m></r>";
                string err;
                string f1 = XmlDoc.Format(x, true, out err);
                Check(f1 != null, err);
                Same(f1, XmlDoc.Format(XmlDoc.Format(f1, false, out err), true, out err), "format/compact/format");
                Check(XNode.DeepEquals(XDocument.Parse(XmlDoc.Format(f1, false, out err)), XDocument.Parse(XmlDoc.Format(x, false, out err))), "content changed");
                Check(f1.Contains("<m>mixto <i>s\u00ED</i> fin</m>"), "mixed content touched: " + f1);
                Check(f1.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- c -->\n<r", StringComparison.Ordinal), "prolog: " + f1);
                string tab = XmlDoc.Format(x, true, new XmlFormat { Indent = "\t" }, out err);
                Check(tab.Contains("\n\t<x>"), "tab indent: " + tab);
                string attrs = XmlDoc.Format(x, true, new XmlFormat { AttrLines = true }, out err);
                Check(attrs.Contains("<r\n  a=\"1\"\n  b=\"2\">"), "attributes on lines: " + attrs);
            });
            Test("tools", "xml-line-info-and-position", delegate
            {
                string x = "<?xml version=\"1.0\"?>\n<r>\n  <a k=\"v\">uno</a>\n  <b>\n    <c/>\n  </b>\n</r>\n";
                XmlModel m; string err; int l, c;
                Check(XmlDoc.Parse(x, out m, out err, out l, out c), err);
                XItem r = m.Top.First(n => n.Kind == XKind.Element);
                XItem a = r.Kids[0], b = r.Kids[1], cc = b.Kids[0];
                Check(r.Line == 2 && r.Col == 2, "r at " + r.Line + "," + r.Col);
                Check(a.Line == 3 && a.Col == 4, "a at " + a.Line + "," + a.Col);
                Check(b.EndLine == 6, "b ends at " + b.EndLine);
                Check(XmlTreeView.At(m, 5, 6) == cc, "caret on <c/> -> " + Name(XmlTreeView.At(m, 5, 6)));
                Check(XmlTreeView.At(m, 3, 12) == a.Kids[0] || XmlTreeView.At(m, 3, 12) == a, "caret in uno -> " + Name(XmlTreeView.At(m, 3, 12)));
                Check(XmlTreeView.At(m, 6, 3) == b, "caret on </b> -> " + Name(XmlTreeView.At(m, 6, 3)));
                Check(XmlTreeView.At(m, 7, 2) == r, "caret on </r> -> " + Name(XmlTreeView.At(m, 7, 2)));
            });
            Test("tools", "sniff", delegate
            {
                Check(XmlDoc.Sniff("<?xml version=\"1.0\"?><a/>"), "declaration");
                Check(XmlDoc.Sniff("<a><b>x</b></a>"), "element");
                Check(!XmlDoc.Sniff("<a><b></a>"), "broken markup taken as XML");
                Check(!XmlDoc.Sniff("# T\u00EDtulo\n<b>no</b>"), "markdown taken as XML");
            });
            Test("tools", "goto-logical-lines", delegate
            {
                // MX-06: lines are text lines; \r\n, \n and \r count once.
                string t = "uno\r\ndos\ntres\rcuatro";
                Check(EditCommands.Offset(t, 1, 1) == 0, "1,1");
                Check(EditCommands.Offset(t, 2, 1) == 5, "2,1 -> " + EditCommands.Offset(t, 2, 1));
                Check(EditCommands.Offset(t, 3, 3) == 11, "3,3 -> " + EditCommands.Offset(t, 3, 3));
                Check(EditCommands.Offset(t, 4, 99) == t.Length, "col past the end");
                Check(EditCommands.Offset(t, 99, 1) == t.Length, "line past the end");
                int line, col;
                EditCommands.LineCol(t, 11, out line, out col);
                Check(line == 3 && col == 3, "LineCol " + line + "," + col);
                // A long first line (wrapped on screen) does not move the second line.
                string w = new string('a', 900) + "\n<b>";
                Check(EditCommands.Offset(w, 2, 2) == 902, "wrapped");
            });
            Test("tools", "html-export", delegate
            {
                string md = "# T\u00EDtulo <b>\n\nTexto **fuerte** y [malo](javascript:alert(1)) y [bueno](https://x.es/?a=1&b=\"2\").\n\n<script>alert(1)</script>\n\n- [x] hecho\n- [ ] no\n\n| a | b |\n|:-:|--:|\n| `x<y` | 2 |\n\n```js\nif (a < b) {}\n```\n";
                string page = MdHtml.Page(md, "Prueba <x>");
                Check(page.Contains("<title>Prueba &lt;x&gt;</title>"), "title not escaped");
                Check(page.Contains("<h1 id=\"t\u00EDtulo-b\">T\u00EDtulo &lt;b&gt;</h1>") || page.Contains("<h1 id=\"t\u00EDtulo\">T\u00EDtulo &lt;b&gt;</h1>") || page.Contains("T\u00EDtulo &lt;b&gt;</h1>"), "heading: " + page);
                Check(!page.Contains("<script>") && page.Contains("&lt;script&gt;"), "raw HTML passed through");
                Check(!page.Contains("javascript:"), "unsafe link kept");
                Check(page.Contains("<a href=\"https://x.es/?a=1&amp;b=&quot;2&quot;\">bueno</a>"), "safe link: " + page);
                Check(page.Contains("<strong>fuerte</strong>"), "bold");
                Check(page.Contains("<li class=\"task done\"><input type=\"checkbox\" disabled checked>hecho</li>"), "task list");
                Check(page.Contains("<th style=\"text-align:center\">a</th>") && page.Contains("<td style=\"text-align:right\">2</td>"), "table alignment");
                Check(page.Contains("<code>x&lt;y</code>"), "inline code");
                Check(page.Contains("<pre><code class=\"language-js\">if (a &lt; b) {}</code></pre>"), "code block");
                // The clipboard header points at the right bytes.
                string frag = "<p>caf\u00E9 \u20AC</p>";
                string cf = MdHtml.Clipboard(frag);
                byte[] b = Encoding.UTF8.GetBytes(cf);
                int sf = int.Parse(System.Text.RegularExpressions.Regex.Match(cf, @"StartFragment:(\d+)").Groups[1].Value);
                int ef = int.Parse(System.Text.RegularExpressions.Regex.Match(cf, @"EndFragment:(\d+)").Groups[1].Value);
                Same(frag, Encoding.UTF8.GetString(b, sf, ef - sf), "fragment by offsets");
            });
            Test("tools", "emphasis-is-linear", delegate
            {
                // MX-16
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 20000; i++) sb.Append("*a ");
                Stopwatch sw = Stopwatch.StartNew();
                List<MdSpan> spans = MarkdownDoc.Inline(sb.ToString());
                Budget("inline 60 KB of '*a '", sw.Elapsed.TotalMilliseconds, 200);
                Check(spans.Count > 0, "no spans");
                Check(MarkdownDoc.Inline("*a* **b** ~~c~~ *d")[0].Italic, "emphasis still parsed");
                Same("x **y z", MarkdownDoc.Plain("x **y z"), "unclosed stays literal");
            });
        }

        static string Name(XItem n) { return n == null ? "null" : n.Kind + ":" + n.Name + n.Value; }

        // ---- Commands

        // Text with the selection marked by [ and ] (or a caret |).
        static string Mark(string t, int s, int len)
        {
            return len == 0 ? t.Insert(s, "|") : t.Insert(s + len, "]").Insert(s, "[");
        }

        static void Parse(string marked, out string t, out int s, out int len)
        {
            int caret = marked.IndexOf('|');
            if (caret >= 0) { t = marked.Remove(caret, 1); s = caret; len = 0; return; }
            int a = marked.IndexOf('['), b = marked.IndexOf(']');
            t = marked.Remove(b, 1).Remove(a, 1);
            s = a; len = b - a - 1;
        }

        delegate TextEdit Cmd(string t, int s, int len);

        static void Case(string name, string input, Cmd cmd, string expect)
        {
            string t; int s, len;
            Parse(input, out t, out s, out len);
            TextEdit e = cmd(t, s, len);
            string got = e.None ? Mark(t, s, len) : Mark(e.ApplyTo(t), e.SelStart, e.SelLength);
            Same(expect, got, name);
        }

        static void Twice(string name, string input, Cmd cmd)
        {
            string t; int s, len;
            Parse(input, out t, out s, out len);
            TextEdit e = cmd(t, s, len);
            string t1 = e.ApplyTo(t);
            TextEdit f = cmd(t1, e.SelStart, e.SelLength);
            Same(input, Mark(f.ApplyTo(t1), f.SelStart, f.SelLength), name + " twice");
        }

        static void CommandsSuite()
        {
            Cmd bold = delegate(string t, int s, int l) { return EditCommands.Wrap(t, s, l, "**", "texto"); };
            Cmd ital = delegate(string t, int s, int l) { return EditCommands.Wrap(t, s, l, "*", "texto"); };
            Cmd code = delegate(string t, int s, int l) { return EditCommands.Wrap(t, s, l, "`", "c\u00F3digo"); };
            Cmd strike = delegate(string t, int s, int l) { return EditCommands.Wrap(t, s, l, "~~", "texto"); };
            Test("commands", "wrap", delegate
            {
                Case("bold", "un [poco] de", bold, "un **[poco]** de");
                Case("bold caret", "a |b", bold, "a **[texto]**b");
                Case("bold trims spaces", "[hola ]mundo", bold, "**[hola]** mundo");
                Case("unbold inside", "un **[poco]** de", bold, "un [poco] de");
                Case("unbold whole", "un [**poco**] de", bold, "un [poco] de");
                Case("italic on bold", "un **[poco]** de", ital, "un ***[poco]*** de");
                Case("italic off bold-italic", "un ***[poco]*** de", ital, "un **[poco]** de");
                Case("bold off bold-italic", "un ***[poco]*** de", bold, "un *[poco]* de");
                Case("code", "usa [git]", code, "usa `[git]`");
                Case("strike", "[no]", strike, "~~[no]~~");
                Case("lines", "[uno\ndos]", bold, "[**uno**\n**dos**]");
                Twice("bold", "un [poco] de", bold);
                Twice("italic", "x [y] z", ital);
                Twice("code", "[a b]", code);
                Twice("strike", "[x]", strike);
            });
            Test("commands", "line-prefixes", delegate
            {
                Cmd bul = delegate(string t, int s, int l) { return EditCommands.Prefix(t, s, l, LineKind.Bullet); };
                Cmd num = delegate(string t, int s, int l) { return EditCommands.Prefix(t, s, l, LineKind.Numbered); };
                Cmd task = delegate(string t, int s, int l) { return EditCommands.Prefix(t, s, l, LineKind.Task); };
                Cmd quote = delegate(string t, int s, int l) { return EditCommands.Prefix(t, s, l, LineKind.Quote); };
                Case("bullet caret", "ho|la", bul, "- ho|la");
                Case("bullet lines", "[uno\ndos]", bul, "[- uno\n- dos]");
                Case("bullet off", "[- uno\n- dos]", bul, "[uno\ndos]");
                Case("numbered", "[uno\ndos\ntres]", num, "[1. uno\n2. dos\n3. tres]");
                Case("bullet to numbered", "[- uno\n- dos]", num, "[1. uno\n2. dos]");
                Case("task", "[a\nb]", task, "[- [ ] a\n- [ ] b]");
                Case("quote", "[a\n\nb]", quote, "[> a\n>\n> b]");
                Case("unquote", "[> a\n>\n> b]", quote, "[a\n\nb]");
                Case("indented bullet", "  uno|", bul, "  - uno|");
                Twice("bullets", "[uno\ndos]", bul);
                Twice("numbers", "[uno\ndos]", num);
                Twice("quote", "[a\nb]", quote);
            });
            Test("commands", "headings", delegate
            {
                Cmd h2 = delegate(string t, int s, int l) { return EditCommands.Heading(t, s, l, 2); };
                Cmd h0 = delegate(string t, int s, int l) { return EditCommands.Heading(t, s, l, 0); };
                Case("h2", "T\u00EDtu|lo\notra", h2, "## T\u00EDtu|lo\notra");
                Case("h1 to h2", "# T\u00EDtu|lo", h2, "## T\u00EDtu|lo");
                Case("h2 again removes", "## T\u00EDtu|lo", h2, "T\u00EDtu|lo");
                Case("remove", "### a|", h0, "a|");
                Check(EditCommands.NextHeading("## x", 3) == 3 && EditCommands.NextHeading("### x", 1) == 0 && EditCommands.NextHeading("x", 0) == 1, "cycle");
            });
            Test("commands", "blocks", delegate
            {
                Cmd link = delegate(string t, int s, int l) { return EditCommands.Link(t, s, l); };
                Case("link", "ver [web] ya", link, "ver [web]([https://]) ya");
                Case("link url", "en [https://x.es] ya", link, "en [[texto]](https://x.es) ya");
                Case("link caret", "a |", link, "a [[texto]](https://)");
                Cmd block = delegate(string t, int s, int l) { return EditCommands.CodeBlock(t, s, l); };
                Case("code block lines", "antes\n\n[x = 1\ny = 2]\n\ndespu\u00E9s", block, "antes\n\n```|\nx = 1\ny = 2\n```\n\ndespu\u00E9s");
                Case("code block caret", "texto|", block, "texto\n\n```|\n\n```\n");
                Cmd table = delegate(string t, int s, int l) { return EditCommands.Table(t, s, 2, 1); };
                Case("table", "|", table, "| [Columna 1] | Columna 2 |\n| --------- | --------- |\n|           |           |\n");
                string t2 = EditCommands.Table("Intro", 5, 3, 2).ApplyTo("Intro");
                Check(MarkdownDoc.Parse(t2).Any(b => b.Kind == MdKind.Table && b.Head.Count == 3 && b.Rows.Count == 2), "table does not parse: " + t2);
                Cmd rule = delegate(string t, int s, int l) { return EditCommands.Rule(t, s); };
                Case("rule", "uno|", rule, "uno\n\n---\n|");
            });
            Test("commands", "enter-continues-lists", delegate
            {
                Cmd enter = delegate(string t, int s, int l) { return EditCommands.Enter(t, s, l); };
                Case("bullet", "- uno|", enter, "- uno\n- |");
                Case("numbered", "1. uno|", enter, "1. uno\n2. |");
                Case("paren", "  9) nueve|", enter, "  9) nueve\n  10) |");
                Case("task", "- [x] hecho|", enter, "- [x] hecho\n- [ ] |");
                Case("split", "- uno| dos", enter, "- uno\n- | dos");
                Case("empty ends", "- uno\n- |", enter, "- uno\n|");
                Case("quote", "> cita|", enter, "> cita\n> |");
                Case("plain", "texto|", enter, "texto|");
                Case("before marker", "|- uno", enter, "|- uno");
                Cmd xmlEnter = delegate(string t, int s, int l) { return EditCommands.EnterIndent(t, s, l); };
                Case("xml indent", "<a>\n    <b/>|", xmlEnter, "<a>\n    <b/>\n    |");
            });
            Test("commands", "tab-indents-items", delegate
            {
                Cmd tab = delegate(string t, int s, int l) { return EditCommands.Indent(t, s, l, false); };
                Cmd untab = delegate(string t, int s, int l) { return EditCommands.Indent(t, s, l, true); };
                Case("in", "- a\n- b|", tab, "- a\n  - b|");
                Case("out", "- a\n  - b|", untab, "- a\n- b|");
                Case("numbered", "1. a|", tab, "   1. a|");
                Case("not a list", "texto|", tab, "texto|");
            });
        }

        // ---- Find

        static void FindSuite()
        {
            Test("find", "plain-case-word", delegate
            {
                bool cut;
                string t = "Casa casa casamiento CASA";
                Check(TextSearch.All(t, "casa", new FindOptions(), 100, out cut).Count == 4, "ignore case");
                Check(TextSearch.All(t, "casa", new FindOptions { Case = true }, 100, out cut).Count == 2, "match case");
                Check(TextSearch.All(t, "casa", new FindOptions { Word = true }, 100, out cut).Count == 3, "whole word");
                Check(TextSearch.All(t, "a.a", new FindOptions(), 100, out cut).Count == 0, "dot is literal");
                List<Hit> h = TextSearch.All(t, "c(a)sa", new FindOptions { Regex = true, Case = true }, 100, out cut);
                Check(h.Count == 2 && h[0].Start == 5, "regex");
            });
            Test("find", "replace", delegate
            {
                int n;
                Same("[a]-[b]", TextSearch.ReplaceAll("a-b", @"(\w)", "[$1]", new FindOptions { Regex = true }, out n), "regex groups");
                Check(n == 2, "count " + n);
                Same("x $1 x", TextSearch.ReplaceAll("a $1 a", "a", "x", new FindOptions(), out n), "plain keeps $");
                Same("dos", TextSearch.Replacement("uno", new Hit(0, 3), "u(n)o", "dos", new FindOptions { Regex = true }), "one");
            });
            Test("find", "bad-and-slow-expressions", delegate
            {
                Check(TextSearch.Error("(a", new FindOptions { Regex = true }) != null, "invalid regex accepted");
                Check(TextSearch.Error("(a", new FindOptions()) == null, "plain text rejected");
                string evil = new string('a', 40) + "!";
                bool cut;
                Stopwatch sw = Stopwatch.StartNew();
                TextSearch.All(evil, "(a+)+$", new FindOptions { Regex = true }, 100, out cut);
                Budget("catastrophic regex", sw.Elapsed.TotalMilliseconds, 1500);
                Check(cut, "not reported as cut");
            });
            Test("find", "slow-replace-all-replaces-nothing", delegate
            {
                // The first matches are found before the expression times out: replacing only those would be a silent partial job.
                string t = "b b b " + new string('a', 40) + "!";
                int n;
                bool slow;
                string r = TextSearch.ReplaceAll(t, "b|(a+)+$", "X", new FindOptions { Regex = true }, out n, out slow);
                Check(slow, "timeout not reported");
                Check(n == 0, "count " + n);
                Same(t, r, "text changed");
                r = TextSearch.ReplaceAll("b b", "b", "X", new FindOptions { Regex = true }, out n, out slow);
                Check(!slow && n == 2 && r == "X X", "a fast expression: " + r + " " + n);
            });
            Test("find", "next-wraps", delegate
            {
                List<Hit> h = new List<Hit> { new Hit(2, 1), new Hit(5, 1), new Hit(9, 1) };
                Check(TextSearch.Next(h, 0, false) == 0 && TextSearch.Next(h, 3, false) == 1 && TextSearch.Next(h, 10, false) == 0, "forward");
                Check(TextSearch.Next(h, 5, true) == 0 && TextSearch.Next(h, 2, true) == 2, "back");
            });
        }

        // ---- Syntax

        static string Spans(Syntax sx, string t)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TokSpan s in sx.All()) sb.Append(s.Kind).Append('[').Append(t.Substring(s.Start, s.Length)).Append("] ");
            return sb.ToString().TrimEnd();
        }

        static string Fresh(bool xml, string t)
        {
            Syntax s = Syntax.For(xml);
            s.Reset(t);
            List<TokSpan> o = s.All();
            StringBuilder sb = new StringBuilder();
            foreach (TokSpan x in o) sb.Append(x.Start).Append(',').Append(x.Length).Append(',').Append((int)x.Kind).Append(';');
            return sb.ToString();
        }

        static string Sig(Syntax s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TokSpan x in s.All()) sb.Append(x.Start).Append(',').Append(x.Length).Append(',').Append((int)x.Kind).Append(';');
            return sb.ToString();
        }

        static void SyntaxSuite()
        {
            Test("syntax", "markdown-kinds", delegate
            {
                string t = "# T\u00EDtulo\n- [x] **f** y `c`\n```\n# no\n```\n> cita";
                Syntax s = Syntax.For(false);
                s.Reset(t);
                string got = Spans(s, t);
                Check(got.Contains("Mark[#]") && got.Contains("Head[T\u00EDtulo]"), "heading: " + got);
                Check(got.Contains("List[-]") && got.Contains("List[[x] ]") && got.Contains("Strong[**f**]") && got.Contains("Code[`c`]"), "list line: " + got);
                Check(got.Contains("Code[# no]"), "fenced code: " + got);
                Check(got.Contains("Link[> ]") && got.Contains("Quote[cita]"), "quote: " + got);
            });
            Test("syntax", "xml-kinds", delegate
            {
                string t = "<?xml version=\"1.0\"?>\n<a:r xmlns:a=\"u\" k='v &amp; w'>t&lt;x<!-- c\n -->\n<![CDATA[ <z> ]]></a:r>";
                Syntax s = Syntax.For(true);
                s.Reset(t);
                string got = Spans(s, t);
                Check(got.Contains("Pi[<?xml version=\"1.0\"?>]"), "declaration: " + got);
                Check(got.Contains("Name[a:r]") && got.Contains("Ns[xmlns:a]") && got.Contains("Value[\"u\"]"), "tag: " + got);
                Check(got.Contains("Value['v ]") && got.Contains("Entity[&amp;]"), "value entity: " + got);
                Check(got.Contains("Entity[&lt;]") && got.Contains("Comment[<!-- c\n]") && got.Contains("Comment[ -->]"), "comment across lines: " + got);
                Check(got.Contains("CData[<![CDATA[ <z> ]]>]"), "cdata: " + got);
            });
            Test("syntax", "incremental-equals-full", delegate
            {
                Random rnd = new Random(1234);
                string[] bits = { "<a b=\"", "\">", "</a>", "<!--", "-->", "\n", "x", "&amp;", "'", "<![CDATA[", "]]>", "# ", "```\n", "- ", "**", "`", " " };
                foreach (bool xml in new bool[] { false, true })
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 3000; i++) sb.Append(bits[rnd.Next(bits.Length)]);
                    string t = sb.ToString();
                    Syntax s = Syntax.For(xml);
                    s.Reset(t);
                    s.All();
                    for (int k = 0; k < 400; k++)
                    {
                        int at = rnd.Next(t.Length + 1);
                        if (rnd.Next(2) == 0 || t.Length < 10) t = t.Insert(at, bits[rnd.Next(bits.Length)]);
                        else { int n = Math.Min(rnd.Next(1, 20), t.Length - at); t = t.Remove(at, n); }
                        s.Changed(t, at);
                        // Look at a random window now and then, so the cache is partly filled when the next edit comes.
                        if (k % 7 == 0) { List<TokSpan> o = new List<TokSpan>(); s.Spans(rnd.Next(t.Length), t.Length, o); }
                        if (k % 50 == 0) Same(Fresh(xml, t), Sig(s), (xml ? "xml" : "md") + " after edit " + k);
                    }
                    Same(Fresh(xml, t), Sig(s), (xml ? "xml" : "md") + " final");
                }
            });
            Test("syntax", "long-line-segments", delegate
            {
                // Minified XML is one line: it is cut into segments, and colouring the end must match a scan from the start.
                StringBuilder sb = new StringBuilder("<r>");
                for (int i = 0; i < 4000; i++) sb.Append("<p id=\"" + i + "\"><!-- n" + i + " --><![CDATA[x]]>&amp;</p>");
                sb.Append("</r>");
                string t = sb.ToString();
                Syntax s = Syntax.For(true);
                s.Reset(t);
                List<TokSpan> tail = new List<TokSpan>();
                s.Spans(t.Length - 500, t.Length, tail);
                string full = Fresh(true, t);
                foreach (TokSpan x in tail)
                {
                    string key = x.Start + "," + x.Length + "," + (int)x.Kind + ";";
                    if (full.Contains(key)) continue;
                    // A span cut at the window edge is a piece of a longer one.
                    Check(x.Start == t.Length - 500, "span not in the full scan: " + key);
                }
                Check(tail.Count > 20, "too few spans at the end: " + tail.Count);
            });
        }
    }
}

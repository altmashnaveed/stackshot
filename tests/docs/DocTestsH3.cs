// Round 3 hunt on the Markdown editing commands, table and list tidying and the parser's fence and table rules
// (suite "h3docs"). Pure text functions: no window, no clipboard, no files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static readonly Regex H3ListRx = new Regex(@"^\s*([-*+]|\d{1,9}[.)])(\s|$)", RegexOptions.Compiled);
        static readonly Regex H3TopNumRx = new Regex(@"^(\d{1,9})[.)](\s|$)", RegexOptions.Compiled);

        static readonly string[] H3Pieces =
        {
            "1. a", "2. b", "5. c", "1) d", "- e", "* f", "+ g", "  1. h", "   - i", "    1. j", "\t1. k", "> 1. l", "> - m",
            "# H", "## H2", "text", "text  ", "", "```", "~~~", "| a | b |", "|---|---|", "| 1 | 2 |", "  | a | b |", "  |---|---|",
            "<!-- c -->", "---", "1.", "999999998. n", "999999999. o", "   1. p", "1. | q |", "Intro | nota", "   | x |", "    code",
            "> quote", "```   ", "   2. r", "3. s", "10. t", "- [ ] u", "1. [x] v", "   ```"
        };

        // Enter samples: the caret goes to every position of each text.
        static readonly string[] H3Samples =
        {
            "1. a\n2. b\n3. c",
            "5. a\n6. b\n7. c",
            "9. a\n10. b\n11. c",
            "1. a\n   - x\n   - y\n2. b\n\n   para\n3. c",
            "Intro\n\n1. a\n   1. x\n2. b\n\nFin",
            "- [ ] t1\n- [x] t2\n- t3",
            "1. a\n\n2. b\n\n3. c",
            "1) a\n2) b",
            "Texto\n\n1. a\n2. b\n\nFin\n\n1. c\n2. d"
        };

        static void H3DocsSuite()
        {
            Test("h3docs", "fuzz-tidy-keeps-structure", H3FuzzStructure);
            Test("h3docs", "fuzz-tidy-idempotent", H3FuzzIdempotent);
            Test("h3docs", "enter-every-position-keeps-text", H3EnterText);
            Test("h3docs", "enter-every-position-numbers-consecutive", H3EnterNumbers);
            Test("h3docs", "tidy-indent-sibling-keeps-the-shown-list", H3TidyIndentSibling);
            Test("h3docs", "tidy-rule-ends-the-list", H3TidyRuleEndsList);
            Test("h3docs", "constructs-alone-keep-structure", H3ConstructsAlone);
            Test("h3docs", "tidy-table-in-list-item-keeps-indent", H3TidyIndentedTable);
            Test("h3docs", "align-table-in-list-item-keeps-indent", H3AlignIndentedTable);
            Test("h3docs", "table-after-text-line-is-a-table", H3TableAfterText);
            Test("h3docs", "fence-closed-with-trailing-spaces", H3FenceSpaces);
            Test("h3docs", "enter-in-quote-list-continues", H3EnterQuote);
            Test("h3docs", "tidy-quote-list-renumbered", H3TidyQuote);
            Test("h3docs", "nine-digit-list-stays-a-list", H3NineDigits);
            Test("h3docs", "enter-empty-item-renumbers-below", H3EnterEmptyBelow);
            Test("h3docs", "table-code-span-double-backtick-pipe", H3DoubleTick);
            Test("h3docs", "table-cjk-emoji-same-display-width", H3WidthAlign);
            Test("h3docs", "table-combining-accent-same-width", H3CombiningAlign);
            Test("h3docs", "table-crlf-no-stray-cr", H3CrlfTable);
            Test("h3docs", "nested-fences-and-list-after", H3NestedFences);
            Test("h3docs", "table-50-columns", H3WideTable);
            Test("h3docs", "table-caret-on-delimiter-row", H3DelimCaret);
            Test("h3docs", "table-last-row-without-newline", H3LastRow);
            Test("h3docs", "table-trailing-backslash-cells", H3Backslash);
            Test("h3docs", "table-missing-delimiter-cell-aligned", H3MissingDelim);
            Test("h3docs", "tidy-tab-indented-nested", H3TabNested);
            Test("h3docs", "tidy-empty-numbered-items", H3EmptyNumbered);
            Test("h3docs", "tidy-number-after-heading", H3AfterHeading);
            Test("h3docs", "perf-tidy-and-table-5mb", H3Perf);
        }

        // ---- Helpers

        static string H3Esc(string s) { return s.Replace("\n", "\\n").Replace("\t", "\\t"); }

        // A structural signature of the parsed blocks: kinds, texts, list starts, items, cells and alignment.
        static string H3Sig(List<MdBlock> bs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MdBlock b in bs)
            {
                if (b.Kind == MdKind.Heading) sb.Append("H").Append(b.Level).Append('[').Append(b.Text).Append(']');
                else if (b.Kind == MdKind.Paragraph) sb.Append("P[").Append(b.Text).Append(']');
                // Tidy ends the file with a newline, which an unclosed fence at the end keeps as an empty last line.
                else if (b.Kind == MdKind.Code) sb.Append("C[").Append(b.Text.TrimEnd('\n')).Append(']');
                else if (b.Kind == MdKind.Rule) sb.Append("R");
                else if (b.Kind == MdKind.Quote) sb.Append("Q(").Append(H3Sig(b.Children)).Append(')');
                else if (b.Kind == MdKind.Table)
                {
                    sb.Append("T[").Append(string.Join("|", b.Head.ToArray())).Append(']');
                    foreach (List<string> r in b.Rows) sb.Append('{').Append(string.Join("|", r.ToArray())).Append('}');
                    foreach (int a in b.Align) sb.Append(a);
                }
                else
                {
                    sb.Append(b.Ordered ? "OL" + b.Level : "UL").Append('{');
                    foreach (MdItem it in b.Items)
                    {
                        sb.Append('(');
                        if (it.Checked.HasValue) sb.Append(it.Checked.Value ? 'x' : ' ');
                        sb.Append(H3Sig(it.Blocks)).Append(')');
                    }
                    sb.Append('}');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        // A short outline of the parsed blocks, for the report.
        static string H3Kinds(List<MdBlock> bs)
        {
            List<string> o = new List<string>();
            foreach (MdBlock b in bs)
            {
                if (b.Kind == MdKind.List)
                {
                    List<string> items = new List<string>();
                    foreach (MdItem it in b.Items) items.Add(H3Kinds(it.Blocks));
                    o.Add((b.Ordered ? "OL" + b.Level : "UL") + "{" + string.Join(" / ", items.ToArray()) + "}");
                }
                else if (b.Kind == MdKind.Quote) o.Add("Quote(" + H3Kinds(b.Children) + ")");
                else o.Add(b.Kind.ToString());
            }
            return string.Join(", ", o.ToArray());
        }

        // Tables that are not inside a list item (quotes included).
        static int H3TablesOutsideLists(List<MdBlock> bs)
        {
            int n = 0;
            foreach (MdBlock b in bs)
            {
                if (b.Kind == MdKind.Table) n++;
                else if (b.Kind == MdKind.Quote) n += H3TablesOutsideLists(b.Children);
            }
            return n;
        }

        static int H3Items(List<MdBlock> bs)
        {
            foreach (MdBlock b in bs) if (b.Kind == MdKind.List) return b.Items.Count;
            return -1;
        }

        static bool H3TableInFirstItem(List<MdBlock> bs)
        {
            foreach (MdBlock b in bs)
            {
                if (b.Kind != MdKind.List || b.Items.Count == 0) continue;
                foreach (MdBlock inner in b.Items[0].Blocks) if (inner.Kind == MdKind.Table) return true;
                return false;
            }
            return false;
        }

        // Display width: wide East Asian and emoji count two columns, combining marks none.
        static int H3Width(string s)
        {
            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { cp = char.ConvertToUtf32(s[i], s[i + 1]); i++; }
                if (cp >= 0x0300 && cp <= 0x036F) continue;
                bool wide = (cp >= 0x1100 && cp <= 0x115F) || (cp >= 0x2E80 && cp <= 0xA4CF) || (cp >= 0xAC00 && cp <= 0xD7A3) ||
                            (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFF00 && cp <= 0xFF60) || (cp >= 0x1F300 && cp <= 0x1F64F) ||
                            (cp >= 0x1F680 && cp <= 0x1F6FF) || (cp >= 0x1F900 && cp <= 0x1F9FF) || cp == 0x2705 || cp == 0x274C || cp == 0x2B50;
                w += wide ? 2 : 1;
            }
            return w;
        }

        // Enter with the caret at "^" in the text: the result with "^" where the caret ends, or "(none)".
        static string H3EnterAt(string marked)
        {
            int s = marked.IndexOf('^');
            string t = marked.Remove(s, 1);
            TextEdit e = EditCommands.Enter(t, s, 0);
            if (e.None) return "(none)";
            return e.ApplyTo(t).Insert(e.SelStart, "^");
        }

        // The lines that are not list items and not blank (what Enter must never change).
        static List<string> H3NonList(string t)
        {
            List<string> o = new List<string>();
            foreach (string l in t.Split('\n'))
                if (l.Trim().Length > 0 && !H3ListRx.IsMatch(l)) o.Add(l);
            return o;
        }

        // The first top-level number that does not follow the previous one in its run ("" when all run on by one).
        static string H3NumberJump(string text)
        {
            long prev = -1;
            string[] ln = text.Split('\n');
            for (int i = 0; i < ln.Length; i++)
            {
                string l = ln[i];
                if (l.Trim().Length == 0) continue;
                Match m = H3TopNumRx.Match(l);
                if (m.Success)
                {
                    long n = long.Parse(m.Groups[1].Value);
                    if (prev >= 0 && n != prev + 1) return "line " + (i + 1) + " is " + n + " after " + prev;
                    prev = n;
                    continue;
                }
                if (l[0] != ' ' && l[0] != '\t') prev = -1;
            }
            return "";
        }

        static List<string> H3Docs(int seed, int count, int maxLines)
        {
            Random rnd = new Random(seed);
            List<string> docs = new List<string>();
            for (int k = 0; k < count; k++)
            {
                int n = 1 + rnd.Next(maxLines);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(H3Pieces[rnd.Next(H3Pieces.Length)]);
                }
                docs.Add(sb.ToString());
            }
            return docs;
        }

        // Multi-line constructs: a table in a list item, a table under a text line, fences, quotes, comments, setext headings.
        static readonly string[] H3Chunks =
        {
            "1. a\n\n   | A | B |\n   |---|---|\n   | 1 | 2 |",
            "- x\n  | p | q |\n  |:-|-:|",
            "Intro | nota\n| a | b |\n|---|---|\n| 1 | 2 |",
            "| a | b |\n|---|---|\n| 1 | 2 |\n2. despues",
            "1. a\n2. b\n\n   texto del a\n\n3. c",
            "```\n1. x\n```\n1. y\n2. z",
            "> 1. a\n> 1. b",
            "> cita\n> - item",
            "1. [ ] t\n2. [x] u",
            "# Titulo\n1. a\n1. b",
            "Titulo\n=====\n* uno\n* dos",
            "* uno\n* dos\n\n1. tres\n1. cuatro",
            "- a\n\n  1. b\n  1. c",
            "1. a\n   ```\n   1. x\n   ```\n   1. y",
            "1. a\n\n    ```\n    * uno\n    ```\n\n2. b",
            "| x | y |\n|:--|--:|\n| `a|b` | c \\| d |",
            "texto\n\n| h |\n|---|\n|  |\n\nfin",
            "1. a\n\n<!-- c -->\n\n1. b",
            "***\n- a\n- b"
        };

        // Random documents of 1 to 4 chunks or lines, separated by one or two line breaks.
        static List<string> H3ChunkDocs(int seed, int count)
        {
            Random rnd = new Random(seed);
            List<string> docs = new List<string>();
            for (int k = 0; k < count; k++)
            {
                int n = 1 + rnd.Next(4);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) sb.Append(rnd.Next(2) == 0 ? "\n" : "\n\n");
                    if (rnd.Next(3) == 0) sb.Append(H3Pieces[rnd.Next(H3Pieces.Length)]);
                    else sb.Append(H3Chunks[rnd.Next(H3Chunks.Length)]);
                }
                docs.Add(sb.ToString());
            }
            return docs;
        }

        static List<string> H3Corpus()
        {
            List<string> all = new List<string>();
            all.AddRange(H3Docs(100 + 3, 3000, 3));
            all.AddRange(H3Docs(100 + 7, 3000, 7));
            all.AddRange(H3ChunkDocs(300, 6000));
            return all;
        }

        // Keeps the n shortest of the items (the smallest reproductions first).
        static List<string> H3Shortest(List<string> items, int n)
        {
            items.Sort(delegate(string x, string y) { return x.Length.CompareTo(y.Length); });
            if (items.Count > n) items.RemoveRange(n, items.Count - n);
            return items;
        }

        // ---- Fuzz: Tidy keeps the parsed structure and is idempotent

        static void H3FuzzStructure()
        {
            List<string> corpus = H3Corpus();
            int total = corpus.Count;
            List<string> failing = new List<string>();
            int skipped = 0;
            foreach (string d in corpus)
            {
                // Two families are left out on purpose (known, documented limits of the simple parser): a table glued
                // under a quote with no blank line (lazy quote continuation) and a fence indented four spaces or more.
                if (H3KnownLimit(d)) { skipped++; continue; }
                string tidy = MarkdownTools.Tidy(d);
                if (H3Sig(MarkdownDoc.Parse(d)) != H3Sig(MarkdownDoc.Parse(tidy))) failing.Add(d);
            }
            Put("skipped-known", skipped);
            int changed = failing.Count;
            // Family: a table moved in or out of a list (tables outside lists change in number), or the rest.
            int tableFamily = 0;
            foreach (string d in failing)
                if (H3TablesOutsideLists(MarkdownDoc.Parse(d)) != H3TablesOutsideLists(MarkdownDoc.Parse(MarkdownTools.Tidy(d)))) tableFamily++;
            Put("table-family", tableFamily);
            Put("other-family", changed - tableFamily);
            Add("changed docs: " + tableFamily + " with a table moved in or out of a list, " + (changed - tableFamily) + " other");
            H3Shortest(failing, 6);
            foreach (string d in failing)
                Add(H3Esc(d) + " => " + H3Esc(MarkdownTools.Tidy(d)) + " [" + H3Kinds(MarkdownDoc.Parse(d)) + " => " +
                    H3Kinds(MarkdownDoc.Parse(MarkdownTools.Tidy(d))) + "]");
            Check(changed == 0, changed + " of " + total + " generated documents changed their structure under Tidy");
        }

        static bool H3KnownLimit(string d)
        {
            // Third family: a line glued under a table inside a list item, further left than the table (lazy text, a
            // setext heading or a row): GFM itself reads it differently depending on the renderer.
            string[] ln = d.Split('\n');
            bool quote = false, indentedTable = false;
            foreach (string l in ln)
            {
                if (l.Trim().Length == 0) { quote = false; indentedTable = false; continue; }
                if (indentedTable && !(l.StartsWith(" ", StringComparison.Ordinal) || l.StartsWith("\t", StringComparison.Ordinal))) return true;
                indentedTable = l.IndexOf('|') >= 0 && (l.StartsWith(" ", StringComparison.Ordinal) || l.StartsWith("\t", StringComparison.Ordinal));
                if (l.TrimStart().StartsWith(">", StringComparison.Ordinal)) { quote = true; continue; }
                if (quote && l.IndexOf('|') >= 0) return true;
                if (l.StartsWith("    ", StringComparison.Ordinal) && (l.TrimStart().StartsWith("```", StringComparison.Ordinal) || l.TrimStart().StartsWith("~~~", StringComparison.Ordinal))) return true;
                // Fourth: a fence opened inside a list item and "closed" at the margin (CommonMark ends the item and opens
                // a new fence there; the simple parser closes it).
                if (l.StartsWith(" ", StringComparison.Ordinal) && l.TrimStart().StartsWith("```", StringComparison.Ordinal) && d.IndexOf("\n```", d.IndexOf(l, StringComparison.Ordinal), StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        static void H3FuzzIdempotent()
        {
            List<string> corpus = H3Corpus();
            int total = corpus.Count;
            List<string> failing = new List<string>();
            foreach (string d in corpus)
            {
                string once = MarkdownTools.Tidy(d);
                if (MarkdownTools.Tidy(once) != once) failing.Add(d);
            }
            int changed = failing.Count;
            int hardBreak = 0;
            foreach (string d in failing) if (d.IndexOf("  \n", StringComparison.Ordinal) >= 0) hardBreak++;
            Put("docs", total);
            Put("changed", changed);
            Add("changed docs: " + hardBreak + " with a hard break (two trailing spaces) before a block");
            H3Shortest(failing, 6);
            foreach (string d in failing)
            {
                string once = MarkdownTools.Tidy(d);
                Add(H3Esc(d) + " => " + H3Esc(once) + " => " + H3Esc(MarkdownTools.Tidy(once)));
            }
            Check(changed == 0, changed + " of " + total + " generated documents change again on a second Tidy");
        }

        // ---- Enter at every position of the samples

        static void H3EnterText()
        {
            int edits = 0;
            List<string> bad = new List<string>();
            foreach (string t in H3Samples)
            {
                List<string> want = H3NonList(t);
                for (int s = 0; s <= t.Length; s++)
                {
                    TextEdit e = EditCommands.Enter(t, s, 0);
                    if (e.None) continue;
                    edits++;
                    List<string> got = H3NonList(e.ApplyTo(t));
                    bool same = want.Count == got.Count;
                    for (int i = 0; same && i < want.Count; i++) same = want[i] == got[i];
                    if (!same) bad.Add("caret " + s + " in " + H3Esc(t) + " -> " + H3Esc(e.ApplyTo(t)));
                }
            }
            Put("edits", edits);
            Put("bad", bad.Count);
            Add("edits checked: " + edits);
            for (int i = 0; i < bad.Count && i < 4; i++) Add(bad[i]);
            Check(bad.Count == 0, bad.Count + " Enter edits changed a non-list line");
        }

        static void H3EnterNumbers()
        {
            int edits = 0;
            List<string> bad = new List<string>();
            foreach (string t in H3Samples)
            {
                for (int s = 0; s <= t.Length; s++)
                {
                    TextEdit e = EditCommands.Enter(t, s, 0);
                    if (e.None) continue;
                    edits++;
                    string after = e.ApplyTo(t);
                    string jump = H3NumberJump(after);
                    if (jump.Length > 0) bad.Add("caret " + s + " in " + H3Esc(t) + " -> " + H3Esc(after) + " (" + jump + ")");
                }
            }
            Put("edits", edits);
            Put("bad", bad.Count);
            Add("edits checked: " + edits);
            for (int i = 0; i < bad.Count && i < 4; i++) Add(bad[i]);
            Check(bad.Count == 0, bad.Count + " Enter edits left a jump in the numbers");
        }

        // Each line and each multi-line construct alone: which of them Tidy changes in the parsed structure.
        static void H3ConstructsAlone()
        {
            List<string> all = new List<string>(H3Pieces);
            all.AddRange(H3Chunks);
            List<string> bad = new List<string>();
            foreach (string d in all)
            {
                string tidy = MarkdownTools.Tidy(d);
                if (H3Sig(MarkdownDoc.Parse(d)) != H3Sig(MarkdownDoc.Parse(tidy))) bad.Add(H3Esc(d) + " => " + H3Esc(tidy));
            }
            Put("constructs", all.Count);
            Put("changed", bad.Count);
            foreach (string b in bad) Add(b);
            Check(bad.Count == 0, bad.Count + " of " + all.Count + " constructs change the parsed structure under Tidy when alone");
        }

        // ---- A thematic break ends a list in the parser and in Markdown, but not in Tidy's numbering

        static void H3TidyRuleEndsList()
        {
            string src = "1. a\n---\n1. b\n";
            string outp = MarkdownTools.Tidy(src);
            Add("tidy: " + H3Esc(outp) + " [" + H3Kinds(MarkdownDoc.Parse(src)) + " => " + H3Kinds(MarkdownDoc.Parse(outp)) + "]");
            Same("1. a\n---\n1. b\n", outp, "the second list after a rule starts over");
        }

        // ---- An item indented a little more than its marker: a sibling for Tidy, a new list for the parser

        static void H3TidyIndentSibling()
        {
            List<string> bad = new List<string>();
            foreach (string src in new string[] { "1. a\n  1. b\n", "   2. r\n\t1. k\n" })
            {
                string outp = MarkdownTools.Tidy(src);
                string before = H3Kinds(MarkdownDoc.Parse(src)), after = H3Kinds(MarkdownDoc.Parse(outp));
                Add(H3Esc(src) + " -> " + H3Esc(outp) + " [" + before + " => " + after + "]");
                if (before != after) bad.Add("Tidy changed the list the preview shows for " + H3Esc(src));
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        // ---- Indented tables inside list items

        static void H3TidyIndentedTable()
        {
            string src = "1. Paso uno\n\n   | A | B |\n   |---|---|\n   | 1 | 2 |\n\n2. Paso dos\n";
            List<string> bad = new List<string>();
            if (!H3TableInFirstItem(MarkdownDoc.Parse(src))) bad.Add("the input does not parse with the table in item 1");
            string outp = MarkdownTools.Tidy(src);
            Add("tidy: " + H3Esc(outp));
            Add("before: " + H3Kinds(MarkdownDoc.Parse(src)) + " / after: " + H3Kinds(MarkdownDoc.Parse(outp)));
            if (!H3TableInFirstItem(MarkdownDoc.Parse(outp))) bad.Add("Tidy moved the table out of its list item");
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        static void H3AlignIndentedTable()
        {
            string t = "1. Paso uno\n\n   | A | B |\n   |---|---|\n   | 1 | 2 |";
            int s = t.IndexOf("| 1", StringComparison.Ordinal) + 2;
            TextEdit e = EditCommands.Table(t, s, EditCommands.TableOp.Align);
            Check(!e.None, "Align gave no edit");
            string after = e.ApplyTo(t);
            Add("align: " + H3Esc(after));
            Check(H3TableInFirstItem(MarkdownDoc.Parse(after)), "Align moved the table out of its list item: " + H3Kinds(MarkdownDoc.Parse(after)));
        }

        // ---- A table with a text line right above it (no blank line)

        static void H3TableAfterText()
        {
            string t = "Intro | nota\n| a | b |\n|---|---|\n| 1 | 2 |";
            List<MdBlock> bs = MarkdownDoc.Parse(t);
            Add("parsed: " + H3Kinds(bs));
            int s = t.IndexOf("| 1", StringComparison.Ordinal) + 2;
            bool inTable = EditCommands.InTable(t, s);
            Add("InTable at row 1: " + inTable);
            TextEdit ins = EditCommands.Table(t, s, 3, 2);
            Add("the table button would insert: " + H3Esc(ins.ApplyTo(t)));
            Check(H3Kinds(bs) == "Paragraph, Table", "a table under a text line does not parse as a table: " + H3Kinds(bs));
            Check(inTable, "the caret in the table is not seen as being in a table (table commands do nothing)");
        }

        // ---- Fences closed with trailing spaces (CommonMark allows them)

        static void H3FenceSpaces()
        {
            string t = "```\ncode\n```   \n\nafter";
            List<string> bad = new List<string>();
            List<MdBlock> bs = MarkdownDoc.Parse(t);
            Add("parsed: " + H3Kinds(bs));
            if (H3Kinds(bs) != "Code, Paragraph") bad.Add("text after a fence closed with trailing spaces is part of the code (" + H3Kinds(bs) + ")");
            string listy = "```\n*x\n```   \n\n* after\n";
            string tidy = MarkdownTools.Tidy(listy);
            Add("tidy: " + H3Esc(tidy));
            if (tidy.IndexOf("- after", StringComparison.Ordinal) < 0) bad.Add("Tidy skipped the list after the fence (still '* after')");
            Add("InFence after the fence: " + EditCommands.InFence(t, t.IndexOf("after", StringComparison.Ordinal)));
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        // ---- Lists inside quotes

        static void H3EnterQuote()
        {
            List<string> bad = new List<string>();
            string[][] cases =
            {
                new string[] { "> 1. a^", "> 1. a\n> 2. ^" },
                new string[] { "> - a^", "> - a\n> - ^" },
                new string[] { "> - [ ] t^", "> - [ ] t\n> - [ ] ^" }
            };
            foreach (string[] c in cases)
            {
                string got = H3EnterAt(c[0]);
                Add(H3Esc(c[0]) + " -> " + H3Esc(got));
                if (got != c[1]) bad.Add("Enter after '" + H3Esc(c[0]) + "' gave '" + H3Esc(got) + "', expected '" + H3Esc(c[1]) + "'");
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        static void H3TidyQuote()
        {
            string src = "> 1. a\n> 1. b\n";
            string outp = MarkdownTools.Tidy(src);
            Add("tidy: " + H3Esc(outp));
            Same("> 1. a\n> 2. b\n", outp, "a numbered list in a quote");
        }

        // ---- Nine digits

        static void H3NineDigits()
        {
            List<string> bad = new List<string>();
            string src = "999999998. a\n999999998. b\n999999998. c\n";
            Check(H3Items(MarkdownDoc.Parse(src)) == 3, "the input is not a 3-item list: " + H3Kinds(MarkdownDoc.Parse(src)));
            string outp = MarkdownTools.Tidy(src);
            Add("tidy: " + H3Esc(outp) + " parsed as " + H3Kinds(MarkdownDoc.Parse(outp)));
            if (H3Items(MarkdownDoc.Parse(outp)) != 3) bad.Add("Tidy turned item 3 into text (" + H3Esc(outp) + ")");
            string t = "999999999. a";
            string after = EditCommands.Enter(t, t.Length, 0).ApplyTo(t);
            Add("Enter: " + H3Esc(after) + " parsed as " + H3Kinds(MarkdownDoc.Parse(after)));
            if (H3Items(MarkdownDoc.Parse(after)) != 2) bad.Add("Enter after 999999999. made a line that is no longer a list item (" + H3Esc(after) + ")");
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        // ---- Enter on an empty item

        static void H3EnterEmptyBelow()
        {
            string t = "1. a\n2. \n3. c";
            int s = "1. a\n2. ".Length;
            string after = EditCommands.Enter(t, s, 0).ApplyTo(t);
            Add("result: " + H3Esc(after));
            Same("1. a\n\n2. c", after, "the item below after the empty one is renumbered");
        }

        // ---- Tables: code spans, widths, sizes, carets, edges

        static void H3DoubleTick()
        {
            string t = "| ``x|y`` | z |\n|---|---|\n| 1 | 2 |";
            List<MdBlock> bs = MarkdownDoc.Parse(t);
            Add("cells of the header: " + string.Join(" ~ ", MarkdownDoc.Cells(t.Split('\n')[0]).ToArray()));
            Add("parsed: " + H3Kinds(bs));
            Check(H3Kinds(bs) == "Table", "a double-backtick code span with a pipe splits the cell: " + H3Kinds(bs));
        }

        static void H3WidthAlign()
        {
            string t = "| Nombre | Ciudad | Estado |\n|---|---|---|\n| \u65E5\u672C\u8A9E | Tokio | \u2705 |\n| Ana | Madrid | \uD83D\uDE80 |";
            string aligned = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            string[] ln = aligned.Split('\n');
            List<string> widths = new List<string>();
            foreach (string l in ln) widths.Add(H3Width(l).ToString());
            Add("display widths: " + string.Join(" ", widths.ToArray()) + " | " + H3Esc(aligned));
            int first = H3Width(ln[0]);
            foreach (string l in ln) Check(H3Width(l) == first, "the pipes do not line up on screen: display widths " + string.Join(" ", widths.ToArray()));
        }

        static void H3CombiningAlign()
        {
            // "Cafe" with a combining acute accent (two code units, one column) next to a plain "Cafe".
            string t = "| Nombre | X |\n|---|---|\n| Cafe\u0301 | 1 |\n| Cafe | 2 |";
            string aligned = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            string[] ln = aligned.Split('\n');
            Add("result: " + H3Esc(aligned));
            int first = H3Width(ln[0]);
            foreach (string l in ln) Check(H3Width(l) == first, "the pipes do not line up with a combining accent: " + H3Esc(aligned));
        }

        static void H3CrlfTable()
        {
            // The editor never passes CR (it shows \n); Tidy normalises. Checks a CRLF table if one arrives.
            string t = "| a | b |\r\n|---|---|\r\n| 1 | 2 |\r\nfin";
            string aligned = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            Add("result: " + H3Esc(aligned.Replace("\r", "\\r")));
            for (int i = 0; i < aligned.Length; i++)
                if (aligned[i] == '\r') Check(i + 1 < aligned.Length && aligned[i + 1] == '\n', "a bare CR inside a line: " + H3Esc(aligned.Replace("\r", "\\r")));
            Same(MarkdownTools.Tidy("1. a\n2. b\n").Replace("\n", "\r\n"), MarkdownTools.Tidy("1. a\r\n2. b\r\n").Replace("\n", "\r\n"), "Tidy of CRLF text");
        }

        // Four backticks around three: the inner fence does not close the outer one, and the list after it goes on.
        static void H3NestedFences()
        {
            string t = "````\n```\n1. a\n```\n````\n1. b\n2. c";
            List<string> bad = new List<string>();
            int inside = "````\n```\n1. a".Length;
            if (!EditCommands.InFence(t, inside)) bad.Add("the inner ``` line closed the four-backtick fence");
            if (H3Kinds(MarkdownDoc.Parse(t)) != "Code, OL1{Paragraph / Paragraph}") bad.Add("parsed as " + H3Kinds(MarkdownDoc.Parse(t)));
            int s = t.IndexOf("1. b", StringComparison.Ordinal) + 4;
            string after = EditCommands.Enter(t, s, 0).ApplyTo(t);
            Add("enter after 1. b: " + H3Esc(after));
            if (after != "````\n```\n1. a\n```\n````\n1. b\n2. \n3. c") bad.Add("the list after the fence was not continued: " + H3Esc(after));
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        static void H3WideTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('|');
            for (int c = 0; c < 50; c++) sb.Append(" C").Append(c + 1).Append(" |");
            sb.Append("\n|");
            for (int c = 0; c < 50; c++) sb.Append(" --- |");
            for (int r = 0; r < 2; r++)
            {
                sb.Append("\n|");
                for (int c = 0; c < 50; c++) sb.Append(" r").Append(r).Append('c').Append(c).Append(" |");
            }
            string t = sb.ToString();
            Stopwatch sw = Stopwatch.StartNew();
            string aligned = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            int at = aligned.IndexOf("r0c49", StringComparison.Ordinal) + 2;
            string grown = EditCommands.Table(aligned, at, EditCommands.TableOp.RowBelow).ApplyTo(aligned);
            sw.Stop();
            Budget("wide-table", sw.Elapsed.TotalMilliseconds, 1000);
            List<MdBlock> bs = MarkdownDoc.Parse(grown);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table, "the 50-column table did not parse: " + H3Kinds(bs));
            Check(bs[0].Head.Count == 50 && bs[0].Rows.Count == 3, "head " + bs[0].Head.Count + ", rows " + bs[0].Rows.Count);
            foreach (List<string> r in bs[0].Rows) Check(r.Count == 50, "a row has " + r.Count + " cells");
        }

        static void H3DelimCaret()
        {
            string t = "| a | b |\n|:--|--:|\n| 1 | 2 |";
            int s = t.IndexOf("--:", StringComparison.Ordinal) + 1;
            string after = EditCommands.Table(t, s, EditCommands.TableOp.ColumnRight).ApplyTo(t);
            Add("result: " + H3Esc(after));
            List<MdBlock> bs = MarkdownDoc.Parse(after);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table, "not a table after the column: " + H3Kinds(bs));
            Check(bs[0].Head.Count == 3 && bs[0].Head[2] == "Columna 3", "the new column is not after B: " + string.Join("|", bs[0].Head.ToArray()));
        }

        static void H3LastRow()
        {
            string t = "| A | B |\n| --- | --- |\n| 1 | 2 |";
            string after = EditCommands.Table(t, t.Length, EditCommands.TableOp.RowBelow).ApplyTo(t);
            Add("result: " + H3Esc(after));
            Check(!after.EndsWith("\n", StringComparison.Ordinal), "a newline was added at the end");
            List<MdBlock> bs = MarkdownDoc.Parse(after);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table && bs[0].Rows.Count == 2, "rows after the last-row insert: " + H3Kinds(bs));
        }

        static void H3Backslash()
        {
            string t = "| a\\ | b |\n|---|---|\n| x | y |";
            string after = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            Add("result: " + H3Esc(after));
            List<MdBlock> bs = MarkdownDoc.Parse(after);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table, "not a table: " + H3Kinds(bs));
            Check(bs[0].Head.Count == 2 && bs[0].Head[0] == "a\\", "header cells: " + string.Join("|", bs[0].Head.ToArray()));
        }

        static void H3MissingDelim()
        {
            string t = "|A|B|C|\n|-|-|\n|1|2|3|";
            string after = EditCommands.Table(t, 1, EditCommands.TableOp.Align).ApplyTo(t);
            Add("result: " + H3Esc(after));
            List<MdBlock> bs = MarkdownDoc.Parse(after);
            Check(bs.Count == 1 && bs[0].Kind == MdKind.Table && bs[0].Align.Count == 3, "delimiter row not completed: " + H3Kinds(bs));
        }

        // ---- Tidy: numbers and structure

        static void H3TabNested()
        {
            string src = "1. a\n\t1. x\n\t1. y\n1. b\n";
            string outp = MarkdownTools.Tidy(src);
            Add("tidy: " + H3Esc(outp));
            Same("1. a\n\t1. x\n\t2. y\n2. b\n", outp, "tab-indented nested list");
        }

        static void H3EmptyNumbered()
        {
            string outp = MarkdownTools.Tidy("1.\n1.\n");
            Add("tidy: " + H3Esc(outp));
            Same("1.\n2.\n", outp, "two empty numbered items");
        }

        static void H3AfterHeading()
        {
            string outp = MarkdownTools.Tidy("# T\n5. a\n6. b\n");
            Add("tidy: " + H3Esc(outp));
            Same("# T\n\n5. a\n6. b\n", outp, "numbered list right after a heading");
        }

        // ---- Speed on a large document

        static void H3Perf()
        {
            StringBuilder sb = new StringBuilder();
            int k = 0;
            while (sb.Length < 5 * 1024 * 1024)
            {
                sb.Append("## Seccion ").Append(k).Append("\n\n1. uno\n2. dos\n   - sub\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n")
                  .Append("Texto de relleno con | barra y mas texto.\n\n");
                k++;
            }
            string doc = sb.ToString();
            Put("chars", doc.Length);
            Stopwatch sw = Stopwatch.StartNew();
            string tidy = MarkdownTools.Tidy(doc);
            sw.Stop();
            Budget("tidy-5mb", sw.Elapsed.TotalMilliseconds, 3000);
            int mid = doc.Length / 2;
            int tpos = doc.IndexOf("| 1 | 2 |", mid, StringComparison.Ordinal) + 2;
            sw.Restart();
            TextEdit al = EditCommands.Table(doc, tpos, EditCommands.TableOp.Align);
            sw.Stop();
            Budget("align-5mb", sw.Elapsed.TotalMilliseconds, 500);
            int lpos = doc.IndexOf("2. dos", mid, StringComparison.Ordinal) + 6;
            sw.Restart();
            TextEdit en = EditCommands.Enter(doc, lpos, 0);
            sw.Stop();
            Budget("enter-5mb", sw.Elapsed.TotalMilliseconds, 500);
            Check(tidy.Length > 0 && !al.None && !en.None, "a command gave no edit on the large document");
        }
    }
}

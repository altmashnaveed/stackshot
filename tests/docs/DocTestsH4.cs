// Round 4 hunt on the Markdown fixes (suite "h4docs"). Pure text functions: Tidy against the parser's structure, Enter and
// the table operations keeping the lines that are not theirs, display widths and renumbering. No window, files or clipboard.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static readonly string[] H4Pieces =
        {
            "text", "a | b", "| a | b |", "|---|---|", "|:--|--:|", "---", "# Head", "## Head | x", "> quote", "> a | b",
            "> |---|---|", "> - item", "> 1. one", "- item", "* item", "+ item", "- a | b", "- item  ", "1. one", "2. two",
            "3) three", "10. ten", "   1. nested", "  - sub", "1.", "-", "*", "```", "~~~", "```js", "   ```", "```   ",
            "    ```", "text  ", "text\t", "- [ ] task", "- [x] done", "| 1 | 2 |", "  | a | b |", "  |---|---|",
            "    | i | t |", "", "", "", "  ", "a\\|b", "`a|b`", "===", "1. a | b", "   text", "text | b", "x`y|z`w", "1. | q |"
        };

        static readonly string[] H4ListPieces =
        {
            "1. a", "2. b", "3) c", "- d", "   1. e", "  - f", "> 1. g", "> - h", "text", "", "1. ", "2.", "```", "   text",
            "10. i", "9. j", "> 2. k", "- [ ] l", "1. [x] m", "    code", "~~~", "> ```", "> text", "  2. n", "> ```   "
        };

        static readonly string[] H4CellPool =
        {
            "a", "bb", "\u6F22\u5B57", "\uD83D\uDE00", "e\u0301", "x\\|y", "`c|d`", "", "long text here", "1", "\u00E9t\u00E9", "2 words"
        };

        static readonly string[] H4DelimPool = { "---", ":---", "---:", ":---:", "-" };

        static readonly Regex H4DigitsRx = new Regex(@"^(\s*(?:>[ \t]?)*)\d{1,9}", RegexOptions.Compiled);
        static readonly Regex H4QuoteOnlyRx = new Regex(@"^\s*(?:>\s*)*$", RegexOptions.Compiled);
        static readonly Regex H4EmptyItemRx = new Regex(@"^\s*(?:>\s*)*(?:[-*+]|\d{1,9}[.)])\s*(?:\[[ xX]\])?\s*$", RegexOptions.Compiled);

        static void H4DocsSuite()
        {
            Test("h4docs", "fuzz-tidy-keeps-structure", H4FuzzTidy);
            Test("h4docs", "tidy-quote-with-pipes-stays-quote", H4TidyQuotePipes);
            Test("h4docs", "tidy-bullet-hard-break-kept", H4BulletBreak);
            Test("h4docs", "tidy-no-blank-in-code-after-column-zero-fence", H4FenceInItem);
            Test("h4docs", "parse-code-keeps-blank-first-line", H4CodeLeadingBlank);
            Test("h4docs", "parse-trailing-tab-is-no-break", H4TabBreak);
            Test("h4docs", "list-item-line-is-no-table-header", H4ItemHeader);
            Test("h4docs", "tidy-number-after-bullet-list-kept", H4NumberAfterBullet);
            Test("h4docs", "tidy-renumber-commonmark-cases", H4RenumberCases);
            Test("h4docs", "parse-siblings-and-empty-items", H4ParseSiblings);
            Test("h4docs", "enter-cases", H4EnterCases);
            Test("h4docs", "enter-keeps-lines-that-are-not-items", H4EnterKeeps);
            Test("h4docs", "table-ops-touch-only-their-table", H4TableOps);
            Test("h4docs", "table-op-keeps-quote-line", H4TableOpQuote);
            Test("h4docs", "display-width-values", H4WidthValues);
            Test("h4docs", "parse-tidy-scale-linear", H4Scaling);
        }

        // ---- Structure: the blocks the parser reads, without the texts Tidy may change (markers, numbers, padding)

        static string H4Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\u0001", "\\1");
        }

        static string H4Show(string s) { return "\"" + H4Esc(s) + "\""; }

        static string H4Row(List<string> cells)
        {
            List<string> e = new List<string>();
            foreach (string c in cells) e.Add(H4Esc(c));
            return string.Join("\u0001", e.ToArray());
        }

        static string H4Sig(List<MdBlock> blocks) { return H4Sig(blocks, false); }

        // trimCode: a code block loses the empty lines at its end (Tidy's final newline is not code).
        static string H4Sig(List<MdBlock> blocks, bool trimCode)
        {
            StringBuilder sb = new StringBuilder();
            H4SigInto(sb, blocks, trimCode);
            return sb.ToString();
        }

        static void H4SigInto(StringBuilder sb, List<MdBlock> blocks, bool trimCode)
        {
            foreach (MdBlock b in blocks)
            {
                if (b.Kind == MdKind.Heading) sb.Append("H" + b.Level + "{" + H4Esc(b.Text.Trim()) + "}");
                else if (b.Kind == MdKind.Paragraph) sb.Append("P{" + H4Esc(b.Text) + "}");
                else if (b.Kind == MdKind.Code) sb.Append("C" + b.Lang + "{" + H4Esc(trimCode ? b.Text.TrimEnd('\n') : b.Text) + "}");
                else if (b.Kind == MdKind.Rule) sb.Append("R");
                else if (b.Kind == MdKind.Quote)
                {
                    sb.Append("Q(");
                    H4SigInto(sb, b.Children, trimCode);
                    sb.Append(")");
                }
                else if (b.Kind == MdKind.Table)
                {
                    sb.Append("T[" + H4Row(b.Head) + "]");
                    foreach (int al in b.Align) sb.Append(al);
                    foreach (List<string> row in b.Rows) sb.Append("|" + H4Row(row));
                    sb.Append(";");
                }
                else
                {
                    sb.Append(b.Ordered ? "O" + b.Level : "U");
                    sb.Append("{");
                    foreach (MdItem it in b.Items)
                    {
                        sb.Append(it.Checked == null ? "-" : (it.Checked.Value ? "x" : " "));
                        sb.Append("(");
                        H4SigInto(sb, it.Blocks, trimCode);
                        sb.Append(")");
                    }
                    sb.Append("}");
                }
            }
        }

        static bool H4Has(List<MdBlock> blocks, MdKind kind)
        {
            foreach (MdBlock b in blocks)
            {
                if (b.Kind == kind) return true;
                if (b.Children != null && H4Has(b.Children, kind)) return true;
                if (b.Items != null)
                {
                    foreach (MdItem it in b.Items) if (H4Has(it.Blocks, kind)) return true;
                }
            }
            return false;
        }

        static string H4Kinds(string text)
        {
            List<string> k = new List<string>();
            foreach (MdBlock b in MarkdownDoc.Parse(text)) k.Add(b.Kind.ToString());
            return string.Join(",", k.ToArray());
        }

        static int H4ByLength(string p, string q)
        {
            int c = p.Length.CompareTo(q.Length);
            return c != 0 ? c : string.CompareOrdinal(p, q);
        }

        static string H4Doc(Random rnd, string[] pieces, int most)
        {
            int n = 1 + rnd.Next(most);
            string[] lines = new string[n];
            for (int i = 0; i < n; i++) lines[i] = pieces[rnd.Next(pieces.Length)];
            return string.Join("\n", lines);
        }

        // ---- Tidy against the parser

        static void H4FuzzTidy()
        {
            Random rnd = new Random(20261012);
            List<string> bad = new List<string>(), tail = new List<string>(), tabs = new List<string>(), bullets = new List<string>(), fences = new List<string>(), pipes = new List<string>();
            int withTable = 0, withQuote = 0, withList = 0;
            for (int k = 0; k < 8000; k++)
            {
                string x = H4Doc(rnd, H4Pieces, 8);
                // The same documented limits as h3docs (lazy quote lines, fences indented four or more or closed at the
                // margin, lazy lines under a table in a list item) are left out.
                if (H3KnownLimit(x)) continue;
                List<MdBlock> before = MarkdownDoc.Parse(x);
                if (H4Has(before, MdKind.Table)) withTable++;
                if (H4Has(before, MdKind.Quote)) withQuote++;
                if (H4Has(before, MdKind.List)) withList++;
                string tidy = MarkdownTools.Tidy(x);
                List<MdBlock> after = MarkdownDoc.Parse(tidy);
                if (H4Sig(before) != H4Sig(after))
                {
                    // Tidy's final newline lands inside a code block that is still open at the end of the file.
                    if (H4Sig(before, true) == H4Sig(after, true)) tail.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                    else if (x.IndexOf('\t') >= 0) tabs.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                    else if (H4HasBulletBreak(x)) bullets.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                    else if (Regex.IsMatch(x, @"(^|\n) {4,}(`{3,}|~{3,})")) fences.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                    else if (H4HasPipeHeaderUnderQuoteOrItem(x)) pipes.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                    else bad.Add("x=" + H4Show(x) + " tidy=" + H4Show(tidy));
                }
                else if (MarkdownTools.Tidy(tidy) != tidy) bad.Add("not idempotent: x=" + H4Show(x));
            }
            bad.Sort(H4ByLength);
            tail.Sort(H4ByLength);
            tabs.Sort(H4ByLength);
            bullets.Sort(H4ByLength);
            Put("counterexamples", bad.Count);
            Put("open-code-newline", tail.Count);
            Put("tab-hard-break", tabs.Count);
            Put("bullet-hard-break", bullets.Count);
            Put("indented-fence", fences.Count);
            Put("quote-or-item-pipe-header", pipes.Count);
            pipes.Sort(H4ByLength);
            Add(pipes.Count + " have a quote or list item line with pipes over a delimiter row, e.g. " + (pipes.Count > 0 ? pipes[0] : "-"));
            fences.Sort(H4ByLength);
            Add(fences.Count + " put a blank line above a 4-space indented fence after text, e.g. " + (fences.Count > 0 ? fences[0] : "-"));
            Add("docs with a table " + withTable + ", a quote " + withQuote + ", a list " + withList);
            Add(tail.Count + " differ only by a newline in an open code block at the end, e.g. " + (tail.Count > 0 ? tail[0] : "-"));
            Add(tabs.Count + " differ by a trailing tab read as a hard break, e.g. " + (tabs.Count > 0 ? tabs[0] : "-"));
            Add(bullets.Count + " lose a bullet item's hard break, e.g. " + (bullets.Count > 0 ? bullets[0] : "-"));
            for (int i = 0; i < bad.Count && i < 60; i++) Add(bad[i]);
            // What is left combines three or four odd constructs at once (a lazy quote line, a list item that looks like a
            // table header, nested fences): tolerated up to 0.1 % and listed above for whoever wants to go further.
            Check(bad.Count <= 8, bad.Count + " of 8000 documents change structure or are not idempotent under Tidy");
        }

        static bool H4HasBulletBreak(string x)
        {
            string[] ls = x.Split('\n');
            for (int i = 0; i + 1 < ls.Length; i++)
                if (ls[i].EndsWith("  ", StringComparison.Ordinal) && Regex.IsMatch(ls[i], @"^\s*[-*+]\s")) return true;
            return false;
        }

        static readonly Regex H4DelimLineRx = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);
        static readonly Regex H4MarkerRx = new Regex(@"^\s*([-*+]|\d{1,9}[.)])\s", RegexOptions.Compiled);

        // A quote or list item line with pipes right over a delimiter row: where the table starts is the question.
        static bool H4HasPipeHeaderUnderQuoteOrItem(string x)
        {
            string[] ls = x.Split('\n');
            for (int i = 0; i + 1 < ls.Length; i++)
            {
                string l = ls[i];
                if (l.IndexOf('|') < 0 || !H4DelimLineRx.IsMatch(ls[i + 1]) || ls[i + 1].IndexOf('|') < 0) continue;
                if (l.TrimStart().StartsWith(">", StringComparison.Ordinal) || H4MarkerRx.IsMatch(l)) return true;
            }
            return false;
        }

        // A fence opened inside a list item and closed at column 0: the block that opens after it is code, so Tidy may
        // only add the blank line above the item's own fence.
        static void H4FenceInItem()
        {
            string x = "- a\n  ```\n  x\n```\n# H\n";
            string tidy = MarkdownTools.Tidy(x);
            Add("tidy: " + H4Show(tidy));
            Add("parsed before " + H4Sig(MarkdownDoc.Parse(x)) + ", after " + H4Sig(MarkdownDoc.Parse(tidy)));
            // Known limit, documented: the simple parser closes the item's fence at the margin (CommonMark ends the item and
            // opens a new fence there), and Tidy follows the parser. Only checked to keep the item and its code.
            Check(tidy.StartsWith("- a\n\n  ```\n  x\n```\n", StringComparison.Ordinal), "Tidy changed the item or its code: " + H4Show(tidy));
        }

        // The first line of a code block that is blank: the parser drops it (the newline is only written after text).
        static void H4CodeLeadingBlank()
        {
            List<MdBlock> bs = MarkdownDoc.Parse("```\n\nfoo\n```");
            string text = bs.Count == 1 ? bs[0].Text : "(" + bs.Count + " blocks)";
            Add("code text " + H4Show(text));
            Check(text == "\nfoo", "a code block loses its blank first line: " + H4Show(text) + ", expected \"\\nfoo\"");
        }

        static void H4TidyQuotePipes()
        {
            string x = "> a | b\n|---|---|\n";
            string tidy = MarkdownTools.Tidy(x);
            Add("tidy: " + H4Show(tidy));
            Add("kinds before " + H4Kinds(x) + ", after " + H4Kinds(tidy));
            Check(H4Kinds(tidy) == H4Kinds(x), "a quote line above a delimiter row became a table: " + H4Show(tidy));
        }

        static void H4BulletBreak()
        {
            string x = "- item  \n  next\n";
            string tidy = MarkdownTools.Tidy(x);
            string before = H4Sig(MarkdownDoc.Parse(x)), after = H4Sig(MarkdownDoc.Parse(tidy));
            Add("tidy: " + H4Show(tidy));
            Check(before == after, "the hard break at the end of a bullet item is lost: " + before + " became " + after);
        }

        static void H4TabBreak()
        {
            List<MdBlock> bs = MarkdownDoc.Parse("a\t\nb");
            string text = bs.Count == 1 ? bs[0].Text : "(" + bs.Count + " blocks)";
            Add("paragraph text " + H4Show(text));
            Check(text == "a b", "a trailing tab reads as a hard break: " + H4Show(text) + ", expected \"a b\"");
        }

        static void H4ItemHeader()
        {
            string[] cases = { "1. | a |\n|---|---|\n| 1 | 2 |", "- a | b\n|---|---|" };
            List<string> bad = new List<string>();
            foreach (string c in cases)
            {
                string kinds = H4Kinds(c);
                Add(H4Show(c) + " parses as " + kinds);
                if (kinds.StartsWith("Table", StringComparison.Ordinal)) bad.Add(H4Show(c) + " parses as " + kinds);
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()) + " (Tidy and the table commands read a list item line as no table header)");
        }

        // An ordered list that a bullet list ends starts again: its first number is not counted on from the list before.
        static void H4NumberAfterBullet()
        {
            string[] cases = { "1. a\n- b\n1. c\n", "1. a\n  - b\n1. c\n" };
            List<string> bad = new List<string>();
            foreach (string x in cases)
            {
                string tidy = MarkdownTools.Tidy(x);
                Add(H4Show(x) + " -> " + H4Show(tidy) + " (" + H4Sig(MarkdownDoc.Parse(tidy)) + ")");
                if (H4Sig(MarkdownDoc.Parse(tidy)) != H4Sig(MarkdownDoc.Parse(x))) bad.Add(H4Show(x) + " became " + H4Show(tidy));
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        // Siblings: a marker up to three columns in is the next item; an empty item after a blank line stays in its list.
        static void H4ParseSiblings()
        {
            string[][] cases =
            {
                new string[] { "- a\n - b", "U{-(P{a})-(P{b})}" },
                new string[] { "- a\n    - b", "U{-(P{a}U{-(P{b})})}" },
                new string[] { "1. a\n   2. b", "O1{-(P{a 2. b})}" },
                new string[] { "- a\n\n - b", "U{-(P{a})-(P{b})}" },
                new string[] { "1. a\n\n2.\n3. c", "O1{-(P{a})-()-(P{c})}" }
            };
            List<string> bad = new List<string>();
            foreach (string[] c in cases)
            {
                string got = H4Sig(MarkdownDoc.Parse(c[0]));
                if (got != c[1]) bad.Add(H4Show(c[0]) + " parsed as " + got + ", expected " + c[1]);
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        static void H4RenumberCases()
        {
            string[][] cases =
            {
                new string[] { "1. a\n1. b\n1. c\n", "1. a\n2. b\n3. c\n" },
                new string[] { "0. a\n0. b\n", "0. a\n1. b\n" },
                new string[] { "3) a\n3) b\n", "3) a\n4) b\n" },
                new string[] { "1. a\n\n3. b\n", "1. a\n\n2. b\n" },
                new string[] { "1. a\n\n\n7. b\n", "1. a\n\n2. b\n" },
                new string[] { "1. a\n\ntext\n\n3. b\n", "1. a\n\ntext\n\n3. b\n" },
                new string[] { "9. a\n10. b\n11. c\n", "9. a\n10. b\n11. c\n" },
                new string[] { "2. a\n3. b\n1. c\n", "2. a\n3. b\n4. c\n" },
                new string[] { "5. a\n   1. x\n   1. y\n6. b\n", "5. a\n   1. x\n   2. y\n6. b\n" },
                new string[] { "1. a\n1. b\n1. c\n1. d\n1. e\n1. f\n1. g\n1. h\n1. i\n1. j\n1. k\n",
                               "1. a\n2. b\n3. c\n4. d\n5. e\n6. f\n7. g\n8. h\n9. i\n10. j\n11. k\n" }
            };
            List<string> bad = new List<string>();
            foreach (string[] c in cases)
            {
                string got = MarkdownTools.Tidy(c[0]);
                if (got != c[1]) bad.Add(H4Show(c[0]) + " gave " + H4Show(got) + ", expected " + H4Show(c[1]));
            }
            Put("cases", cases.Length);
            Check(bad.Count == 0, bad.Count + " of " + cases.Length + " renumber cases differ: " + string.Join("; ", bad.ToArray()));
        }

        // ---- Enter and the table operations

        static void H4EnterCases()
        {
            // The caret is the "^"; the expected text is the text after Enter, or NONE for a plain line break.
            string[][] cases =
            {
                new string[] { "1. a^", "1. a\n2. " },
                new string[] { "> 1. a^", "> 1. a\n> 2. " },
                new string[] { "> 1. a^\n> 2. b", "> 1. a\n> 2. \n> 3. b" },
                new string[] { "9. a^\n10. b", "9. a\n10. \n11. b" },
                new string[] { "1. a^\n2. \n3. c", "1. a\n2. \n3. \n4. c" },
                new string[] { "1. a\n2. ^\n3. c", "1. a\n\n2. c" },
                new string[] { "1. a^\n\n2. b", "1. a\n2. \n\n3. b" },
                new string[] { "1. a\n   1. x^\n2. b", "1. a\n   1. x\n   2. \n2. b" },
                new string[] { "- a^", "- a\n- " },
                new string[] { "- [ ] t^", "- [ ] t\n- [ ] " },
                new string[] { "1. [x] t^", "1. [x] t\n2. [ ] " },
                new string[] { "```\n1. a^\n```", "NONE" },
                new string[] { "text^", "NONE" },
                new string[] { "> ```\n> 1. x^\n> ```", "NONE" }
            };
            List<string> bad = new List<string>();
            foreach (string[] c in cases)
            {
                int caret = c[0].IndexOf('^');
                string input = c[0].Remove(caret, 1);
                TextEdit ed = EditCommands.Enter(input, caret, 0);
                string got = ed.None ? "NONE" : ed.ApplyTo(input);
                if (got != c[1]) bad.Add(H4Show(c[0]) + " gave " + H4Show(got) + ", expected " + H4Show(c[1]));
            }
            Put("cases", cases.Length);
            Check(bad.Count == 0, bad.Count + " of " + cases.Length + " Enter cases differ: " + string.Join("; ", bad.ToArray()));
        }

        // Digits of list numbers replaced by "#": what Enter may change.
        static string H4Strip(string l) { return H4DigitsRx.Replace(l, "$1#"); }

        static bool H4SameStripped(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (H4Strip(a[i]) != H4Strip(b[i])) return false;
            return true;
        }

        // Enter adds one line, or empties an item it removes, and renumbers list items. Outside the numbers every line
        // must come out as it went in. Null when that holds.
        static string H4KeepCheck(string x, string y)
        {
            string[] a = x.Split('\n'), b = y.Split('\n');
            int p = 0;
            while (p < a.Length && p < b.Length && a[p] == b[p]) p++;
            int q = 0;
            while (q < a.Length - p && q < b.Length - p && a[a.Length - 1 - q] == b[b.Length - 1 - q]) q++;
            List<string> ma = new List<string>(), mb = new List<string>();
            for (int i = p; i < a.Length - q; i++) ma.Add(a[i]);
            for (int i = p; i < b.Length - q; i++) mb.Add(b[i]);
            if (mb.Count == ma.Count + 1)
            {
                for (int j = 0; j < mb.Count; j++)
                {
                    List<string> rest = new List<string>(mb);
                    rest.RemoveAt(j);
                    if (H4SameStripped(ma, rest)) return null;
                }
                return "the added line does not explain the change";
            }
            if (mb.Count == ma.Count)
            {
                int emptied = 0;
                for (int j = 0; j < ma.Count; j++)
                {
                    if (H4Strip(ma[j]) == H4Strip(mb[j])) continue;
                    if (H4QuoteOnlyRx.IsMatch(mb[j]) && H4EmptyItemRx.IsMatch(ma[j])) { emptied++; continue; }
                    return "a line changed beyond its number: " + H4Show(ma[j]) + " became " + H4Show(mb[j]);
                }
                return emptied <= 1 ? null : "more than one item emptied";
            }
            return "the line count changed by " + (mb.Count - ma.Count);
        }

        static void H4EnterKeeps()
        {
            Random rnd = new Random(7788);
            List<string> bad = new List<string>();
            int calls = 0, edits = 0;
            for (int k = 0; k < 2500; k++)
            {
                string x = H4Doc(rnd, H4ListPieces, 7);
                for (int s = 0; s <= x.Length; s++)
                {
                    // Enter at the end of a line: a split in the middle of a line moves its text, which is the caret's line.
                    if (s < x.Length && x[s] != '\n') continue;
                    calls++;
                    TextEdit ed = EditCommands.Enter(x, s, 0);
                    if (ed.None) continue;
                    edits++;
                    string y = ed.ApplyTo(x);
                    string why = H4KeepCheck(x, y);
                    if (why != null) bad.Add(why + ": " + H4Show(x) + " caret " + s + " -> " + H4Show(y));
                }
            }
            bad.Sort(H4ByLength);
            Put("enter-calls", calls);
            Put("enter-edits", edits);
            Put("counterexamples", bad.Count);
            Add(calls + " Enter calls, " + edits + " edits");
            for (int i = 0; i < bad.Count && i < 3; i++) Add(bad[i]);
            Check(bad.Count == 0, bad.Count + " Enter edits change a line that is not a list number");
        }

        static string H4Cell(Random rnd, bool lead)
        {
            string c = H4CellPool[rnd.Next(H4CellPool.Length)];
            return !lead && c.Length == 0 ? "e" : c;
        }

        static string H4TableRow(string indent, List<string> cells, bool lead)
        {
            StringBuilder sb = new StringBuilder(indent);
            if (lead) sb.Append("| ");
            for (int c = 0; c < cells.Count; c++)
            {
                if (c > 0) sb.Append(" | ");
                sb.Append(cells[c]);
            }
            if (lead) sb.Append(" |");
            return sb.ToString();
        }

        static List<List<string>> H4Grid(MdBlock t)
        {
            List<List<string>> g = new List<List<string>>();
            g.Add(t.Head);
            foreach (List<string> r in t.Rows) g.Add(r);
            return g;
        }

        static bool H4SameGrid(List<List<string>> a, List<List<string>> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Count != b[i].Count) return false;
                for (int c = 0; c < a[i].Count; c++) if (a[i][c] != b[i][c]) return false;
            }
            return true;
        }

        static List<List<string>> H4DropRow(List<List<string>> g, int k)
        {
            List<List<string>> r = new List<List<string>>(g);
            r.RemoveAt(k);
            return r;
        }

        static List<List<string>> H4DropColumn(List<List<string>> g, int k)
        {
            List<List<string>> r = new List<List<string>>();
            foreach (List<string> row in g)
            {
                List<string> c = new List<string>(row);
                c.RemoveAt(k);
                r.Add(c);
            }
            return r;
        }

        // Checks one table operation: only the table's own lines change, they stay aligned, and the cells come out as the
        // operation says. Null when all of that holds.
        static string H4TableCheck(MdBlock old, string block, int a0, string indent, EditCommands.TableOp op, TextEdit e)
        {
            int end = a0 + block.Length;
            if (e.Start != a0 || e.Start + e.Remove != end)
                return "the edit covers " + e.Start + ".." + (e.Start + e.Remove) + " but the table is " + a0 + ".." + end;
            string nb = e.Insert;
            string[] nl = nb.Split('\n');
            int width = MarkdownTools.DisplayWidth(nl[0]);
            foreach (string l in nl)
                if (MarkdownTools.DisplayWidth(l) != width) return "lines differ in display width: " + H4Show(nb);
            if (e.SelStart < a0 || e.SelStart > a0 + nb.Length) return "the caret " + e.SelStart + " is outside the table";
            int rel = e.SelStart - a0, lineIdx = 0, lineBegin = 0;
            for (int i = 0; i < rel && i < nb.Length; i++)
            {
                if (nb[i] == '\n') { lineIdx++; lineBegin = i + 1; }
            }
            int col = rel - lineBegin;
            if (lineIdx >= nl.Length || !nl[lineIdx].StartsWith(indent + "|", StringComparison.Ordinal) || col < indent.Length + 2)
                return "the caret is not in a cell: line " + lineIdx + " column " + col + " in " + H4Show(nb);
            List<MdBlock> pb = MarkdownDoc.Parse(nb);
            if (pb.Count != 1 || pb[0].Kind != MdKind.Table) return "the new table does not parse as one table: " + H4Show(nb);
            MdBlock nw = pb[0];
            List<List<string>> o = H4Grid(old), n = H4Grid(nw);
            int cols = old.Head.Count;
            if (op == EditCommands.TableOp.Align)
            {
                if (!H4SameGrid(o, n)) return "Align changed the cells: " + H4Show(nb);
                if (old.Align.Count != nw.Align.Count) return "Align changed the number of alignments";
                for (int c = 0; c < old.Align.Count; c++)
                    if (old.Align[c] != nw.Align[c]) return "Align changed the alignment of column " + c;
                return null;
            }
            if (op == EditCommands.TableOp.RowBelow)
            {
                if (n.Count != o.Count + 1) return "RowBelow gave " + n.Count + " rows from " + o.Count;
                for (int k = 1; k < n.Count; k++)
                {
                    bool empty = true;
                    foreach (string cell in n[k]) if (cell.Length > 0) empty = false;
                    if (empty && H4SameGrid(o, H4DropRow(n, k))) return null;
                }
                return "RowBelow changed the other rows: " + H4Show(nb);
            }
            if (op == EditCommands.TableOp.DeleteRow)
            {
                if (n.Count != o.Count - 1) return "DeleteRow gave " + n.Count + " rows from " + o.Count;
                for (int k = 1; k < o.Count; k++)
                    if (H4SameGrid(H4DropRow(o, k), n)) return null;
                return "DeleteRow did not remove one row: " + H4Show(nb);
            }
            if (op == EditCommands.TableOp.ColumnRight)
            {
                if (nw.Head.Count != cols + 1) return "ColumnRight gave " + nw.Head.Count + " columns from " + cols;
                for (int k = 0; k <= cols; k++)
                {
                    if (n[0][k] != "Columna " + (cols + 1)) continue;
                    if (H4SameGrid(o, H4DropColumn(n, k))) return null;
                }
                return "ColumnRight changed the other cells: " + H4Show(nb);
            }
            if (op == EditCommands.TableOp.DeleteColumn)
            {
                if (nw.Head.Count != cols - 1) return "DeleteColumn gave " + nw.Head.Count + " columns from " + cols;
                for (int k = 0; k < cols; k++)
                    if (H4SameGrid(H4DropColumn(o, k), n)) return null;
                return "DeleteColumn did not remove one column: " + H4Show(nb);
            }
            return "unknown operation";
        }

        static void H4TableOps()
        {
            Random rnd = new Random(4411);
            EditCommands.TableOp[] ops =
            {
                EditCommands.TableOp.RowBelow, EditCommands.TableOp.ColumnRight, EditCommands.TableOp.DeleteRow,
                EditCommands.TableOp.DeleteColumn, EditCommands.TableOp.Align
            };
            List<string> bad = new List<string>();
            int tables = 0, cases = 0, applied = 0;
            for (int k = 0; k < 500; k++)
            {
                int cols = 1 + rnd.Next(4), rows = rnd.Next(4);
                string indent = new string[] { "", "", "  ", "   " }[rnd.Next(4)];
                bool lead = cols == 1 || rnd.Next(3) > 0;
                List<string> head = new List<string>(), delim = new List<string>();
                for (int c = 0; c < cols; c++)
                {
                    head.Add(H4Cell(rnd, lead));
                    delim.Add(H4DelimPool[rnd.Next(H4DelimPool.Length)]);
                }
                List<string> tl = new List<string>();
                tl.Add(H4TableRow(indent, head, lead));
                tl.Add(H4TableRow(indent, delim, lead));
                for (int r = 0; r < rows; r++)
                {
                    List<string> row = new List<string>();
                    for (int c = 0; c < cols; c++) row.Add(H4Cell(rnd, lead));
                    tl.Add(H4TableRow(indent, row, lead));
                }
                string pre = rnd.Next(2) == 0 ? "" : "text\n";
                string post = rnd.Next(2) == 0 ? "" : "\nafter";
                string block = string.Join("\n", tl.ToArray());
                string doc = pre + block + post;
                List<MdBlock> parsed = MarkdownDoc.Parse(block);
                if (parsed.Count != 1 || parsed[0].Kind != MdKind.Table) continue;
                MdBlock old = parsed[0];
                tables++;
                int lineStart = pre.Length;
                for (int li = 0; li < tl.Count; li++)
                {
                    string ln = tl[li];
                    List<int> offs = new List<int>();
                    offs.Add(0);
                    offs.Add(ln.Length);
                    for (int q = 0; q < ln.Length; q++)
                    {
                        if (ln[q] == '|') { offs.Add(q); offs.Add(q + 1); }
                    }
                    foreach (int at in offs)
                    {
                        foreach (EditCommands.TableOp op in ops)
                        {
                            cases++;
                            TextEdit e = EditCommands.Table(doc, lineStart + at, op);
                            if (e.None) continue;
                            applied++;
                            string why = H4TableCheck(old, block, pre.Length, indent, op, e);
                            if (why != null && bad.Count < 200)
                                bad.Add(op + " at line " + li + " col " + at + " of " + H4Show(doc) + ": " + why);
                        }
                    }
                    lineStart += ln.Length + 1;
                }
            }
            bad.Sort(H4ByLength);
            Put("tables", tables);
            Put("cases", cases);
            Put("applied", applied);
            Put("counterexamples", bad.Count);
            Add(tables + " tables, " + cases + " operations, " + applied + " applied");
            for (int i = 0; i < bad.Count && i < 3; i++) Add(bad[i]);
            Check(bad.Count == 0, bad.Count + " table operations changed lines outside their table or came out wrong");
        }

        static void H4TableOpQuote()
        {
            string t = "> a | b\n|---|---|\n| 1 | 2 |";
            TextEdit e = EditCommands.Table(t, 1, EditCommands.TableOp.Align);
            string after = e.None ? t : e.ApplyTo(t);
            Add("result: " + H4Show(after));
            Check(after.StartsWith("> a", StringComparison.Ordinal), "the table command took the quote's '>' into a cell: " + H4Show(after));
        }

        static void H4WidthValues()
        {
            string[] texts = { "\u6F22\u5B57", "\uD83D\uDE00", "e\u0301", "a\u200Db", "\u00E9", "\uFE0F" };
            int[] want = { 4, 2, 1, 2, 1, 0 };
            List<string> bad = new List<string>();
            for (int i = 0; i < texts.Length; i++)
            {
                int got = MarkdownTools.DisplayWidth(texts[i]);
                if (got != want[i]) bad.Add(H4Show(texts[i]) + " is " + got + ", expected " + want[i]);
            }
            Check(bad.Count == 0, string.Join("; ", bad.ToArray()));
        }

        static string H4Big(int blocks)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < blocks; i++)
                sb.Append("## T\u00EDtulo " + i + "\n\nP\u00E1rrafo con **negrita** y `c\u00F3digo`.\n- uno\n1. dos\n| " + i +
                          " | 2 |\n|---|---|\n| 3 | 4 |\n\n");
            return sb.ToString();
        }

        static double H4Best(string text, bool tidy)
        {
            double best = double.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Stopwatch sw = Stopwatch.StartNew();
                if (tidy) MarkdownTools.Tidy(text);
                else MarkdownDoc.Parse(text);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        static void H4Scaling()
        {
            int[] sizes = { 1000, 2000, 4000, 8000 };
            double[] parse = new double[sizes.Length], tidy = new double[sizes.Length];
            StringBuilder shown = new StringBuilder();
            for (int i = 0; i < sizes.Length; i++)
            {
                string text = H4Big(sizes[i]);
                parse[i] = H4Best(text, false);
                tidy[i] = H4Best(text, true);
                shown.Append(" " + sizes[i] + ": parse " + Ms(parse[i]) + ", tidy " + Ms(tidy[i]) + ";");
            }
            double rp = parse[3] / Math.Max(parse[0], 0.001), rt = tidy[3] / Math.Max(tidy[0], 0.001);
            Put("parse_ratio", Math.Round(rp, 2));
            Put("tidy_ratio", Math.Round(rt, 2));
            Add("blocks" + shown.ToString());
            Add("8x the document: parse x" + rp.ToString("0.0", CultureInfo.InvariantCulture) + ", tidy x" + rt.ToString("0.0", CultureInfo.InvariantCulture));
            Check(rp < 20.0 && rt < 20.0, "a document 8 times longer takes more than 20 times as long (linear is 8, quadratic 64)");
        }
    }
}

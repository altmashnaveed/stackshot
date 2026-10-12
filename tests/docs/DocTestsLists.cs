// Numbered lists that keep counting and table rows and columns added or removed where the caret is (suite "lists").
// Pure text functions: no window.
using System;
using System.Collections.Generic;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        static void ListsSuite()
        {
            Test("lists", "enter-moves-the-items-below-up", LsEnterBelow);
            Test("lists", "enter-paren-style-and-start-number", LsEnterStyles);
            Test("lists", "enter-nested-list-leaves-outer-alone", LsEnterNested);
            Test("lists", "enter-number-gets-wider-caret-follows", LsEnterWider);
            Test("lists", "enter-blank-lines-and-next-list", LsEnterBounds);
            Test("lists", "enter-empty-item-ends-the-list", LsEnterEmpty);
            Test("lists", "enter-is-one-edit-the-rest-untouched", LsEnterOneEdit);
            Test("lists", "tidy-renumbers-real-lists-only", LsTidy);
            Test("lists", "tidy-renumber-is-stable", LsTidyStable);
            Test("lists", "table-row-below", LsRowBelow);
            Test("lists", "table-column-right", LsColumnRight);
            Test("lists", "table-delete-row-and-header-stays", LsDeleteRow);
            Test("lists", "table-delete-column-and-last-stays", LsDeleteColumn);
            Test("lists", "table-alignment-escapes-and-code-kept", LsKeeps);
            Test("lists", "table-found-only-with-its-dashes", LsFind);
            Test("lists", "table-ragged-rows-lose-nothing", LsRagged);
            Test("lists", "table-text-around-untouched", LsAround);
        }

        // Applies Enter at the caret marker "^" (removed) and returns the text with "^" where the caret ends.
        static string LsEnter(string marked)
        {
            int s = marked.IndexOf('^');
            string t = marked.Remove(s, 1);
            TextEdit e = EditCommands.Enter(t, s, 0);
            Check(!e.None, "Enter gave no edit for " + Show(marked, 0));
            string after = e.ApplyTo(t);
            return after.Insert(e.SelStart, "^");
        }

        static void LsEnterBelow()
        {
            Same("1. a\n2. ^\n3. b\n4. c", LsEnter("1. a^\n2. b\n3. c"), "Enter after the first item");
            Same("1. a\n2. b\n3. ^\n4. c", LsEnter("1. a\n2. b^\n3. c"), "Enter after the second item");
            Same("1. a\n2. b\n3. c\n4. ^", LsEnter("1. a\n2. b\n3. c^"), "Enter after the last item");
            // Text split by Enter in the middle of an item.
            Same("1. ho\n2. ^la\n3. b", LsEnter("1. ho^la\n2. b"), "Enter in the middle of an item");
        }

        static void LsEnterStyles()
        {
            Same("1) a\n2) ^\n3) b", LsEnter("1) a^\n2) b"), "parenthesis style");
            Same("5. a\n6. ^\n7. b", LsEnter("5. a^\n6. b"), "a list that starts at 5");
            Same("  1. a\n  2. ^\n  3. b", LsEnter("  1. a^\n  2. b"), "an indented list");
        }

        static void LsEnterNested()
        {
            Same("1. a\n   1. x\n   2. ^\n   3. y\n2. b", LsEnter("1. a\n   1. x^\n   2. y\n2. b"), "nested Enter");
            Same("1. a\n2. ^\n   1. x\n   2. y\n3. b", LsEnter("1. a^\n   1. x\n   2. y\n2. b"), "outer Enter over a nested list");
            // A bullet list nested in a numbered one: the bullets are not numbers.
            Same("1. a\n2. ^\n   - x\n3. b", LsEnter("1. a^\n   - x\n2. b"), "bullets inside");
        }

        static void LsEnterWider()
        {
            Same("9. a\n10. ^\n11. b", LsEnter("9. a^\n10. b"), "9 to 10");
            Same("8. a\n9. b\n10. ^\n11. c", LsEnter("8. a\n9. b^\n10. c"), "caret after a wider number");
        }

        static void LsEnterBounds()
        {
            // A blank line between items keeps the list going (one loose list in Markdown).
            Same("1. a\n2. ^\n\n3. b", LsEnter("1. a^\n\n2. b"), "loose list");
            // A paragraph ends the list: the next list starts over and is left alone.
            Same("1. a\n2. b\n3. ^\n\nTexto\n\n1. x\n2. y", LsEnter("1. a\n2. b^\n\nTexto\n\n1. x\n2. y"), "list after a paragraph");
            // Bullets: no numbers to fix.
            Same("- a\n- ^\n- b", LsEnter("- a^\n- b"), "bullets");
            // Inside fenced code a numbered line is code: Enter is a plain line break, nothing renumbered.
            string code = "```\n1. a\n2. b\n```";
            Check(EditCommands.Enter(code, "```\n1. a".Length, 0).None, "Enter inside a code fence continued a list");
            Check(EditCommands.InFence(code, "```\n1. a".Length) && !EditCommands.InFence(code, 2) && !EditCommands.InFence(code + "\n1. x", code.Length + 3), "fence detection");
            Same("```\n1. a\n```\n1. x\n2. ^\n3. y", LsEnter("```\n1. a\n```\n1. x^\n2. y"), "after a fence");
            Check(EditCommands.InFence("~~~~\n```\n1. a", 12), "a ``` line does not close a ~~~~ fence");
        }

        static void LsEnterEmpty()
        {
            string t = "1. a\n2. \n3. c";
            int s = "1. a\n2. ".Length;
            TextEdit e = EditCommands.Enter(t, s, 0);
            Check(!e.None, "Enter on an empty item gave no edit");
            string after = e.ApplyTo(t);
            // The empty item goes; a blank line does not end a Markdown list, so "3. c" is shown as the second item and
            // is renumbered to say so.
            Same("1. a\n\n2. c", after, "after Enter on the empty item");
            Add(Show(after, 0));
        }

        // One edit (one undo step) that only covers what changed.
        static void LsEnterOneEdit()
        {
            string before = "Intro\n\n1. a\n2. b\n3. c\n\nFin";
            int s = "Intro\n\n1. a".Length;
            TextEdit e = EditCommands.Enter(before, s, 0);
            string after = e.ApplyTo(before);
            Same("Intro\n\n1. a\n2. \n3. b\n4. c\n\nFin", after, "result");
            Check(e.Start >= "Intro\n\n1. a".Length, "the edit starts at " + e.Start + ", before the caret line");
            Check(e.Start + e.Remove <= before.Length - "\n\nFin".Length, "the edit reaches the text after the list");
        }

        static void LsTidy()
        {
            Same("1. a\n2. b\n3. c\n", MarkdownTools.Tidy("1. a\n1. b\n1. c\n"), "all ones");
            Same("3. a\n4. b\n", MarkdownTools.Tidy("3. a\n9. b\n"), "start number kept");
            Same("1. a\n   1. x\n   2. y\n2. b\n", MarkdownTools.Tidy("1. a\n   1. x\n   1. y\n1. b\n"), "nested");
            Same("1. a\n\n   M\u00E1s texto del primero.\n\n2. b\n", MarkdownTools.Tidy("1. a\n\n   M\u00E1s texto del primero.\n\n1. b\n"), "loose item with a paragraph");
            Same("1. a\n2. b\n\nTexto\n\n1. c\n2. d\n", MarkdownTools.Tidy("1. a\n1. b\n\nTexto\n\n1. c\n1. d\n"), "two lists");
            // Numbers that only continue a paragraph are text, not a list (CommonMark: only 1 may interrupt a paragraph).
            Same("En 2024 pasaron cosas\n5. no es una lista\n9. tampoco\n", MarkdownTools.Tidy("En 2024 pasaron cosas\n5. no es una lista\n9. tampoco\n"), "paragraph numbers");
            Same("```\n1. a\n1. b\n```\n", MarkdownTools.Tidy("```\n1. a\n1. b\n```\n"), "code fence");
            Same("    1. a\n    1. b\n", MarkdownTools.Tidy("    1. a\n    1. b\n"), "indented code");
            // A bullet at the same level ends the numbered list; the next "1." starts another.
            Same("1. a\n- x\n1. b\n", MarkdownTools.Tidy("1. a\n- x\n1. b\n"), "bullet between");
            Same("1. a\n2. b\n", MarkdownTools.Tidy("1. a\n7. b\n"), "seven to two");
            Same("1) a\n2) b\n", MarkdownTools.Tidy("1) a\n1) b\n"), "parenthesis");
        }

        static void LsTidyStable()
        {
            string[] samples =
            {
                "1. a\n1. b\n\n- x\n- y\n\n1. c\n",
                "# T\n\n1. a\n   1. b\n      1. c\n   1. d\n1. e\n",
                "Texto\n5. a\n\n1. b\n1. c\n",
                "| a | b |\n|---|---|\n| 1 | 2 |\n\n1. a\n3. b\n",
                "> 1. cita\n> 1. cita\n",
            };
            foreach (string s in samples)
            {
                string once = MarkdownTools.Tidy(s), twice = MarkdownTools.Tidy(once);
                Same(once, twice, "Tidy twice of " + Show(s, 0));
            }
        }

        const string LsBase = "| A | B |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |";

        // Runs a table operation with the caret right after `at` (its first occurrence) and returns the text with "^" at
        // the caret.
        static string LsTable(string t, string at, EditCommands.TableOp op)
        {
            int s = t.IndexOf(at, StringComparison.Ordinal);
            Check(s >= 0, "no " + at + " in the table");
            s += at.Length;
            TextEdit e = EditCommands.Table(t, s, op);
            if (e.None) return null;
            return e.ApplyTo(t).Insert(e.SelStart, "^");
        }

        static void LsRowBelow()
        {
            Same("| A   | B   |\n| --- | --- |\n| 1   | 2   |\n| ^    |     |\n| 3   | 4   |", LsTable(LsBase, "| 1", EditCommands.TableOp.RowBelow), "row below 1");
            Same("| A   | B   |\n| --- | --- |\n| ^    |     |\n| 1   | 2   |\n| 3   | 4   |", LsTable(LsBase, "| A", EditCommands.TableOp.RowBelow), "row below the header");
            Same("| A   | B   |\n| --- | --- |\n| 1   | 2   |\n| 3   | 4   |\n| ^    |     |", LsTable(LsBase, "| 4", EditCommands.TableOp.RowBelow), "row below the last");
        }

        static void LsColumnRight()
        {
            // The caret stays on its row, in the new cell.
            Same("| A   | Columna 3 | B   |\n| --- | --------- | --- |\n| 1   | ^          | 2   |\n| 3   |           | 4   |",
                 LsTable(LsBase, "| 1", EditCommands.TableOp.ColumnRight), "column right of A");
            Same("| A   | B   | Columna 3 |\n| --- | --- | --------- |\n| 1   | 2   |           |\n| 3   | 4   | ^          |",
                 LsTable(LsBase, "| 4", EditCommands.TableOp.ColumnRight), "column right of B");
            Same("| A   | ^Columna 3 | B   |\n| --- | --------- | --- |\n| 1   |           | 2   |\n| 3   |           | 4   |",
                 LsTable(LsBase, "| A", EditCommands.TableOp.ColumnRight), "from the header");
        }

        static void LsDeleteRow()
        {
            Same("| A   | B   |\n| --- | --- |\n| ^3   | 4   |", LsTable(LsBase, "| 1", EditCommands.TableOp.DeleteRow), "delete row 1");
            Same("| A   | B   |\n| --- | --- |\n| ^1   | 2   |", LsTable(LsBase, "| 3", EditCommands.TableOp.DeleteRow), "delete the last row");
            Check(LsTable(LsBase, "| A", EditCommands.TableOp.DeleteRow) == null, "the header row was deleted");
            Check(LsTable(LsBase, "| --", EditCommands.TableOp.DeleteRow) == null, "the line of dashes was deleted");
        }

        static void LsDeleteColumn()
        {
            Same("| B   |\n| --- |\n| ^2   |\n| 4   |", LsTable(LsBase, "| 1", EditCommands.TableOp.DeleteColumn), "delete column A");
            string one = "| A |\n| --- |\n| 1 |";
            Check(LsTable(one, "| 1", EditCommands.TableOp.DeleteColumn) == null, "the only column was deleted");
        }

        static void LsKeeps()
        {
            string t = "| Izq | Centro | Der |\n|:----|:------:|----:|\n| `a|b` | x \\| y | 3 |";
            string got = LsTable(t, "| Izq", EditCommands.TableOp.RowBelow).Replace("^", "");
            string[] l = got.Split('\n');
            Check(l.Length == 4, "lines: " + l.Length);
            List<string> d = MarkdownDoc.Cells(l[1]);
            Check(d[0].StartsWith(":", StringComparison.Ordinal) && !d[0].EndsWith(":", StringComparison.Ordinal), "left alignment lost: " + l[1]);
            Check(d[1].StartsWith(":", StringComparison.Ordinal) && d[1].EndsWith(":", StringComparison.Ordinal), "centre alignment lost: " + l[1]);
            Check(!d[2].StartsWith(":", StringComparison.Ordinal) && d[2].EndsWith(":", StringComparison.Ordinal), "right alignment lost: " + l[1]);
            Check(l[3].Contains("`a|b`") && l[3].Contains("x \\| y"), "code or escaped pipe changed: " + l[3]);
            Check(MarkdownDoc.Cells(l[3]).Count == 3, "the last row has " + MarkdownDoc.Cells(l[3]).Count + " cells: " + l[3]);
            // The caret in the cell after a code span with a pipe is the third column, not the fourth.
            int s = t.IndexOf("| 3", StringComparison.Ordinal) + 2;
            TextEdit e = EditCommands.Table(t, s, EditCommands.TableOp.DeleteColumn);
            string after = e.ApplyTo(t);
            Check(!after.Contains("Der") && after.Contains("Centro") && after.Contains("`a|b`"), "deleted the wrong column: " + Show(after, 0));
            // Align: an untidy table comes out aligned, an aligned one is unchanged.
            string messy = "|a|bb|\n|-|-|\n|ccc|d|";
            string aligned = EditCommands.Table(messy, 1, EditCommands.TableOp.Align).ApplyTo(messy);
            Same("| a   | bb  |\n| --- | --- |\n| ccc | d   |", aligned, "aligned");
            Same(aligned, EditCommands.Table(aligned, 2, EditCommands.TableOp.Align).ApplyTo(aligned), "aligned twice");
        }

        static void LsFind()
        {
            Check(EditCommands.InTable(LsBase, 3), "the header is in the table");
            Check(EditCommands.InTable(LsBase, LsBase.Length), "the end of the last row is in the table");
            Check(!EditCommands.InTable("a | b\nc | d", 2), "pipes without a line of dashes made a table");
            Check(!EditCommands.InTable("Texto\n\n" + LsBase, 2), "the paragraph above is in the table");
            Check(!EditCommands.InTable(LsBase + "\n\nTexto", LsBase.Length + 4), "the paragraph below is in the table");
            // Without outer pipes.
            string bare = "A | B\n--- | ---\n1 | 2";
            Check(EditCommands.InTable(bare, bare.Length), "a table without outer pipes was not found");
            string got = LsTable(bare, "1 | ", EditCommands.TableOp.DeleteColumn).Replace("^", "");
            Same("| A   |\n| --- |\n| 1   |", got, "bare table, delete B");
        }

        static void LsRagged()
        {
            string t = "| A | B |\n| --- | --- |\n| 1 |\n| 3 | 4 | 5 |";
            string got = LsTable(t, "| 1", EditCommands.TableOp.Align).Replace("^", "");
            Check(got.Contains("5"), "the extra cell was lost: " + Show(got, 0));
            foreach (string l in got.Split('\n')) Check(MarkdownDoc.Cells(l).Count == 3, "a row is not 3 cells: " + l);
        }

        static void LsAround()
        {
            string t = "# T\n\nAntes | con barra\n\n" + LsBase + "\n\nDespu\u00E9s\n";
            string got = LsTable(t, "| 3", EditCommands.TableOp.RowBelow).Replace("^", "");
            Check(got.StartsWith("# T\n\nAntes | con barra\n\n", StringComparison.Ordinal), "the text before changed: " + Show(got, 0));
            Check(got.EndsWith("\n\nDespu\u00E9s\n", StringComparison.Ordinal), "the text after changed: " + Show(got, got.Length - 20));
        }
    }
}

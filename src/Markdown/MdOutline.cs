// Stackshot - The outline of a Markdown document: its headings (H1 to H3) for the side panel, the row that shows one in the
// list, and the toolbar button that opens the panel.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Stackshot
{
    public sealed class MdHeading
    {
        public int Level, Depth;    // Depth: levels below the document's highest heading
        public int Line, Block;     // its line (0-based) and its index among the top-level blocks
        public int Offset;          // where its line starts in the text
        public string Text;

        public override string ToString() { return Text; }
    }

    internal static class MdOutline
    {
        static readonly Regex SpaceRx = new Regex(@"\s+", RegexOptions.Compiled);

        // H1 to H3 at the top level of the document, in order, their text without Markdown.
        internal static List<MdHeading> Headings(List<MdBlock> blocks)
        {
            return Headings(blocks, null);
        }

        // memo: each heading's text by its Markdown from the previous call, refilled with this call's (the panel refreshes
        // after every pause in typing, and most headings have not changed since).
        internal static List<MdHeading> Headings(List<MdBlock> blocks, Dictionary<string, string> memo)
        {
            List<MdHeading> o = new List<MdHeading>();
            if (blocks == null) return o;
            Dictionary<string, string> seen = memo != null ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            int top = 3;
            for (int i = 0; i < blocks.Count; i++)
            {
                MdBlock b = blocks[i];
                if (b.Kind != MdKind.Heading || b.Level > 3 || b.Line < 0) continue;
                string t;
                if (memo == null || !memo.TryGetValue(b.Text, out t)) t = Clean(b.Text);
                if (seen != null) seen[b.Text] = t;
                if (t.Length == 0) continue;
                o.Add(new MdHeading { Level = b.Level, Line = b.Line, Block = i, Text = t });
                top = Math.Min(top, b.Level);
            }
            foreach (MdHeading h in o) h.Depth = h.Level - top;
            if (memo != null)
            {
                memo.Clear();
                foreach (KeyValuePair<string, string> kv in seen) memo[kv.Key] = kv.Value;
            }
            return o;
        }

        // A heading's text as it reads: emphasis, code and link marks gone, pictures left out (their alt text when there is
        // nothing else), spaces collapsed.
        internal static string Clean(string inline)
        {
            StringBuilder sb = new StringBuilder();
            string alt = null;
            foreach (MdSpan s in MarkdownDoc.Inline(inline))
            {
                if (s.Image)
                {
                    if (alt == null) alt = s.Text.StartsWith("\u25A3 ", StringComparison.Ordinal) ? s.Text.Substring(2) : s.Text;
                    continue;
                }
                sb.Append(s.Break ? " " : s.Text);
            }
            string t = SpaceRx.Replace(sb.ToString(), " ").Trim();
            return t.Length > 0 ? t : SpaceRx.Replace(alt ?? "", " ").Trim();
        }

        // Where each heading's line starts in the text (lines split at '\n', as the editor keeps them).
        internal static void Locate(List<MdHeading> list, string text)
        {
            int line = 0, at = 0;
            foreach (MdHeading h in list)
            {
                while (line < h.Line && at < text.Length)
                {
                    int nl = text.IndexOf('\n', at);
                    at = nl < 0 ? text.Length : nl + 1;
                    line++;
                }
                h.Offset = at;
            }
        }

        // The heading whose section holds a place in the text: the last one at or before it (-1 before the first).
        internal static int At(List<MdHeading> list, int offset)
        {
            int lo = 0, hi = list.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].Offset <= offset) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found;
        }
    }

    // One heading in the outline list, indented by its level; the list reuses it, so it fills itself from its data.
    public sealed class MdOutlineRow : Border
    {
        readonly TextBlock label = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        public MdOutlineRow()
        {
            Height = 28;
            Background = Brushes.Transparent;
            Child = label;
            DataContextChanged += delegate { Fill(DataContext as MdHeading); };
        }

        void Fill(MdHeading h)
        {
            label.Text = h == null ? "" : h.Text;
            ToolTip = h != null && h.Text.Length > 28 ? h.Text : null;
            if (h == null) return;
            label.Margin = new Thickness(10 + Math.Min(2, h.Depth) * 14, 0, 8, 0);
            label.FontWeight = h.Depth == 0 ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // A square toolbar button that stays pressed while its panel is open. Only an icon: the toolbar has no room left for
    // another label at the window's usual width (its buttons would lose theirs).
    public sealed class OutlineToggle : Drawn
    {
        readonly string label;
        bool on;
        public event Action Click;

        public OutlineToggle(string label)
        {
            this.label = label;
            Width = Height = 30;
            AutomationProperties.SetName(this, label);
            AcceptFocus();
        }

        public bool On
        {
            get { return on; }
            set { if (on == value) return; on = value; InvalidateVisual(); Access.Toggled(this, on); }
        }

        protected override void Clicked(Point p) { Action h = Click; if (h != null) h(); }
        protected override bool EnterActivates { get { return true; } }
        protected override double RingRadius { get { return 7; } }
        internal override string AccessName { get { return label; } }
        internal override AutomationControlType AccessType { get { return AutomationControlType.Button; } }
        internal override bool? AccessChecked { get { return on; } }

        protected override void Paint(DrawingContext dc)
        {
            Palette pal = Ds.Brushes;
            double w = ActualWidth, h = ActualHeight, hot = Hot, down = Down, px = Ink.Hair(this);
            Rect r = new Rect(0, 0, w, h);
            Color fg;
            if (pal.High) fg = HcPaint.Button(dc, r, 7, on ? ButtonKind.Primary : ButtonKind.Secondary, IsEnabled, hot, down, px);
            else if (on)
            {
                Color bg = Ds.WithAlpha(pal.Accent, (pal.Dark ? 0.30 : 0.14) + 0.06 * hot + 0.08 * down);
                dc.DrawRoundedRectangle(Ds.Brush(bg), new Pen(Ds.Brush(Ds.WithAlpha(pal.Accent, 0.45)), px), Ink.Inset(r, px / 2), 7 - px / 2, 7 - px / 2);
                fg = pal.TintInk;
            }
            else
            {
                // As a secondary MacButton.
                Color bg = pal.Dark ? Mix(Ds.Rgb(70, 70, 74), Ds.Rgb(84, 84, 89), hot) : Mix(Colors.White, Ds.Rgb(246, 246, 248), hot);
                if (down > 0) bg = Mix(bg, pal.Dark ? Colors.Black : Ds.Rgb(220, 220, 224), 0.2 * down);
                if (!pal.Dark)
                {
                    Rect sh = r;
                    sh.Offset(0, px);
                    dc.DrawRoundedRectangle(Ds.Brush(Ds.Argb(0.10, 0, 0, 0)), null, sh, 7, 7);
                }
                Pen edge = new Pen(Ds.Brush(pal.Dark ? Ds.Argb(0.10, 255, 255, 255) : Ds.Argb(0.12, 0, 0, 0)), px);
                dc.DrawRoundedRectangle(Ds.Brush(bg), edge, Ink.Inset(r, px / 2), 7 - px / 2, 7 - px / 2);
                fg = pal.Label;
            }
            // Three lines, the lower two indented: an outline.
            double s = 15, x = Math.Round((w - s) / 2), y = Math.Round((h - s) / 2);
            Pen p = new Pen(Ds.Brush(fg), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(p, new Point(x + 1, y + 3), new Point(x + s - 1, y + 3));
            dc.DrawLine(p, new Point(x + 5, y + 7.5), new Point(x + s - 1, y + 7.5));
            dc.DrawLine(p, new Point(x + 5, y + 12), new Point(x + s - 1, y + 12));
        }
    }
}

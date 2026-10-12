// Stackshot - Where the preview's top-level blocks are, for the outline panel: jump to a heading, find the one on screen.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Stackshot
{
    public sealed partial class MarkdownFlow
    {
        // The element on screen for top-level block i (null when there is none).
        internal Block BlockAt(int i)
        {
            return i >= 0 && i < shown.Count ? shown[i] : null;
        }

        // Where top-level block i starts, from the top of the document (NaN until it is laid out).
        internal double Top(int i)
        {
            Block b = BlockAt(i);
            ScrollViewer sv = Box != null ? Scroller : null;
            if (b == null || sv == null) return double.NaN;
            try
            {
                Rect r = b.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                return r.IsEmpty || double.IsNaN(r.Top) || double.IsInfinity(r.Top) ? double.NaN : r.Top + sv.VerticalOffset;
            }
            catch (InvalidOperationException) { return double.NaN; }
        }

        // Scrolls so top-level block i sits at the top of the view, a little below the edge.
        internal bool ScrollTo(int i)
        {
            Block b = BlockAt(i);
            if (b == null) return false;
            ScrollViewer sv = Scroller;
            double y = Top(i);
            if (sv == null || double.IsNaN(y)) { b.BringIntoView(); return true; }
            sv.ScrollToVerticalOffset(Math.Max(0, Math.Min(sv.ScrollableHeight, y - 12)));
            return true;
        }
    }
}

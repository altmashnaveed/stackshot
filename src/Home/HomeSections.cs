// Stackshot - The section index of a long page: its group titles as chips at the top (a click glides to the group), the
// same chips pinned under the toolbar once the page scrolls past them, and the chip of the group being read lit.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Stackshot
{
    public partial class HomeWindow
    {
        const double SectionPinH = 42, SectionPinTop = (SectionPinH - 1 - 26) / 2;   // the strip, its hairline, a chip

        List<SectionMark> sectionMarks;          // the groups of the page on show, or null when it has no chips
        FrameworkElement sectionInline;          // the chips at the top of the page
        readonly List<Border> sectionChips = new List<Border>(), sectionPinChips = new List<Border>();
        Border sectionPin;                       // the chips pinned under the toolbar
        System.Windows.Controls.Canvas sectionPinClip;   // a canvas: the row slides inside it without a layout clip
        StackPanel sectionPinRow;
        TranslateTransform sectionPinShift;
        bool sectionPinShown;
        int sectionActive = -1, sectionHeld = -1;
        double sectionHeldAt;                    // where a chip's glide ends: its chip stays lit until the page moves on

        internal List<string> SectionTitles
        {
            get
            {
                List<string> l = new List<string>();
                if (sectionMarks != null) foreach (SectionMark m in sectionMarks) l.Add(m.Title);
                return l;
            }
        }

        internal int SectionActive { get { return sectionActive; } }
        internal bool SectionPinned { get { return sectionPinShown; } }
        internal FrameworkElement SectionStripView { get { return sectionInline; } }
        internal FrameworkElement SectionPinView { get { return sectionPin; } }

        // After a page build: the chips go above the first group of a page with three groups or more that is over about
        // two screens tall.
        void SectionStrip()
        {
            sectionMarks = null;
            sectionInline = null;
            sectionChips.Clear();
            sectionPinChips.Clear();
            sectionActive = sectionHeld = -1;
            sectionPinShown = false;
            if (sectionPin != null)
            {
                sectionPin.BeginAnimation(OpacityProperty, null);
                sectionPin.Opacity = 0;
                sectionPin.Visibility = Visibility.Collapsed;
                sectionPinRow.Children.Clear();
            }
            if (liveBody != body || liveSections.Count < 3) return;
            int at = body.Children.IndexOf(liveSections[0].Anchor);
            if (at < 0 || !SectionsWanted()) return;
            sectionMarks = new List<SectionMark>(liveSections);
            WrapPanel wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            for (int i = 0; i < sectionMarks.Count; i++)
            {
                Border c = SectionChip(i);
                c.Margin = new Thickness(0, 0, 6, 6);
                wrap.Children.Add(c);
                sectionChips.Add(c);
            }
            body.Children.Insert(at, wrap);
            sectionInline = wrap;
            SectionPinBuild();
            SectionPaint(0);
        }

        // Measured at the width the page will have (with its scroll bar), which layout then reuses.
        bool SectionsWanted()
        {
            double w = scroller.ActualWidth > 1 ? scroller.ActualWidth - 12 : LW - Side - 12;
            double h = scroller.ActualHeight > 1 ? scroller.ActualHeight : LH;
            body.Measure(new Size(w, double.PositiveInfinity));
            return body.DesiredSize.Height > 2 * h;
        }

        // As the history filters: a capsule, the one lit in the accent.
        Border SectionChip(int i)
        {
            Palette pal = Ds.Brushes;
            string title = sectionMarks[i].Title;
            Border c = new TapBorder { CornerRadius = new CornerRadius(13), Height = 26, Padding = new Thickness(12, 0, 12, 1), Cursor = Cursors.Hand, Background = Ds.Brush(pal.Control),
                                       BorderBrush = Ds.Brush(pal.Hairline), BorderThickness = HcPaint.Edge, Label = title, Role = Access.Link, Do = delegate { SectionGo(i); } };
            c.Child = Label(title, Ds.Medium, 12.5, pal.Label);
            c.MouseEnter += delegate { if (i != sectionActive) c.Background = Ds.Brush(pal.ControlHover); };
            c.MouseLeave += delegate { SectionPaintChip(c, i); };
            c.MouseLeftButtonUp += delegate { SectionGo(i); };
            return c;
        }

        void SectionPaintChip(Border c, int i)
        {
            Palette pal = Ds.Brushes;
            bool on = i == sectionActive;
            c.Background = Ds.Brush(on ? pal.Accent : c.IsMouseOver ? pal.ControlHover : pal.Control);
            TextBlock t = c.Child as TextBlock;
            if (t != null) t.Foreground = on ? Ds.Brush(pal.OnAccent) : Ds.Brush(pal.Label);
        }

        void SectionPaint(int act)
        {
            sectionActive = act;
            for (int i = 0; i < sectionChips.Count; i++) SectionPaintChip(sectionChips[i], i);
            for (int i = 0; i < sectionPinChips.Count; i++) SectionPaintChip(sectionPinChips[i], i);
            SectionPinFollow(true);
        }

        // Glides to the group: its title just under the pinned chips.
        internal void SectionGo(int i)
        {
            if (sectionMarks == null || i < 0 || i >= sectionMarks.Count) return;
            double to = Math.Max(0, Math.Min(scroller.ScrollableHeight, SectionTop(i) - Bar - SectionPinH - 10));
            sectionHeld = i;
            sectionHeldAt = to;
            SectionPaint(i);
            GlideTo(to);
        }

        double SectionTop(int i)
        {
            try { return sectionMarks[i].Anchor.TranslatePoint(new Point(0, 0), pageHost).Y; }
            catch (InvalidOperationException) { return double.MaxValue; }
        }

        // The group being read: the last one whose title has passed under the bars (the first of two side by side); at
        // the end of the page, the last one.
        int SectionAt(double off, bool pin)
        {
            double line = off + Bar + (pin ? SectionPinH : 0) + 24, last = double.NegativeInfinity;
            int act = 0, end = 0;
            for (int i = 0; i < sectionMarks.Count; i++)
            {
                double t = SectionTop(i);
                if (t > last + 1)
                {
                    if (t <= line) act = i;
                    end = i;
                }
                last = Math.Max(last, t);
            }
            if (scroller.ScrollableHeight > 0 && off >= scroller.ScrollableHeight - 2) act = end;
            return act;
        }

        // Follows the page's scroll (from ToolbarShade): pins the chips once they reach the toolbar, where the pinned
        // strip draws the hairline instead of the toolbar, and lights the chip of the group being read.
        void SectionShade()
        {
            if (sectionMarks == null || sectionInline == null) return;
            if (SearchShowing) { SectionPinShow(false, true); return; }
            double off = scroller.VerticalOffset;
            Point p;
            try { p = sectionInline.TranslatePoint(new Point(0, 0), pageHost); }
            catch (InvalidOperationException) { return; }
            // Once the first line of chips reaches the spot of the pinned ones, those take over in the same place.
            bool pin = off > 0 && p.Y - off <= Bar + SectionPinTop;
            SectionPinShow(pin, false);
            if (pin) toolbar.BorderBrush = Brushes.Transparent;
            int act = SectionAt(off, pin);
            if (sectionHeld >= 0)
            {
                if (glide != null || Math.Abs(off - sectionHeldAt) < 3) act = sectionHeld;
                else sectionHeld = -1;
            }
            if (act != sectionActive) SectionPaint(act);
        }

        // ---- Pinned under the toolbar

        void SectionPinBuild()
        {
            Palette pal = Ds.Brushes;
            if (sectionPin == null)
            {
                sectionPinShift = new TranslateTransform();
                sectionPinRow = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = sectionPinShift };
                System.Windows.Controls.Canvas.SetLeft(sectionPinRow, 40);
                System.Windows.Controls.Canvas.SetTop(sectionPinRow, SectionPinTop);
                sectionPinClip = new System.Windows.Controls.Canvas { ClipToBounds = true };
                sectionPinClip.Children.Add(sectionPinRow);
                sectionPin = new Border
                {
                    Height = SectionPinH, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Bar, 0, 0), BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = sectionPinClip, Visibility = Visibility.Collapsed, Opacity = 0
                };
                sectionPin.PreviewMouseWheel += SectionPinWheel;
                sectionPinClip.SizeChanged += delegate { SectionPinFollow(false); };
                pane.Children.Insert(pane.Children.IndexOf(toolbar), sectionPin);
            }
            Color bg = pal.Window;
            sectionPin.Background = Ds.Brush(Color.FromArgb(245, bg.R, bg.G, bg.B));
            sectionPin.BorderBrush = Ds.Brush(pal.Separator);
            sectionPinShift.BeginAnimation(TranslateTransform.XProperty, null);
            sectionPinShift.X = 0;
            sectionPinClip.OpacityMask = null;
            for (int i = 0; i < sectionMarks.Count; i++)
            {
                Border c = SectionChip(i);
                c.Margin = new Thickness(0, 0, 6, 0);
                sectionPinRow.Children.Add(c);
                sectionPinChips.Add(c);
            }
        }

        // At once when the search results cover the page (the strip may sit above them).
        void SectionPinShow(bool on, bool now)
        {
            if (sectionPin == null || on == sectionPinShown) return;
            sectionPinShown = on;
            sectionPin.IsHitTestVisible = on;
            if (on) sectionPin.Visibility = Visibility.Visible;
            if (now || !HomeMotion())
            {
                sectionPin.BeginAnimation(OpacityProperty, null);
                sectionPin.Opacity = on ? 1 : 0;
                if (!on) sectionPin.Visibility = Visibility.Collapsed;
                return;
            }
            DoubleAnimation a = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 140 : 110));
            if (!on) a.Completed += delegate { if (!sectionPinShown && sectionPin.Opacity < 0.01) sectionPin.Visibility = Visibility.Collapsed; };
            sectionPin.BeginAnimation(OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
        }

        // One line of chips, slid so the lit one is in view, faded at an edge with more beyond it.
        void SectionPinFollow(bool animate)
        {
            if (sectionPinRow == null || sectionPinChips.Count == 0 || sectionPinClip.ActualWidth < 1) return;
            double view = sectionPinClip.ActualWidth - 80, total = 0, x0 = 0, x1 = 0;
            for (int i = 0; i < sectionPinChips.Count; i++)
            {
                double w = sectionPinChips[i].ActualWidth;
                if (i == sectionActive) { x0 = total; x1 = total + w; }
                total += w + 6;
            }
            total -= 6;
            double min = Math.Min(0, view - total), s = sectionPinShift.X;
            if (sectionActive >= 0)
            {
                if (x0 + s < 24) s = 24 - x0;
                if (x1 + s > view - 24) s = view - 24 - x1;
            }
            SectionPinSlide(Math.Max(min, Math.Min(0, s)), min, animate);
        }

        void SectionPinSlide(double s, double min, bool animate)
        {
            if (animate && HomeMotion() && Math.Abs(s - sectionPinShift.X) > 0.5)
                sectionPinShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(s, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
                                               HandoffBehavior.SnapshotAndReplace);
            else
            {
                sectionPinShift.BeginAnimation(TranslateTransform.XProperty, null);
                sectionPinShift.X = s;
            }
            double w = sectionPinClip.ActualWidth;
            if (min >= 0 || w < 1) { sectionPinClip.OpacityMask = null; return; }
            double e = 40 / w;
            // In the strip's own coordinates: relative ones would span the whole row, most of it out of view.
            LinearGradientBrush m = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(w, 0) };
            m.GradientStops.Add(new GradientStop(s < -0.5 ? Colors.Transparent : Colors.Black, 0));
            m.GradientStops.Add(new GradientStop(Colors.Black, e));
            m.GradientStops.Add(new GradientStop(Colors.Black, 1 - e));
            m.GradientStops.Add(new GradientStop(s > min + 0.5 ? Colors.Transparent : Colors.Black, 1));
            m.Freeze();
            sectionPinClip.OpacityMask = m;
        }

        // The wheel over the pinned chips scrolls the page; with Shift it slides the chips.
        void SectionPinWheel(object o, MouseWheelEventArgs e)
        {
            e.Handled = true;
            StopGlide();
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                scroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent });
                return;
            }
            if (sectionPinClip.ActualWidth < 1) return;
            double total = 0;
            foreach (Border c in sectionPinChips) total += c.ActualWidth + 6;
            double min = Math.Min(0, sectionPinClip.ActualWidth - 80 - (total - 6));
            SectionPinSlide(Math.Max(min, Math.Min(0, sectionPinShift.X + e.Delta / 2.0)), min, false);
        }
    }
}

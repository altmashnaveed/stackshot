// Stackshot - Thumbnails tucked into the screen edge: after a while unused, the stack slides left one card after another
// until only a sliver of each shows; moving the pointer onto a sliver, the wheel, a new capture or another monitor brings
// them all back, the nearest first.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        readonly Timer tuckTimer = new Timer { Interval = 250 };
        bool tucked, tuckHooked, peeked;   // peeked: brought out by the pointer, not by a new capture
        const double PeekBackMs = 1500;
        double tuckActive;            // last time the stack was in use (Anim.Now)
        Point tuckMouse;              // where the pointer was when they tucked: a card sliding under a still pointer isn't a touch

        public bool Tucked { get { return tucked; } }

        // The left edge of the stack's monitor, where tucked cards hide.
        public int TuckEdge { get { return AnchorScreen().WorkingArea.Left; } }

        // The stack was used just now (a new card, the pointer over it): it stays out a while longer.
        void TuckAwake()
        {
            tuckActive = Anim.Now;
            if (Test || settings.StackTuck <= 0 || cards.Count == 0) { tuckTimer.Stop(); return; }
            if (!tuckHooked)
            {
                tuckHooked = true;
                tuckTimer.Tick += delegate
                {
                    try { TuckTick(); }
                    catch (Exception ex) { Log("Esconder miniaturas: " + ex.Message); }
                };
            }
            if (!tucked) tuckTimer.Start();
        }

        void TuckTick()
        {
            if (exiting || tucked || cards.Count == 0 || settings.StackTuck <= 0) { tuckTimer.Stop(); return; }
            // In use: under the pointer, pressed, swiped or dragged, or a button held (a drag may be on its way to it).
            bool busy = Control.MouseButtons != MouseButtons.None || savingCard != null;
            Point m = Control.MousePosition;
            foreach (Card c in cards) if (c.Busy || (c.Hovered && c.Bounds.Contains(m))) busy = true;
            if (busy) { tuckActive = Anim.Now; return; }
            // After a new capture it waits the chosen time (room to paste or drag it); brought out only to look, it goes
            // back shortly after the pointer leaves, like an auto-hiding dock.
            double wait = settings.StackTuck * 1000.0;
            if (peeked) wait = Math.Min(wait, PeekBackMs);
            if (Anim.Now - tuckActive < wait) return;
            Screen scr = AnchorScreen();
            tuckTimer.Stop();
            tucked = true;
            tuckMouse = Control.MousePosition;
            HideChips();
            // Top to bottom, a beat apart.
            int order = 0;
            for (int i = cards.Count - 1; i >= 0; i--)
                if (!cards[i].Parked && !cards[i].Leaving) cards[i].Tuck(true, scr.WorkingArea.Left, 20 * order++);
            // Once they are in, a tab beside them says they are there and how many.
            PlaceTab(20 * order + 200);
            Log("Miniaturas escondidas en el borde");
        }

        TuckTab tab;
        double tabDelay;              // for the next placement of the tab (after a move to another monitor)

        // The tab beside the tucked cards: centred on them, with the number of captures in the stack.
        void PlaceTab(double delay)
        {
            if (!tucked) { HideTab(); return; }
            int top = int.MaxValue, bottom = int.MinValue;
            foreach (Card c in cards)
            {
                if (c.Parked || c.Leaving) continue;
                top = Math.Min(top, c.Home.Top);
                bottom = Math.Max(bottom, c.Home.Bottom);
            }
            if (top > bottom) { HideTab(); return; }
            Screen scr = AnchorScreen();
            if (tab == null || tab.IsDisposed) tab = new TuckTab(this);
            tab.Show(scr, scr.WorkingArea.Left, (top + bottom) / 2, cards.Count, delay);
        }

        void HideTab()
        {
            if (tab != null && !tab.IsDisposed) tab.Away();
        }

        readonly Timer scrubTimer = new Timer { Interval = 30 };
        bool scrubbing, scrubHooked;
        Card scrubOn;                 // the card out under the pointer
        double scrubAway = -1;        // when the pointer left the slivers (Anim.Now), or -1

        // The pointer on a sliver (pressed: it counts without moving): the card under it slides out and its neighbours
        // peek a little, like the dock on the mascot page; running along the slivers brings each one out in turn.
        public void TuckTouched(bool pressed)
        {
            if (!tucked || (!pressed && Control.MousePosition == tuckMouse)) return;
            if (!scrubHooked)
            {
                scrubHooked = true;
                scrubTimer.Tick += delegate
                {
                    try { ScrubTick(); }
                    catch (Exception ex) { Log("Esconder miniaturas: " + ex.Message); StopScrub(true); }
                };
            }
            if (!scrubbing)
            {
                scrubbing = true;
                HideTab();   // it would sit over the card coming out
                scrubOn = null;
                scrubAway = -1;
                scrubTimer.Start();
            }
            ScrubTick();
        }

        // The tab: all of them out.
        public void TabTouched()
        {
            if (tucked && Control.MousePosition != tuckMouse) Untuck();
        }

        void ScrubTick()
        {
            if (!tucked || exiting) { StopScrub(false); return; }
            List<Card> shown = new List<Card>();
            foreach (Card c in cards) if (!c.Parked && !c.Leaving) shown.Add(c);
            if (shown.Count == 0) { StopScrub(false); return; }
            foreach (Card c in shown) if (c.Busy) return;   // pressed, swiped or dragged: it stays as it is
            shown.Sort(delegate(Card a, Card b) { return a.Home.Top.CompareTo(b.Home.Top); });
            Point m = Control.MousePosition;
            Screen scr = AnchorScreen();
            float s = ScaleFor(scr);
            int edge = scr.WorkingArea.Left;
            // The slivers' strip, or the whole column while a card is out (to reach its buttons).
            int right = edge + (int)Math.Round((scrubOn != null ? 16 + 256 + 24 : Card.Peek + 14) * s);
            int top = shown[0].Home.Top - (int)Math.Round(20 * s), bottom = shown[shown.Count - 1].Home.Bottom + (int)Math.Round(20 * s);
            if (m.X < edge - 2 || m.X > right || m.Y < top || m.Y > bottom)
            {
                // Gone from them for a moment: they all slide back in.
                if (scrubAway < 0) scrubAway = Anim.Now;
                if (Anim.Now - scrubAway > 160) StopScrub(true);
                return;
            }
            scrubAway = -1;
            // The card under the pointer, or the nearest one when it is in a gap between two.
            Card on = null;
            int best = int.MaxValue;
            foreach (Card c in shown)
            {
                int d = m.Y < c.Home.Top ? c.Home.Top - m.Y : m.Y > c.Home.Bottom ? m.Y - c.Home.Bottom : 0;
                if (d < best) { best = d; on = c; }
            }
            if (on == scrubOn) return;
            scrubOn = on;
            int k = shown.IndexOf(on);
            for (int i = 0; i < shown.Count; i++)
            {
                int d = Math.Abs(i - k);
                shown[i].Reveal(d == 0 ? 1 : d == 1 ? 0.26 : 0);
            }
        }

        void StopScrub(bool backIn)
        {
            scrubTimer.Stop();
            scrubbing = false;
            scrubOn = null;
            scrubAway = -1;
            if (backIn)
            {
                foreach (Card c in cards) c.Reveal(0);
                PlaceTab(220);
            }
            // A card sliding back under a still pointer is not a new touch.
            tuckMouse = Control.MousePosition;
        }

        // Everything back out, the card nearest the pointer first, then the pills; the wait to tuck starts again.
        public void Untuck()
        {
            if (!tucked) { TuckAwake(); return; }
            StopScrub(false);
            tucked = false;
            peeked = true;
            HideTab();
            // The pills come back first; the cards keep their tucked spot until each one's turn.
            Relayout();
            int my = Control.MousePosition.Y;
            List<Card> shown = new List<Card>();
            foreach (Card c in cards) if (!c.Parked && !c.Leaving) shown.Add(c);
            shown.Sort(delegate(Card a, Card b) { return Math.Abs(a.Middle - my).CompareTo(Math.Abs(b.Middle - my)); });
            for (int i = 0; i < shown.Count; i++) shown[i].Tuck(false, 0, 14 * i);
            foreach (Card c in cards) if (c.Parked || c.Leaving) c.Tuck(false, 0, 0);
            ToastsFollowStack();   // notices make room for the cards again
            TuckAwake();
        }
    }

    // The tab beside tucked thumbnails: a small dark capsule on the screen edge with an arrow and how many captures wait
    // there. It slides out once the cards are in and goes as they come back; the pointer on it brings them out. It only
    // repaints when it appears, when its number changes and when the pointer comes over it or leaves.
    class TuckTab : FloatWindow
    {
        readonly ShotStack owner;
        int count = -1;
        bool hot, away = true;
        System.Windows.Media.Imaging.BitmapSource shadow;

        public TuckTab(ShotStack owner) { this.owner = owner; Cursor = Cursors.Hand; }

        protected override bool PerPixel { get { return true; } }

        public void Show(Screen scr, int edge, int middle, int n, double delay)
        {
            float ns = ShotStack.ScaleFor(scr);
            if (ns != s) shadow = null;
            s = ns;
            Pad = P(18);
            SetSize(new Size(P(26), P(54)));
            double nx = edge + P(Card.Peek) + P(8), ny = middle - body.Height / 2.0;
            bool changed = n != count;
            count = n;
            if (away)
            {
                away = false;
                hot = false;
                JumpTo(nx - P(14), ny);
                alpha.Set(0);
                ApplyAlpha();
                ShowQuiet();
                Redraw();
                alpha.Go(1, 240, delay, Ease.OutCubic, null);
                MoveTo(nx, ny, 300, 0.74, delay);
                return;
            }
            if (changed) Redraw();
            MoveTo(nx, ny, 320, 0.85, 0);
        }

        public void Away()
        {
            if (away) return;
            away = true;
            hot = false;
            MoveTo(x - P(12), y, 300, 1, 0);
            alpha.Go(0, 140, 0, Ease.OutCubic, delegate { if (away && !IsDisposed) Hide(); });
            Anim.Wake(this);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (away) return;
            if (!hot) { hot = true; Redraw(); }
            owner.TabTouched();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot && !away) { hot = false; Redraw(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!away) owner.Untuck();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!away) owner.Untuck();
        }

        protected override void PaintSurface(System.Windows.Media.DrawingContext dc, int w, int h)
        {
            bool high = Ds.Brushes.High;
            double r = body.Width / 2.0;
            if (shadow == null)
                shadow = Ink.Shadow(w, h, new System.Windows.Rect(Pad, Pad + P(3), body.Width, body.Height), r, P(12), Ds.Argb(0.40, 0, 0, 0));
            if (!high) dc.DrawImage(shadow, new System.Windows.Rect(0, 0, w, h));
            dc.PushTransform(new System.Windows.Media.TranslateTransform(Pad, Pad));
            System.Windows.Rect b = new System.Windows.Rect(0, 0, body.Width, body.Height);
            Ink.Round(dc, Palette.HudBack(hot ? Ds.Argb(0.96, 44, 44, 48) : Ds.Argb(0.90, 30, 30, 32)), b, r);
            Ink.Hairline(dc, Palette.HudLine, b, r);
            System.Windows.Media.Color white = Palette.HudLabel;
            double gs = P(13);
            Glyph.Draw(dc, "right", (body.Width - gs) / 2 + (hot ? P(1) : 0), P(9), gs, white, Math.Max(1.6, 2 * s));
            string num = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            System.Windows.Media.FormattedText t = Ink.Px(num, Ds.Semibold, P(count > 9 ? 10 : 11.5f), Palette.HudLabel2);
            dc.DrawText(t, new System.Windows.Point(Math.Round((body.Width - t.Width) / 2), Math.Round(body.Height - P(9) - t.Height)));
            dc.Pop();
        }
    }
}

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

        // A sliver was touched (only if the pointer really moved since they tucked).
        public void TuckTouched()
        {
            if (!tucked || Control.MousePosition == tuckMouse) return;
            Untuck();
        }

        // Everything back out, the card nearest the pointer first, then the pills; the wait to tuck starts again.
        public void Untuck()
        {
            if (!tucked) { TuckAwake(); return; }
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
            owner.TuckTouched();
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

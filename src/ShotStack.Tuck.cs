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
        readonly Timer tuckTimer = new Timer { Interval = 400 };
        bool tucked, tuckHooked;
        double tuckActive;            // last time the stack was in use (Anim.Now)
        Point tuckMouse;              // where the pointer was when they tucked: a card sliding under a still pointer isn't a touch

        public bool Tucked { get { return tucked; } }

        // The left edge of the stack's monitor, where tucked cards hide.
        public int TuckEdge { get { return AnchorScreen().Bounds.Left; } }

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
            bool busy = Control.MouseButtons != MouseButtons.None;
            Point m = Control.MousePosition;
            foreach (Card c in cards) if (c.Busy || (c.Hovered && c.Bounds.Contains(m))) busy = true;
            if (busy) { tuckActive = Anim.Now; return; }
            if (Anim.Now - tuckActive < settings.StackTuck * 1000.0) return;
            Screen scr = AnchorScreen();
            if (!CanTuck(scr)) return;
            tuckTimer.Stop();
            tucked = true;
            tuckMouse = Control.MousePosition;
            HideChips();
            // Top to bottom, a beat apart.
            int order = 0;
            for (int i = cards.Count - 1; i >= 0; i--)
                if (!cards[i].Parked && !cards[i].Leaving) cards[i].Tuck(true, scr.Bounds.Left, 34 * order++);
            Log("Miniaturas escondidas en el borde");
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
            // The pills come back first; the cards keep their tucked spot until each one's turn.
            Relayout();
            int my = Control.MousePosition.Y;
            List<Card> shown = new List<Card>();
            foreach (Card c in cards) if (!c.Parked && !c.Leaving) shown.Add(c);
            shown.Sort(delegate(Card a, Card b) { return Math.Abs(a.Middle - my).CompareTo(Math.Abs(b.Middle - my)); });
            for (int i = 0; i < shown.Count; i++) shown[i].Tuck(false, 0, 22 * i);
            foreach (Card c in cards) if (c.Parked || c.Leaving) c.Tuck(false, 0, 0);
            ToastsFollowStack();   // notices make room for the cards again
            TuckAwake();
        }

        // Only where nothing would show the hidden part: no taskbar on the left and no monitor right next to that edge.
        static bool CanTuck(Screen scr)
        {
            Rectangle b = scr.Bounds;
            if (scr.WorkingArea.Left > b.Left) return false;
            foreach (Screen o in Screen.AllScreens)
            {
                if (o.DeviceName == scr.DeviceName) continue;
                Rectangle r = o.Bounds;
                if (r.Left < b.Left && r.Right >= b.Left - 2 && r.Bottom > b.Top && r.Top < b.Bottom) return false;
            }
            return true;
        }
    }
}

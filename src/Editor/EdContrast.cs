// Stackshot - The editor's chrome (GDI) under a Windows contrast theme: what is on is filled with Highlight, the pointer
// outlines in Highlight, knobs and tracks get a border in the text colour; nothing translucent. The capture never changes.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using M = System.Windows.Media;

namespace Stackshot
{
    static class EdHc
    {
        // An item of a bar under a contrast theme: Highlight when on, an outline in Highlight under the pointer. Returns the
        // colour for its icon or label.
        public static M.Color Item(Graphics g, RectangleF r, float radius, bool on, float hot, bool enabled)
        {
            Palette pal = Ds.Brushes;
            if (!enabled) return pal.Label3;
            if (on)
            {
                EdLook.Fill(g, r, radius, EdLook.C(pal.Accent));
                if (hot > 0.5f) Outline(g, RectangleF.Inflate(r, -2, -2), Math.Max(0, radius - 2), pal.OnAccent);
                return pal.OnAccent;
            }
            if (hot > 0.5f) Outline(g, r, radius, pal.Accent);
            return pal.Label;
        }

        // A push button under a contrast theme: ButtonFace with a border in ButtonText (two pixels under the pointer).
        public static M.Color Button(Graphics g, RectangleF r, float radius, float hot, bool enabled)
        {
            Palette pal = Ds.Brushes;
            EdLook.Fill(g, r, radius, EdLook.C(pal.Sys.ButtonFace));
            Outline(g, r, radius, enabled ? pal.Sys.ButtonText : pal.Label3);
            if (enabled && hot > 0.5f) Outline(g, RectangleF.Inflate(r, -1, -1), Math.Max(0, radius - 1), pal.Sys.ButtonText);
            return enabled ? pal.Sys.ButtonText : pal.Label3;
        }

        // A segment of a segmented control under a contrast theme: the chosen one in Highlight (GrayText while the control
        // is off), the one under the pointer outlined. Returns the colour for its label.
        public static Color Segment(Graphics g, RectangleF r, float radius, bool sel, bool hot, bool enabled)
        {
            Palette pal = Ds.Brushes;
            if (sel && enabled) EdLook.Fill(g, r, radius, EdLook.C(pal.Accent));
            else if (sel) Outline(g, r, radius, pal.Label3);
            else if (hot && enabled) Outline(g, r, radius, pal.Accent);
            return EdLook.C(sel && enabled ? pal.OnAccent : enabled ? pal.Label : pal.Label3);
        }

        public static void Outline(Graphics g, RectangleF r, float radius, M.Color c)
        {
            EdLook.Hairline(g, r, radius, EdLook.C(c));
        }

        // A round knob (sliders, switches): the window colour with a border in the text colour.
        public static void Knob(Graphics g, RectangleF k)
        {
            Palette pal = Ds.Brushes;
            using (SolidBrush b = new SolidBrush(EdLook.C(pal.Window))) g.FillEllipse(b, k);
            using (Pen p = new Pen(EdLook.C(pal.Label), 1.5f)) g.DrawEllipse(p, RectangleF.Inflate(k, -0.75f, -0.75f));
        }

        // A track or a group of segments: an outline in the text colour (a ButtonFace fill would not show on the window).
        public static void Track(Graphics g, RectangleF r, float radius)
        {
            Outline(g, r, radius, Ds.Brushes.Label);
        }
    }
}

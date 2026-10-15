// Stackshot - More Pokemon tributes, each with its own build: Bulbasaur (spots and the bulb on its back, small pointed
// ears, red eyes), Squirtle (the shell behind with its pale rim, the yellow plastron, a curly tail), Jigglypuff (a pink
// ball with the curl on its forehead, pointed ears dark inside and big teal eyes), Snorlax (dark teal and wide, a cream
// face and belly, always sleepy), Gengar (purple, spikes along its back, tall ears, red eyes under a sly lid and a white
// grin) and Mew (small and pink, a long thin tail with a round tip, big blue eyes). Fan tributes, not affiliated with
// their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindBulbasaur = 28, KindSquirtle = 29, KindJigglypuff = 30, KindSnorlax = 31, KindGengar = 32, KindMew = 33;

        public static bool IsPokemon(int kind) { return kind >= KindBulbasaur && kind <= KindMew; }

        static readonly Color PokeCream = Color.FromArgb(246, 234, 206), PokeCream2 = Color.FromArgb(222, 202, 166);

        // ---- Behind the body.
        static void PokemonBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            switch (l.Kind)
            {
                case KindBulbasaur:
                {
                    // The bulb on its back, peeking over the head, breathing a little.
                    float k = 1 + (float)Math.Sin(p.Now / 900.0) * 0.02f;
                    float cx = 0.03f * D, cy = -0.27f * D, w = 0.46f * D * k, h = 0.36f * D * k;
                    using (GraphicsPath b = new GraphicsPath())
                    {
                        b.AddBezier(P(cx, cy - h * 0.62f), P(cx + w * 0.2f, cy - h * 0.5f), P(cx + w * 0.55f, cy - h * 0.25f), P(cx + w * 0.5f, cy + h * 0.2f));
                        b.AddBezier(P(cx + w * 0.5f, cy + h * 0.2f), P(cx + w * 0.45f, cy + h * 0.55f), P(cx - w * 0.45f, cy + h * 0.55f), P(cx - w * 0.5f, cy + h * 0.2f));
                        b.AddBezier(P(cx - w * 0.5f, cy + h * 0.2f), P(cx - w * 0.55f, cy - h * 0.25f), P(cx - w * 0.2f, cy - h * 0.5f), P(cx, cy - h * 0.62f));
                        b.CloseFigure();
                        RectangleF r = new RectangleF(cx - w / 2, cy - h * 0.62f, w, h);
                        Fill(g, b, r, Color.FromArgb(120, 210, 120), Color.FromArgb(36, 128, 76), 70f);
                        Shine(g, b, r, 90);
                        // The seams of its leaves running down from the top.
                        using (Pen s = new Pen(Color.FromArgb(120, 20, 90, 50), Math.Max(1f, D * 0.01f)))
                        {
                            s.StartCap = LineCap.Round;
                            s.EndCap = LineCap.Round;
                            g.DrawBezier(s, P(cx, cy - h * 0.6f), P(cx - w * 0.22f, cy - h * 0.35f), P(cx - w * 0.26f, cy), P(cx - w * 0.22f, cy + h * 0.25f));
                            g.DrawBezier(s, P(cx, cy - h * 0.6f), P(cx + w * 0.22f, cy - h * 0.35f), P(cx + w * 0.26f, cy), P(cx + w * 0.22f, cy + h * 0.25f));
                        }
                        Stroke(g, b, Color.FromArgb(80, 10, 70, 40), D * 0.008f);
                    }
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.24f * D, -0.2f * D, (float)(s * (44 + (s < 0 ? p.EarL : p.EarR) * 0.5)), 0.16f, 0.8f, Lighter(c1, 0.05), c2, Color.FromArgb(70, Darker(c2, 0.2)));
                    foreach (int s in Sides) Foot(g, D, s, Lighter(c1, 0.05), c2, p);
                    break;
                }
                case KindSquirtle:
                {
                    // The shell: a pale rim with the brown back inside it, showing at the sides.
                    RectangleF rim = new RectangleF(-0.42f * D, -0.1f * D, 0.84f * D, 0.46f * D);
                    using (GraphicsPath rp = new GraphicsPath())
                    {
                        rp.AddEllipse(rim);
                        Fill(g, rp, rim, Color.FromArgb(252, 246, 228), Color.FromArgb(220, 206, 172), 90f);
                    }
                    RectangleF shell = new RectangleF(-0.38f * D, -0.07f * D, 0.76f * D, 0.4f * D);
                    using (GraphicsPath sp = new GraphicsPath())
                    {
                        sp.AddEllipse(shell);
                        Fill(g, sp, shell, Color.FromArgb(196, 128, 62), Color.FromArgb(120, 70, 30), 80f);
                    }
                    { GraphicsState ts = TailSide(g, p); CurlyTail(g, D, c1, c2, p.Now); g.Restore(ts); }
                    foreach (int s in Sides) Foot(g, D, s, Lighter(c1, 0.05), c2, p);
                    break;
                }
                case KindJigglypuff:
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.2f * D, -0.25f * D, (float)(s * (34 + (s < 0 ? p.EarL : p.EarR) * 0.6)), 0.17f, 0.85f, Lighter(c1, 0.05), c2, Color.FromArgb(230, 48, 36, 48));
                    foreach (int s in Sides) Foot(g, D, s, Lighter(c1, 0.05), c2, p);
                    break;
                case KindSnorlax:
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.22f * D, -0.23f * D, (float)(s * (26 + (s < 0 ? p.EarL : p.EarR) * 0.4)), 0.12f, 0.85f, Lighter(c1, 0.05), c2, Color.FromArgb(60, Darker(c2, 0.2)));
                    foreach (int s in Sides) BigFoot(g, D, s, p);
                    break;
                case KindGengar:
                {
                    // Spikes all along its back, then the tall ears.
                    for (int i = 0; i < 7; i++)
                    {
                        double a = (-78 + i * 26) * Math.PI / 180;
                        float bx = (float)Math.Sin(a) * 0.33f * D, by = 0.02f * D - (float)Math.Cos(a) * 0.26f * D;
                        float tx = (float)Math.Sin(a) * 0.48f * D, ty = 0.02f * D - (float)Math.Cos(a) * 0.4f * D;
                        float nx = (float)Math.Cos(a) * 0.065f * D, ny = (float)Math.Sin(a) * 0.065f * D;
                        using (GraphicsPath sp = Poly(P(bx - nx, by - ny), P(tx, ty), P(bx + nx, by + ny)))
                        {
                            Fill(g, sp, sp.GetBounds(), Lighter(c1, 0.04), c2, 80f);
                            Stroke(g, sp, Color.FromArgb(60, Darker(c2, 0.3)), D * 0.006f);
                        }
                    }
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.2f * D, -0.22f * D, (float)(s * (24 + (s < 0 ? p.EarL : p.EarR) * 0.5)), 0.24f, 0.62f, Lighter(c1, 0.05), c2, Color.FromArgb(120, Darker(c2, 0.25)));
                    foreach (int s in Sides) Foot(g, D, s, Lighter(c1, 0.05), c2, p);
                    break;
                }
                case KindMew:
                {
                    // The long thin tail curling up behind, with its round tip.
                    GraphicsState ts = TailSide(g, p);
                    float angle;
                    PointF tip = TaperTail(g, D, Lighter(c1, 0.05), c2, p.Now, 0.05f, 0.024f, out angle);
                    GraphicsState st = g.Save();
                    g.TranslateTransform(tip.X, tip.Y);
                    g.RotateTransform(angle + 90);
                    RectangleF t = new RectangleF(-0.05f * D, -0.11f * D, 0.1f * D, 0.13f * D);
                    using (GraphicsPath tp = new GraphicsPath())
                    {
                        tp.AddEllipse(t);
                        Fill(g, tp, t, Lighter(c1, 0.08), c2, 80f);
                    }
                    g.Restore(st);
                    g.Restore(ts);
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.17f * D, -0.25f * D, (float)(s * (30 + (s < 0 ? p.EarL : p.EarR) * 0.6)), 0.13f, 0.95f, Lighter(c1, 0.05), c2, Color.FromArgb(120, Lighter(c1, 0.4)));
                    foreach (int s in Sides) Foot(g, D, s, Lighter(c1, 0.05), c2, p);
                    break;
                }
            }
        }

        // Squirtle's tail: a thick stroke out to the side that rolls into a curl at the end.
        static void CurlyTail(Graphics g, float D, Color c1, Color c2, double now)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(0.26f * D, 0.22f * D);
            g.RotateTransform((float)(Math.Sin(now / 700.0) * 5));
            Color c = Mac.Mix(c1, c2, 0.3);
            using (Pen pen = new Pen(c, D * 0.075f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, P(0, 0), P(0.08f * D, 0.04f * D), P(0.18f * D, 0.02f * D), P(0.2f * D, -0.07f * D));
                g.DrawArc(pen, 0.07f * D, -0.16f * D, 0.14f * D, 0.13f * D, 0, -260);
            }
            using (Pen hl = new Pen(Color.FromArgb(90, 255, 255, 255), D * 0.02f))
            {
                hl.StartCap = LineCap.Round;
                hl.EndCap = LineCap.Round;
                g.DrawArc(hl, 0.08f * D, -0.17f * D, 0.12f * D, 0.11f * D, 200, 90);
            }
            g.Restore(st);
        }

        // Snorlax's wide cream feet with their brown pads.
        static void BigFoot(Graphics g, float D, int s, MascotPose p)
        {
            float step = Math.Abs(p.Facing) > 0.2 ? (float)Math.Max(0, Math.Sin(p.Now / 120.0 + (s < 0 ? 0 : Math.PI))) * 0.025f * D : 0;
            RectangleF f = new RectangleF(s * 0.2f * D - 0.11f * D, 0.27f * D - step, 0.22f * D, 0.12f * D);
            using (GraphicsPath fp = new GraphicsPath())
            {
                fp.AddEllipse(f);
                Fill(g, fp, f, PokeCream, PokeCream2, 90f);
            }
            Ellipse(g, Color.FromArgb(200, 170, 120, 80), f.X + f.Width * 0.3f, f.Y + f.Height * 0.3f, f.Width * 0.4f, f.Height * 0.45f);
        }

        // ---- On the body (clipped to it, before the face).
        static void PokemonBody(Graphics g, MascotLook l, float D, GraphicsPath body, Color c1, Color c2)
        {
            Region old = g.Clip;
            g.SetClip(body, CombineMode.Intersect);
            switch (l.Kind)
            {
                case KindBulbasaur:
                {
                    // Darker patches scattered over the head and body.
                    Color spot = Color.FromArgb(150, Darker(c2, 0.15));
                    Ellipse(g, spot, -0.3f * D, -0.12f * D, 0.11f * D, 0.08f * D);
                    Ellipse(g, spot, 0.2f * D, -0.17f * D, 0.09f * D, 0.06f * D);
                    Ellipse(g, spot, 0.24f * D, 0.05f * D, 0.1f * D, 0.08f * D);
                    Ellipse(g, spot, -0.26f * D, 0.1f * D, 0.08f * D, 0.07f * D);
                    Ellipse(g, spot, -0.04f * D, -0.25f * D, 0.08f * D, 0.05f * D);
                    break;
                }
                case KindSquirtle:
                {
                    // The yellow plastron with its seams.
                    RectangleF pl = new RectangleF(-0.19f * D, 0.04f * D, 0.38f * D, 0.36f * D);
                    using (GraphicsPath pp = new GraphicsPath())
                    {
                        pp.AddEllipse(pl);
                        Fill(g, pp, pl, Color.FromArgb(255, 234, 150), Color.FromArgb(232, 190, 92), 90f);
                        Region inner = g.Clip;
                        g.SetClip(pp, CombineMode.Intersect);
                        Color seam = Color.FromArgb(120, 170, 120, 40);
                        for (int i = 0; i < 3; i++) Line(g, seam, D * 0.009f, pl.X, (0.13f + i * 0.08f) * D, pl.Right, (0.13f + i * 0.08f) * D);
                        Line(g, seam, D * 0.009f, 0, 0.13f * D, 0, pl.Bottom);
                        g.Clip = inner;
                        inner.Dispose();
                        Stroke(g, pp, Color.FromArgb(90, 170, 120, 40), D * 0.008f);
                    }
                    break;
                }
                case KindSnorlax:
                {
                    // The cream face under a dark forehead that dips in the middle, and the big cream belly.
                    using (GraphicsPath fp = new GraphicsPath())
                    {
                        fp.AddBezier(P(-0.3f * D, -0.04f * D), P(-0.28f * D, -0.17f * D), P(-0.14f * D, -0.2f * D), P(0, -0.15f * D));
                        fp.AddBezier(P(0, -0.15f * D), P(0.14f * D, -0.2f * D), P(0.28f * D, -0.17f * D), P(0.3f * D, -0.04f * D));
                        fp.AddBezier(P(0.3f * D, -0.04f * D), P(0.3f * D, 0.08f * D), P(-0.3f * D, 0.08f * D), P(-0.3f * D, -0.04f * D));
                        fp.CloseFigure();
                        Fill(g, fp, new RectangleF(-0.3f * D, -0.2f * D, 0.6f * D, 0.26f * D), PokeCream, PokeCream2, 90f);
                    }
                    RectangleF belly = new RectangleF(-0.33f * D, 0.04f * D, 0.66f * D, 0.44f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, PokeCream, PokeCream2, 90f);
                    }
                    break;
                }
            }
            g.Clip = old;
            old.Dispose();
        }

        // ---- In front of the face.
        static void PokemonDetails(Graphics g, MascotLook l, Geo geo, float D)
        {
            float fy = geo.FaceY * D;
            switch (l.Kind)
            {
                case KindJigglypuff:
                {
                    // The curl on its forehead.
                    Color c = Color.FromArgb(246, 150, 186);
                    using (Pen pen = new Pen(c, D * 0.06f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawBezier(pen, P(0.05f * D, -0.27f * D), P(0.06f * D, -0.38f * D), P(-0.1f * D, -0.4f * D), P(-0.11f * D, -0.3f * D));
                        g.DrawArc(pen, -0.13f * D, -0.31f * D, 0.07f * D, 0.08f * D, 180, 230);
                    }
                    using (Pen hl = new Pen(Color.FromArgb(110, 255, 255, 255), D * 0.016f))
                    {
                        hl.StartCap = LineCap.Round;
                        hl.EndCap = LineCap.Round;
                        g.DrawBezier(hl, P(0.03f * D, -0.31f * D), P(0.02f * D, -0.37f * D), P(-0.06f * D, -0.38f * D), P(-0.09f * D, -0.34f * D));
                    }
                    break;
                }
                case KindBulbasaur:
                    Ellipse(g, Color.FromArgb(110, Ink), -0.035f * D, fy + 0.055f * D, 0.014f * D, 0.011f * D);
                    Ellipse(g, Color.FromArgb(110, Ink), 0.021f * D, fy + 0.055f * D, 0.014f * D, 0.011f * D);
                    break;
            }
        }
    }

    public partial class Mascot
    {
        // Snorlax's eyes are two flat lines, half asleep, unless something surprises it.
        bool PokemonEye(Graphics g, float x, float y, float D, bool surprised)
        {
            if (Look.Kind == MascotParts.KindGengar) { GengarEye(g, x, y, D, surprised); return true; }
            if (Look.Kind != MascotParts.KindSnorlax || surprised) return false;
            MascotParts.Line(g, Color.FromArgb(230, 30, 34, 40), Math.Max(1.2f, D * 0.018f), x - D * 0.05f, y, x + D * 0.05f, y);
            return true;
        }

        // Gengar's eyes: all red, slanted down towards the middle in a sly V, with a tiny dark pupil and a glint.
        void GengarEye(Graphics g, float x, float y, float D, bool surprised)
        {
            float s = x < 0 ? -1 : 1, w = 0.14f * D, h = (surprised ? 0.12f : 0.1f) * D;
            using (GraphicsPath e = new GraphicsPath())
            {
                PointF inner = new PointF(x - s * w * 0.5f, y - h * 0.05f), outer = new PointF(x + s * w * 0.5f, y - h * 0.6f);
                e.AddLine(inner, outer);
                e.AddBezier(outer, new PointF(x + s * w * 0.62f, y + h * 0.3f), new PointF(x + s * w * 0.1f, y + h * 0.62f), new PointF(x - s * w * 0.22f, y + h * 0.45f));
                e.AddBezier(new PointF(x - s * w * 0.22f, y + h * 0.45f), new PointF(x - s * w * 0.42f, y + h * 0.35f), new PointF(x - s * w * 0.52f, y + h * 0.15f), inner);
                e.CloseFigure();
                MascotParts.Fill(g, e, new RectangleF(x - w / 2, y - h * 0.6f, w, h * 1.2f), Color.FromArgb(255, 70, 80), Color.FromArgb(200, 20, 40), 90f);
                MascotParts.Stroke(g, e, Color.FromArgb(220, 60, 20, 50), Math.Max(1f, D * 0.009f));
            }
            float r = 0.017f * D, px = x - s * w * 0.04f, py = y + h * 0.05f;
            MascotParts.Ellipse(g, Color.FromArgb(240, 60, 8, 20), px - r, py - r, r * 2, r * 2);
            MascotParts.Ellipse(g, Color.FromArgb(230, 255, 255, 255), x + s * w * 0.12f, y - h * 0.32f, w * 0.12f, h * 0.16f);
        }

        // Gengar's wide white grin, with lines between its teeth; Snorlax's small mouth with two little fangs.
        bool PokemonMouth(Graphics g, float D, float mx, float fy, Color ink, float lw, bool talking)
        {
            if (Look.Kind == MascotParts.KindGengar)
            {
                float my = fy + D * 0.1f, w = D * 0.3f, h = D * (talking ? 0.13f + 0.03f * (float)Math.Abs(Math.Sin(now / 110.0)) : 0.12f);
                using (GraphicsPath m = new GraphicsPath())
                {
                    m.AddBezier(new PointF(mx - w / 2, my - h * 0.3f), new PointF(mx - w * 0.2f, my - h * 0.05f), new PointF(mx + w * 0.2f, my - h * 0.05f), new PointF(mx + w / 2, my - h * 0.3f));
                    m.AddBezier(new PointF(mx + w / 2, my - h * 0.3f), new PointF(mx + w * 0.3f, my + h * 0.75f), new PointF(mx - w * 0.3f, my + h * 0.75f), new PointF(mx - w / 2, my - h * 0.3f));
                    m.CloseFigure();
                    MascotParts.FillSolid(g, m, Color.FromArgb(252, 250, 255));
                    Region old = g.Clip;
                    g.SetClip(m, CombineMode.Intersect);
                    for (int i = -3; i <= 3; i++) MascotParts.Line(g, Color.FromArgb(130, 60, 40, 80), Math.Max(1f, D * 0.006f), mx + i * w * 0.12f, my - h, mx + i * w * 0.12f, my + h);
                    MascotParts.Line(g, Color.FromArgb(110, 60, 40, 80), Math.Max(1f, D * 0.006f), mx - w / 2, my + h * 0.12f, mx + w / 2, my + h * 0.12f);
                    g.Clip = old;
                    old.Dispose();
                    MascotParts.Stroke(g, m, Color.FromArgb(220, 40, 20, 60), Math.Max(1f, D * 0.01f));
                }
                return true;
            }
            if (Look.Kind == MascotParts.KindSnorlax && !talking && mood != Mood.Happy && mood != Mood.Love)
            {
                float my = fy + D * 0.06f, w = D * 0.12f;
                using (Pen p = new Pen(ink, lw))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLine(p, mx - w / 2, my, mx + w / 2, my);
                }
                foreach (int s in new int[] { -1, 1 })
                    using (GraphicsPath f = new GraphicsPath())
                    {
                        float fx = mx + s * w * 0.32f;
                        f.AddPolygon(new PointF[] { new PointF(fx - D * 0.014f, my), new PointF(fx + D * 0.014f, my), new PointF(fx, my - D * 0.026f) });
                        MascotParts.FillSolid(g, f, Color.White);
                    }
                return true;
            }
            return false;
        }
    }
}

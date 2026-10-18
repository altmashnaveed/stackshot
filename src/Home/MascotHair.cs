// Stackshot - Each series character's own hairstyle on the mascot's round head, drawn the way anime hair is: a cap over
// the crown and separate locks that taper to points, back ones first, with an ink contour, a shadow layer, the light
// band across the top and a few strands. Origin at the top of the head, u = hat unit (the head is about ±0.6u wide,
// the eyes sit near y = 0.41u, ±0.22u apart).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        // Hairstyles added for characters that used to share a generic one (appended: indices are stored in settings).
        public const int HatGojo = 46, HatKillua = 47, HatLevi = 48, HatShinchan = 49;

        // Levi's curtain of hair on the left (mirrored for the right), in u: along the top to the part, down beside it, then
        // its points along the bottom and back up his temple.
        static readonly float[] LeviCurtain =
        {
            -0.6f, -0.02f, -0.36f, -0.17f, -0.02f, -0.2f, -0.03f, -0.06f, -0.12f, 0.06f, -0.26f, 0.22f, -0.36f, 0.12f, -0.48f, 0.27f, -0.55f, 0.12f, -0.66f, 0.22f
        };

        // The characters' own hair (not hats put on): it turns with the face as the eyes follow the pointer.
        public static bool HairTurns(int hat)
        {
            return hat == 18 || hat == 19 || hat == 41 || hat == HatKaminaHair || hat == HatGojo || hat == HatLevi || hat == HatShinchan;
        }

        // One lock from a root to a pointed tip, w wide at the root, bowed sideways by bend (all in u).
        static GraphicsPath Lock(float u, float x0, float y0, float x1, float y1, float w, float bend)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) len = 1e-4f;
            float nx = -dy / len, ny = dx / len, mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            PointF a = P((x0 + nx * w / 2) * u, (y0 + ny * w / 2) * u), b = P((x0 - nx * w / 2) * u, (y0 - ny * w / 2) * u), t = P(x1 * u, y1 * u);
            PointF qa = P((mx + nx * (bend + w * 0.42f)) * u, (my + ny * (bend + w * 0.42f)) * u);
            PointF qb = P((mx + nx * (bend - w * 0.3f)) * u, (my + ny * (bend - w * 0.3f)) * u);
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(a, Q1(a, qa), Q2(t, qa), t);
            p.AddBezier(t, Q2(t, qb), Q1(b, qb), b);
            p.CloseFigure();
            return p;
        }

        // A lock on a figure (in units of D), with the figures' own ink and shine.
        static void FigLock(Graphics g, float D, float x0, float y0, float x1, float y1, float w, float bend, Color a, Color b)
        {
            using (GraphicsPath p = Lock(D, x0, y0, x1, y1, w, bend)) HairShape(g, p, a, b, D);
        }

        static PointF Q1(PointF p0, PointF q) { return new PointF(p0.X + (q.X - p0.X) * 2 / 3, p0.Y + (q.Y - p0.Y) * 2 / 3); }
        static PointF Q2(PointF p2, PointF q) { return new PointF(p2.X + (q.X - p2.X) * 2 / 3, p2.Y + (q.Y - p2.Y) * 2 / 3); }

        // A smooth closed shape through points given in u.
        static GraphicsPath Blob(float u, float tension, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2] * u, xy[i * 2 + 1] * u);
            GraphicsPath p = new GraphicsPath();
            p.AddClosedCurve(pts, tension);
            return p;
        }

        // A sharp polygon through points given in u (spiky hair).
        static GraphicsPath Spikes(float u, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2] * u, xy[i * 2 + 1] * u);
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        // Paints hair pieces in order (back to front) as one hairdo: shared colour ramp, contour on each piece, then the
        // shadow layer, the light band and strands over all of it.
        static void HairDo(Graphics g, float u, Color light, Color dark, List<GraphicsPath> pieces, float ringY, float ringW, string strands)
        {
            using (GraphicsPath all = new GraphicsPath(FillMode.Winding))
            {
                foreach (GraphicsPath p in pieces) all.AddPath(p, false);
                RectangleF r = all.GetBounds();
                Color ink = Color.FromArgb(235, Darker(dark, 0.55));
                float lw = Math.Max(1.1f, u * 0.026f);
                foreach (GraphicsPath p in pieces)
                {
                    Fill(g, p, r, light, dark, 80f);
                    Stroke(g, p, ink, lw);
                }
                Region old = g.Clip;
                using (Region shadow = new Region(all))
                using (GraphicsPath lit = (GraphicsPath)all.Clone())
                using (Matrix m = new Matrix())
                {
                    m.Translate(-0.07f * u, -0.08f * u);
                    lit.Transform(m);
                    shadow.Exclude(lit);
                    shadow.Intersect(old);
                    g.Clip = shadow;
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(70, Darker(dark, 0.45)))) g.FillPath(sb, all);
                }
                using (Region inside = new Region(all))
                {
                    inside.Intersect(old);
                    g.Clip = inside;
                    if (ringW > 0)
                    {
                        // The light band: a curved stripe across the crown, broken into dashes like anime hair shine.
                        using (Pen ring = new Pen(Color.FromArgb(150, Lighter(light, 0.55)), ringW * u))
                        {
                            ring.DashPattern = new float[] { 2.2f, 0.7f };
                            ring.StartCap = LineCap.Round; ring.EndCap = LineCap.Round;
                            g.DrawArc(ring, -0.42f * u, (ringY - 0.06f) * u, 0.84f * u, 0.3f * u, 200, 140);
                        }
                    }
                    if (strands != null)
                        using (Pen sp = new Pen(Color.FromArgb(110, Darker(dark, 0.3)), Math.Max(0.8f, u * 0.014f)))
                        {
                            sp.StartCap = LineCap.Round; sp.EndCap = LineCap.Round;
                            string[] segs = strands.Split(';');
                            foreach (string s in segs)
                            {
                                string[] n = s.Split(',');
                                if (n.Length < 6) continue;
                                float[] v = new float[6];
                                for (int i = 0; i < 6; i++) v[i] = float.Parse(n[i], System.Globalization.CultureInfo.InvariantCulture) * u;
                                g.DrawBezier(sp, v[0], v[1], (v[0] + v[2]) / 2 + (v[3] - v[1]) * 0.1f, (v[1] + v[3]) / 2, v[2], v[3], v[4], v[5]);
                            }
                        }
                }
                g.Clip = old;
                old.Dispose();
            }
            foreach (GraphicsPath p in pieces) p.Dispose();
        }

        // The crown under every hairdo: a dome over the top of the head down to the hairline.
        static GraphicsPath Crown(float u, float w, float top, float line)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(P(-w * u, line * u), P(-w * 1.02f * u, (top + 0.12f) * u), P(-w * 0.55f * u, top * u), P(0, top * u));
            p.AddBezier(P(0, top * u), P(w * 0.55f * u, top * u), P(w * 1.02f * u, (top + 0.12f) * u), P(w * u, line * u));
            p.AddBezier(P(w * u, line * u), P(w * 0.6f * u, (line - 0.1f) * u), P(-w * 0.6f * u, (line - 0.1f) * u), P(-w * u, line * u));
            p.CloseFigure();
            return p;
        }

        static bool PaintSeriesHair(Graphics g, int hat, float u, double now)
        {
            List<GraphicsPath> h = new List<GraphicsPath>();
            switch (hat)
            {
                case 18:
                {
                    // Edward: golden, parted in the middle, long bangs framing the face, the antenna on top.
                    float sway = (float)Math.Sin(now / 420.0) * 0.025f;
                    h.Add(Lock(u, 0.02f, -0.02f, 0.16f + sway, -0.36f, 0.07f, -0.06f));
                    h.Add(Lock(u, -0.42f, 0.05f, -0.62f, 0.72f, 0.24f, 0.05f));
                    h.Add(Lock(u, 0.42f, 0.05f, 0.62f, 0.72f, 0.24f, -0.05f));
                    h.Add(Crown(u, 0.63f, -0.08f, 0.26f));
                    h.Add(Lock(u, -0.3f, -0.02f, -0.5f, 0.5f, 0.26f, 0.04f));
                    h.Add(Lock(u, 0.3f, -0.02f, 0.5f, 0.5f, 0.26f, -0.04f));
                    h.Add(Lock(u, -0.12f, -0.03f, -0.3f, 0.33f, 0.24f, 0.05f));
                    h.Add(Lock(u, 0.12f, -0.03f, 0.3f, 0.33f, 0.24f, -0.05f));
                    h.Add(Lock(u, -0.03f, -0.02f, -0.07f, 0.24f, 0.1f, 0.0f));
                    HairDo(g, u, Color.FromArgb(255, 226, 120), Color.FromArgb(214, 150, 36), h, 0.02f, 0.05f, "-0.2,0.02,-0.36,0.3,-0.46,0.5;0.2,0.02,0.36,0.3,0.46,0.5");
                    return true;
                }
                case 19:
                {
                    // Eren: dark brown, medium length, a full crown over the whole top of his head, bangs split over the
                    // forehead and locks over the ears.
                    h.Add(Lock(u, -0.5f, 0.1f, -0.68f, 0.66f, 0.26f, 0.03f));
                    h.Add(Lock(u, 0.5f, 0.1f, 0.68f, 0.66f, 0.26f, -0.03f));
                    h.Add(Crown(u, 0.68f, -0.22f, 0.3f));
                    h.Add(Lock(u, -0.38f, -0.08f, -0.58f, 0.42f, 0.28f, 0.05f));
                    h.Add(Lock(u, 0.38f, -0.08f, 0.58f, 0.42f, 0.28f, -0.05f));
                    h.Add(Lock(u, -0.22f, -0.12f, -0.36f, 0.32f, 0.26f, 0.05f));
                    h.Add(Lock(u, 0.2f, -0.12f, 0.32f, 0.32f, 0.26f, -0.06f));
                    h.Add(Lock(u, -0.05f, -0.12f, -0.11f, 0.36f, 0.2f, 0.03f));
                    h.Add(Lock(u, 0.07f, -0.12f, 0.11f, 0.3f, 0.16f, -0.02f));
                    HairDo(g, u, Color.FromArgb(140, 98, 66), Color.FromArgb(62, 38, 26), h, 0.02f, 0.035f,
                           "-0.3,-0.06,-0.42,0.2,-0.5,0.36;0.28,-0.06,0.4,0.2,0.48,0.36;-0.1,-0.18,-0.2,0.0,-0.26,0.2;0.1,-0.18,0.18,0.0,0.22,0.2");
                    return true;
                }
                case HatGojo:
                {
                    // Gojo: white, pushed up by the blindfold into spikes pointing up and out, a few strands falling over it.
                    h.Add(Spikes(u, -0.66f, 0.44f, -0.8f, 0.2f, -0.64f, 0.12f, -0.82f, -0.12f, -0.56f, -0.12f, -0.62f, -0.4f, -0.36f, -0.24f, -0.32f, -0.46f,
                                    -0.14f, -0.3f, 0.0f, -0.5f, 0.12f, -0.3f, 0.32f, -0.46f, 0.36f, -0.24f, 0.62f, -0.4f, 0.56f, -0.12f, 0.82f, -0.12f,
                                    0.64f, 0.12f, 0.8f, 0.2f, 0.66f, 0.44f, 0.5f, 0.28f, -0.5f, 0.28f));
                    h.Add(Lock(u, -0.12f, 0.06f, -0.2f, 0.4f, 0.14f, 0.03f));
                    h.Add(Lock(u, 0.1f, 0.06f, 0.16f, 0.38f, 0.12f, -0.03f));
                    h.Add(Lock(u, 0.0f, 0.04f, 0.02f, 0.44f, 0.1f, 0.0f));
                    HairDo(g, u, Color.FromArgb(255, 255, 255), Color.FromArgb(190, 198, 222), h, -0.2f, 0.05f, "-0.3,-0.1,-0.34,-0.4,-0.32,-0.5;0.3,-0.1,0.34,-0.4,0.32,-0.5;0,-0.1,0,-0.4,0,-0.55");
                    return true;
                }
                case HatKillua:
                {
                    // Killua: white and fluffy, messy points all round and soft bangs over the forehead.
                    h.Add(Spikes(u, -0.62f, 0.5f, -0.78f, 0.36f, -0.7f, 0.22f, -0.86f, 0.04f, -0.66f, -0.02f, -0.74f, -0.24f, -0.48f, -0.2f, -0.46f, -0.42f,
                                    -0.24f, -0.26f, -0.14f, -0.48f, 0.0f, -0.28f, 0.16f, -0.5f, 0.24f, -0.26f, 0.46f, -0.44f, 0.48f, -0.2f, 0.74f, -0.26f,
                                    0.66f, -0.02f, 0.86f, 0.06f, 0.7f, 0.22f, 0.8f, 0.38f, 0.62f, 0.5f, 0.5f, 0.3f, -0.5f, 0.3f));
                    h.Add(Lock(u, -0.42f, 0.02f, -0.54f, 0.38f, 0.22f, 0.06f));
                    h.Add(Lock(u, 0.42f, 0.02f, 0.54f, 0.38f, 0.22f, -0.06f));
                    h.Add(Lock(u, -0.22f, 0.0f, -0.32f, 0.33f, 0.24f, 0.05f));
                    h.Add(Lock(u, 0.22f, 0.0f, 0.3f, 0.33f, 0.24f, -0.05f));
                    h.Add(Lock(u, 0.0f, 0.0f, 0.02f, 0.3f, 0.22f, 0.0f));
                    HairDo(g, u, Color.FromArgb(252, 252, 255), Color.FromArgb(196, 202, 224), h, -0.12f, 0.05f, "-0.4,-0.1,-0.56,0.1,-0.6,0.26;0.4,-0.1,0.56,0.1,0.6,0.26");
                    return true;
                }
                case HatLevi:
                {
                    // Levi: an undercut, the sides shaved short and dark; the longer hair on top parted in the middle and
                    // falling either side in a sleek curtain over his temples, ending in a few points.
                    h.Add(Blob(u, 0.5f, -0.64f, 0.02f, -0.52f, -0.04f, -0.5f, 0.2f, -0.58f, 0.38f, -0.66f, 0.24f));
                    h.Add(Blob(u, 0.5f, 0.64f, 0.02f, 0.52f, -0.04f, 0.5f, 0.2f, 0.58f, 0.38f, 0.66f, 0.24f));
                    h.Add(Crown(u, 0.64f, -0.2f, 0.12f));
                    h.Add(TnSpikes(u, 0.05f, LeviCurtain));
                    h.Add(TnSpikes(u, 0.05f, Mirror(LeviCurtain)));
                    HairDo(g, u, Color.FromArgb(92, 92, 110), Color.FromArgb(22, 22, 30), h, 0.0f, 0.035f,
                           "-0.1,-0.16,-0.3,0.0,-0.44,0.18;0.1,-0.16,0.3,0.0,0.44,0.18;-0.2,-0.14,-0.42,0.0,-0.56,0.16;0.2,-0.14,0.42,0.0,0.56,0.16");
                    return true;
                }
                case HatShinchan:
                {
                    // Shin-chan: a round black helmet of hair with little tufts at the sides and a straight fringe.
                    h.Add(Spikes(u, -0.66f, 0.44f, -0.72f, 0.3f, -0.66f, 0.24f, -0.74f, 0.12f, -0.64f, 0.06f, -0.66f, -0.06f, -0.5f, -0.18f, -0.26f, -0.26f,
                                    0.0f, -0.28f, 0.26f, -0.26f, 0.5f, -0.18f, 0.66f, -0.06f, 0.64f, 0.06f, 0.74f, 0.12f, 0.66f, 0.24f, 0.72f, 0.3f,
                                    0.66f, 0.44f, 0.56f, 0.16f, 0.38f, 0.12f, 0.2f, 0.16f, 0.0f, 0.12f, -0.2f, 0.16f, -0.38f, 0.12f, -0.56f, 0.16f));
                    HairDo(g, u, Color.FromArgb(70, 70, 84), Color.FromArgb(14, 14, 20), h, -0.1f, 0.04f, null);
                    return true;
                }
                case 32:
                {
                    // Gon: black hair standing straight up in tall spikes, green at the tips.
                    h.Add(Spikes(u, -0.64f, 0.34f, -0.74f, 0.0f, -0.6f, -0.08f, -0.7f, -0.44f, -0.42f, -0.2f, -0.4f, -0.56f, -0.2f, -0.3f, -0.08f, -0.66f,
                                    0.06f, -0.32f, 0.24f, -0.62f, 0.28f, -0.26f, 0.5f, -0.5f, 0.48f, -0.14f, 0.72f, -0.3f, 0.62f, 0.0f, 0.74f, 0.08f,
                                    0.64f, 0.34f, 0.48f, 0.22f, 0.3f, 0.3f, 0.14f, 0.2f, 0.0f, 0.3f, -0.16f, 0.2f, -0.32f, 0.3f, -0.48f, 0.2f));
                    HairDo(g, u, Color.FromArgb(70, 92, 76), Color.FromArgb(14, 22, 16), h, -0.22f, 0.04f, "-0.3,0.1,-0.36,-0.2,-0.38,-0.5;0.0,0.1,-0.04,-0.3,-0.06,-0.66;0.24,0.1,0.26,-0.3,0.24,-0.6");
                    // The green tips.
                    using (SolidBrush tip = new SolidBrush(Color.FromArgb(150, 70, 190, 90)))
                        foreach (float[] t in new float[][] { new float[] { -0.4f, -0.56f }, new float[] { -0.08f, -0.66f }, new float[] { 0.24f, -0.62f }, new float[] { 0.5f, -0.5f }, new float[] { -0.7f, -0.44f }, new float[] { 0.72f, -0.3f } })
                            g.FillEllipse(tip, (t[0] - 0.035f) * u, (t[1] - 0.01f) * u, 0.07f * u, 0.12f * u);
                    return true;
                }
                case 33:
                {
                    // Hisoka: magenta, slicked back and up in flame-like points, the forehead bare.
                    h.Add(Spikes(u, -0.62f, 0.24f, -0.76f, 0.02f, -0.6f, -0.02f, -0.7f, -0.3f, -0.44f, -0.16f, -0.46f, -0.5f, -0.22f, -0.24f, -0.12f, -0.62f,
                                    0.02f, -0.26f, 0.2f, -0.6f, 0.24f, -0.22f, 0.5f, -0.48f, 0.46f, -0.14f, 0.74f, -0.26f, 0.6f, 0.02f, 0.76f, 0.04f,
                                    0.62f, 0.24f, 0.4f, 0.06f, 0.0f, 0.02f, -0.4f, 0.06f));
                    HairDo(g, u, Color.FromArgb(250, 120, 160), Color.FromArgb(164, 34, 84), h, -0.16f, 0.04f, "-0.3,0.02,-0.36,-0.2,-0.4,-0.42;0.0,0.0,0.02,-0.3,0.06,-0.52;0.3,0.02,0.36,-0.2,0.42,-0.38");
                    return true;
                }
                case 41:
                {
                    // Superman: short black hair parted at the side, swept up and over in a wave, short at the sides with
                    // sideburns, and the curl falling on his forehead.
                    h.Add(Blob(u, 0.45f, -0.63f, 0.3f, -0.67f, 0.06f, -0.6f, -0.12f, -0.42f, -0.24f, -0.16f, -0.3f, 0.12f, -0.3f, 0.4f, -0.23f, 0.6f, -0.1f,
                               0.67f, 0.08f, 0.63f, 0.3f, 0.55f, 0.22f, 0.5f, 0.1f, 0.32f, 0.06f, 0.14f, 0.09f, 0, 0.05f, -0.14f, -0.02f, -0.3f, 0.02f,
                               -0.46f, 0.08f, -0.55f, 0.22f));
                    h.Add(Blob(u, 0.5f, -0.2f, -0.04f, -0.05f, -0.2f, 0.2f, -0.24f, 0.42f, -0.14f, 0.3f, -0.08f, 0.1f, -0.06f, -0.06f, 0.02f));
                    h.Add(Lock(u, 0.1f, 0.04f, -0.01f, 0.27f, 0.09f, 0.07f));
                    HairDo(g, u, Color.FromArgb(70, 76, 112), Color.FromArgb(10, 12, 24), h, -0.14f, 0.035f,
                           "-0.5,0.04,-0.4,-0.14,-0.18,-0.24;-0.3,0.0,-0.1,-0.12,0.18,-0.18;0.2,0.04,0.4,-0.04,0.55,0.04");
                    // The curl's little hook at its end.
                    using (Pen c = new Pen(Color.FromArgb(240, 14, 16, 30), Math.Max(1.1f, u * 0.034f)))
                    {
                        c.StartCap = LineCap.Round; c.EndCap = LineCap.Round;
                        g.DrawBezier(c, P(-0.01f * u, 0.26f * u), P(-0.02f * u, 0.32f * u), P(0.05f * u, 0.33f * u), P(0.06f * u, 0.28f * u));
                    }
                    return true;
                }
            }
            return false;
        }
    }
}

// Stackshot - Characters from Klei's games, drawn in their own hand-inked style instead of the mascot's round one: thick
// dark ink lines with a second, finer stroke beside them, flat muted colours, a big head on a small body with thin arms
// and legs. Don't Starve Together's Wilson (the swept-up black hair with its two big points, worried brows, a white
// shirt under a red vest) and Willow (a dark bob with blunt bangs and two pigtails, blank staring eyes with dark rings,
// a red top and black skirt); and two Oxygen Not Included duplicants (a wide bean head, dot eyes, a grey suit with a
// yellow collar): Bumpo with short black hair and Ollmeister with a cloud of curls. Fan tributes, not affiliated with
// Klei Entertainment (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindWilson = 76, KindWillow = 77, KindDupe = 78, KindDupeCurly = 79;

        public static bool IsKlei(int kind) { return kind >= KindWilson && kind <= KindDupeCurly; }
        static bool IsDupe(int kind) { return kind == KindDupe || kind == KindDupeCurly; }

        static readonly Color KInk = Color.FromArgb(34, 26, 24);

        // The ink: a thick dark line, and a finer one just beside it so it reads as drawn by hand.
        static void KleiInk(Graphics g, GraphicsPath p, float D)
        {
            Stroke(g, p, Color.FromArgb(240, KInk), Math.Max(1.4f, D * 0.018f));
            GraphicsState st = g.Save();
            g.TranslateTransform(D * 0.004f, D * 0.003f);
            Stroke(g, p, Color.FromArgb(110, KInk), Math.Max(0.8f, D * 0.007f));
            g.Restore(st);
        }

        static void KleiShape(Graphics g, GraphicsPath p, Color a, Color b, float D)
        {
            RectangleF r = p.GetBounds();
            Fill(g, p, r, a, b, 90f);
            KleiInk(g, p, D);
        }

        // Hand-drawn hatching on the shadow side (the right, away from the light), clipped to the shape.
        static void KHatch(Graphics g, GraphicsPath p, float D, int alpha)
        {
            RectangleF r = p.GetBounds();
            if (r.Width < 2 || r.Height < 2) return;
            Region old = g.Clip;
            g.SetClip(p, CombineMode.Intersect);
            using (Pen h = new Pen(Color.FromArgb(alpha, KInk), Math.Max(0.8f, D * 0.006f)))
            {
                h.StartCap = h.EndCap = LineCap.Round;
                float step = 0.028f * D;
                for (float x = r.X + r.Width * 0.55f; x < r.Right + r.Height; x += step)
                    g.DrawLine(h, x, r.Bottom, x - r.Height * 0.6f, r.Y);
            }
            g.Clip = old;
            old.Dispose();
        }

        static GraphicsPath KCurve(float D, float tension, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2] * D, xy[i * 2 + 1] * D);
            GraphicsPath p = new GraphicsPath();
            p.AddClosedCurve(pts, tension);
            return p;
        }

        static GraphicsPath KPoly(float D, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2] * D, xy[i * 2 + 1] * D);
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(pts);
            return p;
        }

        // A limb: a dark ink line with the colour inside it.
        static void KLimb(Graphics g, float D, Color c, float w, float x1, float y1, float x2, float y2)
        {
            using (Pen ink = new Pen(KInk, (w + 0.026f) * D))
            using (Pen fill = new Pen(c, w * D))
            {
                ink.StartCap = ink.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawLine(ink, x1 * D, y1 * D, x2 * D, y2 * D);
                g.DrawLine(fill, x1 * D, y1 * D, x2 * D, y2 * D);
            }
        }

        static void KShoe(Graphics g, float D, float x, float y, Color c)
        {
            RectangleF r = new RectangleF((x - 0.05f) * D, (y - 0.025f) * D, 0.1f * D, 0.05f * D);
            using (GraphicsPath sp = new GraphicsPath())
            {
                sp.AddEllipse(r);
                KleiShape(g, sp, Lighter(c, 0.1), c, D);
            }
        }

        static Geo KleiGeo(int kind)
        {
            return IsDupe(kind) ? G(0.54f, -0.32f, -0.06f, 0.09f, 0.13f, 0.42f, 0.14f, 0.23f, 0.2f, 0.52f)
                                : G(0.5f, -0.33f, -0.11f, 0.085f, 0.12f, 0.4f, 0.13f, 0.22f, 0.2f, 0.5f);
        }

        // The head only; the body, arms and legs are drawn behind it.
        static GraphicsPath KleiPath(int kind, float D)
        {
            if (IsDupe(kind))
                return KCurve(D, 0.5f, 0, -0.32f, 0.2f, -0.29f, 0.27f, -0.12f, 0.27f, 0.04f, 0.18f, 0.12f, 0, 0.14f, -0.18f, 0.12f, -0.27f, 0.04f, -0.27f, -0.12f, -0.2f, -0.29f);
            return KCurve(D, 0.5f, 0, -0.33f, 0.2f, -0.3f, 0.25f, -0.14f, 0.22f, 0.02f, 0.12f, 0.11f, 0, 0.13f, -0.12f, 0.11f, -0.22f, 0.02f, -0.25f, -0.14f, -0.2f, -0.3f);
        }

        // ---- Behind the head: pigtails, then the body with its clothes, arms, legs and shoes.
        static void KleiBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            Color skin = c1;
            if (l.Kind == KindWillow)
            {
                // Two long braided pigtails hanging down her sides to the shoulders, each curling out into a spiral at
                // the end, tied with red at the top; they sway a little.
                Color hair = Color.FromArgb(40, 30, 30);
                foreach (int s in Sides)
                {
                    float sw = (float)Math.Sin(p.Now / 700.0 + s) * 0.01f;
                    PointF a = P(s * 0.24f * D, -0.13f * D), b = P(s * 0.33f * D, -0.04f * D), c = P(s * 0.3f * D, 0.08f * D), e = P((s * 0.34f + sw) * D, 0.15f * D);
                    using (Pen ink = new Pen(KInk, 0.075f * D))
                    using (Pen fill = new Pen(hair, 0.05f * D))
                    using (Pen braid = new Pen(Color.FromArgb(110, 120, 100, 96), Math.Max(1f, D * 0.007f)))
                    {
                        ink.StartCap = ink.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                        g.DrawBezier(ink, a, b, c, e);
                        g.DrawBezier(fill, a, b, c, e);
                        // The braid's crossings along it.
                        for (int i = 1; i < 5; i++)
                        {
                            float t = i / 5f, u = 1 - t;
                            float x = u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * e.X;
                            float y = u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * e.Y;
                            g.DrawLine(braid, x - 0.018f * D, y - 0.008f * D, x + 0.018f * D, y + 0.008f * D);
                        }
                    }
                    // The curl at the end, turning outwards.
                    using (Pen ink = new Pen(KInk, 0.05f * D))
                    using (Pen fill = new Pen(hair, 0.028f * D))
                    {
                        ink.StartCap = ink.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                        RectangleF cr = new RectangleF(e.X + (s > 0 ? -0.01f : -0.07f) * D, e.Y - 0.01f * D, 0.08f * D, 0.08f * D);
                        g.DrawArc(ink, cr, s > 0 ? 180 : 0, s > 0 ? -250 : 250);
                        g.DrawArc(fill, cr, s > 0 ? 180 : 0, s > 0 ? -250 : 250);
                    }
                    Ellipse(g, Color.FromArgb(196, 46, 40), a.X - 0.028f * D, a.Y - 0.022f * D, 0.056f * D, 0.044f * D);
                }
            }
            // The neck.
            using (GraphicsPath nk = KPoly(D, -0.035f, 0.08f, 0.035f, 0.08f, 0.04f, 0.14f, -0.04f, 0.14f)) KleiShape(g, nk, skin, c2, D);
            float bob = (float)Math.Sin(p.Now / 2600.0 * Math.PI * 2) * 0.004f;
            if (l.Kind == KindWilson)
            {
                Color shirt = Color.FromArgb(236, 232, 222), vest = Color.FromArgb(132, 36, 36), dark = Color.FromArgb(46, 42, 50);
                KLimb(g, D, dark, 0.035f, -0.045f, 0.27f, -0.05f, 0.33f);
                KLimb(g, D, dark, 0.035f, 0.045f, 0.27f, 0.05f, 0.33f);
                KShoe(g, D, -0.06f, 0.345f, Color.FromArgb(30, 28, 32));
                KShoe(g, D, 0.06f, 0.345f, Color.FromArgb(30, 28, 32));
                KLimb(g, D, shirt, 0.045f, -0.09f, 0.15f, -0.19f, 0.19f + bob);
                KLimb(g, D, shirt, 0.045f, 0.09f, 0.15f, 0.19f, 0.19f + bob);
                using (GraphicsPath pants = Theme.Round(new RectangleF(-0.1f * D, 0.22f * D, 0.2f * D, 0.08f * D), 0.02f * D)) KleiShape(g, pants, Lighter(dark, 0.1), dark, D);
                using (GraphicsPath sh = KPoly(D, -0.1f, 0.11f, 0.1f, 0.11f, 0.12f, 0.25f, -0.12f, 0.25f)) KleiShape(g, sh, shirt, Color.FromArgb(206, 200, 190), D);
                foreach (int s in Sides)
                    using (GraphicsPath vp = KPoly(D, s * 0.1f, 0.11f, s * 0.035f, 0.11f, 0, 0.2f, 0, 0.25f, s * 0.12f, 0.25f))
                        { KleiShape(g, vp, Lighter(vest, 0.08), Darker(vest, 0.25), D); if (s > 0) KHatch(g, vp, D, 70); }
                using (GraphicsPath tie = KPoly(D, -0.015f, 0.12f, 0.015f, 0.12f, 0.022f, 0.17f, 0, 0.2f, -0.022f, 0.17f)) KleiShape(g, tie, Color.FromArgb(60, 46, 56), Color.FromArgb(34, 26, 32), D);
            }
            else if (l.Kind == KindWillow)
            {
                Color top = Color.FromArgb(206, 62, 52), skirt = Color.FromArgb(36, 32, 40), sock = Color.FromArgb(30, 28, 34);
                KLimb(g, D, sock, 0.03f, -0.045f, 0.27f, -0.05f, 0.33f);
                KLimb(g, D, sock, 0.03f, 0.045f, 0.27f, 0.05f, 0.33f);
                KShoe(g, D, -0.06f, 0.345f, Color.FromArgb(30, 26, 30));
                KShoe(g, D, 0.06f, 0.345f, Color.FromArgb(30, 26, 30));
                KLimb(g, D, top, 0.042f, -0.085f, 0.15f, -0.19f, 0.19f + bob);
                KLimb(g, D, top, 0.042f, 0.085f, 0.15f, 0.19f, 0.19f + bob);
                using (GraphicsPath sk = KPoly(D, -0.1f, 0.21f, 0.1f, 0.21f, 0.15f, 0.29f, -0.15f, 0.29f)) { KleiShape(g, sk, Lighter(skirt, 0.1), skirt, D); KHatch(g, sk, D, 90); }
                using (GraphicsPath tp = KPoly(D, -0.09f, 0.11f, 0.09f, 0.11f, 0.1f, 0.23f, -0.1f, 0.23f)) { KleiShape(g, tp, Lighter(top, 0.08), Darker(top, 0.25), D); KHatch(g, tp, D, 60); }
                foreach (int s in Sides)
                    using (GraphicsPath cl = KPoly(D, 0, 0.115f, s * 0.06f, 0.11f, s * 0.04f, 0.15f)) KleiShape(g, cl, Color.FromArgb(240, 236, 228), Color.FromArgb(210, 204, 196), D);
            }
            else
            {
                // A duplicant's suit: light grey with a yellow collar and a dark belt; short legs.
                Color suit = Color.FromArgb(206, 204, 194), pants = Color.FromArgb(74, 78, 90);
                KLimb(g, D, pants, 0.05f, -0.05f, 0.27f, -0.055f, 0.32f);
                KLimb(g, D, pants, 0.05f, 0.05f, 0.27f, 0.055f, 0.32f);
                KShoe(g, D, -0.065f, 0.34f, Color.FromArgb(44, 44, 52));
                KShoe(g, D, 0.065f, 0.34f, Color.FromArgb(44, 44, 52));
                KLimb(g, D, suit, 0.05f, -0.1f, 0.15f, -0.2f, 0.19f + bob);
                KLimb(g, D, suit, 0.05f, 0.1f, 0.15f, 0.2f, 0.19f + bob);
                using (GraphicsPath sp = Theme.Round(new RectangleF(-0.13f * D, 0.11f * D, 0.26f * D, 0.17f * D), 0.05f * D)) { KleiShape(g, sp, suit, Color.FromArgb(166, 164, 156), D); KHatch(g, sp, D, 45); }
                Line(g, Color.FromArgb(70, 60, 50), D * 0.02f, -0.12f * D, 0.235f * D, 0.12f * D, 0.235f * D);
                using (GraphicsPath col = Theme.Round(new RectangleF(-0.08f * D, 0.105f * D, 0.16f * D, 0.04f * D), 0.02f * D)) KleiShape(g, col, Color.FromArgb(246, 204, 70), Color.FromArgb(214, 160, 30), D);
            }
        }

        // ---- In front of the face: the head's ink, the hair, brows and nose.
        static void KleiDetails(Graphics g, MascotLook l, Geo geo, float D, MascotPose p)
        {
            float fy = geo.FaceY * D, gap = geo.EyeGap * D;
            switch (l.Kind)
            {
                case KindWilson:
                {
                    // The swept-up black hair, as in the game: a tall crown of five points, the middle one highest, and a
                    // widow's peak over the forehead.
                    using (GraphicsPath h = KCurve(D, 0.2f, -0.255f, -0.08f, -0.29f, -0.28f, -0.38f, -0.5f, -0.2f, -0.38f, -0.2f, -0.6f, -0.06f, -0.4f,
                                                   0.02f, -0.65f, 0.1f, -0.4f, 0.22f, -0.6f, 0.22f, -0.36f, 0.38f, -0.48f, 0.28f, -0.26f, 0.255f, -0.08f,
                                                   0.2f, -0.2f, 0.06f, -0.24f, 0, -0.17f, -0.06f, -0.24f, -0.2f, -0.2f))
                    {
                        KleiShape(g, h, Color.FromArgb(52, 48, 56), Color.FromArgb(22, 20, 26), D);
                        Region old = g.Clip;
                        g.SetClip(h, CombineMode.Intersect);
                        using (Pen hl = new Pen(Color.FromArgb(120, 120, 120, 132), Math.Max(1f, D * 0.008f)))
                        {
                            hl.StartCap = hl.EndCap = LineCap.Round;
                            g.DrawBezier(hl, P(-0.2f * D, -0.25f * D), P(-0.25f * D, -0.35f * D), P(-0.29f * D, -0.44f * D), P(-0.33f * D, -0.52f * D));
                            g.DrawBezier(hl, P(0.18f * D, -0.27f * D), P(0.24f * D, -0.36f * D), P(0.28f * D, -0.43f * D), P(0.33f * D, -0.5f * D));
                            g.DrawBezier(hl, P(-0.04f * D, -0.28f * D), P(-0.04f * D, -0.36f * D), P(-0.03f * D, -0.42f * D), P(-0.02f * D, -0.47f * D));
                        }
                        g.Clip = old;
                        old.Dispose();
                    }
                    // Worried brows, raised in the middle, and a small nose.
                    foreach (int s in Sides) Line(g, KInk, D * 0.016f, s * 0.13f * D, fy - 0.06f * D, s * 0.045f * D, fy - 0.082f * D);
                    using (Pen n = new Pen(KInk, Math.Max(1f, D * 0.011f))) { n.StartCap = n.EndCap = LineCap.Round; g.DrawBezier(n, P(0.005f * D, fy + 0.03f * D), P(-0.012f * D, fy + 0.06f * D), P(0.0f, fy + 0.075f * D), P(0.02f * D, fy + 0.07f * D)); }
                    break;
                }
                case KindWillow:
                {
                    // A dark bob framing the face, its bangs cut straight and a little uneven.
                    using (GraphicsPath h = KCurve(D, 0.3f, -0.27f, 0.04f, -0.29f, -0.18f, -0.24f, -0.33f, 0, -0.37f, 0.24f, -0.33f, 0.29f, -0.18f, 0.27f, 0.04f,
                                                   0.22f, 0.0f, 0.21f, -0.17f, 0.15f, -0.2f, 0.1f, -0.165f, 0.04f, -0.205f, -0.02f, -0.165f, -0.08f, -0.205f,
                                                   -0.14f, -0.165f, -0.21f, -0.17f, -0.22f, 0.0f))
                        { KleiShape(g, h, Color.FromArgb(70, 50, 44), Color.FromArgb(40, 28, 26), D); KHatch(g, h, D, 70); }
                    using (Pen n = new Pen(KInk, Math.Max(1f, D * 0.01f))) { n.StartCap = n.EndCap = LineCap.Round; g.DrawArc(n, -0.012f * D, fy + 0.04f * D, 0.024f * D, 0.02f * D, 0, 180); }
                    break;
                }
                case KindDupe:
                {
                    // Short black hair: drawn as a cap of small curls, ink first so the outline is only on the outside.
                    float[][] curls = DupeCurls(false);
                    foreach (float[] c in curls)
                        using (Pen ink = new Pen(KInk, D * 0.03f)) g.DrawEllipse(ink, (c[0] - c[2]) * D, (c[1] - c[2]) * D, c[2] * 2 * D, c[2] * 2 * D);
                    foreach (float[] c in curls) Ellipse(g, Color.FromArgb(40, 34, 36), (c[0] - c[2]) * D, (c[1] - c[2]) * D, c[2] * 2 * D, c[2] * 2 * D);
                    foreach (float[] c in curls) Ellipse(g, Color.FromArgb(90, 255, 255, 255), (c[0] - c[2] * 0.4f) * D, (c[1] - c[2] * 0.6f) * D, c[2] * 0.5f * D, c[2] * 0.35f * D);
                    DupeFace(g, fy, gap, D);
                    break;
                }
                case KindDupeCurly:
                {
                    // A big cloud of curls.
                    float[][] curls = DupeCurls(true);
                    foreach (float[] c in curls)
                        using (Pen ink = new Pen(KInk, D * 0.03f)) g.DrawEllipse(ink, (c[0] - c[2]) * D, (c[1] - c[2]) * D, c[2] * 2 * D, c[2] * 2 * D);
                    foreach (float[] c in curls)
                    {
                        RectangleF r = new RectangleF((c[0] - c[2]) * D, (c[1] - c[2]) * D, c[2] * 2 * D, c[2] * 2 * D);
                        using (GraphicsPath cp = new GraphicsPath())
                        {
                            cp.AddEllipse(r);
                            Fill(g, cp, r, Color.FromArgb(240, 178, 84), Color.FromArgb(196, 116, 42), 90f);
                        }
                    }
                    using (Pen sw = new Pen(Color.FromArgb(150, 150, 84, 30), Math.Max(1f, D * 0.008f)))
                        foreach (float[] c in curls) g.DrawArc(sw, (c[0] - c[2] * 0.55f) * D, (c[1] - c[2] * 0.55f) * D, c[2] * 1.1f * D, c[2] * 1.1f * D, 120, 200);
                    DupeFace(g, fy, gap, D);
                    break;
                }
            }
        }

        // Curls as circles (x, y, radius): a tight cap, or a full cloud with bangs.
        static float[][] DupeCurls(bool cloud)
        {
            System.Collections.Generic.List<float[]> c = new System.Collections.Generic.List<float[]>();
            int n = cloud ? 9 : 8;
            for (int i = 0; i < n; i++)
            {
                double a = (-170 + i * (160.0 / (n - 1))) * Math.PI / 180;
                float rr = cloud ? 0.29f : 0.25f;
                c.Add(new float[] { (float)Math.Cos(a) * rr, -0.1f + (float)Math.Sin(a) * rr * 0.9f, cloud ? 0.095f : 0.07f });
            }
            if (cloud)
            {
                c.Add(new float[] { -0.13f, -0.24f, 0.075f });
                c.Add(new float[] { 0.0f, -0.27f, 0.08f });
                c.Add(new float[] { 0.13f, -0.24f, 0.075f });
                c.Add(new float[] { 0.0f, -0.36f, 0.1f });
            }
            else
            {
                c.Add(new float[] { -0.09f, -0.26f, 0.065f });
                c.Add(new float[] { 0.05f, -0.27f, 0.065f });
                c.Add(new float[] { 0.0f, -0.32f, 0.08f });
            }
            return c.ToArray();
        }

        // A duplicant's brows and little nose.
        static void DupeFace(Graphics g, float fy, float gap, float D)
        {
            using (Pen b = new Pen(KInk, Math.Max(1f, D * 0.012f)))
            {
                b.StartCap = b.EndCap = LineCap.Round;
                foreach (int s in Sides) g.DrawArc(b, s * gap - 0.035f * D, fy - 0.085f * D, 0.07f * D, 0.04f * D, 200, 140);
                g.DrawArc(b, -0.015f * D, fy + 0.03f * D, 0.03f * D, 0.025f * D, 300, 200);
            }
        }

        static string[] KleiLines(int kind)
        {
            switch (kind)
            {
                case KindWilson: return new string[] { "\u00A1Me muero de hambre!", "\u00A1Por la ciencia!", "Deber\u00EDa encender una fogata antes de que anochezca." };
                case KindWillow: return new string[] { "\u00A1Fuego! \u00A1Precioso fuego!", "Bernie y yo te protegemos.", "Esto arder\u00EDa genial." };
                default: return new string[] { "Necesito ox\u00EDgeno\u2026", "\u00BFQui\u00E9n ha dejado el slime aqu\u00ED?", "\u00A1Tarea completada!" };
            }
        }
    }

    public partial class Mascot
    {
        // Wilson's white eyes with small pupils, Willow's blank ones with dark rings, a duplicant's dots.
        bool KleiEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            int k = Look.Kind;
            float sk = surprised ? 1.12f : 1f;
            if (k == MascotParts.KindWilson || k == MascotParts.KindWillow)
            {
                bool willow = k == MascotParts.KindWillow;
                float w = (willow ? 0.112f : 0.085f) * D * sk, h = (willow ? 0.112f : 0.1f) * D * sk * blink;
                MascotParts.Ellipse(g, Color.FromArgb(willow ? 110 : 70, 70, 60, 70), x - w * 0.62f, y - h * 0.42f, w * 1.24f, h * 1.14f);   // the dark rings round their eyes
                if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return true; }
                using (GraphicsPath ep = Ellipse(new RectangleF(x - w / 2, y - h / 2, w, h)))
                {
                    MascotParts.FillSolid(g, ep, Color.FromArgb(250, 248, 240));
                    MascotParts.Stroke(g, ep, Color.FromArgb(240, 34, 26, 24), Math.Max(1.2f, D * (willow ? 0.014f : 0.011f)));
                }
                if (!willow)
                {
                    float r = 0.019f * D;
                    MascotParts.Ellipse(g, Color.FromArgb(26, 22, 22), x - r + (x < 0 ? r * 0.5f : -r * 0.5f), y - r, r * 2, r * 2);
                }
                return true;
            }
            if (k == MascotParts.KindDupe || k == MascotParts.KindDupeCurly)
            {
                float w = 0.045f * D * sk, h = 0.062f * D * sk * blink;
                if (h < D * 0.015f) { ClosedEye(g, x, y, w * 1.4f, D); return true; }
                MascotParts.Ellipse(g, Color.FromArgb(26, 22, 22), x - w / 2, y - h / 2, w, h);
                MascotParts.Ellipse(g, Color.FromArgb(230, 255, 255, 255), x - w * 0.3f, y - h * 0.34f, w * 0.38f, h * 0.28f);
                return true;
            }
            return false;
        }

        bool KleiMouth(Graphics g, float D, float mx, float fy, bool talking)
        {
            int k = Look.Kind;
            if (!MascotParts.IsKlei(k)) return false;
            bool open = talking || mood == Mood.Happy || mood == Mood.Love;
            Color ink = Color.FromArgb(34, 26, 24);
            float lw = Math.Max(1.2f, D * 0.012f);
            bool dupe = k == MascotParts.KindDupe || k == MascotParts.KindDupeCurly;
            float my = fy + (dupe ? 0.11f : 0.14f) * D, w = (dupe ? 0.08f : 0.07f) * D;
            if (open)
            {
                OpenMouth(g, mx, my, w, 0.045f * D, D);
                return true;
            }
            using (Pen p = new Pen(ink, lw))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                if (dupe) g.DrawArc(p, mx - w / 2, my - 0.03f * D, w, 0.05f * D, 20, 140);         // a little smile
                else if (k == MascotParts.KindWilson) g.DrawArc(p, mx - w / 2, my - 0.005f * D, w, 0.04f * D, 200, 140);   // worried
                else g.DrawLine(p, mx - w * 0.35f, my + 0.004f * D, mx + w * 0.35f, my);   // Willow's flat, unimpressed mouth
            }
            return true;
        }
    }
}

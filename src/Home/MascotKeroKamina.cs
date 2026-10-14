// Stackshot - Two more tributes: Kero (the little winged lion, false form) and Kamina (blue spiky hair, pointed
// red shades, red cape, shoulder tattoos and katana).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindKero = 13, ColorHoney = 24, HatKaminaHair = 44, OutfitKamina = 37, FaceKaminaShades = 20;
        static readonly Color Feather1 = Color.FromArgb(255, 255, 255), Feather2 = Color.FromArgb(206, 214, 232);
        // Left and right, without a new array every frame.
        static readonly int[] Sides = { -1, 1 };

        // ---- Kero: behind the body go the tail with its puff, the small white wings and the pointed ears.
        static void KeroBack(Graphics g, float D, Color c1, Color c2, MascotPose p)
        {
            KeroTail(g, D, c1, c2, p.Now);
            foreach (int s in Sides) KeroWing(g, D, s, p.Now, p.Sleeping);
            foreach (int s in Sides) KeroEar(g, D, s, c1, c2, s < 0 ? p.EarL : p.EarR);
        }

        // Tuft puffs: x, y (in puff radii from the tail tip) and size.
        static readonly float[][] TuftPuffs = { new float[] { 0, -0.6f, 1f }, new float[] { -0.55f, 0.1f, 0.85f }, new float[] { 0.55f, 0.05f, 0.85f }, new float[] { 0, 0.55f, 0.8f } };

        static void KeroTail(Graphics g, float D, Color c1, Color c2, double now)
        {
            float sway = (float)Math.Sin(now / 640.0) * 0.04f * D;
            PointF end = P(0.47f * D + sway, -0.05f * D);
            using (GraphicsPath t = new GraphicsPath())
            {
                t.AddBezier(P(0.2f * D, 0.25f * D), P(0.44f * D, 0.31f * D), P(0.52f * D, 0.12f * D), end);
                using (Pen pen = new Pen(Mac.Mix(c1, c2, 0.55), D * 0.036f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawPath(pen, t); }
            }
            // Fluffy tuft: a few overlapping puffs, lighter than the body, filled as one shape.
            float r = 0.05f * D;
            using (GraphicsPath pp = new GraphicsPath(FillMode.Winding))
            {
                foreach (float[] q in TuftPuffs)
                {
                    float rr = r * q[2];
                    pp.AddEllipse(end.X + q[0] * r - rr, end.Y + q[1] * r - rr, rr * 2, rr * 2);
                }
                Fill(g, pp, new RectangleF(end.X - r * 1.4f, end.Y - r * 1.6f, r * 2.8f, r * 3.0f), Color.FromArgb(255, 250, 232), Color.FromArgb(236, 214, 170), 90f);
            }
            Ellipse(g, Color.FromArgb(180, 255, 255, 255), end.X - r * 0.5f, end.Y - r * 1.1f, r * 0.6f, r * 0.45f);
        }

        // Small feathered angel wing out of the shoulder; flaps gently, folds while asleep.
        static void KeroWing(Graphics g, float D, int s, double now, bool sleeping)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(s * 0.26f * D, 0.02f * D);
            g.RotateTransform((float)(s * (sleeping ? 10 : Math.Sin(now / 210.0) * 13 - 3)));
            PointF tip = P(s * 0.27f * D, -0.25f * D);
            using (GraphicsPath w = new GraphicsPath())
            {
                w.AddBezier(P(0, 0.01f * D), P(s * 0.06f * D, -0.14f * D), P(s * 0.16f * D, -0.24f * D), tip);
                // Lower edge: three rounded feathers back to the root.
                w.AddBezier(tip, P(s * 0.31f * D, -0.17f * D), P(s * 0.29f * D, -0.11f * D), P(s * 0.23f * D, -0.1f * D));
                w.AddBezier(P(s * 0.23f * D, -0.1f * D), P(s * 0.25f * D, -0.04f * D), P(s * 0.2f * D, 0), P(s * 0.15f * D, -0.01f * D));
                w.AddBezier(P(s * 0.15f * D, -0.01f * D), P(s * 0.15f * D, 0.05f * D), P(s * 0.08f * D, 0.07f * D), P(s * 0.04f * D, 0.05f * D));
                w.CloseFigure();
                RectangleF r = new RectangleF(-0.32f * D, -0.26f * D, 0.64f * D, 0.34f * D);
                Fill(g, w, r, Feather1, Feather2, 90f);
                Stroke(g, w, Color.FromArgb(110, 150, 160, 190), D * 0.009f);
            }
            using (Pen vein = new Pen(Color.FromArgb(90, 150, 160, 190), Math.Max(0.6f, D * 0.007f)))
            {
                vein.StartCap = vein.EndCap = LineCap.Round;
                g.DrawLine(vein, s * 0.07f * D, -0.03f * D, s * 0.22f * D, -0.1f * D);
                g.DrawLine(vein, s * 0.06f * D, 0.01f * D, s * 0.15f * D, -0.01f * D);
            }
            g.Restore(st);
        }

        // Upright, slightly rounded pointed ears, same fur as the body.
        static void KeroEar(Graphics g, float D, int s, Color c1, Color c2, double twitch)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(s * 0.2f * D, -0.24f * D);
            g.RotateTransform((float)(s * (20 + twitch)));
            using (GraphicsPath ear = new GraphicsPath())
            {
                ear.AddBezier(P(-0.085f * D, 0.06f * D), P(-0.09f * D, -0.06f * D), P(-0.03f * D, -0.17f * D), P(s * 0.01f * D, -0.2f * D));
                ear.AddBezier(P(s * 0.01f * D, -0.2f * D), P(0.04f * D, -0.17f * D), P(0.09f * D, -0.06f * D), P(0.085f * D, 0.06f * D));
                ear.CloseFigure();
                Fill(g, ear, new RectangleF(-0.09f * D, -0.2f * D, 0.18f * D, 0.26f * D), c1, c2, 70f);
                Stroke(g, ear, Color.FromArgb(70, Darker(c2, 0.3)), D * 0.009f);
            }
            using (GraphicsPath inner = new GraphicsPath())
            {
                inner.AddBezier(P(-0.04f * D, 0.04f * D), P(-0.045f * D, -0.05f * D), P(-0.01f * D, -0.11f * D), P(s * 0.006f * D, -0.13f * D));
                inner.AddBezier(P(s * 0.006f * D, -0.13f * D), P(0.02f * D, -0.11f * D), P(0.045f * D, -0.05f * D), P(0.04f * D, 0.04f * D));
                inner.CloseFigure();
                FillSolid(g, inner, Color.FromArgb(70, Darker(c2, 0.1)));
            }
            g.Restore(st);
        }

        // Kero smiles with a single little fang on the upper lip.
        public static void KeroFang(Graphics g, float mx, float my, float w, float h)
        {
            using (GraphicsPath f = Poly(P(mx + w * 0.1f, my), P(mx + w * 0.3f, my), P(mx + w * 0.2f, my + h * 0.45f)))
                FillSolid(g, f, Color.White);
        }

        // ---- Kamina: the cape and the katana behind him.
        static void KaminaBack(Graphics g, Geo geo, float D, double now)
        {
            Cape(g, Color.FromArgb(226, 38, 34), Color.FromArgb(132, 12, 16), geo, D, now);
            Katana(g, D);
        }

        // Held upright at his left side, leaning out: a long wrapped grip with its cap, an oval iron guard with a gold
        // rim and the gold collar, then a gently curved blade (edge outward, with its wavy temper line and the ridge)
        // that ends in an angled point above his shoulder.
        static void Katana(Graphics g, float D)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(-0.38f * D, 0.23f * D);
            g.RotateTransform(-14);

            // Grip: dark wrap with the cream diamond lacing, a dark gold cap at the end.
            RectangleF grip = new RectangleF(-0.024f * D, -0.012f * D, 0.048f * D, 0.2f * D);
            using (GraphicsPath gp = Theme.Round(grip, 0.016f * D))
            {
                Fill(g, gp, grip, Color.FromArgb(58, 44, 60), Color.FromArgb(22, 16, 24), 0f);
                Region old = g.Clip;
                g.SetClip(gp, CombineMode.Intersect);
                using (Pen wrap = new Pen(Color.FromArgb(215, 236, 226, 204), Math.Max(0.6f, D * 0.008f)))
                    for (float y = grip.Y + 0.018f * D; y < grip.Bottom - 0.01f * D; y += 0.034f * D)
                    {
                        g.DrawLine(wrap, grip.X, y, grip.Right, y + 0.017f * D);
                        g.DrawLine(wrap, grip.Right, y, grip.X, y + 0.017f * D);
                    }
                g.Clip = old;
                old.Dispose();
                Stroke(g, gp, Color.FromArgb(150, 10, 6, 12), D * 0.005f);
            }
            RectangleF cap = new RectangleF(-0.027f * D, grip.Bottom - 0.02f * D, 0.054f * D, 0.032f * D);
            using (GraphicsPath cp = Theme.Round(cap, 0.012f * D)) { Fill(g, cp, cap, Gold2, Color.FromArgb(120, 84, 18), 90f); Stroke(g, cp, Color.FromArgb(120, 90, 60, 10), D * 0.005f); }

            // Blade: sampled along its length with a gentle curve; the edge (outer side) is on the left.
            const int n = 16;
            float baseY = -0.075f * D, topY = -0.79f * D, half = 0.027f * D;
            PointF[] edge = new PointF[n + 1], back = new PointF[n + 1], temper = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, y = baseY + (topY - baseY) * t, bend = 0.075f * D * t * t;
                float w = half * (1 - 0.18f * t);
                edge[i] = P(-w + bend, y);
                back[i] = P(w * 0.85f + bend, y);
                temper[i] = P(-w + bend + w * 0.55f + (float)Math.Sin(t * 26) * 0.005f * D, y);
            }
            // The point: the back runs straight up and the edge sweeps into it.
            PointF tip = P(back[n].X - 0.004f * D, topY - 0.062f * D);
            using (GraphicsPath blade = new GraphicsPath())
            {
                List<PointF> outline = new List<PointF>(edge);
                outline.Add(P(edge[n].X + 0.01f * D, topY - 0.04f * D));
                outline.Add(tip);
                for (int i = n; i >= 0; i--) outline.Add(back[i]);
                blade.AddPolygon(outline.ToArray());
                RectangleF br = new RectangleF(edge[0].X, tip.Y, back[n].X - edge[0].X + 0.01f * D, baseY - tip.Y);
                Fill(g, blade, br, Color.FromArgb(250, 252, 255), Color.FromArgb(146, 156, 176), 0f);
                Region old = g.Clip;
                g.SetClip(blade, CombineMode.Intersect);
                // The temper line: a soft frosted band along the edge with a wavy border.
                using (GraphicsPath hamon = new GraphicsPath())
                {
                    List<PointF> hp = new List<PointF>(edge);
                    for (int i = n; i >= 0; i--) hp.Add(temper[i]);
                    hamon.AddPolygon(hp.ToArray());
                    FillSolid(g, hamon, Color.FromArgb(120, 255, 255, 255));
                }
                using (Pen ridge = new Pen(Color.FromArgb(120, 96, 106, 128), Math.Max(0.6f, D * 0.005f)))
                {
                    PointF[] rp = new PointF[n + 1];
                    for (int i = 0; i <= n; i++) rp[i] = P(back[i].X - (back[i].X - edge[i].X) * 0.3f, back[i].Y);
                    g.DrawLines(ridge, rp);
                }
                g.Clip = old;
                old.Dispose();
                Stroke(g, blade, Color.FromArgb(130, 60, 70, 92), D * 0.006f);
            }

            // Gold collar just above the guard, then the guard itself: an oval of dark iron with a gold rim.
            RectangleF collar = new RectangleF(-0.03f * D, -0.085f * D, 0.058f * D, 0.05f * D);
            using (GraphicsPath cl = Theme.Round(collar, 0.008f * D)) { Fill(g, cl, collar, Gold1, Gold2, 0f); Stroke(g, cl, Color.FromArgb(120, 120, 70, 10), D * 0.005f); }
            RectangleF guard = new RectangleF(-0.07f * D, -0.045f * D, 0.14f * D, 0.042f * D);
            using (GraphicsPath gd = new GraphicsPath())
            {
                gd.AddEllipse(guard);
                Fill(g, gd, guard, Color.FromArgb(70, 66, 74), Color.FromArgb(24, 22, 28), 90f);
                Stroke(g, gd, Gold2, D * 0.008f);
            }
            g.Restore(st);
        }

        // ---- Kamina's hair: light blue, wild spikes swept up and out, forehead mostly clear. Origin at the top of the head.
        static readonly float[] KaminaHairPts =
        {
            -0.66f, 0.44f,  -0.9f, 0.14f,  -0.6f, 0.1f,  -0.84f, -0.3f,  -0.42f, -0.08f,  -0.54f, -0.64f,  -0.2f, -0.16f,
            -0.14f, -0.86f,  0.04f, -0.18f,  0.3f, -0.78f,  0.3f, -0.12f,  0.7f, -0.5f,  0.52f, 0.04f,  0.92f, -0.04f,
            0.64f, 0.2f,  0.66f, 0.44f,
            // Hairline back to the left, high on the forehead (the shades' points show under it), with a few short strands.
            0.5f, 0.1f,  0.34f, 0.19f,  0.2f, 0.06f,  0.04f, 0.16f,  -0.12f, 0.04f,  -0.32f, 0.17f,  -0.48f, 0.07f
        };

        static void KaminaHair(Graphics g, float u)
        {
            PointF[] pts = new PointF[KaminaHairPts.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(KaminaHairPts[i * 2] * u, KaminaHairPts[i * 2 + 1] * u);
            using (GraphicsPath h = new GraphicsPath())
            {
                h.AddPolygon(pts);
                RectangleF r = new RectangleF(-0.92f * u, -0.86f * u, 1.84f * u, 1.32f * u);
                Fill(g, h, r, Color.FromArgb(150, 214, 255), Color.FromArgb(38, 96, 200), 80f);
                Shine(g, h, r, 80);
                Region old = g.Clip;
                g.SetClip(h, CombineMode.Intersect);
                // Darker strands from each spike tip back towards the scalp, for volume.
                using (Pen strand = new Pen(Color.FromArgb(70, 20, 50, 130), Math.Max(0.6f, u * 0.022f)))
                {
                    strand.StartCap = strand.EndCap = LineCap.Round;
                    for (int i = 1; i <= 13; i += 2)
                        g.DrawLine(strand, pts[i].X * 0.55f, pts[i].Y * 0.55f + 0.12f * u, pts[i].X * 0.15f, 0.2f * u);
                }
                g.Clip = old;
                old.Dispose();
                Stroke(g, h, Color.FromArgb(120, 16, 40, 100), u * 0.016f);
            }
        }

        // ---- Kamina's shades: two sharp red lenses swept up into points, joined by a thin bridge.
        static void KaminaShades(Graphics g, Geo geo, float D)
        {
            float fy = geo.FaceY * D, gap = geo.EyeGap * D;
            Color frame = Color.FromArgb(150, 16, 20);
            foreach (int s in Sides)
            {
                using (GraphicsPath lens = new GraphicsPath())
                {
                    // Wide enough to cover the eye wherever it looks (the eyes are hidden behind them anyway).
                    PointF a = P(s * 0.03f * D, fy - 0.055f * D), b = P(s * (gap + 0.17f * D), fy - 0.11f * D);
                    PointF c = P(s * (gap + 0.035f * D), fy + 0.062f * D), d = P(s * 0.045f * D, fy + 0.03f * D);
                    lens.AddLine(a, b);
                    lens.AddBezier(b, P(s * (gap + 0.14f * D), fy - 0.02f * D), P(s * (gap + 0.09f * D), fy + 0.05f * D), c);
                    lens.AddLine(c, d);
                    lens.CloseFigure();
                    RectangleF r = new RectangleF(Math.Min(a.X, b.X), b.Y, Math.Abs(b.X - a.X), c.Y - b.Y);
                    Fill(g, lens, r, Color.FromArgb(240, 255, 112, 64), Color.FromArgb(245, 176, 20, 28), 90f);
                    Region old = g.Clip;
                    g.SetClip(lens, CombineMode.Intersect);
                    Line(g, Color.FromArgb(110, 255, 255, 255), D * 0.016f, s * (gap - 0.02f * D), fy + 0.05f * D, s * (gap + 0.05f * D), fy - 0.1f * D);
                    g.Clip = old;
                    old.Dispose();
                    Stroke(g, lens, frame, D * 0.012f);
                }
            }
            Line(g, frame, D * 0.014f, -0.04f * D, fy - 0.052f * D, 0.04f * D, fy - 0.052f * D);
        }

        // Opaque shades: the eyes behind them aren't drawn, and the shades turn with the face (see Mascot.Paint).
        public static bool CoversEyes(MascotLook l) { return l.Face == FaceKaminaShades; }

        // ---- Kamina's outfit (clipped to the body): bare chest, blue flame tattoos on the shoulders, the open cape
        // falling down both sides and light baggy trousers with a dark belt.
        static void KaminaOutfit(Graphics g, float D, float y, Geo geo)
        {
            // Six tongues of flame licking inwards and up, filled as one shape.
            using (GraphicsPath f = new GraphicsPath(FillMode.Winding))
            {
                foreach (int s in Sides)
                    for (int k = 0; k < 3; k++)
                    {
                        float x0 = s * (0.28f - k * 0.04f) * D, y0 = y - 0.02f * D + k * 0.05f * D;
                        f.StartFigure();
                        f.AddBezier(P(x0, y0 + 0.05f * D), P(x0 - s * 0.03f * D, y0), P(x0 - s * 0.05f * D, y0 - 0.01f * D), P(x0 - s * 0.1f * D, y0 - 0.04f * D));
                        f.AddBezier(P(x0 - s * 0.1f * D, y0 - 0.04f * D), P(x0 - s * 0.06f * D, y0 + 0.01f * D), P(x0 - s * 0.03f * D, y0 + 0.04f * D), P(x0 + s * 0.01f * D, y0 + 0.08f * D));
                        f.CloseFigure();
                    }
                FillSolid(g, f, Color.FromArgb(220, 34, 84, 196));
            }
            foreach (int s in Sides)
            {
                using (GraphicsPath side = Poly(P(s * 0.31f * D, y - 0.08f * D), P(s * D, y - 0.08f * D), P(s * D, geo.Bottom * D + D), P(s * 0.34f * D, geo.Bottom * D + D)))
                    Fill(g, side, new RectangleF(-D, y - 0.08f * D, 2 * D, D), Color.FromArgb(236, 48, 40), Color.FromArgb(140, 14, 18), 90f);
                Line(g, Color.FromArgb(90, 80, 0, 0), D * 0.01f, s * 0.31f * D, y - 0.08f * D, s * 0.34f * D, geo.Bottom * D);
            }
            float belt = y + 0.13f * D;
            RectangleF pants = new RectangleF(-D, belt, 2 * D, geo.Bottom * D - belt + 0.2f * D);
            using (GraphicsPath pp = new GraphicsPath())
            {
                pp.AddRectangle(pants);
                Fill(g, pp, pants, Color.FromArgb(244, 240, 232), Color.FromArgb(196, 190, 178), 90f);
            }
            Line(g, Color.FromArgb(70, 90, 80, 60), D * 0.008f, 0, belt + 0.05f * D, 0, geo.Bottom * D);
            RectangleF b = new RectangleF(-D, belt - 0.02f * D, 2 * D, 0.045f * D);
            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddRectangle(b);
                Fill(g, bp, b, Color.FromArgb(92, 62, 44), Color.FromArgb(48, 30, 20), 90f);
            }
            RectangleF buckle = new RectangleF(-0.03f * D, belt - 0.025f * D, 0.06f * D, 0.055f * D);
            using (GraphicsPath k = Theme.Round(buckle, 0.01f * D)) Stroke(g, k, Color.FromArgb(220, 222, 230), D * 0.012f);
        }

        // End of the hat, outfit and face chains.
        static bool KaminaHat(Graphics g, int hat, float u)
        {
            if (hat != HatKaminaHair) return false;
            KaminaHair(g, u);
            return true;
        }

        static bool KaminaOutfitPiece(Graphics g, MascotLook l, float D, float y, Geo geo)
        {
            if (l.Outfit != OutfitKamina) return false;
            KaminaOutfit(g, D, y, geo);
            return true;
        }

        static bool KaminaFace(Graphics g, MascotLook l, float D, Geo geo)
        {
            if (l.Face != FaceKaminaShades) return false;
            KaminaShades(g, geo, D);
            return true;
        }
    }
}

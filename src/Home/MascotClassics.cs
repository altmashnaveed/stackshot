// Stackshot - Classic tributes redone with their own build, truer to the originals: BMO (a little game console: a box
// with a screen for a face, the yellow cross, red, blue and green buttons and a cartridge slot), Doraemon (round and
// blue, a white face and belly with the pocket, white eyes side by side, the red nose with whiskers and the collar with
// its bell) and Totoro (big, grey and pear-shaped, small pointed ears, a light belly with its chevrons, round white
// eyes and whiskers); and Goku's and Vegeta's own hair. Fan tributes, not affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindBmo = 25, KindDoraemon = 26, KindTotoro = 27;
        public const int HatGoku = 46, HatVegeta = 47;

        public static bool IsClassic(int kind) { return kind >= KindBmo && kind <= KindTotoro; }

        // Totoro's shape: narrower at the top, wide and round at the bottom.
        static GraphicsPath PearPath(float D)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(P(0, -0.29f * D), P(0.2f * D, -0.29f * D), P(0.28f * D, -0.2f * D), P(0.32f * D, -0.04f * D));
            p.AddBezier(P(0.32f * D, -0.04f * D), P(0.38f * D, 0.16f * D), P(0.34f * D, 0.33f * D), P(0, 0.33f * D));
            p.AddBezier(P(0, 0.33f * D), P(-0.34f * D, 0.33f * D), P(-0.38f * D, 0.16f * D), P(-0.32f * D, -0.04f * D));
            p.AddBezier(P(-0.32f * D, -0.04f * D), P(-0.28f * D, -0.2f * D), P(-0.2f * D, -0.29f * D), P(0, -0.29f * D));
            p.CloseFigure();
            return p;
        }

        // ---- Behind the body: Totoro's small pointed ears.
        static void ClassicBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            if (l.Kind != KindTotoro) return;
            foreach (int s in Sides)
            {
                GraphicsState st = g.Save();
                g.TranslateTransform(s * 0.13f * D, -0.25f * D);
                g.RotateTransform((float)(s * (10 + (s < 0 ? p.EarL : p.EarR) * 0.6)));
                using (GraphicsPath e = Leaf(0.075f * D, 0.17f * D, D)) Fill(g, e, new RectangleF(-0.08f * D, -0.17f * D, 0.16f * D, 0.2f * D), Lighter(c1, 0.05), c2, 80f);
                g.Restore(st);
            }
        }

        // ---- On the body (clipped to it, before the face).
        static void ClassicBody(Graphics g, MascotLook l, float D, GraphicsPath body, Color c1, Color c2)
        {
            Region old = g.Clip;
            g.SetClip(body, CombineMode.Intersect);
            switch (l.Kind)
            {
                case KindBmo:
                {
                    // The screen: a pale mint panel with rounded corners and a faint inner shade.
                    RectangleF scr = new RectangleF(-0.22f * D, -0.27f * D, 0.44f * D, 0.27f * D);
                    using (GraphicsPath sp = Theme.Round(scr, 0.05f * D))
                    {
                        Fill(g, sp, scr, Color.FromArgb(206, 246, 226), Color.FromArgb(160, 226, 200), 90f);
                        Stroke(g, sp, Color.FromArgb(120, Darker(c2, 0.3)), D * 0.01f);
                    }
                    break;
                }
                case KindDoraemon:
                {
                    // The white face, open at the top, and the white belly.
                    RectangleF face = new RectangleF(-0.3f * D, -0.21f * D, 0.6f * D, 0.38f * D);
                    using (GraphicsPath fp = new GraphicsPath())
                    {
                        fp.AddEllipse(face);
                        Fill(g, fp, face, Color.FromArgb(255, 255, 255), Color.FromArgb(226, 232, 240), 90f);
                    }
                    RectangleF belly = new RectangleF(-0.2f * D, 0.12f * D, 0.4f * D, 0.3f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, Color.FromArgb(255, 255, 255), Color.FromArgb(228, 232, 240), 90f);
                    }
                    break;
                }
                case KindTotoro:
                {
                    // The light belly, and its grey chevrons in two rows.
                    RectangleF belly = new RectangleF(-0.25f * D, -0.02f * D, 0.5f * D, 0.42f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, Color.FromArgb(244, 238, 222), Color.FromArgb(214, 206, 188), 90f);
                    }
                    Color mark = Mac.Mix(c1, c2, 0.4);
                    float[][] rows = { new float[] { -0.08f, 0.0f, 0.08f }, new float[] { -0.12f, -0.04f, 0.04f, 0.12f } };
                    for (int r = 0; r < rows.Length; r++)
                        foreach (float x in rows[r])
                        {
                            float y = (0.07f + r * 0.07f) * D, w = 0.035f * D;
                            using (GraphicsPath v = Poly(P(x * D - w, y), P(x * D, y - 0.03f * D), P(x * D + w, y), P(x * D, y - 0.012f * D)))
                                FillSolid(g, v, mark);
                        }
                    break;
                }
            }
            g.Clip = old;
            old.Dispose();
        }

        // ---- In front of the face.
        static void ClassicDetails(Graphics g, MascotLook l, Geo geo, float D)
        {
            float fy = geo.FaceY * D;
            switch (l.Kind)
            {
                case KindBmo:
                {
                    // The cartridge slot, the yellow cross and the buttons.
                    Line(g, Color.FromArgb(150, 20, 80, 70), D * 0.014f, -0.12f * D, 0.04f * D, 0.04f * D, 0.04f * D);
                    float cx = -0.13f * D, cy = 0.16f * D, a = 0.032f * D, b = 0.011f * D;
                    using (GraphicsPath cross = new GraphicsPath(FillMode.Winding))
                    {
                        cross.AddRectangle(new RectangleF(cx - a, cy - b, a * 2, b * 2));
                        cross.AddRectangle(new RectangleF(cx - b, cy - a, b * 2, a * 2));
                        FillSolid(g, cross, Color.FromArgb(250, 210, 50));
                        Stroke(g, cross, Color.FromArgb(120, 150, 110, 10), D * 0.005f);
                    }
                    Button(g, D, 0.15f * D, 0.13f * D, 0.032f * D, Color.FromArgb(234, 60, 70));
                    Button(g, D, 0.2f * D, 0.21f * D, 0.02f * D, Color.FromArgb(70, 200, 110));
                    using (GraphicsPath tri = Poly(P(0.06f * D, 0.24f * D), P(0.085f * D, 0.195f * D), P(0.11f * D, 0.24f * D)))
                        FillSolid(g, tri, Color.FromArgb(60, 150, 236));
                    break;
                }
                case KindDoraemon:
                {
                    // The red nose, the line down to the mouth, the whiskers and the collar with its bell.
                    float ny = fy + 0.075f * D, r = 0.032f * D;
                    Line(g, Color.FromArgb(200, 40, 40, 50), D * 0.008f, 0, ny + r, 0, fy + 0.16f * D);
                    foreach (int s in Sides)
                        for (int i = -1; i <= 1; i++)
                            Line(g, Color.FromArgb(180, 40, 40, 50), D * 0.007f, s * 0.08f * D, ny + 0.03f * D + i * 0.03f * D, s * 0.24f * D, ny + 0.02f * D + i * 0.05f * D);
                    RectangleF nose = new RectangleF(-r, ny - r, r * 2, r * 2);
                    using (GraphicsPath np = new GraphicsPath())
                    {
                        np.AddEllipse(nose);
                        Fill(g, np, nose, Color.FromArgb(244, 70, 70), Color.FromArgb(196, 26, 36), 90f);
                    }
                    Ellipse(g, Color.FromArgb(220, 255, 255, 255), -r * 0.5f, ny - r * 0.6f, r * 0.6f, r * 0.5f);
                    float cy = geo.NeckY * D + 0.01f * D;
                    RectangleF band = new RectangleF(-0.3f * D, cy - 0.025f * D, 0.6f * D, 0.05f * D);
                    using (GraphicsPath bp = Theme.Round(band, 0.025f * D)) Fill(g, bp, band, Color.FromArgb(232, 44, 52), Color.FromArgb(176, 20, 30), 90f);
                    float br = 0.042f * D;
                    RectangleF bell = new RectangleF(-br, cy, br * 2, br * 2);
                    using (GraphicsPath bl = new GraphicsPath())
                    {
                        bl.AddEllipse(bell);
                        Fill(g, bl, bell, Gold1, Gold2, 90f);
                    }
                    Line(g, Color.FromArgb(160, 120, 80, 10), D * 0.007f, -br * 0.8f, cy + br * 0.8f, br * 0.8f, cy + br * 0.8f);
                    Ellipse(g, Color.FromArgb(200, 110, 70, 10), -br * 0.18f, cy + br * 1.1f, br * 0.36f, br * 0.4f);
                    // The pocket on the belly.
                    using (Pen pp = new Pen(Color.FromArgb(150, 120, 130, 150), Math.Max(1f, D * 0.008f)))
                        g.DrawArc(pp, -0.13f * D, 0.12f * D, 0.26f * D, 0.18f * D, 0, 180);
                    break;
                }
                case KindTotoro:
                {
                    // A small dark nose and whiskers.
                    using (GraphicsPath n = Poly(P(-0.025f * D, fy + 0.03f * D), P(0.025f * D, fy + 0.03f * D), P(0, fy + 0.055f * D)))
                        FillSolid(g, n, Color.FromArgb(200, 40, 40, 46));
                    foreach (int s in Sides)
                        for (int i = 0; i < 3; i++)
                            Line(g, Color.FromArgb(150, 40, 40, 46), D * 0.006f, s * 0.12f * D, fy + 0.06f * D + i * 0.022f * D, s * 0.27f * D, fy + 0.04f * D + i * 0.035f * D);
                    break;
                }
            }
        }

        static void Button(Graphics g, float D, float x, float y, float r, Color c)
        {
            RectangleF b = new RectangleF(x - r, y - r, r * 2, r * 2);
            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddEllipse(b);
                Fill(g, bp, b, Lighter(c, 0.15), Darker(c, 0.2), 90f);
            }
            Ellipse(g, Color.FromArgb(150, 255, 255, 255), x - r * 0.5f, y - r * 0.6f, r * 0.5f, r * 0.4f);
        }

        // ---- Goku's hair: one black mass, bigger than the head, of wide spikes that fan up and out, and uneven points
        // over the forehead.
        static void GokuHair(Graphics g, float u, double now)
        {
            // Tips: angle from straight up, length past the head, and how much the spike turns to point upwards.
            float[][] tips =
            {
                new float[] { -104, 0.34f, 0.05f }, new float[] { -76, 0.5f, 0.15f }, new float[] { -46, 0.6f, 0.25f }, new float[] { -16, 0.62f, 0.15f },
                new float[] { 16, 0.58f, 0.1f }, new float[] { 48, 0.56f, 0.2f }, new float[] { 78, 0.48f, 0.15f }, new float[] { 106, 0.32f, 0.05f }
            };
            float[][] bangs = { new float[] { 0.5f, 0.2f }, new float[] { 0.36f, 0.3f }, new float[] { 0.22f, 0.14f }, new float[] { 0.06f, 0.32f },
                                new float[] { -0.06f, 0.14f }, new float[] { -0.22f, 0.34f }, new float[] { -0.36f, 0.16f }, new float[] { -0.5f, 0.28f } };
            FlameHair(g, u, tips, bangs, 0.68f, 0.5f, Color.FromArgb(44, 46, 60), Color.FromArgb(8, 8, 12), now);
        }

        // ---- Vegeta's hair: a tall black flame standing straight up from the temples, and the deep widow's peak.
        static void VegetaHair(Graphics g, float u, double now)
        {
            float[][] tips =
            {
                new float[] { -62, 0.42f, 0.75f }, new float[] { -40, 0.66f, 0.7f }, new float[] { -18, 0.84f, 0.6f }, new float[] { 4, 0.9f, 0.5f },
                new float[] { 26, 0.78f, 0.6f }, new float[] { 48, 0.6f, 0.7f }, new float[] { 68, 0.4f, 0.75f }
            };
            float[][] peak = { new float[] { 0.5f, 0.14f }, new float[] { 0.28f, 0.06f }, new float[] { 0.12f, 0.14f }, new float[] { 0, 0.32f },
                               new float[] { -0.12f, 0.14f }, new float[] { -0.28f, 0.06f }, new float[] { -0.5f, 0.14f } };
            FlameHair(g, u, tips, peak, 0.6f, 0.46f, Color.FromArgb(48, 44, 56), Color.FromArgb(10, 8, 12), now);
        }

        // A single outline of hair: from the left temple up through wide spikes whose bases touch (each tip as its
        // angle, its length past the head and its turn upwards), down to the right temple and back along the hairline
        // (x, y in u, right to left). The edges curve in, like flames; one shape, so it shades as one.
        static void FlameHair(Graphics g, float u, float[][] tips, float[][] line, float rx, float ry, Color a, Color b, double now)
        {
            float sway = (float)Math.Sin(now / 900.0) * 0.02f;
            int n = tips.Length;
            using (GraphicsPath h = new GraphicsPath())
            {
                PointF prev = P(-0.64f * u, 0.5f * u);
                for (int i = 0; i <= n; i++)
                {
                    float va = i == 0 ? tips[0][0] - (tips[1][0] - tips[0][0]) / 2 : i == n ? tips[n - 1][0] + (tips[n - 1][0] - tips[n - 2][0]) / 2 : (tips[i - 1][0] + tips[i][0]) / 2;
                    PointF v = OnHead(va, u, rx, ry);
                    if (i == 0) h.AddLine(prev, v);
                    else
                    {
                        float[] t = tips[i - 1];
                        PointF bse = OnHead(t[0], u, rx, ry);
                        double d = t[0] * (1 - t[2]) * Math.PI / 180;
                        PointF tip = P(bse.X + ((float)Math.Sin(d) + sway) * t[1] * u, bse.Y - (float)Math.Cos(d) * t[1] * u);
                        PointF m1 = Mid(prev, bse), m2 = Mid(v, bse);
                        h.AddBezier(prev, Lerp(m1, tip, 0.35f), Lerp(m1, tip, 0.75f), tip);
                        h.AddBezier(tip, Lerp(m2, tip, 0.75f), Lerp(m2, tip, 0.35f), v);
                    }
                    prev = v;
                }
                h.AddLine(prev, P(0.64f * u, 0.5f * u));
                prev = P(0.64f * u, 0.5f * u);
                foreach (float[] q in line)
                {
                    h.AddLine(prev, P(q[0] * u, q[1] * u));
                    prev = P(q[0] * u, q[1] * u);
                }
                h.AddLine(prev, P(-0.64f * u, 0.5f * u));
                h.CloseFigure();
                RectangleF r = new RectangleF(-1.3f * u, -1.1f * u, 2.6f * u, 1.7f * u);
                Fill(g, h, r, a, b, 80f);
                Shine(g, h, new RectangleF(-0.8f * u, -0.9f * u, 1.6f * u, 1.4f * u), 60);
                Stroke(g, h, Color.FromArgb(70, 0, 0, 0), u * 0.014f);
            }
        }

        static PointF OnHead(float deg, float u, float rx, float ry)
        {
            double a = deg * Math.PI / 180;
            return P((float)Math.Sin(a) * rx * u, 0.42f * u - (float)Math.Cos(a) * ry * u);
        }

        static PointF Mid(PointF a, PointF b) { return P((a.X + b.X) / 2, (a.Y + b.Y) / 2); }
        static PointF Lerp(PointF a, PointF b, float t) { return P(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
    }

    public partial class Mascot
    {
        // Doraemon's and Totoro's wide smiles, lower than the usual small mouth: open and red when happy or talking.
        bool ClassicMouth(Graphics g, float D, float mx, float fy, Color ink, float lw, bool talking)
        {
            bool dora = Look.Kind == MascotParts.KindDoraemon;
            if (!dora && Look.Kind != MascotParts.KindTotoro) return false;
            float my = fy + D * (dora ? 0.16f : 0.1f), w = D * (dora ? 0.3f : 0.26f), h = D * (dora ? 0.14f : 0.08f);
            bool open = mood == Mood.Happy || mood == Mood.Love || talking;
            if (!open)
            {
                using (Pen p = new Pen(ink, lw))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawArc(p, mx - w / 2, my - h * 0.6f, w, h, 15, 150);
                }
                return true;
            }
            if (talking && mood == Mood.Idle) h *= (float)(0.5 + 0.5 * Math.Abs(Math.Sin(now / 110.0)));
            using (GraphicsPath m = new GraphicsPath())
            {
                m.AddArc(mx - w / 2, my - h / 2, w, h, 0, 180);
                m.CloseFigure();
                MascotParts.FillSolid(g, m, dora ? Color.FromArgb(200, 30, 40) : Color.FromArgb(70, 30, 40));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                MascotParts.Ellipse(g, Color.FromArgb(255, 120, 130), mx - w * 0.26f, my + h * 0.15f, w * 0.52f, h * 0.6f);
                if (!dora)
                    for (int i = -2; i <= 2; i++)
                        using (GraphicsPath t = new GraphicsPath())
                        {
                            float tx = mx + i * w * 0.16f;
                            t.AddPolygon(new PointF[] { new PointF(tx - w * 0.05f, my), new PointF(tx + w * 0.05f, my), new PointF(tx, my + h * 0.22f) });
                            MascotParts.FillSolid(g, t, Color.White);
                        }
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, m, Color.FromArgb(200, ink), Math.Max(1f, D * 0.008f));
            }
            return true;
        }

        // Doraemon's eyes: two tall white ovals side by side, the pupils close to the middle; Totoro's: round and white
        // with a small black pupil. Both with a dark rim and a little shine.
        bool ClassicEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            bool dora = Look.Kind == MascotParts.KindDoraemon;
            if (!dora && Look.Kind != MascotParts.KindTotoro) return false;
            float k = surprised ? 1.1f : 1f, w = D * (dora ? 0.11f : 0.13f) * k, h = D * (dora ? 0.15f : 0.13f) * k * blink;
            if (h < D * 0.02f)
            {
                MascotParts.Line(g, Color.FromArgb(230, 30, 30, 40), Math.Max(1.2f, D * 0.018f), x - w * 0.5f, y, x + w * 0.5f, y);
                return true;
            }
            RectangleF r = new RectangleF(x - w / 2, y - h / 2, w, h);
            using (GraphicsPath ep = Ellipse(r))
            {
                MascotParts.FillSolid(g, ep, Color.FromArgb(255, 255, 255));
                MascotParts.Stroke(g, ep, Color.FromArgb(220, 30, 30, 40), Math.Max(1f, D * 0.008f));
            }
            float pr = D * (dora ? 0.03f : 0.034f), inward = x < 0 ? 1 : -1;
            float px = dora ? x + inward * w * 0.18f : x, py = y + (dora ? h * 0.08f : 0);
            MascotParts.Ellipse(g, Color.FromArgb(250, 16, 16, 22), px - pr, py - pr, pr * 2, pr * 2);
            if (blink >= 0.5) MascotParts.Ellipse(g, Color.FromArgb(240, 255, 255, 255), px - pr * 0.55f, py - pr * 0.6f, pr * 0.6f, pr * 0.6f);
            return true;
        }
    }
}

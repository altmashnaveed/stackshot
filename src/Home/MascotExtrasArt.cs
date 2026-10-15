// Stackshot - The pieces of the big batch of tributes (see MascotExtras.cs): what goes behind the body (ears, tails,
// spikes, quills, wings, backpacks), what is painted on it (bellies, masks, shells, bark, pixels, visors) and what
// sits in front of the face (beaks, snouts, noses, whiskers, coins, bows, floppy ears).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        static readonly Color ExBlack = Color.FromArgb(34, 32, 40), ExBlack2 = Color.FromArgb(12, 10, 16);
        static readonly Color ExWhite = Color.FromArgb(255, 255, 255), ExWhite2 = Color.FromArgb(226, 228, 236);
        static readonly Color ExNone = Color.FromArgb(0, 0, 0, 0);

        // ---- Behind the body.
        static void ExtraBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            if (IsArcade(l.Kind)) { ArcadeBack(g, l, D, c1, c2, p); return; }
            Color e1 = Lighter(c1, 0.05);
            float earL = (float)p.EarL, earR = (float)p.EarR;
            switch (l.Kind)
            {
                case KindPsyduck:
                    // Three thin hairs on top.
                    using (Pen h = new Pen(ExBlack, Math.Max(1f, D * 0.013f)))
                    {
                        h.StartCap = LineCap.Round;
                        h.EndCap = LineCap.Round;
                        g.DrawBezier(h, P(-0.01f * D, -0.27f * D), P(-0.02f * D, -0.35f * D), P(-0.06f * D, -0.39f * D), P(-0.09f * D, -0.4f * D));
                        g.DrawBezier(h, P(0, -0.27f * D), P(0, -0.36f * D), P(0.02f * D, -0.41f * D), P(0.03f * D, -0.44f * D));
                        g.DrawBezier(h, P(0.01f * D, -0.27f * D), P(0.04f * D, -0.34f * D), P(0.08f * D, -0.37f * D), P(0.11f * D, -0.38f * D));
                    }
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(252, 218, 150), Color.FromArgb(224, 170, 90), p);
                    break;
                case KindTogepi:
                    // The crown of round spikes on its head.
                    for (int i = -2; i <= 2; i++)
                    {
                        double a = i * 30 * Math.PI / 180;
                        bool outer = Math.Abs(i) == 2;
                        float x = (float)Math.Sin(a) * (outer ? 0.3f : 0.24f) * D, y = -0.06f * D - (float)Math.Cos(a) * (outer ? 0.24f : 0.2f) * D;
                        SoftEar(g, D, x, y, i * 30, outer ? 0.13f : 0.11f, 0.8f, e1, c2, ExNone);
                    }
                    foreach (int s in Sides) Foot(g, D, s, e1, c2, p);
                    break;
                case KindMeowth:
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.22f * D, -0.23f * D, s * (30 + (s < 0 ? earL : earR) * 0.6f), 0.24f, 0.72f, ExBlack, ExBlack2, Color.FromArgb(230, 196, 146, 120));
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(200, 150, 100), Color.FromArgb(140, 96, 60), p);
                    break;
                case KindPiplup:
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(255, 196, 80), Color.FromArgb(232, 140, 30), p);
                    break;
                case KindMimikyu:
                {
                    // The cloth ears, one standing and one bent over, and the stick tail.
                    PikaEar(g, D, 1, c1, c2, 4 + earR * 0.3);
                    PikaEar(g, D, -1, c1, c2, 62 + earL * 0.3);
                    GraphicsState ts = TailSide(g, p);
                    using (Pen st = new Pen(Color.FromArgb(132, 92, 52), D * 0.035f))
                    {
                        st.LineJoin = LineJoin.Round;
                        st.StartCap = LineCap.Round;
                        st.EndCap = LineCap.Round;
                        g.DrawLines(st, new PointF[] { P(0.18f * D, 0.22f * D), P(0.31f * D, 0.14f * D), P(0.27f * D, 0.06f * D), P(0.41f * D, -0.04f * D) });
                    }
                    g.Restore(ts);
                    break;
                }
                case KindLucario:
                    // The black tassels hanging from the back of its head, then the pointed ears.
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.29f * D, -0.08f * D, 180 - s * 38, 0.2f, 0.42f, ExBlack, ExBlack2, ExNone);
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.15f * D, -0.25f * D, s * (16 + (s < 0 ? earL : earR) * 0.5f), 0.21f, 0.55f, e1, c2, Color.FromArgb(230, 30, 30, 40));
                    foreach (int s in Sides) Foot(g, D, s, ExBlack, ExBlack2, p);
                    break;
                case KindDragonite:
                {
                    // Little teal wings, the tail and the two antennae.
                    Color wing = Color.FromArgb(96, 196, 176);
                    foreach (int s in Sides)
                    {
                        GraphicsState ws = g.Save();
                        g.TranslateTransform(s * 0.3f * D, -0.02f * D);
                        g.RotateTransform((float)(s * (60 + Math.Sin(p.Now / 300.0) * 8)));
                        using (GraphicsPath w = Leaf(0.14f * D, 0.2f * D, D)) Fill(g, w, new RectangleF(-0.08f * D, -0.2f * D, 0.16f * D, 0.22f * D), Lighter(wing, 0.1), Darker(wing, 0.2), 80f);
                        g.Restore(ws);
                    }
                    { GraphicsState ts = TailSide(g, p); RoundTail(g, D, e1, c2, p.Now, 0.12f, 0.05f, 0, ExNone); g.Restore(ts); }
                    using (Pen an = new Pen(Darker(c2, 0.1), Math.Max(1f, D * 0.014f)))
                    {
                        an.StartCap = LineCap.Round;
                        an.EndCap = LineCap.Round;
                        foreach (int s in Sides)
                            g.DrawBezier(an, P(s * 0.05f * D, -0.28f * D), P(s * 0.06f * D, -0.38f * D), P(s * 0.12f * D, -0.44f * D), P(s * 0.2f * D, -0.42f * D));
                    }
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(255, 236, 186), Color.FromArgb(234, 200, 140), p);
                    break;
                }
                case KindMewtwo:
                {
                    // The tube from the back of the head, the two short horns and the long purple tail.
                    using (Pen tube = new Pen(Mac.Mix(c1, c2, 0.35), D * 0.06f))
                    {
                        tube.StartCap = LineCap.Round;
                        tube.EndCap = LineCap.Round;
                        g.DrawBezier(tube, P(0.16f * D, -0.22f * D), P(0.36f * D, -0.2f * D), P(0.4f * D, 0.02f * D), P(0.24f * D, 0.1f * D));
                    }
                    foreach (int s in Sides) SoftEar(g, D, s * 0.13f * D, -0.27f * D, s * 28, 0.1f, 1.0f, e1, c2, ExNone);
                    { GraphicsState ts = TailSide(g, p); RoundTail(g, D, Color.FromArgb(176, 128, 212), Color.FromArgb(112, 70, 160), p.Now, 0.12f, 0.08f, 0.065f, ExNone); g.Restore(ts); }
                    foreach (int s in Sides) Foot(g, D, s, e1, c2, p);
                    break;
                }
                case KindCalcifer:
                    Glow(g, 0, -0.02f * D, 1.3f * D, 1.3f * D, Color.FromArgb(255, 140, 40), (int)(120 + 30 * Math.Sin(p.Now / 120.0)));
                    break;
                case KindYoshi:
                    // The red spikes down the back of its head, on the side it walks away from.
                    {
                        GraphicsState ts = TailSide(g, p);
                        for (int i = 0; i < 3; i++)
                        {
                            double a = (40 + i * 26) * Math.PI / 180;
                            SoftEar(g, D, (float)Math.Sin(a) * 0.35f * D, -(float)Math.Cos(a) * 0.31f * D, 40 + i * 26, 0.14f, 0.9f, Color.FromArgb(244, 76, 64), Color.FromArgb(190, 30, 30), ExNone);
                        }
                        g.Restore(ts);
                    }
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(244, 120, 64), Color.FromArgb(204, 64, 30), p);
                    break;
                case KindCreeper:
                    foreach (int s in Sides)
                    {
                        RectangleF f = new RectangleF(s * 0.13f * D - 0.07f * D, 0.27f * D, 0.14f * D, 0.09f * D);
                        using (GraphicsPath fp = new GraphicsPath())
                        {
                            fp.AddRectangle(f);
                            Fill(g, fp, f, Darker(c1, 0.1), Darker(c2, 0.25), 90f);
                        }
                    }
                    break;
                case KindCrewmate:
                {
                    // The backpack on its left.
                    RectangleF bp = new RectangleF(-0.4f * D, -0.12f * D, 0.2f * D, 0.32f * D);
                    using (GraphicsPath bpp = Theme.Round(bp, 0.06f * D))
                    {
                        Fill(g, bpp, bp, Darker(c1, 0.12), Darker(c2, 0.3), 90f);
                        Stroke(g, bpp, Color.FromArgb(230, 20, 18, 26), D * 0.022f);
                    }
                    break;
                }
                case KindSnoopy:
                case KindPochacco:
                    foreach (int s in Sides) Foot(g, D, s, ExWhite, ExWhite2, p);
                    break;
                case KindKitty:
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.25f * D, -0.21f * D, s * (32 + (s < 0 ? earL : earR) * 0.5f), 0.15f, 0.92f, e1, c2, ExNone);
                    foreach (int s in Sides) Foot(g, D, s, ExWhite, ExWhite2, p);
                    break;
                case KindGroot:
                {
                    // Twigs on top, each with a fresh leaf.
                    Color twig = Darker(c2, 0.1), leaf1 = Color.FromArgb(140, 210, 90), leaf2 = Color.FromArgb(60, 150, 60);
                    float[][] tw = { new float[] { -0.06f, -0.28f, -0.12f, -0.4f, -30 }, new float[] { 0.05f, -0.29f, 0.1f, -0.42f, 25 }, new float[] { 0.13f, -0.26f, 0.22f, -0.34f, 60 } };
                    foreach (float[] t in tw)
                    {
                        Line(g, twig, Math.Max(1f, D * 0.016f), t[0] * D, t[1] * D, t[2] * D, t[3] * D);
                        GraphicsState ls = g.Save();
                        g.TranslateTransform(t[2] * D, t[3] * D);
                        g.RotateTransform(t[4] + (float)Math.Sin(p.Now / 700.0 + t[0] * 10) * 6);
                        using (GraphicsPath lf = Leaf(0.06f * D, 0.09f * D, D)) Fill(g, lf, new RectangleF(-0.04f * D, -0.09f * D, 0.08f * D, 0.1f * D), leaf1, leaf2, 80f);
                        g.Restore(ls);
                    }
                    foreach (int s in Sides) Foot(g, D, s, e1, c2, p);
                    break;
                }
                case KindRilakkuma:
                    foreach (int s in Sides)
                    {
                        float ex = s * 0.24f * D, ey = -0.25f * D, r = 0.085f * D;
                        RectangleF er = new RectangleF(ex - r, ey - r, r * 2, r * 2);
                        using (GraphicsPath ep = new GraphicsPath())
                        {
                            ep.AddEllipse(er);
                            Fill(g, ep, er, e1, c2, 80f);
                        }
                        Ellipse(g, Color.FromArgb(250, 244, 226), ex - r * 0.5f, ey - r * 0.45f, r, r);
                    }
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(252, 240, 214), Color.FromArgb(226, 206, 170), p);
                    break;
                case KindPusheen:
                {
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.25f * D, -0.16f * D, s * (30 + (s < 0 ? earL : earR) * 0.5f), 0.13f, 0.95f, e1, c2, ExNone);
                    // A thick striped tail behind.
                    GraphicsState ts = TailSide(g, p);
                    RoundTail(g, D, e1, c2, p.Now, 0.1f, 0.085f, 0, Color.FromArgb(150, 92, 88, 100));
                    g.Restore(ts);
                    break;
                }
                case KindBlinky:
                case KindNoFace:
                case KindDitto:
                case KindKodama:
                case KindDqSlime:
                    break;
            }
        }

        // A tail that ends round instead of cut off: a bulb on Mewtwo, a round tip elsewhere, and tabby bands if given.
        static void RoundTail(Graphics g, float D, Color a, Color b, double now, float w0, float w1, float bulb, Color band)
        {
            float angle;
            PointF tip = TaperTail(g, D, a, b, now, w0, w1, out angle);
            float r = Math.Max(w1 / 2, bulb) * D;
            RectangleF c = new RectangleF(tip.X - r, tip.Y - r, r * 2, r * 2);
            using (GraphicsPath cp = new GraphicsPath())
            {
                cp.AddEllipse(c);
                // The same gradient box as the tail, so the round end matches it.
                Fill(g, cp, new RectangleF(0.04f * D, 0.12f * D, 0.6f * D, 0.32f * D), a, b, 70f);
            }
            if (band.A == 0) return;
            double t = angle * Math.PI / 180;
            float dx = (float)Math.Cos(t), dy = (float)Math.Sin(t), hw = w1 * D * 0.5f;
            using (Pen p = new Pen(band, D * 0.022f))
                for (int i = 1; i <= 2; i++)
                {
                    float bx = tip.X - dx * i * 0.06f * D, by = tip.Y - dy * i * 0.06f * D;
                    g.DrawLine(p, bx + dy * hw, by - dx * hw, bx - dy * hw, by + dx * hw);
                }
        }

        // ---- On the body (clipped to it, before the face).
        static void ExtraBody(Graphics g, MascotLook l, float D, GraphicsPath body, Color c1, Color c2, MascotPose p)
        {
            if (IsArcade(l.Kind)) { ArcadeBody(g, l, D, body, c1, c2, p); return; }
            Geo geo = GeoFor(l.Kind);
            float fy = geo.FaceY * D, gap = geo.EyeGap * D;
            Region old = g.Clip;
            g.SetClip(body, CombineMode.Intersect);
            switch (l.Kind)
            {
                case KindTogepi:
                {
                    // The eggshell from the middle down, with its zigzag top and red and blue triangles.
                    PointF[] shell = new PointF[14];
                    for (int i = 0; i <= 10; i++) shell[i] = P((-0.5f + i * 0.1f) * D, (i % 2 == 0 ? 0.0f : -0.05f) * D);
                    shell[11] = P(0.5f * D, 0.5f * D);
                    shell[12] = P(-0.5f * D, 0.5f * D);
                    shell[13] = shell[0];
                    using (GraphicsPath sp = new GraphicsPath())
                    {
                        sp.AddPolygon(shell);
                        Fill(g, sp, new RectangleF(-0.5f * D, -0.05f * D, D, 0.4f * D), ExWhite, ExWhite2, 90f);
                        Stroke(g, sp, Color.FromArgb(90, 150, 150, 170), D * 0.008f);
                    }
                    float[][] tri = { new float[] { -0.18f, 0.15f, 0 }, new float[] { 0.14f, 0.11f, 1 }, new float[] { -0.04f, 0.27f, 1 }, new float[] { 0.21f, 0.25f, 0 } };
                    foreach (float[] t in tri)
                        using (GraphicsPath tp = Poly(P((t[0] - 0.04f) * D, (t[1] + 0.03f) * D), P(t[0] * D, (t[1] - 0.04f) * D), P((t[0] + 0.04f) * D, (t[1] + 0.03f) * D)))
                            FillSolid(g, tp, t[2] == 0 ? Color.FromArgb(232, 70, 70) : Color.FromArgb(70, 130, 230));
                    break;
                }
                case KindPiplup:
                {
                    // Light blue from the shoulders down, white patches on the cheeks.
                    RectangleF low = new RectangleF(-0.5f * D, 0.06f * D, D, 0.5f * D);
                    using (GraphicsPath lp = new GraphicsPath())
                    {
                        lp.AddEllipse(-0.42f * D, 0.04f * D, 0.84f * D, 0.6f * D);
                        Fill(g, lp, low, Color.FromArgb(170, 220, 246), Color.FromArgb(110, 176, 226), 90f);
                    }
                    foreach (int s in Sides) Ellipse(g, Color.FromArgb(240, 255, 255, 255), s * (gap + 0.04f * D) - 0.05f * D, fy + 0.06f * D, 0.1f * D, 0.07f * D);
                    for (int i = 0; i < 2; i++) Ellipse(g, Color.FromArgb(230, 255, 255, 255), -0.02f * D, (0.14f + i * 0.07f) * D, 0.04f * D, 0.04f * D);
                    break;
                }
                case KindLucario:
                {
                    // Black legs, the cream chest fur and the black mask across the eyes.
                    using (SolidBrush b = new SolidBrush(ExBlack)) g.FillRectangle(b, -0.5f * D, 0.24f * D, D, 0.2f * D);
                    RectangleF chest = new RectangleF(-0.24f * D, 0.03f * D, 0.48f * D, 0.24f * D);
                    using (GraphicsPath cp = new GraphicsPath())
                    {
                        cp.AddEllipse(chest);
                        Fill(g, cp, chest, Color.FromArgb(246, 222, 150), Color.FromArgb(216, 182, 100), 90f);
                    }
                    PointF[] mask =
                    {
                        P(-0.37f * D, fy - 0.02f * D), P(-0.2f * D, fy - 0.075f * D), P(-0.05f * D, fy - 0.045f * D), P(0, fy - 0.075f * D), P(0.05f * D, fy - 0.045f * D),
                        P(0.2f * D, fy - 0.075f * D), P(0.37f * D, fy - 0.02f * D), P(0.3f * D, fy + 0.065f * D), P(0.15f * D, fy + 0.07f * D), P(0.04f * D, fy + 0.035f * D),
                        P(0, fy + 0.05f * D), P(-0.04f * D, fy + 0.035f * D), P(-0.15f * D, fy + 0.07f * D), P(-0.3f * D, fy + 0.065f * D)
                    };
                    using (GraphicsPath mp = new GraphicsPath())
                    {
                        mp.AddClosedCurve(mask, 0.3f);
                        FillSolid(g, mp, ExBlack);
                    }
                    break;
                }
                case KindDragonite:
                {
                    // The cream belly with its stripes.
                    RectangleF belly = new RectangleF(-0.22f * D, 0.02f * D, 0.44f * D, 0.38f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, Color.FromArgb(255, 238, 186), Color.FromArgb(236, 206, 140), 90f);
                        Region inner = g.Clip;
                        g.SetClip(bp, CombineMode.Intersect);
                        for (int i = 0; i < 4; i++) Line(g, Color.FromArgb(110, 190, 140, 80), D * 0.009f, belly.X, (0.11f + i * 0.065f) * D, belly.Right, (0.11f + i * 0.065f) * D);
                        g.Clip = inner;
                        inner.Dispose();
                    }
                    break;
                }
                case KindMewtwo:
                {
                    RectangleF belly = new RectangleF(-0.18f * D, 0.08f * D, 0.36f * D, 0.32f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, Color.FromArgb(176, 128, 212), Color.FromArgb(120, 80, 170), 90f);
                    }
                    break;
                }
                case KindNoFace:
                {
                    // The white mask with the purple marks above and below the eyes.
                    RectangleF m = new RectangleF(-0.17f * D, -0.31f * D, 0.34f * D, 0.38f * D);
                    using (GraphicsPath mp = new GraphicsPath())
                    {
                        mp.AddEllipse(m);
                        Fill(g, mp, m, Color.FromArgb(252, 252, 250), Color.FromArgb(218, 216, 222), 90f);
                        Stroke(g, mp, Color.FromArgb(70, 120, 110, 130), D * 0.006f);
                    }
                    Color mark = Color.FromArgb(150, 92, 172);
                    foreach (int s in Sides)
                    {
                        float x = s * gap;
                        using (GraphicsPath up = Poly(P(x - 0.022f * D, fy - 0.032f * D), P(x, fy - 0.1f * D), P(x + 0.022f * D, fy - 0.032f * D))) FillSolid(g, up, mark);
                        using (GraphicsPath dn = Poly(P(x - 0.026f * D, fy + 0.032f * D), P(x, fy + 0.11f * D), P(x + 0.026f * D, fy + 0.032f * D))) FillSolid(g, dn, mark);
                    }
                    break;
                }
                case KindCalcifer:
                    // A hot yellow core.
                    Glow(g, 0, 0.06f * D, 0.5f * D, 0.46f * D, Color.FromArgb(255, 236, 120), 200);
                    break;
                case KindYoshi:
                    // White jaw and belly.
                    Ellipse(g, Color.FromArgb(250, 255, 255, 255), -0.25f * D, 0.0f * D, 0.5f * D, 0.22f * D);
                    Ellipse(g, Color.FromArgb(250, 255, 255, 255), -0.19f * D, 0.1f * D, 0.38f * D, 0.3f * D);
                    break;
                case KindCreeper:
                {
                    // Pixel texture, then the face: square eyes and the mouth.
                    float W = geo.HeadW * D, top = geo.Top * D, s = W / 8;
                    for (int r = 0; r < 8; r++)
                        for (int c = 0; c < 8; c++)
                        {
                            int h = (r * 7 + c * 13 + r * c * 3) % 5;
                            if (h > 2) continue;
                            Color px = h == 0 ? Color.FromArgb(60, 255, 255, 255) : h == 1 ? Color.FromArgb(50, 0, 40, 0) : Color.FromArgb(30, 200, 255, 200);
                            using (SolidBrush b = new SolidBrush(px)) g.FillRectangle(b, -W / 2 + c * s, top + r * s, s, s);
                        }
                    int[][] face =
                    {
                        new int[] { 1, 2, 2, 2 }, new int[] { 5, 2, 2, 2 },
                        new int[] { 3, 4, 2, 1 }, new int[] { 2, 5, 4, 2 }, new int[] { 2, 7, 1, 1 }, new int[] { 5, 7, 1, 1 }
                    };
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 30, 24)))
                        foreach (int[] f in face) g.FillRectangle(b, -W / 2 + f[0] * s, top + f[1] * s, f[2] * s, f[3] * s);
                    break;
                }
                case KindCrewmate:
                {
                    // The visor, with its shine.
                    RectangleF v = new RectangleF(-0.18f * D, -0.21f * D, 0.42f * D, 0.17f * D);
                    using (GraphicsPath vp = Theme.Round(v, 0.085f * D))
                    {
                        Fill(g, vp, v, Color.FromArgb(178, 228, 242), Color.FromArgb(92, 146, 186), 90f);
                        Stroke(g, vp, Color.FromArgb(240, 20, 18, 26), D * 0.022f);
                    }
                    RectangleF hl = new RectangleF(0.06f * D, -0.18f * D, 0.12f * D, 0.04f * D);
                    using (GraphicsPath hp = Theme.Round(hl, 0.02f * D)) FillSolid(g, hp, Color.FromArgb(235, 255, 255, 255));
                    break;
                }
                case KindGroot:
                {
                    // Bark: soft darker grain running down.
                    Color grain = Color.FromArgb(80, Darker(c2, 0.3));
                    using (Pen bark = new Pen(grain, Math.Max(1f, D * 0.01f)))
                        for (int i = -3; i <= 3; i++)
                        {
                            float x = i * 0.1f * D;
                            g.DrawBezier(bark, P(x, -0.32f * D), P(x + 0.03f * D, -0.12f * D), P(x - 0.03f * D, 0.1f * D), P(x + 0.01f * D, 0.34f * D));
                        }
                    break;
                }
                case KindRilakkuma:
                    Ellipse(g, Color.FromArgb(252, 244, 226), -0.11f * D, fy + 0.025f * D, 0.22f * D, 0.14f * D);
                    break;
                case KindPusheen:
                    // Tabby stripes on the forehead and the back.
                    using (Pen stripe = new Pen(Color.FromArgb(150, 92, 88, 100), D * 0.024f))
                    {
                        stripe.StartCap = LineCap.Round;
                        stripe.EndCap = LineCap.Round;
                        for (int i = -1; i <= 1; i++) g.DrawLine(stripe, i * 0.05f * D, -0.21f * D, i * 0.05f * D, -0.15f * D);
                        foreach (int s in Sides)
                            for (int i = 0; i < 2; i++) g.DrawLine(stripe, s * 0.42f * D, (-0.02f + i * 0.07f) * D, s * 0.34f * D, (0.0f + i * 0.07f) * D);
                    }
                    break;
            }
            g.Clip = old;
            old.Dispose();
        }

        // ---- In front of the face.
        static void ExtraDetails(Graphics g, MascotLook l, Geo geo, float D, MascotPose p)
        {
            if (IsArcade(l.Kind)) { ArcadeDetails(g, l, geo, D, p); return; }
            float fy = geo.FaceY * D, gap = geo.EyeGap * D;
            switch (l.Kind)
            {
                case KindPsyduck:
                {
                    // The wide flat beak with its seam and nostrils.
                    RectangleF bk = new RectangleF(-0.15f * D, fy + 0.04f * D, 0.3f * D, 0.13f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(bk);
                        Fill(g, bp, bk, Color.FromArgb(255, 236, 188), Color.FromArgb(238, 196, 126), 90f);
                        Stroke(g, bp, Color.FromArgb(120, 170, 120, 60), D * 0.008f);
                    }
                    using (Pen s = new Pen(Color.FromArgb(150, 170, 120, 60), Math.Max(1f, D * 0.008f))) g.DrawArc(s, bk.X + bk.Width * 0.1f, bk.Y + bk.Height * 0.2f, bk.Width * 0.8f, bk.Height * 0.6f, 15, 150);
                    Ellipse(g, Color.FromArgb(150, 120, 80, 40), -0.035f * D, fy + 0.07f * D, 0.014f * D, 0.01f * D);
                    Ellipse(g, Color.FromArgb(150, 120, 80, 40), 0.021f * D, fy + 0.07f * D, 0.014f * D, 0.01f * D);
                    break;
                }
                case KindMeowth:
                {
                    // The gold charm on its forehead and long whiskers.
                    RectangleF coin = new RectangleF(-0.055f * D, geo.Top * D + 0.03f * D, 0.11f * D, 0.12f * D);
                    using (GraphicsPath cp = new GraphicsPath())
                    {
                        cp.AddEllipse(coin);
                        Fill(g, cp, coin, Gold1, Gold2, 90f);
                        Stroke(g, cp, Color.FromArgb(160, 150, 96, 10), D * 0.007f);
                    }
                    Ellipse(g, Color.FromArgb(220, 176, 112, 12), -0.018f * D, coin.Y + 0.025f * D, 0.036f * D, 0.07f * D);
                    Ellipse(g, Color.FromArgb(200, 255, 255, 230), coin.X + 0.02f * D, coin.Y + 0.02f * D, 0.025f * D, 0.02f * D);
                    foreach (int s in Sides)
                        for (int i = -1; i <= 1; i++)
                            Line(g, Color.FromArgb(170, ExBlack), D * 0.007f, s * 0.14f * D, fy + 0.09f * D + i * 0.02f * D, s * 0.44f * D, fy + 0.07f * D + i * 0.05f * D);
                    break;
                }
                case KindPiplup:
                {
                    // The yellow beak, its rim rising in three points between the eyes.
                    using (GraphicsPath cr = Poly(P(-0.05f * D, fy + 0.045f * D), P(-0.045f * D, fy - 0.03f * D), P(-0.02f * D, fy + 0.005f * D), P(0, fy - 0.05f * D),
                                                  P(0.02f * D, fy + 0.005f * D), P(0.045f * D, fy - 0.03f * D), P(0.05f * D, fy + 0.045f * D)))
                        Fill(g, cr, new RectangleF(-0.05f * D, fy - 0.05f * D, 0.1f * D, 0.1f * D), Color.FromArgb(255, 220, 90), Color.FromArgb(236, 170, 40), 90f);
                    using (GraphicsPath bk = new GraphicsPath())
                    {
                        bk.AddBezier(P(-0.06f * D, fy + 0.04f * D), P(-0.05f * D, fy + 0.1f * D), P(-0.01f * D, fy + 0.13f * D), P(0, fy + 0.13f * D));
                        bk.AddBezier(P(0, fy + 0.13f * D), P(0.01f * D, fy + 0.13f * D), P(0.05f * D, fy + 0.1f * D), P(0.06f * D, fy + 0.04f * D));
                        bk.CloseFigure();
                        Fill(g, bk, new RectangleF(-0.06f * D, fy + 0.04f * D, 0.12f * D, 0.09f * D), Color.FromArgb(255, 214, 80), Color.FromArgb(232, 150, 30), 90f);
                        Stroke(g, bk, Color.FromArgb(120, 170, 100, 20), D * 0.007f);
                    }
                    break;
                }
                case KindMimikyu:
                    // Red cheeks painted on.
                    foreach (int s in Sides)
                    {
                        float cx = s * (gap + 0.1f * D), cy = fy + 0.07f * D, r = 0.045f * D;
                        Ellipse(g, Color.FromArgb(236, 70, 60), cx - r, cy - r, r * 2, r * 2);
                    }
                    break;
                case KindLucario:
                {
                    // The spike on its chest and a small black nose.
                    using (GraphicsPath sp = Poly(P(-0.035f * D, 0.16f * D), P(0, 0.08f * D), P(0.035f * D, 0.16f * D)))
                    {
                        Fill(g, sp, new RectangleF(-0.035f * D, 0.08f * D, 0.07f * D, 0.08f * D), Color.FromArgb(250, 250, 255), Color.FromArgb(196, 200, 214), 90f);
                        Stroke(g, sp, Color.FromArgb(90, 80, 80, 100), D * 0.006f);
                    }
                    Ellipse(g, ExBlack, -0.02f * D, fy + 0.07f * D, 0.04f * D, 0.024f * D);
                    break;
                }
                case KindYoshi:
                {
                    // The big round snout and its nostrils.
                    RectangleF sn = new RectangleF(-0.21f * D, fy + 0.05f * D, 0.42f * D, 0.21f * D);
                    using (GraphicsPath sp = new GraphicsPath())
                    {
                        sp.AddEllipse(sn);
                        Color c1 = Colors[l.Color, 0], c2 = Colors[l.Color, 1];
                        Fill(g, sp, sn, Lighter(c1, 0.1), Mac.Mix(c1, c2, 0.5), 90f);
                        Shine(g, sp, sn, 90);
                        Stroke(g, sp, Color.FromArgb(70, Darker(c2, 0.4)), D * 0.007f);
                    }
                    Ellipse(g, Color.FromArgb(170, 30, 70, 30), -0.06f * D, fy + 0.1f * D, 0.025f * D, 0.035f * D);
                    Ellipse(g, Color.FromArgb(170, 30, 70, 30), 0.035f * D, fy + 0.1f * D, 0.025f * D, 0.035f * D);
                    break;
                }
                case KindCrewmate:
                    // Its thick dark outline.
                    using (GraphicsPath bp = ExtraPath(l.Kind, geo, D, p.Now)) Stroke(g, bp, Color.FromArgb(240, 20, 18, 26), D * 0.022f);
                    break;
                case KindSnoopy:
                case KindPochacco:
                {
                    // Long black ears hanging down the sides of the head, the black nose and, on Snoopy, the red collar.
                    bool snoopy = l.Kind == KindSnoopy;
                    foreach (int s in Sides)
                        SoftEar(g, D, s * (snoopy ? 0.25f : 0.27f) * D, -0.2f * D, 180 - s * (snoopy ? 22 : 12), snoopy ? 0.3f : 0.25f, snoopy ? 0.48f : 0.6f, ExBlack, ExBlack2, ExNone);
                    float nw = (snoopy ? 0.1f : 0.045f) * D, nh = (snoopy ? 0.07f : 0.03f) * D, ny = fy + (snoopy ? 0.03f : 0.05f) * D;
                    RectangleF n = new RectangleF(-nw / 2, ny, nw, nh);
                    using (GraphicsPath np = new GraphicsPath())
                    {
                        np.AddEllipse(n);
                        Fill(g, np, n, Color.FromArgb(70, 66, 80), ExBlack2, 90f);
                    }
                    Ellipse(g, Color.FromArgb(150, 255, 255, 255), n.X + nw * 0.2f, n.Y + nh * 0.15f, nw * 0.3f, nh * 0.3f);
                    if (snoopy)
                    {
                        // The collar wraps round the body: a band curving down with it, cut at the body's edges.
                        using (GraphicsPath body = BodyPath(l.Kind, geo, D, p.Now))
                        {
                            Region old = g.Clip;
                            g.SetClip(body, CombineMode.Intersect);
                            RectangleF arc = new RectangleF(-0.42f * D, 0.06f * D, 0.84f * D, 0.16f * D);
                            using (Pen dark = new Pen(Color.FromArgb(150, 20, 26), 0.05f * D)) g.DrawArc(dark, arc, 0, 180);
                            using (Pen red = new Pen(Color.FromArgb(232, 44, 52), 0.036f * D)) g.DrawArc(red, RectangleF.Inflate(arc, 0, -0.004f * D), 0, 180);
                            using (Pen hl = new Pen(Color.FromArgb(110, 255, 255, 255), 0.008f * D)) g.DrawArc(hl, RectangleF.Inflate(arc, 0, -0.016f * D), 30, 120);
                            g.Clip = old;
                            old.Dispose();
                        }
                    }
                    break;
                }
                case KindKitty:
                {
                    // The red bow by her left ear, the yellow nose and whiskers; no mouth.
                    GraphicsState st = g.Save();
                    g.TranslateTransform(0.24f * D, -0.25f * D);
                    g.RotateTransform(18);
                    BowTie(g, D * 1.15f, 0, Color.FromArgb(242, 44, 66), Color.FromArgb(180, 18, 40));
                    g.Restore(st);
                    Ellipse(g, Color.FromArgb(255, 204, 40), -0.026f * D, fy + 0.05f * D, 0.052f * D, 0.036f * D);
                    foreach (int s in Sides)
                        for (int i = 0; i < 3; i++)
                            Line(g, Color.FromArgb(220, ExBlack), D * 0.01f, s * 0.26f * D, fy + 0.03f * D + i * 0.035f * D, s * 0.42f * D, fy + 0.0f * D + i * 0.05f * D);
                    break;
                }
                case KindRilakkuma:
                    Ellipse(g, Color.FromArgb(240, 70, 46, 30), -0.02f * D, fy + 0.05f * D, 0.04f * D, 0.028f * D);
                    break;
            }
        }
    }
}

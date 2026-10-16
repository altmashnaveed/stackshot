// Stackshot - Series characters drawn as full figures, true to their originals: a big anime-proportioned head on a
// small body in its clothes, the character's own hair, eyes in its series' style, marks, masks and props. A shared
// base (head, ears, body, limbs, eyes and mouths) and a table of figures, each with its own drawing on top
// (MascotFiguresArt*.cs). Fan tributes, not affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int FigFirst = 80;

        // Eye styles: big round anime eyes, sharp narrow ones, half-lidded, small dots, or none (a mask or visor draws them).
        public const int EyeRound = 0, EyeSharp = 1, EyeLidded = 2, EyeDot = 3, EyeNone = 4;
        // Mouths: a small smile, a big grin, none (hidden), a serious line, a sly smirk.
        public const int MouthSmile = 0, MouthGrin = 1, MouthNone = 2, MouthLine = 3, MouthSmirk = 4;

        public delegate void FigDraw(Graphics g, float D, MascotPose p, Color skin, Color skin2);

        public class Fig
        {
            public FigDraw Back, Front, OnHead;   // OnHead: painted on the head itself (masks), clipped to it, before the eyes
            public Color Iris = Color.FromArgb(40, 36, 44);
            public int Eyes, Mouth;
            public bool Blush = true, Ears = true;
            public Color Hands = Color.Empty;   // gloves, when the hands are not bare
        }

        static Fig[] figs;
        static Fig[] Figs { get { if (figs == null) figs = BuildFigs(); return figs; } }

        public static bool IsFigure(int kind) { return kind >= FigFirst && kind < FigFirst + Figs.Length; }
        public static Fig FigOf(int kind) { return IsFigure(kind) ? Figs[kind - FigFirst] : null; }

        static Fig F(FigDraw back, FigDraw front, Color iris, int eyes, int mouth)
        {
            Fig f = new Fig();
            f.Back = back; f.Front = front; f.Iris = iris; f.Eyes = eyes; f.Mouth = mouth;
            return f;
        }

        static readonly Color FInk = Color.FromArgb(32, 24, 30);

        // A clean anime line around a shape.
        static void FigLine(Graphics g, GraphicsPath p, float D) { Stroke(g, p, Color.FromArgb(235, FInk), Math.Max(1.2f, D * 0.014f)); }

        static void FigShape(Graphics g, GraphicsPath p, Color a, Color b, float D)
        {
            Fill(g, p, p.GetBounds(), a, b, 90f);
            FigLine(g, p, D);
        }

        // A soft cel shadow on the right side of a shape.
        static void FigShade(Graphics g, GraphicsPath p, Color c, int alpha)
        {
            RectangleF r = p.GetBounds();
            Region old = g.Clip;
            g.SetClip(p, CombineMode.Intersect);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, c))) g.FillEllipse(b, r.X + r.Width * 0.62f, r.Y - r.Height * 0.1f, r.Width * 0.8f, r.Height * 1.3f);
            g.Clip = old;
            old.Dispose();
        }

        static void FLimb(Graphics g, float D, Color c, float w, float x1, float y1, float x2, float y2)
        {
            using (Pen ink = new Pen(FInk, (w + 0.022f) * D))
            using (Pen fill = new Pen(c, w * D))
            {
                ink.StartCap = ink.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawLine(ink, x1 * D, y1 * D, x2 * D, y2 * D);
                g.DrawLine(fill, x1 * D, y1 * D, x2 * D, y2 * D);
            }
        }

        static void FShoe(Graphics g, float D, float x, float y, Color c)
        {
            RectangleF r = new RectangleF((x - 0.05f) * D, (y - 0.025f) * D, 0.1f * D, 0.05f * D);
            using (GraphicsPath sp = new GraphicsPath())
            {
                sp.AddEllipse(r);
                FigShape(g, sp, Lighter(c, 0.12), c, D);
            }
        }

        // ---- Geometry and the head: rounded on top, full cheeks, a small chin.
        static Geo FigureGeo(int kind) { return G(0.52f, -0.34f, -0.06f, 0.1f, 0.14f, 0.4f, 0.14f, 0.22f, 0.21f, 0.52f); }

        static GraphicsPath FigurePath(float D)
        {
            return KCurve(D, 0.5f, 0, -0.34f, 0.19f, -0.32f, 0.26f, -0.18f, 0.25f, -0.04f, 0.19f, 0.07f, 0.08f, 0.13f, 0, 0.14f,
                          -0.08f, 0.13f, -0.19f, 0.07f, -0.25f, -0.04f, -0.26f, -0.18f, -0.19f, -0.32f);
        }

        // ---- The clothed body: what each figure wears.
        public class FigClothes
        {
            public Color Top, Sleeve, Bottom, Legs, Shoes;
            public bool Shorts, BareArms;
            public Color Neck = Color.Empty;   // when a collar or mask covers it
        }

        static FigClothes Wear(Color top, Color sleeve, Color bottom, Color shoes)
        {
            FigClothes o = new FigClothes();
            o.Top = top; o.Sleeve = sleeve; o.Bottom = bottom; o.Legs = bottom; o.Shoes = shoes;
            return o;
        }

        // The neck, legs and shoes, the arms, the trousers or shorts and the top, in that order (back to front).
        static void FigBody(Graphics g, float D, MascotPose p, Color skin, Color skin2, FigClothes o)
        {
            float bob = (float)Math.Sin(p.Now / 2600.0 * Math.PI * 2) * 0.004f;
            using (GraphicsPath nk = KPoly(D, -0.035f, 0.09f, 0.035f, 0.09f, 0.04f, 0.14f, -0.04f, 0.14f)) FigShape(g, nk, o.Neck.IsEmpty ? skin : o.Neck, o.Neck.IsEmpty ? skin2 : Darker(o.Neck, 0.2), D);
            Color legs = o.Shorts ? skin : o.Legs;
            foreach (int s in Sides)
            {
                PointF ft = Stride(p, s);
                FLimb(g, D, legs, 0.036f, s * 0.045f, 0.27f, s * 0.05f + ft.X, 0.33f + ft.Y);
                FShoe(g, D, s * 0.06f + ft.X, 0.345f + ft.Y, o.Shoes);
            }
            Color arm = o.BareArms ? skin : o.Sleeve;
            FLimb(g, D, arm, 0.045f, -0.09f, 0.15f, -0.19f, 0.195f + bob);
            FLimb(g, D, arm, 0.045f, 0.09f, 0.15f, 0.19f, 0.195f + bob);
            using (GraphicsPath pants = Theme.Round(new RectangleF(-0.1f * D, 0.22f * D, 0.2f * D, (o.Shorts ? 0.07f : 0.08f) * D), 0.02f * D))
            {
                FigShape(g, pants, Lighter(o.Bottom, 0.1), o.Bottom, D);
                FigShade(g, pants, Darker(o.Bottom, 0.4), 70);
            }
            using (GraphicsPath top = KPoly(D, -0.1f, 0.11f, 0.1f, 0.11f, 0.12f, 0.25f, -0.12f, 0.25f))
            {
                FigShape(g, top, Lighter(o.Top, 0.08), Darker(o.Top, 0.12), D);
                FigShade(g, top, Darker(o.Top, 0.45), 70);
            }
        }

        static void FigBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            Fig f = FigOf(l.Kind);
            if (f.Ears)
                foreach (int s in Sides)
                    using (GraphicsPath e = new GraphicsPath())
                    {
                        e.AddEllipse(s * 0.255f * D - 0.03f * D, -0.09f * D, 0.06f * D, 0.09f * D);
                        FigShape(g, e, c1, c2, D);
                    }
            if (f.Back != null) f.Back(g, D, p, c1, c2);
        }

        static void FigDetails(Graphics g, MascotLook l, Geo geo, float D, MascotPose p)
        {
            Fig f = FigOf(l.Kind);
            if (f.Front != null) f.Front(g, D, p, Colors[l.Color, 0], Colors[l.Color, 1]);
        }

        static void FigOnHead(Graphics g, MascotLook l, float D, MascotPose p, Color c1, Color c2)
        {
            Fig f = FigOf(l.Kind);
            if (f.OnHead != null) f.OnHead(g, D, p, c1, c2);
        }

        // ---- Hair helpers.
        // A spiky mass of hair: from the left temple up through spikes (angle from straight up, length past the head, and
        // how far each turns upward) to the right temple, then back along the hairline (x, y pairs, right to left).
        static GraphicsPath SpikeHair(float D, float cy, float rx, float ry, float[][] tips, float[][] line)
        {
            GraphicsPath h = new GraphicsPath();
            int n = tips.Length;
            PointF prev = P(-rx * D, cy * D + ry * 0.35f * D);
            for (int i = 0; i <= n; i++)
            {
                float va = i == 0 ? tips[0][0] - (tips[1][0] - tips[0][0]) / 2 : i == n ? tips[n - 1][0] + (tips[n - 1][0] - tips[n - 2][0]) / 2 : (tips[i - 1][0] + tips[i][0]) / 2;
                PointF v = OnOval(va, cy, rx, ry, D);
                if (i == 0) h.AddLine(prev, v);
                else
                {
                    float[] t = tips[i - 1];
                    PointF b = OnOval(t[0], cy, rx, ry, D);
                    double d = (t[0] * (1 - t[2]) + (t.Length > 3 ? t[3] : 0)) * Math.PI / 180;   // t[3]: a lean to one side
                    PointF tip = P(b.X + (float)Math.Sin(d) * t[1] * D, b.Y - (float)Math.Cos(d) * t[1] * D);
                    h.AddBezier(prev, Mix(prev, b, tip, 0.35f), Mix(prev, b, tip, 0.75f), tip);
                    h.AddBezier(tip, Mix(v, b, tip, 0.75f), Mix(v, b, tip, 0.35f), v);
                }
                prev = v;
            }
            PointF rt = P(rx * D, cy * D + ry * 0.35f * D);
            h.AddLine(prev, rt);
            prev = rt;
            foreach (float[] q in line)
            {
                PointF pt = P(q[0] * D, q[1] * D);
                h.AddLine(prev, pt);
                prev = pt;
            }
            h.CloseFigure();
            return h;
        }

        static PointF OnOval(float deg, float cy, float rx, float ry, float D)
        {
            double a = deg * Math.PI / 180;
            return P((float)Math.Sin(a) * rx * D, (cy - (float)Math.Cos(a) * ry) * D);
        }

        // A point part-way from the middle of (a, b) towards c, so spike edges curve in like drawn locks.
        static PointF Mix(PointF a, PointF b, PointF c, float t)
        {
            float mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            return P(mx + (c.X - mx) * t, my + (c.Y - my) * t);
        }

        static void HairShape(Graphics g, GraphicsPath h, Color a, Color b, float D)
        {
            FigShape(g, h, a, b, D);
            FigShade(g, h, Darker(b, 0.35), 80);
            // A band of shine across the top, as anime hair has.
            RectangleF r = h.GetBounds();
            Region old = g.Clip;
            g.SetClip(h, CombineMode.Intersect);
            using (Pen sh = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, D * 0.012f)))
            {
                sh.StartCap = sh.EndCap = LineCap.Round;
                g.DrawArc(sh, r.X + r.Width * 0.2f, r.Y + r.Height * 0.18f, r.Width * 0.6f, r.Height * 0.5f, 200, 60);
            }
            g.Clip = old;
            old.Dispose();
        }

        // A ninja forehead protector: the blue cloth band and the metal plate with the Leaf's swirl.
        static void Headband(Graphics g, float D, float y, float tilt, float plateX) { Headband(g, D, y, tilt, plateX, 1); }

        static void Headband(Graphics g, float D, float y, float tilt, float plateX, float thick)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(0, y * D);
            g.RotateTransform(tilt);
            using (GraphicsPath band = Theme.Round(new RectangleF(-0.275f * D, -0.035f * thick * D, 0.55f * D, 0.07f * thick * D), 0.02f * D)) FigShape(g, band, Color.FromArgb(56, 70, 120), Color.FromArgb(30, 38, 76), D);
            RectangleF pl = new RectangleF((plateX - 0.11f) * D, -0.042f * thick * D, 0.22f * D, 0.084f * thick * D);
            using (GraphicsPath pp = Theme.Round(pl, 0.012f * D))
            {
                FigShape(g, pp, Color.FromArgb(228, 232, 240), Color.FromArgb(150, 156, 170), D);
                Ellipse(g, Color.FromArgb(120, 255, 255, 255), pl.X + pl.Width * 0.1f, pl.Y + pl.Height * 0.12f, pl.Width * 0.3f, pl.Height * 0.25f);
            }
            using (Pen sym = new Pen(Color.FromArgb(220, 50, 54, 64), Math.Max(1f, D * 0.008f)))
            {
                sym.StartCap = sym.EndCap = LineCap.Round;
                float cx = plateX * D;
                // The Leaf: a spiral with a point to its side.
                g.DrawArc(sym, cx - 0.02f * D, -0.022f * D, 0.04f * D, 0.04f * D, 200, 300);
                g.DrawArc(sym, cx - 0.01f * D, -0.01f * D, 0.02f * D, 0.02f * D, 200, 250);
                g.DrawLine(sym, cx + 0.02f * D, -0.005f * D, cx + 0.04f * D, -0.02f * D);
                g.DrawLine(sym, cx + 0.04f * D, -0.02f * D, cx + 0.022f * D, 0.012f * D);
            }
            g.Restore(st);
        }
    }

    public partial class Mascot
    {
        // The series' eyes: big anime ones (sclera, a shaded iris, pupil, two catchlights and a thick upper lash), sharp
        // narrow ones, half-lidded ones, small dots, or none when a mask draws its own.
        bool FigureEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            MascotParts.Fig f = MascotParts.FigOf(Look.Kind);
            if (f == null) return false;
            if (f.Eyes == MascotParts.EyeNone) return true;
            float sk = surprised ? 1.12f : 1f, s = x < 0 ? -1 : 1;
            Color ink = Color.FromArgb(32, 24, 30);
            if (f.Eyes == MascotParts.EyeDot)
            {
                float w = 0.034f * D * sk, h = 0.05f * D * sk * blink;
                if (h < D * 0.012f) { ClosedEye(g, x, y, w * 1.6f, D); return true; }
                MascotParts.Ellipse(g, ink, x - w / 2, y - h / 2, w, h);
                MascotParts.Ellipse(g, Color.FromArgb(220, 255, 255, 255), x - w * 0.28f, y - h * 0.3f, w * 0.34f, h * 0.26f);
                return true;
            }
            bool sharp = f.Eyes == MascotParts.EyeSharp, lid = f.Eyes == MascotParts.EyeLidded;
            float ew = 0.09f * D * sk, eh = (sharp ? 0.07f : 0.1f) * D * sk * blink;
            if (eh < D * 0.02f) { ClosedEye(g, x, y, ew, D); return true; }
            RectangleF r = new RectangleF(x - ew / 2, y - eh / 2, ew, eh);
            using (GraphicsPath eye = new GraphicsPath())
            {
                if (sharp)
                {
                    // An almond: the top edge slanting up to the outside, the bottom curved.
                    eye.AddLine(x - s * ew * 0.5f, y - eh * 0.1f, x + s * ew * 0.5f, y - eh * 0.45f);
                    eye.AddBezier(new PointF(x + s * ew * 0.5f, y - eh * 0.45f), new PointF(x + s * ew * 0.45f, y + eh * 0.5f), new PointF(x - s * ew * 0.4f, y + eh * 0.55f), new PointF(x - s * ew * 0.5f, y - eh * 0.1f));
                    eye.CloseFigure();
                }
                else eye.AddEllipse(r);
                MascotParts.FillSolid(g, eye, Color.FromArgb(252, 252, 250));
                Region old = g.Clip;
                g.SetClip(eye, CombineMode.Intersect);
                float iw = ew * (sharp ? 0.55f : 0.62f), ih = eh * (sharp ? 0.95f : 0.82f), ix = x - s * ew * 0.04f, iy = y + eh * 0.04f;
                RectangleF ir = new RectangleF(ix - iw / 2, iy - ih / 2, iw, ih);
                using (GraphicsPath ip = new GraphicsPath())
                {
                    ip.AddEllipse(ir);
                    MascotParts.Fill(g, ip, ir, MascotParts.Darker(f.Iris, 0.45), MascotParts.Lighter(f.Iris, 0.25), 90f);
                }
                MascotParts.Ellipse(g, Color.FromArgb(240, 14, 12, 18), ix - iw * 0.22f, iy - ih * 0.26f, iw * 0.44f, ih * 0.5f);
                MascotParts.Ellipse(g, Color.FromArgb(245, 255, 255, 255), ix - iw * 0.36f, iy - ih * 0.36f, iw * 0.36f, ih * 0.28f);
                MascotParts.Ellipse(g, Color.FromArgb(200, 255, 255, 255), ix + iw * 0.12f, iy + ih * 0.12f, iw * 0.18f, ih * 0.14f);
                if (lid)
                {
                    // The lid comes halfway down: skin over the top of the eye.
                    using (SolidBrush lb = new SolidBrush(MascotParts.Colors[Look.Color, 0])) g.FillRectangle(lb, r.X - 1, r.Y - 1, r.Width + 2, eh * 0.48f + 1);
                }
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, eye, Color.FromArgb(150, ink), Math.Max(0.8f, D * 0.006f));
            }
            // The thick upper lash line.
            using (Pen lash = new Pen(ink, Math.Max(1.4f, D * 0.017f)))
            {
                lash.StartCap = lash.EndCap = LineCap.Round;
                if (sharp) g.DrawLine(lash, x - s * ew * 0.52f, y - eh * 0.1f, x + s * ew * 0.56f, y - eh * 0.5f);
                else if (lid) g.DrawLine(lash, x - ew * 0.52f, y - eh * 0.02f, x + ew * 0.52f, y - eh * 0.02f);
                else g.DrawArc(lash, r.X - ew * 0.04f, r.Y, r.Width * 1.08f, r.Height * 0.9f, 195, 150);
            }
            return true;
        }

        bool FigureMouth(Graphics g, float D, float mx, float fy, bool talking)
        {
            MascotParts.Fig f = MascotParts.FigOf(Look.Kind);
            if (f == null) return false;
            if (f.Mouth == MascotParts.MouthNone) return true;
            bool open = talking || mood == Mood.Happy || mood == Mood.Love;
            Color ink = Color.FromArgb(32, 24, 30);
            float lw = Math.Max(1.2f, D * 0.012f), my = fy + 0.13f * D;
            if (f.Mouth == MascotParts.MouthGrin)
            {
                // A wide grin: a D of open mouth with a row of teeth.
                float w = 0.14f * D, h = (talking ? 0.06f + 0.02f * (float)Math.Abs(Math.Sin(now / 110.0)) : 0.07f) * D;
                using (GraphicsPath m = new GraphicsPath())
                {
                    m.AddArc(mx - w / 2, my - h / 2, w, h, 0, 180);
                    m.CloseFigure();
                    MascotParts.FillSolid(g, m, Color.FromArgb(120, 30, 40));
                    Region old = g.Clip;
                    g.SetClip(m, CombineMode.Intersect);
                    using (SolidBrush t = new SolidBrush(Color.White)) g.FillRectangle(t, mx - w / 2, my - 1, w, h * 0.3f);
                    MascotParts.Ellipse(g, Color.FromArgb(255, 130, 140), mx - w * 0.25f, my + h * 0.2f, w * 0.5f, h * 0.5f);
                    g.Clip = old;
                    old.Dispose();
                    MascotParts.Stroke(g, m, Color.FromArgb(230, ink), lw);
                }
                return true;
            }
            if (open) { OpenMouth(g, mx, my, 0.07f * D, 0.045f * D, D); return true; }
            using (Pen p = new Pen(ink, lw))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                float w = 0.06f * D;
                if (f.Mouth == MascotParts.MouthLine) g.DrawLine(p, mx - w / 2, my, mx + w / 2, my);
                else if (f.Mouth == MascotParts.MouthSmirk) g.DrawBezier(p, mx - w / 2, my + 0.004f * D, mx - w * 0.1f, my + 0.012f * D, mx + w * 0.3f, my + 0.006f * D, mx + w * 0.55f, my - 0.012f * D);
                else g.DrawArc(p, mx - w / 2, my - 0.02f * D, w, 0.03f * D, 20, 140);
            }
            return true;
        }
    }
}

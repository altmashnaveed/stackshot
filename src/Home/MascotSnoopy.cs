// Stackshot - Peanuts' Snoopy as a vinyl toy, side on as Schulz drew him: the big white head with its long snout and
// the black nose at its tip, the eye a short upright stroke, the long black ear hanging at the back, the pear of a body
// with its red collar, the little tail and the big feet. He only ever shows his side, turning to face the way he walks.
// Fan tribute, not affiliated with its owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindSnoopySd = 97;
        internal static readonly Color SnWhite = Color.FromArgb(252, 252, 250), SnShade = Color.FromArgb(214, 220, 232), SnBlack = Color.FromArgb(34, 32, 36);

        // His head facing right: the dome at the back, the snout running forward from it, rounded at its end.
        static GraphicsPath SnoopyHead(float D)
        {
            return KCurve(D, 0.5f, -0.065f, -0.5f, 0.04f, -0.462f, 0.14f, -0.418f, 0.24f, -0.378f, 0.31f, -0.33f, 0.333f, -0.255f, 0.318f, -0.182f, 0.27f, -0.14f,
                          0.17f, -0.108f, 0.06f, -0.095f, -0.03f, -0.07f, -0.12f, -0.06f, -0.215f, -0.085f, -0.27f, -0.14f, -0.287f, -0.235f, -0.255f, -0.385f, -0.175f, -0.472f);
        }

        // Where a foot is (units of D, his front to the right, side 1 the near one): stepping as he walks, swinging in the air.
        static PointF SnoopyStep(MascotPose p, int side)
        {
            float x = 0, y = 0;
            if (p.Walk >= 0.01 && Math.Abs(p.Facing) > 0.1)
            {
                double ph = p.Step + (side > 0 ? Math.PI : 0);
                x = (float)(-Math.Cos(ph) * 0.05 * p.Walk);
                y = (float)(-Math.Max(0, Math.Sin(ph)) * 0.045 * p.Walk);
            }
            if (p.Air >= 0.01)
            {
                double q = p.Now / (p.Held ? 300.0 : 80.0) + (side > 0 ? Math.PI : 0);
                float a = (float)p.Air, k = (float)Math.Sin(q), trail = -0.04f * (float)Math.Min(1, Math.Abs(p.Facing));
                if (p.Held) { x += (k * 0.03f + trail) * a; y += (0.014f - Math.Max(0, k) * 0.012f) * a; }
                else { x += ((float)Math.Cos(q) * 0.04f + trail) * a; y += (-0.02f - Math.Max(0, k) * 0.06f) * a; }
            }
            return new PointF(x, y);
        }

        // Behind his head: the tail, the far leg and foot, his body with the collar (kept), the near leg and foot, his arms.
        static void SnoopyBody(Graphics g, float D, MascotPose p, Geo geo)
        {
            int k = KindSnoopySd;
            SnoopyTail(g, D, p.Now, Math.Max(p.Walk, p.Sleeping ? 0 : 0.35));
            PointF far = SnoopyStep(p, -1), near = SnoopyStep(p, 1);
            SnoopyLeg(g, D, -0.012f + far.X, 0.364f + far.Y, true);
            if (!p.HideHands) ToonArm(g, D, k, -1, DsHand(p, geo, -1));   // the far arm, behind him
            DsCached(g, D, TnId(k, 3), new RectangleF(-0.12f, -0.09f, 0.25f, 0.45f), bg => SnoopyTorso(bg, D));
            SnoopyLeg(g, D, 0.03f + near.X, 0.366f + near.Y, false);
            if (!p.HideHands) ToonArm(g, D, k, 1, DsHand(p, geo, 1));
        }

        // A short leg down to a big foot, its toes to the front, (x, y) the foot's middle; the far one a little greyer.
        static void SnoopyLeg(Graphics g, float D, float x, float y, bool far)
        {
            int k = KindSnoopySd;
            Color c = far ? Color.FromArgb(234, 236, 242) : SnWhite;
            VLimb(g, k, D, c, 0.075f, P(x * 0.4f, 0.29f), P(x - 0.01f, y - 0.02f), 0);
            VBlob(g, k, D, c, SnShade, 0, 0.5f, x - 0.1f, y - 0.004f, x - 0.06f, y - 0.03f, x + 0.04f, y - 0.034f, x + 0.115f, y - 0.018f, x + 0.13f, y + 0.012f,
                  x + 0.09f, y + 0.032f, x - 0.04f, y + 0.034f, x - 0.1f, y + 0.022f);
            if (!far) for (int i = 0; i < 2; i++) TnLine(g, TnInk(k), 0.007f, D, x + 0.06f + i * 0.03f, y + 0.032f, x + 0.055f + i * 0.03f, y + 0.012f);
        }

        static void SnoopyTorso(Graphics g, float D)
        {
            int k = KindSnoopySd;
            using (GraphicsPath t = KCurve(D, 0.5f, 0.035f, -0.07f, 0.075f, 0.05f, 0.112f, 0.17f, 0.118f, 0.26f, 0.088f, 0.325f, 0, 0.345f, -0.075f, 0.325f, -0.095f, 0.24f,
                                           -0.085f, 0.12f, -0.065f, 0.02f, -0.05f, -0.07f))
                VFill(g, k, t, SnWhite, SnShade, D, 60);
            VRound(g, k, D, Color.FromArgb(226, 40, 48), Color.FromArgb(184, 22, 32), 60, -0.068f, -0.06f, 0.118f, 0.036f, 0.016f);
        }

        // The little tail at his back, wagging (wag 0-1).
        static void SnoopyTail(Graphics g, float D, double now, double wag)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(-0.07f * D, 0.21f * D);
            g.RotateTransform((float)(Math.Sin(now / 160.0) * 12 * wag));
            VBlob(g, KindSnoopySd, D, SnWhite, SnShade, 0, 0.5f, 0.01f, -0.02f, -0.06f, -0.045f, -0.13f, -0.065f, -0.07f, -0.012f, 0.01f, 0.02f);
            g.Restore(st);
        }

        // His long black ear, hanging from the back of his head: it swings as he walks and flies up as he falls.
        static void SnoopyEar(Graphics g, float D, MascotPose p)
        {
            int k = KindSnoopySd;
            float swing = (float)(Math.Sin(p.Step) * 7 * p.Walk + (p.Air > 0.01 ? (p.Held ? 8 : 40) * p.Air : 0));
            GraphicsState st = g.Save();
            g.TranslateTransform(-0.14f * D, -0.37f * D);
            g.RotateTransform(-14 + swing);
            using (GraphicsPath e = new GraphicsPath())
            {
                e.AddEllipse(-0.075f * D, -0.03f * D, 0.15f * D, 0.27f * D);
                VFill(g, k, e, SnBlack, Color.FromArgb(16, 14, 18), D, 0);
            }
            TnLine(g, Color.FromArgb(150, 255, 255, 255), 0.008f, D, -0.028f, 0.03f, -0.044f, 0.09f);
            TnLine(g, Color.FromArgb(110, 255, 255, 255), 0.007f, D, -0.01f, 0.06f, -0.02f, 0.11f);
            g.Restore(st);
        }

        static void SnoopyDetails(Graphics g, float D, MascotPose p)
        {
            SnoopyEar(g, D, p);
            VOval(g, KindSnoopySd, D, SnBlack, Color.Empty, 140, 0.343f, -0.186f, 0.07f, 0.075f);
            Ellipse(g, Color.FromArgb(220, 255, 255, 255), 0.33f * D, -0.212f * D, 0.016f * D, 0.012f * D);
        }
    }

    public partial class Mascot
    {
        // Snoopy's face, side on: one eye, a short upright stroke, and the smile along his snout with the little line at its
        // end; opening, his mouth runs under the smile with the tongue at its front.
        bool SnoopyFace(Graphics g, float D, MascotParts.Geo geo, float ex, float ey, float blink)
        {
            Color ink = Color.FromArgb(30, 26, 34);
            float lw = Math.Max(1.1f, D * 0.012f), x = 0.05f * D + ex * 0.35f, y = geo.FaceY * D + (ey - geo.FaceY * D) * 0.6f;
            using (Pen p = new Pen(ink, lw))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                switch (mood)
                {
                    case Mood.Happy: g.DrawArc(p, x - 0.026f * D, y - 0.01f * D, 0.052f * D, 0.04f * D, 200, 140); break;
                    case Mood.Wink:
                    case Mood.Sleep:
                    case Mood.Yawn: g.DrawArc(p, x - 0.024f * D, y - 0.02f * D, 0.048f * D, 0.03f * D, 20, 140); break;
                    case Mood.Love:
                        using (GraphicsPath hp = MascotParts.Heart(x, y, D * 0.065f)) MascotParts.FillSolid(g, hp, LoveColor);
                        break;
                    case Mood.Dizzy: DizzyEye(g, p, x, y, 0.032f * D, 0); break;
                    default:
                    {
                        float w = (mood == Mood.Surprised ? 0.03f : 0.022f) * D, h = (mood == Mood.Surprised ? 0.072f : 0.062f) * D * (mood == Mood.Surprised ? 1 : blink);
                        if (h < 0.012f * D) g.DrawLine(p, x - 0.016f * D, y, x + 0.016f * D, y);
                        else using (GraphicsPath e = Theme.Round(new RectangleF(x - w / 2, y - h / 2, w, h), w / 2)) MascotParts.FillSolid(g, e, ink);
                        break;
                    }
                }
                float mx = 0;   // his smile is drawn along his snout: it stays put
                PointF a = new PointF(mx + 0.19f * D, -0.152f * D), b = new PointF(mx + 0.09f * D, -0.128f * D), c = new PointF(mx - 0.008f * D, -0.184f * D);
                bool talking = Bubble != null && mood != Mood.Sleep && mood != Mood.Dizzy;
                if (mood == Mood.Surprised || mood == Mood.Dizzy)
                {
                    float r = 0.016f * D;
                    MascotParts.Ellipse(g, Color.FromArgb(120, 36, 44), mx + 0.13f * D - r, -0.14f * D - r, r * 2, r * 2.3f);
                    return true;
                }
                if (talking || mood == Mood.Happy || mood == Mood.Love || mood == Mood.Yawn)
                {
                    float open = mood == Mood.Yawn ? 0.09f * (float)Math.Sin(Math.Min(1, (moodUntil - now) / 1900.0) * Math.PI) + 0.03f : 0.07f;
                    if (talking && mood == Mood.Idle) open *= (float)(0.4 + 0.6 * Math.Abs(Math.Sin(now / 110.0)));
                    using (GraphicsPath m = new GraphicsPath())
                    {
                        m.AddCurve(new PointF[] { a, b, c }, 0.5f);
                        m.AddBezier(c, new PointF(mx + 0.02f * D, (-0.184f + open * 0.9f) * D), new PointF(mx + 0.14f * D, (-0.13f + open) * D), a);
                        m.CloseFigure();
                        MascotParts.FillSolid(g, m, Color.FromArgb(120, 36, 44));
                        Region old = g.Clip;
                        g.SetClip(m, CombineMode.Intersect);
                        MascotParts.Ellipse(g, Color.FromArgb(240, 116, 126), mx + 0.07f * D, (-0.14f + open * 0.45f) * D, 0.13f * D, open * D);
                        g.Clip = old;
                        old.Dispose();
                        using (Pen mp = new Pen(ink, lw)) { mp.LineJoin = LineJoin.Round; g.DrawPath(mp, m); }
                    }
                }
                else g.DrawCurve(p, new PointF[] { a, b, c }, 0.5f);
                // The little line where his smile ends.
                g.DrawLine(p, c.X - 0.01f * D, c.Y - 0.014f * D, c.X + 0.006f * D, c.Y + 0.014f * D);
            }
            return true;
        }
    }
}

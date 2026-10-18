// Stackshot - One Piece's Tony Tony Chopper and Cardcaptor Sakura's Kero as vinyl toys, like the rest of MascotToons.
// Chopper as his figure: the big pink top hat with the white cross, the antlers coming out of its crown and the ears
// resting on its brim, round eyes, the blue nose, the teal backpack, maroon shorts and hooves. Kero in his small form as
// the anime draws him: a round head with big round ears, dot eyes, the little wings of three feathers flapping as he
// hovers, and the long tail with its white tuft. Fan tributes, not affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindChopper = 95, KindKeroSd = 96;

        internal static readonly Color ChFur = Color.FromArgb(240, 204, 140), ChFurShade = Color.FromArgb(214, 166, 106);
        static readonly Color ChPink = Color.FromArgb(242, 132, 142), ChPinkShade = Color.FromArgb(212, 94, 108);
        static readonly Color ChHoof = Color.FromArgb(96, 82, 74), ChHoofShade = Color.FromArgb(68, 56, 50);
        static readonly Color ChAntler = Color.FromArgb(158, 122, 68), ChAntlerShade = Color.FromArgb(120, 88, 46);
        static readonly Color ChTeal = Color.FromArgb(30, 112, 128), ChTealShade = Color.FromArgb(20, 84, 98);
        static readonly Color ChShorts = Color.FromArgb(150, 62, 66), ChShortsShade = Color.FromArgb(108, 40, 46);
        internal static readonly Color KeFur = Color.FromArgb(248, 196, 88), KeFurShade = Color.FromArgb(232, 162, 50);

        // A limb as a shape of its own (so it is shaded like the rest), from a through the control points to b (units of
        // D), w wide with round ends.
        static GraphicsPath TnTube(float D, float w, PointF a, PointF c1, PointF c2, PointF b)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(P(a.X * D, a.Y * D), P(c1.X * D, c1.Y * D), P(c2.X * D, c2.Y * D), P(b.X * D, b.Y * D));
            using (Pen pen = new Pen(Color.Black, w * D))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                p.Widen(pen);
            }
            p.FillMode = FillMode.Winding;
            return p;
        }

        // ---- Chopper.

        static GraphicsPath ChopperHead(float D)
        {
            // Round, the cheeks full low down; its top is under the hat.
            return KCurve(D, 0.5f, 0, -0.315f, 0.12f, -0.302f, 0.178f, -0.26f, 0.2f, -0.19f, 0.206f, -0.12f, 0.185f, -0.055f, 0.125f, -0.013f, 0, 0.002f,
                          -0.125f, -0.013f, -0.185f, -0.055f, -0.206f, -0.12f, -0.2f, -0.19f, -0.178f, -0.26f, -0.12f, -0.302f);
        }

        static void ChopperFace(Graphics g, GraphicsPath head, float D)
        {
            VFill(g, KindChopper, head, ChFur, ChFurShade, D, 60);
            // The brim's shadow across the top of his face.
            Region old = g.Clip;
            g.SetClip(head, CombineMode.Intersect);
            Ellipse(g, Color.FromArgb(70, 190, 130, 80), -0.37f * D, -0.37f * D, 0.74f * D, 0.225f * D);
            Ellipse(g, Color.FromArgb(70, 190, 130, 80), -0.36f * D, -0.37f * D, 0.72f * D, 0.205f * D);
            g.Clip = old;
            old.Dispose();
        }

        // In front of his face (the brim over his eyes when he looks up): the hat, drawn once and kept.
        public static void PaintToonFront(Graphics g, MascotLook l, float D, MascotPose p)
        {
            // Snoopy's ear and nose are part of his head seen side on: they stay put while his eye looks about.
            if (l.Kind == KindSnoopySd) { SnoopyDetails(g, D, p); return; }
            if (l.Kind == KindChopper) DsCached(g, D, TnId(KindChopper, 4), new RectangleF(-0.58f, -0.7f, 1.16f, 0.56f), bg => ChopperHat(bg, D));
            else if (l.Kind == KindKilluaSd) DsCached(g, D, TnId(KindKilluaSd, 4), new RectangleF(-0.36f, -0.5f, 0.72f, 0.5f), bg => KilluaFront(bg, D));
            else if (l.Kind == KindLuffySd) DsCached(g, D, TnId(KindLuffySd, 4), new RectangleF(-0.46f, -0.62f, 0.92f, 0.52f), bg => LuffyFront(bg, D));
            else if (l.Kind == KindPiggySd) DsCached(g, D, TnId(KindPiggySd, 4), new RectangleF(0.19f, -0.04f, 0.17f, 0.18f), bg => BuriSword(bg, D));
            else if (HasPersonaFront(l.Kind)) DsCached(g, D, TnId(l.Kind, 4), PersonaFrontArea(l.Kind), bg => PersonaFront(bg, l.Kind, D));
            else if (IsSoftFig(l.Kind) && PkHasFront(l.Kind)) PkFront(g, l.Kind, D);
        }

        // The hat as his figure has it: the wide brim, seen a little from above; on it his ears; the antlers coming out of
        // the sides of the crown; the crown, a little wider at the top, with the white cross on it.
        static void ChopperHat(Graphics g, float D)
        {
            int k = KindChopper;
            const float cy = -0.29f, rx = 0.345f, ry = 0.078f, lip = 0.032f;
            using (GraphicsPath brim = new GraphicsPath())
            {
                brim.AddArc(-rx * D, (cy - ry) * D, 2 * rx * D, 2 * ry * D, 180, 180);
                brim.AddArc(-rx * D, (cy - ry + lip) * D, 2 * rx * D, 2 * ry * D, 0, 180);
                brim.CloseFigure();
                VFill(g, k, brim, Color.FromArgb(222, 108, 122), Color.FromArgb(190, 78, 94), D, 0);
            }
            using (GraphicsPath top = new GraphicsPath())
            {
                top.AddEllipse(-rx * D, (cy - ry) * D, 2 * rx * D, 2 * ry * D);
                VPaint(g, top, Color.FromArgb(246, 150, 158), Color.Empty, D, 50);
                using (Pen edge = new Pen(Color.FromArgb(140, 150, 50, 66), Math.Max(0.8f, D * 0.006f))) g.DrawArc(edge, -rx * D, (cy - ry) * D, 2 * rx * D, 2 * ry * D, 12, 156);
            }
            foreach (int s in Sides) ChopperAntler(g, D, s);
            foreach (int s in Sides)
            {
                VBlob(g, k, D, ChFur, ChFurShade, 0, 0.3f, s * 0.2f, -0.3f, s * 0.278f, -0.307f, s * 0.348f, -0.284f, s * 0.28f, -0.262f, s * 0.2f, -0.258f);
                using (GraphicsPath inner = KCurve(D, 0.3f, s * 0.225f, -0.292f, s * 0.278f, -0.295f, s * 0.322f, -0.284f, s * 0.278f, -0.271f, s * 0.225f, -0.27f))
                    FillSolid(g, inner, Color.FromArgb(232, 164, 128));
            }
            using (GraphicsPath crown = new GraphicsPath())
            {
                float bw = 0.225f, tw = 0.24f, t = -0.648f, r = 0.05f;
                crown.AddArc(-bw * D, (cy - 0.059f) * D, 2 * bw * D, 0.118f * D, 180, -180);
                crown.AddLine(bw * D, cy * D, tw * D, (t + r) * D);
                crown.AddBezier(P(tw * D, (t + r) * D), P(tw * D, (t + 0.012f) * D), P((tw - 0.03f) * D, t * D), P((tw - r) * D, (t - 0.002f) * D));
                crown.AddBezier(P((tw - r) * D, (t - 0.002f) * D), P(0.08f * D, (t - 0.012f) * D), P(-0.08f * D, (t - 0.012f) * D), P(-(tw - r) * D, (t - 0.002f) * D));
                crown.AddBezier(P(-(tw - r) * D, (t - 0.002f) * D), P(-(tw - 0.03f) * D, t * D), P(-tw * D, (t + 0.012f) * D), P(-tw * D, (t + r) * D));
                crown.CloseFigure();
                VFill(g, k, crown, ChPink, ChPinkShade, D, 70);
            }
            // The cross: two bars, one piece.
            using (GraphicsPath x = new GraphicsPath(FillMode.Winding))
            using (GraphicsPath b1 = Theme.Round(new RectangleF(-0.1f * D, -0.026f * D, 0.2f * D, 0.052f * D), 0.012f * D))
            using (GraphicsPath b2 = Theme.Round(new RectangleF(-0.026f * D, -0.1f * D, 0.052f * D, 0.2f * D), 0.012f * D))
            using (Matrix m = new Matrix())
            {
                x.AddPath(b1, false);
                x.AddPath(b2, false);
                m.Translate(0, -0.452f * D);
                m.Rotate(45);
                x.Transform(m);
                VFill(g, k, x, Color.FromArgb(252, 252, 250), Color.FromArgb(212, 212, 222), D, 0);
            }
        }

        // Each antler's tines from where it leaves the crown, outward positive (units of D): base, bend, tip, width. Three
        // curve up from the end of the beam, side by side like fingers; one rises close to the hat.
        static readonly float[][] AntlerTines =
        {
            new float[] { 0.175f, -0.06f, 0.24f, -0.12f, 0.235f, -0.232f, 0.05f },
            new float[] { 0.15f, -0.075f, 0.2f, -0.15f, 0.2f, -0.268f, 0.05f },
            new float[] { 0.122f, -0.075f, 0.158f, -0.16f, 0.163f, -0.28f, 0.048f },
            new float[] { 0.068f, -0.04f, 0.04f, -0.17f, 0.054f, -0.285f, 0.048f },
        };

        static void ChopperAntler(Graphics g, float D, int s)
        {
            int k = KindChopper;
            float ox = s * 0.228f, oy = -0.335f;
            using (GraphicsPath beam = TnTube(D, 0.072f, P(ox, oy + 0.005f), P(ox + s * 0.06f, oy - 0.005f), P(ox + s * 0.11f, oy - 0.03f), P(ox + s * 0.16f, oy - 0.06f)))
            {
                VFill(g, k, beam, ChAntler, ChAntlerShade, D, 0);
                foreach (float[] t in AntlerTines)
                {
                    PointF a = P(ox + s * t[0], oy + t[1]), c = P(ox + s * t[2], oy + t[3]), b = P(ox + s * t[4], oy + t[5]);
                    using (GraphicsPath tp = TnTube(D, t[6], a, Lerp(a, c, 0.7f), Lerp(b, c, 0.5f), b)) VFill(g, k, tp, ChAntler, ChAntlerShade, D, 40);
                }
                // The beam again, without its outline, so the tines grow out of it.
                VPaint(g, beam, ChAntler, Color.Empty, D, 0);
            }
            // The grey ring where it comes through the hat.
            VOval(g, k, D, Color.FromArgb(118, 110, 106), Color.FromArgb(88, 82, 80), 40, ox + s * 0.012f, oy + 0.004f, 0.06f, 0.07f);
        }

        // Behind his head: the backpack (kept), his legs and hooves stepping as he walks, his body and shorts (kept), his arms.
        static void ChopperBody(Graphics g, float D, MascotPose p, Geo geo)
        {
            int k = KindChopper;
            DsCached(g, D, TnId(k, 1), new RectangleF(-0.2f, -0.1f, 0.4f, 0.38f), bg => ChopperPack(bg, D));
            foreach (int s in Sides)
            {
                PointF f = Foot(p, s, s * 0.072f, 0.372f);
                VLimb(g, k, D, ChFur, 0.046f, P(s * 0.07f, 0.25f), P(f.X, f.Y - 0.012f), 0);
                ChopperHoof(g, D, f.X, f.Y, 1);
            }
            DsCached(g, D, TnId(k, 3), new RectangleF(-0.18f, -0.07f, 0.36f, 0.37f), bg => ChopperTorso(bg, D));
            if (!p.HideHands) foreach (int s in Sides) ToonArm(g, D, k, s, DsHand(p, geo, s));
        }

        // A dark hoof around (x, y), split in two at the front; sc for the smaller ones at his hands.
        static void ChopperHoof(Graphics g, float D, float x, float y, float sc)
        {
            int k = KindChopper;
            float[] xy = { -0.031f, -0.022f, 0.031f, -0.022f, 0.046f, 0, 0.05f, 0.02f, 0.028f, 0.028f, 0, 0.024f, -0.028f, 0.028f, -0.05f, 0.02f, -0.046f, 0 };
            for (int i = 0; i < xy.Length; i += 2) { xy[i] = x + xy[i] * sc; xy[i + 1] = y + xy[i + 1] * sc; }
            VBlob(g, k, D, ChHoof, ChHoofShade, 60, 0.45f, xy);
            TnLine(g, TnInk(k), 0.008f * sc, D, x, y + 0.002f * sc, x, y + 0.026f * sc);
        }

        // The teal backpack showing round his sides, and the cord with its red bead hanging from it.
        static void ChopperPack(Graphics g, float D)
        {
            int k = KindChopper;
            VRound(g, k, D, ChTeal, ChTealShade, 40, -0.168f, -0.085f, 0.336f, 0.25f, 0.07f);
            TnLine(g, Color.FromArgb(120, 84, 56), 0.008f, D, -0.13f, 0.15f, -0.152f, 0.2f, -0.165f, 0.238f);
            VOval(g, k, D, Color.FromArgb(226, 56, 50), Color.FromArgb(180, 30, 32), 120, -0.166f, 0.252f, 0.03f, 0.03f);
        }

        // His bare chest, in the shade of his head at the top, the backpack's straps over his shoulders, and his maroon
        // shorts rolled up at the ends of their legs.
        static void ChopperTorso(Graphics g, float D)
        {
            int k = KindChopper;
            using (GraphicsPath t = KCurve(D, 0.5f, 0, -0.04f, 0.072f, -0.034f, 0.108f, 0, 0.12f, 0.06f, 0.126f, 0.12f, 0.112f, 0.17f, 0, 0.18f,
                                           -0.112f, 0.17f, -0.126f, 0.12f, -0.12f, 0.06f, -0.108f, 0, -0.072f, -0.034f))
            {
                VFill(g, k, t, ChFur, ChFurShade, D, 40);
                Region old = g.Clip;
                g.SetClip(t, CombineMode.Intersect);
                Ellipse(g, Color.FromArgb(110, ChFurShade), -0.14f * D, -0.08f * D, 0.28f * D, 0.1f * D);
                foreach (int s in Sides) VLimb(g, k, D, ChTeal, 0.028f, P(s * 0.068f, -0.04f), P(s * 0.128f, 0.1f), s * 0.04f);
                g.Clip = old;
                old.Dispose();
            }
            VPoly(g, k, D, ChShorts, ChShortsShade, 30, -0.146f, 0.13f, 0.146f, 0.13f, 0.162f, 0.282f, 0.02f, 0.282f, 0, 0.248f, -0.02f, 0.282f, -0.162f, 0.282f);
            foreach (int s in Sides) VRound(g, k, D, Color.FromArgb(170, 76, 80), ChShortsShade, 0, s > 0 ? 0.016f : -0.166f, 0.258f, 0.15f, 0.03f, 0.012f);
            TnLine(g, Color.FromArgb(150, 70, 24, 30), 0.007f, D, -0.14f, 0.152f, 0, 0.158f, 0.14f, 0.152f);
        }

        // His blue nose, rounder at the top, with its shine.
        static void ChopperNose(Graphics g, float D, float f)
        {
            VBlob(g, KindChopper, D, Color.FromArgb(54, 132, 210), Color.FromArgb(30, 96, 176), 120, 0.5f,
                  0, f + 0.007f, 0.022f, f + 0.006f, 0.029f, f + 0.018f, 0.02f, f + 0.036f, 0, f + 0.044f, -0.02f, f + 0.036f, -0.029f, f + 0.018f, -0.022f, f + 0.006f);
            Ellipse(g, Color.FromArgb(220, 255, 255, 255), -0.016f * D, (f + 0.011f) * D, 0.014f * D, 0.008f * D);
        }

        // ---- Kero.

        static GraphicsPath KeroHead(float D)
        {
            return KCurve(D, 0.5f, 0, -0.412f, 0.133f, -0.384f, 0.207f, -0.304f, 0.225f, -0.2f, 0.207f, -0.09f, 0.136f, -0.011f, 0, 0.012f,
                          -0.136f, -0.011f, -0.207f, -0.09f, -0.225f, -0.2f, -0.207f, -0.304f, -0.133f, -0.384f);
        }

        // A big round ear behind his head; inside it the paler yellow, darker in a crescent at the top.
        static void KeroEarSd(Graphics g, float D, int s)
        {
            int k = KindKeroSd;
            float cx = s * 0.168f, cy = -0.35f;
            VOval(g, k, D, KeFur, KeFurShade, 30, cx, cy, 0.22f, 0.22f);
            using (GraphicsPath inner = new GraphicsPath())
            {
                inner.AddEllipse((cx - s * 0.012f - 0.077f) * D, (cy + 0.014f - 0.077f) * D, 0.154f * D, 0.154f * D);
                FillSolid(g, inner, Color.FromArgb(214, 200, 108));
                Region old = g.Clip;
                g.SetClip(inner, CombineMode.Intersect);
                Ellipse(g, Color.FromArgb(244, 232, 156), (cx - s * 0.02f - 0.08f) * D, (cy + 0.04f - 0.08f) * D, 0.16f * D, 0.16f * D);
                g.Clip = old;
                old.Dispose();
                using (Pen pen = new Pen(TnInk(k), Math.Max(0.8f, TnW(D) * 0.8f))) g.DrawPath(pen, inner);
            }
        }

        // Behind his head: the tail, the wings flapping, his short legs stepping as he walks, his body (kept), his arms.
        static void KeroBody(Graphics g, float D, MascotPose p, Geo geo)
        {
            int k = KindKeroSd;
            KeroTailSd(g, D, p.TailLeft ? -1 : 1, p.Now);
            // Faster while he walks, fastest in the air; folded down while asleep.
            double flap = p.Sleeping ? -16 : Math.Sin(p.Now / (p.Air > 0.3 ? 55.0 : p.Walk > 0.3 ? 95.0 : 160.0)) * (p.Air > 0.3 ? 22 : 13) + 4;
            foreach (int s in Sides) KeroWingSd(g, D, s, (float)flap);
            foreach (int s in Sides)
            {
                PointF f = Foot(p, s, s * 0.072f, 0.352f);
                VLimb(g, k, D, KeFur, 0.1f, P(s * 0.065f, 0.28f), f, 0);
            }
            DsCached(g, D, TnId(k, 3), new RectangleF(-0.18f, -0.05f, 0.36f, 0.4f), bg => KeroTorso(bg, D));
            if (!p.HideHands) foreach (int s in Sides) ToonArm(g, D, k, s, DsHand(p, geo, s));
        }

        static void KeroTorso(Graphics g, float D)
        {
            int k = KindKeroSd;
            using (GraphicsPath t = KCurve(D, 0.5f, 0, -0.03f, 0.09f, -0.02f, 0.122f, 0.04f, 0.142f, 0.12f, 0.152f, 0.2f, 0.14f, 0.27f, 0.1f, 0.315f, 0.05f, 0.33f, 0, 0.325f,
                                           -0.05f, 0.33f, -0.1f, 0.315f, -0.14f, 0.27f, -0.152f, 0.2f, -0.142f, 0.12f, -0.122f, 0.04f, -0.09f, -0.02f))
            {
                VFill(g, k, t, KeFur, KeFurShade, D, 50);
                // The shade of his head under his chin.
                Region old = g.Clip;
                g.SetClip(t, CombineMode.Intersect);
                Ellipse(g, Color.FromArgb(150, 232, 156, 44), -0.15f * D, -0.04f * D, 0.3f * D, 0.1f * D);
                g.Clip = old;
                old.Dispose();
            }
        }

        // The feathers of a wing: angle (degrees, up negative) and length.
        static readonly float[][] KeroFeathers = { new float[] { -30, 0.25f }, new float[] { -9, 0.285f }, new float[] { 12, 0.23f } };

        // His little wing out of his back at the shoulder, three long rounded white feathers fanned out; lift in degrees.
        static void KeroWingSd(Graphics g, float D, int s, float lift)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(s * 0.07f * D, 0.06f * D);
            g.RotateTransform(-s * lift);
            foreach (float[] f in KeroFeathers)
                using (GraphicsPath fp = KeroFeather(D, s, f[0], f[1]))
                    VFill(g, KindKeroSd, fp, Color.FromArgb(252, 252, 250), Color.FromArgb(212, 214, 228), D, 50);
            g.Restore(st);
        }

        // A feather from the wing's root, narrow there and round at its end.
        static GraphicsPath KeroFeather(float D, int s, float angle, float len)
        {
            const float r = 0.038f;
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(P(0.03f * D, -0.016f * D), P(0.1f * D, -0.032f * D), P((len - 0.09f) * D, -r * D), P((len - r) * D, -r * D));
            p.AddArc((len - 2 * r) * D, -r * D, 2 * r * D, 2 * r * D, 270, 180);
            p.AddBezier(P((len - r) * D, r * D), P((len - 0.09f) * D, r * D), P(0.1f * D, 0.032f * D), P(0.03f * D, 0.016f * D));
            p.CloseFigure();
            using (Matrix m = new Matrix())
            {
                m.Scale(s, 1);
                m.Rotate(angle);
                p.Transform(m);
            }
            return p;
        }

        // The white tuft at the end of his tail, pointing up from it: notches and tips in turn.
        static readonly float[] KeroTuft =
        {
            -0.02f, 0.005f, -0.06f, -0.01f, -0.042f, -0.04f, -0.07f, -0.07f, -0.036f, -0.075f, -0.045f, -0.11f, -0.012f, -0.095f, 0, -0.135f,
            0.012f, -0.095f, 0.045f, -0.11f, 0.036f, -0.075f, 0.07f, -0.07f, 0.042f, -0.04f, 0.06f, -0.01f, 0.02f, 0.005f, 0, 0.02f
        };

        // His long thin tail from behind him, down and out and up again (on side s), swaying, with its tuft.
        static void KeroTailSd(Graphics g, float D, int s, double now)
        {
            int k = KindKeroSd;
            float sw = (float)Math.Sin(now / 760.0);
            PointF a = P(s * 0.06f * D, 0.28f * D), c1 = P(s * 0.2f * D, 0.47f * D), c2 = P(s * 0.44f * D, 0.44f * D);
            PointF e = P(s * (0.4f + sw * 0.015f) * D, (0.22f + (float)Math.Cos(now / 760.0) * 0.008f) * D);
            using (Pen ink = new Pen(TnInk(k), 0.026f * D + TnW(D) * 2))
            using (Pen fill = new Pen(KeFur, 0.026f * D))
            {
                ink.StartCap = ink.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawBezier(ink, a, c1, c2, e);
                g.DrawBezier(fill, a, c1, c2, e);
            }
            GraphicsState st = g.Save();
            g.TranslateTransform(e.X, e.Y);
            g.RotateTransform(s * (8 + sw * 6));
            using (GraphicsPath t = TnSpikes(D, 0.06f, KeroTuft)) VFill(g, k, t, Color.FromArgb(252, 250, 244), Color.FromArgb(218, 212, 202), D, 40);
            foreach (int i in Sides) TnLine(g, Color.FromArgb(150, 170, 164, 156), 0.006f, D, 0, -0.005f, i * 0.04f, -0.06f);
            TnLine(g, Color.FromArgb(150, 170, 164, 156), 0.006f, D, 0, -0.005f, 0, -0.09f);
            g.Restore(st);
        }

        // His tiny nose, at the height of his eyes.
        static void KeroNose(Graphics g, float D, float f)
        {
            using (SolidBrush b = new SolidBrush(TnInk(KindKeroSd)))
            using (Pen pen = new Pen(TnInk(KindKeroSd), Math.Max(0.8f, D * 0.005f)))
            {
                PointF[] tri = { P(-0.009f * D, (f + 0.004f) * D), P(0.009f * D, (f + 0.004f) * D), P(0, (f + 0.015f) * D) };
                pen.LineJoin = LineJoin.Round;
                g.FillPolygon(b, tri);
                g.DrawPolygon(pen, tri);
            }
        }
    }

    public partial class Mascot
    {
        // Chopper's eye as his figure has it: a little taller than wide, nearly all big black pupil inside a thin ring of
        // white and the dark outline, with its shines; the pupil only shifts a little as he looks about. To blink, his fur
        // comes down over it. pupil: its size against the white (smaller when surprised).
        void ChopperEye(Graphics g, float x, float y, float D, float sk, float blink, float pupil)
        {
            Color ink = Color.FromArgb(26, 22, 24);
            const float tall = 1.08f;
            float ro = 0.05f * D * sk, rw = ro - Math.Max(1f, 0.0065f * D);
            float lx = (float)lookX * (flipped ? -1 : 1), ly = (float)lookY;
            MascotParts.Ellipse(g, ink, x - ro, y - ro * tall, ro * 2, ro * 2 * tall);
            MascotParts.Ellipse(g, Color.FromArgb(252, 252, 250), x - rw, y - rw * tall, rw * 2, rw * 2 * tall);
            float rp = rw * pupil, px = x + lx * (rw - rp) * 0.6f, py = y + ly * (rw - rp) * 0.5f;
            MascotParts.Ellipse(g, ink, px - rp, py - rp * tall, rp * 2, rp * 2 * tall);
            MascotParts.Ellipse(g, Color.FromArgb(240, 255, 255, 255), px - rp * 0.58f, py - rp * 0.66f, rp * 0.42f, rp * 0.42f);
            MascotParts.Ellipse(g, Color.FromArgb(190, 255, 255, 255), px + rp * 0.24f, py + rp * 0.24f, rp * 0.18f, rp * 0.18f);
            if (blink >= 0.999f) return;
            float ry = ro * tall, lidY = y - ry + ry * 2 * (1 - blink);
            Region old = g.Clip;
            using (GraphicsPath ep = new GraphicsPath())
            {
                ep.AddEllipse(x - ro - 0.5f, y - ry - 0.5f, ro * 2 + 1, ry * 2 + 1);
                g.SetClip(ep, CombineMode.Intersect);
            }
            using (SolidBrush lid = new SolidBrush(MascotParts.ChFur)) g.FillRectangle(lid, x - ro - 2, y - ry - 2, ro * 2 + 4, lidY - (y - ry) + 2);
            g.Clip = old;
            old.Dispose();
            float dy = (lidY - y) / tall, half = (float)Math.Sqrt(Math.Max(0, ro * ro - dy * dy));
            using (Pen lp = new Pen(ink, Math.Max(1f, D * 0.011f))) { lp.StartCap = lp.EndCap = LineCap.Round; g.DrawLine(lp, x - half, lidY, x + half, lidY); }
        }

        // An open mouth like a D (w wide, h deep, its straight top at y), the top teeth along it and the tongue below.
        static void ToothyMouth(Graphics g, float mx, float y, float w, float h, Color ink, float lw)
        {
            using (GraphicsPath m = new GraphicsPath())
            {
                m.AddArc(mx - w / 2, y - h, w, h * 2, 0, 180);
                m.CloseFigure();
                MascotParts.FillSolid(g, m, Color.FromArgb(120, 36, 44));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                MascotParts.Ellipse(g, Color.White, mx - w / 2, y - h * 0.55f, w, h * 0.85f);
                MascotParts.Ellipse(g, Color.FromArgb(240, 116, 126), mx - w * 0.3f, y + h * 0.45f, w * 0.6f, h * 0.7f);
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, m, ink, lw);
            }
        }

        // A little "w" of a mouth at (mx, my), each half hw wide.
        static void CatMouth(Graphics g, Pen p, float mx, float my, float hw)
        {
            g.DrawArc(p, mx - hw, my - hw * 0.45f, hw, hw * 0.8f, 15, 150);
            g.DrawArc(p, mx, my - hw * 0.45f, hw, hw * 0.8f, 15, 150);
        }

        // Spiral eyes, dizzy, r across at the most.
        void DizzyEye(Graphics g, Pen p, float x, float y, float r, int i)
        {
            PointF[] pts = new PointF[24];
            double rot = now / 120.0 * (i == 0 ? 1 : -1);
            for (int k = 0; k < pts.Length; k++)
            {
                double a = rot + k * 0.5, rr = r * (0.06 + k * 0.04);
                pts[k] = new PointF(x + (float)(Math.Cos(a) * rr), y + (float)(Math.Sin(a) * rr));
            }
            g.DrawCurve(p, pts);
        }

        // Chopper's whole face, every mood, sized for his eyes and mouth.
        bool ChopperFace(Graphics g, float D, MascotParts.Geo geo, float ex, float ey, float blink)
        {
            Color ink = Color.FromArgb(26, 22, 24);
            float gap = geo.EyeGap * D, lw = Math.Max(1.1f, D * 0.012f);
            using (Pen p = new Pen(ink, lw))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -gap : gap) + ex, y = ey;
                    Mood m = mood == Mood.Wink && i == 1 ? Mood.Sleep : mood;
                    switch (m)
                    {
                        case Mood.Happy:
                        case Mood.Wink: g.DrawArc(p, x - 0.04f * D, y - 0.018f * D, 0.08f * D, 0.056f * D, 200, 140); break;
                        case Mood.Sleep:
                        case Mood.Yawn: g.DrawArc(p, x - 0.038f * D, y - 0.03f * D, 0.076f * D, 0.044f * D, 20, 140); break;
                        case Mood.Love:
                            using (GraphicsPath hp = MascotParts.Heart(x, y, D * 0.1f)) MascotParts.FillSolid(g, hp, LoveColor);
                            break;
                        case Mood.Dizzy: DizzyEye(g, p, x, y, 0.045f * D, i); break;
                        case Mood.Surprised: ChopperEye(g, x, y, D, 1.1f, 1, 0.5f); break;
                        default: ChopperEye(g, x, y, D, 1, blink, 0.8f); break;
                    }
                }
                float mx = ex * 0.6f, my = geo.FaceY * D + MascotParts.ToonMouthDy(MascotParts.KindChopper) * D;
                bool talking = Bubble != null && mood != Mood.Sleep && mood != Mood.Dizzy;
                if (mood == Mood.Yawn)
                {
                    double o = Math.Sin(Math.Min(1, (moodUntil - now) / 1900.0) * Math.PI);
                    float r = D * (0.016f + 0.02f * (float)o);
                    MascotParts.Ellipse(g, Color.FromArgb(120, 36, 44), mx - r * 0.8f, my - r * 0.5f, r * 1.6f, r * 2);
                }
                else if (mood == Mood.Surprised || mood == Mood.Dizzy)
                {
                    float r = D * 0.014f;
                    MascotParts.Ellipse(g, Color.FromArgb(120, 36, 44), mx - r, my - r * 0.4f, r * 2, r * 2.3f);
                }
                else if (talking || mood == Mood.Happy || mood == Mood.Love)
                {
                    float h = 0.044f * D;
                    if (talking && mood == Mood.Idle) h *= (float)(0.45 + 0.55 * Math.Abs(Math.Sin(now / 110.0)));
                    ToothyMouth(g, mx, my, 0.078f * D, h, ink, lw * 0.9f);
                }
                else
                {
                    // The line down from his nose and his little "w".
                    g.DrawLine(p, mx, my - 0.022f * D, mx, my + 0.002f * D);
                    CatMouth(g, p, mx, my + 0.002f * D, (mood == Mood.Sleep ? 0.026f : 0.034f) * D);
                }
            }
            return true;
        }

        // Kero's whole face: tiny dot eyes far apart, and under his tiny nose a "w" that opens tall, its top lip in two
        // little curves, the pink tongue filling it.
        bool KeroFace(Graphics g, float D, MascotParts.Geo geo, float ex, float ey, float blink)
        {
            Color ink = Color.FromArgb(46, 30, 28);
            float gap = geo.EyeGap * D, lw = Math.Max(1f, D * 0.0095f);
            using (Pen p = new Pen(ink, lw))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0 ? -gap : gap) + ex, y = ey;
                    Mood m = mood == Mood.Wink && i == 1 ? Mood.Sleep : mood;
                    switch (m)
                    {
                        case Mood.Happy:
                        case Mood.Wink: g.DrawArc(p, x - 0.026f * D, y - 0.012f * D, 0.052f * D, 0.036f * D, 200, 140); break;
                        case Mood.Sleep:
                        case Mood.Yawn: g.DrawArc(p, x - 0.024f * D, y - 0.02f * D, 0.048f * D, 0.03f * D, 20, 140); break;
                        case Mood.Love:
                            using (GraphicsPath hp = MascotParts.Heart(x, y, D * 0.055f)) MascotParts.FillSolid(g, hp, LoveColor);
                            break;
                        case Mood.Dizzy: DizzyEye(g, p, x, y, 0.028f * D, i); break;
                        default:
                        {
                            float sk = m == Mood.Surprised ? 1.4f : 1f, w = 0.025f * D * sk, h = 0.033f * D * sk * (m == Mood.Surprised ? 1 : blink);
                            if (h < 0.009f * D) g.DrawLine(p, x - 0.015f * D, y, x + 0.015f * D, y);
                            else MascotParts.Ellipse(g, ink, x - w / 2, y - h / 2, w, h);
                            break;
                        }
                    }
                }
                float mx = ex * 0.6f, my = geo.FaceY * D + MascotParts.ToonMouthDy(MascotParts.KindKeroSd) * D;
                bool talking = Bubble != null && mood != Mood.Sleep && mood != Mood.Dizzy;
                if (mood == Mood.Surprised || mood == Mood.Dizzy)
                {
                    float r = D * 0.012f;
                    MascotParts.Ellipse(g, Color.FromArgb(166, 58, 70), mx - r, my - r * 0.2f, r * 2, r * 2.4f);
                }
                else if (talking || mood == Mood.Happy || mood == Mood.Love || mood == Mood.Yawn)
                {
                    float h = 0.08f * D;
                    if (mood == Mood.Yawn) h = D * (0.05f + 0.045f * (float)Math.Sin(Math.Min(1, (moodUntil - now) / 1900.0) * Math.PI));
                    else if (talking && mood == Mood.Idle) h *= (float)(0.5 + 0.5 * Math.Abs(Math.Sin(now / 110.0)));
                    KeroMouth(g, mx, my, 0.064f * D, h, ink, lw);
                }
                else CatMouth(g, p, mx, my, (mood == Mood.Sleep ? 0.018f : 0.024f) * D);
            }
            return true;
        }

        static void KeroMouth(Graphics g, float mx, float my, float w, float h, Color ink, float lw)
        {
            float top = my - w * 0.1f;
            using (GraphicsPath m = new GraphicsPath())
            {
                float dip = Math.Min(h * 0.18f, w * 0.2f);
                m.AddBezier(new PointF(mx - w / 2, top), new PointF(mx - w * 0.4f, top + dip), new PointF(mx - w * 0.1f, top + dip), new PointF(mx, top + dip * 0.15f));
                m.AddBezier(new PointF(mx, top + dip * 0.15f), new PointF(mx + w * 0.1f, top + dip), new PointF(mx + w * 0.4f, top + dip), new PointF(mx + w / 2, top));
                m.AddBezier(new PointF(mx + w / 2, top), new PointF(mx + w * 0.56f, top + h * 0.6f), new PointF(mx + w * 0.36f, top + h), new PointF(mx, top + h));
                m.AddBezier(new PointF(mx, top + h), new PointF(mx - w * 0.36f, top + h), new PointF(mx - w * 0.56f, top + h * 0.6f), new PointF(mx - w / 2, top));
                m.CloseFigure();
                MascotParts.FillSolid(g, m, Color.FromArgb(166, 58, 70));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                MascotParts.Ellipse(g, Color.FromArgb(240, 130, 148), mx - w * 0.42f, top + h * 0.38f, w * 0.84f, h * 0.8f);
                g.Clip = old;
                old.Dispose();
                using (Pen p = new Pen(ink, lw)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, m); }
            }
        }
    }
}

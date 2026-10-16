// Stackshot - Full figures, first batch: Naruto (spiky blond hair, the Leaf headband, whisker marks, the orange and black
// tracksuit), Sasuke (dark hair spiking out at the back, long side bangs, the high-collared navy shirt), Kakashi (silver
// hair swept to one side, the headband over one eye, the mask, the green flak vest), Luffy (the straw hat, the scar
// under his eye, the open red vest and blue shorts), Zoro (short green hair, three earrings, the green sash and three
// swords), Tanjiro (dark red hair, the forehead scar, the hanafuda earrings, the green and black checked haori) and
// Nezuko (long black hair ending in orange, the pink ribbon, the bamboo muzzle, the pink kimono). Fan tributes, not
// affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindNaruto = 80, KindSasuke = 81, KindKakashi = 82, KindLuffy = 83, KindZoro = 84, KindTanjiro = 85, KindNezuko = 86;

        static Fig[] BuildFigs()
        {
            List<Fig> f = new List<Fig>();
            f.Add(F(NarutoBack, NarutoFront, Color.FromArgb(60, 140, 230), EyeRound, MouthGrin));
            f.Add(F(SasukeBack, SasukeFront, Color.FromArgb(40, 40, 54), EyeSharp, MouthLine));
            Fig k = F(KakashiBack, KakashiFront, Color.FromArgb(70, 70, 82), EyeLidded, MouthNone);
            k.Blush = false;
            k.OnHead = KakashiMask;
            f.Add(k);
            f.Add(F(LuffyBack, LuffyFront, Color.FromArgb(40, 36, 40), EyeRound, MouthGrin));
            Fig z = F(ZoroBack, ZoroFront, Color.FromArgb(50, 50, 56), EyeSharp, MouthLine);
            z.Blush = false;
            f.Add(z);
            f.Add(F(TanjiroBack, TanjiroFront, Color.FromArgb(160, 50, 44), EyeRound, MouthSmile));
            f.Add(F(NezukoBack, NezukoFront, Color.FromArgb(236, 120, 170), EyeRound, MouthNone));
            AddFigures2(f);
            return f.ToArray();
        }

        static readonly Color Orange = Color.FromArgb(248, 132, 36), NinjaBlue = Color.FromArgb(60, 90, 180);

        // ---- Naruto
        static void NarutoBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // The headband's tails behind his head, fluttering.
            float fl = (float)Math.Sin(p.Now / 300.0) * 0.012f;
            FLimb(g, D, Color.FromArgb(46, 58, 104), 0.03f, 0.22f, -0.18f, 0.34f, -0.06f + fl);
            FLimb(g, D, Color.FromArgb(46, 58, 104), 0.03f, 0.22f, -0.17f, 0.31f, -0.0f - fl);
            FigClothes o = Wear(Orange, Orange, Orange, NinjaBlue);
            FigBody(g, D, p, skin, skin2, o);
            // The black shoulders and collar of his jacket, its white collar edge and zip.
            using (GraphicsPath yoke = KPoly(D, -0.1f, 0.11f, 0.1f, 0.11f, 0.107f, 0.155f, -0.107f, 0.155f)) FigShape(g, yoke, Color.FromArgb(54, 52, 60), Color.FromArgb(30, 28, 34), D);
            using (GraphicsPath col = Theme.Round(new RectangleF(-0.06f * D, 0.095f * D, 0.12f * D, 0.03f * D), 0.012f * D)) FigShape(g, col, Color.FromArgb(246, 244, 240), Color.FromArgb(214, 210, 204), D);
            Line(g, Color.FromArgb(200, 70, 60, 50), D * 0.008f, 0, 0.155f * D, 0, 0.245f * D);
            Ellipse(g, Color.FromArgb(230, 226, 70, 40), 0.03f * D, 0.17f * D, 0.035f * D, 0.035f * D);   // the red swirl on his jacket
        }

        static void NarutoFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            float[][] tips =
            {
                new float[] { -104, 0.09f, 0.3f }, new float[] { -74, 0.17f, 0.3f }, new float[] { -46, 0.21f, 0.22f }, new float[] { -18, 0.12f, 0.1f },
                new float[] { 6, 0.22f, 0.05f }, new float[] { 32, 0.14f, 0.1f }, new float[] { 58, 0.21f, 0.22f }, new float[] { 84, 0.15f, 0.3f },
                new float[] { 108, 0.08f, 0.3f }
            };
            float[][] bangs =
            {
                new float[] { 0.25f, -0.06f }, new float[] { 0.2f, -0.17f }, new float[] { 0.155f, -0.1f }, new float[] { 0.09f, -0.18f }, new float[] { 0.03f, -0.105f },
                new float[] { -0.035f, -0.18f }, new float[] { -0.095f, -0.105f }, new float[] { -0.16f, -0.18f }, new float[] { -0.205f, -0.1f }, new float[] { -0.25f, -0.06f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.12f, 0.27f, 0.24f, tips, bangs)) HairShape(g, h, Color.FromArgb(255, 222, 80), Color.FromArgb(226, 156, 24), D);
            Headband(g, D, -0.185f, 0, 0);
            // The two clumps of bangs that fall over the band, either side of the plate.
            FigLock(g, D, -0.15f, -0.24f, -0.2f, -0.09f, 0.075f, 0.012f, Color.FromArgb(255, 222, 80), Color.FromArgb(226, 156, 24));
            FigLock(g, D, 0.15f, -0.24f, 0.21f, -0.1f, 0.075f, -0.012f, Color.FromArgb(255, 222, 80), Color.FromArgb(226, 156, 24));
            FigLock(g, D, -0.205f, -0.22f, -0.245f, -0.12f, 0.05f, 0.01f, Color.FromArgb(255, 222, 80), Color.FromArgb(226, 156, 24));
            // Three whisker marks on each cheek.
            using (Pen w = new Pen(Color.FromArgb(200, 70, 50, 40), Math.Max(1f, D * 0.008f)))
            {
                w.StartCap = w.EndCap = LineCap.Round;
                foreach (int s in Sides)
                    for (int i = 0; i < 3; i++)
                        g.DrawLine(w, s * 0.135f * D, (-0.025f + i * 0.025f) * D, s * 0.215f * D, (-0.035f + i * 0.03f) * D);
            }
        }

        // ---- Sasuke
        static void SasukeBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            Color h1 = Color.FromArgb(60, 64, 96), h2 = Color.FromArgb(18, 20, 34);
            // The spikes at the back of his head, sweeping up and out.
            float[][] tips =
            {
                new float[] { -76, 0.14f, -0.1f }, new float[] { -50, 0.19f, -0.1f }, new float[] { -24, 0.21f, -0.05f }, new float[] { 2, 0.2f, 0 },
                new float[] { 28, 0.21f, -0.05f }, new float[] { 54, 0.19f, -0.1f }, new float[] { 80, 0.14f, -0.1f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.14f, 0.25f, 0.24f, tips, new float[][] { new float[] { 0.2f, -0.2f }, new float[] { -0.2f, -0.2f } })) HairShape(g, h, h1, h2, D);
            FigClothes o = Wear(Color.FromArgb(62, 74, 128), Color.FromArgb(232, 230, 222), Color.FromArgb(232, 230, 222), NinjaBlue);
            o.Shorts = true;
            o.Neck = Color.FromArgb(62, 74, 128);
            // The high collar standing up behind his jaw.
            using (GraphicsPath col = KPoly(D, -0.13f, 0.05f, 0.13f, 0.05f, 0.11f, 0.14f, -0.11f, 0.14f)) FigShape(g, col, Color.FromArgb(76, 88, 144), Color.FromArgb(44, 52, 96), D);
            FigBody(g, D, p, skin, skin2, o);
        }

        static void SasukeFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            float[][] tips =
            {
                new float[] { -100, 0.05f, 0 }, new float[] { -70, 0.05f, 0 }, new float[] { -40, 0.05f, 0 }, new float[] { -12, 0.05f, 0 },
                new float[] { 14, 0.05f, 0 }, new float[] { 42, 0.05f, 0 }, new float[] { 72, 0.05f, 0 }, new float[] { 102, 0.05f, 0 }
            };
            // Long locks down both sides of the face and pointed bangs over the forehead.
            float[][] bangs =
            {
                new float[] { 0.265f, 0.06f }, new float[] { 0.21f, -0.09f }, new float[] { 0.155f, -0.165f }, new float[] { 0.095f, -0.085f }, new float[] { 0.04f, -0.175f },
                new float[] { -0.005f, -0.1f }, new float[] { -0.055f, -0.175f }, new float[] { -0.105f, -0.085f }, new float[] { -0.16f, -0.165f }, new float[] { -0.21f, -0.09f },
                new float[] { -0.265f, 0.06f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.13f, 0.27f, 0.23f, tips, bangs)) HairShape(g, h, Color.FromArgb(60, 64, 96), Color.FromArgb(18, 20, 34), D);
            Headband(g, D, -0.2f, 0, 0);
            // The long locks that frame his face down to the jaw.
            FigLock(g, D, -0.2f, -0.2f, -0.235f, 0.06f, 0.075f, 0.02f, Color.FromArgb(60, 64, 96), Color.FromArgb(18, 20, 34));
            FigLock(g, D, 0.2f, -0.2f, 0.235f, 0.06f, 0.075f, -0.02f, Color.FromArgb(60, 64, 96), Color.FromArgb(18, 20, 34));
            FigLock(g, D, -0.03f, -0.21f, -0.06f, -0.11f, 0.05f, 0.005f, Color.FromArgb(60, 64, 96), Color.FromArgb(18, 20, 34));
        }

        // ---- Kakashi
        static readonly Color KakashiNavy = Color.FromArgb(46, 52, 80);

        static void KakashiBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            FigClothes o = Wear(Color.FromArgb(118, 136, 86), KakashiNavy, KakashiNavy, NinjaBlue);
            o.Neck = KakashiNavy;
            FigBody(g, D, p, skin, skin2, o);
            // The flak vest's two chest pockets and its collar.
            foreach (int s in Sides)
                using (GraphicsPath pk = Theme.Round(new RectangleF(s * 0.055f * D - 0.035f * D, 0.16f * D, 0.07f * D, 0.05f * D), 0.01f * D)) FigShape(g, pk, Color.FromArgb(130, 150, 96), Color.FromArgb(96, 112, 66), D);
            using (GraphicsPath col = Theme.Round(new RectangleF(-0.09f * D, 0.1f * D, 0.18f * D, 0.035f * D), 0.015f * D)) FigShape(g, col, Color.FromArgb(130, 150, 96), Color.FromArgb(96, 112, 66), D);
        }

        // The mask over his nose, mouth and neck, painted on the head itself (it does not slide with the gaze).
        static void KakashiMask(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            using (SolidBrush b = new SolidBrush(KakashiNavy)) g.FillRectangle(b, -0.4f * D, -0.025f * D, 0.8f * D, 0.4f * D);
            using (Pen ridge = new Pen(Color.FromArgb(120, 20, 22, 36), Math.Max(1f, D * 0.008f))) g.DrawArc(ridge, -0.025f * D, -0.03f * D, 0.05f * D, 0.06f * D, 200, 140);
            Line(g, FInk, D * 0.012f, -0.3f * D, -0.025f * D, 0.3f * D, -0.025f * D);
        }

        static void KakashiFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // Silver hair swept up and over to his left.
            float[][] tips =
            {
                new float[] { -96, 0.14f, 0, -18 }, new float[] { -70, 0.2f, 0, -24 }, new float[] { -44, 0.24f, 0, -26 }, new float[] { -18, 0.25f, 0, -28 },
                new float[] { 8, 0.24f, 0, -30 }, new float[] { 34, 0.21f, 0, -30 }, new float[] { 60, 0.16f, 0, -26 }, new float[] { 88, 0.1f, 0, -20 }
            };
            float[][] line = { new float[] { 0.25f, -0.1f }, new float[] { 0.15f, -0.2f }, new float[] { 0, -0.21f }, new float[] { -0.15f, -0.2f }, new float[] { -0.25f, -0.1f } };
            using (GraphicsPath h = SpikeHair(D, -0.14f, 0.26f, 0.23f, tips, line)) HairShape(g, h, Color.FromArgb(232, 236, 242), Color.FromArgb(164, 170, 186), D);
            // The headband slanted down over his left eye.
            Headband(g, D, -0.11f, 24, 0.0f, 1.3f);
        }

        // ---- Luffy
        static void LuffyBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            FigClothes o = Wear(Color.FromArgb(214, 44, 44), skin, Color.FromArgb(64, 104, 190), Color.FromArgb(150, 110, 70));
            o.BareArms = true;
            o.Shorts = true;
            FigBody(g, D, p, skin, skin2, o);
            // The vest hangs open over his chest, with its yellow buttons; white cuffs on his shorts.
            using (GraphicsPath ch = KPoly(D, -0.035f, 0.11f, 0.035f, 0.11f, 0.028f, 0.25f, -0.028f, 0.25f)) FigShape(g, ch, skin, skin2, D);
            for (int i = 0; i < 3; i++) Ellipse(g, Color.FromArgb(244, 206, 60), -0.06f * D, (0.14f + i * 0.035f) * D, 0.018f * D, 0.018f * D);
            using (GraphicsPath cuff = Theme.Round(new RectangleF(-0.105f * D, 0.27f * D, 0.21f * D, 0.025f * D), 0.01f * D)) FigShape(g, cuff, Color.FromArgb(246, 244, 238), Color.FromArgb(210, 206, 198), D);
        }

        static void LuffyFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // Messy black hair, then the straw hat on top.
            float[][] tips =
            {
                new float[] { -112, 0.06f, 0 }, new float[] { -86, 0.08f, 0.1f }, new float[] { -60, 0.07f, 0.1f }, new float[] { 60, 0.07f, 0.1f },
                new float[] { 86, 0.08f, 0.1f }, new float[] { 112, 0.06f, 0 }
            };
            float[][] bangs =
            {
                new float[] { 0.26f, -0.03f }, new float[] { 0.2f, -0.15f }, new float[] { 0.15f, -0.11f }, new float[] { 0.1f, -0.17f }, new float[] { 0.04f, -0.12f },
                new float[] { -0.02f, -0.18f }, new float[] { -0.07f, -0.115f }, new float[] { -0.13f, -0.17f }, new float[] { -0.18f, -0.11f }, new float[] { -0.26f, -0.03f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.13f, 0.27f, 0.23f, tips, bangs)) HairShape(g, h, Color.FromArgb(52, 48, 56), Color.FromArgb(16, 14, 20), D);
            Color straw = Color.FromArgb(244, 214, 116), straw2 = Color.FromArgb(206, 166, 70);
            RectangleF brim = new RectangleF(-0.37f * D, -0.3f * D, 0.74f * D, 0.11f * D);
            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddEllipse(brim);
                FigShape(g, bp, straw, straw2, D);
                using (Pen weave = new Pen(Color.FromArgb(110, 150, 110, 40), Math.Max(1f, D * 0.006f)))
                    g.DrawEllipse(weave, RectangleF.Inflate(brim, -0.05f * D, -0.016f * D));
            }
            using (GraphicsPath cr = new GraphicsPath())
            {
                cr.AddArc(-0.19f * D, -0.46f * D, 0.38f * D, 0.42f * D, 180, 180);
                cr.CloseFigure();
                Fill(g, cr, cr.GetBounds(), straw, straw2, 90f);
                Region old = g.Clip;
                g.SetClip(cr, CombineMode.Intersect);
                using (SolidBrush band = new SolidBrush(Color.FromArgb(206, 40, 40))) g.FillRectangle(band, -0.2f * D, -0.31f * D, 0.4f * D, 0.06f * D);
                g.Clip = old;
                old.Dispose();
                FigLine(g, cr, D);
                Ellipse(g, Color.FromArgb(90, 255, 255, 255), -0.12f * D, -0.43f * D, 0.1f * D, 0.05f * D);
            }
            // The scar under his left eye, with its two stitches.
            using (Pen sc = new Pen(Color.FromArgb(200, 120, 50, 50), Math.Max(1f, D * 0.008f)))
            {
                sc.StartCap = sc.EndCap = LineCap.Round;
                g.DrawArc(sc, 0.07f * D, -0.005f * D, 0.07f * D, 0.03f * D, 20, 140);
                g.DrawLine(sc, 0.09f * D, 0.012f * D, 0.088f * D, 0.035f * D);
                g.DrawLine(sc, 0.115f * D, 0.012f * D, 0.117f * D, 0.035f * D);
            }
        }

        // ---- Zoro
        static void ZoroBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // His three swords at his left hip.
            Color[] sheath = { Color.FromArgb(240, 240, 236), Color.FromArgb(40, 36, 40), Color.FromArgb(150, 30, 34) };
            for (int i = 0; i < 3; i++)
            {
                float o = i * 0.022f;
                FLimb(g, D, sheath[i], 0.026f, 0.06f + o, 0.17f + o, 0.32f + o, 0.33f + o * 0.5f);
                Ellipse(g, Color.FromArgb(220, 190, 140, 40), (0.075f + o) * D - 0.016f * D, (0.18f + o) * D - 0.016f * D, 0.032f * D, 0.032f * D);
            }
            FigClothes o2 = Wear(Color.FromArgb(242, 242, 238), skin, Color.FromArgb(44, 44, 52), Color.FromArgb(30, 30, 36));
            o2.BareArms = true;
            FigBody(g, D, p, skin, skin2, o2);
            // The short sleeves, the green haramaki and the black bandana tied on his arm.
            foreach (int s in Sides)
                using (GraphicsPath sl = new GraphicsPath())
                {
                    sl.AddEllipse(s * 0.11f * D - 0.035f * D, 0.12f * D, 0.07f * D, 0.06f * D);
                    FigShape(g, sl, Color.FromArgb(242, 242, 238), Color.FromArgb(210, 210, 204), D);
                }
            using (GraphicsPath hm = Theme.Round(new RectangleF(-0.123f * D, 0.195f * D, 0.246f * D, 0.06f * D), 0.02f * D)) FigShape(g, hm, Color.FromArgb(96, 156, 84), Color.FromArgb(56, 110, 54), D);
            FLimb(g, D, Color.FromArgb(34, 32, 38), 0.05f, 0.14f, 0.165f, 0.155f, 0.172f);
        }

        static void ZoroFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // Short green hair, cropped close.
            float[][] tips = new float[11][];
            for (int i = 0; i < 11; i++) tips[i] = new float[] { -105 + i * 21, 0.035f, 0.2f };
            float[][] line =
            {
                new float[] { 0.25f, -0.09f }, new float[] { 0.19f, -0.19f }, new float[] { 0.1f, -0.2f }, new float[] { 0.03f, -0.185f }, new float[] { -0.04f, -0.2f },
                new float[] { -0.12f, -0.19f }, new float[] { -0.19f, -0.19f }, new float[] { -0.25f, -0.09f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.13f, 0.265f, 0.22f, tips, line)) HairShape(g, h, Color.FromArgb(130, 196, 112), Color.FromArgb(54, 116, 56), D);
            // Three gold earrings on his left ear.
            for (int i = 0; i < 3; i++) Ellipse(g, Gold1, (0.258f + i * 0.012f) * D - 0.008f * D, (0.005f + i * 0.008f) * D, 0.016f * D, 0.032f * D);
            // Stern brows.
            foreach (int s in Sides) Line(g, FInk, D * 0.016f, s * 0.14f * D, -0.135f * D, s * 0.045f * D, -0.115f * D);
        }

        // ---- Tanjiro
        static void TanjiroBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // The sword at his left hip, then the black uniform under the checked haori.
            FLimb(g, D, Color.FromArgb(34, 30, 36), 0.03f, 0.06f, 0.2f, 0.33f, 0.31f);
            Ellipse(g, Color.FromArgb(220, 60, 50, 50), 0.07f * D, 0.19f * D, 0.035f * D, 0.035f * D);
            Color green = Color.FromArgb(54, 128, 92), black = Color.FromArgb(36, 34, 40);
            FigClothes o = Wear(black, green, black, Color.FromArgb(206, 186, 146));
            o.Legs = Color.FromArgb(234, 232, 226);
            FigBody(g, D, p, skin, skin2, o);
            foreach (int s in Sides)
                using (GraphicsPath hp = KPoly(D, s * 0.122f, 0.11f, s * 0.035f, 0.11f, s * 0.028f, 0.262f, s * 0.14f, 0.27f))
                {
                    Fill(g, hp, hp.GetBounds(), green, Darker(green, 0.2), 90f);
                    Checks(g, hp, D, black);
                    FigLine(g, hp, D);
                }
            Line(g, Color.FromArgb(230, 232, 228), D * 0.012f, -0.03f * D, 0.22f * D, 0.03f * D, 0.22f * D);   // the white belt
        }

        // A checked pattern of dark squares, clipped to the shape.
        static void Checks(Graphics g, GraphicsPath p, float D, Color c)
        {
            RectangleF r = p.GetBounds();
            Region old = g.Clip;
            g.SetClip(p, CombineMode.Intersect);
            float s = 0.032f * D;
            using (SolidBrush b = new SolidBrush(c))
                for (int i = 0; i * s < r.Width + s; i++)
                    for (int j = 0; j * s < r.Height + s; j++)
                        if ((i + j) % 2 == 0) g.FillRectangle(b, r.X + i * s, r.Y + j * s, s, s);
            g.Clip = old;
            old.Dispose();
        }

        static void TanjiroFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            float[][] tips =
            {
                new float[] { -106, 0.07f, 0.05f }, new float[] { -80, 0.1f, 0.1f }, new float[] { -54, 0.11f, 0.15f }, new float[] { -28, 0.11f, 0.1f },
                new float[] { -2, 0.1f, 0 }, new float[] { 24, 0.11f, 0.1f }, new float[] { 50, 0.11f, 0.15f }, new float[] { 76, 0.1f, 0.1f }, new float[] { 104, 0.07f, 0.05f }
            };
            float[][] bangs =
            {
                new float[] { 0.255f, -0.05f }, new float[] { 0.2f, -0.16f }, new float[] { 0.15f, -0.12f }, new float[] { 0.1f, -0.185f }, new float[] { 0.03f, -0.13f },
                new float[] { 0.0f, -0.185f }, new float[] { -0.04f, -0.12f }, new float[] { -0.1f, -0.18f }, new float[] { -0.16f, -0.12f }, new float[] { -0.21f, -0.165f },
                new float[] { -0.255f, -0.05f }
            };
            using (GraphicsPath h = SpikeHair(D, -0.13f, 0.27f, 0.24f, tips, bangs)) HairShape(g, h, Color.FromArgb(186, 56, 44), Color.FromArgb(70, 20, 24), D);
            // The scar on his forehead, like a flame.
            using (GraphicsPath sc = KCurve(D, 0.5f, 0.085f, -0.185f, 0.12f, -0.2f, 0.145f, -0.175f, 0.13f, -0.15f, 0.1f, -0.145f))
                FillSolid(g, sc, Color.FromArgb(190, 150, 50, 44));
            // Hanafuda earrings: white cards with a red sun.
            foreach (int s in Sides)
            {
                RectangleF c = new RectangleF(s * 0.262f * D - 0.022f * D, 0.0f, 0.044f * D, 0.075f * D);
                using (GraphicsPath cp = Theme.Round(c, 0.006f * D)) FigShape(g, cp, Color.FromArgb(250, 248, 240), Color.FromArgb(220, 214, 200), D);
                Ellipse(g, Color.FromArgb(220, 40, 40), c.X + c.Width * 0.22f, c.Y + c.Height * 0.25f, c.Width * 0.56f, c.Width * 0.56f);
                Line(g, Color.FromArgb(160, 40, 36, 40), D * 0.004f, c.X + c.Width * 0.2f, c.Bottom - c.Height * 0.2f, c.Right - c.Width * 0.2f, c.Bottom - c.Height * 0.2f);
            }
        }

        // ---- Nezuko
        static void NezukoBack(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // Her long hair down her back, black turning orange at the ends.
            using (GraphicsPath h = KCurve(D, 0.5f, -0.3f, -0.1f, -0.33f, 0.12f, -0.28f, 0.28f, 0, 0.31f, 0.28f, 0.28f, 0.33f, 0.12f, 0.3f, -0.1f, 0.2f, -0.34f, 0, -0.38f, -0.2f, -0.34f))
            {
                RectangleF r = h.GetBounds();
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Color.FromArgb(40, 30, 38), Color.FromArgb(232, 122, 64), 90f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[] { Color.FromArgb(40, 30, 38), Color.FromArgb(40, 30, 38), Color.FromArgb(232, 122, 64) };
                    cb.Positions = new float[] { 0, 0.62f, 1 };
                    b.InterpolationColors = cb;
                    g.FillPath(b, h);
                }
                FigLine(g, h, D);
            }
            Color pink = Color.FromArgb(244, 176, 194), black = Color.FromArgb(40, 36, 42);
            FigClothes o = Wear(pink, black, pink, Color.FromArgb(206, 186, 146));
            o.Legs = Color.FromArgb(242, 240, 236);
            FigBody(g, D, p, skin, skin2, o);
            // The black haori open over her pink kimono, a dark obi, and a few hemp-leaf marks on the kimono.
            foreach (int s in Sides)
                using (GraphicsPath hp = KPoly(D, s * 0.122f, 0.11f, s * 0.04f, 0.11f, s * 0.035f, 0.262f, s * 0.14f, 0.27f)) FigShape(g, hp, Lighter(black, 0.1), black, D);
            using (GraphicsPath obi = Theme.Round(new RectangleF(-0.04f * D, 0.19f * D, 0.08f * D, 0.04f * D), 0.01f * D)) FigShape(g, obi, Color.FromArgb(120, 40, 50), Color.FromArgb(80, 24, 34), D);
            using (Pen st = new Pen(Color.FromArgb(150, 196, 96, 128), Math.Max(1f, D * 0.005f)))
                for (int i = 0; i < 3; i++)
                {
                    float cy = (0.13f + i * 0.04f) * D;
                    g.DrawLine(st, -0.02f * D, cy, 0.02f * D, cy);
                    g.DrawLine(st, -0.01f * D, cy - 0.015f * D, 0.01f * D, cy + 0.015f * D);
                    g.DrawLine(st, 0.01f * D, cy - 0.015f * D, -0.01f * D, cy + 0.015f * D);
                }
        }

        static void NezukoFront(Graphics g, float D, MascotPose p, Color skin, Color skin2)
        {
            // Straight bangs and long side locks framing her face.
            using (GraphicsPath h = KCurve(D, 0.3f, -0.27f, 0.08f, -0.29f, -0.18f, -0.24f, -0.33f, 0, -0.37f, 0.24f, -0.33f, 0.29f, -0.18f, 0.27f, 0.08f,
                                           0.22f, 0.04f, 0.21f, -0.15f, 0.14f, -0.175f, 0.08f, -0.14f, 0.02f, -0.175f, -0.04f, -0.14f, -0.1f, -0.175f,
                                           -0.16f, -0.14f, -0.21f, -0.15f, -0.22f, 0.04f))
                HairShape(g, h, Color.FromArgb(58, 44, 56), Color.FromArgb(28, 20, 28), D);
            // The pink ribbon in her hair.
            GraphicsState st = g.Save();
            g.TranslateTransform(0.17f * D, -0.31f * D);
            g.RotateTransform(20);
            BowTie(g, D * 0.9f, 0, Color.FromArgb(250, 150, 186), Color.FromArgb(214, 90, 136));
            g.Restore(st);
            // The bamboo muzzle, tied behind with red cords.
            float my = 0.065f * D;
            Line(g, Color.FromArgb(200, 170, 50, 40), D * 0.008f, -0.12f * D, my, -0.25f * D, my - 0.02f * D);
            Line(g, Color.FromArgb(200, 170, 50, 40), D * 0.008f, 0.12f * D, my, 0.25f * D, my - 0.02f * D);
            RectangleF bam = new RectangleF(-0.12f * D, my - 0.024f * D, 0.24f * D, 0.048f * D);
            using (GraphicsPath bp = Theme.Round(bam, 0.022f * D))
            {
                FigShape(g, bp, Color.FromArgb(150, 196, 96), Color.FromArgb(82, 130, 56), D);
                Ellipse(g, Color.FromArgb(90, 255, 255, 255), bam.X + bam.Width * 0.1f, bam.Y + bam.Height * 0.15f, bam.Width * 0.5f, bam.Height * 0.25f);
            }
            foreach (int s in Sides) Line(g, Color.FromArgb(200, 60, 90, 40), D * 0.008f, s * 0.06f * D, bam.Y, s * 0.06f * D, bam.Bottom);
        }
    }
}

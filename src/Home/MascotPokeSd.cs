// Stackshot - The Pokemon as little 3D figures (the soft modelled look of the Shin-chan ones), each with its own body as
// the games' models have it instead of the round plush: Pikachu (the long black-tipped ears, red cheeks, the bolt of a
// tail), Eevee (the big ears, the cream ruff, the bushy tail, on four legs), Charmander (the cream belly, the flame at the
// end of its tail), Bulbasaur (the bulb on its back, the spots, red eyes, on four legs), Squirtle (the shell and its
// plates, the curled tail), Jigglypuff (the round pink ball with its curl and big teal eyes), Snorlax (sitting, huge,
// asleep, the cream belly and the soles of its feet), Gengar (the spikes, the red eyes and the grin), Mew (floating, its
// long thin tail), Psyduck (holding its head, the dazed eyes, the bill), Togepi (out of its spotted shell, the crown of
// spikes), Meowth (the gold coin, the long whiskers, the curled tail), Piplup (the white face, the beak with its little
// crown, the flippers), Mimikyu (the rag costume with its face drawn on and its stick of a tail, its own eyes peeping out
// from under it), Lucario (the black mask and snout, the four tassels, the cream chest with its spike, black paws), Dragonite
// (the antennae and horn, the striped cream belly, the little wings), Ditto (the purple blob with its dot eyes) and Mewtwo
// (the blunt horns, the tube to its back, the purple belly and tail, the round fingertips). Fan tributes, not affiliated
// with Nintendo, Game Freak or The Pokemon Company (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindPikachuSd = 140, KindEeveeSd = 141, KindCharmanderSd = 142, KindBulbasaurSd = 143, KindSquirtleSd = 144, KindJigglySd = 145,
                         KindSnorlaxSd = 146, KindGengarSd = 147, KindMewSd = 148, KindPsyduckSd = 149, KindTogepiSd = 150, KindMeowthSd = 151,
                         KindPiplupSd = 152, KindMimikyuSd = 153, KindLucarioSd = 154, KindDragoniteSd = 155, KindDittoSd = 156, KindMewtwoSd = 157, KindGrootSd = 158;
        public const int PokeFirst = 140, PokeLast = 157;

        public static bool IsPokeSd(int kind) { return kind >= PokeFirst && kind <= PokeLast; }

        // Drawn as these soft little figures: the Pokemon, and Baby Groot.
        public static bool IsSoftFig(int kind) { return IsPokeSd(kind) || kind == KindGrootSd; }

        // On four legs, or without hands of their own: what they use floats by them.
        static bool PkNoHands(int k) { return k == KindEeveeSd || k == KindBulbasaurSd || k == KindMimikyuSd || k == KindDittoSd; }

        static readonly Color PkYellow = Color.FromArgb(255, 222, 50), PkYellowShade = Color.FromArgb(228, 166, 20), PkRed = Color.FromArgb(232, 60, 48), PkRedShade = Color.FromArgb(180, 30, 26);
        static readonly Color PkBlack = Color.FromArgb(40, 36, 40), PkBrown = Color.FromArgb(150, 96, 44);
        static readonly Color EvBrown = Color.FromArgb(214, 152, 88), EvBrownShade = Color.FromArgb(164, 106, 54), EvCream = Color.FromArgb(252, 240, 210), EvCreamShade = Color.FromArgb(220, 196, 156), EvDark = Color.FromArgb(110, 66, 40);
        static readonly Color CmOrange = Color.FromArgb(248, 150, 66), CmOrangeShade = Color.FromArgb(214, 102, 36), CmCream = Color.FromArgb(252, 228, 152), CmCreamShade = Color.FromArgb(232, 194, 108);
        static readonly Color BbTeal = Color.FromArgb(122, 198, 172), BbTealShade = Color.FromArgb(80, 156, 136), BbSpot = Color.FromArgb(66, 140, 118), BbGreen = Color.FromArgb(100, 180, 96), BbGreenShade = Color.FromArgb(58, 130, 64);
        static readonly Color SqBlue = Color.FromArgb(126, 198, 236), SqBlueShade = Color.FromArgb(82, 154, 208), SqShell = Color.FromArgb(178, 112, 62), SqShellShade = Color.FromArgb(128, 74, 36), SqCream = Color.FromArgb(252, 228, 150), SqCreamShade = Color.FromArgb(224, 188, 104);
        static readonly Color JgPink = Color.FromArgb(252, 200, 214), JgPinkShade = Color.FromArgb(232, 150, 178);
        static readonly Color SlBlue = Color.FromArgb(58, 120, 146), SlBlueShade = Color.FromArgb(32, 78, 102), SlCream = Color.FromArgb(250, 238, 214), SlCreamShade = Color.FromArgb(214, 196, 164), SlPad = Color.FromArgb(146, 108, 78);
        static readonly Color GnPurple = Color.FromArgb(132, 100, 200), GnPurpleShade = Color.FromArgb(84, 58, 146);
        static readonly Color MwPink = Color.FromArgb(250, 208, 222), MwPinkShade = Color.FromArgb(226, 160, 188);
        static readonly Color PsYellow = Color.FromArgb(250, 212, 84), PsYellowShade = Color.FromArgb(222, 162, 40), PsBill = Color.FromArgb(255, 246, 216), PsBillShade = Color.FromArgb(226, 202, 152);
        static readonly Color TgCream = Color.FromArgb(252, 242, 198), TgCreamShade = Color.FromArgb(222, 200, 140), TgShell = Color.FromArgb(252, 252, 248), TgShellShade = Color.FromArgb(206, 210, 222);
        static readonly Color MeCream = Color.FromArgb(252, 240, 208), MeCreamShade = Color.FromArgb(222, 200, 150), MeBrown = Color.FromArgb(192, 136, 86), MeBrownShade = Color.FromArgb(146, 96, 54);
        static readonly Color PpBlue = Color.FromArgb(58, 126, 216), PpBlueShade = Color.FromArgb(30, 84, 166), PpLight = Color.FromArgb(158, 214, 246), PpLightShade = Color.FromArgb(104, 168, 220);
        static readonly Color PpOrange = Color.FromArgb(252, 194, 72), PpOrangeShade = Color.FromArgb(222, 142, 30);
        static readonly Color MmCloth = Color.FromArgb(234, 218, 150), MmClothShade = Color.FromArgb(192, 170, 98), MmDark = Color.FromArgb(42, 36, 42);
        static readonly Color LcBlue = Color.FromArgb(64, 130, 208), LcBlueShade = Color.FromArgb(34, 84, 156), LcBlack = Color.FromArgb(46, 44, 54), LcBlackShade = Color.FromArgb(22, 20, 28);
        static readonly Color LcCream = Color.FromArgb(250, 230, 162), LcCreamShade = Color.FromArgb(216, 186, 112);
        static readonly Color DnOrange = Color.FromArgb(255, 188, 48), DnOrangeShade = Color.FromArgb(224, 138, 16), DnCream = Color.FromArgb(250, 238, 172), DnCreamShade = Color.FromArgb(222, 204, 128);
        static readonly Color DnTeal = Color.FromArgb(40, 198, 210), DnTealShade = Color.FromArgb(14, 136, 156);
        static readonly Color DtPurple = Color.FromArgb(198, 162, 228), DtPurpleShade = Color.FromArgb(150, 112, 190);
        static readonly Color M2Pale = Color.FromArgb(248, 236, 250), M2PaleShade = Color.FromArgb(202, 182, 212), M2Purple = Color.FromArgb(188, 134, 174), M2PurpleShade = Color.FromArgb(138, 90, 132);
        static readonly Color GrWood = Color.FromArgb(212, 148, 90), GrWoodShade = Color.FromArgb(160, 100, 56), GrGrain = Color.FromArgb(124, 74, 38);
        static readonly Color GrLeaf = Color.FromArgb(150, 200, 92), GrLeafShade = Color.FromArgb(92, 150, 56);

        // ---- Baby Groot.

        // Its head: round at the cheeks and soft at the chin, its top a jagged crown of splintered wood, higher on one side.
        static GraphicsPath GrootHead(float D)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddLines(new PointF[] { P(-0.205f * D, -0.3f * D), P(-0.215f * D, -0.43f * D), P(-0.165f * D, -0.37f * D), P(-0.13f * D, -0.48f * D), P(-0.08f * D, -0.4f * D),
                                      P(-0.03f * D, -0.51f * D), P(0.02f * D, -0.42f * D), P(0.075f * D, -0.53f * D), P(0.12f * D, -0.43f * D), P(0.175f * D, -0.52f * D),
                                      P(0.2f * D, -0.4f * D), P(0.228f * D, -0.46f * D), P(0.225f * D, -0.3f * D) });
            p.AddCurve(new PointF[] { P(0.225f * D, -0.3f * D), P(0.235f * D, -0.17f * D), P(0.215f * D, -0.04f * D), P(0.14f * D, 0.04f * D), P(0, 0.07f * D),
                                      P(-0.14f * D, 0.04f * D), P(-0.21f * D, -0.04f * D), P(-0.225f * D, -0.17f * D), P(-0.205f * D, -0.3f * D) }, 0.5f);
            p.CloseFigure();
            return p;
        }

        // The head, kept: the wood with its grain running up it, the brows over the deep sockets of its eyes, and leaves
        // sprouting from its crown.
        static void GrootHeadDraw(Graphics g, int k, GraphicsPath head, float D)
        {
            VFill(g, k, head, GrWood, GrWoodShade, D, 0);
            Region old = g.Clip;
            g.SetClip(head, CombineMode.Intersect);
            using (Pen grain = new Pen(Color.FromArgb(110, GrGrain), Math.Max(0.7f, D * 0.0055f)))
            {
                grain.StartCap = grain.EndCap = LineCap.Round;
                float[] xs = { -0.17f, -0.115f, -0.06f, 0.0f, 0.06f, 0.12f, 0.175f };
                for (int i = 0; i < xs.Length; i++)
                {
                    float x = xs[i], w = i % 2 == 0 ? 0.012f : -0.012f;
                    g.DrawBezier(grain, P(x * D, 0.07f * D), P((x + w) * D, -0.08f * D), P((x - w) * D, -0.27f * D), P((x + w * 0.5f) * D, -0.52f * D));
                }
            }
            foreach (int s in Sides)
            {
                Glow(g, s * 0.088f * D, -0.125f * D, 0.15f * D, 0.13f * D, Color.FromArgb(110, 60, 26), 80);
                using (GraphicsPath b = KCurve(D, 0.5f, s * 0.025f, -0.19f, s * 0.088f, -0.22f, s * 0.155f, -0.205f, s * 0.16f, -0.18f, s * 0.09f, -0.19f, s * 0.03f, -0.172f))
                    SoftPaint(g, b, Lighter(GrWood, 0.08), GrWoodShade, D, 20);
            }
            g.Clip = old;
            old.Dispose();
            float[][] leaves = { new float[] { -0.11f, -0.45f, -50, 0.06f }, new float[] { 0.0f, -0.47f, -15, 0.065f }, new float[] { 0.1f, -0.48f, 30, 0.06f },
                                 new float[] { 0.19f, -0.45f, 65, 0.055f } };
            foreach (float[] lf in leaves) GrootLeaf(g, k, D, lf[0], lf[1], lf[2], lf[3]);
        }

        // A small leaf on its stalk at x, y, leaning rot degrees.
        static void GrootLeaf(Graphics g, int k, float D, float x, float y, float rot, float len)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x * D, y * D);
            g.RotateTransform(rot);
            using (GraphicsPath lf = new GraphicsPath())
            {
                float L = len * D, w = len * 0.55f * D;
                lf.AddBezier(P(0, 0), P(-w * 1.1f, -L * 0.25f), P(-w * 0.8f, -L * 0.85f), P(0, -L));
                lf.AddBezier(P(0, -L), P(w * 0.8f, -L * 0.85f), P(w * 1.1f, -L * 0.25f), P(0, 0));
                lf.CloseFigure();
                VFill(g, k, lf, GrLeaf, GrLeafShade, D, 0);
                using (Pen vein = new Pen(Color.FromArgb(150, 70, 120, 40), Math.Max(0.6f, D * 0.004f))) g.DrawLine(vein, 0, -L * 0.1f, 0, -L * 0.8f);
            }
            g.Restore(st);
        }

        // Its little legs of twisted wood on rooty feet, and its body: wooden strands woven round each other, a leaf or two
        // growing out of them.
        static void GrootBack(Graphics g, int k, float D, MascotPose p)
        {
            foreach (int s in Sides)
            {
                PointF f = Foot(p, s, s * 0.06f, 0.322f);
                VLimb(g, k, D, GrWood, 0.05f, P(s * 0.04f, 0.22f), P(f.X, f.Y - 0.015f), s * 0.01f);
                VOval(g, k, D, GrWood, GrWoodShade, 0, f.X + s * 0.01f, f.Y + 0.006f, 0.085f, 0.045f);
            }
            DsCached(g, D, TnId(k, 3), new RectangleF(-0.16f, -0.03f, 0.32f, 0.31f), bg =>
            {
                using (GraphicsPath t = KCurve(D, 0.5f, 0, 0.0f, 0.06f, 0.01f, 0.085f, 0.07f, 0.08f, 0.15f, 0.07f, 0.22f, 0.04f, 0.25f, 0, 0.255f, -0.04f, 0.25f, -0.07f, 0.22f,
                                               -0.08f, 0.15f, -0.085f, 0.07f, -0.06f, 0.01f))
                    VFill(bg, k, t, GrWood, GrWoodShade, D, 0);
                SoftLimb(bg, k, D, Lighter(GrWood, 0.05), 0.03f, P(-0.06f * D, 0.02f * D), P(0.07f * D, 0.07f * D), P(-0.06f * D, 0.16f * D), P(0.05f * D, 0.24f * D));
                SoftLimb(bg, k, D, Lighter(GrWood, 0.05), 0.03f, P(0.06f * D, 0.02f * D), P(-0.07f * D, 0.08f * D), P(0.06f * D, 0.15f * D), P(-0.05f * D, 0.24f * D));
                GrootLeaf(bg, k, D, 0.075f, 0.05f, 62, 0.045f);
                GrootLeaf(bg, k, D, -0.07f, 0.17f, -70, 0.04f);
            });
        }

        // ---- Layout of each one.

        static Geo PkGeo(int k)
        {
            switch (k)
            {
                case KindPikachuSd: return G(0.46f, -0.355f, -0.155f, 0.106f, 0.04f, 0.3f, 0.33f, 0.165f, 0.15f, 0.4f);
                case KindEeveeSd: return G(0.44f, -0.33f, -0.135f, 0.1f, 0.05f, 0.36f, 0.33f, 0.17f, 0.22f, 0.4f);
                case KindCharmanderSd: return G(0.43f, -0.37f, -0.195f, 0.098f, 0.03f, 0.28f, 0.33f, 0.19f, 0.16f, 0.38f);
                case KindBulbasaurSd: return G(0.6f, -0.21f, -0.075f, 0.15f, 0.14f, 0.42f, 0.3f, 0.22f, 0.22f, 0.44f);
                case KindSquirtleSd: return G(0.46f, -0.37f, -0.19f, 0.1f, 0.03f, 0.3f, 0.33f, 0.2f, 0.16f, 0.4f);
                case KindSnorlaxSd: return G(0.44f, -0.37f, -0.2f, 0.075f, -0.03f, 0.3f, 0.36f, 0.31f, 0.14f, 0.42f);
                case KindGengarSd: return G(0.6f, -0.235f, -0.075f, 0.118f, 0.16f, 0.44f, 0.3f, 0.375f, 0.075f, 0.4f);
                case KindMewSd: return G(0.42f, -0.33f, -0.12f, 0.098f, 0.03f, 0.2f, 0.32f, 0.135f, 0.11f, 0.4f);
                case KindPsyduckSd: return G(0.48f, -0.36f, -0.16f, 0.1f, 0.06f, 0.3f, 0.34f, 0.255f, -0.11f, 0.46f);
                case KindTogepiSd: return G(0.4f, -0.29f, -0.13f, 0.09f, 0.02f, 0.3f, 0.36f, 0.28f, 0.07f, 0.4f);
                case KindMeowthSd: return G(0.5f, -0.35f, -0.14f, 0.11f, 0.05f, 0.22f, 0.33f, 0.19f, 0.17f, 0.46f);
                case KindPiplupSd: return G(0.46f, -0.36f, -0.13f, 0.098f, 0.05f, 0.28f, 0.34f, 0.2f, 0.17f, 0.44f);
                case KindMimikyuSd: return G(0.5f, -0.31f, -0.12f, 0.085f, 0.06f, 0.3f, 0.34f, 0.2f, 0.2f, 0.46f);
                case KindLucarioSd: return G(0.4f, -0.36f, -0.19f, 0.074f, 0.0f, 0.24f, 0.33f, 0.2f, 0.15f, 0.32f);
                case KindDragoniteSd: return G(0.44f, -0.43f, -0.235f, 0.092f, -0.03f, 0.3f, 0.33f, 0.21f, 0.135f, 0.34f);
                case KindDittoSd: return G(0.52f, -0.15f, 0.04f, 0.09f, 0.2f, 0.4f, 0.33f, 0.3f, 0.2f, 0.5f);
                case KindMewtwoSd: return G(0.38f, -0.37f, -0.2f, 0.076f, -0.03f, 0.18f, 0.33f, 0.2f, 0.15f, 0.3f);
                case KindGrootSd: return G(0.44f, -0.42f, -0.13f, 0.088f, 0.05f, 0.2f, 0.34f, 0.17f, 0.2f, 0.42f);
                default: return G(0.6f, -0.33f, -0.05f, 0.13f, 0.15f, 0.42f, 0.3f, 0.3f, 0.1f, 0.5f);
            }
        }

        static float PkMouthDy(int k)
        {
            switch (k)
            {
                case KindPikachuSd: return 0.08f;
                case KindEeveeSd: return 0.075f;
                case KindCharmanderSd: return 0.108f;
                case KindBulbasaurSd: return 0.12f;
                case KindSquirtleSd: return 0.11f;
                case KindSnorlaxSd: return 0.11f;
                case KindGengarSd: return 0.12f;
                case KindMewSd: return 0.085f;
                case KindTogepiSd: return 0.075f;
                case KindMeowthSd: return 0.1f;
                case KindLucarioSd: return 0.125f;
                case KindDragoniteSd: return 0.137f;
                case KindDittoSd: return 0.06f;
                case KindMewtwoSd: return 0.095f;
                case KindGrootSd: return 0.1f;
                default: return 0.12f;
            }
        }

        static float PkTop(int k)
        {
            switch (k)
            {
                case KindPikachuSd: return -0.62f;
                case KindEeveeSd: return -0.5f;
                case KindBulbasaurSd: return -0.6f;
                case KindJigglySd: return -0.42f;
                case KindSnorlaxSd: return -0.4f;
                case KindGengarSd: return -0.44f;
                case KindMewSd: return -0.48f;
                case KindPsyduckSd: return -0.5f;
                case KindTogepiSd: return -0.42f;
                case KindMeowthSd: return -0.56f;
                case KindPiplupSd: return -0.38f;
                case KindMimikyuSd: return -0.56f;
                case KindLucarioSd: return -0.58f;
                case KindDragoniteSd: return -0.64f;
                case KindDittoSd: return -0.17f;
                case KindMewtwoSd: return -0.5f;
                case KindGrootSd: return -0.55f;
                default: return -0.39f;
            }
        }

        // A round head like an egg: rx wide, ry0 above its centre and ry1 below, widening by widen towards its cheeks.
        static GraphicsPath PkEgg(float D, float cx, float cy, float rx, float ry0, float ry1, float widen)
        {
            const int n = 24;
            float[] xy = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                double t = -Math.PI / 2 + Math.PI * 2 * i / n;
                float s = (float)Math.Sin(t), c = (float)Math.Cos(t);
                xy[i * 2] = cx + rx * c * (1 + widen * Math.Max(0, s) * (1 - Math.Max(0, s) * 0.6f));
                xy[i * 2 + 1] = cy + (s < 0 ? ry0 : ry1) * s;
            }
            return KCurve(D, 0.5f, xy);
        }

        // A closed outline through points (x, y, corner) in units of D: smooth through the plain ones, a sharp corner at
        // each point flagged 1.
        static GraphicsPath PkOutline(float D, float tension, params float[] xyc)
        {
            int n = xyc.Length / 3, first = -1;
            GraphicsPath p = new GraphicsPath();
            for (int i = 0; i < n && first < 0; i++) if (xyc[i * 3 + 2] > 0) first = i;
            if (first < 0)
            {
                PointF[] all = new PointF[n];
                for (int i = 0; i < n; i++) all[i] = P(xyc[i * 3] * D, xyc[i * 3 + 1] * D);
                p.AddClosedCurve(all, tension);
                return p;
            }
            System.Collections.Generic.List<PointF> run = new System.Collections.Generic.List<PointF>();
            for (int s = 0; s <= n; s++)
            {
                int i = (first + s) % n;
                PointF pt = P(xyc[i * 3] * D, xyc[i * 3 + 1] * D);
                run.Add(pt);
                if (s == 0 || xyc[i * 3 + 2] <= 0) continue;
                if (run.Count == 2) p.AddLine(run[0], run[1]);
                else p.AddCurve(run.ToArray(), tension);
                run.Clear();
                run.Add(pt);
            }
            p.CloseFigure();
            return p;
        }

        // The same outline mirrored left to right and joined on: the right half given from the top middle round to the
        // bottom middle, clockwise.
        static float[] PkHalf(params float[] xyc)
        {
            System.Collections.Generic.List<float> all = new System.Collections.Generic.List<float>(xyc);
            for (int i = xyc.Length / 3 - 1; i >= 0; i--)
                if (Math.Abs(xyc[i * 3]) > 1e-4f) all.AddRange(new float[] { -xyc[i * 3], xyc[i * 3 + 1], xyc[i * 3 + 2] });
            return all.ToArray();
        }

        // A tube along points (x, y, width) in units of D, thicker or thinner along it, round at both ends.
        static GraphicsPath PkTube(float D, params float[] xyw)
        {
            int n = xyw.Length / 3;
            PointF[] c = new PointF[n], nr = new PointF[n];
            float[] w = new float[n];
            for (int i = 0; i < n; i++) { c[i] = P(xyw[i * 3] * D, xyw[i * 3 + 1] * D); w[i] = xyw[i * 3 + 2] * D / 2; }
            for (int i = 0; i < n; i++)
            {
                PointF a = c[Math.Max(0, i - 1)], b = c[Math.Min(n - 1, i + 1)];
                float dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Max(1e-4f, (float)Math.Sqrt(dx * dx + dy * dy));
                nr[i] = P(-dy / l, dx / l);
            }
            System.Collections.Generic.List<PointF> pts = new System.Collections.Generic.List<PointF>();
            for (int i = 0; i < n; i++) pts.Add(P(c[i].X + nr[i].X * w[i], c[i].Y + nr[i].Y * w[i]));
            PointF e = c[n - 1], en = nr[n - 1];
            for (int q = 1; q < 4; q++)
            {
                double a = q * Math.PI / 4;
                float cs = (float)Math.Cos(a) * w[n - 1], sn = (float)Math.Sin(a) * w[n - 1];
                pts.Add(P(e.X + en.X * cs + en.Y * sn, e.Y + en.Y * cs - en.X * sn));
            }
            for (int i = n - 1; i >= 0; i--) pts.Add(P(c[i].X - nr[i].X * w[i], c[i].Y - nr[i].Y * w[i]));
            PointF s0 = c[0], sn0 = nr[0];
            for (int q = 1; q < 4; q++)
            {
                double a = q * Math.PI / 4;
                float cs = (float)Math.Cos(a) * w[0], sn = (float)Math.Sin(a) * w[0];
                pts.Add(P(s0.X - sn0.X * cs - sn0.Y * sn, s0.Y - sn0.Y * cs + sn0.X * sn));
            }
            GraphicsPath p = new GraphicsPath();
            p.AddClosedCurve(pts.ToArray(), 0.5f);
            return p;
        }

        // Gengar: head and body in one round shape, broad at the shoulders, its two big pointed ears rising out to the
        // sides.
        static GraphicsPath GengarBody(float D)
        {
            return PkOutline(D, 0.5f, PkHalf(0, -0.228f, 0, 0.07f, -0.224f, 0, 0.125f, -0.212f, 0, 0.235f, -0.3f, 0, 0.355f, -0.43f, 1, 0.33f, -0.3f, 0,
                                             0.29f, -0.16f, 0, 0.305f, -0.03f, 0, 0.285f, 0.11f, 0, 0.18f, 0.225f, 0, 0, 0.258f, 0));
        }

        // Mewtwo's head: a rounded skull, broad at the cheekbones and narrowing to a small chin (its horns rise from behind
        // it, see PkBack).
        static GraphicsPath MewtwoHead(float D)
        {
            return PkOutline(D, 0.5f, PkHalf(0, -0.372f, 0, 0.08f, -0.364f, 0, 0.142f, -0.33f, 0, 0.18f, -0.272f, 0, 0.192f, -0.21f, 0, 0.178f, -0.15f, 0,
                                             0.138f, -0.1f, 0, 0.082f, -0.068f, 0, 0, -0.056f, 0));
        }

        // Togepi's head: round, its top a crown of five big spikes, the middle one standing straight up.
        static GraphicsPath TogepiHead(float D)
        {
            const float cy = -0.1f, r = 0.19f, R = 0.29f;
            System.Collections.Generic.List<float> xy = new System.Collections.Generic.List<float>();
            Action<double, float, int> at = (deg, rad, corner) =>
            {
                double a = deg * Math.PI / 180;
                xy.AddRange(new float[] { (float)Math.Sin(a) * rad, cy - (float)Math.Cos(a) * rad, corner });
            };
            for (int i = -2; i <= 2; i++)
            {
                double t = i * 34;
                at(t - 17, r, 1);
                at(t - 9, r + (R - r) * 0.62f, 0);
                at(t, i == 0 ? R + 0.01f : R, 1);
                at(t + 9, r + (R - r) * 0.62f, 0);
            }
            at(85, r, 1);
            foreach (double deg in new double[] { 105, 130, 155, 180, 205, 230, 255 }) at(deg, r * (deg > 120 && deg < 240 ? 1.02f : 1f), 0);
            at(275, r, 1);
            return PkOutline(D, 0.5f, xy.ToArray());
        }

        // Lucario's head: a jackal's, round at the back and narrowing down to its snout.
        static GraphicsPath LucarioHead(float D)
        {
            return PkOutline(D, 0.5f, PkHalf(0, -0.352f, 0, 0.09f, -0.345f, 0, 0.155f, -0.305f, 0, 0.185f, -0.235f, 0, 0.178f, -0.168f, 0, 0.142f, -0.11f, 0,
                                             0.088f, -0.07f, 0, 0.04f, -0.05f, 0, 0, -0.046f, 0));
        }

        // The head (or, for Jigglypuff, the whole round body).
        static GraphicsPath PkPath(int k, float D)
        {
            switch (k)
            {
                case KindPikachuSd: return PkEgg(D, 0, -0.15f, 0.215f, 0.205f, 0.19f, 0.22f);
                case KindEeveeSd:
                    // Round, a fluffy tuft on its brow and another at each cheek.
                    return PkOutline(D, 0.5f, PkHalf(0, -0.348f, 1, 0.03f, -0.318f, 1, 0.062f, -0.34f, 1, 0.092f, -0.31f, 0, 0.16f, -0.29f, 0, 0.205f, -0.23f, 0, 0.215f, -0.15f, 0,
                                                     0.2f, -0.088f, 0, 0.234f, -0.062f, 1, 0.178f, -0.035f, 0, 0.1f, -0.006f, 0, 0, 0.004f, 0));
                case KindCharmanderSd:
                    return PkOutline(D, 0.5f, PkHalf(0, -0.372f, 0, 0.1f, -0.364f, 0, 0.17f, -0.315f, 0, 0.205f, -0.235f, 0, 0.207f, -0.155f, 0, 0.182f, -0.085f, 0,
                                                     0.122f, -0.038f, 0, 0.05f, -0.02f, 0, 0, -0.018f, 0));
                case KindBulbasaurSd:
                    // Wide and flat, broad across its jaw.
                    return PkOutline(D, 0.5f, PkHalf(0, -0.212f, 0, 0.12f, -0.207f, 0, 0.22f, -0.172f, 0, 0.285f, -0.1f, 0, 0.302f, -0.02f, 0, 0.272f, 0.06f, 0,
                                                     0.19f, 0.11f, 0, 0.09f, 0.13f, 0, 0, 0.135f, 0));
                case KindSquirtleSd: return PkEgg(D, 0, -0.17f, 0.22f, 0.2f, 0.17f, 0.12f);
                case KindSnorlaxSd: return PkEgg(D, 0, -0.2f, 0.205f, 0.17f, 0.15f, 0.12f);
                case KindGengarSd: return GengarBody(D);
                case KindMewSd: return PkEgg(D, 0, -0.14f, 0.2f, 0.19f, 0.17f, 0.12f);
                case KindPsyduckSd: return PkEgg(D, 0, -0.15f, 0.23f, 0.21f, 0.2f, 0.06f);
                case KindTogepiSd: return TogepiHead(D);
                case KindMeowthSd:
                    // Round, the fur sticking out in two tufts at each cheek.
                    return PkOutline(D, 0.5f, PkHalf(0, -0.35f, 0, 0.12f, -0.34f, 0, 0.2f, -0.282f, 0, 0.236f, -0.195f, 0, 0.228f, -0.122f, 0, 0.268f, -0.09f, 1, 0.214f, -0.068f, 0,
                                                     0.24f, -0.038f, 1, 0.17f, -0.012f, 0, 0.08f, 0.01f, 0, 0, 0.014f, 0));
                case KindPiplupSd: return PkEgg(D, 0, -0.15f, 0.215f, 0.21f, 0.2f, 0.06f);
                case KindMimikyuSd:
                    return KCurve(D, 0.5f, -0.02f, -0.3f, 0.12f, -0.29f, 0.21f, -0.22f, 0.235f, -0.11f, 0.22f, -0.01f, 0.15f, 0.05f, 0, 0.07f, -0.15f, 0.05f, -0.225f, -0.02f,
                                  -0.235f, -0.13f, -0.2f, -0.23f, -0.12f, -0.285f);
                case KindLucarioSd: return LucarioHead(D);
                case KindDragoniteSd:
                    // Round over its brow and broad at the cheeks, down to the wide rounded muzzle.
                    return PkOutline(D, 0.5f, PkHalf(0, -0.43f, 0, 0.1f, -0.422f, 0, 0.168f, -0.385f, 0, 0.208f, -0.325f, 0, 0.222f, -0.255f, 0, 0.212f, -0.19f, 0,
                                                     0.188f, -0.138f, 0, 0.15f, -0.098f, 0, 0.09f, -0.068f, 0, 0, -0.058f, 0));
                case KindDittoSd:
                    // A lumpy blob, spreading at its bottom, two soft bumps standing up off its top.
                    return KCurve(D, 0.5f, 0.0f, -0.105f, 0.07f, -0.118f, 0.12f, -0.16f, 0.18f, -0.158f, 0.215f, -0.1f, 0.24f, 0.0f, 0.3f, 0.1f, 0.36f, 0.2f, 0.35f, 0.29f, 0.22f, 0.335f,
                                  0, 0.342f, -0.22f, 0.335f, -0.355f, 0.29f, -0.37f, 0.2f, -0.32f, 0.11f, -0.262f, 0.02f, -0.235f, -0.08f, -0.2f, -0.15f, -0.14f, -0.17f, -0.07f, -0.13f);
                case KindMewtwoSd: return MewtwoHead(D);
                case KindGrootSd: return GrootHead(D);
                default:
                {
                    GraphicsPath p = new GraphicsPath();
                    p.AddEllipse(-0.3f * D, -0.33f * D, 0.6f * D, 0.6f * D);
                    return p;
                }
            }
        }

        static RectangleF PkHeadArea(int k)
        {
            switch (k)
            {
                case KindPikachuSd: return new RectangleF(-0.27f, -0.37f, 0.54f, 0.43f);
                case KindCharmanderSd: return new RectangleF(-0.23f, -0.39f, 0.46f, 0.39f);
                case KindEeveeSd: return new RectangleF(-0.27f, -0.37f, 0.54f, 0.4f);
                case KindBulbasaurSd: return new RectangleF(-0.33f, -0.23f, 0.66f, 0.39f);
                case KindJigglySd: return new RectangleF(-0.34f, -0.46f, 0.68f, 0.78f);
                case KindSnorlaxSd: return new RectangleF(-0.24f, -0.4f, 0.48f, 0.38f);
                case KindGengarSd: return new RectangleF(-0.4f, -0.46f, 0.8f, 0.75f);
                case KindMewtwoSd: return new RectangleF(-0.21f, -0.39f, 0.42f, 0.35f);
                case KindPsyduckSd: return new RectangleF(-0.28f, -0.4f, 0.56f, 0.5f);
                case KindTogepiSd: return new RectangleF(-0.31f, -0.42f, 0.62f, 0.53f);
                case KindMeowthSd: return new RectangleF(-0.28f, -0.38f, 0.56f, 0.46f);
                case KindMimikyuSd: return new RectangleF(-0.27f, -0.33f, 0.54f, 0.43f);
                case KindLucarioSd: return new RectangleF(-0.22f, -0.37f, 0.44f, 0.34f);
                case KindDittoSd: return new RectangleF(-0.39f, -0.19f, 0.78f, 0.56f);
                case KindDragoniteSd: return new RectangleF(-0.24f, -0.45f, 0.48f, 0.41f);
                case KindGrootSd: return new RectangleF(-0.27f, -0.6f, 0.54f, 0.7f);
                default: return new RectangleF(-0.28f, -0.4f, 0.56f, 0.52f);
            }
        }

        // The head, kept: its colour, cheeks, spots and the like.
        static void PkHeadDraw(Graphics g, int k, GraphicsPath head, float D)
        {
            switch (k)
            {
                case KindPikachuSd:
                    VFill(g, k, head, PkYellow, PkYellowShade, D, 0);
                    foreach (int s in Sides) VOval(g, k, D, PkRed, PkRedShade, 0, s * 0.148f, -0.085f, 0.088f, 0.08f);
                    break;
                case KindEeveeSd: VFill(g, k, head, EvBrown, EvBrownShade, D, 0); break;
                case KindCharmanderSd: VFill(g, k, head, CmOrange, CmOrangeShade, D, 0); break;
                case KindBulbasaurSd:
                {
                    VFill(g, k, head, BbTeal, BbTealShade, D, 0);
                    // The darker patches over its brow and cheeks.
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    float[][] spots = { new float[] { -0.04f, -0.19f, 0.09f, 0.06f }, new float[] { 0.07f, -0.17f, 0.07f, 0.05f }, new float[] { -0.12f, -0.16f, 0.06f, 0.045f },
                                        new float[] { 0.2f, -0.13f, 0.05f, 0.04f }, new float[] { -0.24f, 0.0f, 0.04f, 0.05f }, new float[] { 0.25f, 0.02f, 0.035f, 0.045f } };
                    foreach (float[] sp in spots)
                        using (GraphicsPath s = KCurve(D, 0.5f, sp[0] - sp[2] / 2, sp[1], sp[0] - sp[2] * 0.1f, sp[1] - sp[3] / 2, sp[0] + sp[2] / 2, sp[1] - sp[3] * 0.2f,
                                                       sp[0] + sp[2] * 0.3f, sp[1] + sp[3] / 2, sp[0] - sp[2] * 0.3f, sp[1] + sp[3] * 0.4f))
                            FillSolid(g, s, BbSpot);
                    g.Clip = old;
                    old.Dispose();
                    break;
                }
                case KindSquirtleSd: VFill(g, k, head, SqBlue, SqBlueShade, D, 0); break;
                case KindSnorlaxSd:
                {
                    // Dark teal, the cream face from under its brow down.
                    VFill(g, k, head, SlBlue, SlBlueShade, D, 0);
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    using (GraphicsPath f = KCurve(D, 0.45f, -0.23f, -0.15f, -0.12f, -0.24f, 0, -0.21f, 0.12f, -0.24f, 0.23f, -0.15f, 0.23f, -0.03f, 0, -0.02f, -0.23f, -0.03f))
                        SoftPaint(g, f, SlCream, SlCreamShade, D, 30);
                    g.Clip = old;
                    old.Dispose();
                    break;
                }
                case KindGengarSd: VFill(g, k, head, GnPurple, GnPurpleShade, D, 0); break;
                case KindMewSd: VFill(g, k, head, MwPink, MwPinkShade, D, 0); break;
                case KindPsyduckSd:
                    VFill(g, k, head, PsYellow, PsYellowShade, D, 0);
                    PsyBillSd(g, k, D);
                    break;
                case KindTogepiSd: VFill(g, k, head, TgCream, TgCreamShade, D, 0); break;
                case KindMeowthSd:
                    VFill(g, k, head, MeCream, MeCreamShade, D, 0);
                    MeowthCoinSd(g, k, D);
                    break;
                case KindPiplupSd:
                {
                    // Dark blue, the big white patch round each eye, the light blue crown on its brow over the yellow beak.
                    VFill(g, k, head, PpBlue, PpBlueShade, D, 0);
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    using (GraphicsPath w = new GraphicsPath())
                    {
                        foreach (int s in Sides) w.AddEllipse((s * 0.092f - 0.09f) * D, -0.222f * D, 0.18f * D, 0.186f * D);
                        w.FillMode = FillMode.Winding;
                        SoftPaint(g, w, Color.FromArgb(252, 252, 252), Color.FromArgb(206, 214, 230), D, 30);
                    }
                    g.Clip = old;
                    old.Dispose();
                    using (GraphicsPath cr = PkOutline(D, 0.5f, -0.05f, -0.072f, 0, -0.05f, -0.13f, 1, -0.022f, -0.104f, 1, 0, -0.148f, 1, 0.022f, -0.104f, 1, 0.05f, -0.13f, 1, 0.05f, -0.072f, 0))
                        VFill(g, k, cr, PpLight, PpLightShade, D, 0);
                    using (GraphicsPath bk = KCurve(D, 0.45f, -0.058f, -0.082f, 0.058f, -0.082f, 0.034f, -0.036f, 0, -0.01f, -0.034f, -0.036f))
                        VFill(g, k, bk, PpOrange, PpOrangeShade, D, 0);
                    break;
                }
                case KindMimikyuSd:
                {
                    // The rag over its head with the face drawn on it: two scribbled black eyes, the red cheeks, a zigzag mouth.
                    VFill(g, k, head, MmCloth, MmClothShade, D, 0);
                    using (Pen sc = new Pen(Color.FromArgb(150, 120, 116, 120), Math.Max(0.7f, D * 0.004f)))
                    {
                        foreach (int s in Sides)
                        {
                            float ex = s * 0.085f, ey = -0.12f;
                            Ellipse(g, MmDark, (ex - 0.036f) * D, (ey - 0.04f) * D, 0.072f * D, 0.08f * D);
                            for (int i = 1; i < 4; i++) g.DrawEllipse(sc, (ex - 0.009f * i) * D, (ey - 0.01f * i) * D, 0.018f * i * D, 0.02f * i * D);
                            float cx = s * 0.15f, cy = -0.03f;
                            Ellipse(g, Color.FromArgb(232, 96, 70), (cx - 0.034f) * D, (cy - 0.03f) * D, 0.068f * D, 0.06f * D);
                            using (Pen cs = new Pen(Color.FromArgb(150, 150, 50, 30), Math.Max(0.7f, D * 0.004f)))
                                for (int i = 1; i < 3; i++) g.DrawEllipse(cs, (cx - 0.012f * i) * D, (cy - 0.011f * i) * D, 0.024f * i * D, 0.022f * i * D);
                        }
                    }
                    PointF[] z = new PointF[7];
                    for (int i = 0; i < 7; i++) z[i] = P((-0.07f + 0.14f * i / 6f) * D, (i % 2 == 0 ? -0.04f : -0.018f) * D);
                    using (Pen mp = new Pen(MmDark, Math.Max(1f, D * 0.009f))) { mp.LineJoin = LineJoin.Round; g.DrawLines(mp, z); }
                    break;
                }
                case KindLucarioSd:
                {
                    // Blue, the black mask across its eyes and out to the sides of its head, the blue of its brow coming
                    // down in a point between them, the black snout; its jaw blue again under it.
                    VFill(g, k, head, LcBlue, LcBlueShade, D, 0);
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    using (GraphicsPath m = PkOutline(D, 0.5f, PkHalf(0, -0.158f, 1, 0.045f, -0.222f, 0, 0.11f, -0.254f, 0, 0.21f, -0.258f, 1, 0.21f, -0.165f, 1, 0.13f, -0.152f, 0,
                                                                    0.068f, -0.148f, 0, 0.05f, -0.115f, 0, 0.034f, -0.084f, 0, 0, -0.074f, 0)))
                        SoftPaint(g, m, LcBlack, LcBlackShade, D, 30);
                    g.Clip = old;
                    old.Dispose();
                    break;
                }
                case KindDragoniteSd:
                {
                    VFill(g, k, head, DnOrange, DnOrangeShade, D, 0);
                    // The broad muzzle standing out at the bottom of its face, lit on top, a soft crease where it meets the
                    // cheeks.
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    using (GraphicsPath m = KCurve(D, 0.5f, 0, -0.183f, 0.09f, -0.172f, 0.14f, -0.13f, 0.125f, -0.085f, 0.06f, -0.066f, 0, -0.062f, -0.06f, -0.066f,
                                                   -0.125f, -0.085f, -0.14f, -0.13f, -0.09f, -0.172f))
                        SoftPaint(g, m, Lighter(DnOrange, 0.18), DnOrange, D, 30);
                    TnLine(g, Color.FromArgb(80, DnOrangeShade), 0.008f, D, -0.135f, -0.148f, -0.075f, -0.181f, 0, -0.189f, 0.075f, -0.181f, 0.135f, -0.148f);
                    g.Clip = old;
                    old.Dispose();
                    break;
                }
                case KindDittoSd: VFill(g, k, head, DtPurple, DtPurpleShade, D, 0); break;
                case KindMewtwoSd:
                {
                    VFill(g, k, head, M2Pale, M2PaleShade, D, 0);
                    // The ridge of its brow over each eye, lit along its top; the shadow under its cheekbones.
                    Region old = g.Clip;
                    g.SetClip(head, CombineMode.Intersect);
                    foreach (int s in Sides)
                    {
                        using (GraphicsPath b = KCurve(D, 0.5f, s * 0.02f, -0.222f, s * 0.07f, -0.256f, s * 0.15f, -0.26f, s * 0.19f, -0.236f, s * 0.13f, -0.238f, s * 0.07f, -0.232f))
                            FillSolid(g, b, Color.FromArgb(80, M2PaleShade));
                        TnLine(g, Color.FromArgb(200, 255, 255, 255), 0.008f, D, s * 0.035f, -0.252f, s * 0.085f, -0.272f, s * 0.15f, -0.275f);
                        Glow(g, s * 0.15f * D, -0.1f * D, 0.08f * D, 0.06f * D, M2PaleShade, 60);
                    }
                    g.Clip = old;
                    old.Dispose();
                    break;
                }
                case KindGrootSd: GrootHeadDraw(g, k, head, D); break;
                default:
                    VFill(g, k, head, JgPink, JgPinkShade, D, 0);
                    JigglyCurl(g, k, D);
                    break;
            }
        }

        // Psyduck's big flat cream bill, wider at its round front, the lower half a shade darker, two nostrils.
        static void PsyBillSd(Graphics g, int k, float D)
        {
            using (GraphicsPath b = KCurve(D, 0.5f, -0.14f, -0.104f, -0.075f, -0.12f, 0, -0.124f, 0.075f, -0.12f, 0.14f, -0.104f, 0.19f, -0.06f, 0.172f, -0.004f, 0.09f, 0.018f, 0, 0.022f,
                                           -0.09f, 0.018f, -0.172f, -0.004f, -0.19f, -0.06f))
            {
                VFill(g, k, b, PsBill, PsBillShade, D, 0);
                Region old = g.Clip;
                g.SetClip(b, CombineMode.Intersect);
                using (GraphicsPath lo = KPoly(D, -0.22f, -0.048f, -0.09f, -0.03f, 0.09f, -0.03f, 0.22f, -0.048f, 0.22f, 0.04f, -0.22f, 0.04f)) FillSolid(g, lo, Color.FromArgb(70, 190, 150, 80));
                Glow(g, -0.05f * D, -0.1f * D, 0.2f * D, 0.04f * D, Color.White, 120);
                g.Clip = old;
                old.Dispose();
            }
            TnLine(g, Color.FromArgb(150, 168, 120, 64), 0.007f, D, -0.17f, -0.048f, -0.08f, -0.03f, 0.08f, -0.03f, 0.17f, -0.048f);
            foreach (int s in Sides) Ellipse(g, Color.FromArgb(160, 130, 90, 44), (s * 0.03f - 0.009f) * D, -0.1f * D, 0.018f * D, 0.011f * D);
        }

        // Meowth's gold koban on its forehead, with its engraved lines.
        static void MeowthCoinSd(Graphics g, int k, float D)
        {
            RectangleF c = new RectangleF(-0.042f * D, -0.345f * D, 0.084f * D, 0.108f * D);
            using (GraphicsPath cp = new GraphicsPath())
            {
                cp.AddEllipse(c);
                VFill(g, k, cp, Color.FromArgb(255, 220, 90), Color.FromArgb(210, 140, 20), D, 0);
                Region old = g.Clip;
                g.SetClip(cp, CombineMode.Intersect);
                for (int i = 0; i < 4; i++)
                {
                    float ly = c.Y + c.Height * (0.27f + i * 0.155f);
                    Line(g, Color.FromArgb(200, 170, 104, 10), D * 0.007f, c.X + c.Width * 0.2f, ly, c.Right - c.Width * 0.2f, ly);
                }
                g.Clip = old;
                old.Dispose();
            }
            Ellipse(g, Color.FromArgb(220, 255, 255, 236), c.X + 0.016f * D, c.Y + 0.012f * D, 0.022f * D, 0.014f * D);
        }

        // ---- Behind the head: ears, tails, bodies, legs and arms.

        static void PkBack(Graphics g, int k, float D, MascotPose p, Geo geo)
        {
            float earL = (float)p.EarL, earR = (float)p.EarR;
            switch (k)
            {
                case KindPikachuSd:
                {
                    // The bolt of its tail, the long ears with their black tips, its little feet and the chubby pear of its
                    // body.
                    PikaTailSd(g, k, D, p);
                    foreach (int s in Sides) PkEar(g, k, D, s * 0.112f, -0.3f, s * (25 + (s < 0 ? earL : earR) * 0.4f), 0.33f, 0.33f, PkYellow, PkYellowShade, PkBlack, 0.3f, Color.Empty);
                    foreach (int s in Sides) PkToes(g, k, D, Foot(p, s, s * 0.085f, 0.322f), PkYellow, PkYellowShade, 0.098f, 3, Color.Empty);
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.2f, -0.04f, 0.4f, 0.4f), bg =>
                        VBlob(bg, k, D, PkYellow, PkYellowShade, 0, 0.5f, 0, -0.01f, 0.1f, 0.0f, 0.148f, 0.07f, 0.172f, 0.17f, 0.168f, 0.255f, 0.12f, 0.312f, 0, 0.328f,
                              -0.12f, 0.312f, -0.168f, 0.255f, -0.172f, 0.17f, -0.148f, 0.07f, -0.1f, 0.0f));
                    break;
                }
                case KindEeveeSd:
                {
                    // Standing: the bushy tail, the big ears with their dark insides, the hind legs behind, the body, the
                    // slim front legs, and the big cream ruff round its neck.
                    EeveeTailSd(g, k, D, p);
                    foreach (int s in Sides) PkEar(g, k, D, s * 0.125f, -0.25f, s * (37 + (s < 0 ? earL : earR) * 0.4f), 0.33f, 0.56f, EvBrown, EvBrownShade, Color.Empty, 0, EvDark);
                    foreach (int s in Sides)
                    {
                        PointF hf = Foot(p, -s, s * 0.14f, 0.322f);
                        VOval(g, k, D, EvBrown, EvBrownShade, 0, s * 0.13f, 0.215f, 0.13f, 0.15f);
                        PkToes(g, k, D, hf, EvBrown, EvBrownShade, 0.075f, 0, Color.Empty);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.18f, 0.0f, 0.36f, 0.28f), bg => VOval(bg, k, D, EvBrown, EvBrownShade, 0, 0, 0.15f, 0.27f, 0.2f));
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.06f, 0.326f);
                        VLimb(g, k, D, EvBrown, 0.064f, P(s * 0.062f, 0.14f), P(f.X, f.Y - 0.022f), s * 0.03f);
                        PkToes(g, k, D, f, EvBrown, EvBrownShade, 0.08f, 3, Color.Empty);
                    }
                    DsCached(g, D, TnId(k, 4), new RectangleF(-0.27f, -0.06f, 0.54f, 0.26f), bg =>
                    {
                        using (GraphicsPath r = PkOutline(D, 0.5f, PkHalf(0, -0.03f, 0, 0.12f, -0.035f, 0, 0.2f, -0.02f, 0, 0.245f, 0.03f, 1, 0.2f, 0.055f, 1, 0.228f, 0.1f, 1,
                                                                          0.15f, 0.1f, 1, 0.14f, 0.15f, 1, 0.075f, 0.125f, 1, 0.035f, 0.17f, 1, 0, 0.13f, 1)))
                            VFill(bg, k, r, EvCream, EvCreamShade, D, 0);
                    });
                    break;
                }
                case KindCharmanderSd:
                {
                    // The thick tail with the flame at its end, the short legs with their white claws, and the chubby body
                    // with its cream belly.
                    CharTailSd(g, k, D, p);
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.095f, 0.322f);
                        VLimb(g, k, D, CmOrange, 0.08f, P(s * 0.085f, 0.24f), P(f.X, f.Y - 0.02f), 0);
                        PkToes(g, k, D, f, CmOrange, CmOrangeShade, 0.1f, 3, Color.FromArgb(252, 252, 250));
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.2f, -0.05f, 0.4f, 0.4f), bg =>
                    {
                        using (GraphicsPath b = KCurve(D, 0.5f, 0, -0.03f, 0.1f, -0.012f, 0.15f, 0.065f, 0.17f, 0.165f, 0.158f, 0.258f, 0.105f, 0.312f, 0, 0.326f, -0.105f, 0.312f,
                                                       -0.158f, 0.258f, -0.17f, 0.165f, -0.15f, 0.065f, -0.1f, -0.012f))
                        {
                            VFill(bg, k, b, CmOrange, CmOrangeShade, D, 0);
                            Region old = bg.Clip;
                            bg.SetClip(b, CombineMode.Intersect);
                            using (GraphicsPath belly = KCurve(D, 0.5f, 0, 0.02f, 0.08f, 0.04f, 0.115f, 0.13f, 0.11f, 0.23f, 0.07f, 0.3f, 0, 0.32f, -0.07f, 0.3f, -0.11f, 0.23f, -0.115f, 0.13f, -0.08f, 0.04f))
                                SoftPaint(bg, belly, CmCream, CmCreamShade, D, 40);
                            bg.Clip = old;
                            old.Dispose();
                        }
                    });
                    break;
                }
                case KindBulbasaurSd:
                {
                    // The big bulb on its back, rising behind its head off to the side its tail would be on (the way the
                    // games show it, turned a little); the little pointed ears in front of it; its hind legs, body and
                    // stout front legs with their white claws under the head.
                    GraphicsState ts = TailSide(g, p);
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.28f, -0.64f, 0.74f, 0.6f), bg =>
                    {
                        GraphicsState bs = bg.Save();
                        bg.TranslateTransform(0.1f * D, -0.08f * D);
                        bg.ScaleTransform(1.18f, 1.18f);
                        bg.TranslateTransform(0, 0.13f * D);
                        BulbSd(bg, k, D);
                        bg.Restore(bs);
                    });
                    g.Restore(ts);
                    DsCached(g, D, TnId(k, 7), new RectangleF(-0.3f, -0.3f, 0.6f, 0.17f), bg =>
                    {
                        foreach (int s in Sides) PkEar(bg, k, D, s * 0.2f, -0.165f, s * 28, 0.105f, 0.85f, BbTeal, BbTealShade, Color.Empty, 0, Color.Empty);
                    });
                    foreach (int s in Sides)
                    {
                        PointF hf = Foot(p, -s, s * 0.275f, 0.29f);
                        VLimb(g, k, D, BbTealShade, 0.11f, P(s * 0.26f, 0.13f), P(hf.X, hf.Y - 0.02f), 0);
                        PkToes(g, k, D, hf, BbTealShade, Darker(BbTealShade, 0.2), 0.11f, 0, Color.Empty);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.32f, -0.05f, 0.64f, 0.34f), bg => VOval(bg, k, D, BbTeal, BbTealShade, 0, 0, 0.13f, 0.56f, 0.26f));
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.165f, 0.31f);
                        VLimb(g, k, D, BbTeal, 0.12f, P(s * 0.16f, 0.15f), P(f.X, f.Y - 0.02f), 0);
                        PkToes(g, k, D, f, BbTeal, BbTealShade, 0.13f, 3, Color.FromArgb(252, 252, 250));
                    }
                    break;
                }
                case KindSquirtleSd:
                {
                    SquirtleTailSd(g, k, D, p);
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.095f, 0.322f);
                        VLimb(g, k, D, SqBlue, 0.08f, P(s * 0.085f, 0.25f), P(f.X, f.Y - 0.02f), 0);
                        PkToes(g, k, D, f, SqBlue, SqBlueShade, 0.1f, 3, Color.Empty);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.25f, -0.05f, 0.5f, 0.42f), bg => SquirtleBody(bg, k, D));
                    break;
                }
                case KindSnorlaxSd:
                {
                    // Sitting: the little ears on its head, its huge body with the cream belly, and its feet held out in
                    // front, their cream soles with the brown pads and the claws.
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.24f, -0.44f, 0.48f, 0.2f), bg =>
                    {
                        foreach (int s in Sides) PkEar(bg, k, D, s * 0.13f, -0.31f, s * 32, 0.085f, 1.0f, SlBlue, SlBlueShade, Color.Empty, 0, Color.Empty);
                    });
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.36f, -0.15f, 0.72f, 0.52f), bg =>
                    {
                        VOval(bg, k, D, SlBlue, SlBlueShade, 0, 0, 0.11f, 0.66f, 0.48f);
                        VOval(bg, k, D, SlCream, SlCreamShade, 0, 0, 0.135f, 0.46f, 0.38f);
                    });
                    foreach (int s in Sides) SnorlaxFootSd(g, k, D, Foot(p, s, s * 0.175f, 0.3f), s);
                    break;
                }
                case KindGengarSd:
                {
                    // The ragged crest of spikes down its back, peeping over its head; its short thick legs.
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.2f, -0.42f, 0.4f, 0.3f), bg =>
                    {
                        using (GraphicsPath cr = TnSpikes(D, 0.05f, new float[] { -0.17f, -0.18f, -0.12f, -0.285f, -0.085f, -0.235f, -0.02f, -0.35f, 0.01f, -0.245f, 0.085f, -0.39f,
                                                                                  0.09f, -0.25f, 0.175f, -0.315f, 0.15f, -0.18f, 0.0f, -0.13f }))
                            VFill(bg, k, cr, GnPurple, GnPurpleShade, D, 0);
                    });
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.13f, 0.316f);
                        VLimb(g, k, D, GnPurple, 0.1f, P(s * 0.12f, 0.17f), P(f.X, f.Y - 0.03f), 0);
                        GengarFoot(g, k, D, f);
                    }
                    break;
                }
                case KindMewSd:
                {
                    MewTailSd(g, k, D, p);
                    foreach (int s in Sides) PkEar(g, k, D, s * 0.12f, -0.27f, s * (26 + (s < 0 ? earL : earR) * 0.4f), 0.17f, 0.74f, MwPink, MwPinkShade, Color.Empty, 0, Color.Empty);
                    foreach (int s in Sides) PkLeg(g, k, D, P(s * 0.06f, 0.19f), Foot(p, s, s * 0.075f, 0.31f), s, MwPink, MwPinkShade, 0.072f, 0.078f);
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.16f, -0.05f, 0.32f, 0.36f), bg =>
                        VBlob(bg, k, D, MwPink, MwPinkShade, 0, 0.5f, 0, -0.01f, 0.08f, 0.02f, 0.11f, 0.1f, 0.115f, 0.19f, 0.08f, 0.25f, 0, 0.27f, -0.08f, 0.25f, -0.115f, 0.19f,
                              -0.11f, 0.1f, -0.08f, 0.02f));
                    break;
                }
                case KindPsyduckSd:
                {
                    // Three thick black hairs on its head, its pale webbed feet and its round body.
                    float sw = (float)Math.Sin(p.Now / 800.0) * 0.008f;
                    float[][] hairs = { new float[] { -0.025f, -0.05f, -0.1f, -0.13f, -0.47f }, new float[] { 0, 0, 0.005f, 0.01f, -0.5f }, new float[] { 0.025f, 0.06f, 0.11f, 0.135f, -0.465f } };
                    foreach (float[] h in hairs)
                        using (GraphicsPath hp = Strand(P(h[0] * D, -0.33f * D), P(h[1] * D, -0.4f * D), P(h[2] * D, (h[4] + 0.02f) * D), P((h[3] + sw) * D, h[4] * D), 0.045f * D, 0.032f * D))
                            Fill(g, hp, hp.GetBounds(), Color.FromArgb(52, 50, 60), Color.FromArgb(12, 10, 16), 0f);
                    foreach (int s in Sides) PkFoot(g, k, D, Foot(p, s, s * 0.095f, 0.322f), s, Color.FromArgb(248, 240, 222), Color.FromArgb(214, 202, 180), 0.12f);
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.24f, -0.06f, 0.48f, 0.44f), bg =>
                        VBlob(bg, k, D, PsYellow, PsYellowShade, 0, 0.5f, 0, -0.03f, 0.13f, -0.01f, 0.19f, 0.08f, 0.21f, 0.18f, 0.19f, 0.27f, 0.13f, 0.325f, 0, 0.34f,
                              -0.13f, 0.325f, -0.19f, 0.27f, -0.21f, 0.18f, -0.19f, 0.08f, -0.13f, -0.01f));
                    break;
                }
                case KindTogepiSd:
                {
                    // Its feet under the shell (the crown of spikes is part of its head, the shell goes in front: PkFront).
                    foreach (int s in Sides) PkToes(g, k, D, Foot(p, s, s * 0.1f, 0.33f), TgCream, TgCreamShade, 0.09f, 0, Color.Empty);
                    break;
                }
                case KindMeowthSd:
                {
                    MeowthTailSd(g, k, D, p);
                    DsCached(g, D, TnId(k, 7), new RectangleF(-0.52f, -0.56f, 1.04f, 0.6f), bg =>
                    {
                        MeowthWhiskersSd(bg, k, D);
                        foreach (int s in Sides)
                            PkEar(bg, k, D, s * 0.15f, -0.27f, s * 30, 0.22f, 0.72f, Color.FromArgb(48, 42, 48), Color.FromArgb(24, 20, 24), Color.Empty, 0, Color.FromArgb(198, 148, 120));
                    });
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.08f, 0.322f);
                        VLimb(g, k, D, MeCream, 0.048f, P(s * 0.055f, 0.24f), P(f.X, f.Y - 0.02f), 0);
                        PkToes(g, k, D, f, MeBrown, MeBrownShade, 0.1f, 3, Color.Empty);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.16f, -0.06f, 0.32f, 0.36f), bg =>
                        VBlob(bg, k, D, MeCream, MeCreamShade, 0, 0.5f, 0, -0.03f, 0.075f, -0.012f, 0.11f, 0.06f, 0.122f, 0.15f, 0.105f, 0.232f, 0.06f, 0.272f, 0, 0.282f,
                              -0.06f, 0.272f, -0.105f, 0.232f, -0.122f, 0.15f, -0.11f, 0.06f, -0.075f, -0.012f));
                    break;
                }
                case KindPiplupSd:
                {
                    // Its orange feet, the pale blue body with the two white spots and the dark blue cape over its shoulders.
                    foreach (int s in Sides) PkToes(g, k, D, Foot(p, s, s * 0.085f, 0.322f), PpOrange, PpOrangeShade, 0.11f, 3, Color.Empty);
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.22f, -0.06f, 0.44f, 0.42f), bg =>
                    {
                        VBlob(bg, k, D, PpLight, PpLightShade, 0, 0.5f, 0, -0.03f, 0.12f, -0.01f, 0.17f, 0.08f, 0.185f, 0.18f, 0.17f, 0.27f, 0.12f, 0.325f, 0, 0.34f,
                              -0.12f, 0.325f, -0.17f, 0.27f, -0.185f, 0.18f, -0.17f, 0.08f, -0.12f, -0.01f);
                        VBlob(bg, k, D, PpBlue, PpBlueShade, 0, 0.45f, -0.16f, 0.0f, 0.16f, 0.0f, 0.18f, 0.06f, 0.1f, 0.075f, 0, 0.06f, -0.1f, 0.075f, -0.18f, 0.06f);
                        for (int i = 0; i < 2; i++) VOval(bg, k, D, Color.FromArgb(252, 252, 252), Color.Empty, 0, 0, 0.13f + i * 0.06f, 0.036f, 0.036f);
                    });
                    break;
                }
                case KindMimikyuSd:
                {
                    // Its wooden zigzag of a tail, the ears (one standing with its tip bent over, one hanging limp), and the
                    // rag narrowing under the head and then spreading to the ground in its ragged hem, dark claws peeping out
                    // under it and two dark holes cut in its front.
                    GraphicsState ts = TailSide(g, p);
                    using (GraphicsPath t = KPoly(D, 0.12f, 0.25f, 0.24f, 0.17f, 0.21f, 0.12f, 0.33f, 0.05f, 0.3f, 0.0f, 0.42f, -0.08f, 0.45f, -0.04f, 0.35f, 0.04f, 0.38f, 0.09f,
                                                  0.26f, 0.17f, 0.29f, 0.21f, 0.15f, 0.29f))
                        VFill(g, k, t, Color.FromArgb(132, 96, 62), Color.FromArgb(92, 62, 36), D, 0);
                    g.Restore(ts);
                    using (GraphicsPath e = PkTube(D, -0.1f, -0.25f, 0.078f, -0.115f, -0.35f, 0.07f, -0.13f, -0.415f, 0.058f, -0.19f, -0.45f, 0.05f))
                        VFill(g, k, e, MmCloth, MmClothShade, D, 0);
                    using (GraphicsPath tip = PkTube(D, -0.18f, -0.452f, 0.052f, -0.225f, -0.462f, 0.04f, -0.255f, -0.44f, 0.022f))
                        VFill(g, k, tip, MmDark, Color.Empty, D, 0);
                    PkEar(g, k, D, 0.13f, -0.24f, 74 + earR * 0.3f, 0.2f, 0.42f, MmCloth, MmClothShade, MmDark, 0.32f, Color.Empty);
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.32f, -0.08f, 0.64f, 0.46f), bg =>
                    {
                        float[] hx = { 0.29f, 0.24f, 0.19f, 0.13f, 0.07f, 0.0f, -0.07f, -0.13f, -0.19f, -0.24f, -0.29f };
                        float[] hy = { 0.31f, 0.345f, 0.31f, 0.35f, 0.315f, 0.352f, 0.315f, 0.35f, 0.31f, 0.345f, 0.31f };
                        for (int i = 1; i < hx.Length; i += 2)
                            using (GraphicsPath cl = PkOutline(D, 0.5f, hx[i] - 0.025f, hy[i] - 0.03f, 0, hx[i] + 0.025f, hy[i] - 0.03f, 0, hx[i], hy[i] + 0.02f, 1))
                                VFill(bg, k, cl, MmDark, Color.Empty, D, 0);
                        using (GraphicsPath c = new GraphicsPath())
                        {
                            c.AddCurve(new PointF[] { P(-0.15f * D, -0.03f * D), P(-0.15f * D, 0.06f * D), P(-0.23f * D, 0.2f * D), P(-0.29f * D, 0.31f * D) }, 0.5f);
                            for (int i = hx.Length - 1; i > 0; i--) c.AddLine(P(hx[i] * D, hy[i] * D), P(hx[i - 1] * D, hy[i - 1] * D));
                            c.AddCurve(new PointF[] { P(0.29f * D, 0.31f * D), P(0.23f * D, 0.2f * D), P(0.15f * D, 0.06f * D), P(0.15f * D, -0.03f * D) }, 0.5f);
                            c.CloseFigure();
                            VFill(bg, k, c, MmCloth, MmClothShade, D, 0);
                        }
                        foreach (int s in Sides) Ellipse(bg, MmDark, (s * 0.045f - 0.014f) * D, 0.2f * D, 0.028f * D, 0.05f * D);
                    });
                    break;
                }
                case KindLucarioSd:
                {
                    // The tail, the four black locks hanging from the back of its head (wider at their ends), the tall ears
                    // with their dark insides, its black legs, and the body: the cream chest with its spiky collar and white
                    // spike, the black band of fur round its waist, the blue hips.
                    LucarioTailSd(g, k, D, p);
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.34f, -0.3f, 0.68f, 0.38f), bg =>
                    {
                        foreach (int s in Sides)
                        {
                            using (GraphicsPath a = PkTube(D, s * 0.14f, -0.24f, 0.042f, s * 0.2f, -0.17f, 0.05f, s * 0.232f, -0.09f, 0.06f, s * 0.236f, -0.02f, 0.066f))
                                VFill(bg, k, a, LcBlack, LcBlackShade, D, 0);
                            using (GraphicsPath a = PkTube(D, s * 0.12f, -0.19f, 0.04f, s * 0.168f, -0.12f, 0.048f, s * 0.188f, -0.04f, 0.056f, s * 0.184f, 0.03f, 0.062f))
                                VFill(bg, k, a, LcBlack, LcBlackShade, D, 0);
                        }
                    });
                    foreach (int s in Sides) PkEar(g, k, D, s * 0.098f, -0.3f, s * (13 + (s < 0 ? earL : earR) * 0.5f), 0.25f, 0.44f, LcBlue, LcBlueShade, Color.Empty, 0, LcBlack);
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.07f, 0.318f);
                        VLimb(g, k, D, LcBlack, 0.06f, P(s * 0.065f, 0.22f), P(f.X, f.Y - 0.02f), 0);
                        VOval(g, k, D, LcBlack, LcBlackShade, 0, f.X + s * 0.006f, f.Y + 0.002f, 0.085f, 0.048f);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.16f, -0.05f, 0.32f, 0.34f), bg => LucarioBodySd(bg, k, D));
                    break;
                }
                case KindDragoniteSd:
                {
                    // Its wings and tail, the little horn and the antennae, its big feet, and the round body with the
                    // striped cream belly.
                    foreach (int s in Sides) DragoniteWingSd(g, k, D, s, (float)(s * (55 + Math.Sin(p.Now / 300.0) * 7)));
                    DragoniteTailSd(g, k, D, p);
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.28f, -0.67f, 0.56f, 0.3f), bg =>
                    {
                        PkEar(bg, k, D, 0, -0.418f, 0, 0.065f, 0.85f, DnOrange, DnOrangeShade, Color.Empty, 0, Color.Empty);
                        // The two thin antennae, rising from its brow and curling over at their ends.
                        foreach (int s in Sides)
                        {
                            PointF a0 = P(s * 0.05f * D, -0.412f * D), a1 = P(s * 0.055f * D, -0.53f * D), a2 = P(s * 0.14f * D, -0.628f * D), a3 = P(s * 0.215f * D, -0.578f * D);
                            PointF c1 = P(s * 0.248f * D, -0.556f * D), c2 = P(s * 0.24f * D, -0.514f * D), c3 = P(s * 0.208f * D, -0.522f * D);
                            using (Pen o = new Pen(TnInk(k), 0.014f * D + TnW(D, k) * 2)) { o.StartCap = o.EndCap = LineCap.Round; bg.DrawBezier(o, a0, a1, a2, a3); bg.DrawBezier(o, a3, c1, c2, c3); }
                            using (Pen f = new Pen(DnCream, 0.014f * D)) { f.StartCap = f.EndCap = LineCap.Round; bg.DrawBezier(f, a0, a1, a2, a3); bg.DrawBezier(f, a3, c1, c2, c3); }
                        }
                    });
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.115f, 0.322f);
                        VLimb(g, k, D, DnOrange, 0.11f, P(s * 0.11f, 0.22f), P(f.X, f.Y - 0.025f), 0);
                        PkToes(g, k, D, f, DnOrange, DnOrangeShade, 0.125f, 3, Color.FromArgb(252, 252, 250));
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.25f, -0.16f, 0.5f, 0.52f), bg =>
                    {
                        // Big and round under its small head, the cream belly striped across.
                        using (GraphicsPath b = KCurve(D, 0.5f, 0, -0.145f, 0.08f, -0.135f, 0.125f, -0.06f, 0.172f, 0.04f, 0.215f, 0.15f, 0.214f, 0.252f, 0.158f, 0.316f, 0, 0.338f,
                                                       -0.158f, 0.316f, -0.214f, 0.252f, -0.215f, 0.15f, -0.172f, 0.04f, -0.125f, -0.06f, -0.08f, -0.135f))
                        {
                            VFill(bg, k, b, DnOrange, DnOrangeShade, D, 0);
                            Region old = bg.Clip;
                            bg.SetClip(b, CombineMode.Intersect);
                            using (GraphicsPath belly = KCurve(D, 0.5f, 0, -0.095f, 0.062f, -0.075f, 0.11f, 0.02f, 0.15f, 0.14f, 0.15f, 0.24f, 0.1f, 0.312f, 0, 0.334f,
                                                               -0.1f, 0.312f, -0.15f, 0.24f, -0.15f, 0.14f, -0.11f, 0.02f, -0.062f, -0.075f))
                            {
                                SoftPaint(bg, belly, DnCream, DnCreamShade, D, 40);
                                bg.SetClip(belly, CombineMode.Intersect);
                                using (Pen st = new Pen(Color.FromArgb(130, 186, 146, 86), Math.Max(0.8f, D * 0.006f)))
                                    for (int i = 0; i < 9; i++)
                                    {
                                        float y = -0.035f + i * 0.042f;
                                        bg.DrawBezier(st, -0.17f * D, (y - 0.006f) * D, -0.06f * D, (y + 0.012f) * D, 0.06f * D, (y + 0.012f) * D, 0.17f * D, (y - 0.006f) * D);
                                    }
                            }
                            bg.Clip = old;
                            old.Dispose();
                        }
                    });
                    break;
                }
                case KindDittoSd: break;   // all of it is its main shape
                case KindGrootSd: GrootBack(g, k, D, p); break;
                case KindMewtwoSd:
                {
                    // Its thick tail, the tube from the back of its head down to its back, the body (the chest narrowing to
                    // its waist, the purple of its lower body), and in front of it the strong thighs down to thin shins and
                    // the feet with their round toes.
                    Mewtwo2TailSd(g, k, D, p);
                    DsCached(g, D, TnId(k, 1), new RectangleF(-0.2f, -0.5f, 0.42f, 0.6f), bg =>
                    {
                        // The two short blunt horns rising from the back of its skull, leaning out; the tube from the back of
                        // its head down to its back.
                        foreach (int s in Sides)
                            using (GraphicsPath hn = PkTube(D, s * 0.084f, -0.31f, 0.086f, s * 0.114f, -0.374f, 0.066f, s * 0.14f, -0.425f, 0.046f))
                                VFill(bg, k, hn, M2Pale, M2PaleShade, D, 0);
                        using (GraphicsPath tb = PkTube(D, 0.09f, -0.085f, 0.036f, 0.165f, -0.062f, 0.038f, 0.195f, -0.005f, 0.038f, 0.16f, 0.045f, 0.036f))
                            VFill(bg, k, tb, M2Pale, M2PaleShade, D, 0);
                    });
                    // Short legs under its body, the feet with their two round toes.
                    foreach (int s in Sides)
                    {
                        PointF f = Foot(p, s, s * 0.07f, 0.318f);
                        VLimb(g, k, D, M2Pale, 0.07f, P(s * 0.07f, 0.22f), P(f.X, f.Y - 0.016f), 0);
                        VOval(g, k, D, M2Pale, M2PaleShade, 0, f.X, f.Y - 0.004f, 0.074f, 0.042f);
                        foreach (int i in new int[] { -1, 1 }) VOval(g, k, D, M2Pale, M2PaleShade, 0, f.X + i * 0.022f, f.Y + 0.013f, 0.034f, 0.032f);
                    }
                    DsCached(g, D, TnId(k, 3), new RectangleF(-0.16f, -0.06f, 0.32f, 0.34f), bg =>
                    {
                        // Narrow at the chest, rounding out below; the big purple belly on its front.
                        using (GraphicsPath b = PkOutline(D, 0.5f, PkHalf(0, -0.045f, 0, 0.034f, -0.04f, 0, 0.056f, -0.004f, 0, 0.098f, 0.03f, 0, 0.104f, 0.074f, 0,
                                                                          0.11f, 0.13f, 0, 0.13f, 0.19f, 0, 0.122f, 0.236f, 0, 0.088f, 0.262f, 0, 0.04f, 0.272f, 0, 0, 0.274f, 0)))
                        {
                            VFill(bg, k, b, M2Pale, M2PaleShade, D, 0);
                            Region old = bg.Clip;
                            bg.SetClip(b, CombineMode.Intersect);
                            TnLine(bg, Color.FromArgb(90, M2PaleShade), 0.008f, D, -0.065f, 0.065f, 0, 0.08f, 0.065f, 0.065f);
                            using (GraphicsPath pb = KCurve(D, 0.5f, 0, 0.098f, 0.064f, 0.106f, 0.1f, 0.146f, 0.106f, 0.198f, 0.08f, 0.24f, 0, 0.256f, -0.08f, 0.24f,
                                                            -0.106f, 0.198f, -0.1f, 0.146f, -0.064f, 0.106f))
                            {
                                SoftPaint(bg, pb, M2Purple, M2PurpleShade, D, 30);
                                MascotParts.Stroke(bg, pb, Color.FromArgb(70, M2PurpleShade), Math.Max(0.8f, D * 0.005f));
                            }
                            bg.Clip = old;
                            old.Dispose();
                        }
                    });
                    break;
                }
                default:
                {
                    // Jigglypuff: the ears go with the head; its feet under it.
                    foreach (int s in Sides) PkEar(g, k, D, s * 0.2f, -0.235f, s * (40 + (s < 0 ? earL : earR) * 0.4f), 0.19f, 1.05f, JgPink, JgPinkShade, Color.Empty, 0, Color.FromArgb(56, 48, 70));
                    foreach (int s in Sides) PkToes(g, k, D, Foot(p, s, s * 0.12f, 0.3f), JgPink, JgPinkShade, 0.11f, 0, Color.Empty);
                    break;
                }
            }
            if (!p.HideHands && !PkNoHands(k)) foreach (int s in Sides) ToonArm(g, D, k, s, DsHand(p, geo, s));
        }

        // A pointed ear on its base at x, y, turned rot degrees: its colour, a dark tip (the top tipT of it) or a dark inside.
        static void PkEar(Graphics g, int k, float D, float x, float y, float rot, float len, float width, Color c, Color shade, Color tip, float tipT, Color inner)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x * D, y * D);
            g.RotateTransform(rot);
            float L = len * D, w = len * width * D;
            using (GraphicsPath e = new GraphicsPath())
            {
                e.AddBezier(P(-w * 0.5f, 0.03f * D), P(-w * 0.66f, -L * 0.5f), P(-w * 0.2f, -L * 0.9f), P(0, -L));
                e.AddBezier(P(0, -L), P(w * 0.2f, -L * 0.9f), P(w * 0.66f, -L * 0.5f), P(w * 0.5f, 0.03f * D));
                e.CloseFigure();
                VFill(g, k, e, c, shade, D, 0);
                Region old = g.Clip;
                g.SetClip(e, CombineMode.Intersect);
                if (!tip.IsEmpty)
                    using (GraphicsPath t = new GraphicsPath())
                    {
                        t.AddRectangle(new RectangleF(-w, -L * 1.1f, w * 2, L * (0.1f + tipT)));
                        SoftPaint(g, t, tip, Color.Empty, D, 20);
                    }
                if (!inner.IsEmpty)
                    using (GraphicsPath i = new GraphicsPath())
                    {
                        i.AddBezier(P(-w * 0.24f, -L * 0.08f), P(-w * 0.32f, -L * 0.45f), P(-w * 0.1f, -L * 0.74f), P(0, -L * 0.8f));
                        i.AddBezier(P(0, -L * 0.8f), P(w * 0.1f, -L * 0.74f), P(w * 0.32f, -L * 0.45f), P(w * 0.24f, -L * 0.08f));
                        i.CloseFigure();
                        FillSolid(g, i, inner);
                    }
                g.Clip = old;
                old.Dispose();
            }
            g.Restore(st);
        }

        // A short leg from the hip a down to the foot f (units of D), w thick, ending in a paw fw wide.
        static void PkLeg(Graphics g, int k, float D, PointF a, PointF f, int s, Color c, Color shade, float w, float fw)
        {
            VLimb(g, k, D, c, w, a, P(f.X, f.Y - 0.02f), 0);
            PkFoot(g, k, D, f, s, c, shade, fw);
        }

        static void PkFoot(Graphics g, int k, float D, PointF f, int s, Color c, Color shade, float w)
        {
            VOval(g, k, D, c, shade, 0, f.X + s * 0.006f, f.Y + 0.006f, w, w * 0.58f);
        }

        // A foot at f (units of D), w wide: round, with n toes marked on it, or n little claws of colour claw at its front.
        static void PkToes(Graphics g, int k, float D, PointF f, Color c, Color shade, float w, int n, Color claw)
        {
            float h = w * 0.56f, y = f.Y + 0.004f;
            VOval(g, k, D, c, shade, 0, f.X, y, w, h);
            for (int i = 0; i < n; i++)
            {
                if (claw.IsEmpty)
                {
                    if (i == 0) continue;
                    float lx = f.X + (i - n / 2f) * w * 0.26f;
                    TnLine(g, Color.FromArgb(120, Darker(c, 0.35)), 0.006f, D, lx, y + h * 0.1f, lx, y + h * 0.44f);
                }
                else
                {
                    float tx = f.X + (i - (n - 1) / 2f) * w * 0.27f;
                    using (GraphicsPath cl = PkOutline(D, 0.5f, tx - w * 0.075f, y + h * 0.3f, 0, tx + w * 0.075f, y + h * 0.3f, 0, tx + w * 0.02f, y + h * 0.3f + w * 0.12f, 1))
                        VFill(g, k, cl, claw, Color.FromArgb(206, 208, 216), D, 0);
                }
            }
        }

        // Pikachu's tail behind it on the side it walks away from: the zigzag bolt, brown where it meets its back.
        static void PikaTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            g.TranslateTransform(0.14f * D, 0.24f * D);
            g.RotateTransform((float)(Math.Sin(p.Now / 640.0) * 4));
            g.TranslateTransform(-0.14f * D, -0.24f * D);
            DsCached(g, D, TnId(k, 1), new RectangleF(0.08f, -0.32f, 0.52f, 0.62f), bg =>
            {
                using (GraphicsPath t = KPoly(D, 0.12f, 0.22f, 0.24f, 0.12f, 0.2f, 0.07f, 0.34f, -0.04f, 0.3f, -0.09f, 0.46f, -0.27f, 0.56f, -0.14f, 0.4f, 0.0f, 0.44f, 0.05f,
                                             0.32f, 0.14f, 0.36f, 0.19f, 0.2f, 0.28f))
                {
                    VFill(bg, k, t, PkYellow, PkYellowShade, D, 0);
                    Region old = bg.Clip;
                    bg.SetClip(t, CombineMode.Intersect);
                    using (GraphicsPath b = new GraphicsPath())
                    {
                        b.AddRectangle(new RectangleF(0.08f * D, 0.16f * D, 0.32f * D, 0.16f * D));
                        SoftPaint(bg, b, PkBrown, Color.Empty, D, 20);
                    }
                    bg.Clip = old;
                    old.Dispose();
                }
            });
            g.Restore(st);
        }

        // Eevee's big bushy tail rising behind it, its tip cream.
        static void EeveeTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            g.TranslateTransform(0.12f * D, 0.22f * D);
            g.RotateTransform((float)(38 + Math.Sin(p.Now / (p.Walk > 0.3 ? 140.0 : 520.0)) * 8));
            DsCached(g, D, TnId(k, 1), new RectangleF(-0.12f, -0.4f, 0.24f, 0.44f), bg =>
            {
                using (GraphicsPath t = KCurve(D, 0.5f, 0, 0.02f, 0.07f, -0.06f, 0.1f, -0.18f, 0.09f, -0.3f, 0.03f, -0.38f, -0.04f, -0.34f, -0.08f, -0.24f, -0.08f, -0.12f, -0.05f, -0.03f))
                {
                    VFill(bg, k, t, EvBrown, EvBrownShade, D, 0);
                    Region old = bg.Clip;
                    bg.SetClip(t, CombineMode.Intersect);
                    using (GraphicsPath c = KCurve(D, 0.5f, -0.12f, -0.26f, -0.04f, -0.25f, 0.06f, -0.27f, 0.14f, -0.24f, 0.14f, -0.45f, -0.12f, -0.45f))
                        SoftPaint(bg, c, EvCream, EvCreamShade, D, 40);
                    bg.Clip = old;
                    old.Dispose();
                }
            });
            g.Restore(st);
        }

        // Charmander's thick tail curving out behind it, the flame burning at its tip.
        static void CharTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 700.0) * 0.015f;
            PointF a = P(0.08f, 0.26f), b = P(0.25f, 0.33f), c = P(0.35f, 0.22f), e = P(0.36f + sw, 0.08f);
            using (Pen ink = new Pen(TnInk(k), 0.07f * D + TnW(D, k) * 2))
            {
                ink.StartCap = LineCap.Round;
                ink.EndCap = LineCap.Round;
                g.DrawBezier(ink, a.X * D, a.Y * D, b.X * D, b.Y * D, c.X * D, c.Y * D, e.X * D, e.Y * D);
            }
            SoftLimb(g, k, D, CmOrange, 0.07f, P(a.X * D, a.Y * D), P(b.X * D, b.Y * D), P(c.X * D, c.Y * D), P(e.X * D, e.Y * D));
            PkFlame(g, D, e.X * D, (e.Y - 0.02f) * D, p.Now);
            g.Restore(st);
        }

        // A flame at x, y (pixels) flickering: red outside, orange, a yellow heart.
        static void PkFlame(Graphics g, float D, float x, float y, double now)
        {
            float f = (float)Math.Sin(now / 90.0), f2 = (float)Math.Sin(now / 61.0 + 1.3);
            Glow(g, x, y - 0.03f * D, 0.22f * D, 0.24f * D, Color.FromArgb(255, 150, 40), 110);
            Color[] cs = { Color.FromArgb(234, 64, 34), Color.FromArgb(255, 146, 40), Color.FromArgb(255, 232, 110) };
            float[] ks = { 1f, 0.72f, 0.42f };
            for (int i = 0; i < 3; i++)
            {
                float s = ks[i], h = (0.15f + f * 0.012f) * s * D, w = 0.085f * s * D, lean = (f2 * 0.012f) * D;
                using (GraphicsPath fl = new GraphicsPath())
                {
                    fl.AddBezier(new PointF(x, y + w * 0.55f), new PointF(x - w * 0.95f, y + w * 0.4f), new PointF(x - w * 0.55f, y - h * 0.45f), new PointF(x + lean, y - h));
                    fl.AddBezier(new PointF(x + lean, y - h), new PointF(x + w * 0.55f, y - h * 0.45f), new PointF(x + w * 0.95f, y + w * 0.4f), new PointF(x, y + w * 0.55f));
                    fl.CloseFigure();
                    FillSolid(g, fl, cs[i]);
                }
            }
        }

        // Bulbasaur's bulb: big and round, rising to a point, its leaves folded round it.
        static void BulbSd(Graphics g, int k, float D)
        {
            // An onion of folded leaves, swelling wide and rising to a soft point; each fold a dark crease lit along its edge.
            using (GraphicsPath b = PkOutline(D, 0.5f, 0.03f, -0.548f, 1, 0.11f, -0.49f, 0, 0.21f, -0.415f, 0, 0.275f, -0.315f, 0, 0.282f, -0.215f, 0, 0.2f, -0.145f, 0,
                                              0, -0.128f, 0, -0.2f, -0.145f, 0, -0.282f, -0.215f, 0, -0.272f, -0.32f, 0, -0.19f, -0.43f, 0, -0.07f, -0.505f, 0))
            {
                VFill(g, k, b, BbGreen, BbGreenShade, D, 0);
                Region old = g.Clip;
                g.SetClip(b, CombineMode.Intersect);
                float[][] folds = { new float[] { 0.03f, -0.548f, -0.08f, -0.42f, -0.1f, -0.26f, -0.06f, -0.13f }, new float[] { 0.03f, -0.548f, 0.13f, -0.42f, 0.16f, -0.27f, 0.12f, -0.13f },
                                    new float[] { -0.12f, -0.47f, -0.22f, -0.38f, -0.25f, -0.25f, -0.21f, -0.14f } };
                using (Pen crease = new Pen(Color.FromArgb(160, 34, 92, 44), Math.Max(0.9f, D * 0.009f)))
                using (Pen lit = new Pen(Color.FromArgb(110, 214, 246, 170), Math.Max(0.8f, D * 0.007f)))
                {
                    crease.StartCap = crease.EndCap = lit.StartCap = lit.EndCap = LineCap.Round;
                    foreach (float[] f in folds)
                    {
                        g.DrawBezier(crease, P(f[0] * D, f[1] * D), P(f[2] * D, f[3] * D), P(f[4] * D, f[5] * D), P(f[6] * D, f[7] * D));
                        float o = -0.012f * D;
                        g.DrawBezier(lit, P(f[0] * D + o, f[1] * D), P(f[2] * D + o, f[3] * D), P(f[4] * D + o, f[5] * D), P(f[6] * D + o, f[7] * D));
                    }
                }
                g.Clip = old;
                old.Dispose();
            }
        }

        // Squirtle's body: the brown shell round its back with its pale rim, and the cream plates of its belly.
        static void SquirtleBody(Graphics g, int k, float D)
        {
            VOval(g, k, D, SqShell, SqShellShade, 0, 0, 0.165f, 0.42f, 0.34f);
            VOval(g, k, D, Color.FromArgb(250, 246, 236), Color.FromArgb(214, 206, 192), 0, 0, 0.17f, 0.34f, 0.31f);
            using (GraphicsPath b = KCurve(D, 0.5f, 0, 0.03f, 0.11f, 0.05f, 0.15f, 0.14f, 0.14f, 0.24f, 0.09f, 0.31f, 0, 0.325f, -0.09f, 0.31f, -0.14f, 0.24f, -0.15f, 0.14f, -0.11f, 0.05f))
            {
                VFill(g, k, b, SqCream, SqCreamShade, D, 0);
                Region old = g.Clip;
                g.SetClip(b, CombineMode.Intersect);
                using (Pen seam = new Pen(Color.FromArgb(150, 176, 132, 56), Math.Max(0.8f, D * 0.007f)))
                {
                    g.DrawLine(seam, 0, 0.03f * D, 0, 0.33f * D);
                    foreach (float y in new float[] { 0.12f, 0.2f, 0.27f }) g.DrawLine(seam, -0.16f * D, y * D, 0.16f * D, y * D);
                }
                g.Clip = old;
                old.Dispose();
            }
        }

        // Squirtle's tail curling up behind it.
        static void SquirtleTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 800.0) * 0.01f;
            PointF[] pts = new PointF[22];
            PointF c = P(0.33f + sw, 0.17f);
            for (int i = 0; i < pts.Length; i++)
            {
                float t = i / (float)(pts.Length - 1);
                if (t < 0.35f) pts[i] = P((0.12f + t / 0.35f * (c.X + 0.085f - 0.12f)) * D, (0.26f + (float)Math.Sin(t / 0.35f * Math.PI / 2) * 0.02f) * D);
                else
                {
                    double a = Math.PI / 2 - (t - 0.35f) / 0.65f * Math.PI * 1.7;
                    float r = 0.085f * (1 - 0.55f * (t - 0.35f) / 0.65f);
                    pts[i] = P((c.X + (float)Math.Cos(a) * r) * D, (c.Y + (float)Math.Sin(a) * r) * D);
                }
            }
            using (Pen ink = new Pen(TnInk(k), 0.06f * D + TnW(D, k) * 2))
            using (Pen body = new Pen(SqBlueShade, 0.06f * D))
            using (Pen lit = new Pen(SqBlue, 0.04f * D))
            {
                ink.StartCap = ink.EndCap = body.StartCap = body.EndCap = lit.StartCap = lit.EndCap = LineCap.Round;
                ink.LineJoin = body.LineJoin = lit.LineJoin = LineJoin.Round;
                g.DrawCurve(ink, pts, 0.5f);
                g.DrawCurve(body, pts, 0.5f);
                g.DrawCurve(lit, pts, 0.5f);
            }
            g.Restore(st);
        }

        // The curl of hair on Jigglypuff's brow.
        static void JigglyCurl(Graphics g, int k, float D)
        {
            PointF[] pts = new PointF[18];
            for (int i = 0; i < pts.Length; i++)
            {
                float t = i / (float)(pts.Length - 1);
                double a = Math.PI * 1.15 + t * Math.PI * 1.75;
                float r = 0.075f * (1 - 0.62f * t);
                pts[i] = P((0.035f + (float)Math.Cos(a) * r) * D, (-0.31f + (float)Math.Sin(a) * r) * D);
            }
            using (Pen ink = new Pen(TnInk(k), 0.05f * D + TnW(D, k) * 2))
            using (Pen body = new Pen(JgPinkShade, 0.05f * D))
            using (Pen lit = new Pen(JgPink, 0.032f * D))
            {
                ink.StartCap = ink.EndCap = body.StartCap = body.EndCap = lit.StartCap = lit.EndCap = LineCap.Round;
                ink.LineJoin = body.LineJoin = lit.LineJoin = LineJoin.Round;
                g.DrawCurve(ink, pts, 0.5f);
                g.DrawCurve(body, pts, 0.5f);
                g.DrawCurve(lit, pts, 0.5f);
            }
        }

        // A foot of Snorlax's held out in front of it: the big cream sole, the brown pad on it and three white claws on top.
        static void SnorlaxFootSd(Graphics g, int k, float D, PointF f, int s)
        {
            VOval(g, k, D, SlCream, SlCreamShade, 0, f.X, f.Y, 0.15f, 0.125f);
            VOval(g, k, D, SlPad, Darker(SlPad, 0.25), 0, f.X + s * 0.008f, f.Y + 0.012f, 0.08f, 0.07f);
            for (int i = -1; i <= 1; i++)
                using (GraphicsPath c = KPoly(D, f.X + i * 0.035f - 0.012f, f.Y - 0.05f, f.X + i * 0.035f + 0.012f, f.Y - 0.05f, f.X + i * 0.032f, f.Y - 0.085f))
                    VFill(g, k, c, Color.FromArgb(250, 250, 248), Color.FromArgb(210, 210, 216), D, 0);
        }

        // Mew's long thin tail looping up behind it, its end swelling into an oval.
        static void MewTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 900.0) * 0.02f;
            PointF a0 = P(0.04f * D, 0.24f * D), c1 = P(0.26f * D, 0.37f * D), c2 = P(0.48f * D, 0.18f * D), a1 = P(0.42f * D, -0.02f * D);
            PointF c3 = P(0.37f * D, -0.2f * D), c4 = P((0.22f + sw) * D, -0.28f * D), a2 = P((0.28f + sw) * D, -0.42f * D);
            using (Pen ink = new Pen(TnInk(k), 0.026f * D + TnW(D, k) * 2))
            {
                ink.StartCap = ink.EndCap = LineCap.Round;
                g.DrawBezier(ink, a0, c1, c2, a1);
                g.DrawBezier(ink, a1, c3, c4, a2);
            }
            SoftLimb(g, k, D, MwPink, 0.026f, a0, c1, c2, a1);
            SoftLimb(g, k, D, MwPink, 0.026f, a1, c3, c4, a2);
            GraphicsState t = g.Save();
            g.TranslateTransform(a2.X, a2.Y);
            g.RotateTransform(30);
            VOval(g, k, D, MwPink, MwPinkShade, 0, 0, -0.03f, 0.06f, 0.095f);
            g.Restore(t);
            g.Restore(st);
        }

        // Meowth's thin cream tail rising behind it, rolled up at the end in a brown curl.
        static void MeowthTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState ts = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 650.0) * 0.02f * D, w = 0.03f * D, r0 = 0.05f * D;
            PointF a0 = P(0.06f * D, 0.26f * D), a1 = P(0.3f * D, 0.38f * D), a2 = P(0.48f * D, 0.32f * D), a3 = P(0.46f * D + sw, 0.15f * D);
            PointF[] curl = new PointF[18];
            for (int i = 0; i < curl.Length; i++)
            {
                double t = -i / (double)(curl.Length - 1) * 1.6 * Math.PI;
                float r = r0 * (1 - 0.5f * i / (curl.Length - 1));
                curl[i] = P(a3.X - r0 + r * (float)Math.Cos(t), a3.Y + r * (float)Math.Sin(t));
            }
            using (Pen o = new Pen(TnInk(k), w + TnW(D, k) * 2))
            {
                o.StartCap = o.EndCap = LineCap.Round;
                o.LineJoin = LineJoin.Round;
                g.DrawBezier(o, a0, a1, a2, a3);
                g.DrawLines(o, curl);
            }
            SoftLimb(g, k, D, MeCream, 0.03f, a0, a1, a2, a3);
            using (Pen t = new Pen(MeBrown, w))
            using (Pen l = new Pen(Lighter(MeBrown, 0.2), w * 0.45f))
            {
                t.StartCap = t.EndCap = l.StartCap = l.EndCap = LineCap.Round;
                t.LineJoin = l.LineJoin = LineJoin.Round;
                g.DrawLines(t, curl);
                g.DrawLines(l, curl);
            }
            g.Restore(ts);
        }

        // Meowth's long whiskers, two to a side, out of its cheeks and curving up and down at their ends.
        static void MeowthWhiskersSd(Graphics g, int k, float D)
        {
            foreach (int s in Sides)
                for (int i = 0; i < 2; i++)
                {
                    float y0 = i == 0 ? -0.11f : -0.07f, bend = i == 0 ? -0.05f : 0.045f;
                    using (GraphicsPath w = PkTube(D, s * 0.2f, y0, 0.018f, s * 0.28f, y0 + bend * 0.3f, 0.014f, s * 0.35f, y0 + bend * 0.75f, 0.01f, s * 0.39f, y0 + bend * 1.3f, 0.006f))
                        VFill(g, k, w, Color.FromArgb(255, 250, 232), Color.FromArgb(220, 206, 170), D, 0);
                }
        }

        // Togepi's shell, in front of it from the waist down: white, its broken top in a zigzag, the red and blue marks.
        static void TogepiShellSd(Graphics g, int k, float D)
        {
            using (GraphicsPath sh = new GraphicsPath())
            {
                sh.AddLines(new PointF[] { P(-0.235f * D, 0.045f * D), P(-0.18f * D, -0.01f * D), P(-0.12f * D, 0.04f * D), P(-0.06f * D, -0.018f * D), P(0, 0.032f * D),
                                           P(0.06f * D, -0.018f * D), P(0.12f * D, 0.04f * D), P(0.18f * D, -0.01f * D), P(0.235f * D, 0.045f * D) });
                sh.AddCurve(new PointF[] { P(0.235f * D, 0.045f * D), P(0.25f * D, 0.15f * D), P(0.2f * D, 0.28f * D), P(0.1f * D, 0.345f * D), P(0, 0.356f * D),
                                           P(-0.1f * D, 0.345f * D), P(-0.2f * D, 0.28f * D), P(-0.25f * D, 0.15f * D), P(-0.235f * D, 0.045f * D) }, 0.5f);
                sh.CloseFigure();
                VFill(g, k, sh, TgShell, TgShellShade, D, 0);
                Region old = g.Clip;
                g.SetClip(sh, CombineMode.Intersect);
                float[][] marks = { new float[] { -0.12f, 0.12f, 0, 0.055f }, new float[] { 0.11f, 0.1f, 1, 0.05f }, new float[] { -0.02f, 0.25f, 1, 0.06f },
                                    new float[] { 0.17f, 0.24f, 0, 0.045f }, new float[] { -0.19f, 0.25f, 1, 0.04f } };
                foreach (float[] m in marks)
                {
                    float x = m[0], y = m[1], r = m[3];
                    Color c = m[2] == 0 ? Color.FromArgb(232, 74, 74) : Color.FromArgb(74, 140, 230);
                    using (GraphicsPath t = KCurve(D, 0.2f, x - r, y + r * 0.6f, x, y - r * 0.9f, x + r, y + r * 0.6f)) FillSolid(g, t, c);
                    using (GraphicsPath t = KCurve(D, 0.2f, x - r * 0.48f, y + r * 0.3f, x, y - r * 0.38f, x + r * 0.48f, y + r * 0.3f)) FillSolid(g, t, TgShell);
                }
                g.Clip = old;
                old.Dispose();
            }
        }

        // Lucario's body: blue over its shoulders, the cream fur of its chest with its spiky collar and the white spike, the
        // black waist of tufted fur, the blue hips.
        static void LucarioBodySd(Graphics g, int k, float D)
        {
            // The blue hips, like shorts, a little notch between its legs.
            using (GraphicsPath hp = PkOutline(D, 0.5f, PkHalf(0, 0.13f, 0, 0.08f, 0.135f, 0, 0.122f, 0.165f, 0, 0.13f, 0.215f, 0, 0.118f, 0.258f, 0, 0.07f, 0.268f, 0,
                                                               0.025f, 0.252f, 1, 0, 0.236f, 1)))
                VFill(g, k, hp, LcBlue, LcBlueShade, D, 0);
            using (GraphicsPath b = PkOutline(D, 0.5f, PkHalf(0, -0.018f, 0, 0.058f, -0.01f, 0, 0.1f, 0.022f, 0, 0.1f, 0.075f, 0, 0.085f, 0.12f, 0, 0.092f, 0.16f, 0, 0, 0.17f, 0)))
            {
                VFill(g, k, b, LcBlue, LcBlueShade, D, 0);
                Region old = g.Clip;
                g.SetClip(b, CombineMode.Intersect);
                // Blue at its sides, the cream fur down the middle of its chest; the ragged black fur round its waist.
                using (GraphicsPath ch = PkOutline(D, 0.5f, PkHalf(0, -0.02f, 0, 0.052f, -0.012f, 0, 0.072f, 0.03f, 0, 0.068f, 0.082f, 0, 0.05f, 0.118f, 0, 0, 0.13f, 0)))
                    SoftPaint(g, ch, LcCream, LcCreamShade, D, 30);
                using (GraphicsPath w = KPoly(D, -0.14f, 0.12f, -0.085f, 0.112f, -0.06f, 0.135f, -0.03f, 0.112f, 0, 0.138f, 0.03f, 0.112f, 0.06f, 0.135f, 0.085f, 0.112f,
                                             0.14f, 0.12f, 0.14f, 0.2f, -0.14f, 0.2f))
                    SoftPaint(g, w, LcBlack, LcBlackShade, D, 10);
                g.Clip = old;
                old.Dispose();
            }
            // The tufts of its collar, and the white spike on its chest.
            using (GraphicsPath c = PkOutline(D, 0.5f, -0.06f, -0.016f, 0, -0.03f, 0.03f, 1, -0.012f, 0.012f, 1, 0, 0.042f, 1, 0.012f, 0.012f, 1, 0.03f, 0.03f, 1, 0.06f, -0.016f, 0))
                VFill(g, k, c, LcCream, LcCreamShade, D, 0);
            using (GraphicsPath sp = PkOutline(D, 0.5f, -0.026f, 0.098f, 0, -0.012f, 0.072f, 0, 0, 0.05f, 1, 0.012f, 0.072f, 0, 0.026f, 0.098f, 0, 0, 0.106f, 0))
                VFill(g, k, sp, Color.FromArgb(252, 252, 252), Color.FromArgb(190, 196, 214), D, 0);
        }

        // A tail from behind it, out to the side it walks away from: Lucario's blue one, Dragonite's orange one, Mewtwo's
        // thick purple one swelling at its end.
        static void LucarioTailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 700.0) * 0.012f;
            using (GraphicsPath t = PkTube(D, 0.05f, 0.2f, 0.05f, 0.17f, 0.25f, 0.075f, 0.29f, 0.215f + sw * 0.5f, 0.082f, 0.36f + sw, 0.13f + sw, 0.06f, 0.39f + sw, 0.065f + sw, 0.03f))
                VFill(g, k, t, LcBlue, LcBlueShade, D, 0);
            g.Restore(st);
        }

        static void DragoniteTailSd(Graphics g, int k, float D, MascotPose p) { PkTail(g, k, D, p, DnOrange, DnCream, 0.075f, 0); }

        static void Mewtwo2TailSd(Graphics g, int k, float D, MascotPose p)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 700.0) * 0.012f;
            using (GraphicsPath t = PkTube(D, 0.04f, 0.2f, 0.085f, 0.16f, 0.262f, 0.075f, 0.285f, 0.24f, 0.056f, 0.352f + sw, 0.14f, 0.048f, 0.364f + sw, 0.025f, 0.054f,
                                          0.33f + sw * 1.5f, -0.07f, 0.064f))
                VFill(g, k, t, M2Purple, M2PurpleShade, D, 0);
            g.Restore(st);
        }

        // A foot of Gengar's: short and round, three little pointed toes at its front.
        static void GengarFoot(Graphics g, int k, float D, PointF f)
        {
            float x = f.X, y = f.Y;
            using (GraphicsPath ft = PkOutline(D, 0.5f, x - 0.062f, y + 0.004f, 0, x - 0.045f, y - 0.03f, 0, x, y - 0.04f, 0, x + 0.045f, y - 0.03f, 0, x + 0.062f, y + 0.004f, 0,
                                               x + 0.05f, y + 0.036f, 1, x + 0.03f, y + 0.026f, 1, x + 0.012f, y + 0.044f, 1, x - 0.008f, y + 0.03f, 1, x - 0.026f, y + 0.044f, 1,
                                               x - 0.045f, y + 0.026f, 0))
                VFill(g, k, ft, GnPurple, GnPurpleShade, D, 0);
        }

        static void PkTail(Graphics g, int k, float D, MascotPose p, Color c, Color under, float w, float bulb)
        {
            GraphicsState st = TailSide(g, p);
            float sw = (float)Math.Sin(p.Now / 700.0) * 0.015f;
            PointF a = P(0.06f * D, 0.25f * D), b = P(0.2f * D, 0.34f * D), cc = P(0.34f * D, 0.3f * D), e = P((0.38f + sw) * D, (0.17f + sw * 0.5f) * D);
            using (Pen ink = new Pen(TnInk(k), w * D + TnW(D, k) * 2)) { ink.StartCap = ink.EndCap = LineCap.Round; g.DrawBezier(ink, a, b, cc, e); }
            SoftLimb(g, k, D, c, w, a, b, cc, e);
            if (!under.IsEmpty)
                using (Pen u = new Pen(Color.FromArgb(200, under), w * 0.32f * D)) { u.StartCap = u.EndCap = LineCap.Round; g.DrawBezier(u, P(a.X, a.Y + w * 0.28f * D), P(b.X, b.Y + w * 0.28f * D), P(cc.X + w * 0.28f * D, cc.Y), P(e.X + w * 0.2f * D, e.Y)); }
            if (bulb > 0)
            {
                GraphicsState t = g.Save();
                g.TranslateTransform(e.X, e.Y);
                VOval(g, k, D, c, Darker(c, 0.25), 0, 0, -0.01f, bulb, bulb * 1.15f);
                g.Restore(t);
            }
            g.Restore(st);
        }

        // Dragonite's small wing on its back: a teal membrane with a scalloped back edge, held by an orange bone.
        static void DragoniteWingSd(Graphics g, int k, float D, int s, float rot)
        {
            GraphicsState ws = g.Save();
            g.TranslateTransform(s * 0.14f * D, 0.02f * D);
            g.RotateTransform(rot);
            using (GraphicsPath mp = KCurve(D, 0.3f, 0, 0.02f, s * 0.005f, -0.19f, s * 0.06f, -0.14f, s * 0.1f, -0.12f, s * 0.078f, -0.085f, s * 0.11f, -0.04f, s * 0.07f, -0.02f, s * 0.05f, 0.02f))
                VFill(g, k, mp, DnTeal, DnTealShade, D, 0);
            VLimb(g, k, D, DnOrange, 0.022f, P(0, 0.02f), P(s * 0.005f, -0.19f), 0);
            g.Restore(ws);
        }

        // A short blunt horn, a tube with a round end, standing on its base at x, y.
        static void PkHorn(Graphics g, int k, float D, float x, float y, float rot, float len, Color c, Color shade)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x * D, y * D);
            g.RotateTransform(rot);
            float L = len * D, w = 0.06f * D, tw = 0.07f * D;
            using (GraphicsPath h = new GraphicsPath())
            {
                h.AddLine(P(-w / 2, 0.02f * D), P(-tw / 2, -L + tw / 2));
                h.AddArc(-tw / 2, -L, tw, tw, 180, 180);
                h.AddLine(P(tw / 2, -L + tw / 2), P(w / 2, 0.02f * D));
                h.CloseFigure();
                VFill(g, k, h, c, shade, D, 0);
            }
            g.Restore(st);
        }

        static bool PkHasFront(int k) { return k == KindTogepiSd; }

        // In front of everything, not turning with the face: Togepi's shell.
        static void PkFront(Graphics g, int k, float D)
        {
            if (k == KindTogepiSd) DsCached(g, D, TnId(k, 4), new RectangleF(-0.28f, -0.08f, 0.56f, 0.46f), bg => TogepiShellSd(bg, k, D));
        }

        static void PkArm(Graphics g, float D, int k, int side, PointF h)
        {
            switch (k)
            {
                case KindSnorlaxSd: VLimb(g, k, D, SlBlue, 0.12f, P(side * 0.25f, 0.02f), h, side * 0.04f); break;
                case KindGengarSd: VLimb(g, k, D, GnPurple, 0.085f, P(side * 0.25f, 0.05f), h, side * 0.02f); break;
                case KindMewSd: VLimb(g, k, D, MwPink, 0.036f, P(side * 0.07f, 0.06f), h, side * 0.02f); break;
                case KindPsyduckSd: VLimb(g, k, D, PsYellow, 0.058f, P(side * 0.14f, 0.06f), h, side * 0.07f); break;
                case KindTogepiSd: VLimb(g, k, D, TgCream, 0.05f, P(side * 0.2f, 0.05f), h, 0); break;
                case KindMeowthSd: VLimb(g, k, D, MeCream, 0.046f, P(side * 0.1f, 0.08f), h, side * 0.02f); break;
                case KindPiplupSd: VLimb(g, k, D, PpBlue, 0.062f, P(side * 0.14f, 0.06f), h, side * 0.03f); break;
                case KindLucarioSd:
                    // Blue, from the round black pad on its shoulder.
                    VLimb(g, k, D, LcBlue, 0.05f, P(side * 0.1f, 0.04f), h, side * 0.02f);
                    VOval(g, k, D, LcBlack, LcBlackShade, 0, side * 0.1f, 0.035f, 0.056f, 0.052f);
                    break;
                case KindDragoniteSd: VLimb(g, k, D, DnOrange, 0.074f, P(side * 0.155f, 0.065f), h, side * 0.02f); break;
                case KindMewtwoSd: VLimb(g, k, D, M2Pale, 0.036f, P(side * 0.098f, 0.04f), h, side * 0.03f); break;
                case KindGrootSd: VLimb(g, k, D, GrWood, 0.036f, P(side * 0.075f, 0.07f), h, side * 0.03f); break;
                case KindPikachuSd: VLimb(g, k, D, PkYellow, 0.052f, P(side * 0.1f, 0.07f), h, side * 0.02f); break;
                case KindCharmanderSd: VLimb(g, k, D, CmOrange, 0.056f, P(side * 0.11f, 0.07f), h, side * 0.02f); break;
                case KindSquirtleSd: VLimb(g, k, D, SqBlue, 0.062f, P(side * 0.13f, 0.08f), h, side * 0.02f); break;
                default: VLimb(g, k, D, JgPink, 0.05f, P(side * 0.26f, 0.06f), h, 0); break;
            }
        }

        static void PkHand(Graphics g, float D, int k)
        {
            switch (k)
            {
                case KindPikachuSd: VOval(g, k, D, PkYellow, Color.Empty, 0, 0, 0, 0.056f, 0.05f); break;
                case KindCharmanderSd:
                    // A little paw with three white claws.
                    VOval(g, k, D, CmOrange, Color.Empty, 0, 0, 0, 0.06f, 0.054f);
                    for (int i = -1; i <= 1; i++) VOval(g, k, D, Color.FromArgb(252, 252, 250), Color.Empty, 0, i * 0.018f, -0.024f + Math.Abs(i) * 0.006f, 0.014f, 0.016f);
                    break;
                case KindSquirtleSd: VOval(g, k, D, SqBlue, Color.Empty, 0, 0, 0, 0.06f, 0.054f); break;
                case KindSnorlaxSd:
                case KindPsyduckSd:
                {
                    // A big paw with three pale claws on top.
                    bool sl = k == KindSnorlaxSd;
                    VOval(g, k, D, sl ? SlBlue : PsYellow, Color.Empty, 0, 0, 0, sl ? 0.09f : 0.07f, sl ? 0.08f : 0.064f);
                    for (int i = -1; i <= 1; i++)
                        using (GraphicsPath c = KPoly(D, i * 0.022f - 0.008f, -0.022f, i * 0.022f + 0.008f, -0.022f, i * 0.024f, -0.048f))
                            VFill(g, k, c, sl ? Color.FromArgb(250, 250, 248) : PsBill, Color.Empty, D, 0);
                    break;
                }
                case KindGengarSd:
                    // Three pointed fingers spread out.
                    using (GraphicsPath hp = PkOutline(D, 0.5f, -0.04f, 0.032f, 0, -0.05f, 0.0f, 0, -0.076f, -0.03f, 1, -0.026f, -0.022f, 1, 0, -0.074f, 1, 0.026f, -0.022f, 1,
                                                       0.076f, -0.03f, 1, 0.05f, 0.0f, 0, 0.04f, 0.032f, 0, 0, 0.042f, 0))
                        VFill(g, k, hp, GnPurple, GnPurpleShade, D, 0);
                    break;
                case KindMewSd: VOval(g, k, D, MwPink, Color.Empty, 0, 0, 0, 0.04f, 0.038f); break;
                case KindTogepiSd: VOval(g, k, D, TgCream, Color.Empty, 0, 0, 0, 0.058f, 0.05f); break;
                case KindMeowthSd: VOval(g, k, D, MeCream, Color.Empty, 0, 0, 0, 0.06f, 0.056f); break;
                case KindPiplupSd: VOval(g, k, D, PpBlue, Color.Empty, 0, 0, 0.005f, 0.06f, 0.07f); break;
                case KindLucarioSd:
                    // A black paw with the white spike on its back.
                    using (GraphicsPath sp = KPoly(D, -0.014f, -0.016f, 0.014f, -0.016f, 0, -0.058f)) VFill(g, k, sp, Color.FromArgb(252, 252, 252), Color.FromArgb(190, 196, 214), D, 0);
                    VOval(g, k, D, LcBlack, Color.Empty, 0, 0, 0, 0.058f, 0.054f);
                    break;
                case KindDragoniteSd:
                    // A chubby paw, its three white claws at its tip.
                    for (int i = -1; i <= 1; i++)
                        using (GraphicsPath c = KPoly(D, i * 0.02f - 0.008f, 0.018f, i * 0.02f + 0.008f, 0.018f, i * 0.022f, 0.044f - Math.Abs(i) * 0.005f))
                            VFill(g, k, c, Color.FromArgb(252, 252, 250), Color.FromArgb(206, 210, 222), D, 0);
                    VOval(g, k, D, DnOrange, Color.Empty, 0, 0, 0, 0.07f, 0.064f);
                    break;
                case KindMewtwoSd:
                    // Three round fingertips.
                    VOval(g, k, D, M2Pale, Color.Empty, 0, 0, 0.004f, 0.044f, 0.04f);
                    for (int i = -1; i <= 1; i++) VOval(g, k, D, M2Pale, M2PaleShade, 0, i * 0.024f, -0.026f + Math.Abs(i) * 0.008f, 0.026f, 0.026f);
                    break;
                case KindGrootSd:
                    // A little wooden hand with its stubby fingers.
                    for (int i = -1; i <= 1; i++) VOval(g, k, D, GrWood, GrWoodShade, 0, i * 0.017f, 0.02f, 0.016f, 0.03f);
                    VOval(g, k, D, GrWood, Color.Empty, 0, 0, 0, 0.05f, 0.044f);
                    break;
                default: VOval(g, k, D, JgPink, Color.Empty, 0, 0, 0, 0.054f, 0.05f); break;
            }
        }

        // In front of the face, turning with it: noses and nostrils.
        static void PkDetails(Graphics g, int k, float D, float f)
        {
            switch (k)
            {
                case KindPikachuSd:
                case KindEeveeSd:
                {
                    float y = f + (k == KindPikachuSd ? 0.048f : 0.052f);
                    using (GraphicsPath n = KCurve(D, 0.5f, -0.012f, y, 0.012f, y, 0, y + 0.011f)) FillSolid(g, n, Color.FromArgb(52, 40, 40));
                    break;
                }
                case KindCharmanderSd:
                case KindSquirtleSd:
                case KindBulbasaurSd:
                case KindDragoniteSd:
                {
                    float y = f + (k == KindBulbasaurSd ? 0.075f : k == KindDragoniteSd ? 0.072f : 0.06f), dx = k == KindBulbasaurSd ? 0.032f : k == KindDragoniteSd ? 0.032f : 0.022f;
                    foreach (int s in Sides) Ellipse(g, Color.FromArgb(150, 40, 30, 30), (s * dx - 0.006f) * D, y * D, 0.012f * D, 0.008f * D);
                    break;
                }
                case KindLucarioSd:
                    // The tip of its black snout catching the light.
                    Ellipse(g, Color.FromArgb(150, 255, 255, 255), -0.012f * D, (f + 0.088f) * D, 0.024f * D, 0.011f * D);
                    break;
            }
        }

        static string[] PkLines(int k)
        {
            switch (k)
            {
                case KindPikachuSd: return OwnLinesOf(KindPikachu);
                case KindEeveeSd: return OwnLinesOf(KindEevee);
                case KindCharmanderSd: return OwnLinesOf(KindCharmander);
                case KindBulbasaurSd: return OwnLinesOf(KindBulbasaur);
                case KindSquirtleSd: return OwnLinesOf(KindSquirtle);
                case KindSnorlaxSd: return OwnLinesOf(KindSnorlax);
                case KindGengarSd: return OwnLinesOf(KindGengar);
                case KindMewSd: return OwnLinesOf(KindMew);
                case KindPsyduckSd: return OwnLinesOf(KindPsyduck);
                case KindTogepiSd: return OwnLinesOf(KindTogepi);
                case KindMeowthSd: return OwnLinesOf(KindMeowth);
                case KindPiplupSd: return OwnLinesOf(KindPiplup);
                case KindMimikyuSd: return OwnLinesOf(KindMimikyu);
                case KindLucarioSd: return OwnLinesOf(KindLucario);
                case KindDragoniteSd: return OwnLinesOf(KindDragonite);
                case KindDittoSd: return OwnLinesOf(KindDitto);
                case KindMewtwoSd: return OwnLinesOf(KindMewtwo);
                case KindGrootSd: return OwnLinesOf(KindGroot);
                default: return OwnLinesOf(KindJigglypuff);
            }
        }
    }

    public partial class Mascot
    {
        // Their eyes, as the games' models have them: Pikachu's and Eevee's dark and glossy, Charmander's teal, Bulbasaur's
        // red with the white showing at their outer corners, Squirtle's maroon on white, Jigglypuff's big teal ones.
        bool PkEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            switch (Look.Kind)
            {
                case MascotParts.KindPikachuSd: PkEyeCached(g, x, y, D, blink, surprised, 0.078f, 0.09f, Color.Empty, Color.FromArgb(46, 38, 40), 0.0f, 0); return true;
                case MascotParts.KindEeveeSd: PkEyeCached(g, x, y, D, blink, surprised, 0.1f, 0.116f, Color.Empty, Color.FromArgb(120, 70, 36), 0.0f, 0); return true;
                case MascotParts.KindCharmanderSd: PkEyeCached(g, x, y, D, blink, surprised, 0.094f, 0.122f, Color.FromArgb(84, 186, 216), Color.FromArgb(20, 74, 112), 0.5f, 0); return true;
                case MascotParts.KindBulbasaurSd: PkLidEye(g, x, y, D, blink, surprised, BulbasaurLid); return true;
                case MascotParts.KindSquirtleSd: PkEyeCached(g, x, y, D, blink, surprised, 0.1f, 0.124f, Color.FromArgb(196, 64, 64), Color.FromArgb(100, 22, 34), 0.48f, 0); return true;
                case MascotParts.KindSnorlaxSd:
                    // Always dozing: two closed curves, unless something surprises it.
                    if (surprised) { PkEyeDraw(g, x, y, D, 0.05f, 0.06f, Color.Empty, Color.FromArgb(40, 34, 38), 0, 0, x < 0 ? -1 : 1); return true; }
                    using (Pen p = new Pen(Color.FromArgb(40, 34, 40), Math.Max(1f, D * 0.011f)))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawArc(p, x - 0.04f * D, y - 0.03f * D, 0.08f * D, 0.04f * D, 20, 140);
                    }
                    return true;
                case MascotParts.KindGengarSd: PkLidEye(g, x, y, D, blink, surprised, GengarLid); return true;
                case MascotParts.KindMewSd: PkEyeCached(g, x, y, D, blink, surprised, 0.108f, 0.128f, Color.FromArgb(120, 196, 250), Color.FromArgb(30, 92, 190), 0.4f, 0); return true;
                case MascotParts.KindPsyduckSd:
                case MascotParts.KindMeowthSd:
                {
                    // White and blank: Psyduck's round, a tiny dot in the middle; Meowth's oval, a thin dark slit.
                    bool psy = Look.Kind == MascotParts.KindPsyduckSd;
                    float sk = surprised ? 1.15f : 1f, w = (psy ? 0.118f : 0.09f) * D * sk, h = (psy ? 0.112f : 0.122f) * D * sk * (surprised ? 1 : blink);
                    if (h < 0.018f * D) { MascotParts.Line(g, Color.FromArgb(30, 26, 34), Math.Max(1f, D * 0.011f), x - w * 0.45f, y, x + w * 0.45f, y); return true; }
                    RectangleF r = new RectangleF(x - w / 2, y - h / 2, w, h);
                    MascotParts.Ellipse(g, Color.White, r.X, r.Y, r.Width, r.Height);
                    using (Pen p = new Pen(Color.FromArgb(30, 26, 34), Math.Max(1f, D * 0.009f))) g.DrawEllipse(p, r);
                    if (psy) MascotParts.Ellipse(g, Color.FromArgb(22, 18, 22), x - 0.012f * D, y - 0.012f * D, 0.024f * D, 0.024f * D);
                    else MascotParts.Ellipse(g, Color.FromArgb(22, 18, 22), x - w * 0.1f, y - h * 0.38f, w * 0.2f, h * 0.76f);
                    return true;
                }
                case MascotParts.KindTogepiSd: PkEyeCached(g, x, y, D, blink, surprised, 0.058f, 0.076f, Color.Empty, Color.FromArgb(46, 38, 40), 0, 0); return true;
                case MascotParts.KindPiplupSd: PkEyeCached(g, x, y, D, blink, surprised, 0.062f, 0.086f, Color.Empty, Color.FromArgb(44, 60, 130), 0, 0); return true;
                case MascotParts.KindMimikyuSd: return true;   // its face is drawn on the rag
                case MascotParts.KindLucarioSd: PkLidEye(g, x, y, D, blink, surprised, LucarioLid); return true;
                case MascotParts.KindDragoniteSd: PkEyeCached(g, x, y, D, blink, surprised, 0.076f, 0.092f, Color.Empty, Color.FromArgb(52, 34, 26), 0, 0); return true;
                case MascotParts.KindDittoSd: return DotEye(g, x, y, D, 0.022f * D, 0.03f * D, blink);
                case MascotParts.KindMewtwoSd: PkLidEye(g, x, y, D, blink, surprised, MewtwoLid); return true;
                case MascotParts.KindGrootSd: PkEyeCached(g, x, y, D, blink, surprised, 0.084f, 0.096f, Color.Empty, Color.FromArgb(56, 34, 24), 0, 0); return true;
                default: PkEyeCached(g, x, y, D, blink, surprised, 0.162f, 0.17f, Color.FromArgb(104, 214, 220), Color.FromArgb(24, 112, 132), 0.42f, 0); return true;
            }
        }

        // A lidded eye, W by H (units of D): its top a lid slanting down towards the nose by Slant (of its height), its
        // bottom round. White with an iris (Iris of its height across, IrisX of its width in from the middle, light at the
        // bottom and dark at the top), or all iris when White is empty; a round pupil, or a slit; a shine.
        sealed class PkLid
        {
            public float W, H, Slant, Iris, IrisX, Pupil, Arch = 0.07f;
            public Color White, Light, Dark;
            public bool Slit;
        }

        static readonly PkLid GengarLid = new PkLid { W = 0.152f, H = 0.108f, Slant = 0.52f, Iris = 1, IrisX = -0.2f, Pupil = 0.3f, White = Color.Empty,
                                                       Light = Color.FromArgb(255, 112, 104), Dark = Color.FromArgb(196, 30, 46), Slit = true };
        static readonly PkLid MewtwoLid = new PkLid { W = 0.088f, H = 0.084f, Slant = 0.16f, Iris = 0.95f, IrisX = -0.1f, Pupil = 0.42f, White = Color.FromArgb(252, 250, 255),
                                                       Light = Color.FromArgb(200, 140, 232), Dark = Color.FromArgb(108, 48, 150) };
        static readonly PkLid BulbasaurLid = new PkLid { W = 0.13f, H = 0.104f, Slant = 0.22f, Arch = 0.32f, Iris = 1.0f, IrisX = -0.12f, Pupil = 0.42f, White = Color.FromArgb(252, 252, 250),
                                                          Light = Color.FromArgb(244, 86, 86), Dark = Color.FromArgb(150, 22, 34) };
        static readonly PkLid LucarioLid = new PkLid { W = 0.092f, H = 0.07f, Slant = 0.3f, Iris = 0.95f, IrisX = -0.12f, Pupil = 0.4f, White = Color.FromArgb(250, 250, 250),
                                                        Light = Color.FromArgb(252, 96, 84), Dark = Color.FromArgb(170, 24, 32) };

        // Kept while open; squeezed towards the lid while it blinks.
        void PkLidEye(Graphics g, float x, float y, float D, float blink, bool surprised, PkLid e)
        {
            int side = x < 0 ? -1 : 1;
            float sk = surprised ? 1.12f : 1f;
            if (surprised || blink < 0.999f)
            {
                PkLidDraw(g, x, y, D * sk, e, surprised ? 1 : blink, side);
                return;
            }
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            MascotParts.DsCached(g, D, MascotParts.TnId(Look.Kind, side > 0 ? 6 : 5), new RectangleF(-0.11f, -0.11f, 0.22f, 0.22f), bg => PkLidDraw(bg, 0, 0, D, e, 1, side));
            g.Restore(st);
        }

        static void PkLidDraw(Graphics g, float x, float y, float D, PkLid e, float open, int side)
        {
            Color ink = Color.FromArgb(30, 26, 34);
            float w = e.W * D, h = e.H * D;
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            if (side < 0) g.ScaleTransform(-1, 1);   // drawn as the right eye: its outer corner to the right
            PointF ot = new PointF(w * 0.5f, -h * 0.5f), it = new PointF(-w * 0.5f, -h * 0.5f + e.Slant * h);
            PointF l1 = new PointF(w * 0.15f, ot.Y + (it.Y - ot.Y) * 0.3f - h * (e.Arch + 0.01f)), l2 = new PointF(-w * 0.22f, ot.Y + (it.Y - ot.Y) * 0.7f - h * e.Arch);
            if (open < 0.16f)
            {
                using (Pen p = new Pen(ink, Math.Max(1f, D * 0.012f)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    float dy = h * 0.35f;
                    g.DrawBezier(p, ot.X, ot.Y + dy, l1.X, l1.Y + dy, l2.X, l2.Y + dy, it.X, it.Y + dy);
                }
                g.Restore(st);
                return;
            }
            if (open < 0.999f)
            {
                float ly = (ot.Y + it.Y) / 2;
                g.TranslateTransform(0, ly);
                g.ScaleTransform(1, open);
                g.TranslateTransform(0, -ly);
            }
            using (GraphicsPath eye = new GraphicsPath())
            {
                eye.AddBezier(ot, l1, l2, it);
                eye.AddBezier(it, new PointF(-w * 0.52f, h * 0.48f), new PointF(w * 0.46f, h * 0.66f), ot);
                eye.CloseFigure();
                float ih = h * e.Iris, iw = ih * 0.92f, icx = e.IrisX * w, icy = h * 0.1f;
                RectangleF ir = e.White.IsEmpty ? RectangleF.Inflate(eye.GetBounds(), 1, 1) : new RectangleF(icx - iw / 2, icy - ih / 2, iw, ih);
                MascotParts.FillSolid(g, eye, e.White.IsEmpty ? e.Dark : e.White);
                Region old = g.Clip;
                g.SetClip(eye, CombineMode.Intersect);
                using (GraphicsPath iris = new GraphicsPath())
                {
                    iris.AddEllipse(ir);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(ir.X, ir.Y - 1, ir.Width, ir.Height + 2), e.Dark, e.Light, 90f)) g.FillPath(b, iris);
                }
                // The shadow the lid casts over the top of it.
                using (Pen sh = new Pen(Color.FromArgb(70, 20, 10, 30), h * 0.3f)) g.DrawBezier(sh, ot.X, ot.Y + h * 0.08f, l1.X, l1.Y + h * 0.08f, l2.X, l2.Y + h * 0.08f, it.X, it.Y + h * 0.08f);
                float pw = e.Slit ? w * 0.075f : ih * e.Pupil, ph = e.Slit ? h * 0.5f : ih * e.Pupil;
                MascotParts.Ellipse(g, Color.FromArgb(22, 14, 22), icx - pw / 2, icy - ph / 2 + h * 0.03f, pw, ph);
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, eye, ink, Math.Max(1f, D * 0.009f));
                using (Pen lid = new Pen(ink, Math.Max(1.2f, D * 0.016f))) { lid.StartCap = lid.EndCap = LineCap.Round; g.DrawBezier(lid, ot, l1, l2, it); }
                float hr = Math.Min(w, h);
                MascotParts.Ellipse(g, Color.FromArgb(235, 255, 255, 255), icx + w * 0.1f, icy - h * 0.2f, hr * 0.3f, hr * 0.24f);
                MascotParts.Ellipse(g, Color.FromArgb(170, 255, 255, 255), icx - w * 0.16f, icy + h * 0.14f, hr * 0.13f, hr * 0.11f);
            }
            g.Restore(st);
        }

        // An eye w by h (units of D): with an iris (colours light at its bottom, dark at its top) on white, or all dark and
        // glossy when light is empty; slant tilts its outer corner up. Kept while open; squeezed while it blinks.
        void PkEyeCached(Graphics g, float x, float y, float D, float blink, bool surprised, float w, float h, Color light, Color dark, float pupil, float slant)
        {
            int side = x < 0 ? -1 : 1;
            float sk = surprised ? 1.15f : 1f;
            if (surprised || blink < 0.999f)
            {
                PkEyeDraw(g, x, y, D, w * sk, h * sk * (surprised ? 1 : blink), light, dark, pupil, slant, side);
                return;
            }
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            MascotParts.DsCached(g, D, MascotParts.TnId(Look.Kind, side > 0 ? 6 : 5), new RectangleF(-0.11f, -0.11f, 0.22f, 0.22f),
                                 bg => PkEyeDraw(bg, 0, 0, D, w, h, light, dark, pupil, slant, side));
            g.Restore(st);
        }

        static void PkEyeDraw(Graphics g, float x, float y, float D, float w, float h, Color light, Color dark, float pupil, float slant, int side)
        {
            Color ink = Color.FromArgb(30, 26, 34);
            if (h < 0.018f)
            {
                using (Pen p = new Pen(ink, Math.Max(1f, D * 0.011f))) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, x - w * D * 0.45f, y, x + w * D * 0.45f, y); }
                return;
            }
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(-side * slant);
            RectangleF r = new RectangleF(-w * D / 2, -h * D / 2, w * D, h * D);
            using (GraphicsPath e = new GraphicsPath())
            {
                e.AddEllipse(r);
                if (light.IsEmpty)
                {
                    // All dark and glossy.
                    using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(r, 0, 1), Color.FromArgb(24, 20, 24), dark, 90f)) g.FillPath(b, e);
                }
                else
                {
                    MascotParts.FillSolid(g, e, Color.White);
                    Region old = g.Clip;
                    g.SetClip(e, CombineMode.Intersect);
                    float iw = r.Width * 0.82f, ih = r.Height * 0.86f, ix = -iw / 2 - side * r.Width * 0.06f, iy = -ih / 2 + r.Height * 0.04f;
                    using (GraphicsPath iris = new GraphicsPath())
                    {
                        iris.AddEllipse(ix, iy, iw, ih);
                        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(ix, iy - 1, iw, ih + 2), dark, light, 90f)) g.FillPath(b, iris);
                    }
                    if (pupil > 0) MascotParts.Ellipse(g, Color.FromArgb(22, 18, 22), ix + iw * (0.5f - pupil / 2), iy + ih * (0.5f - pupil / 2) - ih * 0.04f, iw * pupil, ih * pupil);
                    g.Clip = old;
                    old.Dispose();
                }
                MascotParts.Stroke(g, e, ink, Math.Max(1f, D * 0.009f));
            }
            // The shines: a big one high on the inner side, a small one low.
            float hr = Math.Min(r.Width, r.Height);
            MascotParts.Ellipse(g, Color.FromArgb(245, 255, 255, 255), -side * r.Width * 0.08f - hr * 0.2f, -r.Height * 0.38f, hr * 0.42f, hr * 0.38f);
            MascotParts.Ellipse(g, Color.FromArgb(200, 255, 255, 255), side * r.Width * 0.12f, r.Height * 0.12f, hr * 0.17f, hr * 0.15f);
            g.Restore(st);
        }

        // Their mouths: a cat's "w" for Pikachu and Eevee, Charmander's and Squirtle's smiles, Bulbasaur's wide one,
        // Jigglypuff's little one; open, each its own (Pikachu's with the pink tongue, Charmander's two little fangs).
        bool PkMouth(Graphics g, float D, float mx, float my, bool open, bool talking)
        {
            int k = Look.Kind;
            Color ink = Color.FromArgb(40, 30, 32);
            float lw = Math.Max(1f, D * 0.011f);
            if (k == MascotParts.KindPsyduckSd || k == MascotParts.KindPiplupSd) return true;   // the bill or beak is the mouth
            if (k == MascotParts.KindMimikyuSd) return true;   // drawn on the rag
            if (k == MascotParts.KindMewtwoSd && !open)
            {
                using (Pen p = new Pen(ink, lw)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, mx - 0.022f * D, my + 0.004f * D, mx + 0.022f * D, my + 0.004f * D); }
                return true;
            }
            if (k == MascotParts.KindGengarSd) { GengarGrin(g, D, mx, my, open, talking, ink, lw); return true; }
            if (k == MascotParts.KindMeowthSd)
            {
                float s = 0.024f * D;
                if (open)
                {
                    float ow = 0.11f * D, oh = 0.07f * D;
                    if (talking && mood == Mood.Idle) oh *= (float)(0.5 + 0.5 * Math.Abs(Math.Sin(now / 110.0)));
                    OpenMouth(g, mx, my, ow, oh, D);
                    MascotParts.Fang(g, mx - ow * 0.25f, my, D * 0.9f);
                    MascotParts.Fang(g, mx + ow * 0.25f, my, D * 0.9f);
                    return true;
                }
                using (Pen p = new Pen(ink, lw))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawArc(p, mx - s * 2, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                    g.DrawArc(p, mx, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                }
                MascotParts.Fang(g, mx + s * 1.05f, my + s * 0.66f, D * 0.9f);
                return true;
            }
            if (k == MascotParts.KindSnorlaxSd && !open)
            {
                // A sleepy smile with its two little fangs pointing up from the lower lip.
                using (Pen p = new Pen(ink, lw)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawArc(p, mx - 0.07f * D, my - 0.035f * D, 0.14f * D, 0.05f * D, 20, 140); }
                foreach (int s in new int[] { -1, 1 })
                {
                    float fx = mx + s * 0.045f * D, fy = my + 0.012f * D;
                    using (GraphicsPath f = new GraphicsPath())
                    {
                        f.AddPolygon(new PointF[] { new PointF(fx - 0.011f * D, fy + 0.002f * D), new PointF(fx + 0.011f * D, fy + 0.002f * D), new PointF(fx, fy - 0.02f * D) });
                        MascotParts.FillSolid(g, f, Color.White);
                        MascotParts.Stroke(g, f, Color.FromArgb(150, 60, 40, 50), Math.Max(0.8f, D * 0.005f));
                    }
                }
                return true;
            }
            float w = (k == MascotParts.KindBulbasaurSd || k == MascotParts.KindDittoSd ? 0.17f : k == MascotParts.KindJigglySd || k == MascotParts.KindMewSd || k == MascotParts.KindLucarioSd || k == MascotParts.KindMewtwoSd ? 0.06f : k == MascotParts.KindSnorlaxSd || k == MascotParts.KindDragoniteSd ? 0.13f : 0.09f) * D;
            if (!open)
            {
                using (Pen p = new Pen(ink, lw))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    if (k == MascotParts.KindPikachuSd || k == MascotParts.KindEeveeSd)
                    {
                        float s = (k == MascotParts.KindPikachuSd ? 0.024f : 0.02f) * D;
                        g.DrawArc(p, mx - s * 2, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                        g.DrawArc(p, mx, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                    }
                    else g.DrawArc(p, mx - w / 2, my - w * 0.32f, w, w * 0.5f, 25, 130);
                }
                return true;
            }
            float h = (k == MascotParts.KindBulbasaurSd ? 0.075f : k == MascotParts.KindJigglySd ? 0.045f : 0.065f) * D;
            if (talking && mood == Mood.Idle) h *= (float)(0.5 + 0.5 * Math.Abs(Math.Sin(now / 110.0)));
            using (GraphicsPath m = new GraphicsPath())
            {
                if (k == MascotParts.KindPikachuSd || k == MascotParts.KindEeveeSd)
                {
                    // A rounded triangle under the "w", its top in two little curves.
                    float hw = w * 0.42f;
                    m.AddBezier(mx - hw, my - h * 0.1f, mx - hw * 0.5f, my + h * 0.08f, mx - hw * 0.15f, my + h * 0.04f, mx, my - h * 0.08f);
                    m.AddBezier(mx, my - h * 0.08f, mx + hw * 0.15f, my + h * 0.04f, mx + hw * 0.5f, my + h * 0.08f, mx + hw, my - h * 0.1f);
                    m.AddBezier(mx + hw, my - h * 0.1f, mx + hw * 0.9f, my + h * 0.7f, mx + hw * 0.4f, my + h, mx, my + h);
                    m.AddBezier(mx, my + h, mx - hw * 0.4f, my + h, mx - hw * 0.9f, my + h * 0.7f, mx - hw, my - h * 0.1f);
                }
                else m.AddArc(mx - w / 2, my - h * 0.6f, w, h * 1.6f, 0, 180);
                m.CloseFigure();
                MascotParts.FillSolid(g, m, Color.FromArgb(150, 46, 54));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                MascotParts.Ellipse(g, Color.FromArgb(246, 128, 140), mx - w * 0.3f, my + h * 0.35f, w * 0.6f, h * 0.8f);
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, m, ink, lw);
            }
            if (k == MascotParts.KindCharmanderSd)
                foreach (int s in new int[] { -1, 1 }) MascotParts.Fang(g, mx + s * w * 0.3f, my + 0.002f * D, D * 0.8f);
            return true;
        }

        // Gengar's wide grin from cheek to cheek, full of teeth; open, a dark mouth under the top row and its tongue.
        void GengarGrin(Graphics g, float D, float mx, float my, bool open, bool talking, Color ink, float lw)
        {
            // From one curled-up corner to the other, its top dipping a little in the middle, its bottom a deep curve.
            float w = 0.37f * D, h = (open ? 0.14f : 0.088f) * D, up = 0.052f * D;
            if (open && talking && mood == Mood.Idle) h *= (float)(0.7 + 0.3 * Math.Abs(Math.Sin(now / 110.0)));
            PointF l = new PointF(mx - w / 2, my - up), r = new PointF(mx + w / 2, my - up);
            using (GraphicsPath m = new GraphicsPath())
            {
                m.AddBezier(l, new PointF(mx - w * 0.3f, my + 0.004f * D), new PointF(mx + w * 0.3f, my + 0.004f * D), r);
                m.AddBezier(r, new PointF(mx + w * 0.4f, my + h * 1.05f), new PointF(mx - w * 0.4f, my + h * 1.05f), l);
                m.CloseFigure();
                MascotParts.FillSolid(g, m, open ? Color.FromArgb(96, 26, 48) : Color.FromArgb(250, 250, 252));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                if (open)
                {
                    // The top teeth, the bottom ones, the tongue between.
                    MascotParts.Ellipse(g, Color.FromArgb(240, 112, 150), mx - w * 0.22f, my + h * 0.3f, w * 0.44f, h * 0.7f);
                    using (Pen t = new Pen(Color.FromArgb(250, 250, 252), 0.06f * D)) g.DrawBezier(t, l.X, l.Y + 0.012f * D, mx - w * 0.3f, my + 0.03f * D, mx + w * 0.3f, my + 0.03f * D, r.X, r.Y + 0.012f * D);
                }
                else
                {
                    // Lit from above: grey towards the bottom of the teeth.
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(mx - w / 2, my - up - 1, w, h + up + 2), Color.FromArgb(0, 180, 176, 200), Color.FromArgb(120, 150, 144, 178), 90f))
                        g.FillPath(b, m);
                }
                using (Pen tl = new Pen(Color.FromArgb(170, 96, 86, 120), Math.Max(0.7f, D * 0.0045f)))
                    for (int i = 1; i < 8; i++)
                    {
                        float t = i / 8f, tx = mx - w / 2 + w * t, lean = (t - 0.5f) * 0.03f * D;
                        g.DrawLine(tl, tx, my - up, tx + lean, open ? my + 0.04f * D : my + h);
                    }
                g.Clip = old;
                old.Dispose();
                MascotParts.Stroke(g, m, ink, lw);
            }
            // The curl at each corner.
            using (Pen c = new Pen(ink, lw)) { c.StartCap = c.EndCap = LineCap.Round; foreach (PointF q in new[] { l, r }) g.DrawArc(c, q.X - 0.016f * D, q.Y - 0.022f * D, 0.032f * D, 0.032f * D, q.X < mx ? 120 : -30, 90); }
        }
    }
}

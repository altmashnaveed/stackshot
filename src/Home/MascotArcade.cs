// Stackshot - Classic game characters, each drawn true to its original: Pac-Man (chomping, with the ghosts Pinky,
// Inky and Clyde as colours of Blinky), a Space Invaders crab in pixels, Mario's Goomba, Boo, Toad, the Super Mushroom,
// the ? block, Bob-omb and Chain Chomp, Angry Birds' Red and a green pig, Hollow Knight's Knight, Portal's companion
// cube, Bomberman, Dig Dug's Pooka, a Fall Guy, a Minecraft pig, a Metroid and Kirby's Waddle Dee. Fan tributes, not
// affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindInvader = 58, KindGoomba = 59, KindBoo = 60, KindToad = 61, KindMushroom = 62, KindBobomb = 63,
                         KindChomp = 64, KindRedBird = 65, KindGreenPig = 66, KindKnight = 67, KindCube = 68, KindBomberman = 69,
                         KindPooka = 70, KindFallGuy = 71, KindMcPig = 72, KindQBlock = 73, KindMetroid = 74, KindWaddleDee = 75;

        public static bool IsArcade(int kind) { return kind == KindPacman || (kind >= KindInvader && kind <= KindWaddleDee); }

        static bool ArcadeNoHands(int kind)
        {
            return kind == KindPacman || kind == KindInvader || kind == KindGoomba || kind == KindMushroom || kind == KindBobomb ||
                   kind == KindChomp || kind == KindRedBird || kind == KindGreenPig || kind == KindKnight || kind == KindCube ||
                   kind == KindPooka || kind == KindMcPig || kind == KindQBlock || kind == KindMetroid;
        }

        // Characters with a front that turn whole to face the way they walk.
        public static bool TurnsToWalk(int kind) { return kind == KindPacman || kind == KindBoo || kind == KindChomp || kind == KindCrewmate; }

        // Eyes that are part of the character and stay whatever its mood (no happy arcs on an angry Goomba).
        public static bool KeepsOwnEyes(int kind)
        {
            return kind == KindGoomba || kind == KindBobomb || kind == KindChomp || kind == KindRedBird || kind == KindGreenPig ||
                   kind == KindPooka || kind == KindMushroom || kind == KindWillow;
        }

        static bool ArcadeNoCheeks(int kind) { return kind != KindToad && kind != KindWaddleDee && kind != KindFallGuy; }

        // Faces drawn whole on the body (pixels, a glyph, a cube's heart) or by the face code itself.
        public static bool ArcadeFixedFace(int kind)
        {
            return kind == KindPacman || kind == KindInvader || kind == KindCube || kind == KindMcPig || kind == KindQBlock ||
                   kind == KindMetroid || kind == KindKnight;
        }

        static Geo ArcadeGeo(int kind)
        {
            switch (kind)
            {
                case KindPacman: return G(0.66f, -0.32f, -0.15f, 0.1f, 0.2f, 0.6f, 0.32f, 0.34f, 0.2f, 0.56f);
                case KindInvader: return G(0.66f, -0.24f, 0, 0.1f, 0.2f, 0.6f, 0.24f, 0.33f, 0.2f, 0.5f);
                case KindGoomba: return G(0.82f, -0.3f, -0.02f, 0.085f, 0.2f, 0.6f, 0.31f, 0.36f, 0.2f, 0.62f);
                case KindBoo: return G(0.68f, -0.31f, -0.06f, 0.11f, 0.12f, 0.6f, 0.31f, 0.36f, 0.12f, 0.56f);
                case KindToad: return G(0.86f, -0.33f, 0.12f, 0.085f, 0.26f, 0.46f, 0.31f, 0.3f, 0.25f, 0.62f);
                case KindMushroom: return G(0.86f, -0.3f, 0.18f, 0.07f, 0.26f, 0.44f, 0.31f, 0.3f, 0.22f, 0.62f);
                case KindBobomb: return G(0.68f, -0.28f, -0.04f, 0.085f, 0.12f, 0.6f, 0.31f, 0.34f, 0.2f, 0.5f);
                case KindChomp: return G(0.72f, -0.32f, -0.13f, 0.13f, 0.1f, 0.6f, 0.32f, 0.36f, 0.2f, 0.56f);
                case KindRedBird: return G(0.72f, -0.29f, -0.07f, 0.065f, 0.12f, 0.6f, 0.31f, 0.36f, 0.2f, 0.54f);
                case KindGreenPig: return G(0.76f, -0.28f, -0.1f, 0.17f, 0.12f, 0.6f, 0.31f, 0.38f, 0.2f, 0.56f);
                case KindKnight: return G(0.6f, -0.33f, -0.12f, 0.09f, 0.06f, 0.5f, 0.32f, 0.3f, 0.2f, 0.5f);
                case KindCube:
                case KindMcPig:
                case KindQBlock: return G(0.62f, -0.31f, -0.05f, 0.1f, 0.2f, 0.6f, 0.31f, 0.33f, 0.2f, 0.62f);
                case KindBomberman: return G(0.7f, -0.3f, -0.04f, 0.07f, 0.14f, 0.6f, 0.31f, 0.36f, 0.2f, 0.54f);
                case KindPooka: return G(0.72f, -0.3f, -0.06f, 0.11f, 0.12f, 0.6f, 0.31f, 0.36f, 0.2f, 0.56f);
                case KindFallGuy: return G(0.56f, -0.33f, -0.15f, 0.08f, 0.06f, 0.5f, 0.32f, 0.31f, 0.14f, 0.48f);
                case KindMetroid: return G(0.74f, -0.3f, -0.08f, 0.1f, 0.1f, 0.6f, 0.17f, 0.36f, 0.1f, 0.56f);
                default: return G(0.74f, -0.3f, -0.04f, 0.1f, 0.14f, 0.62f, 0.31f, 0.37f, 0.14f, 0.56f); // Waddle Dee
            }
        }

        // The crab invader, in its two frames (11 x 8 pixels).
        static readonly string[][] InvaderFrames =
        {
            new string[] { "..X.....X..", "...X...X...", "..XXXXXXX..", ".XX.XXX.XX.", "XXXXXXXXXXX", "X.XXXXXXX.X", "X.X.....X.X", "...XX.XX..." },
            new string[] { "..X.....X..", "X..X...X..X", "X.XXXXXXX.X", "XXX.XXX.XXX", "XXXXXXXXXXX", ".XXXXXXXXX.", "..X.....X..", ".X.......X." }
        };

        // A mushroom-like outline: a wide cap (from capTop down to capY at its rim, half width capW) over a narrower
        // rounded stem (half width stemW) down to bottom.
        static GraphicsPath CapPath(float D, float capTop, float capY, float capW, float stemW, float bottom)
        {
            GraphicsPath p = new GraphicsPath();
            float rim = capY + 0.04f;
            p.AddBezier(P(-capW * D, capY * D), P(-(capW + 0.02f) * D, (capTop + 0.12f) * D), P(-capW * 0.55f * D, capTop * D), P(0, capTop * D));
            p.AddBezier(P(0, capTop * D), P(capW * 0.55f * D, capTop * D), P((capW + 0.02f) * D, (capTop + 0.12f) * D), P(capW * D, capY * D));
            p.AddBezier(P(capW * D, capY * D), P((capW - 0.02f) * D, rim * D), P((stemW + 0.06f) * D, rim * D), P(stemW * D, rim * D));
            p.AddLine(P(stemW * D, rim * D), P(stemW * D, (bottom - 0.07f) * D));
            p.AddBezier(P(stemW * D, (bottom - 0.07f) * D), P(stemW * D, bottom * D), P(stemW * 0.6f * D, bottom * D), P(0, bottom * D));
            p.AddBezier(P(0, bottom * D), P(-stemW * 0.6f * D, bottom * D), P(-stemW * D, bottom * D), P(-stemW * D, (bottom - 0.07f) * D));
            p.AddLine(P(-stemW * D, (bottom - 0.07f) * D), P(-stemW * D, rim * D));
            p.AddBezier(P(-stemW * D, rim * D), P(-(stemW + 0.06f) * D, rim * D), P(-(capW - 0.02f) * D, rim * D), P(-capW * D, capY * D));
            p.CloseFigure();
            return p;
        }

        static GraphicsPath ArcadePath(int kind, Geo geo, float D, double now)
        {
            float W = geo.HeadW * D, top = geo.Top * D, bottom = geo.Bottom * D;
            switch (kind)
            {
                case KindPacman:
                {
                    // A circle with the wedge of its mouth opening and closing.
                    float open = 6 + 34 * (float)Math.Abs(Math.Sin(now / 130.0));
                    GraphicsPath p = new GraphicsPath();
                    p.AddPie(-0.32f * D, -0.32f * D, 0.64f * D, 0.64f * D, open, 360 - open * 2);
                    return p;
                }
                case KindInvader:
                {
                    string[] f = InvaderFrames[((long)(now / 500.0)) % 2 == 0 ? 0 : 1];
                    float s = 0.06f * D, x0 = -0.33f * D, y0 = -0.24f * D;
                    GraphicsPath p = new GraphicsPath();
                    for (int r = 0; r < f.Length; r++)
                        for (int c = 0; c < f[r].Length; c++)
                            if (f[r][c] == 'X') p.AddRectangle(new RectangleF(x0 + c * s, y0 + r * s, s * 1.02f, s * 1.02f));
                    return p;
                }
                case KindGoomba:
                {
                    // The big brown head, wide at the bottom, over a short tan body.
                    GraphicsPath p = new GraphicsPath();
                    p.AddBezier(P(-0.41f * D, 0.12f * D), P(-0.43f * D, -0.1f * D), P(-0.22f * D, -0.3f * D), P(0, -0.3f * D));
                    p.AddBezier(P(0, -0.3f * D), P(0.22f * D, -0.3f * D), P(0.43f * D, -0.1f * D), P(0.41f * D, 0.12f * D));
                    p.AddBezier(P(0.41f * D, 0.12f * D), P(0.4f * D, 0.17f * D), P(0.26f * D, 0.17f * D), P(0.19f * D, 0.17f * D));
                    p.AddLine(P(0.19f * D, 0.17f * D), P(0.19f * D, 0.24f * D));
                    p.AddBezier(P(0.19f * D, 0.24f * D), P(0.19f * D, 0.3f * D), P(0.12f * D, 0.3f * D), P(0, 0.3f * D));
                    p.AddBezier(P(0, 0.3f * D), P(-0.12f * D, 0.3f * D), P(-0.19f * D, 0.3f * D), P(-0.19f * D, 0.24f * D));
                    p.AddLine(P(-0.19f * D, 0.24f * D), P(-0.19f * D, 0.17f * D));
                    p.AddBezier(P(-0.19f * D, 0.17f * D), P(-0.26f * D, 0.17f * D), P(-0.4f * D, 0.17f * D), P(-0.41f * D, 0.12f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindBoo:
                {
                    // Round, with a little pointed tail low on one side.
                    PointF[] pts = new PointF[16];
                    for (int i = 0; i < 16; i++)
                    {
                        double a = i * Math.PI * 2 / 16;
                        float r = (i == 6 ? 0.44f : 0.32f) * D;
                        pts[i] = P((float)Math.Cos(a) * r - (i == 6 ? 0.06f * D : 0), (float)Math.Sin(a) * r);
                    }
                    GraphicsPath p = new GraphicsPath();
                    p.AddClosedCurve(pts, 0.5f);
                    return p;
                }
                case KindToad: return CapPath(D, -0.33f, 0.03f, 0.43f, 0.25f, 0.31f);
                case KindMushroom: return CapPath(D, -0.3f, 0.07f, 0.43f, 0.22f, 0.31f);
                case KindBobomb:
                case KindChomp:
                case KindRedBird:
                case KindPooka:
                    return Squircle(new RectangleF(-W / 2, top, W, bottom - top), 2.0);
                case KindGreenPig:
                case KindBomberman:
                case KindWaddleDee:
                    return Squircle(new RectangleF(-W / 2, top, W, bottom - top), 2.1);
                case KindKnight:
                {
                    // Only the cloak; the mask is drawn on top with the face.
                    GraphicsPath p = new GraphicsPath();
                    p.AddBezier(P(-0.17f * D, -0.02f * D), P(-0.3f * D, 0.04f * D), P(-0.34f * D, 0.18f * D), P(-0.34f * D, 0.3f * D));
                    p.AddLine(P(-0.34f * D, 0.3f * D), P(-0.2f * D, 0.27f * D));
                    p.AddLine(P(-0.2f * D, 0.27f * D), P(-0.08f * D, 0.32f * D));
                    p.AddLine(P(-0.08f * D, 0.32f * D), P(0.06f * D, 0.27f * D));
                    p.AddLine(P(0.06f * D, 0.27f * D), P(0.2f * D, 0.32f * D));
                    p.AddLine(P(0.2f * D, 0.32f * D), P(0.34f * D, 0.29f * D));
                    p.AddBezier(P(0.34f * D, 0.29f * D), P(0.34f * D, 0.18f * D), P(0.3f * D, 0.04f * D), P(0.17f * D, -0.02f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindCube:
                case KindMcPig:
                case KindQBlock:
                    return Theme.Round(new RectangleF(-W / 2, top, W, bottom - top), (kind == KindCube ? 0.07f : 0.03f) * D);
                case KindFallGuy:
                    return Theme.Round(new RectangleF(-0.28f * D, top, 0.56f * D, bottom - top), 0.27f * D);
                case KindMetroid:
                {
                    // The jelly dome.
                    GraphicsPath p = new GraphicsPath();
                    p.AddArc(-0.37f * D, -0.3f * D, 0.74f * D, 0.84f * D, 180, 180);
                    p.AddBezier(P(0.37f * D, 0.12f * D), P(0.3f * D, 0.2f * D), P(-0.3f * D, 0.2f * D), P(-0.37f * D, 0.12f * D));
                    p.CloseFigure();
                    return p;
                }
                default:
                    return null;
            }
        }

        // ---- Behind the body.
        static void ArcadeBack(Graphics g, MascotLook l, float D, Color c1, Color c2, MascotPose p)
        {
            Color e1 = Lighter(c1, 0.05);
            switch (l.Kind)
            {
                case KindGoomba:
                    // Big dark shoes, each with a clear outline, stepping in turn while it walks.
                    foreach (int s in Sides)
                    {
                        float step = Math.Abs(p.Facing) > 0.2 ? (float)Math.Max(0, Math.Sin(p.Now / 95.0 + (s < 0 ? 0 : Math.PI))) * 0.03f * D : 0;
                        RectangleF f = new RectangleF(s * 0.14f * D - 0.12f * D, 0.24f * D - step, 0.24f * D, 0.12f * D);
                        using (GraphicsPath fp = new GraphicsPath())
                        {
                            fp.AddEllipse(f);
                            Fill(g, fp, f, Color.FromArgb(120, 70, 38), Color.FromArgb(62, 32, 14), 90f);
                            Stroke(g, fp, Color.FromArgb(230, 44, 22, 10), D * 0.014f);
                        }
                        Ellipse(g, Color.FromArgb(70, 255, 255, 255), f.X + f.Width * 0.2f, f.Y + f.Height * 0.15f, f.Width * 0.3f, f.Height * 0.25f);
                    }
                    break;
                case KindToad:
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(150, 90, 46), Color.FromArgb(96, 54, 24), p);
                    break;
                case KindBobomb:
                {
                    // The wind-up key on its side, turning, and the yellow feet.
                    GraphicsState st = TailSide(g, p);
                    float turn = (float)Math.Cos(p.Now / 260.0);
                    Line(g, Color.FromArgb(200, 150, 30), D * 0.03f, 0.3f * D, -0.02f * D, 0.42f * D, -0.02f * D);
                    foreach (int s in Sides)
                    {
                        float w = 0.07f * D, h = 0.1f * D * Math.Max(0.15f, Math.Abs(turn));
                        RectangleF k = new RectangleF(0.42f * D, -0.02f * D + (s < 0 ? -h : 0), w, h);
                        using (GraphicsPath kp = new GraphicsPath())
                        {
                            kp.AddEllipse(k);
                            Fill(g, kp, k, Gold1, Gold2, 90f);
                            Stroke(g, kp, Color.FromArgb(150, 120, 80, 10), D * 0.008f);
                        }
                    }
                    g.Restore(st);
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(255, 206, 50), Color.FromArgb(226, 150, 20), p);
                    break;
                }
                case KindChomp:
                {
                    // The chain trailing down behind it (on its back side; the drawing turns as it walks): steel links,
                    // each with a dark edge and a glint, swaying a little.
                    float sway = (float)Math.Sin(p.Now / 400.0) * 0.012f;
                    using (Pen edge = new Pen(Color.FromArgb(230, 40, 42, 50), D * 0.034f))
                    using (Pen steel = new Pen(Color.FromArgb(176, 180, 192), D * 0.02f))
                    using (Pen glint = new Pen(Color.FromArgb(200, 240, 244, 252), D * 0.007f))
                        for (int i = 0; i < 4; i++)
                        {
                            float x = -(0.27f + i * 0.08f) * D, y = (0.24f + i * 0.045f + sway * i) * D;
                            RectangleF r = i % 2 == 0 ? new RectangleF(x - 0.05f * D, y - 0.03f * D, 0.1f * D, 0.06f * D) : new RectangleF(x - 0.025f * D, y - 0.04f * D, 0.05f * D, 0.08f * D);
                            g.DrawEllipse(edge, r);
                            g.DrawEllipse(steel, r);
                            g.DrawArc(glint, r, 200, 70);
                        }
                    break;
                }
                case KindRedBird:
                {
                    // Two red feathers on top and the black tail feathers behind.
                    SoftEar(g, D, -0.02f * D, -0.27f * D, -18, 0.12f, 0.7f, e1, c2, ExNone);
                    SoftEar(g, D, 0.03f * D, -0.27f * D, 12, 0.14f, 0.7f, e1, c2, ExNone);
                    GraphicsState st = TailSide(g, p);
                    for (int i = 0; i < 3; i++) SoftEar(g, D, 0.3f * D, 0.04f * D, 66 + i * 22, 0.13f, 0.6f, ExBlack, ExBlack2, ExNone);
                    g.Restore(st);
                    break;
                }
                case KindGreenPig:
                    foreach (int s in Sides)
                        SoftEar(g, D, s * 0.2f * D, -0.24f * D, s * (28 + (s < 0 ? (float)p.EarL : (float)p.EarR) * 0.4f), 0.09f, 1.0f, e1, c2, Color.FromArgb(150, Darker(c2, 0.2)));
                    break;
                case KindKnight:
                {
                    // The two white horns curving up from the mask.
                    Color h1 = Color.FromArgb(252, 252, 252), h2 = Color.FromArgb(206, 208, 218);
                    foreach (int s in Sides)
                        using (GraphicsPath hp = new GraphicsPath())
                        {
                            hp.AddBezier(P(s * 0.08f * D, -0.27f * D), P(s * 0.1f * D, -0.38f * D), P(s * 0.16f * D, -0.46f * D), P(s * 0.25f * D, -0.52f * D));
                            hp.AddBezier(P(s * 0.25f * D, -0.52f * D), P(s * 0.22f * D, -0.44f * D), P(s * 0.2f * D, -0.36f * D), P(s * 0.17f * D, -0.25f * D));
                            hp.CloseFigure();
                            Fill(g, hp, new RectangleF(-0.26f * D, -0.53f * D, 0.52f * D, 0.3f * D), h1, h2, 90f);
                            Stroke(g, hp, Color.FromArgb(120, 90, 94, 110), D * 0.007f);
                        }
                    break;
                }
                case KindBomberman:
                    // The antenna with its pink ball.
                    Line(g, Color.FromArgb(150, 150, 160), D * 0.018f, 0, -0.26f * D, 0, -0.4f * D);
                    {
                        RectangleF b = new RectangleF(-0.05f * D, -0.47f * D, 0.1f * D, 0.1f * D);
                        using (GraphicsPath bp = new GraphicsPath())
                        {
                            bp.AddEllipse(b);
                            Fill(g, bp, b, Color.FromArgb(255, 130, 190), Color.FromArgb(220, 50, 130), 90f);
                        }
                        Ellipse(g, Color.FromArgb(200, 255, 255, 255), b.X + 0.02f * D, b.Y + 0.018f * D, 0.03f * D, 0.025f * D);
                    }
                    foreach (int s in Sides) Foot(g, D, s, Color.FromArgb(255, 120, 180), Color.FromArgb(220, 50, 130), p);
                    break;
                case KindPooka:
                    foreach (int s in Sides) Foot(g, D, s, ExWhite, ExWhite2, p);
                    break;
                case KindFallGuy:
                case KindWaddleDee:
                    foreach (int s in Sides)
                        Foot(g, D, s, l.Kind == KindWaddleDee ? Color.FromArgb(255, 206, 70) : e1, l.Kind == KindWaddleDee ? Color.FromArgb(226, 150, 30) : c2, p);
                    break;
                case KindMetroid:
                {
                    // The dark underside and its four fangs.
                    Ellipse(g, Color.FromArgb(120, 36, 60), -0.3f * D, 0.08f * D, 0.6f * D, 0.16f * D);
                    Color f1 = Color.FromArgb(244, 232, 200), f2 = Color.FromArgb(196, 176, 140);
                    float[][] fangs = { new float[] { -0.17f, 0.34f, 1 }, new float[] { -0.06f, 0.28f, 0.6f }, new float[] { 0.06f, 0.28f, 0.6f }, new float[] { 0.17f, 0.34f, 1 } };
                    foreach (float[] f in fangs)
                    {
                        float x = f[0] * D, s = Math.Sign(f[0]), len = (f[1] - 0.14f) * D, w = 0.045f * D * f[2];
                        using (GraphicsPath fp = new GraphicsPath())
                        {
                            fp.AddBezier(P(x - w, 0.14f * D), P(x - w, 0.14f * D + len * 0.6f), P(x - s * 0.02f * D, 0.14f * D + len * 0.9f), P(x - s * 0.04f * D, 0.14f * D + len));
                            fp.AddBezier(P(x - s * 0.04f * D, 0.14f * D + len), P(x + w * 0.2f, 0.14f * D + len * 0.7f), P(x + w, 0.14f * D + len * 0.4f), P(x + w, 0.14f * D));
                            fp.CloseFigure();
                            Fill(g, fp, new RectangleF(x - w, 0.14f * D, w * 2, len), f1, f2, 90f);
                            Stroke(g, fp, Color.FromArgb(110, 120, 100, 70), D * 0.006f);
                        }
                    }
                    break;
                }
            }
        }

        // ---- On the body (clipped to it, before the face).
        static void ArcadeBody(Graphics g, MascotLook l, float D, GraphicsPath body, Color c1, Color c2, MascotPose p)
        {
            Geo geo = GeoFor(l.Kind);
            float fy = geo.FaceY * D, gap = geo.EyeGap * D, W = geo.HeadW * D, top = geo.Top * D;
            Region old = g.Clip;
            g.SetClip(body, CombineMode.Intersect);
            switch (l.Kind)
            {
                case KindGoomba:
                {
                    // The tan body under the head, shaded where the head overhangs it; a gloss on top of the head.
                    RectangleF bd = new RectangleF(-0.3f * D, 0.16f * D, 0.6f * D, 0.16f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddRectangle(bd);
                        Fill(g, bp, bd, Color.FromArgb(212, 172, 120), Color.FromArgb(250, 222, 176), 90f);
                    }
                    Glow(g, 0, 0.17f * D, 0.42f * D, 0.06f * D, Color.FromArgb(90, 50, 20), 140);
                    Ellipse(g, Color.FromArgb(60, 255, 240, 220), -0.26f * D, -0.27f * D, 0.24f * D, 0.1f * D);
                    break;
                }
                case KindToad:
                case KindMushroom:
                {
                    // The cap (white with red spots on Toad, red with white spots on the mushroom) over the face or stem.
                    bool toad = l.Kind == KindToad;
                    float rim = (toad ? 0.07f : 0.11f) * D;
                    Color cap = toad ? Color.FromArgb(252, 252, 252) : c1, cap2 = toad ? Color.FromArgb(222, 222, 230) : c2;
                    Color spot = toad ? Color.FromArgb(232, 44, 44) : Color.FromArgb(252, 252, 252);
                    if (!toad)
                    {
                        // The stem: shaded under the cap, lighter lower down, with a soft gleam on its left.
                        RectangleF st = new RectangleF(-0.5f * D, rim - 0.01f * D, D, 0.32f * D);
                        using (GraphicsPath sp = new GraphicsPath())
                        {
                            sp.AddRectangle(st);
                            Fill(g, sp, st, Color.FromArgb(222, 200, 160), Color.FromArgb(255, 246, 226), 90f);
                        }
                        Ellipse(g, Color.FromArgb(120, 255, 255, 255), -0.17f * D, rim + 0.05f * D, 0.06f * D, 0.12f * D);
                    }
                    using (GraphicsPath cp = new GraphicsPath())
                    {
                        cp.AddRectangle(new RectangleF(-0.5f * D, -0.5f * D, D, 0.5f * D + rim));
                        if (toad) Fill(g, cp, new RectangleF(-0.5f * D, -0.34f * D, D, 0.34f * D + rim), cap, cap2, 90f);
                    }
                    // The spots, each with a faint edge so they stand out from the cap.
                    float[][] spots = { new float[] { -0.11f, -0.29f, 0.22f, 0.19f }, new float[] { -0.48f, -0.14f, 0.17f, 0.17f }, new float[] { 0.31f, -0.14f, 0.17f, 0.17f } };
                    foreach (float[] s in spots)
                    {
                        RectangleF sr = new RectangleF(s[0] * D, s[1] * D, s[2] * D, s[3] * D);
                        using (GraphicsPath sp = new GraphicsPath())
                        {
                            sp.AddEllipse(sr);
                            Fill(g, sp, sr, spot, toad ? Color.FromArgb(196, 24, 30) : Color.FromArgb(226, 226, 232), 90f);
                            Stroke(g, sp, toad ? Color.FromArgb(120, 140, 20, 20) : Color.FromArgb(110, Darker(c2, 0.3)), D * 0.007f);
                        }
                    }
                    // A gloss on top of the cap and the shadow it casts under its rim.
                    Ellipse(g, Color.FromArgb(70, 255, 255, 255), -0.3f * D, -0.27f * D, 0.22f * D, 0.1f * D);
                    Glow(g, 0, rim + 0.02f * D, 0.56f * D, 0.08f * D, Color.FromArgb(110, 70, 40), 130);
                    break;
                }
                case KindRedBird:
                {
                    RectangleF belly = new RectangleF(-0.24f * D, 0.05f * D, 0.48f * D, 0.32f * D);
                    using (GraphicsPath bp = new GraphicsPath())
                    {
                        bp.AddEllipse(belly);
                        Fill(g, bp, belly, Color.FromArgb(250, 234, 206), Color.FromArgb(226, 200, 166), 90f);
                    }
                    break;
                }
                case KindCube:
                {
                    // Pink strips between the corner plates, the plates themselves, and the round centre with its heart.
                    Color pink = Color.FromArgb(244, 120, 176);
                    using (SolidBrush b = new SolidBrush(pink))
                    {
                        g.FillRectangle(b, -0.03f * D, top, 0.06f * D, W);
                        g.FillRectangle(b, -W / 2, -0.03f * D, W, 0.06f * D);
                    }
                    float ps = 0.24f * D;
                    foreach (int sx in Sides)
                        foreach (int sy in Sides)
                        {
                            RectangleF pl = new RectangleF(sx < 0 ? -W / 2 + 0.02f * D : W / 2 - 0.02f * D - ps, sy < 0 ? top + 0.02f * D : top + W - 0.02f * D - ps, ps, ps);
                            using (GraphicsPath pp = Theme.Round(pl, 0.05f * D))
                            {
                                Fill(g, pp, pl, Color.FromArgb(176, 182, 192), Color.FromArgb(120, 126, 138), 90f);
                                Stroke(g, pp, Color.FromArgb(90, 60, 64, 74), D * 0.007f);
                            }
                        }
                    RectangleF c = new RectangleF(-0.13f * D, -0.13f * D, 0.26f * D, 0.26f * D);
                    using (GraphicsPath cp = new GraphicsPath())
                    {
                        cp.AddEllipse(c);
                        Fill(g, cp, c, Color.FromArgb(236, 238, 242), Color.FromArgb(190, 194, 202), 90f);
                        Stroke(g, cp, Color.FromArgb(150, 110, 116, 128), D * 0.012f);
                    }
                    using (GraphicsPath hp = Heart(0, 0.005f * D, 0.12f * D)) FillSolid(g, hp, pink);
                    break;
                }
                case KindBomberman:
                {
                    // The skin-coloured face opening in the white helmet.
                    RectangleF f = new RectangleF(-0.21f * D, -0.15f * D, 0.42f * D, 0.25f * D);
                    using (GraphicsPath fp = Theme.Round(f, 0.11f * D))
                    {
                        Fill(g, fp, f, Color.FromArgb(255, 220, 192), Color.FromArgb(240, 184, 152), 90f);
                        Stroke(g, fp, Color.FromArgb(120, 150, 110, 100), D * 0.008f);
                    }
                    break;
                }
                case KindPooka:
                    // The bridge between the goggles.
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 214, 40))) g.FillRectangle(b, -0.04f * D, fy - 0.02f * D, 0.08f * D, 0.04f * D);
                    break;
                case KindFallGuy:
                {
                    RectangleF f = new RectangleF(-0.2f * D, -0.27f * D, 0.4f * D, 0.26f * D);
                    using (GraphicsPath fp = new GraphicsPath())
                    {
                        fp.AddEllipse(f);
                        Fill(g, fp, f, Color.FromArgb(255, 246, 238), Color.FromArgb(240, 222, 208), 90f);
                        Stroke(g, fp, Color.FromArgb(90, 160, 120, 130), D * 0.008f);
                    }
                    break;
                }
                case KindMcPig:
                {
                    // Pixel texture, the two eyes and the snout with its nostrils.
                    float s = W / 8;
                    for (int r = 0; r < 8; r++)
                        for (int c = 0; c < 8; c++)
                        {
                            int h = (r * 5 + c * 11 + r * c) % 5;
                            if (h > 1) continue;
                            using (SolidBrush b = new SolidBrush(h == 0 ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 120, 40, 60))) g.FillRectangle(b, -W / 2 + c * s, top + r * s, s, s);
                        }
                    int[][] px =
                    {
                        new int[] { 1, 3, 0 }, new int[] { 2, 3, 1 }, new int[] { 5, 3, 1 }, new int[] { 6, 3, 0 }
                    };
                    foreach (int[] q in px)
                        using (SolidBrush b = new SolidBrush(q[2] == 0 ? Color.White : Color.FromArgb(24, 20, 24))) g.FillRectangle(b, -W / 2 + q[0] * s, top + q[1] * s, s, s);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(252, 206, 206))) g.FillRectangle(b, -W / 2 + 2 * s, top + 4 * s, 4 * s, 2 * s);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 70, 90))) { g.FillRectangle(b, -W / 2 + 2 * s, top + 5 * s, s, s); g.FillRectangle(b, -W / 2 + 5 * s, top + 5 * s, s, s); }
                    break;
                }
                case KindQBlock:
                {
                    // Rivets in the corners and the question mark with its shadow; just hit, it shows empty and brown.
                    bool used = p.Power == PowerCoin && p.Fx > 0;
                    if (used)
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(176, 112, 60))) g.FillRectangle(b, -W / 2, top, W, W);
                    float inset = 0.07f * D, r = 0.022f * D;
                    foreach (int sx in Sides)
                        foreach (int sy in Sides)
                            Ellipse(g, Color.FromArgb(200, 120, 66, 10), (sx < 0 ? -W / 2 + inset : W / 2 - inset) - r, (sy < 0 ? top + inset : top + W - inset) - r, r * 2, r * 2);
                    if (used) break;
                    using (GraphicsPath q = new GraphicsPath())
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        q.AddString("?", FontFamily.GenericSansSerif, (int)FontStyle.Bold, 0.5f * D, new PointF(0, 0.01f * D), sf);
                        GraphicsState st = g.Save();
                        g.TranslateTransform(0.025f * D, 0.025f * D);
                        FillSolid(g, q, Color.FromArgb(150, 80, 10));
                        g.Restore(st);
                        FillSolid(g, q, Color.FromArgb(255, 246, 222));
                    }
                    break;
                }
                case KindMetroid:
                {
                    // Three red nuclei glowing inside the membrane.
                    float[][] nuc = { new float[] { -0.1f, -0.06f }, new float[] { 0.1f, -0.06f }, new float[] { 0, 0.05f } };
                    foreach (float[] n in nuc)
                    {
                        Glow(g, n[0] * D, n[1] * D, 0.22f * D, 0.2f * D, Color.FromArgb(255, 90, 60), 110);
                        RectangleF nr = new RectangleF(n[0] * D - 0.065f * D, n[1] * D - 0.055f * D, 0.13f * D, 0.11f * D);
                        using (GraphicsPath np = new GraphicsPath())
                        {
                            np.AddEllipse(nr);
                            Fill(g, np, nr, Color.FromArgb(250, 96, 70), Color.FromArgb(176, 30, 30), 90f);
                        }
                    }
                    break;
                }
                case KindWaddleDee:
                {
                    RectangleF f = new RectangleF(-0.24f * D, -0.21f * D, 0.48f * D, 0.33f * D);
                    using (GraphicsPath fp = new GraphicsPath())
                    {
                        fp.AddEllipse(f);
                        Fill(g, fp, f, Color.FromArgb(255, 230, 196), Color.FromArgb(244, 204, 164), 90f);
                    }
                    break;
                }
            }
            g.Clip = old;
            old.Dispose();
        }

        // ---- In front of the face.
        static void ArcadeDetails(Graphics g, MascotLook l, Geo geo, float D, MascotPose p)
        {
            float fy = geo.FaceY * D, gap = geo.EyeGap * D;
            switch (l.Kind)
            {
                case KindBoo:
                {
                    // Shy: a blush, and its two little hands rising to cover its eyes.
                    if (p.Shy < 0.02) break;
                    float k = (float)p.Shy, front = 0.05f * D;
                    foreach (int s in Sides)
                        Glow(g, front + s * (gap + 0.07f * D), fy + 0.07f * D, 0.12f * D, 0.06f * D, Color.FromArgb(255, 110, 150), (int)(150 * k));
                    foreach (int s in Sides)
                    {
                        float hx = front + s * gap, hy = fy + 0.24f * D + (0.0f - 0.24f) * D * k;
                        RectangleF hand = new RectangleF(hx - 0.075f * D, hy - 0.06f * D, 0.15f * D, 0.12f * D);
                        using (GraphicsPath hp = new GraphicsPath())
                        {
                            hp.AddEllipse(hand);
                            Fill(g, hp, hand, Color.FromArgb(255, 255, 255), Color.FromArgb(214, 216, 226), 80f);
                            Stroke(g, hp, Color.FromArgb(190, 90, 94, 112), D * 0.011f);
                        }
                    }
                    break;
                }
                case KindGoomba:
                {
                    // A clear dark outline and the line where the head meets the body, then the thick angry brows.
                    Color edge = Color.FromArgb(230, 52, 26, 10);
                    using (GraphicsPath gp = ArcadePath(KindGoomba, geo, D, p.Now)) Stroke(g, gp, edge, D * 0.016f);
                    using (Pen pen = new Pen(edge, D * 0.012f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawBezier(pen, P(-0.4f * D, 0.13f * D), P(-0.3f * D, 0.175f * D), P(0.3f * D, 0.175f * D), P(0.4f * D, 0.13f * D));
                    }
                    foreach (int s in Sides)
                        using (GraphicsPath b = Poly(P(s * 0.19f * D, fy - 0.12f * D), P(s * 0.025f * D, fy - 0.055f * D), P(s * 0.025f * D, fy - 0.01f * D), P(s * 0.19f * D, fy - 0.07f * D)))
                            FillSolid(g, b, Color.FromArgb(28, 18, 14));
                    break;
                }
                case KindBobomb:
                {
                    // The fuse cap, the wick and its spark.
                    float t = geo.Top * D;
                    RectangleF cap = new RectangleF(-0.06f * D, t - 0.05f * D, 0.12f * D, 0.07f * D);
                    using (GraphicsPath cp = Theme.Round(cap, 0.015f * D)) Fill(g, cp, cap, Color.FromArgb(200, 200, 210), Color.FromArgb(120, 120, 134), 90f);
                    using (Pen w = new Pen(Color.FromArgb(150, 130, 110), D * 0.02f))
                    {
                        w.StartCap = LineCap.Round;
                        w.EndCap = LineCap.Round;
                        g.DrawBezier(w, P(0, t - 0.05f * D), P(0, t - 0.1f * D), P(0.04f * D, t - 0.1f * D), P(0.05f * D, t - 0.15f * D));
                    }
                    float fl = (float)(0.6 + 0.4 * Math.Abs(Math.Sin(p.Now / 70.0))) * (p.Power == PowerBoom ? 1 + (float)p.Fx * 1.3f : 1);
                    PointF sp = P(0.05f * D, t - 0.15f * D);
                    Glow(g, sp.X, sp.Y, 0.14f * D * fl, 0.14f * D * fl, Color.FromArgb(255, 180, 40), 200);
                    for (int i = 0; i < 4; i++)
                    {
                        double a = p.Now / 120.0 + i * Math.PI / 2;
                        Line(g, Color.FromArgb(240, 255, 236, 120), D * 0.008f, sp.X, sp.Y, sp.X + (float)Math.Cos(a) * 0.045f * D * fl, sp.Y + (float)Math.Sin(a) * 0.045f * D * fl);
                    }
                    break;
                }
                case KindRedBird:
                {
                    // The thick dark brows meeting in the middle, then the beak.
                    using (GraphicsPath b = Poly(P(-0.2f * D, fy - 0.1f * D), P(0, fy - 0.035f * D), P(0.2f * D, fy - 0.1f * D), P(0.2f * D, fy - 0.055f * D), P(0, fy + 0.005f * D), P(-0.2f * D, fy - 0.055f * D)))
                        FillSolid(g, b, Color.FromArgb(46, 20, 20));
                    using (GraphicsPath up = Poly(P(-0.075f * D, fy + 0.07f * D), P(0.075f * D, fy + 0.07f * D), P(0.01f * D, fy + 0.15f * D)))
                        Fill(g, up, new RectangleF(-0.075f * D, fy + 0.07f * D, 0.15f * D, 0.08f * D), Color.FromArgb(255, 204, 60), Color.FromArgb(236, 150, 30), 90f);
                    using (GraphicsPath lo = Poly(P(-0.05f * D, fy + 0.13f * D), P(0.05f * D, fy + 0.13f * D), P(0.005f * D, fy + 0.18f * D)))
                        FillSolid(g, lo, Color.FromArgb(226, 140, 30));
                    break;
                }
                case KindGreenPig:
                {
                    // The big snout with its nostrils.
                    RectangleF sn = new RectangleF(-0.13f * D, fy + 0.06f * D, 0.26f * D, 0.17f * D);
                    using (GraphicsPath sp = new GraphicsPath())
                    {
                        sp.AddEllipse(sn);
                        Fill(g, sp, sn, Color.FromArgb(196, 244, 150), Color.FromArgb(140, 210, 90), 90f);
                        Stroke(g, sp, Color.FromArgb(110, 60, 120, 30), D * 0.008f);
                    }
                    Ellipse(g, Color.FromArgb(220, 40, 90, 20), -0.07f * D, fy + 0.11f * D, 0.04f * D, 0.06f * D);
                    Ellipse(g, Color.FromArgb(220, 40, 90, 20), 0.03f * D, fy + 0.11f * D, 0.04f * D, 0.06f * D);
                    break;
                }
                case KindToad:
                case KindMushroom:
                {
                    // A clear dark outline round the whole shape and along the rim of the cap, like the originals.
                    bool toad = l.Kind == KindToad;
                    Color edge = toad ? Color.FromArgb(200, 70, 50, 50) : Color.FromArgb(220, Darker(Colors[l.Color, 1], 0.5));
                    using (GraphicsPath sp = toad ? CapPath(D, -0.33f, 0.03f, 0.43f, 0.25f, 0.31f) : CapPath(D, -0.3f, 0.07f, 0.43f, 0.22f, 0.31f))
                        Stroke(g, sp, edge, D * 0.016f);
                    float capY = (toad ? 0.03f : 0.07f) * D, rim = capY + 0.04f * D, sw = (toad ? 0.25f : 0.22f) * D;
                    using (Pen pen = new Pen(edge, D * 0.012f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawBezier(pen, P(-0.42f * D, capY), P(-0.3f * D, rim + 0.005f * D), P(0.3f * D, rim + 0.005f * D), P(0.42f * D, capY));
                        g.DrawLine(pen, -sw, rim, sw, rim);
                    }
                    break;
                }
            }
        }
    }

    public partial class Mascot
    {
        // Faces drawn whole: Pac-Man's eye, the Knight's mask and hollow eyes, and none at all for the pixel and block ones.
        bool ArcadeFace(Graphics g, float D, MascotParts.Geo geo, float ex, float ey)
        {
            int k = Look.Kind;
            if (k == MascotParts.KindPacman)
            {
                float h = (mood == Mood.Sleep ? 0.012f : 0.07f) * D;
                // On the front side; the whole drawing turns when it walks the other way.
                MascotParts.Ellipse(g, InkBlack, 0.02f * D + ex * 0.5f, -0.17f * D - h / 2, 0.05f * D, h);
                return true;
            }
            if (k == MascotParts.KindKnight)
            {
                RectangleF m = new RectangleF(-0.25f * D, -0.34f * D, 0.5f * D, 0.4f * D);
                using (GraphicsPath mp = new GraphicsPath())
                {
                    mp.AddEllipse(m);
                    MascotParts.Fill(g, mp, m, Color.FromArgb(252, 252, 252), Color.FromArgb(208, 210, 222), 90f);
                    MascotParts.Stroke(g, mp, Color.FromArgb(120, 90, 94, 110), Math.Max(1f, D * 0.008f));
                }
                foreach (int s in new int[] { -1, 1 })
                {
                    float x = s * geo.EyeGap * D + ex * 0.6f, y = geo.FaceY * D, w = 0.085f * D, h = (mood == Mood.Sleep ? 0.015f : 0.13f) * D;
                    MascotParts.Ellipse(g, Color.FromArgb(14, 14, 20), x - w / 2, y - h / 2, w, h);
                }
                return true;
            }
            return MascotParts.IsArcade(k) && MascotParts.ArcadeFixedFace(k);
        }

        bool ArcadeEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            int k = Look.Kind;
            float sk = surprised ? 1.15f : 1f;
            switch (k)
            {
                case MascotParts.KindGoomba:
                {
                    // White ovals with black pupils looking inward, under the brows.
                    float w = 0.07f * D * sk, h = 0.11f * D * sk * blink;
                    if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return true; }
                    MascotParts.Ellipse(g, Color.White, x - w / 2, y - h / 2, w, h);
                    using (Pen p = new Pen(Color.FromArgb(200, InkBlack), Math.Max(1f, D * 0.008f))) g.DrawEllipse(p, x - w / 2, y - h / 2, w, h);
                    MascotParts.Ellipse(g, InkBlack, x - w * 0.25f + (x < 0 ? w * 0.12f : -w * 0.12f), y - h * 0.22f, w * 0.5f, h * 0.6f);
                    return true;
                }
                case MascotParts.KindBoo:
                case MascotParts.KindToad:
                case MascotParts.KindMushroom:
                case MascotParts.KindBomberman:
                case MascotParts.KindWaddleDee:
                {
                    // Tall black ovals, with a glint except on Boo and the mushroom.
                    float w = (k == MascotParts.KindBoo ? 0.045f : k == MascotParts.KindWaddleDee ? 0.05f : 0.042f) * D * sk;
                    float h = (k == MascotParts.KindBoo ? 0.09f : k == MascotParts.KindWaddleDee ? 0.1f : 0.088f) * D * sk * blink;
                    if (h < D * 0.02f) { ClosedEye(g, x, y, w * 1.4f, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    if (k != MascotParts.KindBoo && k != MascotParts.KindMushroom) MascotParts.Ellipse(g, Color.FromArgb(230, 255, 255, 255), x - w * 0.3f, y - h * 0.36f, w * 0.38f, h * 0.26f);
                    return true;
                }
                case MascotParts.KindBobomb:
                {
                    float w = 0.07f * D * sk, h = 0.12f * D * sk * blink;
                    if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return true; }
                    MascotParts.Ellipse(g, Color.White, x - w / 2, y - h / 2, w, h);
                    MascotParts.Ellipse(g, InkBlack, x - w * 0.22f, y - h * 0.2f, w * 0.44f, h * 0.55f);
                    return true;
                }
                case MascotParts.KindChomp:
                    WhiteEye(g, x, y, 0.12f * D * sk, 0.12f * D * sk * blink, 0.022f * D, 0, 0, D);
                    return true;
                case MascotParts.KindRedBird:
                    WhiteEye(g, x, y, 0.1f * D * sk, 0.1f * D * sk * blink, 0.026f * D, (x < 0 ? 1 : -1) * 0.015f * D, 0.012f * D, D);
                    return true;
                case MascotParts.KindGreenPig:
                    WhiteEye(g, x, y, 0.085f * D * sk, 0.1f * D * sk * blink, 0.022f * D, 0, 0.01f * D, D);
                    return true;
                case MascotParts.KindPooka:
                {
                    // Big yellow goggles with the eye inside.
                    float r = 0.085f * D;
                    RectangleF gr = new RectangleF(x - r, y - r, r * 2, r * 2);
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddEllipse(gr);
                        MascotParts.Fill(g, gp, gr, Color.FromArgb(255, 226, 60), Color.FromArgb(226, 170, 20), 90f);
                        MascotParts.Stroke(g, gp, Color.FromArgb(200, 120, 80, 10), Math.Max(1f, D * 0.01f));
                    }
                    WhiteEye(g, x, y, 0.11f * D * sk, 0.11f * D * sk * blink, 0.024f * D, 0, 0, D);
                    return true;
                }
                case MascotParts.KindFallGuy:
                {
                    float w = 0.075f * D * sk, h = 0.085f * D * sk * blink;
                    if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    MascotParts.Ellipse(g, Color.White, x - w * 0.28f, y - h * 0.32f, w * 0.32f, h * 0.3f);
                    MascotParts.Ellipse(g, Color.FromArgb(200, 255, 255, 255), x + w * 0.08f, y + h * 0.08f, w * 0.16f, h * 0.14f);
                    return true;
                }
                default:
                    return false;
            }
        }

        bool ArcadeMouth(Graphics g, float D, float mx, float fy, Color ink, float lw, bool talking)
        {
            int k = Look.Kind;
            bool open = talking || mood == Mood.Happy || mood == Mood.Love;
            switch (k)
            {
                case MascotParts.KindGoomba:
                {
                    // A frown with two fangs sticking up from it.
                    float my = fy + 0.13f * D, w = 0.16f * D;
                    using (Pen p = new Pen(InkBlack, Math.Max(1.2f, D * 0.014f)))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, mx - w / 2, my - 0.01f * D, w, 0.07f * D, 200, 140);
                    }
                    foreach (int s in new int[] { -1, 1 })
                        using (GraphicsPath f = new GraphicsPath())
                        {
                            float fx = mx + s * w * 0.32f, fb = my + 0.005f * D;
                            f.AddPolygon(new PointF[] { new PointF(fx - 0.022f * D, fb), new PointF(fx + 0.022f * D, fb), new PointF(fx + s * 0.004f * D, fb - 0.045f * D) });
                            MascotParts.FillSolid(g, f, Color.White);
                            MascotParts.Stroke(g, f, Color.FromArgb(160, InkBlack), Math.Max(1f, D * 0.006f));
                        }
                    return true;
                }
                case MascotParts.KindBoo:
                {
                    // Wide open, fangs at the top and the tongue hanging out; a small shy wiggle while it hides.
                    if (shy > 0.5)
                    {
                        using (Pen sp = new Pen(InkBlack, Math.Max(1f, D * 0.012f)))
                        {
                            sp.LineJoin = LineJoin.Round;
                            g.DrawLines(sp, new PointF[] { new PointF(mx - 0.05f * D, fy + 0.14f * D), new PointF(mx - 0.025f * D, fy + 0.125f * D), new PointF(mx, fy + 0.14f * D),
                                                           new PointF(mx + 0.025f * D, fy + 0.125f * D), new PointF(mx + 0.05f * D, fy + 0.14f * D) });
                        }
                        return true;
                    }
                    float my = fy + 0.11f * D, w = 0.26f * D, h = (talking ? 0.13f + 0.03f * (float)Math.Abs(Math.Sin(now / 110.0)) : 0.15f) * D;
                    using (GraphicsPath m = new GraphicsPath())
                    {
                        m.AddArc(mx - w / 2, my - h / 2, w, h, 0, 180);
                        m.CloseFigure();
                        MascotParts.FillSolid(g, m, Color.FromArgb(130, 20, 40));
                        MascotParts.Stroke(g, m, Color.FromArgb(200, InkBlack), Math.Max(1f, D * 0.008f));
                    }
                    foreach (int s in new int[] { -1, 1 })
                        using (GraphicsPath f = new GraphicsPath())
                        {
                            float fx = mx + s * w * 0.3f;
                            f.AddPolygon(new PointF[] { new PointF(fx - 0.02f * D, my), new PointF(fx + 0.02f * D, my), new PointF(fx, my + 0.04f * D) });
                            MascotParts.FillSolid(g, f, Color.White);
                        }
                    RectangleF t = new RectangleF(mx - 0.045f * D, my + h * 0.15f, 0.09f * D, h * 0.75f);
                    using (GraphicsPath tp = new GraphicsPath())
                    {
                        tp.AddEllipse(t);
                        MascotParts.Fill(g, tp, t, Color.FromArgb(255, 130, 160), Color.FromArgb(230, 80, 120), 90f);
                    }
                    return true;
                }
                case MascotParts.KindChomp:
                {
                    // The huge mouth snapping open and shut, with rows of white teeth.
                    bool lunge = power == MascotParts.PowerLunge && powerStart >= 0 && now - powerStart < 1300;
                    float my = fy + 0.24f * D, w = (lunge ? 0.56f : 0.5f) * D, h = (0.06f + (lunge ? 0.22f : 0.18f) * (float)Math.Abs(Math.Sin(now / (lunge ? 70.0 : 200.0)))) * D;
                    using (GraphicsPath m = new GraphicsPath())
                    {
                        m.AddEllipse(mx - w / 2, my - h / 2, w, h);
                        MascotParts.FillSolid(g, m, Color.FromArgb(120, 16, 24));
                        Region old = g.Clip;
                        g.SetClip(m, CombineMode.Intersect);
                        for (int i = 0; i < 7; i++)
                        {
                            float tx = mx - w / 2 + w * (i + 0.5f) / 7f, tw = w / 14f;
                            using (GraphicsPath tt = new GraphicsPath())
                            {
                                tt.AddPolygon(new PointF[] { new PointF(tx - tw, my - h / 2), new PointF(tx + tw, my - h / 2), new PointF(tx, my - h / 2 + 0.05f * D) });
                                MascotParts.FillSolid(g, tt, Color.White);
                            }
                            using (GraphicsPath tb = new GraphicsPath())
                            {
                                tb.AddPolygon(new PointF[] { new PointF(tx - tw, my + h / 2), new PointF(tx + tw, my + h / 2), new PointF(tx, my + h / 2 - 0.05f * D) });
                                MascotParts.FillSolid(g, tb, Color.White);
                            }
                        }
                        g.Clip = old;
                        old.Dispose();
                        MascotParts.Stroke(g, m, Color.FromArgb(220, 10, 10, 14), Math.Max(1f, D * 0.01f));
                    }
                    return true;
                }
                case MascotParts.KindGreenPig:
                    ArcMouth(g, mx, fy + 0.27f * D, 0.14f * D, 0.04f * D, InkBlack, Math.Max(1f, D * 0.012f));
                    return true;
                case MascotParts.KindToad:
                    if (open) OpenMouth(g, mx, fy + 0.11f * D, 0.09f * D, 0.06f * D, D);
                    else ArcMouth(g, mx, fy + 0.1f * D, 0.08f * D, 0.035f * D, ink, lw);
                    return true;
                case MascotParts.KindFallGuy:
                    if (open) OpenMouth(g, mx, fy + 0.085f * D, 0.07f * D, 0.045f * D, D);
                    else ArcMouth(g, mx, fy + 0.08f * D, 0.06f * D, 0.025f * D, ink, lw);
                    return true;
                case MascotParts.KindMushroom:
                case MascotParts.KindBobomb:
                case MascotParts.KindRedBird:
                case MascotParts.KindBomberman:
                case MascotParts.KindPooka:
                case MascotParts.KindWaddleDee:
                    return true;   // no mouth in the originals (or the beak stands for it)
                default:
                    return false;
            }
        }
    }
}

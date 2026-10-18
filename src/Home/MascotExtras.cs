// Stackshot - The big batch of tributes: more Pokemon (Psyduck, Togepi, Meowth, Piplup, Mimikyu, Lucario, Dragonite,
// Ditto, Mewtwo), Ghibli spirits (No-Face, Calcifer, a kodama), game icons (Yoshi, Pac-Man, a Creeper, an Among Us
// crewmate, a Pac-Man ghost, a Dragon Quest slime) and classic cuties (Snoopy, Hello Kitty, Baby Groot, Rilakkuma,
// Pusheen, Pochacco). This file holds their shapes, eyes and mouths; MascotExtrasArt.cs draws their pieces. Fan
// tributes, not affiliated with their owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        public const int KindPsyduck = 34, KindTogepi = 35, KindMeowth = 36, KindPiplup = 37, KindMimikyu = 38, KindLucario = 39,
                         KindDragonite = 40, KindDitto = 41, KindMewtwo = 42, KindNoFace = 43, KindCalcifer = 44, KindKodama = 45,
                         KindYoshi = 46, KindPacman = 47, KindCreeper = 48, KindCrewmate = 49, KindBlinky = 50, KindDqSlime = 51,
                         KindSnoopy = 52, KindKitty = 53, KindGroot = 54, KindRilakkuma = 55, KindPusheen = 56, KindPochacco = 57;

        public static bool IsExtra(int kind) { return kind >= KindPsyduck && (kind <= KindDupeCurly || IsFigure(kind) || kind == KindAbigail || IsToon(kind)); }

        // A dark outline in the body's own shade, for a crisp drawn look; not on the see-through or glowing ones, nor the pixels.
        public static bool Inked(int kind) { return kind != 3 && kind != KindCalcifer && kind != KindMetroid && kind != KindInvader; }

        // Characters without paws of their own, and the ones whose face has no pink blush.
        public static bool NoHands(int kind)
        {
            return kind == KindMimikyu || kind == KindNoFace || kind == KindCalcifer || kind == KindCreeper || kind == KindCrewmate ||
                   kind == KindBlinky || kind == KindDqSlime || kind == KindAbigail || kind == KindNevadoSd || PkNoHands(kind) || (IsArcade(kind) && ArcadeNoHands(kind));
        }

        public static bool NoCheeks(int kind)
        {
            return kind == KindMimikyu || kind == KindDitto || kind == KindNoFace || kind == KindCalcifer || kind == KindKodama ||
                   kind == KindCreeper || kind == KindCrewmate || kind == KindBlinky || kind == KindKitty || kind == KindSnoopy ||
                   kind == KindMewtwo || kind == KindLucario || kind == KindGroot || kind == KindPsyduck || kind == KindKirby || kind == KindWilson || kind == KindWillow || kind == KindAbigail || IsToon(kind) || (IsFigure(kind) && !FigOf(kind).Blush) || (IsArcade(kind) && ArcadeNoCheeks(kind));
        }

        static Geo G(float headW, float top, float faceY, float eyeGap, float neckY, float neckW, float bottom, float handX, float handY, float hatW)
        {
            Geo g = new Geo();
            g.HeadW = headW; g.Top = top; g.FaceY = faceY; g.EyeGap = eyeGap; g.NeckY = neckY; g.NeckW = neckW;
            g.Bottom = bottom; g.HandX = handX; g.HandY = handY; g.HatW = hatW;
            return g;
        }

        static Geo ExtraGeo(int kind)
        {
            if (IsArcade(kind)) return ArcadeGeo(kind);
            if (IsKlei(kind)) return KleiGeo(kind);
            if (IsToon(kind)) return ToonGeo(kind);
            if (IsFigure(kind)) return FigureGeo(kind);
            switch (kind)
            {
                case KindPsyduck: return G(0.74f, -0.3f, -0.08f, 0.12f, 0.14f, 0.62f, 0.31f, 0.37f, 0.17f, 0.56f);
                case KindTogepi: return G(0.66f, -0.28f, -0.1f, 0.1f, 0.1f, 0.56f, 0.32f, 0.33f, 0.1f, 0.5f);
                case KindMeowth: return G(0.76f, -0.3f, -0.06f, 0.13f, 0.13f, 0.62f, 0.31f, 0.36f, 0.2f, 0.56f);
                case KindPiplup: return G(0.68f, -0.3f, -0.1f, 0.11f, 0.06f, 0.58f, 0.31f, 0.34f, 0.16f, 0.54f);
                case KindMimikyu: return G(0.7f, -0.29f, -0.1f, 0.12f, 0.1f, 0.6f, 0.34f, 0.33f, 0.18f, 0.54f);
                case KindLucario: return G(0.68f, -0.3f, -0.08f, 0.12f, 0.1f, 0.58f, 0.31f, 0.34f, 0.2f, 0.54f);
                case KindDragonite: return G(0.76f, -0.3f, -0.1f, 0.1f, 0.1f, 0.62f, 0.32f, 0.38f, 0.16f, 0.56f);
                case KindDitto: return G(0.8f, -0.2f, -0.02f, 0.1f, 0.16f, 0.7f, 0.32f, 0.41f, 0.18f, 0.5f);
                case KindMewtwo: return G(0.66f, -0.3f, -0.08f, 0.12f, 0.1f, 0.56f, 0.31f, 0.33f, 0.2f, 0.52f);
                case KindNoFace: return G(0.6f, -0.34f, -0.15f, 0.085f, 0.1f, 0.56f, 0.34f, 0.31f, 0.18f, 0.48f);
                case KindCalcifer: return G(0.64f, -0.22f, 0.0f, 0.11f, 0.2f, 0.56f, 0.3f, 0.33f, 0.2f, 0.48f);
                case KindKodama: return G(0.62f, -0.32f, -0.1f, 0.1f, 0.08f, 0.54f, 0.31f, 0.3f, 0.18f, 0.5f);
                case KindYoshi: return G(0.72f, -0.28f, -0.13f, 0.08f, 0.14f, 0.6f, 0.31f, 0.36f, 0.2f, 0.54f);
                case KindCreeper: return G(0.62f, -0.31f, -0.1f, 0.1f, 0.2f, 0.6f, 0.31f, 0.33f, 0.2f, 0.62f);
                case KindCrewmate: return G(0.58f, -0.32f, -0.12f, 0.1f, 0.1f, 0.56f, 0.33f, 0.3f, 0.2f, 0.5f);
                case KindBlinky: return G(0.66f, -0.32f, -0.1f, 0.12f, 0.12f, 0.6f, 0.32f, 0.34f, 0.2f, 0.56f);
                case KindDqSlime: return G(0.78f, -0.22f, 0.03f, 0.13f, 0.17f, 0.7f, 0.33f, 0.4f, 0.21f, 0.5f);
                case KindSnoopy: return G(0.7f, -0.31f, -0.06f, 0.1f, 0.13f, 0.6f, 0.31f, 0.34f, 0.21f, 0.54f);
                case KindKitty: return G(0.8f, -0.28f, -0.04f, 0.16f, 0.14f, 0.64f, 0.31f, 0.38f, 0.2f, 0.56f);
                case KindGroot: return G(0.66f, -0.3f, -0.05f, 0.12f, 0.12f, 0.56f, 0.31f, 0.33f, 0.2f, 0.52f);
                case KindRilakkuma: return G(0.74f, -0.3f, -0.06f, 0.13f, 0.12f, 0.62f, 0.31f, 0.36f, 0.21f, 0.56f);
                case KindPusheen: return G(0.84f, -0.2f, 0.02f, 0.15f, 0.16f, 0.7f, 0.31f, 0.4f, 0.24f, 0.52f);
                default: return G(0.72f, -0.3f, -0.04f, 0.11f, 0.12f, 0.6f, 0.31f, 0.35f, 0.21f, 0.54f); // Pochacco
            }
        }

        // Body outlines that are not the usual rounded square; null keeps the squircle, with this roundness.
        static GraphicsPath ExtraPath(int kind, Geo geo, float D, double now)
        {
            if (IsArcade(kind)) return ArcadePath(kind, geo, D, now);
            if (IsKlei(kind)) return KleiPath(kind, D, now);
            if (IsToon(kind)) return ToonPath(kind, D);
            if (IsFigure(kind)) return FigurePath(D);
            float W = geo.HeadW * D, top = geo.Top * D, bottom = geo.Bottom * D;
            switch (kind)
            {
                case KindDitto:
                {
                    // A soft blob, wider at the bottom, wobbling a little.
                    float[] xs = { -0.18f, 0, 0.2f, 0.36f, 0.42f, 0.3f, 0, -0.3f, -0.42f, -0.36f };
                    float[] ys = { -0.2f, -0.22f, -0.19f, -0.02f, 0.2f, 0.32f, 0.33f, 0.32f, 0.22f, 0.0f };
                    PointF[] pts = new PointF[xs.Length];
                    for (int i = 0; i < xs.Length; i++)
                    {
                        float w = (float)Math.Sin(now / 520.0 + i * 1.7) * 0.012f;
                        pts[i] = P((xs[i] + w) * D, (ys[i] - w) * D);
                    }
                    GraphicsPath p = new GraphicsPath();
                    p.AddClosedCurve(pts, 0.55f);
                    return p;
                }
                case KindNoFace:
                {
                    // A tall hood that widens down to the ground.
                    GraphicsPath p = new GraphicsPath();
                    p.AddBezier(P(-0.37f * D, 0.34f * D), P(-0.34f * D, 0.1f * D), P(-0.3f * D, -0.08f * D), P(-0.27f * D, -0.14f * D));
                    p.AddArc(-0.27f * D, -0.34f * D, 0.54f * D, 0.4f * D, 180, 180);
                    p.AddBezier(P(0.27f * D, -0.14f * D), P(0.3f * D, -0.08f * D), P(0.34f * D, 0.1f * D), P(0.37f * D, 0.34f * D));
                    p.AddBezier(P(0.37f * D, 0.34f * D), P(0.2f * D, 0.37f * D), P(-0.2f * D, 0.37f * D), P(-0.37f * D, 0.34f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindCalcifer:
                {
                    // A flame: a round bottom and tongues on top that flicker.
                    float[] xs = { 0, 0.24f, 0.32f, 0.3f, 0.37f, 0.2f, 0.14f, 0.04f, -0.06f, -0.14f, -0.27f, -0.28f, -0.32f, -0.24f };
                    float[] ys = { 0.3f, 0.24f, 0.06f, -0.1f, -0.24f, -0.14f, -0.35f, -0.2f, -0.42f, -0.18f, -0.3f, -0.08f, 0.08f, 0.24f };
                    PointF[] pts = new PointF[xs.Length];
                    for (int i = 0; i < xs.Length; i++)
                    {
                        bool tip = i == 4 || i == 6 || i == 8 || i == 10;
                        float f = tip ? (float)Math.Sin(now / 90.0 + i * 1.3) * 0.025f : 0;
                        pts[i] = P(xs[i] * D + f * 0.4f * D, ys[i] * D + f * D);
                    }
                    GraphicsPath p = new GraphicsPath();
                    p.AddClosedCurve(pts, 0.5f);
                    return p;
                }
                case KindKodama:
                {
                    // A slightly lopsided round head.
                    float[] xs = { 0, 0.24f, 0.31f, 0.27f, 0.15f, 0, -0.15f, -0.28f, -0.31f, -0.22f };
                    float[] ys = { -0.32f, -0.27f, -0.06f, 0.14f, 0.29f, 0.31f, 0.29f, 0.13f, -0.08f, -0.28f };
                    PointF[] pts = new PointF[xs.Length];
                    for (int i = 0; i < xs.Length; i++) pts[i] = P(xs[i] * D, ys[i] * D);
                    GraphicsPath p = new GraphicsPath();
                    p.AddClosedCurve(pts, 0.5f);
                    return p;
                }
                case KindCreeper:
                    return Theme.Round(new RectangleF(-W / 2, top, W, bottom - top), 0.03f * D);
                case KindCrewmate:
                {
                    // The bean: a round top, straight sides and two short legs.
                    GraphicsPath p = new GraphicsPath();
                    p.AddArc(-0.29f * D, -0.32f * D, 0.58f * D, 0.5f * D, 180, 180);
                    p.AddLine(P(0.29f * D, -0.07f * D), P(0.29f * D, 0.265f * D));
                    p.AddArc(0.06f * D, 0.2f * D, 0.23f * D, 0.13f * D, 0, 180);
                    p.AddLine(P(0.06f * D, 0.265f * D), P(0.04f * D, 0.21f * D));
                    p.AddLine(P(0.04f * D, 0.21f * D), P(-0.04f * D, 0.21f * D));
                    p.AddLine(P(-0.04f * D, 0.21f * D), P(-0.06f * D, 0.265f * D));
                    p.AddArc(-0.29f * D, 0.2f * D, 0.23f * D, 0.13f * D, 0, 180);
                    p.AddLine(P(-0.29f * D, 0.265f * D), P(-0.29f * D, -0.07f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindBlinky:
                {
                    // A dome, straight sides and the wavy skirt that switches between two steps.
                    GraphicsPath p = new GraphicsPath();
                    p.AddArc(-0.33f * D, -0.32f * D, 0.66f * D, 0.66f * D, 180, 180);
                    bool alt = ((long)(now / 220.0)) % 2 == 0;
                    p.AddLine(P(0.33f * D, 0.01f * D), P(0.33f * D, 0.33f * D));
                    for (int i = 1; i <= 6; i++)
                    {
                        float x = 0.33f - i * 0.11f;
                        bool up = (i % 2 == 1) ^ alt;
                        p.AddLine(p.GetLastPoint(), P(x * D, (up ? 0.25f : 0.33f) * D));
                    }
                    p.AddLine(p.GetLastPoint(), P(-0.33f * D, 0.01f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindDqSlime:
                    return BodyPath(4, geo, D, now);
                case KindMimikyu:
                {
                    // The rag costume: a round head and a ragged hem.
                    GraphicsPath p = new GraphicsPath();
                    p.AddArc(-0.35f * D, -0.29f * D, 0.7f * D, 0.6f * D, 180, 180);
                    float[] hx = { 0.35f, 0.26f, 0.18f, 0.08f, -0.02f, -0.12f, -0.2f, -0.29f, -0.35f };
                    float[] hy = { 0.3f, 0.35f, 0.29f, 0.35f, 0.3f, 0.35f, 0.29f, 0.35f, 0.3f };
                    p.AddLine(P(0.35f * D, 0.01f * D), P(hx[0] * D, hy[0] * D));
                    for (int i = 1; i < hx.Length; i++) p.AddLine(P(hx[i - 1] * D, hy[i - 1] * D), P(hx[i] * D, hy[i] * D));
                    p.AddLine(P(-0.35f * D, 0.3f * D), P(-0.35f * D, 0.01f * D));
                    p.CloseFigure();
                    return p;
                }
                case KindTogepi:
                case KindKitty:
                    return Squircle(new RectangleF(-W / 2, top, W, bottom - top), 2.2);
                case KindPusheen:
                    return Squircle(new RectangleF(-W / 2, top, W, bottom - top), 2.4);
                default:
                    return null;
            }
        }

        // Eyes drawn by the shared big-eye code: size, iris and pupil per character.
        static void ExtraEyeSpec(int kind, ref EyeSpec e)
        {
            switch (kind)
            {
                case KindTogepi: e.W = 0.1f; e.H = 0.12f; e.Dark = true; break;
                case KindPiplup: e.W = 0.1f; e.H = 0.12f; e.Dark = true; break;
                case KindDragonite: e.W = 0.09f; e.H = 0.11f; e.Dark = true; break;
                case KindLucario: e.W = 0.1f; e.H = 0.11f; e.Pupil = 0.45f; e.Iris = Color.FromArgb(220, 40, 52); e.Lid = true; e.LidColor = Color.FromArgb(20, 20, 28); break;
                case KindMewtwo: e.W = 0.105f; e.H = 0.1f; e.Pupil = 0.45f; e.Iris = Color.FromArgb(130, 60, 180); e.Lid = true; e.LidColor = Color.FromArgb(90, 70, 110); break;
                case KindGroot: e.W = 0.12f; e.H = 0.13f; e.Pupil = 0.7f; e.Iris = Color.FromArgb(70, 44, 24); break;
                default: e.W = 0.11f; e.H = 0.12f; e.Dark = true; break;
            }
        }
    }

    public partial class Mascot
    {
        static readonly Color InkBlack = Color.FromArgb(20, 18, 24);

        // A whole face of its own, instead of the usual eyes, blush and mouth: the Creeper's pixels (drawn on the body),
        // the crewmate's visor (also on the body), the Pac-Man ghost's eyes, which look where it goes, and Finn's small face.
        bool ExtraFace(Graphics g, float D, MascotParts.Geo geo, float ex, float ey, float blink)
        {
            int k = Look.Kind;
            if (k == MascotParts.KindFinn) return FinnFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindChopper) return ChopperFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindKeroSd) return KeroFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindSnoopySd) return SnoopyFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindTanjiroSd) return TanjiroFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindNezukoSd) return NezukoFace(g, D, geo, ex, ey, blink);
            if (MascotParts.IsShin(k)) return ShinFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindPiggySd) return BuriFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindNevadoSd) return NevadoFace(g, D, geo, ex, ey, blink);
            if (k == MascotParts.KindMimikyuSd) return true;   // its face is drawn on the rag, the same whatever its mood
            if (MascotParts.IsArcade(k)) return ArcadeFace(g, D, geo, ex, ey);
            if (k == MascotParts.KindCreeper || k == MascotParts.KindCrewmate) return true;
            if (k != MascotParts.KindBlinky) return false;
            if (pose.Power == MascotParts.PowerScared && pose.Fx > 0)
            {
                // Frightened: two small pale eyes and a wavy mouth (red on the white flashes).
                bool white = pose.Fx < 0.28 && ((long)(now / 160.0)) % 2 == 0;
                Color face = white ? Color.FromArgb(230, 40, 40) : Color.FromArgb(255, 206, 176);
                float fy = geo.FaceY * D, s = 0.05f * D;
                using (SolidBrush b = new SolidBrush(face))
                    foreach (int side in new int[] { -1, 1 }) g.FillRectangle(b, side * 0.09f * D - s / 2 + ex * 0.5f, fy - 0.03f * D, s, s);
                PointF[] z = new PointF[9];
                for (int i = 0; i < 9; i++) z[i] = new PointF(-0.2f * D + i * 0.05f * D, fy + (i % 2 == 0 ? 0.13f : 0.1f) * D);
                using (Pen mp = new Pen(face, Math.Max(1.2f, D * 0.016f))) { mp.LineJoin = LineJoin.Round; g.DrawLines(mp, z); }
                return true;
            }
            float dir = (float)Math.Max(-1, Math.Min(1, lookX + Facing));
            foreach (int s in new int[] { -1, 1 })
            {
                float x = s * geo.EyeGap * D + ex, y = ey, w = 0.12f * D, h = 0.15f * D;
                if (mood == Mood.Sleep) { ClosedEye(g, x, y, w * 0.8f, D); continue; }
                MascotParts.Ellipse(g, Color.White, x - w / 2, y - h / 2, w, h);
                float r = 0.036f * D;
                MascotParts.Ellipse(g, Color.FromArgb(40, 70, 220), x - r + dir * w * 0.22f, y - r + h * 0.08f, r * 2, r * 2);
            }
            return true;
        }

        // Eyes of their own, whatever the eye style.
        bool ExtraEye(Graphics g, float x, float y, float D, float blink, bool surprised)
        {
            int k = Look.Kind;
            if (!MascotParts.IsExtra(k)) return false;
            if (MascotParts.IsArcade(k)) return ArcadeEye(g, x, y, D, blink, surprised);
            if (MascotParts.IsKlei(k)) return KleiEye(g, x, y, D, blink, surprised);
            if (MascotParts.IsToon(k)) return ToonEye(g, x, y, D, blink, surprised);
            if (MascotParts.IsFigure(k)) return FigureEye(g, x, y, D, blink, surprised);
            float sk = surprised ? 1.15f : 1f;
            switch (k)
            {
                case MascotParts.KindPsyduck:
                    // Big blank round eyes with a tiny dot: always a little lost.
                    WhiteEye(g, x, y, 0.125f * D * sk, 0.12f * D * sk * blink, 0.014f * D, 0, 0, D);
                    return true;
                case MascotParts.KindMeowth:
                {
                    // White ovals with a thin black slit.
                    float w = 0.1f * D * sk, h = 0.13f * D * sk * blink;
                    if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return true; }
                    MascotParts.Ellipse(g, Color.White, x - w / 2, y - h / 2, w, h);
                    using (Pen p = new Pen(Color.FromArgb(200, InkBlack), Math.Max(1f, D * 0.008f))) g.DrawEllipse(p, x - w / 2, y - h / 2, w, h);
                    MascotParts.Ellipse(g, InkBlack, x - w * 0.12f, y - h * 0.38f, w * 0.24f, h * 0.76f);
                    return true;
                }
                case MascotParts.KindMimikyu:
                    // Painted on the cloth: plain black circles that never blink.
                    MascotParts.Ellipse(g, InkBlack, x - 0.035f * D, y - 0.04f * D, 0.07f * D, 0.08f * D);
                    return true;
                case MascotParts.KindDitto:
                case MascotParts.KindRilakkuma:
                case MascotParts.KindPusheen:
                case MascotParts.KindPochacco:
                {
                    float w = (k == MascotParts.KindDitto ? 0.03f : 0.045f) * D * sk, h = (k == MascotParts.KindDitto ? 0.034f : 0.05f) * D * sk * blink;
                    if (h < D * 0.015f) { ClosedEye(g, x, y, w * 1.4f, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    if (k != MascotParts.KindDitto) MascotParts.Ellipse(g, Color.FromArgb(220, 255, 255, 255), x - w * 0.3f, y - h * 0.32f, w * 0.32f, h * 0.3f);
                    return true;
                }
                case MascotParts.KindNoFace:
                {
                    float w = 0.056f * D, h = 0.032f * D * blink;
                    if (h < D * 0.01f) { ClosedEye(g, x, y, w, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    return true;
                }
                case MascotParts.KindCalcifer:
                    WhiteEye(g, x, y, 0.12f * D * sk, 0.14f * D * sk * blink, 0.032f * D, 0, 0.01f * D, D);
                    return true;
                case MascotParts.KindKodama:
                {
                    // Empty dark hollows.
                    float w = 0.075f * D * sk, h = 0.085f * D * sk * Math.Max(0.3f, blink);
                    MascotParts.Ellipse(g, Color.FromArgb(235, 34, 34, 32), x - w / 2, y - h / 2, w, h);
                    return true;
                }
                case MascotParts.KindYoshi:
                {
                    // Tall white eyes on top of the snout, the pupils low and inward.
                    float w = 0.11f * D * sk, h = 0.17f * D * sk * blink, yy = y - 0.02f * D;
                    if (h < D * 0.02f) { ClosedEye(g, x, yy, w, D); return true; }
                    MascotParts.Ellipse(g, Color.White, x - w / 2, yy - h / 2, w, h);
                    using (Pen p = new Pen(Color.FromArgb(150, 30, 60, 30), Math.Max(1f, D * 0.008f))) g.DrawEllipse(p, x - w / 2, yy - h / 2, w, h);
                    float pw = w * 0.42f, ph = h * 0.42f, px = x + (x < 0 ? 1 : -1) * w * 0.14f, py = yy + h * 0.12f;
                    MascotParts.Ellipse(g, InkBlack, px - pw / 2, py - ph / 2, pw, ph);
                    MascotParts.Ellipse(g, Color.White, px - pw * 0.3f, py - ph * 0.32f, pw * 0.36f, ph * 0.3f);
                    return true;
                }
                case MascotParts.KindDqSlime:
                    WhiteEye(g, x, y, 0.11f * D * sk, 0.11f * D * sk * blink, 0.028f * D, 0, 0.012f * D, D);
                    return true;
                case MascotParts.KindSnoopy:
                {
                    // A small black dash.
                    float w = 0.03f * D, h = 0.062f * D * sk * blink;
                    if (h < D * 0.015f) { ClosedEye(g, x, y, 0.05f * D, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    return true;
                }
                case MascotParts.KindKitty:
                {
                    float w = 0.05f * D * sk, h = 0.07f * D * sk * blink;
                    if (h < D * 0.015f) { ClosedEye(g, x, y, 0.06f * D, D); return true; }
                    MascotParts.Ellipse(g, InkBlack, x - w / 2, y - h / 2, w, h);
                    return true;
                }
                default:
                    return false;
            }
        }

        // A white eye with a round black pupil (offset by dx, dy) and a little shine.
        void WhiteEye(Graphics g, float x, float y, float w, float h, float r, float dx, float dy, float D)
        {
            if (h < D * 0.02f) { ClosedEye(g, x, y, w, D); return; }
            MascotParts.Ellipse(g, Color.White, x - w / 2, y - h / 2, w, h);
            using (Pen p = new Pen(Color.FromArgb(170, InkBlack), Math.Max(1f, D * 0.008f))) g.DrawEllipse(p, x - w / 2, y - h / 2, w, h);
            MascotParts.Ellipse(g, InkBlack, x + dx - r, y + dy - r, r * 2, r * 2);
            if (r > D * 0.025f) MascotParts.Ellipse(g, Color.White, x + dx - r * 0.55f, y + dy - r * 0.6f, r * 0.6f, r * 0.6f);
        }

        void ClosedEye(Graphics g, float x, float y, float w, float D)
        {
            MascotParts.Line(g, Color.FromArgb(230, InkBlack), Math.Max(1.2f, D * 0.016f), x - w * 0.5f, y, x + w * 0.5f, y);
        }

        // Mouths of their own; true when drawn (or when the character has none, like Hello Kitty).
        bool ExtraMouth(Graphics g, float D, float mx, float fy, Color ink, float lw, bool talking)
        {
            int k = Look.Kind;
            if (!MascotParts.IsExtra(k)) return false;
            if (MascotParts.IsArcade(k)) return ArcadeMouth(g, D, mx, fy, ink, lw, talking);
            if (MascotParts.IsKlei(k)) return KleiMouth(g, D, mx, fy, talking);
            if (MascotParts.IsToon(k)) return ToonMouth(g, D, mx, fy, talking);
            if (MascotParts.IsFigure(k)) return FigureMouth(g, D, mx, fy, talking);
            bool open = talking || mood == Mood.Happy || mood == Mood.Love;
            switch (k)
            {
                case MascotParts.KindKitty:
                case MascotParts.KindPsyduck:
                case MascotParts.KindPiplup:
                    return true;   // no mouth, or the beak stands for it
                case MascotParts.KindMimikyu:
                {
                    // A zigzag painted on.
                    float my = fy + 0.09f * D, w = 0.15f * D;
                    PointF[] z = new PointF[7];
                    for (int i = 0; i < 7; i++) z[i] = new PointF(mx - w / 2 + w * i / 6f, my + (i % 2 == 0 ? 0 : 0.025f * D));
                    using (Pen p = new Pen(InkBlack, Math.Max(1.2f, D * 0.014f))) { p.LineJoin = LineJoin.Round; g.DrawLines(p, z); }
                    return true;
                }
                case MascotParts.KindDitto:
                    ArcMouth(g, mx, fy + 0.08f * D, 0.17f * D, 0.05f * D, InkBlack, Math.Max(1f, D * 0.01f));
                    return true;
                case MascotParts.KindNoFace:
                    ArcMouth(g, mx, fy + 0.13f * D, 0.05f * D, 0.02f * D, Color.FromArgb(160, 60, 56, 64), Math.Max(1f, D * 0.009f));
                    return true;
                case MascotParts.KindKodama:
                {
                    float w = 0.045f * D, h = (open ? 0.06f : 0.045f) * D;
                    MascotParts.Ellipse(g, Color.FromArgb(235, 34, 34, 32), mx - w / 2, fy + 0.1f * D, w, h);
                    return true;
                }
                case MascotParts.KindCalcifer:
                case MascotParts.KindDqSlime:
                {
                    // A wide open grin with a tongue.
                    bool slime = k == MascotParts.KindDqSlime;
                    float my = fy + (slime ? 0.09f : 0.1f) * D, w = (slime ? 0.22f : 0.2f) * D, h = (open ? 0.12f : 0.09f) * D;
                    if (talking && mood == Mood.Idle) h *= (float)(0.6 + 0.4 * Math.Abs(Math.Sin(now / 110.0)));
                    using (GraphicsPath m = new GraphicsPath())
                    {
                        m.AddArc(mx - w / 2, my - h / 2, w, h, 0, 180);
                        m.CloseFigure();
                        MascotParts.FillSolid(g, m, slime ? Color.FromArgb(150, 30, 44) : Color.FromArgb(120, 24, 12));
                        Region old = g.Clip;
                        g.SetClip(m, CombineMode.Intersect);
                        MascotParts.Ellipse(g, slime ? Color.FromArgb(255, 120, 140) : Color.FromArgb(255, 150, 60), mx - w * 0.25f, my + h * 0.12f, w * 0.5f, h * 0.6f);
                        g.Clip = old;
                        old.Dispose();
                        MascotParts.Stroke(g, m, Color.FromArgb(200, InkBlack), Math.Max(1f, D * 0.008f));
                    }
                    return true;
                }
                case MascotParts.KindYoshi:
                    if (open) OpenMouth(g, mx, fy + 0.29f * D, 0.2f * D, 0.08f * D, D);
                    else ArcMouth(g, mx, fy + 0.25f * D, 0.22f * D, 0.06f * D, ink, lw);
                    return true;
                case MascotParts.KindSnoopy:
                case MascotParts.KindPochacco:
                case MascotParts.KindRilakkuma:
                case MascotParts.KindLucario:
                {
                    float my = fy + (k == MascotParts.KindSnoopy ? 0.13f : 0.11f) * D;
                    if (open) OpenMouth(g, mx, my, 0.08f * D, 0.05f * D, D);
                    else ArcMouth(g, mx, my, 0.08f * D, 0.035f * D, ink, lw);
                    return true;
                }
                case MascotParts.KindMeowth:
                {
                    // A cat mouth with one little fang showing; open, it shows both.
                    float my = fy + 0.085f * D, s = 0.026f * D;
                    if (open)
                    {
                        float w = 0.12f * D, h = 0.075f * D;
                        if (talking && mood == Mood.Idle) h *= (float)(0.5 + 0.5 * Math.Abs(Math.Sin(now / 110.0)));
                        OpenMouth(g, mx, my, w, h, D);
                        MascotParts.Fang(g, mx - w * 0.25f, my, D);
                        MascotParts.Fang(g, mx + w * 0.25f, my, D);
                        return true;
                    }
                    using (Pen p = new Pen(ink, lw))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, mx - s * 2, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                        g.DrawArc(p, mx, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                    }
                    MascotParts.Fang(g, mx + s * 1.05f, my + s * 0.66f, D);
                    return true;
                }
                case MascotParts.KindPusheen:
                {
                    // A tiny cat "w".
                    float my = fy + 0.07f * D, s = 0.018f * D;
                    using (Pen p = new Pen(InkBlack, Math.Max(1f, D * 0.011f)))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, mx - s * 2, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                        g.DrawArc(p, mx, my - s * 0.7f, s * 2, s * 1.4f, 10, 160);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        static void ArcMouth(Graphics g, float mx, float my, float w, float h, Color c, float lw)
        {
            using (Pen p = new Pen(c, lw))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawArc(p, mx - w / 2, my - h, w, h * 2, 20, 140);
            }
        }

        static void OpenMouth(Graphics g, float mx, float my, float w, float h, float D)
        {
            using (GraphicsPath m = new GraphicsPath())
            {
                m.AddArc(mx - w / 2, my - h / 2, w, h, 0, 180);
                m.CloseFigure();
                MascotParts.FillSolid(g, m, Color.FromArgb(90, 30, 60));
                Region old = g.Clip;
                g.SetClip(m, CombineMode.Intersect);
                MascotParts.Ellipse(g, Color.FromArgb(255, 120, 150), mx - w * 0.3f, my + h * 0.1f, w * 0.6f, h * 0.6f);
                g.Clip = old;
                old.Dispose();
            }
        }
    }
}

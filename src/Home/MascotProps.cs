// Stackshot - Props for the mascot's idle activities and sleep variants, drawn in the mascot's own soft style.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    // Everything is drawn in units of D (the body center is the origin) with a handful of cheap shapes, so an activity
    // costs about as much as the mascot itself.
    public static class MascotProps
    {
        public const int None = 0, Coffee = 1, Juggle = 2, Console = 3, Stretch = 4, Read = 5, Photo = 6;
        // Healthy breaks: a glass of water, looking far away (20 s), squats and a slow breathing exercise (6 breaths).
        public const int Water = 7, EyeRest = 8, Legs = 9, Breathe = 10;
        public const int Count = 10;

        public const double SquatMs = 1700, BreathMs = 10000;

        public static double Duration(int act)
        {
            switch (act)
            {
                case Coffee: return 8400;
                case Juggle: return 7000;
                case Console: return 9000;
                case Stretch: return 3600;
                case Read: return 9500;
                case Water: return 7000;
                case EyeRest: return 20600;
                case Legs: return SquatMs * 8 + 400;
                case Breathe: return BreathMs * 6 + 400;
                default: return 6200;
            }
        }

        // 0..1: how far down a squat is.
        public static double SquatAmt(double t)
        {
            double p = (Math.Max(0, t - 200) % SquatMs) / SquatMs;
            double s = Math.Sin(p * Math.PI);
            return s * s;
        }

        // 0..1 over one breath: in for 4 s, out for 6 s.
        public static double BreathAmt(double t)
        {
            double u = Math.Max(0, t - 200) % BreathMs;
            return u < 4000 ? 0.5 - 0.5 * Math.Cos(u / 4000 * Math.PI) : 0.5 + 0.5 * Math.Cos((u - 4000) / 6000 * Math.PI);
        }

        public static bool Inhaling(double t) { return (Math.Max(0, t - 200) % BreathMs) < 4000; }

        // 0..1 bump over a stretch.
        public static double StretchAmt(double t)
        {
            double p = Math.Max(0, Math.Min(1, (t - 300) / 2600.0));
            return Math.Pow(Math.Sin(p * Math.PI), 0.5);
        }

        static readonly Color Cream = Color.FromArgb(250, 246, 238);
        static readonly Color PageEdge = Color.FromArgb(214, 206, 190);

        static void Fill(Graphics g, Color c, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath p = Theme.Round(new RectangleF(x, y, w, h), r))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        static void Grad(Graphics g, Color a, Color b, float x, float y, float w, float h, float r)
        {
            RectangleF rc = new RectangleF(x, y, w, h);
            using (GraphicsPath p = Theme.Round(rc, r))
            using (LinearGradientBrush br = new LinearGradientBrush(RectangleF.Inflate(rc, 0.01f, 0.01f), a, b, 90f)) g.FillPath(br, p);
        }

        static void Seg(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
        {
            using (Pen p = new Pen(c, w)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, x1, y1, x2, y2); }
        }

        static void Dot(Graphics g, Color c, float x, float y, float r)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }

        static void Hand(Graphics g, Color c1, Color c2, float x, float y)
        {
            if (noHands) return;
            if (bodyM != null) { DrawnHand(g, x, y); return; }
            Color c = Mac.Mix(c1, c2, 0.3);
            Dot(g, c, x, y, 0.05f);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 0.007f)) g.DrawEllipse(p, x - 0.05f, y - 0.05f, 0.1f, 0.1f);
        }

        // Wilson and Willow have drawn arms: while they hold something, each hand gets its arm from the shoulder and is
        // drawn small and inked, as their game draws them (MascotKlei). Set for the length of one Paint; bodyM is the
        // drawing in body units, before the prop's fade-in scale.
        [ThreadStatic] static Matrix bodyM;
        [ThreadStatic] static int armKind;
        [ThreadStatic] static float armD;
        [ThreadStatic] static Color armSkin;
        [ThreadStatic] static bool armsDone;
        [ThreadStatic] static bool noHands;   // Abigail, a ghost, and Nevado, a little dog: what they use floats by them
        [ThreadStatic] static float reach;    // how far up a stretch goes
        [ThreadStatic] static float spread;   // and how far out
        [ThreadStatic] static Color handTint; // the colour of its hands
        [ThreadStatic] static float mouthX, faceX;   // where its mouth and face are across, for a face drawn three quarters on

        // The colour of a character's own hand, taken once from its drawing.
        static readonly System.Collections.Generic.Dictionary<int, Color> handTints = new System.Collections.Generic.Dictionary<int, Color>();

        static Color HandTint(int kind, Color c1, Color c2)
        {
            if (!MascotParts.HasDrawnArms(kind)) return Mac.Mix(c1, c2, 0.3);
            lock (handTints)
            {
                Color c;
                if (handTints.TryGetValue(kind, out c)) return c;
                using (Bitmap b = new Bitmap(64, 64))
                using (Graphics hg = Graphics.FromImage(b))
                {
                    hg.SmoothingMode = SmoothingMode.AntiAlias;
                    hg.TranslateTransform(32, 32);
                    MascotParts.StarveHand(hg, 400, kind, c1);
                    // The middle of the hand, a little below its shine.
                    c = b.GetPixel(32, 34);
                }
                c = c.A < 200 ? Mac.Mix(c1, c2, 0.3) : Color.FromArgb(255, c);
                handTints[kind] = c;
                return c;
            }
        }

        // A resting hand: out at the side, closer in for the ones with arms.
        static float RestX { get { return bodyM != null ? 0.2f : 0.3f; } }

        // Where a point of the current drawing falls in body units.
        static PointF ToBody(Graphics g, float x, float y)
        {
            PointF[] pt = { new PointF(x, y) };
            using (Matrix m = g.Transform) m.TransformPoints(pt);
            using (Matrix inv = bodyM.Clone()) { inv.Invert(); inv.TransformPoints(pt); }
            return pt[0];
        }

        // The arm from its shoulder to a hand at (x, y) of the current drawing.
        static void DrawnArm(Graphics g, float x, float y)
        {
            PointF h = ToBody(g, x, y);
            GraphicsState st = g.Save();
            g.Transform = bodyM;
            g.ScaleTransform(1 / armD, 1 / armD);
            MascotParts.StarveArm(g, armD, armKind, h.X < 0 ? -1 : 1, h, armSkin);
            g.Restore(st);
        }

        static void DrawnHand(Graphics g, float x, float y)
        {
            if (!armsDone) DrawnArm(g, x, y);
            PointF h = ToBody(g, x, y);
            GraphicsState st = g.Save();
            g.Transform = bodyM;
            g.ScaleTransform(1 / armD, 1 / armD);
            g.TranslateTransform(h.X * armD, h.Y * armD);
            MascotParts.StarveHand(g, armD, armKind, armSkin);
            g.Restore(st);
        }

        // Arms that pass behind the prop (a console, a book, a camera) go on before it, the hands after.
        static void ArmsFirst(Graphics g, float x1, float y1, float x2, float y2)
        {
            if (bodyM == null) return;
            DrawnArm(g, x1, y1);
            DrawnArm(g, x2, y2);
            armsDone = true;
        }

        static Color A(Color c, double k) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * k))), c); }

        // t: ms since the activity began; k: 0..1 fade in and out.
        public static void Paint(Graphics g, int act, float D, double t, double k, Color c1, Color c2)
        {
            Paint(g, act, D, t, k, c1, c2, -0.05f);
        }

        // faceY: the height of the eyes (Geo.FaceY), so what goes to the mouth finds it on every species.
        public static void Paint(Graphics g, int act, float D, double t, double k, Color c1, Color c2, float faceY)
        {
            Paint(g, act, D, t, k, c1, c2, faceY, faceY + 0.085f, -1);
        }

        // mouthY: where the mouth is, for what goes to it; kind: the character, for the ones drawn with arms.
        public static void Paint(Graphics g, int act, float D, double t, double k, Color c1, Color c2, float faceY, float mouthY, int kind)
        {
            if (act == None || k <= 0.01) return;
            GraphicsState st = g.Save();
            g.ScaleTransform(D, D);
            // Without hands of their own (Abigail, Nevado, Eevee on its four legs, Ditto...): what they use floats by them.
            noHands = kind >= 0 && MascotParts.NoHands(kind);
            if (MascotParts.HasDrawnArms(kind) && !noHands) { bodyM = g.Transform; armKind = kind; armD = D; armSkin = c1; armsDone = false; }
            handTint = kind >= 0 && !noHands ? HandTint(kind, c1, c2) : Mac.Mix(c1, c2, 0.3);
            mouthX = kind >= 0 ? MascotParts.MouthX(kind) : 0;
            faceX = kind >= 0 ? MascotParts.EyeX(kind) : 0;
            // Finn stands taller and slimmer than the rest: what he holds is a little smaller and up at his hands, still
            // reaching his mouth and eyes, and he stretches up past his hat. The little ones (the Pokemon, Baby Groot)
            // only get their short arms up beside their heads.
            reach = 0.74f;
            spread = 0.3f;
            if (MascotParts.ShortArms(kind)) reach = 0.48f;
            if (kind >= 0 && MascotParts.IsSoftFig(kind)) { reach = 0.34f; spread = 0.2f; }
            else if (bodyM == null) { reach = 0.5f; spread = 0.33f; }   // the round ones' stubby arms, not long poles
            if (kind == MascotParts.KindFinn)
            {
                const float s = 0.8f, dy = -0.14f;
                g.TranslateTransform(0, dy);
                g.ScaleTransform(s, s);
                faceY = (faceY - dy) / s;
                mouthY = (mouthY - dy) / s;
                reach = 1.02f;
            }
            float e = (float)(0.6 + 0.4 * k);
            g.TranslateTransform(0, 0.2f);
            g.ScaleTransform(e, e);
            g.TranslateTransform(0, -0.2f);
            try
            {
                switch (act)
                {
                    case Coffee: PaintCoffee(g, t, k, c1, c2, mouthY); break;
                    case Juggle: PaintJuggle(g, t, c1, c2); break;
                    case Console:
                    case Read:
                    case Photo:
                    {
                        // Held out in front: a character drawn side on holds it ahead of its chest, not across it.
                        float ahead = kind >= 0 ? MascotParts.HoldAhead(kind) : 0;
                        if (ahead != 0) g.TranslateTransform(ahead, 0);
                        if (act == Console) PaintConsole(g, t, c1, c2);
                        else if (act == Read) PaintRead(g, t, c1, c2);
                        else PaintPhoto(g, t, c1, c2);
                        break;
                    }
                    case Stretch: PaintStretch(g, t, c1, c2); break;
                    case Water: PaintWater(g, t, k, c1, c2, mouthY); break;
                    case EyeRest: PaintEyeRest(g, t, c1, c2, faceY); break;
                    case Legs: PaintLegs(g, t, c1, c2); break;
                    case Breathe: PaintBreathe(g, t, k, c1, c2); break;
                }
            }
            finally
            {
                if (bodyM != null) { bodyM.Dispose(); bodyM = null; }
                noHands = false;
            }
            g.Restore(st);
        }

        static readonly Color GlassEdge = Color.FromArgb(200, 225, 240, 255), Water1 = Color.FromArgb(200, 120, 200, 255), Water2 = Color.FromArgb(220, 40, 130, 230);

        // A glass of water raised to the mouth in three sips; the level goes down with each one. Held from the outside, so
        // the arm never crosses it; tilted, the near corner of its rim meets the mouth.
        static void PaintWater(Graphics g, double t, double k, Color c1, Color c2, float mouthY)
        {
            double cyc = (t % 2300) / 2300.0;
            float lift = (float)Math.Max(0, Math.Min(1, (0.5 - 0.5 * Math.Cos(cyc * Math.PI * 2)) * 1.7));
            float toX = mouthX + 0.115f, toY = mouthY + 0.063f;
            float cx = 0.27f + (toX - 0.27f) * lift, cy = 0.19f + (toY - 0.19f) * lift;
            float level = (float)(0.85 - 0.6 * Math.Min(1, t / Duration(Water)));
            Hand(g, c1, c2, -RestX, 0.22f);
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-22 * lift);
            g.ScaleTransform(1.35f, 1.35f);
            // The hand behind the glass (seen through it and showing past its side), the glass in front on its way to the
            // mouth.
            Hand(g, c1, c2, 0.06f, 0.032f);
            PointF[] glass = { new PointF(-0.048f, -0.07f), new PointF(0.048f, -0.07f), new PointF(0.038f, 0.07f), new PointF(-0.038f, 0.07f) };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 230, 245, 255))) g.FillPolygon(b, glass);
            // Water up to the level, staying level while the glass tilts a little.
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddPolygon(glass);
                Region old = g.Clip;
                g.SetClip(gp, CombineMode.Intersect);
                float top = 0.07f - 0.14f * level;
                g.RotateTransform(22 * lift);
                RectangleF w = new RectangleF(-0.12f, top, 0.24f, 0.2f);
                using (LinearGradientBrush br = new LinearGradientBrush(RectangleF.Inflate(w, 0.01f, 0.01f), Water1, Water2, 90f)) g.FillRectangle(br, w);
                Seg(g, Color.FromArgb(160, 255, 255, 255), 0.006f, -0.1f, top, 0.1f, top);
                g.RotateTransform(-22 * lift);
                g.Clip = old;
                old.Dispose();
            }
            using (Pen p = new Pen(GlassEdge, 0.007f)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, glass); }
            Seg(g, Color.FromArgb(150, 255, 255, 255), 0.008f, -0.03f, -0.05f, -0.024f, 0.04f);
            g.Restore(st);
            // A tiny drop now and then, between sips.
            double d = (t % 2300) / 2300.0;
            if (d > 0.62 && d < 0.95)
            {
                float u = (float)((d - 0.62) / 0.33);
                Dot(g, A(Water1, k * (1 - u)), 0.2f + 0.02f * u, 0.0f + 0.12f * u, 0.014f);
            }
        }

        // Shading the eyes with one hand and looking far away; the other hand rests.
        static void PaintEyeRest(Graphics g, double t, Color c1, Color c2, float faceY)
        {
            Hand(g, c1, c2, -RestX - 0.01f, 0.22f);
            Color arm = Mac.Mix(c1, c2, 0.25);
            float sway = (float)Math.Sin(t / 1400.0) * 0.008f;
            float y = faceY - 0.1f + sway, x = faceX + 0.1f;
            if (!noHands)
            {
                if (bodyM != null) DrawnArm(g, x + 0.13f, y + 0.015f);
                else Seg(g, arm, 0.07f, 0.27f, 0.08f, x + 0.15f, y + 0.02f);
                FlatHand(g, handTint, x, y, -8, reach < 0.4f ? 0.2f : 0.25f);
            }
            // Little sparkles where it is looking: far away, out to the side.
            for (int i = 0; i < 3; i++)
            {
                double u = ((t / 1800.0) + i / 3.0) % 1;
                float a = (float)Math.Sin(u * Math.PI);
                Dot(g, Color.FromArgb((int)(150 * a), 255, 255, 255), -0.44f - i * 0.05f, -0.28f + i * 0.07f, 0.012f + 0.01f * a);
            }
        }

        // A hand held flat over the eyes, palm down, its fingers together pointing across the brow: from the front, the
        // back of the hand with the fingertips round at its far end, the thumb tucked under at the wrist. In the
        // character's own colour, inked like it.
        static void FlatHand(Graphics g, Color c, float x, float y, float rot, float len)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(rot);
            g.ScaleTransform(len, len);
            PointF[] pts = { new PointF(-0.5f, 0.0f), new PointF(-0.47f, -0.1f), new PointF(-0.38f, -0.14f), new PointF(0.18f, -0.16f), new PointF(0.42f, -0.12f),
                             new PointF(0.5f, -0.02f), new PointF(0.47f, 0.1f), new PointF(0.32f, 0.21f), new PointF(0.17f, 0.21f), new PointF(0.1f, 0.13f),
                             new PointF(-0.38f, 0.13f), new PointF(-0.47f, 0.1f) };
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddClosedCurve(pts, 0.5f);
                RectangleF r = p.GetBounds();
                using (Pen ink = new Pen(Color.FromArgb(30, 26, 34), 0.05f)) { ink.LineJoin = LineJoin.Round; g.DrawPath(ink, p); }
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(r, 0.01f, 0.01f), MascotParts.Lighter(c, 0.14), MascotParts.Darker(c, 0.12), 90f)) g.FillPath(b, p);
                // The gaps between its fingers, and a shine along its back.
                using (Pen gap = new Pen(Color.FromArgb(110, MascotParts.Darker(c, 0.45)), 0.022f))
                {
                    gap.StartCap = gap.EndCap = LineCap.Round;
                    g.DrawLine(gap, -0.47f, -0.035f, -0.26f, -0.045f);
                    g.DrawLine(gap, -0.47f, 0.045f, -0.26f, 0.04f);
                }
                Seg(g, Color.FromArgb(120, 255, 255, 255), 0.035f, -0.3f, -0.09f, 0.25f, -0.1f);
            }
            g.Restore(st);
        }

        // Squats with the arms out in front.
        static void PaintLegs(Graphics g, double t, Color c1, Color c2)
        {
            float d = (float)SquatAmt(t);
            Color arm = Mac.Mix(c1, c2, 0.25);
            foreach (int s in new int[] { -1, 1 })
            {
                float hx = s * 0.17f, hy = 0.16f - 0.12f * d;
                if (bodyM == null && !noHands) Seg(g, arm, 0.07f, s * 0.27f, 0.1f, hx, hy);
                Hand(g, c1, c2, hx, hy);
            }
            // A drop of effort at the bottom of each squat.
            if (d > 0.7) Dot(g, Color.FromArgb((int)(170 * (d - 0.7) / 0.3), 170, 220, 255), 0.3f, -0.26f, 0.018f);
        }

        // Hands on the belly, and a soft ring that grows as it breathes in.
        static void PaintBreathe(Graphics g, double t, double k, Color c1, Color c2)
        {
            float b = (float)BreathAmt(t);
            float r = 0.48f + 0.1f * b;
            using (Pen p = new Pen(A(Mac.Mix(c1, Color.White, 0.5), 0.55 * k * (0.35 + 0.65 * b)), 0.012f))
                g.DrawEllipse(p, -r, -0.02f - r, r * 2, r * 2);
            Hand(g, c1, c2, -0.12f, 0.2f - 0.02f * b);
            Hand(g, c1, c2, 0.12f, 0.2f - 0.02f * b);
        }

        static void PaintCoffee(Graphics g, double t, double k, Color c1, Color c2, float mouthY)
        {
            double cyc = (t % 4200) / 4200.0;
            float lift = (float)Math.Max(0, Math.Min(1, (0.5 - 0.5 * Math.Cos(cyc * Math.PI * 2)) * 1.7));
            // Held by its handle, on the outside, so the arm never crosses the cup. Raised, the tilted cup's near rim meets
            // the mouth (wherever the face draws it), never the eyes: its top-left corner sits 0.104 left of and 0.035
            // above the cup's centre at full tilt.
            float toX = mouthX + 0.124f, toY = mouthY + 0.035f;
            float cx = 0.27f + (toX - 0.27f) * lift, cy = 0.19f + (toY - 0.19f) * lift;
            Hand(g, c1, c2, -RestX, 0.22f);
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-24 * lift);
            g.ScaleTransform(1.35f, 1.35f);
            // The hand behind the cup, gripping the handle and showing past it: the cup it brings to its mouth stays in front.
            Hand(g, c1, c2, 0.098f, 0.008f);
            Seg(g, Cream, 0.022f, 0.06f, -0.02f, 0.085f, 0.0f);
            Seg(g, Cream, 0.022f, 0.085f, 0.0f, 0.058f, 0.03f);
            Grad(g, Cream, Color.FromArgb(222, 214, 200), -0.06f, -0.055f, 0.12f, 0.11f, 0.028f);
            Fill(g, Color.FromArgb(120, 76, 52), -0.05f, -0.052f, 0.1f, 0.02f, 0.01f);
            Fill(g, Color.FromArgb(70, c2.R, c2.G, c2.B), -0.06f, 0.0f, 0.12f, 0.012f, 0.006f);
            g.Restore(st);
            for (int i = 0; i < 3; i++)
            {
                double u = ((t / 1900.0) + i / 3.0) % 1;
                float x = cx + (i - 1) * 0.035f + (float)Math.Sin(u * 7 + i) * 0.018f, y = cy - 0.08f - (float)u * 0.2f;
                // The steam fades while it drinks, so it never drifts across the eyes.
                Seg(g, A(Color.White, 0.55 * (1 - u) * k * (1 - lift)), 0.014f, x, y, x + (float)Math.Sin(u * 7 + i + 1) * 0.02f, y - 0.045f);
            }
        }

        static readonly Color[] Balls = { Color.FromArgb(255, 92, 140), Color.FromArgb(255, 214, 10), Color.FromArgb(100, 210, 255) };

        static void PaintJuggle(Graphics g, double t, Color c1, Color c2)
        {
            float dip = (float)Math.Sin(t / 1000.0 * Math.PI * 2) * 0.012f;
            Hand(g, c1, c2, -0.3f, 0.17f + dip);
            Hand(g, c1, c2, 0.3f, 0.17f - dip);
            for (int i = 0; i < 3; i++)
            {
                double T = t / 1000.0 + i / 3.0, u = T - Math.Floor(T);
                bool right = (((int)Math.Floor(T)) & 1) == 0;
                float x0 = right ? -0.3f : 0.3f, x1 = -x0;
                float x = x0 + (x1 - x0) * (float)u, y = 0.14f - 0.62f * (float)(4 * u * (1 - u));
                Dot(g, Balls[i], x, y, 0.05f);
                Dot(g, Color.FromArgb(150, 255, 255, 255), x - 0.016f, y - 0.016f, 0.014f);
            }
        }

        static void PaintConsole(Graphics g, double t, Color c1, Color c2)
        {
            float bob = (float)Math.Sin(t / 700.0) * 0.006f;
            g.TranslateTransform(0, bob);
            ArmsFirst(g, -0.2f, 0.285f, 0.2f, 0.285f);
            Color shell = Mac.Mix(c1, Color.White, 0.55);
            Grad(g, shell, Mac.Mix(shell, c2, 0.35), -0.2f, 0.1f, 0.4f, 0.21f, 0.05f);
            Fill(g, Color.FromArgb(24, 30, 46), -0.115f, 0.122f, 0.23f, 0.15f, 0.014f);
            // The game: a block jumping over scrolling obstacles.
            double u = (t / 1500.0) % 1;
            float ox = 0.085f - 0.19f * (float)u, jh = 0;
            if (ox > -0.06f && ox < 0.0f) jh = 0.055f * (float)Math.Sin(Math.PI * (0.0 - ox) / 0.06);
            Color lcd = Color.FromArgb(150, 232, 170);
            Fill(g, A(lcd, 0.5), -0.1f, 0.255f, 0.2f, 0.008f, 0.003f);
            Fill(g, lcd, -0.075f, 0.226f - jh, 0.03f, 0.03f, 0.006f);
            if (ox > -0.1f) Fill(g, lcd, ox, 0.232f, 0.018f, 0.024f, 0.003f);
            Fill(g, A(lcd, 0.8), -0.09f, 0.14f, 0.012f, 0.012f, 0.002f);
            // D-pad and buttons.
            Color btn = Color.FromArgb(60, 60, 80);
            Fill(g, btn, -0.175f, 0.2f, 0.05f, 0.014f, 0.005f);
            Fill(g, btn, -0.157f, 0.182f, 0.014f, 0.05f, 0.005f);
            bool press = (int)(t / 380) % 3 == 0;
            Dot(g, press ? Color.FromArgb(255, 92, 140) : Color.FromArgb(205, 70, 110), 0.16f, 0.195f, 0.016f);
            Dot(g, !press ? Color.FromArgb(255, 214, 10) : Color.FromArgb(205, 170, 10), 0.13f, 0.225f, 0.016f);
            Hand(g, c1, c2, -0.2f, 0.285f);
            Hand(g, c1, c2, 0.2f, 0.285f);
        }

        static void PaintStretch(Graphics g, double t, Color c1, Color c2)
        {
            float a = (float)StretchAmt(t);
            Color arm = Mac.Mix(c1, c2, 0.25);
            foreach (int s in new int[] { -1, 1 })
            {
                float hx = s * (spread + 0.06f * a), hy = 0.21f - reach * a;
                if (bodyM == null && !noHands) Seg(g, arm, 0.075f, s * 0.26f, 0.1f, hx, hy);
                Hand(g, c1, c2, hx, hy);
            }
        }

        static void PaintRead(Graphics g, double t, Color c1, Color c2)
        {
            ArmsFirst(g, -0.2f, 0.29f, 0.2f, 0.29f);
            Fill(g, MascotParts.Darker(c2, 0.35), -0.215f, 0.125f, 0.43f, 0.19f, 0.02f);
            PointF[] l = { new PointF(-0.2f, 0.12f), new PointF(-0.008f, 0.14f), new PointF(-0.008f, 0.3f), new PointF(-0.2f, 0.28f) };
            PointF[] r = { new PointF(0.008f, 0.14f), new PointF(0.2f, 0.12f), new PointF(0.2f, 0.28f), new PointF(0.008f, 0.3f) };
            using (SolidBrush b = new SolidBrush(Cream)) { g.FillPolygon(b, l); g.FillPolygon(b, r); }
            using (Pen p = new Pen(PageEdge, 0.006f)) { g.DrawPolygon(p, l); g.DrawPolygon(p, r); }
            for (int i = 0; i < 4; i++)
            {
                float y = 0.16f + i * 0.03f;
                Seg(g, Color.FromArgb(110, 90, 80, 110), 0.008f, -0.17f, y, -0.04f - (i == 3 ? 0.05f : 0), y + 0.004f);
                Seg(g, Color.FromArgb(110, 90, 80, 110), 0.008f, 0.04f, y + 0.004f, 0.17f - (i == 2 ? 0.06f : 0), y);
            }
            // A page turns over every few seconds.
            double ft = (t % 3400) / 520.0;
            if (ft < 1)
            {
                float x = 0.19f * (float)Math.Cos(ft * Math.PI), lift = 0.03f * (float)Math.Sin(ft * Math.PI);
                PointF[] f = { new PointF(0.008f, 0.14f), new PointF(x, 0.12f - lift), new PointF(x, 0.28f - lift), new PointF(0.008f, 0.3f) };
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 252, 244))) g.FillPolygon(b, f);
                using (Pen p = new Pen(PageEdge, 0.006f)) g.DrawPolygon(p, f);
            }
            Hand(g, c1, c2, -0.2f, 0.29f);
            Hand(g, c1, c2, 0.2f, 0.29f);
        }

        static void PaintPhoto(Graphics g, double t, Color c1, Color c2)
        {
            double cyc = (t % 2600) / 2600.0;
            float shake = cyc > 0.7 && cyc < 0.78 ? 0.006f : 0;
            g.TranslateTransform(shake, 0.04f);
            ArmsFirst(g, -0.115f, 0.06f, 0.115f, 0.06f);
            Grad(g, Color.FromArgb(86, 90, 108), Color.FromArgb(48, 50, 66), -0.1f, -0.03f, 0.2f, 0.12f, 0.025f);
            Fill(g, Color.FromArgb(205, 208, 220), -0.1f, -0.03f, 0.2f, 0.022f, 0.011f);
            Fill(g, Color.FromArgb(255, 238, 160), 0.05f, -0.045f, 0.04f, 0.022f, 0.006f);
            Dot(g, Color.FromArgb(20, 22, 34), 0.0f, 0.03f, 0.047f);
            Dot(g, Color.FromArgb(52, 78, 140), 0.0f, 0.03f, 0.033f);
            Dot(g, Color.FromArgb(190, 255, 255, 255), -0.012f, 0.018f, 0.009f);
            Hand(g, c1, c2, -0.115f, 0.06f);
            Hand(g, c1, c2, 0.115f, 0.06f);
            double fl = (cyc - 0.72) / 0.14;
            if (fl > 0 && fl < 1)
            {
                double a = 1 - fl;
                for (int i = 0; i < 3; i++)
                {
                    float r = (0.06f + 0.16f * (float)fl) * (1 - i * 0.28f);
                    Dot(g, Color.FromArgb((int)(150 * a * (0.5 + i * 0.25)), 255, 255, 235), 0.07f, -0.035f, r);
                }
            }
        }

        // Sleep variants (the owner picks one each time it falls asleep): 1 blanket, 2 snore bubble, 3 nodding off, 4 both.
        public static void PaintSleep(Graphics g, int variant, float D, double now, Color c1, Color c2)
        {
            PaintSleep(g, variant, D, now, c1, c2, 0.045f);
        }

        // Cosy quilt colours; each sleeper gets the one furthest from its own colour, so the blanket always reads.
        static readonly Color[] Quilts = { Color.FromArgb(70, 110, 196), Color.FromArgb(206, 76, 78), Color.FromArgb(72, 156, 116), Color.FromArgb(138, 96, 196), Color.FromArgb(226, 164, 58) };

        // A quilt pulled up to its chest: arched over it, falling to a soft wavy hem, the sheet's cuff folded over its top,
        // quilted in diamonds with stitching, shaded towards its folds. y: its top, in units of D (g is scaled by D).
        static void PaintBlanket(Graphics g, float y, Color body)
        {
            Color q = Quilts[0];
            int far = -1;
            foreach (Color c in Quilts)
            {
                int d = Math.Abs(c.R - body.R) + Math.Abs(c.G - body.G) + Math.Abs(c.B - body.B);
                if (d > far) { far = d; q = c; }
            }
            Color light = MascotParts.Lighter(q, 0.18), dark = MascotParts.Darker(q, 0.28), ink = MascotParts.Darker(q, 0.55);
            PointF[] top = { new PointF(-0.355f, y + 0.065f), new PointF(-0.2f, y + 0.012f), new PointF(0, y - 0.012f), new PointF(0.2f, y + 0.012f), new PointF(0.355f, y + 0.065f) };
            System.Collections.Generic.List<PointF> edge = new System.Collections.Generic.List<PointF>(top);
            edge.AddRange(new PointF[] { new PointF(0.39f, y + 0.15f), new PointF(0.385f, y + 0.25f) });
            for (int i = 0; i <= 8; i++) edge.Add(new PointF(0.33f - i * 0.0825f, y + (i % 2 == 0 ? 0.287f : 0.268f)));
            edge.AddRange(new PointF[] { new PointF(-0.385f, y + 0.25f), new PointF(-0.39f, y + 0.15f) });
            RectangleF box = new RectangleF(-0.4f, y - 0.03f, 0.8f, 0.33f);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddClosedCurve(edge.ToArray(), 0.45f);
                using (LinearGradientBrush br = new LinearGradientBrush(box, light, dark, 90f)) g.FillPath(br, p);
                Region old = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                // Quilted diamonds: a dark seam with a lit edge beside it, and the soft shade of the folds at its sides.
                using (Pen seam = new Pen(Color.FromArgb(80, ink), 0.006f))
                using (Pen lit = new Pen(Color.FromArgb(60, 255, 255, 255), 0.004f))
                    for (float x = -0.62f; x < 0.62f; x += 0.085f)
                    {
                        g.DrawLine(seam, x, y + 0.04f, x + 0.3f, y + 0.34f);
                        g.DrawLine(seam, x + 0.3f, y + 0.04f, x, y + 0.34f);
                        g.DrawLine(lit, x + 0.008f, y + 0.04f, x + 0.308f, y + 0.34f);
                    }
                foreach (int s in new int[] { -1, 1 })
                    using (GraphicsPath fold = new GraphicsPath())
                    {
                        fold.AddEllipse(s * 0.27f - 0.05f, y + 0.06f, 0.1f, 0.3f);
                        using (PathGradientBrush fb = new PathGradientBrush(fold) { CenterColor = Color.FromArgb(55, ink), SurroundColors = new Color[] { Color.FromArgb(0, ink) } }) g.FillPath(fb, fold);
                    }
                // The sheet's cuff folded over the top, with its shadow on the quilt and a row of stitches.
                PointF[] low = new PointF[top.Length];
                for (int i = 0; i < top.Length; i++) low[top.Length - 1 - i] = new PointF(top[i].X * 1.04f, top[i].Y + 0.062f);
                using (GraphicsPath cuff = new GraphicsPath())
                {
                    cuff.AddCurve(top, 0.45f);
                    cuff.AddLine(top[top.Length - 1], low[0]);
                    cuff.AddCurve(low, 0.45f);
                    cuff.CloseFigure();
                    using (Pen shade = new Pen(Color.FromArgb(70, ink), 0.02f)) g.DrawCurve(shade, Shift(low, 0.012f), 0.45f);
                    using (LinearGradientBrush cb = new LinearGradientBrush(box, Color.FromArgb(252, 248, 238), Color.FromArgb(222, 214, 200), 90f)) g.FillPath(cb, cuff);
                    using (Pen st = new Pen(Color.FromArgb(110, q), 0.005f) { DashPattern = new float[] { 2.2f, 1.6f } }) g.DrawCurve(st, Shift(low, -0.014f), 0.45f);
                    using (Pen cuffEdge = new Pen(Color.FromArgb(90, 150, 140, 125), 0.005f)) g.DrawCurve(cuffEdge, low, 0.45f);
                }
                g.Clip = old;
                old.Dispose();
                using (Pen outline = new Pen(Color.FromArgb(150, ink), 0.009f) { LineJoin = LineJoin.Round }) g.DrawPath(outline, p);
            }
        }

        static PointF[] Shift(PointF[] pts, float dy)
        {
            PointF[] o = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++) o[i] = new PointF(pts[i].X, pts[i].Y + dy);
            return o;
        }

        // bubbleY: the height the snore bubble grows at, by the nose.
        public static void PaintSleep(Graphics g, int variant, float D, double now, Color c1, Color c2, float bubbleY)
        {
            bool blanket = variant == 1 || variant == 4, bubble = variant == 2 || variant == 4;
            if (!blanket && !bubble) return;
            GraphicsState st = g.Save();
            g.ScaleTransform(D, D);
            if (blanket) PaintBlanket(g, 0.13f + (float)Math.Sin(now / 900.0) * 0.006f, c1);
            if (bubble)
            {
                double cyc = (now % 4200) / 4200.0;
                float r = cyc < 0.8 ? 0.012f + 0.05f * (float)(0.5 - 0.5 * Math.Cos(cyc / 0.8 * Math.PI)) : 0.012f;
                float x = 0.075f + r * 0.5f, y = bubbleY;
                Dot(g, Color.FromArgb(70, 200, 225, 255), x, y, r);
                using (Pen pn = new Pen(Color.FromArgb(150, 235, 245, 255), 0.007f)) g.DrawEllipse(pn, x - r, y - r, r * 2, r * 2);
                Dot(g, Color.FromArgb(170, 255, 255, 255), x - r * 0.35f, y - r * 0.35f, r * 0.18f);
                if (cyc > 0.8 && cyc < 0.86)
                    using (Pen pn = new Pen(Color.FromArgb(120, 235, 245, 255), 0.007f)) { float pr = 0.06f + (float)(cyc - 0.8) * 1.2f; g.DrawEllipse(pn, x - pr, y - pr, pr * 2, pr * 2); }
            }
            g.Restore(st);
        }
    }
}

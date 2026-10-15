// Stackshot - The crewmate going home the way it does in its game: a vent opens at its feet, it hops and drops into
// it, and the grate shuts behind it. Fan tribute, not affiliated with its owners (see the README).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public partial class Mascot
    {
        double ventStart = -1;

        // How long the whole vent scene takes (ms); the window switches the mascot off after it.
        public const double VentTime = 1700;

        public bool CanVent { get { return Look.Kind == MascotParts.KindCrewmate; } }

        public void Vent()
        {
            now = Clock();
            Wake();
            Stop(false);
            mood = Mood.Idle;
            ventStart = now;
        }

        public void EndVent() { ventStart = -1; }

        // Vertical offset in units of D: still while the vent opens, a little hop, then down into the hole and gone.
        static float VentOffset(double t)
        {
            if (t < 300) return 0;
            if (t < 650) return -0.12f * (float)Math.Sin(Math.PI * (t - 300) / 350.0);
            if (t < 1050) return 1.1f * (float)Math.Pow((t - 650) / 400.0, 1.5);
            return 2;
        }

        // How open the grate is (0 shut, 1 fully up): it opens first and shuts once the crewmate is through.
        static float VentOpen(double t)
        {
            if (t < 300) return (float)(t / 300.0);
            if (t < 1100) return 1;
            if (t < 1400) return 1 - (float)((t - 1100) / 300.0);
            return 0;
        }

        // The vent on the floor: a grey frame, the dark hole and the slatted grate, which stands up behind the hole when open.
        static void PaintVent(Graphics g, float cx, float floorY, float D, double t)
        {
            float o = VentOpen(t), appear = (float)Math.Min(1, t / 160.0), w = 0.56f * D * appear, h = 0.12f * D * appear;
            if (w < 2 || h < 2) return;
            RectangleF frame = new RectangleF(cx - w / 2, floorY - h * 0.35f, w, h);
            using (GraphicsPath fp = Theme.Round(frame, h * 0.3f))
            {
                MascotParts.Fill(g, fp, frame, Color.FromArgb(150, 156, 168), Color.FromArgb(86, 90, 102), 90f);
                MascotParts.Stroke(g, fp, Color.FromArgb(230, 34, 36, 44), Math.Max(1f, D * 0.012f));
            }
            RectangleF hole = RectangleF.Inflate(frame, -w * 0.07f, -h * 0.2f);
            using (GraphicsPath hp = Theme.Round(hole, hole.Height * 0.25f)) MascotParts.FillSolid(g, hp, Color.FromArgb(18, 18, 24));
            // The grate: lying on the hole when shut, standing up behind it when open.
            RectangleF grate = o < 0.05f ? hole : new RectangleF(hole.X, hole.Y - hole.Height * 1.4f * o, hole.Width, hole.Height * 1.4f * o);
            if (grate.Height < 1) return;
            using (GraphicsPath gp = Theme.Round(grate, Math.Min(grate.Height, grate.Width) * 0.2f))
            {
                MascotParts.Fill(g, gp, grate, Color.FromArgb(178, 184, 196), Color.FromArgb(112, 118, 130), 90f);
                MascotParts.Stroke(g, gp, Color.FromArgb(220, 40, 42, 50), Math.Max(1f, D * 0.009f));
                Region old = g.Clip;
                g.SetClip(gp, CombineMode.Intersect);
                using (Pen slat = new Pen(Color.FromArgb(200, 50, 52, 62), Math.Max(1f, D * 0.012f)))
                    for (int i = 1; i <= 4; i++)
                    {
                        float x = grate.X + grate.Width * i / 5f;
                        g.DrawLine(slat, x, grate.Y, x, grate.Bottom);
                    }
                g.Clip = old;
                old.Dispose();
            }
        }
    }
}

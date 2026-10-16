// Stackshot - The finish of the series characters, closer to how their originals are drawn: a firm ink contour, a
// shadow layer on the side away from the light, a thin light rim on the lit side and a crisp gloss on top. The
// mascot's own species keep their soft look.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stackshot
{
    public static partial class MascotParts
    {
        // From the winged lion on, every species is a character from a series or a game; Klei's keep their flat ink.
        public static bool IsSeries(int kind) { return kind >= 13 && !IsKlei(kind) && !IsToon(kind); }

        // A chibi dressed as a series character: its hair, suit or mask comes from one.
        public static bool SeriesLook(MascotLook l)
        {
            return l.Kind == 7 && ((l.Hat >= 18 && l.Hat != HatNightcap) || l.Outfit >= 8 || l.Face >= 8);
        }

        public static bool IsPokemonLike(int kind)
        {
            return IsPokemon(kind) || kind == KindPikachu || kind == KindEevee || kind == KindCharmander || (kind >= KindPsyduck && kind <= KindMewtwo);
        }

        public static void SeriesFinish(Graphics g, GraphicsPath body, RectangleF b, Color c2, float D)
        {
            Region old = g.Clip;
            // The shadow: what the body leaves uncovered when it is moved up and to the left (the light comes from there).
            using (Region shadow = new Region(body))
            using (GraphicsPath lit = (GraphicsPath)body.Clone())
            using (Matrix m = new Matrix())
            {
                m.Translate(-0.05f * D, -0.06f * D);
                lit.Transform(m);
                shadow.Exclude(lit);
                shadow.Intersect(old);
                g.Clip = shadow;
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(78, Darker(c2, 0.4)))) g.FillPath(sb, body);
            }
            // The rim: a thin light edge along the top left.
            using (Region rim = new Region(body))
            using (GraphicsPath back = (GraphicsPath)body.Clone())
            using (Matrix m = new Matrix())
            {
                m.Translate(0.018f * D, 0.022f * D);
                back.Transform(m);
                rim.Exclude(back);
                rim.Intersect(old);
                g.Clip = rim;
                using (SolidBrush rb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(rb, body);
            }
            // The gloss: a crisp highlight high on the lit side, and a small dot beside it.
            using (Region inside = new Region(body))
            {
                inside.Intersect(old);
                g.Clip = inside;
                GraphicsState st = g.Save();
                float gx = b.X + b.Width * 0.27f, gy = b.Y + b.Height * 0.16f;
                g.TranslateTransform(gx, gy);
                g.RotateTransform(-24);
                Ellipse(g, Color.FromArgb(120, 255, 255, 255), -b.Width * 0.11f, -b.Height * 0.045f, b.Width * 0.22f, b.Height * 0.09f);
                g.Restore(st);
                Ellipse(g, Color.FromArgb(105, 255, 255, 255), gx + b.Width * 0.12f, gy - b.Height * 0.035f, D * 0.03f, D * 0.03f);
            }
            g.Clip = old;
            old.Dispose();
        }
    }
}

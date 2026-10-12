// Stackshot - Editor canvas: covers for personal data, as ordinary Redact marks.
// MIT License - https://github.com/rubenitx/stackshot
using System.Collections.Generic;
using System.Drawing;

namespace Stackshot
{
    public partial class Canvas
    {
        // A box counts as covered already when Redact marks hide this share of it.
        const float CoveredShare = 0.8f;

        // Redact marks over the boxes (image pixels), all in one undo step. Boxes already under Redact marks are left out,
        // so asking twice adds nothing. Returns how many were added.
        public int AddCovers(IList<Rectangle> boxes)
        {
            List<RectangleF> taken = new List<RectangleF>();
            foreach (Shape s in shapes) if (s.Kind == Tool.Redact) taken.Add(s.Box);
            List<Shape> add = new List<Shape>();
            foreach (Rectangle b in boxes)
            {
                Rectangle r = Rectangle.Intersect(b, new Rectangle(0, 0, Img.Width, Img.Height));
                if (r.Width < 1 || r.Height < 1 || Covered(r, taken)) continue;
                add.Add(Cover(r));
                taken.Add(r);
            }
            if (add.Count == 0) return 0;
            CommitText();
            Commit();
            shapes.AddRange(add);
            Fire(Changed);
            Fire(StateChanged);
            Invalidate();
            return add.Count;
        }

        // How many of the boxes Redact marks already hide, as AddCovers judges it.
        public int CoveredAlready(IList<Rectangle> boxes)
        {
            List<RectangleF> taken = new List<RectangleF>();
            foreach (Shape s in shapes) if (s.Kind == Tool.Redact) taken.Add(s.Box);
            int n = 0;
            foreach (Rectangle b in boxes)
            {
                Rectangle r = Rectangle.Intersect(b, new Rectangle(0, 0, Img.Width, Img.Height));
                if (r.Width >= 1 && r.Height >= 1 && Covered(r, taken)) n++;
            }
            return n;
        }

        // Sampled on a grid: overlapping marks count once.
        static bool Covered(Rectangle r, List<RectangleF> taken)
        {
            const int nx = 10, ny = 6;
            int hit = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    PointF p = new PointF(r.X + (i + 0.5f) * r.Width / nx, r.Y + (j + 0.5f) * r.Height / ny);
                    foreach (RectangleF t in taken) if (t.Contains(p)) { hit++; break; }
                }
            return hit >= nx * ny * CoveredShare;
        }

        Shape Cover(Rectangle r)
        {
            Shape s = new Shape();
            s.Kind = Tool.Redact;
            s.Color = Color;
            s.Weight = Weight;
            s.Width = StrokeFor(Weight);
            s.FontPx = FontFor(Weight);
            s.A = new PointF(r.Left, r.Top);
            s.B = new PointF(r.Right, r.Bottom);
            return s;
        }

        // The boxes of the Redact marks (image pixels).
        public List<Rectangle> RedactBoxes()
        {
            List<Rectangle> o = new List<Rectangle>();
            foreach (Shape s in shapes) if (s.Kind == Tool.Redact) o.Add(Rectangle.Round(s.Box));
            return o;
        }

        // The image as edited (crop and marks, without the backdrop), and where its top left corner is in the image. The
        // Redact marks are left out: what they hide is read too, so data already covered (or half covered) is seen as such.
        public Bitmap RenderPlain(out Point origin)
        {
            Rectangle area = Rectangle.Round(Crop);
            area.Intersect(new Rectangle(0, 0, Img.Width, Img.Height));
            if (area.Width < 1 || area.Height < 1) area = new Rectangle(0, 0, Img.Width, Img.Height);
            origin = area.Location;
            Bitmap b = CopyArea(Img, area);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.TranslateTransform(-area.X, -area.Y);
                Painter.DrawMarks(g, shapes.FindAll(delegate(Shape s) { return s.Kind != Tool.Redact; }), null, null, Img, area);
            }
            return b;
        }

        // Nothing done yet: no marks, crop, undo history, text being typed or gesture.
        public bool Pristine
        {
            get { return shapes.Count == 0 && undo.Count == 0 && redo.Count == 0 && !IsCropped && box == null && drag == Drag.None && !panning; }
        }

        // A covered capture opened again: its original pixels, with the covers as Redact marks from the start (no undo
        // step, nothing to save). Only on a pristine canvas and an original of the same size; otherwise the original is
        // disposed and nothing changes.
        public bool ShowCovers(Bitmap original, IList<Rectangle> boxes)
        {
            if (original.Width != Img.Width || original.Height != Img.Height || !Pristine) { original.Dispose(); return false; }
            ReplaceImage(original);
            foreach (Rectangle r in boxes) shapes.Add(Cover(r));
            coversAtOpen = RedactBoxes();
            Fire(StateChanged);
            Invalidate();
            return true;
        }

        // The covers a reopened capture started with (its file has them burned in); null otherwise.
        List<Rectangle> coversAtOpen;

        // The marks are what the file already holds: none, or exactly the covers a reopened capture started with. Removing
        // those covers is a change too.
        public bool MarksAsOpened
        {
            get
            {
                if (coversAtOpen == null) return shapes.Count == 0;
                if (shapes.Count != coversAtOpen.Count) return false;
                for (int i = 0; i < shapes.Count; i++)
                    if (shapes[i].Kind != Tool.Redact || Rectangle.Round(shapes[i].Box) != coversAtOpen[i]) return false;
                return true;
            }
        }
    }
}

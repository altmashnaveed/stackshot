// Editor marks drawn to bitmaps (no window): renumbered steps, spotlight, pixelate and line; a few editor rules.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static Shape Counter(int n)
        {
            Shape s = new Shape();
            s.Kind = Tool.Counter;
            s.Number = n;
            return s;
        }

        static Shape Box(Tool t, float x0, float y0, float x1, float y1)
        {
            Shape s = new Shape();
            s.Kind = t;
            s.Color = Color.FromArgb(255, 230, 40, 40);
            s.Width = 6;
            s.A = new PointF(x0, y0);
            s.B = new PointF(x1, y1);
            return s;
        }

        static Bitmap White(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.White);
            return b;
        }

        // IsMaster of an editor that was never constructed: only the fields it reads are set.
        static bool MasterFor(string path, bool rgb)
        {
            BindingFlags np = BindingFlags.NonPublic | BindingFlags.Instance;
            Editor e = (Editor)FormatterServices.GetUninitializedObject(typeof(Editor));
            typeof(Editor).GetField("path", np).SetValue(e, path);
            typeof(Editor).GetField("ffmpeg", np).SetValue(e, "ffmpeg.exe");
            typeof(Editor).GetField("rgbSource", np).SetValue(e, rgb);
            return (bool)typeof(Editor).GetProperty("IsMaster", np).GetValue(e, null);
        }

        static void ToolsSuite()
        {
            Test("tools", "counters-renumbered", delegate
            {
                List<Shape> list = new List<Shape> { Counter(1), Box(Tool.Rect, 0, 0, 5, 5), Counter(3), Counter(4) };
                Canvas.Renumber(list);
                Check(list[0].Number == 1 && list[2].Number == 2 && list[3].Number == 3, "1, 3, 4 should become 1, 2, 3: " + list[0].Number + "," + list[2].Number + "," + list[3].Number);
                // Order follows the numbers, not the list; ties keep the order they were placed in.
                List<Shape> mixed = new List<Shape> { Counter(5), Counter(2), Counter(2) };
                Canvas.Renumber(mixed);
                Check(mixed[1].Number == 1 && mixed[2].Number == 2 && mixed[0].Number == 3, "order by number: " + mixed[0].Number + "," + mixed[1].Number + "," + mixed[2].Number);
            });
            Test("tools", "spotlights-dim-outside-once", delegate
            {
                using (Bitmap b = White(200, 100))
                {
                    List<Shape> list = new List<Shape> { Box(Tool.Spotlight, 20, 20, 80, 80), Box(Tool.Spotlight, 60, 20, 140, 80) };
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        Painter.DrawSpotlights(g, list, null, new RectangleF(0, 0, 200, 100));
                        foreach (Shape s in list) Painter.Draw(g, s, b);
                    }
                    Color outside = b.GetPixel(180, 50), lit1 = b.GetPixel(40, 50), lit2 = b.GetPixel(120, 50), overlap = b.GetPixel(70, 50);
                    Add("outside " + outside.R + " lit " + lit1.R + "/" + lit2.R + " overlap " + overlap.R);
                    Check(outside.R < 140, "outside should be dimmed");
                    Check(lit1.R == 255 && lit2.R == 255 && overlap.R == 255, "both spotlights and their overlap stay lit");
                }
                using (Bitmap b = White(50, 50))
                {
                    using (Graphics g = Graphics.FromImage(b)) Painter.DrawSpotlights(g, new List<Shape> { Box(Tool.Rect, 5, 5, 40, 40) }, null, new RectangleF(0, 0, 50, 50));
                    Check(b.GetPixel(2, 2).R == 255, "no spotlight: nothing dimmed");
                }
            });
            Test("tools", "spotlight-in-video-overlay", delegate
            {
                // On transparent (the overlay FFmpeg puts over a video) the dimming is a translucent black.
                using (Bitmap b = new Bitmap(100, 100, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b)) Painter.DrawSpotlights(g, new List<Shape> { Box(Tool.Spotlight, 30, 30, 70, 70) }, null, new RectangleF(0, 0, 100, 100));
                    Color o = b.GetPixel(5, 5), i = b.GetPixel(50, 50);
                    Check(o.A > 100 && o.R == 0, "outside: translucent black, got " + o);
                    Check(i.A == 0, "inside stays transparent, got " + i);
                }
            });
            Test("tools", "spotlight-edges-antialiased", delegate
            {
                using (Bitmap b = White(100, 100))
                {
                    using (Graphics g = Graphics.FromImage(b)) Painter.DrawSpotlights(g, new List<Shape> { Box(Tool.Spotlight, 20, 20, 80, 80) }, null, new RectangleF(0, 0, 100, 100));
                    int soft = 0;
                    // A Region fill gives only full dim (105) or lit (255) pixels around the corner arc.
                    for (int y = 18; y <= 40; y++)
                        for (int x = 18; x <= 40; x++) { int r = b.GetPixel(x, y).R; if (r > 108 && r < 252) soft++; }
                    Add("partial pixels around the corner: " + soft);
                    Check(soft > 0, "the rounded corner has antialiased pixels");
                    Check(Math.Abs(b.GetPixel(2, 2).R - 105) <= 1, "full dimming outside, got " + b.GetPixel(2, 2).R);
                    Check(b.GetPixel(50, 50).R == 255, "lit inside");
                }
            });
            Test("tools", "spotlight-under-zoom", delegate
            {
                using (Bitmap b = White(200, 200))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.ScaleTransform(2, 2);
                        Painter.DrawSpotlights(g, new List<Shape> { Box(Tool.Spotlight, 30, 30, 70, 70) }, null, new RectangleF(0, 0, 100, 100));
                    }
                    Check(b.GetPixel(100, 100).R == 255, "the spotlight scales with the view");
                    Check(b.GetPixel(190, 190).R < 140 && b.GetPixel(5, 5).R < 140, "dimmed up to the zoomed edges: " + b.GetPixel(190, 190).R + "/" + b.GetPixel(5, 5).R);
                }
            });
            Test("tools", "pixelate-outside-spotlight-dimmed", delegate
            {
                using (Bitmap img = White(200, 100))
                using (Bitmap b = White(200, 100))
                {
                    List<Shape> list = new List<Shape> { Box(Tool.Spotlight, 10, 10, 90, 90), Box(Tool.Pixelate, 120, 20, 180, 80), Box(Tool.Pixelate, 30, 30, 70, 70) };
                    using (Graphics g = Graphics.FromImage(b)) Painter.DrawMarks(g, list, null, null, img, new RectangleF(0, 0, 200, 100));
                    foreach (Shape s in list) s.DropCache();
                    int outside = b.GetPixel(150, 50).R, inside = b.GetPixel(50, 50).R, plain = b.GetPixel(195, 50).R;
                    Add("pixelate outside " + outside + ", inside " + inside + ", plain dim " + plain);
                    Check(Math.Abs(outside - plain) <= 2, "a pixelated area outside the spotlight is dimmed like the rest");
                    Check(inside == 255, "a pixelated area inside the spotlight stays lit");
                }
            });
            Test("tools", "marks-keep-their-order", delegate
            {
                // A mark placed before a pixelated area stays hidden under it.
                using (Bitmap img = White(100, 100))
                using (Bitmap b = White(100, 100))
                {
                    List<Shape> list = new List<Shape> { Box(Tool.Highlight, 20, 20, 80, 80), Box(Tool.Pixelate, 10, 10, 90, 90) };
                    using (Graphics g = Graphics.FromImage(b)) Painter.DrawMarks(g, list, null, null, img, new RectangleF(0, 0, 100, 100));
                    foreach (Shape s in list) s.DropCache();
                    Color c = b.GetPixel(50, 50);
                    Check(c.R == 255 && c.G == 255, "the highlight under the pixelated area is covered, got " + c);
                }
            });
            Test("tools", "master-needs-rgb", delegate
            {
                // Only a Cine master (RGB) gets the lossless-master texts; another Matroska is a lossy video.
                Check(MasterFor("a" + Cine.Extension, true), "RGB .mkv is a master");
                Check(!MasterFor("a" + Cine.Extension, false), "YUV .mkv (OBS) is not a master");
                Check(!MasterFor("a.mp4", true), "an .mp4 is not a master");
            });
            Test("tools", "quit-answer-leaves-no-flag", delegate
            {
                // A later question can still cancel the quit: answering one editor must not stop it asking again.
                string src = File.ReadAllText(Path.Combine(testsDir, @"..\..\src\Editor\Editor.cs"));
                int at = src.IndexOf("bool ConfirmQuit()", StringComparison.Ordinal);
                Check(at > 0, "ConfirmQuit found");
                int end = src.IndexOf("protected override void OnFormClosed", at, StringComparison.Ordinal);
                Check(end > at, "end of ConfirmQuit found");
                Check(src.Substring(at, end - at).IndexOf("closeWithoutAsking", StringComparison.Ordinal) < 0, "ConfirmQuit sets no close-without-asking flag");
            });
            Test("tools", "line-drawn-and-bent", delegate
            {
                using (Bitmap b = White(100, 100))
                {
                    Shape line = Box(Tool.Line, 10, 50, 90, 50);
                    using (Graphics g = Graphics.FromImage(b)) Painter.Draw(g, line, b);
                    Color on = b.GetPixel(50, 50), off = b.GetPixel(50, 20);
                    Check(on.R > 200 && on.G < 90, "the line's colour at its middle, got " + on);
                    Check(off.R == 255 && off.G == 255, "nothing away from the line");
                    Check(Shape.IsLinear(Tool.Line) && Shape.IsLinear(Tool.Arrow) && !Shape.IsLinear(Tool.Rect), "line has ends like an arrow");
                    line.SetMid(new PointF(50, 20), 1);
                    Check(line.Curved && Math.Abs(line.Mid.Y - 20) < 0.01, "bend through the middle point");
                    RectangleF bounds = Painter.Bounds(line);
                    Check(bounds.Top < 21, "bounds follow the bend: " + bounds);
                }
            });
        }
    }
}

// The editor's backdrop sidebar, suite "backdrop": the geometry of inset and alignment, every kind of backdrop in the
// saved image, the preview and the saved image agreeing, and pictures of the editor with the sidebar open.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using D = System.Drawing;
using DI = System.Drawing.Imaging;

namespace Stackshot.Tests
{
    static class AppTestsBackdrop
    {
        const string S = "backdrop";
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static string Dir(string name)
        {
            string data = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
            string d = Path.Combine(data, "backdrop", name);
            Directory.CreateDirectory(d);
            return d;
        }

        // A capture with a white page on a grey desktop: wide margins on the left and top, narrow ones on the right
        // and bottom, so balancing has something to even out.
        static D.Bitmap Sample()
        {
            D.Bitmap img = new D.Bitmap(640, 400, DI.PixelFormat.Format32bppArgb);
            using (D.Graphics g = D.Graphics.FromImage(img))
            {
                g.Clear(D.Color.FromArgb(244, 244, 246));
                g.FillRectangle(new D.SolidBrush(D.Color.FromArgb(40, 90, 200)), 120, 80, 480, 300);
                g.FillRectangle(D.Brushes.White, 140, 100, 440, 260);
                g.FillRectangle(new D.SolidBrush(D.Color.FromArgb(230, 60, 60)), 160, 120, 120, 40);
            }
            return img;
        }

        static Editor NewEditor(D.Bitmap img, Settings s, D.Size size)
        {
            Editor ed = new Editor(null, Path.Combine(Dir("editor"), "prueba.png"), img);
            typeof(Editor).GetField("bgs", NP).SetValue(ed, s);
            Canvas c = CanvasOf(ed);
            c.Bg = s;
            BgPanel bg = PanelOf(ed);
            bg.Bg = s;
            ed.StartPosition = FormStartPosition.Manual;
            ed.Location = new D.Point(-32000, -32000);
            ed.Size = size;
            typeof(Editor).GetMethod("ToggleBgPanel", NP).Invoke(ed, null);
            ed.PerformLayout();
            return ed;
        }

        static Canvas CanvasOf(Editor ed) { return (Canvas)typeof(Editor).GetField("canvas", NP).GetValue(ed); }
        static BgPanel PanelOf(Editor ed) { return (BgPanel)typeof(Editor).GetField("bgPanel", NP).GetValue(ed); }

        static Settings NewSettings()
        {
            Settings s = new Settings();
            s.SaveFolder = Dir("Capturas");
            return s;
        }

        // Every docked control as it paints itself, where it sits.
        static D.Bitmap Render(Editor ed)
        {
            D.Bitmap pic = new D.Bitmap(ed.ClientSize.Width, ed.ClientSize.Height);
            using (D.Graphics g = D.Graphics.FromImage(pic))
            {
                g.Clear(D.Color.Magenta);
                MethodInfo onPaint = typeof(Control).GetMethod("OnPaint", NP);
                foreach (Control c in ed.Controls)
                {
                    if (c.Width <= 0 || c.Height <= 0) continue;
                    using (D.Bitmap part = new D.Bitmap(c.Width, c.Height))
                    {
                        using (D.Graphics pg = D.Graphics.FromImage(part))
                        {
                            pg.Clear(c.BackColor.A == 255 ? c.BackColor : D.Color.Magenta);
                            onPaint.Invoke(c, new object[] { new PaintEventArgs(pg, new D.Rectangle(0, 0, c.Width, c.Height)) });
                        }
                        g.DrawImage(part, c.Bounds);
                    }
                }
            }
            return pic;
        }

        static bool Near(D.Color a, D.Color b, int tol)
        {
            return Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;
        }

        // ---- Geometry

        static void Geometry()
        {
            Random r = new Random(7107);
            for (int it = 0; it < 6000; it++)
            {
                Settings s = new Settings();
                s.BgPadding = r.Next(0, 101);
                s.BgRatio = new string[] { "auto", "16:9", "4:3", "1:1" }[r.Next(4)];
                s.BgAlign = r.Next(9);
                D.Size content = new D.Size(r.Next(1, 2500), r.Next(1, 2500));
                Padding inset = r.Next(3) == 0 ? Padding.Empty : new Padding(r.Next(0, 200), r.Next(0, 200), r.Next(0, 200), r.Next(0, 200));
                bool even = r.Next(2) == 0;
                D.Size frame;
                D.Rectangle card, inner;
                Backdrop.Measure(content, s, even, inset, out frame, out card, out inner);
                string where = "case " + it + " content " + content + " inset " + inset + " align " + s.BgAlign + " ratio " + s.BgRatio + " even " + even + " -> frame " + frame + " card " + card + " inner " + inner;
                Check(inner.Size == content, where + ": inner size");
                Check(card.Width == content.Width + inset.Horizontal && card.Height == content.Height + inset.Vertical, where + ": card size");
                Check(inner.X - card.X == inset.Left && inner.Y - card.Y == inset.Top, where + ": inner not at the inset");
                Check(card.X >= 0 && card.Y >= 0 && card.Right <= frame.Width && card.Bottom <= frame.Height, where + ": card outside the frame");
                if (even) Check(frame.Width % 2 == 0 && frame.Height % 2 == 0, where + ": odd frame");
                int left = card.X, right = frame.Width - card.Right, top = card.Y, bottom = frame.Height - card.Bottom;
                int col = s.BgAlign % 3, row = s.BgAlign / 3;
                Check(col == 0 ? left == 0 : col == 2 ? right <= 1 : Math.Abs(left - right) <= 3, where + ": not aligned across");
                Check(row == 0 ? top == 0 : row == 2 ? bottom <= 1 : Math.Abs(top - bottom) <= 3, where + ": not aligned down");
            }
            // Centred and without an inset, the old measure is unchanged.
            for (int it = 0; it < 2000; it++)
            {
                Settings s = new Settings();
                s.BgPadding = r.Next(0, 101);
                s.BgRatio = new string[] { "auto", "16:9", "4:3", "1:1" }[r.Next(4)];
                D.Size content = new D.Size(r.Next(1, 3000), r.Next(1, 3000));
                D.Size f1, f2;
                D.Rectangle i1, i2, c2;
                Backdrop.Measure(content, s, true, out f1, out i1);
                Backdrop.Measure(content, s, true, Padding.Empty, out f2, out c2, out i2);
                Check(f1 == f2 && i1 == i2 && c2 == i2, "case " + it + ": the two measures differ");
            }
        }

        // ---- Each kind in the saved image

        static void Kinds()
        {
            using (D.Bitmap img = Sample())
            {
                Settings s = NewSettings();
                s.BgPadding = 60;
                s.BgShadow = false;
                s.BgKind = 1;
                s.BgColor = 0x1D72E8;
                using (D.Bitmap b = Backdrop.Compose(img, s)) Check(Near(b.GetPixel(2, 2), D.Color.FromArgb(0x1D, 0x72, 0xE8), 2), "plain colour corner " + b.GetPixel(2, 2));

                s.BgKind = 2;
                D.Color[] corner = new D.Color[3];
                for (int v = 0; v < 3; v++)
                {
                    s.BgBlur = v;
                    using (D.Bitmap b = Backdrop.Compose(img, s)) corner[v] = b.GetPixel(4, 4);
                }
                // The capture's corner is light grey: blurred it stays light; the dark variant is darker, the light one lighter.
                Check(corner[0].GetBrightness() > 0.7f, "blurred backdrop is not taken from the capture: " + corner[0]);
                Check(corner[1].GetBrightness() < corner[0].GetBrightness() - 0.1f, "dark blur not darker: " + corner[1] + " vs " + corner[0]);
                Check(corner[2].GetBrightness() >= corner[0].GetBrightness(), "light blur not lighter: " + corner[2] + " vs " + corner[0]);

                // Inset: a border in the capture's edge colour between the backdrop and the capture.
                s.BgKind = 1;
                s.BgColor = 0x000000;
                s.BgRadius = 0;
                s.BgInset = 50;
                using (Backdrop.ShotInfo info = Backdrop.Study(img, new D.Rectangle(0, 0, img.Width, img.Height), s))
                {
                    Check(info.Inset.Left > 0 && info.Inset.Left == info.Inset.Right && info.Inset.Top == info.Inset.Bottom, "even inset " + info.Inset);
                    Check(Near(info.Edge, D.Color.FromArgb(244, 244, 246), 3), "edge colour " + info.Edge);
                    D.Size frame;
                    D.Rectangle card, inner;
                    Backdrop.Measure(img.Size, s, false, info.Inset, out frame, out card, out inner);
                    using (D.Bitmap b = Backdrop.Compose(img, s, info))
                    {
                        Check(b.Size == frame, "saved size " + b.Size + " for frame " + frame);
                        Check(Near(b.GetPixel(card.X + 2, card.Y + card.Height / 2), info.Edge, 3), "the inset is not the edge colour: " + b.GetPixel(card.X + 2, card.Y + card.Height / 2));
                        Check(Near(b.GetPixel(Math.Max(0, card.X - 3), card.Y + card.Height / 2), D.Color.Black, 3), "the backdrop is not outside the card");
                    }

                    // Balanced: the capture's own margins (120 left, 80 top, 40 right, 20 bottom) are evened out.
                    s.BgBalance = true;
                    using (Backdrop.ShotInfo bal = Backdrop.Study(img, new D.Rectangle(0, 0, img.Width, img.Height), s))
                    {
                        int l = 120 + bal.Inset.Left, t = 80 + bal.Inset.Top, rr = 40 + bal.Inset.Right, bb = 20 + bal.Inset.Bottom;
                        Check(Math.Abs(l - rr) <= 2 && Math.Abs(t - bb) <= 2 && Math.Abs(l - t) <= 2, "not balanced: " + bal.Inset + " gives " + l + "," + t + "," + rr + "," + bb);
                    }
                }

                // Shadow level 0 is no shadow; 50 is the shadow as it was.
                s.BgInset = 0;
                s.BgColor = 0xFFFFFF;
                s.BgBalance = false;
                s.BgShadow = true;
                s.BgShadowSize = 50;
                using (Backdrop.ShotInfo info = Backdrop.Study(img, new D.Rectangle(0, 0, img.Width, img.Height), s))
                using (D.Bitmap a = Backdrop.Compose(img, s, info))
                {
                    s.BgShadow = false;
                    using (D.Bitmap b = Backdrop.Compose(img, s, info))
                    {
                        D.Size frame;
                        D.Rectangle inner;
                        Backdrop.Measure(img.Size, s, false, out frame, out inner);
                        D.Point below = new D.Point(inner.X + inner.Width / 2, inner.Bottom + 6);
                        Check(a.GetPixel(below.X, below.Y).GetBrightness() < b.GetPixel(below.X, below.Y).GetBrightness() - 0.05f, "no shadow under the capture");
                    }
                }
            }

            // A very tall scrolling capture is blurred from samples, at the cost of an ordinary one.
            using (D.Bitmap tall = new D.Bitmap(1600, 24000, DI.PixelFormat.Format32bppArgb))
            {
                using (D.Graphics g = D.Graphics.FromImage(tall))
                {
                    g.Clear(D.Color.FromArgb(30, 120, 220));
                    g.FillRectangle(D.Brushes.White, 0, 12000, 1600, 12000);
                }
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                using (D.Bitmap small = Backdrop.BlurSmall(tall, new D.Rectangle(0, 0, tall.Width, tall.Height)))
                {
                    double ms = sw.Elapsed.TotalMilliseconds;
                    Check(ms < 600, "blurring a 1600 x 24000 capture took " + Math.Round(ms) + " ms");
                    Check(Near(small.GetPixel(small.Width / 2, 1), D.Color.FromArgb(30, 120, 220), 6) && Near(small.GetPixel(small.Width / 2, small.Height - 2), D.Color.White, 6),
                          "the blurred copy lost the capture's colours: " + small.GetPixel(small.Width / 2, 1) + " " + small.GetPixel(small.Width / 2, small.Height - 2));
                }
            }
        }

        // ---- The sidebar

        static void Sidebar()
        {
            Settings s = NewSettings();
            D.Bitmap img = Sample();
            Editor ed = NewEditor(img, s, new D.Size(1200, 820));
            try
            {
                BgPanel bg = PanelOf(ed);
                Canvas c = CanvasOf(ed);
                Check(bg.Dock == DockStyle.Left && bg.Width > 200, "the backdrop panel is not a sidebar: " + bg.Dock + " " + bg.Width);
                Check(c.Left >= bg.Right - 1, "the canvas is under the sidebar: canvas " + c.Bounds + " panel " + bg.Bounds);
                List<string> names = new List<string>();
                AccessibleObject a = bg.AccessibilityObject;
                for (int i = 0; i < a.GetChildCount(); i++) names.Add(a.GetChild(i).Name);
                foreach (string want in new string[] { "Sin fondo", "Stackshot", "Tu escritorio", "Desenfocada", "Desenfocada y oscura", "Color negro", "Otro color\u2026",
                                                        "Margen", "Esquinas", "Sombra", "Inserci\u00F3n", "Auto-equilibrar", "Alineaci\u00F3n: centrada", "Proporci\u00F3n 1:1" })
                    Check(names.Contains(want), "the sidebar has no \"" + want + "\": " + string.Join(", ", names.ToArray()));

                // A choice in the sidebar reaches the saved image.
                Pick(bg, "Color rojo");
                Check(s.BgKind == 1 && s.BgColor == 0xE5383B && c.BgOn, "red: kind " + s.BgKind + " colour " + s.BgColor.ToString("X6"));
                using (D.Bitmap out1 = c.Render()) Check(Near(out1.GetPixel(1, 1), D.Color.FromArgb(0xE5, 0x38, 0x3B), 2), "red not in the saved image: " + out1.GetPixel(1, 1));
                SetSlider(bg, "Inserci\u00F3n", "40");
                Check(s.BgInset == 40, "inset from the sidebar " + s.BgInset);
                using (D.Bitmap out2 = c.Render()) Check(out2.Size == c.OutputSize, "saved " + out2.Size + " but the editor says " + c.OutputSize);
                Pick(bg, "Alineaci\u00F3n: arriba a la izquierda");
                using (D.Bitmap out3 = c.Render())
                    Check(Near(out3.GetPixel(1, 60), D.Color.FromArgb(244, 244, 246), 3), "aligned top left, the left edge is not the inset: " + out3.GetPixel(1, 60));
                SetSlider(bg, "Sombra", "3");
                SetSlider(bg, "Sombra", "0");
                Check(!s.BgShadow, "shadow at 0 is still on");
                Check(s.BgShadowSize >= 10, "turned off from a faint shadow, it would come back almost invisible: " + s.BgShadowSize);
                SetSlider(bg, "Sombra", "80");
                Check(s.BgShadow && s.BgShadowSize == 80, "shadow at 80: " + s.BgShadow + " " + s.BgShadowSize);
                Pick(bg, "Desenfocada y clara");
                Check(s.BgKind == 2 && s.BgBlur == 2, "blurred light: " + s.BgKind + " " + s.BgBlur);
                Pick(bg, "Sin fondo");
                Check(!c.BgOn, "no backdrop still on");
                using (D.Bitmap out4 = c.Render()) Check(out4.Size == img.Size, "without a backdrop the saved image is the capture: " + out4.Size);
            }
            finally { ed.Dispose(); }
        }

        static void Pick(BgPanel bg, string name)
        {
            AccessibleObject a = bg.AccessibilityObject;
            for (int i = 0; i < a.GetChildCount(); i++)
                if (a.GetChild(i).Name == name) { a.GetChild(i).DoDefaultAction(); Application.DoEvents(); return; }
            throw new InvalidOperationException("no \"" + name + "\" in the sidebar");
        }

        static void SetSlider(BgPanel bg, string name, string value)
        {
            AccessibleObject a = bg.AccessibilityObject;
            for (int i = 0; i < a.GetChildCount(); i++)
                if (a.GetChild(i).Name == name) { a.GetChild(i).Value = value; Application.DoEvents(); return; }
            throw new InvalidOperationException("no slider \"" + name + "\"");
        }

        // Pictures of the editor with the sidebar, light and dark, and each kind of backdrop.
        static void Pictures()
        {
            Backdrop.NoWallpaper = true;   // never the wallpaper of whoever runs the tests
            string[] looks = { "gradient", "blur", "plain", "inset", "aligned" };
            for (int theme = 1; theme <= 2; theme++)
            {
                Ds.Apply(theme);
                for (int k = 0; k < looks.Length; k++)
                {
                    Settings s = NewSettings();
                    switch (looks[k])
                    {
                        case "blur": s.BgKind = 2; break;
                        case "plain": s.BgKind = 1; s.BgColor = 0x2C2C2E; break;
                        case "inset": s.BgInset = 45; s.BgBalance = true; s.BgShadowSize = 80; break;
                        case "aligned": s.BgAlign = 7; s.BgRatio = "16:9"; s.BgPreset = 7; break;
                    }
                    Editor ed = NewEditor(Sample(), s, new D.Size(1200, 820));
                    try
                    {
                        using (D.Bitmap pic = Render(ed)) pic.Save(Path.Combine(Dir("shots"), "editor-" + (theme == 1 ? "light" : "dark") + "-" + looks[k] + ".png"), DI.ImageFormat.Png);
                        if (k == 3)
                        {
                            // The whole sidebar, tall enough for all of it.
                            BgPanel bg = new BgPanel();
                            bg.Bg = s;
                            bg.On = true;
                            bg.BlurThumb = CanvasOf(ed).BlurThumb;
                            bg.Size = new D.Size(252, 1060);
                            using (D.Bitmap side = new D.Bitmap(bg.Width, bg.Height))
                            {
                                using (D.Graphics g = D.Graphics.FromImage(side))
                                    typeof(Control).GetMethod("OnPaint", NP).Invoke(bg, new object[] { new PaintEventArgs(g, new D.Rectangle(0, 0, bg.Width, bg.Height)) });
                                side.Save(Path.Combine(Dir("shots"), "sidebar-" + (theme == 1 ? "light" : "dark") + ".png"), DI.ImageFormat.Png);
                            bg.Dispose();
                            }
                        }
                    }
                    finally { ed.Dispose(); }
                }
            }
            Ds.Apply(0);
            Backdrop.NoWallpaper = false;
            Console.WriteLine("    pictures in " + Dir("shots"));
        }

        // ---- Saved styles

        static void SavedStyles()
        {
            Settings s = NewSettings();
            s.BgKind = 1; s.BgColor = 0x1D72E8; s.BgPadding = 30; s.BgInset = 20; s.BgAlign = 7; s.BgRatio = "16:9"; s.BgShadow = false;
            Check(BgPanel.Styles.Keep(s), "the style on screen was not kept");
            Check(!BgPanel.Styles.Keep(s), "the same style was kept twice");
            string kept = BgPanel.Styles.Of(s);
            Check(BgPanel.Styles.Name(kept) == "Color azul \u00B7 margen 30, inserci\u00F3n, sin sombra, abajo, 16:9", "name: " + BgPanel.Styles.Name(kept));
            Settings other = NewSettings();
            other.BgStyles = s.BgStyles;
            Check(BgPanel.Styles.Apply(other, BgPanel.Styles.All(other)[0]), "a kept style does not apply");
            Check(BgPanel.Styles.Of(other) == kept, "applied " + BgPanel.Styles.Of(other) + " instead of " + kept);
            for (int i = 0; i < 12; i++) { s.BgPadding = i; BgPanel.Styles.Keep(s); }
            Check(BgPanel.Styles.Count(s) == BgPanel.Styles.Max, "kept " + BgPanel.Styles.Count(s) + " styles, at most " + BgPanel.Styles.Max);
            Check(!BgPanel.Styles.All(s).Contains(kept), "the oldest style was not the one let go");
            BgPanel.Styles.Remove(s, 0);
            Check(BgPanel.Styles.Count(s) == BgPanel.Styles.Max - 1, "removing a style");
            // Pictures of the user's own: removing the 14th backdrop moves the styles that used later ones up by one.
            Settings pics = NewSettings();
            pics.BgStyles = "0,13,0,0,50,40,1,50,0,0,4,auto;0,14,0,0,50,40,1,50,0,0,4,auto;0,15,0,0,50,40,1,50,0,0,4,auto;0,2,0,0,50,40,1,50,0,0,4,auto;1,15,0,0,50,40,1,50,0,0,4,auto";
            BgPanel.Styles.PictureRemoved(pics, 14);
            Check(pics.BgStyles == "0,13,0,0,50,40,1,50,0,0,4,auto;0,0,0,0,50,40,1,50,0,0,4,auto;0,14,0,0,50,40,1,50,0,0,4,auto;0,2,0,0,50,40,1,50,0,0,4,auto;1,15,0,0,50,40,1,50,0,0,4,auto",
                  "styles after removing a picture: " + pics.BgStyles);
            // What settings.ini brings is checked: broken entries and a picture that is gone.
            Check(!BgPanel.Styles.Apply(other, "1,2,3"), "a broken style applied");
            Check(!BgPanel.Styles.Valid("0,0,0,0,50,40,1,50,0,0,4,5:4"), "an unknown proportion is valid");
            Check(BgPanel.Styles.Apply(other, "0,999,0,9,500,-3,1,0,999,1,42,auto"), "a style out of range is refused instead of clamped");
            Check(other.BgPreset == 0 && other.BgBlur == 2 && other.BgPadding == 100 && other.BgRadius == 0 && other.BgShadowSize == 1 && other.BgInset == 100 && other.BgAlign == 8,
                  "out of range not clamped: " + BgPanel.Styles.Of(other));

            // The sidebar's + keeps it and its menu lists it.
            D.Bitmap img = Sample();
            Settings e = NewSettings();
            Editor ed = NewEditor(img, e, new D.Size(1200, 820));
            try
            {
                BgPanel bg = PanelOf(ed);
                Pick(bg, "Color negro");
                Pick(bg, "Guardar este estilo");
                Check(BgPanel.Styles.Count(e) == 1 && BgPanel.Styles.All(e)[0] == BgPanel.Styles.Of(e), "the + did not keep the style: " + e.BgStyles);
            }
            finally { ed.Dispose(); }
        }

        public static void Suite()
        {
            AppTests.Test(S, "inset and alignment keep the capture inside the frame, where asked", Geometry);
            AppTests.Test(S, "plain, blurred, inset, balanced and shadow reach the saved image", Kinds);
            AppTests.Test(S, "the sidebar offers every choice and each one reaches the saved image", Sidebar);
            AppTests.Test(S, "saved styles: kept once, brought back exactly, at most eight, checked when read", SavedStyles);
            AppTests.Test(S, "pictures of the editor with the sidebar", Pictures);
        }
    }
}

// Scrolling capture, suite "scroll": the Stitcher alone, fed with frames cut from a synthetic page (lines of random
// "text" on white). Whole-pixel and fractional scrolls (what 125 % and 150 % produce), scrolling up, jumps too long to
// follow, repeating content and a floating button that stays put while the page moves. No screen is read.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Stackshot.Tests
{
    static class AppTestsScroll
    {
        const string SuiteName = "scroll";
        const int W = 320, H = 320, PageH = 1800, IgnoreRight = 24;

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        public static void Suite()
        {
            AppTests.Test(SuiteName, "whole-pixel scrolls stitch exactly", WholePixels);
            AppTests.Test(SuiteName, "fractional scrolls still stitch", Fractional);
            AppTests.Test(SuiteName, "scrolling up is told and stitching resumes", Up);
            AppTests.Test(SuiteName, "a jump past the screen is lost, not guessed", Jump);
            AppTests.Test(SuiteName, "repeating content is never guessed by the tolerant match", Repeating);
            AppTests.Test(SuiteName, "a floating button is not repeated down the image", Floating);
        }

        // Lines of 11 rows every 18, each row its own random pattern of dark "letters".
        static int[] Page(bool repeating)
        {
            int[] p = new int[W * PageH];
            for (int i = 0; i < p.Length; i++) p[i] = 0xFFFFFF;
            Random line = new Random(7);
            for (int top = 6; top + 11 < PageH; top += 18)
            {
                Random rnd = repeating ? new Random(11) : new Random(line.Next());
                for (int y = top; y < top + 11; y++)
                {
                    int x = 8 + rnd.Next(6);
                    while (x < W - 40)
                    {
                        int len = 2 + rnd.Next(5), gap = 1 + rnd.Next(4);
                        int shade = 0x20 + rnd.Next(0x50);
                        for (int k = 0; k < len && x + k < W - 40; k++) p[y * W + x + k] = (shade << 16) | (shade << 8) | shade;
                        x += len + gap;
                    }
                }
            }
            return p;
        }

        // The area as it shows the page scrolled to `at` rows, blending two rows when `at` is fractional.
        static int[] Frame(int[] page, double at)
        {
            int[] f = new int[W * H];
            int a = (int)Math.Floor(at);
            double t = at - a;
            for (int y = 0; y < H; y++)
            {
                int r0 = (a + y) * W, r1 = Math.Min(PageH - 1, a + y + 1) * W;
                for (int x = 0; x < W; x++)
                {
                    int p0 = page[r0 + x], p1 = page[r1 + x];
                    if (t == 0) { f[y * W + x] = p0; continue; }
                    int c = 0;
                    for (int s = 0; s < 24; s += 8)
                    {
                        int v = (int)Math.Round(((p0 >> s) & 255) * (1 - t) + ((p1 >> s) & 255) * t);
                        c |= v << s;
                    }
                    f[y * W + x] = c;
                }
            }
            return f;
        }

        static Stitcher New() { return new Stitcher(W, H, IgnoreRight, 30000); }

        static Stitcher.Result Feed(Stitcher s, int[] frame)
        {
            Stitcher.Result r;
            int added;
            s.Add(frame, out r, out added);
            return r;
        }

        static int[] Pixels(Bitmap b)
        {
            int[] px = new int[b.Width * b.Height];
            BitmapData bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y = 0; y < b.Height; y++) Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, y * b.Width, b.Width); }
            finally { b.UnlockBits(bd); }
            return px;
        }

        // Pixels of the stitched image that differ from the page, in rows [0, rows) and columns [x0, x1).
        static int Differences(int[] stitched, int[] page, int rows, int x0, int x1)
        {
            int n = 0;
            for (int y = 0; y < rows; y++)
                for (int x = x0; x < x1; x++)
                    if ((stitched[y * W + x] & 0xFFFFFF) != (page[y * W + x] & 0xFFFFFF)) n++;
            return n;
        }

        static void WholePixels()
        {
            int[] page = Page(false);
            Stitcher s = New();
            int[] at = { 0, 37, 90, 160, 230, 300, 380 };
            for (int i = 0; i < at.Length; i++)
            {
                Stitcher.Result r = Feed(s, Frame(page, at[i]));
                Check(r == (i == 0 ? Stitcher.Result.First : Stitcher.Result.Added), "frame at " + at[i] + ": " + r);
            }
            Check(s.Height == H + 380, "height " + s.Height);
            using (Bitmap b = s.Compose())
                Check(Differences(Pixels(b), page, H + 380, 0, W) == 0, "the stitched image is the page");
        }

        static void Fractional()
        {
            int[] page = Page(false);
            Stitcher s = New();
            double[] at = { 0, 40.4, 81.2, 120.6, 161.0, 201.4, 242.2 };
            for (int i = 0; i < at.Length; i++)
            {
                Stitcher.Result r = Feed(s, Frame(page, at[i]));
                Check(r != Stitcher.Result.Lost && r != Stitcher.Result.Up, "frame at " + at[i] + ": " + r);
            }
            int want = H + 242;
            Check(Math.Abs(s.Height - want) <= at.Length, "height " + s.Height + ", about " + want);

            // A steady fractional scroll (4.2 rows a frame, as smooth scrolling at 125 % gives) keeps its length: the
            // fraction left over is carried instead of lost on every frame.
            Stitcher t = New();
            for (int i = 0; i <= 60; i++) Feed(t, Frame(page, i * 4.2));
            int steady = H + (int)Math.Round(60 * 4.2);
            Check(Math.Abs(t.Height - steady) <= 2, "steady 4.2-row scroll: height " + t.Height + ", should be about " + steady);
        }

        static void Up()
        {
            int[] page = Page(false);
            Stitcher s = New();
            Check(Feed(s, Frame(page, 0)) == Stitcher.Result.First, "first");
            Check(Feed(s, Frame(page, 60)) == Stitcher.Result.Added, "down 60");
            Stitcher.Result up = Feed(s, Frame(page, 30));
            Check(up == Stitcher.Result.Up, "up 30: " + up);
            Check(s.Height == H + 60, "nothing added going up: " + s.Height);
            Check(Feed(s, Frame(page, 90)) == Stitcher.Result.Added, "down again to 90");
            Check(s.Height == H + 90, "height " + s.Height);
            using (Bitmap b = s.Compose())
                Check(Differences(Pixels(b), page, H + 90, 0, W) == 0, "the stitched image is the page");
        }

        static void Jump()
        {
            int[] page = Page(false);
            Stitcher s = New();
            Feed(s, Frame(page, 0));
            Stitcher.Result r = Feed(s, Frame(page, H + 50));
            Check(r == Stitcher.Result.Lost, "no overlap: " + r);
            Check(s.Height == H, "nothing added: " + s.Height);
        }

        static void Repeating()
        {
            int[] page = Page(true);
            Stitcher s = New();
            Feed(s, Frame(page, 0));
            Stitcher.Result r = Feed(s, Frame(page, 40.5));
            Check(r != Stitcher.Result.Added, "a shift was guessed in repeating lines: " + r + ", height " + s.Height);
        }

        // A 16 x 16 textured square that stays at the same place on screen, over the text, not aligned to the cells.
        static void Stamp(int[] frame)
        {
            for (int y = 271; y < 287; y++)
                for (int x = 237; x < 253; x++)
                    frame[y * W + x] = ((x / 3 + y / 3) & 1) == 0 ? 0x3060C0 : 0xF0A030;
        }

        static void Floating()
        {
            int[] page = Page(false);
            Stitcher s = New();
            int last = 0;
            for (int at = 0; at <= 640; at += 40)
            {
                int[] f = Frame(page, at);
                Stamp(f);
                Stitcher.Result r = Feed(s, f);
                Check(r == (at == 0 ? Stitcher.Result.First : Stitcher.Result.Added), "frame at " + at + ": " + r);
                last = at;
            }
            Check(s.Height == H + last, "height " + s.Height);
            using (Bitmap b = s.Compose())
            {
                // Above the last screen the button would show 16 times without the fix; at most a sliver may remain.
                int diff = Differences(Pixels(b), page, last, 232, 256);
                Check(diff < 16 * 16, "pixels of the button left above the last screen: " + diff);
            }
        }
    }
}

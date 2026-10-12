// hunt2editor: second bug hunt over the editor's logic (h2-editor): fuzzed clip segments, kept ranges, reorder and removal
// maps, backdrop geometry and output size; ExpectFor against real exports of random combinations; saves at awkward paths
// and over locked or directory targets; the image tools on a 1x1 and a 20000x200 image.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static double RandIn(Random r, double lo, double hi) { return lo + (hi - lo) * r.NextDouble(); }

        static void Hunt2EditorSuite()
        {
            Test("hunt2editor", "segments-fuzz-measure-and-order", Hunt2Segments);
            Test("hunt2editor", "keep-fuzz-invariants-and-playable", Hunt2Keep);
            Test("hunt2editor", "reorder-and-remove-keep-each-clips-cuts", Hunt2Moves);
            Test("hunt2editor", "reorder-trimmed-clip-stays-trimmed", Hunt2ReorderTrimmedBack);
            Test("hunt2editor", "reorder-clip-behind-end-trim-keeps-its-time", Hunt2ReorderTrimmedAway);
            Test("hunt2editor", "backdrop-measure-fuzz-even-centred", Hunt2Backdrop);
            Test("hunt2editor", "output-size-fuzz-even-and-aspect", Hunt2OutputSize);
            Test("hunt2editor", "expectfor-vs-real-exports-random", Hunt2ExpectReal);
            Test("hunt2editor", "atomic-259-control-plain-write-legal", Hunt2Atomic259Control);
            Test("hunt2editor", "atomic-259-new-file-saves", Hunt2Atomic259New);
            Test("hunt2editor", "atomic-path-length-sweep-240-259", Hunt2AtomicSweep);
            Test("hunt2editor", "atomic-255-existing-old-intact-on-failure", Hunt2AtomicOldIntact);
            Test("hunt2editor", "atomic-locked-by-reader-old-or-new-whole", Hunt2AtomicLocked);
            Test("hunt2editor", "atomic-target-is-directory-no-tmp", Hunt2AtomicDirectory);
            Test("hunt2editor", "image-1x1-crop-render-save", Hunt2Image1x1);
            Test("hunt2editor", "image-20000x200-crop-render-save", Hunt2Image20000);
        }

        // ---- Clip segments ----

        static List<VideoClip> Hunt2Clips(Random r, int n)
        {
            List<VideoClip> clips = new List<VideoClip>();
            for (int i = 0; i < n; i++)
            {
                VideoClip c = new VideoClip();
                c.Path = "c" + i + ".mp4";
                c.Duration = r.Next(4) == 0 ? RandIn(r, 0.001, 0.05) : RandIn(r, 0.05, 12);
                clips.Add(c);
            }
            ClipSeq.Layout(clips);
            return clips;
        }

        // Kept ranges on the joined timeline: ordered and disjoint, some ending or starting a little off a clip boundary.
        static List<double[]> Hunt2RandKeep(Random r, List<VideoClip> clips)
        {
            double total = 0;
            List<double> edges = new List<double>();
            foreach (VideoClip c in clips) { total += c.Duration; edges.Add(c.End); }
            List<double[]> keep = new List<double[]>();
            double t = 0;
            int pieces = r.Next(1, 5);
            for (int i = 0; i < pieces && t < total; i++)
            {
                double a = t + (r.Next(2) == 0 ? 0 : RandIn(r, 0, total / 4));
                if (r.Next(3) == 0) a = Math.Max(t, edges[r.Next(edges.Count)] + RandIn(r, -0.03, 0.03));
                if (a >= total) break;
                double b = r.Next(3) == 0 ? edges[r.Next(edges.Count)] + RandIn(r, -0.03, 0.03) : a + RandIn(r, 0.001, total / 2);
                if (b > total) b = total;
                if (b <= a) continue;
                keep.Add(new double[] { Math.Max(0, a), b });
                t = b + (r.Next(2) == 0 ? 0 : RandIn(r, 0, 0.5));
            }
            return keep;
        }

        // Independent measure of the segments: everything the kept ranges cover inside the clips must be either exported
        // or one of the slivers (under 0.02 s) that are dropped on purpose.
        static void CheckSegments(List<VideoClip> clips, List<double[]> keep, List<ClipSegment> segs, string where)
        {
            double total = 0;
            foreach (VideoClip c in clips) total += c.Duration;
            List<double[]> ranges = keep ?? new List<double[]> { new double[] { 0, total } };
            double measure = 0, dropped = 0;
            foreach (double[] k in ranges)
                foreach (VideoClip c in clips)
                {
                    double a = Math.Max(k[0], c.Offset), b = Math.Min(k[1], c.End);
                    if (b <= a) continue;
                    if (b - a >= 0.02) measure += b - a;
                    else dropped += b - a;
                }
            double sum = 0, prevEnd = -1;
            foreach (ClipSegment g in segs)
            {
                VideoClip c = g.Clip;
                Check(clips.Contains(c), where + ": segment of a clip that is not in the sequence");
                Check(g.A >= 0 && g.B <= c.Duration + 1e-9 && g.B - g.A >= 0.02 - 1e-9, where + ": piece " + g.A + ".." + g.B + " outside its clip " + c.Duration);
                Check(!g.Whole || (g.A == 0 && g.B == c.Duration), where + ": Whole without the whole clip");
                Check(g.Whole || g.A > 0.0005 || g.B < c.Duration - 0.0005, where + ": a whole clip is not flagged Whole");
                double ga = c.Offset + g.A, gb = c.Offset + g.B;
                Check(ga >= prevEnd - 1e-9, where + ": pieces out of order or overlapping at " + ga);
                bool inside = false;
                foreach (double[] k in ranges) if (ga >= k[0] - 0.0006 && gb <= k[1] + 0.0006) inside = true;
                Check(inside, where + ": piece " + ga + ".." + gb + " is outside the kept ranges");
                sum += g.B - g.A;
                prevEnd = gb;
            }
            double tol = 1e-6 + 0.0006 * segs.Count;
            Check(Math.Abs(measure - dropped - sum) <= tol || Math.Abs(measure - sum) <= tol,
                  where + ": exported " + sum + " s, kept " + measure + " s, slivers " + dropped + " s");
        }

        static void Hunt2Segments()
        {
            Random r = new Random(4101);
            int cases = 0;
            for (int it = 0; it < 3000; it++)
            {
                List<VideoClip> clips = Hunt2Clips(r, r.Next(1, 6));
                List<double[]> keep = r.Next(5) == 0 ? null : Hunt2RandKeep(r, clips);
                if (keep != null && keep.Count == 0) continue;
                List<ClipSegment> segs = ClipSeq.Segments(clips, keep);
                CheckSegments(clips, keep, segs, "case " + it);
                cases++;
            }
            Add(cases + " random sequences checked");
        }

        // ---- Kept ranges of the timeline ----

        static void Hunt2Keep()
        {
            Random r = new Random(4202);
            for (int it = 0; it < 3000; it++)
            {
                double total = RandIn(r, 0.2, 60);
                Timeline tl = new Timeline();
                tl.Init(total);
                tl.Pieces.Clear();
                double t = 0;
                while (t < total)
                {
                    double len = r.Next(3) == 0 ? RandIn(r, 0.001, 0.08) : RandIn(r, 0.05, total / 3);
                    double b = Math.Min(total, t + len);
                    if (total - b < 0.001) b = total;
                    tl.Pieces.Add(new Timeline.Piece { A = t, B = b, Off = r.Next(3) == 0 });
                    t = b;
                }
                tl.In = r.Next(2) == 0 ? 0 : RandIn(r, 0, total / 2);
                tl.Out = r.Next(2) == 0 ? total : Math.Min(total, Math.Max(tl.In + 0.01, RandIn(r, tl.In, total)));
                string where = "case " + it + " total " + total + " in " + tl.In + " out " + tl.Out;
                List<double[]> keep = tl.Keep();
                double expect = 0;
                foreach (Timeline.Piece p in tl.Pieces)
                {
                    if (p.Off) continue;
                    double a = Math.Max(p.A, tl.In), b = Math.Min(p.B, tl.Out);
                    if (b - a >= 0.05) expect += b - a;
                }
                double got = 0;
                for (int i = 0; i < keep.Count; i++)
                {
                    double[] k = keep[i];
                    got += k[1] - k[0];
                    Check(k[1] - k[0] >= 0.05 - 1e-9, where + ": range shorter than 0.05 s");
                    Check(k[0] >= tl.In - 1e-9 && k[1] <= tl.Out + 1e-9, where + ": range outside the handles");
                    if (i > 0) Check(k[0] >= keep[i - 1][1] - 1e-9, where + ": ranges overlap");
                }
                Check(Math.Abs(got - expect) <= 0.001 * keep.Count + 1e-9, where + ": keep " + got + " s, pieces " + expect + " s");
                Check(Math.Abs(tl.KeptSeconds - got) < 1e-9, where + ": KeptSeconds differs from Keep()");
                for (int j = 0; j < 400; j++)
                {
                    double x = tl.In + (tl.Out - tl.In) * (j + 0.5) / 400;
                    bool inKeep = false;
                    foreach (double[] k in keep) if (x >= k[0] && x < k[1]) inKeep = true;
                    bool inPiece = false;
                    foreach (Timeline.Piece p in tl.Pieces)
                    {
                        if (p.Off) continue;
                        double a = Math.Max(p.A, tl.In), b = Math.Min(p.B, tl.Out);
                        if (b - a >= 0.05 && x >= a && x < b) inPiece = true;
                    }
                    Check(inKeep == inPiece, where + ": sample " + x + " kept " + inKeep + " but piece says " + inPiece);
                }
                foreach (double[] k in keep)
                {
                    double mid = (k[0] + k[1]) / 2;
                    Check(Math.Abs(tl.PlayableFrom(mid) - mid) < 0.0011, where + ": PlayableFrom moves a time that is already kept");
                }
                for (int j = 0; j < 50; j++)
                {
                    double x = RandIn(r, tl.In, tl.Out);
                    double want = double.PositiveInfinity;
                    foreach (Timeline.Piece p in tl.Pieces)
                    {
                        double a = Math.Max(p.A, tl.In), b = Math.Min(p.B, tl.Out);
                        if (!p.Off && b - a >= 0.05 && x < b - 0.001) { want = Math.Max(x, a); break; }
                    }
                    double gotP = tl.PlayableFrom(x);
                    Check((double.IsInfinity(want) && double.IsInfinity(gotP)) || Math.Abs(gotP - want) < 1e-9,
                          where + ": PlayableFrom(" + x + ") " + gotP + ", expected " + want);
                }
            }
        }

        // ---- Reorder and removal of clips with cuts ----

        static string Hunt2Fmt(double v) { return v.ToString("0.000000", CultureInfo.InvariantCulture); }

        static string Hunt2Dump(Timeline tl, List<VideoClip> clips, string before)
        {
            StringBuilder sb = new StringBuilder(before == null ? "" : "[before " + before + "] ");
            sb.Append("In=" + Hunt2Fmt(tl.In) + " Out=" + Hunt2Fmt(tl.Out) + " clips:");
            foreach (VideoClip c in clips) sb.Append(" [" + c.Offset.ToString("0.0000", CultureInfo.InvariantCulture) + "+" + c.Duration.ToString("0.0000", CultureInfo.InvariantCulture) + "]");
            sb.Append(" pieces:");
            foreach (Timeline.Piece p in tl.Pieces) sb.Append(" " + p.A.ToString("0.0000", CultureInfo.InvariantCulture) + "-" + p.B.ToString("0.0000", CultureInfo.InvariantCulture) + (p.Off ? "off" : "on"));
            return sb.ToString();
        }

        // Time of each clip kept: with the handles (what the export keeps) or without them (what is not cut out).
        static Dictionary<VideoClip, double> Hunt2KeptPerClip(Timeline tl, List<VideoClip> clips, bool handles)
        {
            Dictionary<VideoClip, double> r = new Dictionary<VideoClip, double>();
            foreach (VideoClip c in clips)
            {
                double s = 0;
                foreach (Timeline.Piece p in tl.Pieces)
                {
                    if (p.Off) continue;
                    double a = p.A, b = p.B;
                    if (handles) { a = Math.Max(a, tl.In); b = Math.Min(b, tl.Out); }
                    s += Math.Max(0, Math.Min(b, c.End) - Math.Max(a, c.Offset));
                }
                r[c] = s;
            }
            return r;
        }

        // Moves clip index by dir the way the editor does (its offsets are laid out again and the timeline relaid).
        static void Hunt2Move(Timeline tl, List<VideoClip> clips, int index, int dir)
        {
            List<VideoClip> old = new List<VideoClip>(clips);
            double[] oldOff = ClipSeq.Offsets(old);
            ClipSeq.Move(clips, index, dir);
            double tot = ClipSeq.Layout(clips);
            tl.Relayout(ClipSeq.MovedMap(old, oldOff), tot);
        }

        // The cuts follow their clips exactly; what the handles keep may change when a clip crosses one of them (the
        // handles are positions on the timeline). That is counted and reported, not checked.
        static void Hunt2Moves()
        {
            Random r = new Random(4303);
            int ops = 0, positional = 0;
            for (int it = 0; it < 1500; it++)
            {
                List<VideoClip> clips = Hunt2Clips(r, r.Next(2, 6));
                for (int i = 0; i < clips.Count; i++) clips[i].Duration = RandIn(r, 0.3, 8);
                double total = ClipSeq.Layout(clips);
                Timeline tl = new Timeline();
                tl.Init(total);
                tl.Pieces.Clear();
                foreach (VideoClip c in clips)
                {
                    double a = c.Offset;
                    int parts = r.Next(1, 4);
                    for (int p = 0; p < parts; p++)
                    {
                        double b = p == parts - 1 ? c.End : a + (c.End - a) * RandIn(r, 0.1, 0.9);
                        if (b - a < 0.0005) { a = b; continue; }
                        tl.Pieces.Add(new Timeline.Piece { A = a, B = b, Off = r.Next(3) == 0 });
                        a = b;
                    }
                }
                if (tl.Pieces.Count == 0) continue;
                tl.Pieces.Sort(delegate(Timeline.Piece x, Timeline.Piece y) { return x.A.CompareTo(y.A); });
                if (r.Next(2) == 0) { tl.In = RandIn(r, 0, total / 3); tl.Out = RandIn(r, total * 2 / 3, total); }
                else { tl.In = 0; tl.Out = total; }
                Dictionary<VideoClip, double> cut = Hunt2KeptPerClip(tl, clips, false);
                Dictionary<VideoClip, double> kept = Hunt2KeptPerClip(tl, clips, true);
                for (int op = 0; op < 3 && clips.Count > 1; op++)
                {
                    string dumpBefore = Hunt2Dump(tl, clips, null);
                    string what;
                    if (r.Next(2) == 0 && clips.Count > 2)
                    {
                        int i = r.Next(1, clips.Count);
                        VideoClip gone = clips[i];
                        double start = gone.Offset, len = gone.Duration;
                        clips.RemoveAt(i);
                        double tot = ClipSeq.Layout(clips);
                        tl.Relayout(ClipSeq.RemovedMap(start, len), tot);
                        cut.Remove(gone);
                        kept.Remove(gone);
                        what = "remove " + i;
                    }
                    else
                    {
                        int i = r.Next(1, clips.Count), dir = r.Next(2) == 0 ? -1 : 1;
                        if (i + dir < 1 || i + dir >= clips.Count) continue;
                        Hunt2Move(tl, clips, i, dir);
                        what = "move " + i + " " + dir;
                    }
                    ops++;
                    double now = 0;
                    foreach (VideoClip c in clips) now += c.Duration;
                    string where = "case " + it + " op " + op + " (" + what + ")";
                    Check(Math.Abs(tl.Duration - now) < 1e-9, where + ": timeline length " + tl.Duration + ", clips " + now);
                    Dictionary<VideoClip, double> cutNow = Hunt2KeptPerClip(tl, clips, false);
                    Dictionary<VideoClip, double> keptNow = Hunt2KeptPerClip(tl, clips, true);
                    foreach (VideoClip c in clips)
                    {
                        Check(Math.Abs(cutNow[c] - cut[c]) < 1e-3, where + ": the uncut time of a clip changed from " + cut[c] + " to " + cutNow[c] + " | DUMP " + Hunt2Dump(tl, clips, dumpBefore));
                        Check(keptNow[c] <= cutNow[c] + 1e-6, where + ": more kept than uncut");
                        if (Math.Abs(keptNow[c] - kept[c]) > 1e-6) positional++;
                    }
                    cut = cutNow;
                    kept = keptNow;
                }
            }
            Add(ops + " operations checked; " + positional + " clip(s) exported differently only because a handle moved with the timeline");
        }

        // Two clips, the end trimmed inside the first one: moving the second in front of the first brings it back in full.
        static void Hunt2ReorderTrimmedBack()
        {
            VideoClip a = new VideoClip(), b = new VideoClip();
            a.Path = "a.mp4"; a.Duration = 5;
            b.Path = "b.mp4"; b.Duration = 5;
            List<VideoClip> clips = new List<VideoClip> { a, b };
            ClipSeq.Layout(clips);
            Timeline tl = new Timeline();
            tl.Init(5);
            tl.Extend(10);
            tl.Out = 3;
            double before = tl.KeptSeconds;
            Hunt2Move(tl, clips, 1, -1);
            double after = tl.KeptSeconds;
            Add("exports " + before.ToString("0.###", CultureInfo.InvariantCulture) + " s before, " + after.ToString("0.###", CultureInfo.InvariantCulture) + " s after");
            // Decided by the user (11/10/2026): the in/out handles belong to the timeline, not to a clip, so what the bar shows
            // is what is exported. Moving b in front of a puts it inside the handles: all of it (5 s) plus a's first 3 s.
            Check(Math.Abs(before - 3) < 1e-6 && Math.Abs(after - 8) < 1e-6, "handles on the timeline: expected 3 s then 8 s, got " + before + " then " + after);
        }

        // Three clips, the end trimmed 2 s into the second one: moving the first clip behind the handle drops it entirely.
        static void Hunt2ReorderTrimmedAway()
        {
            VideoClip a = new VideoClip(), b = new VideoClip(), c = new VideoClip();
            a.Path = "a.mp4"; a.Duration = 5;
            b.Path = "b.mp4"; b.Duration = 5;
            c.Path = "c.mp4"; c.Duration = 5;
            List<VideoClip> clips = new List<VideoClip> { a, b, c };
            ClipSeq.Layout(clips);
            Timeline tl = new Timeline();
            tl.Init(5);
            tl.Extend(10);
            tl.Extend(15);
            tl.Out = 7;
            double before = tl.KeptSeconds;
            Hunt2Move(tl, clips, 0, 1);
            double after = tl.KeptSeconds;
            Add("exports " + before.ToString("0.###", CultureInfo.InvariantCulture) + " s before, " + after.ToString("0.###", CultureInfo.InvariantCulture) + " s after");
            // Decided by the user (11/10/2026): the handles stay where they are on the timeline, so moving a past the end handle
            // takes it out of what is exported (measured: 7 s before, 2 s after). Pinned so a change in this behaviour shows.
            Check(Math.Abs(before - 7) < 1e-6 && Math.Abs(after - 2) < 1e-6, "handles on the timeline: expected 7 s then 2 s, got " + before + " then " + after);
            Add("decided: the handles stay on the timeline");
        }

        // ---- Backdrop geometry ----

        static void Hunt2Backdrop()
        {
            Random r = new Random(4404);
            string[] ratios = { "auto", "16:9", "4:3", "1:1" };
            for (int it = 0; it < 4000; it++)
            {
                Size content = it % 4 == 0 ? new Size(r.Next(1, 40), r.Next(1, 40)) : new Size(r.Next(1, 3000), r.Next(1, 3000));
                Settings s = new Settings();
                s.BgPadding = r.Next(0, 101);
                s.BgRadius = r.Next(0, 101);
                s.BgRatio = ratios[r.Next(4)];
                Size frame;
                Rectangle inner;
                Backdrop.Measure(content, s, true, out frame, out inner);
                string where = "case " + it + " content " + content + " pad " + s.BgPadding + " ratio " + s.BgRatio + " -> frame " + frame + " inner " + inner;
                Check(frame.Width % 2 == 0 && frame.Height % 2 == 0, where + ": odd frame");
                Check(inner.Size == content, where + ": inner size");
                Check(inner.X % 2 == 0 && inner.Y % 2 == 0, where + ": inner not on an even offset");
                Check(inner.X >= 0 && inner.Y >= 0 && inner.Right <= frame.Width && inner.Bottom <= frame.Height, where + ": inner outside the frame");
                int pad = Backdrop.PaddingFor(content, s);
                int left = inner.X, right = frame.Width - inner.Right, top = inner.Y, bottom = frame.Height - inner.Bottom;
                Check(Math.Min(Math.Min(left, right), Math.Min(top, bottom)) >= pad - 1, where + ": margin under the padding " + pad);
                Check(Math.Abs(left - right) <= 3 && Math.Abs(top - bottom) <= 3, where + ": not centred");
                double ratio = s.BgRatio == "16:9" ? 16.0 / 9 : s.BgRatio == "4:3" ? 4.0 / 3 : s.BgRatio == "1:1" ? 1 : 0;
                if (ratio > 0 && Math.Min(frame.Width, frame.Height) >= 100)
                    Check(Math.Abs(frame.Width / (double)frame.Height - ratio) / ratio <= 2.0 / Math.Min(frame.Width, frame.Height) + 0.002, where + ": ratio off");
                int rad = Backdrop.RadiusFor(content, s);
                Check(rad >= 0 && rad <= Math.Min(content.Width, content.Height) / 2, where + ": radius " + rad + " too big");
            }
        }

        static void Hunt2OutputSize()
        {
            Random r = new Random(4505);
            int[] maxes = { 0, 720, 1080 };
            for (int it = 0; it < 4000; it++)
            {
                Size crop = new Size(2 * r.Next(8, 2000), 2 * r.Next(8, 1200));
                ExportSpec s = new ExportSpec();
                s.Crop = new Rectangle(Point.Empty, crop);
                s.MaxHeight = maxes[r.Next(3)];
                Size o = VideoExport.OutputSize(s);
                string where = "case " + it + " crop " + crop + " max " + s.MaxHeight + " -> " + o;
                Check(o.Width >= 2 && o.Width % 2 == 0 && o.Height % 2 == 0, where + ": odd output");
                if (s.MaxHeight > 0 && crop.Height > s.MaxHeight)
                {
                    Check(o.Height == s.MaxHeight, where + ": height not the limit");
                    double want = crop.Width * (double)s.MaxHeight / crop.Height;
                    Check(Math.Abs(o.Width - want) <= 1.5, where + ": width not proportional");
                }
                else Check(o == crop, where + ": changed without a limit to pass");
            }
        }

        // ---- ExpectFor against real exports ----

        static string Hunt2Source(string dir, string name, int w, int h, string rate, double seconds, bool sound)
        {
            string mp4 = Path.Combine(dir, name);
            string secs = seconds.ToString("0.###", CultureInfo.InvariantCulture);
            string audio = sound ? "-f lavfi -i \"sine=f=440:d=" + secs + "\" -map 0:v -map 1:a -c:a aac -shortest " : "-map 0:v ";
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=size=" + w + "x" + h + ":rate=" + rate + ":duration=" + secs + "\" " +
                 audio + "-c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(mp4), 120000);
            return mp4;
        }

        static void Hunt2ExpectReal()
        {
            NeedFfmpeg();
            string dir = WorkDir("h2exp");
            Random r = new Random(4606);
            int[] sw = { 320, 640, 854, 1000, 642 }, sh = { 240, 360, 480, 810, 362 };
            string[] rates = { "24", "25", "30", "50", "60", "30000/1001", "24000/1001", "60000/1001" };
            int n = 30, bad = 0;
            for (int it = 0; it < n; it++)
            {
                int si = r.Next(sw.Length);
                int w1 = sw[si], h1 = sh[si];
                string rate1 = rates[r.Next(rates.Length)];
                double secs1 = 1.5 + r.Next(0, 16) / 10.0;
                bool joined = r.Next(3) == 0;
                bool sound1 = r.Next(2) == 0;
                string src1 = Hunt2Source(dir, "s1_" + it + ".mp4", w1, h1, rate1, secs1, sound1);
                string p1 = Probe(src1);
                double fps1;
                bool known = Editor.ConstantRate(p1, out fps1);
                double dur1 = DurationOf(p1);
                List<VideoClip> clips = new List<VideoClip>();
                VideoClip c1 = new VideoClip();
                c1.Path = src1; c1.Duration = dur1; c1.Fps = fps1; c1.Width = w1; c1.Height = h1;
                c1.HasAudio = Regex.IsMatch(p1, @"Stream #0:\d+[^\n]*: Audio:");
                clips.Add(c1);
                if (joined)
                {
                    int w2 = sw[r.Next(sw.Length)], h2 = sh[r.Next(sw.Length)];
                    string rate2 = rates[r.Next(rates.Length)];
                    string src2 = Hunt2Source(dir, "s2_" + it + ".mp4", w2, h2, rate2, 1.5 + r.Next(0, 10) / 10.0, r.Next(2) == 0);
                    string p2 = Probe(src2);
                    double f2;
                    Editor.ConstantRate(p2, out f2);
                    VideoClip c2 = new VideoClip();
                    c2.Path = src2; c2.Duration = DurationOf(p2); c2.Fps = f2; c2.Width = w2; c2.Height = h2;
                    c2.HasAudio = Regex.IsMatch(p2, @"Stream #0:\d+[^\n]*: Audio:");
                    clips.Add(c2);
                }
                ClipSeq.Layout(clips);
                double total = clips[clips.Count - 1].End;
                Timeline tl = new Timeline();
                tl.Init(dur1);
                if (joined) tl.Extend(total);
                for (int k = r.Next(0, 3); k > 0; k--)
                {
                    tl.Playhead = RandIn(r, 0.3, total - 0.3);
                    tl.SplitAtPlayhead();
                    if (r.Next(2) == 0 && tl.CanCut) tl.ToggleCut();
                }
                if (r.Next(3) == 0 && total > 1)
                {
                    tl.In = RandIn(r, 0.05, total / 3);
                    tl.Out = Math.Max(tl.In + 0.3, RandIn(r, total * 2 / 3, total));
                }
                tl.Speed = r.Next(3);
                tl.OutSize = r.Next(3);
                tl.Quality = r.Next(3);
                bool toGif = r.Next(8) == 0;

                // The crop of the editor: inside the first frame, even, at least 16 px.
                Rectangle crop = new Rectangle(r.Next(0, w1 / 4), r.Next(0, h1 / 4), 0, 0);
                crop.Width = r.Next(w1 / 3, w1 - crop.X + 1);
                crop.Height = r.Next(h1 / 3, h1 - crop.Y + 1);
                crop.Intersect(new Rectangle(0, 0, w1, h1));
                crop = new Rectangle(crop.X & ~1, crop.Y & ~1, Math.Max(16, crop.Width & ~1), Math.Max(16, crop.Height & ~1));
                crop.X = Math.Max(0, Math.Min(crop.X, (w1 - crop.Width) & ~1));
                crop.Y = Math.Max(0, Math.Min(crop.Y, (h1 - crop.Height) & ~1));

                bool trimmed = tl.IsTrimmed, cuts = tl.HasCuts;
                List<double[]> keep = trimmed || cuts || joined ? tl.Keep() : null;
                if (keep != null && keep.Count == 0) continue;
                double speed = Timeline.SpeedValues[tl.Speed];
                int maxH = Timeline.SizeValues[tl.OutSize];
                bool bg = r.Next(2) == 0;
                Settings bgs = new Settings();
                bgs.BgPadding = r.Next(0, 101);
                bgs.BgRadius = r.Next(0, 101);
                bgs.BgRatio = new string[] { "auto", "16:9", "4:3", "1:1" }[r.Next(4)];
                bgs.BgShadow = r.Next(2) == 0;
                Size frame = crop.Size;
                Rectangle inner = new Rectangle(Point.Empty, crop.Size);
                string overlay = null;
                if (bg)
                {
                    Backdrop.Measure(crop.Size, bgs, true, out frame, out inner);
                    using (Bitmap top = Backdrop.VideoTop(frame, inner, bgs, Backdrop.RadiusFor(crop.Size, bgs), null))
                    {
                        overlay = Path.Combine(dir, "top_" + it + ".png");
                        top.Save(overlay, ImageFormat.Png);
                    }
                }
                ExportSpec spec = new ExportSpec();
                spec.Source = src1;
                spec.Overlay = overlay;
                spec.Output = Path.Combine(dir, "out_" + it + (toGif ? ".gif" : ".mp4"));
                spec.Crop = crop;
                spec.Backdrop = bg;
                spec.Frame = frame;
                spec.Inner = inner;
                spec.Keep = keep;
                spec.Speed = speed;
                spec.MaxHeight = maxH;
                spec.ToGif = toGif;
                spec.HasAudio = joined ? ClipSeq.AnyAudio(clips) : c1.HasAudio;
                spec.Level = tl.Quality;
                spec.Fps = fps1;
                if (joined)
                {
                    spec.Clips = new List<VideoClip>(clips);
                    spec.Canvas = new Size(w1, h1);
                }
                string desc = "case " + it + ": src " + w1 + "x" + h1 + "@" + rate1 + " " + secs1.ToString("0.0", CultureInfo.InvariantCulture) + "s" +
                              (joined ? " + joined " + clips[1].Width + "x" + clips[1].Height : "") + (c1.HasAudio ? " sound" : "") +
                              " crop " + crop + " keep " + (keep == null ? "all" : keep.Count + " ranges") + " speed " + speed + " max " + maxH +
                              " bg " + bg + " gif " + toGif + " q " + spec.Level + " known " + known;
                string err;
                int code = Ff(VideoExport.Arguments(spec), out err, 300000);
                if (code != 0 || !File.Exists(spec.Output))
                {
                    bad++;
                    Add(desc + " | export failed (" + code + "): " + Tail(err));
                    continue;
                }
                ShareExpect e = Editor.ExpectFor(spec, joined ? total : dur1, fps1, known);
                ShareReport rep = ShareCheck.Validate(ffmpeg, spec.Output, e);
                if (!rep.Passed)
                {
                    bad++;
                    string text = rep.ToString().Replace("\n", " | ");
                    Add(desc + " | " + (text.Length > 500 ? text.Substring(0, 500) : text));
                }
                File.Delete(spec.Output);
            }
            Add((n - bad) + " of " + n + " random exports agree with ExpectFor");
            Check(bad == 0, bad + " of " + n + " random exports disagree with ExpectFor (see the detail)");
        }

        // ---- Saves ----

        // A path of exactly 'total' characters: nested folders, then the file name.
        static string Hunt2PathOfLength(string dir, int total, string name)
        {
            string cur = dir;
            while (total - cur.Length - 2 - name.Length > 41)
            {
                cur = Path.Combine(cur, new string('d', 40));
                Directory.CreateDirectory(cur);
            }
            int last = total - cur.Length - 2 - name.Length;
            cur = Path.Combine(cur, new string('e', last));
            Directory.CreateDirectory(cur);
            string file = Path.Combine(cur, name);
            Check(file.Length == total, "the test path has " + file.Length + " characters, not " + total);
            return file;
        }

        static void Hunt2Atomic259Control()
        {
            string dir = WorkDir("h2p259c");
            string ctrl = Hunt2PathOfLength(dir, 259, "ctrl001.png");
            File.WriteAllBytes(ctrl, new byte[] { 1, 2, 3 });
            Check(File.ReadAllBytes(ctrl).Length == 3, "a plain write at 259 characters does not work here");
            Add("a plain write at 259 characters works");
        }

        static void Hunt2Atomic259New()
        {
            string dir = WorkDir("h2p259");
            string file = Hunt2PathOfLength(dir, 259, "shot001.png");
            byte[] data = new byte[] { 9, 8, 7, 6 };
            try { AtomicFile.Write(file, data); }
            catch (Exception ex) { throw new TestFailed("a save to a 259-character path fails: " + ex.GetType().Name + ": " + ex.Message); }
            Check(File.Exists(file) && File.ReadAllBytes(file).Length == 4, "the file is not there after the save");
        }

        static void Hunt2AtomicOldIntact()
        {
            string dir = WorkDir("h2p255");
            string file = Hunt2PathOfLength(dir, 255, "keep.png");
            byte[] old = new byte[] { 1, 1, 2, 3, 5, 8 }, fresh = new byte[] { 4, 4, 4, 4, 4, 4, 4, 4, 4 };
            File.WriteAllBytes(file, old);
            string threw = "no error";
            try { AtomicFile.Write(file, fresh); }
            catch (Exception ex) { threw = ex.GetType().Name; }
            byte[] now = File.ReadAllBytes(file);
            bool isOld = now.Length == old.Length && now[0] == old[0] && now[5] == old[5];
            bool isNew = now.Length == fresh.Length && now[0] == fresh[0];
            Check(isOld || isNew, "the file is neither the old nor the new content: " + now.Length + " bytes");
            int tmps = Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length;
            Check(tmps == 0, tmps + " temporary file(s) left behind");
            Add("save " + threw + ", file " + (isOld ? "old" : "new") + " whole");
        }

        static void Hunt2AtomicLocked()
        {
            string dir = WorkDir("h2lock");
            string file = Path.Combine(dir, "locked.png");
            byte[] old = new byte[] { 1, 1, 1, 1 }, fresh = new byte[] { 2, 2, 2, 2, 2, 2 };
            File.WriteAllBytes(file, old);
            string threw = "no error";
            using (FileStream hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { AtomicFile.Write(file, fresh); }
                catch (Exception ex) { threw = ex.GetType().Name; }
            }
            byte[] now = File.ReadAllBytes(file);
            bool isOld = now.Length == old.Length, isNew = now.Length == fresh.Length;
            Check(isOld || isNew, "the file is neither the old nor the new content: " + now.Length + " bytes");
            Check(isNew || threw != "no error", "the save reported no error and kept the old content");
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "a temporary file is left behind");
            Add("save " + threw + ", file " + (isOld ? "old" : "new"));
        }

        static void Hunt2AtomicDirectory()
        {
            string dir = WorkDir("h2dir");
            string target = Path.Combine(dir, "adir.png");
            Directory.CreateDirectory(target);
            bool threw = false;
            try { AtomicFile.Write(target, new byte[] { 1, 2 }); }
            catch (Exception) { threw = true; }
            Check(threw, "a save over a folder reported success");
            Check(Directory.Exists(target), "the folder was replaced");
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "a temporary file is left behind");
        }

        static void Hunt2AtomicSweep()
        {
            string dir = WorkDir("h2sweep");
            List<string> failing = new List<string>();
            for (int len = 240; len <= 259; len++)
            {
                string ctrl = Hunt2PathOfLength(dir, len, "ctrl001.png");
                File.WriteAllBytes(ctrl, new byte[] { 1 });
                string file = Hunt2PathOfLength(dir, len, "shot001.png");
                try { AtomicFile.Write(file, new byte[] { 2, 2 }); }
                catch (Exception) { failing.Add(len.ToString(CultureInfo.InvariantCulture)); continue; }
                Check(File.ReadAllBytes(file).Length == 2, "the content saved at " + len + " characters is wrong");
            }
            Add("lengths that fail: " + (failing.Count == 0 ? "none" : string.Join(",", failing.ToArray())));
            Check(failing.Count == 0, "a save to a path of 240..259 characters fails at " + failing.Count + " lengths");
        }

        // ---- Image tools ----

        static void Hunt2Image1x1()
        {
            string dir = WorkDir("h2one");
            using (Bitmap one = Solid(1, 1, Color.FromArgb(255, 200, 30, 30)))
            using (Canvas c = new Canvas(one))
            {
                CallCanvas(c, "ApplyCrop", new RectangleF(0, 0, 1, 1));
                Check(c.CropRect == new Rectangle(0, 0, 1, 1), "a crop of a 1x1 image changed the area: " + c.CropRect);
                using (Bitmap r = c.Render())
                {
                    Check(r.Width == 1 && r.Height == 1, "render of 1x1 is " + r.Width + "x" + r.Height);
                    Check(r.GetPixel(0, 0).R == 200, "render of 1x1 lost its colour");
                    string png = Path.Combine(dir, "one.png");
                    Editor.Write(r, png);
                    using (Bitmap back = new Bitmap(png)) Check(back.Width == 1 && back.Height == 1, "the saved 1x1 PNG is " + back.Width + "x" + back.Height);
                }
            }
        }

        static void Hunt2Image20000()
        {
            string dir = WorkDir("h2wide");
            using (Bitmap wide = Gradient(20000, 200))
            using (Canvas c = new Canvas(wide))
            {
                CallCanvas(c, "ApplyCrop", new RectangleF(19990, 150, 10, 50));
                Check(c.CropRect == new Rectangle(19990, 150, 10, 50), "crop area is " + c.CropRect);
                CallCanvas(c, "ApplyCrop", new RectangleF(0, 0, 20000, 7));
                Check(c.CropRect == new Rectangle(19990, 150, 10, 50), "a 7 px tall crop was accepted: " + c.CropRect);
                using (Bitmap r = c.Render())
                {
                    Check(r.Width == 10 && r.Height == 50, "render is " + r.Width + "x" + r.Height);
                    Check(r.GetPixel(0, 0) == wide.GetPixel(19990, 150), "top-left pixel differs from the source");
                    Check(r.GetPixel(9, 49) == wide.GetPixel(19999, 199), "bottom-right pixel differs from the source");
                    string png = Path.Combine(dir, "wide.png");
                    Editor.Write(r, png);
                    using (Bitmap back = new Bitmap(png)) Check(back.Width == 10 && back.Height == 50, "saved PNG is " + back.Width + "x" + back.Height);
                }
            }
        }
    }
}

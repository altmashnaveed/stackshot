// Stackshot - The videos joined in one editor session: where each one sits on the timeline and how a time maps to a clip.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Stackshot
{
    // One video of the sequence. The first one defines the output canvas (size and frame rate).
    public sealed class VideoClip
    {
        public string Path;
        public double Duration, Fps, Offset;     // Offset: where it starts on the timeline, set by ClipSeq.Layout
        public int Width, Height;
        public bool HasAudio, Rgb;
        public string Matrix;                    // how a player reads its YUV ("bt709", "smpte170m"...); null: unknown, left as it is
        public bool FullRange;
        public Bitmap[] Thumbs;
        internal MediaInfo Info;
        internal Playback Play;

        // The colour matrix and range as a player would read the file, for the joined export to bring them to BT.709.
        internal void TakeColour(MediaInfo mi)
        {
            if (mi == null) return;
            Matrix = VideoExport.ClipMatrix(mi.ColorMatrix, mi.Width, mi.Height);
            FullRange = mi.FullRange;
        }

        public string Name { get { return System.IO.Path.GetFileNameWithoutExtension(Path ?? ""); } }
        public double End { get { return Offset + Duration; } }
    }

    // A piece of one clip that goes into the export: local times in that clip.
    internal sealed class ClipSegment
    {
        public VideoClip Clip;
        public double A, B;
        public bool Whole;      // the clip from start to end: read without -ss / -to
    }

    internal static class ClipSeq
    {
        // Shorter than this (about half a frame at 30 fps) a piece is only a rounding sliver at a clip boundary.
        const double Sliver = 0.02;

        // Offsets in order; returns the total length.
        internal static double Layout(List<VideoClip> clips)
        {
            double t = 0;
            foreach (VideoClip c in clips)
            {
                c.Offset = t;
                t += c.Duration;
            }
            return t;
        }

        // The clip that holds timeline time t (a boundary belongs to the clip that starts there; past the end, the last
        // one) and the time inside it.
        internal static int Locate(List<VideoClip> clips, double t, out double local)
        {
            local = 0;
            if (clips.Count == 0) return -1;
            for (int i = 0; i < clips.Count; i++)
            {
                bool last = i == clips.Count - 1;
                if (t < clips[i].Offset + clips[i].Duration || last)
                {
                    local = Math.Max(0, Math.Min(clips[i].Duration, t - clips[i].Offset));
                    return i;
                }
            }
            return clips.Count - 1;
        }

        // The kept timeline ranges (null: everything) cut at the clip boundaries, in order.
        internal static List<ClipSegment> Segments(List<VideoClip> clips, List<double[]> keep)
        {
            List<ClipSegment> r = new List<ClipSegment>();
            double total = 0;
            foreach (VideoClip c in clips) total += c.Duration;
            List<double[]> ranges = keep;
            if (ranges == null) { ranges = new List<double[]>(); ranges.Add(new double[] { 0, total }); }
            foreach (double[] k in ranges)
            {
                double off = 0;
                foreach (VideoClip c in clips)
                {
                    double a = Math.Max(k[0], off), b = Math.Min(k[1], off + c.Duration);
                    if (b - a >= Sliver)
                    {
                        ClipSegment s = new ClipSegment();
                        s.Clip = c;
                        s.A = a - off;
                        s.B = b - off;
                        s.Whole = s.A <= 0.0005 && s.B >= c.Duration - 0.0005;
                        if (s.Whole) { s.A = 0; s.B = c.Duration; }
                        r.Add(s);
                    }
                    off += c.Duration;
                }
            }
            return r;
        }

        // The rectangle a clip of this size fills inside the canvas: as large as fits, centred (what the export's
        // scale + pad does).
        internal static Rectangle Fit(Size src, Size canvas)
        {
            if (src.Width <= 0 || src.Height <= 0 || canvas.Width <= 0 || canvas.Height <= 0) return new Rectangle(Point.Empty, canvas);
            double k = Math.Min(canvas.Width / (double)src.Width, canvas.Height / (double)src.Height);
            int w = Math.Max(1, Math.Min(canvas.Width, (int)Math.Round(src.Width * k)));
            int h = Math.Max(1, Math.Min(canvas.Height, (int)Math.Round(src.Height * k)));
            return new Rectangle((canvas.Width - w) / 2, (canvas.Height - h) / 2, w, h);
        }

        // Moves the clip at index by dir (-1 / +1); false when it cannot.
        internal static bool Move(List<VideoClip> clips, int index, int dir)
        {
            int to = index + dir;
            if (index < 0 || index >= clips.Count || to < 0 || to >= clips.Count) return false;
            VideoClip c = clips[index];
            clips.RemoveAt(index);
            clips.Insert(to, c);
            return true;
        }

        // How the times of a timeline change when the clip that spans start..start+len is removed (for Timeline.Relayout;
        // the flag says the time is the end of a span, which belongs to the clip it closes).
        internal static Func<double, bool, double> RemovedMap(double start, double len)
        {
            return delegate(double t, bool end)
            {
                if (end ? t <= start + 0.0005 : t < start) return t;
                if (end ? t <= start + len + 0.0005 : t < start + len) return start;
                return t - len;
            };
        }

        // The same for clips that were reordered: before is the old order (with Offset already laid out again for the
        // new one) and oldOffsets the starts it had.
        internal static Func<double, bool, double> MovedMap(List<VideoClip> before, double[] oldOffsets)
        {
            return delegate(double t, bool end)
            {
                int hit = end ? 0 : before.Count - 1;
                for (int k = 0; k < before.Count; k++)
                {
                    double a = oldOffsets[k], b = a + before[k].Duration;
                    if (end ? (t > a + 0.0005 && t <= b + 0.0005) : (t >= a - 0.0005 && t < b - 0.0005)) { hit = k; break; }
                }
                return before[hit].Offset + (t - oldOffsets[hit]);
            };
        }

        internal static double[] Offsets(List<VideoClip> clips)
        {
            double[] o = new double[clips.Count];
            for (int i = 0; i < o.Length; i++) o[i] = clips[i].Offset;
            return o;
        }

        internal static bool AnyAudio(List<VideoClip> clips)
        {
            foreach (VideoClip c in clips) if (c.HasAudio) return true;
            return false;
        }
    }
}

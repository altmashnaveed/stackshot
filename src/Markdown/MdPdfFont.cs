// Stackshot - TrueType fonts for the PDF export: the glyphs a document uses copied into a small font of their own (glyph
// numbers kept, so the PDF can name them directly), with the metrics the PDF needs.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace Stackshot
{
    internal sealed class MdPdfFont
    {
        readonly byte[] data;
        readonly Dictionary<string, int[]> tables = new Dictionary<string, int[]>(StringComparer.Ordinal);
        readonly bool longLoca;
        readonly int numHMetrics;
        public readonly int UnitsPerEm, NumGlyphs, Ascent, Descent, CapHeight;
        public readonly int[] Box = new int[4];
        public readonly double ItalicAngle;
        public readonly bool Fixed;

        // The font behind a WPF typeface when it can go into a PDF: TrueType outlines (not CFF), not a variable font (a PDF
        // reader would show its default instance), and a licence that allows embedding. Null otherwise. files: whole font
        // files already read during this export.
        public static MdPdfFont Open(GlyphTypeface face, Dictionary<string, byte[]> files)
        {
            try
            {
                if (face.EmbeddingRights == FontEmbeddingRight.RestrictedLicense) return null;
                Uri u = face.FontUri;
                string key = u.GetLeftPart(UriPartial.Path);
                byte[] all;
                if (!files.TryGetValue(key, out all))
                {
                    using (Stream s = face.GetFontStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        all = ms.ToArray();
                    }
                    files[key] = all;
                }
                int index = 0;
                if (!string.IsNullOrEmpty(u.Fragment)) int.TryParse(u.Fragment.TrimStart('#'), out index);
                MdPdfFont f = new MdPdfFont(all, index);
                return f.NumGlyphs > 0 ? f : null;
            }
            catch (Exception ex)
            {
                ShotStack.Log("PDF fuente " + face.FontUri + ": " + ex.Message);
                return null;
            }
        }

        MdPdfFont(byte[] all, int index)
        {
            data = all;
            int start = 0;
            if (Tag(0) == "ttcf")
            {
                if (index < 0 || index >= (int)U32(8)) return;
                start = (int)U32(12 + 4 * index);
            }
            uint version = U32(start);
            if (version != 0x00010000 && version != 0x74727565) return;
            int n = U16(start + 4);
            for (int i = 0; i < n; i++)
            {
                int rec = start + 12 + 16 * i;
                int off = (int)U32(rec + 8), len = (int)U32(rec + 12);
                if (off < 0 || len < 0 || (long)off + len > data.Length) continue;
                tables[Tag(rec)] = new int[] { off, len };
            }
            foreach (string t in new string[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf" }) if (!tables.ContainsKey(t)) return;
            if (tables.ContainsKey("fvar") || tables["head"][1] < 54 || tables["hhea"][1] < 36 || tables["maxp"][1] < 6) return;
            int head = tables["head"][0], hhea = tables["hhea"][0];
            UnitsPerEm = Math.Max(16, (int)U16(head + 18));
            for (int i = 0; i < 4; i++) Box[i] = S16(head + 36 + 2 * i);
            longLoca = S16(head + 50) == 1;
            Ascent = S16(hhea + 4);
            Descent = S16(hhea + 6);
            numHMetrics = U16(hhea + 34);
            CapHeight = Ascent * 7 / 10;
            int[] os2;
            if (tables.TryGetValue("OS/2", out os2) && os2[1] >= 90 && U16(os2[0]) >= 2) CapHeight = S16(os2[0] + 88);
            int[] post;
            if (tables.TryGetValue("post", out post) && post[1] >= 16)
            {
                ItalicAngle = (int)U32(post[0] + 4) / 65536.0;
                Fixed = U32(post[0] + 12) != 0;
            }
            int glyphs = U16(tables["maxp"][0] + 4);
            if (numHMetrics < 1 || numHMetrics > glyphs || tables["hmtx"][1] < numHMetrics * 4 + (glyphs - numHMetrics) * 2) return;
            if (tables["loca"][1] < (glyphs + 1) * (longLoca ? 4 : 2)) return;
            NumGlyphs = glyphs;
        }

        string Tag(int at) { return Encoding.ASCII.GetString(data, at, 4); }
        uint U32(int at) { return (uint)(data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3]); }
        int U16(int at) { return data[at] << 8 | data[at + 1]; }
        int S16(int at) { return (short)(data[at] << 8 | data[at + 1]); }

        // A glyph's advance in thousandths of the em, as the PDF's widths are given.
        public int Width(int g)
        {
            if (g < 0 || g >= NumGlyphs) return 0;
            int hmtx = tables["hmtx"][0];
            int adv = U16(hmtx + 4 * Math.Min(g, numHMetrics - 1));
            return (int)Math.Round(adv * 1000.0 / UnitsPerEm);
        }

        public int Scale(int units) { return (int)Math.Round(units * 1000.0 / UnitsPerEm); }

        void Glyph(int g, out int off, out int len)
        {
            int loca = tables["loca"][0];
            int a = longLoca ? (int)U32(loca + 4 * g) : U16(loca + 2 * g) * 2;
            int b = longLoca ? (int)U32(loca + 4 * g + 4) : U16(loca + 2 * g + 2) * 2;
            int glyfLen = tables["glyf"][1];
            if (a < 0 || b < a || b > glyfLen) { off = 0; len = 0; return; }
            off = tables["glyf"][0] + a;
            len = b - a;
        }

        // The glyphs a composite glyph is built from (and theirs).
        void Parts(int g, HashSet<int> keep, int depth)
        {
            int off, len;
            Glyph(g, out off, out len);
            if (len < 12 || depth > 8 || S16(off) >= 0) return;
            int p = off + 10, end = off + len;
            while (p + 4 <= end)
            {
                int flags = U16(p), part = U16(p + 2);
                p += 4;
                if (part < NumGlyphs && keep.Add(part)) Parts(part, keep, depth + 1);
                p += (flags & 0x0001) != 0 ? 4 : 2;
                if ((flags & 0x0008) != 0) p += 2;
                else if ((flags & 0x0040) != 0) p += 4;
                else if ((flags & 0x0080) != 0) p += 8;
                if ((flags & 0x0020) == 0) break;
            }
        }

        // A font with only these glyphs drawn (the others empty, so the numbers stay) and the tables a PDF reader uses; it
        // ends at the highest glyph used, so a single emoji does not bring the metrics of sixty thousand others along.
        public byte[] Subset(IEnumerable<ushort> used)
        {
            HashSet<int> keep = new HashSet<int>();
            keep.Add(0);
            foreach (ushort g in used) if (g < NumGlyphs && keep.Add(g)) Parts(g, keep, 0);
            int n = 0;
            foreach (int g in keep) n = Math.Max(n, g + 1);
            MemoryStream glyf = new MemoryStream();
            byte[] loca = new byte[(n + 1) * 4];
            for (int g = 0; g <= n; g++)
            {
                long at = glyf.Length;
                loca[4 * g] = (byte)(at >> 24); loca[4 * g + 1] = (byte)(at >> 16); loca[4 * g + 2] = (byte)(at >> 8); loca[4 * g + 3] = (byte)at;
                if (g == n || !keep.Contains(g)) continue;
                int off, len;
                Glyph(g, out off, out len);
                if (len <= 0) continue;
                glyf.Write(data, off, len);
                while (glyf.Length % 4 != 0) glyf.WriteByte(0);
            }
            byte[] head = Copy("head");
            head[8] = head[9] = head[10] = head[11] = 0;
            head[50] = 0; head[51] = 1;
            // The horizontal metrics of the glyphs kept: full records first, then the left side bearings after them.
            int full = Math.Min(numHMetrics, n);
            byte[] hmtx = new byte[full * 4 + (n - full) * 2];
            int hm = tables["hmtx"][0];
            Array.Copy(data, hm, hmtx, 0, full * 4);
            if (n > full) Array.Copy(data, hm + numHMetrics * 4 + (full - numHMetrics) * 2, hmtx, full * 4, (n - full) * 2);
            byte[] hhea = Copy("hhea");
            hhea[34] = (byte)(full >> 8); hhea[35] = (byte)full;
            byte[] maxp = Copy("maxp");
            maxp[4] = (byte)(n >> 8); maxp[5] = (byte)n;
            byte[] post = new byte[32];
            int[] p;
            if (tables.TryGetValue("post", out p) && p[1] >= 32) Array.Copy(data, p[0], post, 0, 32);
            post[0] = 0; post[1] = 3; post[2] = 0; post[3] = 0;
            SortedDictionary<string, byte[]> o = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string t in new string[] { "OS/2", "cvt ", "fpgm", "prep", "name" }) if (tables.ContainsKey(t)) o[t] = Copy(t);
            o["glyf"] = glyf.ToArray();
            o["loca"] = loca;
            o["head"] = head;
            o["hhea"] = hhea;
            o["hmtx"] = hmtx;
            o["maxp"] = maxp;
            o["post"] = post;
            return Build(o);
        }

        byte[] Copy(string t)
        {
            int[] r = tables[t];
            byte[] b = new byte[r[1]];
            Array.Copy(data, r[0], b, 0, r[1]);
            return b;
        }

        static byte[] Build(SortedDictionary<string, byte[]> t)
        {
            int n = t.Count, pow = 1, log = 0;
            while (pow * 2 <= n) { pow *= 2; log++; }
            MemoryStream ms = new MemoryStream();
            BinaryWriterBE w = new BinaryWriterBE(ms);
            w.U32(0x00010000);
            w.U16(n); w.U16(pow * 16); w.U16(log); w.U16(n * 16 - pow * 16);
            long at = 12 + 16 * n;
            long headAt = 0;
            foreach (KeyValuePair<string, byte[]> kv in t)
            {
                w.Tag(kv.Key);
                w.U32(Sum(kv.Value));
                w.U32((uint)at);
                w.U32((uint)kv.Value.Length);
                if (kv.Key == "head") headAt = at;
                at += (kv.Value.Length + 3) & ~3;
            }
            foreach (KeyValuePair<string, byte[]> kv in t)
            {
                ms.Write(kv.Value, 0, kv.Value.Length);
                while (ms.Length % 4 != 0) ms.WriteByte(0);
            }
            byte[] font = ms.ToArray();
            uint adjust = unchecked(0xB1B0AFBA - Sum(font));
            font[headAt + 8] = (byte)(adjust >> 24); font[headAt + 9] = (byte)(adjust >> 16); font[headAt + 10] = (byte)(adjust >> 8); font[headAt + 11] = (byte)adjust;
            return font;
        }

        static uint Sum(byte[] b)
        {
            uint s = 0;
            for (int i = 0; i < b.Length; i += 4)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < b.Length ? b[i + k] : (byte)0);
                s = unchecked(s + v);
            }
            return s;
        }

        sealed class BinaryWriterBE
        {
            readonly Stream s;
            public BinaryWriterBE(Stream s) { this.s = s; }
            public void U16(int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
            public void U32(uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
            public void Tag(string t) { byte[] b = Encoding.ASCII.GetBytes(t); s.Write(b, 0, 4); }
        }
    }
}

// Stackshot - checks that an exported MP4 or GIF is complete, plays and has what was asked for, before it is offered.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Stackshot
{
    // What the export was meant to produce.
#pragma warning disable 649   // filled in by the caller
    internal sealed class ShareExpect
    {
        public int Width, Height;
        public double Fps, Seconds;               // 0: not checked
        public bool HasAudio, Gif, RgbSource;     // RgbSource: the colours were converted here, so they must be tagged BT.709
        public bool CentredChroma;                // the 4:2:0 chroma was made here and the stream must say where it sits
        public int Level;                         // H.264 level x10 the stream must declare (VideoExport.H264Level); 0: not checked
        public long MaxBytes;                     // a size limit the file must fit (VideoExport.TargetBytes); 0: none
        public double Slack;                      // extra seconds of duration (and the frames in them) accepted, e.g. one cut piece joins with a gap
    }
#pragma warning restore 649

    internal sealed class ShareReport
    {
        internal sealed class Check
        {
            public string Name, Detail;
            public bool Passed;
        }

        public bool Passed;
        public List<Check> Checks = new List<Check>();
        public string Summary;

        public Check Find(string name)
        {
            foreach (Check c in Checks) if (c.Name == name) return c;
            return null;
        }

        public bool Failed(string name)
        {
            Check c = Find(name);
            return c != null && !c.Passed;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Check c in Checks) sb.Append(c.Passed ? "ok    " : "FALLO ").Append(c.Name).Append(": ").Append(c.Detail).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }

    internal sealed class Mp4Boxes
    {
        public List<string> Types = new List<string>();
        public List<long> Sizes = new List<long>();
        public long Length;
        public string Error;   // null when the top-level boxes add up exactly to the file length
    }

    internal sealed class ShareAudio
    {
        public string Codec, Profile, Layout;
        public int Rate;
    }

    // The relevant parts of what "ffmpeg -i file" prints about a file.
    internal sealed class ShareProbe
    {
        public double Seconds = -1, Fps = -1;
        public int VideoStreams, Width, Height;
        public string VideoCodec, Profile, PixFmt, Color;
        public List<ShareAudio> Audio = new List<ShareAudio>();
    }

    internal static class ShareCheck
    {
        internal const string NameFile = "Archivo";
        internal const string NameStructure = "Estructura MP4";
        internal const string NameFaststart = "Inicio r\u00E1pido";
        internal const string NameVideo = "V\u00EDdeo";
        internal const string NameSize = "Tama\u00F1o";
        internal const string NameFps = "Fotogramas por segundo";
        internal const string NameColor = "Color";
        internal const string NameAudio = "Sonido";
        internal const string NameDuration = "Duraci\u00F3n";
        internal const string NameDecode = "Lectura completa";
        internal const string NameFrames = "N\u00FAmero de fotogramas";
        internal const string NameLevel = "Nivel H.264";
        internal const string NameFileSize = "L\u00EDmite de tama\u00F1o";
        internal const string NameCheck = "Comprobaci\u00F3n";

        // The top-level boxes of an MP4: type and size of each, and whether they cover the file exactly.
        internal static Mp4Boxes WalkBoxes(Stream s)
        {
            Mp4Boxes r = new Mp4Boxes();
            long len = s.Length, pos = 0;
            r.Length = len;
            byte[] h = new byte[16];
            while (pos < len)
            {
                if (len - pos < 8) { r.Error = "sobran " + (len - pos) + " bytes al final"; return r; }
                s.Position = pos;
                if (!Fill(s, h, 8)) { r.Error = "no se puede leer en " + pos; return r; }
                long size = ((long)h[0] << 24) | ((long)h[1] << 16) | ((long)h[2] << 8) | h[3];
                string type = Encoding.ASCII.GetString(h, 4, 4);
                long head = 8;
                if (size == 1)
                {
                    if (len - pos < 16 || !Fill(s, h, 8)) { r.Error = "la caja '" + type + "' est\u00E1 cortada"; return r; }
                    size = 0;
                    for (int i = 0; i < 8; i++) size = (size << 8) | h[i];
                    head = 16;
                }
                else if (size == 0) size = len - pos;
                if (size < head) { r.Error = "la caja '" + type + "' en " + pos + " tiene un tama\u00F1o imposible"; return r; }
                r.Types.Add(type);
                r.Sizes.Add(size);
                if (pos + size > len || size < 0)
                {
                    r.Error = "la caja '" + type + "' declara " + size + " bytes y solo quedan " + (len - pos) + " (archivo cortado)";
                    return r;
                }
                pos += size;
            }
            return r;
        }

        static bool Fill(Stream s, byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        internal static ShareProbe ParseProbe(string text)
        {
            ShareProbe p = new ShareProbe();
            text = text ?? "";
            Match d = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            if (d.Success)
                p.Seconds = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
                            double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
            foreach (string line in text.Split('\n'))
            {
                Match m = Regex.Match(line, @"^\s*Stream #\d+:\d+[^\n]*?: (Video|Audio): ([A-Za-z0-9_]+)(?: \(([^)]*)\))?");
                if (!m.Success) continue;
                if (m.Groups[1].Value == "Video")
                {
                    p.VideoStreams++;
                    if (p.VideoStreams > 1) continue;
                    p.VideoCodec = m.Groups[2].Value;
                    p.Profile = m.Groups[3].Success ? m.Groups[3].Value : null;
                    Match v = Regex.Match(line, @",\s*([a-z][a-z0-9_]*)(?:\(([^)]*)\))?,\s*(\d{1,5})x(\d{1,5})");
                    if (v.Success)
                    {
                        p.PixFmt = v.Groups[1].Value;
                        p.Color = v.Groups[2].Success ? v.Groups[2].Value : null;
                        p.Width = int.Parse(v.Groups[3].Value, CultureInfo.InvariantCulture);
                        p.Height = int.Parse(v.Groups[4].Value, CultureInfo.InvariantCulture);
                    }
                    Match f = Regex.Match(line, @"(\d+(?:\.\d+)?) fps");
                    if (f.Success) p.Fps = double.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    ShareAudio a = new ShareAudio();
                    a.Codec = m.Groups[2].Value;
                    a.Profile = m.Groups[3].Success ? m.Groups[3].Value : null;
                    Match r = Regex.Match(line, @",\s*(\d+) Hz,\s*([^,\r\n]+)");
                    if (r.Success)
                    {
                        a.Rate = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
                        a.Layout = r.Groups[2].Value.Trim();
                    }
                    p.Audio.Add(a);
                }
            }
            return p;
        }

        // What the first sequence parameter set of an H.264 stream declares, from FFmpeg's trace_headers.
        internal sealed class SpsInfo
        {
            public int Level = -1;        // level_idc (x10)
            public int ChromaLoc = -1;    // chroma_sample_loc_type; -1 when the stream does not say (H.264 then means 0, left)
        }

        internal static SpsInfo ParseSps(string trace)
        {
            SpsInfo s = new SpsInfo();
            Match l = Regex.Match(trace ?? "", @"\blevel_idc\s+[01]+\s*=\s*(\d+)");
            if (l.Success) s.Level = int.Parse(l.Groups[1].Value, CultureInfo.InvariantCulture);
            Match c = Regex.Match(trace ?? "", @"\bchroma_sample_loc_type_top_field\s+[01]+\s*=\s*(\d+)");
            if (c.Success) s.ChromaLoc = int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture);
            return s;
        }

        internal static string LevelName(int level) { return (level / 10) + "." + (level % 10); }

        // Frames listed by "-f framecrc": every line that is not a comment.
        internal static int CountFrameLines(IEnumerable<string> lines)
        {
            int n = 0;
            foreach (string l in lines)
                if (l.Length > 0 && l[0] != '#') n++;
            return n;
        }

        static void Add(ShareReport r, string name, bool ok, string detail)
        {
            ShareReport.Check c = new ShareReport.Check();
            c.Name = name;
            c.Passed = ok;
            c.Detail = detail;
            r.Checks.Add(c);
        }

        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // Opens the file the way a player would and checks it against what was asked for. Takes about as long as playing
        // the file through once at several times its speed; call it from a background thread.
        internal static ShareReport Validate(string ff, string path, ShareExpect e)
        {
            ShareReport r = new ShareReport();
            try { Run(r, ff, path, e); }
            catch (Exception ex) { Add(r, NameCheck, false, ex.Message); }
            bool all = true;
            foreach (ShareReport.Check c in r.Checks) if (!c.Passed) all = false;
            r.Passed = all;
            if (all) r.Summary = I18n.T("El v\u00EDdeo se ha comprobado: se abre entero y tiene el tama\u00F1o, la duraci\u00F3n y el sonido esperados.", "The video was verified: it opens completely and has the expected size, duration and sound.");
            else
            {
                StringBuilder sb = new StringBuilder(I18n.T("El v\u00EDdeo no pasa la comprobaci\u00F3n: ", "The video did not pass verification: "));
                bool first = true;
                foreach (ShareReport.Check c in r.Checks)
                {
                    if (c.Passed) continue;
                    sb.Append(first ? "" : "; ").Append(c.Name).Append(" (").Append(c.Detail).Append(')');
                    first = false;
                }
                r.Summary = sb.ToString();
            }
            return r;
        }

        static void Run(ShareReport r, string ff, string path, ShareExpect e)
        {
            long size = File.Exists(path) ? new FileInfo(path).Length : -1;
            Add(r, NameFile, size > 1024, size < 0 ? "no existe" : size + " bytes" + (size > 1024 ? "" : ", demasiado peque\u00F1o"));
            if (size <= 1024) return;
            if (e.MaxBytes > 0)
                Add(r, NameFileSize, size <= e.MaxBytes, (size / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB, como mucho " + (e.MaxBytes / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB");

            if (e.Gif)
            {
                // A complete GIF starts with its signature and ends with the 0x3B trailer.
                byte[] head = new byte[6];
                int last;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    Fill(fs, head, 6);
                    fs.Position = fs.Length - 1;
                    last = fs.ReadByte();
                }
                string sig = Encoding.ASCII.GetString(head);
                bool whole = (sig == "GIF89a" || sig == "GIF87a") && last == 0x3B;
                Add(r, NameStructure, whole, whole ? "GIF completo" : "el GIF est\u00E1 cortado o no es un GIF");
            }
            else
            {
                Mp4Boxes b;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) b = WalkBoxes(fs);
                bool ftyp = b.Types.Count > 0 && b.Types[0] == "ftyp";
                int moov = b.Types.IndexOf("moov"), mdat = b.Types.IndexOf("mdat");
                string why = b.Error ?? (!ftyp ? "no empieza por 'ftyp'" : moov < 0 ? "falta 'moov'" : mdat < 0 ? "falta 'mdat'" : null);
                Add(r, NameStructure, why == null, why ?? string.Join(" ", b.Types.ToArray()));
                if (moov >= 0 && mdat >= 0)
                    Add(r, NameFaststart, moov < mdat, moov < mdat ? "el \u00EDndice va antes de los datos" : "el \u00EDndice va despu\u00E9s de los datos: no empieza a verse hasta que baja entero");
            }

            string err;
            string safe = Recorder.SafeInput;
            int code = VideoExport.Run(ff, "-hide_banner " + safe + "-i " + Recorder.Quote(path), 30000, out err);
            ShareProbe p = ParseProbe(err);
            if (code == -1) { Add(r, NameVideo, false, "FFmpeg no termin\u00F3 de leer el archivo"); return; }

            if (e.Gif)
            {
                Add(r, NameVideo, p.VideoStreams == 1 && p.VideoCodec == "gif", p.VideoStreams + " v\u00EDdeo(s), " + (p.VideoCodec ?? "sin c\u00F3dec"));
                Add(r, NameSize, p.Width == e.Width && p.Height == e.Height, p.Width + "x" + p.Height + ", esperado " + e.Width + "x" + e.Height);
            }
            else
            {
                bool profile = p.Profile == "High" || p.Profile == "Main" || p.Profile == "Constrained Baseline";
                bool ok = p.VideoStreams == 1 && p.VideoCodec == "h264" && profile && p.PixFmt == "yuv420p";
                Add(r, NameVideo, ok, p.VideoStreams + " v\u00EDdeo(s), " + (p.VideoCodec ?? "sin c\u00F3dec") + " (" + (p.Profile ?? "sin perfil") + "), " + (p.PixFmt ?? "sin formato") +
                                      (ok ? "" : "; se esperaba h264 High/Main, yuv420p"));
                Add(r, NameSize, p.Width == e.Width && p.Height == e.Height, p.Width + "x" + p.Height + ", esperado " + e.Width + "x" + e.Height);
                if (e.Fps > 0) Add(r, NameFps, p.Fps > 0 && Math.Abs(p.Fps - e.Fps) <= 0.01, N(p.Fps) + ", esperado " + N(e.Fps));
                // The level and the chroma location are read from the stream's own header, not from what was asked of x264.
                SpsInfo sps = null;
                if (e.Level > 0 || e.CentredChroma)
                {
                    string trace;
                    VideoExport.Run(ff, "-hide_banner " + safe + "-i " + Recorder.Quote(path) + " -map 0:v:0 -c copy -bsf:v trace_headers -frames:v 1 -f null -", 30000, out trace);
                    sps = ParseSps(trace);
                }
                if (e.Level > 0)
                    Add(r, NameLevel, sps.Level == e.Level, sps.Level < 0 ? "no se pudo leer" : LevelName(sps.Level) + (sps.Level == e.Level ? "" : ", esperado " + LevelName(e.Level)));
                if (e.RgbSource)
                {
                    string col = p.Color ?? "";
                    bool tagged = col.Contains("tv") && col.Contains("bt709/bt709/iec61966-2-1");
                    bool sited = !e.CentredChroma || sps.ChromaLoc == 1;
                    Add(r, NameColor, tagged && sited, !tagged ? "sin las etiquetas BT.709 (" + (col.Length == 0 ? "ninguna" : col) + ")" :
                                                       !sited ? "BT.709, pero no dice que el croma va centrado" :
                                                       "BT.709, rango limitado" + (e.CentredChroma ? ", croma centrado" : ""));
                }
                bool audio;
                string adetail;
                if (e.HasAudio)
                {
                    ShareAudio a = p.Audio.Count > 0 ? p.Audio[0] : null;
                    audio = p.Audio.Count == 1 && a.Codec == "aac" && a.Profile == "LC" && a.Rate == 48000 && a.Layout == "stereo";
                    adetail = p.Audio.Count == 1 ? a.Codec + " (" + a.Profile + "), " + a.Rate + " Hz, " + a.Layout : p.Audio.Count + " pistas de sonido, esperada 1";
                }
                else
                {
                    audio = p.Audio.Count == 0;
                    adetail = audio ? "sin sonido, como se esperaba" : p.Audio.Count + " pistas de sonido, esperadas 0";
                }
                Add(r, NameAudio, audio, adetail);
                if (e.Seconds > 0)
                {
                    double tol = Math.Max(0.15, e.Fps > 0 ? 2 / e.Fps : 0) + e.Slack;
                    Add(r, NameDuration, p.Seconds >= 0 && Math.Abs(p.Seconds - e.Seconds) <= tol, N(p.Seconds) + " s, esperado " + N(e.Seconds) + " s");
                }
            }

            // One pass decodes everything (errors show on stderr) and lists the video frames for the count. Every frame
            // keeps the file's own time: by default the outputs would round them to the nominal rate, and two frames of a
            // variable-rate video falling on one tick read as an error ("non monotonically increasing dts").
            double dur = e.Seconds > 0 ? e.Seconds : Math.Max(0, p.Seconds);
            int timeout = (int)Math.Min(30 * 60 * 1000.0, 30000 + dur * 3000);
            int frames = 0;
            object sync = new object();
            const string asIs = " -fps_mode passthrough -enc_time_base demux";
            string dargs = "-hide_banner -v error " + safe + "-i " + Recorder.Quote(path) + " -map 0" + asIs + " -f null - -map 0:v:0" + asIs + " -f framecrc -";
            code = VideoExport.Run(ff, dargs, timeout, delegate(string line) { if (line.Length > 0 && line[0] != '#') lock (sync) frames++; }, out err);
            err = (err ?? "").Trim();
            if (code == -1) Add(r, NameDecode, false, "tard\u00F3 demasiado y se cancel\u00F3");
            else
            {
                string tail = err.Length > 160 ? err.Substring(0, 160) + "\u2026" : err;
                Add(r, NameDecode, code == 0 && err.Length == 0, code == 0 && err.Length == 0 ? "sin errores" : "FFmpeg sali\u00F3 con " + code + (err.Length > 0 ? ": " + tail.Replace("\r", " ").Replace("\n", " | ") : ""));
            }
            if (!e.Gif && e.Fps > 0 && e.Seconds > 0 && code != -1)
            {
                int want = (int)Math.Round(e.Seconds * e.Fps);
                int got;
                lock (sync) got = frames;
                int tolFrames = 1 + (int)Math.Ceiling(e.Slack * e.Fps);
                Add(r, NameFrames, Math.Abs(got - want) <= tolFrames, got + " fotogramas, esperados " + want);
            }
        }
    }
}

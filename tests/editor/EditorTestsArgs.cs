// Pure tests of the export command line, source detection and the frame strip's seeks (no FFmpeg needed).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        const string GbrpProbe = "  Stream #0:0: Video: h264 (High 4:4:4 Predictive), gbrp(pc, gbr/unknown/unknown, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 60 fps, 60 tbr, 1k tbn\n";
        const string Yuv709Probe = "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709/bt709/iec61966-2-1, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 60 fps\n  Stream #0:1[0x2](und): Audio: aac (LC), 48000 Hz, stereo\n";
        const string GifProbe = "  Stream #0:0: Video: gif, bgra, 320x240 [SAR 64:64 DAR 4:3], 15 fps, 15 tbr, 100 tbn\n";

        static ExportSpec Spec(bool rgb, bool gif)
        {
            ExportSpec s = new ExportSpec();
            s.Source = @"C:\v\clip.mkv";
            s.Output = @"C:\v\clip (editado)." + (gif ? "gif" : "mp4");
            s.Crop = new Rectangle(0, 0, 1920, 1080);
            s.RgbSource = rgb;
            s.ToGif = gif;
            s.Level = 0;
            s.Fps = 60;
            return s;
        }

        static void Has(string args, string part) { Check(args.Contains(part), "falta '" + part + "' en: " + args); }
        static void HasNot(string args, string part) { Check(!args.Contains(part), "sobra '" + part + "' en: " + args); }

        static void ArgsSuite()
        {
            Test("args", "rgb-source-bt709-and-tags", delegate
            {
                string a = VideoExport.Arguments(Spec(true, false));
                Has(a, "[0:v]" + VideoExport.RgbToYuv + ",format=yuv444p,crop=1920:1080:0:0");
                Has(a, "," + Recorder.Tags + "," + VideoExport.CentredChroma + "format=yuv420p[o]");
                Has(a, "out_color_matrix=bt709");
                Has(a, "-movflags +faststart");
                Check(a.IndexOf(VideoExport.RgbToYuv) < a.IndexOf("crop="), "the conversion must come before the crop");
            });
            Test("args", "yuv-source-unchanged", delegate
            {
                string a = VideoExport.Arguments(Spec(false, false));
                Has(a, "[0:v]format=yuv444p,crop=1920:1080:0:0,format=yuv420p[o]");
                HasNot(a, "out_color_matrix");
                HasNot(a, "setparams");
            });
            Test("args", "overlay-bt709-both-sides", delegate
            {
                ExportSpec s = Spec(true, false);
                s.Overlay = @"C:\t\top.png";
                s.Backdrop = true;
                s.Frame = new Size(2120, 1280);
                s.Inner = new Rectangle(100, 100, 1920, 1080);
                string a = VideoExport.Arguments(s);
                Has(a, "[1:v]scale=out_color_matrix=bt709:out_range=tv,format=yuva444p[top];[0:v]" + VideoExport.RgbToYuv);
                Has(a, ",pad=2120:1280:100:100:black[v];[v][top]overlay=0:0:format=yuv444");
                Has(a, " -i \"C:\\t\\top.png\"");
                Has(a, Recorder.Tags + "," + VideoExport.CentredChroma + "format=yuv420p[o]");
            });
            Test("args", "trim-seconds-invariant", delegate
            {
                CultureInfo before = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");
                try
                {
                    ExportSpec s = Spec(false, false);
                    s.Keep = new List<double[]> { new double[] { 1.5, 3.25 } };
                    string a = VideoExport.Arguments(s);
                    Has(a, "-ss 1.500 -to 3.250 " + Recorder.SafeInput + "-i \"C:\\v\\clip.mkv\"");
                    HasNot(a, "concat");
                    Check(VideoExport.Seconds(62.0004) == "62.000", "Seconds: " + VideoExport.Seconds(62.0004));
                    Check(VideoExport.Seconds(-1) == "0.000", "negative seconds: " + VideoExport.Seconds(-1));
                    Check(VideoExport.Seconds(3599.9999) == "3600.000", "rounding: " + VideoExport.Seconds(3599.9999));
                }
                finally { Thread.CurrentThread.CurrentCulture = before; }
            });
            Test("args", "cuts-concat-audio", delegate
            {
                ExportSpec s = Spec(false, false);
                s.HasAudio = true;
                s.Keep = new List<double[]> { new double[] { 0, 2 }, new double[] { 5, 7.5 } };
                string a = VideoExport.Arguments(s);
                Has(a, "-y -to 2.000 ");
                Check(!a.Contains("-ss 0.000"), "no seek to 0: " + a);
                Has(a, "-ss 5.000 -to 7.500 ");
                Has(a, "[0:v][0:a:0][1:v][1:a:0]concat=n=2:v=1:a=1[cv][ca];[cv]format=yuv444p");
                Has(a, "-map \"[ca]\" " + VideoExport.ShareAudioCodec(s.Level));
            });
            Test("args", "speed-video-and-audio", delegate
            {
                ExportSpec s = Spec(true, false);
                s.HasAudio = true;
                s.Speed = 1.5;
                string a = VideoExport.Arguments(s);
                Has(a, ",setpts=PTS/1.5,");
                Has(a, "[0:a:0]atempo=1.5[ao]");
                Has(a, "-map \"[ao]\"");
                CultureInfo before = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");
                try { Has(VideoExport.Arguments(s), "setpts=PTS/1.5"); }
                finally { Thread.CurrentThread.CurrentCulture = before; }
            });
            Test("args", "max-height-shrinks-only", delegate
            {
                ExportSpec s = Spec(false, false);
                s.MaxHeight = 720;
                string a = VideoExport.Arguments(s);
                Has(a, ",scale=1280:720:flags=lanczos");
                Has(a, VideoExport.ShareCodec(s.Level, new Size(1280, 720), 60));
                s.Crop = new Rectangle(0, 0, 640, 480);
                HasNot(VideoExport.Arguments(s), "flags=lanczos");
                Check(VideoExport.OutputSize(s) == new Size(640, 480), "never upscaled");
            });
            Test("args", "gif-output", delegate
            {
                ExportSpec s = Spec(false, true);
                string a = VideoExport.Arguments(s);
                HasNot(a, "yuv444p");
                HasNot(a, "setparams");
                Has(a, ",fps=15,scale=960:-2:flags=lanczos,split[a][b];[a]palettegen");
                Has(a, "-loop 0");
                s.SourceIsGif = true;
                s.RgbSource = true;
                a = VideoExport.Arguments(s);
                HasNot(a, "fps=15");
                HasNot(a, VideoExport.RgbToYuv);
            });
            Test("args", "rgb-detection", delegate
            {
                Check(VideoExport.PixelFormat(GbrpProbe) == "gbrp", "gbrp: " + VideoExport.PixelFormat(GbrpProbe));
                Check(VideoExport.PixelFormat(Yuv709Probe) == "yuv420p", "yuv420p: " + VideoExport.PixelFormat(Yuv709Probe));
                Check(VideoExport.PixelFormat(GifProbe) == "bgra", "bgra: " + VideoExport.PixelFormat(GifProbe));
                Check(VideoExport.IsRgb("a.mkv", GbrpProbe), "Cine master is RGB");
                Check(!VideoExport.IsRgb("a.mp4", Yuv709Probe), "MP4 is YUV");
                Check(VideoExport.IsRgb("a.gif", ""), "GIF by extension");
                Check(VideoExport.IsRgb("a.mov", "Stream #0:0: Video: png (png  / 0x20676E70), rgba(pc), 640x480"), "PNG mov is RGB");
                Check(VideoExport.IsRgb("a.avi", "Stream #0:0: Video: rawvideo (BGR[24] / 0x18524742), bgr24, 640x480"), "bgr24 is RGB");
                Check(!VideoExport.IsRgb("a.mkv", ""), "no probe: not RGB");
            });
            Test("args", "unedited-master-needs-export", delegate
            {
                Check(VideoExport.NeedsTranscode(@"C:\v\Cine 1.mkv", false), "an unedited .mkv must still export to MP4");
                Check(VideoExport.NeedsTranscode(@"C:\v\a.mov", false), ".mov to MP4");
                Check(!VideoExport.NeedsTranscode(@"C:\v\a.MP4", false), "an unedited MP4 needs nothing");
                Check(!VideoExport.NeedsTranscode(@"C:\v\a.gif", true), "an unedited GIF as GIF needs nothing");
                Check(VideoExport.NeedsTranscode(@"C:\v\a.gif", false), "GIF to MP4");
                Check(VideoExport.NeedsTranscode(@"C:\v\a.mp4", true), "MP4 to GIF");
            });
            Test("args", "strip-fast-seeks", delegate
            {
                double[] t = FrameStrip.Times(1800, 14);
                Check(t.Length == 14, "14 times");
                Check(Math.Abs(t[0] - 1800 / 28.0) < 1e-9 && Math.Abs(t[13] - 1800 * 27 / 28.0) < 1e-9, "slot centres: " + t[0] + " " + t[13]);
                string a = FrameStrip.Arguments(@"C:\v\a.mkv", 1735.71428, 72, @"C:\t\t00.png", false);
                Check(a.IndexOf("-noaccurate_seek") >= 0 && a.IndexOf("-noaccurate_seek") < a.IndexOf("-ss ") && a.IndexOf("-ss ") < a.IndexOf("-i "),
                      "input seek before -i: " + a);
                Has(a, "-ss 1735.714 ");
                Has(a, "-frames:v 1 -vf scale=-2:72 ");
                HasNot(FrameStrip.Arguments(@"C:\v\a.mkv", 5, 72, @"C:\t\t00.png", true), "noaccurate");
            });
        }

        static void KeepSuite()
        {
            Test("keep", "saved-file-is-not-copied-again", delegate
            {
                string temp = Path.Combine(workRoot, "temp");
                Check(Editor.IsTempCapture(Path.Combine(temp, "Captura 1.png"), temp), "a temp capture is kept by copying");
                Check(Editor.IsTempCapture(Path.Combine(temp, "Captura 1.png"), temp + "\\"), "trailing slash");
                Check(!Editor.IsTempCapture(Path.Combine(workRoot, "Capturas", "Captura 1.png"), temp), "a file in the save folder is saved in place");
                Check(!Editor.IsTempCapture(Path.Combine(temp, "sub", "Captura 1.png"), temp), "subfolder is not the temp folder");
            });
            Test("keep", "no-editors-nothing-to-ask", delegate
            {
                Check(!Editor.AnyUnsaved, "no editor open: nothing unsaved");
                Check(Editor.ConfirmCloseAll(), "no editor open: can close");
            });
        }
    }
}

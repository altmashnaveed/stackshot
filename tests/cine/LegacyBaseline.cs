// Stackshot - Reference of the FFmpeg arguments of the Standard, High and Maximum profiles (and GIF) as built by
// v2.0.0 (commit a7bf44f) before the Cinema work. The logic below is a verbatim copy of Session.Start, MakeVideo,
// MakeGif, FinalCodec, AudioCodec and HardwareEncoder at that commit, with the machine-dependent inputs (NV12 support,
// the probed hardware encoder, the voice gain) turned into parameters. It writes baseline-profiles.txt, which
// CineTests compares with what the current code builds.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class LegacyBaseline
{
    const int Standard = 0, Maximum = 1, High = 2, Cinema = 3;
    const string Tags = "setparams=color_primaries=bt709:color_trc=iec61966-2-1:colorspace=bt709:range=tv";

    static string Quote(string path) { return "\"" + path + "\""; }

    static string FinalCodec(int quality, long pixels, double fps)
    {
        string gop = " -g " + Math.Max(2, (int)Math.Round(fps * 2));
        if (quality == Cinema) return "-c:v libx264 -preset " + (pixels > 2400000 ? "medium" : "slow") + " -crf 10 -profile:v high" + gop;
        if (quality == Maximum) return "-c:v libx264 -preset " + (pixels > 2400000 ? "medium" : "slow") + " -crf 14 -profile:v high" + gop;
        if (quality == High) return "-c:v libx264 -preset medium -crf 16 -profile:v high" + gop;
        return "-c:v libx264 -preset medium -crf 20 -profile:v high" + gop;
    }

    static string AudioCodec(int quality)
    {
        return "-c:a aac -b:a " + (quality == Cinema ? "320k" : quality == Maximum ? "256k" : quality == High ? "192k" : "160k") + " -ar 48000";
    }

    static string HardwareEncoder(string hardware, int level)
    {
        if (hardware == null) return null;
        int q = level == 0 ? 12 : level == 1 ? 18 : 24;
        if (hardware == "h264_nvenc") return "-c:v h264_nvenc -preset p5 -rc constqp -qp " + q;
        if (hardware == "h264_qsv") return "-c:v h264_qsv -preset medium -global_quality " + q + " -look_ahead 0";
        return "-c:v h264_amf -quality quality -rc cqp -qp_i " + q + " -qp_p " + q;
    }

    // Session.Start: the live FFmpeg command. nv12 = what ScreenSource.Open(area, large, ...) delivered.
    static string Live(int quality, bool gif, int videoFps, int gifFps, int w, int h, bool gpuNv12, string hardware, bool audio,
                       int fpsNum, int fpsDen, string output, string temp, out string video, out bool direct, out bool lossless, out bool nv12)
    {
        int target = gif ? Math.Max(10, gifFps * 2) : quality == Standard ? videoFps : 60;
        lossless = gif || quality == Maximum || quality == Cinema;
        bool large = !gif && (long)w * h * target > 1920L * 1200 * 60;
        nv12 = large && gpuNv12 && w % 2 == 0 && h % 2 == 0;
        string rate = fpsNum + "/" + fpsDen;
        string gop = " -g " + Math.Max(2, (int)Math.Round(2.0 * fpsNum / fpsDen));
        string input = "-f rawvideo -pix_fmt " + (nv12 ? "nv12" : "bgra") + " -video_size " + w + "x" + h +
                       " -framerate " + rate + " -i - ";
        string enc;
        direct = false;
        if (lossless && !nv12)
        {
            enc = "-c:v libx264rgb -preset ultrafast -qp 0";
            video = temp + ".nut";
        }
        else
        {
            string vf = nv12 ? Tags : "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p," + Tags;
            int level = lossless ? 0 : quality == High ? 1 : 2;
            string hw = large ? HardwareEncoder(hardware, level) : null;
            if (hw == null)
                hw = "-c:v libx264 -preset " + (large ? "ultrafast" : "veryfast") + " -crf " + (level == 0 ? 8 : level == 1 ? 16 : 23);
            enc = "-vf \"" + vf + "\" " + hw + gop;
            direct = !lossless && !audio;
            video = direct ? output : temp + ".nut";
            if (direct) enc += " -movflags +faststart";
        }
        return "-hide_banner -loglevel error -y " + input + enc + " " + Quote(video);
    }

    // Session.MakeVideo (voiceGain: what VoiceGain() returned).
    static string Finish(int quality, bool lossless, bool nv12, int w, int h, int fpsNum, int fpsDen, string video, string sysPath, string micPath,
                         string voiceGain, string output)
    {
        double fps = fpsNum / (double)fpsDen;
        string args = "-i " + Quote(video);
        int next = 1, sysIn = -1, micIn = -1;
        if (sysPath != null) { args += " -f f32le -sample_rate 48000 -ch_layout stereo -i " + Quote(sysPath); sysIn = next++; }
        if (micPath != null) { args += " -f f32le -sample_rate 48000 -ch_layout stereo -i " + Quote(micPath); micIn = next++; }
        List<string> graph = new List<string>();
        string vmap = "0:v", vcodec = "-c:v copy", amap = null;
        if (lossless)
        {
            long pixels = (long)w * h;
            string chain;
            if (nv12) chain = Tags;
            else if (quality == Cinema && ((w * 2 + 15) / 16) * ((h * 2 + 15) / 16) <= 36864)
            {
                chain = "scale=iw*2:ih*2:flags=neighbor+accurate_rnd+full_chroma_int+full_chroma_inp:out_color_matrix=bt709:out_range=tv,format=yuv420p," + Tags;
                pixels *= 4;
            }
            else chain = "scale=out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int+full_chroma_inp,format=yuv420p," + Tags;
            graph.Add("[0:v]" + chain + "[v]");
            vmap = "[v]";
            vcodec = FinalCodec(quality, pixels, fps);
        }
        string voice = micIn >= 0 ? "[" + micIn + ":a]highpass=f=70" + voiceGain : null;
        const string limiter = "alimiter=limit=0.891:attack=5:release=60:level=0:latency=1";
        if (sysIn >= 0 && micIn >= 0)
        {
            graph.Add(voice + "[m];[" + sysIn + ":a][m]amix=inputs=2:duration=first:dropout_transition=0:normalize=0," + limiter + "[a]");
            amap = "[a]";
        }
        else if (micIn >= 0)
        {
            graph.Add(voice + "," + limiter + "[a]");
            amap = "[a]";
        }
        else if (sysIn >= 0) amap = sysIn + ":a";
        if (graph.Count > 0) args += " -filter_complex \"" + string.Join(";", graph.ToArray()) + "\"";
        args += " -map \"" + vmap + "\" " + vcodec;
        if (amap != null) args += " -map \"" + amap + "\" " + AudioCodec(quality);
        args += " -movflags +faststart " + Quote(output);
        return args;
    }

    // Session.MakeGif.
    static string Gif(int w, int gifFps, string video, string output)
    {
        int gw = Math.Min(w, 960) & ~1;
        return "-i " + Quote(video) + " -vf \"fps=" + gifFps + ",scale=" + gw + ":-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];" +
               "[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle\" -loop 0 " + Quote(output);
    }

    // One line per scenario: the key, then the live command, then the final pass (or "-" when the live file is final).
    public static List<string> Lines()
    {
        List<string> lines = new List<string>();
        int[][] sizes = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 1920, 1200 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 1000, 2000 } };
        string[] hws = { null, "h264_nvenc", "h264_qsv", "h264_amf" };
        int[][] rates = { new[] { 60, 1 }, new[] { 60000, 1001 }, new[] { 30, 1 }, new[] { 24, 1 } };
        string[] gains = { "", ",volume=3.0dB" };
        const string temp = "T", output = "O.mp4";
        foreach (int quality in new[] { Standard, High, Maximum, -1 })
            foreach (int videoFps in quality == Standard ? new[] { 24, 30, 60 } : new[] { 60 })
                foreach (int[] sz in sizes)
                    foreach (bool gpuNv12 in new[] { false, true })
                        foreach (string hw in hws)
                            foreach (int[] rate in rates)
                                for (int audioMask = 0; audioMask < 4; audioMask++)
                                    foreach (string gain in gains)
                                    {
                                        bool gif = quality < 0;
                                        if (gif && audioMask != 0) continue;          // GIFs never record sound
                                        if ((audioMask & 2) == 0 && gain != "") continue;  // the gain only exists with a microphone
                                        int q = gif ? Standard : quality;
                                        string video; bool direct, lossless, nv12;
                                        string live = Live(q, gif, videoFps, 15, sz[0], sz[1], gpuNv12, hw, audioMask != 0, rate[0], rate[1],
                                                           gif ? "O.gif" : output, temp, out video, out direct, out lossless, out nv12);
                                        string fin;
                                        if (gif) fin = Gif(sz[0], 15, video, "O.gif");
                                        else if (direct) fin = "-";
                                        else fin = Finish(q, lossless, nv12, sz[0], sz[1], rate[0], rate[1], video,
                                                          (audioMask & 1) != 0 ? temp + " equipo.f32" : null, (audioMask & 2) != 0 ? temp + " micro.f32" : null, gain, output);
                                        string key = (gif ? "gif" : q == Standard ? "standard" : q == High ? "high" : "maximum") + " fps=" + videoFps +
                                                     " " + sz[0] + "x" + sz[1] + " gpuNv12=" + gpuNv12 + " hw=" + (hw ?? "none") + " rate=" + rate[0] + "/" + rate[1] +
                                                     " audio=" + audioMask + " gain=" + (gain == "" ? "none" : "3dB");
                                        lines.Add(key + "\n  live: " + live + "\n  final: " + fin);
                                    }
        // The editor's export and every other user of these helpers.
        foreach (int quality in new[] { Standard, High, Maximum, Cinema })
            foreach (long px in new long[] { 1920L * 1080, 2400001L })
                foreach (double fps in new[] { 30.0, 59.94, 60.0 })
                    lines.Add("FinalCodec q=" + quality + " px=" + px + " fps=" + fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n  " + FinalCodec(quality, px, fps));
        foreach (int quality in new[] { Standard, High, Maximum, Cinema }) lines.Add("AudioCodec q=" + quality + "\n  " + AudioCodec(quality));
        return lines;
    }

    static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "baseline-profiles.txt";
        List<string> lines = Lines();
        File.WriteAllText(path, string.Join("\n", lines.ToArray()) + "\n", new UTF8Encoding(false));
        Console.WriteLine(lines.Count + " scenarios -> " + path);
        return 0;
    }
}

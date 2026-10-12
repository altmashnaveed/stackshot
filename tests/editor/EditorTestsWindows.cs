// Tests that Windows itself (Media Foundation, the decoders every Windows 10/11 has) decodes a shared MP4 from start to
// end, video and sound, and that it cannot decode a Cine master (which is why Stackshot plays masters with FFmpeg).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static void WindowsSuite()
        {
            string dir = Path.Combine(workRoot, "windows");
            Directory.CreateDirectory(dir);
            string master = Path.Combine(dir, "master.mkv"), mp4 = Path.Combine(dir, "share.mp4");

            Test("windows", "master-and-share-built", delegate
            {
                NeedFfmpeg();
                // Same stream layout as a Cine master: lossless RGB H.264 (libx264rgb, gbrp) and PCM f32 tracks in Matroska.
                RunWinFf("-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1280x720:rate=60:duration=4 -f lavfi -i sine=frequency=440:sample_rate=48000:duration=4 " +
                      "-map 0:v -map 1:a -c:v libx264rgb -preset ultrafast -qp 0 -pix_fmt gbrp -c:a pcm_f32le -ac 2 " + Recorder.Quote(master));
                ExportSpec s = new ExportSpec();
                s.Source = master;
                s.Output = mp4;
                s.Crop = new System.Drawing.Rectangle(0, 0, 1280, 720);
                s.HasAudio = true;
                s.RgbSource = true;
                s.Level = 0;
                s.Fps = 60;
                RunWinFf(VideoExport.Arguments(s));
                Check(File.Exists(mp4), "no MP4");
                Add("master " + new FileInfo(master).Length + " B, MP4 " + new FileInfo(mp4).Length + " B");
            });

            Test("windows", "share-mp4-decodes-whole-in-windows", delegate
            {
                if (!File.Exists(mp4)) throw new TestSkipped("no MP4 (previous test failed)");
                MfResult r = MfDecode(mp4);
                Add(r.ToString());
                Check(r.Error == null, "Media Foundation: " + r.Error);
                Check(r.VideoFrames == 240, "video frames decoded by Windows: " + r.VideoFrames + " (expected 240)");
                Check(Math.Abs(r.AudioSeconds - 4.0) < 0.06, "audio decoded by Windows: " + r.AudioSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Check(r.Width == 1280 && r.Height == 720, "size " + r.Width + "x" + r.Height);
            });

            Test("windows", "cine-master-not-decodable-by-windows", delegate
            {
                if (!File.Exists(master)) throw new TestSkipped("no master (previous test failed)");
                MfResult r = MfDecode(master);
                Add(r.ToString());
                // Documents the reason for the built-in player: Windows has no decoder for 4:4:4 RGB H.264.
                Check(r.Error != null || r.VideoFrames == 0, "Windows decoded the master: " + r);
            });

            Test("windows", "real-share-export-of-the-user", delegate
            {
                // Read-only look at the user's own export when present (never modified, never required).
                string real = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Stackshot", "temp", "Cine 2026-10-10 12.53.58 (para compartir).mp4");
                if (!File.Exists(real)) throw new TestSkipped("not on this machine");
                MfResult r = MfDecode(real);
                Add(r.ToString());
                Check(r.Error == null && r.VideoFrames > 0, "Media Foundation: " + r);
            });
        }

        static void RunWinFf(string args)
        {
            string err;
            int code = VideoExport.Run(ffmpeg, args, 300000, out err);
            if (code != 0) throw new TestFailed("ffmpeg " + code + ": " + err);
        }

        sealed class MfResult
        {
            public string Error;
            public int VideoFrames, Width, Height;
            public double AudioSeconds, LastVideoSeconds;
            public override string ToString()
            {
                return (Error != null ? "error " + Error + "; " : "") + "video " + VideoFrames + " frames " + Width + "x" + Height + " (last at " +
                       LastVideoSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s), audio " + AudioSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s";
            }
        }

        // Media Foundation Source Reader with decoding to NV12 (video) and float PCM (audio): every sample is a decoded one.
        static MfResult MfDecode(string path)
        {
            MfResult r = new MfResult();
            int hr = Mf.MFStartup(0x20070, 0);
            if (hr < 0) { r.Error = "MFStartup 0x" + hr.ToString("X8"); return r; }
            IntPtr reader = IntPtr.Zero;
            try
            {
                hr = Mf.MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader);
                if (hr < 0) { r.Error = "open 0x" + hr.ToString("X8"); return r; }
                bool video = Mf.Want(reader, Mf.FirstVideo, Mf.Video, Mf.NV12), audio = Mf.Want(reader, Mf.FirstAudio, Mf.Audio, Mf.Float);
                if (!video) { r.Error = "no Windows decoder for the video stream"; return r; }
                Mf.Size(reader, out r.Width, out r.Height);
                long audioBytes = 0;
                foreach (uint stream in audio ? new[] { Mf.FirstVideo, Mf.FirstAudio } : new[] { Mf.FirstVideo })
                {
                    while (true)
                    {
                        uint actual, flags;
                        long ts;
                        IntPtr sample;
                        hr = Mf.Fn<Mf.ReadSampleFn>(reader, 9)(reader, stream, 0, out actual, out flags, out ts, out sample);
                        if (hr < 0) { r.Error = "read 0x" + hr.ToString("X8"); return r; }
                        if ((flags & 1) != 0) { r.Error = "stream error"; return r; }
                        if (sample != IntPtr.Zero)
                        {
                            if (stream == Mf.FirstVideo) { r.VideoFrames++; r.LastVideoSeconds = ts / 1e7; }
                            else
                            {
                                uint len;
                                Mf.Fn<Mf.TotalLengthFn>(sample, 45)(sample, out len);
                                audioBytes += len;
                            }
                            Marshal.Release(sample);
                        }
                        if ((flags & 2) != 0) break; // end of stream
                    }
                }
                r.AudioSeconds = audioBytes / (48000.0 * 2 * 4);
                return r;
            }
            finally
            {
                if (reader != IntPtr.Zero) Marshal.Release(reader);
                Mf.MFShutdown();
            }
        }

        static class Mf
        {
            public const uint FirstVideo = 0xFFFFFFFC, FirstAudio = 0xFFFFFFFD;
            public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
            public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
            public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71"), Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
            public static readonly Guid NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71"), Float = new Guid("00000003-0000-0010-8000-00AA00389B71");

            [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
            [DllImport("mfplat.dll")] public static extern int MFShutdown();
            [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IntPtr type);
            [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetUInt64Fn(IntPtr self, ref Guid key, out ulong value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetTypeFn(IntPtr self, uint stream, IntPtr reserved, IntPtr type);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetTypeFn(IntPtr self, uint stream, out IntPtr type);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ReadSampleFn(IntPtr self, uint stream, uint control, out uint actual, out uint flags, out long ts, out IntPtr sample);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int TotalLengthFn(IntPtr self, out uint length);

            public static T Fn<T>(IntPtr obj, int slot) where T : class
            {
                IntPtr vt = Marshal.ReadIntPtr(obj);
                return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T)) as T;
            }

            // Asks the reader to decode the stream to the given subtype; false when Windows has no decoder for it.
            public static bool Want(IntPtr reader, uint stream, Guid major, Guid sub)
            {
                IntPtr t;
                if (MFCreateMediaType(out t) < 0) return false;
                try
                {
                    Guid mk = MajorType, sk = SubType;
                    Fn<SetGuidFn>(t, 24)(t, ref mk, ref major);
                    Fn<SetGuidFn>(t, 24)(t, ref sk, ref sub);
                    return Fn<SetTypeFn>(reader, 7)(reader, stream, IntPtr.Zero, t) >= 0;
                }
                finally { Marshal.Release(t); }
            }

            public static void Size(IntPtr reader, out int w, out int h)
            {
                w = h = 0;
                IntPtr t;
                if (Fn<GetTypeFn>(reader, 6)(reader, FirstVideo, out t) < 0) return;
                try
                {
                    ulong v;
                    Guid fs = FrameSize;
                    if (Fn<GetUInt64Fn>(t, 8)(t, ref fs, out v) >= 0) { w = (int)(v >> 32); h = (int)(v & 0xFFFFFFFF); }
                }
                finally { Marshal.Release(t); }
            }
        }
    }
}

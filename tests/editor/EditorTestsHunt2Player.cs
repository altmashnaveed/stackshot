// Bug hunt round 2 of the built-in player (tester h2-player, 2026-10-11): anamorphic pictures (SAR), a default sound
// track that is not the first one and the track heard when exporting, two windows exporting the same file, tiny videos
// in the export, fullscreen and back, a rotated phone clip in a window, odd extreme shapes, the wording of the scale
// note, and the size of the window for a narrow clip. Suite "hunt2player". Headless: no window is shown and the screen
// is never captured. Export is not offered for .mp4 sources (by design), so the sources here are .mov or .mkv.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static void Hunt2PlayerSuite()
        {
            Test("hunt2player", "sar-anamorphic-picture-keeps-display-aspect", H2Sar);
            Test("hunt2player", "default-track-not-first-heard-and-exported", H2Tracks);
            Test("hunt2player", "two-windows-same-file-export-at-once", H2TwoExports);
            Test("hunt2player", "tiny-videos-export-keep-their-size", H2TinyExport);
            Test("hunt2player", "fullscreen-and-back-restores-window", H2Fullscreen);
            Test("hunt2player", "rotated-phone-window-portrait-and-export", H2Rotated);
            Test("hunt2player", "odd-shapes-3x2000-and-4000x3-aspect", H2Extreme);
            Test("hunt2player", "scale-note-contract-fuzz", H2ScaleNote);
            Test("hunt2player", "narrow-clip-window-size-design", H2NarrowWindow);
        }

        static bool H2Busy(PlayerWindow w)
        {
            FieldInfo f = typeof(PlayerWindow).GetField("exportThread", BindingFlags.NonPublic | BindingFlags.Instance);
            return f.GetValue(w) != null;
        }

        // Runs the window's own Exportar MP4 (the method the bar and the menu call) and waits for it to end.
        static void H2Export(PlayerWindow w)
        {
            PlayerWindow.Quiet = true;
            typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, null);
            Stopwatch sw = Stopwatch.StartNew();
            while (H2Busy(w) && sw.ElapsedMilliseconds < 150000) { Application.DoEvents(); Thread.Sleep(10); }
            Check(!H2Busy(w), "the export did not end in 150 s");
        }

        static string[] H2Outputs(string dir)
        {
            return Directory.GetFiles(dir, "*para compartir*.mp4");
        }

        static string H2Names(string[] files)
        {
            List<string> n = new List<string>();
            foreach (string f in files) n.Add(Path.GetFileName(f));
            return string.Join(", ", n.ToArray());
        }

        // The sound of a media file as stereo float samples at 48 kHz, first track.
        static float[] H2Pcm(string media, string dir)
        {
            string raw = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".f32");
            string err;
            int code = Ff("-hide_banner -loglevel error -y -i " + Q(media) + " -map 0:a:0 -f f32le -ac 2 -ar 48000 " + Q(raw), out err, 60000);
            Check(code == 0, "decoding the sound of " + Path.GetFileName(media) + ": " + Tail(err));
            byte[] b = File.ReadAllBytes(raw);
            float[] f = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
            return f;
        }

        // Amplitude of a tone of hz a quarter second in (half a second long), or about 0 for silence.
        static double H2Tone(float[] pcm, double hz)
        {
            int frames = pcm.Length / 2;
            int start = Math.Min(12000, frames / 4), len = Math.Min(24000, frames - start);
            if (len <= 0) return 0;
            return PlTonePower(pcm, start, len, hz);
        }

        static string H2F(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        // 1. An anamorphic clip (1440x1080 stored, SAR 4:3, so 16:9 on screen, the HDV/camcorder way): the window must show
        //    it at 16:9, and the export of a window on the same file must keep 16:9.
        static void H2Sar()
        {
            NeedFfmpeg();
            string dir = WorkDir("h2-sar");
            string path = Path.Combine(dir, "hdv.mov");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=1440x1080:r=30:d=1\" -vf setsar=4/3 -c:v libx264 -preset ultrafast -pix_fmt yuv420p " + Q(path), 120000);
            string probe = Probe(path);
            Check(probe.Contains("SAR 4:3 DAR 16:9"), "the test file does not say SAR 4:3 DAR 16:9: " + Tail(probe));
            MediaInfo mi = MediaInfo.Probe(ffmpeg, path);
            Check(mi.CanPlay && mi.Width == 1440 && mi.Height == 1080, "probe " + mi.Width + "x" + mi.Height);
            bool playerOk = true;
            PlayerWindow w = PwWindow(path, new PlFakeSound(), new Settings());
            try
            {
                IntPtr h = w.Handle;
                Rectangle pic = w.PictureRect;
                double shown = pic.Width / (double)pic.Height, want = 16 / 9.0;
                playerOk = Math.Abs(shown - want) / want < 0.02;
                if (!playerOk)
                    Add("player: the picture is " + pic.Width + "x" + pic.Height + " on screen (" + H2F(shown) +
                        ":1), the display aspect is 16:9 (stored 1440x1080 with SAR 4:3)");
            }
            finally { w.Dispose(); }
            // The export of a window on the same file: its MP4 keeps the display aspect (the player is the one that ignores it).
            PlayerWindow e = PwWindow(path, new PlFakeSound(), new Settings());
            try
            {
                IntPtr h = e.Handle;
                H2Export(e);
                string[] outs = H2Outputs(dir);
                Check(outs.Length == 1, "export left " + H2Names(outs) + " | note " + e.ExportNote);
                Check(Probe(outs[0]).Contains("DAR 16:9"), "the MP4 does not keep DAR 16:9: " + Tail(Probe(outs[0])));
                Add("export: " + (Probe(outs[0]).Contains("DAR 16:9") ? "keeps DAR 16:9" : "no DAR 16:9"));
            }
            finally { e.Dispose(); PlayerWindow.Quiet = false; }
            Check(playerOk, "the player shows the video at the wrong shape");
        }

        // 2. A file whose default sound track is the second one: the window plays it, and an export after switching to
        //    the first (silent) track must carry that silence, and the default one must carry the tone.
        static void H2Tracks()
        {
            NeedFfmpeg();
            string dir = WorkDir("h2-tracks");
            string src = Path.Combine(dir, "dos-pistas.mkv");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=320x180:r=30:d=1.5\" -f lavfi -i \"anullsrc=r=48000:cl=stereo:d=1.5\" " +
                 "-f lavfi -i \"sine=f=880:d=1.5\" -map 0:v -map 1:a -map 2:a -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac " +
                 "-disposition:a:0 0 -disposition:a:1 default -metadata:s:a:0 title=Comentario -metadata:s:a:1 title=Original " + Q(src), 120000);
            MediaInfo mi = MediaInfo.Probe(ffmpeg, src);
            Check(mi.Audio.Count == 2 && !mi.Audio[0].Default && mi.Audio[1].Default, "the default flags read as " + mi.Audio.Count + " tracks");
            PlayerWindow w = PwWindow(src, new PlFakeSound(), new Settings());
            PlayerWindow.Quiet = true;
            try
            {
                IntPtr h = w.Handle;
                Check(w.Engine != null && w.Engine.Track == 1, "the window plays track " + (w.Engine != null ? w.Engine.Track : -1) + ", the default is 1");
                H2Export(w);
                string[] first = H2Outputs(dir);
                Check(first.Length == 1, "first export left " + H2Names(first));
                double tone = H2Tone(H2Pcm(first[0], dir), 880);
                Check(tone > 0.03, "the default track (880 Hz tone) is not in the MP4: tone power " + H2F(tone));
                // Switch to the silent first track, as the menu does, and export again.
                w.Engine.Track = 0;
                H2Export(w);
                string[] all = H2Outputs(dir);
                Check(all.Length == 2, "second export left " + H2Names(all));
                string second = null;
                foreach (string f in all) if (f != first[0]) second = f;
                double silent = H2Tone(H2Pcm(second, dir), 880);
                Check(silent < 0.01, "the track heard was silence but the MP4 has the tone (" + H2F(silent) + ")");
                Add("tone " + H2F(tone) + " then silence " + H2F(silent));
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // 3. Two windows on the same file (possible: drop the file onto a window that shows another file, then export from
        //    both). Both exports are asked for at once: each must leave its own MP4.
        static void H2TwoExports()
        {
            NeedFfmpeg();
            string clip = PwClip(".avi", "-c:v mpeg4 -q:v 3 -c:a pcm_s16le", 1.5, false, null);
            if (clip == null) Skip("this FFmpeg cannot write .avi");
            string dir = Path.GetDirectoryName(clip);
            PlayerWindow.Quiet = true;
            PlayerWindow a = PwWindow(clip, new PlFakeSound(), new Settings());
            PlayerWindow b = PwWindow(clip, new PlFakeSound(), new Settings());
            try
            {
                IntPtr ha = a.Handle, hb = b.Handle;
                MethodInfo m = typeof(PlayerWindow).GetMethod("ExportMp4", BindingFlags.NonPublic | BindingFlags.Instance);
                m.Invoke(a, null);
                m.Invoke(b, null);
                Stopwatch sw = Stopwatch.StartNew();
                while ((H2Busy(a) || H2Busy(b)) && sw.ElapsedMilliseconds < 150000) { Application.DoEvents(); Thread.Sleep(10); }
                string[] outs = H2Outputs(dir);
                Check(outs.Length == 2, "two exports of the same file left " + outs.Length + " file(s): " + H2Names(outs) +
                      " | notes: " + a.ExportNote + " / " + b.ExportNote);
            }
            finally { a.Dispose(); b.Dispose(); PlayerWindow.Quiet = false; }
        }

        // 4. Videos smaller than the export's 16-pixel floor (1x1, 8x8, a 320x8 strip): the player plays them; the export
        //    must give a file of the same size (never enlarged) or at least not fail.
        static void H2TinyExport()
        {
            NeedFfmpeg();
            // 1x1 cannot be 4:2:0 (sides must be even): the export says it could not be made, and nothing is left; the
            // others keep their size (they used to be cropped to at least 16 px, past the picture).
            string[] sizes = { "1x1", "8x8", "320x8" };
            List<string> bad = new List<string>();
            foreach (string size in sizes)
            {
                if (size == "1x1")
                {
                    string d1 = WorkDir("h2-tiny");
                    string s1 = Path.Combine(d1, "t1x1.mkv");
                    FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0xFF0000:s=1x1:r=30:d=0.5\" -c:v ffv1 -pix_fmt yuv444p " + Q(s1), 60000);
                    PlayerWindow w1 = PwWindow(s1, new PlFakeSound(), new Settings());
                    try
                    {
                        IntPtr h1 = w1.Handle;
                        H2Export(w1);
                        if (w1.ExportNote == "ok" || H2Outputs(d1).Length != 0) bad.Add("1x1: expected a clear failure and no file, got " + w1.ExportNote + " and " + H2Outputs(d1).Length + " file(s)");
                    }
                    finally { w1.Dispose(); PlayerWindow.Quiet = false; }
                    continue;
                }
                string dir = WorkDir("h2-tiny");
                string src = Path.Combine(dir, "t" + size + ".mkv");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0xFF0000:s=" + size + ":r=30:d=0.5\" -c:v ffv1 -pix_fmt yuv444p " + Q(src), 60000);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, src);
                if (!mi.CanPlay) { bad.Add(size + " does not read as playable"); continue; }
                PlayerWindow w = PwWindow(src, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    H2Export(w);
                    string[] outs = H2Outputs(dir);
                    if (outs.Length != 1) { bad.Add(size + ": no MP4 (" + (w.ExportNote != null ? w.ExportNote.Split('\n')[0] : "no note") + ")"); continue; }
                    MediaInfo om = MediaInfo.Probe(ffmpeg, outs[0]);
                    if (om.Width != mi.Width || om.Height != mi.Height) bad.Add(size + ": exported " + om.Width + "x" + om.Height);
                }
                finally { w.Dispose(); PlayerWindow.Quiet = false; }
            }
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
            Add(sizes.Length + " sizes");
        }

        // 5. F (fullscreen) and F again: the window comes back to its own bounds, the picture stays at its real size and
        //    centred. (Maximize is not tested: it would show the window.)
        static void H2Fullscreen()
        {
            NeedFfmpeg();
            string mp4 = PlMp4("h2-fs.mp4", false, 1);
            PlayerWindow w = PwWindow(mp4, new PlFakeSound(), new Settings());
            try
            {
                IntPtr h = w.Handle;
                Rectangle before = w.Bounds;
                Size client = w.ClientSize;
                Rectangle screen = Screen.FromControl(w).Bounds;
                Check(PwKey(w, Keys.F), "F was not taken");
                Check(w.Bounds == screen, "fullscreen bounds " + w.Bounds + " are not the screen " + screen);
                Size box = w.ClientSize;
                Rectangle pic = w.PictureRect;
                Check(pic.Size == new Size(320, 180) && pic.X == (box.Width - 320) / 2 && pic.Y == (box.Height - 180) / 2,
                    "fullscreen picture " + pic + " in " + box + ", want the 320x180 video centred and not enlarged");
                Check(PwKey(w, Keys.F), "F was not taken the second time");
                Check(w.Bounds == before, "back from fullscreen the bounds are " + w.Bounds + ", not " + before);
                Check(w.ClientSize == client, "back from fullscreen the client is " + w.ClientSize + ", not " + client);
                Add("bounds " + before + " and back");
            }
            finally { w.Dispose(); }
        }

        // 6. A phone clip stored sideways (display rotation 90): the window opens portrait, the picture keeps 9:16, the
        //    pictures are 180x320, and the export is a validated 180x320 MP4.
        static void H2Rotated()
        {
            NeedFfmpeg();
            string src = PlMp4("h2-upright.mp4", false, 1);
            string rot = Path.Combine(WorkDir("h2-rot"), "celular.mov");
            string err;
            if (Ff("-hide_banner -loglevel error -y -display_rotation:v:0 90 -i " + Q(src) + " -c copy " + Q(rot), out err, 30000) != 0)
                Skip("this FFmpeg cannot set a display rotation: " + Tail(err));
            PlayerWindow w = PwWindow(rot, new PlFakeSound(), new Settings());
            PlayerWindow.Quiet = true;
            try
            {
                IntPtr h = w.Handle;
                Check(w.Media.Width == 180 && w.Media.Height == 320, "probe " + w.Media.Width + "x" + w.Media.Height);
                // At real size a small portrait clip gets a window as wide as the bar needs and at least as tall as the clip
                // (no black above and below); the picture itself is what must be portrait.
                Check(w.ClientSize.Height >= 320 && w.ClientSize.Width >= 180, "a portrait clip opened as " + w.ClientSize);
                Rectangle pic = w.PictureRect;
                Check(Math.Abs(pic.Width / (double)pic.Height - 9 / 16.0) < 0.01, "picture " + pic + " is not 9:16");
                string note = PlayerKit.ScaleNote(new Size(180, 320), pic, true);
                Check(note.StartsWith("100 %"), "scale note " + note);
                PwStills seen = new PwStills();
                w.FrameHook = delegate(PlayerFrame f) { lock (seen) { seen.Index.Add(0); seen.Width.Add(f.Width); seen.Height.Add(f.Height); } };
                w.BeginPlay();
                PlWait(delegate { return seen.Count >= 3; }, 15000, "pictures of the rotated clip");
                lock (seen) Check(seen.Width[0] == 180 && seen.Height[0] == 320, "picture " + seen.Width[0] + "x" + seen.Height[0] + " for a portrait clip");
                H2Export(w);
                string[] outs = H2Outputs(Path.GetDirectoryName(rot));
                Check(outs.Length == 1, "export left " + H2Names(outs) + " | note " + w.ExportNote);
                ShareExpect e = new ShareExpect();
                e.Width = 180; e.Height = 320; e.Seconds = 1.0; e.HasAudio = false;
                ShareReport rep = ShareCheck.Validate(ffmpeg, outs[0], e);
                Check(rep.Passed, "the export of the rotated clip does not pass the check: " + rep.Summary);
                Add("portrait window " + w.ClientSize + ", picture " + pic);
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // 7. Odd extreme shapes in a window: a 3x2000 and a 4000x3 clip. The picture on screen must keep the video's aspect
        //    within 5 % (rounding to whole pixels may not distort them).
        static void H2Extreme()
        {
            NeedFfmpeg();
            string[] sizes = { "3x2000", "4000x3" };
            List<string> bad = new List<string>();
            foreach (string size in sizes)
            {
                string src = Path.Combine(WorkDir("h2-odd"), "o" + size + ".mkv");
                FfOk("-hide_banner -loglevel error -y -f lavfi -i \"color=c=0x2060C0:s=" + size + ":r=30:d=0.5\" -c:v ffv1 -pix_fmt yuv444p " + Q(src), 60000);
                MediaInfo mi = MediaInfo.Probe(ffmpeg, src);
                if (!mi.CanPlay) { bad.Add(size + " does not read as playable"); continue; }
                PlayerWindow w = PwWindow(src, new PlFakeSound(), new Settings());
                try
                {
                    IntPtr h = w.Handle;
                    Rectangle pic = w.PictureRect;
                    double want = mi.Width / (double)mi.Height, got = pic.Width / (double)Math.Max(1, pic.Height);
                    double err = Math.Abs(got - want) / want;
                    Add(size + ": window " + w.ClientSize + ", picture " + pic.Width + "x" + pic.Height + ", error " + H2F(err));
                    // A side of one or two pixels cannot keep the shape better than the nearest whole pixel: what is checked is
                    // that one side of the picture is within a pixel of what the other side asks for (the error is reported).
                    double wantW = mi.Width * (pic.Height / (double)mi.Height), wantH = mi.Height * (pic.Width / (double)mi.Width);
                    if (Math.Abs(pic.Width - wantW) > 1 && Math.Abs(pic.Height - wantH) > 1)
                        bad.Add(size + ": picture " + pic.Width + "x" + pic.Height + " in " + w.ClientSize + " is more than a pixel off its shape");
                }
                finally { w.Dispose(); }
            }
            Check(bad.Count == 0, string.Join(" | ", bad.ToArray()));
        }

        // 8. The scale note's contract: "100 %" only for the exact video size; "NN %" below 100 only when the picture is
        //    smaller; "ampliado" only when it is enlarged (fill); never an enlargement without fill; always centred inside.
        //    Breaks are counted per kind.
        static void H2ScaleNote()
        {
            Dictionary<string, int> kinds = new Dictionary<string, int>();
            Dictionary<string, string> example = new Dictionary<string, string>();
            string exact = PlayerKit.ScaleNote(new Size(999, 999), new Rectangle(0, 0, 1000, 1000), true);
            if (exact.StartsWith("100 %", StringComparison.Ordinal)) H2Kind(kinds, example, "100 % for a picture that is not the exact size", "999x999 video filling 1000x1000 reads \"" + exact + "\"");
            Random rnd = new Random(7);
            int notes = 0;
            for (int i = 0; i < 3000; i++)
            {
                Size video = new Size(rnd.Next(1, 4000), rnd.Next(1, 3000));
                Size box = new Size(rnd.Next(50, 2500), rnd.Next(50, 1500));
                if (i % 4 == 0) box = new Size(video.Width + rnd.Next(-3, 4), video.Height + rnd.Next(-3, 4));
                bool fill = i % 2 == 0;
                Rectangle pic = PlayerKit.Picture(video, box, fill);
                string text = PlayerKit.ScaleNote(video, pic, true);
                notes++;
                bool same = pic.Width == video.Width && pic.Height == video.Height;
                if (!fill && (pic.Width > video.Width || pic.Height > video.Height)) H2Kind(kinds, example, "enlarged without fill", pic + " for " + video);
                if (pic.X < 0 || pic.Y < 0 || pic.Right > box.Width || pic.Bottom > box.Height) H2Kind(kinds, example, "picture outside the window", pic + " in " + box);
                if (Math.Abs(pic.X - (box.Width - pic.Width) / 2) > 1 || Math.Abs(pic.Y - (box.Height - pic.Height) / 2) > 1) H2Kind(kinds, example, "picture not centred", pic + " in " + box);
                if (same && !text.StartsWith("100 % (tama", StringComparison.Ordinal)) H2Kind(kinds, example, "exact size not read as real size", "\"" + text + "\"");
                if (!same && text.StartsWith("100 %", StringComparison.Ordinal)) H2Kind(kinds, example, "100 % for a picture that is not the exact size", video + " shown as " + pic + ": \"" + text + "\"");
                if (!same && pic.Width < video.Width && text.Contains("ampliado")) H2Kind(kinds, example, "smaller picture says ampliado", pic + " for " + video);
                if (!same && pic.Width > video.Width && !text.Contains("ampliado")) H2Kind(kinds, example, "enlarged picture not said", pic + ": " + text);
            }
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> kv in kinds) lines.Add(kv.Value + " x " + kv.Key + " (e.g. " + example[kv.Key] + ")");
            Check(kinds.Count == 0, string.Join(" | ", lines.ToArray()));
            Add(notes + " random pictures");
        }

        static void H2Kind(Dictionary<string, int> kinds, Dictionary<string, string> example, string kind, string ex)
        {
            int n;
            kinds.TryGetValue(kind, out n);
            kinds[kind] = n + 1;
            if (!example.ContainsKey(kind)) example[kind] = ex;
        }

        // 9. Design check, not a contract: a narrow or tall clip (360x640 phone clip) opens in a window at least 560 px wide,
        //    so the window ends up far larger than the video. Reports the window and its area against the video's.
        static void H2NarrowWindow()
        {
            Rectangle work = new Rectangle(0, 0, 1920, 1040);
            Size frame = new Size(16, 39);
            // At real size the window only widens to what the bar needs (560): no black above and below the picture.
            Size win = PlayerKit.WindowFor(new Size(360, 640), work, 560, frame, false);
            double ratio = (win.Width * (double)win.Height) / (360.0 * 640.0);
            Check(win.Width == 560 && win.Height == 640, "a 360x640 clip opens in a " + win.Width + "x" + win.Height + " client (" + H2F(ratio) + " times its area)");
            // With "llenar la ventana" the window keeps the clip's shape, as the picture fills it.
            Size fill = PlayerKit.WindowFor(new Size(360, 640), work, 560, frame, true);
            Check(Math.Abs(fill.Width / (double)fill.Height - 360 / 640.0) < 0.01, "fill: " + fill);
            Size tiny = PlayerKit.WindowFor(new Size(16, 16), work, 560, frame, false);
            Check(tiny.Width == 560 && tiny.Height == 315, "a 16x16 clip still gets room for the bar: " + tiny);
            Add("real size " + win + ", fill " + fill + ", tiny " + tiny);
        }
    }
}

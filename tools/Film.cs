// Stackshot - Trailer: a motion piece rendered frame by frame with WPF and piped to FFmpeg, at 1080p or 4K.
// Usage (through tools\make-film.ps1):
//   film.exe plates <dir>
//   film.exe render <dir> <en|es> <out.mp4> <ffmpeg> <fps> <scale>           the whole film
//   film.exe part <dir> <en|es> <name> <out.mp4> <ffmpeg> <fps> <scale>      one extra part on its own
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Drawing;
using System.Reflection;

namespace Stackshot
{
    public static partial class Film
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Program.Init();
            ShotStack.Test = true;
            Settings.ReadOnly = true;
            if (args[0] == "plates")
            {
                using (Bitmap sharp = Desktop(2)) sharp.Save(System.IO.Path.Combine(args[1], "desktop.png"));
                using (Bitmap desk = Desktop(1)) Plates.Make(args[1], desk);
                return;
            }
            if (args[0] == "part") { RenderPart(args[1], args[2], args[3], args[4], args[5], int.Parse(args[6]), int.Parse(args[7])); return; }
            Render(args[1], args[2], args[3], args[4], int.Parse(args[5]), int.Parse(args[6]));
        }

        // The studio's fake desktop (a chat app on a gradient), laid out at 1920x1080 and drawn at that size times scale.
        static Bitmap Desktop(float scale)
        {
            MethodInfo mi = typeof(Studio).GetMethod("Desktop", BindingFlags.NonPublic | BindingFlags.Static);
            return (Bitmap)mi.Invoke(null, new object[] { new Size(1920, 1080), scale });
        }
    }
}

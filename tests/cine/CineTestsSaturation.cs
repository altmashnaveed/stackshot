// Clipping warning (suite "saturation"): a source held at full scale while recording is counted, told in the report
// and in the notice, and normal loud sound or single peaks are not. Fed through AudioCapture.ForTest: no device.
using System;
using System.Collections.Generic;
using System.IO;

namespace Stackshot.Tests
{
    static partial class CineTests
    {
        static void SaturationSuite()
        {
            Test("saturation", "loud-but-clean-is-not-clipping", SatClean);
            Test("saturation", "held-full-scale-counted-once-per-burst", SatBursts);
            Test("saturation", "single-peaks-and-pairs-are-not-clipping", SatPeaks);
            Test("saturation", "one-channel-and-above-one-float", SatChannels);
            Test("saturation", "burst-across-packets-is-one", SatAcrossPackets);
            Test("saturation", "first-time-and-total-in-the-text", SatText);
            Test("saturation", "report-summary-json-and-notice", SatReport);
        }

        // Packets of a 440 Hz tone at `amp`, with full-scale bursts of `burst` frames at the given frames.
        static AudioCapture SatFeed(string dir, string name, int packets, float amp, int[] at, int burst, bool leftOnly, float clipValue)
        {
            string path = TempAudioPath(dir, name);
            AudioCapture ac = AudioCapture.ForTest(path, true);
            long frame = 0;
            for (int p = 0; p < packets; p++)
            {
                float[] s = new float[PacketFrames * 2];
                for (int i = 0; i < PacketFrames; i++, frame++)
                {
                    float v = (float)(amp * Math.Sin(2 * Math.PI * 440 * frame / 48000.0));
                    bool clip = false;
                    foreach (int a in at) if (frame >= a && frame < a + burst) clip = true;
                    s[2 * i] = clip ? clipValue * Math.Sign(v == 0 ? 1 : v) : v;
                    s[2 * i + 1] = clip && !leftOnly ? clipValue * Math.Sign(v == 0 ? 1 : v) : v;
                }
                ac.Feed(s, PacketFrames, false, p * PacketTicks);
            }
            ac.Finish((long)packets * PacketFrames);
            return ac;
        }

        static void SatClean()
        {
            string dir = WorkDir("sat-clean");
            try
            {
                AudioCapture ac = SatFeed(dir, "clean", 300, 0.995f, new int[0], 0, false, 1);
                Check(ac.ClipEvents == 0 && ac.ClipFrames == 0, "a tone at 0.995 counted as clipping: " + ac.ClipEvents + " / " + ac.ClipFrames);
                List<string> notes = new List<string>();
                Session.AudioNote(notes, "Micr\u00F3fono", ac);
                Check(notes.Count == 0, "notes for clean sound: " + string.Join(" | ", notes.ToArray()));
                Check(Session.Saturation(null, ac) == null, "a notice for clean sound");
            }
            finally { DeleteDir(dir); }
        }

        static void SatBursts()
        {
            string dir = WorkDir("sat-bursts");
            try
            {
                AudioCapture ac = SatFeed(dir, "bursts", 300, 0.5f, new int[] { 4800, 48000, 96000 }, 40, false, 1);
                Check(ac.ClipEvents == 3, "3 bursts counted as " + ac.ClipEvents);
                // Each burst of 40 frames counts from its third sample on.
                Check(ac.ClipFrames >= 3 * 36 && ac.ClipFrames <= 3 * 40, "clipped frames " + ac.ClipFrames);
                Check(Math.Abs(ac.FirstClipSeconds - 0.1) < 0.002, "first at " + ac.FirstClipSeconds + " s, it was at 0.1 s");
                Note(ac.ClipEvents + " momentos, " + ac.ClipFrames + " muestras, primero a " + ac.FirstClipSeconds + " s");
            }
            finally { DeleteDir(dir); }
        }

        static void SatPeaks()
        {
            string dir = WorkDir("sat-peaks");
            try
            {
                // Two frames at full scale are a peak, not a held clip.
                AudioCapture ac = SatFeed(dir, "peaks", 200, 0.5f, new int[] { 1000, 20000, 50000, 70000 }, 2, false, 1);
                Check(ac.ClipEvents == 0, "2-frame peaks counted as " + ac.ClipEvents);
                AudioCapture three = SatFeed(dir, "three", 200, 0.5f, new int[] { 1000 }, 3, false, 1);
                Check(three.ClipEvents == 1 && three.ClipFrames == 1, "3 frames: " + three.ClipEvents + " / " + three.ClipFrames);
            }
            finally { DeleteDir(dir); }
        }

        static void SatChannels()
        {
            string dir = WorkDir("sat-channels");
            try
            {
                AudioCapture left = SatFeed(dir, "left", 200, 0.3f, new int[] { 9600 }, 100, true, 1);
                Check(left.ClipEvents == 1, "a clip in the left channel only counted as " + left.ClipEvents);
                // Float sound from the system mix can go past 1.0: that is past full scale too.
                AudioCapture over = SatFeed(dir, "over", 200, 0.3f, new int[] { 9600 }, 100, false, 1.7f);
                Check(over.ClipEvents == 1, "samples at 1.7 counted as " + over.ClipEvents);
                // Muted (silent) packets never count.
                string path = TempAudioPath(dir, "muted");
                AudioCapture muted = AudioCapture.ForTest(path, true);
                float[] loud = new float[PacketFrames * 2];
                for (int i = 0; i < loud.Length; i++) loud[i] = 1f;
                for (int p = 0; p < 50; p++) muted.Feed(loud, PacketFrames, true, p * PacketTicks);
                muted.Finish(50L * PacketFrames);
                Check(muted.ClipEvents == 0, "silent packets counted as clipping: " + muted.ClipEvents);
            }
            finally { DeleteDir(dir); }
        }

        static void SatAcrossPackets()
        {
            string dir = WorkDir("sat-across");
            try
            {
                // A burst from frame 470 to 530 crosses the edge of the first packet (480 frames).
                AudioCapture ac = SatFeed(dir, "across", 100, 0.5f, new int[] { 470 }, 60, false, 1);
                Check(ac.ClipEvents == 1, "a burst across two packets counted as " + ac.ClipEvents);
                Check(ac.ClipFrames == 58, "frames " + ac.ClipFrames + ", expected 58 (60 minus the first two)");
            }
            finally { DeleteDir(dir); }
        }

        static void SatText()
        {
            string dir = WorkDir("sat-text");
            try
            {
                AudioCapture one = SatFeed(dir, "one", 300, 0.5f, new int[] { 48000 * 2 + 4800 }, 4800, false, 1);
                string t1 = Session.ClipText(one);
                Check(t1.StartsWith("satur\u00F3 una vez (a 0:02, ", StringComparison.Ordinal), "one burst: " + t1);
                AudioCapture many = SatFeed(dir, "many", 6500, 0.5f, new int[] { 48000 * 61, 48000 * 62 }, 480, false, 1);
                string t2 = Session.ClipText(many);
                Check(t2.StartsWith("satur\u00F3 en 2 momentos (el primero a 1:01, ", StringComparison.Ordinal), "two bursts: " + t2);
                List<string> notes = new List<string>();
                Session.AudioNote(notes, "Sonido del equipo", many);
                Check(notes.Count == 1 && notes[0].StartsWith("Sonido del equipo: satur\u00F3 en 2 momentos", StringComparison.Ordinal) && notes[0].Contains("baja el volumen"), "note: " + string.Join(" | ", notes.ToArray()));
                string both = Session.Saturation(many, one);
                Check(both.StartsWith("Ojo: el sonido del equipo satur\u00F3", StringComparison.Ordinal) && both.Contains(" y el micr\u00F3fono satur\u00F3 una vez"), "notice: " + both);
                Note(both);
            }
            finally { DeleteDir(dir); }
        }

        static void SatReport()
        {
            CineReport r = new CineReport();
            r.Width = 320; r.Height = 180; r.FpsNum = 30; r.FpsDen = 1;
            r.AudioNotes = new List<string> { "Micr\u00F3fono: satur\u00F3 una vez (a 0:05, 0,1 s a fondo de escala). La grabaci\u00F3n lo guarda tal cual." };
            r.Saturation = "Ojo: el micr\u00F3fono satur\u00F3 una vez (a 0:05, 0,1 s a fondo de escala).";
            string sum = r.Summary();
            Check(sum.Contains("- Micr\u00F3fono: satur\u00F3 una vez"), "the summary does not list it: " + sum);
            string json = r.ToJson();
            Check(json.Contains("\"saturation\""), "no saturation in the JSON: " + json);
            // Clipping is not a capture fault: a clean recording with it is still the short "verified" notice.
            Check(r.CaptureClean, "clipping made the capture unclean");
            r.Saturation = null;
            Check(r.ToJson().Contains("\"saturation\":null"), "null saturation not written as null");
        }
    }
}

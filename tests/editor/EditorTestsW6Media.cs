// Round 6 (microphone meter, GIF of a piece), suite "w6media". The meter is tested on samples made here and with a fake
// device: no real microphone is ever opened. Windows are parked off screen and never take the focus. The GIF tests make
// their videos with FFmpeg and skip without it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace Stackshot.Tests
{
    static partial class EditorTests
    {
        static int w6mStrays;

        static void W6MediaSuite()
        {
            // No test may ever open a real microphone: a meter that slips past a test's own fakes gets a dead one, and
            // the last test says so. Left in place for the rest of the run.
            MicMonitor.NewSource = delegate(Func<string> device)
            {
                Interlocked.Increment(ref w6mStrays);
                W6mFakeMic f = new W6mFakeMic();
                f.FailOpens = int.MaxValue;
                return f;
            };
            Test("w6media", "mic-levels-float32-sine", W6mLevelsFloat);
            Test("w6media", "mic-levels-int16-and-device-formats", W6mLevelsInt16);
            Test("w6media", "mic-clipping-needs-three-in-a-row-across-packets", W6mClipping);
            Test("w6media", "mic-take-resets-but-total-keeps-counting", W6mTake);
            Test("w6media", "mic-dbfs-scale-and-colours", W6mScale);
            Test("w6media", "mic-ballistics-hold-decay-and-saturation", W6mBallistics);
            Test("w6media", "mic-monitor-start-stop-with-fake-device", W6mMonitorLifecycle);
            Test("w6media", "mic-monitor-no-device-denied-unplugged-silent", W6mMonitorProblems);
            Test("w6media", "mic-monitor-never-opens-during-a-recording", W6mMonitorRecording);
            Test("w6media", "mic-monitor-stop-never-waits-for-a-stuck-device", W6mMonitorStuck);
            Test("w6media", "mic-monitor-follows-a-device-change", W6mMonitorMoved);
            Test("w6media", "mic-check-wanted-rules-and-words", W6mCheckRules);
            Test("w6media", "mic-check-runs-only-while-shown", W6mCheckShown);
            Test("w6media", "mic-check-button-test-ends-when-hidden", W6mCheckButton);
            Test("w6media", "mic-check-shows-saturando-problems-and-recording", W6mCheckStates);
            Test("w6media", "mic-record-setup-row-follows-the-switch", W6mRecordSetup);
            Test("w6media", "mic-record-page-has-the-test-button", W6mRecordPage);
            Test("w6media", "gif-args-size-rate-and-editor-palette", W6mGifArgs);
            Test("w6media", "gif-palette-with-stand-in-equals-plain-palette", W6mGifPalette);
            Test("w6media", "gif-piece-frames-duration-size-and-content", W6mGifPiece);
            Test("w6media", "gif-rotated-phone-video", W6mGifRotated);
            Test("w6media", "gif-anamorphic-gets-square-pixels", W6mGifAnamorphic);
            Test("w6media", "gif-cancel-leaves-nothing-behind", W6mGifCancel);
            Test("w6media", "gif-long-piece-asks-first", W6mGifLong);
            Test("w6media", "gif-key-bar-button-and-menu-item", W6mGifUi);
            Test("w6media", "gif-from-a-gif-keeps-its-timing", W6mGifFromGif);
            Test("w6media", "gif-tiny-piece", W6mGifTiny);
            Test("w6media", "gif-odd-paths-accents-spaces-percent", W6mGifPaths);
            Test("w6media", "ui-shots-when-asked", W6mShots);
            Test("w6media", "r4-gif-flash-follows-dated-save-folders", R4GifReadyText);
            Test("w6media", "r4-wasapi-mic-states-without-a-device", R4WasapiStates);
            Test("w6media", "r4-gif-failure-leaves-nothing-and-frees-the-name", R4GifFailure);
            // One real microphone, opened for at most 2 s, only when asked (STACKSHOT_W6_MIC_PROBE=1); never in a normal run.
            if (Environment.GetEnvironmentVariable("STACKSHOT_W6_MIC_PROBE") == "1") Test("w6media", "r4-real-microphone-probe", R4MicProbe);
            Test("w6media", "mic-no-meter-ever-reached-a-real-device", delegate
            {
                W6mPump(MicCheck.GraceMs + 300);
                Check(Thread.VolatileRead(ref w6mStrays) == 0, w6mStrays + " meters asked for a device outside their test");
            });
        }

        // ---------------------------------------------------------------- the level computation

        static float[] W6mSine(int frames, int channels, double amp, double hz)
        {
            float[] s = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                float v = (float)(amp * Math.Sin(2 * Math.PI * hz * i / 48000.0));
                for (int c = 0; c < channels; c++) s[i * channels + c] = v;
            }
            return s;
        }

        static string W6F(double v) { return v.ToString("0.0000", CultureInfo.InvariantCulture); }

        static void W6mLevelsFloat()
        {
            MicLevels l = new MicLevels();
            // One second of a 1 kHz sine at half scale, stereo, in 10 ms packets.
            float[] s = W6mSine(48000, 2, 0.5, 1000);
            for (int p = 0; p < 100; p++) l.Add(s, p * 480 * 2, 480, 2);
            MicReading r = l.Take();
            Check(Math.Abs(r.Peak - 0.5) < 0.001, "peak " + W6F(r.Peak));
            Check(Math.Abs(r.Rms - 0.5 / Math.Sqrt(2)) < 0.001, "rms " + W6F(r.Rms) + ", a sine's is amplitude / sqrt 2");
            Check(r.Frames == 48000, "frames " + r.Frames);
            Check(!r.Clipped, "a half-scale sine clipped");
            Check(Math.Abs(MicScale.Dbfs(r.Peak) - (-6.0206)) < 0.02, "peak dBFS " + W6F(MicScale.Dbfs(r.Peak)));
            Check(Math.Abs(MicScale.Dbfs(r.Rms) - (-9.0309)) < 0.02, "rms dBFS " + W6F(MicScale.Dbfs(r.Rms)));
            // A quiet one: -40 dBFS.
            l.Add(W6mSine(4800, 1, 0.01, 440), 0, 4800, 1);
            r = l.Take();
            Check(Math.Abs(MicScale.Dbfs(r.Peak) + 40) < 0.1, "quiet peak " + W6F(MicScale.Dbfs(r.Peak)));
            // Only the loudest channel counts for the peak; garbage never breaks it.
            float[] lr = { 0.1f, -0.8f, 0.2f, 0.3f, float.NaN, 0.05f };
            l.Add(lr, 0, 3, 2);
            r = l.Take();
            Check(Math.Abs(r.Peak - 0.8) < 1e-6, "loudest channel " + W6F(r.Peak));
            Check(!float.IsNaN(r.Rms) && r.Rms > 0, "rms with a NaN sample: " + r.Rms);
            // Bad arguments are ignored.
            l.Add((float[])null, 0, 10, 2);
            l.Add(lr, 0, 100, 2);
            l.Add(lr, 0, 3, 0);
            l.Add(lr, 10, 3, 2);
            r = l.Take();
            Check(r.Frames == 3, "a count past the end was not cut to what is there: " + r.Frames);
            Add("sine peak " + W6F(MicScale.Dbfs(0.5)) + " dBFS, rms " + W6F(MicScale.Dbfs(0.5 / Math.Sqrt(2))) + " dBFS");
        }

        static byte[] W6mInt16Bytes(float[] s)
        {
            byte[] b = new byte[s.Length * 2];
            for (int i = 0; i < s.Length; i++)
            {
                short v = (short)Math.Max(-32768, Math.Min(32767, Math.Round(s[i] * 32767)));
                b[2 * i] = (byte)(v & 0xFF);
                b[2 * i + 1] = (byte)((v >> 8) & 0xFF);
            }
            return b;
        }

        static void W6mLevelsInt16()
        {
            float[] s = W6mSine(4800, 2, 0.25, 1000);
            short[] pcm = new short[s.Length];
            for (int i = 0; i < s.Length; i++) pcm[i] = (short)Math.Round(s[i] * 32767);
            MicLevels a = new MicLevels(), b = new MicLevels(), c = new MicLevels(), d = new MicLevels();
            a.Add(pcm, 0, 4800, 2);
            b.AddRaw(W6mInt16Bytes(s), 0, 4800, 2, 16, false);
            byte[] f32 = new byte[s.Length * 4];
            Buffer.BlockCopy(s, 0, f32, 0, f32.Length);
            c.AddRaw(f32, 0, 4800, 2, 32, true);
            d.Add(s, 0, 4800, 2);
            MicReading ra = a.Take(), rb = b.Take(), rc = c.Take(), rd = d.Take();
            Check(Math.Abs(ra.Peak - 0.25) < 0.001 && Math.Abs(ra.Rms - 0.25 / Math.Sqrt(2)) < 0.001, "int16 peak " + W6F(ra.Peak) + " rms " + W6F(ra.Rms));
            Check(Math.Abs(ra.Peak - rb.Peak) < 1e-6 && Math.Abs(ra.Rms - rb.Rms) < 1e-6, "int16 as raw bytes differs: " + W6F(rb.Peak) + "/" + W6F(rb.Rms));
            Check(rc.Peak == rd.Peak && Math.Abs(rc.Rms - rd.Rms) < 1e-7, "float32 as raw bytes differs: " + W6F(rc.Peak) + " vs " + W6F(rd.Peak));
            Check(Math.Abs(ra.Peak - rd.Peak) < 0.0001, "int16 and float32 of the same sine: " + W6F(ra.Peak) + " vs " + W6F(rd.Peak));
            // Full scale in int16 (-32768 and 32767) reads as full scale.
            MicLevels e = new MicLevels();
            e.Add(new short[] { -32768, 32767, 0, 0 }, 0, 4, 1);
            MicReading re = e.Take();
            Check(re.Peak >= 0.999f, "int16 full scale peak " + W6F(re.Peak));
            // Other device formats: 24-bit packed, 32-bit integer, 8-bit unsigned and 64-bit float, at -6 dBFS.
            MicLevels g = new MicLevels();
            int half24 = 1 << 22;
            g.AddRaw(new byte[] { (byte)(half24 & 0xFF), (byte)((half24 >> 8) & 0xFF), (byte)((half24 >> 16) & 0xFF) }, 0, 1, 1, 24, false);
            Check(Math.Abs(g.Take().Peak - 0.5) < 1e-6, "24-bit half scale");
            g.AddRaw(BitConverter.GetBytes(int.MinValue / 2), 0, 1, 1, 32, false);
            Check(Math.Abs(g.Take().Peak - 0.5) < 1e-6, "32-bit integer half scale");
            g.AddRaw(new byte[] { 192 }, 0, 1, 1, 8, false);
            Check(Math.Abs(g.Take().Peak - 0.5) < 1e-6, "8-bit half scale");
            g.AddRaw(BitConverter.GetBytes(-0.5), 0, 1, 1, 64, true);
            Check(Math.Abs(g.Take().Peak - 0.5) < 1e-6, "64-bit float half scale");
            Add("int16 and float32 agree to " + W6F(Math.Abs(ra.Peak - rd.Peak)));
        }

        static void W6mClipping()
        {
            MicLevels l = new MicLevels();
            // Two full-scale samples in a row: a peak, not clipping.
            l.Add(new float[] { 0.2f, 1f, 1f, 0.3f }, 0, 4, 1);
            MicReading r = l.Take();
            Check(!r.Clipped && r.Peak == 1f, "two samples at full scale counted as clipping");
            // Three in a row clip.
            l.Add(new float[] { 0.2f, 1f, -1f, 0.9995f, 0.1f }, 0, 5, 1);
            Check(l.Take().Clipped, "three samples at full scale did not clip");
            // A run split over two packets still counts.
            l.Add(new float[] { 0f, 1f, 1f }, 0, 3, 1);
            Check(!l.Take().Clipped, "two at the end of a packet clipped already");
            l.Add(new float[] { 1f, 0f }, 0, 2, 1);
            Check(l.Take().Clipped, "a run of three across two packets was missed");
            // A run still going carries on into the next packet, even after a Take.
            l.Add(new float[] { 1f, 1f, 1f }, 0, 3, 1);
            Check(l.Take().Clipped, "three at full scale");
            l.Add(new float[] { 1f }, 0, 1, 1);
            Check(l.Take().Clipped, "a run still going did not carry on");
            // Each channel has its own run: alternating channels never make three in a row on one.
            MicLevels st = new MicLevels();
            st.Add(new float[] { 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f }, 0, 4, 2);
            Check(!st.Take().Clipped, "full scale alternating between channels clipped");
            st.Add(new float[] { 1f, 0f, 1f, 0f, 1f, 0f }, 0, 3, 2);
            Check(st.Take().Clipped, "three in a row on the left channel did not clip");
            // A silent packet breaks a run.
            MicLevels si = new MicLevels();
            si.Add(new float[] { 1f, 1f }, 0, 2, 1);
            si.AddSilence(480, 1);
            si.Add(new float[] { 1f }, 0, 1, 1);
            Check(!si.Take().Clipped, "a silent packet did not break the run");
            // Just under the threshold never clips; int16 at 32767 does.
            MicLevels th = new MicLevels();
            th.Add(new float[] { 0.998f, 0.998f, 0.998f, 0.998f }, 0, 4, 1);
            Check(!th.Take().Clipped, "0.998 clipped");
            th.Add(new short[] { 32767, 32767, 32767 }, 0, 3, 1);
            Check(th.Take().Clipped, "int16 32767 three times did not clip");
            // The recording uses the same threshold.
            Check(MicLevels.FullScale == 0.999f, "full scale is " + MicLevels.FullScale);
        }

        static void W6mTake()
        {
            MicLevels l = new MicLevels();
            l.Add(W6mSine(480, 2, 0.5, 1000), 0, 480, 2);
            l.AddSilence(480, 2);
            MicReading r = l.Take();
            Check(r.Frames == 960, "frames " + r.Frames);
            // The silence halves the power: rms of half scale sine over twice the time.
            Check(Math.Abs(r.Rms - 0.5 / Math.Sqrt(2) / Math.Sqrt(2)) < 0.002, "rms with silence " + W6F(r.Rms));
            MicReading z = l.Take();
            Check(z.Peak == 0 && z.Rms == 0 && z.Frames == 0 && !z.Clipped, "a second take is not empty");
            Check(l.Total == 960, "total " + l.Total);
            l.AddSilence(100, 2);
            Check(l.Total == 1060 && l.Take().Peak == 0, "silence changed the peak or the total");
            // A reader on another thread never sees a half-updated reading.
            MicLevels shared = new MicLevels();
            bool stop = false;
            Thread writer = new Thread(delegate()
            {
                float[] s = W6mSine(480, 2, 0.5, 1000);
                while (!stop) shared.Add(s, 0, 480, 2);
            });
            writer.IsBackground = true;
            writer.Start();
            int bad = 0;
            for (int i = 0; i < 2000; i++)
            {
                MicReading m = shared.Take();
                if (m.Frames > 0 && (m.Peak > 0.5001 || m.Rms > m.Peak + 1e-6)) bad++;
            }
            stop = true;
            writer.Join(2000);
            Check(bad == 0, bad + " torn readings");
        }

        static void W6mScale()
        {
            Check(MicScale.Dbfs(1) == 0, "full scale " + MicScale.Dbfs(1));
            Check(double.IsNegativeInfinity(MicScale.Dbfs(0)), "silence " + MicScale.Dbfs(0));
            Check(MicScale.Clamp(double.NegativeInfinity) == MicScale.Floor && MicScale.Clamp(double.NaN) == MicScale.Floor, "silence is not the empty bar");
            Check(MicScale.Clamp(3) == 0, "above full scale is past the end of the bar");
            Check(MicScale.Position(MicScale.Floor) == 0 && MicScale.Position(0) == 1 && Math.Abs(MicScale.Position(-30) - 0.5) < 1e-9, "positions");
            Check(Math.Abs(MicScale.Position(-6) - 0.9) < 1e-9, "-6 dBFS sits at " + MicScale.Position(-6));
            Check(MicScale.Zone(-20, false) == 0 && MicScale.Zone(-6.01, true) == 0, "quiet is not green");
            Check(MicScale.Zone(-6, false) == 1 && MicScale.Zone(-1, false) == 1 && MicScale.Zone(0, false) == 1, "loud is not yellow");
            Check(MicScale.Zone(-3, true) == 2 && MicScale.Zone(0, true) == 2, "clipping is not red");
        }

        static MicReading W6mReading(double db, bool clip)
        {
            MicReading r = new MicReading();
            r.Peak = (float)Math.Pow(10, db / 20);
            r.Rms = r.Peak * 0.7f;
            r.Clipped = clip;
            r.Frames = 1600;
            return r;
        }

        static void W6mBallistics()
        {
            MicBallistics b = new MicBallistics();
            Check(b.Peak == MicScale.Floor && b.Hold(0) == MicScale.Floor && !b.Clipping(0), "a new meter is not empty");
            b.Step(W6mReading(-6, false), 1000);
            Check(Math.Abs(b.Peak + 6) < 0.01 && Math.Abs(b.Hold(1000) + 6) < 0.01, "attack: peak " + W6F(b.Peak) + ", hold " + W6F(b.Hold(1000)));
            // Silence after it, ticked every 33 ms for a second.
            double t = 1000;
            for (int i = 0; i < 30; i++) { t += 33; b.Step(new MicReading(), t); }
            double fell = -6 - MicBallistics.BarFall * (t - 1000) / 1000;
            Check(Math.Abs(b.Peak - fell) < 0.05, "the bar fell to " + W6F(b.Peak) + ", expected " + W6F(fell));
            // The held peak stays for 1.5 s, then falls at 20 dB/s down to the floor.
            Check(Math.Abs(b.Hold(2000) + 6) < 0.01 && Math.Abs(b.Hold(2500) + 6) < 0.01, "the hold moved before 1.5 s: " + W6F(b.Hold(2500)));
            Check(Math.Abs(b.Hold(3000) + 16) < 0.01, "hold 0.5 s after it ends: " + W6F(b.Hold(3000)) + ", expected -16");
            Check(b.Hold(10000) == MicScale.Floor, "the hold never reached the floor");
            // A lower peak during the hold keeps the mark; a higher one moves it.
            MicBallistics h = new MicBallistics();
            h.Step(W6mReading(-10, false), 0);
            h.Step(W6mReading(-20, false), 500);
            Check(Math.Abs(h.Hold(600) + 10) < 0.01, "a lower peak moved the mark to " + W6F(h.Hold(600)));
            h.Step(W6mReading(-3, false), 700);
            Check(Math.Abs(h.Hold(800) + 3) < 0.01, "a higher peak did not move the mark: " + W6F(h.Hold(800)));
            // Under the falling mark (-39 at 4 s) a peak leaves it; once the mark has fallen below one (-59 at 5 s), that
            // peak takes it.
            h.Step(W6mReading(-40, false), 4000);
            Check(Math.Abs(h.Hold(4000) + 39) < 0.01, "a peak under the falling mark moved it: " + W6F(h.Hold(4000)));
            h.Step(W6mReading(-40, false), 5000);
            Check(Math.Abs(h.Hold(5000) + 40) < 0.01, "a peak above the falling mark did not take it: " + W6F(h.Hold(5000)));
            // RMS never above the peak.
            Check(h.Rms <= h.Peak + 1e-9, "rms " + W6F(h.Rms) + " above peak " + W6F(h.Peak));
            // Saturando stays 1.5 s after the last clipped samples.
            MicBallistics c = new MicBallistics();
            c.Step(W6mReading(0, true), 100);
            Check(c.Clipping(100) && c.Clipping(1500), "Saturando did not stay up");
            Check(!c.Clipping(1700), "Saturando stayed too long");
            c.Step(W6mReading(-1, true), 1000);
            Check(c.Clipping(2400) && !c.Clipping(2600), "a new clip did not extend Saturando");
            // A long gap between ticks (the window was busy) never pushes the bar below the floor.
            c.Step(new MicReading(), 60000);
            Check(c.Peak == MicScale.Floor && c.Rms == MicScale.Floor, "after a long gap: " + W6F(c.Peak));
            c.Reset();
            Check(c.Peak == MicScale.Floor && c.Hold(60000) == MicScale.Floor && !c.Clipping(60000), "reset left something");
        }

        // ---------------------------------------------------------------- the monitor, with a fake device

        // A microphone that is not there: a sine (or full scale) in 10 ms packets, or failures on demand.
        sealed class W6mFakeMic : IMicSource
        {
            public int opens, closes, reads;
            public volatile bool IsOpen;
            public string Device;
            public int FailOpens;                        // the first opens fail
            public MicState OpenFailure = MicState.NoDevice;
            public int OpenDelayMs;
            public int UnplugAtRead = -1;                // that read reports the device gone
            public bool Silent, Clip;
            public volatile bool MoveOnce, ThrowOnRead;
            public float Amplitude = 0.5f;
            long lastRead;

            public int Opens { get { return Thread.VolatileRead(ref opens); } }
            public int Closes { get { return Thread.VolatileRead(ref closes); } }
            public int Reads { get { return Thread.VolatileRead(ref reads); } }

            public MicState Open()
            {
                int n = Interlocked.Increment(ref opens);
                if (OpenDelayMs > 0) Thread.Sleep(OpenDelayMs);
                if (n <= FailOpens) return OpenFailure;
                IsOpen = true;
                lastRead = Stopwatch.GetTimestamp();
                return MicState.Live;
            }

            public bool Read(MicLevels into)
            {
                if (!IsOpen) throw new InvalidOperationException("read while closed");
                int n = Interlocked.Increment(ref reads);
                if (ThrowOnRead) { ThrowOnRead = false; throw new InvalidOperationException("driver failure"); }
                if (n == UnplugAtRead) return false;
                if (Silent) return true;
                // What has arrived since the last read, as a device would deliver it.
                long now = Stopwatch.GetTimestamp();
                int frames = (int)Math.Min(4800, (now - lastRead) * 48000 / Stopwatch.Frequency);
                if (frames <= 0) return true;
                lastRead = now;
                float[] s = new float[frames * 2];
                for (int i = 0; i < frames; i++)
                {
                    float v = Clip ? (i % 100 < 50 ? 1f : -1f) : (float)(Amplitude * Math.Sin(2 * Math.PI * 1000 * i / 48000.0));
                    s[2 * i] = s[2 * i + 1] = v;
                }
                into.Add(s, 0, frames, 2);
                return true;
            }

            public bool Moved()
            {
                if (!MoveOnce) return false;
                MoveOnce = false;
                return true;
            }

            public string Name { get { return "Micro de prueba"; } }
            public string Problem { get { return "simulado"; } }

            public void Close()
            {
                Interlocked.Increment(ref closes);
                IsOpen = false;
            }
        }

        static volatile bool w6mRecording;

        // Runs body with every meter using fake devices made by make (collected in made), and puts the real ones back.
        static void W6mWithFakes(Func<W6mFakeMic> make, List<W6mFakeMic> made, Action body)
        {
            Func<Func<string>, IMicSource> oldSource = MicMonitor.NewSource;
            Func<bool> oldRecording = MicMonitor.IsRecording;
            MicMonitor.NewSource = delegate(Func<string> device)
            {
                W6mFakeMic f = make();
                f.Device = device != null ? device() : null;
                lock (made) made.Add(f);
                return f;
            };
            MicMonitor.IsRecording = delegate { return w6mRecording; };
            w6mRecording = false;
            try { body(); }
            finally
            {
                MicMonitor.NewSource = oldSource;
                MicMonitor.IsRecording = oldRecording;
                w6mRecording = false;
            }
        }

        static bool W6mUntil(Func<bool> cond, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return cond();
        }

        static int W6mCount(List<W6mFakeMic> made) { lock (made) return made.Count; }

        static W6mFakeMic W6mAt(List<W6mFakeMic> made, int i) { lock (made) return made[i]; }

        static void W6mJoin(MicMonitor m)
        {
            Thread t = m.LastThread;
            Check(t == null || t.Join(3000), "the meter's thread did not end");
        }

        static void W6mMonitorLifecycle()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                MicMonitor m = new MicMonitor(delegate { return "{0.0.1.00000000}.{mic-1}"; });
                Check(!m.Running && m.State == MicState.Off, "a new monitor runs");
                Check(m.Take().Frames == 0, "a stopped monitor has a reading");
                m.Start();
                Check(m.Running, "Start did not start it");
                Check(W6mUntil(delegate { return m.State == MicState.Live; }, 3000), "never live: " + m.State);
                Check(m.DeviceName == "Micro de prueba", "device name " + m.DeviceName);
                Check(W6mAt(made, 0).Device == "{0.0.1.00000000}.{mic-1}", "the chosen device was not asked for: " + W6mAt(made, 0).Device);
                m.Take();
                Thread.Sleep(150);
                MicReading r = m.Take();
                Check(r.Frames > 2000 && Math.Abs(r.Peak - 0.5) < 0.01, "reading: " + r.Frames + " frames, peak " + W6F(r.Peak));
                // A second Start is ignored.
                m.Start();
                Check(W6mCount(made) == 1, "a second Start opened another device");
                Thread t = m.LastThread;
                Stopwatch sw = Stopwatch.StartNew();
                m.Stop();
                Check(sw.ElapsedMilliseconds < 50, "Stop took " + sw.ElapsedMilliseconds + " ms");
                Check(!m.Running && m.State == MicState.Off, "after Stop: " + m.Running + " " + m.State);
                Check(t.Join(2000), "the thread did not end after Stop");
                Check(!W6mAt(made, 0).IsOpen && W6mAt(made, 0).Closes >= 1, "the device was left open");
                Check(m.Take().Frames == 0, "a stopped monitor still reads");
                m.Stop();
                // Again, with a new device.
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.Live && W6mCount(made) == 2; }, 3000), "the second start: " + m.State);
                m.Stop();
                W6mJoin(m);
                Check(!W6mAt(made, 1).IsOpen, "the second device was left open");
                Add(made.Count + " opens, reading " + r.Frames + " frames in 150 ms");
            });
        }

        static void W6mMonitorProblems()
        {
            List<string> seen = new List<string>();
            // No microphone at all: said at once and retried every second.
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.FailOpens = 1000; f.OpenFailure = MicState.NoDevice; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.NoDevice; }, 2000), "no device: " + m.State);
                Thread.Sleep(2300);
                Check(W6mAt(made, 0).Opens >= 3, "retried " + W6mAt(made, 0).Opens + " times in 2.3 s");
                Check(m.State == MicState.NoDevice, "no device became " + m.State);
                m.Stop();
                W6mJoin(m);
                seen.Add("sin micro: " + W6mAt(made, 0).Opens + " intentos");
            });
            // Windows' privacy settings say no.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.FailOpens = 1000; f.OpenFailure = MicState.Denied; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.Denied; }, 2000), "denied: " + m.State);
                m.Stop();
                W6mJoin(m);
            });
            // Busy at first (exclusive mode elsewhere), then free.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.FailOpens = 2; f.OpenFailure = MicState.NotResponding; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.NotResponding; }, 2000), "busy: " + m.State);
                Check(W6mUntil(delegate { return m.State == MicState.Live; }, 4000), "never recovered from busy: " + m.State);
                m.Stop();
                W6mJoin(m);
            });
            // Unplugged while measuring: does not respond, then opens again.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.UnplugAtRead = 15; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                bool lost = W6mUntil(delegate { return m.State == MicState.NotResponding; }, 3000);
                Check(lost, "an unplugged device still reads as " + m.State);
                Check(W6mUntil(delegate { return m.State == MicState.Live && W6mAt(made, 0).Opens >= 2; }, 3000), "not reopened: " + m.State + ", " + W6mAt(made, 0).Opens + " opens");
                m.Stop();
                W6mJoin(m);
                Check(!W6mAt(made, 0).IsOpen, "left open");
                seen.Add("desenchufado: reabierto");
            });
            // Opens but never delivers.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.Silent = true; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Thread.Sleep(500);
                Check(m.State == MicState.Opening, "a device with nothing yet reads as " + m.State);
                Check(W6mUntil(delegate { return m.State == MicState.NotResponding; }, 4000), "a silent device: " + m.State);
                m.Stop();
                W6mJoin(m);
            });
            // A driver that throws: does not respond, retried.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.ThrowOnRead = true; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return W6mCount(made) > 0 && W6mAt(made, 0).Opens >= 2 && m.State == MicState.Live; }, 4000), "after a throw: " + m.State);
                m.Stop();
                W6mJoin(m);
            });
            Add(string.Join("; ", seen.ToArray()));
        }

        static void W6mMonitorRecording()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                w6mRecording = true;
                m.Start();
                Check(!m.Running && m.State == MicState.Recording, "started during a recording: " + m.Running + " " + m.State);
                Thread.Sleep(100);
                Check(W6mCount(made) == 0, "a device was opened during a recording");
                Check(!m.Yield(), "Yield while stopped");
                w6mRecording = false;
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.Live; }, 3000), "after the recording: " + m.State);
                Check(!m.Yield(), "Yield with no recording");
                w6mRecording = true;
                Thread t = m.LastThread;
                Check(m.Yield(), "Yield did not step aside for the recording");
                Check(!m.Running && m.State == MicState.Recording, "after Yield: " + m.State);
                Check(t.Join(2000) && !W6mAt(made, 0).IsOpen, "the device stayed open during the recording");
                m.Stop();
                Check(m.State == MicState.Off, "Stop after Yield: " + m.State);
            });
        }

        static void W6mMonitorStuck()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.OpenDelayMs = 800; return f; }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return W6mCount(made) == 1 && W6mAt(made, 0).Opens == 1; }, 2000), "never tried to open");
                Check(m.State == MicState.Opening, "while opening: " + m.State);
                Thread t = m.LastThread;
                Stopwatch sw = Stopwatch.StartNew();
                m.Stop();
                long took = sw.ElapsedMilliseconds;
                Check(took < 50, "Stop waited " + took + " ms for a device that hangs");
                Check(t.Join(3000), "the thread never ended");
                Check(!W6mAt(made, 0).IsOpen && W6mAt(made, 0).Reads == 0, "the device opened late was not closed (or was read)");
                // Four hanging threads at once: no fifth until they end.
                MicMonitor[] ms = new MicMonitor[4];
                for (int i = 0; i < 4; i++)
                {
                    ms[i] = new MicMonitor(null);
                    ms[i].Start();
                    int k = i + 2;
                    Check(W6mUntil(delegate { return W6mCount(made) == k && W6mAt(made, k - 1).Opens == 1; }, 2000), "hanging device " + (i + 1) + " never tried to open");
                    ms[i].Stop();
                }
                MicMonitor fifth = new MicMonitor(null);
                fifth.Start();
                Check(!fifth.Running && fifth.State == MicState.NotResponding, "a fifth thread started while four hang: " + fifth.State);
                foreach (MicMonitor x in ms) W6mJoin(x);
                fifth.Start();
                Check(fifth.Running, "no start after the stuck threads ended");
                fifth.Stop();
                W6mJoin(fifth);
                Add("Stop returned in " + took + " ms");
            });
        }

        static void W6mMonitorMoved()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                MicMonitor m = new MicMonitor(null);
                m.Start();
                Check(W6mUntil(delegate { return m.State == MicState.Live; }, 3000), "never live");
                W6mAt(made, 0).MoveOnce = true;
                Check(W6mUntil(delegate { return W6mAt(made, 0).Opens >= 2; }, 3000), "a device change did not reopen");
                Check(W6mAt(made, 0).Closes >= 1, "reopened without closing");
                Check(W6mUntil(delegate { return m.State == MicState.Live && W6mAt(made, 0).IsOpen; }, 3000), "after the change: " + m.State);
                m.Stop();
                W6mJoin(m);
            });
        }

        // ---------------------------------------------------------------- the control

        static void W6mPump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
        }

        static bool W6mPumpUntil(Func<bool> cond, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Application.DoEvents();
                Thread.Sleep(5);
            }
            return cond();
        }

        // A plain window off screen that never takes the focus.
        static W.Window W6mParked(W.UIElement content)
        {
            W.Window w = new W.Window();
            w.WindowStyle = W.WindowStyle.None;
            w.ResizeMode = W.ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.Focusable = false;
            w.WindowStartupLocation = W.WindowStartupLocation.Manual;
            w.Left = -32000;
            w.Top = -32000;
            w.Width = 420;
            w.Height = 120;
            w.Content = content;
            w.Show();
            return w;
        }

        static void W6mCheckRules()
        {
            Check(MicCheck.Wanted(true, true, true, W.WindowState.Normal), "on and shown");
            Check(MicCheck.Wanted(true, true, true, W.WindowState.Maximized), "maximized");
            Check(!MicCheck.Wanted(false, true, true, W.WindowState.Normal), "switched off");
            Check(!MicCheck.Wanted(true, false, true, W.WindowState.Normal), "the control is hidden (another page)");
            Check(!MicCheck.Wanted(true, true, false, W.WindowState.Normal), "the window is hidden");
            Check(!MicCheck.Wanted(true, true, true, W.WindowState.Minimized), "the window is minimized");
            Check(MicCheck.Words(MicState.Live, 5000) == null && MicCheck.Words(MicState.Off, 0) == null, "a line over a working meter");
            Check(MicCheck.Words(MicState.Opening, 100) == null, "the opening line shows at once");
            // R4: the real built-in microphone took ~0.6 s to deliver (the guarded probe); a line due at 600 ms flashed for a tick.
            Check(MicCheck.Words(MicState.Opening, 700) == null, "the opening line flashes during a normal open (0.6 s)");
            Check(MicCheck.Words(MicState.Opening, 1100) == "Abriendo el micr\u00F3fono\u2026", "opening: " + MicCheck.Words(MicState.Opening, 1100));
            Check(MicCheck.Words(MicState.NoDevice, 0) == "No hay micr\u00F3fono", "no device: " + MicCheck.Words(MicState.NoDevice, 0));
            Check(MicCheck.Words(MicState.NotResponding, 0) == "El micr\u00F3fono no responde", "not responding: " + MicCheck.Words(MicState.NotResponding, 0));
            Check(MicCheck.Words(MicState.Denied, 0) == "Windows no deja usar el micr\u00F3fono", "denied: " + MicCheck.Words(MicState.Denied, 0));
            Check(MicCheck.Words(MicState.Recording, 0) == "Se est\u00E1 grabando", "recording: " + MicCheck.Words(MicState.Recording, 0));
        }

        static void W6mCheckShown()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                MicCheck c = new MicCheck(delegate { return ""; }, false);
                c.On = true;
                W6mPump(100);
                Check(!c.Active && !c.Monitor.Running && W6mCount(made) == 0, "the meter opened the device off screen");
                WC.Grid host = new WC.Grid();
                host.Children.Add(c);
                W.Window w = W6mParked(host);
                try
                {
                    Check(W6mPumpUntil(delegate { return c.Monitor.State == MicState.Live; }, 3000), "shown, the meter is " + c.Monitor.State);
                    Check(W6mPumpUntil(delegate { return c.BarPeak > -10; }, 2000), "the bar does not move: " + W6F(c.BarPeak));
                    // A rebuild that puts it back at once keeps the device open.
                    host.Children.Remove(c);
                    WC.StackPanel other = new WC.StackPanel();
                    other.Children.Add(c);
                    w.Content = other;
                    W6mPump(MicCheck.GraceMs + 300);
                    Check(c.Monitor.Running && W6mCount(made) == 1, "a rebuild reopened or closed the device (" + W6mCount(made) + " opens)");
                    // Hidden: lets go after the grace period, keeps On (no button), and comes back when shown.
                    w.Hide();
                    Check(W6mPumpUntil(delegate { return !c.Monitor.Running; }, MicCheck.GraceMs + 1000), "still running with the window hidden");
                    Check(W6mUntil(delegate { return !W6mAt(made, 0).IsOpen; }, 2000), "the device stayed open with the window hidden");
                    Check(c.On, "hiding switched the meter off");
                    w.Show();
                    Check(W6mPumpUntil(delegate { return c.Monitor.State == MicState.Live && W6mCount(made) == 2; }, 3000), "shown again: " + c.Monitor.State);
                    // Taken out of the window: stops.
                    other.Children.Remove(c);
                    Check(W6mPumpUntil(delegate { return !c.Monitor.Running; }, MicCheck.GraceMs + 1000), "still running out of the window");
                    other.Children.Add(c);
                    Check(W6mPumpUntil(delegate { return c.Monitor.Running; }, 2000), "back in the window it did not start");
                    // Switched off: at once.
                    c.On = false;
                    Check(!c.Monitor.Running && !c.Active, "switching off did not stop it at once");
                    c.On = true;
                    Check(W6mPumpUntil(delegate { return c.Monitor.State == MicState.Live; }, 3000), "switched on again: " + c.Monitor.State);
                }
                finally { w.Close(); }
                Check(W6mPumpUntil(delegate { return !c.Monitor.Running; }, MicCheck.GraceMs + 1000), "still running after the window closed");
                W6mJoin(c.Monitor);
                foreach (W6mFakeMic f in made) Check(!f.IsOpen, "a device was left open");
                Add(made.Count + " opens");
            });
        }

        static void W6mClick(MacButton b)
        {
            MethodInfo m = typeof(Drawn).GetMethod("Clicked", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(b, new object[] { new W.Point(5, 5) });
        }

        static void W6mCheckButton()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                MicCheck c = new MicCheck(delegate { return ""; }, true);
                List<bool> switched = new List<bool>();
                c.Switched += delegate(bool v) { switched.Add(v); };
                W.Window w = W6mParked(c);
                try
                {
                    W6mPump(200);
                    Check(!c.On && !c.Monitor.Running && W6mCount(made) == 0, "the test started by itself");
                    Check(c.Button != null && c.Button.Label == "Probar micr\u00F3fono", "button: " + (c.Button == null ? "none" : c.Button.Label));
                    W6mClick(c.Button);
                    Check(c.On && c.Button.Label == "Dejar de probar", "after the click: " + c.On + " " + c.Button.Label);
                    Check(switched.Count == 1 && switched[0], "the click was not reported");
                    Check(W6mPumpUntil(delegate { return c.Monitor.State == MicState.Live; }, 3000), "the test: " + c.Monitor.State);
                    // The main window hides: the test ends and stays off.
                    w.Hide();
                    Check(W6mPumpUntil(delegate { return !c.On; }, MicCheck.GraceMs + 1000), "the test went on with the window hidden");
                    Check(!c.Monitor.Running && c.Button.Label == "Probar micr\u00F3fono", "after hiding: " + c.Monitor.Running + " " + c.Button.Label);
                    Check(switched.Count == 2 && !switched[1], "the end was not reported: " + switched.Count);
                    w.Show();
                    W6mPump(400);
                    Check(!c.On && !c.Monitor.Running && W6mCount(made) == 1, "the test came back by itself");
                    // Clicked twice: on and off.
                    W6mClick(c.Button);
                    Check(W6mPumpUntil(delegate { return c.Monitor.Running; }, 1000), "second test did not start");
                    W6mClick(c.Button);
                    Check(!c.On && !c.Monitor.Running, "the second click did not stop it");
                    // Halt (the window closes) ends it too.
                    W6mClick(c.Button);
                    c.Halt();
                    Check(!c.On && !c.Monitor.Running, "Halt did not end the test");
                }
                finally { w.Close(); }
                W6mJoin(c.Monitor);
                foreach (W6mFakeMic f in made) Check(!f.IsOpen, "a device was left open");
            });
        }

        static void W6mCheckStates()
        {
            // Clipping: Saturando and a red end.
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.Clip = true; return f; }, made, delegate
            {
                MicCheck c = new MicCheck(delegate { return ""; }, false);
                c.On = true;
                W.Window w = W6mParked(c);
                try
                {
                    Check(W6mPumpUntil(delegate { return c.Saturating; }, 3000), "no Saturando with a clipping device");
                    Check(c.NoteText == null, "a line over the bar: " + c.NoteText);
                    Check(c.BarPeak >= -0.5, "a clipping bar at " + W6F(c.BarPeak));
                }
                finally { w.Close(); }
                Check(W6mPumpUntil(delegate { return !c.Active; }, MicCheck.GraceMs + 1000), "still running after the window closed");
                Check(!c.Saturating, "Saturando stayed after the window closed");
                c.Halt();
                W6mJoin(c.Monitor);
            });
            // No device, no Saturando for a normal level.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.FailOpens = 1000; return f; }, made, delegate
            {
                MicCheck c = new MicCheck(delegate { return ""; }, false);
                c.On = true;
                W.Window w = W6mParked(c);
                try
                {
                    Check(W6mPumpUntil(delegate { return c.NoteText == "No hay micr\u00F3fono"; }, 3000), "no device says: " + c.NoteText);
                    Check(!c.Saturating, "Saturando with no device");
                }
                finally { w.Close(); c.Halt(); }
                Check(c.NoteText == null && !c.Active, "the line stayed after the meter stopped");
                W6mJoin(c.Monitor);
            });
            // A recording runs: the device is not opened, and the test carries on after it.
            made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { W6mFakeMic f = new W6mFakeMic(); f.Amplitude = 0.1f; return f; }, made, delegate
            {
                w6mRecording = true;
                MicCheck c = new MicCheck(delegate { return ""; }, false);
                c.On = true;
                W.Window w = W6mParked(c);
                bool closed = false;
                try
                {
                    Check(W6mPumpUntil(delegate { return c.NoteText == "Se est\u00E1 grabando"; }, 2000), "during a recording: " + c.NoteText);
                    Check(W6mCount(made) == 0, "opened the device during a recording");
                    w6mRecording = false;
                    Check(W6mPumpUntil(delegate { return c.Monitor.State == MicState.Live && c.NoteText == null; }, 3000), "after the recording: " + c.Monitor.State + " " + c.NoteText);
                    Check(W6mPumpUntil(delegate { return c.BarPeak > -25 && c.BarPeak < -15; }, 2000), "a -20 dBFS sine shows at " + W6F(c.BarPeak));
                    Check(!c.Saturating, "Saturando for a quiet sine");
                    // A recording starts while it measures: it lets go of the device.
                    w6mRecording = true;
                    Check(W6mPumpUntil(delegate { return !c.Monitor.Running && c.NoteText == "Se est\u00E1 grabando"; }, 2000), "a recording started and the test kept the device");
                    Check(W6mUntil(delegate { return !W6mAt(made, 0).IsOpen; }, 2000), "the device stayed open");
                    w6mRecording = false;
                    // Closed while it waits for the recording: it stays closed, even though the recording ends now.
                    w6mRecording = true;
                    W6mPump(MicCheck.IdleTickMs + 100);
                    closed = true;
                    w.Close();
                    w6mRecording = false;
                    W6mPump(MicCheck.GraceMs + MicCheck.IdleTickMs + 200);
                    Check(!c.Active && !c.Monitor.Running && W6mCount(made) == 1, "the meter came back after its window closed (" + W6mCount(made) + " opens)");
                }
                finally { if (!closed) w.Close(); c.Halt(); }
            });
        }

        static object W6mGet(object o, string field)
        {
            FieldInfo f = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "no field " + field);
            return f.GetValue(o);
        }

        static void W6mSetField(object o, string field, object v)
        {
            FieldInfo f = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "no field " + field);
            f.SetValue(o, v);
        }

        static object W6mCallPrivate(object o, string method)
        {
            MethodInfo m = o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            Check(m != null, "no method " + method);
            try { return m.Invoke(o, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        static RecordSetup W6mSetup(Settings s, bool gif)
        {
            ConstructorInfo c = typeof(RecordSetup).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                new Type[] { typeof(Settings), typeof(Rectangle), typeof(bool) }, null);
            Check(c != null, "no RecordSetup constructor");
            try { return (RecordSetup)c.Invoke(new object[] { s, new Rectangle(100, 100, 640, 360), gif }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        static void W6mRecordSetup()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                Settings s = new Settings();
                s.RecordMic = true;
                RecordSetup w = W6mSetup(s, false);
                MicCheck mc = (MicCheck)W6mGet(w, "micCheck");
                WC.TextBlock label = (WC.TextBlock)W6mGet(w, "micLabel");
                Check(mc != null && label != null, "no level row");
                Check(label.Text == "Nivel de entrada", "label: " + label.Text);
                Check(mc.On && mc.Visibility == W.Visibility.Visible && label.Visibility == W.Visibility.Visible, "the row is not on with the microphone on");
                W6mPump(200);
                Check(!mc.Monitor.Running && W6mCount(made) == 0, "the meter opened the device before the window showed");
                // The switch off hides the row and switches it off.
                W6mSetField(w, "mic", false);
                W6mCallPrivate(w, "MicLevel");
                Check(!mc.On && mc.Visibility == W.Visibility.Collapsed && label.Visibility == W.Visibility.Collapsed, "the row stays with the microphone off");
                W6mSetField(w, "mic", true);
                W6mCallPrivate(w, "MicLevel");
                Check(mc.On && mc.Visibility == W.Visibility.Visible, "the row did not come back");
                // A rebuild (another quality, the theme) keeps the same meter.
                W6mCallPrivate(w, "Build");
                Check(ReferenceEquals(W6mGet(w, "micCheck"), mc) && mc.On, "a rebuild made another meter");
                Check(mc.Parent != null, "the meter is not in the page after a rebuild");
                W6mCallPrivate(w, "Accept");
                Check(s.RecordMic, "Accept changed the microphone setting");
                Check(!mc.Monitor.Running, "running after the window closed");
                // Microphone off from the start: the row is there but hidden.
                Settings off = new Settings();
                off.RecordMic = false;
                RecordSetup w2 = W6mSetup(off, false);
                MicCheck mc2 = (MicCheck)W6mGet(w2, "micCheck");
                Check(mc2 != null && !mc2.On && mc2.Visibility == W.Visibility.Collapsed, "microphone off shows the level");
                w2.Close();
                // A GIF has no sound: no meter.
                RecordSetup g = W6mSetup(new Settings(), true);
                Check(W6mGet(g, "micCheck") == null, "a GIF take has a microphone meter");
                g.Close();
                Check(W6mCount(made) == 0, "a device was opened by a window never shown");
            });
        }

        static void W6mFind(object o, List<MicCheck> acc)
        {
            MicCheck c = o as MicCheck;
            if (c != null) acc.Add(c);
            W.DependencyObject d = o as W.DependencyObject;
            if (d == null) return;
            foreach (object child in W.LogicalTreeHelper.GetChildren(d)) W6mFind(child, acc);
        }

        static void W6mRecordPage()
        {
            List<W6mFakeMic> made = new List<W6mFakeMic>();
            bool dark = Ds.Dark;
            try
            {
                W6mRecordPageIn(made);
            }
            finally { Ds.Apply(dark ? 2 : 1); }
        }

        static void W6mRecordPageIn(List<W6mFakeMic> made)
        {
            W6mWithFakes(delegate { return new W6mFakeMic(); }, made, delegate
            {
                Ds.Apply(2);
                Settings s = new Settings();
                ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
                HomeWindow hw = new HomeWindow(owner, s);
                MethodInfo setPage = typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance);
                setPage.Invoke(hw, new object[] { "record", false });
                List<MicCheck> found = new List<MicCheck>();
                W6mFind(hw.Content, found);
                Check(found.Count == 1, found.Count + " microphone tests on the Grabacion page");
                MicCheck c = found[0];
                Check(c.Button != null && c.Button.Label == "Probar micr\u00F3fono" && !c.On && !c.Active, "the test is not a button that waits");
                // A rebuild of the page puts the same one back.
                W6mCallPrivate(hw, "Rebuild");
                found.Clear();
                W6mFind(hw.Content, found);
                Check(found.Count == 1 && ReferenceEquals(found[0], c), "a rebuild made another test (" + found.Count + ")");
                // Another page: not there.
                setPage.Invoke(hw, new object[] { "general", false });
                found.Clear();
                W6mFind(hw.Content, found);
                Check(found.Count == 0, "the test shows on another page");
                // Back to Grabacion, in light mode.
                Ds.Apply(1);
                setPage.Invoke(hw, new object[] { "record", false });
                found.Clear();
                W6mFind(hw.Content, found);
                Check(found.Count == 1 && ReferenceEquals(found[0], c), "back on the page: " + found.Count);
                W6mPump(200);
                Check(W6mCount(made) == 0 && !c.Monitor.Running, "a window never shown opened the device");
            });
        }

        // With STACKSHOT_UI_SHOTS set to a folder, the meter in each state, the options before recording and the Grabacion
        // page are saved there as PNG to be looked at (rendered off screen; nothing is captured).
        static void W6mShots()
        {
            string dir = Environment.GetEnvironmentVariable("STACKSHOT_UI_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            bool dark = Ds.Dark;
            try
            {
                foreach (int appearance in new int[] { 2, 1 })
                {
                    Ds.Apply(appearance);
                    string mode = appearance == 2 ? "dark" : "light";
                    W6mShotMeter(dir, "meter-live-" + mode, delegate { W6mFakeMic f = new W6mFakeMic(); f.Amplitude = 0.3f; return f; }, delegate(MicCheck c) { return c.BarPeak > -12; });
                    W6mShotMeter(dir, "meter-loud-" + mode, delegate { W6mFakeMic f = new W6mFakeMic(); f.Amplitude = 0.8f; return f; }, delegate(MicCheck c) { return c.BarPeak > -3; });
                    W6mShotMeter(dir, "meter-clip-" + mode, delegate { W6mFakeMic f = new W6mFakeMic(); f.Clip = true; return f; }, delegate(MicCheck c) { return c.Saturating; });
                    W6mShotMeter(dir, "meter-nodevice-" + mode, delegate { W6mFakeMic f = new W6mFakeMic(); f.FailOpens = 1000; return f; }, delegate(MicCheck c) { return c.NoteText != null; });
                    Settings s = new Settings();
                    s.RecordMic = true;
                    RecordSetup rs = W6mSetup(s, false);
                    W6mSave((W.FrameworkElement)rs.Content, 460, Path.Combine(dir, "setup-" + mode + ".png"));
                    rs.Close();
                    ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
                    HomeWindow hw = new HomeWindow(owner, new Settings());
                    typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, new object[] { "record", false });
                    W6mSave((W.FrameworkElement)hw.Content, 980, Path.Combine(dir, "record-" + mode + ".png"));
                }
            }
            finally { Ds.Apply(dark ? 2 : 1); }
            // The player's bar with a piece marked, painted into a bitmap (the window is never shown).
            if (File.Exists(ffmpeg))
            {
                string clip = W6mClip("barra.mkv", "320x180", 30, 3, null, true);
                Settings st = new Settings();
                PlayerWindow p = PwWindow(clip, new PlFakeSound(), st);
                try
                {
                    IntPtr h = p.Handle;
                    foreach (int width in new int[] { 1000, 700 })
                    {
                        p.ClientSize = new Size(width, 420);
                        PpSet(p, "markIn", 0.5);
                        PpSet(p, "markOut", 2.0);
                        using (Bitmap b = new Bitmap(p.Width, p.Height))
                        {
                            p.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
                            b.Save(Path.Combine(dir, "player-bar-" + width + ".png"), ImageFormat.Png);
                        }
                    }
                }
                finally { p.Dispose(); }
            }
            Add("saved in " + dir);
        }

        static void W6mShotMeter(string dir, string name, Func<W6mFakeMic> make, Func<MicCheck, bool> ready)
        {
            foreach (bool setup in new bool[] { false, true })
            {
                List<W6mFakeMic> made = new List<W6mFakeMic>();
                W6mWithFakes(make, made, delegate
                {
                    // On the page (with its button) and in the options before recording (at the right, 240 wide).
                    MicCheck c = new MicCheck(delegate { return ""; }, !setup);
                    if (setup) { c.Width = 240; c.AlignRight = true; c.HorizontalAlignment = W.HorizontalAlignment.Right; }
                    WC.Border b = new WC.Border { Background = Ds.Brush(Ds.Brushes.Window), Padding = new W.Thickness(16), Width = 300, Child = c };
                    W.Window w = W6mParked(b);
                    try
                    {
                        if (setup) c.On = true;
                        else W6mClick(c.Button);
                        W6mPumpUntil(delegate { return ready(c); }, 3000);
                        W6mPump(300);
                        W6mSave(b, 300, Path.Combine(dir, name + (setup ? "-setup" : "") + ".png"));
                    }
                    finally { w.Close(); c.Halt(); }
                    W6mJoin(c.Monitor);
                });
            }
        }

        static void W6mSave(W.FrameworkElement e, int width, string file)
        {
            e.Measure(new W.Size(width, double.PositiveInfinity));
            int h = Math.Max(1, (int)Math.Ceiling(e.DesiredSize.Height));
            e.Arrange(new W.Rect(0, 0, width, h));
            e.UpdateLayout();
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width * 2, h * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
            System.Windows.Media.DrawingVisual dv = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Ds.Brush(Ds.Brushes.Window), null, new W.Rect(0, 0, width, h));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(e), null, new W.Rect(0, 0, width, h));
            }
            rtb.Render(dv);
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }

        // ---------------------------------------------------------------- the GIF of a piece

        static void W6mGifArgs()
        {
            Check(PieceGif.OutputSize(new Size(1920, 1080)) == new Size(960, 540), "1080p: " + PieceGif.OutputSize(new Size(1920, 1080)));
            Check(PieceGif.OutputSize(new Size(320, 180)) == new Size(320, 180), "small video grew or shrank");
            Check(PieceGif.OutputSize(new Size(1080, 1920)) == new Size(960, 1706), "portrait: " + PieceGif.OutputSize(new Size(1080, 1920)));
            Check(PieceGif.OutputSize(new Size(321, 181)) == new Size(320, 180), "odd: " + PieceGif.OutputSize(new Size(321, 181)));
            Check(PieceGif.OutputSize(new Size(3, 2000)) == new Size(2, 1334), "3x2000: " + PieceGif.OutputSize(new Size(3, 2000)));
            Check(PieceGif.OutputSize(new Size(4000, 3)) == new Size(960, 2), "4000x3: " + PieceGif.OutputSize(new Size(4000, 3)));
            Check(PieceGif.OutputSize(Size.Empty) == new Size(2, 2), "empty");
            // The editor's own check of a GIF's size agrees (ExpectFor, scale=w:-2).
            ExportSpec es = new ExportSpec();
            es.ToGif = true;
            es.Crop = new Rectangle(0, 0, 1080, 1920);
            ShareExpect ee = Editor.ExpectFor(es, 10, 30, true);
            Check(ee.Width == 960 && ee.Height == 1706, "the editor would make " + ee.Width + "x" + ee.Height);
            Check(PieceGif.Fps(15, 30) == 15 && PieceGif.Fps(25, 10) == 10 && PieceGif.Fps(3, 30) == 5 && PieceGif.Fps(50, 60) == 30, "rates");
            Check(PieceGif.Fps(15, 0) == 15 && Math.Abs(PieceGif.Fps(30, 23.976) - 23.976) < 1e-9, "rate with an unknown or slow source");
            string src = "C:\\v\\pel\u00EDcula.mkv", pal = "C:\\t\\p.png", gif = "C:\\v\\out.gif";
            Size sz = new Size(320, 180);
            string p1 = PieceGif.PaletteArgs(src, 1, 3, sz, 10, pal);
            string p0 = PieceGif.PlainPaletteArgs(src, 1, 3, sz, 10, pal);
            string p2 = PieceGif.GifArgs(src, 1, 3, sz, 10, pal, gif);
            foreach (string a in new string[] { p1, p0, p2 })
            {
                Check(a.Contains("-ss 1.000 -to 3.000 " + Recorder.SafeInput + "-i \"" + src + "\""), "input: " + a);
                Check(a.Contains("[0:v]fps=10,scale=320:180:flags=lanczos,setsar=1"), "frames: " + a);
                Check(a.Contains("-progress pipe:1") && a.Contains("-loglevel error"), "no progress: " + a);
                Check(a.IndexOf(Recorder.SafeInput, StringComparison.Ordinal) == a.LastIndexOf(Recorder.SafeInput, StringComparison.Ordinal), "the whitelist applies twice: " + a);
            }
            Check(p1.Contains(PieceGif.PaletteGen) && p1.Contains("-update 1 \"" + pal + "\"") && p1.Contains("-f null NUL") && p1.Contains("concat=n=2"), "palette run: " + p1);
            Check(p0.Contains(PieceGif.PaletteGen) && p0.Contains("-frames:v 1 -update 1"), "plain palette run: " + p0);
            Check(p2.Contains(" -f image2 -pattern_type none -i \"" + pal + "\"") && p2.Contains("[x][1:v]" + PieceGif.PaletteUse + "[o]") && p2.Contains("-loop 0 \"" + gif + "\""), "GIF run: " + p2);
            Check(!p2.Contains("-c:a") && !p2.Contains("[0:a"), "the GIF run maps sound: " + p2);
            // The same palette work as the editor's GIF export.
            ExportSpec s = new ExportSpec();
            s.Source = src;
            s.Output = gif;
            s.ToGif = true;
            s.Crop = new Rectangle(0, 0, 320, 180);
            s.Frame = s.Crop.Size;
            s.Inner = s.Crop;
            string editor = VideoExport.Arguments(s);
            Check(editor.Contains(PieceGif.PaletteGen + "[p]") && editor.Contains(PieceGif.PaletteUse + "[o]"), "the editor's palette changed: " + editor);
            // A GIF keeps its own timing; the start needs no seek.
            string g = PieceGif.GifArgs(src, 0, 2, sz, 0, pal, gif);
            Check(!g.Contains("fps=") && !g.Contains("-ss "), "a GIF from 0: " + g);
            // An MPEG-TS seeks 3 s early and drops them.
            string ts = PieceGif.GifArgs("C:\\v\\cam.ts", 10, 13, sz, 15, pal, gif);
            Check(ts.Contains("-ss 7.000 -to 13.000 ") && ts.Contains("trim=start=3.000:end=6.000,setpts=PTS-STARTPTS,fps=15,"), "MPEG-TS: " + ts);
        }

        static string W6mClip(string name, string size, int rate, double secs, string extra, bool audio)
        {
            string path = Path.Combine(WorkDir("w6m"), name);
            string d = secs.ToString("0.###", CultureInfo.InvariantCulture);
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=" + size + ":r=" + rate + ":d=" + d + "\"" +
                 (audio ? " -f lavfi -i \"sine=f=440:d=" + d + "\"" : "") + " " + (extra ?? "") +
                 " -c:v libx264 -preset ultrafast -pix_fmt yuv420p" + (audio ? " -c:a aac -shortest" : " -an") + " " + Q(path), 120000);
            return path;
        }

        static void W6mGifPalette()
        {
            NeedFfmpeg();
            string clip = W6mClip("paleta.mkv", "320x180", 30, 3, null, false);
            string dir = Path.GetDirectoryName(clip);
            string a = Path.Combine(dir, "a.png"), b = Path.Combine(dir, "b.png");
            string err;
            int reports = 0;
            int c1 = VideoExport.Run(ffmpeg, PieceGif.PaletteArgs(clip, 0.5, 2.5, new Size(320, 180), 15, a), 60000,
                                     delegate(string line) { if (line.StartsWith("out_time=0", StringComparison.Ordinal)) Interlocked.Increment(ref reports); }, out err);
            Check(c1 == 0, "palette with stand-in: " + Tail(err));
            Check(reports > 0, "the palette run reported no progress");
            int c2 = VideoExport.Run(ffmpeg, PieceGif.PlainPaletteArgs(clip, 0.5, 2.5, new Size(320, 180), 15, b), 60000, delegate(string line) { }, out err);
            Check(c2 == 0, "plain palette: " + Tail(err));
            byte[] ba = File.ReadAllBytes(a), bb = File.ReadAllBytes(b);
            bool same = ba.Length == bb.Length;
            for (int i = 0; same && i < ba.Length; i++) same = ba[i] == bb[i];
            Check(same, "the palettes differ (" + ba.Length + " and " + bb.Length + " bytes)");
            Check(PieceGif.PaletteOk(a) && PieceGif.PaletteOk(b), "a real palette is refused");
            // The stand-in itself (all transparent), a picture of another size and no file are refused.
            string clear = Path.Combine(dir, "clear.png"), small = Path.Combine(dir, "small.png");
            using (Bitmap x = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) x.Save(clear, ImageFormat.Png);
            using (Bitmap y = new Bitmap(8, 8)) { using (Graphics gr = Graphics.FromImage(y)) gr.Clear(Color.Red); y.Save(small, ImageFormat.Png); }
            Check(!PieceGif.PaletteOk(clear), "the transparent stand-in passes as a palette");
            Check(!PieceGif.PaletteOk(small), "an 8x8 picture passes as a palette");
            Check(!PieceGif.PaletteOk(Path.Combine(dir, "no.png")), "a missing file passes as a palette");
            File.WriteAllBytes(Path.Combine(dir, "junk.png"), new byte[] { 1, 2, 3 });
            Check(!PieceGif.PaletteOk(Path.Combine(dir, "junk.png")), "junk passes as a palette");
            Add("palette " + ba.Length + " bytes, identical");
        }

        // What a GIF file holds, read here (not by FFmpeg): size, frames, total delay and whether it loops.
        sealed class W6mGifInfo
        {
            public int Width, Height, Frames, DelayCs;
            public bool Loops;
        }

        static int W6mSkipBlocks(byte[] b, int p)
        {
            while (p < b.Length)
            {
                int n = b[p++];
                if (n == 0) break;
                p += n;
            }
            return p;
        }

        static W6mGifInfo W6mReadGif(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            Check(b.Length > 13, "the GIF is " + b.Length + " bytes");
            string sig = Encoding.ASCII.GetString(b, 0, 6);
            Check(sig == "GIF89a" || sig == "GIF87a", "not a GIF: " + sig);
            W6mGifInfo g = new W6mGifInfo();
            g.Width = b[6] | (b[7] << 8);
            g.Height = b[8] | (b[9] << 8);
            int p = 13;
            if ((b[10] & 0x80) != 0) p += 3 * (1 << ((b[10] & 7) + 1));
            bool trailer = false;
            while (p < b.Length)
            {
                byte k = b[p++];
                if (k == 0x3B) { trailer = true; break; }
                if (k == 0x21)
                {
                    byte label = b[p++];
                    if (label == 0xF9 && b[p] == 4) g.DelayCs += b[p + 2] | (b[p + 3] << 8);
                    if (label == 0xFF && b[p] == 11 && Encoding.ASCII.GetString(b, p + 1, 11) == "NETSCAPE2.0") g.Loops = true;
                    p = W6mSkipBlocks(b, p);
                }
                else if (k == 0x2C)
                {
                    g.Frames++;
                    byte packed = b[p + 8];
                    p += 9;
                    if ((packed & 0x80) != 0) p += 3 * (1 << ((packed & 7) + 1));
                    p++;
                    p = W6mSkipBlocks(b, p);
                }
                else throw new TestFailed("bad GIF block 0x" + k.ToString("X2") + " at " + (p - 1));
            }
            Check(trailer, "the GIF has no trailer");
            return g;
        }

        // One frame as rgb24 at w x h (the GIF itself, or the video at t as FFmpeg turns and decodes it).
        static byte[] W6mFrame(string file, double t, int w, int h, string extra)
        {
            string raw = Path.Combine(Path.GetDirectoryName(file), Guid.NewGuid().ToString("N") + ".rgb");
            FfOk("-hide_banner -loglevel error -y " + (t > 0 ? "-ss " + t.ToString("0.000", CultureInfo.InvariantCulture) + " " : "") + "-i " + Q(file) +
                 " -frames:v 1 -vf \"scale=" + w + ":" + h + ":flags=lanczos" + (extra ?? "") + "\" -f rawvideo -pix_fmt rgb24 " + Q(raw), 60000);
            byte[] b = File.ReadAllBytes(raw);
            File.Delete(raw);
            Check(b.Length == w * h * 3, "frame of " + Path.GetFileName(file) + ": " + b.Length + " bytes");
            return b;
        }

        static double W6mDiff(byte[] a, byte[] b)
        {
            double sum = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) sum += Math.Abs(a[i] - b[i]);
            return sum / Math.Max(1, n);
        }

        static bool W6mBusy(PlayerWindow w)
        {
            return typeof(PlayerWindow).GetField("exportThread", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w) != null;
        }

        static void W6mExportGif(PlayerWindow w, double a, double b, int waitMs)
        {
            PpSet(w, "markIn", a);
            PpSet(w, "markOut", b);
            PpCall(w, "ExportGif");
            Stopwatch sw = Stopwatch.StartNew();
            while (W6mBusy(w) && sw.ElapsedMilliseconds < waitMs) { Application.DoEvents(); Thread.Sleep(10); }
            Check(!W6mBusy(w), "the GIF did not end in " + waitMs / 1000 + " s");
            Application.DoEvents();
        }

        static string W6mPalettesLeft()
        {
            return string.Join(", ", Directory.GetFiles(Path.GetTempPath(), "stackshot-palette-*"));
        }

        static void W6mGifPiece()
        {
            NeedFfmpeg();
            string clip = W6mClip("clip.mkv", "320x180", 30, 4, null, true);
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            st.GifFps = 10;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                Stopwatch sw = Stopwatch.StartNew();
                W6mExportGif(w, 1.0, 3.0, 90000);
                long ms = sw.ElapsedMilliseconds;
                string gif = Path.Combine(dir, "clip (tramo).gif");
                Check(File.Exists(gif), "no GIF | note " + w.ExportNote + " | files " + string.Join(", ", Directory.GetFiles(dir)));
                Check(w.ExportNote == "ok", "the check said: " + w.ExportNote);
                Check((string)PpField(w, "flash") == "GIF listo", "flash: " + PpField(w, "flash"));
                W6mGifInfo g = W6mReadGif(gif);
                Check(g.Width == 320 && g.Height == 180, "size " + g.Width + "x" + g.Height);
                Check(Math.Abs(g.Frames - 20) <= 1, g.Frames + " frames for 2 s at 10 fps");
                Check(Math.Abs(g.DelayCs - 200) <= 10, "lasts " + g.DelayCs + " cs, the piece is 200");
                Check(g.Loops, "the GIF does not loop");
                // It starts at the mark, not at the beginning of the video.
                byte[] first = W6mFrame(gif, 0, 320, 180, null);
                double atMark = W6mDiff(first, W6mFrame(clip, 1.0, 320, 180, null)), atZero = W6mDiff(first, W6mFrame(clip, 0, 320, 180, null));
                Check(atMark < 8 && atMark < atZero / 2, "first frame vs the mark " + atMark.ToString("0.0", CultureInfo.InvariantCulture) + ", vs 0 s " + atZero.ToString("0.0", CultureInfo.InvariantCulture));
                // Nothing else left: the video, the GIF, no palette.
                Check(Directory.GetFiles(dir).Length == 2, "files: " + string.Join(", ", Directory.GetFiles(dir)));
                Check(W6mPalettesLeft().Length == 0, "palettes left: " + W6mPalettesLeft());
                Check(PpField(w, "exportText") == null && PpField(w, "exportAbort") == null, "the progress stayed");
                // A second GIF of the same piece gets its own name.
                W6mExportGif(w, 1.0, 3.0, 90000);
                Check(File.Exists(Path.Combine(dir, "clip (tramo) (2).gif")), "the second GIF: " + string.Join(", ", Directory.GetFiles(dir)));
                Add(g.Frames + " frames, " + g.DelayCs + " cs, " + new FileInfo(gif).Length / 1024 + " KB in " + ms + " ms, first frame off by " + atMark.ToString("0.0", CultureInfo.InvariantCulture));
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void W6mGifRotated()
        {
            NeedFfmpeg();
            string src = W6mClip("upright.mkv", "320x180", 30, 3, null, false);
            string rot = Path.Combine(WorkDir("w6m-rot"), "movil.mov");
            string err;
            if (Ff("-hide_banner -loglevel error -y -display_rotation:v:0 90 -i " + Q(src) + " -c copy " + Q(rot), out err, 30000) != 0)
                Skip("this FFmpeg cannot set a display rotation: " + Tail(err));
            Settings st = new Settings();
            st.GifFps = 15;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(rot, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                Check(w.Media.Width == 180 && w.Media.Height == 320, "probe " + w.Media.Width + "x" + w.Media.Height);
                W6mExportGif(w, 0.5, 1.5, 90000);
                string gif = Path.Combine(Path.GetDirectoryName(rot), "movil (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "no GIF | " + w.ExportNote);
                W6mGifInfo g = W6mReadGif(gif);
                Check(g.Width == 180 && g.Height == 320, "a portrait video made a " + g.Width + "x" + g.Height + " GIF");
                Check(Math.Abs(g.Frames - 15) <= 1, g.Frames + " frames for 1 s at 15 fps");
                // Turned the way players turn it, not the other way.
                byte[] first = W6mFrame(gif, 0, 180, 320, null);
                double right = W6mDiff(first, W6mFrame(rot, 0.5, 180, 320, null));
                double wrong = W6mDiff(first, W6mFrame(rot, 0.5, 180, 320, ",hflip,vflip"));
                Check(right < 8 && wrong > right + 10, "against the turned picture " + right.ToString("0.0", CultureInfo.InvariantCulture) + ", against it upside down " + wrong.ToString("0.0", CultureInfo.InvariantCulture));
                Add("portrait GIF, off by " + right.ToString("0.0", CultureInfo.InvariantCulture) + " (upside down " + wrong.ToString("0.0", CultureInfo.InvariantCulture) + ")");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void W6mGifAnamorphic()
        {
            NeedFfmpeg();
            string clip = W6mClip("hdv.mov", "480x360", 25, 2, "-vf setsar=4/3", false);
            Settings st = new Settings();
            st.GifFps = 10;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                Check(w.Media.Display == new Size(640, 360), "probe shows at " + w.Media.Display);
                W6mExportGif(w, 0, 1.0, 90000);
                string gif = Path.Combine(Path.GetDirectoryName(clip), "hdv (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "no GIF | " + w.ExportNote);
                W6mGifInfo g = W6mReadGif(gif);
                Check(g.Width == 640 && g.Height == 360, "480x360 at SAR 4:3 made a " + g.Width + "x" + g.Height + " GIF, it shows at 640x360");
                // The circle of testsrc2 stays round: compared with the picture as players show it.
                double off = W6mDiff(W6mFrame(gif, 0, 640, 360, null), W6mFrame(clip, 0, 640, 360, null));
                Check(off < 8, "the GIF differs from the picture by " + off.ToString("0.0", CultureInfo.InvariantCulture));
                Add("640x360, off by " + off.ToString("0.0", CultureInfo.InvariantCulture));
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void W6mGifCancel()
        {
            NeedFfmpeg();
            string clip = W6mClip("larga.mkv", "1280x720", 30, 26, null, false);
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            st.GifFps = 25;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                PpSet(w, "markIn", 0.0);
                PpSet(w, "markOut", 25.0);
                PpCall(w, "ExportGif");
                Check(W6mBusy(w), "the GIF did not start");
                string text = (string)PpField(w, "exportText");
                Check(text != null && text.StartsWith("Creando el GIF\u2026 ", StringComparison.Ordinal) && text.EndsWith("Esc cancela", StringComparison.Ordinal), "progress: " + text);
                // Progress moves during the palette run (half of the bar).
                string moved = null;
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 20000 && W6mBusy(w))
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                    string t = (string)PpField(w, "exportText");
                    if (t != null && !t.Contains(" 0 %") && t.Contains("%")) { moved = t; break; }
                }
                Check(W6mBusy(w), "the GIF ended before it could be cancelled (" + sw.ElapsedMilliseconds + " ms)");
                Check(moved != null, "the progress never moved");
                // Esc cancels.
                sw.Restart();
                PwKey(w, Keys.Escape);
                long took = sw.ElapsedMilliseconds;
                Check(!W6mBusy(w) && PpField(w, "exportText") == null, "Esc did not cancel");
                Check((double)PpField(w, "markIn") == 0.0 && (double)PpField(w, "markOut") == 25.0, "Esc cancelled and also cleared the piece");
                W6mPump(1500);
                Check(Directory.GetFiles(dir, "*.gif").Length == 0, "files left: " + string.Join(", ", Directory.GetFiles(dir)));
                Check(W6mPalettesLeft().Length == 0, "palettes left: " + W6mPalettesLeft());
                Check(w.ExportNote != "ok", "a cancelled GIF reported ok");
                // The window still works: a short GIF now.
                W6mExportGif(w, 2.0, 2.4, 90000);
                Check(File.Exists(Path.Combine(dir, "larga (tramo).gif")) && w.ExportNote == "ok", "after cancelling, the next GIF: " + w.ExportNote);
                Add("cancelled at '" + moved + "' in " + took + " ms");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        static void W6mGifLong()
        {
            NeedFfmpeg();
            string clip = W6mClip("larguisima.mkv", "160x90", 5, 40, null, false);
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            st.GifFps = 5;
            PlayerWindow.Quiet = true;
            List<string> asked = new List<string>();
            bool answer = false;
            PlayerWindow.AskLongGif = delegate(string q) { asked.Add(q); return answer; };
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                // 33 s: asked first; "no" leaves nothing.
                PpSet(w, "markIn", 2.0);
                PpSet(w, "markOut", 35.0);
                PpCall(w, "ExportGif");
                Check(asked.Count == 1, "asked " + asked.Count + " times");
                Check(asked[0].StartsWith("El tramo dura 0:33: un GIF tan largo pesa mucho", StringComparison.Ordinal) && asked[0].EndsWith("\u00BFCrear el GIF igualmente?", StringComparison.Ordinal), "question: " + asked[0]);
                Check(!W6mBusy(w) && PpField(w, "exportText") == null, "a GIF started after no");
                Check(Directory.GetFiles(dir, "*.gif").Length == 0, "a file was left after no");
                // The name was not kept reserved.
                Check(ShotStack.Unique(Path.Combine(dir, "larguisima (tramo).gif")) == Path.Combine(dir, "larguisima (tramo).gif"), "the name stayed reserved after no");
                // 30 s exactly: not asked.
                PpSet(w, "markOut", 32.0);
                PpCall(w, "ExportGif");
                Check(asked.Count == 1 && W6mBusy(w), "a 30 s piece was asked about or did not start");
                PwKey(w, Keys.Escape);
                Check(!W6mBusy(w), "Esc did not cancel");
                W6mPump(500);
                // "Yes": it is made, all 33 s of it.
                answer = true;
                W6mExportGif(w, 2.0, 35.0, 120000);
                Check(asked.Count == 2, "asked " + asked.Count + " times");
                string gif = Path.Combine(dir, "larguisima (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "after yes: " + w.ExportNote);
                W6mGifInfo g = W6mReadGif(gif);
                Check(Math.Abs(g.Frames - 165) <= 2 && Math.Abs(g.DelayCs - 3300) <= 30, g.Frames + " frames, " + g.DelayCs + " cs for 33 s at 5 fps");
                Add(g.Frames + " frames after yes");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; PlayerWindow.AskLongGif = null; }
        }

        static List<string[]> W6mButtons(PlayerWindow w)
        {
            using (Bitmap b = new Bitmap(1300, 760))
            using (Graphics g = Graphics.FromImage(b))
                typeof(PlayerWindow).GetMethod("LayoutBar", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { g });
            List<string[]> r = new List<string[]>();
            System.Collections.IEnumerable list = (System.Collections.IEnumerable)PpField(w, "btns");
            foreach (object o in list)
            {
                Type t = o.GetType();
                r.Add(new string[] { (string)t.GetField("Id").GetValue(o), (string)t.GetField("Tip").GetValue(o), (string)t.GetField("Text").GetValue(o) });
            }
            return r;
        }

        static string[] W6mButton(List<string[]> all, string id)
        {
            foreach (string[] b in all) if (b[0] == id) return b;
            return null;
        }

        static void W6mGifUi()
        {
            NeedFfmpeg();
            string clip = W6mClip("ui.mkv", "320x180", 30, 3, null, true);
            Settings st = new Settings();
            st.GifFps = 20;
            st.ExportQuality = 1;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                w.ClientSize = new Size(1600, 900);
                // No piece: G says how, nothing starts, no button, the menu item is greyed out.
                Check(PwKey(w, Keys.G), "G was not taken");
                Check(!W6mBusy(w), "G without a piece started a GIF");
                Check((string)PpField(w, "flash") == "Marca el tramo con E y S para crear el GIF", "flash: " + PpField(w, "flash"));
                Check(W6mButton(W6mButtons(w), "gif") == null, "a GIF button without a piece");
                using (ToolStripMenuItem it = (ToolStripMenuItem)PpCall(w, "GifMenuItem"))
                {
                    Check(it.Text == "Exportar GIF del tramo" && it.ShortcutKeyDisplayString == "G" && (string)it.Tag == "gif", "menu item: " + it.Text + " / " + it.ShortcutKeyDisplayString + " / " + it.Tag);
                    Check(!it.Enabled, "the menu item works without a piece");
                }
                // With a piece: next to Exportar tramo, with its rate and key.
                PpSet(w, "markIn", 0.5);
                PpSet(w, "markOut", 1.5);
                List<string[]> all = W6mButtons(w);
                string[] gb = W6mButton(all, "gif"), eb = W6mButton(all, "export");
                Check(eb != null && eb[2] == "Exportar tramo", "no Exportar tramo button");
                Check(gb != null && gb[2] == "GIF", "no GIF button: " + all.Count + " buttons");
                Check(gb[1] == "Crear un GIF con el tramo marcado (E y S) \u00B7 20 fps  (G)", "tip: " + gb[1]);
                Check(all.IndexOf(gb) == all.IndexOf(eb) + 1, "the GIF button is not right beside Exportar tramo");
                using (ToolStripMenuItem it = (ToolStripMenuItem)PpCall(w, "GifMenuItem")) Check(it.Enabled, "the menu item is greyed out with a piece");
                // The rate follows the setting of the Grabacion page.
                st.GifFps = 30;
                gb = W6mButton(W6mButtons(w), "gif");
                Check(gb != null && gb[1].Contains("\u00B7 30 fps"), "30 fps tip: " + (gb == null ? "none" : gb[1]));
                st.GifFps = 20;
                // While a GIF is made: no buttons to start another, and G does nothing.
                PpCall(w, "ExportGif");
                Check(W6mBusy(w), "the GIF did not start");
                all = W6mButtons(w);
                Check(W6mButton(all, "gif") == null && W6mButton(all, "export") == null, "export buttons while a GIF is made");
                PwKey(w, Keys.G);
                Stopwatch sw = Stopwatch.StartNew();
                while (W6mBusy(w) && sw.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(10); }
                Check(w.ExportNote == "ok", "the GIF: " + w.ExportNote);
                Check(Directory.GetFiles(Path.GetDirectoryName(clip), "*.gif").Length == 1, "G during a GIF made another one");
                // The bar's button does the same as G.
                typeof(PlayerWindow).GetMethod("Act", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { "gif" });
                Check(W6mBusy(w), "the bar's GIF button did not start one");
            }
            finally
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (W6mBusy(w) && sw.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(10); }
                w.Dispose();
                PlayerWindow.Quiet = false;
            }
            Check(Directory.GetFiles(Path.GetDirectoryName(clip), "*.gif").Length == 2, "the bar's GIF button did not make one");
        }

        static void W6mGifFromGif()
        {
            NeedFfmpeg();
            string dir = WorkDir("w6m-gif");
            string src = Path.Combine(dir, "anim.gif");
            FfOk("-hide_banner -loglevel error -y -f lavfi -i \"testsrc2=s=160x90:r=10:d=4\" -vf \"split[a][b];[a]palettegen[p];[b][p]paletteuse\" " + Q(src), 60000);
            Settings st = new Settings();
            st.GifFps = 25;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(src, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                Check(w.Media.CanPlay, "the GIF does not play");
                W6mExportGif(w, 1.0, 3.0, 90000);
                string gif = Path.Combine(dir, "anim (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "no GIF | " + w.ExportNote);
                W6mGifInfo g = W6mReadGif(gif);
                Check(g.Width == 160 && g.Height == 90, "size " + g.Width + "x" + g.Height);
                Check(Math.Abs(g.Frames - 20) <= 1, g.Frames + " frames: a 10 fps GIF must keep its frames, not go to 25 fps");
                Check(Math.Abs(g.DelayCs - 200) <= 10, "lasts " + g.DelayCs + " cs");
                Add(g.Frames + " frames, " + g.DelayCs + " cs");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // A video in a folder with accents, spaces and %, and the palette in a temporary folder with % too.
        static void W6mGifPaths()
        {
            NeedFfmpeg();
            string made = W6mClip("origen.mkv", "320x180", 30, 2, null, false);
            string dir = Path.Combine(WorkDir("w6m-paths"), "carpeta 100% \u00F1 %d");
            Directory.CreateDirectory(dir);
            string clip = Path.Combine(dir, "v\u00EDdeo %d 50%.mkv");
            File.Copy(made, clip);
            string temp = Path.Combine(WorkDir("w6m-temp"), "tmp %d 20%");
            Directory.CreateDirectory(temp);
            string oldTmp = Environment.GetEnvironmentVariable("TMP"), oldTemp = Environment.GetEnvironmentVariable("TEMP");
            Settings st = new Settings();
            st.GifFps = 10;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                Check(w.Media.CanPlay, "the video does not open");
                Environment.SetEnvironmentVariable("TMP", temp);
                Environment.SetEnvironmentVariable("TEMP", temp);
                Check(Path.GetTempPath().TrimEnd('\\') == temp, "the temporary folder did not change: " + Path.GetTempPath());
                W6mExportGif(w, 0.5, 1.5, 90000);
                string gif = Path.Combine(dir, "v\u00EDdeo %d 50% (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "no GIF | " + w.ExportNote + " | " + string.Join(", ", Directory.GetFiles(dir)));
                Check(Math.Abs(W6mReadGif(gif).Frames - 10) <= 1, "frames " + W6mReadGif(gif).Frames);
                Check(Directory.GetFiles(temp).Length == 0, "left in the temporary folder: " + string.Join(", ", Directory.GetFiles(temp)));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMP", oldTmp);
                Environment.SetEnvironmentVariable("TEMP", oldTemp);
                w.Dispose();
                PlayerWindow.Quiet = false;
            }
        }

        static void W6mGifTiny()
        {
            NeedFfmpeg();
            string clip = W6mClip("corta.mkv", "320x180", 30, 2, null, false);
            Settings st = new Settings();
            st.GifFps = 10;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                // Two frames of video (the shortest piece the marks allow).
                W6mExportGif(w, 1.0, 1.0 + 2 / 30.0, 60000);
                string gif = Path.Combine(Path.GetDirectoryName(clip), "corta (tramo).gif");
                Check(File.Exists(gif) && w.ExportNote == "ok", "no GIF | " + w.ExportNote);
                W6mGifInfo g = W6mReadGif(gif);
                Check(g.Frames >= 1 && g.Frames <= 2, g.Frames + " frames for 67 ms");
                Add(g.Frames + " frame(s), " + g.DelayCs + " cs");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // ---------------------------------------------------------------- review (R4)

        // A GIF of a video kept in a dated subfolder (2026-10, 2026\10) is written next to it (ShotStack.ExportFolder) and the
        // stack shows it as kept: the flash must say so, as the MP4 export does (SaveFolders.Inside). The old check compared
        // with the save folder alone and said "esta en la pila".
        static void R4GifReadyText()
        {
            string root = Path.Combine(WorkDir("r4-save"), "Capturas");
            Settings s = new Settings();
            s.SaveFolder = root;
            const string Kept = "GIF listo y guardado junto al original", Stack = "GIF listo: est\u00E1 en la pila";
            DateTime when = new DateTime(2026, 10, 12, 9, 30, 0);
            for (int sub = 0; sub <= 2; sub++)
            {
                s.SaveSubfolder = sub;
                string folder = SaveFolders.Target(s, when);
                string video = Path.Combine(folder, "V\u00EDdeo 2026-10-12 09.30.00.mp4");
                string dir = ShotStack.ExportFolder(video, root);
                Check(string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase), "subcarpeta " + sub + ": el GIF va a " + dir + " y no junto al v\u00EDdeo (" + folder + ")");
                string gif = Path.Combine(dir, "V\u00EDdeo 2026-10-12 09.30.00 (tramo).gif");
                Check(PieceGif.ReadyText(s, gif) == Kept, "subcarpeta " + sub + ": " + PieceGif.ReadyText(s, gif));
                Check(SaveFolders.Inside(s, dir), "subcarpeta " + sub + ": el MP4 dir\u00EDa otra cosa");
                if (sub > 0) Check(!ShotStack.SameFolder(dir, root), "subcarpeta " + sub + ": la comparaci\u00F3n antigua ya la reconoc\u00EDa");
            }
            // Kept captures folder changed later (SaveSubfolder off): an older dated folder still counts.
            s.SaveSubfolder = 0;
            Check(PieceGif.ReadyText(s, Path.Combine(root, "2026-09", "a (tramo).gif")) == Kept, "una subcarpeta de otro mes no cuenta");
            // Anywhere else (the temporary captures, another folder, a look-alike name): only on the stack.
            string other = Path.Combine(Path.GetDirectoryName(root), "Otra");
            Check(PieceGif.ReadyText(s, Path.Combine(other, "a (tramo).gif")) == Stack, "otra carpeta: " + PieceGif.ReadyText(s, Path.Combine(other, "a (tramo).gif")));
            Check(PieceGif.ReadyText(s, Path.Combine(root, "Recortes", "a (tramo).gif")) == Stack, "una subcarpeta cualquiera cuenta como guardada");
            Check(PieceGif.ReadyText(s, Path.Combine(root + " 2", "a (tramo).gif")) == Stack, "una carpeta con nombre parecido cuenta como guardada");
            Add("carpeta, mes y a\u00F1o\\mes: \u00AB" + Kept + "\u00BB; fuera: \u00AB" + Stack + "\u00BB");
        }

        // The real microphone source before it has opened anything: what each failure means on screen, and nothing to read,
        // move or close. No device is touched (no COM object is made until Open).
        static void R4WasapiStates()
        {
            WasapiMic m = new WasapiMic(delegate { return ""; });
            MethodInfo fail = typeof(WasapiMic).GetMethod("Fail", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(fail != null, "no Fail");
            int[] codes = { unchecked((int)0x80070005), unchecked((int)0x8889000A), unchecked((int)0x88890004), unchecked((int)0x88890010), unchecked((int)0x80004005) };
            MicState[] want = { MicState.Denied, MicState.NotResponding, MicState.NotResponding, MicState.NotResponding, MicState.NotResponding };
            for (int i = 0; i < codes.Length; i++)
            {
                MicState got = (MicState)fail.Invoke(m, new object[] { codes[i] });
                Check(got == want[i], "0x" + codes[i].ToString("X8") + " -> " + got + ", expected " + want[i]);
                Check(m.Problem == "0x" + codes[i].ToString("X8"), "problem " + m.Problem);
            }
            Check(MicCheck.Words(MicState.Denied, 0) == "Windows no deja usar el micr\u00F3fono", "denied says " + MicCheck.Words(MicState.Denied, 0));
            Check(MicCheck.Words(MicState.NotResponding, 0) == "El micr\u00F3fono no responde", "busy says " + MicCheck.Words(MicState.NotResponding, 0));
            Check(!m.Read(new MicLevels()), "an unopened source reads");
            Check(!m.Moved(), "an unopened source asks to move");
            m.Close();
            m.Close();
            Add("acceso denegado -> Denied; en uso, invalidado, sin servicio, otro -> NotResponding; sin abrir no lee ni se mueve");
        }

        // FFmpeg failing (here it cannot even start): the alert with the reason, nothing left behind (no GIF, no palette),
        // the window free for the next export, and the reserved name given back (the next GIF takes it, not "(2)").
        static void R4GifFailure()
        {
            NeedFfmpeg();
            string clip = W6mClip("falla.mkv", "160x90", 10, 2, null, false);
            string dir = Path.GetDirectoryName(clip);
            Settings st = new Settings();
            st.GifFps = 10;
            PlayerWindow.Quiet = true;
            PlayerWindow w = PwWindow(clip, new PlFakeSound(), st);
            try
            {
                IntPtr h = w.Handle;
                string real = (string)PpField(w, "ffmpeg");
                PpSet(w, "ffmpeg", Path.Combine(dir, "no-existe", "ffmpeg.exe"));
                W6mExportGif(w, 0.2, 1.2, 60000);
                string note = w.ExportNote ?? "";
                Check(note.StartsWith("No se pudo crear el GIF.", StringComparison.Ordinal), "the alert: " + note);
                Check(Directory.GetFiles(dir, "*.gif").Length == 0, "files left: " + string.Join(", ", Directory.GetFiles(dir)));
                Check(W6mPalettesLeft().Length == 0, "palettes left: " + W6mPalettesLeft());
                Check(PpField(w, "exportText") == null && PpField(w, "exportAbort") == null, "the progress stayed");
                PpSet(w, "ffmpeg", real);
                W6mExportGif(w, 0.2, 1.2, 90000);
                Check(File.Exists(Path.Combine(dir, "falla (tramo).gif")) && w.ExportNote == "ok",
                      "the next GIF: " + w.ExportNote + " | " + string.Join(", ", Directory.GetFiles(dir)));
                Add("sin FFmpeg: \u00AB" + note.Replace("\n", " ").Trim() + "\u00BB; nada se queda y el siguiente GIF lleva su nombre");
            }
            finally { w.Dispose(); PlayerWindow.Quiet = false; }
        }

        // The real path once: the default capture endpoint through MicMonitor and WasapiMic (its own MTA thread), at most 2 s,
        // levels computed in memory and nothing written, then released (thread ended, COM objects let go). No device, or
        // Windows saying no: a quiet skip.
        static void R4MicProbe()
        {
            if (AudioDevices.Microphones().Count == 0) Skip("no hay micr\u00F3fono: no se abre nada");
            FieldInfo aliveField = typeof(MicMonitor).GetField("alive", BindingFlags.NonPublic | BindingFlags.Static);
            int aliveBefore = (int)aliveField.GetValue(null);
            Func<Func<string>, IMicSource> guard = MicMonitor.NewSource;
            Func<bool> recording = MicMonitor.IsRecording;
            WasapiMic made = null;
            MicMonitor mon = new MicMonitor(delegate { return ""; });
            MicMonitor.NewSource = delegate(Func<string> device) { WasapiMic m = new WasapiMic(device); made = m; return m; };
            MicMonitor.IsRecording = delegate { return false; };
            MicState state = MicState.Opening;
            long frames = 0;
            float peak = 0;
            double power = 0;
            long liveAt = -1, ms;
            Thread t;
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                mon.Start();
                t = mon.LastThread;
                Check(mon.Running && t != null, "the meter did not start");
                while (sw.ElapsedMilliseconds < 1800)
                {
                    Thread.Sleep(50);
                    MicReading r = mon.Take();
                    frames += r.Frames;
                    peak = Math.Max(peak, r.Peak);
                    power += (double)r.Rms * r.Rms * r.Frames;
                    state = mon.State;
                    if (state == MicState.Live && liveAt < 0) liveAt = sw.ElapsedMilliseconds;
                    if (state == MicState.NoDevice || state == MicState.Denied) break;
                }
                mon.Stop();
                Check(t.Join(3000), "the meter thread still runs 3 s after Stop");
                ms = sw.ElapsedMilliseconds;
            }
            finally
            {
                mon.Stop();
                MicMonitor.NewSource = guard;
                MicMonitor.IsRecording = recording;
            }
            Check((int)aliveField.GetValue(null) == aliveBefore, "meter threads counted alive: " + aliveField.GetValue(null) + ", before " + aliveBefore);
            Check(made != null, "the real source was never made");
            foreach (string f in new string[] { "client", "capture", "en" })
                Check(typeof(WasapiMic).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(made) == null, f + " was not released");
            string name = made.Name ?? "";
            if (state == MicState.Denied) Skip("Windows no deja usar el micr\u00F3fono (" + made.Problem + "); abierto y soltado sin error en " + ms + " ms");
            if (state == MicState.NoDevice) Skip("no hay micr\u00F3fono por defecto (" + made.Problem + ")");
            Check(state == MicState.Live && frames > 0, "the default microphone did not deliver: " + state + ", " + frames + " frames, problem " + made.Problem);
            double rms = frames > 0 ? Math.Sqrt(power / frames) : 0;
            Add("\u00AB" + name + "\u00BB en directo a los " + liveAt + " ms; " + frames + " fotogramas, pico " + MicScale.Dbfs(peak).ToString("0.0", CultureInfo.InvariantCulture) +
                " dBFS, RMS " + MicScale.Dbfs(rms).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS; soltado a los " + ms + " ms (hilo terminado, COM liberado)");
        }
    }
}

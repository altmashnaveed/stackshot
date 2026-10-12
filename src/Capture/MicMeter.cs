// Stackshot - Microphone test: the live level of the chosen microphone, measured on a thread of its own while a meter
// shows it (the options before recording, and the Grabacion page).
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stackshot
{
    // What the meter can show: nothing yet, the level, or why there is none.
    internal enum MicState { Off, Opening, Live, NoDevice, NotResponding, Denied, Recording }

    // What the microphone gave since the last look, as fractions of full scale.
    internal struct MicReading
    {
        public float Peak, Rms;
        public bool Clipped;
        public long Frames;
    }

    // Peak, power and clipping of the samples it is given, handed over and reset by Take. Clipping follows the recording's
    // own rule (AudioCapture.CountClipping): three samples or more in a row at full scale on one channel, so a single peak
    // that touches it is not clipping. One thread adds (the meter's), any thread takes.
    internal sealed class MicLevels
    {
        public const float FullScale = 0.999f;
        const int MaxChannels = 32;
        readonly object gate = new object();
        readonly int[] runs = new int[MaxChannels];
        float peak;
        double power;
        long samples, frames, total;
        bool clipped;
        float[] scratch = new float[0];

        // Every frame ever added (Take does not reset it): tells a device that delivers from one that has gone quiet.
        public long Total { get { lock (gate) return total; } }

        // Interleaved float samples, count frames of channels each.
        public void Add(float[] data, int offset, int count, int channels)
        {
            if (data == null || channels <= 0 || offset < 0) return;
            count = Math.Min(count, (data.Length - offset) / channels);
            if (count <= 0) return;
            lock (gate)
            {
                float pk = peak;
                double sum = power;
                for (int i = 0; i < count; i++)
                {
                    int at = offset + i * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float v = data[at + c];
                        if (float.IsNaN(v)) v = 0;
                        else if (v > 8) v = 8;
                        else if (v < -8) v = -8;
                        float a = v < 0 ? -v : v;
                        if (a > pk) pk = a;
                        sum += (double)v * v;
                        int ch = c < MaxChannels ? c : MaxChannels - 1;
                        if (a >= FullScale)
                        {
                            if (runs[ch] < 3) runs[ch]++;
                            if (runs[ch] >= 3) clipped = true;
                        }
                        else runs[ch] = 0;
                    }
                }
                peak = pk;
                power = sum;
                samples += (long)count * channels;
                frames += count;
                total += count;
            }
        }

        // Interleaved 16-bit samples.
        public void Add(short[] data, int offset, int count, int channels)
        {
            if (data == null || channels <= 0 || offset < 0) return;
            count = Math.Min(count, (data.Length - offset) / channels);
            if (count <= 0) return;
            int n = count * channels;
            if (scratch.Length < n) scratch = new float[n];
            for (int i = 0; i < n; i++) scratch[i] = data[offset + i] / 32768f;
            Add(scratch, 0, count, channels);
        }

        // A device buffer as it comes: float (32 or 64 bit) or integer PCM (8, 16, 24 or 32 bit).
        public void AddRaw(byte[] raw, int offset, int count, int channels, int bits, bool isFloat)
        {
            int bytes = bits / 8;
            if (raw == null || bytes <= 0 || channels <= 0 || offset < 0) return;
            count = Math.Min(count, (raw.Length - offset) / (bytes * channels));
            if (count <= 0) return;
            int n = count * channels;
            if (scratch.Length < n) scratch = new float[n];
            if (isFloat && bytes == 4) Buffer.BlockCopy(raw, offset, scratch, 0, n * 4);
            else for (int i = 0; i < n; i++) scratch[i] = Sample(raw, offset + i * bytes, bytes, isFloat);
            Add(scratch, 0, count, channels);
        }

        static float Sample(byte[] raw, int o, int bytes, bool isFloat)
        {
            if (isFloat) return bytes == 8 ? (float)BitConverter.ToDouble(raw, o) : 0f;
            if (bytes == 1) return (raw[o] - 128) / 128f;
            if (bytes == 2) return BitConverter.ToInt16(raw, o) / 32768f;
            if (bytes == 3) return ((raw[o] << 8) | (raw[o + 1] << 16) | (raw[o + 2] << 24)) / 2147483648f;
            if (bytes == 4) return BitConverter.ToInt32(raw, o) / 2147483648f;
            return 0f;
        }

        // A packet the device marked silent: it counts as time with nothing in it and breaks a run of clipped samples.
        public void AddSilence(int count, int channels)
        {
            if (count <= 0) return;
            lock (gate)
            {
                Array.Clear(runs, 0, runs.Length);
                samples += (long)count * Math.Max(1, channels);
                frames += count;
                total += count;
            }
        }

        public MicReading Take()
        {
            lock (gate)
            {
                MicReading r = new MicReading();
                r.Peak = peak;
                r.Rms = samples > 0 ? (float)Math.Sqrt(power / samples) : 0f;
                r.Clipped = clipped;
                r.Frames = frames;
                peak = 0;
                power = 0;
                samples = 0;
                frames = 0;
                clipped = false;
                return r;
            }
        }
    }

    // The meter's scale: dBFS from -60 (the empty bar) to 0 (full), yellow from -6 and red while it clips.
    internal static class MicScale
    {
        public const double Floor = -60, Warn = -6;

        public static double Dbfs(double amplitude)
        {
            return amplitude > 1e-9 ? 20 * Math.Log10(amplitude) : double.NegativeInfinity;
        }

        public static double Clamp(double db)
        {
            if (double.IsNaN(db) || db < Floor) return Floor;
            return db > 0 ? 0 : db;
        }

        // Where a level sits along the bar, 0..1.
        public static double Position(double db) { return (Clamp(db) - Floor) / -Floor; }

        // Colour of the bar at a level: 0 green, 1 yellow (from -6 dBFS), 2 red (that part while it clips).
        public static int Zone(double db, bool clipping)
        {
            if (Clamp(db) < Warn) return 0;
            return clipping ? 2 : 1;
        }
    }

    // How the meter moves: the bar jumps up at once and falls back smoothly, the loudest peak stays marked for a moment
    // and then falls, and "Saturando" stays up a little after the last clipped samples. Times in milliseconds.
    internal sealed class MicBallistics
    {
        public const double HoldMs = 1500, PeakFall = 20, BarFall = 26, ClipMs = 1500;   // falls in dB per second
        double peak = MicScale.Floor, rms = MicScale.Floor, held = MicScale.Floor, heldAt, clipUntil = double.MinValue, last = -1;

        public double Peak { get { return peak; } }
        public double Rms { get { return rms; } }

        public double Hold(double now)
        {
            return Math.Max(MicScale.Floor, held - PeakFall * Math.Max(0, now - heldAt - HoldMs) / 1000);
        }

        public bool Clipping(double now) { return now < clipUntil; }

        public void Step(MicReading r, double now)
        {
            double dt = last < 0 ? 0 : Math.Max(0, now - last) / 1000;
            last = now;
            double p = MicScale.Clamp(MicScale.Dbfs(r.Peak)), q = MicScale.Clamp(MicScale.Dbfs(r.Rms));
            peak = Math.Max(p, Math.Max(MicScale.Floor, peak - BarFall * dt));
            rms = Math.Max(q, Math.Max(MicScale.Floor, rms - BarFall * dt));
            if (p >= Hold(now)) { held = p; heldAt = now; }
            if (r.Clipped) clipUntil = now + ClipMs;
        }

        public void Reset()
        {
            peak = rms = held = MicScale.Floor;
            heldAt = 0;
            clipUntil = double.MinValue;
            last = -1;
        }
    }

    // Where the meter's samples come from: the microphone (WasapiMic), or a fake one in the tests. Every call is made on
    // the meter's own thread.
    internal interface IMicSource
    {
        // Opens the device: Live when it is ready to deliver, otherwise why not (NoDevice, Denied, NotResponding).
        MicState Open();
        // Adds what arrived since the last call; false when the device stopped working (unplugged, disabled, taken).
        bool Read(MicLevels into);
        // Another device should be used now: the choice in Grabacion changed, Windows' default changed, or the chosen
        // one came back or went away.
        bool Moved();
        string Name { get; }
        string Problem { get; }   // the last failure in a few words, for the log
        void Close();
    }

    // The live level of the microphone, on a background thread that runs only between Start and Stop. Opening the device
    // (seconds, with Bluetooth) and reading it never happen on the UI thread; the meter polls State and Take. It never
    // opens the device while a recording runs (the recording has it): Start refuses, and Yield lets go when one begins.
    // A device that is unplugged, busy or silent is retried every second, following Windows' default like a recording.
    internal sealed class MicMonitor
    {
        internal static Func<bool> IsRecording = delegate { return Recorder.Recording; };
        internal static Func<Func<string>, IMicSource> NewSource = delegate(Func<string> device) { return new WasapiMic(device); };
        static int alive;          // meter threads still running (a stopped one ends after its current call to the device)
        const int MaxAlive = 4;    // more than this are stuck in a driver: no new ones

        readonly Func<string> device;
        Session session;
        MicState idle = MicState.Off;

        // device: the chosen microphone's id, read when it opens and every second after (empty: Windows' default).
        public MicMonitor(Func<string> device) { this.device = device; }

        public bool Running { get { return session != null; } }
        public MicState State { get { Session s = session; return s != null ? s.State : idle; } }
        public string DeviceName { get { Session s = session; return s != null ? s.Name : ""; } }
        internal Thread LastThread { get; private set; }

        public void Start()
        {
            if (session != null) return;
            if (IsRecording()) { idle = MicState.Recording; return; }
            if (Thread.VolatileRead(ref alive) >= MaxAlive) { idle = MicState.NotResponding; return; }
            Session s = new Session(device, NewSource);
            Thread t = new Thread(s.Run);
            t.IsBackground = true;
            t.Name = "Stackshot mic test";
            t.SetApartmentState(ApartmentState.MTA);
            Interlocked.Increment(ref alive);
            try { t.Start(); }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref alive);
                idle = MicState.NotResponding;
                ShotStack.Log("Prueba del micr\u00F3fono: " + ex.Message);
                return;
            }
            session = s;
            idle = MicState.Off;
            LastThread = t;
        }

        // Returns at once: the thread lets go of the device after its current call.
        public void Stop()
        {
            Session s = session;
            session = null;
            idle = MicState.Off;
            if (s != null) s.Stop();
        }

        // A recording has started: the test steps aside. True when it did.
        public bool Yield()
        {
            if (session == null || !IsRecording()) return false;
            Stop();
            idle = MicState.Recording;
            return true;
        }

        public MicReading Take()
        {
            Session s = session;
            return s != null ? s.Levels.Take() : new MicReading();
        }

        sealed class Session
        {
            public readonly MicLevels Levels = new MicLevels();
            readonly Func<string> device;
            readonly Func<Func<string>, IMicSource> make;
            readonly object gate = new object();
            volatile bool stopping;
            volatile MicState state = MicState.Opening;
            volatile string name = "";

            public Session(Func<string> device, Func<Func<string>, IMicSource> make)
            {
                this.device = device;
                this.make = make;
            }

            public MicState State { get { return state; } }
            public string Name { get { return name; } }

            public void Stop()
            {
                lock (gate) { stopping = true; Monitor.PulseAll(gate); }
            }

            void Wait(int ms)
            {
                lock (gate) { if (!stopping) Monitor.Wait(gate, ms); }
            }

            public void Run()
            {
                IMicSource src = null;
                try
                {
                    src = make(device);
                    Loop(src);
                }
                catch (Exception ex)
                {
                    state = MicState.NotResponding;
                    ShotStack.Log("Prueba del micr\u00F3fono: " + ex.Message);
                }
                finally
                {
                    if (src != null)
                        try { src.Close(); }
                        catch (Exception ex) { ShotStack.Log("Prueba del micr\u00F3fono: " + ex.Message); }
                    Interlocked.Decrement(ref alive);
                }
            }

            void Loop(IMicSource src)
            {
                long freq = Stopwatch.Frequency;
                bool open = false;
                long retryAt = 0, checkAt = 0, heardAt = 0, seen = -1;
                MicState said = MicState.Off;   // the last problem written to the log, so a retry every second says it once
                while (!stopping)
                {
                    long now = Stopwatch.GetTimestamp();
                    try
                    {
                        if (!open)
                        {
                            if (now >= retryAt)
                            {
                                MicState s = src.Open();
                                now = Stopwatch.GetTimestamp();
                                if (s == MicState.Live)
                                {
                                    // Live once samples arrive; a device that opens but never delivers does not respond.
                                    open = true;
                                    name = src.Name ?? "";
                                    heardAt = now;
                                    checkAt = now + freq;
                                    seen = Levels.Total;
                                }
                                else
                                {
                                    src.Close();
                                    said = Problem(s, src, said);
                                    retryAt = now + freq;
                                }
                            }
                        }
                        else if (!src.Read(Levels))
                        {
                            src.Close();
                            open = false;
                            said = Problem(MicState.NotResponding, src, said);
                            retryAt = now + freq / 2;
                        }
                        else
                        {
                            long total = Levels.Total;
                            if (total != seen) { seen = total; heardAt = now; state = MicState.Live; said = MicState.Off; }
                            else if (now - heardAt > 4 * freq)
                            {
                                // Nothing for a long while (a working one sends even silence): opened again.
                                src.Close();
                                open = false;
                                retryAt = now + freq;
                                continue;
                            }
                            else if (now - heardAt > 2 * freq) said = Problem(MicState.NotResponding, src, said);
                            if (now >= checkAt)
                            {
                                checkAt = now + freq;
                                if (src.Moved()) { src.Close(); open = false; retryAt = 0; }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { src.Close(); } catch { }
                        open = false;
                        if (said != MicState.NotResponding) ShotStack.Log("Prueba del micr\u00F3fono: " + ex.Message);
                        state = MicState.NotResponding;
                        said = MicState.NotResponding;
                        retryAt = Stopwatch.GetTimestamp() + freq;
                    }
                    Wait(open ? 15 : 50);
                }
            }

            MicState Problem(MicState s, IMicSource src, MicState said)
            {
                state = s;
                if (said == s) return said;
                string why = src.Problem;
                ShotStack.Log("Prueba del micr\u00F3fono: " + (s == MicState.NoDevice ? "no hay micr\u00F3fono" : s == MicState.Denied ? "Windows no deja usarlo" : "no responde") +
                              (string.IsNullOrEmpty(why) ? "" : " (" + why + ")"));
                return s;
            }
        }
    }

    // The microphone through WASAPI in shared mode, at the device's own mix format (measuring needs no conversion): the
    // one chosen in Grabacion while it is plugged in, else Windows' default, as a recording picks it (AudioCapture.Open).
    internal sealed class WasapiMic : IMicSource
    {
        const int Shared = 0, ClsctxAll = 23, MaxPackets = 64;
        const int NotFound = unchecked((int)0x80070490), AccessDenied = unchecked((int)0x80070005), BufferEmpty = 0x08890001;
        static readonly Guid IidAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        static readonly Guid IidCapture = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        static readonly Guid FloatFormat = new Guid("00000003-0000-0010-8000-00AA00389B71");

        readonly Func<string> device;
        IMMDeviceEnumerator en;
        IAudioClient client;
        IAudioCaptureClient capture;
        string wanted, openId, name = "", problem;
        int channels = 2, bits = 32, frameBytes = 8;
        bool isFloat = true;
        byte[] buf = new byte[0];

        public WasapiMic(Func<string> device) { this.device = device; }

        public string Name { get { return name; } }
        public string Problem { get { return problem; } }

        string Wanted()
        {
            string id = device != null ? device() : null;
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public MicState Open()
        {
            Close();
            en = AudioDevices.Enumerator();
            wanted = Wanted();
            IMMDevice dev = null;
            int hr = -1;
            if (wanted != null && AudioDevices.IsActive(en, AudioDevices.Capture, wanted)) hr = en.GetDevice(wanted, out dev);
            if (hr < 0 || dev == null) hr = en.GetDefaultAudioEndpoint(AudioDevices.Capture, AudioDevices.Console, out dev);
            if (hr < 0 || dev == null)
            {
                problem = "0x" + hr.ToString("X8");
                return hr == NotFound || hr >= 0 ? MicState.NoDevice : MicState.NotResponding;
            }
            try
            {
                dev.GetId(out openId);
                name = AudioDevices.Name(dev) ?? "";
                object o;
                Guid iid = IidAudioClient;
                hr = dev.Activate(ref iid, ClsctxAll, IntPtr.Zero, out o);
                if (hr < 0) return Fail(hr);
                client = (IAudioClient)o;
                IntPtr mix;
                hr = client.GetMixFormat(out mix);
                if (hr < 0) return Fail(hr);
                try
                {
                    ReadFormat(mix);
                    hr = client.Initialize(Shared, 0, 2000000, 0, mix, IntPtr.Zero);
                }
                finally { Marshal.FreeCoTaskMem(mix); }
                if (hr < 0) return Fail(hr);
                object svc;
                iid = IidCapture;
                hr = client.GetService(ref iid, out svc);
                if (hr < 0) return Fail(hr);
                capture = (IAudioCaptureClient)svc;
                hr = client.Start();
                if (hr < 0) return Fail(hr);
                problem = null;
                return MicState.Live;
            }
            finally { Marshal.ReleaseComObject(dev); }
        }

        // Privacy settings say no; anything else (busy in exclusive mode, unplugged meanwhile) does not respond.
        MicState Fail(int hr)
        {
            problem = "0x" + hr.ToString("X8");
            return hr == AccessDenied ? MicState.Denied : MicState.NotResponding;
        }

        void ReadFormat(IntPtr f)
        {
            int tag = Marshal.ReadInt16(f, 0) & 0xFFFF;
            channels = Math.Max(1, (int)Marshal.ReadInt16(f, 2));
            frameBytes = Math.Max(1, Marshal.ReadInt16(f, 12) & 0xFFFF);
            bits = frameBytes * 8 / channels;
            isFloat = tag == 3;
            if (tag == 0xFFFE)
            {
                byte[] g = new byte[16];
                Marshal.Copy(new IntPtr(f.ToInt64() + 24), g, 0, 16);
                isFloat = new Guid(g) == FloatFormat;
            }
        }

        public bool Read(MicLevels into)
        {
            if (capture == null) return false;
            for (int packets = 0; packets < MaxPackets; packets++)
            {
                int n;
                int hr = capture.GetNextPacketSize(out n);
                if (hr < 0) { problem = "0x" + hr.ToString("X8"); return false; }
                if (n == 0) return true;
                IntPtr data;
                int flags;
                long pos, qpc;
                hr = capture.GetBuffer(out data, out n, out flags, out pos, out qpc);
                if (hr < 0) { problem = "0x" + hr.ToString("X8"); return false; }
                if (hr == BufferEmpty || n == 0) { capture.ReleaseBuffer(n); return true; }
                try
                {
                    if ((flags & 2) != 0) into.AddSilence(n, channels); // AUDCLNT_BUFFERFLAGS_SILENT
                    else
                    {
                        int bytes = n * frameBytes;
                        if (buf.Length < bytes) buf = new byte[bytes];
                        Marshal.Copy(data, buf, 0, bytes);
                        into.AddRaw(buf, 0, n, channels, bits, isFloat);
                    }
                }
                finally { capture.ReleaseBuffer(n); }
            }
            return true;
        }

        public bool Moved()
        {
            if (en == null) return false;
            string want = Wanted();
            if (!string.Equals(want, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            string target = want != null && AudioDevices.IsActive(en, AudioDevices.Capture, want) ? want : DefaultId();
            return target != null && !string.Equals(target, openId, StringComparison.OrdinalIgnoreCase);
        }

        string DefaultId()
        {
            IMMDevice d;
            if (en.GetDefaultAudioEndpoint(AudioDevices.Capture, AudioDevices.Console, out d) < 0 || d == null) return null;
            try
            {
                string id;
                return d.GetId(out id) >= 0 ? id : null;
            }
            finally { Marshal.ReleaseComObject(d); }
        }

        public void Close()
        {
            try { if (client != null) client.Stop(); } catch { }
            if (capture != null) { Marshal.ReleaseComObject(capture); capture = null; }
            if (client != null) { Marshal.ReleaseComObject(client); client = null; }
            if (en != null) { Marshal.ReleaseComObject(en); en = null; }
        }
    }
}

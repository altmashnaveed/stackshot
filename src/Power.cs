// Stackshot - Power and child-process helpers shared by recording, the mascot and FFmpeg passes.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Stackshot
{
    public static class Power
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottling { public uint Version, ControlMask, StateMask; }

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref PowerThrottling info, int size);
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;
        static int unthrottled;

        // Windows 11 "efficiency mode" (EcoQoS) and timer throttling make a background process tick slowly: the mascot
        // stutters and recording threads miss frames while another app has focus. Opts the whole process out of both,
        // once; false if Windows refused (Windows 10 before 1709 has no such setting).
        static volatile bool optedOut;

        public static bool OptOutOfThrottling()
        {
            if (Interlocked.Exchange(ref unthrottled, 1) == 1) return optedOut;
            try
            {
                PowerThrottling st = new PowerThrottling();
                st.Version = 1;
                st.ControlMask = 1 | 4;   // PROCESS_POWER_THROTTLING_EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                st.StateMask = 0;         // bit cleared: throttling off
                bool ok = SetProcessInformation(GetCurrentProcess(), 4, ref st, Marshal.SizeOf(typeof(PowerThrottling)));
                if (!ok)
                {
                    // Windows 10 before build 22000 rejects the timer bit, and with it the whole call.
                    st.ControlMask = 1;
                    ok = SetProcessInformation(GetCurrentProcess(), 4, ref st, Marshal.SizeOf(typeof(PowerThrottling)));
                }
                if (!ok) ShotStack.Log("Energ\u00EDa: Windows no deja quitar el modo eficiencia (" + Marshal.GetLastWin32Error() + ")");
                optedOut = ok;
                return ok;
            }
            catch (Exception ex)
            {
                ShotStack.Log("Energ\u00EDa: " + ex.Message);
                return false;
            }
        }

        // A child process (the live encoder) out of the same throttling: it is window-less, so Windows would otherwise
        // treat it as background work and park it on efficient cores.
        public static void OptOutOfThrottling(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return;
                PowerThrottling st = new PowerThrottling();
                st.Version = 1;
                st.ControlMask = 1 | 4;
                st.StateMask = 0;
                SetProcessInformation(p.Handle, 4, ref st, Marshal.SizeOf(typeof(PowerThrottling)));
            }
            catch { }
        }

        // The calling thread out of execution-speed throttling (ThreadPowerThrottling accepts only that bit).
        public static void OptOutThisThread()
        {
            try
            {
                PowerThrottling th = new PowerThrottling();
                th.Version = 1;
                th.ControlMask = 1;
                SetThreadInformation(GetCurrentThread(), 3, ref th, Marshal.SizeOf(typeof(PowerThrottling)));
            }
            catch { }
        }

        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr AvSetMmThreadCharacteristics(string task, ref uint index);
        [DllImport("avrt.dll", SetLastError = true)] static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

        // Registers the calling thread with the multimedia scheduler (MMCSS) under a task such as "Pro Audio" or
        // "Capture", so it keeps its time slices when the machine is busy. No admin rights needed; IntPtr.Zero if refused.
        public static IntPtr JoinMmcss(string task)
        {
            try
            {
                uint index = 0;
                return AvSetMmThreadCharacteristics(task, ref index);
            }
            catch { return IntPtr.Zero; }
        }

        // Undoes JoinMmcss, on the same thread.
        public static void LeaveMmcss(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try { AvRevertMmThreadCharacteristics(handle); }
            catch { }
        }

        // While the returned object lives, neither the system nor the display sleeps (a recording in progress). The
        // request is per thread: call it on a thread that lives as long as the object, or rely on Dispose from it.
        public static IDisposable KeepAwake()
        {
            return new Awake();
        }

        sealed class Awake : IDisposable
        {
            readonly Thread holder;
            readonly ManualResetEvent done = new ManualResetEvent(false);

            public Awake()
            {
                // A dedicated thread holds the request, so it doesn't depend on which thread disposes it.
                holder = new Thread(delegate()
                {
                    SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                    done.WaitOne();
                    SetThreadExecutionState(EsContinuous);
                });
                holder.IsBackground = true;
                holder.Start();
            }

            public void Dispose()
            {
                done.Set();
            }
        }
    }

    // Every helper process (FFmpeg) joins a job object that Windows kills when Stackshot's last handle closes: a crash
    // or a kill of Stackshot never leaves FFmpeg running on its own. The live recording encoders stay out on purpose:
    // when Stackshot dies they see the end of their input and finish the file (a direct MP4 killed mid-way has no index
    // and does not play).
    public static class ChildJob
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static readonly object sync = new object();
        static IntPtr job;
        static bool failed;

        // Adds a started process to the job; harmless if it already exited or the job can't be made.
        public static void Add(Process p)
        {
            if (p == null) return;
            try
            {
                IntPtr j = Job();
                if (j != IntPtr.Zero && !p.HasExited) AssignProcessToJobObject(j, p.Handle);
            }
            catch { }
        }

        static IntPtr Job()
        {
            lock (sync)
            {
                if (job != IntPtr.Zero || failed) return job;
                IntPtr j = CreateJobObject(IntPtr.Zero, null);
                if (j == IntPtr.Zero) { failed = true; return IntPtr.Zero; }
                ExtendedLimits info = new ExtendedLimits();
                info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(j, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimits)))) { failed = true; return IntPtr.Zero; }
                job = j; // never closed: it lives as long as the process, which is the point
                return job;
            }
        }
    }
}

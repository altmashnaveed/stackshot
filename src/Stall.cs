// Stackshot - Notices when the interface thread stops answering for a moment and writes it to the log with the last
// thing marked, so any remaining hitch can be traced from the user's log.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot
{
    public static class Stall
    {
        const int EveryMs = 250, SlowMs = 300, GapMs = 5000, MaxLines = 50;

        static Control sync;
        static Thread watcher;
        static volatile string what = "";
        static long whatAt;
        static int pending, lines;
        static int lastLine = Environment.TickCount - 60000;

        // What the interface thread is about to do (cheap: a string and a time).
        public static void Mark(string action)
        {
            what = action;
            Interlocked.Exchange(ref whatAt, Environment.TickCount);
        }

        public static void Start(Control owner)
        {
            if (watcher != null || owner == null) return;
            sync = owner;
            watcher = new Thread(Watch) { IsBackground = true, Name = "Stackshot stall watch", Priority = ThreadPriority.BelowNormal };
            watcher.Start();
        }

        static void Watch()
        {
            while (true)
            {
                Thread.Sleep(EveryMs);
                if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;   // the last ping is still waiting
                int sent = Environment.TickCount;
                try { sync.BeginInvoke((Action)delegate { Answer(sent); }); }
                catch (Exception) { pending = 0; Thread.Sleep(1000); }
            }
        }

        static void Answer(int sent)
        {
            pending = 0;
            int now = Environment.TickCount, late = unchecked(now - sent);
            // A minute or more is the computer sleeping, not the app.
            if (late < SlowMs || late > 60000 || lines >= MaxLines || unchecked(now - lastLine) < GapMs) return;
            lines++;
            lastLine = now;
            string w = what;
            int ago = unchecked(now - (int)Interlocked.Read(ref whatAt));
            ShotStack.Log("Interfaz parada " + late + " ms" + (w.Length > 0 ? " (\u00FAltima acci\u00F3n: " + w + ", hace " + ago + " ms)" : ""));
        }
    }
}

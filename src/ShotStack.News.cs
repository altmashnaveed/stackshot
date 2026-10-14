// Stackshot - Right after an update, what the new version brings is told once: by the desktop mascot when it is out
// and free, otherwise by a notice. Never while recording, with Windows asking for quiet or with nobody at the computer.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        Timer newsTimer;
        int newsTries;

        // justInstalled: a fresh install (the welcome said enough), so its news count as told.
        void StartNews(bool justInstalled)
        {
            if (Test) return;
            string v = Installer.MyVersion.ToString(3);
            if (settings.NewsSeen == v) return;
            // Nothing to tell: a fresh install, or a version without news of its own. Remembered, quietly.
            if (justInstalled || News.Of(v).Count == 0) { NewsTold(v); return; }
            newsTimer = new Timer { Interval = 15000 };
            newsTimer.Tick += delegate
            {
                try { NewsTick(v); }
                catch (Exception ex) { Log("Novedades: " + ex.Message); NewsTold(v); }
            };
            newsTimer.Start();
        }

        void NewsTick(string v)
        {
            if (exiting) { StopNews(); return; }
            // Half an hour without a quiet moment: it gives up (Acerca de > Novedades still has them).
            if (++newsTries > 120) { NewsTold(v); return; }
            if (Recorder.Recording || ToastQuietNow(Screen.PrimaryScreen.Bounds) != null || Native.IdleMs() >= Wellness.ActiveIdleMs) return;
            // The mascot on the desktop tells it once it is free (a couple of minutes at most, then a notice does).
            if (pet != null && !pet.CanRemind && newsTries < 8) return;
            List<News.Item> items = News.Of(v);
            string head = News.Headline(items, 1);
            NewsTold(v);
            Action open = delegate { if (!exiting) NewsWindow.Open(false); };
            if (pet != null && pet.CanRemind) pet.AnnounceNews(v, head, open);
            else Notify("Stackshot " + v + " ya est\u00E1 aqu\u00ED", (head.Length > 0 ? "Entre sus novedades: \u00AB" + head + "\u00BB. " : "") + "\u00BFQuieres ver qu\u00E9 trae?",
                        NoticeKind.Info, "Ver novedades", open);
        }

        void NewsTold(string v)
        {
            StopNews();
            settings.NewsSeen = v;
            settings.Save();
        }

        void StopNews()
        {
            if (newsTimer == null) return;
            newsTimer.Stop();
            newsTimer.Dispose();
            newsTimer = null;
        }
    }
}

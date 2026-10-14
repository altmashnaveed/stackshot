// Stackshot - Focus mode (Pomodoro) as the user lives it: started from the desktop mascot or Mascota > Ajustes, a block
// of work with the mascot reading quietly beside you, a break when it ends (a long one every few blocks), and a question
// at each change unless "Seguir solo" is on. Away for 5 minutes, the clock stops and asks when you are back. Healthy
// breaks wait for the break, and today's blocks are counted (and are friendship points). The clock ticks only while on.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        readonly FocusClock focus = new FocusClock();
        Timer focusTimer;
        string trayText;
        bool focusWasAway;                          // a minute or more without input (a question is asked again on return)
        const int FocusAwayMs = 5 * 60 * 1000;      // away this long during a block: the clock stops
        const int FocusForgetMs = 45 * 60 * 1000;   // a question unanswered this long: focus ends on its own

        public FocusClock FocusState { get { return focus; } }

        // A short status for menus and the settings page: "Concentración · bloque 2 de 4 · quedan 12 min".
        public string FocusLine()
        {
            DateTime now = DateTime.UtcNow;
            if (!focus.On) return null;
            string left = FocusClock.Clock(focus.Left(now));
            if (focus.Asking) return focus.Now == FocusClock.Phase.Work ? "Bloque " + focus.Block + " de " + focus.Rounds + " hecho" : "Fin del descanso";
            if (focus.Now == FocusClock.Phase.Break) return (focus.LongBreak ? "Descanso largo" : "Descanso") + (focus.Paused ? " en pausa" : "") + " \u00B7 quedan " + left;
            return "Concentraci\u00F3n" + (focus.Paused ? " en pausa" : "") + " \u00B7 bloque " + focus.Block + " de " + focus.Rounds + " \u00B7 quedan " + left;
        }

        // Today's blocks and minutes ("Hoy: 3 bloques, 1 h 15 min"), or null before the first.
        public string FocusToday()
        {
            FocusDayCheck();
            if (settings.FocusBlocks == 0) return null;
            return "Hoy: " + settings.FocusBlocks + (settings.FocusBlocks == 1 ? " bloque, " : " bloques, ") + FocusClock.Minutes(settings.FocusMinutes) + " de concentraci\u00F3n";
        }

        void FocusDayCheck()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (settings.FocusDay == today) return;
            settings.FocusDay = today;
            settings.FocusBlocks = settings.FocusMinutes = 0;
        }

        public void FocusStart()
        {
            if (exiting) return;
            FocusDayCheck();
            focus.Rounds = settings.FocusRounds;
            if (focus.On) focus.Stop();
            focus.StartWork(settings.FocusWork, DateTime.UtcNow, false);
            Log("Concentraci\u00F3n: bloque de " + settings.FocusWork + " min");
            if (settings.FocusSound) FocusChime.Play(true);
            FocusTell("Concentraci\u00F3n: bloque 1 de " + focus.Rounds, "\u00A1A concentrarnos! " + FocusClock.Minutes(settings.FocusWork) + ". Te aviso al terminar.", null, null);
            FocusRun();
        }

        public void FocusPause()
        {
            focus.Pause(DateTime.UtcNow);
            FocusTell((focus.Now == FocusClock.Phase.Work ? "Concentraci\u00F3n" : "Descanso") + " en pausa", "Quedan " + FocusClock.Clock(focus.Left(DateTime.UtcNow)) + ".", "Reanudar", delegate { if (focus.Paused) FocusResume(); });
            FocusChanged();
        }

        public void FocusResume()
        {
            focus.Resume(DateTime.UtcNow);
            bool work = focus.Now == FocusClock.Phase.Work;
            FocusTell(work ? "Seguimos con la concentraci\u00F3n" : "Seguimos con el descanso", "Quedan " + FocusClock.Clock(focus.Left(DateTime.UtcNow)) + ".", null, null);
            FocusChanged();
        }

        public void FocusStop()
        {
            if (!focus.On) return;
            focus.Stop();
            Log("Concentraci\u00F3n: terminada");
            string today = FocusToday();
            FocusTell("Concentraci\u00F3n terminada", today != null ? today + ". \u00A1Bien hecho!" : "Lo dejamos aqu\u00ED.", null, null);
            FocusChanged();
        }

        // After a block: its break now.
        public void FocusBreak() { FocusBreak(null); }

        // done: the block just finished (its notice says both things).
        void FocusBreak(string done)
        {
            focus.StartBreak(settings.FocusShort, settings.FocusLong, DateTime.UtcNow);
            string what = focus.LongBreak ? "Descanso largo de " + FocusClock.Minutes(settings.FocusLong) : "Descanso de " + FocusClock.Minutes(settings.FocusShort);
            FocusTell(done ?? (focus.LongBreak ? "Descanso largo" : "Descanso"), what + ": lev\u00E1ntate, mira lejos, bebe agua. Te aviso al terminar.", null, null);
            FocusRun();
        }

        // The next block (skipping what is left of a break), or the same block again.
        public void FocusWork(bool again) { FocusWork(again, null); }

        // done: the break just finished.
        void FocusWork(bool again, string done)
        {
            focus.Rounds = settings.FocusRounds;
            focus.StartWork(settings.FocusWork, DateTime.UtcNow, again);
            FocusTell(done ?? (again ? "Bloque " + focus.Block + " de nuevo" : "Concentraci\u00F3n: bloque " + focus.Block + " de " + focus.Rounds),
                      (done != null ? "Bloque " + focus.Block + " de " + focus.Rounds + ": " : "") + FocusClock.Minutes(settings.FocusWork) + " de concentraci\u00F3n. \u00A1Vamos!", null, null);
            FocusRun();
        }

        // A few more minutes of break.
        public void FocusMore(int minutes)
        {
            focus.Extend(minutes, DateTime.UtcNow);
            FocusTell("Descanso", minutes + " minutos m\u00E1s. Te aviso al terminar.", null, null);
            FocusRun();
        }

        // Every change is told: in the mascot's bubble when it is on the desktop, and as a notice by the clock (with a
        // button for the obvious next step, if any) unless those are turned off.
        void FocusTell(string title, string text, string actionLabel, Action action)
        {
            FocusSay(text);
            // Without the desktop mascot the notices are the only way to know, whatever the setting.
            if (settings.FocusNotices || pet == null) Notify(title, text, NoticeKind.Info, actionLabel, action);
        }

        void FocusRun()
        {
            if (focusTimer == null)
            {
                focusTimer = new Timer { Interval = 1000 };
                focusTimer.Tick += delegate
                {
                    try { FocusTick(); }
                    catch (Exception ex) { Log("Concentraci\u00F3n: " + ex.Message); }
                };
            }
            focusTimer.Start();
            FocusChanged();
        }

        void FocusTick()
        {
            if (exiting || !focus.On) { if (focusTimer != null) focusTimer.Stop(); FocusChanged(); return; }
            DateTime now = DateTime.UtcNow;
            int idle = Native.IdleMs();
            // Away in the middle of a block: the clock stops where they left (that time doesn't count).
            if (focus.Working && idle >= FocusAwayMs)
            {
                focus.PauseAway(now - TimeSpan.FromMilliseconds(idle), now);
                Log("Concentraci\u00F3n: en pausa por ausencia");
            }
            // Back: asked once whether to go on.
            if (focus.Away && idle < 3000 && !focus.Asking)
            {
                focus.Away = false;
                FocusAskBack();
            }
            // A question that came while they were away (a break ending) is asked again when they are back.
            bool back = focusWasAway && idle < 3000;
            if (idle >= 60000) focusWasAway = true;
            else if (idle < 3000) focusWasAway = false;
            if (back && focus.Asking && pet != null)
            {
                if (focus.Now == FocusClock.Phase.Work) pet.FocusAskWork(null);
                else pet.FocusAskBreak();
            }
            // A question nobody answers: it ends quietly (what was done still counts).
            if (focus.Asking && now - focus.AskingSince > TimeSpan.FromMilliseconds(FocusForgetMs)) { focus.Stop(); Log("Concentraci\u00F3n: terminada sin respuesta"); }
            if (focus.Due(now)) FocusPhaseEnded(now);
            FocusChanged();
        }

        void FocusPhaseEnded(DateTime now)
        {
            if (settings.FocusSound) FocusChime.Play(focus.Now == FocusClock.Phase.Break);
            // Without the desktop mascot nobody could answer: it goes on by itself, and notices say so.
            bool auto = settings.FocusAuto || pet == null;
            if (focus.Now == FocusClock.Phase.Work)
            {
                FocusDayCheck();
                settings.FocusBlocks++;
                settings.FocusMinutes += (int)Math.Round(focus.Total.TotalMinutes);
                settings.Save();
                string up = LoveUp();
                Log("Concentraci\u00F3n: bloque " + focus.Block + " hecho");
                string done = "Bloque " + focus.Block + " de " + focus.Rounds + " hecho";
                if (auto) { FocusBreak(done); return; }
                focus.Ask(now);
                pet.FocusAskWork(up);
                if (settings.FocusNotices) Notify(done, (FocusToday() ?? "") + " \u00BFDescansamos?", NoticeKind.Info, "Empezar el descanso", delegate { if (focus.Asking && focus.Now == FocusClock.Phase.Work) FocusBreak(); });
            }
            else
            {
                if (auto) { FocusWork(false, "Fin del descanso"); return; }
                focus.Ask(now);
                pet.FocusAskBreak();
                if (settings.FocusNotices) Notify("Fin del descanso", "\u00BFSeguimos con el siguiente bloque?", NoticeKind.Info, "Empezar el bloque", delegate { if (focus.Asking && focus.Now == FocusClock.Phase.Break) FocusWork(false); });
            }
        }

        void FocusAskBack()
        {
            int m = Math.Max(1, (int)Math.Round((DateTime.UtcNow - focus.AwaySince).TotalMinutes));
            string text = "Has estado fuera " + m + " min y he parado el reloj: quedaban " + FocusClock.Clock(focus.Left(DateTime.UtcNow)) + ".";
            if (pet != null) pet.FocusAskBack(text);
            else Notify("Concentraci\u00F3n en pausa", text, NoticeKind.Info, "Seguir", delegate { if (focus.Paused) FocusResume(); });
        }

        // What the mascot says about it (in its bubble), or nothing without it on the desktop.
        void FocusSay(string text)
        {
            if (pet != null) pet.FocusSay(text);
        }

        // The pill above the mascot, the tray tooltip and the settings page follow the clock.
        void FocusChanged()
        {
            string t = focus.On ? "Stackshot \u00B7 " + FocusLine() : "Stackshot";
            if (t.Length > 63) t = t.Substring(0, 63);
            if (t != trayText && tray != null && !Test) { trayText = t; tray.Text = t; }
        }

        // Healthy breaks wait while a block runs (the break is the moment for them).
        bool FocusHoldsBreaks { get { return focus.Working; } }
    }
}

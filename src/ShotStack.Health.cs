// Stackshot - Health with the mascot: a daily water goal (the glasses done with it, "+1 vaso" and the water logged on
// the watch), the day's summary at the hour chosen, and, with a Pixel Watch or Fitbit connected, a few gentle words
// about steps and sleep (once each, never during a focus block, a call or a recording). A short night makes the eye
// and movement breaks come a little sooner that day. The watch is read every half hour, only while connected.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Stackshot
{
    public partial class ShotStack
    {
        Timer healthTimer;
        Fitbit.Today watch;
        bool watchReading;
        double nextWatchRead;
        const double WatchEveryMs = 30 * 60 * 1000;
        static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        static string Today { get { return DateTime.Today.ToString("yyyy-MM-dd"); } }

        void StartHealth()
        {
            if (Test) return;
            watch = LastReading();
            nextWatchRead = Anim.Now + 60000;
            healthTimer = new Timer { Interval = 60000 };
            healthTimer.Tick += delegate
            {
                try { HealthTick(); }
                catch (Exception ex) { Log("Salud: " + ex.Message); }
            };
            healthTimer.Start();
        }

        void HealthTick()
        {
            if (exiting) return;
            if (Fitbit.Connected && !watchReading && Anim.Now >= nextWatchRead) ReadWatch(null);
            DaySummaryTick();
        }

        // ---- Water

        // Glasses today: the ones done with the mascot or "+1 vaso", or the water logged on the watch if that is more.
        public int WaterToday()
        {
            int app = Wellness.Count(settings, WellKind.Water);
            int fromWatch = watch != null && watch.ReadAt.Date == DateTime.Today ? watch.WaterMl / 250 : 0;
            return Math.Max(app, fromWatch);
        }

        // A glass drunk: counted (a friendship point) and the water reminder starts over.
        public int WaterPlusOne(out string levelUp)
        {
            WellDone(WellKind.Water, out levelUp);
            return WaterToday();
        }

        // ---- The day's summary

        // "Hoy: 6 de 8 vasos de agua, 3 descansos de vista, 2 bloques de concentración (50 min), 5.400 pasos y 14 capturas."
        public string DaySummary()
        {
            List<string> parts = new List<string>();
            int water = WaterToday();
            if (water > 0 || settings.WaterGoal > 0)
                parts.Add(settings.WaterGoal > 0 ? water + " de " + settings.WaterGoal + " vasos de agua" : water + (water == 1 ? " vaso" : " vasos") + " de agua");
            int eyes = Wellness.Count(settings, WellKind.Eyes), move = Wellness.Count(settings, WellKind.Move), breathe = Wellness.Count(settings, WellKind.Breathe);
            if (eyes > 0) parts.Add(eyes + (eyes == 1 ? " descanso" : " descansos") + " de vista");
            if (move > 0) parts.Add(move + (move == 1 ? " pausa" : " pausas") + " para moverte");
            if (breathe > 0) parts.Add(breathe + " " + (breathe == 1 ? "minuto" : "minutos") + " respirando");
            FocusDayCheck();
            if (settings.FocusBlocks > 0) parts.Add(settings.FocusBlocks + (settings.FocusBlocks == 1 ? " bloque" : " bloques") + " de concentraci\u00F3n (" + FocusClock.Minutes(settings.FocusMinutes) + ")");
            if (watch != null && watch.ReadAt.Date == DateTime.Today && watch.Steps > 0) parts.Add(watch.Steps.ToString("N0", Es) + " pasos");
            StatsDayCheck();
            if (settings.StatsCaptures > 0) parts.Add(settings.StatsCaptures + (settings.StatsCaptures == 1 ? " captura" : " capturas"));
            if (parts.Count == 0) return null;
            string last = parts[parts.Count - 1];
            parts.RemoveAt(parts.Count - 1);
            return "Hoy: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) + " y " + last : last) + ".";
        }

        void DaySummaryTick()
        {
            if (settings.SummaryHour <= 0 || DateTime.Now.Hour < settings.SummaryHour || settings.SummaryDay == Today) return;
            if (Native.IdleMs() >= Wellness.ActiveIdleMs || WellQuiet() || focus.Working) return;
            if (pet != null && !pet.CanRemind) return;
            string text = DaySummary();
            settings.SummaryDay = Today;
            settings.Save();
            if (text == null) return;
            if (pet != null) pet.HealthTell("\u00BFQu\u00E9 tal el d\u00EDa? " + text, true);
            else Notify("Tu d\u00EDa", text);
        }

        // ---- Today's captures

        void StatsDayCheck()
        {
            if (settings.StatsDay == Today) return;
            settings.StatsDay = Today;
            settings.StatsCaptures = 0;
        }

        void CountCapture()
        {
            StatsDayCheck();
            settings.StatsCaptures++;
            settings.Save();
        }

        // ---- The watch

        public Fitbit.Today Watch { get { return watch != null && watch.ReadAt.Date == DateTime.Today ? watch : null; } }

        // "Hoy: 4.230 pasos, 7 h 10 min de sueño y 500 ml de agua (a las 11:40)."
        public string WatchLine()
        {
            Fitbit.Today w = Watch;
            if (w == null) return null;
            List<string> parts = new List<string> { w.Steps.ToString("N0", Es) + " pasos" };
            if (w.SleepMinutes > 0) parts.Add(Hours(w.SleepMinutes) + " de sue\u00F1o");
            if (w.ActiveMinutes > 0) parts.Add(w.ActiveMinutes + " min activo");
            if (w.WaterMl > 0) parts.Add(w.WaterMl.ToString("N0", Es) + " ml de agua");
            string last = parts[parts.Count - 1];
            parts.RemoveAt(parts.Count - 1);
            return "Hoy: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) + " y " + last : last) + " (a las " + w.ReadAt.ToString("HH:mm") + ").";
        }

        static string Hours(int minutes) { return minutes / 60 + " h" + (minutes % 60 > 0 ? " " + minutes % 60 + " min" : ""); }

        // Read now (asked from the settings) or on the half-hour; done runs here with the error, or null.
        public void ReadWatch(Action<string> done)
        {
            if (watchReading) return;
            watchReading = true;
            nextWatchRead = Anim.Now + WatchEveryMs;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                Fitbit.Today t = null;
                try { t = Fitbit.Read(out error); }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    watchReading = false;
                    if (t != null)
                    {
                        watch = t;
                        settings.FitbitLast = t.Steps + "|" + t.StepsGoal + "|" + t.SleepMinutes + "|" + t.ActiveMinutes + "|" + t.WaterMl + "|" + t.ReadAt.Ticks;
                        settings.Save();
                        WatchSays();
                    }
                    else Log("Fitbit: " + error);
                    if (done != null) done(error);
                });
            });
        }

        Fitbit.Today LastReading()
        {
            string[] p = (settings.FitbitLast ?? "").Split('|');
            long ticks;
            if (p.Length < 6 || !long.TryParse(p[5], out ticks)) return null;
            Fitbit.Today t = new Fitbit.Today { ReadAt = new DateTime(ticks) };
            int.TryParse(p[0], out t.Steps); int.TryParse(p[1], out t.StepsGoal); int.TryParse(p[2], out t.SleepMinutes);
            int.TryParse(p[3], out t.ActiveMinutes); int.TryParse(p[4], out t.WaterMl);
            return t;
        }

        public void ConnectWatch(Action changed)
        {
            Fitbit.Connect(delegate(string error)
            {
                Ui(delegate
                {
                    if (error != null) Notify("No se ha conectado el reloj", error);
                    else
                    {
                        Notify("Reloj conectado", "Stackshot leer\u00E1 tus pasos, tu sue\u00F1o y el agua cada media hora. Los datos no salen de tu equipo.");
                        ReadWatch(delegate { if (changed != null) changed(); });
                    }
                    if (changed != null) changed();
                });
            });
        }

        public void DisconnectWatch()
        {
            Fitbit.Disconnect();
            watch = null;
            settings.FitbitLast = "";
            settings.Save();
            Log("Fitbit: desconectado");
        }

        // A few words about the numbers, once each a day: never in a block, a call, a recording or with nobody there,
        // and only through the mascot (no notices for this).
        void WatchSays()
        {
            Fitbit.Today w = Watch;
            if (!settings.HealthTalk || w == null || pet == null || !pet.CanRemind) return;
            if (Native.IdleMs() >= Wellness.ActiveIdleMs || WellQuiet() || focus.Working) return;
            DateTime now = DateTime.Now;
            // Last night, in the morning.
            if (w.SleepMinutes > 0 && now.Hour >= 8 && now.Hour < 13 && Said("sueno"))
            {
                if (w.SleepMinutes < 360)
                {
                    settings.ShortSleepDay = Today;
                    settings.Save();
                    pet.HealthTell("Esta noche has dormido " + Hours(w.SleepMinutes) + ". Hoy te propondr\u00E9 las pausas para la vista y para moverte un poco antes.", false);
                }
                else if (w.SleepMinutes >= 450) pet.HealthTell("Has dormido " + Hours(w.SleepMinutes) + ". \u00A1Buen d\u00EDa para concentrarse!", false);
                return;
            }
            int goal = w.StepsGoal > 0 ? w.StepsGoal : 10000;
            if (w.Steps >= goal && Said("meta"))
            {
                pet.HealthTell("\u00A1" + w.Steps.ToString("N0", Es) + " pasos! Has cumplido tu objetivo de hoy.", true);
                return;
            }
            // Few steps for the time of day, at most once every three hours between 11:00 and 18:00.
            if (now.Hour >= 11 && now.Hour < 18)
            {
                int expected = goal * (now.Hour - 8) / 12;
                if (w.Steps < expected / 2 && Said("pasos" + now.Hour / 3))
                    pet.HealthAskMove("Llevas " + w.Steps.ToString("N0", Es) + " pasos hoy. \u00BFEstiramos las piernas un par de minutos?");
            }
        }

        // True the first time this is said today (and remembered); false if it was already said.
        bool Said(string key)
        {
            string[] p = (settings.HealthSaid ?? "").Split('|');
            List<string> keys = p.Length == 2 && p[0] == Today ? new List<string>(p[1].Split(',')) : new List<string>();
            if (keys.Contains(key)) return false;
            keys.Add(key);
            settings.HealthSaid = Today + "|" + string.Join(",", keys.ToArray());
            settings.Save();
            return true;
        }

        // The minutes between healthy breaks today: after a short night, eyes and movement come a quarter sooner; with
        // the day's water goal reached, water is no longer reminded.
        int[] WellMinutes()
        {
            int[] m = (int[])Wellness.MinutesOf(settings).Clone();
            if (settings.WaterGoal > 0 && WaterToday() >= settings.WaterGoal) m[WellKind.Water] = 0;
            if (settings.ShortSleepDay == Today)
                foreach (int k in new int[] { WellKind.Eyes, WellKind.Move })
                    if (m[k] > 0) m[k] = Math.Max(15, m[k] * 3 / 4);
            return m;
        }
    }
}

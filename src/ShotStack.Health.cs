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
        GoogleHealth.Today watch;
        bool watchReading;
        double nextWatchRead;
        const double WatchEveryMs = 30 * 60 * 1000;
        static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        static string Today { get { return DateTime.Today.ToString("yyyy-MM-dd"); } }

        void StartHealth()
        {
            if (Test) return;
            watch = LastReading();
            if (watch != null && !watch.Empty && watch.ReadAt.Date == DateTime.Today) Logged(watch);
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
            if (GoogleHealth.Connected && !watchReading && Anim.Now >= nextWatchRead) ReadWatch(null);
            DaySummaryTick();
            WatchOfferTick();
        }

        // ---- Offering to connect a watch: once, a while after starting, by the mascot when it is free (or a notice);
        // "Luego" asks again three days later, "No tengo reloj" never again, and a connected watch is never asked about.

        double startedAt = Anim.Now;

        void WatchOfferTick()
        {
            if (!GoogleHealth.Available || GoogleHealth.Connected || GoogleHealth.Connecting || settings.FitbitAsked == "no") return;
            DateTime put;
            if (settings.FitbitAsked.Length > 0 && DateTime.TryParseExact(settings.FitbitAsked, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out put) && DateTime.Today < put.AddDays(3)) return;
            if (Anim.Now - startedAt < 10 * 60 * 1000) return;
            if (Native.IdleMs() >= Wellness.ActiveIdleMs || WellQuiet() || focus.On) return;
            if (pet != null && !pet.CanRemind) return;
            settings.FitbitAsked = Today;
            settings.Save();
            string text = "\u00BFTienes un Pixel Watch o un Fitbit? Si lo conectas, te acompa\u00F1o con tus pasos, tu actividad y tu sue\u00F1o. Los datos van de Google a tu equipo.";
            if (pet != null) pet.WatchOffer(text, delegate { ConnectWatch(null); }, delegate { settings.FitbitAsked = "no"; settings.Save(); });
            else Notify("\u00BFConectamos tu reloj?", text, NoticeKind.Info, "Conectar", delegate { ConnectWatch(null); });
        }

        // ---- Water

        // Glasses today: the ones done with the mascot or "+1 vaso", or the water logged on the watch if that is more.
        public int WaterToday()
        {
            int app = Wellness.Today(settings)[WellKind.Water];
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
            // Only what the user wants in it (Mascota > Ajustes > Salud y actividad).
            int want = settings.SummaryParts;
            List<string> parts = new List<string>();
            int water = WaterToday();
            if ((want & Settings.SummaryWater) != 0 && (water > 0 || settings.WaterGoal > 0))
                parts.Add(settings.WaterGoal > 0 ? water + " de " + settings.WaterGoal + " vasos de agua" : water + (water == 1 ? " vaso" : " vasos") + " de agua");
            if ((want & Settings.SummaryBreaks) != 0)
            {
                int[] done = Wellness.Today(settings);
                int eyes = done[WellKind.Eyes], move = done[WellKind.Move], breathe = done[WellKind.Breathe];
                if (eyes > 0) parts.Add(eyes + (eyes == 1 ? " descanso" : " descansos") + " de vista");
                if (move > 0) parts.Add(move + (move == 1 ? " pausa" : " pausas") + " para moverte");
                if (breathe > 0) parts.Add(breathe + " " + (breathe == 1 ? "minuto" : "minutos") + " respirando");
            }
            FocusDayCheck();
            if ((want & Settings.SummaryFocus) != 0 && settings.FocusBlocks > 0)
                parts.Add(settings.FocusBlocks + (settings.FocusBlocks == 1 ? " bloque" : " bloques") + " de concentraci\u00F3n (" + FocusClock.Minutes(settings.FocusMinutes) + ")");
            if ((want & Settings.SummarySteps) != 0 && watch != null && watch.ReadAt.Date == DateTime.Today && watch.Steps > 0)
                parts.Add(watch.Steps.ToString("N0", Es) + " pasos");
            StatsDayCheck();
            if ((want & Settings.SummaryCaptures) != 0 && settings.StatsCaptures > 0)
                parts.Add(settings.StatsCaptures + (settings.StatsCaptures == 1 ? " captura" : " capturas"));
            if (parts.Count == 0) return null;
            string last = parts[parts.Count - 1];
            parts.RemoveAt(parts.Count - 1);
            return "Hoy: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) + " y " + last : last) + ".";
        }

        void DaySummaryTick()
        {
            if (settings.SummaryHour <= 0 || DateTime.Now.TimeOfDay < new TimeSpan(settings.SummaryHour, settings.SummaryMinute, 0) || settings.SummaryDay == Today) return;
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

        // Today's reading, with the steps goal chosen in the settings.
        public GoogleHealth.Today Watch
        {
            get
            {
                if (watch == null || watch.ReadAt.Date != DateTime.Today) return null;
                watch.StepsGoal = settings.StepsGoal;
                return watch;
            }
        }

        // Today's steps at each reading (to tell a walk from an hour sitting still); kept while the app runs.
        readonly List<KeyValuePair<DateTime, int>> stepLog = new List<KeyValuePair<DateTime, int>>();

        // "Hoy llevas 4.343 pasos (el 43 % de tu objetivo), anoche dormiste 7 h 10 min y llevas 12 min de actividad intensa."
        public string ActivityLine()
        {
            GoogleHealth.Today w = Watch;
            if (w == null) return WatchError != null ? "No he podido leer tu reloj: " + WatchError : "Todavía no he leído tu reloj hoy.";
            if (w.Empty) return "Google aún no tiene datos de hoy. Abre la app de tu reloj en el móvil para que se sincronice y vuelvo a mirarlo.";
            int goal = w.StepsGoal;
            string steps = w.Steps >= goal ? "¡Hoy ya llevas " + w.Steps.ToString("N0", Es) + " pasos: objetivo cumplido!"
                         : "Hoy llevas " + w.Steps.ToString("N0", Es) + " pasos, el " + w.Steps * 100 / goal + " % de tu objetivo de " + goal.ToString("N0", Es) + ".";
            List<string> more = new List<string>();
            if (w.SleepMinutes > 0) more.Add("anoche dormiste " + Hours(w.SleepMinutes));
            if (w.ActiveMinutes > 0) more.Add("llevas " + w.ActiveMinutes + " min de actividad intensa");
            string extra = more.Count == 0 ? "" : " " + char.ToUpper(more[0][0]) + string.Join(" y ", more.ToArray()).Substring(1) + ".";
            if (w.Steps < goal && DateTime.Now.Hour >= 12) extra += " Te faltan " + (goal - w.Steps).ToString("N0", Es) + ": unos " + WalkMinutes(goal - w.Steps) + " min andando.";
            return steps + extra;
        }

        // About a hundred steps a minute at an easy pace.
        static int WalkMinutes(int steps) { return Math.Max(5, (int)Math.Ceiling(steps / 100.0 / 5) * 5); }

        // "Voy a dar un paseo": the watch is read again in twenty minutes, to tell how it went.
        public void WalkStarted()
        {
            nextWatchRead = Math.Min(nextWatchRead, Anim.Now + 20 * 60 * 1000);
        }

        static string Hours(int minutes) { return minutes / 60 + " h" + (minutes % 60 > 0 ? " " + minutes % 60 + " min" : ""); }

        public bool WatchReading { get { return watchReading; } }

        // Why the last reading failed (null when it worked).
        public string WatchError { get; private set; }

        // A reading has finished, well or not (the windows showing the numbers follow it).
        public event Action WatchRead;

        // Read now (asked from the settings) or on the half-hour; done runs here with the error, or null.
        public void ReadWatch(Action<string> done) { ReadWatch(done, false); }

        // quiet: the mascot says nothing about it on its own (whoever asked tells it).
        public void ReadWatch(Action<string> done, bool quiet)
        {
            if (watchReading) return;
            watchReading = true;
            nextWatchRead = Anim.Now + WatchEveryMs;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                GoogleHealth.Today t = null;
                try { t = GoogleHealth.Read(out error); }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    watchReading = false;
                    WatchError = t != null ? null : error;
                    if (t != null)
                    {
                        if (!t.Empty) Logged(t);
                        watch = t;
                        settings.FitbitLast = t.Steps + "|" + t.StepsGoal + "|" + t.SleepMinutes + "|" + t.ActiveMinutes + "|" + t.WaterMl + "|" + t.ReadAt.Ticks + "|" + (t.Empty ? "vacio" : "");
                        settings.Save();
                        Log(t.Empty ? "Google Health: leído, Google aún no tiene pasos de hoy"
                                    : "Google Health: leído, " + t.Steps + " pasos, " + t.SleepMinutes + " min de sueño, " + t.ActiveMinutes + " min activo");
                        if (!t.Empty) WatchMoved();
                        if (!quiet) WatchSays();
                    }
                    else Log("Google Health: " + error);
                    if (done != null) done(error);
                    Action h = WatchRead;
                    if (h != null) h();
                });
            });
        }

        GoogleHealth.Today LastReading()
        {
            string[] p = (settings.FitbitLast ?? "").Split('|');
            long ticks;
            if (p.Length < 6 || !long.TryParse(p[5], out ticks)) return null;
            GoogleHealth.Today t = new GoogleHealth.Today { ReadAt = new DateTime(ticks) };
            int.TryParse(p[0], out t.Steps); int.TryParse(p[1], out t.StepsGoal); int.TryParse(p[2], out t.SleepMinutes);
            int.TryParse(p[3], out t.ActiveMinutes); int.TryParse(p[4], out t.WaterMl);
            t.Empty = p.Length > 6 && p[6] == "vacio";
            return t;
        }

        public void ConnectWatch(Action changed)
        {
            GoogleHealth.Connect(delegate(string error)
            {
                Ui(delegate
                {
                    if (error == GoogleHealth.Cancelled) { }
                    else if (error != null) Notify("No se ha conectado el reloj", error);
                    else
                    {
                        Notify("Reloj conectado", "Stackshot leer\u00E1 tus pasos, tu actividad y tu sue\u00F1o cada media hora. Los datos van de Google a tu equipo.");
                        ReadWatch(delegate { if (changed != null) changed(); });
                    }
                    if (changed != null) changed();
                });
            });
        }

        public void DisconnectWatch()
        {
            GoogleHealth.Disconnect();
            watch = null;
            settings.FitbitLast = "";
            settings.Save();
            Log("Google Health: desconectado");
        }

        // Keeps today's readings (a new day starts the list over).
        void Logged(GoogleHealth.Today t)
        {
            stepLog.RemoveAll(delegate(KeyValuePair<DateTime, int> e) { return e.Key.Date != t.ReadAt.Date || e.Value > t.Steps; });
            stepLog.Add(new KeyValuePair<DateTime, int>(t.ReadAt, t.Steps));
            if (stepLog.Count > 64) stepLog.RemoveAt(0);
        }

        // Steps since the latest reading at least `minutes` old (and how long ago that was), or -1 without one.
        int StepsSince(double minutes, out double ago)
        {
            ago = 0;
            if (stepLog.Count < 2) return -1;
            KeyValuePair<DateTime, int> last = stepLog[stepLog.Count - 1];
            for (int i = stepLog.Count - 2; i >= 0; i--)
            {
                double m = (last.Key - stepLog[i].Key).TotalMinutes;
                if (m >= minutes) { ago = m; return last.Value - stepLog[i].Value; }
            }
            return -1;
        }

        // Walking since the last reading counts as the "Moverse" break: its timer starts over, so it doesn't ask you to
        // move right after a walk.
        void WatchMoved()
        {
            double ago;
            int walked = StepsSince(1, out ago);
            if (walked >= 250 && ago <= 50) wellness.Done(WellKind.Move, Anim.Now);
        }

        // A few words about the numbers, each once a day (or once every few hours): never in a block, a call, a recording
        // or with nobody there, and only through the mascot (no notices for this). Big news in a card, small in a bubble.
        void WatchSays()
        {
            GoogleHealth.Today w = Watch;
            if (!settings.HealthTalk || w == null || w.Empty || pet == null || !pet.CanRemind) return;
            if (Native.IdleMs() >= Wellness.ActiveIdleMs || WellQuiet() || focus.Working) return;
            DateTime now = DateTime.Now;
            int goal = w.StepsGoal;
            double ago;
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
                else pet.FocusSay("Anoche dormiste " + Hours(w.SleepMinutes) + ". \u00A1A por el d\u00EDa!");
                return;
            }
            // The goal: a card, a cheer and a friendship point.
            if (w.Steps >= goal && Said("meta"))
            {
                string up = LoveUp();
                pet.HealthTell("\u00A1" + w.Steps.ToString("N0", Es) + " pasos! Has cumplido tu objetivo de hoy." + (up != null ? " " + up : ""), true);
                return;
            }
            // A good walk since the last reading.
            int walked = StepsSince(1, out ago);
            if (walked >= 1000 && ago <= 50 && Said("paseo" + now.Hour / 3))
            {
                pet.FocusSay("\u00A1Buen paseo! " + walked.ToString("N0", Es) + " pasos m\u00E1s en " + (int)Math.Round(ago) + " min.");
                return;
            }
            // Halfway there.
            if (w.Steps >= goal / 2 && w.Steps < goal && Said("mitad"))
            {
                pet.FocusSay("\u00A1Ya vas por la mitad! " + w.Steps.ToString("N0", Es) + " de " + goal.ToString("N0", Es) + " pasos.");
                return;
            }
            // An hour sitting still during the working day: a nudge (it stands for the "Moverse" break).
            int still = StepsSince(55, out ago);
            if (now.Hour >= 9 && now.Hour < 19 && still >= 0 && still < 150 && ago <= 120 && Said("quieto" + now.Hour / 2))
            {
                if (settings.WellOn) WellLater(WellKind.Move, 40 * 60 * 1000);
                pet.HealthAskMove("En " + (ago >= 90 ? "la \u00FAltima hora y media" : "la \u00FAltima hora") + " solo has dado " + still + " pasos. \u00BFEstiramos las piernas un par de minutos?");
                return;
            }
            // Late in the day and close to the goal: how far it is, and a walk to get there.
            if (now.Hour >= 16 && now.Hour < 20 && w.Steps >= goal * 6 / 10 && w.Steps < goal && Said("falta"))
            {
                int left = goal - w.Steps;
                pet.HealthAskWalk("Te faltan " + left.ToString("N0", Es) + " pasos para tu objetivo: un paseo de unos " + WalkMinutes(left) + " minutos y lo tienes.");
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

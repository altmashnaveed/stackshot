// Stackshot - Asking the mascot to do things: chips on Home and its page, a menu with every command, and its behavior settings.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Stackshot
{
    public partial class HomeWindow
    {
        // The behavior settings reach the shared mascot (cheap field copies, so it can run every tick).
        void ApplyMascotBehavior()
        {
            mascot.Talks = settings.MascotTalks;
            mascot.Energy = settings.MascotEnergy;
            mascot.Chatter = settings.MascotChatter;
            mascot.Alone = settings.MascotAlone;
            mascot.SleepIdle = settings.MascotSleepIdle;
        }

        // How long between its unprompted tips, by how talkative it is.
        double TipScale { get { return settings.MascotChatter == 0 ? 3.0 : settings.MascotChatter == 2 ? 0.5 : 1.0; } }

        void MascotDo(int cmd)
        {
            if (!settings.MascotOn) return;
            ApplyMascotBehavior();
            mascot.Perform(cmd);
            Wake();
        }

        MacButton CommandButton(int cmd, int height)
        {
            MacButton b = new MacButton(MascotCmd.Names[cmd], ButtonKind.Secondary, MascotCmd.Glyphs[cmd], null, height);
            b.ToolTip = MascotCmd.Hints[cmd];
            int c = cmd;
            b.Click += delegate { MascotDo(c); };
            return b;
        }

        // The usual commands side by side, and a button with all of them.
        FrameworkElement CommandChips(bool vertical)
        {
            Panel row = vertical ? (Panel)new StackPanel() : new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
            foreach (int cmd in MascotCmd.Quick)
            {
                MacButton b = CommandButton(cmd, 28);
                b.Margin = vertical ? new Thickness(0, 0, 0, 7) : new Thickness(4, 4, 4, 0);
                if (vertical) b.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(b);
            }
            MacButton more = new MacButton("M\u00E1s", ButtonKind.Plain, "down", null, 28);
            more.ToolTip = "Todo lo que sabe hacer";
            more.Margin = vertical ? new Thickness(0) : new Thickness(4, 4, 4, 0);
            if (vertical) more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Click += delegate { CommandMenu(more); };
            row.Children.Add(more);
            return row;
        }

        // One small button that opens the menu, for places with no room for chips.
        FrameworkElement CommandPill()
        {
            MacButton b = new MacButton("Juega", ButtonKind.Secondary, "play", null, 28);
            b.ToolTip = "P\u00EDdele algo a " + settings.MascotName;
            b.Click += delegate { CommandMenu(b); };
            return b;
        }

        Popup CommandMenu(FrameworkElement anchor)
        {
            List<MenuEntry> m = new List<MenuEntry>();
            m.Add(MenuEntry.Title("P\u00EDdele algo a " + settings.MascotName));
            for (int i = 0; i < MascotCmd.Count; i++)
            {
                int c = i;
                MenuEntry e = MenuEntry.Item(MascotCmd.Short[c], MascotCmd.Glyphs[c], false, delegate { MascotDo(c); });
                e.Detail = MascotCmd.Hints[c];
                m.Add(e);
                if (c == MascotCmd.Sleep || c == MascotCmd.Love || c == MascotCmd.Tricks - 1) m.Add(MenuEntry.Line());
            }
            return MacMenu.ShowGrid(anchor, null, m, 4);
        }

        // ---- Behavior, in the mascot settings

        void BuildMascotBehavior()
        {
            Func<bool> shown = delegate { return settings.MascotOn; };
            Group("Comportamiento",
                new SegRow("Actividad", "Cada cu\u00E1nto hace cosas por su cuenta.", "hop", Mac.Orange, Mac.Pink, new string[] { "Tranquila", "Normal", "Juguetona" },
                           delegate { return settings.MascotEnergy; }, delegate(int i) { settings.MascotEnergy = i; ApplyMascotBehavior(); mascot.Perform(i == 0 ? MascotCmd.Bow : i == 1 ? MascotCmd.Wave : MascotCmd.Jump); Wake(); owner.MascotChanged(); }) { When = shown },
                new SegRow("Charla", "Cu\u00E1nto comenta lo que hace y te cuenta trucos.", "sparkle", Mac.Blue, Mac.Purple, new string[] { "Poca", "Normal", "Mucha" },
                           delegate { return settings.MascotChatter; }, delegate(int i) { settings.MascotChatter = i; ApplyMascotBehavior(); if (settings.MascotTalks) mascot.Say(MascotTalk.Hello(settings), 3000); Wake(); owner.MascotChanged(); }) { When = shown },
                new ToggleRow("Se duerme sola", "Si no le haces caso un rato, bosteza y se echa una siesta.", "moon", Mac.Indigo, Mac.Purple,
                              delegate { return settings.MascotSleepIdle; }, delegate(bool v) { settings.MascotSleepIdle = v; ApplyMascotBehavior(); owner.MascotChanged(); }) { When = shown },
                new ToggleRow("Recorre todas las pantallas", "En el escritorio pasea de un monitor a otro en vez de quedarse en uno.", "screen", Mac.Teal, Mac.Blue,
                              delegate { return settings.MascotAllScreens; }, delegate(bool v) { settings.MascotAllScreens = v; owner.MascotChanged(); }) { When = shown });
            Group("Lo que hace por su cuenta",
                AloneRow("Saltitos y gestos", "Hops, gui\u00F1os y estiramientos.", "hop", MascotCmd.AloneSmall),
                AloneRow("Bailes y piruetas", "Bailar, girar, voltereta y pompas.", "sound", MascotCmd.AloneDance),
                AloneRow("Juegos con objetos", "Malabares, caf\u00E9, consola, lectura y fotos.", "play", MascotCmd.AloneProps),
                AloneRow("Saludos y miradas", "Saluda, mira alrededor y hace reverencias.", "heart", MascotCmd.AloneLook));
        }

        // ---- Healthy breaks, in the mascot settings

        void BuildWellness()
        {
            Func<bool> on = delegate { return settings.WellOn; };
            Group("Pausas saludables",
                new ToggleRow("Recordarme hacer pausas", "Te avisa para beber agua, descansar la vista, moverte y respirar, y las hace contigo.", "heart", Mac.Pink, Mac.Red,
                              delegate { return settings.WellOn; }, delegate(bool v) { settings.WellOn = v; }),
                WellRow("Beber agua", "Se toma un vaso contigo.", "drop", Mac.Blue, Mac.Teal, new int[] { 0, 30, 45, 60, 90 },
                        delegate { return settings.WellWater; }, delegate(int m) { settings.WellWater = m; }, on),
                WellRow("Descansar la vista", "Regla 20-20-20: mirar algo a 6 metros durante 20 segundos.", "eye", Mac.Green, Mac.Teal, new int[] { 0, 20, 30, 45, 60 },
                        delegate { return settings.WellEyes; }, delegate(int m) { settings.WellEyes = m; }, on),
                WellRow("Moverse", "Sentadillas o estirar la espalda, una vez cada cosa.", "walk", Mac.Orange, Mac.Pink, new int[] { 0, 30, 45, 60, 90 },
                        delegate { return settings.WellMove; }, delegate(int m) { settings.WellMove = m; }, on),
                WellRow("Respirar", "Un minuto de respiraci\u00F3n lenta: 4 segundos dentro y 6 fuera.", "wind", Mac.Indigo, Mac.Purple, new int[] { 0, 60, 90, 120 },
                        delegate { return settings.WellBreathe; }, delegate(int m) { settings.WellBreathe = m; }, on),
                new ToggleRow("Avisar con notificaciones", "Cuando la mascota no est\u00E1 en el escritorio, el aviso llega como una notificaci\u00F3n de Stackshot.", "bell", Mac.Orange, Mac.Red,
                              delegate { return settings.WellToasts; }, delegate(bool v) { settings.WellToasts = v; }) { When = on });
            Note(Wellness.Summary(settings) + " Solo cuenta el tiempo que est\u00E1s delante del ordenador: si te ausentas 5 minutos, la vista y el cuerpo ya han descansado y empieza de cero. " +
                 "Nunca te interrumpe en una llamada, presentando, grabando, a pantalla completa ni en el tiempo de silencio de Windows: espera a que termines. " +
                 "Puedes hacer una pausa cuando quieras desde su men\u00FA en el escritorio (\u00ABUna pausa conmigo\u00BB).");
        }

        // ---- Focus mode (Pomodoro), in the mascot settings

        void BuildFocus()
        {
            FocusClock f = owner.FocusState;
            string line = owner.FocusLine(), today = owner.FocusToday();
            int[] work = { 25, 30, 45, 50 }, shortB = { 5, 10 }, longB = { 15, 20, 30 }, rounds = { 3, 4, 5 };
            Group("Modo concentraci\u00F3n",
                new ButtonRow(f.On ? "Concentraci\u00F3n en marcha" : "Concentrarme ahora",
                              (line ?? "Bloques de trabajo con descansos entre medias (la t\u00E9cnica Pomodoro).") + (today != null ? " " + today + "." : ""),
                              "clock", Mac.Orange, Mac.Red, f.On ? "Terminar" : "Empezar",
                              delegate { if (owner.FocusState.On) owner.FocusStop(); else owner.FocusStart(); Rebuild(); }),
                FocusRow("Cada bloque", "El tiempo de trabajo concentrado.", "clock", Mac.Orange, Mac.Pink, work,
                         delegate { return settings.FocusWork; }, delegate(int m) { settings.FocusWork = m; }),
                FocusRow("Descanso corto", "Entre un bloque y el siguiente.", "coffee", Mac.Green, Mac.Teal, shortB,
                         delegate { return settings.FocusShort; }, delegate(int m) { settings.FocusShort = m; }),
                FocusRow("Descanso largo", "Al completar una ronda de bloques.", "moon", Mac.Indigo, Mac.Purple, longB,
                         delegate { return settings.FocusLong; }, delegate(int m) { settings.FocusLong = m; }),
                new SegRow("Bloques por ronda", "Cu\u00E1ntos bloques hay antes del descanso largo.", "stack", Mac.Blue, Mac.Indigo, new string[] { "3", "4", "5" },
                           delegate { int i = Array.IndexOf(rounds, settings.FocusRounds); return i < 0 ? 1 : i; },
                           delegate(int i) { settings.FocusRounds = rounds[Math.Max(0, Math.Min(rounds.Length - 1, i))]; }),
                new ToggleRow("Seguir solo", "Al acabar un bloque empieza el descanso, y al acabar el descanso el siguiente bloque, sin preguntarte.", "update", Mac.Teal, Mac.Blue,
                              delegate { return settings.FocusAuto; }, delegate(bool v) { settings.FocusAuto = v; }),
                new ToggleRow("Sonido al terminar", "Un aviso suave al acabar cada bloque y cada descanso.", "sound", Mac.Pink, Mac.Red,
                              delegate { return settings.FocusSound; }, delegate(bool v) { settings.FocusSound = v; if (v) FocusChime.Play(false); }));
            Note("Empieza desde aqu\u00ED o desde el men\u00FA de la mascota en el escritorio (\u00ABConcentrarme\u00BB). Mientras dura un bloque, la mascota lee a tu lado sin hablar, " +
                 "lleva el reloj encima de la cabeza y las pausas saludables esperan al descanso, donde te propone la que toque. Si te levantas m\u00E1s de 5 minutos, " +
                 "el reloj se para y al volver te pregunta si sigues. Sin la mascota en el escritorio, todo sigue solo y te avisa con notificaciones.");
        }

        // ---- Health and activity, in the mascot settings

        void BuildHealth()
        {
            int[] goals = { 0, 6, 8, 10 }, hours = { 0, 17, 18, 19 };
            List<Row> rows = new List<Row>();
            int water = owner.WaterToday();
            rows.Add(new SegRow("Objetivo de agua", "Vasos al d\u00EDa. Cuentan los que bebes con la mascota y \u00AB+1 vaso\u00BB en su men\u00FA" +
                                (Fitbit.Available ? ", o el agua que apuntes en Fitbit" : "") + ". Hoy: " + water + (water == 1 ? " vaso." : " vasos."),
                                "drop", Mac.Blue, Mac.Teal, new string[] { "Sin objetivo", "6 vasos", "8 vasos", "10 vasos" },
                                delegate { int i = Array.IndexOf(goals, settings.WaterGoal); return i < 0 ? 2 : i; },
                                delegate(int i) { settings.WaterGoal = goals[Math.Max(0, Math.Min(goals.Length - 1, i))]; }));
            rows.Add(new SegRow("Resumen del d\u00EDa", "A esa hora te cuenta c\u00F3mo ha ido: agua, pausas, concentraci\u00F3n, capturas y pasos.", "heart", Mac.Pink, Mac.Red,
                                new string[] { "No", "17:00", "18:00", "19:00" },
                                delegate { int i = Array.IndexOf(hours, settings.SummaryHour); return i < 0 ? 2 : i; },
                                delegate(int i) { settings.SummaryHour = hours[Math.Max(0, Math.Min(hours.Length - 1, i))]; }));
            if (Fitbit.Available)
            {
                bool on = Fitbit.Connected;
                string line = owner.WatchLine();
                rows.Add(new ButtonRow("Pixel Watch o Fitbit",
                                       Fitbit.Connecting ? "Termina de iniciar sesi\u00F3n en el navegador\u2026"
                                       : on ? "Conectado. " + (line ?? "Leyendo tus datos\u2026") + " Se lee cada media hora."
                                       : "Pasos, sue\u00F1o, minutos activos y agua de tu reloj, para que la mascota te acompa\u00F1e mejor. Inicias sesi\u00F3n en el navegador, una vez.",
                                       "heart", Mac.Teal, Mac.Green, on ? "Desconectar" : "Conectar",
                                       delegate
                                       {
                                           if (Fitbit.Connected) { owner.DisconnectWatch(); Rebuild(); }
                                           else owner.ConnectWatch(delegate { if (IsVisible) Rebuild(); });
                                       }));
                rows.Add(new ToggleRow("Comentarios sobre tu actividad", "Una vez al d\u00EDa como mucho por cada cosa: si has dormido poco, si llevas pocos pasos o si cumples tu objetivo.", "sparkle", Mac.Orange, Mac.Pink,
                                       delegate { return settings.HealthTalk; }, delegate(bool v) { settings.HealthTalk = v; }) { When = delegate { return Fitbit.Connected; } });
            }
            Group("Salud y actividad", rows.ToArray());
            Note("Todo se queda en tu equipo. Con el reloj conectado, los datos van de Fitbit a tu ordenador y no pasan por ning\u00FAn otro sitio; " +
                 "las claves se guardan cifradas con tu usuario de Windows y puedes desconectarlo cuando quieras. La mascota no da consejos m\u00E9dicos: " +
                 "solo te anima a moverte, beber y descansar. Si una noche duermes menos de 6 horas, ese d\u00EDa las pausas para la vista y para moverte llegan un poco antes; " +
                 "y con el objetivo de agua cumplido, deja de record\u00E1rtelo.");
        }

        SegRow FocusRow(string title, string sub, string icon, System.Drawing.Color c1, System.Drawing.Color c2, int[] minutes, Func<int> get, Action<int> set)
        {
            string[] names = new string[minutes.Length];
            for (int i = 0; i < minutes.Length; i++) names[i] = minutes[i] + " min";
            return new SegRow(title, sub, icon, c1, c2, names,
                              delegate { int i = Array.IndexOf(minutes, get()); return i < 0 ? 0 : i; },
                              delegate(int i) { set(minutes[Math.Max(0, Math.Min(minutes.Length - 1, i))]); });
        }

        SegRow WellRow(string title, string sub, string icon, System.Drawing.Color c1, System.Drawing.Color c2, int[] minutes, Func<int> get, Action<int> set, Func<bool> when)
        {
            string[] names = new string[minutes.Length];
            for (int i = 0; i < minutes.Length; i++) names[i] = WellKind.MinuteNames[WellKind.IndexOf(minutes[i])];
            return new SegRow(title, sub, icon, c1, c2, names,
                              delegate { int i = Array.IndexOf(minutes, get()); return i < 0 ? 0 : i; },
                              delegate(int i) { set(minutes[Math.Max(0, Math.Min(minutes.Length - 1, i))]); })
                { When = when };
        }

        ToggleRow AloneRow(string title, string sub, string icon, int bit)
        {
            return new ToggleRow(title, sub, icon, Mac.Teal, Mac.Blue,
                                 delegate { return (settings.MascotAlone & bit) != 0; },
                                 delegate(bool v) { settings.MascotAlone = v ? settings.MascotAlone | bit : settings.MascotAlone & ~bit; ApplyMascotBehavior(); owner.MascotChanged(); })
                { When = delegate { return settings.MascotOn; } };
        }
    }
}

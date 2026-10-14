// Stackshot - "Informar de un problema": a form inside the app that opens a GitHub issue through the report relay
// (tools/report-relay), so nobody needs a GitHub account. Without a relay, the issue opens in the browser, filled in.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Stackshot
{
    public class ReportWindow : Sheet
    {
        static ReportWindow open;
        // Within what the relay accepts (tools/report-relay/worker.js LIMITS), so a report is never refused for size.
        const int MaxTitle = 120, MaxWhat = 6000, MaxSteps = 4000, MaxLog = 20000;
        static bool Relay { get { return !string.IsNullOrEmpty(Program.ReportUrl); } }
        static DateTime lastSent = DateTime.MinValue;

        // What is typed survives a theme change (the window is rebuilt).
        int kind;
        string title = "", what = "", steps = "", status = "";
        bool withLog, showLog, sending, failed, sent;
        int sentNumber;

        // "Mis reportes": the list and, once asked, how each one is doing (fetched once per window, or on Actualizar).
        bool mine, loading;
        Dictionary<int, ReportHistory.State> states;
        string statesError;
        readonly HashSet<int> fresh = new HashSet<int>();   // replies not seen before this window
        ScrollViewer mineScroll;
        // While the list is on screen it asks again every couple of minutes.
        readonly System.Windows.Threading.DispatcherTimer autoRefresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };

        StackPanel page;
        TextBox titleBox, whatBox, stepsBox;
        MacButton send;
        TextBlock statusLabel, missing, titleNeed, whatNeed;

        public static void Open() { Open(false); }

        public static void OpenMine() { Open(true); }

        static void Open(bool mine)
        {
            if (open != null)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                if (mine && !open.mine && !open.sending) open.ShowMine();
                open.Activate();
                return;
            }
            open = new ReportWindow();
            open.Closed += delegate { open = null; };
            if (mine) open.ShowMine();
            open.Show();
            open.Activate();
        }

        ReportWindow()
        {
            Title = "Informar de un problema";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = AppIcon;
            Root.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { if (e.ClickCount == 1 && e.OriginalSource == Root) try { DragMove(); } catch { } };
            Build();
            KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Handled || e.IsRepeat) return;
                if (e.Key == Key.Escape) { e.Handled = true; if (!sending) Close(); }
                else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Send(); }
            };
            Closing += delegate(object o, System.ComponentModel.CancelEventArgs e) { if (sending) e.Cancel = true; };
            autoRefresh.Tick += delegate { if (mine && IsVisible && WindowState != WindowState.Minimized) Refresh(true); };
            autoRefresh.Start();
            Closed += delegate { autoRefresh.Stop(); };
            Loaded += delegate { if (titleBox != null) titleBox.Focus(); };
        }

        protected override void ThemeChanged() { Keep(); Build(); }

        void Keep()
        {
            if (titleBox != null) title = titleBox.Text;
            if (whatBox != null) what = whatBox.Text;
            if (stepsBox != null) steps = stepsBox.Text;
        }

        void Build()
        {
            Palette pal = Ds.Brushes;
            if (page != null) Root.Children.Remove(page);
            Root.Background = HasMica ? Brushes.Transparent : Ds.Brush(pal.Window);
            page = new StackPanel { Margin = new Thickness(30, 30, 30, 26) };
            Root.Children.Add(page);
            KeepCaptionsOnTop();
            Title = mine ? "Mis reportes" : "Informar de un problema";
            if (mine) { BuildMine(pal); return; }
            mineScroll = null;
            if (sent) { BuildSent(pal); return; }
            page.Children.Add(ModeSwitch());

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new IconTile("bell", 40, Ds.Rgb(255, 159, 10), Ds.Rgb(255, 69, 58)));
            head.Children.Add(Text("Cu\u00E9ntanos qu\u00E9 pasa", Ds.Title, 20, pal.Label, new Thickness(14, 0, 0, 0)));
            page.Children.Add(head);
            page.Children.Add(Para((Relay ? "Llega directamente a quien hace Stackshot, sin cuentas ni correos. " : "Al continuar se abre GitHub con todo lo que escribas ya rellenado. ") +
                                   "Se publica en GitHub, que es p\u00FAblico: no pongas datos personales, contrase\u00F1as ni nada de tu empresa.", pal.Label2, new Thickness(0, 14, 0, 0)));

            MacSegmented seg = new MacSegmented(new string[] { "Algo falla", "Tengo una idea" }, kind) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
            seg.Changed += delegate(int i) { Keep(); kind = i; Build(); };
            page.Children.Add(seg);

            page.Children.Add(Caption("T\u00EDtulo", pal, out titleNeed));
            titleBox = Field(title, MaxTitle, false, 0, "T\u00EDtulo");
            page.Children.Add(Frame(titleBox));
            page.Children.Add(Caption(kind == 0 ? "\u00BFQu\u00E9 ha pasado?" : "\u00BFQu\u00E9 te gustar\u00EDa?", pal, out whatNeed));
            whatBox = Field(what, MaxWhat, true, 110, "Descripci\u00F3n");
            page.Children.Add(Frame(whatBox));
            if (kind == 0)
            {
                page.Children.Add(Caption("\u00BFC\u00F3mo se repite? (opcional)", pal));
                stepsBox = Field(steps, MaxSteps, true, 70, "Pasos para repetirlo");
                page.Children.Add(Frame(stepsBox));
            }
            else stepsBox = null;

            // The log, only if you want, with a look at exactly what goes.
            Grid logRow = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            logRow.ColumnDefinitions.Add(new ColumnDefinition());
            logRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel logText = new StackPanel();
            logText.Children.Add(Text("Incluir el registro de Stackshot", Ds.Regular, 13, pal.Label, new Thickness(0)));
            TextBlock look = Text(showLog ? "Ocultar lo que se env\u00EDa" : "Ver lo que se env\u00EDa", Ds.Regular, 12, pal.Link, new Thickness(0, 2, 0, 0));
            look.Cursor = Cursors.Hand;
            look.MouseLeftButtonUp += delegate { Keep(); showLog = !showLog; Build(); };
            logText.Children.Add(look);
            logRow.Children.Add(logText);
            MacSwitch sw = new MacSwitch(withLog) { VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(sw, "Incluir el registro de Stackshot");
            sw.Toggled += delegate(bool v) { withLog = v; };
            Grid.SetColumn(sw, 1);
            logRow.Children.Add(sw);
            page.Children.Add(logRow);
            if (showLog)
            {
                if (cachedLog == null) cachedLog = LogTail();
                TextBox preview = Field(Machine() + "\n\n--- Registro (solo con el interruptor encendido) ---\n" + cachedLog, 100000, true, 140, "Lo que se env\u00EDa");
                preview.IsReadOnly = true;
                preview.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                preview.FontSize = 11;
                Border f = Frame(preview);
                f.Margin = new Thickness(0, 8, 0, 0);
                page.Children.Add(f);
            }
            page.Children.Add(Para("Siempre se a\u00F1ade: " + Machine().Replace("\n", " \u00B7 ") + ". De lo que escribes y del registro se quitan tu usuario, el nombre del equipo, correos, DNI y NIE, cuentas, tarjetas y tel\u00E9fonos.",
                                   pal.Label3, new Thickness(0, 10, 0, 0)));

            statusLabel = Text(status, Ds.Regular, 12, failed ? pal.Error : pal.Label3, new Thickness(0, 14, 0, 0));
            statusLabel.TextWrapping = TextWrapping.Wrap;
            statusLabel.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            page.Children.Add(statusLabel);

            Grid buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
            MacButton cancel = new MacButton("Cancelar", ButtonKind.Secondary, null, null, 32) { HorizontalAlignment = HorizontalAlignment.Left };
            cancel.Click += delegate { if (!sending) Close(); };
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (failed)
            {
                MacButton web = new MacButton("Abrir en GitHub", ButtonKind.Secondary, "open", null, 32) { Margin = new Thickness(0, 0, 8, 0) };
                web.ToolTip = "Abre la incidencia en el navegador con todo lo que has escrito";
                web.Click += delegate { Keep(); OpenInBrowser(); };
                right.Children.Add(web);
            }
            send = new MacButton(!Relay ? "Continuar en GitHub" : sending ? "Enviando\u2026" : failed ? "Reintentar" : "Enviar", ButtonKind.Primary, Relay ? "share" : "open", null, 32);
            send.ToolTip = "Ctrl + Intro";
            send.Click += Send;
            // While the button can't be pressed, it says what is missing.
            missing = Text("", Ds.Regular, 12, pal.Label3, new Thickness(0, 0, 12, 0));
            missing.TextAlignment = TextAlignment.Right;
            missing.TextWrapping = TextWrapping.Wrap;
            missing.MaxWidth = 250;
            right.Children.Add(missing);
            right.Children.Add(send);
            buttons.Children.Add(cancel);
            buttons.Children.Add(right);
            page.Children.Add(buttons);
            titleBox.TextChanged += delegate { Validate(); };
            whatBox.TextChanged += delegate { Validate(); };
            Validate();
        }

        void BuildSent(Palette pal)
        {
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new IconTile("check", 40, Ds.Rgb(52, 199, 89), Ds.Rgb(0, 160, 90)));
            head.Children.Add(Text("\u00A1Enviado! Gracias", Ds.Title, 20, pal.Label, new Thickness(14, 0, 0, 0)));
            page.Children.Add(head);
            page.Children.Add(Para("Tu reporte es el n\u00FAmero " + sentNumber + ". En \u00ABMis reportes\u00BB ver\u00E1s c\u00F3mo avanza y las respuestas, aqu\u00ED mismo y sin cuenta.", pal.Label2, new Thickness(0, 14, 0, 0)));
            Grid buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            MacButton see = new MacButton("Mis reportes", ButtonKind.Secondary, "history", null, 32) { Margin = new Thickness(0, 0, 8, 0) };
            see.Click += delegate { ShowMine(); };
            MacButton close = new MacButton("Cerrar", ButtonKind.Primary, null, null, 32);
            close.Click += delegate { Close(); };
            right.Children.Add(see);
            right.Children.Add(close);
            buttons.Children.Add(right);
            page.Children.Add(buttons);
        }

        // ---- Mis reportes

        FrameworkElement ModeSwitch()
        {
            MacSegmented seg = new MacSegmented(new string[] { "Nuevo reporte", "Mis reportes" }, mine ? 1 : 0) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 22) };
            seg.Changed += delegate(int i)
            {
                if (sending) return;
                Keep();
                if (i == 1) { ShowMine(); return; }
                mine = false;
                // After a report went, "Nuevo reporte" starts a fresh one.
                if (sent) { sent = false; title = what = steps = status = ""; failed = false; }
                Build();
            };
            return seg;
        }

        void ShowMine()
        {
            Keep();
            mine = true;
            if (states == null && !loading) Refresh();
            else Build();
        }

        void Refresh() { Refresh(false); }

        // quiet: the check every few minutes while the list is on screen. It shows nothing while it asks, keeps what is
        // shown if the answer doesn't come, and redraws only when something changed.
        void Refresh(bool quiet)
        {
            if (loading) return;
            List<int> numbers = new List<int>();
            foreach (ReportHistory.Entry e in ReportHistory.Load()) numbers.Add(e.Number);
            if (numbers.Count == 0) { if (!quiet) { states = new Dictionary<int, ReportHistory.State>(); Build(); } return; }
            loading = true;
            if (!quiet) { statesError = null; Build(); }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                Dictionary<int, ReportHistory.State> got = ReportHistory.Fetch(numbers, out error);
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        loading = false;
                        if (error != null)
                        {
                            if (quiet) return;
                            statesError = error;
                        }
                        else
                        {
                            string before = Signature(states);
                            states = got;
                            statesError = null;
                            // Replies that arrived since they were last shown are marked as new for this window.
                            foreach (ReportHistory.Entry e in ReportHistory.Load())
                            {
                                ReportHistory.State s;
                                if (got.TryGetValue(e.Number, out s) && s.Replies.Count > e.Seen) fresh.Add(e.Number);
                            }
                            ReportHistory.Record(got, true);
                            if (quiet && Signature(got) == before) return;
                        }
                        if (!IsDisposed && mine) Build();
                    }));
                }
                catch { }
            });
        }

        static string Signature(Dictionary<int, ReportHistory.State> d)
        {
            if (d == null) return "";
            List<int> keys = new List<int>(d.Keys);
            keys.Sort();
            StringBuilder b = new StringBuilder();
            foreach (int k in keys) b.Append(k).Append(':').Append(ReportHistory.Key(d[k])).Append(':').Append(d[k].Replies.Count).Append(';');
            return b.ToString();
        }

        void BuildMine(Palette pal)
        {
            page.Children.Add(ModeSwitch());
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new IconTile("history", 40, Ds.Rgb(90, 200, 250), Ds.Rgb(0, 122, 255)));
            head.Children.Add(Text("Mis reportes", Ds.Title, 20, pal.Label, new Thickness(14, 0, 0, 0)));
            page.Children.Add(head);
            page.Children.Add(Para("Lo que has enviado desde este equipo, c\u00F3mo va y las respuestas que recibas. No hace falta ninguna cuenta.", pal.Label2, new Thickness(0, 14, 0, 0)));

            List<ReportHistory.Entry> list = ReportHistory.Load();
            if (list.Count == 0)
                page.Children.Add(Para("Todav\u00EDa no has enviado nada desde este equipo. Cuando lo hagas, aparecer\u00E1 aqu\u00ED.", pal.Label3, new Thickness(0, 22, 0, 4)));
            else
            {
                string note = loading ? "Consultando c\u00F3mo van\u2026" : statesError;
                if (note != null)
                {
                    TextBlock t = Para(note, statesError != null && !loading ? pal.Error : pal.Label3, new Thickness(0, 12, 0, 0));
                    page.Children.Add(t);
                }
                StackPanel rows = new StackPanel();
                foreach (ReportHistory.Entry e in list) rows.Children.Add(ReportRow(e, pal));
                ScrollViewer sv = new ScrollViewer { MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows, Margin = new Thickness(0, 16, 0, 0), Resources = HomeWindow.ScrollStyle(), Focusable = false };
                // Redrawn by a refresh: the list stays where it was scrolled to.
                double keep = mineScroll != null ? mineScroll.VerticalOffset : 0;
                if (keep > 0) sv.Loaded += delegate { sv.ScrollToVerticalOffset(keep); };
                mineScroll = sv;
                page.Children.Add(sv);
            }

            Grid buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            if (list.Count > 0)
            {
                MacButton again = new MacButton(loading ? "Actualizando\u2026" : "Actualizar", ButtonKind.Secondary, "update", null, 32) { HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !loading };
                again.Click += delegate { if (!loading) Refresh(); };
                buttons.Children.Add(again);
            }
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            MacButton write = new MacButton("Nuevo reporte", ButtonKind.Secondary, "plus", null, 32) { Margin = new Thickness(0, 0, 8, 0) };
            write.Click += delegate
            {
                mine = false;
                if (sent) { sent = false; title = what = steps = status = ""; failed = false; }
                Build();
            };
            MacButton close = new MacButton("Cerrar", ButtonKind.Primary, null, null, 32);
            close.Click += delegate { Close(); };
            right.Children.Add(write);
            right.Children.Add(close);
            buttons.Children.Add(right);
            page.Children.Add(buttons);
        }

        // One report: what it is, where it stands and, under it, the replies.
        FrameworkElement ReportRow(ReportHistory.Entry e, Palette pal)
        {
            ReportHistory.State s = null;
            if (states != null) states.TryGetValue(e.Number, out s);
            string label;
            Color ink;
            if (s == null) { label = states == null ? "Enviado" : "No disponible"; ink = pal.Label2; }
            else if (s.Open) { label = "Abierto"; ink = pal.High ? pal.Label : pal.Accent; }
            else if (s.Reason == "completed") { label = e.Idea ? "Hecho" : "Arreglado"; ink = pal.High ? pal.Label : pal.Green; }
            else if (s.Reason == "duplicate") { label = "Repetido"; ink = pal.Label2; }
            else if (s.Reason == "not_planned") { label = "Descartado"; ink = pal.Label2; }
            else { label = "Cerrado"; ink = pal.Label2; }

            Border card = new Border
            {
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Ds.Brush(pal.Hairline),
                Background = Ds.Brush(pal.Control), Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel body = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DockPanel name = new DockPanel { LastChildFill = true };
            GlyphView icon = new GlyphView(e.Idea ? "sparkle" : "warning", 15, e.Idea ? pal.Accent : pal.Orange, 1.6) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (pal.High) icon = new GlyphView(e.Idea ? "sparkle" : "warning", 15, pal.Label, 1.6) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(icon);
            TextBlock t = Text(e.Title, Ds.Semibold, 13, pal.Label, new Thickness(0));
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.ToolTip = e.Title;
            name.Children.Add(t);
            top.Children.Add(name);
            StackPanel chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (fresh.Contains(e.Number)) chips.Children.Add(Chip("Respuesta nueva", pal.High ? pal.Label : pal.Accent, pal, new Thickness(0, 0, 6, 0)));
            chips.Children.Add(Chip(label, ink, pal, new Thickness(0)));
            Grid.SetColumn(chips, 1);
            top.Children.Add(chips);
            body.Children.Add(top);
            body.Children.Add(Text("N.\u00BA " + e.Number + "  \u00B7  " + (e.Idea ? "Idea" : "Fallo") + "  \u00B7  enviado el " + e.SentUtc.ToLocalTime().ToString("d/M/yyyy"),
                                   Ds.Regular, 12, pal.Label3, new Thickness(23, 3, 0, 0)));
            if (s != null)
                foreach (ReportHistory.Reply r in s.Replies)
                {
                    Border reply = new Border
                    {
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 9), Margin = new Thickness(23, 10, 0, 0),
                        Background = Ds.Brush(pal.High ? pal.Window : pal.Dark ? Ds.Argb(0.06, 255, 255, 255) : Ds.Argb(0.04, 0, 0, 0)),
                        BorderBrush = Ds.Brush(pal.Hairline), BorderThickness = new Thickness(pal.High ? 1 : 0)
                    };
                    StackPanel rs = new StackPanel();
                    rs.Children.Add(Text((r.Team ? "Respuesta de Stackshot" : "Comentario") + "  \u00B7  " + r.AtUtc.ToLocalTime().ToString("d/M/yyyy HH:mm"),
                                         Ds.Semibold, 11.5, r.Team ? pal.Label : pal.Label2, new Thickness(0)));
                    TextBlock rt = Para(r.Text.Trim(), pal.Label, new Thickness(0, 4, 0, 0));
                    rt.FontSize = 12.5;
                    rs.Children.Add(rt);
                    reply.Child = rs;
                    body.Children.Add(reply);
                }
            card.Child = body;
            return card;
        }

        static Border Chip(string text, Color ink, Palette pal, Thickness m)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = m, VerticalAlignment = VerticalAlignment.Center,
                Background = Ds.Brush(pal.High ? pal.Window : Ds.WithAlpha(ink, pal.Dark ? 0.22 : 0.13)),
                BorderBrush = Ds.Brush(pal.Hairline), BorderThickness = new Thickness(pal.High ? 1 : 0)
            };
            b.Child = Text(text, Ds.Semibold, 11.5, ink, new Thickness(0));
            return b;
        }

        void Validate()
        {
            if (send == null) return;
            int t = titleBox.Text.Trim().Length, w = whatBox.Text.Trim().Length;
            send.IsEnabled = !sending && t >= 4 && w >= 10;
            Need(titleNeed, t, 4);
            Need(whatNeed, w, 10);
            if (missing == null) return;
            string field = kind == 0 ? "\u00AB\u00BFQu\u00E9 ha pasado?\u00BB" : "\u00AB\u00BFQu\u00E9 te gustar\u00EDa?\u00BB";
            missing.Text = sending ? "" : t == 0 && w == 0 ? "Escribe un t\u00EDtulo y " + field + "."
                         : t < 4 ? "El t\u00EDtulo necesita " + Letters(4 - t) + " m\u00E1s."
                         : w < 10 ? field + " necesita " + Letters(10 - w) + " m\u00E1s." : "";
            missing.Visibility = missing.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static string Letters(int n) { return n == 1 ? "1 letra" : n + " letras"; }

        // ---- Sending

        void Send()
        {
            if (mine) return;   // Ctrl+Intro on the list sends nothing
            Keep();
            if (sent || sending || title.Trim().Length < 4 || what.Trim().Length < 10) return;
            if (string.IsNullOrEmpty(Program.ReportUrl)) { OpenInBrowser(); return; }
            if ((DateTime.Now - lastSent).TotalSeconds < 60) { SetStatus("Espera un minuto antes de enviar otro reporte.", true); return; }
            Dictionary<string, object> body = Payload();
            string sentTitle = title.Trim();
            bool idea = kind == 1;
            sending = true;
            failed = false;
            status = "Enviando\u2026";
            Build();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                int number = 0;
                try
                {
                    // The log is read and cleaned here, off the window's thread (or reused if the preview already did).
                    if (body.ContainsKey("log")) body["log"] = cachedLog ?? LogTail();
                    byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
                    HttpWebRequest req = Net.Create(Program.ReportUrl, 20000, 20000);
                    req.Method = "POST";
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    {
                        Dictionary<string, object> r = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(rd.ReadToEnd());
                        object n;
                        if (r != null && r.TryGetValue("number", out n)) number = Convert.ToInt32(n);
                        if (number <= 0) error = "El servidor no ha confirmado el reporte.";
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse r = ex.Response as HttpWebResponse;
                    int code = r != null ? (int)r.StatusCode : 0;
                    error = code == 429 ? "Demasiados reportes seguidos. Prueba dentro de un minuto." :
                            code != 0 ? "No se ha podido enviar (" + code + ")." : "No hay conexi\u00F3n con el servidor de reportes.";
                    if (r != null) r.Close();
                }
                catch (Exception ex) { error = "No se ha podido enviar: " + ex.Message; }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        sending = false;
                        Keep();   // what was typed while it was being sent stays
                        if (error != null)
                        {
                            ShotStack.Log("Reporte: " + error);
                            failed = true;
                            status = error + " Puedes reintentarlo o abrirlo en GitHub.";
                        }
                        else
                        {
                            ShotStack.Log("Reporte enviado: #" + number);
                            ReportHistory.Add(number, idea, sentTitle);
                            lastSent = DateTime.Now;
                            sent = true;
                            sentNumber = number;
                        }
                        if (!IsDisposed) Build();
                    }));
                }
                catch { }
            });
        }

        Dictionary<string, object> Payload()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["kind"] = kind == 1 ? "idea" : "bug";
            d["title"] = Scrub(title.Trim());
            d["what"] = Scrub(what.Trim());
            d["steps"] = kind == 0 ? Scrub(steps.Trim()) : "";
            d["version"] = Installer.MyVersion.ToString(3);
            d["windows"] = WindowsVersion();
            d["screens"] = Screens();
            if (withLog) d["log"] = "";   // filled in on the sending thread
            return d;
        }

        // No relay yet (or it failed): the same report as a new issue in the browser.
        void OpenInBrowser()
        {
            StringBuilder b = new StringBuilder();
            b.Append(Scrub(what.Trim()));
            if (kind == 0 && steps.Trim().Length > 0) b.Append("\n\n### \u00BFC\u00F3mo se repite?\n").Append(Scrub(steps.Trim()));
            b.Append("\n\n---\n").Append(Machine().Replace("\n", " \u00B7 "));
            if (withLog)
            {
                // A browser address has room for the end of the log only.
                string log = cachedLog ?? LogTail();
                if (log.Length > 2500) log = log.Substring(log.Length - 2500);
                b.Append("\n\n<details><summary>Registro</summary>\n\n~~~text\n").Append(log.Replace("~~~", "~ ~ ~")).Append("\n~~~\n</details>");
            }
            // Browsers and GitHub cut long addresses: the text is shortened before escaping, never mid-escape.
            string body = b.ToString(), head = Program.RepoUrl + "/issues/new?title=" + Uri.EscapeDataString(Scrub(title.Trim())) + "&body=";
            string url = head + Uri.EscapeDataString(body);
            while (url.Length > 7000 && body.Length > 200)
            {
                body = body.Substring(0, body.Length * 4 / 5) + "\u2026";
                url = head + Uri.EscapeDataString(body);
            }
            try { Process.Start(url); } catch (Exception ex) { ShotStack.Log("Reporte en el navegador: " + ex.Message); }
        }

        void SetStatus(string text, bool error)
        {
            status = text;
            if (statusLabel == null) return;
            statusLabel.Text = text;
            statusLabel.Foreground = Ds.Brush(error ? Ds.Brushes.Error : Ds.Brushes.Label3);
            statusLabel.Visibility = Visibility.Visible;
        }

        // ---- What the report carries about the computer

        // Worked out once per window (the form rebuilds on every change).
        string machine;
        volatile string cachedLog;
        string Machine()
        {
            if (machine == null) machine = "Stackshot " + Installer.MyVersion.ToString(3) + "\nWindows " + WindowsVersion() + "\n" + Screens();
            return machine;
        }

        static string WindowsVersion()
        {
            Version v = Environment.OSVersion.Version;
            return v.Major + "." + v.Minor + "." + v.Build + (Environment.Is64BitOperatingSystem ? " (64 bits)" : "");
        }

        static string Screens()
        {
            System.Windows.Forms.Screen[] all = System.Windows.Forms.Screen.AllScreens;
            List<string> parts = new List<string>();
            foreach (System.Windows.Forms.Screen s in all)
                parts.Add(s.Bounds.Width + "\u00D7" + s.Bounds.Height + " al " + Math.Round(ShotStack.ScaleFor(s) * 100) + " %");
            return (all.Length == 1 ? "1 pantalla: " : all.Length + " pantallas: ") + string.Join(", ", parts.ToArray());
        }

        // The last lines of the log, without the user name, the computer name, profile paths or e-mail addresses.
        static string LogTail()
        {
            string text;
            bool mid;
            try
            {
                using (FileStream f = new FileStream(Settings.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(f, Encoding.UTF8))
                {
                    mid = f.Length > 64000;
                    if (mid) f.Seek(-64000, SeekOrigin.End);
                    text = r.ReadToEnd();
                }
            }
            catch { return "(sin registro)"; }
            string[] lines = text.Replace("\r", "").Split('\n');
            // Read from the middle of the file, the first line is a partial one.
            int from = Math.Max(mid ? 1 : 0, lines.Length - 150);
            StringBuilder b = new StringBuilder();
            for (int i = from; i < lines.Length; i++) if (lines[i].Length > 0) b.Append(lines[i]).Append('\n');
            string s = Scrub(b.ToString());
            // The relay keeps 20,000 characters: the newest ones, where the problem is.
            return s.Length > MaxLog ? s.Substring(s.Length - MaxLog) : s;
        }

        internal static string Scrub(string s)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) s = Regex.Replace(s, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            // Any other profile folder, also in its short 8.3 form (C:\Users\NOMBRE~1, as in TEMP).
            s = Regex.Replace(s, @"(?i)(\b[A-Z]:\\Users\\)[^\\/\r\n""'<>|:*?]+", "$1<usuario>");
            s = Regex.Replace(s, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "<correo>");
            foreach (string name in new string[] { Environment.UserName, Environment.MachineName, Environment.UserDomainName })
                if (!string.IsNullOrEmpty(name) && name.Length >= 3) s = Regex.Replace(s, @"\b" + Regex.Escape(name) + @"\b", "<oculto>", RegexOptions.IgnoreCase);
            // DNI, NIE, Social Security numbers, IBAN, cards and phone numbers: the same finder as the editor's
            // "Datos sensibles" (if it fails, nothing of the log goes).
            return Sensitive.Scrub(s);
        }

        // ---- Pieces

        static TextBlock Text(string s, Typeface face, double size, Color c, Thickness m)
        {
            return new TextBlock { Text = s, FontFamily = face.FontFamily, FontWeight = face.Weight, FontSize = size, Foreground = Ds.Brush(c), Margin = m, VerticalAlignment = VerticalAlignment.Center };
        }

        static TextBlock Para(string s, Color c, Thickness m)
        {
            TextBlock t = Text(s, Ds.Regular, 12.5, c, m);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 18;
            return t;
        }

        static TextBlock Caption(string s, Palette pal)
        {
            return Text(s, Ds.Semibold, 12.5, pal.Label, new Thickness(0, 16, 0, 6));
        }

        // A caption with the field's minimum on its right ("mínimo 10 letras"), which Validate ticks once it is reached.
        static FrameworkElement Caption(string s, Palette pal, out TextBlock need)
        {
            Grid g = new Grid { Margin = new Thickness(0, 16, 0, 6) };
            g.Children.Add(Text(s, Ds.Semibold, 12.5, pal.Label, new Thickness(0)));
            need = Text("", Ds.Regular, 12, pal.Label3, new Thickness(0));
            need.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(need);
            return g;
        }

        static void Need(TextBlock t, int have, int min)
        {
            if (t == null) return;
            Palette pal = Ds.Brushes;
            bool ok = have >= min;
            t.Text = "m\u00EDnimo " + min + " letras" + (ok ? "  \u2713" : have > 0 ? "  \u00B7  " + have + "/" + min : "");
            t.Foreground = Ds.Brush(ok ? (pal.High ? pal.Label : pal.Green) : pal.Label3);
        }

        static TextBox Field(string text, int max, bool multi, double height, string name)
        {
            Palette pal = Ds.Brushes;
            TextBox b = new TextBox
            {
                Text = text, MaxLength = max, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(10, multi ? 7 : 0, 10, multi ? 7 : 1), FontFamily = Ds.Text, FontSize = 13,
                Foreground = Ds.Brush(pal.Label), CaretBrush = Ds.Brush(pal.Accent), SelectionBrush = Ds.Brush(pal.Accent),
                AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center
            };
            if (multi) { b.Height = height; b.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        static Border Frame(TextBox box)
        {
            Palette pal = Ds.Brushes;
            Border f = new Border
            {
                MinHeight = 34, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.IBeam,
                BorderBrush = Ds.Brush(pal.Hairline), Background = Ds.Brush(pal.Control), Child = box
            };
            box.GotKeyboardFocus += delegate { f.BorderBrush = Ds.Brush(Ds.WithAlpha(Ds.Brushes.Accent, 0.85)); };
            box.LostKeyboardFocus += delegate { f.BorderBrush = Ds.Brush(Ds.Brushes.Hairline); };
            f.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { if (!box.IsKeyboardFocusWithin) { box.Focus(); box.CaretIndex = box.Text.Length; e.Handled = true; } };
            return f;
        }
    }
}

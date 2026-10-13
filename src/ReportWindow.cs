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
        string sentUrl;

        StackPanel page;
        TextBox titleBox, whatBox, stepsBox;
        MacButton send;
        TextBlock statusLabel;

        public static void Open()
        {
            if (open != null)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            open = new ReportWindow();
            open.Closed += delegate { open = null; };
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
            if (sent) { BuildSent(pal); return; }

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new IconTile("bell", 40, Ds.Rgb(255, 159, 10), Ds.Rgb(255, 69, 58)));
            head.Children.Add(Text("Cu\u00E9ntanos qu\u00E9 pasa", Ds.Title, 20, pal.Label, new Thickness(14, 0, 0, 0)));
            page.Children.Add(head);
            page.Children.Add(Para((Relay ? "Llega directamente a quien hace Stackshot, sin cuentas ni correos. " : "Al continuar se abre GitHub con todo lo que escribas ya rellenado. ") +
                                   "Se publica en GitHub, que es p\u00FAblico: no pongas datos personales, contrase\u00F1as ni nada de tu empresa.", pal.Label2, new Thickness(0, 14, 0, 0)));

            MacSegmented seg = new MacSegmented(new string[] { "Algo falla", "Tengo una idea" }, kind) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
            seg.Changed += delegate(int i) { Keep(); kind = i; Build(); };
            page.Children.Add(seg);

            page.Children.Add(Caption("T\u00EDtulo", pal));
            titleBox = Field(title, MaxTitle, false, 0, "T\u00EDtulo");
            page.Children.Add(Frame(titleBox));
            page.Children.Add(Caption(kind == 0 ? "\u00BFQu\u00E9 ha pasado?" : "\u00BFQu\u00E9 te gustar\u00EDa?", pal));
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
            page.Children.Add(Para("Siempre se a\u00F1ade: " + Machine().Replace("\n", " \u00B7 ") + ". Del registro se quitan tu usuario, el nombre del equipo, correos, DNI y NIE, cuentas, tarjetas y tel\u00E9fonos.",
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
            page.Children.Add(Para("Tu reporte es el n\u00FAmero " + sentNumber + ". Puedes ver c\u00F3mo avanza en GitHub, sin necesidad de cuenta.", pal.Label2, new Thickness(0, 14, 0, 0)));
            Grid buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            MacButton see = new MacButton("Ver el reporte", ButtonKind.Secondary, "open", null, 32) { Margin = new Thickness(0, 0, 8, 0) };
            see.Click += delegate { try { if (sentUrl != null && sentUrl.StartsWith(Program.RepoUrl + "/issues/", StringComparison.Ordinal)) Process.Start(sentUrl); } catch { } };
            MacButton close = new MacButton("Cerrar", ButtonKind.Primary, null, null, 32);
            close.Click += delegate { Close(); };
            right.Children.Add(see);
            right.Children.Add(close);
            buttons.Children.Add(right);
            page.Children.Add(buttons);
        }

        void Validate()
        {
            if (send == null) return;
            send.IsEnabled = !sending && titleBox.Text.Trim().Length >= 4 && whatBox.Text.Trim().Length >= 10;
        }

        // ---- Sending

        void Send()
        {
            Keep();
            if (sent || sending || title.Trim().Length < 4 || what.Trim().Length < 10) return;
            if (string.IsNullOrEmpty(Program.ReportUrl)) { OpenInBrowser(); return; }
            if ((DateTime.Now - lastSent).TotalSeconds < 60) { SetStatus("Espera un minuto antes de enviar otro reporte.", true); return; }
            Dictionary<string, object> body = Payload();
            sending = true;
            failed = false;
            status = "Enviando\u2026";
            Build();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null, url = null;
                int number = 0;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    // The log is read and cleaned here, off the window's thread (or reused if the preview already did).
                    if (body.ContainsKey("log")) body["log"] = cachedLog ?? LogTail();
                    byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Program.ReportUrl);
                    req.Method = "POST";
                    req.ContentType = "application/json; charset=utf-8";
                    req.UserAgent = "Stackshot/" + Installer.MyVersion.ToString(3);
                    req.Timeout = 20000;
                    req.ReadWriteTimeout = 20000;
                    if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials; // corporate proxies
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    {
                        Dictionary<string, object> r = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(rd.ReadToEnd());
                        object n, u;
                        if (r != null && r.TryGetValue("number", out n)) number = Convert.ToInt32(n);
                        if (r != null && r.TryGetValue("url", out u)) url = u as string;
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
                            lastSent = DateTime.Now;
                            sent = true;
                            sentNumber = number;
                            sentUrl = url;
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
            d["title"] = title.Trim();
            d["what"] = what.Trim();
            d["steps"] = kind == 0 ? steps.Trim() : "";
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
            b.Append(what.Trim());
            if (kind == 0 && steps.Trim().Length > 0) b.Append("\n\n### \u00BFC\u00F3mo se repite?\n").Append(steps.Trim());
            b.Append("\n\n---\n").Append(Machine().Replace("\n", " \u00B7 "));
            if (withLog)
            {
                // A browser address has room for the end of the log only.
                string log = cachedLog ?? LogTail();
                if (log.Length > 2500) log = log.Substring(log.Length - 2500);
                b.Append("\n\n<details><summary>Registro</summary>\n\n~~~text\n").Append(log.Replace("~~~", "~ ~ ~")).Append("\n~~~\n</details>");
            }
            // Browsers and GitHub cut long addresses: the text is shortened before escaping, never mid-escape.
            string body = b.ToString(), head = Program.RepoUrl + "/issues/new?title=" + Uri.EscapeDataString(title.Trim()) + "&body=";
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

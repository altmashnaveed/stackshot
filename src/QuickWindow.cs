// Stackshot - The small window the mascot opens to write something down: a reminder (what and when, with the ones
// still pending below) or a note for Notas.md. Enter saves (Ctrl+Enter in a note of several lines), Esc closes.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Stackshot
{
    public class QuickWindow : Sheet
    {
        static QuickWindow open;
        readonly ShotStack owner;
        readonly bool note;
        string text = "", at = "";
        int when = 1;                 // 10 min, 30 min, 1 h, 2 h, tomorrow 9:00, at a time
        static readonly int[] Minutes = { 10, 30, 60, 120 };
        StackPanel page;
        TextBox box, time;
        TextBlock error;

        public static void Remind(ShotStack owner) { Show(owner, false); }
        public static void Note(ShotStack owner) { Show(owner, true); }

        static void Show(ShotStack owner, bool note)
        {
            if (open != null && open.note != note) open.Close();
            if (open != null) { open.Activate(); return; }
            open = new QuickWindow(owner, note);
            open.Closed += delegate { open = null; };
            open.Show();
            open.Activate();
        }

        QuickWindow(ShotStack owner, bool note)
        {
            this.owner = owner;
            this.note = note;
            Title = note ? "Apuntar" : "Recordatorio";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Icon = AppIcon;
            at = DateTime.Now.AddHours(1).ToString("H:00");
            DragAnywhere();
            KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Handled) return;
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.Enter && (!note || Keyboard.Modifiers == ModifierKeys.Control)) { e.Handled = true; Save(); }
            };
            Build();
            Loaded += delegate { if (box != null) { box.Focus(); box.CaretIndex = box.Text.Length; } };
        }

        protected override void ThemeChanged() { Keep(); Build(); }

        void Keep()
        {
            if (box != null) text = box.Text;
            if (time != null) at = time.Text;
        }

        void Build()
        {
            Palette pal = Ds.Brushes;
            if (page != null) Root.Children.Remove(page);
            Root.Background = HasMica ? Brushes.Transparent : Ds.Brush(pal.Window);
            page = new StackPanel { Margin = new Thickness(26, 26, 26, 22) };
            Root.Children.Add(page);
            KeepCaptionsOnTop();

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(note ? new IconTile("memo", 36, Ds.Rgb(255, 214, 10), Ds.Rgb(255, 159, 10)) : new IconTile("clock", 36, Ds.Rgb(90, 200, 250), Ds.Rgb(0, 122, 255)));
            head.Children.Add(Text(note ? "\u00BFQu\u00E9 apunto?" : "\u00BFQu\u00E9 te recuerdo?", Ds.Title, 18, pal.Label, new Thickness(12, 0, 0, 0)));
            page.Children.Add(head);

            string hint = note ? "Lo que quieras apuntar" : "Por ejemplo: llamar a Laura";
            box = Field(text, note, note ? 90 : 0, hint);
            Border frame = Frame(box, hint);
            frame.Margin = new Thickness(0, 16, 0, 0);
            page.Children.Add(frame);

            if (!note)
            {
                page.Children.Add(Text("\u00BFCu\u00E1ndo?", Ds.Semibold, 12.5, pal.Label, new Thickness(0, 16, 0, 6)));
                // Short labels across the whole width; the line below says exactly when.
                MacSegmented seg = new MacSegmented(new string[] { "10 min", "30 min", "1 h", "2 h", "Ma\u00F1ana", "A las\u2026" }, when);
                seg.ClearValue(WidthProperty);
                seg.HorizontalAlignment = HorizontalAlignment.Stretch;
                seg.Changed += delegate(int i) { Keep(); when = i; Build(); if (i == 5 && time != null) { time.Focus(); time.SelectAll(); } };
                page.Children.Add(seg);
                if (when < 5) page.Children.Add(Text("Te lo recuerdo " + ShotStack.WhenText(Preset()) + ".", Ds.Regular, 12, pal.Label2, new Thickness(2, 8, 0, 0)));
                if (when == 5)
                {
                    StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                    row.Children.Add(Text("Hora", Ds.Regular, 13, pal.Label2, new Thickness(0, 0, 10, 0)));
                    time = Field(at, false, 0, "Hora");
                    time.Width = 70;
                    row.Children.Add(Frame(time, null));
                    row.Children.Add(Text("(por ejemplo 16:30)", Ds.Regular, 12, pal.Label3, new Thickness(10, 0, 0, 0)));
                    page.Children.Add(row);
                }
                else time = null;

                // The ones still pending, each with its cross.
                List<Reminder> list = owner.Reminders;
                if (list.Count > 0)
                {
                    page.Children.Add(Text("Pendientes", Ds.Semibold, 12.5, pal.Label, new Thickness(0, 18, 0, 4)));
                    foreach (Reminder r in list)
                    {
                        Reminder it = r;
                        Grid g = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                        g.ColumnDefinitions.Add(new ColumnDefinition());
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        TextBlock t = Text(ShotStack.WhenText(r.DueUtc.ToLocalTime()) + "  \u00B7  " + r.Text, Ds.Regular, 12.5, pal.Label2, new Thickness(0));
                        t.TextTrimming = TextTrimming.CharacterEllipsis;
                        g.Children.Add(t);
                        TapBorder x = new TapBorder { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand, Label = "Quitar este recordatorio", ToolTip = "Quitar" };
                        x.Child = new GlyphView("close", 11, pal.Label2, 1.8) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        Action drop = delegate { Keep(); owner.RemoveReminder(it); Build(); };
                        x.MouseLeftButtonUp += delegate { drop(); };
                        Access.Clickable(x, drop);
                        Grid.SetColumn(x, 1);
                        g.Children.Add(x);
                        page.Children.Add(g);
                    }
                }
            }
            else
            {
                // The latest ones at a glance, each to tick off or remove; all of them in Notas.md.
                List<ShotStack.Note> last = owner.LastNotes(5);
                if (last.Count > 0)
                {
                    page.Children.Add(Text("\u00DAltimas notas", Ds.Semibold, 12.5, pal.Label, new Thickness(0, 18, 0, 4)));
                    foreach (ShotStack.Note n in last)
                    {
                        ShotStack.Note it = n;
                        DockPanel row = new DockPanel { Margin = new Thickness(0, 2, 0, 0), MinHeight = 26 };
                        // The tick: a ring, or a green disc with a check once done.
                        TapBorder tick = new TapBorder
                        {
                            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0),
                            Background = n.Done ? Ds.Brush(pal.Green) : Brushes.Transparent, BorderThickness = new Thickness(n.Done ? 0 : 1.5),
                            BorderBrush = Ds.Brush(pal.Label3), Label = n.Done ? "Marcar como pendiente" : "Marcar como hecha",
                            ToolTip = n.Done ? "Marcar como pendiente" : "Marcar como hecha", VerticalAlignment = VerticalAlignment.Center
                        };
                        if (n.Done) tick.Child = new GlyphView("check", 11, pal.High ? pal.Window : Colors.White, 2) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        Action flip = delegate { Keep(); if (!owner.NoteDone(it, !it.Done)) Say("Esa nota ha cambiado en Notas.md: \u00E1brela para verla."); else Build(); };
                        tick.MouseLeftButtonUp += delegate { flip(); };
                        Access.Clickable(tick, flip);
                        DockPanel.SetDock(tick, Dock.Left);
                        row.Children.Add(tick);
                        TapBorder x = new TapBorder { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent, Cursor = Cursors.Hand, Label = "Borrar esta nota", ToolTip = "Borrar", Margin = new Thickness(8, 0, 0, 0) };
                        x.Child = new GlyphView("close", 11, pal.Label2, 1.8) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        Action drop = delegate { Keep(); if (!owner.DeleteNote(it)) Say("Esa nota ha cambiado en Notas.md: \u00E1brela para verla."); else Build(); };
                        x.MouseLeftButtonUp += delegate { drop(); };
                        Access.Clickable(x, drop);
                        DockPanel.SetDock(x, Dock.Right);
                        row.Children.Add(x);
                        TextBlock when = Text(n.When, Ds.Regular, 12, pal.Label3, new Thickness(0, 0, 10, 0));
                        DockPanel.SetDock(when, Dock.Left);
                        row.Children.Add(when);
                        TextBlock what = Text(n.Text, Ds.Regular, 12.5, n.Done ? pal.Label3 : pal.Label2, new Thickness(0));
                        what.TextTrimming = TextTrimming.CharacterEllipsis;
                        what.ToolTip = n.Text;
                        if (n.Done) what.TextDecorations = TextDecorations.Strikethrough;
                        row.Children.Add(what);
                        page.Children.Add(row);
                    }
                }
                TextBlock see = Text(last.Count > 0 ? "Ver todas (Notas.md, en tu carpeta de capturas)" : "Ver mis notas (Notas.md, en tu carpeta de capturas)", Ds.Regular, 12.5, pal.Link, new Thickness(0, 10, 0, 0));
                see.Cursor = Cursors.Hand;
                see.HorizontalAlignment = HorizontalAlignment.Left;
                see.MouseLeftButtonUp += delegate { owner.OpenNotes(); };
                page.Children.Add(see);
            }

            error = Text("", Ds.Regular, 12, pal.Error, new Thickness(0, 10, 0, 0));
            error.Visibility = Visibility.Collapsed;
            page.Children.Add(error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            MacButton cancel = new MacButton("Cancelar", ButtonKind.Secondary, null, null, 32) { Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            MacButton save = new MacButton(note ? "Apuntar" : "Recu\u00E9rdamelo", ButtonKind.Primary, note ? "check" : "clock", note ? "Ctrl+Intro" : null, 32);
            save.Click += Save;
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            page.Children.Add(buttons);
        }

        void Save()
        {
            Keep();
            if (text.Trim().Length == 0) { Say(note ? "Escribe lo que quieres apuntar." : "Escribe qu\u00E9 te recuerdo."); return; }
            if (note)
            {
                if (owner.AddNote(text)) { owner.Told("Apuntado en tus notas."); Close(); }
                return;
            }
            DateTime due;
            if (when < 5) due = Preset();
            else
            {
                DateTime t;
                if (!DateTime.TryParseExact(at.Trim().Replace('.', ':'), new string[] { "H:mm", "HH:mm", "H" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                { Say("Escribe la hora como 16:30."); return; }
                due = DateTime.Today.Add(t.TimeOfDay);
                // A time already gone today means tomorrow.
                if (due <= DateTime.Now) due = due.AddDays(1);
            }
            owner.AddReminder(text, due, false);
            Close();
        }

        // The moment a preset choice means (every one but "A las…").
        DateTime Preset() { return when < 4 ? DateTime.Now.AddMinutes(Minutes[when]) : DateTime.Today.AddDays(1).AddHours(9); }

        void Say(string s)
        {
            if (error == null) return;
            error.Text = s;
            error.Visibility = Visibility.Visible;
        }

        internal static TextBlock Text(string s, Typeface face, double size, Color c, Thickness m)
        {
            return new TextBlock { Text = s, FontFamily = face.FontFamily, FontWeight = face.Weight, FontSize = size, Foreground = Ds.Brush(c), Margin = m, VerticalAlignment = VerticalAlignment.Center };
        }

        internal static TextBox Field(string text, bool multi, double height, string name)
        {
            Palette pal = Ds.Brushes;
            TextBox b = new TextBox
            {
                Text = text, MaxLength = multi ? 4000 : 200, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Margin = new Thickness(10, multi ? 7 : 0, 10, multi ? 7 : 1), FontFamily = Ds.Text, FontSize = 13.5,
                Foreground = Ds.Brush(pal.Label), CaretBrush = Ds.Brush(pal.Accent), SelectionBrush = Ds.Brush(pal.Accent),
                AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center
            };
            if (multi) { b.Height = height; b.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            return b;
        }

        // The field's box; hint: grey text shown while it is empty (null: none).
        internal static Border Frame(TextBox box, string hint)
        {
            Palette pal = Ds.Brushes;
            Grid inside = new Grid();
            if (hint != null)
            {
                TextBlock h = Text(hint, Ds.Regular, 13.5, pal.Label3, new Thickness(box.Margin.Left + 2, box.Margin.Top, box.Margin.Right, box.Margin.Bottom));
                h.VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center;
                h.IsHitTestVisible = false;
                h.TextTrimming = TextTrimming.CharacterEllipsis;
                Action show = delegate { h.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; };
                box.TextChanged += delegate { show(); };
                show();
                inside.Children.Add(h);
            }
            inside.Children.Add(box);
            Border f = new Border
            {
                MinHeight = 34, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.IBeam,
                BorderBrush = Ds.Brush(pal.Hairline), Background = Ds.Brush(pal.Control), Child = inside
            };
            box.GotKeyboardFocus += delegate { f.BorderBrush = Ds.Brush(Ds.WithAlpha(Ds.Brushes.Accent, 0.85)); };
            box.LostKeyboardFocus += delegate { f.BorderBrush = Ds.Brush(Ds.Brushes.Hairline); };
            // A click on the padding around the text goes to the field too.
            f.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { if (!box.IsMouseOver) { box.Focus(); box.CaretIndex = box.Text.Length; e.Handled = true; } };
            return f;
        }
    }
}

// Stackshot - Comparing the open XML with the one before it in its folder, or with any other file: computed off the UI
// thread, shown in place of the tree (read-only) with a bar to filter, step through the differences and leave.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stackshot
{
    public sealed partial class MarkdownWindow
    {
        // Tests answer the file picker of "Comparar con otro archivo..." here (null cancels).
        internal static Func<string> PickCompare { get; set; }

        sealed class Comparison
        {
            public string Text;         // the document text it was made from
            public DiffResult Result;
            public XmlDiffView View;
            public Palette Pal;
            public string OldPath;
            public bool OnlyDiffs = true;
        }

        Comparison compare;
        int compareToken;
        bool comparing;
        TextBlock comparePos;
        DocIconButton compareUp, compareDown;

        internal XmlDiffView CompareView { get { return compare != null ? compare.View : null; } }
        internal bool Comparing { get { return compare != null; } }
        internal bool CompareBusy { get { return comparing; } }

        // A comparison belongs to the text it was made from, in Vista: editing, another document or a reload ends it.
        void DropStaleCompare()
        {
            if (compare != null && (!xml || editing || text == null || compare.Text != text)) compare = null;
        }

        bool CompareShown()
        {
            DropStaleCompare();
            return compare != null && compare.View != null;
        }

        // Once the tree is in the preview: the comparison takes its place while it lasts (redrawn for a new theme).
        void ShowCompareView()
        {
            if (!CompareShown() || previewHost == null) return;
            if (compare.Pal != Ds.Brushes) NewCompareView();
            // Still the child of the preview host of the page this one replaced.
            Border was = compare.View.Parent as Border;
            if (was != null && was != previewHost) was.Child = null;
            previewHost.Child = compare.View;
        }

        void NewCompareView()
        {
            DiffNode keep = compare.View != null ? compare.View.SelectedNode : null;
            XmlDiffView v = new XmlDiffView(Ds.Brushes, compare.Result, compare.OnlyDiffs);
            v.Moved += UpdateComparePos;
            v.Menu += ShowMenu;
            v.CopyRequest += delegate(string t, string what)
            {
                if (Retry(delegate { Clipboard.SetText(t); })) Say(what, false);
                else Say("No he podido usar el portapapeles.", true);
            };
            compare.View = v;
            compare.Pal = Ds.Brushes;
            if (keep != null) v.Reveal(keep);
        }

        // "3 cambios · 1 añadido · 2 quitados" (only what there is), for the status line.
        internal string CompareStatus()
        {
            if (compare == null) return "";
            DiffResult r = compare.Result;
            if (r.Total == 0) return "Sin diferencias";
            List<string> parts = new List<string>();
            if (r.Changed > 0) parts.Add(r.Changed.ToString("N0") + (r.Changed == 1 ? " cambio" : " cambios"));
            if (r.Added > 0) parts.Add(r.Added.ToString("N0") + (r.Added == 1 ? " a\u00F1adido" : " a\u00F1adidos"));
            if (r.Removed > 0) parts.Add(r.Removed.ToString("N0") + (r.Removed == 1 ? " quitado" : " quitados"));
            return string.Join(" \u00B7 ", parts.ToArray());
        }

        FrameworkElement CompareBar()
        {
            Palette pal = Ds.Brushes;
            Border b = new Border { Background = Ds.Brush(pal.High ? pal.Window : Ds.WithAlpha(pal.Accent, pal.Dark ? 0.12 : 0.06)), BorderBrush = Ds.Brush(pal.High ? pal.Hairline : Ds.WithAlpha(pal.Accent, 0.28)), BorderThickness = new Thickness(0, Soap() != null ? 0 : 1, 0, 1), Padding = new Thickness(18, 4, 12, 4) };
            Grid g = new Grid();
            for (int i = 0; i < 6; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            g.Children.Add(new TextBlock { Text = "Comparaci\u00F3n", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ds.Brush(pal.Label), VerticalAlignment = VerticalAlignment.Center });

            string oldName = System.IO.Path.GetFileName(compare.OldPath), newName = name ?? "Sin t\u00EDtulo";
            TextBlock files = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 10, 0) };
            files.Inlines.Add(new Run("antes ") { Foreground = Ds.Brush(pal.Label2) });
            files.Inlines.Add(new Run(oldName) { Foreground = Ds.Brush(pal.Label) });
            files.Inlines.Add(new Run("   ahora ") { Foreground = Ds.Brush(pal.Label2) });
            files.Inlines.Add(new Run(newName) { Foreground = Ds.Brush(pal.Label) });
            files.ToolTip = "Antes: " + compare.OldPath + "\nAhora: " + (file != null && file.Path != null ? file.Path : newName);
            Grid.SetColumn(files, 1);
            g.Children.Add(files);

            ChipToggle only = new ChipToggle("Solo diferencias", "Mostrar solo lo que cambia (y lo que lleva hasta ello)", false) { IsOn = compare.OnlyDiffs, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            only.Toggled += delegate(bool on)
            {
                if (compare == null) return;
                compare.OnlyDiffs = on;
                if (compare.View != null) compare.View.OnlyDiffs = on;
            };
            Grid.SetColumn(only, 2);
            g.Children.Add(only);

            comparePos = new TextBlock { FontSize = 12.5, Foreground = Ds.Brush(pal.Label2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(comparePos, 3);
            g.Children.Add(comparePos);

            StackPanel nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            compareUp = new DocIconButton("up", "Diferencia anterior (May\u00FAs+F7)", 26);
            compareUp.Click += delegate { StepCompare(-1); };
            compareDown = new DocIconButton("down", "Siguiente diferencia (F7)", 26) { Margin = new Thickness(2, 0, 0, 0) };
            compareDown.Click += delegate { StepCompare(1); };
            nav.Children.Add(compareUp);
            nav.Children.Add(compareDown);
            Grid.SetColumn(nav, 4);
            g.Children.Add(nav);

            MacButton exit = new MacButton("Salir de la comparaci\u00F3n", ButtonKind.Secondary, "close", null, 26) { VerticalAlignment = VerticalAlignment.Center };
            exit.ToolTip = "Salir de la comparaci\u00F3n (Esc)";
            exit.Click += ExitCompare;
            Grid.SetColumn(exit, 5);
            g.Children.Add(exit);
            b.Child = g;
            UpdateComparePos();
            return b;
        }

        void UpdateComparePos()
        {
            if (compare == null || comparePos == null) return;
            int total = compare.Result.Stops.Count, cur = compare.View != null ? compare.View.Current : -1;
            comparePos.Text = cur >= 0 ? (cur + 1).ToString("N0") + " de " + total.ToString("N0") : total == 0 ? "Sin diferencias" : total.ToString("N0") + (total == 1 ? " diferencia" : " diferencias");
            if (compareUp != null) compareUp.IsEnabled = total > 0;
            if (compareDown != null) compareDown.IsEnabled = total > 0;
        }

        void StepCompare(int dir)
        {
            if (!CompareShown()) return;
            int r = compare.View.Step(dir);
            if (r == 0) Say("No hay diferencias.", false);
            else if (r == 2) Say(dir > 0 ? "De vuelta en la primera diferencia." : "De vuelta en la \u00FAltima diferencia.", false);
        }

        // F7 / Shift+F7 step through the differences; Esc leaves the comparison (the find bar and text fields first).
        bool CompareKey(Key key, bool ctrl, bool shift, bool alt)
        {
            if (compare == null || !CompareShown()) return false;
            if (key == Key.F7 && !ctrl && !alt) { StepCompare(shift ? -1 : 1); return true; }
            if (key == Key.Escape && findBar == null && !(Keyboard.FocusedElement is TextBox)) { ExitCompare(); return true; }
            return false;
        }

        // Más opciones for an XML: the comparisons and the JSON copy.
        void XmlExtraEntries(List<MenuEntry> m, bool onDisk)
        {
            bool ready = xml && Current() != null && model != null && parsedText == text;
            m.Add(MenuEntry.Line());
            MenuEntry prev = MenuEntry.Item("Comparar con el XML anterior de la carpeta", "doc", false, delegate { CompareWith(null); });
            prev.Enabled = ready && onDisk && (!neighboursKnown || hasPrev);
            m.Add(prev);
            MenuEntry other = MenuEntry.Item("Comparar con otro archivo\u2026", "doc", false, delegate { CompareOther(); });
            other.Enabled = ready;
            m.Add(other);
            if (compare != null) m.Add(MenuEntry.Item("Salir de la comparaci\u00F3n   Esc", "close", false, delegate { ExitCompare(); }));
            m.Add(MenuEntry.Line());
            MenuEntry json = MenuEntry.Item("Copiar como JSON", "copy", false, delegate { CopyJsonFromMenu(); });
            json.Detail = JsonSelection() != null ? "El nodo seleccionado" : "Todo el documento";
            json.Enabled = ready && tree != null;
            m.Add(json);
        }

        void CompareOther()
        {
            string path = null;
            Func<string> pick = PickCompare;
            if (pick != null) path = pick();
            else
            {
                try
                {
                    using (System.Windows.Forms.OpenFileDialog d = new System.Windows.Forms.OpenFileDialog())
                    {
                        d.Title = "Comparar con otro XML";
                        d.Filter = "XML|*.xml;*.xsd;*.config;*.svg;*.xaml|Todos los archivos|*.*";
                        if (file != null && file.Path != null) d.InitialDirectory = System.IO.Path.GetDirectoryName(file.Path);
                        if (d.ShowDialog(Win32) == System.Windows.Forms.DialogResult.OK) path = d.FileName;
                    }
                }
                catch (Exception ex) { ShotStack.Log("Comparar: " + ex.Message); Say("No he podido abrir el selector de archivos.", true); return; }
            }
            if (path != null) CompareWith(path);
        }

        // The other file (null: the XML before this one in the folder) against the open document, as it is on screen.
        void CompareWith(string other)
        {
            if (!xml || Current() == null) return;
            if (editing) SetEditing(false);
            if (model == null || parsedText != text || treeFor != model) { Say(model == null && parsedText == text ? "Corrige el XML antes de compararlo." : "Un momento: todav\u00EDa estoy leyendo el XML.", true); return; }
            string cur = file != null ? file.Path : null;
            if (other == null && cur == null) return;
            XmlModel now = model;
            string snap = text;
            int my = ++compareToken;
            comparing = true;
            fade.Stop();
            status = "Comparando\u2026";
            statusBad = false;
            Stats();
            Cursor = Cursors.AppStarting;
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string err = null, oldPath = other;
                bool soft = false, cut = now.Truncated;
                DiffResult r = null;
                Func<bool> stop = delegate { return System.Threading.Volatile.Read(ref compareToken) != my || open != this; };
                try
                {
                    if (oldPath == null)
                    {
                        oldPath = XmlWatch.Sibling(cur, -1);
                        if (oldPath == null) { err = "No hay XML anteriores en la carpeta."; soft = true; }
                    }
                    if (oldPath != null)
                    {
                        string lerr;
                        DocFile d = DocFile.Load(oldPath, out lerr);
                        if (d == null) err = lerr ?? "No he podido leer el archivo.";
                        else
                        {
                            XmlModel om;
                            string perr;
                            int l, c;
                            if (!XmlDoc.Parse(d.Text, stop, out om, out perr, out l, out c))
                                err = perr != null ? "\u00AB" + System.IO.Path.GetFileName(oldPath) + "\u00BB no es un XML bien formado. " + perr : null;
                            else
                            {
                                XmlDoc.ExpandInner(om);
                                cut |= om.Truncated;
                                r = XmlDiff.Compare(om, now, stop);
                            }
                        }
                    }
                }
                catch (Exception ex) { ShotStack.Log("XML comparar: " + ex.Message); err = "No he podido comparar los XML."; r = null; }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (my != compareToken || open != this) return;
                        comparing = false;
                        Cursor = null;
                        status = null;
                        if (r == null) { if (err != null) Say(err, !soft); else Stats(); return; }
                        if (text != snap || model != now || editing || !xml) { Stats(); return; }
                        StartCompare(r, oldPath, cut);
                    }));
                }
                catch { }
            }, 16 * 1024 * 1024);
            t.IsBackground = true;
            t.Name = "Stackshot XML compare";
            t.Start();
        }

        void StartCompare(DiffResult r, string oldPath, bool cut)
        {
            compare = new Comparison { Text = text, Result = r, OldPath = oldPath };
            NewCompareView();
            if (previewHost != null) previewHost.Child = compare.View;
            ShowHeader();
            Stats();
            if (r.Stops.Count > 0) compare.View.Step(1);
            else compare.View.List.Focus();
            UpdateComparePos();
            if (cut) Say("El XML es muy grande: solo se comparan sus primeros " + XmlDoc.MaxNodes.ToString("N0") + " nodos.", false);
        }

        internal void ExitCompare()
        {
            compareToken++;
            if (comparing) { comparing = false; Cursor = null; status = null; }
            if (compare == null) { Stats(); return; }
            compare = null;
            comparePos = null;
            compareUp = compareDown = null;
            if (previewHost != null && tree != null && model != null && treeFor == model) previewHost.Child = tree;
            ShowHeader();
            Stats();
            if (tree != null && previewHost != null && previewHost.Child == tree) tree.List.Focus();
        }

        // Expandir todo / Contraer todo act on what is on screen.
        void ExpandXml(bool open)
        {
            if (CompareShown()) { if (open) compare.View.ExpandAll(); else compare.View.CollapseAll(); return; }
            if (tree == null) return;
            if (open) tree.ExpandAll(); else tree.CollapseAll();
        }

        // Copiar como imagen and Listo picture the comparison while it is on screen.
        BitmapSource CompareImage(out string err)
        {
            return XmlDiffView.Render(compare.View.Rows, compare.View.Look, out err);
        }
    }
}

// Round 6 (accessibility), suite "w6a11y": what screen readers and the keyboard get from the controls Stackshot draws
// itself. The automation tree of every Home page (light and dark), the document viewer and the editor and player bars is
// walked through the WPF automation peers and the WinForms accessible objects of windows built by this test (laid out or
// parked off screen, never shown or activated): every interactive element has a name and the right role and patterns,
// decorative pictures stay out, patterns act on test-owned controls, Tab order follows the page, and the focus ring
// shows only after the keyboard. No input is injected, no screen is read, files go only to STACKSHOT_TEST_DATA.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AccessibleObject = System.Windows.Forms.AccessibleObject;

namespace Stackshot.Tests
{
    static class AppTestsW6A11y
    {
        const string S = "w6a11y";

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        public static void Suite()
        {
            AppTests.Test(S, "names leave the shortcut apart", Names);
            AppTests.Test(S, "decorative pictures are left out", Decorative);
            AppTests.Test(S, "patterns act on test-owned controls", Patterns);
            AppTests.Test(S, "keys press, toggle and choose", Keys);
            AppTests.Test(S, "xml tree lines are tree items that open and close", XmlTree);
            AppTests.Test(S, "menu rows are menu items", MenuRows);
            AppTests.Test(S, "tab order of a settings page", TabOrder);
            AppTests.Test(S, "focus comes back to the same control after a rebuild", FocusBack);
            foreach (int theme in new int[] { 1, 2 })
            {
                int th = theme;
                string label = th == 1 ? "light" : "dark";
                AppTests.Test(S, "focus ring only after the keyboard " + label, delegate { FocusRing(th); });
                AppTests.Test(S, "record options " + label, delegate { RecordOptions(th); });
            }
            AppTests.Test(S, "tray panel parts", TrayParts);
            AppTests.Test(S, "stack card parts", CardParts);
            AppTests.Test(S, "editor bar and backdrop strip parts", EditorParts);
            AppTests.Test(S, "player parts", PlayerParts);
            foreach (int theme in new int[] { 1, 2 })
            {
                int th = theme;
                string label = th == 1 ? "light" : "dark";
                foreach (string page in new string[] { "home:0", "home:1", "home:2", "history", "keys", "general", "editor", "record", "mascot", "mascotset", "about" })
                {
                    string pg = page;
                    AppTests.Test(S, "home " + pg + " " + label, delegate { HomePage(th, pg); });
                }
                foreach (string kind in new string[] { "zone", "md-view", "md-edit", "md-outline", "xml-soap", "xml-edit", "xml-compare" })
                {
                    string k = kind;
                    AppTests.Test(S, "viewer " + k + " " + label, delegate { DocPage(th, k); });
                }
            }
            HistoryIndex.Dir = Path.Combine(Settings.DataDir, "historial");
            HistoryIndex.TempFolder = Settings.TempDir;
        }

        // ---- Private data

        static string root;

        static string Root()
        {
            if (root != null) return root;
            string data = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
            root = Path.Combine(data, "w6a11y", DateTime.Now.ToString("HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(root);
            return root;
        }

        static void Png(string path, int w, int h, Color c)
        {
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(0, 0, w, h));
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) e.Save(fs);
        }

        // Settings that only ever point at this test's folders, with a few synthetic captures in them.
        static Settings NewSettings()
        {
            Settings s = new Settings();
            string save = Path.Combine(Root(), "Capturas");
            Directory.CreateDirectory(save);
            if (Directory.GetFiles(save).Length == 0)
                for (int i = 0; i < 3; i++) Png(Path.Combine(save, "Captura " + (i + 1) + ".png"), 160 + 20 * i, 100, Color.FromRgb((byte)(60 * i), 120, 200));
            s.SaveFolder = save;
            s.ShowIntro = false;
            HistoryIndex.Dir = Path.Combine(Root(), "historial");
            HistoryIndex.TempFolder = Settings.TempDir;
            return s;
        }

        // ---- Home

        static HomeWindow MakeHome(int appearance, Settings s)
        {
            Ds.Apply(appearance);
            s.Appearance = appearance;
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            return new HomeWindow(owner, s);
        }

        static void SetPage(HomeWindow hw, string id)
        {
            MethodInfo m = typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance);
            try { m.Invoke(hw, new object[] { id, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("page " + id + ": " + ex.InnerException, ex.InnerException); }
        }

        // Off screen, never activated, not in the taskbar: nothing ever shows on the desktop.
        static void Park(Window w)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -32000;
            w.Top = -32000;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Show();
        }

        static void Layout(Window w, int width, int height)
        {
            UIElement r = (UIElement)w.Content;
            r.Measure(new Size(width, height));
            r.Arrange(new Rect(0, 0, width, height));
            r.UpdateLayout();
        }

        static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            do
            {
                DispatcherFrame f = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { f.Continue = false; });
                Dispatcher.PushFrame(f);
                if (sw.ElapsedMilliseconds < ms) Thread.Sleep(5);
            } while (sw.ElapsedMilliseconds < ms);
        }

        static void HomePage(int theme, string page)
        {
            Settings s = NewSettings();
            string id = page;
            if (page.StartsWith("home:", StringComparison.Ordinal)) { s.HomeStyle = int.Parse(page.Substring(5)); id = "home"; }
            HomeWindow hw = MakeHome(theme, s);
            try
            {
                Park(hw);
                SetPage(hw, id);
                Pump(80);
                Audit a = new Audit();
                a.Walk(hw);
                a.Save("home-" + page.Replace(':', '-') + "-" + (theme == 1 ? "light" : "dark"));
                Console.WriteLine("    " + a.Summary());
                Check(a.Problems.Count == 0, a.Report());
            }
            finally { try { hw.Shutdown(); } catch { } Ds.Apply(1); }
        }

        // ---- Test-owned controls

        static AutomationPeer Peer(UIElement e) { return UIElementAutomationPeer.CreatePeerForElement(e); }

        // A plain window of this test, parked off screen and never activated.
        static Window Owned(UIElement content, double width, double height)
        {
            Window w = new Window { Width = width, Height = height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Content = content,
                                    Background = Ds.Brush(Ds.Brushes.Window), UseLayoutRounding = true };
            Park(w);
            Pump(60);
            return w;
        }

        static void Names()
        {
            string[,] cases =
            {
                { "Guardar (Ctrl+S)", "Guardar", "Ctrl+S" },
                { "Flecha  (A)", "Flecha", "A" },
                { "Grosor: Fino  (\u2212 / +)", "Grosor: Fino", "\u2212 / +" },
                { "T\u00EDtulo (Ctrl+1 a Ctrl+6)", "T\u00EDtulo", "Ctrl+1 a Ctrl+6" },
                { "Anterior (May\u00FAs+F3)", "Anterior", "May\u00FAs+F3" },
                { "XML anterior de la carpeta ([)", "XML anterior de la carpeta", "[" },
                { "Pausa  (Espacio)", "Pausa", "Espacio" },
                { "Tabla (dentro de una: filas y columnas)", "Tabla (dentro de una: filas y columnas)", null },
                { "V\u00EDdeo (MP4)", "V\u00EDdeo (MP4)", null },
                { "Recientes  \u00B7  3 capturas", "Recientes  \u00B7  3 capturas", null },
                { "C\u00E1mara HD (2)", "C\u00E1mara HD (2)", null },
                { "Silenciar  (M)", "Silenciar", "M" },
                { "Aplicar, copiar y cerrar  (Enter)", "Aplicar, copiar y cerrar", "Enter" },
                { "Capturar un \u00E1rea  (Ctrl + Impr Pant)", "Capturar un \u00E1rea", "Ctrl + Impr Pant" },
                { "Capturar un \u00E1rea", "Capturar un \u00E1rea", null }
            };
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                string s = cases[i, 0];
                Check(Access.Clean(s) == cases[i, 1], "name of <" + s + ">: <" + Access.Clean(s) + ">");
                Check(Access.Shortcut(s) == cases[i, 2], "shortcut of <" + s + ">: <" + Access.Shortcut(s) + ">");
            }
            // A tooltip that only repeats the label is no help; its shortcut is the accelerator key.
            MacButton b = new MacButton("Guardar", ButtonKind.Secondary) { ToolTip = "Guardar (Ctrl+S)" };
            AutomationPeer p = Peer(b);
            Check(p.GetName() == "Guardar" && p.GetHelpText() == "" && p.GetAcceleratorKey() == "Ctrl+S",
                  "button: '" + p.GetName() + "' help '" + p.GetHelpText() + "' keys '" + p.GetAcceleratorKey() + "'");
            DocIconButton ib = new DocIconButton("more", "M\u00E1s opciones");
            p = Peer(ib);
            Check(p.GetName() == "M\u00E1s opciones" && p.GetHelpText() == "", "icon button: '" + p.GetName() + "' help '" + p.GetHelpText() + "'");
            // A settings row: its title names the control, the line under it is the help.
            MacSwitch sw = new MacSwitch(true);
            Access.Describe(sw, "Sonido de c\u00E1mara", "Un clic suave cada vez que capturas.");
            p = Peer(sw);
            Check(p.GetName() == "Sonido de c\u00E1mara" && p.GetHelpText() == "Un clic suave cada vez que capturas.", "row switch: '" + p.GetName() + "' help '" + p.GetHelpText() + "'");
            MacButton open = new MacButton("Abrir", ButtonKind.Secondary);
            Access.Describe(open, "Aplicaciones predeterminadas de Windows", null);
            Check(Peer(open).GetName() == "Aplicaciones predeterminadas de Windows, Abrir", "row button: " + Peer(open).GetName());
        }

        static void Decorative()
        {
            Check(Peer(new GlyphView("check", 14, Colors.Black)) == null, "an icon is listed");
            Check(Peer(new IconTile("gear", 20, Colors.Red, Colors.Blue)) == null, "an icon tile is listed");
            Check(Peer(new Keycaps("Ctrl + S", 20)) == null, "keycaps are listed");
            Check(Peer(new DecorImage()) == null, "a decorative picture is listed");
            Check(Peer(new SoftShadow(8, 8, 2, 0.2)) == null, "a shadow is listed");
            Check(Peer(new MacSwitch(false) { AccessHidden = true }) == null, "a switch that a pill speaks for is listed");
            TapBorder b = new TapBorder { Label = "Copiar", Child = new GlyphView("copy", 14, Colors.Black) };
            List<AutomationPeer> kids = Peer(b).GetChildren();
            Check(kids == null || kids.Count == 0, "the icon inside a button is listed");
            Check(Peer(new RoleGrid { Role = Access.Menu }).GetAutomationControlType() == AutomationControlType.Menu, "a menu's container is not a menu");
            // The microphone's level is a read-only bar readers can query.
            MicBar bar = new MicBar();
            AutomationProperties.SetName(bar, "Nivel del micr\u00F3fono");
            AutomationPeer pb = Peer(bar);
            IRangeValueProvider level = pb == null ? null : (IRangeValueProvider)pb.GetPattern(PatternInterface.RangeValue);
            Check(pb != null && pb.GetAutomationControlType() == AutomationControlType.ProgressBar && pb.GetName() == "Nivel del micr\u00F3fono" && level != null && level.IsReadOnly && level.Value >= 0 && level.Value <= 100,
                  "microphone level: " + (pb == null ? "not listed" : pb.GetAutomationControlType() + " '" + pb.GetName() + "'"));
        }

        static void Patterns()
        {
            Ds.Apply(1);
            MacSwitch sw = new MacSwitch(false);
            bool toggled = false;
            sw.Toggled += delegate(bool on) { toggled = on; };
            Access.Describe(sw, "Activar", null);
            MacSegmented seg = new MacSegmented(new string[] { "Uno", "Dos", "Tres" }, 0);
            int changed = -1;
            seg.Changed += delegate(int i) { changed = i; };
            Access.Describe(seg, "Cantidad", "Cu\u00E1ntos");
            MacButton btn = new MacButton("Aceptar", ButtonKind.Primary);
            int clicks = 0;
            btn.Click += delegate { clicks++; };
            MacButton off = new MacButton("Apagado", ButtonKind.Secondary) { IsEnabled = false };
            int tapped = 0;
            TapBorder tap = new TapBorder { Label = "Abrir", Width = 80, Height = 24, Background = Brushes.Transparent, Do = delegate { tapped++; } };
            bool check = false;
            TapBorder chk = new TapBorder { Label = "Recordar", Role = Access.Check, Width = 80, Height = 24, Background = Brushes.Transparent };
            chk.On = delegate { return check; };
            chk.Do = delegate { check = !check; };
            ChipToggle chip = new ChipToggle("Aa", "Coincidir may\u00FAsculas", false);
            StackPanel panel = new StackPanel { Margin = new Thickness(12) };
            foreach (UIElement e in new UIElement[] { sw, seg, btn, off, tap, chk, chip }) panel.Children.Add(e);
            Window w = Owned(panel, 420, 320);
            try
            {
                AutomationPeer ps = Peer(sw);
                Check(ps.GetAutomationControlType() == AutomationControlType.CheckBox && ps.GetName() == "Activar" && ps.IsKeyboardFocusable(), "switch: " + ps.GetAutomationControlType() + " '" + ps.GetName() + "'");
                IToggleProvider t = (IToggleProvider)ps.GetPattern(PatternInterface.Toggle);
                Check(t != null && t.ToggleState == ToggleState.Off && ps.GetPattern(PatternInterface.Invoke) == null, "switch patterns");
                t.Toggle();
                Check(!sw.IsOn, "the switch flipped before the reader's call returned");
                Pump(40);
                Check(sw.IsOn && toggled && t.ToggleState == ToggleState.On, "the toggle pattern did not flip the switch (" + sw.IsOn + ", event " + toggled + ")");

                AutomationPeer pg = Peer(seg);
                Check(pg.GetAutomationControlType() == AutomationControlType.List && pg.GetName() == "Cantidad" && pg.GetHelpText() == "Cu\u00E1ntos", "segmented: " + pg.GetAutomationControlType() + " '" + pg.GetName() + "'");
                ISelectionProvider sel = (ISelectionProvider)pg.GetPattern(PatternInterface.Selection);
                List<AutomationPeer> items = pg.GetChildren();
                string[] opts = { "Uno", "Dos", "Tres" };
                Check(sel != null && !sel.CanSelectMultiple && items != null && items.Count == 3, "segmented items");
                for (int i = 0; i < 3; i++)
                    Check(items[i].GetAutomationControlType() == AutomationControlType.ListItem && items[i].GetName() == opts[i], "item " + i + ": '" + items[i].GetName() + "'");
                ISelectionItemProvider two = (ISelectionItemProvider)items[1].GetPattern(PatternInterface.SelectionItem);
                Check(!two.IsSelected && ((ISelectionItemProvider)items[0].GetPattern(PatternInterface.SelectionItem)).IsSelected, "first option not the selected one");
                Check(items[1].GetPositionInSet() == 2 && items[1].GetSizeOfSet() == 3, "position in the set");
                two.Select();
                Pump(40);
                Check(seg.Selected == 1 && changed == 1 && two.IsSelected && sel.GetSelection().Length == 1, "selecting the second option: " + seg.Selected + ", changed " + changed);

                ((IInvokeProvider)Peer(btn).GetPattern(PatternInterface.Invoke)).Invoke();
                Pump(40);
                Check(clicks == 1, "invoke did not press the button");
                bool threw = false;
                try { ((IInvokeProvider)Peer(off).GetPattern(PatternInterface.Invoke)).Invoke(); }
                catch (ElementNotEnabledException) { threw = true; }
                Check(threw && !Peer(off).IsEnabled(), "a disabled button was pressed");

                AutomationPeer pt = Peer(tap);
                Check(pt.GetAutomationControlType() == AutomationControlType.Button && pt.GetName() == "Abrir" && pt.IsKeyboardFocusable(), "tap: " + pt.GetAutomationControlType());
                ((IInvokeProvider)pt.GetPattern(PatternInterface.Invoke)).Invoke();
                Pump(40);
                Check(tapped == 1, "invoke did not run the surface's action");

                AutomationPeer pc = Peer(chk);
                IToggleProvider tc = (IToggleProvider)pc.GetPattern(PatternInterface.Toggle);
                Check(pc.GetAutomationControlType() == AutomationControlType.CheckBox && tc != null && pc.GetPattern(PatternInterface.Invoke) == null, "check surface patterns");
                tc.Toggle();
                Pump(40);
                Check(check && tc.ToggleState == ToggleState.On, "the check surface did not toggle");

                AutomationPeer pk = Peer(chip);
                Check(pk.GetAutomationControlType() == AutomationControlType.CheckBox && pk.GetName() == "Coincidir may\u00FAsculas", "chip: '" + pk.GetName() + "'");
                ((IToggleProvider)pk.GetPattern(PatternInterface.Toggle)).Toggle();
                Pump(40);
                Check(chip.IsOn, "the chip did not toggle");
            }
            finally { w.Close(); }
        }

        // A key press as WPF routes it to the element that has the focus: PreviewKeyDown down to it, then KeyDown back up.
        static void Press(UIElement target, System.Windows.Input.Key k)
        {
            PresentationSource src = PresentationSource.FromVisual(target);
            KeyEventArgs e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e);
            if (e.Handled) return;
            e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, k) { RoutedEvent = Keyboard.KeyDownEvent };
            target.RaiseEvent(e);
        }

        static void Keys()
        {
            Ds.Apply(1);
            Check(Keyboard.Modifiers == ModifierKeys.None, "a modifier key is held down");
            MacSwitch sw = new MacSwitch(false);
            MacSegmented seg = new MacSegmented(new string[] { "A", "B", "C" }, 0);
            int ran = 0, outer = 0;
            TapBorder tap = new TapBorder { Label = "Ir", Width = 60, Height = 24, Background = Brushes.Transparent, Do = delegate { ran++; } };
            bool on = false;
            TapBorder chk = new TapBorder { Label = "Ver", Role = Access.Check, Width = 60, Height = 24, Background = Brushes.Transparent, Do = delegate { on = !on; } };
            chk.On = delegate { return on; };
            TextBox box = new TextBox { Width = 120 };
            TapBorder holder = new TapBorder { Label = "Contenedor", Background = Brushes.Transparent, Do = delegate { outer++; }, Child = box };
            StackPanel panel = new StackPanel { Margin = new Thickness(12) };
            foreach (UIElement e in new UIElement[] { sw, seg, tap, chk, holder }) panel.Children.Add(e);
            Window w = Owned(panel, 360, 260);
            try
            {
                Press(sw, System.Windows.Input.Key.Space);
                Check(sw.IsOn, "Space did not flip the switch");
                Press(seg, System.Windows.Input.Key.Right);
                Check(seg.Selected == 1, "Right did not move the choice: " + seg.Selected);
                Press(seg, System.Windows.Input.Key.End);
                Check(seg.Selected == 2, "End did not go to the last choice");
                Press(seg, System.Windows.Input.Key.Left);
                Check(seg.Selected == 1, "Left did not move back");
                Press(tap, System.Windows.Input.Key.Space);
                Pump(40);
                Check(ran == 1, "Space did not press the surface");
                Press(tap, System.Windows.Input.Key.Enter);
                Pump(40);
                Check(ran == 2, "Enter did not press the surface");
                Press(chk, System.Windows.Input.Key.Enter);
                Pump(40);
                Check(!on, "Enter toggled a check box (only Space does)");
                Press(chk, System.Windows.Input.Key.Space);
                Pump(40);
                Check(on, "Space did not toggle the check surface");
                // Typed in a field inside a clickable surface: the surface stays as it is.
                Press(box, System.Windows.Input.Key.Space);
                Press(box, System.Windows.Input.Key.Enter);
                Pump(40);
                Check(outer == 0, "a key typed in a field pressed the surface around it");
            }
            finally { w.Close(); }
        }

        static void XmlTree()
        {
            Ds.Apply(1);
            XmlModel m;
            string err;
            int l, c;
            Check(XmlDoc.Parse("<pedido id=\"7\"><cliente>Ana</cliente><lineas><linea sku=\"A\"/><linea sku=\"B\"/></lineas></pedido>", out m, out err, out l, out c), "does not parse: " + err);
            XmlTreeView tv = new XmlTreeView(Ds.Brushes);
            tv.Load(m);
            Window w = Owned(tv, 520, 360);
            try
            {
                Pump(120);
                AutomationPeer list = Peer(tv.List);
                Check(list.GetAutomationControlType() == AutomationControlType.Tree && list.GetName() == "\u00C1rbol del XML", "tree: " + list.GetAutomationControlType() + " '" + list.GetName() + "'");
                List<AutomationPeer> rows = list.GetChildren();
                Check(rows != null && rows.Count == 7 && tv.Rows.Count == 7, "lines: " + (rows == null ? -1 : rows.Count));
                AutomationPeer top = rows[0], cliente = rows[1], lineas = rows[2];
                Check(top.GetAutomationControlType() == AutomationControlType.TreeItem && top.GetName() == "<pedido id=\"7\">", "first line: '" + top.GetName() + "'");
                Check(cliente.GetName() == "<cliente>Ana</cliente>" && cliente.GetPattern(PatternInterface.ExpandCollapse) == null, "a line that does not open: '" + cliente.GetName() + "'");
                IExpandCollapseProvider ec = (IExpandCollapseProvider)lineas.GetPattern(PatternInterface.ExpandCollapse);
                Check(ec != null && ec.ExpandCollapseState == ExpandCollapseState.Expanded, "lineas does not say it is open");
                ec.Collapse();
                Pump(80);
                Check(tv.Rows.Count == 4, "collapsing from the reader: " + tv.Rows.Count + " lines");
                Peer(tv.List).ResetChildrenCache();   // as WPF does after the next layout
                rows = Peer(tv.List).GetChildren();
                IExpandCollapseProvider closed = (IExpandCollapseProvider)rows[2].GetPattern(PatternInterface.ExpandCollapse);
                Check(closed.ExpandCollapseState == ExpandCollapseState.Collapsed && rows[2].GetName().StartsWith("<lineas>", StringComparison.Ordinal), "closed line: '" + rows[2].GetName() + "'");
                closed.Expand();
                Pump(80);
                Check(tv.Rows.Count == 7, "expanding from the reader: " + tv.Rows.Count + " lines");
            }
            finally { w.Close(); }
        }

        static void MenuRows()
        {
            Ds.Apply(1);
            Type rowsType = typeof(MacMenu).GetNestedType("Rows", BindingFlags.NonPublic);
            System.Windows.Controls.Primitives.Popup pop = new System.Windows.Controls.Primitives.Popup();   // never opened
            object rows = Activator.CreateInstance(rowsType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pop }, null);
            MethodInfo add = rowsType.GetMethod("Add");
            int ran = 0;
            MenuEntry a = MenuEntry.Item("Copiar", "copy", false, delegate { ran++; });
            MenuEntry b = MenuEntry.Item("Formato", null, true, delegate { });
            MenuEntry c = MenuEntry.Item("Pegar", null, false, null);
            c.Enabled = false;
            c.Detail = "Nada que pegar";
            AutomationPeer pa = Peer((UIElement)add.Invoke(rows, new object[] { a })), pb = Peer((UIElement)add.Invoke(rows, new object[] { b })), pc = Peer((UIElement)add.Invoke(rows, new object[] { c }));
            Check(pa.GetAutomationControlType() == AutomationControlType.MenuItem && pa.GetName() == "Copiar" && pa.GetPattern(PatternInterface.Invoke) != null && pa.GetPattern(PatternInterface.Toggle) == null, "menu row: " + pa.GetAutomationControlType() + " '" + pa.GetName() + "'");
            IToggleProvider tb = (IToggleProvider)pb.GetPattern(PatternInterface.Toggle);
            Check(tb != null && tb.ToggleState == ToggleState.On, "a checked row does not say so");
            Check(!pc.IsEnabled() && pc.GetName() == "Pegar" && pc.GetHelpText() == "Nada que pegar", "a disabled row: '" + pc.GetName() + "' help '" + pc.GetHelpText() + "'");
            ((IInvokeProvider)pa.GetPattern(PatternInterface.Invoke)).Invoke();
            Pump(60);
            Check(ran == 1, "choosing a row from the reader did not run it");
        }

        // WPF's own Tab navigation (KeyboardNavigation), asked for the stop after each one without moving the focus.
        static List<DependencyObject> TabSequence(Window w, int max)
        {
            Type kn = typeof(KeyboardNavigation);
            object nav = kn.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            kn.GetField("_navigationProperty", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(nav, KeyboardNavigation.TabNavigationProperty);
            MethodInfo next = kn.GetMethod("GetNextTab", BindingFlags.NonPublic | BindingFlags.Instance);
            List<DependencyObject> seq = new List<DependencyObject>();
            DependencyObject at = w;
            for (int i = 0; i < max; i++)
            {
                at = (DependencyObject)next.Invoke(nav, new object[] { at, w, false });
                if (at == null || seq.Contains(at)) break;
                seq.Add(at);
            }
            return seq;
        }

        static void Collect<T>(DependencyObject o, List<T> into) where T : DependencyObject
        {
            if (o is T) into.Add((T)o);
            int n = VisualTreeHelper.GetChildrenCount(o);
            for (int i = 0; i < n; i++) Collect(VisualTreeHelper.GetChild(o, i), into);
        }

        static void TabOrder()
        {
            Settings s = NewSettings();
            HomeWindow hw = MakeHome(1, s);
            try
            {
                Park(hw);
                SetPage(hw, "general");
                Pump(80);
                List<DependencyObject> seq = TabSequence(hw, 400);
                Check(seq.Count > 30, "only " + seq.Count + " stops");
                // The settings search first, then the sections in their order.
                AutomationPeer field = Peer((UIElement)seq[0]);
                Check(field != null && field.GetAutomationControlType() == AutomationControlType.Edit && field.GetName() == "Buscar ajustes", "stop 0: " + (field == null ? seq[0].GetType().Name : field.GetName()));
                string[] pages = { "Inicio", "Historial", "Atajos", "General", "Fondo y editor", "Grabaci\u00F3n", "Mascota", "Acerca de" };
                for (int i = 0; i < pages.Length; i++)
                {
                    AutomationPeer p = Peer((UIElement)seq[i + 1]);
                    Check(p != null && p.GetAutomationControlType() == AutomationControlType.TabItem && p.GetName() == pages[i], "stop " + (i + 1) + ": " + (p == null ? seq[i + 1].GetType().Name : p.GetName()));
                }
                // Then the page, as it reads: top to bottom, and left to right on a line.
                ScrollViewer scroller = (ScrollViewer)typeof(HomeWindow).GetField("scroller", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hw);
                UIElement page = (UIElement)scroller.Content;
                Point last = new Point(double.MinValue, double.MinValue);
                int inPage = 0;
                foreach (DependencyObject d in seq)
                {
                    UIElement u = d as UIElement;
                    if (u == null || !page.IsAncestorOf(u)) continue;
                    Point at = u.TranslatePoint(new Point(0, 0), page);
                    bool sameLine = Math.Abs(at.Y - last.Y) < 14;
                    Check(at.Y > last.Y - 14 && (!sameLine || at.X >= last.X - 1 || at.Y > last.Y + 2), "stop out of reading order at " + Peer(u).GetName() + " (" + (int)at.X + "," + (int)at.Y + " after " + (int)last.X + "," + (int)last.Y + ")");
                    last = at;
                    inPage++;
                }
                // Every control on the page is a stop.
                List<Drawn> drawn = new List<Drawn>();
                Collect(page, drawn);
                int missing = 0;
                StringBuilder miss = new StringBuilder();
                foreach (Drawn d in drawn)
                    if (d.IsVisible && d.IsEnabled && d.Focusable && !seq.Contains(d)) { missing++; miss.Append(' ').Append(Peer(d).GetName()); }
                Check(missing == 0, missing + " controls never reached with Tab:" + miss);
                Console.WriteLine("    " + seq.Count + " stops, " + inPage + " on the page, " + drawn.Count + " drawn controls");
                // The arrows' geometry works too: below a switch there is the next control.
                MacSwitch first = null;
                foreach (Drawn d in drawn) if (d is MacSwitch && d.IsEnabled) { first = (MacSwitch)d; break; }
                DependencyObject down = first.PredictFocus(FocusNavigationDirection.Down);
                Check(down is UIElement && ((UIElement)down).TranslatePoint(new Point(0, 0), page).Y > first.TranslatePoint(new Point(0, 0), page).Y, "nothing predicted below the first switch");
            }
            finally { try { hw.Shutdown(); } catch { } Ds.Apply(1); }
        }

        static void FocusBack()
        {
            Settings s = NewSettings();
            HomeWindow hw = MakeHome(1, s);
            try
            {
                Park(hw);
                SetPage(hw, "general");
                Pump(80);
                List<MacSwitch> sws = new List<MacSwitch>();
                Collect(hw, sws);
                MacSwitch intro = sws.Find(delegate(MacSwitch x) { return Peer(x).GetName() == "Animaci\u00F3n al abrir"; });
                Check(intro != null, "no switch for the intro");
                string key = Access.KeyOf(intro);
                int nth = Access.Nth(hw, intro, key);
                Check(nth == 0, "nth " + nth);
                // Two buttons of the same kind and name on one page (the folder rows' "Cambiar…"): the second is the second.
                List<MacButton> buttons = new List<MacButton>();
                Collect(hw, buttons);
                MacButton second = null;
                string twinKey = null;
                Dictionary<string, int> seen = new Dictionary<string, int>();
                foreach (MacButton b in buttons)
                {
                    string k = Access.KeyOf(b);
                    int n;
                    seen.TryGetValue(k, out n);
                    seen[k] = n + 1;
                    if (n == 1 && second == null) { second = b; twinKey = k; }
                }
                int twinNth = second == null ? -1 : Access.Nth(hw, second, twinKey);
                // The page is built again, as a choice that rebuilds it or a theme change does.
                typeof(HomeWindow).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, null);
                Pump(60);
                Check(PresentationSource.FromVisual(intro) == null, "the old switch is still on the page");
                FrameworkElement again = Access.Find(hw, key, nth);
                Check(again is MacSwitch && again != intro && again.IsVisible && Peer(again).GetName() == "Animaci\u00F3n al abrir", "not found again: " + (again == null ? "nothing" : Peer(again).GetName()));
                if (second != null)
                {
                    Check(twinNth == 1, "the second twin counted as " + twinNth);
                    FrameworkElement twin = Access.Find(hw, twinKey, twinNth);
                    Check(twin != null && Access.Nth(hw, twin, twinKey) == 1, "the second twin was not found again as the second");
                }
                else Note("no two controls with the same name on the page");
            }
            finally { try { hw.Shutdown(); } catch { } Ds.Apply(1); }
        }

        // ---- The focus ring

        // The control's own focus handlers, called directly: raising the routed event would make WPF move its logical (and
        // then the real) focus around this test's window.
        static void Focused(UIElement e, bool got)
        {
            KeyboardFocusChangedEventArgs a = got ? new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, e) : new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, e, null);
            a.RoutedEvent = got ? Keyboard.GotKeyboardFocusEvent : Keyboard.LostKeyboardFocusEvent;
            Handler(e, got ? "OnGotKeyboardFocus" : "OnLostKeyboardFocus", a);
        }

        // A press of the mouse as the control's own handler gets it (nothing is clicked).
        static void MouseDown(UIElement e)
        {
            MouseButtonEventArgs a = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent };
            Handler(e, "OnPreviewMouseDown", a);
        }

        static void Handler(UIElement e, string name, RoutedEventArgs a)
        {
            MethodInfo m = null;
            for (Type t = e.GetType(); t != null && m == null; t = t.BaseType)
                m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Check(m != null, "no " + name + " on " + e.GetType().Name);
            try { m.Invoke(e, new object[] { a }); }
            catch (TargetInvocationException x) { throw x.InnerException; }
        }

        static void KeyPressed(Window w)
        {
            KeyEventArgs a = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, System.Windows.Input.Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = true };
            w.RaiseEvent(a);
        }

        static bool RingOn(UIElement e)
        {
            Drawn d = e as Drawn;
            if (d != null) return d.RingShown;
            ITap t = e as ITap;
            return t != null && t.State.RingShown;
        }

        static BitmapSource Shot(Window w)
        {
            FrameworkElement root = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            root.UpdateLayout();
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            rtb.Freeze();
            return rtb;
        }

        static byte[] Pixels(BitmapSource b)
        {
            byte[] px = new byte[b.PixelWidth * b.PixelHeight * 4];
            b.CopyPixels(px, b.PixelWidth * 4, 0);
            return px;
        }

        // Pixels of the accent colour in the band 1 to 3 px outside the element.
        static int AccentAround(BitmapSource shot, UIElement e, Window w)
        {
            byte[] px = Pixels(shot);
            int bw = shot.PixelWidth, bh = shot.PixelHeight;
            Point o = e.TranslatePoint(new Point(0, 0), (UIElement)VisualTreeHelper.GetChild(w, 0));
            Rect r = new Rect(o, e.RenderSize);
            Color ac = Ds.Brushes.Accent;
            int hits = 0;
            for (int y = (int)Math.Floor(r.Top - 4); y <= (int)Math.Ceiling(r.Bottom + 4); y++)
                for (int x = (int)Math.Floor(r.Left - 4); x <= (int)Math.Ceiling(r.Right + 4); x++)
                {
                    if (x < 0 || y < 0 || x >= bw || y >= bh) continue;
                    bool inside = x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
                    if (inside) continue;
                    int i = (y * bw + x) * 4;
                    if (Math.Abs(px[i] - ac.B) + Math.Abs(px[i + 1] - ac.G) + Math.Abs(px[i + 2] - ac.R) < 60) hits++;
                }
            return hits;
        }

        static void SavePng(BitmapSource b, string name)
        {
            string dir = Path.Combine(Root(), "anillos");
            Directory.CreateDirectory(dir);
            PngBitmapEncoder e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(b));
            using (FileStream fs = File.Create(Path.Combine(dir, name))) e.Save(fs);
        }

        static void FocusRing(int theme)
        {
            Ds.Apply(theme);
            string label = theme == 1 ? "claro" : "oscuro";
            Palette pal = Ds.Brushes;
            MacSwitch sw = new MacSwitch(true) { Margin = new Thickness(18) };
            MacSegmented seg = new MacSegmented(new string[] { "Uno", "Dos" }, 0) { Margin = new Thickness(18) };
            MacButton btn = new MacButton("Guardar", ButtonKind.Secondary) { Margin = new Thickness(18) };
            DocIconButton icon = new DocIconButton("more", "M\u00E1s opciones") { Margin = new Thickness(18) };
            TapBorder tap = new TapBorder { Label = "Abrir", Width = 90, Height = 28, CornerRadius = new CornerRadius(8), Background = Ds.Brush(pal.Control), Margin = new Thickness(18), Do = delegate { } };
            TapText link = new TapText { Text = "Ver todas", FontSize = 13, Foreground = Ds.Brush(pal.Accent), Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center, Do = delegate { } };
            UIElement[] all = { sw, seg, btn, icon, tap, link };
            WrapPanel panel = new WrapPanel { Margin = new Thickness(10) };
            foreach (UIElement e in all) panel.Children.Add(e);
            Window w = Owned(panel, 560, 170);
            try
            {
                BitmapSource none = Shot(w);
                // Reached from the keyboard: every control shows the ring.
                KeyPressed(w);
                foreach (UIElement e in all) Focused(e, true);
                Pump(40);
                foreach (UIElement e in all) Check(RingOn(e), Peer(e).GetName() + " (" + e.GetType().Name + "): no ring after the keyboard");
                BitmapSource keys = Shot(w);
                SavePng(keys, "teclado-" + label + ".png");
                foreach (UIElement e in all)
                {
                    int n = AccentAround(keys, e, w), before = AccentAround(none, e, w);
                    Check(n - before > 40, Peer(e).GetName() + ": only " + (n - before) + " accent pixels around it");
                }
                foreach (UIElement e in all) Focused(e, false);
                Pump(40);
                foreach (UIElement e in all) Check(!RingOn(e), Peer(e).GetName() + ": the ring stayed after the focus left");
                // Focused right after a mouse press: no ring, and not a pixel changes for mouse users.
                foreach (UIElement e in all) { MouseDown(e); Focused(e, true); }
                Pump(40);
                foreach (UIElement e in all) Check(!RingOn(e), Peer(e).GetName() + ": a ring after the mouse");
                BitmapSource mouse = Shot(w);
                SavePng(mouse, "raton-" + label + ".png");
                byte[] a = Pixels(none), b = Pixels(mouse);
                int diff = 0;
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
                Check(diff == 0, diff + " bytes changed for a mouse user");
                foreach (UIElement e in all) Focused(e, false);
                // A key again: the keyboard's rings come back.
                KeyPressed(w);
                Focused(sw, true);
                Check(RingOn(sw), "no ring once the keyboard is used again");
                Focused(sw, false);
                Note("PNGs in " + Path.Combine(Root(), "anillos"));
            }
            finally { w.Close(); Ds.Apply(1); }
        }

        static void Note(string s) { Console.WriteLine("    " + s); }


        // ---- The options before recording (never shown: the dialog would place itself over the area on screen)

        static void RecordOptions(int theme)
        {
            Ds.Apply(theme);
            Settings s = NewSettings();
            s.RecordMic = false;   // the microphone meter only opens a device while the microphone is on
            s.RecordSystemAudio = false;
            ConstructorInfo ci = typeof(RecordSetup).GetConstructors(NP)[0];
            foreach (bool gif in new bool[] { false, true })
            {
                RecordSetup w = (RecordSetup)ci.Invoke(new object[] { s, new System.Drawing.Rectangle(100, 100, 800, 600), gif });
                try
                {
                    Layout(w, 460, 640);
                    Audit a = new Audit();
                    a.Walk((UIElement)w.Content);
                    a.Save("opciones-" + (gif ? "gif" : "video") + "-" + (theme == 1 ? "light" : "dark"));
                    Check(a.Problems.Count == 0, a.Report());
                    Check(AutomationProperties.GetIsDialog(w), "not a dialog for readers");
                    List<Drawn> drawn = new List<Drawn>();
                    Collect((DependencyObject)w.Content, drawn);
                    List<string> names = new List<string>();
                    foreach (Drawn d in drawn)
                    {
                        if (d.Visibility != Visibility.Visible) continue;
                        Check(d.Focusable, Peer(d).GetName() + " is not reached with Tab");
                        names.Add(Peer(d).GetName());
                    }
                    string all = string.Join("|", names.ToArray());
                    foreach (string want in gif ? new string[] { "Fotogramas", "Cuenta atr\u00E1s", "Preguntar siempre", "Cancelar", "Grabar" }
                                                : new string[] { "Calidad", "Fotogramas", "Sonido del equipo", "Micr\u00F3fono", "C\u00E1mara", "Cuenta atr\u00E1s", "Preguntar siempre", "Cancelar", "Grabar" })
                        Check(names.Contains(want), "no control named " + want + ": " + all);
                }
                finally { w.Close(); }
            }
            Ds.Apply(1);
        }

        // ---- The WinForms surfaces

        static List<AccessibleObject> Parts(AccessibleObject a)
        {
            List<AccessibleObject> l = new List<AccessibleObject>();
            int n = a.GetChildCount();
            for (int i = 0; i < n; i++) l.Add(a.GetChild(i));
            return l;
        }

        static AccessibleObject PartNamed(List<AccessibleObject> l, string name)
        {
            foreach (AccessibleObject p in l) if (p.Name == name) return p;
            throw new InvalidOperationException("no part named " + name);
        }

        static string Listed(List<AccessibleObject> l)
        {
            List<string> s = new List<string>();
            foreach (AccessibleObject p in l) s.Add(p.Name + " (" + p.Role + ")");
            return string.Join(", ", s.ToArray());
        }

        static void TrayParts()
        {
            Ds.Apply(1);
            Settings s = NewSettings();
            TrayPanel tp = new TrayPanel(s);
            try
            {
                typeof(TrayPanel).GetMethod("Rebuild", NP).Invoke(tp, new object[] { 1f, 0 });
                AccessibleObject a = tp.AccessibilityObject;
                Check(a.Role == System.Windows.Forms.AccessibleRole.MenuPopup && a.Name == "Stackshot", "panel: " + a.Role + " '" + a.Name + "'");
                List<AccessibleObject> parts = Parts(a);
                List<string> rows = new List<string>();
                foreach (object it in (System.Collections.IEnumerable)typeof(TrayPanel).GetField("items", NP).GetValue(tp))
                    if ((int)it.GetType().GetField("Kind").GetValue(it) == 0) rows.Add((string)it.GetType().GetField("Text").GetValue(it));
                List<AccessibleObject> menu = parts.FindAll(delegate(AccessibleObject p) { return p.Role == System.Windows.Forms.AccessibleRole.MenuItem; });
                Check(menu.Count == rows.Count && rows.Count > 5, "menu items " + menu.Count + " for " + rows.Count + " rows: " + Listed(parts));
                for (int i = 0; i < rows.Count; i++) Check(menu[i].Name == rows[i] && menu[i].DefaultAction == "Ejecutar", "row " + i + ": " + menu[i].Name);
                int region = -1;
                foreach (object it in (System.Collections.IEnumerable)typeof(TrayPanel).GetField("items", NP).GetValue(tp))
                    if ((string)it.GetType().GetField("Id").GetValue(it) == "region") region = rows.IndexOf((string)it.GetType().GetField("Text").GetValue(it));
                Check(region >= 0 && !string.IsNullOrEmpty(menu[region].KeyboardShortcut), "no shortcut for the area capture");
                Check(parts[0].Role == System.Windows.Forms.AccessibleRole.StaticText && parts[0].Name.StartsWith("Stackshot: ", StringComparison.Ordinal), "header: " + parts[0].Name);
                // The keyboard's highlight is the focused part.
                typeof(TrayPanel).GetMethod("Arrow", NP).Invoke(tp, new object[] { 1 });
                List<AccessibleObject> now = Parts(a);
                AccessibleObject focused = now.Find(delegate(AccessibleObject p) { return (p.State & System.Windows.Forms.AccessibleStates.Focused) != 0; });
                Check(focused != null && focused.Name == rows[0], "the highlighted row is not the focused part: " + (focused == null ? "none" : focused.Name));
                Check(a.GetFocused() == focused || a.GetFocused().Name == focused.Name, "GetFocused does not point at it");
                // A reader presses a row: it is chosen as Enter chooses it.
                PartNamed(now, rows[1]).DoDefaultAction();
                System.Windows.Forms.Application.DoEvents();
                string pending = (string)typeof(TrayPanel).GetField("pendingCommand", NP).GetValue(tp);
                Check(pending != null, "the row was not chosen");
            }
            finally { tp.Dispose(); }
        }

        sealed class PressLog
        {
            public readonly List<string> Pressed = new List<string>();
        }

        static void CardParts()
        {
            Ds.Apply(1);
            NewSettings();
            string png = Path.Combine(Root(), "Capturas", "Captura 1.png");
            Card c = new Card(null, png);
            try
            {
                c.Place(new System.Drawing.Rectangle(-32000, -32000, 256, 160), "prueba", 1f, 0, false);
                AccessibleObject a = c.AccessibilityObject;
                Check(a.Name == "Captura Captura 1.png" && a.Role == System.Windows.Forms.AccessibleRole.Grouping, "card: " + a.Role + " '" + a.Name + "'");
                List<AccessibleObject> parts = Parts(a);
                string[] want = { "Cerrar", "Fijar en pantalla", "Editar", "Copiar texto", "Copiar", "Guardar" };
                foreach (string n in want) Check(PartNamed(parts, n).Role == System.Windows.Forms.AccessibleRole.PushButton, n + " is not a button");
                Check(parts.Count == want.Length, "parts: " + Listed(parts));
                // Each button runs its own action (replaced here: the real ones copy, pin or open the editor).
                PressLog rec = new PressLog();
                foreach (object b in (System.Collections.IEnumerable)typeof(Card).GetField("btns", NP).GetValue(c))
                {
                    string lbl = (string)b.GetType().GetField("Label").GetValue(b);
                    b.GetType().GetField("Do").SetValue(b, (Action)delegate { rec.Pressed.Add(lbl); });
                }
                foreach (AccessibleObject p in Parts(a)) p.DoDefaultAction();
                System.Windows.Forms.Application.DoEvents();
                Check(string.Join("|", rec.Pressed.ToArray()) == string.Join("|", Names(parts)), "pressed: " + string.Join("|", rec.Pressed.ToArray()));
            }
            finally { c.Dispose(); }
        }

        static string[] Names(List<AccessibleObject> l)
        {
            List<string> s = new List<string>();
            foreach (AccessibleObject p in l) s.Add(p.Name);
            return s.ToArray();
        }

        static void EditorParts()
        {
            Ds.Apply(1);
            NewSettings();
            string png = Path.Combine(Root(), "Capturas", "Captura 2.png");
            System.Drawing.Bitmap img = new System.Drawing.Bitmap(320, 200);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(img)) g.Clear(System.Drawing.Color.SteelBlue);
            Editor ed = new Editor(null, png, img);
            try
            {
                Bar bar = (Bar)typeof(Editor).GetField("bar", NP).GetValue(ed);
                bar.Size = new System.Drawing.Size(1200, bar.Height > 0 ? bar.Height : 52);
                bar.LayoutItems();
                AccessibleObject a = bar.AccessibilityObject;
                Check(a.Role == System.Windows.Forms.AccessibleRole.ToolBar, "bar role " + a.Role);
                List<AccessibleObject> parts = Parts(a);
                Check(parts.Count > 15, "bar parts: " + Listed(parts));
                foreach (AccessibleObject p in parts)
                    Check(!string.IsNullOrEmpty(p.Name) && p.Name.IndexOf("  (", StringComparison.Ordinal) < 0, "bar part named '" + p.Name + "'");
                AccessibleObject arrow = PartNamed(parts, "Flecha");
                Check(arrow.Role == System.Windows.Forms.AccessibleRole.RadioButton && !string.IsNullOrEmpty(arrow.KeyboardShortcut) && arrow.KeyboardShortcut.Length == 1, "arrow tool: " + arrow.Role + " keys " + arrow.KeyboardShortcut);
                foreach (string n in new string[] { "Minimizar", "Cerrar" }) Check(PartNamed(parts, n).Role == System.Windows.Forms.AccessibleRole.PushButton, n);
                string pick = (arrow.State & System.Windows.Forms.AccessibleStates.Checked) != 0 ? "Recuadro" : "Flecha";
                AccessibleObject other = PartNamed(parts, pick);
                Check((other.State & System.Windows.Forms.AccessibleStates.Checked) == 0, pick + " is already the tool");
                other.DoDefaultAction();
                System.Windows.Forms.Application.DoEvents();
                List<AccessibleObject> now = Parts(a);
                Check((PartNamed(now, pick).State & System.Windows.Forms.AccessibleStates.Checked) != 0, "choosing " + pick + " from a reader did not select it: " + Listed(now));
                int tools = 0;
                foreach (AccessibleObject p in now)
                    if (p.Role == System.Windows.Forms.AccessibleRole.RadioButton && (p.State & System.Windows.Forms.AccessibleStates.Checked) != 0 && Array.IndexOf(new string[] { "Flecha", "Recuadro" }, p.Name) >= 0) tools++;
                Check(tools == 1, tools + " tools checked at once");

                BgPanel bg = (BgPanel)typeof(Editor).GetField("bgPanel", NP).GetValue(ed);
                bg.Size = new System.Drawing.Size(1200, 58);
                List<AccessibleObject> strip = Parts(bg.AccessibilityObject);
                Check(PartNamed(strip, "Sin fondo").Role == System.Windows.Forms.AccessibleRole.RadioButton, "no backdrop option");
                AccessibleObject margin = PartNamed(strip, "Margen");
                Check(margin.Role == System.Windows.Forms.AccessibleRole.Slider && margin.Value == bg.Bg.BgPadding.ToString(), "margin: " + margin.Role + " " + margin.Value);
                margin.Value = "40";
                System.Windows.Forms.Application.DoEvents();
                Check(bg.Bg.BgPadding == 40 && bg.On, "setting the margin from a reader: " + bg.Bg.BgPadding);
                Check(PartNamed(Parts(bg.AccessibilityObject), "Margen").Value == "40", "the slider does not read its new value");
                PartNamed(strip, "Proporci\u00F3n 16:9").DoDefaultAction();
                System.Windows.Forms.Application.DoEvents();
                Check(bg.Bg.BgRatio == "16:9", "choosing a proportion from a reader: " + bg.Bg.BgRatio);
                Check(PartNamed(Parts(bg.AccessibilityObject), "Sombra").Role == System.Windows.Forms.AccessibleRole.CheckButton, "shadow is not a check button");
            }
            finally { ed.Dispose(); img.Dispose(); }
        }

        static void PlayerParts()
        {
            Ds.Apply(1);
            Settings s = NewSettings();
            string noFfmpeg = Path.Combine(Root(), "sin-ffmpeg.exe");   // never exists: the probe fails quietly, nothing runs
            string clip = Path.Combine(Root(), "clip.mp4");
            File.WriteAllBytes(clip, new byte[64]);
            PlayerWindow w = new PlayerWindow(null, s, noFfmpeg, clip, delegate { return null; });
            try
            {
                AccessibleObject a = w.AccessibilityObject;
                List<AccessibleObject> parts = Parts(a);
                Check(parts.Count == 1 && parts[0].Role == System.Windows.Forms.AccessibleRole.StaticText && parts[0].Name == w.Message, "a file that cannot play: " + Listed(parts));
                // A playable file, as the probe would describe it, and the bar laid out for it.
                MediaInfo mi = (MediaInfo)Activator.CreateInstance(typeof(MediaInfo), true);
                mi.Width = 320; mi.Height = 180; mi.Fps = 30; mi.Duration = 75;
                mi.Audio.Add(new AudioTrackInfo { Index = 0 });
                typeof(PlayerWindow).GetField("info", NP).SetValue(w, mi);
                typeof(PlayerWindow).GetField("message", NP).SetValue(w, null);
                w.ClientSize = new System.Drawing.Size(960, 540);
                using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(8, 8))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(b))
                    typeof(PlayerWindow).GetMethod("LayoutBar", NP).Invoke(w, new object[] { g });
                parts = Parts(a);
                AccessibleObject pos = PartNamed(parts, "Posici\u00F3n");
                Check(pos.Role == System.Windows.Forms.AccessibleRole.Slider && pos.Value == "0:00 de 1:15", "position: " + pos.Role + " " + pos.Value);
                AccessibleObject play = PartNamed(parts, "Reproducir");
                Check(play.Role == System.Windows.Forms.AccessibleRole.PushButton && play.KeyboardShortcut == "Espacio", "play: " + play.Role + " " + play.KeyboardShortcut);
                AccessibleObject vol = PartNamed(parts, "Volumen");
                Check(vol.Role == System.Windows.Forms.AccessibleRole.Slider && vol.Value.EndsWith(" %", StringComparison.Ordinal), "volume: " + vol.Value);
                foreach (string n in new string[] { "Silenciar", "Pantalla completa", "Repetir al terminar", "M\u00E1s opciones", "Informaci\u00F3n", "Mostrar en carpeta" })
                    Check(PartNamed(parts, n) != null, n);
                Check(PartNamed(parts, "Informaci\u00F3n").Role == System.Windows.Forms.AccessibleRole.CheckButton, "information is not a check button");
                bool before = (bool)typeof(PlayerWindow).GetField("infoOn", NP).GetValue(w);
                PartNamed(parts, "Informaci\u00F3n").DoDefaultAction();
                System.Windows.Forms.Application.DoEvents();
                Check((bool)typeof(PlayerWindow).GetField("infoOn", NP).GetValue(w) != before, "pressing Informaci\u00F3n from a reader did nothing");
                vol.Value = "40";
                System.Windows.Forms.Application.DoEvents();
                Check(Math.Abs((double)typeof(PlayerWindow).GetField("volume", NP).GetValue(w) - 0.4) < 1e-6, "setting the volume from a reader");
            }
            finally { w.Dispose(); }
        }

        // ---- The document viewer

        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        static object Call(object o, string method, params object[] args)
        {
            foreach (MethodInfo m in o.GetType().GetMethods(NP | BindingFlags.Public))
                if (m.Name == method && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(o, args); }
                    catch (TargetInvocationException e) { throw e.InnerException; }
                }
            throw new InvalidOperationException("no method " + method + "/" + args.Length);
        }

        static bool Until(Func<bool> done, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!done())
            {
                if (sw.ElapsedMilliseconds > ms) return false;
                Pump(20);
            }
            return true;
        }

        static MarkdownWindow NewDoc()
        {
            ConstructorInfo ci = typeof(MarkdownWindow).GetConstructors(NP)[0];
            MarkdownWindow w = (MarkdownWindow)ci.Invoke(new object[] { null });
            typeof(MarkdownWindow).GetField("open", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, w);
            Park(w);
            Pump(150);
            return w;
        }

        static void ShutDoc(MarkdownWindow w)
        {
            Func<string, string, string[], int> keep = DocAlert.Answer;
            DocAlert.Answer = delegate { return 1; };
            try { w.Close(); } catch { }
            DocAlert.Answer = keep;
            Pump(50);
        }

        static string Put(string name, string content)
        {
            string dir = Path.Combine(Root(), "docs");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name);
            File.WriteAllText(p, content, new UTF8Encoding(false));
            return p;
        }

        static void OpenDoc(MarkdownWindow w, string path)
        {
            Call(w, "LoadFile", path);
            Check(Until(delegate { return w.Text != null && w.Doc != null && w.Doc.Path == path; }, 8000), "file did not load: " + path);
            Pump(150);
        }

        const string Md = "# Notas de versi\u00F3n\n\nStackshot **2.1** trae un *editor* de verdad.\n\n## Novedades\n\n- Guardar con `Ctrl+S`\n- [x] Formato\n\n| Tecla | Acci\u00F3n |\n|---|---|\n| Ctrl+B | Negrita |\n\n![Esquema del flujo](esquema.png)\n\n> Una cita.\n";
        const string Soap1 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\">\n  <S:Header><t:Traza xmlns:t=\"urn:traza\">A</t:Traza></S:Header>\n  <S:Body>\n    <ns2:consultaResponse xmlns:ns2=\"urn:prueba\">\n      <return><codigo>0</codigo><nombre>Ana</nombre></return>\n    </ns2:consultaResponse>\n  </S:Body>\n</S:Envelope>\n";

        static void DocPage(int theme, string kind)
        {
            Ds.Apply(theme);
            MarkdownWindow w = NewDoc();
            try
            {
                if (kind.StartsWith("md", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Path.Combine(Root(), "docs"));
                    Png(Path.Combine(Root(), "docs", "esquema.png"), 120, 60, Color.FromRgb(40, 160, 90));
                    OpenDoc(w, Put("notas.md", Md));
                    Pump(250);   // the picture loads off the UI thread
                    if (kind == "md-edit") { w.SetEditing(true); Pump(300); Call(w, "OpenFind", true); Pump(200); }
                    if (kind == "md-outline") { w.SetOutline(true); Pump(250); Check(w.OutlineList != null && w.OutlineList.Items.Count == 2, "outline entries"); }
                }
                else if (kind.StartsWith("xml", StringComparison.Ordinal))
                {
                    string a = Put("tmp1.xml", Soap1);
                    string b = Put("tmp2.xml", Soap1.Replace(">A<", ">B<").Replace("Ana", "Eva"));
                    File.SetLastWriteTime(a, DateTime.Now.AddMinutes(-5));
                    File.SetLastWriteTime(b, DateTime.Now.AddMinutes(-4));
                    OpenDoc(w, b);
                    Check(Until(delegate { return w.Tree != null && w.Tree.Rows.Count > 0 && w.HeaderHost != null && w.HeaderHost.Child != null; }, 8000), "no SOAP bar");
                    if (kind == "xml-edit") { w.SetEditing(true); Pump(400); }
                    if (kind == "xml-compare")
                    {
                        Call(w, "CompareWith", new object[] { null });
                        Check(Until(delegate { return w.Comparing && !w.CompareBusy; }, 8000), "no comparison");
                        Pump(200);
                    }
                }
                if (kind == "md-view")
                {
                    List<Image> pics = new List<Image>();
                    Check(Until(delegate { pics.Clear(); Collect(w, pics); return pics.Count > 0; }, 5000), "the picture never showed in the preview");
                    AutomationPeer pp = Peer(pics[0]);
                    Check(pp != null && pp.GetName() == "Esquema del flujo", "picture in the preview: '" + (pp == null ? "unlisted" : pp.GetName()) + "'");
                }
                Audit au = new Audit();
                au.Walk(w);
                au.Save("viewer-" + kind + "-" + (theme == 1 ? "light" : "dark"));
                Console.WriteLine("    " + au.Summary());
                Check(au.Problems.Count == 0, au.Report());
            }
            finally { ShutDoc(w); Ds.Apply(1); }
        }

        // ---- The automation audit

        sealed class Audit
        {
            public readonly List<string> Problems = new List<string>();
            public int Controls, Focusable;
            readonly HashSet<string> seen = new HashSet<string>();
            static readonly Regex KeySuffix = new Regex(@"(\((Ctrl|Alt|May\u00FAs|Shift|F\d)[^)]*\)|\s{2}\S+)$");

            public string Summary() { return Controls + " controls, " + Focusable + " reached with Tab"; }

            public string Report()
            {
                StringBuilder b = new StringBuilder();
                b.Append(Problems.Count + " problems:");
                foreach (string p in Problems) b.Append("\n      ").Append(p);
                return b.ToString();
            }

            void Problem(string s) { if (seen.Add(s)) Problems.Add(s); }

            public void Walk(UIElement root)
            {
                AutomationPeer p = UIElementAutomationPeer.CreatePeerForElement(root);
                if (p != null) Walk(p, Describe(p), 0);
                else WalkElements(root, root.GetType().Name, 0);   // a plain panel at the top: its children speak
                Clickables(root);
            }

            void WalkElements(DependencyObject o, string path, int depth)
            {
                int n = VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++)
                {
                    DependencyObject c = VisualTreeHelper.GetChild(o, i);
                    UIElement u = c as UIElement;
                    AutomationPeer p = u == null ? null : UIElementAutomationPeer.CreatePeerForElement(u);
                    if (p == null) { WalkElements(c, path, depth); continue; }
                    string d = Describe(p);
                    if (p.IsControlElement()) Tree.Append(' ', depth * 2).Append(d).Append('\n');
                    Inspect(p, path + " > " + d);
                    Walk(p, Short(path) + " > " + d, depth + 1);
                }
            }

            static string Describe(AutomationPeer p)
            {
                return p.GetClassName() + "(" + p.GetAutomationControlType() + " '" + p.GetName() + "')";
            }

            // The tree as readers get it, for a look (written next to the test's files).
            public readonly StringBuilder Tree = new StringBuilder();

            void Walk(AutomationPeer p, string path, int depth)
            {
                if (depth > 60) return;
                List<AutomationPeer> ch = p.GetChildren();
                if (ch == null) return;
                foreach (AutomationPeer c in ch)
                {
                    string d = Describe(c);
                    if (c.IsControlElement())
                    {
                        Tree.Append(' ', depth * 2).Append(d);
                        string help = c.GetHelpText(), keys = c.GetAcceleratorKey();
                        if (!string.IsNullOrEmpty(help)) Tree.Append(" help='").Append(help).Append('\'');
                        if (!string.IsNullOrEmpty(keys)) Tree.Append(" keys='").Append(keys).Append('\'');
                        if (c.IsKeyboardFocusable()) Tree.Append(" [tab]");
                        IToggleProvider tg = c.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                        if (tg != null) Tree.Append(" toggle=").Append(tg.ToggleState);
                        ISelectionItemProvider si = c.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
                        if (si != null) Tree.Append(si.IsSelected ? " selected" : " selectable");
                        if (c.GetPattern(PatternInterface.Invoke) != null) Tree.Append(" invoke");
                        IRangeValueProvider rv = c.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                        if (rv != null) Tree.Append(" range=").Append(rv.Value).Append('/').Append(rv.Maximum);
                        Tree.Append('\n');
                    }
                    Inspect(c, path + " > " + d);
                    Walk(c, Short(path) + " > " + d, depth + 1);
                }
            }

            public void Save(string name)
            {
                string dir = Path.Combine(Root(), "arboles");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".txt"), Tree.ToString(), Encoding.UTF8);
            }

            static string Short(string path)
            {
                int i = path.LastIndexOf(" > ", StringComparison.Ordinal);
                int j = i > 0 ? path.LastIndexOf(" > ", i - 1, StringComparison.Ordinal) : -1;
                return j > 0 ? "\u2026" + path.Substring(j) : path;
            }

            static bool Acts(AutomationPeer c)
            {
                IRangeValueProvider rv = c.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                return c.GetPattern(PatternInterface.Invoke) != null || c.GetPattern(PatternInterface.Toggle) != null || c.GetPattern(PatternInterface.SelectionItem) != null ||
                       (rv != null && !rv.IsReadOnly) || c.GetPattern(PatternInterface.ExpandCollapse) != null;
            }

            void Inspect(AutomationPeer c, string path)
            {
                AutomationControlType t = c.GetAutomationControlType();
                if (t == AutomationControlType.ScrollBar || t == AutomationControlType.Thumb) return;
                UIElementAutomationPeer up = c as UIElementAutomationPeer;
                if (up != null && Unseen(up.Owner)) return;
                string name = c.GetName();
                bool acts = Acts(c), focus = c.IsKeyboardFocusable();
                if (acts || focus) Controls++;
                if (focus) Focusable++;
                if ((acts || focus) && string.IsNullOrEmpty(name) && t != AutomationControlType.Edit && t != AutomationControlType.Document) Problem("no name: " + Short(path));
                if (t == AutomationControlType.Image && string.IsNullOrEmpty(name)) Problem("unnamed picture readers list: " + Short(path));
                if (!string.IsNullOrEmpty(name) && acts && KeySuffix.IsMatch(name)) Problem("shortcut in the name: " + Short(path));
                if (acts && (t == AutomationControlType.Custom || t == AutomationControlType.Pane || t == AutomationControlType.Group || t == AutomationControlType.Text))
                    Problem("acts but is a " + t + ": " + Short(path));
                if (c.GetPattern(PatternInterface.Toggle) != null && t != AutomationControlType.CheckBox && t != AutomationControlType.Button)
                    Problem("toggles but is a " + t + ": " + Short(path));
                if (c.GetPattern(PatternInterface.SelectionItem) != null && t != AutomationControlType.ListItem && t != AutomationControlType.TabItem && t != AutomationControlType.RadioButton && t != AutomationControlType.DataItem && t != AutomationControlType.TreeItem)
                    Problem("selectable but is a " + t + ": " + Short(path));
                if (acts && !focus && c.IsEnabled() && !c.IsOffscreen() && Array.IndexOf(OwnKeys, c.GetClassName()) < 0)
                    Problem("acts but is not reached with Tab: " + Short(path));
                if (focus && !acts && c is TapPeer) Problem("reached with Tab but does nothing: " + Short(path));
            }

            // Off and inside something fully transparent (a message that is not showing): nobody can see or reach it.
            static bool Unseen(UIElement u)
            {
                if (u == null || u.IsEnabled) return false;
                for (DependencyObject d = u; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    UIElement e = d as UIElement;
                    if (e != null && e.Opacity < 0.01) return true;
                }
                return false;
            }

            // Parts the keyboard reaches through their container (its arrows) or through the window itself.
            static readonly string[] OwnKeys = { "MacSegmentedItem", "DockTabsBarItem", "CaptionButton", "MascotHost", "ListBoxItem", "MenuRow" };

            // Surfaces that react to the mouse (a click handler, or a hand cursor of their own) must be something readers can
            // see and press, and the keyboard can reach.
            void Clickables(DependencyObject o)
            {
                FrameworkElement fe = o as FrameworkElement;
                if (fe != null)
                {
                    if (!fe.IsVisible) return;
                    if (Unseen(fe)) return;
                    object cur = fe.ReadLocalValue(FrameworkElement.CursorProperty);
                    bool hand = cur == Cursors.Hand && fe.IsHitTestVisible;
                    string click = Clicks(fe);
                    if (click != null && TitleBar(fe)) click = null;
                    if ((hand || click != null) && !(fe is Drawn))
                    {
                        AutomationPeer by = Covered(fe);
                        if (by == null) Problem("clickable but unknown to readers: " + Where(fe) + (click != null ? " (" + click + ")" : ""));
                        else if (!Reached(fe) && Array.IndexOf(OwnKeys, by.GetClassName()) < 0) Problem("clickable but not reached with Tab: " + Where(fe));
                    }
                }
                int n = VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++) Clickables(VisualTreeHelper.GetChild(o, i));
            }

            static readonly RoutedEvent[] ClickEvents = { UIElement.MouseLeftButtonUpEvent, UIElement.MouseLeftButtonDownEvent, UIElement.MouseUpEvent, UIElement.MouseDownEvent,
                                                          UIElement.PreviewMouseLeftButtonUpEvent, UIElement.PreviewMouseLeftButtonDownEvent };

            // A wide strip along the top of the window with nothing in it: it drags the window, as a title bar does.
            static bool TitleBar(FrameworkElement fe)
            {
                Window w = Window.GetWindow(fe);
                if (w == null || VisualTreeHelper.GetChildrenCount(fe) > 0 && !(fe is Border && ((Border)fe).Child == null)) return false;
                try
                {
                    Point at = fe.TranslatePoint(new Point(0, 0), (UIElement)w.Content);
                    return at.Y < 20 && fe.ActualHeight <= 64 && fe.ActualWidth >= ((FrameworkElement)w.Content).ActualWidth / 2;
                }
                catch (InvalidOperationException) { return false; }
            }

            // The method behind a click handler of the element itself (not its class), when it has one that is not a drag.
            static string Clicks(UIElement u)
            {
                PropertyInfo store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.NonPublic | BindingFlags.Instance);
                object s = store == null ? null : store.GetValue(u, null);
                if (s == null) return null;
                MethodInfo get = s.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (RoutedEvent ev in ClickEvents)
                {
                    RoutedEventHandlerInfo[] hs = (RoutedEventHandlerInfo[])get.Invoke(s, new object[] { ev });
                    if (hs == null) continue;
                    foreach (RoutedEventHandlerInfo h in hs)
                    {
                        string m = h.Handler.Method.Name;
                        if (m.IndexOf("Drag", StringComparison.Ordinal) >= 0) continue;
                        return ev.Name + ":" + m;
                    }
                }
                return null;
            }

            // The element or a near ancestor is a named control with an action, or the element only hands its clicks to a
            // control inside it (a frame that puts the caret in its text box, a slot around a name chip).
            static AutomationPeer Covered(FrameworkElement fe)
            {
                DependencyObject d = fe;
                for (int up = 0; d != null && up < 4; up++, d = VisualTreeHelper.GetParent(d))
                {
                    UIElement u = d as UIElement;
                    if (u == null) continue;
                    AutomationPeer p = UIElementAutomationPeer.CreatePeerForElement(u);
                    if (p != null && p.IsControlElement() && (Acts(p) || u is Selector) && !string.IsNullOrEmpty(p.GetName())) return p;
                }
                return ControlInside(fe, 0);
            }

            static AutomationPeer ControlInside(DependencyObject o, int depth)
            {
                if (depth > 6) return null;
                int n = VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++)
                {
                    DependencyObject c = VisualTreeHelper.GetChild(o, i);
                    UIElement u = c as UIElement;
                    if (u != null)
                    {
                        AutomationPeer p = UIElementAutomationPeer.CreatePeerForElement(u);
                        if (p != null && !string.IsNullOrEmpty(p.GetName()) && (u is TextBoxBase || (p.IsControlElement() && Acts(p)))) return p;
                    }
                    AutomationPeer deeper = ControlInside(c, depth + 1);
                    if (deeper != null) return deeper;
                }
                return null;
            }

            // The element, a near ancestor, or something inside it takes the keyboard focus.
            static bool Reached(FrameworkElement fe)
            {
                DependencyObject d = fe;
                for (int up = 0; d != null && up < 4; up++, d = VisualTreeHelper.GetParent(d))
                {
                    UIElement u = d as UIElement;
                    if (u != null && u.Focusable && u.IsEnabled) return true;
                }
                return FocusableInside(fe, 0);
            }

            static bool FocusableInside(DependencyObject o, int depth)
            {
                if (depth > 6) return false;
                int n = VisualTreeHelper.GetChildrenCount(o);
                for (int i = 0; i < n; i++)
                {
                    DependencyObject c = VisualTreeHelper.GetChild(o, i);
                    UIElement u = c as UIElement;
                    if (u != null && u.Focusable && u.IsVisible) return true;
                    if (FocusableInside(c, depth + 1)) return true;
                }
                return false;
            }

            static string Where(FrameworkElement fe)
            {
                List<string> parts = new List<string>();
                DependencyObject d = fe;
                for (int up = 0; d != null && up < 5; up++, d = VisualTreeHelper.GetParent(d)) parts.Insert(0, d.GetType().Name);
                string text = Access.TextOf(fe);
                string box = "";
                Window w = Window.GetWindow(fe);
                if (w != null && w.Content is UIElement)
                    try { Point at = fe.TranslatePoint(new Point(0, 0), (UIElement)w.Content); box = " at " + (int)at.X + "," + (int)at.Y + " " + (int)fe.ActualWidth + "x" + (int)fe.ActualHeight; }
                    catch (InvalidOperationException) { }
                return string.Join("/", parts.ToArray()) + (text != null ? " '" + text + "'" : "") + (fe.ToolTip is string ? " tip '" + fe.ToolTip + "'" : "") + box;
            }
        }
    }
}

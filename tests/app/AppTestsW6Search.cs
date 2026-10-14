// Round 6 (settings search and section index), suite "w6search": the index follows the rows the pages really build,
// words in any order without accents or case, synonyms, ranking, the results drawn off screen (PNG, light and dark),
// opening a result (page, scroll, glow), Grabacion's quality options, Esc, Ctrl+F routing, no side effects while indexing,
// the first search's time (also typed through the dispatcher) and the section chips, inline and pinned. Windows are built
// and laid out, never shown; no real microphone is reachable; files go only to STACKSHOT_TEST_DATA.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Stackshot.Tests
{
    static class AppTestsW6Search
    {
        const string Suite_ = "w6search";
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        const double Bar = 52, PinH = 42;
        static readonly string[] Pages = { "keys", "general", "editor", "record", "mascotset" };

        static void T(string name, Action body) { AppTests.Test(Suite_, name, body); }
        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        public static void Suite()
        {
            Func<bool> motion = HomeWindow.HomeMotion;
            Func<Func<string>, IMicSource> mic = MicMonitor.NewSource;
            // Nothing glides (final positions at once) and no test may ever reach a real microphone.
            HomeWindow.HomeMotion = delegate { return false; };
            MicMonitor.NewSource = delegate(Func<string> device) { micSources++; return new NoMic(); };
            try
            {
                T("words-fold-accents-and-case", Folding);
                T("synonyms", Synonyms);
                T("every-word-in-any-order", EveryWord);
                T("title-matches-rank-first-and-pages-group", Ranking);
                T("index-covers-every-row-of-every-page", Coverage);
                T("indexing-has-no-side-effects", NoSideEffects);
                T("first-search-timing", Timing);
                T("typing-through-the-dispatcher", Typing);
                T("results-view-light-and-dark", ResultsView);
                T("keyboard-and-opening-a-result", OpenResult);
                T("xml-watch-found-only-with-a-policy-or-a-setup", Advanced);
                T("recording-quality-options", Quality);
                T("esc-returns-to-the-page", Esc);
                T("ctrl-f-routing", CtrlF);
                T("index-follows-changes", Stale);
                T("section-chips-scroll-and-follow", Sections);
                T("section-chips-only-on-long-pages", SectionPages);
            }
            finally
            {
                HomeWindow.HomeMotion = motion;
                MicMonitor.NewSource = mic;
            }
        }

        static int micSources;

        sealed class NoMic : IMicSource
        {
            public MicState Open() { return MicState.NoDevice; }
            public bool Read(MicLevels into) { return false; }
            public bool Moved() { return false; }
            public string Name { get { return ""; } }
            public string Problem { get { return "test"; } }
            public void Close() { }
        }

        // ---- Helpers

        static string Root()
        {
            string root = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string d = Path.Combine(root, "w6search");
            Directory.CreateDirectory(d);
            return d;
        }

        static Settings Fresh()
        {
            Settings s = new Settings();
            s.SaveFolder = Path.Combine(Root(), "guardadas");
            s.ShowRecent = false;
            s.ShowIntro = false;
            return s;
        }

        static HomeWindow Make(int appearance, Settings s)
        {
            Ds.Apply(appearance);
            s.Appearance = appearance;
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            return new HomeWindow(owner, s);
        }

        static void SetPage(HomeWindow hw, string id)
        {
            try { typeof(HomeWindow).GetMethod("SetPage", NP).Invoke(hw, new object[] { id, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("page " + id + ": " + ex.InnerException, ex.InnerException); }
        }

        static void Call(HomeWindow hw, string method)
        {
            try { typeof(HomeWindow).GetMethod(method, NP).Invoke(hw, new object[0]); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException(method + ": " + ex.InnerException, ex.InnerException); }
        }

        static object Field(HomeWindow hw, string name) { return typeof(HomeWindow).GetField(name, NP).GetValue(hw); }
        static void SetField(HomeWindow hw, string name, object v) { typeof(HomeWindow).GetField(name, NP).SetValue(hw, v); }

        static ScrollViewer Scroller(HomeWindow hw) { return (ScrollViewer)Field(hw, "scroller"); }
        static Grid PageHost(HomeWindow hw) { return (Grid)Field(hw, "pageHost"); }
        static StackPanel Body(HomeWindow hw) { return (StackPanel)Field(hw, "body"); }

        static void Layout(HomeWindow hw)
        {
            UIElement root = (UIElement)hw.Content;
            root.Measure(new Size(980, 660));
            root.Arrange(new Rect(0, 0, 980, 660));
            root.UpdateLayout();
        }

        static void Walk(object o, Action<DependencyObject> visit)
        {
            DependencyObject d = o as DependencyObject;
            if (d == null) return;
            visit(d);
            foreach (object c in LogicalTreeHelper.GetChildren(d)) Walk(c, visit);
        }

        static List<HomeWindow.Row> Rows(object root)
        {
            List<HomeWindow.Row> rows = new List<HomeWindow.Row>();
            Walk(root, delegate(DependencyObject d) { HomeWindow.Row r = d as HomeWindow.Row; if (r != null) rows.Add(r); });
            return rows;
        }

        static List<string> Texts(object root)
        {
            List<string> acc = new List<string>();
            Walk(root, delegate(DependencyObject d)
            {
                TextBlock t = d as TextBlock;
                if (t == null || t.Visibility != Visibility.Visible) return;
                // Text written as runs (the marked results) is not in Text.
                string s = t.Text ?? "";
                if (s.Length == 0) foreach (Inline i in t.Inlines) { Run r = i as Run; if (r != null) s += r.Text; }
                acc.Add(s);
            });
            return acc;
        }

        static bool Has(List<string> texts, string part)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // The highlighted parts of the results.
        static List<string> Marks(object root)
        {
            List<string> acc = new List<string>();
            Walk(root, delegate(DependencyObject d)
            {
                TextBlock t = d as TextBlock;
                if (t == null) return;
                foreach (Inline i in t.Inlines)
                {
                    Run r = i as Run;
                    if (r != null && r.Background != null) acc.Add(r.Text);
                }
            });
            return acc;
        }

        // The titles of the Grabacion page's widgets and tiles, as drawn: semibold 13 or 13.5 in the text font.
        static List<string> WidgetTitles(object root)
        {
            List<string> acc = new List<string>();
            Walk(root, delegate(DependencyObject d)
            {
                TextBlock t = d as TextBlock;
                if (t == null || t.FontWeight != FontWeights.SemiBold || t.FontFamily != Ds.Text) return;
                if (Math.Abs(t.FontSize - 13) < 0.01 || Math.Abs(t.FontSize - 13.5) < 0.01) acc.Add(t.Text);
            });
            return acc;
        }

        static double TopIn(FrameworkElement e, Visual host) { return e.TranslatePoint(new Point(0, 0), (UIElement)host).Y; }

        static HomeWindow.Row RowTitled(HomeWindow hw, string title)
        {
            foreach (HomeWindow.Row r in Rows(Body(hw))) if (r.Title == title) return r;
            return null;
        }

        static void Shot(HomeWindow hw, string file)
        {
            Layout(hw);
            string dir = Path.Combine(Root(), "shots");
            Directory.CreateDirectory(dir);
            DrawingVisual dv = new DrawingVisual();
            // The window's own background is not part of its content; without Mica behind it, the sidebar's colour.
            using (DrawingContext dc = dv.RenderOpen()) dc.DrawRectangle(Ds.Brush(Ds.Brushes.Sidebar), null, new Rect(0, 0, 980, 660));
            RenderTargetBitmap rtb = new RenderTargetBitmap(980, 660, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Render((Visual)hw.Content);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(Path.Combine(dir, file))) enc.Save(fs);
        }

        // A key as WPF routes it, from the window down to target, with a keyboard whose modifiers the test chooses.
        sealed class FakeKeys : KeyboardDevice
        {
            readonly ModifierKeys mods;
            public FakeKeys(ModifierKeys m) : base(InputManager.Current) { mods = m; }
            protected override KeyStates GetKeyStatesFromSystem(Key key)
            {
                ModifierKeys need = key == Key.LeftCtrl ? ModifierKeys.Control : key == Key.LeftShift ? ModifierKeys.Shift : key == Key.LeftAlt ? ModifierKeys.Alt : ModifierKeys.None;
                return need != ModifierKeys.None && (mods & need) != 0 ? KeyStates.Down : KeyStates.None;
            }
        }

        sealed class FakeSource : PresentationSource
        {
            Visual root;
            public override Visual RootVisual { get { return root; } set { root = value; } }
            public override bool IsDisposed { get { return false; } }
            protected override CompositionTarget GetCompositionTargetCore() { return null; }
        }

        static readonly Dictionary<ModifierKeys, FakeKeys> keyboards = new Dictionary<ModifierKeys, FakeKeys>();
        static FakeSource source;

        static bool Press(UIElement target, Key k, ModifierKeys mods)
        {
            FakeKeys kb;
            if (!keyboards.TryGetValue(mods, out kb)) { kb = new FakeKeys(mods); keyboards[mods] = kb; }
            if (source == null) source = new FakeSource();
            KeyEventArgs e = new KeyEventArgs(kb, source, 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e);
            return e.Handled;
        }

        static List<SearchEntry> Index(HomeWindow hw) { return hw.SearchIndexNow(null); }

        static SearchEntry Entry(List<SearchEntry> index, string page, string title)
        {
            foreach (SearchEntry e in index) if (e.Page == page && e.Title == title) return e;
            return null;
        }

        static string Titles(List<SearchHit> hits, int max)
        {
            List<string> l = new List<string>();
            for (int i = 0; i < hits.Count && i < max; i++) l.Add(hits[i].Entry.Page + ":" + hits[i].Entry.Title);
            return string.Join(" | ", l.ToArray());
        }

        static bool Finds(List<SearchHit> hits, string title)
        {
            foreach (SearchHit h in hits) if (h.Entry.Title == title) return true;
            return false;
        }

        // ---- Matching (on the real index)

        static List<SearchEntry> shared;

        static List<SearchEntry> SharedIndex()
        {
            if (shared != null) return shared;
            HomeWindow hw = Make(1, Fresh());
            shared = Index(hw);
            hw.Shutdown();
            return shared;
        }

        static void Folding()
        {
            List<SearchEntry> ix = SharedIndex();
            string[] same = { "microfono", "MICR\u00D3FONO", "Micr\u00F3fono", "  mIcRoFoNo  " };
            string first = null;
            foreach (string q in same)
            {
                List<SearchHit> h = SettingsSearch.Find(ix, q);
                Check(h.Count > 0, "<" + q + "> finds nothing");
                string t = Titles(h, 99);
                if (first == null) first = t;
                Check(t == first, "<" + q + "> differs: " + t + " vs " + first);
            }
            Check(Finds(SettingsSearch.Find(ix, "camara"), "Sonido de c\u00E1mara"), "camara without accent");
            Check(Finds(SettingsSearch.Find(ix, "SONIDO DE C\u00C1MARA"), "Sonido de c\u00E1mara"), "capitals with accent");
            Check(SettingsSearch.Find(ix, "").Count == 0 && SettingsSearch.Find(ix, "  , ;").Count == 0, "nothing typed finds nothing");
            string[] w = SettingsSearch.Words("\u00ABCarpeta\u00BB de las Capturas, guardadas.");
            Check(string.Join("|", w) == "carpeta|capturas|guardadas", "words: " + string.Join("|", w));
            Check(string.Join("|", SettingsSearch.Words("de la")) == "de|la", "only small words: kept");
        }

        static void Synonyms()
        {
            List<SearchEntry> ix = SharedIndex();
            string[,] pairs =
            {
                { "cursor", "Incluir el puntero" }, { "rat\u00F3n", "Incluir el puntero" }, { "puntero", "Incluir el puntero" },
                { "tecla", "Capturar un \u00E1rea" }, { "combinaci\u00F3n", "Grabar v\u00EDdeo" }, { "atajo", "Copiar texto de la pantalla" },
                { "micro", "Micr\u00F3fono" }, { "voz", "Micr\u00F3fono" },
                { "audio", "Sonido de c\u00E1mara" }, { "audio", "Sonido del equipo" },
                { "privacidad", "Tapar los datos sensibles al capturar" },
                { "oscuro", "Tema" }, { "claro", "Tema" },
                { "versi\u00F3n", "Buscar actualizaciones" }, { "actualizar", "Instalar actualizaciones autom\u00E1ticamente" },
                { "guardar", "Subcarpetas" }, { "webcam", "C\u00E1mara" }
            };
            for (int i = 0; i < pairs.GetLength(0); i++)
            {
                List<SearchHit> h = SettingsSearch.Find(ix, pairs[i, 0]);
                Check(Finds(h, pairs[i, 1]), "<" + pairs[i, 0] + "> does not find <" + pairs[i, 1] + ">: " + Titles(h, 8));
            }
            // A word found through a synonym is marked where the synonym is.
            List<SearchHit> c = SettingsSearch.Find(ix, "cursor");
            foreach (SearchHit h in c)
                if (h.Entry.Title == "Incluir el puntero")
                    Check(h.TitleMarks.Count == 1 && h.Entry.Title.Substring(h.TitleMarks[0].Key, h.TitleMarks[0].Value) == "puntero", "mark of the synonym");
            // The folder of the captures, by what people type for it.
            List<SearchHit> f = SettingsSearch.Find(ix, "carpeta");
            bool folder = false;
            foreach (SearchHit h in f) if (h.Entry.Group == "Capturas guardadas" && h.Entry.Icon == "folder") folder = true;
            Check(folder, "carpeta finds the save folder: " + Titles(f, 8));
        }

        static void EveryWord()
        {
            List<SearchEntry> ix = SharedIndex();
            List<SearchHit> a = SettingsSearch.Find(ix, "sonido camara"), b = SettingsSearch.Find(ix, "c\u00E1mara SONIDO");
            Check(a.Count > 0 && a[0].Entry.Title == "Sonido de c\u00E1mara", "two words: " + Titles(a, 5));
            Check(Titles(a, 99) == Titles(b, 99), "any order: " + Titles(b, 5));
            List<SearchHit> c = SettingsSearch.Find(ix, "puntero capturar");
            Check(c.Count > 0 && c[0].Entry.Title == "Incluir el puntero", "a word in the group: " + Titles(c, 5));
            Check(SettingsSearch.Find(ix, "puntero zzqq").Count == 0, "a word nowhere leaves nothing");
            List<SearchHit> d = SettingsSearch.Find(ix, "mostrar mascota");
            Check(d.Count > 0 && d[0].Entry.Title == "Mostrar la mascota", "mostrar mascota: " + Titles(d, 5));
            foreach (SearchHit h in d)
            {
                string all = h.Entry.FTitle + " " + h.Entry.FSub + " " + h.Entry.FGroup + " " + h.Entry.FPage + " " + h.Entry.FExtra;
                Check(all.Contains("mostr") && all.Contains("mascota"), "every word must be there: " + h.Entry.Title);
            }
            // Words that only say what to do with a setting are left out, unless they are all there is.
            List<SearchHit> dark = SettingsSearch.Find(ix, "modo oscuro"), mic = SettingsSearch.Find(ix, "Activar el micro"), folder = SettingsSearch.Find(ix, "cambiar carpeta");
            Check(dark.Count > 0 && dark[0].Entry.Title == "Tema", "modo oscuro: " + Titles(dark, 3));
            Check(mic.Count > 0 && mic[0].Entry.Title == "Micr\u00F3fono", "activar el micro: " + Titles(mic, 3));
            bool saveFolder = false;
            foreach (SearchHit h in folder) if (h.Entry.Group == "Capturas guardadas" && h.Entry.Icon == "folder") saveFolder = true;
            Check(saveFolder, "cambiar carpeta: " + Titles(folder, 5));
            Check(string.Join("|", SettingsSearch.Words("Ajustes")) == "ajustes" && SettingsSearch.Find(ix, "ajustes").Count > 0, "a filler word alone is searched");
        }

        static void Ranking()
        {
            List<SearchEntry> ix = SharedIndex();
            foreach (string q in new string[] { "puntero", "copiar", "sonido", "carpeta", "fondo" })
            {
                List<SearchHit> h = SettingsSearch.Find(ix, q);
                Check(h.Count > 0, q + ": nothing");
                // Pages come in one run each.
                List<string> seen = new List<string>();
                for (int i = 0; i < h.Count; i++)
                {
                    if (i > 0 && h[i].Entry.Page == h[i - 1].Entry.Page) continue;
                    Check(!seen.Contains(h[i].Entry.Page), q + ": page " + h[i].Entry.Page + " comes twice: " + Titles(h, 20));
                    seen.Add(h[i].Entry.Page);
                }
                // In each page, a title that has the word before one that only says it below.
                string folded = HistoryIndex.Fold(q);
                for (int i = 1; i < h.Count; i++)
                {
                    if (h[i].Entry.Page != h[i - 1].Entry.Page) continue;
                    bool prev = h[i - 1].Entry.FTitle.Contains(folded), cur = h[i].Entry.FTitle.Contains(folded);
                    Check(prev || !cur, q + ": <" + h[i].Entry.Title + "> (title) after <" + h[i - 1].Entry.Title + ">: " + Titles(h, 20));
                }
                Check(h[0].Entry.FTitle.Contains(folded), q + ": the first result has it in its title: " + Titles(h, 5));
            }
            List<SearchHit> p = SettingsSearch.Find(ix, "puntero");
            Check(p[0].Entry.Title == "Incluir el puntero" && p[0].Entry.Page == "general", "puntero first: " + Titles(p, 5));
        }

        // ---- The index follows the pages

        static void Coverage()
        {
            Settings s = Fresh();
            HomeWindow hw = Make(1, s);
            List<SearchEntry> ix = Index(hw);
            Console.WriteLine("    " + ix.Count + " entries");
            foreach (string id in Pages)
            {
                SetPage(hw, id);
                Layout(hw);
                List<string> onPage = new List<string>();
                foreach (HomeWindow.Row r in Rows(Body(hw))) if (!string.IsNullOrEmpty(r.Title) && r.Visibility == Visibility.Visible) onPage.Add(r.Title.Trim());
                if (id == "record") onPage.AddRange(WidgetTitles(Body(hw)));
                Check(onPage.Count >= 3, id + ": the walk found " + onPage.Count + " rows");
                foreach (string t in onPage) Check(Entry(ix, id, t) != null, id + ": <" + t + "> is on the page but not in the index");
                List<string> shown = ShownTexts(Body(hw));
                int mine = 0;
                foreach (SearchEntry e in ix)
                {
                    if (e.Page != id) continue;
                    mine++;
                    Check(e.Reveal != null || onPage.Contains(e.Title) || shown.Contains(e.Title), id + ": the index has <" + e.Title + "> that the page does not show");
                    Check(e.PageName == HomeWindow.PageTitle(id), id + ": page name " + e.PageName);
                }
                Console.WriteLine("    " + id + ": " + onPage.Count + " on the page, " + mine + " in the index");
            }
            // Every Atajos action, with its combination as the description.
            int actions = 0;
            foreach (SearchEntry e in ix) if (e.Page == "keys" && e.Extra != null) actions++;
            Check(actions == Settings.Actions.Length, "Atajos actions in the index: " + actions);
            SearchEntry region = Entry(ix, "keys", "Capturar un \u00E1rea");
            List<string> combos = new List<string>();
            foreach (string c in Hotkeys.Split(s.HotRegion)) combos.Add(Hotkeys.DisplayOne(c));
            string expected = combos.Count > 0 ? string.Join(", ", combos.ToArray()) : "Sin atajo";
            Check(region != null && region.Sub == expected, "Capturar un \u00E1rea says its combination: " + (region != null ? region.Sub : null) + " vs " + expected);
            // Groups and places.
            SearchEntry pointer = Entry(ix, "general", "Incluir el puntero");
            Check(pointer != null && pointer.Group == "Al capturar" && pointer.Icon == "cursor", "Incluir el puntero lives in General > Al capturar");
            SearchEntry mic = Entry(ix, "record", "Micr\u00F3fono");
            Check(mic != null && mic.Group == "Sonido", "Micr\u00F3fono lives in Grabaci\u00F3n > Sonido");
            SearchEntry talk = Entry(ix, "mascotset", "Saludos y consejos");
            Check(talk != null && talk.Group == "Car\u00E1cter" && talk.PageName == "Ajustes de la mascota", "the mascot's settings");
            // Grabacion's quality cards, and widgets described by what they say further down.
            foreach (string q in new string[] { "Est\u00E1ndar", "Alta", "M\u00E1xima", "Cine" })
            {
                SearchEntry card = Entry(ix, "record", q);
                Check(card != null && card.Group == "Calidad" && card.Sub != null && card.Icon != null, "the quality card " + q);
            }
            SearchEntry gif = Entry(ix, "record", "GIF"), engine = Entry(ix, "record", "Motor de v\u00EDdeo");
            Check(gif != null && gif.Sub != null && gif.Sub.Contains("15") && gif.Extra != null && gif.Extra.Contains("fotogramas"), "the GIF widget's words");
            Check(engine != null && engine.Sub != null, "the engine widget's line");
            hw.Shutdown();
        }

        // The texts the page shows (nothing folded away on the way up).
        static List<string> ShownTexts(object root)
        {
            List<string> acc = new List<string>();
            Walk(root, delegate(DependencyObject d)
            {
                TextBlock t = d as TextBlock;
                if (t == null) return;
                // Up to the window, which is never shown here.
                for (DependencyObject u = t; u != null && !(u is Window); u = LogicalTreeHelper.GetParent(u))
                {
                    UIElement e = u as UIElement;
                    if (e != null && e.Visibility != Visibility.Visible) return;
                }
                acc.Add(t.Text);
            });
            return acc;
        }

        // Every field of the window, by name (the search index's own left out).
        static Dictionary<string, object> State(HomeWindow hw)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (FieldInfo f in typeof(HomeWindow).GetFields(NP | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (f.Name.StartsWith("searchIndex") || f.Name.StartsWith("searchJob") || f.Name == "searchPartial" || f.Name == "SearchIndexBuilds") continue;
                d[f.Name] = f.GetValue(hw);
            }
            return d;
        }

        static string SettingsText(Settings s)
        {
            List<string> l = new List<string>();
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) l.Add(f.Name + "=" + f.GetValue(s));
            return string.Join("\n", l.ToArray());
        }

        static int RunningTimers()
        {
            FieldInfo f = typeof(Dispatcher).GetField("_timers", NP);
            if (f == null) return -1;
            System.Collections.ICollection c = f.GetValue(Dispatcher.CurrentDispatcher) as System.Collections.ICollection;
            return c == null ? -1 : c.Count;
        }

        static void NoSideEffects()
        {
            Settings s = Fresh();
            HomeWindow hw = Make(2, s);
            // A live page with the microphone test, so an index build that moved it or opened it would show.
            SetPage(hw, "record");
            Layout(hw);
            object micTest = Field(hw, "recMicTest");
            Check(micTest != null, "the record page has its microphone test");
            DependencyObject micParent = ((FrameworkElement)micTest).Parent;
            int hostChildren = PageHost(hw).Children.Count, bodyChildren = Body(hw).Children.Count;
            Dictionary<string, object> before = State(hw);
            string settingsBefore = SettingsText(s);
            int sources = micSources, timers = RunningTimers();
            bool history = HistoryIndex.Running;
            hw.SearchIndexNow(null);
            Dictionary<string, object> after = State(hw);
            foreach (KeyValuePair<string, object> kv in before)
                Check(object.Equals(kv.Value, after[kv.Key]), "the window's field " + kv.Key + " changed: <" + kv.Value + "> -> <" + after[kv.Key] + ">");
            Check(SettingsText(s) == settingsBefore, "a setting changed while indexing");
            Check(micSources == sources, "a microphone source was made");
            Check(Field(hw, "recMicTest") == micTest && ((FrameworkElement)micTest).Parent == micParent, "the microphone test moved");
            Check(!((MicCheck)micTest).Active && !((MicCheck)micTest).Monitor.Running, "the microphone test runs");
            Check(HistoryIndex.Running == history && (int)Field(hw, "historyLoads") == 0 && !(bool)Field(hw, "historyHooked"), "the history started");
            Check(Field(hw, "tryTimer") == null, "the backdrop preview timer was made");
            if (timers >= 0) Check(RunningTimers() == timers, "timers running: " + timers + " -> " + RunningTimers());
            else Console.WriteLine("    (the dispatcher's timer list is not reachable here: not counted)");
            Check(PageHost(hw).Children.Count == hostChildren && Body(hw).Children.Count == bodyChildren, "the page on show changed");
            Check(Field(hw, "page") as string == "record" && Field(hw, "body") == Body(hw), "the page or body changed");
            // The record page on show still works on its own controls.
            object cards = Field(hw, "recCards");
            Check(cards != null && ((Array)cards).Length == 4, "the quality cards");
            // The editor on show with a backdrop being tried under the mouse (its short timer running): left alone.
            SetPage(hw, "editor");
            Layout(hw);
            typeof(HomeWindow).GetMethod("TryBackdrop", NP).Invoke(hw, new object[] { 1 });
            DispatcherTimer trying = (DispatcherTimer)Field(hw, "tryTimer");
            Check(trying != null && trying.IsEnabled, "a backdrop is being tried");
            hw.SearchIndexNow(null);
            Check(trying.IsEnabled && (int)Field(hw, "tryPreset") == 1 && Field(hw, "tryTimer") == trying, "the backdrop being tried was stopped by indexing");
            trying.Stop();
            hw.Shutdown();
        }

        static double Median(List<double> l)
        {
            List<double> s = new List<double>(l);
            s.Sort();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }

        static void Timing()
        {
            // The very first build in this run includes compiling the pages' code (as the first search after starting
            // Stackshot would, for pages not visited yet); then a fresh window, as any later first search.
            List<double> cold = new List<double>(), warm = new List<double>();
            HomeWindow a = Make(1, Fresh());
            a.SearchIndexNow(cold);
            a.Shutdown();
            HomeWindow hw = Make(1, Fresh());
            Stopwatch sw = Stopwatch.StartNew();
            List<SearchEntry> ix = hw.SearchIndexNow(warm);
            double build = sw.Elapsed.TotalMilliseconds;
            List<double> queries = new List<double>();
            foreach (string q in new string[] { "p", "pu", "pun", "puntero", "carpeta de capturas", "sonido", "zzzz", "a" })
            {
                sw.Restart();
                SettingsSearch.Find(ix, q);
                queries.Add(sw.Elapsed.TotalMilliseconds);
            }
            // The results drawn and laid out, against the same for the Atajos page (nine rows) on this machine now: the
            // machine may be busy, so medians of a few rounds.
            List<double> results = new List<double>(), page = new List<double>();
            for (int round = 0; round < 3; round++)
            {
                SetPage(hw, "home");
                Layout(hw);
                sw.Restart();
                SetPage(hw, "keys");
                Layout(hw);
                page.Add(sw.Elapsed.TotalMilliseconds);
            }
            // Building a page makes the index stale: one build, then searches alone reuse it. Each search as typing gets
            // it: the first screenful at once (timed, laid out as the window would), the rest a few at a time below
            // input, each turn timed the same way. One letter that most settings have is the most there is to draw.
            hw.SearchIndexNow(null);
            int builds = hw.SearchIndexBuilds;
            List<double> steps = new List<double>(), most = new List<double>();
            MethodInfo run = typeof(HomeWindow).GetMethod("SearchRun", NP), draw = typeof(HomeWindow).GetMethod("SearchDrawRows", NP);
            string[] words = { "captura", "sonido", "mascota", "carpeta", "tema", "a", "a", "a" };
            for (int round = 0; round < words.Length; round++)
            {
                hw.SearchBox.Text = words[round];
                sw.Restart();
                run.Invoke(hw, new object[0]);
                Layout(hw);
                (words[round] == "a" ? most : results).Add(sw.Elapsed.TotalMilliseconds);
                for (int guard = 0; Field(hw, "searchPending") != null && guard < 20; guard++)
                {
                    sw.Restart();
                    draw.Invoke(hw, new object[] { (int)typeof(HomeWindow).GetField("SearchMoreRows", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) });
                    Layout(hw);
                    steps.Add(sw.Elapsed.TotalMilliseconds);
                }
                Check(hw.SearchResults.Count == Math.Min(50, SettingsSearch.Find(ix, words[round]).Count), words[round] + ": every result drawn in the end (" + hw.SearchResults.Count + ")");
                hw.SearchBox.Text = "";
                Layout(hw);
            }
            Check(hw.SearchIndexBuilds == builds, "the index was built again by a search");
            Func<List<double>, string> fmt = delegate(List<double> l) { List<string> r = new List<string>(); foreach (double d in l) r.Add(d.ToString("0.0")); return string.Join(" / ", r.ToArray()); };
            Console.WriteLine("    pages (keys/general/editor/record/mascotset), first build: " + fmt(cold) + " ms; fresh window: " + fmt(warm) + " ms (" + build.ToString("0") + " ms in all)");
            Console.WriteLine("    queries: " + fmt(queries) + " ms");
            Console.WriteLine("    results, first screenful drawn and laid out: " + fmt(results) + " ms; \u00ABa\u00BB (" + SettingsSearch.Find(ix, "a").Count + " results): " + fmt(most) +
                              " ms; the rest, " + steps.Count + " turns: " + fmt(steps) + " ms; the Atajos page for comparison: " + fmt(page) + " ms");
            // Built a page per turn of the UI thread: each turn is what the UI waits for.
            foreach (double d in warm) Check(d < 100, "an index step took " + d.ToString("0") + " ms");
            foreach (double d in queries) Check(d < 16, "a query took " + d.ToString("0.0") + " ms");
            double basis = Median(page);
            foreach (List<double> l in new List<double>[] { results, most, steps })
                Check(Median(l) < 50 || Median(l) < 1.5 * basis, "drawing results held the UI thread " + Median(l).ToString("0") + " ms (Atajos " + basis.ToString("0") + " ms)");
            hw.Shutdown();
        }

        // Runs the dispatcher until done() or ms have passed, as other suites do.
        static void Pump(Func<bool> done, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!done() && sw.ElapsedMilliseconds < ms)
            {
                DispatcherFrame f = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { f.Continue = false; });
                Dispatcher.PushFrame(f);
                if (!done()) System.Threading.Thread.Sleep(5);
            }
        }

        // Typing as the window gets it: the index made a page per turn below input, the debounce, the first screenful and
        // the rest drawn a few at a time. The longest turn of the UI thread is reported (layout is not part of it here:
        // the window is never shown).
        static void Typing()
        {
            HomeWindow hw = Make(1, Fresh());
            SetPage(hw, "general");
            Layout(hw);
            Check(hw.SearchIndexBuilds == 0, "no index before the first search");
            // What other tests left queued runs first, so the turns measured are this search's own.
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, (Action)delegate { });
            double longest = 0;
            Stopwatch op = new Stopwatch();
            DispatcherHookEventHandler started = delegate { op.Restart(); }, completed = delegate { longest = Math.Max(longest, op.Elapsed.TotalMilliseconds); };
            Dispatcher.CurrentDispatcher.Hooks.OperationStarted += started;
            Dispatcher.CurrentDispatcher.Hooks.OperationCompleted += completed;
            try
            {
                hw.SearchBox.Text = "a";
                Check(!hw.SearchShowing, "nothing before the pause in typing");
                Pump(delegate { return hw.SearchShowing && Field(hw, "searchPending") == null && hw.SearchIndexBuilds == 1; }, 10000);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.Hooks.OperationStarted -= started;
                Dispatcher.CurrentDispatcher.Hooks.OperationCompleted -= completed;
            }
            Console.WriteLine("    longest turn of the UI thread while the index was made and the results drawn: " + longest.ToString("0.0") + " ms");
            Check(hw.SearchShowing && hw.SearchIndexBuilds == 1, "results with the index made once");
            Check(longest < 100, "a turn of the UI thread took " + longest.ToString("0") + " ms");
            List<SearchEntry> ix = (List<SearchEntry>)Field(hw, "searchIndex");
            Check(hw.SearchResults.Count == Math.Min(50, SettingsSearch.Find(ix, "a").Count), "every result drawn: " + hw.SearchResults.Count);
            Check(Field(hw, "searchPartial") == null && (int)Field(hw, "searchJobNext") == -1, "the index job is over");
            // Another word replaces what is still being drawn.
            hw.SearchBox.Text = "sonido camara";
            Pump(delegate { return hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Sonido de c\u00E1mara" && Field(hw, "searchPending") == null; }, 5000);
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Sonido de c\u00E1mara", "the new words: " + Titles(hw.SearchResults, 3));
            Check(hw.SearchIndexBuilds == 1, "the index is kept while typing");
            hw.SearchBox.Text = "";
            Check(!hw.SearchShowing, "an empty field closes the results");
            hw.Shutdown();
        }

        // ---- The results view

        static void ResultsView()
        {
            foreach (int appearance in new int[] { 1, 2 })
            {
                string theme = appearance == 2 ? "oscuro" : "claro";
                HomeWindow hw = Make(appearance, Fresh());
                SetPage(hw, "general");
                Layout(hw);
                hw.SearchFor("captura");
                Layout(hw);
                Check(hw.SearchShowing, theme + ": no results view");
                Check(Scroller(hw).Visibility == Visibility.Hidden, theme + ": the page still shows under the results");
                object view = Field(hw, "searchView");
                List<string> t = Texts(view);
                foreach (string want in new string[] { "Resultados", "ajustes coinciden con \u00ABcaptura\u00BB", "General\u00A0\u203A\u00A0Al capturar", "Copiar cada captura" })
                    Check(Has(t, want), theme + ": no text <" + want + ">: " + string.Join(" | ", t.ToArray()));
                List<string> marks = Marks(view);
                Check(marks.Contains("captura") || marks.Contains("Captura"), theme + ": the word is not marked: " + string.Join(" | ", marks.ToArray()));
                List<SearchHit> hits = hw.SearchResults;
                Check(hits.Count > 3 && hw.SearchSelected == 0, theme + ": results and the first chosen");
                Shot(hw, "busqueda-" + theme + ".png");
                // Nothing.
                hw.SearchFor("zzqq xxw");
                Layout(hw);
                t = Texts(view);
                Check(Has(t, "No hay ajustes que coincidan con \u00ABzzqq xxw\u00BB"), theme + ": the empty message");
                Check(Has(t, "Prueba con otras palabras"), theme + ": the hint");
                Check(hw.SearchResults.Count == 0 && hw.SearchSelected == -1, theme + ": nothing to choose");
                Shot(hw, "busqueda-vacia-" + theme + ".png");
                // The toolbar names the results once they scroll under it, and fades back.
                hw.SearchFor("a");
                Layout(hw);
                ScrollViewer rs = (ScrollViewer)Field(hw, "searchScroller");
                rs.ScrollToVerticalOffset(200);
                Layout(hw);
                Border bar = (Border)Field(hw, "toolbar");
                TextBlock title = (TextBlock)Field(hw, "toolbarTitle");
                Check(title.Text == "Resultados" && title.Opacity > 0.99 && ((SolidColorBrush)bar.Background).Color.A == 245, theme + ": the toolbar over scrolled results");
                hw.SearchHide();
                Layout(hw);
                Check(title.Text == "General" && Scroller(hw).Visibility == Visibility.Visible, theme + ": back to the page");
                hw.Shutdown();
            }
        }

        static void OpenResult()
        {
            HomeWindow hw = Make(1, Fresh());
            SetPage(hw, "keys");
            Layout(hw);
            hw.SearchFor("puntero");
            Layout(hw);
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Incluir el puntero", "first: " + Titles(hw.SearchResults, 4));
            // Down and Up move the choice; Enter opens it.
            TextBox box = hw.SearchBox;
            int n = hw.SearchResults.Count;
            Check(Press(box, Key.Down, ModifierKeys.None), "Down handled");
            Check(hw.SearchSelected == Math.Min(1, n - 1), "Down moves to " + hw.SearchSelected);
            Check(Press(box, Key.Up, ModifierKeys.None) && hw.SearchSelected == 0, "Up comes back");
            Press(box, Key.Up, ModifierKeys.None);
            Check(hw.SearchSelected == 0, "Up stops at the first");
            Check(Press(box, Key.Enter, ModifierKeys.None), "Enter handled");
            Layout(hw);
            Check(!hw.SearchShowing && hw.SearchPage == "general", "Enter opened General: " + hw.SearchPage);
            Check(box.Text == "puntero", "the field keeps the words");
            HomeWindow.Row row = RowTitled(hw, "Incluir el puntero");
            Check(row != null, "the row on the page");
            InView(hw, row, "Incluir el puntero");
            FrameworkElement glow = hw.SearchGlowView;
            Check(glow != null && VisualTreeHelper.GetParent(glow) == PageHost(hw), "the row glows");
            double gy = TopIn(glow, PageHost(hw)), ry = TopIn(row, PageHost(hw));
            Check(Math.Abs(gy - ry) < 1 && Math.Abs(glow.ActualHeight - row.ActualHeight) < 1 && glow.ActualWidth > row.ActualWidth, "the glow covers the row: " + gy + " vs " + ry);
            Settle(hw);
            Shot(hw, "abierto-puntero-claro.png");
            // A row of another page, opened by a click, and a widget tile of Grabacion.
            hw.SearchFor("micr\u00F3fono");
            Check(hw.SearchResults[0].Entry.Title == "Micr\u00F3fono", "micr\u00F3fono first: " + Titles(hw.SearchResults, 3));
            hw.SearchOpen(0);
            Layout(hw);
            Check(hw.SearchPage == "record", "opened Grabaci\u00F3n");
            FrameworkElement tile = (FrameworkElement)hw.SearchGlowView;
            Check(tile != null, "the tile glows");
            List<string> tt = Texts(PageHost(hw));
            Check(Has(tt, "Micr\u00F3fono"), "the tile is there");
            // Same page: no rebuild, it only glides.
            object bodyBefore = Body(hw);
            hw.SearchFor("cuenta atr\u00E1s");
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Cuenta atr\u00E1s", "cuenta atr\u00E1s: " + Titles(hw.SearchResults, 3));
            hw.SearchOpen(0);
            Layout(hw);
            Check(Body(hw) == bodyBefore, "the same page was rebuilt");
            hw.Shutdown();
        }

        // A page that came in with its fade and rise (nothing pumps the dispatcher here) as it ends up, for the PNG.
        static void Settle(HomeWindow hw)
        {
            Grid host = PageHost(hw);
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.Opacity = 1;
            host.RenderTransform = Transform.Identity;
            Layout(hw);
        }

        // The glow sits over the element, a few pixels out all round.
        static void Glows(HomeWindow hw, FrameworkElement e, string what)
        {
            FrameworkElement glow = hw.SearchGlowView;
            Check(glow != null && VisualTreeHelper.GetParent(glow) == PageHost(hw), what + ": nothing glows");
            Rect g = new Rect(glow.TranslatePoint(new Point(0, 0), PageHost(hw)), new Size(glow.ActualWidth, glow.ActualHeight));
            Rect r = new Rect(e.TranslatePoint(new Point(0, 0), PageHost(hw)), new Size(e.ActualWidth, e.ActualHeight));
            Check(g.Contains(r) && g.Width - r.Width < 20 && g.Height - r.Height < 10, what + ": the glow " + g + " is not over " + r);
        }

        // The element is in view, below the toolbar (and the pinned chips), centred unless the page ends first.
        static void InView(HomeWindow hw, FrameworkElement e, string what)
        {
            ScrollViewer sc = Scroller(hw);
            double off = sc.VerticalOffset, top = TopIn(e, PageHost(hw)), h = e.ActualHeight, view = sc.ViewportHeight;
            double cover = Bar + (hw.SectionTitles.Count > 0 ? PinH : 0);
            Console.WriteLine("    " + what + ": offset " + off.ToString("0") + ", row at " + (top - off).ToString("0") + " of " + view.ToString("0"));
            Check(top - off >= cover - 0.5 && top + h - off <= view + 0.5, what + ": not in view (offset " + off + ", top " + top + ")");
            double centre = (cover + view) / 2, at = top + h / 2 - off;
            bool clamped = off < 0.5 || off > sc.ScrollableHeight - 0.5;
            Check(clamped || Math.Abs(at - centre) < 2, what + ": not centred (" + at + " vs " + centre + ")");
        }

        static void Advanced()
        {
            Func<string> real = XmlWatch.Preset;
            try
            {
                // A folder set by the organization's policy: the XML watch is on General, in the index, and opens in view.
                XmlWatch.Preset = delegate { return @"D:\mensajes soap"; };
                Settings s = Fresh();
                s.XmlWatch = false;
                s.XmlWatchFolder = "";
                HomeWindow hw = Make(1, s);
                List<SearchEntry> ix = Index(hw);
                SearchEntry watch = Entry(ix, "general", "Abrir los XML nuevos de una carpeta");
                Check(watch != null, "the XML watch is in the index");
                SetPage(hw, "keys");
                Layout(hw);
                hw.SearchFor("xml nuevos");
                Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == watch.Title, "found first: " + Titles(hw.SearchResults, 3));
                hw.SearchOpen(0);
                Layout(hw);
                HomeWindow.Row row = RowTitled(hw, watch.Title);
                Check(hw.SearchPage == "general" && row != null, "opened with the row on the page");
                Glows(hw, row, watch.Title);
                InView(hw, row, watch.Title);
                // The folder row next to it, dimmed while the watch is off, is found too.
                hw.SearchFor("mensajes soap");
                Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Group == "Avanzado", "the watched folder: " + Titles(hw.SearchResults, 3));
                hw.Shutdown();

                // Anywhere else the search knows nothing of it either.
                XmlWatch.Preset = delegate { return null; };
                HomeWindow other = Make(1, Fresh());
                List<SearchEntry> none = Index(other);
                Check(Entry(none, "general", "Abrir los XML nuevos de una carpeta") == null, "the XML watch is in the index without a policy or a setup");
                foreach (SearchEntry e in none) Check(e.Group != "Avanzado", "the folder watch shows in the index: " + e.Title);
                other.SearchFor("xml nuevos");
                foreach (SearchHit h in other.SearchResults) Check(h.Entry.Group != "Avanzado", "found without a policy or a setup: " + h.Entry.Title);
                other.Shutdown();
            }
            finally { XmlWatch.Preset = real; }
        }

        static FrameworkElement Card(HomeWindow hw, int i)
        {
            object card = ((Array)Field(hw, "recCards")).GetValue(i);
            return (FrameworkElement)card.GetType().GetField("Root").GetValue(card);
        }

        static TextBlock TextOn(HomeWindow hw, string text)
        {
            TextBlock found = null;
            Walk(Body(hw), delegate(DependencyObject d) { TextBlock t = d as TextBlock; if (found == null && t != null && t.Text == text) found = t; });
            return found;
        }

        static SearchEntry InGroup(List<SearchEntry> ix, string group, string title)
        {
            foreach (SearchEntry e in ix) if (e.Page == "record" && e.Group == group && e.Title == title) return e;
            return null;
        }

        static void Quality()
        {
            // Estandar: its frame rate is an entry; Cine's options are not on show, and their words find the Cine card.
            Settings s = Fresh();
            s.VideoQuality = Recorder.Standard;
            HomeWindow hw = Make(1, s);
            List<SearchEntry> ix = Index(hw);
            SearchEntry fps = InGroup(ix, "Calidad", "Fotogramas por segundo");
            Check(fps != null && fps.Sub != null, "Est\u00E1ndar's frame rate is in the index");
            Check(InGroup(ix, "Calidad", "Memoria para picos de movimiento") == null, "Cine's options are not on show");
            List<SearchHit> mem = SettingsSearch.Find(ix, "memoria");
            Check(mem.Count > 0 && mem[0].Entry.Title == "Cine", "memoria finds the Cine card: " + Titles(mem, 3));
            List<SearchHit> mkv = SettingsSearch.Find(ix, "mkv sin perdidas");
            Check(mkv.Count > 0 && mkv[0].Entry.Title == "Cine", "mkv: " + Titles(mkv, 3));
            SetPage(hw, "keys");
            Layout(hw);
            hw.SearchFor("fotogramas");
            Layout(hw);
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Fotogramas por segundo", "fotogramas: " + Titles(hw.SearchResults, 5));
            Shot(hw, "busqueda-fotogramas-claro.png");
            hw.SearchFor("memoria picos");
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Cine", "memoria picos: " + Titles(hw.SearchResults, 3));
            hw.SearchOpen(0);
            Layout(hw);
            Check(hw.SearchPage == "record", "opened Grabaci\u00F3n");
            Glows(hw, Card(hw, 3), "the Cine card");
            InView(hw, Card(hw, 3), "the Cine card");
            // Cine chosen on the page, as its card does (no rebuild): the index follows, and the option is opened on the page
            // built again, where it shows.
            s.VideoQuality = Recorder.Cinema;
            try { typeof(HomeWindow).GetMethod("RecShowNote", NP).Invoke(hw, new object[] { 3, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("RecShowNote: " + ex.InnerException, ex.InnerException); }
            Layout(hw);
            object bodyBefore = Body(hw);
            hw.SearchFor("memoria picos");
            Check(hw.SearchResults.Count > 0 && hw.SearchResults[0].Entry.Title == "Memoria para picos de movimiento", "with Cine: " + Titles(hw.SearchResults, 3));
            Check(hw.SearchResults[0].Entry.Sub != null, "the option says what it does");
            hw.SearchOpen(0);
            Layout(hw);
            Check(Body(hw) != bodyBefore, "the page was built again to find it");
            TextBlock title = TextOn(hw, "Memoria para picos de movimiento");
            Check(title != null && ShownTexts(Body(hw)).Contains(title.Text), "the option shows on the page");
            FrameworkElement column = (FrameworkElement)title.Parent;
            Glows(hw, column, "the memory option");
            InView(hw, column, "the memory option");
            // Now Estandar's frame rate is no entry (its card keeps the words), Cine's is.
            ix = Index(hw);
            int rates = 0;
            foreach (SearchEntry e in ix) if (e.Page == "record" && e.Group == "Calidad" && e.Title == "Fotogramas por segundo") rates++;
            Check(rates == 1, "one frame rate on show: " + rates);
            Check(Entry(ix, "record", "Est\u00E1ndar").Extra == "Fotogramas por segundo", "Est\u00E1ndar's card has its option's words");
            hw.Shutdown();
        }

        static void Esc()
        {
            HomeWindow hw = Make(1, Fresh());
            SetPage(hw, "general");
            Layout(hw);
            ScrollViewer sc = Scroller(hw);
            sc.ScrollToVerticalOffset(400);
            Layout(hw);
            double off = sc.VerticalOffset;
            Check(off > 100, "scrolled to " + off);
            object bodyBefore = Body(hw);
            hw.SearchFor("sonido");
            Layout(hw);
            Check(hw.SearchShowing, "results");
            Check(Press(hw.SearchBox, Key.Escape, ModifierKeys.None), "Esc handled by the field");
            Layout(hw);
            Check(!hw.SearchShowing && hw.SearchBox.Text == "" && hw.SearchPage == "general", "Esc empties the field and closes the results");
            Check(sc.Visibility == Visibility.Visible && Math.Abs(sc.VerticalOffset - off) < 0.5 && Body(hw) == bodyBefore, "back to the page as it was (offset " + sc.VerticalOffset + ")");
            // With the focus elsewhere, the window's own Esc (which tucks it away) gives way to the results.
            hw.SearchFor("sonido");
            FocusManager.SetFocusedElement(hw, null);
            Check(Press(hw, Key.Escape, ModifierKeys.None), "Esc taken by the window");
            Check(!hw.SearchShowing && hw.SearchBox.Text == "" && Math.Abs(sc.VerticalOffset - off) < 0.5 && Body(hw) == bodyBefore, "Esc away from the field closes the results");
            // Clicking the page's own entry in the sidebar closes the results too.
            hw.SearchFor("sonido");
            StackPanel nav = (StackPanel)Field(hw, "navList");
            string[] ids = (string[])typeof(HomeWindow).GetField("PageIds", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            UIElement general = nav.Children[Array.IndexOf(ids, "general")];
            general.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Check(!hw.SearchShowing && Body(hw) == bodyBefore, "the sidebar entry of the page closes the results without a rebuild");
            // Another page from the sidebar while the results show: that page.
            hw.SearchFor("sonido");
            SetPage(hw, "editor");
            Check(!hw.SearchShowing && hw.SearchPage == "editor" && sc.Visibility == Visibility.Visible, "another page closes the results");
            hw.Shutdown();
        }

        static void CtrlF()
        {
            HomeWindow hw = Make(1, Fresh());
            SetPage(hw, "general");
            Layout(hw);
            bool reached = false;
            KeyEventHandler probe = delegate(object o, KeyEventArgs e) { reached = true; };
            hw.PreviewKeyDown += probe;   // stands for the page's own Ctrl+F, hooked after the window's
            FocusManager.SetFocusedElement(hw, null);
            Check(Press(hw, Key.F, ModifierKeys.Control), "Ctrl+F on General is taken");
            Check(FocusManager.GetFocusedElement(hw) == hw.SearchBox, "the search field gets the focus");
            Check(!reached, "nothing after it sees it");
            Check(!Press(hw, Key.F, ModifierKeys.None), "F alone is not taken");
            Check(!Press(hw, Key.F, ModifierKeys.Control | ModifierKeys.Shift), "Ctrl+Shift+F is not taken");
            // Historial keeps it for its own search (its field is the one Ctrl+F reaches).
            SetField(hw, "page", "history");
            SetField(hw, "historyBody", Body(hw));
            SetField(hw, "historyBox", new TextBox());
            FocusManager.SetFocusedElement(hw, null);
            reached = false;
            Check(hw.SearchCtrlFStays(), "Historial keeps Ctrl+F");
            Check(!Press(hw, Key.F, ModifierKeys.Control), "Ctrl+F on Historial is left to the page");
            Check(reached && FocusManager.GetFocusedElement(hw) != hw.SearchBox, "it goes on to the page's own handler");
            // Unless the settings results cover it.
            hw.SearchFor("tema");
            FocusManager.SetFocusedElement(hw, null);
            Check(!hw.SearchCtrlFStays() && Press(hw, Key.F, ModifierKeys.Control), "over the results it is the search's");
            hw.SearchHide();
            // The mascot's wardrobe search, likewise.
            SetField(hw, "page", "mascot");
            SetField(hw, "dockSearch", new TextBox());
            reached = false;
            Check(!Press(hw, Key.F, ModifierKeys.Control) && reached, "the mascot page keeps Ctrl+F");
            hw.PreviewKeyDown -= probe;
            hw.Shutdown();
        }

        static void Stale()
        {
            Settings s = Fresh();
            HomeWindow hw = Make(1, s);
            hw.SearchFor("tema");
            int builds = hw.SearchIndexBuilds;
            Check(builds == 1, "built once: " + builds);
            hw.SearchFor("sonido");
            Check(hw.SearchIndexBuilds == 1, "kept while nothing changes");
            SearchEntry before = Entry(hw.SearchIndexNow(null), "keys", "Capturar un \u00E1rea");
            builds = hw.SearchIndexBuilds;
            // A setting changes (here a combination, as the tray or the settings file could): the next search sees it.
            s.HotRegion = "Ctrl+Shift+F9";
            hw.SearchFor("capturar un area");
            Check(hw.SearchIndexBuilds == builds + 1, "rebuilt after a setting changed");
            SearchHit h = hw.SearchResults[0];
            Check(h.Entry.Title == "Capturar un \u00E1rea" && h.Entry.Sub != before.Sub && h.Entry.Sub.Contains("F9"), "the new combination: " + h.Entry.Sub);
            // A page rebuilt for what it shows (a row came or went), or any page built.
            builds = hw.SearchIndexBuilds;
            Call(hw, "Rebuild");
            hw.SearchFor("tema");
            Check(hw.SearchIndexBuilds == builds + 1, "rebuilt after a page rebuild");
            SetPage(hw, "editor");
            hw.SearchFor("tema");
            Check(hw.SearchIndexBuilds == builds + 2, "rebuilt after a page was built");
            hw.SearchFor("sonido");
            Check(hw.SearchIndexBuilds == builds + 2, "kept again while nothing changes");
            // A theme while the results show: they are drawn again at once, and again once the index has the new colours.
            Ds.Apply(2);
            try { typeof(HomeWindow).GetMethod("ThemeChanged", NP).Invoke(hw, new object[0]); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("ThemeChanged: " + ex.InnerException, ex.InnerException); }
            Check(hw.SearchShowing && hw.SearchBox.Text == "sonido" && hw.SearchResults.Count > 0, "the results after a theme change");
            Check((int)Field(hw, "searchJobNext") >= 0, "the index is being made again for the new theme");
            Ds.Apply(1);
            // A theme: the colours of the widgets' icons follow.
            builds = hw.SearchIndexBuilds;
            Ds.Apply(2);
            hw.SearchFor("tema");
            Check(hw.SearchIndexBuilds == builds + 1, "rebuilt with the theme");
            Ds.Apply(1);
            hw.Shutdown();
        }

        // ---- Section chips

        static void Sections()
        {
            foreach (int appearance in new int[] { 1, 2 })
            {
                string theme = appearance == 2 ? "oscuro" : "claro";
                HomeWindow hw = Make(appearance, Fresh());
                SetPage(hw, "general");
                Layout(hw);
                List<string> titles = hw.SectionTitles;
                Console.WriteLine("    General: " + string.Join(", ", titles.ToArray()));
                foreach (string want in new string[] { "Perfil", "Al capturar", "Datos sensibles", "Capturas guardadas", "Recientes" })
                    Check(titles.Contains(want), theme + ": no chip <" + want + ">");
                Check(titles[0] == "Perfil", theme + ": the first chip");
                StackPanel body = Body(hw);
                FrameworkElement strip = hw.SectionStripView;
                int si = body.Children.IndexOf((UIElement)strip);
                Check(si > 0 && body.Children[si + 1] is TextBlock && ((TextBlock)body.Children[si + 1]).Text == "Perfil", theme + ": the chips go above the first group");
                ScrollViewer sc = Scroller(hw);
                Border bar = (Border)Field(hw, "toolbar");
                Check(sc.VerticalOffset == 0 && !hw.SectionPinned && hw.SectionActive == 0, theme + ": at the top, not pinned, the first lit");
                Check(((SolidColorBrush)bar.Background).Color.A == 0, theme + ": the toolbar is clear at the top");
                Shot(hw, "secciones-arriba-" + theme + ".png");
                // Each chip glides its group under the pinned chips.
                for (int i = 0; i < titles.Count; i++)
                {
                    hw.SectionGo(i);
                    Layout(hw);
                    List<SectionLike> marks = Anchors(hw);
                    double top = TopIn(marks[i].Anchor, PageHost(hw)), want = top - Bar - PinH - 10;
                    bool end = want > sc.ScrollableHeight - 0.5;
                    Check(Math.Abs(sc.VerticalOffset - Math.Min(want, sc.ScrollableHeight)) < 1, theme + ": chip " + titles[i] + " scrolled to " + sc.VerticalOffset + " instead of " + want);
                    Check(hw.SectionActive == i, theme + ": chip " + titles[i] + " is not lit (" + hw.SectionActive + ")");
                    Check(hw.SectionPinned, theme + ": not pinned after " + titles[i]);
                    PinnedChipShows(hw, theme + ": " + titles[i]);
                    if (!end) Check(top - sc.VerticalOffset >= Bar + PinH, theme + ": " + titles[i] + " under the pinned chips");
                }
                Check(((SolidColorBrush)bar.Background).Color.A == 245, theme + ": the toolbar is shaded when scrolled");
                Check(bar.BorderBrush == Brushes.Transparent, theme + ": the pinned chips draw the hairline, not the toolbar");
                Shot(hw, "secciones-fijas-" + theme + ".png");
                // Scrolling by hand lights the group being read.
                List<SectionLike> all = Anchors(hw);
                for (int i = 0; i < titles.Count; i++)
                {
                    double top = TopIn(all[i].Anchor, PageHost(hw)), to = top - Bar - PinH - 10;
                    if (to > sc.ScrollableHeight - 2) continue;
                    sc.ScrollToVerticalOffset(to + 4);
                    Layout(hw);
                    int act = hw.SectionActive;
                    bool twin = i > 0 && Math.Abs(TopIn(all[i - 1].Anchor, PageHost(hw)) - top) < 1;
                    Check(act == i || (twin && act == i - 1), theme + ": scrolled to " + titles[i] + ", lit " + (act >= 0 ? titles[act] : "none"));
                    if (hw.SectionPinned) PinnedChipShows(hw, theme + ": scrolled to " + titles[i]);
                }
                sc.ScrollToVerticalOffset(sc.ScrollableHeight);
                Layout(hw);
                Check(hw.SectionActive == titles.Count - 1, theme + ": at the end the last is lit");
                sc.ScrollToVerticalOffset(0);
                Layout(hw);
                Check(!hw.SectionPinned && hw.SectionActive == 0, theme + ": back at the top");
                Check(((SolidColorBrush)bar.BorderBrush).Color.A == 0 && ((SolidColorBrush)bar.Background).Color.A == 0, theme + ": the toolbar fades back");
                hw.Shutdown();
            }
        }

        // The pinned chips slide so the lit one is whole and clear of the faded edges, and nothing cuts the row short.
        static void PinnedChipShows(HomeWindow hw, string what)
        {
            Panel clip = (Panel)((Border)hw.SectionPinView).Child;
            StackPanel row = (StackPanel)clip.Children[0];
            Check(LayoutInformation.GetLayoutClip(row) == null, what + ": the pinned row is cut by layout");
            Border lit = (Border)row.Children[hw.SectionActive];
            Rect r = lit.TransformToAncestor(clip).TransformBounds(new Rect(lit.RenderSize));
            Check(r.Left >= 40 - 0.5 && r.Right <= clip.ActualWidth - 40 + 0.5, what + ": the lit pinned chip is at " + r + " in " + clip.ActualWidth);
            Check(clip.ClipToBounds && (clip.OpacityMask == null || ((LinearGradientBrush)clip.OpacityMask).MappingMode == BrushMappingMode.Absolute), what + ": the edges");
        }

        sealed class SectionLike { public string Title; public FrameworkElement Anchor; }

        static List<SectionLike> Anchors(HomeWindow hw)
        {
            List<SectionLike> l = new List<SectionLike>();
            System.Collections.IEnumerable marks = (System.Collections.IEnumerable)Field(hw, "sectionMarks");
            foreach (object m in marks)
                l.Add(new SectionLike { Title = (string)m.GetType().GetField("Title").GetValue(m), Anchor = (FrameworkElement)m.GetType().GetField("Anchor").GetValue(m) });
            return l;
        }

        static void SectionPages()
        {
            HomeWindow hw = Make(1, Fresh());
            foreach (string id in new string[] { "home", "keys", "editor", "about", "record", "mascotset" })
            {
                SetPage(hw, id);
                Layout(hw);
                ScrollViewer sc = Scroller(hw);
                double tall = sc.ExtentHeight, view = sc.ViewportHeight;
                List<string> t = hw.SectionTitles;
                Console.WriteLine("    " + id + ": " + tall.ToString("0") + " tall in " + view.ToString("0") + ", chips: " + (t.Count > 0 ? string.Join(", ", t.ToArray()) : "none"));
                if (t.Count > 0) Check(tall > 2 * view, id + ": chips on a page of " + tall);
                else Check(tall <= 2 * view + 60 || id == "home" || id == "about", id + ": a long page without chips (" + tall + ")");
            }
            SetPage(hw, "record");
            Layout(hw);
            List<string> r = hw.SectionTitles;
            Check(r.Contains("Calidad") && r.Contains("Sonido") && r.Contains("Despu\u00E9s de grabar"), "Grabaci\u00F3n's widgets as chips: " + string.Join(", ", r.ToArray()));
            Shot(hw, "secciones-grabacion-claro.png");
            ScrollViewer rsc = Scroller(hw);
            rsc.ScrollToVerticalOffset(rsc.ScrollableHeight);
            Layout(hw);
            Check(hw.SectionPinned && hw.SectionActive == r.Count - 1, "Grabaci\u00F3n at the end: the last chip, pinned");
            PinnedChipShows(hw, "Grabaci\u00F3n at the end");
            Shot(hw, "secciones-grabacion-fijas-claro.png");
            hw.Shutdown();
        }
    }
}

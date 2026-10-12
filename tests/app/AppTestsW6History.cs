// Round 6 (capture history searched by text), suite "w6history": the text index built from captures drawn in a private
// folder (with the real OCR and with a stand-in reader), search semantics, deleted, renamed and changed files, a broken
// index, the setting off, the cap, timings, and the Historial page built off screen in light and dark. Nothing is shown,
// no screen is read, files go only to STACKSHOT_TEST_DATA.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using W = System.Windows;
using WC = System.Windows.Controls;
using WD = System.Windows.Documents;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Stackshot.Tests
{
    static class AppTestsW6History
    {
        const string Suite_ = "w6history";

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        // ---- Private folders and the index pointed at them

        static string root;

        static string Root()
        {
            if (root != null) return root;
            string data = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
            root = Path.Combine(data, "w6history", DateTime.Now.ToString("HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(root);
            return root;
        }

        sealed class Box
        {
            public string Save, Temp, Index;
            public Settings S;
            public int Reads;
            public readonly Dictionary<int, string> ByWidth = new Dictionary<int, string>();
            public int Delay;                  // ms each stand-in read takes
            public string IndexFile { get { return Path.Combine(Index, "indice.gz"); } }
        }

        // Fresh folders, the index reset onto them, a stand-in reader that answers by image width (real OCR when real).
        static Box NewBox(string name, bool realOcr)
        {
            Box b = new Box();
            string d = Path.Combine(Root(), name);
            b.Save = Path.Combine(d, "Capturas");
            b.Temp = Path.Combine(d, "temp");
            b.Index = Path.Combine(d, "historial");
            Directory.CreateDirectory(b.Save);
            Directory.CreateDirectory(b.Temp);
            b.S = new Settings();
            b.S.SaveFolder = b.Save;
            b.S.HistorySearch = true;
            Point(b, realOcr);
            return b;
        }

        // Points the (reset) index at a box again, keeping its files.
        static void Point(Box b, bool realOcr)
        {
            HistoryIndex.ResetForTests();
            HistoryIndex.Dir = b.Index;
            HistoryIndex.TempFolder = b.Temp;
            HistoryIndex.Limit = 5000;
            HistoryIndex.MaxPixels = 40000000;
            HistoryIndex.StartDelay = 0;
            HistoryIndex.FreshDelay = 0;
            HistoryIndex.IdleRescan = 10 * 60 * 1000;
            HistoryIndex.Busy = delegate { return false; };
            if (realOcr)
                HistoryIndex.Reader = delegate(Bitmap bmp, out string error)
                {
                    Interlocked.Increment(ref b.Reads);
                    return Ocr.Read(bmp, out error);
                };
            else
                HistoryIndex.Reader = delegate(Bitmap bmp, out string error)
                {
                    error = null;
                    Interlocked.Increment(ref b.Reads);
                    if (b.Delay > 0) Thread.Sleep(b.Delay);
                    string t;
                    lock (b.ByWidth) return b.ByWidth.TryGetValue(bmp.Width, out t) ? t : "";
                };
        }

        static void Restore()
        {
            HistoryIndex.ResetForTests();
            HistoryIndex.Dir = Path.Combine(Settings.DataDir, "historial");
            HistoryIndex.TempFolder = Settings.TempDir;
            HistoryIndex.Limit = 5000;
            HistoryIndex.MaxPixels = 40000000;
            HistoryIndex.StartDelay = 20000;
            HistoryIndex.FreshDelay = 4000;
            HistoryIndex.IdleRescan = 10 * 60 * 1000;
            HistoryIndex.Busy = delegate { return Recorder.Recording || ScrollCapture.Active; };
            HistoryIndex.Reader = Ocr.Read;
        }

        // Each test logs to its own file, printed when it fails.
        static void Run(string name, Action body)
        {
            AppTests.Test(Suite_, name, delegate
            {
                string log = Path.Combine(Root(), "log-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
                ShotStack.LogPath = log;
                bool ok = false;
                try
                {
                    body();
                    ok = true;
                }
                finally
                {
                    Restore();
                    ShotStack.LogPath = null;
                    if (!ok && File.Exists(log)) foreach (string l in File.ReadAllLines(log)) Console.WriteLine("    log: " + l);
                }
            });
        }

        // A capture with lines of text drawn big enough for Windows' OCR.
        static void Png(string path, string[] lines, DateTime when)
        {
            const int lh = 44;
            using (Bitmap b = new Bitmap(720, 30 + lines.Length * lh))
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = new Font("Segoe UI", 20))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, Brushes.Black, 14, 12 + i * lh);
                b.Save(path, ImageFormat.Png);
            }
            File.SetLastWriteTime(path, when);
        }

        // A plain picture the stand-in reader knows by its width.
        static void Blank(Box box, string path, int width, string text, DateTime when)
        {
            lock (box.ByWidth) box.ByWidth[width] = text;
            using (Bitmap b = new Bitmap(width, 24))
            {
                using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(200, 210, 230));
                b.Save(path, ImageFormat.Png);
            }
            File.SetLastWriteTime(path, when);
        }

        static void Bytes(string path, int n, DateTime when)
        {
            byte[] d = new byte[n];
            for (int i = 0; i < n; i++) d[i] = (byte)(i * 7);
            File.WriteAllBytes(path, d);
            File.SetLastWriteTime(path, when);
        }

        // Until the reading thread has run out of work after the pass number `after`.
        static void WaitPass(int after, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (HistoryIndex.Passes > after && HistoryIndex.Idle) return;
                Thread.Sleep(15);
            }
            throw new InvalidOperationException("the index did not finish its pass within " + ms + " ms (passes " + HistoryIndex.Passes + ")");
        }

        static void Rescan(int ms)
        {
            int p = HistoryIndex.Passes;
            HistoryIndex.RequestScan();
            WaitPass(p, ms);
        }

        static void WaitDeleted()
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (HistoryIndex.Deleting && sw.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Check(!HistoryIndex.Deleting, "the index is still being deleted after 5 s");
        }

        static string Text(string path)
        {
            string t;
            return HistoryIndex.TextsForTests().TryGetValue(path, out t) ? t : null;
        }

        static bool Has(string text, string word) { return text != null && HistoryIndex.Fold(text).Contains(HistoryIndex.Fold(word)); }

        static List<string> Names(List<HistoryItem> l)
        {
            List<string> r = new List<string>();
            foreach (HistoryItem it in l) r.Add(it.Name);
            return r;
        }

        static string Join(List<HistoryItem> l) { return string.Join(", ", Names(l).ToArray()); }

        static byte[] Gz(string content)
        {
            byte[] raw = new UTF8Encoding(false).GetBytes(content);
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        static HistoryItem Item(string name, HistoryKind kind, string text, DateTime when)
        {
            HistoryItem it = new HistoryItem();
            it.Path = Path.Combine("C:\\Capturas", name);
            it.Name = name;
            it.Kind = kind;
            it.When = when;
            it.Size = 100;
            it.Ticks = when.ToUniversalTime().Ticks;
            if (text != null) it.Entry = new HistoryIndex.Entry(it.Path, it.Size, it.Ticks, HistoryIndex.Read, text);
            return it;
        }

        // ---- Folding, search, snippets and day groups

        static void FoldKeepsLength()
        {
            string s = "Informaci\u00F3n CAMI\u00D3N \u00D1and\u00FA \u00DCn\u00EFcode\u00A0x \u2013 \u201Ccomillas\u201D";
            string f = HistoryIndex.Fold(s);
            Check(f.Length == s.Length, "length changed: " + s.Length + " -> " + f.Length);
            Check(f == "informacion camion nandu unicode x - \"comillas\"", "folded as <" + f + ">");
            Check(HistoryIndex.Fold("") == "" && HistoryIndex.Fold(null) == "", "empty input");
            string emoji = "a\uD83D\uDE00B";
            Check(HistoryIndex.Fold(emoji) == "a\uD83D\uDE00b", "a surrogate pair must stay as it is");
        }

        static List<HistoryItem> Sample()
        {
            DateTime now = DateTime.Now;
            List<HistoryItem> l = new List<HistoryItem>();
            l.Add(Item("Captura 2026-10-12 10.00.00.png", HistoryKind.Image, "Factura n\u00FAmero 4521\nCliente: Jos\u00E9 P\u00E9rez", now));
            l.Add(Item("Pantalla 2026-10-11 09.00.00.png", HistoryKind.Image, "Reuni\u00F3n del equipo de VENTAS\nAcci\u00F3n pendiente", now.AddMinutes(-5)));
            l.Add(Item("Factura escaneada.jpg", HistoryKind.Image, "", now.AddMinutes(-10)));
            l.Add(Item("Grabaci\u00F3n reuni\u00F3n.mp4", HistoryKind.Video, null, now.AddMinutes(-15)));
            l.Add(Item("demo.gif", HistoryKind.Gif, null, now.AddMinutes(-20)));
            l.Add(Item("Sin leer.png", HistoryKind.Image, null, now.AddMinutes(-25)));
            return l;
        }

        static void Expect(List<HistoryItem> all, string query, int filter, string expected)
        {
            string got = Join(HistoryIndex.Search(all, query, filter));
            Check(got == expected, "search <" + query + "> filter " + filter + ": got [" + got + "], expected [" + expected + "]");
        }

        static void SearchSemantics()
        {
            List<HistoryItem> l = Sample();
            string f1 = "Captura 2026-10-12 10.00.00.png", f2 = "Pantalla 2026-10-11 09.00.00.png", f3 = "Factura escaneada.jpg",
                   v = "Grabaci\u00F3n reuni\u00F3n.mp4", g = "demo.gif", u = "Sin leer.png";
            Expect(l, "factura", 0, f1 + ", " + f3);                   // the text of one, the name of the other
            Expect(l, "FACTURA", 0, f1 + ", " + f3);                   // case
            Expect(l, "numero", 0, f1);                                // accents in the text
            Expect(l, "N\u00DAMERO 4521", 0, f1);                      // accents in the query
            Expect(l, "4521 perez", 0, f1);                            // every word, in any order
            Expect(l, "  perez   cliente ", 0, f1);                    // blanks around and between
            Expect(l, "perez ventas", 0, "");                          // words in two different captures
            Expect(l, "reunion", 0, f2 + ", " + v);                    // text of an image and name of a video
            Expect(l, "reunion", 1, f2);                               // Capturas
            Expect(l, "reunion", 2, v);                                // Videos
            Expect(l, "accion", 0, f2);
            Expect(l, "", 3, g);                                       // GIF
            Expect(l, "demo", 1, "");                                  // a GIF is not a capture
            Expect(l, "leer", 0, u);                                   // an image not read yet: its name
            Expect(l, "", 0, f1 + ", " + f2 + ", " + f3 + ", " + v + ", " + g + ", " + u);
            Expect(l, "   ", 0, f1 + ", " + f2 + ", " + f3 + ", " + v + ", " + g + ", " + u);
            Expect(l, "2026-10-11", 0, f2);                            // the date in the file name
            Expect(l, "png", 1, f1 + ", " + f2 + ", " + u);
            Check(HistoryIndex.Words("Hola hola HOLA mundo").Length == 2, "repeated words are one");
        }

        static void Snippets()
        {
            HistoryItem it = Item("a.png", HistoryKind.Image,
                "Primera l\u00EDnea del documento\n\nUna l\u00EDnea m\u00E1s de relleno para alejar el texto\nEl importe total de la factura es 1.234,56 euros y vence ma\u00F1ana por la tarde", DateTime.Now);
            string[] w = HistoryIndex.Words("FACTURA total manana");
            HistorySnippet sn = HistoryIndex.Snippet(it, w, 60);
            Console.WriteLine("    snippet: <" + sn.Text + ">");
            Check(!sn.FromName, "the match is in the text");
            Check(sn.Text.IndexOf('\n') < 0 && sn.Text.IndexOf("  ", StringComparison.Ordinal) < 0, "lines joined and blanks collapsed: <" + sn.Text + ">");
            Check(sn.Text.StartsWith("\u2026", StringComparison.Ordinal), "a cut at the start shows an ellipsis");
            List<string> marked = new List<string>();
            foreach (KeyValuePair<int, int> m in sn.Marks) marked.Add(HistoryIndex.Fold(sn.Text.Substring(m.Key, m.Value)));
            Console.WriteLine("    marks: " + string.Join(" | ", marked.ToArray()));
            Check(marked.Contains("total") && marked.Contains("factura"), "every word marked: " + string.Join(" | ", marked.ToArray()));
            foreach (string m in marked) Check(m == "total" || m == "factura" || m == "manana", "a mark that is not a word: <" + m + ">");
            HistorySnippet byName = HistoryIndex.Snippet(Item("Factura escaneada.jpg", HistoryKind.Image, "", DateTime.Now), HistoryIndex.Words("escaneada"), 60);
            Check(byName.FromName && byName.Text == "Factura escaneada.jpg", "a name-only match shows the name");
            Check(byName.Marks.Count == 1 && byName.Marks[0].Key == 8 && byName.Marks[0].Value == 9, "the name's match is marked");
            // Overlapping words merge into one mark.
            HistorySnippet over = HistoryIndex.Snippet(Item("b.png", HistoryKind.Image, "contrase\u00F1a olvidada", DateTime.Now), HistoryIndex.Words("contra trase"), 60);
            Check(over.Marks.Count == 1 && over.Marks[0].Key == 0 && over.Marks[0].Value == 8, "overlapping matches merged: " + over.Marks.Count + " marks, first " + over.Marks[0].Key + "+" + over.Marks[0].Value);
        }

        static void DayGroups()
        {
            DateTime wed = new DateTime(2026, 10, 14, 15, 0, 0);
            Check(wed.DayOfWeek == DayOfWeek.Wednesday, "calendar");
            Check(HistoryIndex.GroupOf(wed.AddHours(-3), wed) == 0, "today");
            Check(HistoryIndex.GroupOf(wed.AddHours(5), wed) == 0, "later today (or a clock ahead) is today");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 10, 13, 23, 0, 0), wed) == 1, "yesterday");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 10, 12, 9, 0, 0), wed) == 2, "Monday this week");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 10, 11, 9, 0, 0), wed) == 3, "last Sunday, this month");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 9, 30, 9, 0, 0), wed) == 4, "last month");
            DateTime mon = new DateTime(2026, 10, 12, 8, 0, 0);
            Check(HistoryIndex.GroupOf(new DateTime(2026, 10, 11, 20, 0, 0), mon) == 1, "Monday: Sunday is yesterday");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 10, 10, 20, 0, 0), mon) == 3, "Monday: Saturday is this month");
            DateTime thu = new DateTime(2026, 10, 1, 8, 0, 0);
            Check(HistoryIndex.GroupOf(new DateTime(2026, 9, 30, 8, 0, 0), thu) == 1, "the 1st: the 30th is yesterday");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 9, 28, 8, 0, 0), thu) == 2, "the 1st: Monday the 28th is this week");
            Check(HistoryIndex.GroupOf(new DateTime(2026, 9, 27, 8, 0, 0), thu) == 4, "the 1st: Sunday the 27th is before");
        }

        // ---- The index

        static void BuildWithRealOcr()
        {
            Box b = NewBox("ocr", true);
            DateTime now = DateTime.Now;
            string month = Path.Combine(b.Save, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(month);
            string p1 = Path.Combine(b.Temp, "Captura 1.png"), p2 = Path.Combine(b.Save, "Captura 2.png"), p3 = Path.Combine(month, "Captura 3.png");
            Png(p1, new string[] { "Factura n\u00FAmero 4521", "Cliente Jos\u00E9 P\u00E9rez" }, now.AddMinutes(-1));
            Png(p2, new string[] { "Reuni\u00F3n del equipo de ventas" }, now.AddMinutes(-2));
            Png(p3, new string[] { "Contrase\u00F1a olvidada" }, now.AddMinutes(-3));
            using (Bitmap gif = new Bitmap(40, 30)) gif.Save(Path.Combine(b.Save, "Animaci\u00F3n demo.gif"), ImageFormat.Gif);
            Bytes(Path.Combine(b.Temp, "Grabaci\u00F3n prueba.mp4"), 4096, now.AddMinutes(-4));
            Stopwatch sw = Stopwatch.StartNew();
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 90000);
            Console.WriteLine("    3 captures read with the OCR in " + sw.ElapsedMilliseconds + " ms; index " + new FileInfo(b.IndexFile).Length + " bytes");
            Dictionary<string, string> texts = HistoryIndex.TextsForTests();
            foreach (KeyValuePair<string, string> kv in texts) Console.WriteLine("    " + Path.GetFileName(kv.Key) + ": " + (kv.Value ?? "").Replace("\n", " | "));
            Check(texts.Count == 3, "3 images indexed, got " + texts.Count + " (GIF and MP4 are listed only)");
            Check(Has(Text(p1), "factura") && Has(Text(p1), "4521"), "text of the temporary capture");
            Check(Has(Text(p2), "reunion") && Has(Text(p2), "ventas"), "text of the saved capture");
            Check(Has(Text(p3), "olvidada"), "text of the capture in the month folder");
            List<HistoryItem> cat = HistoryIndex.Catalog(b.S);
            Check(cat.Count == 5, "listed: " + Join(cat));
            Check(Join(HistoryIndex.Search(cat, "factura 4521", 0)) == "Captura 1.png", "search factura 4521");
            Check(Join(HistoryIndex.Search(cat, "REUNION ventas", 0)) == "Captura 2.png", "search reunion ventas");
            Check(Join(HistoryIndex.Search(cat, "animacion", 0)) == "Animaci\u00F3n demo.gif", "a GIF by its name");
            Check(Join(HistoryIndex.Search(cat, "prueba", 2)) == "Grabaci\u00F3n prueba.mp4", "a video by its name");
            bool broken;
            List<HistoryIndex.Entry> saved = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(!broken && saved.Count == 3, "index on disk: " + saved.Count + " entries, broken " + broken);
            Check(HistoryIndex.Running && HistoryIndex.Idle, "idle and still there for new captures");
            // Idle costs nothing: the thread waits on an event.
            Process me = Process.GetCurrentProcess();
            TimeSpan cpu0 = me.TotalProcessorTime;
            Thread.Sleep(2000);
            me.Refresh();
            double cpu = (me.TotalProcessorTime - cpu0).TotalMilliseconds;
            Console.WriteLine("    idle CPU over 2 s: " + cpu.ToString("0") + " ms (the whole test process)");
            Check(cpu < 150, "idle CPU " + cpu + " ms in 2 s");
            Check(HistoryIndex.Idle, "still idle");
            // Started again on the same folders: the texts come back from the file, nothing is read again.
            Point(b, true);
            b.Reads = 0;
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 30000);
            Check(b.Reads == 0, "read again after a restart: " + b.Reads);
            Check(Has(Text(p1), "factura") && Has(Text(p3), "olvidada"), "texts back from the file");
        }

        static void ChangesAreFollowed()
        {
            Box b = NewBox("cambios", false);
            DateTime now = DateTime.Now;
            string a = Path.Combine(b.Temp, "a.png"), bb = Path.Combine(b.Save, "b.png"), c = Path.Combine(b.Save, "c.png");
            Blank(b, a, 301, "alfa uno", now.AddMinutes(-3));
            Blank(b, bb, 302, "beta dos", now.AddMinutes(-2));
            Blank(b, c, 303, "gamma tres", now.AddMinutes(-1));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(Text(a) == "alfa uno" && Text(bb) == "beta dos" && Text(c) == "gamma tres", "first pass");
            Check(b.Reads == 3, "reads " + b.Reads);
            // Deleted.
            File.Delete(a);
            Rescan(20000);
            Check(Text(a) == null, "a deleted file keeps its text");
            Check(!Names(HistoryIndex.Catalog(b.S)).Contains("a.png"), "a deleted file is still listed");
            // Renamed.
            string b2 = Path.Combine(b.Save, "b2.png");
            File.Move(bb, b2);
            Rescan(20000);
            Check(Text(bb) == null, "the old name keeps its text");
            Check(Text(b2) == "beta dos", "the new name: " + Text(b2));
            // Changed (another picture under the same name).
            Blank(b, c, 304, "delta cuatro", now.AddMinutes(1));
            Rescan(20000);
            Check(Text(c) == "delta cuatro", "a changed file is read again: " + Text(c));
            List<HistoryItem> cat = HistoryIndex.Catalog(b.S);
            Check(HistoryIndex.Search(cat, "gamma", 0).Count == 0, "the old text is gone from the search");
            Check(Join(HistoryIndex.Search(cat, "delta", 0)) == "c.png", "the new text is found");
            // A capture just taken is read first, without a scan; kept in the save folder, its copy needs no second read.
            string k = Path.Combine(b.Temp, "k.png");
            Blank(b, k, 305, "kappa cinco", now.AddMinutes(2));
            int reads = b.Reads, p = HistoryIndex.Passes;
            HistoryIndex.NotifyCaptured(k);
            WaitPass(p, 20000);
            Check(Text(k) == "kappa cinco", "a new capture was not read: " + Text(k));
            Check(b.Reads == reads + 1, "reads for the new capture: " + (b.Reads - reads));
            string kept = Path.Combine(b.Save, "k.png");
            File.Copy(k, kept);
            Rescan(20000);
            Check(b.Reads == reads + 1, "the kept copy was read again");
            cat = HistoryIndex.Catalog(b.S);
            List<HistoryItem> found = HistoryIndex.Search(cat, "kappa", 0);
            Check(found.Count == 1 && string.Equals(found[0].Path, kept, StringComparison.OrdinalIgnoreCase), "the kept copy is listed once, from the save folder: " + Join(found));
            Check(Text(kept) == "kappa cinco" && Text(k) == null, "the text moved to the kept copy");
        }

        static void BrokenIndexIsRebuilt()
        {
            Box b = NewBox("roto", false);
            DateTime now = DateTime.Now;
            string a = Path.Combine(b.Save, "a.png");
            Blank(b, a, 311, "texto bueno", now);
            Directory.CreateDirectory(b.Index);
            string good = "stackshot-historial 1\n" + a + "\t" + new FileInfo(a).Length + "\t" + new FileInfo(a).LastWriteTimeUtc.Ticks + "\t1\tdel archivo\\nsegunda\n";
            bool broken;
            // Lines that make no sense are skipped; the rest are kept.
            File.WriteAllBytes(b.IndexFile, Gz(good + "basura sin campos\nC:\\x.png\t-5\t1\t1\tnegativo\nrelativo.png\t1\t1\t1\tno\nC:\\y.png\t1\t1\t9\testado\nC:\\z.png\t1\t1\t2\t\n"));
            List<HistoryIndex.Entry> l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(broken && l.Count == 2, "bad lines: " + l.Count + " kept, broken " + broken);
            Check(l[0].Text == "del archivo\nsegunda", "escapes read back: <" + l[0].Text + ">");
            // Another version's file: nothing.
            File.WriteAllBytes(b.IndexFile, Gz("stackshot-historial 9\n" + a + "\t1\t1\t1\tx\n"));
            l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(broken && l.Count == 0, "other version: " + l.Count);
            // Cut short: what came before the cut, never an exception.
            byte[] whole = Gz(good + good.Substring(good.IndexOf('\n') + 1).Replace("a.png", "b.png"));
            byte[] cut = new byte[whole.Length - 12];
            Array.Copy(whole, cut, cut.Length);
            File.WriteAllBytes(b.IndexFile, cut);
            l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Console.WriteLine("    cut short: " + l.Count + " entries, broken " + broken);
            Check(broken, "a cut file reads as broken");
            // Empty, and noise.
            File.WriteAllBytes(b.IndexFile, new byte[0]);
            l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(broken && l.Count == 0, "empty file");
            byte[] noise = new byte[3000];
            new Random(5).NextBytes(noise);
            File.WriteAllBytes(b.IndexFile, noise);
            l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(broken && l.Count == 0, "noise");
            // With the noise in place the index starts, reads the capture and writes a clean file.
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(Text(a) == "texto bueno", "read after a broken index: " + Text(a));
            l = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(!broken && l.Count == 1 && l[0].Text == "texto bueno", "rewritten clean: " + l.Count + ", broken " + broken);
            Check(Directory.GetFiles(b.Index).Length == 1, "only the index in its folder: " + string.Join(", ", Directory.GetFiles(b.Index)));
        }

        static void OffMeansNothing()
        {
            Box b = NewBox("apagado", false);
            b.S.HistorySearch = false;
            DateTime now = DateTime.Now;
            Blank(b, Path.Combine(b.Save, "a.png"), 321, "uno", now.AddMinutes(-1));
            Blank(b, Path.Combine(b.Temp, "b.png"), 322, "dos", now);
            // An index left from before (the setting edited by hand): deleted at once.
            Directory.CreateDirectory(b.Index);
            File.WriteAllBytes(b.IndexFile, Gz("stackshot-historial 1\n"));
            HistoryIndex.Attach(null, b.S);
            WaitDeleted();
            Check(!File.Exists(b.IndexFile) && !HistoryIndex.Exists, "the old index is still there");
            Check(!Directory.Exists(b.Index), "its empty folder is still there");
            Thread.Sleep(600);
            Check(b.Reads == 0 && HistoryIndex.Count == 0 && !HistoryIndex.Running, "something was read with the setting off (" + b.Reads + ")");
            Check(HistoryIndex.Catalog(b.S).Count == 2, "the history is still listed by name");
            // On: read.
            b.S.HistorySearch = true;
            HistoryIndex.Apply(b.S);
            WaitPass(0, 20000);
            Check(b.Reads == 2 && HistoryIndex.Exists, "turned on: " + b.Reads + " reads, index " + HistoryIndex.Exists);
            // Off again: stopped and deleted.
            b.S.HistorySearch = false;
            HistoryIndex.Apply(b.S);
            WaitDeleted();
            Stopwatch sw = Stopwatch.StartNew();
            while (HistoryIndex.Running && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10);
            Check(!HistoryIndex.Running, "the thread did not stop");
            Check(!File.Exists(b.IndexFile) && HistoryIndex.Count == 0, "turned off: the index is still there");
            Blank(b, Path.Combine(b.Save, "c.png"), 323, "tres", now.AddMinutes(1));
            HistoryIndex.NotifyCaptured(Path.Combine(b.Save, "c.png"));
            HistoryIndex.RequestScan();
            Thread.Sleep(500);
            Check(b.Reads == 2, "read after being turned off");
        }

        static void CapKeepsNewest()
        {
            Box b = NewBox("tope", false);
            HistoryIndex.Limit = 5;
            DateTime now = DateTime.Now;
            for (int i = 0; i < 8; i++) Blank(b, Path.Combine(b.Save, "n" + i + ".png"), 401 + i, "numero " + i, now.AddMinutes(-i));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(HistoryIndex.Count == 5 && b.Reads == 5, "entries " + HistoryIndex.Count + ", reads " + b.Reads);
            for (int i = 0; i < 8; i++) Check((Text(Path.Combine(b.Save, "n" + i + ".png")) != null) == (i < 5), "n" + i + (i < 5 ? " should" : " should not") + " be indexed");
            Check(Join(HistoryIndex.Catalog(b.S)) == "n0.png, n1.png, n2.png, n3.png, n4.png", "listed: " + Join(HistoryIndex.Catalog(b.S)));
        }

        static void BigAndBrokenImages()
        {
            Box b = NewBox("grandes", false);
            HistoryIndex.MaxPixels = 10000;
            DateTime now = DateTime.Now;
            string big = Path.Combine(b.Save, "enorme.png"), bad = Path.Combine(b.Save, "roto.png"), ok = Path.Combine(b.Save, "normal.png");
            using (Bitmap bmp = new Bitmap(200, 100)) bmp.Save(big, ImageFormat.Png);
            File.WriteAllText(bad, "esto no es una imagen");
            Blank(b, ok, 99, "normal", now.AddMinutes(-1));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(Text(big) == "<grande>", "a picture over the limit: " + Text(big));
            Check(Text(bad) == "<fallo>", "a broken picture: " + Text(bad));
            Check(Text(ok) == "normal", "a normal one: " + Text(ok));
            Check(b.Reads == 1, "only the normal one goes to the OCR: " + b.Reads);
            Check(HistoryIndex.Search(HistoryIndex.Catalog(b.S), "enorme", 0).Count == 1, "the big one is found by its name");
            bool broken;
            List<HistoryIndex.Entry> saved = HistoryIndex.LoadFile(b.IndexFile, out broken);
            Check(saved.Count == 2, "saved: the big one and the normal one (a failure is tried again next time), got " + saved.Count);
        }

        static void PausedWhileRecording()
        {
            Box b = NewBox("pausa", false);
            bool busy = true;
            HistoryIndex.Busy = delegate { return busy; };
            Blank(b, Path.Combine(b.Save, "a.png"), 331, "uno", DateTime.Now);
            HistoryIndex.Attach(null, b.S);
            Thread.Sleep(700);
            Check(HistoryIndex.Paused && b.Reads == 0, "reading while recording (paused " + HistoryIndex.Paused + ", reads " + b.Reads + ")");
            busy = false;
            WaitPass(0, 20000);
            Check(!HistoryIndex.Paused && b.Reads == 1, "after the recording: paused " + HistoryIndex.Paused + ", reads " + b.Reads);
        }

        // ---- Timings

        static readonly string[] Vocabulary =
        {
            "factura", "cliente", "importe", "fecha", "pedido", "env\u00EDo", "direcci\u00F3n", "tel\u00E9fono", "correo", "usuario", "contrase\u00F1a",
            "configuraci\u00F3n", "archivo", "carpeta", "ventana", "bot\u00F3n", "aceptar", "cancelar", "guardar", "abrir", "cerrar", "error", "aviso",
            "informe", "resumen", "total", "pendiente", "pagado", "reuni\u00F3n", "proyecto", "equipo", "ventas", "compras", "almac\u00E9n", "n\u00F3mina",
            "expediente", "solicitud", "registro", "c\u00F3digo", "referencia", "descripci\u00F3n", "cantidad", "precio", "impuesto", "descuento"
        };

        static List<HistoryItem> Synthetic(int n)
        {
            Random r = new Random(17);
            List<HistoryItem> l = new List<HistoryItem>(n);
            DateTime now = DateTime.Now;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Length = 0;
                while (sb.Length < 800)
                {
                    sb.Append(Vocabulary[r.Next(Vocabulary.Length)]);
                    sb.Append(r.Next(9) == 0 ? '\n' : ' ');
                    if (r.Next(6) == 0) sb.Append(r.Next(100000)).Append(' ');
                }
                l.Add(Item("Captura " + now.AddMinutes(-i).ToString("yyyy-MM-dd HH.mm.ss") + ".png", HistoryKind.Image, sb.ToString(), now.AddMinutes(-i)));
            }
            return l;
        }

        static double Median(List<double> v)
        {
            v.Sort();
            return v[v.Count / 2];
        }

        static void SearchTimings()
        {
            List<HistoryItem> l = Synthetic(5000);
            Stopwatch sw = Stopwatch.StartNew();
            int n = HistoryIndex.Search(l, "factura cliente", 0).Count;
            double cold = sw.Elapsed.TotalMilliseconds;
            string[] queries = { "factura", "factura cliente", "contrasena configuracion boton", "zzzz", "reunion 123", "nomina total pendiente" };
            List<double> warm = new List<double>();
            foreach (string q in queries)
                for (int k = 0; k < 5; k++)
                {
                    sw.Restart();
                    HistoryIndex.Search(l, q, 0);
                    warm.Add(sw.Elapsed.TotalMilliseconds);
                }
            double median = Median(warm), worst = warm[warm.Count - 1];
            Console.WriteLine("    5000 captures of ~800 characters: first search " + cold.ToString("0.0") + " ms (folds the texts, " + n + " results); then median " + median.ToString("0.0") + " ms, worst " + worst.ToString("0.0") + " ms");
            Check(median < 50, "search over 5000 entries took " + median.ToString("0.0") + " ms (median)");
            Check(worst < 100, "worst search over 5000 entries took " + worst.ToString("0.0") + " ms");
            sw.Restart();
            for (int i = 0; i < 200; i++) HistoryIndex.Snippet(l[i], HistoryIndex.Words("factura cliente"), 90);
            Console.WriteLine("    200 snippets: " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms");
        }

        // ---- The page

        static HomeWindow Make(int appearance, Settings s)
        {
            Ds.Apply(appearance);
            s.Appearance = appearance;
            Drain();
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            HomeWindow hw = new HomeWindow(owner, s);
            Drain();
            return hw;
        }

        // Lets everything already queued on the UI thread run first (a theme change rebuilds every window other suites
        // left behind), so the timings that follow are this page's own.
        static void Drain()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, (Action)delegate { });
        }

        static void SetPage(HomeWindow hw, string id)
        {
            MethodInfo m = typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance);
            try { m.Invoke(hw, new object[] { id, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("page " + id + ": " + ex.InnerException, ex.InnerException); }
        }

        static object Field(object o, string name)
        {
            return typeof(HomeWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        }

        static void Pump(Func<bool> done, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!done() && sw.ElapsedMilliseconds < ms)
            {
                DispatcherFrame f = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate { f.Continue = false; });
                Dispatcher.PushFrame(f);
                if (!done()) Thread.Sleep(5);
            }
        }

        static void Layout(HomeWindow hw, int w, int h)
        {
            W.UIElement root = (W.UIElement)hw.Content;
            root.Measure(new W.Size(w, h));
            root.Arrange(new W.Rect(0, 0, w, h));
            root.UpdateLayout();
        }

        static int LiveRows(HomeWindow hw)
        {
            object g = Field(hw, "historyGrid");
            return g == null ? 0 : (int)g.GetType().GetProperty("LiveRows").GetValue(g, null);
        }

        static bool Loaded(HomeWindow hw) { return Field(hw, "historyAll") != null; }

        // The texts on show (a collapsed part is skipped).
        static void Collect(object o, List<string> acc)
        {
            W.UIElement u = o as W.UIElement;
            if (u != null && u.Visibility != W.Visibility.Visible) return;
            WC.TextBlock tb = o as WC.TextBlock;
            if (tb != null && tb.Text != null) acc.Add(tb.Text);
            if (tb != null) foreach (WD.Inline i in tb.Inlines) Inline(i, acc);
            W.DependencyObject d = o as W.DependencyObject;
            if (d == null) return;
            foreach (object c in W.LogicalTreeHelper.GetChildren(d)) Collect(c, acc);
        }

        // Runs inside links and spans count too.
        static void Inline(WD.Inline i, List<string> acc)
        {
            WD.Run r = i as WD.Run;
            if (r != null) { acc.Add(r.Text); return; }
            WD.Span s = i as WD.Span;
            if (s != null) foreach (WD.Inline c in s.Inlines) Inline(c, acc);
        }

        static List<string> TextsOf(HomeWindow hw)
        {
            List<string> acc = new List<string>();
            Collect(hw.Content, acc);
            return acc;
        }

        static bool Shows(List<string> texts, string part)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // Runs drawn as a highlight (a background), and their text.
        static void Marks(object o, List<string> acc)
        {
            WC.TextBlock tb = o as WC.TextBlock;
            if (tb != null)
                foreach (WD.Inline i in tb.Inlines)
                {
                    WD.Run r = i as WD.Run;
                    if (r != null && r.Background != null) acc.Add(r.Text);
                }
            W.DependencyObject d = o as W.DependencyObject;
            if (d == null) return;
            foreach (object c in W.LogicalTreeHelper.GetChildren(d)) Marks(c, acc);
        }

        static void Shot(HomeWindow hw, int w, int h, string file)
        {
            string dir = Path.Combine(Root(), "shots");
            Directory.CreateDirectory(dir);
            W.UIElement root = (W.UIElement)hw.Content;
            Layout(hw, w, h);
            Pump(delegate { return false; }, 600);   // the thumbnails, decoded on the thread pool
            Layout(hw, w, h);
            // The window's own background first (it is not part of its content), then the content over it; no VisualBrush,
            // which would keep laying the window out afterwards.
            WM.DrawingVisual dv = new WM.DrawingVisual();
            using (WM.DrawingContext dc = dv.RenderOpen()) dc.DrawRectangle(hw.Background, null, new W.Rect(0, 0, w, h));
            WMI.RenderTargetBitmap rtb = new WMI.RenderTargetBitmap(w, h, 96, 96, WM.PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Render(root);
            WMI.PngBitmapEncoder enc = new WMI.PngBitmapEncoder();
            enc.Frames.Add(WMI.BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(Path.Combine(dir, file))) enc.Save(fs);
        }

        // Opens the page and waits for its listing; ms is how long that took (without building the window).
        static HomeWindow OpenPage(int appearance, Settings s, out double ms)
        {
            HomeWindow hw = Make(appearance, s);
            Stopwatch sw = Stopwatch.StartNew();
            SetPage(hw, "history");
            double build = sw.Elapsed.TotalMilliseconds;
            Pump(delegate { return Loaded(hw); }, 10000);
            Check(Loaded(hw), "the listing never arrived");
            double listed = sw.Elapsed.TotalMilliseconds;
            Layout(hw, 980, 660);
            ms = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("    open: page built " + build.ToString("0") + " ms, listing in " + listed.ToString("0") + " ms, laid out " + ms.ToString("0") + " ms");
            return hw;
        }

        static void Type(HomeWindow hw, string text)
        {
            WC.TextBox box = (WC.TextBox)Field(hw, "historyBox");
            box.Text = text;
            int before = (int)Field(hw, "historySearches");
            Pump(delegate { return (int)Field(hw, "historySearches") > before; }, 2000);
            Pump(delegate { return false; }, 250);
            Layout(hw, 980, 660);
        }

        static Box PageBox(string name)
        {
            Box b = NewBox(name, false);
            DateTime now = DateTime.Now;
            string month = Path.Combine(b.Save, now.AddMonths(-1).ToString("yyyy-MM"));
            Directory.CreateDirectory(month);
            Blank(b, Path.Combine(b.Temp, "Captura de la factura.png"), 501, "Factura n\u00FAmero 4521 del cliente Jos\u00E9 P\u00E9rez, importe total 1.234,56 euros", now.AddMinutes(-3));
            Blank(b, Path.Combine(b.Temp, "Pantalla correo.png"), 502, "Bandeja de entrada\nAsunto: reuni\u00F3n del equipo de ventas el jueves", now.AddMinutes(-30));
            Blank(b, Path.Combine(b.Save, "Ventana ajustes.png"), 503, "Configuraci\u00F3n avanzada\nGuardar cambios", now.AddDays(-1));
            Blank(b, Path.Combine(b.Save, "Error del servidor.png"), 504, "Error 500: el servidor no responde", now.AddDays(-2).AddHours(-1));
            Blank(b, Path.Combine(month, "Informe mensual.png"), 505, "Informe mensual de ventas y compras", now.AddMonths(-1));
            Bytes(Path.Combine(b.Save, "Grabaci\u00F3n de la demo.mp4"), 2048, now.AddHours(-2));
            Bytes(Path.Combine(b.Temp, "Animaci\u00F3n bot\u00F3n.gif"), 1024, now.AddDays(-1).AddHours(-3));
            for (int i = 0; i < 6; i++) Blank(b, Path.Combine(b.Save, "Captura antigua " + i + ".png"), 510 + i, "texto antiguo " + i, now.AddMonths(-2).AddDays(-i));
            return b;
        }

        static void PageBuilds(int appearance)
        {
            string theme = appearance == 2 ? "dark" : "light";
            Box b = PageBox("pagina-" + theme);
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            double ms;
            HomeWindow hw = OpenPage(appearance, b.S, out ms);
            List<string> t = TextsOf(hw);
            Console.WriteLine("    page with 14 captures, " + theme + ": " + ms.ToString("0") + " ms; rows built " + LiveRows(hw));
            foreach (string s in new string[] { "Historial", "Todo", "Capturas", "V\u00EDdeos", "GIF", "Hoy", "13 elementos", "Ctrl+F", "Buscar por texto o nombre" })
                Check(Shows(t, s), theme + ": no text <" + s + ">");
            Check(!Shows(t, "Leyendo el texto"), theme + ": says it is reading after the pass ended");
            Check(LiveRows(hw) > 0, "no rows built");
            Shot(hw, 980, 1200, "historial-" + theme + ".png");
            // Searching: one result, its snippet with the match highlighted.
            Type(hw, "FACTURA perez");
            t = TextsOf(hw);
            Check(Shows(t, "1 resultado"), theme + ": search count; texts: " + string.Join(" | ", t.ToArray()));
            List<string> marks = new List<string>();
            Marks(hw.Content, marks);
            Console.WriteLine("    highlighted: " + string.Join(" | ", marks.ToArray()));
            Check(marks.Contains("Factura") && marks.Contains("P\u00E9rez"), theme + ": the matches are not highlighted: " + string.Join(" | ", marks.ToArray()));
            Shot(hw, 980, 660, "historial-busqueda-" + theme + ".png");
            // A name: a video.
            Type(hw, "demo");
            Check(Shows(TextsOf(hw), "1 resultado"), theme + ": a video by its name");
            // Nothing.
            Type(hw, "zzzz qqqq");
            t = TextsOf(hw);
            Check(Shows(t, "Nada coincide con \u00ABzzzz qqqq\u00BB"), theme + ": the no-results message");
            Shot(hw, 980, 660, "historial-nada-" + theme + ".png");
            // The GIF chip.
            Type(hw, "");
            typeof(HomeWindow).GetMethod("HistoryPick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, new object[] { 3 });
            Pump(delegate { return false; }, 300);
            Layout(hw, 980, 660);
            Check(Shows(TextsOf(hw), "1 elemento"), theme + ": the GIF chip");
        }

        static void PageWhileReadingAndOff()
        {
            Box b = PageBox("pagina-leyendo");
            b.Delay = 250;
            HistoryIndex.Attach(null, b.S);
            double ms;
            HomeWindow hw = OpenPage(1, b.S, out ms);
            Pump(delegate { return Shows(TextsOf(hw), "Leyendo el texto de las capturas"); }, 8000);
            List<string> t = TextsOf(hw);
            string line = null;
            foreach (string s in t) if (s.StartsWith("Leyendo el texto de las capturas\u2026 ", StringComparison.Ordinal)) line = s;
            Console.WriteLine("    while reading: " + line);
            Check(line != null && line.Contains(" de "), "no progress line while reading");
            Shot(hw, 980, 660, "historial-leyendo.png");
            // The UI thread never waits on the reading: every dispatcher operation stays short meanwhile.
            double worst = 0;
            string worstName = "";
            DispatcherHooks hooks = Dispatcher.CurrentDispatcher.Hooks;
            Dictionary<DispatcherOperation, Stopwatch> running = new Dictionary<DispatcherOperation, Stopwatch>();
            FieldInfo method = typeof(DispatcherOperation).GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<DispatcherOperation, int> gcs = new Dictionary<DispatcherOperation, int>();
            DispatcherHookEventHandler started = delegate(object o, DispatcherHookEventArgs e)
            {
                running[e.Operation] = Stopwatch.StartNew();
                gcs[e.Operation] = GC.CollectionCount(0) + GC.CollectionCount(1) * 100 + GC.CollectionCount(2) * 10000;
            };
            DispatcherHookEventHandler completed = delegate(object o, DispatcherHookEventArgs e)
            {
                Stopwatch w;
                if (!running.TryGetValue(e.Operation, out w)) return;
                running.Remove(e.Operation);
                int gc0 = gcs[e.Operation];
                gcs.Remove(e.Operation);
                if (w.Elapsed.TotalMilliseconds <= worst) return;
                worst = w.Elapsed.TotalMilliseconds;
                Delegate d = method != null ? method.GetValue(e.Operation) as Delegate : null;
                int gc = GC.CollectionCount(0) + GC.CollectionCount(1) * 100 + GC.CollectionCount(2) * 10000 - gc0;
                DispatcherTimer timer = d != null ? d.Target as DispatcherTimer : null;
                worstName = (d != null ? d.Method.DeclaringType.Name + "." + d.Method.Name : "?") + (timer != null ? " every " + timer.Interval.TotalMilliseconds + " ms" : "") +
                            (gc != 0 ? ", with a garbage collection" : "");
            };
            Drain();
            hooks.OperationStarted += started;
            hooks.OperationCompleted += completed;
            try
            {
                int p = HistoryIndex.Passes;
                Pump(delegate { return HistoryIndex.Passes > p && HistoryIndex.Idle; }, 30000);
                Pump(delegate { return false; }, 1700);
            }
            finally
            {
                hooks.OperationStarted -= started;
                hooks.OperationCompleted -= completed;
            }
            Console.WriteLine("    longest UI operation while the index read 13 captures: " + worst.ToString("0.0") + " ms (" + worstName + ")");
            Check(worst < 30, "a UI operation took " + worst.ToString("0.0") + " ms while reading");
            Check(!Shows(TextsOf(hw), "Leyendo el texto"), "the progress line stays after reading");
            // Off: the explanation and the way to turn it on; the list is still searched by name.
            Box off = PageBox("pagina-apagado");
            off.S.HistorySearch = false;
            HomeWindow hw2 = OpenPage(1, off.S, out ms);
            t = TextsOf(hw2);
            Check(Shows(t, "La b\u00FAsqueda por texto est\u00E1 desactivada"), "off: no explanation");
            Check(Shows(t, "Activar la b\u00FAsqueda por texto"), "off: no link to turn it on");
            Check(Shows(t, "General\u00A0\u203A\u00A0Recientes"), "off: where the switch is");
            Check(Shows(t, "Buscar por nombre"), "off: the field says it searches names");
            Shot(hw2, 980, 900, "historial-apagado.png");
            Type(hw2, "ventas");
            t = TextsOf(hw2);
            Check(Shows(t, "Nada coincide"), "off: the text was searched");
            Check(Shows(t, "Solo se busca por el nombre"), "off: the no-results message explains why");
            // No captures at all.
            Box none = NewBox("pagina-vacia", false);
            HomeWindow hw3 = OpenPage(2, none.S, out ms);
            Check(Shows(TextsOf(hw3), "Todav\u00EDa no hay capturas"), "empty: no empty state");
            Shot(hw3, 980, 660, "historial-vacio-dark.png");
        }

        // A tiny real PNG written many times, spread over two months, half of it in month folders.
        static Box ManyFiles(string name, int n)
        {
            Box b = NewBox(name, false);
            byte[] png;
            using (Bitmap bmp = new Bitmap(32, 20))
            using (MemoryStream ms = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.SteelBlue);
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }
            DateTime now = DateTime.Now;
            for (int i = 0; i < n; i++)
            {
                DateTime when = now.AddMinutes(-i * 40);
                string dir = i % 2 == 0 ? b.Save : Path.Combine(b.Save, when.ToString("yyyy-MM"));
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "Captura " + when.ToString("yyyy-MM-dd HH.mm.ss") + " " + i + ".png");
                File.WriteAllBytes(p, png);
                File.SetLastWriteTime(p, when);
            }
            return b;
        }

        static void PageOpensFast()
        {
            Box b = ManyFiles("muchas", 2000);
            double first, second;
            HomeWindow hw = OpenPage(1, b.S, out first);
            object all = Field(hw, "historyAll");
            int count = (int)all.GetType().GetProperty("Count").GetValue(all, null);
            Check(count == 2000, "listed " + count);
            HomeWindow hw2 = OpenPage(1, b.S, out second);
            Stopwatch sw = Stopwatch.StartNew();
            Layout(hw2, 980, 661);
            double layout = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("    page with 2000 captures: first open " + first.ToString("0") + " ms (with JIT), second " + second.ToString("0") + " ms; rows built " + LiveRows(hw2) + "; relayout " + layout.ToString("0.0") + " ms");
            Check(second < 300, "the page took " + second.ToString("0") + " ms to open with 2000 captures");
            // The grid alone, laid out again at another width (every row computed again, the visible ones rebuilt).
            W.UIElement g2 = (W.UIElement)Field(hw2, "historyGrid");
            List<double> lays = new List<double>();
            for (int i = 0; i < 6; i++)
            {
                sw.Restart();
                g2.Measure(new W.Size(640 - i, double.PositiveInfinity));
                lays.Add(sw.Elapsed.TotalMilliseconds);
            }
            double gridLay = Median(lays);
            Console.WriteLine("    grid relaid out at a new width: median " + gridLay.ToString("0.0") + " ms");
            Check(gridLay < 16, "the grid's layout took " + gridLay.ToString("0.0") + " ms");
            Check(LiveRows(hw2) > 0 && LiveRows(hw2) < 40, "rows built: " + LiveRows(hw2) + " (only those near the view)");
            // Thumbnails come a few at a time, and every tile built gets one.
            int most = 0;
            Layout(hw2, 980, 660);
            Pump(delegate
            {
                most = Math.Max(most, (int)Field(hw2, "historyThumbsBusy"));
                return false;
            }, 400);
            Pump(delegate
            {
                most = Math.Max(most, (int)Field(hw2, "historyThumbsBusy"));
                return (int)Field(hw2, "historyThumbsBusy") == 0 && ((System.Collections.ICollection)Field(hw2, "historyThumbs")).Count == 0;
            }, 15000);
            int tiles = 0, shown = 0;
            Tiles(hw2.Content, ref tiles, ref shown);
            Console.WriteLine("    thumbnails: " + shown + " of " + tiles + " tiles built, at most " + most + " decoding at once");
            Check(most <= 3, "thumbnails decoded " + most + " at once");
            Check(tiles > 0 && shown == tiles, "tiles without a thumbnail: " + (tiles - shown) + " of " + tiles);
            // Scrolled to the end: the rows there are built and the first ones let go.
            WC.ScrollViewer sv = (WC.ScrollViewer)Field(hw2, "scroller");
            sv.ScrollToVerticalOffset(sv.ExtentHeight);
            Layout(hw2, 980, 660);
            Layout(hw2, 980, 660);
            Check(LiveRows(hw2) > 0 && LiveRows(hw2) < 40, "rows built at the end: " + LiveRows(hw2));
            object grid = Field(hw2, "historyGrid");
            System.Collections.ICollection keys = ((System.Collections.IDictionary)grid.GetType().GetField("live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(grid)).Keys;
            int lines = ((System.Collections.ICollection)grid.GetType().GetField("lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(grid)).Count;
            int lo = int.MaxValue, hi = -1;
            foreach (int k in keys) { lo = Math.Min(lo, k); hi = Math.Max(hi, k); }
            Console.WriteLine("    at the end: lines " + lo + " to " + hi + " of " + lines + " built");
            Check(hi == lines - 1 && lo > 10, "at the end of the list the rows built are " + lo + " to " + hi + " of " + lines);
            Shot(hw2, 980, 660, "historial-2000-final.png");
        }

        // ---- General > Recientes

        static void GeneralRowFollowsIndex()
        {
            Box b = NewBox("general", false);
            Blank(b, Path.Combine(b.Save, "a.png"), 341, "uno", DateTime.Now);
            HomeWindow before = Make(1, b.S);
            SetPage(before, "general");
            Check(!Shows(TextsOf(before), "Borrar el \u00EDndice de texto"), "the row shows without an index");
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(HistoryIndex.Exists, "no index after a pass");
            HomeWindow hw = Make(1, b.S);
            SetPage(hw, "general");
            Layout(hw, 980, 3200);
            List<string> t = TextsOf(hw);
            Check(Shows(t, "Borrar el \u00EDndice de texto"), "no row to delete the index");
            Check(Shows(t, "Buscar por el texto de las capturas"), "the switch");
            HomeWindow.HistoryIndexRow row = null;
            Find(hw.Content, ref row);
            Check(row != null && row.Visibility == W.Visibility.Visible, "the row is not visible");
            HistoryIndex.Delete();
            WaitDeleted();
            row.Sync(false);
            Check(row.Visibility == W.Visibility.Collapsed, "the row stays after deleting");
            WC.Panel list = row.Parent as WC.Panel;
            int i = list.Children.IndexOf(row);
            Check(list.Children[i - 1].Visibility == W.Visibility.Collapsed, "the separator above it stays");
            Check(!File.Exists(b.IndexFile), "the file is still there");
        }

        // Tiles built in the grid, and how many show a picture.
        static void Tiles(object o, ref int tiles, ref int shown)
        {
            if (o != null && o.GetType().Name == "HistoryTile")
            {
                tiles++;
                WC.Border img = (WC.Border)o.GetType().GetField("Img").GetValue(o);
                if (img.Background is WM.ImageBrush) shown++;
                return;
            }
            W.DependencyObject d = o as W.DependencyObject;
            if (d == null) return;
            foreach (object c in W.LogicalTreeHelper.GetChildren(d)) Tiles(c, ref tiles, ref shown);
        }

        static void Find(object o, ref HomeWindow.HistoryIndexRow row)
        {
            if (row != null) return;
            HomeWindow.HistoryIndexRow r = o as HomeWindow.HistoryIndexRow;
            if (r != null) { row = r; return; }
            W.DependencyObject d = o as W.DependencyObject;
            if (d == null) return;
            foreach (object c in W.LogicalTreeHelper.GetChildren(d)) Find(c, ref row);
        }

        // ---- Review round (R3): retention, deleting, policies

        // The folders are listed again on time even while new captures keep waking the thread: before, each capture
        // restarted the 10-minute wait, so a deleted capture's text could stay in the index for as long as you kept capturing.
        static void RescanWhileCapturing()
        {
            Box b = NewBox("reescaneo", false);
            HistoryIndex.IdleRescan = 600;
            DateTime now = DateTime.Now;
            string gone = Path.Combine(b.Save, "borrada.png");
            Blank(b, gone, 601, "texto de una captura borrada", now.AddMinutes(-5));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(Text(gone) != null, "first pass");
            File.Delete(gone);
            Stopwatch sw = Stopwatch.StartNew();
            int i = 0;
            while (sw.ElapsedMilliseconds < 3000 && Text(gone) != null)
            {
                string p = Path.Combine(b.Temp, "nueva " + i + ".png");
                Blank(b, p, 700 + i, "nueva " + i, DateTime.Now);
                HistoryIndex.NotifyCaptured(p);
                i++;
                Thread.Sleep(120);
            }
            Console.WriteLine("    deleted capture's text gone after " + sw.ElapsedMilliseconds + " ms, " + i + " captures meanwhile (rescan every 600 ms)");
            Check(Text(gone) == null, "a deleted capture's text is still in the index after " + sw.ElapsedMilliseconds + " ms of captures every 120 ms");
            HistoryIndex.Flush();
            bool broken;
            foreach (HistoryIndex.Entry e in HistoryIndex.LoadFile(b.IndexFile, out broken))
                Check(!string.Equals(e.Path, gone, StringComparison.OrdinalIgnoreCase), "the deleted capture is still in the file");
        }

        // A temporary capture swept away (the "Al cerrar" sweep runs right before Stackshot closes, then the index is
        // saved) never reaches the file with its text.
        static void SweptTempLeavesFile()
        {
            Box b = NewBox("temporal-barrida", false);
            DateTime now = DateTime.Now;
            string swept = Path.Combine(b.Temp, "Captura barrida.png"), stays = Path.Combine(b.Temp, "Captura que sigue.png"), kept = Path.Combine(b.Save, "Guardada.png");
            Blank(b, swept, 611, "IBAN ES91 2100 0418 4502 0005 1332", now.AddMinutes(-3));
            Blank(b, stays, 612, "sigue aqui", now.AddMinutes(-2));
            Blank(b, kept, 613, "guardada", now.AddMinutes(-1));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            bool broken;
            Check(HistoryIndex.LoadFile(b.IndexFile, out broken).Count == 3, "3 entries saved first");
            File.Delete(swept);                 // the sweep
            HistoryIndex.Flush();               // what ApplicationExit and ProcessExit do next
            List<HistoryIndex.Entry> saved = HistoryIndex.LoadFile(b.IndexFile, out broken);
            List<string> names = new List<string>();
            foreach (HistoryIndex.Entry e in saved) names.Add(Path.GetFileName(e.Path));
            Check(!names.Contains("Captura barrida.png"), "the swept capture's text was saved again: " + string.Join(", ", names.ToArray()));
            Check(names.Contains("Captura que sigue.png") && names.Contains("Guardada.png") && saved.Count == 2, "the others stay: " + string.Join(", ", names.ToArray()));
            Check(Text(swept) == null && Text(stays) == "sigue aqui", "in memory too");
        }

        // An antivirus or the search indexer holding the index for a moment doesn't leave it behind once the setting is off.
        static void DeleteWhileHeld()
        {
            Box b = NewBox("borrar-retenido", false);
            Blank(b, Path.Combine(b.Save, "a.png"), 621, "uno", DateTime.Now);
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(File.Exists(b.IndexFile), "no index after a pass");
            using (FileStream held = new FileStream(b.IndexFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                b.S.HistorySearch = false;
                HistoryIndex.Apply(b.S);
                Thread.Sleep(700);
                Check(File.Exists(b.IndexFile), "the test could not hold the file");
            }
            WaitDeleted();
            Check(!File.Exists(b.IndexFile), "the index stays on disk with the setting off after the file was held for a moment");
        }

        // Without Windows' OCR new captures are not lined up for good (nothing would ever read them).
        static void NoOcrQueuesNothing()
        {
            Box b = NewBox("sin-ocr", false);
            HistoryIndex.Reader = delegate(Bitmap bmp, out string error)
            {
                Interlocked.Increment(ref b.Reads);
                error = Ocr.Unavailable;
                return null;
            };
            Blank(b, Path.Combine(b.Save, "a.png"), 631, "", DateTime.Now.AddMinutes(-1));
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(HistoryIndex.Unavailable, "no OCR noticed");
            for (int i = 0; i < 25; i++)
            {
                string p = Path.Combine(b.Temp, "n" + i + ".png");
                Blank(b, p, 640 + i, "", DateTime.Now);
                HistoryIndex.NotifyCaptured(p);
            }
            Thread.Sleep(400);
            System.Collections.ICollection fresh = (System.Collections.ICollection)typeof(HistoryIndex).GetField("fresh", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int done, total;
            HistoryIndex.Progress(out done, out total);
            Check(fresh.Count == 0, fresh.Count + " captures waiting for an OCR that isn't there");
            Check(total == 0, "a pass of " + total + " announced without OCR");
            Check(b.Reads == 1, "reads " + b.Reads);
            // The page says why the search goes by name only.
            double ms;
            HomeWindow hw = OpenPage(1, b.S, out ms);
            List<string> t = TextsOf(hw);
            Check(Shows(t, "Este Windows no tiene el reconocimiento de texto (OCR): de momento se busca solo por el nombre."), "the page does not say there is no OCR");
            Check(!Shows(t, "Leyendo el texto"), "the page says it is reading without OCR");
        }

        // Right after startup the reading waits (StartDelay); opening the Historial starts it at once.
        static void StartDelayCutShortByPage()
        {
            Box b = NewBox("espera", false);
            HistoryIndex.StartDelay = 4000;
            Blank(b, Path.Combine(b.Save, "a.png"), 671, "uno", DateTime.Now);
            // The reset between tests leaves the wake-up event set (with no thread to take it): cleared, as at startup.
            ((AutoResetEvent)typeof(HistoryIndex).GetField("wake", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Reset();
            HistoryIndex.Attach(null, b.S);
            Thread.Sleep(700);
            Check(b.Reads == 0 && HistoryIndex.Passes == 0, "read during the start delay (" + b.Reads + ")");
            Stopwatch sw = Stopwatch.StartNew();
            HistoryIndex.RequestScan();   // what opening the page does
            WaitPass(0, 3000);
            Console.WriteLine("    read " + sw.ElapsedMilliseconds + " ms after the page asked, with 3.3 s of the start delay left");
            Check(b.Reads == 1 && Text(Path.Combine(b.Save, "a.png")) == "uno", "not read after the page asked");
        }

        // Turned off while a scan runs: nothing more goes to the OCR.
        static void OffDuringScanReadsNothing()
        {
            Box b = NewBox("apagado-escaneando", false);
            Blank(b, Path.Combine(b.Save, "a.png"), 651, "uno", DateTime.Now.AddMinutes(-1));
            bool armed = false;
            HistoryIndex.Busy = delegate
            {
                // Called on the reading thread right before it lists the folders: the user turns the setting off there.
                if (!armed) return false;
                armed = false;
                b.S.HistorySearch = false;
                HistoryIndex.Apply(b.S);
                return false;
            };
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            int reads = b.Reads;
            Blank(b, Path.Combine(b.Save, "b.png"), 652, "dos", DateTime.Now);
            armed = true;
            HistoryIndex.RequestScan();
            Stopwatch sw = Stopwatch.StartNew();
            while ((armed || HistoryIndex.Running) && sw.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Thread.Sleep(200);
            Check(!armed && !HistoryIndex.Running, "the thread did not stop");
            Check(b.Reads == reads, "read " + (b.Reads - reads) + " more after being turned off");
        }

        // The Recents thumbnail asked with the listing's time (nothing read from the disk on the UI thread) is the same
        // picture, from the same cache, as the one asked without it.
        static void ThumbWithKnownTime()
        {
            Box b = NewBox("miniatura", false);
            string p = Path.Combine(b.Save, "m.png");
            Blank(b, p, 160, "", DateTime.Now.AddMinutes(-1));
            long ticks = new FileInfo(p).LastWriteTimeUtc.Ticks;
            WMI.BitmapSource first = null, second = null;
            Recents.Thumb(p, ticks, 96, delegate(WMI.BitmapSource s) { first = s; });
            Pump(delegate { return first != null; }, 8000);
            Check(first != null, "no thumbnail with the listing's time");
            Recents.Thumb(p, 96, delegate(WMI.BitmapSource s) { second = s; });
            Check(second != null && ReferenceEquals(first, second), "the usual call does not find the same cached thumbnail");
        }

        // Throwaway keys standing for the policy hives (HKCU\Software\StackshotTests only), removed afterwards.
        sealed class HistoryPolicy : IDisposable
        {
            const string Root = @"Software\StackshotTests\HistoryPolicy";
            readonly RegistryKey machine, user;

            public HistoryPolicy()
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(Root, false); } catch { }
                machine = Registry.CurrentUser.CreateSubKey(Root + @"\Machine");
                user = Registry.CurrentUser.CreateSubKey(Root + @"\User");
                Policy.MachineOverride = machine;
                Policy.UserOverride = user;
                Policy.Refresh();
            }

            public void Dword(string name, int value)
            {
                using (RegistryKey k = user.CreateSubKey(Policy.KeyPath)) k.SetValue(name, value, RegistryValueKind.DWord);
            }

            public void Clear() { try { user.DeleteSubKeyTree("SOFTWARE", false); } catch { } }

            public void Dispose()
            {
                Clear();
                Policy.Refresh();
                Policy.MachineOverride = null;
                Policy.UserOverride = null;
                machine.Close();
                user.Close();
                try { Registry.CurrentUser.DeleteSubKeyTree(Root, false); } catch { }
                try
                {
                    bool empty;
                    using (RegistryKey t = Registry.CurrentUser.OpenSubKey(@"Software\StackshotTests"))
                        empty = t != null && t.SubKeyCount == 0 && t.ValueCount == 0;
                    if (empty) Registry.CurrentUser.DeleteSubKey(@"Software\StackshotTests", false);
                }
                catch { }
                Check(!Policy.Any, "policies forgotten after the test");
            }
        }

        // With HistorySearch forced off by IT, the Historial says who decides and offers no way to turn the reading on.
        static void PolicyPageOffersNoSwitch()
        {
            using (HistoryPolicy p = new HistoryPolicy())
            {
                p.Dword("HistorySearch", 0);
                Box b = PageBox("directiva-pagina");
                b.S.MascotOn = false;
                b.S.MascotDesktop = false;
                Policy.Apply(b.S);
                Check(!b.S.HistorySearch, "the policy did not force it off");
                HistoryIndex.Attach(null, b.S);
                double ms;
                HomeWindow hw = OpenPage(1, b.S, out ms);
                List<string> t = TextsOf(hw);
                Check(Shows(t, "La b\u00FAsqueda por texto est\u00E1 desactivada"), "no explanation");
                Check(Shows(t, Policy.Managed), "it does not say who decides: " + string.Join(" | ", t.ToArray()));
                Check(!Shows(t, "Activar la b\u00FAsqueda por texto"), "a link turns on what the organization keeps off");
                Shot(hw, 980, 660, "historial-directiva.png");
                try { typeof(HomeWindow).GetMethod("HistoryTurnOn", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, null); }
                catch (TargetInvocationException) { }   // the owner is not built: whatever it would have done next
                Thread.Sleep(500);
                Check(!b.S.HistorySearch && !HistoryIndex.Running && b.Reads == 0,
                      "turned on against the policy (setting " + b.S.HistorySearch + ", reading " + HistoryIndex.Running + ", reads " + b.Reads + ")");
            }
        }

        // A HistorySearch policy that appears while Stackshot runs stops the reading and deletes the index as soon as a
        // section is built (HomeWindow.ReadPolicy); once it is removed, the reading starts again.
        static void PolicyChangeReachesIndex()
        {
            Box b = NewBox("directiva-en-marcha", false);
            b.S.MascotOn = false;
            b.S.MascotDesktop = false;
            Blank(b, Path.Combine(b.Save, "a.png"), 661, "uno", DateTime.Now);
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            Check(HistoryIndex.Exists && HistoryIndex.Running, "no index to start with");
            using (HistoryPolicy p = new HistoryPolicy())
            {
                ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
                HomeWindow hw = new HomeWindow(owner, b.S);
                typeof(ShotStack).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, b.S);
                MethodInfo read = typeof(HomeWindow).GetMethod("ReadPolicy", BindingFlags.NonPublic | BindingFlags.Instance);
                p.Dword("HistorySearch", 0);
                read.Invoke(hw, null);
                Check(!b.S.HistorySearch, "the policy did not force it off");
                WaitDeleted();
                Stopwatch sw = Stopwatch.StartNew();
                while (HistoryIndex.Running && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10);
                Check(!HistoryIndex.Running, "still reading with HistorySearch forced off");
                Check(!File.Exists(b.IndexFile) && HistoryIndex.Count == 0, "the index stays with HistorySearch forced off");
                p.Clear();
                int before = b.Reads, passes = HistoryIndex.Passes;
                read.Invoke(hw, null);
                Check(b.S.HistorySearch, "the user's choice did not come back");
                WaitPass(passes, 20000);
                Check(HistoryIndex.Running && b.Reads > before && HistoryIndex.Exists, "the reading did not start again (reads " + (b.Reads - before) + ")");
            }
        }

        // Historial, then General to turn the search off, then Historial again in the same window: what was read before
        // is neither shown (the page used to show its last results at once, snippets and all) nor searched any more.
        static void OffForgetsShownTexts()
        {
            Box b = PageBox("apagar-y-volver");
            b.S.MascotOn = false;
            HistoryIndex.Attach(null, b.S);
            WaitPass(0, 20000);
            double ms;
            HomeWindow hw = OpenPage(1, b.S, out ms);
            Type(hw, "4521");
            Check(Shows(TextsOf(hw), "1 resultado"), "the text was not found while on");
            SetPage(hw, "general");
            b.S.HistorySearch = false;
            HistoryIndex.Apply(b.S);
            WaitDeleted();
            SetPage(hw, "history");
            Layout(hw, 980, 660);
            List<string> right = TextsOf(hw);
            Check(!Shows(right, "4521"), "right after coming back the old snippets are on show: " + string.Join(" | ", right.ToArray()));
            Pump(delegate { return Loaded(hw) && Shows(TextsOf(hw), "Nada coincide"); }, 10000);
            Layout(hw, 980, 660);
            List<string> later = TextsOf(hw);
            Check(!Shows(later, "4521 del cliente") && Shows(later, "Nada coincide con \u00AB4521\u00BB"), "the text is still searched with the search off: " + string.Join(" | ", later.ToArray()));
            Type(hw, "factura");
            later = TextsOf(hw);
            Check(Shows(later, "1 resultado") && !Shows(later, "importe total"), "found by its name only: " + string.Join(" | ", later.ToArray()));
        }

        // Earlier runs' folders go (the shots of this run stay to be looked at); the 2000 files go when the suite ends.
        static void Tidy(bool end)
        {
            try
            {
                if (end) { Directory.Delete(Path.Combine(Root(), "muchas"), true); return; }
                foreach (string d in Directory.GetDirectories(Path.GetDirectoryName(Root())))
                    if (!string.Equals(d, Root(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(d, true);
            }
            catch (Exception) { }
        }

        public static void Suite()
        {
            Tidy(false);
            try { Suites(); }
            finally { Tidy(true); }
        }

        static void Suites()
        {
            Run("fold keeps the length, drops accents and case", FoldKeepsLength);
            Run("search: accents, case, every word, names, filters", SearchSemantics);
            Run("snippet around the first match, every word marked", Snippets);
            Run("day groups: Hoy, Ayer, Esta semana, Este mes, Anteriores", DayGroups);
            Run("index built with the real OCR, read back, idle CPU", BuildWithRealOcr);
            Run("deleted, renamed, changed, new and kept captures", ChangesAreFollowed);
            Run("a broken index is skipped or rebuilt, never fatal", BrokenIndexIsRebuilt);
            Run("setting off: nothing read, index deleted, thread stopped", OffMeansNothing);
            Run("cap: only the newest captures", CapKeepsNewest);
            Run("too big and broken pictures are listed, not read", BigAndBrokenImages);
            Run("paused while recording", PausedWhileRecording);
            Run("search over 5000 entries under 50 ms", SearchTimings);
            Run("page builds off screen, light", delegate { PageBuilds(1); });
            Run("page builds off screen, dark", delegate { PageBuilds(2); });
            Run("page while reading, with the setting off and empty", PageWhileReadingAndOff);
            Run("page opens under 300 ms with 2000 captures", PageOpensFast);
            Run("General: the delete-index row follows the index", GeneralRowFollowsIndex);
            Run("review: folders listed again on time while captures keep coming", RescanWhileCapturing);
            Run("review: a swept temporary capture's text never reaches the file", SweptTempLeavesFile);
            Run("review: the index is deleted even if something held it a moment", DeleteWhileHeld);
            Run("review: without OCR nothing is lined up for good, and the page says so", NoOcrQueuesNothing);
            Run("review: the start delay ends as soon as the page asks", StartDelayCutShortByPage);
            Run("review: turned off during a scan, nothing more is read", OffDuringScanReadsNothing);
            Run("review: thumbnails asked with the listing's time share the cache", ThumbWithKnownTime);
            Run("review: HistorySearch forced off: the page offers no switch", PolicyPageOffersNoSwitch);
            Run("review: a policy that appears or goes while running reaches the index", PolicyChangeReachesIndex);
            Run("review: turned off in General, the page forgets the texts it showed", OffForgetsShownTexts);
        }
    }
}

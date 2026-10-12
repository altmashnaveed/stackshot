// The folder watch for XML files (suite "xmlwatch"): a watcher on a throwaway folder under %TEMP%, fed with synthetic
// SOAP messages written here. The folder of a real client is never read.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Stackshot.Tests
{
    static class AppTestsXml
    {
        const string SuiteName = "xmlwatch";

        const string Request = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:pru=\"urn:prueba\">\n" +
                               "  <soapenv:Header/>\n  <soapenv:Body>\n    <pru:consultaPrueba><pru:id>123</pru:id></pru:consultaPrueba>\n  </soapenv:Body>\n</soapenv:Envelope>\n";
        const string Response = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\"><S:Body wsu:Id=\"b1\" xmlns:wsu=\"urn:wsu\">" +
                                "<ns2:consultaPruebaResponse xmlns:ns2=\"urn:prueba\"><return>&lt;Respuesta&gt;&lt;Codigo&gt;0&lt;/Codigo&gt;&lt;/Respuesta&gt;</return></ns2:consultaPruebaResponse></S:Body></S:Envelope>";
        const string FaultMsg = "<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\"><S:Body><S:Fault><faultcode>S:Server</faultcode><faultstring>Error de prueba</faultstring></S:Fault></S:Body></S:Envelope>";

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static void Same<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(what + ": expected <" + expected + ">, got <" + actual + ">");
        }

        static string Folder(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "stackshot-app-xml", Guid.NewGuid().ToString("N"), name);
            Directory.CreateDirectory(d);
            return d;
        }

        static void Pump(Func<bool> done, int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms && !done()) { Application.DoEvents(); Thread.Sleep(5); }
        }

        static void Wait(int ms) { Pump(delegate { return false; }, ms); }

        static void Put(string path, string text) { File.WriteAllText(path, text, new UTF8Encoding(false)); }

        // A watcher with its own window handle, recording what it opens.
        sealed class Rig : IDisposable
        {
            public readonly Control Sync = new Control();
            public readonly List<string> Opened = new List<string>();
            public readonly Settings S = new Settings();
            public readonly XmlWatch Watch;
            public Action<string> Also;

            public Rig(string dir)
            {
                Sync.CreateControl();
                IntPtr forceHandle = Sync.Handle;
                Watch = new XmlWatch(Sync, delegate(string p) { Opened.Add(p); if (Also != null) Also(p); });
                S.XmlWatch = true;
                S.XmlWatchFolder = dir;
                Watch.Apply(S);
            }

            public void Dispose() { Watch.Dispose(); Sync.Dispose(); }
        }

        public static void Suite()
        {
            AppTests.Test(SuiteName, "settings: XmlWatch and its folder written and read back", SettingsRoundTrip);
            AppTests.Test(SuiteName, "settings: off by default, garbage reads as off", SettingsDefaults);
            AppTests.Test(SuiteName, "folder: Egara's unless another one is chosen", FolderChoice);
            AppTests.Test(SuiteName, "watch: a file written slowly opens once, after the writer closes it", SlowWriter);
            AppTests.Test(SuiteName, "watch: other extensions and folders are ignored; .XML in capitals is not", Ignored);
            AppTests.Test(SuiteName, "watch: a file renamed to .xml opens", Renamed);
            AppTests.Test(SuiteName, "watch: request and response in a burst open in the order written", Burst);
            AppTests.Test(SuiteName, "watch: a file created empty opens once it has content", EmptyThenFilled);
            AppTests.Test(SuiteName, "watch: a failing open does not stop the next one", OpenThrows);
            AppTests.Test(SuiteName, "apply: off stops, on again starts without reopening old files, same twice keeps it", ApplyOnOff);
            AppTests.Test(SuiteName, "apply: a missing folder waits quietly; moving to another folder follows it", MissingAndMove);
            AppTests.Test(SuiteName, "operation: request, response, fault, plain root, comments, BOM, garbage", Operations);
            AppTests.Test(SuiteName, "recent and latest: newest first, capped, only .xml", RecentLatest);
            AppTests.Test(SuiteName, "sibling: by write time, ties by name, null at both ends", Siblings);
            AppTests.Test(SuiteName, "old viewer: the check answers without throwing (diagnostic)", OldViewer);
            AppTests.Test(SuiteName, "settings page: Avanzado shown on Egara PCs or when in use, a link otherwise", SettingsPage);
            AppTests.Test(SuiteName, "tray: the latest-XML entry only with the watch on", TrayEntry);
        }

        // ---- Pages (built and laid out, never shown; only Directory.Exists looks at the real folder)

        static List<string> GeneralTexts(Settings s)
        {
            Ds.Apply(1);
            ShotStack owner = (ShotStack)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ShotStack));
            HomeWindow hw = new HomeWindow(owner, s);
            typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hw, new object[] { "general", false });
            List<string> acc = new List<string>();
            Collect(hw.Content, acc);
            return acc;
        }

        static void Collect(object o, List<string> acc)
        {
            System.Windows.Controls.TextBlock tb = o as System.Windows.Controls.TextBlock;
            if (tb != null && tb.Text != null) acc.Add(tb.Text);
            System.Windows.DependencyObject d = o as System.Windows.DependencyObject;
            if (d == null) return;
            foreach (object c in System.Windows.LogicalTreeHelper.GetChildren(d)) Collect(c, acc);
        }

        static bool Has(List<string> texts, string part)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static void SettingsPage()
        {
            Console.WriteLine("    Egara folder on this PC: " + Directory.Exists(@"C:\egarsat"));
            Func<bool> real = XmlWatch.EgaraWorkstation;
            List<string> t;
            try
            {
                XmlWatch.EgaraWorkstation = delegate { return true; };
                t = GeneralTexts(new Settings());
                Check(Has(t, "Avanzado") && Has(t, "Abrir los XML nuevos de una carpeta"), "Egara PC: the group is hidden");
                // Anywhere else nothing at all: no group, no link to unfold it.
                XmlWatch.EgaraWorkstation = delegate { return false; };
                t = GeneralTexts(new Settings());
                Check(!Has(t, "Avanzado") && !Has(t, "Abrir los XML nuevos de una carpeta") && !Has(t, "Egara") && !Has(t, "avanzados"), "PC without Egara: something of the folder watch shows");
                Check(!XmlWatch.Offered(new Settings()), "offered without Egara");
                Settings already = new Settings();
                already.XmlWatchFolder = @"D:\soap entrada";
                Check(XmlWatch.Offered(already), "a watch already set up is hidden from its owner");
            }
            finally { XmlWatch.EgaraWorkstation = real; }

            Settings on = new Settings();
            on.XmlWatch = true;
            t = GeneralTexts(on);
            Check(Has(t, "Avanzado") && Has(t, "Abrir los XML nuevos de una carpeta"), "group hidden while on");
            Check(Has(t, XmlWatch.EgaraFolder), "Egara's folder not shown");
            Check(Has(t, "La carpeta de Egara."), "folder note");
            Check(Has(t, "Activo") || Has(t, "no existe todav") || Has(t, "ci\u00E9rralo"), "state of the watch not told");
            if (XmlWatch.OldViewerRunning()) Check(Has(t, "ci\u00E9rralo para no abrir cada XML dos veces"), "the old viewer is open and nothing says so");

            Settings custom = new Settings();
            custom.XmlWatch = true;
            custom.XmlWatchFolder = @"D:\soap entrada";
            t = GeneralTexts(custom);
            Check(Has(t, @"D:\soap entrada") && Has(t, "Carpeta elegida por ti."), "chosen folder");
            Check(Has(t, "no existe todav") || Has(t, "ci\u00E9rralo"), "a missing folder is not told");

            Settings folderOnly = new Settings();
            folderOnly.XmlWatchFolder = @"D:\soap entrada";
            Check(Has(GeneralTexts(folderOnly), "Avanzado"), "a chosen folder hides the group");
        }

        static void TrayEntry()
        {
            Func<Settings, bool> has = delegate(Settings s)
            {
                TrayPanel tp = new TrayPanel(s);
                typeof(TrayPanel).GetMethod("BuildItems", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tp, new object[] { false, false, false });
                foreach (object it in (System.Collections.IEnumerable)typeof(TrayPanel).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tp))
                    if ((string)it.GetType().GetField("Id").GetValue(it) == "lastxml") return true;
                return false;
            };
            Settings s1 = new Settings();
            Check(!has(s1), "entry with the watch off");
            s1.XmlWatch = true;
            Check(has(s1), "no entry with the watch on");
        }

        static string WriteSettings(Settings s, string path)
        {
            typeof(Settings).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(s, new object[] { path });
            return path;
        }

        static void SettingsRoundTrip()
        {
            string path = Path.Combine(Folder("settings"), "settings.ini");
            Settings s = new Settings();
            s.XmlWatch = true;
            s.XmlWatchFolder = "D:\\Mensajes SOAP\\Pe\u00F1a #1";
            WriteSettings(s, path);
            string ini = File.ReadAllText(path);
            Check(ini.Contains("XmlWatch = 1"), "XmlWatch not written as 1");
            Settings back = Settings.Load(path);
            Same(true, back.XmlWatch, "XmlWatch");
            Same(s.XmlWatchFolder, back.XmlWatchFolder, "XmlWatchFolder");
            s.XmlWatch = false;
            s.XmlWatchFolder = "";
            WriteSettings(s, path);
            back = Settings.Load(path);
            Same(false, back.XmlWatch, "XmlWatch off");
            Same("", back.XmlWatchFolder, "empty folder");
        }

        static void SettingsDefaults()
        {
            Settings d = new Settings();
            Same(false, d.XmlWatch, "default");
            Same("", d.XmlWatchFolder, "default folder");
            string path = Path.Combine(Folder("garbage"), "settings.ini");
            foreach (string v in new string[] { "quiz\u00E1", "2", "", "-1" })
            {
                File.WriteAllText(path, "XmlWatch = " + v + "\n");
                Same(false, Settings.Load(path).XmlWatch, "XmlWatch = " + v);
            }
            File.WriteAllText(path, "XmlWatch = si\nXmlWatchFolder = \"C:\\uno dos\"\n");
            Settings s = Settings.Load(path);
            Same(true, s.XmlWatch, "si");
            Same(@"C:\uno dos", s.XmlWatchFolder, "quoted folder");
            // Only a full path: anything else would depend on the folder Stackshot happens to start in.
            foreach (string bad in new string[] { "temp", "..\\x", "C:", "\\\\", "C:temp", "<>|" })
            {
                File.WriteAllText(path, "XmlWatchFolder = " + bad + "\n");
                Same("", Settings.Load(path).XmlWatchFolder, "XmlWatchFolder = " + bad);
            }
            File.WriteAllText(path, "XmlWatchFolder = \\\\servidor\\soap\n");
            Same(@"\\servidor\soap", Settings.Load(path).XmlWatchFolder, "UNC");
        }

        static void FolderChoice()
        {
            Settings s = new Settings();
            Same(XmlWatch.EgaraFolder, XmlWatch.FolderFor(s), "empty");
            s.XmlWatchFolder = @"E:\otra";
            Same(@"E:\otra", XmlWatch.FolderFor(s), "chosen");
            Same(@"C:\egarsat\temp", XmlWatch.EgaraFolder, "Egara's folder");
        }

        static void SlowWriter()
        {
            string dir = Folder("slow");
            using (Rig r = new Rig(dir))
            {
                Check(r.Watch.Watching && r.Watch.Folder == dir, "not watching " + dir);
                string p = Path.Combine(dir, "tmp41237.xml");
                byte[] all = Encoding.UTF8.GetBytes(Response);
                using (FileStream fs = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    int chunk = all.Length / 5 + 1;
                    for (int at = 0; at < all.Length; at += chunk)
                    {
                        fs.Write(all, at, Math.Min(chunk, all.Length - at));
                        fs.Flush();
                        Wait(180);
                        Check(r.Opened.Count == 0, "opened while still being written");
                    }
                }
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "opened");
                Same(p, r.Opened[0], "path");
                Same(Response, File.ReadAllText(p), "content complete when opened");
                Wait(900);
                Same(1, r.Opened.Count, "opened again");
            }
        }

        static void Ignored()
        {
            string dir = Folder("ignored");
            using (Rig r = new Rig(dir))
            {
                Put(Path.Combine(dir, "nota.txt"), Request);
                Put(Path.Combine(dir, "copia.xml.bak"), Request);
                Put(Path.Combine(dir, "libro.xmlx"), Request);
                Directory.CreateDirectory(Path.Combine(dir, "carpeta.xml"));
                Directory.CreateDirectory(Path.Combine(dir, "sub"));
                Put(Path.Combine(dir, "sub", "dentro.xml"), Request);
                Wait(1200);
                Check(r.Opened.Count == 0, "opened: " + string.Join(", ", r.Opened.ToArray()));
                string up = Path.Combine(dir, "MAYUS.XML");
                Put(up, Request);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "capitals");
                Same(up, r.Opened[0], "capitals path");
            }
        }

        static void Renamed()
        {
            string dir = Folder("renamed");
            using (Rig r = new Rig(dir))
            {
                string part = Path.Combine(dir, "mensaje.part"), xml = Path.Combine(dir, "mensaje.xml");
                Put(part, Response);
                Wait(500);
                Check(r.Opened.Count == 0, "the .part opened");
                File.Move(part, xml);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "renamed");
                Same(xml, r.Opened[0], "renamed path");
                Wait(700);
                Same(1, r.Opened.Count, "once");
            }
        }

        static void Burst()
        {
            string dir = Folder("burst");
            using (Rig r = new Rig(dir))
            {
                string a = Path.Combine(dir, "tmp10002.xml"), b = Path.Combine(dir, "tmp10001.xml");
                Put(a, Request);
                Thread.Sleep(60);
                Put(b, Response);
                Pump(delegate { return r.Opened.Count >= 2; }, 4000);
                Same(2, r.Opened.Count, "both opened");
                Same(a, r.Opened[0], "request first (written first, though its name sorts after)");
                Same(b, r.Opened[1], "response second");
            }
        }

        static void EmptyThenFilled()
        {
            string dir = Folder("empty");
            using (Rig r = new Rig(dir))
            {
                string p = Path.Combine(dir, "vacio.xml");
                File.WriteAllBytes(p, new byte[0]);
                Wait(900);
                Check(r.Opened.Count == 0, "an empty file opened");
                Put(p, FaultMsg);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "opened once filled");
            }
        }

        static void OpenThrows()
        {
            string dir = Folder("throws");
            using (Rig r = new Rig(dir))
            {
                int calls = 0;
                r.Also = delegate { if (++calls == 1) throw new IOException("prueba"); };
                Put(Path.Combine(dir, "uno.xml"), Request);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Put(Path.Combine(dir, "dos.xml"), Response);
                Pump(delegate { return r.Opened.Count > 1; }, 4000);
                Same(2, r.Opened.Count, "the second opened after the first threw");
            }
        }

        static void ApplyOnOff()
        {
            string dir = Folder("onoff");
            using (Rig r = new Rig(dir))
            {
                r.S.XmlWatch = false;
                r.Watch.Apply(r.S);
                Check(!r.Watch.Watching && r.Watch.Folder == null, "still watching when off");
                Put(Path.Combine(dir, "apagado.xml"), Request);
                Wait(900);
                Same(0, r.Opened.Count, "opened while off");
                r.S.XmlWatch = true;
                r.Watch.Apply(r.S);
                Check(r.Watch.Watching, "not watching again");
                Wait(700);
                Same(0, r.Opened.Count, "a file that was already there opened");
                r.Watch.Apply(r.S);
                r.Watch.Apply(r.S);
                Check(r.Watch.Watching && r.Watch.Folder == dir, "applying the same settings broke it");
                Put(Path.Combine(dir, "encendido.xml"), Response);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "new file after turning it on again");
            }
        }

        static void MissingAndMove()
        {
            string dir = Folder("move"), other = Folder("move2");
            string missing = Path.Combine(dir, "no-existe");
            using (Rig r = new Rig(missing))
            {
                Check(!r.Watch.Watching, "watching a missing folder");
                Same(missing, r.Watch.Folder, "folder kept for the retry");
                r.Watch.Apply(r.S);
                Check(!r.Watch.Watching, "second apply");
                r.S.XmlWatchFolder = dir;
                r.Watch.Apply(r.S);
                Check(r.Watch.Watching && r.Watch.Folder == dir, "did not move to " + dir);
                r.S.XmlWatchFolder = other;
                r.Watch.Apply(r.S);
                Check(r.Watch.Watching && r.Watch.Folder == other, "did not move to " + other);
                Put(Path.Combine(dir, "viejo.xml"), Request);
                Wait(800);
                Same(0, r.Opened.Count, "the old folder still watched");
                string p = Path.Combine(other, "nuevo.xml");
                Put(p, Response);
                Pump(delegate { return r.Opened.Count > 0; }, 4000);
                Same(1, r.Opened.Count, "new folder");
                Same(p, r.Opened[0], "new folder path");
            }
        }

        static void Operations()
        {
            string dir = Folder("ops");
            Func<string, string, string> op = delegate(string name, string text) { string p = Path.Combine(dir, name); Put(p, text); return XmlWatch.Operation(p); };
            Same("consultaPrueba", op("a.xml", Request), "request");
            Same("consultaPruebaResponse", op("b.xml", Response), "response (Body with attributes)");
            Same("SOAP Fault", op("c.xml", FaultMsg), "fault");
            Same("factura", op("d.xml", "<?xml version=\"1.0\"?>\n<!-- generado -->\n<factura n=\"1\"><linea/></factura>"), "plain root after a comment");
            Same("Envelope", op("e.xml", "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Header/></env:Envelope>"), "envelope without a body");
            Same("getQuote", op("f.xml", "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Body><m:getQuote xmlns:m=\"urn:m\"/></env:Body></env:Envelope>"), "SOAP 1.2");
            string bom = Path.Combine(dir, "g.xml");
            File.WriteAllText(bom, "<pedido/>", new UTF8Encoding(true));
            Same("pedido", XmlWatch.Operation(bom), "UTF-8 with BOM");
            string bin = Path.Combine(dir, "h.xml");
            File.WriteAllBytes(bin, new byte[] { 0, 1, 2, 3, 255, 254 });
            Same("", XmlWatch.Operation(bin), "binary");
            Same("", XmlWatch.Operation(Path.Combine(dir, "no-existe.xml")), "missing file");
            Same("", op("i.xml", "solo texto"), "no element");
        }

        static void RecentLatest()
        {
            string dir = Folder("recent");
            Check(XmlWatch.Latest(dir) == null, "latest of an empty folder");
            Check(XmlWatch.Latest(Path.Combine(dir, "no-existe")) == null, "latest of a missing folder");
            Check(XmlWatch.Recent(null, 5).Count == 0, "recent of null");
            DateTime t0 = new DateTime(2026, 10, 12, 9, 0, 0);
            for (int i = 0; i < 6; i++)
            {
                string p = Path.Combine(dir, "tmp" + (50 - i) + ".xml");
                Put(p, i % 2 == 0 ? Request : Response);
                File.SetLastWriteTime(p, t0.AddMinutes(i));
            }
            string txt = Path.Combine(dir, "zz.txt");
            Put(txt, Request);
            File.SetLastWriteTime(txt, t0.AddHours(1));
            List<XmlRecent> r = XmlWatch.Recent(dir, 4);
            Same(4, r.Count, "capped");
            Same("tmp45.xml", Path.GetFileName(r[0].Path), "newest first");
            Same("tmp48.xml", Path.GetFileName(r[3].Path), "fourth");
            Same("consultaPruebaResponse", r[0].Operation, "operation of the newest");
            Same("consultaPrueba", r[1].Operation, "operation of the second");
            Same(t0.AddMinutes(5), r[0].Written, "time");
            Same(Path.Combine(dir, "tmp45.xml"), XmlWatch.Latest(dir), "latest");
            Same(6, XmlWatch.Recent(dir, 50).Count, "only .xml");
        }

        static void Siblings()
        {
            string dir = Folder("sib");
            DateTime t0 = new DateTime(2026, 10, 12, 9, 0, 0);
            string[] names = { "c.xml", "a.xml", "b.xml", "d.xml" };
            int[] minutes = { 0, 1, 1, 2 };   // a and b share a time: name decides
            for (int i = 0; i < names.Length; i++)
            {
                string p = Path.Combine(dir, names[i]);
                Put(p, Request);
                File.SetLastWriteTime(p, t0.AddMinutes(minutes[i]));
            }
            Put(Path.Combine(dir, "e.txt"), "x");
            Func<string, int, string> sib = delegate(string n, int step) { string s = XmlWatch.Sibling(Path.Combine(dir, n), step); return s == null ? null : Path.GetFileName(s); };
            Same("a.xml", sib("c.xml", 1), "after c");
            Same("b.xml", sib("a.xml", 1), "after a (same time, by name)");
            Same("a.xml", sib("b.xml", -1), "before b");
            Same("d.xml", sib("b.xml", 1), "after b");
            Same(null, sib("c.xml", -1), "before the first");
            Same(null, sib("d.xml", 1), "after the last");
            Same(null, sib("no-existe.xml", 1), "a file that is not there");
            Same(null, XmlWatch.Sibling(Path.Combine(dir, "falta", "x.xml"), 1), "a folder that is not there");
        }

        static void OldViewer()
        {
            bool running = XmlWatch.OldViewerRunning();
            Console.WriteLine("    old tray viewer running: " + running);
        }
    }
}

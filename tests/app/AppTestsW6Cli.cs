// Round 6 (command line and IT policies), suite "w6cli": how --capture, --open-home and --last-xml are read, how a
// command travels to the running Stackshot in open.txt (only the private data folder's file is written and read; the
// running Stackshot is never signalled), and the IT policies, read from throwaway keys under
// HKCU\Software\StackshotTests\Policies\{Machine,User}, with the main window's sections built off screen, never shown.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Stackshot.Tests
{
    static class AppTestsW6Cli
    {
        const string SuiteName = "w6cli";
        const string PolicyRoot = @"Software\StackshotTests\Policies";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void Suite()
        {
            AppTests.Test(SuiteName, "cli: every hotkey action and pause are capture modes", EveryMode);
            AppTests.Test(SuiteName, "cli: any case, /capture, /capture:mode and --capture=mode", Forms);
            AppTests.Test(SuiteName, "cli: --open-home and --last-xml", OtherCommands);
            AppTests.Test(SuiteName, "cli: an unknown or missing mode is refused", UnknownModes);
            AppTests.Test(SuiteName, "cli: no command when none is asked for", NoCommand);
            AppTests.Test(SuiteName, "cli: an existing file named like an option is still a file", FileNamedLikeOption);
            AppTests.Test(SuiteName, "cli: the arguments for the installed copy ask for the same", ArgumentsRoundTrip);

            AppTests.Test(SuiteName, "requests: a command comes back as a command, files as files", RequestMixed);
            AppTests.Test(SuiteName, "requests: the old reader drops commands and never opens them as files", RequestOldReader);
            AppTests.Test(SuiteName, "requests: same command once; old, future, untimed and unknown dropped", RequestFiltering);
            AppTests.Test(SuiteName, "requests: files named like commands or options still open", RequestFileLikeCommand);
            AppTests.Test(SuiteName, "requests: a command is taken once", RequestTakenOnce);
            AppTests.Test(SuiteName, "commands: each once, only the last capture, after the rest", BatchRules);

            AppTests.Test(SuiteName, "policy: nothing set forces nothing", PolicyNone);
            AppTests.Test(SuiteName, "policy: each value forces its setting", PolicyEach);
            AppTests.Test(SuiteName, "policy: 1 in an off-only switch leaves it to the user", PolicyOneIsFree);
            AppTests.Test(SuiteName, "policy: the machine's value wins over the user's", PolicyMachineWins);
            AppTests.Test(SuiteName, "policy: invalid values are ignored and logged", PolicyInvalid);
            AppTests.Test(SuiteName, "policy: folders expand variables, quotes and network paths", PolicyFolders);
            AppTests.Test(SuiteName, "policy: settings.ini keeps the user's own choices", PolicyFileKeepsOwn);
            AppTests.Test(SuiteName, "policy: removing it brings the user's choices back", PolicyRemoved);
            AppTests.Test(SuiteName, "policy: a change made while forced is kept for later", PolicyChangeWhileForced);
            AppTests.Test(SuiteName, "policy: a forced value that changes never becomes the user's", PolicyValueChanges);
            AppTests.Test(SuiteName, "policy: Settings.Load applies them, Save keeps the user's", PolicyLoadAndSave);
            AppTests.Test(SuiteName, "policy: no update is announced while checks are forced off", PolicyUpdater);

            AppTests.Test(SuiteName, "policy ui: General rows of forced settings are disabled and say who decides", PolicyUiGeneral);
            AppTests.Test(SuiteName, "policy ui: Ajustes de la mascota", PolicyUiMascotSettings);
            AppTests.Test(SuiteName, "policy ui: the mascot's stage can't call it back", PolicyUiStage);
            AppTests.Test(SuiteName, "policy ui: Acerca de shows the line and no update button", PolicyUiAbout);
            AppTests.Test(SuiteName, "policy ui: only automatic installs forced", PolicyUiAutoOnly);
            AppTests.Test(SuiteName, "policy ui: without policies nothing changes", PolicyUiNone);

            AppTests.Test(SuiteName, "review: \"--capture region\" quoted as one argument still captures", QuotedAsOneArgument);
            AppTests.Test(SuiteName, "review: docs/it-policies.md names what the code reads and accepts", DocsMatchCode);
            AppTests.Test(SuiteName, "review: the widgets home doesn't offer to show a mascot IT keeps hidden", PolicyUiHomeWidgets);

            // Last: Program.Main runs Program.Init (process-wide switches the app sets at startup).
            AppTests.Test(SuiteName, "cli: an unknown mode exits with 2, logged, before anything starts", UnknownExitCode);
        }

        // ---- helpers

        static void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException(what); }

        static void Same<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(what + ": expected <" + expected + ">, got <" + actual + ">");
        }

        static string Show(List<string> l) { return "[" + string.Join(" | ", l.ToArray()) + "]"; }

        static string Parse(params string[] args)
        {
            string bad;
            string c = CliCommand.Parse(args, out bad);
            Check(bad == null, "unexpected bad <" + bad + "> for " + string.Join(" ", args));
            return c;
        }

        static string Bad(params string[] args)
        {
            string bad;
            string c = CliCommand.Parse(args, out bad);
            Check(c == null, "a command came out of " + string.Join(" ", args) + ": " + c);
            Check(bad != null, "not refused: " + string.Join(" ", args));
            return bad;
        }

        static string Work(string name)
        {
            string root = Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA");
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string d = Path.Combine(root, "w6cli", name);
            Directory.CreateDirectory(d);
            return d;
        }

        // ---- command line

        static void EveryMode()
        {
            foreach (string a in Settings.Actions)
            {
                Same("capture " + a, Parse("--capture", a), "--capture " + a);
                Check(CliCommand.IsMode(a) && CliCommand.Known("capture " + a), a + " known");
            }
            Same("capture pause", Parse("--capture", "pause"), "pause");
            Check(CliCommand.Known("capture pause"), "pause known");
        }

        static void Forms()
        {
            Same("capture region", Parse("--CAPTURE", "Region"), "upper case");
            Same("capture screen", Parse("/capture", "screen"), "slash");
            Same("capture window", Parse("/Capture:WINDOW"), "slash and colon");
            Same("capture gif", Parse("--capture=gif"), "equals");
            Same("capture video", Parse("--background", "--capture", " video "), "spaces around the mode, other options before");
            Same("capture text", Parse("--capture", "text", "--open-home"), "the first command counts");
        }

        static void OtherCommands()
        {
            Same(CliCommand.Home, Parse("--open-home"), "open-home");
            Same(CliCommand.Home, Parse("/OPEN-HOME"), "slash, upper case");
            Same(CliCommand.LastXml, Parse("--last-xml"), "last-xml");
            Same(CliCommand.LastXml, Parse("/Last-Xml"), "slash, mixed case");
            Same(CliCommand.Home, Parse("--test", "--open-home"), "with --test");
        }

        static void UnknownModes()
        {
            Same("--capture nope", Bad("--capture", "nope"), "unknown mode");
            Same("--capture", Bad("--capture"), "no mode at all");
            Same("/capture:pantalla", Bad("/capture:pantalla"), "Spanish name is not a mode");
            Same("--capture=", Bad("--capture="), "empty after equals");
            Same("--capture --open-home", Bad("--capture", "--open-home"), "an option is not a mode");
            Bad("--capture", "region.md");
            Bad("--capture", "");
            Check(!CliCommand.Known("capture nope") && !CliCommand.Known("format c:") && !CliCommand.Known("") && !CliCommand.Known(null), "unknown commands");
        }

        static void NoCommand()
        {
            Same<string>(null, Parse(), "no arguments");
            Same<string>(null, Parse("--background"), "--background");
            Same<string>(null, Parse("--test", "--home"), "--test --home");
            Same<string>(null, Parse(@"C:\v\nota.md"), "a file");
            Same<string>(null, Parse("--open", @"C:\v\nota.md"), "--open file");
            Same<string>(null, Parse("--open-home=1"), "open-home with a value is not the command");
            Same<string>(null, Parse("--open-homes"), "a longer name");
            Same<string>(null, Parse("-capture", "region"), "a single dash is not an option");
            Same<string>(null, Parse("capture", "region"), "no dash at all");
            Same<string>(null, Parse("--", "/", "--=x"), "empty options");
            string bad;
            Same<string>(null, CliCommand.Parse(null, out bad), "null arguments");
            Same<string>(null, bad, "null arguments are not bad");
        }

        // A file literally named "--open-home" or "--capture" in the current folder is a file, not a command.
        static void FileNamedLikeOption()
        {
            string dir = Work("like-option-" + Guid.NewGuid().ToString("N"));
            string before = Environment.CurrentDirectory;
            try
            {
                File.WriteAllText(Path.Combine(dir, "--open-home"), "x");
                File.WriteAllText(Path.Combine(dir, "--capture"), "x");
                Environment.CurrentDirectory = dir;
                Same<string>(null, Parse("--open-home"), "--open-home that exists");
                Same<string>(null, Parse("--capture", "region"), "--capture that exists");
                Same(CliCommand.LastXml, Parse("--last-xml"), "a name with no such file still works");
            }
            finally
            {
                Environment.CurrentDirectory = before;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // Split as Windows does for these simple arguments (quotes only around the path).
        static string[] SplitArgs(string line)
        {
            List<string> r = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (char ch in line)
            {
                if (ch == '"') { quoted = !quoted; any = true; continue; }
                if (ch == ' ' && !quoted) { if (any) r.Add(cur.ToString()); cur.Length = 0; any = false; continue; }
                cur.Append(ch);
                any = true;
            }
            if (any) r.Add(cur.ToString());
            return r.ToArray();
        }

        static void ArgumentsRoundTrip()
        {
            List<string> all = new List<string> { CliCommand.Home, CliCommand.LastXml, "capture pause" };
            foreach (string a in Settings.Actions) all.Add("capture " + a);
            foreach (string c in all)
            {
                string line = CliCommand.Arguments(c, null);
                Same(c, Parse(SplitArgs(line)), "round trip of " + line);
            }
            string view = @"C:\Mis documentos\nota con espacios.md";
            string withView = CliCommand.Arguments("capture region", view);
            Same("--capture region --open \"" + view + "\"", withView, "with the file");
            string[] parts = SplitArgs(withView);
            Same("capture region", Parse(parts), "command with the file");
            Same(view, parts[parts.Length - 1], "the file comes through whole");
        }

        // Program.Main itself with an unknown mode (and --test): refused right after reading the arguments, logged, 2.
        // Only run when Parse already refuses those arguments, so Main can never go on to start anything.
        static void UnknownExitCode()
        {
            string[] args = { "--test", "--capture", "nope" + Guid.NewGuid().ToString("N").Substring(0, 6) };
            string bad;
            Check(CliCommand.Parse(args, out bad) == null && bad != null, "Parse must refuse these arguments first");
            string log = Path.Combine(Work("log"), "exit-" + Guid.NewGuid().ToString("N") + ".log");
            string before = ShotStack.LogPath;
            ShotStack.LogPath = log;
            int code;
            try { code = Program.Main(args); }
            finally
            {
                ShotStack.LogPath = before;
                Settings.ReadOnly = true;
                ShotStack.CommandAtStart = null;
            }
            Same(2, code, "exit code");
            string text = File.Exists(log) ? File.ReadAllText(log) : "";
            Check(text.Contains("Orden desconocida: --capture " + args[2]), "logged: " + text);
            Same<string>(null, ShotStack.OpenAtStart, "nothing left to open");
            try { File.Delete(log); } catch { }
        }

        // ---- requests in open.txt (private data folder only)

        static string OpenFile { get { return Path.Combine(Settings.DataDir, "open.txt"); } }

        static bool PrivateData()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STACKSHOT_TEST_DATA"))) return false;
            string data = Path.GetFullPath(Settings.DataDir).TrimEnd('\\') + "\\";
            string real = Path.GetFullPath(Path.Combine(local, "Stackshot")).TrimEnd('\\') + "\\";
            return !data.StartsWith(real, StringComparison.OrdinalIgnoreCase);
        }

        static bool Private(string what)
        {
            if (PrivateData()) return true;
            Console.WriteLine("  (" + what + " skipped: STACKSHOT_TEST_DATA is not a private folder)");
            return false;
        }

        static void WriteOpen(string text)
        {
            if (!PrivateData()) throw new InvalidOperationException("refused: the real data folder");
            Directory.CreateDirectory(Settings.DataDir);
            foreach (string f in Directory.GetFiles(Settings.DataDir, "open*.txt")) File.Delete(f);
            File.WriteAllBytes(OpenFile, Utf8.GetBytes(text));
        }

        // The line SendOpen writes for a path or a command.
        static string Req(long ticks, string pathOrRequest) { return ticks + "\t" + pathOrRequest + "\n"; }

        static List<string> Take(List<string> commands)
        {
            if (!PrivateData()) throw new InvalidOperationException("refused: the real data folder");
            return Installer.TakeOpenRequests(commands);
        }

        static void RequestMixed()
        {
            if (!Private("requests")) return;
            long now = DateTime.UtcNow.Ticks;
            WriteOpen(Req(now, @"C:\v\a.md") + Req(now, CliCommand.Request("capture region")) + Req(now, @"C:\v\b.mkv") + Req(now, CliCommand.Request(CliCommand.LastXml)));
            List<string> commands = new List<string>();
            List<string> files = Take(commands);
            Same(2, files.Count, "files " + Show(files));
            Check(files.Contains(@"C:\v\a.md") && files.Contains(@"C:\v\b.mkv"), "files " + Show(files));
            Same(2, commands.Count, "commands " + Show(commands));
            Same("capture region", commands[0], "first command");
            Same(CliCommand.LastXml, commands[1], "second command");
            Check(!File.Exists(OpenFile), "open.txt taken away");
        }

        static void RequestOldReader()
        {
            if (!Private("requests")) return;
            long now = DateTime.UtcNow.Ticks;
            WriteOpen(Req(now, CliCommand.Request("capture region")) + Req(now, @"C:\v\a.md"));
            List<string> files = Installer.TakeOpenRequests();
            Same(1, files.Count, "files " + Show(files));
            Same(@"C:\v\a.md", files[0], "the file");
            Check(!File.Exists(OpenFile), "the command went with the file, not left behind");
        }

        static void RequestFiltering()
        {
            if (!Private("requests")) return;
            long now = DateTime.UtcNow.Ticks, second = TimeSpan.TicksPerSecond;
            string text = Req(now, CliCommand.Request("capture region")) +
                          Req(now - 2 * second, CliCommand.Request("capture region")) +          // the same, again: once
                          Req(now - 40 * second, CliCommand.Request("capture screen")) +         // older than Fresh
                          Req(now + 3 * TimeSpan.TicksPerMinute, CliCommand.Request(CliCommand.Home)) + // from the future
                          CliCommand.Request(CliCommand.LastXml) + "\n" +                        // no time at all
                          Req(now, CliCommand.Request("capture nope")) +                          // unknown mode
                          Req(now, CliCommand.Request("format c:")) +                             // unknown command
                          Req(now, "*") +                                                         // the marker alone
                          Req(now - 20 * second, CliCommand.Request("  open-home  "));            // inside Fresh, padded
            WriteOpen(text);
            List<string> commands = new List<string>();
            List<string> files = Take(commands);
            Same(0, files.Count, "no file " + Show(files));
            Same(2, commands.Count, "commands " + Show(commands));
            Same("capture region", commands[0], "the fresh capture, once");
            Same(CliCommand.Home, commands[1], "the padded one within the limit");
        }

        static void RequestFileLikeCommand()
        {
            if (!Private("requests")) return;
            long now = DateTime.UtcNow.Ticks;
            WriteOpen(Req(now, @"C:\v\--capture region.md") + Req(now, @"C:\v\--open-home.xml") + Req(now, @"C:\v\*raro.mkv"));
            List<string> commands = new List<string>();
            List<string> files = Take(commands);
            Same(0, commands.Count, "no command " + Show(commands));
            Same(3, files.Count, "files " + Show(files));
            Same<string>(null, Parse(@"C:\v\--capture region.md"), "a path is never a command");
        }

        static void RequestTakenOnce()
        {
            if (!Private("requests")) return;
            WriteOpen(Req(DateTime.UtcNow.Ticks, CliCommand.Request("capture gif")));
            List<string> first = new List<string>(), second = new List<string>();
            Take(first);
            Take(second);
            Same(1, first.Count, "first take " + Show(first));
            Same(0, second.Count, "second take " + Show(second));
        }

        static void BatchRules()
        {
            List<string> run = CliCommand.Batch(new List<string> { "capture region", CliCommand.Home, "capture screen", CliCommand.Home, "capture gif", CliCommand.LastXml }, null);
            Same(CliCommand.Home + "," + CliCommand.LastXml + ",capture gif", string.Join(",", run.ToArray()), "order");
            run = CliCommand.Batch(new List<string> { "capture region" }, "capture region");
            Same("capture region", string.Join(",", run.ToArray()), "the start command and the same request run once");
            run = CliCommand.Batch(new List<string>(), CliCommand.Home);
            Same(CliCommand.Home, string.Join(",", run.ToArray()), "only the start command");
            run = CliCommand.Batch(new List<string> { "capture nope", "format c:" }, null);
            Same(0, run.Count, "unknown ones never run");
            Same(0, CliCommand.Batch(null, null).Count, "nothing");
        }

        // ---- policies

        // Throwaway hive roots standing for HKLM and HKCU; removed afterwards, and what was read is forgotten so later
        // suites see no policy.
        sealed class Policies : IDisposable
        {
            public readonly RegistryKey Machine, User;

            public Policies()
            {
                Wipe();
                Machine = Registry.CurrentUser.CreateSubKey(PolicyRoot + @"\Machine");
                User = Registry.CurrentUser.CreateSubKey(PolicyRoot + @"\User");
                Policy.MachineOverride = Machine;
                Policy.UserOverride = User;
                Policy.Refresh();
            }

            public void Set(bool machine, string name, object value, RegistryValueKind kind)
            {
                using (RegistryKey k = (machine ? Machine : User).CreateSubKey(Policy.KeyPath)) k.SetValue(name, value, kind);
            }

            public void Dword(bool machine, string name, int value) { Set(machine, name, value, RegistryValueKind.DWord); }

            public void Clear()
            {
                foreach (RegistryKey root in new RegistryKey[] { Machine, User })
                    try { root.DeleteSubKeyTree("SOFTWARE", false); } catch { }
            }

            public void Dispose()
            {
                Clear();
                Policy.Refresh();
                Policy.MachineOverride = null;
                Policy.UserOverride = null;
                Machine.Close();
                User.Close();
                Wipe();
                Check(!Policy.Any, "policies forgotten after the test");
            }

            static void Wipe()
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(PolicyRoot, false); } catch { }
                try
                {
                    bool empty;
                    using (RegistryKey t = Registry.CurrentUser.OpenSubKey(@"Software\StackshotTests"))
                        empty = t != null && t.SubKeyCount == 0 && t.ValueCount == 0;
                    if (empty) Registry.CurrentUser.DeleteSubKey(@"Software\StackshotTests", false);
                }
                catch { }
            }
        }

        const string OwnFolder = @"D:\Mis capturas";

        // A user who chose the opposite of everything a policy can force.
        static Settings Own()
        {
            Settings s = new Settings();
            s.SaveFolder = OwnFolder;
            s.Profile = 2;   // Completo: the only profile where the desktop mascot can be on (Settings.MascotDesktop)
            s.CheckUpdates = true;
            s.AutoUpdate = true;
            s.MascotOn = true;
            s.MascotTalks = true;
            s.MascotDesktop = true;
            s.SensitiveAuto = false;
            s.HistorySearch = true;
            return s;
        }

        static void SetAll(Policies p, bool machine)
        {
            p.Set(machine, "SaveFolder", @"C:\Empresa\Capturas", RegistryValueKind.String);
            p.Dword(machine, "CheckUpdates", 0);
            p.Dword(machine, "AutoUpdate", 0);
            p.Dword(machine, "Mascot", 0);
            p.Dword(machine, "SensitiveAuto", 1);
            p.Dword(machine, "HistorySearch", 0);
        }

        static void IsOwn(Settings s, string when)
        {
            Same(OwnFolder, s.SaveFolder, when + ": folder");
            Check(s.CheckUpdates && s.AutoUpdate && s.MascotOn && s.MascotTalks && s.MascotDesktop && !s.SensitiveAuto && s.HistorySearch, when + ": switches are the user's");
        }

        static void IsForced(Settings s, string when)
        {
            Same(@"C:\Empresa\Capturas", s.SaveFolder, when + ": folder");
            Check(!s.CheckUpdates && !s.AutoUpdate && !s.MascotOn && !s.MascotTalks && !s.MascotDesktop && s.SensitiveAuto && !s.HistorySearch, when + ": switches are forced");
        }

        static void PolicyNone()
        {
            using (new Policies())
            {
                Settings s = Own();
                Check(!Policy.Apply(s), "nothing changed");
                Check(!Policy.Any && !Policy.FolderForced && !Policy.ChecksForced && !Policy.AutoForced && !Policy.MascotForced && !Policy.SensitiveForced && !Policy.SearchForced, "nothing forced");
                IsOwn(s, "no policy");
                Check(Policy.ForFile(s) == s, "the file gets the settings themselves");
            }
        }

        static void PolicyEach()
        {
            using (Policies p = new Policies())
            {
                SetAll(p, false);
                Settings s = Own();
                Check(Policy.Apply(s), "changed");
                Check(Policy.Any && Policy.FolderForced && Policy.ChecksForced && Policy.AutoForced && Policy.MascotForced && Policy.SensitiveForced && Policy.SearchForced, "all forced");
                IsForced(s, "user key");
                Check(!Policy.Apply(s), "a second read changes nothing");

                // One at a time, each forces only its own settings.
                p.Clear();
                p.Dword(false, "AutoUpdate", 0);
                s = Own();
                Policy.Apply(s);
                Check(Policy.AutoForced && !Policy.ChecksForced && !s.AutoUpdate && s.CheckUpdates && s.MascotOn, "AutoUpdate alone");
                p.Clear();
                p.Dword(false, "CheckUpdates", 0);
                s = Own();
                Policy.Apply(s);
                Check(Policy.ChecksForced && Policy.AutoForced && !s.CheckUpdates && !s.AutoUpdate, "CheckUpdates also stops automatic installs");
                p.Clear();
                p.Dword(false, "SensitiveAuto", 0);
                s = Own();
                s.SensitiveAuto = true;
                Policy.Apply(s);
                Check(Policy.SensitiveForced && !s.SensitiveAuto && s.HistorySearch, "SensitiveAuto 0 forces it off");
                p.Clear();
                p.Dword(false, "HistorySearch", 0);
                s = Own();
                Policy.Apply(s);
                Check(Policy.SearchForced && !s.HistorySearch && !s.SensitiveAuto && s.MascotOn, "HistorySearch alone");
                p.Clear();
                p.Dword(false, "Mascot", 0);
                s = Own();
                Policy.Apply(s);
                Check(Policy.MascotForced && !s.MascotOn && !s.MascotTalks && !s.MascotDesktop && s.CheckUpdates && s.SaveFolder == OwnFolder, "Mascot alone");
            }
        }

        static void PolicyOneIsFree()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "CheckUpdates", 1);
                p.Dword(false, "AutoUpdate", 1);
                p.Dword(false, "Mascot", 1);
                p.Dword(false, "HistorySearch", 1);
                Settings s = Own();
                s.CheckUpdates = false;
                s.MascotOn = false;
                Check(!Policy.Apply(s), "nothing changed");
                Check(!Policy.Any, "nothing forced");
                Check(!s.CheckUpdates && !s.MascotOn && s.AutoUpdate && s.HistorySearch, "the user's choices stay");
            }
        }

        static void PolicyMachineWins()
        {
            using (Policies p = new Policies())
            {
                p.Set(true, "SaveFolder", @"C:\Equipo", RegistryValueKind.String);
                p.Set(false, "SaveFolder", @"C:\Usuario", RegistryValueKind.String);
                p.Dword(true, "SensitiveAuto", 0);
                p.Dword(false, "SensitiveAuto", 1);
                p.Dword(true, "CheckUpdates", 1);    // the machine leaves it to the user...
                p.Dword(false, "CheckUpdates", 0);   // ...over the user key's 0
                p.Set(true, "Mascot", "0", RegistryValueKind.String); // invalid on the machine: the user's 0 applies
                p.Dword(false, "Mascot", 0);
                p.Dword(false, "HistorySearch", 0);  // only in the user's key
                Settings s = Own();
                s.SensitiveAuto = true;
                Policy.Apply(s);
                Same(@"C:\Equipo", s.SaveFolder, "machine folder");
                Check(Policy.SensitiveForced && !s.SensitiveAuto, "machine's SensitiveAuto 0");
                Check(!Policy.ChecksForced && s.CheckUpdates, "machine's 1 leaves checks to the user");
                Check(Policy.MascotForced && !s.MascotOn, "user's Mascot 0 after the machine's invalid value");
                Check(Policy.SearchForced && !s.HistorySearch, "user's HistorySearch");
            }
        }

        static void PolicyInvalid()
        {
            string log = Path.Combine(Work("log"), "invalid-" + Guid.NewGuid().ToString("N") + ".log");
            string before = ShotStack.LogPath;
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            using (Policies p = new Policies())
            {
                ShotStack.LogPath = log;
                try
                {
                    p.Set(true, "SaveFolder", 5, RegistryValueKind.DWord);
                    p.Set(false, "SaveFolder", @"relativa\" + tag, RegistryValueKind.String);
                    p.Set(false, "CheckUpdates", "0", RegistryValueKind.String);
                    p.Dword(false, "AutoUpdate", 2);
                    p.Set(false, "Mascot", 0L, RegistryValueKind.QWord);
                    p.Dword(false, "SensitiveAuto", -1);
                    p.Set(false, "HistorySearch", new byte[] { 0 }, RegistryValueKind.Binary);
                    p.Set(true, "CheckUpdates", "", RegistryValueKind.ExpandString);
                    Settings s = Own();
                    Check(!Policy.Apply(s), "nothing changed");
                    Check(!Policy.Any, "nothing forced");
                    IsOwn(s, "invalid values");
                    // A folder that names no full path, an empty one and an unexpanded variable are refused too.
                    p.Clear();
                    p.Set(false, "SaveFolder", "   ", RegistryValueKind.String);
                    Policy.Apply(s);
                    Check(!Policy.FolderForced, "blank folder");
                    p.Set(false, "SaveFolder", @"%NO_EXISTE_" + tag + @"%\Capturas", RegistryValueKind.ExpandString);
                    Policy.Apply(s);
                    Check(!Policy.FolderForced && s.SaveFolder == OwnFolder, "unknown variable");
                }
                finally { ShotStack.LogPath = before; }
            }
            string text = File.Exists(log) ? File.ReadAllText(log) : "";
            foreach (string name in new string[] { @"HKLM\SOFTWARE\Policies\Stackshot\SaveFolder", @"HKCU\SOFTWARE\Policies\Stackshot\SaveFolder", @"HKCU\SOFTWARE\Policies\Stackshot\CheckUpdates",
                                                   @"HKCU\SOFTWARE\Policies\Stackshot\AutoUpdate", @"HKCU\SOFTWARE\Policies\Stackshot\Mascot", @"HKCU\SOFTWARE\Policies\Stackshot\SensitiveAuto",
                                                   @"HKCU\SOFTWARE\Policies\Stackshot\HistorySearch", @"HKLM\SOFTWARE\Policies\Stackshot\CheckUpdates" })
                Check(text.Contains("Directiva " + name + " no v\u00E1lida"), "logged " + name + ":\n" + text);
            Check(text.Contains(tag), "the bad folder is named in the log");
            try { File.Delete(log); } catch { }
        }

        static void PolicyFolders()
        {
            using (Policies p = new Policies())
            {
                string profile = Environment.GetEnvironmentVariable("USERPROFILE");
                string user = Environment.GetEnvironmentVariable("USERNAME");
                Settings s = Own();
                p.Set(false, "SaveFolder", @"%USERPROFILE%\Capturas empresa", RegistryValueKind.ExpandString);
                Policy.Apply(s);
                Same(Path.Combine(profile, "Capturas empresa"), s.SaveFolder, "REG_EXPAND_SZ");
                p.Set(false, "SaveFolder", @"%USERPROFILE%\Otra", RegistryValueKind.String);
                Policy.Apply(s);
                Same(Path.Combine(profile, "Otra"), s.SaveFolder, "REG_SZ expands too");
                p.Set(false, "SaveFolder", "  \"C:\\Empresa\\Con comillas\"  ", RegistryValueKind.String);
                Policy.Apply(s);
                Same(@"C:\Empresa\Con comillas", s.SaveFolder, "quotes and spaces");
                p.Set(false, "SaveFolder", @"\\servidor\capturas\%USERNAME%", RegistryValueKind.ExpandString);
                Policy.Apply(s);
                Same(@"\\servidor\capturas\" + user, s.SaveFolder, "network path");
                p.Set(false, "SaveFolder", @"C:/Empresa/Barras", RegistryValueKind.String);
                Policy.Apply(s);
                Same(@"C:\Empresa\Barras", s.SaveFolder, "forward slashes");
                p.Clear();
                Policy.Apply(s);
                Same(OwnFolder, s.SaveFolder, "back to the user's folder");
            }
        }

        static string WriteIni(Settings s, string name)
        {
            string path = Path.Combine(Work("ini"), name + "-" + Guid.NewGuid().ToString("N") + ".ini");
            MethodInfo write = typeof(Settings).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance);
            write.Invoke(s, new object[] { path });
            return path;
        }

        static void PolicyFileKeepsOwn()
        {
            using (Policies p = new Policies())
            {
                SetAll(p, true);
                Settings s = Own();
                s.Sound = false; // something no policy touches
                Policy.Apply(s);
                Settings f = Policy.ForFile(s);
                Check(f != s, "a copy for the file");
                IsOwn(f, "file copy");
                Check(!f.Sound, "the rest as it is");
                IsForced(s, "in use");
                string path = WriteIni(f, "own");
                Settings back = Settings.Load(path);
                IsOwn(back, "read back");
                string text = File.ReadAllText(path);
                Check(text.Contains("SaveFolder = " + OwnFolder) && text.Contains("Mascot = 1") && text.Contains("SensitiveAuto = 0") && text.Contains("HistorySearch = 1"),
                      "file text:\n" + text);
                try { File.Delete(path); } catch { }
            }
        }

        static void PolicyRemoved()
        {
            using (Policies p = new Policies())
            {
                SetAll(p, false);
                Settings s = Own();
                Policy.Apply(s);
                IsForced(s, "with the policy");
                p.Clear();
                Check(Policy.Apply(s), "removing it changes the settings in use");
                IsOwn(s, "without the policy");
                Check(Policy.ForFile(s) == s, "nothing kept aside any more");
                Check(!Policy.Apply(s), "and nothing more to change");
            }
        }

        static void PolicyChangeWhileForced()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "Mascot", 0);
                p.Set(false, "SaveFolder", @"C:\Empresa\Capturas", RegistryValueKind.String);
                Settings s = Own();
                s.MascotTalks = false;
                Policy.Apply(s);
                // A profile turns the greetings on, an --install --folder sets a folder: the app changes them meanwhile.
                s.MascotTalks = true;
                s.SaveFolder = @"E:\Desde la instalacion";
                Settings f = Policy.ForFile(s);
                Check(f.MascotTalks && f.SaveFolder == @"E:\Desde la instalacion", "the file gets the new choices");
                Check(!s.MascotTalks && s.SaveFolder == @"C:\Empresa\Capturas", "in use they are forced again");
                p.Clear();
                Policy.Apply(s);
                Check(s.MascotTalks && s.SaveFolder == @"E:\Desde la instalacion" && s.MascotOn, "after the policy: the new choices");
            }
        }

        static void PolicyValueChanges()
        {
            foreach (bool own in new bool[] { false, true })
            {
                using (Policies p = new Policies())
                {
                    Settings s = Own();
                    s.SensitiveAuto = own;
                    p.Dword(false, "SensitiveAuto", 1);
                    Policy.Apply(s);
                    Check(s.SensitiveAuto, own + ": forced on");
                    p.Dword(false, "SensitiveAuto", 0);
                    Policy.Apply(s);
                    Check(!s.SensitiveAuto, own + ": forced off");
                    Same(own, Policy.ForFile(s).SensitiveAuto, own + ": the file keeps the user's");
                    p.Dword(false, "SensitiveAuto", 1);
                    Policy.Apply(s);
                    Check(s.SensitiveAuto, own + ": forced on again");
                    p.Clear();
                    Policy.Apply(s);
                    Same(own, s.SensitiveAuto, own + ": back to the user's");
                }
            }
        }

        // Settings.Load() reads the private data folder's settings.ini (STACKSHOT_TEST_DATA) and applies the policies;
        // Save() writes the user's choices back. The file there is put back as it was.
        static void PolicyLoadAndSave()
        {
            if (!Private("Settings.Load")) return;
            string file = Settings.FilePath, bak = file + ".bak";
            string keepFile = File.Exists(file) ? File.ReadAllText(file) : null, keepBak = File.Exists(bak) ? File.ReadAllText(bak) : null;
            try
            {
                File.Copy(WriteIni(Own(), "load"), file, true);
                using (Policies p = new Policies())
                {
                    SetAll(p, false);
                    Settings s = Settings.Load();
                    IsForced(s, "loaded");
                    s.Sound = false;
                    Settings.ReadOnly = false;
                    try { s.Save(); }
                    finally { Settings.ReadOnly = true; }
                    Settings plain = Settings.Load(file);
                    IsOwn(plain, "saved");
                    Check(!plain.Sound, "the change made was saved");
                    IsForced(s, "still in use");
                    p.Clear();
                    Settings again = Settings.Load();
                    IsOwn(again, "loaded without the policy");
                }
            }
            finally
            {
                Settings.ReadOnly = true;
                if (keepFile != null) File.WriteAllText(file, keepFile, Utf8); else { try { File.Delete(file); } catch { } }
                if (keepBak != null) File.WriteAllText(bak, keepBak, Utf8); else { try { File.Delete(bak); } catch { } }
            }
        }

        // Updater.Act, with the notice replaced: an available version is announced, unless the checks are forced off.
        // (Check and Install are not called here: they would reach GitHub or open the browser.)
        static void PolicyUpdater()
        {
            FieldInfo ownerField = typeof(Updater).GetField("owner", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo act = typeof(Updater).GetMethod("Act", BindingFlags.NonPublic | BindingFlags.Static);
            Func<bool> canPresent = Updater.CanPresent;
            Action<Updater.Release> present = Updater.Present;
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            Settings s = new Settings();
            typeof(ShotStack).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, s);
            int shown = 0;
            using (Policies p = new Policies())
            {
                try
                {
                    ownerField.SetValue(null, owner);
                    Updater.CanPresent = delegate { return true; };
                    Updater.Present = delegate { shown++; };
                    Updater.Available = new Updater.Release { Version = new Version(99, 0, 0), Tag = "v99.0.0", Page = Program.RepoUrl };
                    p.Dword(false, "CheckUpdates", 0);
                    Policy.Apply(s);
                    act.Invoke(null, null);
                    Same(0, shown, "announced with checks forced off");
                    Same("", s.UpdateNotified, "not marked as announced");
                    p.Clear();
                    Policy.Apply(s);
                    act.Invoke(null, null);
                    Same(1, shown, "announced once the policy is gone");
                }
                finally
                {
                    Updater.Available = null;
                    Updater.CanPresent = canPresent;
                    Updater.Present = present;
                    ownerField.SetValue(null, null);
                }
            }
        }

        // ---- the main window's sections (built and laid out, never shown; the owner is not created)

        static HomeWindow Make(Settings s)
        {
            Ds.Apply(1);
            s.Appearance = 1;
            Policy.Apply(s); // already in force, as at startup: building the window changes nothing more
            ShotStack owner = (ShotStack)FormatterServices.GetUninitializedObject(typeof(ShotStack));
            return new HomeWindow(owner, s);
        }

        static void SetPage(HomeWindow hw, string id)
        {
            MethodInfo m = typeof(HomeWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance);
            try { m.Invoke(hw, new object[] { id, false }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("page " + id + ": " + ex.InnerException, ex.InnerException); }
            UIElement root = (UIElement)hw.Content;
            root.Measure(new Size(980, 3000));
            root.Arrange(new Rect(0, 0, 980, 3000));
        }

        // With STACKSHOT_UI_SHOTS set to a folder, the section is also saved there as PNG to be looked at.
        static void Shot(HomeWindow hw, string name)
        {
            string dir = Environment.GetEnvironmentVariable("STACKSHOT_UI_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            UIElement root = (UIElement)hw.Content;
            const int w = 980, h = 2400;
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            System.Windows.Media.DrawingVisual dv = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(hw.Background ?? System.Windows.Media.Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w, h));
            }
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(Path.Combine(dir, "w6cli-" + name + ".png"))) enc.Save(fs);
        }

        static void Walk(object o, Action<DependencyObject> visit)
        {
            DependencyObject d = o as DependencyObject;
            if (d == null) return;
            visit(d);
            foreach (object c in LogicalTreeHelper.GetChildren(d)) Walk(c, visit);
        }

        static List<string> Texts(object root)
        {
            List<string> acc = new List<string>();
            Walk(root, delegate(DependencyObject d) { TextBlock t = d as TextBlock; if (t != null && t.Text != null && t.Visibility == Visibility.Visible) acc.Add(t.Text); });
            return acc;
        }

        static bool Has(List<string> texts, string part)
        {
            foreach (string t in texts) if (t.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static List<HomeWindow.Row> Rows(HomeWindow hw)
        {
            List<HomeWindow.Row> rows = new List<HomeWindow.Row>();
            Walk(hw.Content, delegate(DependencyObject d) { HomeWindow.Row r = d as HomeWindow.Row; if (r != null) rows.Add(r); });
            return rows;
        }

        static HomeWindow.Row Row(List<HomeWindow.Row> rows, string title)
        {
            foreach (HomeWindow.Row r in rows) if (r.Title == title) return r;
            List<string> seen = new List<string>();
            foreach (HomeWindow.Row r in rows) seen.Add(r.Title);
            throw new InvalidOperationException("no row <" + title + "> among " + Show(seen));
        }

        static HomeWindow.Row Folder(List<HomeWindow.Row> rows)
        {
            foreach (HomeWindow.Row r in rows) if (r is HomeWindow.FolderRow) return r;
            throw new InvalidOperationException("no folder row");
        }

        static void Locked(HomeWindow.Row r, string what)
        {
            Check(!r.IsEnabled, what + ": still enabled");
            Check(r.Opacity < 0.5, what + ": not dimmed");
            Check(Has(Texts(r), Policy.Managed), what + ": does not say who decides " + Show(Texts(r)));
        }

        static void Free(HomeWindow.Row r, string what)
        {
            Check(r.IsEnabled, what + ": disabled");
            Check(!Has(Texts(r), Policy.Managed), what + ": says the organization decides");
        }

        static readonly string[] GeneralForced = { "Buscar actualizaciones", "Instalar actualizaciones autom\u00E1ticamente", "Tapar los datos sensibles al capturar", "Buscar por el texto de las capturas" };
        static readonly string[] MascotForced = { "Mostrar la mascota", "Saludos y consejos", "Mascota en el escritorio" };

        static void PolicyUiGeneral()
        {
            using (Policies p = new Policies())
            {
                SetAll(p, true);
                HomeWindow hw = Make(Own());
                SetPage(hw, "general");
                List<HomeWindow.Row> rows = Rows(hw);
                foreach (string t in GeneralForced) Locked(Row(rows, t), t);
                HomeWindow.Row folder = Folder(rows);
                Locked(folder, "folder");
                Same(@"C:\Empresa\Capturas", folder.Title, "the folder shown is the forced one");
                Free(Row(rows, "Sonido de c\u00E1mara"), "a setting no policy touches");
                Free(Row(rows, "Incluir el puntero"), "another one");
                List<string> texts = Texts(hw.Content);
                Check(!Has(texts, "los saludos de la mascota") && !Has(texts, "la mascota en el escritorio"), "the profile blames the user for what the policy forces " + Show(texts));
                Shot(hw, "general");
            }
        }

        static void PolicyUiMascotSettings()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "Mascot", 0);
                HomeWindow hw = Make(Own());
                SetPage(hw, "mascotset");
                List<HomeWindow.Row> rows = Rows(hw);
                foreach (string t in MascotForced) Locked(Row(rows, t), t);
                Shot(hw, "mascotset");
            }
        }

        static void PolicyUiStage()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "Mascot", 0);
                HomeWindow hw = Make(Own());
                SetPage(hw, "mascot");
                Check(Has(Texts(hw.Content), Policy.Managed + "."), "the hidden mascot says who decides");
                int buttons = 0, locked = 0;
                Walk(hw.Content, delegate(DependencyObject d)
                {
                    MacButton b = d as MacButton;
                    if (b != null && b.Label == "Mostrar la mascota") { buttons++; Check(!b.IsEnabled, "Mostrar la mascota is enabled"); }
                    FrameworkElement e = d as FrameworkElement;
                    if (e != null && Policy.Managed.Equals(e.ToolTip) && !e.IsEnabled) locked++;
                });
                Same(1, buttons, "the button to call it back");
                Check(locked >= 2, "the button and the desktop switch are locked: " + locked);
                Shot(hw, "stage");
            }
        }

        static void PolicyUiAbout()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "CheckUpdates", 0);
                HomeWindow hw = Make(Own());
                SetPage(hw, "about");
                List<string> texts = Texts(hw.Content);
                Check(Has(texts, Policy.ManagedNote), "the organization line " + Show(texts));
                Check(Has(texts, "Las actualizaciones de Stackshot las gestiona tu organizaci\u00F3n"), "the update card says it");
                int locked = 0, offers = 0;
                Walk(hw.Content, delegate(DependencyObject d)
                {
                    FrameworkElement e = d as FrameworkElement;
                    if (e != null && Policy.Managed.Equals(e.ToolTip) && !e.IsEnabled) locked++;
                    MacButton b = d as MacButton;
                    if (b != null && (b.Label == "Buscar ahora" || b.Label == "Reintentar" || b.Label == "Descargar e instalar" || b.Label == "Instalar ahora")) offers++;
                });
                Same(0, offers, "no button to look for or install updates");
                Check(locked >= 1, "the automatic checks switch is locked");
                Shot(hw, "about");
            }
        }

        static void PolicyUiAutoOnly()
        {
            using (Policies p = new Policies())
            {
                p.Dword(false, "AutoUpdate", 0);
                HomeWindow hw = Make(Own());
                SetPage(hw, "general");
                List<HomeWindow.Row> rows = Rows(hw);
                Locked(Row(rows, "Instalar actualizaciones autom\u00E1ticamente"), "automatic installs");
                Free(Row(rows, "Buscar actualizaciones"), "looking for them stays the user's");
                Free(Folder(rows), "folder");
                SetPage(hw, "about");
                List<string> texts = Texts(hw.Content);
                Check(Has(texts, Policy.ManagedNote), "the organization line");
                Check(!Has(texts, "Las actualizaciones de Stackshot las gestiona tu organizaci\u00F3n"), "the card still offers updates");
            }
        }

        static void PolicyUiNone()
        {
            using (new Policies())
            {
                HomeWindow hw = Make(Own());
                SetPage(hw, "general");
                List<HomeWindow.Row> rows = Rows(hw);
                foreach (string t in new string[] { "Buscar actualizaciones", "Tapar los datos sensibles al capturar", "Buscar por el texto de las capturas" }) Free(Row(rows, t), t);
                Free(Folder(rows), "folder");
                Same(OwnFolder, Folder(rows).Title, "the user's folder");
                Check(!Has(Texts(hw.Content), Policy.Managed), "nobody else decides");
                SetPage(hw, "mascotset");
                rows = Rows(hw);
                foreach (string t in MascotForced) Free(Row(rows, t), t);
                SetPage(hw, "about");
                Check(!Has(Texts(hw.Content), Policy.ManagedNote), "no organization line");
            }
        }

        // ---- review round (R3)

        // Some launchers quote their whole arguments field: Windows then hands over "--capture region" as one argument.
        // Before, that was not an option at all and Stackshot just opened its window.
        static void QuotedAsOneArgument()
        {
            Same("capture region", Parse(SplitArgs("\"--capture region\"")), "quoted as one argument");
            Same("capture screen", Parse(" --capture  screen "), "spaces around and between");
            Same("capture window", Parse("/Capture WINDOW"), "slash, mixed case");
            Same("capture pause", Parse("--capture\tpause"), "a tab");
            Same(CliCommand.Home, Parse("--open-home "), "a trailing space");
            Same(CliCommand.LastXml, Parse(SplitArgs("--background \"--last-xml\"")), "quotes around a plain option");
            Same<string>(null, Parse("--open-home ahora"), "open-home with a value is not the command");
            Same("--capture nope", Bad("--capture nope"), "an unknown mode is still refused");
            Same("--capture", Bad("--capture ").Trim(), "no mode at all");
            Same<string>(null, Parse(@"C:\Mis capturas\--capture region.png"), "a path with the words in it");
            string dir = Work("quoted-" + Guid.NewGuid().ToString("N"));
            string before = Environment.CurrentDirectory;
            try
            {
                File.WriteAllText(Path.Combine(dir, "--capture region"), "x");
                Environment.CurrentDirectory = dir;
                Same<string>(null, Parse("--capture region"), "a file with that very name stays a file");
            }
            finally
            {
                Environment.CurrentDirectory = before;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        static string Here([System.Runtime.CompilerServices.CallerFilePath] string file = "") { return file; }

        // The IT guide is what administrators read: its modes, value names, types, key and quoted texts must be the code's.
        static void DocsMatchCode()
        {
            string doc = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here()), @"..\..\docs\it-policies.md"));
            Check(File.Exists(doc), "no guide at " + doc);
            string text = File.ReadAllText(doc, Encoding.UTF8);
            int policies = text.IndexOf("## Directivas", StringComparison.Ordinal);
            Check(policies > 0, "no policies section");
            string cli = text.Substring(0, policies), pol = text.Substring(policies);

            // Every capture mode the code accepts is listed, and every listed one is accepted.
            List<string> modes = new List<string>(Settings.Actions);
            modes.Add("pause");
            List<string> listed = TableKeys(cli);
            foreach (string m in modes) Check(listed.Contains(m), "mode not in the guide: " + m);
            foreach (string k in listed)
                if (!k.StartsWith("--", StringComparison.Ordinal)) Check(CliCommand.IsMode(k), "the guide lists a mode the code refuses: " + k);
            foreach (string o in new string[] { "--capture <modo>", "--open-home", "--last-xml" }) Check(listed.Contains(o), "option not in the guide: " + o);

            // Each value the code reads, with its type, and nothing else.
            string[] names = { "SaveFolder", "CheckUpdates", "AutoUpdate", "Mascot", "SensitiveAuto", "HistorySearch", "XmlWatchFolder" };
            List<string> values = TableKeys(pol);
            Same(string.Join(",", names), string.Join(",", values.ToArray()), "policy values in the guide");
            using (Policies p = new Policies())
            {
                foreach (string n in names)
                {
                    string row = RowOf(pol, n);
                    if (n == "XmlWatchFolder")
                    {
                        Check(row.Contains("REG_SZ") && row.Contains("REG_EXPAND_SZ"), "XmlWatchFolder types: " + row);
                        p.Set(true, n, @"C:\Guia\Xml", RegistryValueKind.String);
                        Policy.Refresh();
                        Check(Policy.XmlFolder != null, "XmlWatchFolder as REG_SZ in HKLM");
                        continue;
                    }
                    if (n == "SaveFolder")
                    {
                        Check(row.Contains("REG_SZ") && row.Contains("REG_EXPAND_SZ"), "SaveFolder types: " + row);
                        p.Set(true, n, @"C:\Guia", RegistryValueKind.String);
                        Policy.Refresh();
                        Check(Policy.FolderForced, "SaveFolder as REG_SZ in HKLM");
                        continue;
                    }
                    Check(row.Contains("| DWORD `") && row.Contains("`0`"), n + " documented without DWORD 0: " + row);
                    p.Dword(true, n, 0);
                }
                Policy.Refresh();
                Check(Policy.ChecksForced && Policy.AutoForced && Policy.MascotForced && Policy.SensitiveForced && Policy.SearchForced, "every DWORD 0 of the guide forces something");
                Check(RowOf(pol, "SensitiveAuto").Contains("DWORD `1`"), "SensitiveAuto 1");
                p.Clear();
                p.Dword(false, "SensitiveAuto", 1);
                Policy.Refresh();
                Check(Policy.SensitiveForced, "SensitiveAuto 1 in HKCU");
            }
            Check(pol.Contains(@"HKEY_LOCAL_MACHINE\" + Policy.KeyPath) && pol.Contains(@"HKEY_CURRENT_USER\" + Policy.KeyPath), "the key paths");
            Check(pol.Contains(@"[HKEY_LOCAL_MACHINE\" + Policy.KeyPath + "]") && pol.Contains(@"[-HKEY_LOCAL_MACHINE\" + Policy.KeyPath + "]"), "the .reg examples");
            Check(text.Contains("\u00AB" + Policy.Managed + "\u00BB") && text.Contains("\u00AB" + Policy.ManagedNote + "\u00BB"), "the texts the window shows");
            Check(text.Contains(Path.GetFileName(Settings.LogFile)) && text.Contains(@"AppData\Local\Programs\Stackshot\Stackshot.exe"), "log and install paths");
        }

        // The first cell of every table row ("| `name` | ..."), in order.
        static List<string> TableKeys(string text)
        {
            List<string> r = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("| `", StringComparison.Ordinal)) continue;
                int end = l.IndexOf('`', 3);
                if (end > 3) r.Add(l.Substring(3, end - 3));
            }
            return r;
        }

        static string RowOf(string text, string name)
        {
            foreach (string line in text.Split('\n'))
                if (line.Trim().StartsWith("| `" + name + "`", StringComparison.Ordinal)) return line;
            throw new InvalidOperationException("no row for " + name);
        }

        // Inicio with widgets and the mascot hidden: its tile invites to show it, unless the organization keeps it hidden.
        static void PolicyUiHomeWidgets()
        {
            using (Policies p = new Policies())
            {
                Settings own = Own();
                own.HomeStyle = 1;
                own.MascotOn = false;
                own.ShowRecent = false;
                HomeWindow hw = Make(own);
                SetPage(hw, "home");
                List<string> texts = Texts(hw.Content);
                Check(Has(texts, "Mascota escondida") && Has(texts, "Mostrar en Mascota"), "hidden by the user: the tile offers it " + Show(texts));
                Check(!Has(texts, Policy.Managed), "nobody else decides");
                p.Dword(false, "Mascot", 0);
                Settings forced = Own();
                forced.HomeStyle = 1;
                forced.ShowRecent = false;
                hw = Make(forced);
                SetPage(hw, "home");
                texts = Texts(hw.Content);
                Check(Has(texts, "Mascota escondida"), "the tile " + Show(texts));
                Check(!Has(texts, "Mostrar en Mascota"), "the tile offers to show a mascot the organization keeps hidden " + Show(texts));
                Check(Has(texts, Policy.Managed), "the tile says who decides " + Show(texts));
                Shot(hw, "home-widgets");
            }
        }
    }
}

// Stackshot - The mascot as an assistant: quick reminders ("Recuérdame en 30 min..."), notes appended to Notas.md,
// files and text dropped on it (each to where it opens) and a word when a SOAP fault arrives in the watched folder.
// Reminders are kept on disk and wait until they can be shown (never lost to a call or a full-screen app); one due
// while the computer was off comes as soon as it is back, saying when it was for.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Stackshot
{
    public sealed class Reminder
    {
        public DateTime DueUtc;
        public string Text;
    }

    public partial class ShotStack
    {
        readonly List<Reminder> reminders = new List<Reminder>();
        Timer remindTimer;

        static string RemindersFile { get { return Path.Combine(Settings.DataDir, "recordatorios.txt"); } }

        void StartAssist()
        {
            if (Test) return;
            LoadReminders();
            remindTimer = new Timer { Interval = 15000 };
            remindTimer.Tick += delegate
            {
                try { RemindTick(); }
                catch (Exception ex) { Log("Recordatorios: " + ex.Message); }
            };
            if (reminders.Count > 0) remindTimer.Start();
        }

        // ---- Reminders

        public List<Reminder> Reminders
        {
            get
            {
                List<Reminder> r = new List<Reminder>(reminders);
                r.Sort(delegate(Reminder a, Reminder b) { return a.DueUtc.CompareTo(b.DueUtc); });
                return r;
            }
        }

        public void AddReminder(string text, DateTime dueLocal, bool quiet)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            if (text.Length == 0) return;
            reminders.Add(new Reminder { DueUtc = dueLocal.ToUniversalTime(), Text = text });
            SaveReminders();
            if (remindTimer != null) remindTimer.Start();
            Log("Recordatorio para las " + dueLocal.ToString("HH:mm"));
            if (quiet) return;
            string when = WhenText(dueLocal);
            if (PetShown) pet.FocusSay("Te lo recuerdo " + when + ".");
            else Notify("Recordatorio guardado", "Te lo recuerdo " + when + ": \u00AB" + text + "\u00BB.");
        }

        public void RemoveReminder(Reminder r)
        {
            reminders.Remove(r);
            SaveReminders();
        }

        // "a las 11:45", "mañana a las 9:00", "el 16/10 a las 9:00".
        public static string WhenText(DateTime local)
        {
            string at = "a las " + local.ToString("H:mm");
            if (local.Date == DateTime.Today) return at;
            if (local.Date == DateTime.Today.AddDays(1)) return "ma\u00F1ana " + at;
            return "el " + local.ToString("d/M") + " " + at;
        }

        void RemindTick()
        {
            if (exiting || reminders.Count == 0) { if (remindTimer != null) remindTimer.Stop(); return; }
            Reminder due = null;
            foreach (Reminder r in Reminders) if (r.DueUtc <= DateTime.UtcNow) { due = r; break; }
            if (due == null) return;
            // Shown when someone is there to see it, and kept until it can be: the mascot when it is free, otherwise a
            // notice (which itself waits through presentations and full-screen apps).
            if (Native.IdleMs() >= Wellness.ActiveIdleMs) return;
            DateTime local = due.DueUtc.ToLocalTime();
            string late = DateTime.UtcNow - due.DueUtc > TimeSpan.FromMinutes(3) ? "Era para " + WhenText(local).Replace("ma\u00F1ana ", "") + ": " : "";
            if (pet != null)
            {
                if (!pet.CanRemind) return;
                RemoveReminder(due);
                pet.ReminderCard(late + due.Text, delegate(int minutes) { AddReminder(due.Text, DateTime.Now.AddMinutes(minutes), true); });
                return;
            }
            if (ToastQuietNow(Screen.PrimaryScreen.Bounds) != null) return;
            RemoveReminder(due);
            Notify("Recordatorio", late + due.Text, NoticeKind.Info, "10 min m\u00E1s", delegate { AddReminder(due.Text, DateTime.Now.AddMinutes(10), true); });
        }

        void LoadReminders()
        {
            reminders.Clear();
            try
            {
                if (!File.Exists(RemindersFile)) return;
                foreach (string line in File.ReadAllLines(RemindersFile, Encoding.UTF8))
                {
                    string[] p = line.Split(new char[] { '\t' }, 2);
                    long ticks;
                    if (p.Length == 2 && long.TryParse(p[0], out ticks) && p[1].Trim().Length > 0)
                        reminders.Add(new Reminder { DueUtc = new DateTime(ticks, DateTimeKind.Utc), Text = p[1].Trim() });
                }
            }
            catch (Exception ex) { Log("Recordatorios: " + ex.Message); }
        }

        void SaveReminders()
        {
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (Reminder r in reminders) b.Append(r.DueUtc.Ticks).Append('\t').Append(r.Text).Append('\n');
                Directory.CreateDirectory(Settings.DataDir);
                File.WriteAllText(RemindersFile, b.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("Recordatorios: " + ex.Message); }
        }

        // ---- Notes

        // Notas.md in the save folder, next to the captures.
        public string NotesPath { get { return Path.Combine(settings.SaveFolder, "Notas.md"); } }

        public bool AddNote(string text)
        {
            text = (text ?? "").Replace("\r", "").Trim();
            if (text.Length == 0) return false;
            try
            {
                Directory.CreateDirectory(settings.SaveFolder);
                bool fresh = !File.Exists(NotesPath);
                // A note of several lines keeps them, indented under its bullet.
                string body = text.Replace("\n", "\n  ");
                string entry = (fresh ? "# Notas\n\n" : "") + "- **" + DateTime.Now.ToString("d/M/yyyy H:mm", CultureInfo.GetCultureInfo("es-ES")) + "** \u00B7 " + body + "\n";
                File.AppendAllText(NotesPath, entry, new UTF8Encoding(false));
                Log("Nota apuntada");
                return true;
            }
            catch (Exception ex)
            {
                Log("Notas: " + ex.Message);
                Notify("No se pudo apuntar", Explain(ex, NotesPath));
                return false;
            }
        }

        public void OpenNotes()
        {
            if (!File.Exists(NotesPath))
                try { Directory.CreateDirectory(settings.SaveFolder); File.WriteAllText(NotesPath, "# Notas\n\n", new UTF8Encoding(false)); }
                catch (Exception ex) { Log("Notas: " + ex.Message); }
            OpenViewerFile(NotesPath);
        }

        // ---- Dropped on the mascot

        static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
        static readonly string[] Videos = { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".wmv", ".m4v" };
        static readonly string[] Docs = { ".md", ".markdown", ".xml", ".txt" };

        static bool Is(string path, string[] exts) { return Array.IndexOf(exts, Path.GetExtension(path).ToLowerInvariant()) >= 0; }

        public static bool CanTake(string path) { return Is(path, Pictures) || Is(path, Videos) || Is(path, Docs) || Directory.Exists(path); }

        // Each file to where it opens (at most five), a folder to the quick links; what the mascot says about it.
        public string OpenDropped(string[] files)
        {
            int pics = 0, vids = 0, docs = 0;
            string skipped = null;
            foreach (string f in files)
            {
                if (pics + vids + docs >= 5) break;
                if (Directory.Exists(f))
                {
                    string why = AddLink(null, f);
                    return why ?? "¡Apuntada! «" + LinkName(f) + "» ya está en mis accesos rápidos.";
                }
                if (!File.Exists(f)) continue;
                if (Is(f, Docs)) { OpenViewerFile(f); docs++; }
                else if (Is(f, Pictures) || Is(f, Videos)) { OpenMedia(f); if (Is(f, Videos)) vids++; else pics++; }
                else if (skipped == null) skipped = Path.GetFileName(f);
            }
            int n = pics + vids + docs;
            if (n == 0) return skipped != null ? "No s\u00E9 qu\u00E9 hacer con \u00AB" + skipped + "\u00BB. Su\u00E9ltame im\u00E1genes, v\u00EDdeos, XML o Markdown." : null;
            if (n > 1) return "\u00A1Abro " + n + " archivos!";
            return pics == 1 ? "\u00A1Al editor!" : vids == 1 ? "\u00A1A verlo!" : "\u00A1Te lo abro!";
        }

        // ---- A SOAP fault arriving in the watched folder

        // Read off this thread; told by the mascot when it is free, otherwise by a notice (a fault shouldn't wait).
        void CheckArrivedFault(string path)
        {
            if (!settings.XmlFaultAlert) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string code = null, message = null, op = null;
                try
                {
                    if (new FileInfo(path).Length > 8 * 1024 * 1024) return;
                    string err;
                    string text = MarkdownTools.Load(path, out err);
                    XmlModel m;
                    int line, col;
                    if (text == null || !XmlDoc.Parse(text, out m, out err, out line, out col) || m == null) return;
                    XmlDoc.ExpandInner(m);
                    if (!XmlDoc.Fault(m, out code, out message)) return;
                    SoapInfo s = XmlSoap.Read(m);
                    op = s != null && s.Operation.Length > 0 ? s.Operation : null;
                }
                catch (Exception ex) { Log("XML con fallo: " + ex.Message); return; }
                Ui(delegate
                {
                    string what = (op != null ? op + ": " : "") + (string.IsNullOrEmpty(message) ? (string.IsNullOrEmpty(code) ? "SOAP Fault" : code) : message);
                    Log("XML con fallo: " + Path.GetFileName(path));
                    if (pet != null && pet.CanRemind) pet.FaultCard("Ojo: ha llegado un XML con fallo. " + what, delegate { OpenViewerFile(path); });
                    else Notify("XML con fallo", Path.GetFileName(path) + " \u00B7 " + what, NoticeKind.Warning, "Abrir el XML", delegate { if (!exiting) OpenViewerFile(path); });
                });
            });
        }
    }
}

// io: files load and save byte for byte (encodings, BOMs, declared XML encodings, line endings), atomic writes, changes
// made outside, and the copy saved next to a capture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        sealed class Sample
        {
            public string Name;
            public byte[] Bytes;
            public string Expect;     // the text the editor should show
            public Sample(string name, byte[] bytes, string expect) { Name = name; Bytes = bytes; Expect = expect; }
        }

        static byte[] Cat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        static List<Sample> Corpus()
        {
            List<Sample> c = new List<Sample>();
            Encoding u8 = new UTF8Encoding(false), le = new UnicodeEncoding(false, false), be = new UnicodeEncoding(true, false), w1252 = Encoding.GetEncoding(1252), latin = Encoding.GetEncoding(28591);
            string md = "# T\u00EDtulo\r\n\r\nCaf\u00E9 con \u00F1 y \u20AC, emoji \uD83D\uDE00.\r\n- uno\r\n- dos\r\n";
            c.Add(new Sample("utf8-crlf.md", u8.GetBytes(md), md.Replace("\r\n", "\n")));
            string lf = md.Replace("\r\n", "\n");
            c.Add(new Sample("utf8-bom-lf.md", Cat(new byte[] { 0xEF, 0xBB, 0xBF }, u8.GetBytes(lf)), lf));
            c.Add(new Sample("utf16le-bom.md", Cat(new byte[] { 0xFF, 0xFE }, le.GetBytes(md)), lf));
            string x16 = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<a b=\"\u00F1\">texto \u00E9</a>\r\n";
            c.Add(new Sample("utf16be-bom.xml", Cat(new byte[] { 0xFE, 0xFF }, be.GetBytes(x16)), x16.Replace("\r\n", "\n")));
            c.Add(new Sample("utf16le-nobom.xml", le.GetBytes(x16), x16.Replace("\r\n", "\n")));
            string x1252 = "<?xml version=\"1.0\" encoding=\"windows-1252\"?>\n<pedido cliente=\"Pe\u00F1a\">Caf\u00E9 \u20AC 3</pedido>\n";
            c.Add(new Sample("win1252-declared.xml", w1252.GetBytes(x1252), x1252));
            string xl = "<?xml version='1.0' encoding='ISO-8859-1'?>\r\n<a>caf\u00E9 \u00BF?</a>";
            c.Add(new Sample("latin1-declared.xml", latin.GetBytes(xl), xl.Replace("\r\n", "\n")));
            string fb = "Notas de caf\u00E9\r\nl\u00EDnea dos";
            c.Add(new Sample("ansi-fallback.md", w1252.GetBytes(fb), fb.Replace("\r\n", "\n")));
            string mixed = "uno\r\ndos\ntres\rcuatro\r\n\r\ncinco";
            c.Add(new Sample("mixed-endings.md", u8.GetBytes(mixed), "uno\ndos\ntres\ncuatro\n\ncinco"));
            string cr = "a\rb\rc\r";
            c.Add(new Sample("cr-only.md", u8.GetBytes(cr), "a\nb\nc\n"));
            return c;
        }

        static string Write(string dir, Sample s)
        {
            string p = Path.Combine(dir, s.Name);
            File.WriteAllBytes(p, s.Bytes);
            return p;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return n;
        }

        // Independent of DocFile: where the n-th character of the \n text starts in the raw text.
        static int RawAt(string raw, int n)
        {
            int i = 0, k = 0;
            while (k < n && i < raw.Length) { i += raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n' ? 2 : 1; k++; }
            return i;
        }

        static void IoSuite()
        {
            foreach (Sample s0 in Corpus())
            {
                Sample s = s0;
                Test("io", "load/" + s.Name, delegate
                {
                    string dir = WorkDir("load");
                    string p = Write(dir, s);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d != null, "not loaded: " + err);
                    Same(s.Expect, d.Text, "text");
                    Note(d.EncodingName + " " + d.NewlineName + (d.Mixed ? " mixed" : ""));
                    Check(MarkdownTools.Load(p, out err) == d.Text, "MarkdownTools.Load differs");
                });
                Test("io", "save-unchanged/" + s.Name, delegate
                {
                    string dir = WorkDir("same");
                    string p = Write(dir, s);
                    DateTime before = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    File.SetLastWriteTimeUtc(p, before);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    Check(d.Save(d.Text, null, out err), "save failed: " + err);
                    Check(SameBytes(s.Bytes, File.ReadAllBytes(p)), "bytes changed");
                    Check(File.GetLastWriteTimeUtc(p) == before, "the file was written although nothing changed");
                    // Saving as another file writes the very same bytes.
                    string q = Path.Combine(dir, "copia-" + s.Name);
                    Check(d.Save(d.Text, q, out err), "save as failed: " + err);
                    byte[] got = File.ReadAllBytes(q);
                    Check(SameBytes(s.Bytes, got), "save as differs at byte " + FirstDiff(s.Bytes, got));
                });
                Test("io", "edit-one-char/" + s.Name, delegate
                {
                    string dir = WorkDir("edit");
                    string p = Write(dir, s);
                    string err;
                    DocFile d = DocFile.Load(p, out err);
                    int at = d.Text.Length / 2;
                    string edited = d.Text.Insert(at, "X");
                    Check(d.Save(edited, null, out err), "save failed: " + err);
                    byte[] got = File.ReadAllBytes(p);
                    // Expected: the original bytes with "X" encoded at the same place; BOM and every line ending kept.
                    int bom = s.Name.Contains("-bom") ? (s.Name.StartsWith("utf8") ? 3 : 2) : 0;
                    Encoding enc = DetectForTest(s.Name);
                    string raw = enc.GetString(s.Bytes, bom, s.Bytes.Length - bom);
                    string expRaw = raw.Insert(RawAt(raw, at), "X");
                    byte[] exp = Cat(Sub(s.Bytes, 0, bom), enc.GetBytes(expRaw));
                    Check(SameBytes(exp, got), "bytes differ at " + FirstDiff(exp, got) + " (" + got.Length + " vs " + exp.Length + ")");
                    // Saving the same text again writes nothing.
                    DateTime t = File.GetLastWriteTimeUtc(p);
                    System.Threading.Thread.Sleep(30);
                    Check(d.Save(edited, null, out err), "second save failed");
                    Check(File.GetLastWriteTimeUtc(p) == t, "rewritten without changes");
                    Check(d.Check() == DiskState.Unchanged, "own save seen as an outside change");
                });
            }

            Test("io", "mixed-endings-kept-around-an-edit", delegate
            {
                string dir = WorkDir("mixed");
                string p = Path.Combine(dir, "m.md");
                File.WriteAllBytes(p, Encoding.UTF8.GetBytes("uno\r\ndos\ntres\rcuatro"));
                string err;
                DocFile d = DocFile.Load(p, out err);
                Check(d.Mixed, "not seen as mixed");
                // Insert a new line in the middle: untouched lines keep their own endings, the new one uses the main one
                // (a tie goes to CRLF).
                string t = d.Text.Replace("dos\n", "dos\nnueva\n");
                Check(d.Save(t, null, out err), err);
                Same("uno\r\ndos\nnueva\r\ntres\rcuatro", Encoding.UTF8.GetString(File.ReadAllBytes(p)), "file");
            });

            Test("io", "1252-emoji-refused", delegate
            {
                string dir = WorkDir("emoji");
                string p = Path.Combine(dir, "a.xml");
                byte[] orig = Encoding.GetEncoding(1252).GetBytes("<?xml version=\"1.0\" encoding=\"windows-1252\"?>\n<a>caf\u00E9</a>\n");
                File.WriteAllBytes(p, orig);
                string err;
                DocFile d = DocFile.Load(p, out err);
                string t = d.Text.Replace("caf\u00E9", "caf\u00E9 \uD83D\uDE00");
                string warn;
                Check(d.Encode(t, out warn) == null && warn != null, "encoded an emoji into 1252");
                Check(!d.Save(t, null, out err) && err != null && err.Contains("no caben"), "save did not refuse: " + err);
                Check(SameBytes(orig, File.ReadAllBytes(p)), "file touched");
                // Switching to UTF-8 rewrites the declaration and saves.
                string t2 = d.ToUtf8(t);
                Check(t2.Contains("encoding=\"UTF-8\""), "declaration not updated");
                Check(d.Save(t2, null, out err), err);
                Same(t2, Encoding.UTF8.GetString(File.ReadAllBytes(p)), "utf-8 file");
            });

            Test("io", "declared-encoding-change", delegate
            {
                string dir = WorkDir("decl");
                string p = Path.Combine(dir, "a.xml");
                File.WriteAllBytes(p, Encoding.GetEncoding(28591).GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<a>\u00F1</a>\n"));
                string err;
                DocFile d = DocFile.Load(p, out err);
                string t = d.Text.Replace("ISO-8859-1", "UTF-8");
                Check(d.Save(t, null, out err), err);
                byte[] b = File.ReadAllBytes(p);
                Same(t, new UTF8Encoding(false, true).GetString(b), "now UTF-8");
                Check(b.Length == Encoding.UTF8.GetByteCount(t), "not UTF-8 bytes");
            });

            Test("io", "atomic-locked-target", delegate
            {
                string dir = WorkDir("lock");
                string p = Path.Combine(dir, "a.md");
                byte[] orig = Encoding.UTF8.GetBytes("original\n");
                File.WriteAllBytes(p, orig);
                string err;
                DocFile d = DocFile.Load(p, out err);
                bool ok;
                using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None)) ok = d.Save("cambiado\n", null, out err);
                Check(!ok && err != null, "saved over a locked file");
                Check(SameBytes(orig, File.ReadAllBytes(p)), "original damaged");
                Check(Directory.GetFiles(dir).Length == 1, "a temporary file was left: " + string.Join(", ", Directory.GetFiles(dir)));
                Note(err);
            });

            Test("io", "read-only", delegate
            {
                string dir = WorkDir("ro");
                string p = Path.Combine(dir, "a.md");
                File.WriteAllText(p, "texto\n");
                string err;
                DocFile d = DocFile.Load(p, out err);
                File.SetAttributes(p, FileAttributes.ReadOnly);
                try
                {
                    Check(!d.Save("otro\n", null, out err), "saved over a read-only file");
                    Check(err.Contains("solo lectura"), "message: " + err);
                    Check(File.ReadAllText(p) == "texto\n", "file changed");
                }
                finally { File.SetAttributes(p, FileAttributes.Normal); }
            });

            Test("io", "outside-changes", delegate
            {
                string dir = WorkDir("ext");
                string p = Path.Combine(dir, "a.md");
                File.WriteAllText(p, "uno\n");
                string err;
                DocFile d = DocFile.Load(p, out err);
                Check(d.Check() == DiskState.Unchanged, "fresh file not unchanged");
                File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(1));
                Check(d.Check() == DiskState.Unchanged, "a touch with the same content counts as a change");
                File.WriteAllText(p, "dos!\n");
                Check(d.Check() == DiskState.Modified, "outside write not seen");
                d.Acknowledge();
                Check(d.Check() == DiskState.Unchanged, "acknowledged version still a change");
                File.Delete(p);
                Check(d.Check() == DiskState.Deleted, "deletion not seen");
                Check(d.Save("tres\n", null, out err), "could not save again: " + err);
                Check(File.ReadAllText(p) == "tres\n" && d.Check() == DiskState.Unchanged, "after saving again");
            });

            Test("io", "size-limits", delegate
            {
                string dir = WorkDir("size");
                string ok = Path.Combine(dir, "ok.md"), big = Path.Combine(dir, "big.md");
                File.WriteAllBytes(ok, Encoding.ASCII.GetBytes(new string('a', MarkdownTools.MaxBytes)));
                File.WriteAllBytes(big, Encoding.ASCII.GetBytes(new string('a', MarkdownTools.MaxBytes + 1)));
                string err;
                Check(DocFile.Load(ok, out err) != null, "2 MB refused: " + err);
                Check(DocFile.Load(big, out err) == null && err.Contains("pesa demasiado"), "over 2 MB accepted");
            });

            Test("io", "companion-copy-encoding", delegate
            {
                // MX-11: the copy next to a capture is written in the declared encoding.
                string t = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<a>caf\u00E9</a>\n";
                byte[] b = MarkdownWindow.Companion(t, true, null);
                Same(t, Encoding.GetEncoding(28591).GetString(b), "latin-1 copy");
                Check(b.Length == t.Length, "not one byte per character");
                string e = t.Replace("caf\u00E9", "\uD83D\uDE00");
                byte[] u = MarkdownWindow.Companion(e, true, null);
                string back = Encoding.UTF8.GetString(u);
                Check(back.Contains("encoding=\"UTF-8\"") && back.Contains("\uD83D\uDE00"), "emoji copy not UTF-8 with its declaration: " + back);
                // A loaded file that did not change: the same bytes.
                string dir = WorkDir("comp");
                string p = Path.Combine(dir, "a.md");
                byte[] orig = Cat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("a\r\nb\n"));
                File.WriteAllBytes(p, orig);
                string err;
                DocFile d = DocFile.Load(p, out err);
                Check(SameBytes(orig, MarkdownWindow.Companion(d.Text, false, d)), "unchanged companion differs");
            });

            Test("io", "declaration-that-lies-is-not-trusted", delegate
            {
                // Declared us-ascii, but the bytes are UTF-8: reading them as ASCII turned the accent into "??" and an edit
                // wrote that back.
                string dir = WorkDir("lie");
                string p = Path.Combine(dir, "a.xml");
                byte[] orig = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"us-ascii\"?>\n<a>\u00E1</a>\n");
                File.WriteAllBytes(p, orig);
                string err;
                DocFile d = DocFile.Load(p, out err);
                Check(d != null, err);
                Check(d.Text.Contains("<a>\u00E1</a>") && !d.Lossy, "read as " + d.Text);
                string t = d.Text.Replace("<a>", "<a>X");
                Check(d.Save(t, null, out err), err);
                Check(SameBytes(Encoding.UTF8.GetBytes(t), File.ReadAllBytes(p)), "bytes: " + Encoding.UTF8.GetString(File.ReadAllBytes(p)));
            });

            Test("io", "undecodable-bytes-are-not-saved-over", delegate
            {
                // UTF-16 with a lone surrogate and an odd last byte: shown with U+FFFD, and an edit is refused instead of
                // writing U+FFFD over the original bytes.
                string dir = WorkDir("lossy");
                string p = Path.Combine(dir, "a.md");
                byte[] body = new UnicodeEncoding(false, false).GetBytes("uno\r\n" + new string('z', 400) + "\r\n");
                byte[] orig = Cat(Cat(new byte[] { 0xFF, 0xFE }, body), new byte[] { 0x00, 0xD8, 0x41, 0x00, 0x0A, 0x00, 0x42 });
                File.WriteAllBytes(p, orig);
                string err, warn;
                DocFile d = DocFile.Load(p, out err);
                Check(d != null, "not loaded: " + err);
                Check(d.Lossy && d.Text.IndexOf('\uFFFD') >= 0, "not marked lossy: " + d.Text);
                string t = d.Text.Replace("uno", "dos");
                Check(d.Encode(t, out warn) == null && warn != null && warn.Contains("UTF-16 LE"), "encoded: " + warn);
                Check(!d.Save(t, null, out err) && err != null, "saved over the file");
                Check(SameBytes(orig, File.ReadAllBytes(p)), "file touched");
                // The unchanged text still copies byte for byte.
                string q = Path.Combine(dir, "copia.md");
                Check(d.Save(d.Text, q, out err), err);
                Check(SameBytes(orig, File.ReadAllBytes(q)), "copy differs");
                // The document is now the copy, which holds the same bytes: still refused.
                Check(d.Lossy && !d.Save(t, null, out err), "the copy lost its protection");
                d.AcceptLoss();
                Check(d.Save(t, null, out err), "after accepting: " + err);
                Check(!d.Lossy && new UnicodeEncoding(false, true, false).GetString(File.ReadAllBytes(q)).Contains("dos"), "not saved after accepting");
                Check(SameBytes(orig, File.ReadAllBytes(p)), "original touched");
                // Bad bytes after a declaration fall back to UTF-8 or Windows-1252, which read every byte.
                byte[] w = Encoding.GetEncoding(1252).GetBytes("<?xml version=\"1.0\" encoding=\"us-ascii\"?><a>caf\u00E9</a>");
                DocFile x = DocFile.FromBytes(w, true);
                Check(!x.Lossy && x.Enc.CodePage == 1252 && x.Text.Contains("caf\u00E9"), "1252 fallback: " + x.EncodingName);
            });

            Test("io", "bom-and-declaration-disagree", delegate
            {
                // The BOM wins and stays: only an edit of the declaration changes the encoding.
                string dir = WorkDir("bomdecl");
                string x16 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<a>\u00F1</a>\r\n";
                string p = Path.Combine(dir, "a.xml");
                byte[] orig = Cat(new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false).GetBytes(x16));
                File.WriteAllBytes(p, orig);
                string err, warn;
                DocFile d = DocFile.Load(p, out err);
                Check(d.Enc.CodePage == 1200 && d.Bom, "loaded as " + d.EncodingName);
                Check(SameBytes(orig, d.Encode(d.Text, out warn)), "unchanged text re-encoded");
                string t = d.Text.Replace("\u00F1", "\u00F1o");
                Check(d.Save(t, null, out err), err);
                byte[] got = File.ReadAllBytes(p);
                byte[] exp = Cat(new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false).GetBytes(x16.Replace("\u00F1", "\u00F1o")));
                Check(SameBytes(exp, got), "UTF-16 lost at byte " + FirstDiff(exp, got));
                // UTF-8 with BOM that declares Latin-1.
                string xl = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<a>\u00E9</a>\n";
                byte[] o8 = Cat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(xl));
                string p8 = Path.Combine(dir, "b.xml");
                File.WriteAllBytes(p8, o8);
                DocFile d8 = DocFile.Load(p8, out err);
                Check(SameBytes(o8, d8.Encode(d8.Text, out warn)), "unchanged UTF-8 text re-encoded");
                string t8 = d8.Text.Replace("\u00E9", "\u00E9!");
                Check(d8.Save(t8, null, out err), err);
                Check(SameBytes(Cat(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(t8)), File.ReadAllBytes(p8)), "not UTF-8 with BOM");
                // Editing the declaration still switches.
                string t9 = t8.Replace("ISO-8859-1", "UTF-16");
                Check(d8.Save(t9, null, out err), err);
                byte[] b9 = File.ReadAllBytes(p8);
                Check(b9[0] == 0xFF && b9[1] == 0xFE && new UnicodeEncoding(false, true, true).GetString(b9, 2, b9.Length - 2) == t9, "declaration edit did not switch to UTF-16");
            });
        }

        static byte[] Sub(byte[] b, int a, int n)
        {
            byte[] r = new byte[n];
            Buffer.BlockCopy(b, a, r, 0, n);
            return r;
        }

        static Encoding DetectForTest(string name)
        {
            if (name.StartsWith("utf16le")) return new UnicodeEncoding(false, false);
            if (name.StartsWith("utf16be")) return new UnicodeEncoding(true, false);
            if (name.StartsWith("win1252") || name.StartsWith("ansi")) return Encoding.GetEncoding(1252);
            if (name.StartsWith("latin1")) return Encoding.GetEncoding(28591);
            return new UTF8Encoding(false);
        }
    }
}

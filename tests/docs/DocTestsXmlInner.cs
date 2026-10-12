// SOAP messages in the XML viewer (suite "xmlinner"): XML escaped inside a field opened as a branch, its paths, the
// SOAP fault banner and [ / ] through the files of a folder. Synthetic messages only, in throwaway folders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using System.Windows.Controls;

namespace Stackshot.Tests
{
    static partial class DocTests
    {
        const string InnerDoc = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Respuesta><Codigo>0</Codigo><Persona nombre=\"Ana\"><Nota>&lt;b&gt;negrita&lt;/b&gt;</Nota></Persona></Respuesta>";

        static string Soap(string bodyInner)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\">\n  <S:Body>\n" +
                   "    <ns2:consultaPruebaResponse xmlns:ns2=\"urn:prueba\">\n      " + bodyInner + "\n    </ns2:consultaPruebaResponse>\n  </S:Body>\n</S:Envelope>\n";
        }

        static void SameV<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new TestFailed(what + ": expected <" + expected + ">, got <" + actual + ">");
        }

        static XmlModel Model(string text)
        {
            XmlModel m;
            string err;
            int l, c;
            Check(XmlDoc.Parse(text, out m, out err, out l, out c), "does not parse: " + err);
            return m;
        }

        static XItem FirstNamed(List<XItem> items, string local)
        {
            foreach (XItem i in items)
            {
                if (i.Kind == XKind.Element && i.Local == local) return i;
                XItem k = FirstNamed(i.Kids, local);
                if (k != null) return k;
            }
            return null;
        }

        static void XmlInnerSuite()
        {
            Test("xmlinner", "escaped-document-opens-as-branch", delegate
            {
                XmlModel m = Model(Soap("<return>" + SecurityElement.Escape(InnerDoc) + "</return>"));
                SameV(2, XmlDoc.ExpandInner(m), "fields opened (the response and the escaped <b> inside Nota)");
                XItem ret = FirstNamed(m.Top, "return");
                Check(ret.Inner, "return not marked Inner");
                SameV(1, ret.Kids.Count, "the declaration is left out");
                SameV("Respuesta", ret.Kids[0].Name, "inner root");
                XItem nota = FirstNamed(ret.Kids, "Nota");
                Check(nota != null && nota.Inner && nota.Kids[0].Name == "b", "second level not opened");
                SameV("negrita", XmlDoc.TextOf(nota.Kids[0]), "text two levels down");
                XItem persona = FirstNamed(ret.Kids, "Persona");
                SameV(ret.Line, persona.Line, "inner node points at its host's line");
                SameV(0, persona.EndLine, "inner node has no end of its own");
                SameV(ret, ret.Kids[0].Parent, "parent");
                Check(ret.Kids[0].Sibs == ret.Kids, "siblings list");
                SameV(0, XmlDoc.ExpandInner(m), "a second pass opens nothing more");
            });
            Test("xmlinner", "cdata-and-whitespace-around-also-open", delegate
            {
                XmlModel m = Model(Soap("<return><![CDATA[\n   " + InnerDoc + "\n  ]]></return>"));
                Check(XmlDoc.ExpandInner(m) >= 1 && FirstNamed(m.Top, "return").Inner, "CDATA not opened");
            });
            Test("xmlinner", "text-that-is-not-a-document-stays-text", delegate
            {
                string[] values = { "&lt;b&gt;", "a &lt; b &gt; c", "&lt;sin cerrar", "&lt;a&gt;&lt;/b&gt;", "&lt;&gt;", "&lt;!-- solo --&gt;", "&lt;?pi x?&gt;", "12", "" };
                foreach (string v in values)
                {
                    XmlModel m = Model("<r><f>" + v + "</f></r>");
                    SameV(0, XmlDoc.ExpandInner(m), "opened: " + v);
                    XItem f = FirstNamed(m.Top, "f");
                    Check(!f.Inner, "marked Inner: " + v);
                }
                XmlModel mixed = Model("<r><f>&lt;a/&gt;<g/></f></r>");
                SameV(0, XmlDoc.ExpandInner(mixed), "mixed content opened");
            });
            Test("xmlinner", "three-levels-at-most", delegate
            {
                string doc = "<hoja>fin</hoja>";
                for (int i = 0; i < 5; i++) doc = "<n" + i + ">" + SecurityElement.Escape(doc) + "</n" + i + ">";
                XmlModel m = Model("<r>" + doc + "</r>");
                SameV(3, XmlDoc.ExpandInner(m), "levels opened");
                Check(FirstNamed(m.Top, "hoja") == null, "went deeper than three levels");
                // The viewer may ask again for the same model (a rebuilt tree): still three.
                SameV(0, XmlDoc.ExpandInner(m), "second pass");
                SameV(0, XmlDoc.ExpandInner(m), "third pass");
                Check(FirstNamed(m.Top, "n1") != null && !FirstNamed(m.Top, "n1").Inner, "a later pass went a level deeper");
            });
            Test("xmlinner", "xpath-of-inner-node-has-two-parts", delegate
            {
                XmlModel m = Model(Soap("<return>" + SecurityElement.Escape(InnerDoc) + "</return>"));
                XmlDoc.ExpandInner(m);
                XItem codigo = FirstNamed(m.Top, "Codigo");
                SameV("/S:Envelope/S:Body/ns2:consultaPruebaResponse/return \u00BB /Respuesta/Codigo", XmlDoc.XPath(codigo), "path");
                XItem b = FirstNamed(m.Top, "b");
                SameV("/S:Envelope/S:Body/ns2:consultaPruebaResponse/return \u00BB /Respuesta/Persona/Nota \u00BB /b", XmlDoc.XPath(b), "path two levels down");
                SameV("/S:Envelope/S:Body/ns2:consultaPruebaResponse/return", XmlDoc.XPath(FirstNamed(m.Top, "return")), "host keeps its plain path");
                XmlModel plain = Model("<a><b x=\"1\"/><b/></a>");
                SameV("/a/b[2]", XmlDoc.XPath(plain.Top[0].Kids[1]), "plain document unchanged");
            });
            Test("xmlinner", "fault-soap11-soap12-escaped-and-none", delegate
            {
                string code, msg;
                XmlModel f11 = Model("<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\"><S:Body><S:Fault><faultcode>S:Server</faultcode><faultstring> Error de prueba </faultstring></S:Fault></S:Body></S:Envelope>");
                Check(XmlDoc.Fault(f11, out code, out msg), "1.1 fault not found");
                SameV("S:Server", code, "1.1 code");
                SameV("Error de prueba", msg, "1.1 message");
                XmlModel f12 = Model("<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Body><env:Fault><env:Code><env:Value>env:Sender</env:Value></env:Code>" +
                                     "<env:Reason><env:Text xml:lang=\"es\">Datos no v\u00E1lidos</env:Text></env:Reason></env:Fault></env:Body></env:Envelope>");
                Check(XmlDoc.Fault(f12, out code, out msg), "1.2 fault not found");
                SameV("env:Sender", code, "1.2 code");
                SameV("Datos no v\u00E1lidos", msg, "1.2 message");
                XmlModel bare = Model("<Envelope><Body><Fault/></Body></Envelope>");
                Check(XmlDoc.Fault(bare, out code, out msg) && code == "" && msg == "", "bare fault");
                XmlModel escaped = Model(Soap("<return>" + SecurityElement.Escape("<Fault><faultcode>X</faultcode><faultstring>dentro</faultstring></Fault>") + "</return>"));
                Check(!XmlDoc.Fault(escaped, out code, out msg), "fault seen before opening the escaped text");
                XmlDoc.ExpandInner(escaped);
                Check(XmlDoc.Fault(escaped, out code, out msg) && msg == "dentro", "escaped fault not found once opened");
                Check(!XmlDoc.Fault(Model(Soap("<return>ok</return>")), out code, out msg), "fault in a normal answer");
            });
            Test("xmlinner", "viewer-fault-banner-and-brackets", delegate
            {
                string dir = WorkDir("xmlfolder");
                DateTime t0 = DateTime.Now.AddMinutes(-10);
                string a = Put(dir, "tmp1.xml", Soap("<return>uno</return>"));
                string b = Put(dir, "tmp2.xml", "<S:Envelope xmlns:S=\"http://schemas.xmlsoap.org/soap/envelope/\"><S:Body><S:Fault><faultcode>S:Client</faultcode><faultstring>Falta el DNI</faultstring></S:Fault></S:Body></S:Envelope>");
                string c = Put(dir, "tmp3.xml", Soap("<return>" + SecurityElement.Escape(InnerDoc) + "</return>"));
                Put(dir, "nota.txt", "no es XML");
                File.SetLastWriteTime(a, t0);
                File.SetLastWriteTime(b, t0.AddMinutes(1));
                File.SetLastWriteTime(c, t0.AddMinutes(2));
                MarkdownWindow w = NewWindow();
                try
                {
                    // Rebuilt with the window: read it each time.
                    Func<Border> hostNow = delegate { return (Border)typeof(MarkdownWindow).GetField("bannerHost", NP).GetValue(w); };
                    Open(w, a);
                    Check(Until(delegate { return w.Tree != null && w.Tree.Rows.Count > 0; }, 8000), "tree not shown");
                    Check(hostNow().Child == null, "banner on a normal answer");
                    Call(w, "Neighbour", 1);
                    Check(Until(delegate { return w.Doc != null && w.Doc.Path == b && w.Tree != null && w.Tree.Rows.Count > 0; }, 8000), "] did not go to the next file: " + (w.Doc != null ? w.Doc.Path : "null"));
                    Pump(150);
                    // A SOAP envelope's fault shows in its SOAP bar (code and message), not in a banner of its own.
                    Check(hostNow().Child == null, "a fault banner besides the SOAP bar");
                    Check(w.HeaderHost != null && w.HeaderHost.Child != null, "no SOAP bar on a SOAP fault");
                    Check(Flat(w.HeaderHost.Child).Contains("Falta el DNI") && Flat(w.HeaderHost.Child).Contains("S:Client"), "fault text: " + Flat(w.HeaderHost.Child));
                    Call(w, "Neighbour", 1);
                    Check(Until(delegate { return w.Doc != null && w.Doc.Path == c; }, 8000), "] did not reach the third file");
                    Pump(150);
                    Check(hostNow().Child == null, "banner left over from the fault");
                    Check(Until(delegate { return w.Tree != null && w.Tree.Rows.Count > 0; }, 8000), "tree of the third file");
                    Check(Until(delegate { return w.HeaderHost != null && w.HeaderHost.Child != null && !Flat(w.HeaderHost.Child).Contains("Falta"); }, 8000), "fault left over in the SOAP bar");
                    bool innerRow = false;
                    foreach (XRow r in w.Tree.Rows) if (r.Node.Inner) innerRow = true;
                    Check(innerRow, "the escaped answer is not a branch in the tree");
                    Call(w, "Neighbour", 1);
                    Pump(200);
                    SameV(c, w.Doc.Path, "past the last file");
                    string said = (string)typeof(MarkdownWindow).GetField("status", NP).GetValue(w);
                    Check(said != null && said.Contains("posteriores"), "no word at the end: " + said);
                    Call(w, "Neighbour", -1);
                    Check(Until(delegate { return w.Doc != null && w.Doc.Path == b; }, 8000), "[ did not go back");
                    // Typed as text (AltGr on a Spanish keyboard), outside any field.
                    Type("]", w);
                    Check(Until(delegate { return w.Doc != null && w.Doc.Path == c; }, 8000), "typed ] did not go forward");
                    Pump(150);
                    Type("[", w);
                    Check(Until(delegate { return w.Doc != null && w.Doc.Path == b; }, 8000), "typed [ did not go back");
                    Pump(150);
                    Type("x", w);
                    Pump(200);
                    SameV(b, w.Doc.Path, "another letter moved");
                }
                finally { Shut(w); DeleteDir(dir); }
            });
            Test("xmlinner", "arrived-file-waits-for-unsaved-changes", delegate
            {
                string dir = WorkDir("arrived");
                string a = Put(dir, "propio.xml", "<pedido><linea/></pedido>");
                string b = Put(dir, "nuevo.xml", Soap("<return>dos</return>"));
                MarkdownWindow w = NewWindow();
                try
                {
                    Open(w, a);
                    w.SetEditing(true);
                    Pump(200);
                    w.Editor.Apply(TextEdit.Make(w.Editor.Text.Length, 0, "\n<!-- cambio -->", 0, 0));
                    Pump(100);
                    Check(w.Dirty, "not dirty after typing");
                    Check(!MarkdownWindow.OpenArrived(null, b), "replaced a document with unsaved changes");
                    Pump(300);
                    SameV(a, w.Doc.Path, "document kept");
                    Check(w.Dirty, "changes kept");
                }
                finally { Shut(w); DeleteDir(dir); }
            });
        }

        static void Type(string text, System.Windows.UIElement target)
        {
            System.Windows.Input.TextComposition tc = new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, target, text);
            System.Windows.Input.TextCompositionEventArgs e = new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, tc);
            e.RoutedEvent = System.Windows.Input.TextCompositionManager.PreviewTextInputEvent;
            target.RaiseEvent(e);
        }

        // Every text in an element tree, for checking what a banner says.
        static string Flat(object o)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            FlatInto(o as System.Windows.DependencyObject, sb);
            return sb.ToString();
        }

        static void FlatInto(System.Windows.DependencyObject d, System.Text.StringBuilder sb)
        {
            if (d == null) return;
            TextBlock t = d as TextBlock;
            if (t != null)
            {
                foreach (System.Windows.Documents.Inline i in t.Inlines)
                {
                    System.Windows.Documents.Run r = i as System.Windows.Documents.Run;
                    if (r != null) sb.Append(r.Text).Append(' ');
                }
                if (t.Inlines.Count == 0) sb.Append(t.Text).Append(' ');
            }
            foreach (object c in System.Windows.LogicalTreeHelper.GetChildren(d)) FlatInto(c as System.Windows.DependencyObject, sb);
        }
    }
}

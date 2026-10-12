// Stackshot - Syntax colours for the editor: Markdown and XML tokenizers that resume from saved states, so only what is
// on screen is coloured, at any document size.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Stackshot
{
    public enum Tok : byte { Plain, Head, Mark, Code, Strong, Em, Link, Quote, List, Faint, Name, Attr, Value, Ns, Comment, CData, Pi, Entity, Count }

    public struct TokSpan
    {
        public int Start, Length;
        public Tok Kind;
        public TokSpan(int start, int length, Tok kind) { Start = start; Length = length; Kind = kind; }
    }

    // The text is cut into segments at line ends (and every SegMax characters on very long lines); the state at the start
    // of each segment is kept. An edit drops the segments after it; colouring a range resumes from the segment before it.
    public abstract class Syntax
    {
        public const int SegMax = 4096;
        readonly List<int> starts = new List<int>(), states = new List<int>();
        string text = "";
        bool done;

        protected Syntax() { Reset(""); }

        public static Syntax For(bool xml) { return xml ? (Syntax)new XmlSyntax() : new MdSyntax(); }

        protected abstract int Initial { get; }
        protected abstract int Advance(string t, int s, int e, int state);
        protected abstract void Tokenize(string t, int s, int e, int state, List<TokSpan> o);

        public void Reset(string t)
        {
            text = t ?? "";
            starts.Clear(); states.Clear();
            starts.Add(0); states.Add(Initial);
            done = false;
        }

        // The text changed from this offset on: what came before it is still valid.
        public void Changed(string t, int offset)
        {
            text = t ?? "";
            int k = Find(Math.Max(0, offset));
            if (k + 1 < starts.Count) { starts.RemoveRange(k + 1, starts.Count - k - 1); states.RemoveRange(k + 1, states.Count - k - 1); }
            done = false;
        }

        int Find(int pos)
        {
            int lo = 0, hi = starts.Count - 1, pick = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (starts[mid] <= pos) { pick = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return pick;
        }

        int End(int s)
        {
            int room = Math.Min(SegMax, text.Length - s);
            int nl = room > 0 ? text.IndexOf('\n', s, room) : -1;
            return nl >= 0 ? nl + 1 : s + room;
        }

        void Ensure(int pos)
        {
            while (!done && starts[starts.Count - 1] <= pos)
            {
                int s = starts[starts.Count - 1], e = End(s);
                if (e >= text.Length && (e == s || text[e - 1] != '\n')) { done = true; break; }
                states.Add(Advance(text, s, e, states[states.Count - 1]));
                starts.Add(e);
            }
        }

        // The coloured spans that touch [from, to), cut to it.
        public void Spans(int from, int to, List<TokSpan> o)
        {
            o.Clear();
            from = Math.Max(0, from); to = Math.Min(text.Length, to);
            if (from >= to) return;
            Ensure(to);
            List<TokSpan> seg = new List<TokSpan>();
            for (int k = Find(from); k < starts.Count && starts[k] < to; k++)
            {
                int s = starts[k], e = k + 1 < starts.Count ? starts[k + 1] : End(s);
                if (e <= from) continue;
                seg.Clear();
                Tokenize(text, s, e, states[k], seg);
                foreach (TokSpan sp in seg)
                {
                    int a = Math.Max(from, sp.Start), b = Math.Min(to, sp.Start + sp.Length);
                    if (b > a && sp.Kind != Tok.Plain) o.Add(new TokSpan(a, b - a, sp.Kind));
                }
            }
        }

        // Every span of the whole text (tests and measurements).
        public List<TokSpan> All()
        {
            List<TokSpan> o = new List<TokSpan>();
            Spans(0, text.Length, o);
            return o;
        }

        protected static void Add(List<TokSpan> o, int start, int length, Tok kind)
        {
            if (length <= 0) return;
            if (o.Count > 0)
            {
                TokSpan last = o[o.Count - 1];
                if (last.Kind == kind && last.Start + last.Length == start) { last.Length += length; o[o.Count - 1] = last; return; }
            }
            o.Add(new TokSpan(start, length, kind));
        }
    }

    public sealed class MdSyntax : Syntax
    {
        // Lines over this length are coloured only up to it: a regex over a huge pasted line is not worth the wait.
        const int LineCap = 10000;
        static readonly Regex FenceRx = new Regex(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.Compiled);
        static readonly Regex HeadRx = new Regex(@"^( {0,3})(#{1,6})([ \t]+)(.*)$", RegexOptions.Compiled);
        static readonly Regex RuleRx = new Regex(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.Compiled);
        static readonly Regex QuoteRx = new Regex(@"^(\s*(?:>\s?)+)(.*)$", RegexOptions.Compiled);
        static readonly Regex ListRx = new Regex(@"^(\s*)([-*+]|\d+[.)])([ \t]+)(\[[ xX]\][ \t]+)?(.*)$", RegexOptions.Compiled);
        static readonly Regex InlineRx = new Regex(@"(`[^`\n]+`)|(\*\*[^*\n]+\*\*|__[^_\n]+__)|(\*[^*\s][^*\n]*\*|(?<![\w])_[^_\s][^_\n]*_(?![\w]))|(\[[^\]\n]*\]\([^)\n]*\))|(~~[^~\n]+~~)", RegexOptions.Compiled);

        // State: 0 outside code; inside a fence, (length << 1) | (1 for ``` or 0 for ~~~).
        protected override int Initial { get { return 0; } }

        static bool LineStart(string t, int s) { return s == 0 || t[s - 1] == '\n'; }

        static string LineOf(string t, int s, int e)
        {
            int n = e - s;
            if (n > 0 && t[e - 1] == '\n') n--;
            if (n > 0 && t[s + n - 1] == '\r') n--;
            return t.Substring(s, n);
        }

        protected override int Advance(string t, int s, int e, int state)
        {
            if (!LineStart(t, s)) return state;
            string l = LineOf(t, s, e);
            Match f = FenceRx.Match(l);
            if (state != 0)
            {
                if (!f.Success) return state;
                string fm = f.Groups[1].Value;
                bool same = (fm[0] == '`') == ((state & 1) == 1) && fm.Length >= (state >> 1);
                return same && l.Trim().Trim('`', '~').Length == 0 ? 0 : state;
            }
            if (!f.Success) return 0;
            string m = f.Groups[1].Value;
            return (m.Length << 1) | (m[0] == '`' ? 1 : 0);
        }

        protected override void Tokenize(string t, int s, int e, int state, List<TokSpan> o)
        {
            string l = LineOf(t, s, e);
            if (l.Length > LineCap) l = l.Substring(0, LineCap);
            if (state != 0 || FenceRx.IsMatch(l) && LineStart(t, s)) { Add(o, s, l.Length, Tok.Code); return; }
            if (!LineStart(t, s)) { Inline(o, l, s, Tok.Plain); return; }
            Match m = HeadRx.Match(l);
            if (m.Success)
            {
                Add(o, s + m.Groups[2].Index, m.Groups[2].Length, Tok.Mark);
                Add(o, s + m.Groups[4].Index, m.Groups[4].Length, Tok.Head);
                return;
            }
            if (RuleRx.IsMatch(l)) { Add(o, s, l.Length, Tok.Faint); return; }
            m = QuoteRx.Match(l);
            if (m.Success)
            {
                Add(o, s + m.Groups[1].Index, m.Groups[1].Length, Tok.Link);
                Inline(o, m.Groups[2].Value, s + m.Groups[2].Index, Tok.Quote);
                return;
            }
            m = ListRx.Match(l);
            if (m.Success)
            {
                Add(o, s + m.Groups[2].Index, m.Groups[2].Length, Tok.List);
                if (m.Groups[4].Success) Add(o, s + m.Groups[4].Index, m.Groups[4].Length, Tok.List);
                Inline(o, m.Groups[5].Value, s + m.Groups[5].Index, Tok.Plain);
                return;
            }
            if (l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            {
                int at = 0;
                while (at <= l.Length)
                {
                    int bar = l.IndexOf('|', at);
                    int end = bar < 0 ? l.Length : bar;
                    if (end > at) Inline(o, l.Substring(at, end - at), s + at, Tok.Plain);
                    if (bar < 0) break;
                    Add(o, s + bar, 1, Tok.Mark);
                    at = bar + 1;
                }
                return;
            }
            Inline(o, l, s, Tok.Plain);
        }

        static void Inline(List<TokSpan> o, string str, int at, Tok baseKind)
        {
            int p = 0;
            foreach (Match m in InlineRx.Matches(str))
            {
                if (m.Index > p) Add(o, at + p, m.Index - p, baseKind);
                Tok k = m.Groups[1].Success ? Tok.Code : m.Groups[2].Success ? Tok.Strong : m.Groups[3].Success ? Tok.Em : m.Groups[4].Success ? Tok.Link : Tok.Faint;
                Add(o, at + m.Index, m.Length, k);
                p = m.Index + m.Length;
            }
            if (p < str.Length) Add(o, at + p, str.Length - p, baseKind);
        }
    }

    // A character-level scanner: it can stop and resume anywhere, so long one-line (minified) XML is cut into segments too.
    public sealed class XmlSyntax : Syntax
    {
        const int Text = 0, TagName = 1, InTag = 2, AttrName = 3, AttrNs = 4, AfterAttr = 5, BeforeValue = 6, ValueDq = 7, ValueSq = 8,
                  CommentS = 9, CDataS = 10, PiS = 11, DoctypeS = 12, EntityS = 13, ValueEntDq = 14, ValueEntSq = 15;

        protected override int Initial { get { return Text; } }

        protected override int Advance(string t, int s, int e, int state)
        {
            for (int i = s; i < e; ) state = Step(t, ref i, state, null);
            return state;
        }

        protected override void Tokenize(string t, int s, int e, int state, List<TokSpan> o)
        {
            for (int i = s; i < e; ) state = Step(t, ref i, state, o);
        }

        static bool Ahead(string t, int i, string what)
        {
            return i + what.Length <= t.Length && string.CompareOrdinal(t, i, what, 0, what.Length) == 0;
        }

        static bool Space(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r'; }

        // Reads one character (or a whole delimiter) at i, colours it and returns the state after it.
        static int Step(string t, ref int i, int st, List<TokSpan> o)
        {
            char c = t[i];
            switch (st)
            {
                case Text:
                    if (c == '<')
                    {
                        if (Ahead(t, i, "<!--")) { Put(o, ref i, 4, Tok.Comment); return CommentS; }
                        if (Ahead(t, i, "<![CDATA[")) { Put(o, ref i, 9, Tok.CData); return CDataS; }
                        if (Ahead(t, i, "<?")) { Put(o, ref i, 2, Tok.Pi); return PiS; }
                        if (Ahead(t, i, "<!")) { Put(o, ref i, 2, Tok.Pi); return DoctypeS; }
                        Put(o, ref i, Ahead(t, i, "</") ? 2 : 1, Tok.Mark);
                        return TagName;
                    }
                    if (c == '&') { Put(o, ref i, 1, Tok.Entity); return EntityS; }
                    Put(o, ref i, 1, Tok.Plain);
                    return Text;
                case EntityS:
                    if (c == ';') { Put(o, ref i, 1, Tok.Entity); return Text; }
                    if (Space(c) || c == '<' || c == '&') return Text;
                    Put(o, ref i, 1, Tok.Entity);
                    return EntityS;
                case TagName:
                    if (c == '>') { Put(o, ref i, 1, Tok.Mark); return Text; }
                    if (c == '/') { Put(o, ref i, 1, Tok.Mark); return InTag; }
                    if (Space(c)) { Put(o, ref i, 1, Tok.Plain); return InTag; }
                    Put(o, ref i, 1, Tok.Name);
                    return TagName;
                case InTag:
                case AfterAttr:
                    if (Space(c)) { Put(o, ref i, 1, Tok.Plain); return st; }
                    if (c == '>') { Put(o, ref i, 1, Tok.Mark); return Text; }
                    if (c == '/' || c == '?') { Put(o, ref i, 1, Tok.Mark); return InTag; }
                    if (c == '=' && st == AfterAttr) { Put(o, ref i, 1, Tok.Mark); return BeforeValue; }
                    if (c == '<') return Text;
                    {
                        bool ns = Ahead(t, i, "xmlns") && (i + 5 >= t.Length || t[i + 5] == ':' || t[i + 5] == '=' || Space(t[i + 5]));
                        Put(o, ref i, 1, ns ? Tok.Ns : Tok.Attr);
                        return ns ? AttrNs : AttrName;
                    }
                case AttrName:
                case AttrNs:
                    if (c == '=') { Put(o, ref i, 1, Tok.Mark); return BeforeValue; }
                    if (Space(c)) { Put(o, ref i, 1, Tok.Plain); return AfterAttr; }
                    if (c == '>') { Put(o, ref i, 1, Tok.Mark); return Text; }
                    if (c == '/') { Put(o, ref i, 1, Tok.Mark); return InTag; }
                    Put(o, ref i, 1, st == AttrNs ? Tok.Ns : Tok.Attr);
                    return st;
                case BeforeValue:
                    if (Space(c)) { Put(o, ref i, 1, Tok.Plain); return BeforeValue; }
                    if (c == '"') { Put(o, ref i, 1, Tok.Value); return ValueDq; }
                    if (c == '\'') { Put(o, ref i, 1, Tok.Value); return ValueSq; }
                    if (c == '>') { Put(o, ref i, 1, Tok.Mark); return Text; }
                    return InTag;
                case ValueDq:
                case ValueSq:
                    if (c == (st == ValueDq ? '"' : '\'')) { Put(o, ref i, 1, Tok.Value); return InTag; }
                    if (c == '&') { Put(o, ref i, 1, Tok.Entity); return st == ValueDq ? ValueEntDq : ValueEntSq; }
                    Put(o, ref i, 1, Tok.Value);
                    return st;
                case ValueEntDq:
                case ValueEntSq:
                {
                    int back = st == ValueEntDq ? ValueDq : ValueSq;
                    if (c == ';') { Put(o, ref i, 1, Tok.Entity); return back; }
                    if (Space(c) || c == '"' || c == '\'' || c == '&') return back;
                    Put(o, ref i, 1, Tok.Entity);
                    return st;
                }
                case CommentS:
                    if (Ahead(t, i, "-->")) { Put(o, ref i, 3, Tok.Comment); return Text; }
                    Put(o, ref i, 1, Tok.Comment);
                    return CommentS;
                case CDataS:
                    if (Ahead(t, i, "]]>")) { Put(o, ref i, 3, Tok.CData); return Text; }
                    Put(o, ref i, 1, Tok.CData);
                    return CDataS;
                case PiS:
                    if (Ahead(t, i, "?>")) { Put(o, ref i, 2, Tok.Pi); return Text; }
                    Put(o, ref i, 1, Tok.Pi);
                    return PiS;
                default:
                    Put(o, ref i, 1, Tok.Pi);
                    return c == '>' ? Text : DoctypeS;
            }
        }

        static void Put(List<TokSpan> o, ref int i, int n, Tok k)
        {
            if (o != null) Add(o, i, n, k);
            i += n;
        }
    }
}

// Stackshot - Two XML documents compared as trees, merged into one where every node is unchanged, added, removed or
// changed. Children are lined up first by whole-branch equality and then by name (plus a key attribute such as id or
// codigo) with longest-common-subsequence passes, so an element inserted in the middle does not shift the ones after it
// into false changes. Whitespace-only differences do not count, names compare by namespace URI (never by prefix) and XML
// escaped inside a field (XmlDoc.ExpandInner) is compared as a tree too. Nothing recurses over the documents.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Stackshot
{
    public enum DiffKind { Same, Added, Removed, Changed }

    public sealed class AttrDiff
    {
        public string Name, Old, New;
        public DiffKind Kind;
        public bool Ns;         // a namespace declaration: shown as it is in the newer document, never compared
    }

    public sealed class DiffNode
    {
        public DiffKind Kind;
        public XItem Old, New;              // the node in each document (null on the side where it is missing)
        public DiffNode Parent;
        readonly List<DiffNode> kids = new List<DiffNode>();
        internal bool Lazy;                 // an unchanged branch whose children are only made when first looked at
        internal bool Unfold = true;        // whether children made later start open
        public List<AttrDiff> Attrs;        // elements with attributes, in the newer document's order (removed ones last)
        public bool TextChanged;            // the text (inline in an element, or of a text or comment) differs
        public string TextOld, TextNew;     // when TextChanged; null where that side has none
        public bool Inside;                 // some difference below this node
        public bool Expanded = true;
        public int Size = 1;                // nodes in this branch, itself included (inside an unchanged branch made later: 1)
        public int Order;                   // position in document order (inside an unchanged branch: the branch's)

        public XItem Item { get { return New ?? Old; } }
        public bool Differs { get { return Kind != DiffKind.Same; } }
        // Where a step through the differences stops: every change, and the top of each added or removed branch.
        public bool Stop { get { return Kind != DiffKind.Same && (Parent == null || Kind == DiffKind.Changed || Parent.Kind != Kind); } }

        // The children, made now for an unchanged branch seen for the first time (both sides are equal: paired in order).
        public List<DiffNode> Kids
        {
            get
            {
                if (!Lazy) return kids;
                Lazy = false;
                if (Old == null || New == null) return kids;
                List<XItem> a = Old.Kids, b = New.Kids;
                int n = Math.Min(a.Count, b.Count);
                kids.Capacity = n;
                for (int i = 0; i < n; i++)
                    kids.Add(new DiffNode { Kind = DiffKind.Same, Old = a[i], New = b[i], Parent = this, Order = Order, Lazy = b[i].Kids.Count > 0, Unfold = Unfold, Expanded = Unfold });
                return kids;
            }
        }

        // The children made so far, without making more.
        internal List<DiffNode> Made { get { return kids; } }
    }

    public sealed class DiffResult
    {
        public readonly List<DiffNode> Top = new List<DiffNode>();
        public readonly List<DiffNode> Stops = new List<DiffNode>();    // in document order
        public int Changed, Added, Removed, Nodes;
        public bool Laid;       // the view has set which nodes start open
        public int Total { get { return Changed + Added + Removed; } }
    }

    public static class XmlDiff
    {
        // Attributes that name an element among its siblings, by preference (unprefixed, any case).
        static readonly string[] KeyNames = { "id", "key", "codigo", "code", "name", "nombre", "clave", "ref", "uuid", "guid", "numero", "nif" };
        const string XmlNs = "http://www.w3.org/XML/1998/namespace";
        const long DpCells = 1L << 20, WeightedCells = 40000;
        const long Side = 1L << 20;   // more than a weighted stretch can hold on one side

        sealed class N
        {
            public XItem X;
            public char Kind;               // E element, T text or CDATA, C comment, P processing instruction
            public string Name;             // E: "{uri}local"; others: their kind
            public long NameHash, IdHash, Hash;
            public bool Keyed;              // E: has a key attribute (IdHash then includes its value)
            public int Size = 1;            // nodes in this branch, itself included
            public string Text;             // T, C, P: the value with whitespace runs collapsed
            public Name[] AttrNames;        // E: per attribute of X, its name by namespace URI (null for a namespace declaration)
            public N[] Kids = None;
            long[] features;

            // What similarity looks at, sorted: the attributes (name and value) and the children's hashes. Only the nodes
            // of a stretch that needs pairing ever work it out.
            public long[] Features
            {
                get
                {
                    if (features != null) return features;
                    List<long> f = new List<long>();
                    if (Kind == 'E')
                    {
                        if (AttrNames != null)
                            for (int i = 0; i < AttrNames.Length && f.Count < 32; i++)
                                if (AttrNames[i] != null) f.Add(Mix(AttrNames[i].Hash, X.Attrs[i].Value));
                        for (int i = 0; i < Kids.Length && i < 64; i++) f.Add(Kids[i].Hash);
                    }
                    else f.Add(Hash);
                    long[] a = f.ToArray();
                    Array.Sort(a);
                    features = a;
                    return a;
                }
            }
        }

        // A name as compared: "{uri}local" (just "local" without a namespace), its hash and, for an attribute that can
        // name its element, its rank among KeyNames (-1 otherwise).
        sealed class Name
        {
            public string Text;
            public long Hash;
            public int Rank = -1;
        }

        // The names of one document. The parser hands out the same string for the same name, so they are looked up by
        // reference: no "{uri}local" built and hashed again for every element.
        sealed class Names
        {
            struct Key
            {
                public string A, B;
            }

            sealed class ByRef : IEqualityComparer<Key>
            {
                public bool Equals(Key x, Key y) { return ReferenceEquals(x.A, y.A) && ReferenceEquals(x.B, y.B); }
                public int GetHashCode(Key k) { return unchecked(RuntimeHelpers.GetHashCode(k.A) * 31 + RuntimeHelpers.GetHashCode(k.B)); }
            }

            readonly Dictionary<Key, Name> map = new Dictionary<Key, Name>(new ByRef());

            public Name Element(XItem x)
            {
                Key k = new Key { A = x.NsUri, B = x.Local };
                Name n;
                if (map.TryGetValue(k, out n)) return n;
                string t = x.NsUri.Length > 0 ? "{" + x.NsUri + "}" + x.Local : x.Local;
                n = new Name { Text = t, Hash = Mix(Seed, t) };
                map[k] = n;
                return n;
            }

            // An attribute by its namespace (null when it has no prefix) and its name as written.
            public Name Attribute(string uri, string written)
            {
                Key k = new Key { A = uri, B = written };
                Name n;
                if (map.TryGetValue(k, out n)) return n;
                string t = uri == null ? written : "{" + uri + "}" + written.Substring(written.IndexOf(':') + 1);
                n = new Name { Text = t, Hash = Mix(Seed, t) };
                int r;
                if (uri == null && KeyRank.TryGetValue(t, out r)) n.Rank = r;
                map[k] = n;
                return n;
            }
        }

        static readonly N[] None = new N[0];
        static readonly Dictionary<string, int> KeyRank = Ranks();

        static Dictionary<string, int> Ranks()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < KeyNames.Length; i++) d[KeyNames[i]] = i;
            return d;
        }

        sealed class Scope
        {
            public readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
            public Scope Up;
        }

        // ---- Hashing (64-bit FNV-1a over text; a multiply and shift to fold in numbers)

        const long Seed = unchecked((long)14695981039346656037UL), Prime = 1099511628211L;
        static readonly long TextName = Mix(Seed, "#text"), CommentName = Mix(Seed, "#comment");

        static long Mix(long h, string s)
        {
            unchecked
            {
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= Prime; }
                h ^= 0xFF; h *= Prime;
                return h;
            }
        }

        static long Mix(long h, long v)
        {
            unchecked
            {
                h ^= v;
                h *= Prime;
                h ^= (long)((ulong)h >> 29);
                h *= 0x5851F42D4C957F2DL;
                return h ^ (long)((ulong)h >> 32);
            }
        }

        // Runs of XML whitespace become one space; nothing at either end.
        static string Collapse(string s)
        {
            if (s == null) return "";
            int start = 0, end = s.Length;
            while (start < end && Ws(s[start])) start++;
            while (end > start && Ws(s[end - 1])) end--;
            // Single spaces between words need no copy (the trimmed end is never blank, so s[i + 1] exists).
            bool plain = true;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c == '\t' || c == '\r' || c == '\n' || (c == ' ' && s[i + 1] == ' ')) { plain = false; break; }
            }
            if (plain) return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
            StringBuilder sb = new StringBuilder(end - start);
            bool gap = false;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (Ws(c)) { gap = true; continue; }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static bool Ws(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

        // ---- The comparable copy of a document

        static N[] Make(List<XItem> items)
        {
            int n = 0;
            foreach (XItem x in items) if (x.Kind != XKind.Decl) n++;
            if (n == 0) return None;
            N[] o = new N[n];
            int k = 0;
            foreach (XItem x in items)
            {
                if (x.Kind == XKind.Decl) continue;
                o[k++] = new N { X = x, Kind = x.Kind == XKind.Element ? 'E' : x.Kind == XKind.Comment ? 'C' : x.Kind == XKind.Pi ? 'P' : 'T' };
            }
            return o;
        }

        static N[] Mirror(XmlModel m, Func<bool> cancelled)
        {
            Names names = new Names();
            N[] roots = Make(m.Top);
            List<N> pre = new List<N>(Math.Max(16, m.Count));
            Stack<KeyValuePair<N, Scope>> st = new Stack<KeyValuePair<N, Scope>>();
            for (int i = roots.Length - 1; i >= 0; i--) st.Push(new KeyValuePair<N, Scope>(roots[i], null));
            while (st.Count > 0)
            {
                if ((pre.Count & 8191) == 0 && cancelled != null && cancelled()) return null;
                KeyValuePair<N, Scope> top = st.Pop();
                N n = top.Key;
                pre.Add(n);
                if (n.Kind != 'E') continue;
                Scope sc = Declare(n.X, top.Value);
                Resolve(n, sc, names);
                n.Kids = Make(n.X.Kids);
                for (int i = n.Kids.Length - 1; i >= 0; i--) st.Push(new KeyValuePair<N, Scope>(n.Kids[i], sc));
            }
            // Descendants come after their ancestor: backwards, every node is finished after its children.
            for (int i = pre.Count - 1; i >= 0; i--)
            {
                if ((i & 8191) == 0 && cancelled != null && cancelled()) return null;
                Finish(pre[i]);
            }
            return roots;
        }

        static Scope Declare(XItem e, Scope up)
        {
            if (e.Attrs == null) return up;
            Scope s = null;
            foreach (KeyValuePair<string, string> a in e.Attrs)
            {
                string p = a.Key == "xmlns" ? "" : a.Key.StartsWith("xmlns:", StringComparison.Ordinal) ? a.Key.Substring(6) : null;
                if (p == null) continue;
                if (s == null) s = new Scope { Up = up };
                s.Map[p] = a.Value;
            }
            return s ?? up;
        }

        static string Uri(string prefix, Scope s)
        {
            if (prefix == "xml") return XmlNs;
            for (; s != null; s = s.Up)
            {
                string u;
                if (s.Map.TryGetValue(prefix, out u)) return u;
            }
            return prefix;  // undeclared (cannot parse anyway): the prefix stands for itself
        }

        static void Resolve(N n, Scope sc, Names names)
        {
            XItem x = n.X;
            Name en = names.Element(x);
            n.Name = en.Text;
            n.NameHash = en.Hash;
            if (x.Attrs == null) return;
            n.AttrNames = new Name[x.Attrs.Count];
            for (int i = 0; i < x.Attrs.Count; i++)
            {
                string k = x.Attrs[i].Key;
                if (k == "xmlns" || k.StartsWith("xmlns:", StringComparison.Ordinal)) continue;
                int colon = k.IndexOf(':');
                n.AttrNames[i] = names.Attribute(colon < 0 ? null : Uri(k.Substring(0, colon), sc), k);
            }
        }

        static void Finish(N n)
        {
            unchecked
            {
                long h = Mix(Seed, (long)n.Kind);
                if (n.Kind == 'E')
                {
                    h = Mix(h, n.NameHash);
                    Name key = null;
                    string keyValue = null;
                    if (n.AttrNames != null)
                    {
                        // Summed, so the order of the attributes never counts.
                        long sum = 0;
                        int count = 0;
                        for (int i = 0; i < n.AttrNames.Length; i++)
                        {
                            Name an = n.AttrNames[i];
                            if (an == null) continue;
                            string v = n.X.Attrs[i].Value;
                            sum += Mix(an.Hash, v);
                            count++;
                            if (an.Rank >= 0 && (key == null || an.Rank < key.Rank)) { key = an; keyValue = v; }
                        }
                        h = Mix(Mix(h, (long)count), sum);
                    }
                    n.Keyed = key != null;
                    n.IdHash = key == null ? n.NameHash : Mix(Mix(n.NameHash, key.Hash), keyValue.Trim());
                    h = Mix(h, (long)n.Kids.Length);
                    foreach (N k in n.Kids) { h = Mix(h, k.Hash); n.Size += k.Size; }
                }
                else
                {
                    n.Text = Collapse(n.Kind == 'P' ? n.X.Name + " " + n.X.Value : n.X.Value);
                    n.Name = n.Kind == 'T' ? "#text" : n.Kind == 'C' ? "#comment" : "?" + n.X.Name;
                    n.NameHash = n.Kind == 'T' ? TextName : n.Kind == 'C' ? CommentName : Mix(Seed, n.Name);
                    n.IdHash = n.NameHash;
                    h = Mix(Mix(h, n.NameHash), n.Text);
                }
                n.Hash = h;
            }
        }

        // ---- Comparing

        struct Work
        {
            public int Type;    // 0 compare a pair, 1 a pair known equal, 2 a branch on one side only
            public DiffNode D;
            public N A, B;
        }

        // The merged tree of old and new; null when cancelled. The two documents are read side by side.
        public static DiffResult Compare(XmlModel older, XmlModel newer, Func<bool> cancelled)
        {
            System.Threading.Tasks.Task<N[]> first = System.Threading.Tasks.Task.Factory.StartNew(delegate { return Mirror(older, cancelled); });
            N[] b = Mirror(newer, cancelled);
            N[] a = first.Result;
            if (a == null || b == null) return null;
            DiffResult r = new DiffResult();
            Stack<Work> work = new Stack<Work>();
            Children(a, b, null, r.Top, work);
            int steps = 0;
            while (work.Count > 0)
            {
                if ((++steps & 4095) == 0 && cancelled != null && cancelled()) return null;
                Work w = work.Pop();
                if (w.Type == 2) { Single(w, work); continue; }
                if (w.Type == 1 && Equal(w)) continue;
                Pair(w.D, w.A, w.B, work);
            }
            Tally(r);
            return r;
        }

        static DiffNode Node(DiffKind k, N a, N b, DiffNode parent)
        {
            return new DiffNode { Kind = k, Old = a != null ? a.X : null, New = b != null ? b.X : null, Parent = parent };
        }

        // A branch present on one side: every node in it added (or removed).
        static void Single(Work w, Stack<Work> work)
        {
            N n = w.A ?? w.B;
            foreach (N k in n.Kids)
            {
                DiffNode d = Node(w.D.Kind, w.A != null ? k : null, w.B != null ? k : null, w.D);
                w.D.Made.Add(d);
                if (k.Kids.Length > 0) work.Push(new Work { Type = 2, D = d, A = w.A != null ? k : null, B = w.B != null ? k : null });
            }
        }

        // Two branches with the same hash: the same node and children (checked one level down: a hash collision there falls
        // back to a real comparison). Nothing below is made until the view looks (DiffNode.Kids), and their attributes
        // are the same, so a line shows the newer document's as they are.
        static bool Equal(Work w)
        {
            N a = w.A, b = w.B;
            if (a.Kids.Length != b.Kids.Length || a.Kind != b.Kind || a.Name != b.Name || a.Text != b.Text) return false;
            for (int i = 0; i < a.Kids.Length; i++) if (a.Kids[i].Hash != b.Kids[i].Hash) return false;
            w.D.Kind = DiffKind.Same;
            w.D.Size = a.Size;
            w.D.Lazy = a.Kids.Length > 0;
            return true;
        }

        // A pair lined up by name: its own differences (attributes, text), then its children.
        static void Pair(DiffNode d, N a, N b, Stack<Work> work)
        {
            d.Made.Clear();
            if (a.Kind != 'E')
            {
                bool same = a.Text == b.Text;
                d.Kind = same ? DiffKind.Same : DiffKind.Changed;
                if (!same) { d.TextChanged = true; d.TextOld = a.X.Value; d.TextNew = b.X.Value; }
                return;
            }
            d.Attrs = Attrs(a, b);
            bool changed = false;
            if (d.Attrs != null) foreach (AttrDiff x in d.Attrs) if (x.Kind != DiffKind.Same) changed = true;
            Children(a.Kids, b.Kids, d, d.Made, work);
            // One piece of text that differs inside an element on both sides: shown on the element's own line.
            if (d.Made.Count == 1 && d.Made[0].Item.Kind != XKind.Element && d.Made[0].Item.Kind != XKind.Comment && d.Made[0].Item.Kind != XKind.Pi && d.Made[0].Differs)
            {
                DiffNode t = d.Made[0];
                d.TextChanged = true;
                d.TextOld = t.Old != null ? t.Old.Value : null;
                d.TextNew = t.New != null ? t.New.Value : null;
                d.Made.Clear();
                changed = true;
            }
            d.Kind = changed ? DiffKind.Changed : DiffKind.Same;
        }

        static List<AttrDiff> Attrs(N a, N b)
        {
            if (a.AttrNames == null && b.AttrNames == null) return null;
            List<AttrDiff> o = new List<AttrDiff>();
            Dictionary<string, int> old = new Dictionary<string, int>(StringComparer.Ordinal);
            if (a.AttrNames != null) for (int i = 0; i < a.AttrNames.Length; i++) if (a.AttrNames[i] != null) old[a.AttrNames[i].Text] = i;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (b.AttrNames != null)
                for (int i = 0; i < b.AttrNames.Length; i++)
                {
                    KeyValuePair<string, string> attr = b.X.Attrs[i];
                    if (b.AttrNames[i] == null) { o.Add(new AttrDiff { Name = attr.Key, New = attr.Value, Old = attr.Value, Ns = true }); continue; }
                    string k = b.AttrNames[i].Text;
                    seen.Add(k);
                    int j;
                    if (!old.TryGetValue(k, out j)) { o.Add(new AttrDiff { Name = attr.Key, New = attr.Value, Kind = DiffKind.Added }); continue; }
                    string ov = a.X.Attrs[j].Value;
                    o.Add(new AttrDiff { Name = attr.Key, Old = ov, New = attr.Value, Kind = ov == attr.Value ? DiffKind.Same : DiffKind.Changed });
                }
            if (a.AttrNames != null)
                for (int i = 0; i < a.AttrNames.Length; i++)
                {
                    if (a.AttrNames[i] == null || seen.Contains(a.AttrNames[i].Text)) continue;
                    o.Add(new AttrDiff { Name = a.X.Attrs[i].Key, Old = a.X.Attrs[i].Value, Kind = DiffKind.Removed });
                }
            return o.Count > 0 ? o : null;
        }

        // Lines up two lists of children: equal branches first (they anchor everything), then, between anchors, the
        // pairs with the same name and key (preferred) or the same name, the most alike together. The rest was added or
        // removed. The merged list keeps document order: what was removed before what was added in each stretch.
        static void Children(N[] a, N[] b, DiffNode parent, List<DiffNode> into, Stack<Work> work)
        {
            long[] ha = new long[a.Length], hb = new long[b.Length], ida = new long[a.Length], idb = new long[b.Length];
            for (int i = 0; i < a.Length; i++) { ha[i] = a[i].Hash; ida[i] = a[i].IdHash; }
            for (int i = 0; i < b.Length; i++) { hb[i] = b[i].Hash; idb[i] = b[i].IdHash; }
            List<int> pa = new List<int>(), pb = new List<int>();
            Lcs(ha, 0, a.Length, hb, 0, b.Length, pa, pb, 0);
            int ia = 0, ib = 0;
            for (int p = 0; p <= pa.Count; p++)
            {
                int ea = p < pa.Count ? pa[p] : a.Length, eb = p < pb.Count ? pb[p] : b.Length;
                Gap(a, ida, ia, ea, b, idb, ib, eb, parent, into, work);
                if (p == pa.Count) break;
                DiffNode d = Node(DiffKind.Same, a[ea], b[eb], parent);
                into.Add(d);
                work.Push(new Work { Type = 1, D = d, A = a[ea], B = b[eb] });
                ia = ea + 1;
                ib = eb + 1;
            }
        }

        static void Gap(N[] a, long[] ida, int a0, int a1, N[] b, long[] idb, int b0, int b1, DiffNode parent, List<DiffNode> into, Stack<Work> work)
        {
            if (a0 == a1 && b0 == b1) return;
            List<int> pa = new List<int>(), pb = new List<int>();
            if (a0 < a1 && b0 < b1)
            {
                if ((long)(a1 - a0) * (b1 - b0) <= WeightedCells) Weighted(a, a0, a1, b, b0, b1, pa, pb);
                else Lcs(ida, a0, a1, idb, b0, b1, pa, pb, 0);
            }
            int ia = a0, ib = b0;
            for (int p = 0; p <= pa.Count; p++)
            {
                int ea = p < pa.Count ? pa[p] : a1, eb = p < pb.Count ? pb[p] : b1;
                for (; ia < ea; ia++) Lone(a[ia], true, parent, into, work);
                for (; ib < eb; ib++) Lone(b[ib], false, parent, into, work);
                if (p == pa.Count) break;
                DiffNode d = Node(DiffKind.Changed, a[ea], b[eb], parent);
                into.Add(d);
                // Leaves are settled here, so the parent can tell at once whether its only text changed.
                if (a[ea].Kind != 'E') Pair(d, a[ea], b[eb], work);
                else work.Push(new Work { Type = 0, D = d, A = a[ea], B = b[eb] });
                ia = ea + 1;
                ib = eb + 1;
            }
        }

        static void Lone(N n, bool removed, DiffNode parent, List<DiffNode> into, Stack<Work> work)
        {
            DiffNode d = Node(removed ? DiffKind.Removed : DiffKind.Added, removed ? n : null, removed ? null : n, parent);
            into.Add(d);
            if (n.Kids.Length > 0) work.Push(new Work { Type = 2, D = d, A = removed ? n : null, B = removed ? null : n });
        }

        // How alike two nodes are, 0 to 1: shared attributes, children and text over the larger of the two.
        static double Similar(N a, N b)
        {
            long[] x = a.Features, y = b.Features;
            int i = 0, j = 0, same = 0;
            while (i < x.Length && j < y.Length)
            {
                if (x[i] == y[j]) { same++; i++; j++; }
                else if (x[i] < y[j]) i++;
                else j++;
            }
            int most = Math.Max(x.Length, y.Length);
            return most == 0 ? 1 : same / (double)most;
        }

        // Best pairing of two short stretches: only same names pair, and two different keys only when that name is there
        // once on each side (an element whose key changed); in a list, an item replaced by another is removed and added,
        // not changed. A matching key weighs as much as ten plain pairs; among equals the most similar win.
        static void Weighted(N[] a, int a0, int a1, N[] b, int b0, int b1, List<int> pa, List<int> pb)
        {
            int n = a1 - a0, m = b1 - b0;
            Dictionary<long, long> once = new Dictionary<long, long>();
            for (int i = a0; i < a1; i++) { long c; once.TryGetValue(a[i].NameHash, out c); once[a[i].NameHash] = c + 1; }
            for (int j = b0; j < b1; j++) { long c; once.TryGetValue(b[j].NameHash, out c); once[b[j].NameHash] = c + Side; }
            double[] s = new double[(n + 1) * (m + 1)];
            int w = m + 1;
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                {
                    double best = Math.Max(s[(i + 1) * w + j], s[i * w + j + 1]);
                    N x = a[a0 + i], y = b[b0 + j];
                    if (Pairs(x, y, once))
                    {
                        double v = (x.IdHash == y.IdHash ? 10 : 1) + Similar(x, y) + s[(i + 1) * w + j + 1];
                        if (v > best) best = v;
                    }
                    s[i * w + j] = best;
                }
            int ci = 0, cj = 0;
            while (ci < n && cj < m)
            {
                N x = a[a0 + ci], y = b[b0 + cj];
                double here = s[ci * w + cj];
                if (Pairs(x, y, once))
                {
                    double v = (x.IdHash == y.IdHash ? 10 : 1) + Similar(x, y) + s[(ci + 1) * w + cj + 1];
                    if (v >= here) { pa.Add(a0 + ci); pb.Add(b0 + cj); ci++; cj++; continue; }
                }
                if (s[(ci + 1) * w + cj] >= s[ci * w + cj + 1]) ci++; else cj++;
            }
        }

        // once: per name, how many times it is in the stretch (old ones as units, new ones as multiples of Side).
        static bool Pairs(N x, N y, Dictionary<long, long> once)
        {
            if (x.NameHash != y.NameHash || x.Kind != y.Kind) return false;
            if (x.IdHash == y.IdHash || !(x.Keyed && y.Keyed)) return true;
            long c;
            return once.TryGetValue(x.NameHash, out c) && c == Side + 1;
        }

        // ---- Longest common subsequence of two key ranges (pairs appended in order)

        static void Lcs(long[] ka, int a0, int a1, long[] kb, int b0, int b1, List<int> pa, List<int> pb, int depth)
        {
            while (a0 < a1 && b0 < b1 && ka[a0] == kb[b0]) { pa.Add(a0++); pb.Add(b0++); }
            int sa = a1, sb = b1;
            while (sa > a0 && sb > b0 && ka[sa - 1] == kb[sb - 1]) { sa--; sb--; }
            Middle(ka, a0, sa, kb, b0, sb, pa, pb, depth);
            for (int i = 0; i < a1 - sa; i++) { pa.Add(sa + i); pb.Add(sb + i); }
        }

        static void Middle(long[] ka, int a0, int a1, long[] kb, int b0, int b1, List<int> pa, List<int> pb, int depth)
        {
            int n = a1 - a0, m = b1 - b0;
            if (n == 0 || m == 0) return;
            if ((long)(n + 1) * (m + 1) <= DpCells) { Table(ka, a0, a1, kb, b0, b1, pa, pb); return; }
            // Patience: keys found exactly once on each side, kept where their order agrees, split the problem.
            Dictionary<long, int> ca = new Dictionary<long, int>(), cb = new Dictionary<long, int>();
            Dictionary<long, int> posB = new Dictionary<long, int>();
            for (int i = a0; i < a1; i++) { int c; ca.TryGetValue(ka[i], out c); ca[ka[i]] = c + 1; }
            for (int j = b0; j < b1; j++) { int c; cb.TryGetValue(kb[j], out c); cb[kb[j]] = c + 1; posB[kb[j]] = j; }
            List<int> ua = new List<int>(), ub = new List<int>();
            for (int i = a0; i < a1; i++)
            {
                int c;
                if (ca[ka[i]] == 1 && cb.TryGetValue(ka[i], out c) && c == 1) { ua.Add(i); ub.Add(posB[ka[i]]); }
            }
            if (ua.Count == 0 || depth > 48) { Greedy(ka, a0, a1, kb, b0, b1, pa, pb); return; }
            List<int> keep = Increasing(ub);
            int ia = a0, ib = b0;
            foreach (int k in keep)
            {
                Lcs(ka, ia, ua[k], kb, ib, ub[k], pa, pb, depth + 1);
                pa.Add(ua[k]);
                pb.Add(ub[k]);
                ia = ua[k] + 1;
                ib = ub[k] + 1;
            }
            Lcs(ka, ia, a1, kb, ib, b1, pa, pb, depth + 1);
        }

        // Classic table for ranges small enough (up to a million cells).
        static void Table(long[] ka, int a0, int a1, long[] kb, int b0, int b1, List<int> pa, List<int> pb)
        {
            int n = a1 - a0, m = b1 - b0, w = m + 1;
            int[] t = new int[(n + 1) * w];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    t[i * w + j] = ka[a0 + i] == kb[b0 + j] ? t[(i + 1) * w + j + 1] + 1 : Math.Max(t[(i + 1) * w + j], t[i * w + j + 1]);
            int ci = 0, cj = 0;
            while (ci < n && cj < m)
            {
                if (ka[a0 + ci] == kb[b0 + cj]) { pa.Add(a0 + ci); pb.Add(b0 + cj); ci++; cj++; }
                else if (t[(ci + 1) * w + cj] >= t[ci * w + cj + 1]) ci++;
                else cj++;
            }
        }

        // Huge ranges without a single unique key: each item takes the next equal one ahead on the other side.
        static void Greedy(long[] ka, int a0, int a1, long[] kb, int b0, int b1, List<int> pa, List<int> pb)
        {
            Dictionary<long, Queue<int>> at = new Dictionary<long, Queue<int>>();
            for (int j = b0; j < b1; j++)
            {
                Queue<int> q;
                if (!at.TryGetValue(kb[j], out q)) { q = new Queue<int>(); at[kb[j]] = q; }
                q.Enqueue(j);
            }
            int last = b0 - 1;
            for (int i = a0; i < a1; i++)
            {
                Queue<int> q;
                if (!at.TryGetValue(ka[i], out q)) continue;
                while (q.Count > 0 && q.Peek() <= last) q.Dequeue();
                if (q.Count == 0) continue;
                last = q.Dequeue();
                pa.Add(i);
                pb.Add(last);
            }
        }

        // Indexes of a longest strictly increasing run of values (patience sorting).
        static List<int> Increasing(List<int> v)
        {
            List<int> tails = new List<int>();
            int[] prev = new int[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                int lo = 0, hi = tails.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (v[tails[mid]] < v[i]) lo = mid + 1; else hi = mid; }
                prev[i] = lo > 0 ? tails[lo - 1] : -1;
                if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
            }
            List<int> o = new List<int>();
            for (int k = tails.Count > 0 ? tails[tails.Count - 1] : -1; k >= 0; k = prev[k]) o.Add(k);
            o.Reverse();
            return o;
        }

        // ---- Totals, document order, what holds a difference

        // Over the nodes made so far: an unchanged branch not opened yet counts its size and holds no difference.
        static void Tally(DiffResult r)
        {
            List<DiffNode> pre = new List<DiffNode>();
            Stack<DiffNode> st = new Stack<DiffNode>();
            for (int i = r.Top.Count - 1; i >= 0; i--) st.Push(r.Top[i]);
            while (st.Count > 0)
            {
                DiffNode d = st.Pop();
                d.Order = pre.Count;
                pre.Add(d);
                List<DiffNode> kids = d.Made;
                for (int i = kids.Count - 1; i >= 0; i--) st.Push(kids[i]);
            }
            for (int i = pre.Count - 1; i >= 0; i--)
            {
                DiffNode d = pre[i];
                if (d.Lazy) { d.Inside = false; continue; }
                int size = 1;
                bool inside = false;
                foreach (DiffNode k in d.Made) { size += k.Size; if (k.Differs || k.Inside) inside = true; }
                d.Size = size;
                d.Inside = inside;
            }
            foreach (DiffNode d in pre)
            {
                if (!d.Stop) continue;
                r.Stops.Add(d);
                if (d.Kind == DiffKind.Changed) r.Changed++;
                else if (d.Kind == DiffKind.Added) r.Added++;
                else r.Removed++;
            }
            foreach (DiffNode t in r.Top) r.Nodes += t.Size;
        }
    }
}

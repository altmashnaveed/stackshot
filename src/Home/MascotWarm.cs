// Stackshot - Looks drawn ahead on a worker, so a view's first frame of a new look doesn't stall the UI thread.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Stackshot
{
    // A look new to a view draws its cached parts (heads, hair, clothes) from scratch on its first frame there: 10 to 40 ms
    // on the UI thread, a hitch while the mouse sweeps the dock or when the desktop pet changes clothes. Asked first, a
    // worker draws that look off screen at the size the view draws it and hands the parts to the UI thread's cache; the
    // view puts it on once they are in, and that first frame costs what the next ones do. The UI thread's side (Ahead,
    // Seen and the callbacks) only ever runs on the UI thread.
    public static class MascotWarm
    {
        sealed class Job
        {
            public string Key;
            public MascotLook Look;
            public float D;
            public Action<Action> Post;
            public readonly List<Action> Done = new List<Action>();
        }

        // Looks the UI thread drew or took in lately, newest last: about what its part cache keeps.
        static readonly List<string> warm = new List<string>();
        const int WarmKept = 16;
        // Waiting to be drawn, newest first; pending also holds the one being drawn.
        static readonly LinkedList<Job> queue = new LinkedList<Job>();
        static readonly Dictionary<string, Job> pending = new Dictionary<string, Job>();
        static bool working;

        static string KeyOf(MascotLook look, float d) { return look.Key + "@" + (int)Math.Round(d * 4); }

        static void Touch(string key)
        {
            warm.Remove(key);
            warm.Add(key);
            if (warm.Count > WarmKept) warm.RemoveAt(0);
        }

        // The UI thread's part cache was emptied: nothing counts as drawn any more.
        public static void Forget() { warm.Clear(); }

        // A view shows look at size d now: from here on its parts are in the UI thread's cache.
        public static void Seen(MascotLook look, float d)
        {
            if (look != null && d >= 4) Touch(KeyOf(look, d));
        }

        // True when look at size d is new to the UI thread: a worker draws it, and done runs on the UI thread (through
        // post) once its parts are in, or once it is given up. False: nothing to wait for, show it now.
        public static bool Ahead(MascotLook look, float d, Action<Action> post, Action done)
        {
            if (look == null || d < 4) return false;
            string key = KeyOf(look, d);
            if (warm.Contains(key)) { Touch(key); return false; }
            List<Job> dropped = new List<Job>();
            lock (queue)
            {
                Job j;
                if (pending.TryGetValue(key, out j))
                {
                    j.Done.Add(done);
                    if (queue.Remove(j)) queue.AddFirst(j);
                    return true;
                }
                j = new Job { Key = key, Look = look.Clone(), D = d, Post = post };
                j.Done.Add(done);
                pending[key] = j;
                queue.AddFirst(j);
                // Only the latest few wait: the mouse has moved on from the others, which are told and simply draw as
                // they always did.
                while (queue.Count > 3)
                {
                    Job old = queue.Last.Value;
                    queue.RemoveLast();
                    pending.Remove(old.Key);
                    dropped.Add(old);
                }
                if (!working)
                {
                    working = true;
                    new System.Threading.Thread(Work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "Mascota: preparar" }.Start();
                }
            }
            foreach (Job old in dropped) foreach (Action a in old.Done) a();
            return true;
        }

        static void Work()
        {
            try
            {
                while (true)
                {
                    Job j;
                    lock (queue)
                    {
                        if (queue.Count == 0) { working = false; return; }
                        j = queue.First.Value;
                        queue.RemoveFirst();
                    }
                    List<KeyValuePair<long, Bitmap>> parts = null;
                    try
                    {
                        MascotLook look = j.Look;
                        float d = j.D;
                        parts = MascotParts.DrawParts(delegate
                        {
                            using (Bitmap b = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                            using (Graphics g = Graphics.FromImage(b)) Mascot.PaintAtRest(g, d, look);
                        });
                    }
                    catch (Exception ex) { ShotStack.Log("Mascota, preparar un look: " + ex.Message); }
                    Action[] done;
                    lock (queue)
                    {
                        pending.Remove(j.Key);
                        done = j.Done.ToArray();
                    }
                    List<KeyValuePair<long, Bitmap>> got = parts;
                    string key = j.Key;
                    try
                    {
                        j.Post(delegate
                        {
                            MascotParts.AdoptParts(got);
                            if (got != null) Touch(key);
                            foreach (Action a in done) a();
                        });
                    }
                    catch (Exception)
                    {
                        // The window is gone: nobody takes them.
                        if (got != null) foreach (KeyValuePair<long, Bitmap> kv in got) kv.Value.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (queue) working = false;
                ShotStack.Log("Mascota, preparar: " + ex.Message);
            }
        }
    }
}

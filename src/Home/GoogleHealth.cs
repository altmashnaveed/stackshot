// Stackshot - A Pixel Watch or Fitbit through the Google Health API (the old Fitbit Web API is switched off): signed in
// once in the browser with Google (OAuth 2.0 with PKCE, no password ever reaches Stackshot), the keys kept encrypted for
// this Windows user, and today's steps, active minutes, sleep and, when Google allows it, water read straight from
// Google to this computer. Only the key exchange goes through the report relay, which adds the app's client secret
// (never in this public code); the health data does not.
// MIT License - https://github.com/rubenitx/stackshot
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Stackshot
{
    public static class GoogleHealth
    {
        public sealed class Today
        {
            public int Steps, StepsGoal, SleepMinutes, ActiveMinutes, WaterMl;
            public DateTime ReadAt;
        }

        const string Api = "https://health.googleapis.com/v4/users/me/dataTypes/";
        const string Scopes = "https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly https://www.googleapis.com/auth/googlehealth.sleep.readonly";
        static readonly object gate = new object();
        static string access, refresh;
        static DateTime expiresUtc;
        static bool loaded;

        static string KeyFile { get { return Path.Combine(Settings.DataDir, "google-health.dat"); } }
        static string TokenUrl { get { return Program.ReportUrl.Substring(0, Program.ReportUrl.LastIndexOf('/')) + "/google/token"; } }

        public static bool Available { get { return !string.IsNullOrEmpty(Program.GoogleClientId) && !string.IsNullOrEmpty(Program.ReportUrl); } }

        public static bool Connected
        {
            get { lock (gate) { Load(); return refresh != null; } }
        }

        // ---- Signing in

        static volatile bool connecting, cancel;
        public static bool Connecting { get { return connecting; } }
        public const string Cancelled = "Has cancelado la conexi\u00F3n.";

        // Stops waiting for the browser (the sign-in tab was closed or left half done).
        public static void Cancel() { if (connecting) cancel = true; }

        // Opens Google's sign-in and waits (on another thread) for the browser to come back; done(error) with null when
        // it worked.
        public static void Connect(Action<string> done)
        {
            if (!Available) { done("Falta configurar la conexi\u00F3n con Google Health."); return; }
            if (connecting) return;
            connecting = true;
            cancel = false;
            Uri back = new Uri(Program.WatchRedirect);
            string verifier = Random(64), state = Random(24);
            string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            TcpListener ear = null;
            try
            {
                // Loopback only: no firewall question, no administrator rights.
                ear = new TcpListener(IPAddress.Loopback, back.Port);
                ear.Start();
                string url = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id=" + Uri.EscapeDataString(Program.GoogleClientId) +
                             "&redirect_uri=" + Uri.EscapeDataString(Program.WatchRedirect) + "&scope=" + Uri.EscapeDataString(Scopes) +
                             "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state + "&access_type=offline&prompt=consent";
                Process.Start(url);
            }
            catch (Exception ex)
            {
                if (ear != null) ear.Stop();
                connecting = false;
                ShotStack.Log("Google Health: " + ex.Message);
                done(ex is SocketException ? "El puerto " + back.Port + " est\u00E1 ocupado: cierra lo que lo use y vuelve a intentarlo." : "No se ha podido abrir el inicio de sesi\u00F3n: " + ex.Message);
                return;
            }
            Thread t = new Thread(delegate()
            {
                string error;
                try { error = Wait(ear, back, state, verifier); }
                catch (Exception ex) { error = "No se ha podido conectar: " + ex.Message; }
                try { ear.Stop(); } catch { }
                connecting = false;
                ShotStack.Log(error == null ? "Google Health: conectado" : "Google Health: " + error);
                done(error);
            });
            t.IsBackground = true;
            t.Start();
        }

        // The browser's request to the redirect address, five minutes at most; then the code is traded for the keys.
        static string Wait(TcpListener ear, Uri back, string state, string verifier)
        {
            DateTime until = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < until)
            {
                if (cancel) return Cancelled;
                if (!ear.Pending()) { Thread.Sleep(150); continue; }
                using (TcpClient c = ear.AcceptTcpClient())
                using (NetworkStream s = c.GetStream())
                {
                    s.ReadTimeout = 5000;
                    string line = new StreamReader(s, Encoding.ASCII).ReadLine() ?? "";
                    string[] parts = line.Split(' ');
                    string target = parts.Length > 1 ? parts[1] : "";
                    if (!target.StartsWith(back.AbsolutePath, StringComparison.Ordinal)) { Answer(s, 404, "No encontrado."); continue; }
                    Dictionary<string, string> q = Query(target);
                    string got;
                    if (q.TryGetValue("error", out got)) { Answer(s, 200, "Has cancelado la conexi\u00F3n. Puedes cerrar esta pesta\u00F1a."); return got == "access_denied" ? Cancelled :"Google ha dicho: " + got; }
                    string code, st;
                    if (!q.TryGetValue("code", out code) || !q.TryGetValue("state", out st) || st != state) { Answer(s, 400, "Respuesta no v\u00E1lida."); continue; }
                    string error = Token("grant_type=authorization_code&code=" + Uri.EscapeDataString(code) + "&code_verifier=" + verifier +
                                         "&redirect_uri=" + Uri.EscapeDataString(Program.WatchRedirect));
                    Answer(s, 200, error == null ? "\u00A1Listo! Tu reloj ya est\u00E1 conectado con Stackshot. Puedes cerrar esta pesta\u00F1a." : "No se ha podido terminar la conexi\u00F3n. Vuelve a Stackshot.");
                    return error;
                }
            }
            return "Se acab\u00F3 el tiempo para iniciar sesi\u00F3n. Vuelve a intentarlo.";
        }

        static void Answer(NetworkStream s, int code, string text)
        {
            string html = "<!doctype html><meta charset=utf-8><title>Stackshot</title><body style=\"font:16px Segoe UI,sans-serif;display:grid;place-items:center;height:90vh;color:#222\"><p>" +
                          WebUtility.HtmlEncode(text) + "</p></body>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            s.Write(body, 0, body.Length);
        }

        static Dictionary<string, string> Query(string target)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            int q = target.IndexOf('?');
            if (q < 0) return d;
            foreach (string kv in target.Substring(q + 1).Split('&'))
            {
                int e = kv.IndexOf('=');
                if (e > 0) d[Uri.UnescapeDataString(kv.Substring(0, e))] = Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
            }
            return d;
        }

        // ---- Keys: traded and refreshed through the relay (which adds the secret), kept encrypted for this Windows user.

        static string Token(string form)
        {
            Dictionary<string, object> r;
            try { r = Send("POST", TokenUrl, "application/x-www-form-urlencoded", form, null) as Dictionary<string, object>; }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                int code = res != null ? (int)res.StatusCode : 0;
                if (res != null) res.Close();
                // A refresh key that no longer works (revoked, or the week of a project still in testing): signed out.
                if (code == 400 || code == 401) { Forget(); return "Google ya no acepta la conexi\u00F3n: vuelve a conectar el reloj."; }
                if (code == 503) return "Falta la clave de Google en el intermediario.";
                return code != 0 ? "Google no responde (" + code + ")." : "No hay conexi\u00F3n con Google.";
            }
            if (r == null || !r.ContainsKey("access_token")) return "Google no ha dado las claves.";
            lock (gate)
            {
                access = r["access_token"] as string;
                if (r.ContainsKey("refresh_token")) refresh = r["refresh_token"] as string;
                int secs = r.ContainsKey("expires_in") ? Convert.ToInt32(r["expires_in"]) : 3600;
                expiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, secs - 120));
                if (refresh == null) return "Google no ha dado una clave duradera: vuelve a conectar.";
                Save();
            }
            return null;
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(KeyFile)) return;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), Entropy, DataProtectionScope.CurrentUser);
                string[] p = Encoding.UTF8.GetString(plain).Split('\n');
                if (p.Length < 3) return;
                access = p[0]; refresh = p[1];
                expiresUtc = new DateTime(long.Parse(p[2]), DateTimeKind.Utc);
            }
            catch (Exception ex) { ShotStack.Log("Google Health: claves ilegibles (" + ex.Message + ")"); access = refresh = null; }
        }

        static void Save()
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(access + "\n" + refresh + "\n" + expiresUtc.Ticks);
                Directory.CreateDirectory(Settings.DataDir);
                File.WriteAllBytes(KeyFile, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) { ShotStack.Log("Google Health: no se han podido guardar las claves (" + ex.Message + ")"); }
        }

        static readonly byte[] Entropy = Encoding.ASCII.GetBytes("Stackshot.GoogleHealth.v1");

        static void Forget()
        {
            lock (gate)
            {
                access = refresh = null;
                try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch { }
            }
        }

        // Signed out here and at Google.
        public static void Disconnect()
        {
            string token;
            lock (gate) { Load(); token = refresh; }
            Forget();
            if (token == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Send("POST", "https://oauth2.googleapis.com/revoke", "application/x-www-form-urlencoded", "token=" + Uri.EscapeDataString(token), null); }
                catch (Exception ex) { ShotStack.Log("Google Health: revocar: " + ex.Message); }
            });
        }

        // ---- Today's numbers (blocking: call off the UI thread)

        public static Today Read(out string error)
        {
            error = null;
            string key;
            lock (gate)
            {
                Load();
                if (refresh == null) { error = "Sin conectar."; return null; }
                key = access;
            }
            if (key == null || DateTime.UtcNow >= expiresUtc)
            {
                error = Token("grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refresh));
                if (error != null) return null;
                lock (gate) key = access;
            }
            Today t = new Today { ReadAt = DateTime.Now, StepsGoal = 10000 };
            try
            {
                // Steps first: if this fails, nothing else will work either.
                Dictionary<string, object> steps = Rollup(key, "steps");
                t.Steps = Long(Dict(steps, "steps"), "countSum");
            }
            catch (WebException ex) { error = Explain(ex); return null; }
            // The rest is best effort: a data type the watch doesn't have, or that Google doesn't let read yet, is left at 0.
            t.ActiveMinutes = Try(delegate
            {
                int sum = 0;
                IList levels = List(Dict(Rollup(key, "active-minutes"), "activeMinutes"), "activeMinutesRollupByActivityLevel");
                if (levels != null)
                    foreach (object o in levels)
                    {
                        Dictionary<string, object> l = o as Dictionary<string, object>;
                        string level = l != null && l.ContainsKey("activityLevel") ? l["activityLevel"] as string : "";
                        if (level == "MODERATE" || level == "VIGOROUS") sum += Long(l, "activeMinutesSum");
                    }
                return sum;
            }, "minutos activos");
            t.SleepMinutes = Try(delegate { return LastNight(key); }, "sue\u00F1o");
            t.WaterMl = Try(delegate
            {
                Dictionary<string, object> h = Dict(Dict(Rollup(key, "hydration-log"), "hydrationLog"), "amountConsumed");
                object ml;
                return h != null && h.TryGetValue("millilitersSum", out ml) && ml != null ? (int)Math.Round(Convert.ToDouble(ml, System.Globalization.CultureInfo.InvariantCulture)) : 0;
            }, "agua");
            return t;
        }

        static readonly HashSet<string> missing = new HashSet<string>();

        static int Try(Func<int> read, string what)
        {
            try { return read(); }
            catch (Exception ex)
            {
                lock (missing) if (missing.Add(what)) ShotStack.Log("Google Health: sin " + what + " (" + ex.Message + ")");
                return 0;
            }
        }

        static string Explain(WebException ex)
        {
            HttpWebResponse res = ex.Response as HttpWebResponse;
            int code = res != null ? (int)res.StatusCode : 0;
            if (res != null) res.Close();
            if (code == 401) lock (gate) expiresUtc = DateTime.MinValue;   // refreshed at the next reading
            if (code == 403) return "Google no deja leer los datos: revisa que el reloj est\u00E9 vinculado en la app Google Health.";
            return code == 429 ? "Google pide esperar un poco." : code != 0 ? "Google no responde (" + code + ")." : "No hay conexi\u00F3n con Google.";
        }

        // Today's total of one data type, from midnight to midnight in local time.
        static Dictionary<string, object> Rollup(string key, string type)
        {
            DateTime d = DateTime.Today, n = d.AddDays(1);
            string body = "{\"range\":{\"start\":{\"date\":{\"year\":" + d.Year + ",\"month\":" + d.Month + ",\"day\":" + d.Day + "},\"time\":{\"hours\":0}}," +
                          "\"end\":{\"date\":{\"year\":" + n.Year + ",\"month\":" + n.Month + ",\"day\":" + n.Day + "},\"time\":{\"hours\":0}}},\"windowSizeDays\":1}";
            Dictionary<string, object> r = Send("POST", Api + type + "/dataPoints:dailyRollUp", "application/json", body, key) as Dictionary<string, object>;
            IList points = List(r, "rollupDataPoints");
            return points != null && points.Count > 0 ? points[points.Count - 1] as Dictionary<string, object> : null;
        }

        // Minutes asleep in the sleep that ended today (the main one; or all of them if none is marked main).
        static int LastNight(string key)
        {
            string filter = "sleep.interval.civil_end_time >= \"" + DateTime.Today.ToString("yyyy-MM-dd") + "\"";
            Dictionary<string, object> r = Send("GET", Api + "sleep/dataPoints?pageSize=25&filter=" + Uri.EscapeDataString(filter), null, null, key) as Dictionary<string, object>;
            IList points = List(r, "dataPoints");
            int main = 0, all = 0;
            if (points != null)
                foreach (object o in points)
                {
                    Dictionary<string, object> sleep = Dict(o as Dictionary<string, object>, "sleep");
                    int m = Long(Dict(sleep, "summary"), "minutesAsleep");
                    all += m;
                    Dictionary<string, object> meta = Dict(sleep, "metadata");
                    if (meta != null && meta.ContainsKey("mainSleep") && meta["mainSleep"] is bool && (bool)meta["mainSleep"]) main += m;
                }
            return main > 0 ? main : all;
        }

        static object Send(string method, string url, string type, string body, string key)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = "Stackshot/" + Installer.MyVersion.ToString(3);
            if (key != null) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 20000;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
            if (body != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentType = type;
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
            {
                string text = rd.ReadToEnd();
                return text.Length == 0 ? null : new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(text);
            }
        }

        static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        // JSON lists come as object[] (IList covers it).
        static IList List(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as IList : null;
        }

        // Google sends 64-bit numbers as strings.
        static int Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return (int)Math.Min(int.MaxValue, Math.Round(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture))); }
            catch { return 0; }
        }

        static string Random(int n)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            byte[] b = new byte[n];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            StringBuilder s = new StringBuilder(n);
            foreach (byte x in b) s.Append(chars[x % chars.Length]);
            return s.ToString();
        }

        static string Base64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    }
}

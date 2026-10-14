// Stackshot - The Pixel Watch or Fitbit of whoever wants it: signed in once in the browser (OAuth 2.0 with PKCE, no
// password ever reaches Stackshot), the keys kept encrypted for this Windows user, and today's steps, sleep, active
// minutes and water read straight from Fitbit to this computer. Nothing goes anywhere else.
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
    public static class Fitbit
    {
        public sealed class Today
        {
            public int Steps, StepsGoal, SleepMinutes, ActiveMinutes, WaterMl;
            public DateTime ReadAt;
        }

        // Where the browser comes back after signing in (registered with the app at dev.fitbit.com, exactly).
        const string Scopes = "activity sleep nutrition";
        static readonly object gate = new object();
        static string access, refresh, user;
        static DateTime expiresUtc;
        static bool loaded;

        static string KeyFile { get { return Path.Combine(Settings.DataDir, "fitbit.dat"); } }

        public static bool Available { get { return !string.IsNullOrEmpty(Program.FitbitClientId); } }

        public static bool Connected
        {
            get { lock (gate) { Load(); return refresh != null; } }
        }

        // ---- Signing in

        static volatile bool connecting;
        public static bool Connecting { get { return connecting; } }

        // Opens the sign-in page and waits (on another thread) for the browser to come back; done(error) runs on the
        // caller's side with null when it worked.
        public static void Connect(Action<string> done)
        {
            if (!Available) { done("Falta dar de alta Stackshot en Fitbit."); return; }
            if (connecting) return;
            connecting = true;
            Uri back = new Uri(Program.FitbitRedirect);
            string verifier = Random(64), state = Random(24);
            string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            List<TcpListener> ears = new List<TcpListener>();
            try
            {
                // Loopback only: no firewall question, no administrator rights.
                foreach (IPAddress a in new IPAddress[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
                {
                    try { TcpListener l = new TcpListener(a, back.Port); l.Start(); ears.Add(l); }
                    catch (SocketException) { }
                }
                if (ears.Count == 0) throw new IOException("el puerto " + back.Port + " est\u00E1 ocupado");
                string url = "https://www.fitbit.com/oauth2/authorize?response_type=code&client_id=" + Uri.EscapeDataString(Program.FitbitClientId) +
                             "&redirect_uri=" + Uri.EscapeDataString(Program.FitbitRedirect) + "&scope=" + Uri.EscapeDataString(Scopes) +
                             "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state + "&prompt=consent";
                Process.Start(url);
            }
            catch (Exception ex)
            {
                foreach (TcpListener l in ears) l.Stop();
                connecting = false;
                ShotStack.Log("Fitbit: " + ex.Message);
                done("No se ha podido abrir el inicio de sesi\u00F3n: " + ex.Message);
                return;
            }
            Thread t = new Thread(delegate()
            {
                string error;
                try { error = Wait(ears, back, state, verifier); }
                catch (Exception ex) { error = "No se ha podido conectar: " + ex.Message; }
                foreach (TcpListener l in ears) { try { l.Stop(); } catch { } }
                connecting = false;
                if (error != null) ShotStack.Log("Fitbit: " + error);
                else ShotStack.Log("Fitbit: conectado");
                done(error);
            });
            t.IsBackground = true;
            t.Start();
        }

        // The browser's request to the redirect address, five minutes at most; then the code is traded for the keys.
        static string Wait(List<TcpListener> ears, Uri back, string state, string verifier)
        {
            DateTime until = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < until)
            {
                TcpListener ready = null;
                foreach (TcpListener l in ears) if (l.Pending()) { ready = l; break; }
                if (ready == null) { Thread.Sleep(150); continue; }
                using (TcpClient c = ready.AcceptTcpClient())
                using (NetworkStream s = c.GetStream())
                {
                    s.ReadTimeout = 5000;
                    string line = new StreamReader(s, Encoding.ASCII).ReadLine() ?? "";
                    // "GET /fitbit?code=...&state=... HTTP/1.1"
                    string[] parts = line.Split(' ');
                    string target = parts.Length > 1 ? parts[1] : "";
                    if (!target.StartsWith(back.AbsolutePath, StringComparison.Ordinal)) { Answer(s, 404, "No encontrado."); continue; }
                    Dictionary<string, string> q = Query(target);
                    string got;
                    if (q.TryGetValue("error", out got)) { Answer(s, 200, "Has cancelado la conexi\u00F3n. Puedes cerrar esta pesta\u00F1a."); return got == "access_denied" ? "Has cancelado la conexi\u00F3n." : "Fitbit ha dicho: " + got; }
                    string code, st;
                    if (!q.TryGetValue("code", out code) || !q.TryGetValue("state", out st) || st != state) { Answer(s, 400, "Respuesta no v\u00E1lida."); continue; }
                    string error = Token("grant_type=authorization_code&code=" + Uri.EscapeDataString(code) + "&code_verifier=" + verifier +
                                         "&redirect_uri=" + Uri.EscapeDataString(Program.FitbitRedirect));
                    Answer(s, 200, error == null ? "\u00A1Listo! Tu reloj ya est\u00E1 conectado con Stackshot. Puedes cerrar esta pesta\u00F1a." : "No se ha podido terminar la conexi\u00F3n. Vuelve a Stackshot.");
                    return error;
                }
            }
            return "Se acab\u00F3 el tiempo para iniciar sesi\u00F3n. Vuelve a intentarlo.";
        }

        static void Answer(NetworkStream s, int code, string text)
        {
            string html = "<!doctype html><meta charset=utf-8><title>Stackshot</title><body style=\"font:16px Segoe UI,sans-serif;display:grid;place-items:center;height:90vh;color:#222\"><p>" +
                          System.Net.WebUtility.HtmlEncode(text) + "</p></body>";
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

        // ---- Keys: traded, refreshed (each refresh key works once) and kept encrypted for this Windows user.

        static string Token(string form)
        {
            Dictionary<string, object> r;
            try { r = Post("https://api.fitbit.com/oauth2/token", form + "&client_id=" + Uri.EscapeDataString(Program.FitbitClientId)) as Dictionary<string, object>; }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                int code = res != null ? (int)res.StatusCode : 0;
                if (res != null) res.Close();
                // A refresh key that no longer works (revoked, expired): signed out.
                if (code == 400 || code == 401) { Forget(); return "Fitbit ya no acepta la conexi\u00F3n: vuelve a conectar el reloj."; }
                return code != 0 ? "Fitbit no responde (" + code + ")." : "No hay conexi\u00F3n con Fitbit.";
            }
            if (r == null || !r.ContainsKey("access_token") || !r.ContainsKey("refresh_token")) return "Fitbit no ha dado las claves.";
            lock (gate)
            {
                access = r["access_token"] as string;
                refresh = r["refresh_token"] as string;
                user = r.ContainsKey("user_id") ? r["user_id"] as string : user;
                int secs = r.ContainsKey("expires_in") ? Convert.ToInt32(r["expires_in"]) : 3600;
                expiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, secs - 120));
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
                if (p.Length < 4) return;
                access = p[0]; refresh = p[1]; user = p[2];
                expiresUtc = new DateTime(long.Parse(p[3]), DateTimeKind.Utc);
            }
            catch (Exception ex) { ShotStack.Log("Fitbit: claves ilegibles (" + ex.Message + ")"); access = refresh = null; }
        }

        static void Save()
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(access + "\n" + refresh + "\n" + user + "\n" + expiresUtc.Ticks);
                Directory.CreateDirectory(Settings.DataDir);
                File.WriteAllBytes(KeyFile, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) { ShotStack.Log("Fitbit: no se han podido guardar las claves (" + ex.Message + ")"); }
        }

        static readonly byte[] Entropy = Encoding.ASCII.GetBytes("Stackshot.Fitbit.v1");

        static void Forget()
        {
            lock (gate)
            {
                access = refresh = user = null;
                try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch { }
            }
        }

        // Signed out here and, as far as Fitbit lets, there too.
        public static void Disconnect()
        {
            string token;
            lock (gate) { Load(); token = refresh; }
            Forget();
            if (token == null || !Available) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Post("https://api.fitbit.com/oauth2/revoke", "token=" + Uri.EscapeDataString(token) + "&client_id=" + Uri.EscapeDataString(Program.FitbitClientId)); }
                catch (Exception ex) { ShotStack.Log("Fitbit: revocar: " + ex.Message); }
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
            Today t = new Today { ReadAt = DateTime.Now };
            try
            {
                Dictionary<string, object> act = Get("https://api.fitbit.com/1/user/-/activities/date/today.json", key);
                Dictionary<string, object> sum = Dict(act, "summary"), goals = Dict(act, "goals");
                t.Steps = Int(sum, "steps");
                t.StepsGoal = Int(goals, "steps");
                t.ActiveMinutes = Int(sum, "fairlyActiveMinutes") + Int(sum, "veryActiveMinutes");
                t.SleepMinutes = Int(Dict(Get("https://api.fitbit.com/1.2/user/-/sleep/date/today.json", key), "summary"), "totalMinutesAsleep");
                t.WaterMl = Int(Dict(Get("https://api.fitbit.com/1/user/-/foods/log/water/date/today.json", key), "summary"), "water");
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                int code = res != null ? (int)res.StatusCode : 0;
                if (res != null) res.Close();
                // An access key that stopped working before its time: refreshed at the next reading.
                if (code == 401) lock (gate) expiresUtc = DateTime.MinValue;
                error = code == 429 ? "Fitbit pide esperar un poco." : code != 0 ? "Fitbit no responde (" + code + ")." : "No hay conexi\u00F3n con Fitbit.";
                return null;
            }
            return t;
        }

        static Dictionary<string, object> Get(string url, string key)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Stackshot/" + Installer.MyVersion.ToString(3);
            req.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            // Metric units: water in millilitres.
            req.Headers[HttpRequestHeader.AcceptLanguage] = "es_ES";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 20000;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return new JavaScriptSerializer().DeserializeObject(rd.ReadToEnd()) as Dictionary<string, object>;
        }

        static object Post(string url, string form)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.UserAgent = "Stackshot/" + Installer.MyVersion.ToString(3);
            req.ContentType = "application/x-www-form-urlencoded";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 20000;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
            byte[] data = Encoding.ASCII.GetBytes(form);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
            {
                string body = rd.ReadToEnd();
                return body.Length == 0 ? null : new JavaScriptSerializer().DeserializeObject(body);
            }
        }

        static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static int Int(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(Math.Round(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture))); }
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

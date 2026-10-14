// Stackshot report relay: receives the in-app "Informar de un problema" form and opens a GitHub issue.
// The GitHub token lives only here (a Cloudflare secret), never in the app. MIT License.
//
// POST /report  JSON { kind: "bug" | "idea", title, what, steps, version, windows, screens, log }
//   -> 201 { ok: true, number, url }   or   4xx/5xx { ok: false, error }
// GET /status?n=12,15  (the app's "Mis reportes": no GitHub account needed to follow a report)
//   -> 200 { ok: true, issues: [{ number, state, reason, comments: [{ team, at, text }] }] }
// POST /google/token  form { grant_type: "authorization_code", code, code_verifier, redirect_uri } or
//   { grant_type: "refresh_token", refresh_token }  -> Google's own answer. Signing in to Google Health (watch data):
//   the client secret stays here (GOOGLE_CLIENT_SECRET); the health data never passes through, only the keys.

const LIMITS = { title: 120, what: 6000, steps: 4000, version: 32, windows: 64, screens: 64, log: 24000 };
// Fields cut from the start keep their end instead: the newest log lines are where the problem is.
const KEEP_END = { log: true };

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/status") return status(request, env, url);
    if (url.pathname === "/google/token") return googleToken(request, env);
    if (url.pathname !== "/report") return json(404, { ok: false, error: "not found" });
    if (request.method !== "POST") return json(405, { ok: false, error: "method" });
    if (!/^Stackshot\//.test(request.headers.get("User-Agent") || "")) return json(403, { ok: false, error: "client" });

    // A few reports a minute per address at most (Cloudflare's rate limiting binding).
    if (env.LIMITER) {
      const ip = request.headers.get("CF-Connecting-IP") || "unknown";
      const { success } = await env.LIMITER.limit({ key: ip });
      if (!success) return json(429, { ok: false, error: "too many" });
    }

    // JSON escaping (\uXXXX for accents, doubled backslashes in paths) can triple the text; the fields are cut below.
    const raw = await request.text();
    if (raw.length > 150000) return json(413, { ok: false, error: "too large" });
    let r;
    try { r = JSON.parse(raw); } catch { return json(400, { ok: false, error: "json" }); }
    const f = {};
    for (const k of Object.keys(LIMITS)) f[k] = clean(r[k], LIMITS[k], KEEP_END[k]);
    const kind = r.kind === "idea" ? "idea" : "bug";
    if (f.title.length < 4 || f.what.length < 10) return json(400, { ok: false, error: "empty" });

    // The computer as one tight list ("screens" already says how many: "1 pantalla: 1920\u00D71080 al 100 %").
    const machine = ["- Stackshot " + (f.version || "?"), "- Windows " + (f.windows || "?"), f.screens ? "- " + f.screens : ""]
      .filter(Boolean).join("\n");
    const body = [
      "### " + (kind === "idea" ? "Idea" : "\u00BFQu\u00E9 ha pasado?"),
      quote(f.what),
      f.steps ? "### \u00BFC\u00F3mo se repite?\n" + quote(f.steps) : "",
      "### Equipo\n" + machine,
      f.log ? "<details><summary>Registro de Stackshot</summary>\n\n" + fence(f.log) + "\n</details>" : "",
      "_Enviado desde la app._"
    ].filter(Boolean).join("\n\n");

    const labels = ["desde la app", kind === "idea" ? "enhancement" : "bug"];
    let res = await createIssue(env, f.title, body, labels);
    // Labels the token may not create: try once more without them.
    if (res.status === 422) res = await createIssue(env, f.title, body, []);
    if (!res.ok) return json(502, { ok: false, error: "github " + res.status });
    const issue = await res.json();
    return json(201, { ok: true, number: issue.number, url: issue.html_url });
  }
};

// How the reports sent from one computer are doing: their state and the replies, as plain text. Only issues that came
// from the app, at most 20 per request (each costs one or two calls to GitHub, within a Worker's 50).
async function status(request, env, url) {
  if (request.method !== "GET") return json(405, { ok: false, error: "method" });
  if (!/^Stackshot\//.test(request.headers.get("User-Agent") || "")) return json(403, { ok: false, error: "client" });
  const ns = [...new Set((url.searchParams.get("n") || "").split(",").map(s => parseInt(s, 10)).filter(n => n > 0))].slice(0, 20);
  if (!ns.length) return json(400, { ok: false, error: "empty" });
  const issues = [];
  for (const n of ns) {
    const res = await github(env, "/issues/" + n);
    if (res.status === 404 || res.status === 410) continue;
    if (!res.ok) return json(502, { ok: false, error: "github " + res.status });
    const i = await res.json();
    if (i.pull_request || !(i.labels || []).some(l => l.name === "desde la app")) continue;
    let comments = [];
    if (i.comments > 0) {
      const c = await github(env, "/issues/" + n + "/comments?per_page=50");
      if (c.ok) comments = (await c.json()).map(x => ({
        team: ["OWNER", "MEMBER", "COLLABORATOR"].includes(x.author_association),
        at: x.created_at,
        text: (x.body || "").slice(0, 4000)
      }));
    }
    issues.push({ number: i.number, state: i.state, reason: i.state_reason || "", comments });
  }
  return json(200, { ok: true, issues });
}

// Google's token exchange with the client secret added. Only the app's own loopback redirect is accepted, and only the
// two grants it uses; nothing is logged or kept.
async function googleToken(request, env) {
  if (request.method !== "POST") return json(405, { ok: false, error: "method" });
  if (!/^Stackshot\//.test(request.headers.get("User-Agent") || "")) return json(403, { ok: false, error: "client" });
  if (!env.GOOGLE_CLIENT_ID || !env.GOOGLE_CLIENT_SECRET) return json(503, { ok: false, error: "not configured" });
  const form = new URLSearchParams(await request.text());
  const grant = form.get("grant_type");
  const out = new URLSearchParams({ client_id: env.GOOGLE_CLIENT_ID, client_secret: env.GOOGLE_CLIENT_SECRET, grant_type: grant || "" });
  if (grant === "authorization_code") {
    const redirect = form.get("redirect_uri") || "";
    if (!/^http:\/\/127\.0\.0\.1:\d+\/google$/.test(redirect)) return json(400, { ok: false, error: "redirect" });
    out.set("code", form.get("code") || "");
    out.set("code_verifier", form.get("code_verifier") || "");
    out.set("redirect_uri", redirect);
  } else if (grant === "refresh_token") {
    out.set("refresh_token", form.get("refresh_token") || "");
  } else return json(400, { ok: false, error: "grant" });
  const res = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: out.toString()
  });
  return new Response(await res.text(), { status: res.status, headers: { "Content-Type": "application/json; charset=utf-8" } });
}

function github(env, path, init) {
  return fetch("https://api.github.com/repos/" + env.REPO + path, Object.assign({
    headers: {
      "Authorization": "Bearer " + env.GITHUB_TOKEN,
      "Accept": "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "stackshot-report-relay",
      "Content-Type": "application/json"
    }
  }, init || {}));
}

function createIssue(env, title, body, labels) {
  return github(env, "/issues", { method: "POST", body: JSON.stringify({ title: "[App] " + title, body, labels }) });
}

// Plain text only: control characters out; @mentions, #123 / owner/repo#123 / GH-123 references and links to GitHub
// defused with a zero-width space, so a report can't ping anyone or leave a cross-reference on another project's issues.
function clean(v, max, keepEnd) {
  if (typeof v !== "string") return "";
  const s = v.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "")
             .replace(/@(?=\w)/g, "@\u200B")
             .replace(/#(?=\d)/g, "#\u200B")
             .replace(/\bGH-(?=\d)/gi, "GH\u200B-")
             .replace(/github\.com/gi, "github\u200B.com")
             .trim();
  return s.length <= max ? s : keepEnd ? s.slice(-max) : s.slice(0, max);
}

function quote(s) { return s.split("\n").map(l => "> " + l).join("\n"); }

// A code fence longer than any run of backticks or tildes inside.
function fence(s) {
  const longest = Math.max(2, ...(s.match(/~+/g) || []).map(m => m.length));
  const f = "~".repeat(longest + 1);
  return f + "text\n" + s + "\n" + f;
}

function json(status, obj) {
  return new Response(JSON.stringify(obj), { status, headers: { "Content-Type": "application/json; charset=utf-8" } });
}

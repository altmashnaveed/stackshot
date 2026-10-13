// Stackshot report relay: receives the in-app "Informar de un problema" form and opens a GitHub issue.
// The GitHub token lives only here (a Cloudflare secret), never in the app. MIT License.
//
// POST /report  JSON { kind: "bug" | "idea", title, what, steps, version, windows, screens, log }
//   -> 201 { ok: true, number, url }   or   4xx/5xx { ok: false, error }

const LIMITS = { title: 120, what: 6000, steps: 4000, version: 32, windows: 64, screens: 64, log: 24000 };
// Fields cut from the start keep their end instead: the newest log lines are where the problem is.
const KEEP_END = { log: true };

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
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

    const body = [
      "### " + (kind === "idea" ? "Idea" : "¿Qué ha pasado?"),
      quote(f.what),
      f.steps ? "### ¿Cómo se repite?\n" + quote(f.steps) : "",
      "### Equipo",
      "- Stackshot " + (f.version || "?"),
      "- Windows " + (f.windows || "?"),
      f.screens ? "- Pantallas: " + f.screens : "",
      f.log ? "<details><summary>Registro de Stackshot</summary>\n\n" + fence(f.log) + "\n</details>" : "",
      "",
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

function createIssue(env, title, body, labels) {
  return fetch("https://api.github.com/repos/" + env.REPO + "/issues", {
    method: "POST",
    headers: {
      "Authorization": "Bearer " + env.GITHUB_TOKEN,
      "Accept": "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "stackshot-report-relay",
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ title: "[App] " + title, body, labels })
  });
}

// Plain text only: control characters out; @mentions, #123 / owner/repo#123 / GH-123 references and links to GitHub
// defused, so a report can't ping anyone or leave a cross-reference on another project's issues.
function clean(v, max, keepEnd) {
  if (typeof v !== "string") return "";
  const s = v.replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "")
             .replace(/@(?=\w)/g, "@​")
             .replace(/#(?=\d)/g, "#​")
             .replace(/\bGH-(?=\d)/gi, "GH​-")
             .replace(/github\.com/gi, "github​.com")
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

# Report relay

The in-app form (Inicio > Acerca de > *Informar de un problema*) sends reports here, and this
[Cloudflare Worker](https://developers.cloudflare.com/workers/) opens a GitHub issue with them. The GitHub token is a
Cloudflare secret: it is never in the app or in this repository. People who send a report need no account.

## Deploy (once)

1. A free Cloudflare account.
2. A **fine-grained** GitHub token: *Settings > Developer settings > Fine-grained tokens*, only the `rubenitx/stackshot`
   repository, permission **Issues: Read and write**, nothing else.
3. From this folder:

   ```sh
   npx wrangler login
   npx wrangler secret put GITHUB_TOKEN     # paste the token
   npx wrangler deploy
   ```

4. Wrangler prints the address, e.g. `https://stackshot-report.<your-subdomain>.workers.dev`. Put it, with `/report`
   at the end, in `Program.ReportUrl` (`src/Program.cs`) and build the app.

Until `Program.ReportUrl` is set, the form opens a new GitHub issue in the browser with everything filled in.

## What it accepts

`POST /report` with a JSON body (`kind`, `title`, `what`, `steps`, `version`, `windows`, `screens`, `log`), at most
150 KB, from a client whose User-Agent starts with `Stackshot/`. Three reports a minute per address. Text is cut to
fixed lengths (the log keeps its end), control characters are removed and `@mentions`, issue references (`#123`, `owner/repo#123`) and GitHub links are defused, so a report can't ping anyone or touch other projects. Issues get the labels `desde la app` and
`bug` or `enhancement`.

## Revoking

Delete the token on GitHub (or `npx wrangler secret delete GITHUB_TOKEN`); the form then shows an error and offers to
open the issue in the browser instead.

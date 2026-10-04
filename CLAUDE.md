# CLAUDE.md

Working rules for Claude Code sessions in this repo. Keep this file short; details live in `docs/`.

## Project in one paragraph

mailpane is a local, single-user web frontend for Gmail whose only reason to exist is low RAM use. A C# ASP.NET Core backend on `localhost:8425` talks to the Gmail API, caches metadata in SQLite and serves server-rendered pages with htmx. Each Gmail account has its own URL prefix and its own browser tab. Gmail is the source of truth; the cache is a rebuildable projection.

## Read before working

1. `CURRENT_STATE.md` for where things stand.
2. `docs/MILESTONE_1.md` for the current milestone and its session plan.
3. `docs/ARCHITECTURE.md` for the part you are touching.
4. `DISCOVERIES.md` before trusting an assumption about the Gmail API. Items marked **unverified** are recall, not fact.

## Hard rules

These are safety properties. Do not relax them without a new entry in `DECISIONS.md`.

1. **Milestone 1 is read-only.** The only OAuth scope is `gmail.readonly`. No call that mutates the mailbox may exist in the code: no `modify`, `trash`, `send`, `drafts.*`, `labels.create/update/delete`, `settings.*` writes.
2. **GET never mutates.** Not the mailbox, not local state beyond caching. Sanitised email content can cause GET requests to this app, so GET must always be safe.
3. **Email HTML is hostile.** It is rendered only through the pipeline in `docs/ARCHITECTURE.md` section 7: sanitise server-side, serve from the body endpoint with the strict CSP, show inside the sandboxed iframe. Never inline email HTML into an app page. Never add `allow-scripts` to the sandbox.
4. **Remote content is off by default.** Images load only after an explicit per-message action.
5. **Loopback only.** Bind to `127.0.0.1`. Keep the Host header allow-list (`localhost`) in place; it is the DNS-rebinding defence.
6. **Secrets stay out of git and logs.** Never log refresh tokens, access tokens, the OAuth client secret, message bodies, subjects or addresses. Never commit `client_secret.json`, `*.db` or real mail as test fixtures. Fixtures are synthetic.
7. **Every Gmail call goes through the quota limiter.** No direct `GmailService` use outside the gateway. See `docs/GMAIL_API.md` section 3 for costs.
8. **Stay light.** No SPA framework, no bundler, no npm. JavaScript budget: htmx plus under 5 KB of own code. A new client-side dependency needs a `DECISIONS.md` entry.

## Working method

- One session per coherent task. The session plan is in `docs/MILESTONE_1.md`.
- Plan before executing. State the plan, then implement in small verifiable steps.
- Tests are the ground truth for the sync engine, the MIME/body selection, the sanitiser and the quota limiter.
- During exploration, tag findings in your notes and move them into `DISCOVERIES.md` at the end of the session:
  - `[DISCOVERY]` something verified, with how it was verified
  - `[DEAD END]` something tried that does not work, with why
  - `[OPEN QUESTION]` something still unknown
- When an open question in `DISCOVERIES.md` gets answered, move it to the verified section; do not delete it.
- When a decision changes, add a new entry to `DECISIONS.md` that supersedes the old one. Do not rewrite history.
- End every session by updating `CURRENT_STATE.md`: what works, what is next, anything the next session must know.

The architecture document is intent and context, not a frozen spec. Deviate when there is a reason, and write the reason down.

## Layout (intended)

```
src/Mailpane.Web/      Razor Pages, routes, OAuth callback, body endpoint
src/Mailpane.Core/     sync engine, cache, quota limiter, body pipeline; no ASP.NET or Google types
src/Mailpane.Gmail/    IGmailGateway implementation over Google.Apis.Gmail.v1
tests/Mailpane.Tests/  unit tests with synthetic fixtures
tools/Spike/           discovery console app (session S0)
docs/                  architecture, API notes, milestone spec, runbooks
```

## Commands

Fill in once the solution exists.

```sh
dotnet build
dotnet test
dotnet run --project src/Mailpane.Web
```

## Local paths

| Path | Contents | Precious? |
|---|---|---|
| `~/.config/mailpane/client_secret.json` | OAuth client downloaded from Google Cloud | yes, never commit |
| `~/.config/mailpane/config.json` | port, poll intervals | no |
| `~/.local/share/mailpane/accounts.db` | accounts and refresh tokens, mode 600 | yes |
| `~/.local/share/mailpane/cache/<slug>.db` | per-account metadata cache | no, rebuildable |

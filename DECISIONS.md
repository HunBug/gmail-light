# Decisions

Each entry records what was decided, why, what was rejected and when to reconsider. Decisions can be overturned: add a new entry that supersedes the old one and say why. Do not edit accepted entries beyond marking them superseded.

All entries below were accepted on 2026-10-04, before any code was written.

| ID | Decision |
|---|---|
| [D-001](#d-001-build-a-thin-client-instead-of-adopting-an-existing-one) | Build a thin client instead of adopting an existing one |
| [D-002](#d-002-gmail-api-not-imap) | Gmail API, not IMAP |
| [D-003](#d-003-web-ui-not-a-desktop-app) | Web UI, not a desktop app |
| [D-004](#d-004-c-aspnet-core-razor-pages-with-htmx) | C# ASP.NET Core Razor Pages with htmx |
| [D-005](#d-005-one-backend-one-url-prefix-per-account-no-unified-inbox) | One backend, one URL prefix per account, no unified inbox |
| [D-006](#d-006-gmail-is-the-source-of-truth-the-cache-is-a-rebuildable-projection) | Gmail is the source of truth; the cache is a rebuildable projection |
| [D-007](#d-007-cache-metadata-only-fetch-bodies-on-demand) | Cache metadata only; fetch bodies on demand |
| [D-008](#d-008-one-oauth-client-published-unverified-one-refresh-token-per-account) | One OAuth client, published unverified, one refresh token per account |
| [D-009](#d-009-gmailreadonly-for-milestone-1-gmailmodify-afterwards) | `gmail.readonly` for milestone 1, `gmail.modify` afterwards |
| [D-010](#d-010-email-html-sanitised-sandboxed-and-under-a-strict-csp) | Email HTML: sanitised, sandboxed and under a strict CSP |
| [D-011](#d-011-loopback-binding-and-host-allow-list-no-login) | Loopback binding and Host allow-list, no login |
| [D-012](#d-012-poll-historylist-no-pubsub-push) | Poll `history.list`; no Pub/Sub push |
| [D-013](#d-013-a-per-account-quota-limiter-in-front-of-every-api-call) | A per-account quota limiter in front of every API call |
| [D-014](#d-014-measure-ram-after-milestone-1-and-stop-if-it-is-not-clearly-better) | Measure RAM after milestone 1 and stop if it is not clearly better |

---

## D-001 Build a thin client instead of adopting an existing one

**Context.** The official Gmail web app costs roughly 500-600 MB per tab and three accounts are open at once. The things worth keeping (labels, threading, search, filters, categories) are server-side.

**Decision.** Write a small Gmail-only client on top of the official Gmail client library.

**Rejected.**
- *Zero (Mail-0).* The closest existing Gmail-API client, but built on Next.js, React and PostgreSQL, with mail sync stored in Cloudflare Durable Objects and R2, and setup asking for Autumn and Twilio keys. A React app of that size is unlikely to be lighter in the tab, and the stack is far heavier to run locally.
- *Cypht, Roundcube, SnappyMail.* Light, but they speak IMAP, so labels become folders and Gmail threading, search syntax and categories are lost.
- *lieer + notmuch.* Uses the Gmail API but keeps a full local copy of the mail, has no web UI, ignores category labels by default and replaces Gmail search with notmuch search.
- *GizTUI.* A terminal client. Useful as a reference for caching and batching, but not a web page.

**Consequences.** We own the HTML rendering and security work. Existing libraries cover API access and sanitising.

**Revisit if** a lightweight server-rendered Gmail-API client appears, or milestone 1 fails its kill criteria.

## D-002 Gmail API, not IMAP

**Context.** Gmail can be reached through its REST API or through IMAP with Gmail extensions.

**Decision.** Use the Gmail REST API only.

**Why.** It exposes threads, labels with colours, category labels, the full search syntax, drafts, filters, send-as and incremental sync through `history.list` as first-class concepts. IMAP with `X-GM-*` extensions reaches some of this but is clunkier and needs a persistent connection per account.

**Consequences.** Quota units become a design constraint (see D-013). OAuth setup is required (see D-008). The client is Gmail-only.

**Revisit if** the Gmail API becomes paid at this usage level in a way that matters. As of the quota page dated 2026-09-10, standard use is free and charges are planned only above 80,000,000 units per day per project; three accounts cannot exceed 25,920,000 per day even at the per-user cap.

## D-003 Web UI, not a desktop app

**Context.** A native desktop app was acceptable to the user. The target is total RAM, and differences of 100-200 MB do not matter.

**Decision.** Serve a web UI from a local backend and use the browser that is already open.

**Why.** Rendering HTML mail needs a browser engine either way. A desktop app would have to embed one (WebKitGTK or CEF), loading a second engine next to the running browser, and embedding it is the painful part of C# desktop work on Linux. Server-rendered pages are far less code than a desktop UI with list virtualisation, a compose editor and a webview bridge.

**Rejected.** Avalonia with an embedded webview; a desktop app with plain-text-only rendering.

**Consequences.** A window per account without browser chrome is still available through `chromium --app=http://localhost:8425/work/`.

**Revisit if** the tab itself turns out to be the heavy part in the milestone 1 measurement.

## D-004 C# ASP.NET Core Razor Pages with htmx

**Context.** The backend needs an HTTP server, templates, a Gmail client library and SQLite.

**Decision.** ASP.NET Core Razor Pages on .NET 10, `Google.Apis.Gmail.v1`, `Microsoft.Data.Sqlite`, `HtmlSanitizer`, htmx for partial updates.

**Why.** C# is the developer's main stack, and Google maintains an official .NET client. Razor Pages gives server-side templates and built-in antiforgery, which milestone 2 needs.

**Rejected.** Python with FastAPI and Jinja (equally workable, faster to iterate, not the main stack). Any SPA framework (defeats the purpose).

**Consequences.** The backend's own memory counts toward the total. An ASP.NET Core process is not tiny; workstation GC and other settings are to be measured in milestone 1 (see `DISCOVERIES.md`, Q-07). Razor Pages rules out Native AOT, which stays available as a later step via minimal APIs if the backend proves heavy.

**Revisit if** backend RSS exceeds the budget in `docs/MILESTONE_1.md` after tuning.

## D-005 One backend, one URL prefix per account, no unified inbox

**Context.** The user wants each account in its own view and keeps one tab per account.

**Decision.** A single backend process serves all accounts. Each account has a slug chosen when it is added and lives under `/<slug>/`. There is no cross-account view.

**Why.** One process shares the runtime cost across accounts. URL prefixes keep tabs independent and bookmarkable.

**Consequences.** Every route, cache file, limiter and sync loop is keyed by account from day one. Tab title and favicon colour carry the account so tabs are distinguishable.

## D-006 Gmail is the source of truth; the cache is a rebuildable projection

**Context.** List views need subject, sender, date and labels for many threads, and fetching those costs quota every time.

**Decision.** Keep a per-account SQLite cache of thread and label metadata. Treat it as disposable: deleting `cache/<slug>.db` must only cost a re-sync. The only precious local state is `accounts.db` (accounts and refresh tokens).

**Why.** Keeps the failure mode simple: when in doubt, drop the cache. It also separates what must be backed up and protected from what need not be.

**Consequences.** Local-only features that Gmail cannot store (snooze, scheduled send, undo-send queue) do not fit in the cache. When they arrive they get their own non-rebuildable store, designed then.

## D-007 Cache metadata only; fetch bodies on demand

**Context.** Bodies and attachments are large and sensitive.

**Decision.** The cache stores headers, snippets, labels and thread structure. Message bodies are fetched from the API when a thread is opened and held only in a small in-memory LRU. Attachments are streamed through, never stored.

**Why.** Less sensitive data at rest, a small database, and opening a thread costs one `threads.get` (40 units), which is affordable.

**Consequences.** No offline reading. Opening a thread needs the network.

**Revisit if** thread opening feels slow in practice; an on-disk body cache with a size cap is the fallback.

## D-008 One OAuth client, published unverified, one refresh token per account

**Context.** Multiple Gmail accounts must be reachable. Google's Testing publishing status expires refresh tokens seven days after consent.

**Decision.** Create one Google Cloud project with one OAuth client. Publish the consent screen to "In production" without submitting for verification. Each Gmail account consents once and the backend stores one refresh token per account.

**Why.** The client ID identifies the app, not a mailbox, so one client serves any number of accounts. Publishing removes the seven-day expiry. Verification is unnecessary for personal use; the cost is clicking through the "Google hasn't verified this app" warning once per account.

**Consequences.** Unverified apps are capped at 100 users, which is irrelevant here. A Google Workspace account may be blocked by its admin from granting restricted scopes to an unverified app (unverified, see Q-09).

**Open detail.** OAuth client type: "Web application" with the registered redirect `http://localhost:8425/oauth/callback` is the default. A "Desktop app" client with a loopback redirect may also work and avoids registering the URI (Q-08).

## D-009 `gmail.readonly` for milestone 1, `gmail.modify` afterwards

**Context.** Scopes fix what the app can do to the mailbox. Changing scope later means consenting again, one click per account.

**Decision.** Milestone 1 requests only `https://www.googleapis.com/auth/gmail.readonly`. Milestone 2 moves to `https://www.googleapis.com/auth/gmail.modify`. Filter management later adds `gmail.settings.basic`. The full `https://mail.google.com/` scope is never requested.

**Why.** While the code is a prototype, the token itself guarantees it cannot change or lose mail. `gmail.modify` covers reading, labelling, drafts and sending but not permanent deletion that bypasses the trash, which this client does not need.

**Consequences.** In milestone 1, opening a thread does not mark it read. That is expected, not a bug.

## D-010 Email HTML: sanitised, sandboxed and under a strict CSP

**Context.** Email HTML is attacker-controlled content shown inside an app that holds mailbox credentials.

**Decision.** Three independent layers:
1. Server-side sanitising with `HtmlSanitizer` using an explicit allow-list.
2. The sanitised body is served from its own endpoint and shown in an `<iframe sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox">`. No `allow-scripts`, no `allow-forms`.
3. The body endpoint sends a CSP of `default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:` so nothing remote loads. An explicit "show images" action reloads the frame with `https:` added to `img-src`.

**Why.** Any single layer can have holes. `allow-same-origin` without `allow-scripts` lets the parent page read the frame's height to size it, while the frame content itself still cannot run script.

**Rejected.** Inlining sanitised HTML into the page (one sanitiser bug becomes full compromise). A fully isolated sandbox without `allow-same-origin` (the parent cannot size the frame without a script inside it).

**Consequences.** Hard rule: GET requests never mutate anything, because sanitised content can still trigger GETs to this origin. Showing remote images reveals the reader's IP address and open time to the sender; Gmail's own image proxy hides this and we have no equivalent.

**Revisit if** the frame-sizing approach fails in practice (Q-06).

## D-011 Loopback binding and Host allow-list, no login

**Context.** The backend holds mailbox access and listens on a TCP port.

**Decision.** Bind to `127.0.0.1` only. Reject any request whose Host header names a host other than `localhost`. No login page or session in milestone 1.

**Why.** Loopback keeps the network out. The Host check defeats DNS rebinding, where a malicious website points its own hostname at 127.0.0.1 to read responses. The same-origin policy stops other websites reading responses cross-origin.

**Accepted risk.** Any process running as any local user can read mail through the port. Acceptable on a single-user laptop; not acceptable on a shared machine.

**Consequences.** From milestone 2, every mutating request is a POST with an antiforgery token, plus a `Sec-Fetch-Site` check, to stop cross-site request forgery from other tabs.

**Revisit if** the machine becomes multi-user, or the app is ever reached from another device. Either case needs authentication first.

## D-012 Poll `history.list`; no Pub/Sub push

**Context.** New mail and label changes made elsewhere must show up.

**Decision.** A background loop per account calls `history.list` with the stored `historyId` every 60 seconds while the account has had UI activity in the last five minutes, and every ten minutes otherwise.

**Why.** `history.list` costs 2 quota units, so polling is nearly free. Gmail push notifications need a Cloud Pub/Sub topic and a subscriber, which is infrastructure a local app should not need.

**Consequences.** New mail appears within about a minute, not instantly. If the stored `historyId` is too old the API returns HTTP 404 and the account does a full sync.

## D-013 A per-account quota limiter in front of every API call

**Context.** New Google Cloud projects (created on or after 2026-05-01) get 6,000 quota units per user per minute, and `threads.get` costs 40 units. One uncached page of 50 threads costs 2,010 units, so three quick uncached page loads would hit the limit.

**Decision.** All Gmail calls pass through a token-bucket limiter per account with a budget of 4,500 units per minute. Interactive requests take priority over background sync. Rate-limit errors trigger truncated exponential backoff.

**Why.** The quota makes the cache a requirement, not an optimisation, and the limiter keeps a cold cache from turning into errors.

**Consequences.** On a cold cache, rows appear progressively. Initial sync of the newest 200 inbox threads costs about 8,010 units and takes roughly two minutes.

**Note.** Many older articles and libraries quote the previous numbers (250 units per user per second, `threads.get` at 10, `messages.get` at 5). Those do not apply to a newly created project.

## D-014 Measure RAM after milestone 1 and stop if it is not clearly better

**Context.** The only reason for the project is memory. The expected saving is an estimate.

**Decision.** Milestone 1 is a read-only prototype that ends with a measurement against the official Gmail tabs, using the protocol and thresholds in `docs/MILESTONE_1.md`. Compose and actions are not started until the measurement passes.

**Why.** Compose, actions and polish are most of the work. They are not worth building on a foundation that does not deliver the saving.

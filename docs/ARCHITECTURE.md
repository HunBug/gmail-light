# Architecture

Intent and context for the whole system. Milestone 1 builds only the read-only parts; sections that apply later are marked. Reasoning for each choice is in `../DECISIONS.md` (referenced as D-0xx). Facts about the Gmail API are in `GMAIL_API.md` and `../DISCOVERIES.md` (V-0x verified, Q-xx open).

## 1. Goals and non-goals

**Goals**

1. Total RAM for three open accounts far below the official web app (targets in `MILESTONE_1.md`).
2. Keep Gmail's organisation: labels, threads, search syntax, categories, filters.
3. Runs entirely on the user's machine. No hosted component.
4. Safe by construction against hostile email content.

**Non-goals**

- A unified inbox across accounts.
- Offline reading.
- Other mail providers.
- Mobile layout. Desktop browser only.
- Feature parity with Gmail. Smart Compose, Chat, Meet and confidential mode are out for good.

## 2. System overview

```
 Browser (already running)                    Backend (one process)                     Google
+---------------------------+        +--------------------------------------+        +-----------+
| tab: localhost:8425/work/ |        | ASP.NET Core, bound to 127.0.0.1     |        |           |
| tab: .../personal/        | <----> |                                      |        |           |
| tab: .../third/           |  HTTP  |  Web: Razor Pages, htmx fragments,   |        |           |
|                           |        |       OAuth callback, body endpoint  |        |           |
| server-rendered HTML      |        |                                      |        | Gmail API |
| htmx + <5 KB own JS       |        |  Core: sync engine, cache,           | <----> |           |
| email body in sandboxed   |        |        quota limiter, body pipeline  | HTTPS  |           |
| iframe                    |        |                                      |        |           |
+---------------------------+        |  Gmail: IGmailGateway over           |        |           |
                                     |         Google.Apis.Gmail.v1         |        |           |
                                     +-------------------+------------------+        +-----------+
                                                         |
                                     +-------------------+------------------+
                                     | ~/.local/share/mailpane/             |
                                     |   accounts.db      (precious)        |
                                     |   cache/<slug>.db  (rebuildable)     |
                                     +--------------------------------------+
```

One backend process serves every account (D-005). The browser never talks to Google directly and never sees a token.

## 3. Components

| Component | Project | Responsibility |
|---|---|---|
| Web | `Mailpane.Web` | Routes, Razor templates, htmx fragments, OAuth start and callback, the email body endpoint, security headers |
| Sync engine | `Mailpane.Core` | Full and incremental sync per account, applying history records to the cache |
| Cache | `Mailpane.Core` | SQLite schema and queries for labels, threads, messages |
| Quota limiter | `Mailpane.Core` | Token bucket per account in front of every Gmail call (D-013) |
| Body pipeline | `Mailpane.Core` | Pick the body part, decode, sanitise, rewrite links and inline images |
| Account store | `Mailpane.Core` | Accounts and refresh tokens in `accounts.db` |
| Gmail gateway | `Mailpane.Gmail` | The only code that references `Google.Apis.*`; implements `IGmailGateway` |

`Mailpane.Core` has no reference to ASP.NET or to Google's library. It depends on `IGmailGateway`, which makes the sync engine testable with fakes built from synthetic JSON.

`IGmailGateway` is thin and maps one-to-one onto API methods: `GetProfile`, `ListLabels`, `GetLabel`, `ListThreads`, `GetThread(format)`, `ListHistory`, `GetAttachment`. Mutating methods are added in milestone 2, not before.

## 4. URLs

Account slugs are chosen when an account is added (`work`, `personal`, ...). Lowercase letters, digits and hyphens only, and not one of the reserved words `accounts`, `oauth`, `static`.

| Route | Purpose |
|---|---|
| `GET /` | List of accounts, link to add one |
| `GET /accounts/add` | Ask for a slug, then redirect to Google |
| `GET /oauth/callback` | Exchange the code, store the token, redirect to `/<slug>/` |
| `GET /<slug>/` | Inbox, same as `/<slug>/label/INBOX` |
| `GET /<slug>/label/<labelId>?page=` | Thread list for a label |
| `GET /<slug>/search?q=&page=` | Thread list for a Gmail search |
| `GET /<slug>/thread/<threadId>` | Thread view |
| `GET /<slug>/message/<messageId>/body?images=0\|1` | Sanitised body document, shown inside the iframe |
| `GET /<slug>/message/<messageId>/attachment/<attachmentId>` | Attachment download or inline image |
| `GET /<slug>/_rows?...` | htmx fragment: thread rows still being fetched |
| `GET /<slug>/_status` | htmx fragment: "new mail" notice and unread counts; 204 when nothing changed |

All routes are GET in milestone 1 and none of them changes anything (hard rule 2 in `../CLAUDE.md`).

Navigation between pages is ordinary full page loads. htmx is used only for fragments: progressive rows on a cold cache and the status poll. This keeps the DOM small and avoids long-lived page state, which is where web apps accumulate memory.

Each page sets `<title>` to `(unread) Label - slug` and a favicon tinted with the account's colour, so tabs can be told apart.

## 5. Data

### 5.1 `accounts.db` (precious)

```sql
CREATE TABLE accounts (
  id            INTEGER PRIMARY KEY,
  slug          TEXT NOT NULL UNIQUE,
  email         TEXT NOT NULL UNIQUE,
  color         TEXT NOT NULL,           -- for favicon and header
  refresh_token TEXT NOT NULL,
  scopes        TEXT NOT NULL,           -- space-separated, as granted
  added_at      INTEGER NOT NULL         -- unix seconds
);
```

The file is created with mode 600. The refresh token is stored as-is. Encrypting it with a key that sits next to it on the same disk would add nothing; the protections that count are file permissions and full-disk encryption. Storing it in the desktop keyring through libsecret is a possible later improvement.

Losing this file means adding the accounts again. Leaking it means leaking mailbox access within the granted scope.

### 5.2 `cache/<slug>.db` (rebuildable)

One file per account, so removing an account or resetting its cache is deleting a file.

```sql
CREATE TABLE meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);  -- history_id, schema_version, last_full_sync_at, last_sync_at

CREATE TABLE labels (
  id              TEXT PRIMARY KEY,      -- Gmail label id, e.g. INBOX, Label_123
  name            TEXT NOT NULL,         -- "Parent/Child" for nested labels
  type            TEXT NOT NULL,         -- system | user
  bg_color        TEXT,
  text_color      TEXT,
  list_visibility TEXT,
  threads_total   INTEGER,
  threads_unread  INTEGER,
  fetched_at      INTEGER NOT NULL
);

CREATE TABLE threads (
  id              TEXT PRIMARY KEY,
  history_id      TEXT NOT NULL,
  subject         TEXT,
  snippet         TEXT,
  participants    TEXT NOT NULL,         -- JSON array of display names, oldest first
  message_count   INTEGER NOT NULL,
  last_message_at INTEGER NOT NULL,      -- internalDate of newest message, ms
  fetched_at      INTEGER NOT NULL
);

CREATE TABLE messages (
  id            TEXT PRIMARY KEY,
  thread_id     TEXT NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
  internal_date INTEGER NOT NULL,
  from_addr     TEXT,
  to_addrs      TEXT,
  cc_addrs      TEXT,
  subject       TEXT,
  snippet       TEXT,
  size_estimate INTEGER
);

CREATE TABLE message_labels (
  message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
  label_id   TEXT NOT NULL,
  PRIMARY KEY (message_id, label_id)
);

-- Derived: a thread carries a label when any of its messages does.
CREATE TABLE thread_labels (
  label_id        TEXT NOT NULL,
  thread_id       TEXT NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
  last_message_at INTEGER NOT NULL,
  PRIMARY KEY (label_id, thread_id)
);
CREATE INDEX thread_labels_order ON thread_labels (label_id, last_message_at DESC);
```

Notes:

- Labels are stored per message because Gmail applies them per message and `history.list` reports changes per message. `thread_labels` is recomputed for a thread whenever one of its messages changes.
- A thread is unread when `thread_labels` has a row for `UNREAD`.
- The list view for a label is one indexed query on `thread_labels` joined to `threads`.
- No bodies, no attachments (D-007).
- A `schema_version` mismatch on startup drops and recreates the cache. There are no cache migrations.

## 6. Sync

Costs are from V-02. Every call goes through the limiter (section 8).

### 6.1 First sync for an account

1. `getProfile` (1 unit). Store its `historyId` **before** anything else, so changes that happen during the sync are replayed afterwards.
2. `labels.list` (1), then `labels.get` (1 each) for counts and colours.
3. `threads.list` with `labelIds=INBOX`, `maxResults=200` (10).
4. `threads.get` with `format=metadata` for each (40 each), in batches of at most 50 (V-03). Insert into the cache as results arrive.
5. Run an incremental sync from the `historyId` stored in step 1.

Total for 200 threads is about 8,010 units plus labels, roughly two minutes at the limiter's budget. The UI is usable as soon as the first rows land.

Other labels are not synced up front. They fill on first visit (6.3).

### 6.2 Incremental sync

1. `history.list` with the stored `historyId`, paging until done (2 units per page).
2. For each record:
   - `labelsAdded` / `labelsRemoved`: update `message_labels` from the record itself, then recompute `thread_labels` for that thread. No fetch needed when the message is already cached.
   - `messagesAdded`: mark the thread dirty.
   - `messagesDeleted`: delete the message; delete the thread if it has no messages left.
   - A record for a message that is not in the cache: mark its thread dirty only if the thread is cached, otherwise ignore. It will be fetched when a list view needs it.
3. `threads.get` (metadata) for each dirty thread and replace its rows.
4. Store the `historyId` returned by the response.
5. Refresh `labels.get` counts for labels touched by the changes.

If `history.list` returns HTTP 404, the stored ID is out of range (V-05). Drop the thread tables and run the first sync again.

Schedule (D-012): every 60 seconds while the account had a UI request in the last five minutes, every ten minutes otherwise. One loop per account, never overlapping with itself.

### 6.3 Filling a list view

A list page (label or search) works the same way:

1. `threads.list` with `labelIds` or `q`, `maxResults=50` (10 units). This call always goes to the API so the ordering and membership are Gmail's.
2. Render rows for the IDs that are in the cache and still current (cached `history_id` equals the one returned by `threads.list`; this relies on Q-13).
3. For the rest, render placeholder rows that load through `GET /<slug>/_rows`, which fetches them with `threads.get` at interactive priority and returns the finished rows.

A fully cached page costs 10 units. A fully uncached one costs 2,010. Search results over old mail are the expensive case, so search uses a page size of 25.

`threads.list` returns a `nextPageToken`; paging uses it. The "older" link carries the token.

### 6.4 Opening a thread

`threads.get` with `format=full` (40 units) returns every message with its MIME tree, without attachment data. The result goes into an in-memory LRU (about 20 threads) so the body endpoint and the attachment list do not fetch it again. Nothing is written to disk.

## 7. Rendering email bodies

This is the security-critical path (D-010). Treat every input as hostile.

### 7.1 Pipeline

1. **Select the part.** Walk the MIME tree. Within `multipart/alternative` prefer `text/html`, fall back to `text/plain`. Descend through `multipart/mixed`, `multipart/related` and `multipart/signed`. Parts with a filename or `Content-Disposition: attachment` are attachments.
2. **Decode.** base64url to bytes, bytes to text using the part's charset (see Q-01).
3. **Plain text:** HTML-encode, turn URLs into links, wrap in `<pre>` with wrapping enabled. Skip to step 6.
4. **Sanitise HTML** with `HtmlSanitizer` using an explicit allow-list:
   - Remove: `script`, `iframe`, `frame`, `object`, `embed`, `applet`, `form`, `input`, `button`, `select`, `textarea`, `base`, `meta`, `link`, `svg`, `math`, and all `on*` attributes.
   - Allow URL schemes `http`, `https`, `mailto` on links; `cid` and `data` (images only) on `img`.
   - Keep inline `style` attributes and `<style>` blocks, filtered: no `position: fixed`, no `expression()`, no `@import`, no `behavior`.
5. **Rewrite.**
   - Every `<a>` gets `target="_blank"` and `rel="noopener noreferrer"`.
   - `img src="cid:..."` becomes the attachment route for the part with that Content-ID.
   - When images are off, remote `img` sources are removed and replaced with a placeholder so the layout does not collapse.
6. **Wrap** in a minimal HTML document with a base stylesheet (readable default font, `max-width: 100%` on images, word wrapping).

### 7.2 Serving and framing

The wrapped document is served by `GET /<slug>/message/<id>/body` with:

```
Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'self'; base-uri 'none'; form-action 'none'
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
Cache-Control: no-store
```

With `?images=1`, `img-src` becomes `'self' data: https:`.

The CSP is the enforcement point for remote content. It covers images referenced from CSS as well as `<img>`, and blocks remote stylesheets and fonts. The sanitiser is the second layer, not the first.

The thread page shows it in:

```html
<iframe sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox"
        src="/work/message/abc123/body?images=0" loading="lazy"></iframe>
```

- No `allow-scripts`: script cannot run in the frame even if it survives sanitising.
- No `allow-forms`: forms cannot submit.
- `allow-same-origin`: lets the parent page read the frame's content height on load and size the frame. Without script in the frame this grants the content nothing (Q-06 confirms the sizing works).
- `allow-popups allow-popups-to-escape-sandbox`: links open in a normal new tab.

In a thread, the newest message is expanded and older ones are collapsed using `<details>`, so collapsed bodies are not loaded (`loading="lazy"`) and no script is needed to expand them.

### 7.3 Attachments

`GET /<slug>/message/<id>/attachment/<attId>` calls `messages.attachments.get` (20 units) and streams the result.

- Inline images referenced by `cid:` are served with their image content type, only for `image/png`, `image/jpeg`, `image/gif` and `image/webp`.
- Everything else is served with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`. HTML, SVG and PDF attachments are never rendered inline.

### 7.4 App pages

Every app page (not the body endpoint) sends:

```
Content-Security-Policy: default-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

All text from email (subjects, names, snippets) is HTML-encoded by Razor's default encoding. Never use `Html.Raw` on mail data.

htmx is served from `/static`, not a CDN. Under this CSP it needs two settings in its `htmx-config` meta tag: `includeIndicatorStyles: false` (it otherwise injects a `<style>` element) and `allowEval: false`. Do not use `hx-on` or `js:` values. Check both setting names against the htmx version in use.

## 8. Quota limiter

One token bucket per account (D-013).

- Capacity and refill: 4,500 units per minute, leaving headroom under the 6,000 limit (V-01).
- Each gateway call declares its cost from the table in `GMAIL_API.md` and waits for tokens.
- Two priorities: interactive (a request the user is waiting on) and background (sync). Background waits whenever an interactive call is queued.
- On HTTP 429, or 403 with a rate-limit reason, apply truncated exponential backoff with jitter (1 s, 2 s, 4 s ... capped at 32 s) and empty the bucket.
- The limiter logs units spent per minute per account at debug level. No message content in logs.

At the per-user cap, three accounts cannot exceed 3 x 6,000 x 1,440 = 25,920,000 units a day, well under the 80,000,000 daily billing threshold. No daily budget is needed.

## 9. Authentication with Google

Setup steps are in `OAUTH_SETUP.md`. Design:

- One OAuth client for the app, one refresh token per account (D-008).
- Adding an account: `GET /accounts/add` generates a random `state`, remembers it in server memory with the chosen slug for ten minutes, and redirects to Google with `access_type=offline` and `prompt=consent`. `GET /oauth/callback` checks `state`, exchanges the code, calls `getProfile` to learn the address, and stores the account.
- Access tokens live in memory only and are refreshed by the Google library.
- If a refresh fails with `invalid_grant`, the account is marked as needing consent and its pages show a "reconnect" link. Sync for that account stops. Other accounts are unaffected.
- Scope for milestone 1: `gmail.readonly` only (D-009).

## 10. Local security model

| Threat | Defence |
|---|---|
| Access from the network | Bind to `127.0.0.1` only |
| DNS rebinding from a malicious website | Host header allow-list: host name `localhost` only; anything else gets 400 (ASP.NET Core `AllowedHosts`, which matches the host name without the port) |
| Another website reading responses | Same-origin policy; no CORS headers are ever sent |
| Another website triggering actions (from milestone 2) | POST only, antiforgery token, reject `Sec-Fetch-Site: cross-site` |
| Hostile email HTML | Section 7 |
| Email content triggering GETs to this app | GET never mutates |
| Token theft from disk | File mode 600; rely on disk encryption |
| Another local user or process | **Not defended.** Accepted for a single-user machine (D-011) |

If the browser resolves `localhost` to `::1` first and the page fails to load, bind `[::1]` as well; both are loopback.

The only origin is `http://localhost:8425`. A request to `http://127.0.0.1:8425` is rejected by the Host filter, so the OAuth redirect URI and the browser always see one origin.

## 11. Memory budget

The numbers that decide the project are in `MILESTONE_1.md`. Design choices that serve them:

**Tab**
- Full page loads; no client-side router or store.
- At most 50 rows in the DOM; paging, not infinite scroll.
- One email iframe loaded at a time by default; collapsed messages are not loaded.
- htmx plus under 5 KB of own script. One small stylesheet. No web fonts, no icon font; inline SVG or text for icons.

**Backend**
- Workstation, non-concurrent GC.
- Streaming for attachments; no buffering of whole files.
- Bounded in-memory LRU for opened threads.
- One SQLite connection per account, opened on demand and closed when idle.

## 12. Configuration and running

`~/.config/mailpane/config.json`:

```json
{
  "port": 8425,
  "activePollSeconds": 60,
  "idlePollSeconds": 600,
  "quotaUnitsPerMinute": 4500,
  "listPageSize": 50,
  "searchPageSize": 25
}
```

The port must match the redirect URI registered in Google Cloud.

Intended way to keep it running, as a systemd user service:

```ini
# ~/.config/systemd/user/mailpane.service
[Unit]
Description=mailpane

[Service]
ExecStart=%h/.local/lib/mailpane/Mailpane.Web
Environment=DOTNET_gcServer=0
Environment=DOTNET_gcConcurrent=0
Restart=on-failure

[Install]
WantedBy=default.target
```

```sh
systemctl --user enable --now mailpane
```

A chromeless window per account, if wanted: `chromium --app=http://localhost:8425/work/`.

## 13. Errors

- **Network down or API error on a page load:** render what the cache has, with a banner saying the list may be stale. A thread that cannot be fetched shows an error in place of the body.
- **Rate limited:** the limiter backs off; placeholder rows keep waiting. No error page.
- **`invalid_grant`:** see section 9.
- **History 404:** full re-sync for that account (6.2).
- **Corrupt or mismatched cache:** delete and rebuild.

## 14. Testing

Unit tests with synthetic fixtures, no network:

| Area | What is tested |
|---|---|
| Sync engine | Applying each history record type; dirty-thread refetch; 404 leading to full sync; `thread_labels` recomputation; changes arriving during first sync |
| Body selection | `multipart/alternative` inside `mixed` and `related`; plain-only; HTML-only; charset decoding; inline `cid` images |
| Sanitiser | A corpus of hostile inputs: `<script>`, `onerror`, `javascript:` and `data:` URLs on links, CSS `url()` and `@import`, `<meta refresh>`, `<base>`, `<form>`, `<svg onload>`, malformed nesting |
| Quota limiter | Budget respected over time; interactive priority; backoff |
| Web | Host filter rejects other hosts; body endpoint sends the CSP; no route accepts a non-GET in milestone 1 |

Fixtures are hand-written JSON shaped like Gmail API responses. Real mail never enters the repo.

Manual checks against a real account are listed as acceptance criteria in `MILESTONE_1.md`.

## 15. Later milestones

Summarised in `ROADMAP.md`. Design consequences to keep in mind now:

- Mutations (milestone 2) are optimistic in the cache and confirmed by the next incremental sync, since Gmail remains the source of truth.
- Local-only features (snooze, scheduled send, undo-send) need a store that is not rebuildable. It is separate from the cache and designed when needed (D-006).
- Compose needs a MIME builder (MimeKit) and upload handling. Nothing in milestone 1 should assume messages are read-only forever.

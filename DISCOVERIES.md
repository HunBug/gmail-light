# Discoveries

Facts, dead ends and open questions. Add to this file at the end of every session.

- `[DISCOVERY]` verified, with the source or the test that verified it
- `[DEAD END]` tried or evaluated and rejected, with the reason
- `[OPEN QUESTION]` not yet known; answer it and move it up

**Unverified** means it comes from recall or a secondary source and has not been checked against Google's documentation or a real account. Do not build on an unverified item without checking it first.

---

## Verified

Checked against Google's documentation on 2026-10-04 unless stated otherwise.

### V-01 [DISCOVERY] Quota limits for new projects

Source: [Gmail API usage limits](https://developers.google.com/workspace/gmail/api/reference/quota), page dated 2026-09-10.

- 6,000 quota units per minute per user per project.
- 1,200,000 quota units per minute per project.
- 80,000,000 units per day per project is a billing threshold. Use below it is free. Charges above it are planned for later in 2026 with 90 days' notice.
- These limits apply to Cloud projects created on or after 2026-05-01. Projects that used the API between November 2025 and April 2026 keep their earlier quotas. This project will be new, so the numbers above apply.

### V-02 [DISCOVERY] Per-method quota costs

Same source as V-01. The ones this client uses:

| Method | Units |
|---|---|
| `getProfile` | 1 |
| `labels.list` | 1 |
| `labels.get` | 1 |
| `history.list` | 2 |
| `messages.list` | 5 |
| `threads.list` | 10 |
| `messages.get` | 20 |
| `messages.attachments.get` | 20 |
| `threads.get` | 40 |
| `threads.modify` | 10 |
| `messages.modify` | 5 |
| `messages.batchModify` | 50 |
| `threads.trash` | 20 |
| `drafts.create` | 10 |
| `drafts.update` | 15 |
| `drafts.send` | 100 |
| `messages.send` | 100 |
| `settings.filters.list` | 1 |
| `settings.sendAs.list` | 1 |

The table lists one cost per method, with no variation by `format`.

### V-03 [DISCOVERY] Batch requests

Source: [Batch requests guide](https://developers.google.com/workspace/gmail/api/guides/batch).

- At most 100 calls per batch. Google advises against batches larger than 50 because they tend to trigger rate limiting.
- A batch of n calls counts as n calls against the quota. Batching saves round trips, not units.

### V-04 [DISCOVERY] Scope classification

Source: [Choose Gmail API scopes](https://developers.google.com/workspace/gmail/api/auth/scopes), page dated 2026-09-10.

| Scope | Class | Grants |
|---|---|---|
| `gmail.labels` | non-sensitive | See and edit labels |
| `gmail.send` | sensitive | Send only |
| `gmail.readonly` | restricted | View messages and settings |
| `gmail.modify` | restricted | Read, compose, send; no permanent delete bypassing trash |
| `gmail.compose` | restricted | Manage drafts and send |
| `gmail.metadata` | restricted | Labels and headers, no body |
| `gmail.settings.basic` | restricted | Settings and filters |
| `https://mail.google.com/` | restricted | Everything including permanent delete |

Restricted scopes need verification and a security assessment only for a public app. For personal use the app stays unverified.

### V-05 [DISCOVERY] Sync model

Sources: [Synchronize clients with Gmail](https://developers.google.com/workspace/gmail/api/guides/sync) (dated 2026-09-15) and the `users.history.list` reference.

- Full sync: list IDs, then batch-get them, then store the newest `historyId`. The thread methods can be used in place of the message methods.
- Partial sync: `history.list` with `startHistoryId` returns records newer than that ID.
- `history.list` parameters: `startHistoryId` (required), `historyTypes` (`messageAdded`, `messageDeleted`, `labelAdded`, `labelRemoved`), `labelId`, `maxResults` (default 100, max 500), `pageToken`.
- The response carries `history[]`, `nextPageToken` and the mailbox's current `historyId`. Each history record has `messagesAdded`, `messagesDeleted`, `labelsAdded`, `labelsRemoved`.
- History is typically available for at least a week, sometimes much less. An out-of-range `startHistoryId` returns HTTP 404 and the client must do a full sync.

### V-06 [DISCOVERY] Testing status expires refresh tokens after seven days

Sources: secondary, quoting Google's "Manage App Audience" page: "Authorizations by a test user will expire seven days from the time of consent." Consistent across several independent write-ups. Google's own page was not fetched.

- Applies to an External app in Testing status that requests more than basic profile scopes.
- Publishing to "In production" removes the expiry. The app shows as unverified and the user clicks through the warning once.
- Unverified apps are limited to 100 users.

### V-07 [DISCOVERY] `http://localhost` redirect URIs work with a Web application client

Source: Zero's README registers `http://localhost:8787/api/auth/callback/google` on a Web application client. Not checked against Google's own page.

---

## Dead ends

### X-01 [DEAD END] Zero (Mail-0) as a ready-made solution

Source: [Mail-0/Zero README](https://github.com/Mail-0/Zero), fetched 2026-10-04.

Next.js, React, TypeScript, PostgreSQL, Better Auth. Sync stores mail in Cloudflare Durable Objects and an R2 bucket. Setup asks for Autumn and Twilio keys. Too heavy to run locally and unlikely to be lighter in the tab. Tab RAM was not measured.

### X-02 [DEAD END] IMAP webmail (Cypht, Roundcube, SnappyMail)

Light and mature, and Cypht supports OAuth2 over IMAP for Gmail. Over IMAP, labels are folders and Gmail threading, search syntax and category tabs are lost. That is the part the user wants to keep.

### X-03 [DEAD END] lieer + notmuch

[lieer](https://github.com/gauteh/lieer) syncs mail and labels through the Gmail API into a local maildir indexed by notmuch. No web UI, a full local copy of all mail, category labels ignored by default, and notmuch search in place of Gmail search.

### X-04 [DEAD END] Desktop app with Avalonia

Needs an embedded browser engine for HTML mail, which loads a second engine beside the running browser and is awkward to integrate on Linux. See D-003.

### X-05 [DEAD END] IMAP as the transport for our own client

Gmail's IMAP extensions expose labels, thread IDs and raw search, but clunkily, and a persistent connection per account is needed. See D-002.

---

## Open questions

Answer these in session S0 (the discovery spike) unless marked otherwise.

### Q-01 [OPEN QUESTION] Body encoding with `format=full`

Is `payload.parts[].body.data` already UTF-8, or raw bytes in the part's original charset? Test with a message in ISO-8859-2 or windows-1250. If bytes are in the original charset, .NET needs `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` for legacy charsets. Fallback: `format=raw` parsed with MimeKit, at the cost of downloading attachments with the message.

### Q-02 [OPEN QUESTION] Header decoding

Are `Subject` and `From` values returned already decoded from RFC 2047 encoded-words, or must the client decode them? Test with a non-ASCII subject and display name.

### Q-03 [OPEN QUESTION] What `threads.get` with `format=metadata` returns

Expected per message: `id`, `threadId`, `labelIds`, `snippet`, `internalDate`, `sizeEstimate` and the headers named in `metadataHeaders`. Confirm. Also: there is no part list in metadata format, so can the list view show an attachment indicator without fetching `full`? If not, drop the paperclip from milestone 1.

### Q-04 [OPEN QUESTION] Category tabs

Do `CATEGORY_PERSONAL`, `CATEGORY_SOCIAL`, `CATEGORY_PROMOTIONS`, `CATEGORY_UPDATES` and `CATEGORY_FORUMS` appear as label IDs on messages, and does `INBOX` plus `CATEGORY_PERSONAL` match the Primary tab? What do messages carry when the user has category tabs switched off?

### Q-05 [OPEN QUESTION] Snooze and scheduled send

**Unverified:** believed not to be exposed by the API. Check whether snoozed threads are findable (`in:snoozed`), whether they carry any label, and what a snoozed thread looks like in `threads.get`. Needed before designing a local snooze in a later milestone.

### Q-06 [OPEN QUESTION] Sizing the sandboxed body iframe

With `sandbox="allow-same-origin"` and no `allow-scripts`, can the parent page read `contentDocument.documentElement.scrollHeight` on the frame's load event and set its height, in both Chromium and Firefox? Does the height stay right after the user enables images? Also: does an `<iframe loading="lazy">` inside a closed `<details>` stay unloaded until the element is opened, in both browsers? Answer in session S4.

### Q-07 [OPEN QUESTION] Backend memory

What is the RSS of an idle ASP.NET Core Razor Pages process on .NET 10 with workstation, non-concurrent GC? What does it reach after an hour of use with three accounts? Settings to try: `ServerGarbageCollector=false`, `ConcurrentGarbageCollection=false`, `TieredPGO` off, `DOTNET_GCConserveMemory`, `InvariantGlobalization`. Answer in sessions S1 and S7.

### Q-08 [OPEN QUESTION] OAuth client type

"Web application" with a registered `http://localhost:8425/oauth/callback` (default plan) or "Desktop app" with a loopback redirect? Check which the .NET library's `GoogleAuthorizationCodeFlow` handles most simply when the backend itself receives the callback, and how to force `access_type=offline` and `prompt=consent` so a refresh token is always returned.

### Q-09 [OPEN QUESTION] Google Workspace accounts

**Unverified:** a Workspace admin can restrict which third-party apps may use restricted Gmail scopes, which would block an unverified app. Only relevant if one of the accounts is a Workspace account. Check by trying to add it.

### Q-10 [OPEN QUESTION] Refresh token lifetime in production status

**Unverified:** besides the Testing expiry, a refresh token is said to stop working when unused for six months, when the user changes their password (for tokens with Gmail scopes), when access is revoked, or when a per-client per-account cap on live refresh tokens is exceeded (sources disagree on 50 or 100). Practical check: confirm the token from day one still works on day eight.

### Q-11 [OPEN QUESTION] Label counts

`labels.list` is believed not to include unread counts; `labels.get` returns `threadsUnread`, `threadsTotal`, `messagesUnread`, `messagesTotal`. **Unverified.** Confirm, and confirm that label colours come back as `color.backgroundColor` and `color.textColor`.

### Q-12 [OPEN QUESTION] Stars and importance

`STARRED` and `IMPORTANT` are system labels. **Unverified:** the coloured "superstars" are believed not to be distinguishable through the API. Check whether that matters to the user.

### Q-13 [OPEN QUESTION] `threads.list` details

**Unverified from recall:** parameters `q`, `labelIds`, `maxResults` (max 500), `pageToken`, `includeSpamTrash`; each returned thread has only `id`, `snippet` and `historyId`. Confirm, and check whether the ordering matches Gmail's list ordering (newest message first).

---

## Measurements

Fill in during session S7. Protocol in `docs/MILESTONE_1.md`.

| Date | Subject | Browser | Per tab (MB) | Backend RSS (MB) | Total for 3 accounts (MB) | Notes |
|---|---|---|---|---|---|---|
| | Official Gmail, idle | | | n/a | | |
| | Official Gmail, after 10 threads | | | n/a | | |
| | mailpane, idle | | | | | |
| | mailpane, after 10 threads | | | | | |
| | mailpane, after 1 hour | | | | | |

# Gmail API notes

What this client reuses from Gmail, what it cannot, and what each operation costs. Status column: **verified** means checked against Google's documentation on 2026-10-04 (details in `../DISCOVERIES.md`); **methods verified** means the methods appear in Google's quota table but their fields were not checked; **recall** means not yet checked and to be confirmed in the discovery spike.

## 1. Features reused from Gmail

| Gmail feature | How the client gets it | Status |
|---|---|---|
| Labels, nested | `labels.list`; nesting is the `/` in the label name | recall |
| Label colours | `labels.get` returns `color.backgroundColor` and `color.textColor` | recall (Q-11) |
| Unread counts per label | `labels.get` returns `threadsUnread` and `threadsTotal` | recall (Q-11) |
| Conversation threading | `threads.list` and `threads.get`; Gmail's own thread IDs | methods verified; fields recall (Q-13) |
| Search with Gmail syntax | `threads.list` with `q=`; `from:`, `has:attachment`, `older_than:` and the rest run server-side | recall |
| Category tabs | System labels `CATEGORY_PERSONAL`, `_SOCIAL`, `_PROMOTIONS`, `_UPDATES`, `_FORUMS` | recall (Q-04) |
| Importance markers | System label `IMPORTANT` | recall (Q-12) |
| Stars | System label `STARRED` | recall (Q-12) |
| Spam filtering | Server-side; spam carries the `SPAM` label and is excluded from lists by default | recall |
| Filters | Keep running server-side with no client involvement; manageable via `settings.filters.*` | methods verified |
| Drafts synced with other devices | `drafts.*` | methods verified |
| Send-as aliases and signatures | `settings.sendAs.list` | methods verified |
| Vacation responder | `settings.getVacation`, `settings.updateVacation` | methods verified |
| Incremental sync | `history.list` from a stored `historyId` | verified (V-05) |

Milestone 1 uses the read side of the first eight rows plus incremental sync.

## 2. Not available through the API

| Feature | Situation | Status |
|---|---|---|
| Snooze | No API. Could be rebuilt locally, but would not sync with official apps | recall (Q-05) |
| Scheduled send | No API. Same as snooze | recall (Q-05) |
| Smart Compose, Smart Reply, nudges | Not exposed | recall |
| Confidential mode | Not exposed | recall |
| Chat and Meet panels | Not part of the Gmail API | recall |
| Undo send | A client-side delay in Gmail too; recreate by delaying the send | n/a |
| Multiple inboxes, priority sections | UI features built on searches; recreate as saved searches | n/a |
| Coloured superstars | Believed to collapse into `STARRED` | recall (Q-12) |

## 3. Quota

Source: Google's usage limits page dated 2026-09-10 (V-01, V-02).

### 3.1 Limits

| Limit | Value |
|---|---|
| Per user, per minute | 6,000 units |
| Per project, per minute | 1,200,000 units |
| Per project, per day, before charges are planned to apply | 80,000,000 units |
| Calls per batch | 100 max, 50 advised; a batch of n counts as n calls |

These apply to Cloud projects created on or after 2026-05-01, which includes this one. Older articles quote 250 units per user per second and lower per-method costs. Ignore them.

The limiter budget is 4,500 units per minute per account (D-013).

### 3.2 Cost per method

| Method | Units | Used in |
|---|---|---|
| `getProfile` | 1 | M1 |
| `labels.list` | 1 | M1 |
| `labels.get` | 1 | M1 |
| `history.list` | 2 | M1 |
| `threads.list` | 10 | M1 |
| `threads.get` | 40 | M1 |
| `messages.get` | 20 | M1 (rarely) |
| `messages.attachments.get` | 20 | M1 |
| `messages.modify` | 5 | M2 |
| `threads.modify` | 10 | M2 |
| `threads.trash` | 20 | M2 |
| `messages.batchModify` | 50 | M2 |
| `drafts.create` | 10 | M3 |
| `drafts.update` | 15 | M3 |
| `drafts.send` | 100 | M3 |
| `messages.send` | 100 | M3 |
| `settings.sendAs.list` | 1 | M3 |
| `settings.filters.list` | 1 | M4 |

### 3.3 Cost of things the user does

| Action | Calls | Units |
|---|---|---|
| Open a label page, everything cached | 1 x `threads.list` | 10 |
| Open a label page, nothing cached (50 rows) | 1 x `threads.list` + 50 x `threads.get` | 2,010 |
| Search, nothing cached (25 rows) | 1 x `threads.list` + 25 x `threads.get` | 1,010 |
| Open a thread | 1 x `threads.get` (full) | 40 |
| Show one inline image or download one attachment | 1 x `attachments.get` | 20 |
| Background poll, nothing changed | 1 x `history.list` | 2 |
| Background poll, one new message | `history.list` + 1 x `threads.get` + a few `labels.get` | about 45 |
| First sync, 200 inbox threads | `getProfile` + labels + `threads.list` + 200 x `threads.get` | about 8,100 |

Reading is cheap once cached. The expensive cases are cold pages and searches over old mail, which is why rows load progressively.

`threads.get` costs 40 whatever the format. `messages.get` at 20 is cheaper for a thread with a single message, but `threads.get` gives the whole thread in one call and is cheaper from two messages up. Milestone 1 uses `threads.get` throughout; revisit only if quota becomes a problem in use.

## 4. Scopes

Source: V-04.

| Milestone | Scope | Why |
|---|---|---|
| M1 | `https://www.googleapis.com/auth/gmail.readonly` | Read messages, labels and settings. Cannot change anything |
| M2, M3 | `https://www.googleapis.com/auth/gmail.modify` | Adds label changes, trash, drafts and sending. No permanent delete bypassing trash |
| M4 | plus `https://www.googleapis.com/auth/gmail.settings.basic` | Only if filter management is built |
| never | `https://mail.google.com/` | Only needed for immediate permanent deletion |

All four are classed as restricted. That classification matters only when publishing an app to other people; for personal use the app stays unverified (D-008).

Changing scope means each account consents again.

## 5. System labels

From recall; confirm the exact set in session S0.

| ID | Meaning |
|---|---|
| `INBOX` | In the inbox. Archiving removes this label |
| `UNREAD` | Not read. Applied per message |
| `STARRED` | Starred |
| `IMPORTANT` | Importance marker |
| `SENT` | Sent mail |
| `DRAFT` | Drafts |
| `SPAM` | Spam |
| `TRASH` | Trash |
| `CATEGORY_PERSONAL` | Primary tab |
| `CATEGORY_SOCIAL` | Social tab |
| `CATEGORY_PROMOTIONS` | Promotions tab |
| `CATEGORY_UPDATES` | Updates tab |
| `CATEGORY_FORUMS` | Forums tab |

User labels have IDs like `Label_123` and a `name` such as `Projects/Alpha`.

"All Mail" is not a label. It is a list with no `labelIds`, which excludes spam and trash by default.

## 6. Search

`threads.list` takes the same query string as the Gmail search box in its `q` parameter. The client passes the user's text through unchanged and does not parse it.

Search results are not ordered by anything the client controls. Use the order returned.

Searching does not need the cache, but showing the results does (section 3.3).

## 7. Sync

The algorithm is in `ARCHITECTURE.md` section 6. Facts it relies on (V-05):

- `getProfile` returns the mailbox's current `historyId`.
- `history.list` needs `startHistoryId` and returns records after it, plus the current `historyId`.
- History is usually kept at least a week. An ID outside the range gives HTTP 404, and the answer is a full sync.
- History reports label changes per message, so labels are cached per message.

## 8. .NET library notes

From recall. Check each against the package when first used.

- NuGet: `Google.Apis.Gmail.v1` (brings `Google.Apis.Auth`).
- `GmailService` is the client. Requests look like `service.Users.Threads.List("me")` with properties for `Q`, `LabelIds`, `MaxResults`, `PageToken`.
- `GoogleAuthorizationCodeFlow` builds the consent URL and exchanges the code. `UserCredential` wraps a stored refresh token and refreshes access tokens on demand.
- The flow persists tokens through an `IDataStore`. Implement one over `accounts.db` in place of the default file store.
- `BatchRequest` queues several requests into one HTTP call.
- Body data in `MessagePartBody.Data` is base64url, not standard base64.
- There is no official .NET quickstart for the Gmail API on Google's site; the Java and Python quickstarts show the same flow.

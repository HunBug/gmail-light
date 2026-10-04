# Milestone 1: read-only prototype

The smallest client that can stand in for a Gmail tab when only reading, built to answer one question: **is it dramatically lighter?**

## Scope

In:

- Add accounts through OAuth; several accounts, each under its own URL
- Label sidebar with nesting, colours and unread counts
- Thread list for any label, with paging
- Category tabs on the inbox
- Search with Gmail syntax
- Thread view with HTML and plain-text bodies, inline images and attachment download
- New mail and outside changes appear without a manual reload
- RAM measurement and a go/stop decision

Out:

- Anything that changes the mailbox. Opening a thread does not mark it read (D-009)
- Compose, reply, drafts
- Keyboard shortcuts
- Settings UI; configuration is a JSON file
- Styling beyond clean and readable

## Sessions

One Claude Code session per row. Each ends with tests passing and `../CURRENT_STATE.md` updated.

| # | Session | Outcome |
|---|---|---|
| S0 | Discovery spike | Open questions Q-01 to Q-05 and Q-08 to Q-13 answered in `../DISCOVERIES.md` |
| S1 | Skeleton and accounts | Backend runs, an account can be added, tokens persist |
| S2 | Gateway, limiter, cache, labels | Sidebar shows real labels with counts |
| S3 | Thread list | Inbox and any label list correctly, with paging |
| S4 | Thread view | Messages render safely; attachments download |
| S5 | Search | Gmail queries return the same threads as Gmail |
| S6 | Incremental sync | Changes appear within about a minute |
| S7 | Measure and decide | Numbers recorded; go or stop |

### S0 Discovery spike

A throwaway console app in `tools/Spike/`. It does the OAuth consent for one account (listening on `localhost:8425` for the callback), then dumps raw JSON for a handful of calls so the open questions can be answered from real responses.

Pick about ten real threads that cover: a plain-text message, an HTML newsletter, a message with attachments, one with inline images, one with a non-ASCII subject, one in a legacy charset if you have any, a long thread, a snoozed thread, a starred one, and one from each category tab.

Dump for each: `threads.get` in `metadata` and `full` formats. Also dump `labels.list`, `labels.get` for three labels, `getProfile`, `threads.list` for the inbox and for one search, and `history.list` after changing a label in official Gmail.

The dumps contain real mail. Write them outside the repo. Record only the conclusions in `../DISCOVERIES.md`.

Done when each listed question has an answer or a reason it cannot be answered yet.

### S1 Skeleton and accounts

- Solution with the three projects and the test project
- Kestrel bound to `127.0.0.1:8425`; `AllowedHosts` set to `localhost`
- Security headers on app pages (`ARCHITECTURE.md` 7.4)
- `accounts.db` with mode 600
- `/accounts/add`, `/oauth/callback`, `/` listing accounts
- Refresh tokens stored through a custom `IDataStore`
- Note idle backend RSS (first data point for Q-07)

### S2 Gateway, limiter, cache, labels

- `IGmailGateway` and its implementation; nothing else references `Google.Apis`
- Quota limiter with tests
- Per-account cache database and schema
- Label sync; sidebar rendering with nesting, colours, unread counts
- Page layout: sidebar, header with search box, content area

### S3 Thread list

- First sync (`ARCHITECTURE.md` 6.1)
- List view for any label (6.3), including placeholder rows that fill in
- Row: senders, subject, snippet, date, unread weight, label chips, message count
- Newer and older paging
- Category tabs on the inbox, if Q-04 says the labels behave as expected

### S4 Thread view

- Body pipeline with tests, including the hostile sanitiser corpus
- Body endpoint with its CSP; sandboxed iframe; frame sizing (answers Q-06)
- Remote images off, with a per-message "show images"
- Inline `cid:` images
- Attachment list and download
- Older messages collapsed with `<details>`

### S5 Search

- Search box submits to `/<slug>/search?q=`
- Results reuse the list view at page size 25
- Clicking a label or a category clears the search

### S6 Incremental sync

- Background loop per account with the active and idle intervals
- History application with tests, including the 404 path
- `_status` poll updating unread counts and showing a "new mail" notice
- `invalid_grant` handling with a reconnect link

### S7 Measure and decide

Follow "Measurement" below, record results in `../DISCOVERIES.md`, and write the decision into `../DECISIONS.md` as a new entry.

## Acceptance criteria

Functional, checked by hand against a real account:

| # | Criterion |
|---|---|
| A1 | An account can be added through the browser. After a backend restart it still works with no new consent |
| A2 | A second account can be added and neither sees the other's mail, labels or cache file |
| A3 | The sidebar lists the same labels as Gmail, nested the same way, with matching unread counts |
| A4 | For the inbox and three other labels, the first page shows the same threads in the same order as Gmail |
| A5 | Unread threads are visually distinct; label chips match Gmail |
| A6 | Five different searches, including `from:`, `has:attachment` and `older_than:`, return the same threads as Gmail |
| A7 | An HTML newsletter, a plain-text message and a long thread all display readably |
| A8 | Remote images do not load until "show images" is clicked; the browser's network panel confirms it |
| A9 | An attachment downloads intact; an inline image displays |
| A10 | Mail that arrives while the tab is open appears within 90 seconds without a reload |
| A11 | A label change made in official Gmail is reflected within 90 seconds |
| A12 | Deleting `cache/<slug>.db` and reloading rebuilds the view with no other loss |

Safety, checked by tests and inspection:

| # | Criterion |
|---|---|
| B1 | The granted scope is `gmail.readonly` and nothing else |
| B2 | No mutating Gmail method is referenced anywhere in the code |
| B3 | No route accepts anything but GET |
| B4 | The sanitiser corpus passes; a test message containing `<script>`, an `onerror` handler and a form shows none of them active |
| B5 | A request with `Host: evil.example` gets HTTP 400 |
| B6 | The backend listens on loopback addresses only (`ss -ltnp` shows nothing but `127.0.0.1` or `[::1]` for the port) |
| B7 | Logs at default level contain no addresses, subjects, bodies or tokens |
| B8 | Normal browsing for ten minutes produces no rate-limit error from Google |

Performance:

| # | Criterion |
|---|---|
| C1 | A cached label page renders in under 300 ms server time |
| C2 | A cached thread opens in under 1 s including the API call |
| C3 | The RAM measurement is recorded |

## Measurement

Use the browser you use every day, with your normal extensions, since that is the real comparison.

**Baseline: official Gmail**

1. Close all mail tabs. Open the three Gmail accounts in three tabs.
2. Wait two minutes. Record each tab's memory.
3. In each tab open ten threads, return to the inbox, wait one minute. Record again.

**Prototype**

4. Close the Gmail tabs. Open the same three accounts in mailpane.
5. Repeat steps 2 and 3.
6. Record the backend's memory at each point, and again after an hour of normal use.

**Where to read the numbers**

- Chromium-based browsers: the browser's task manager (Shift+Esc), column "Memory footprint". Sum the rows belonging to the mail tabs, including their subframes.
- Firefox: `about:processes`.
- Backend: `ps -o rss= -p "$(pgrep -f Mailpane.Web)"` gives RSS in KB.

Tabs from the same site may share a renderer process. Count the process once.

If only one or two accounts are connected at that point, measure those and scale the tab figure; the backend figure does not scale linearly.

## Kill criteria

Total means all three tabs plus the backend. The baseline is expected to be around 1.5 GB or more.

| Result | Meaning | Action |
|---|---|---|
| Total at or under 500 MB | Clear win | Go on to milestone 2 |
| Total between 500 and 800 MB | Unclear | Find the biggest consumer, spend one session reducing it, measure again |
| Total over 800 MB, or any single tab over 250 MB | Not worth it | Stop. Record why in `../DECISIONS.md` |

Guide figures, not pass marks: about 100 MB or less per tab, about 150 MB or less for the backend.

If the backend is the problem and the tabs are fine, try in this order: GC settings (Q-07), trimming, then replacing Razor Pages with minimal APIs compiled with Native AOT. If the tabs are the problem, the approach itself is in doubt.

These thresholds are proposals. Adjust them before S7 if they do not match what "worth it" means to you, but adjust them before measuring, not after.

## Definition of done

- All A, B and C criteria met, or each miss explained in `../CURRENT_STATE.md`
- Measurement table filled in
- Go or stop decision recorded in `../DECISIONS.md`
- No open question from Q-01 to Q-13 left unanswered without a note saying why

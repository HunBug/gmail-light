# mailpane

> Working name. Rename freely; nothing depends on it yet.

A lightweight, locally-run web frontend for Gmail. It keeps what Gmail does well on the server side (labels, threads, search, filters, categories, spam filtering) and replaces only the heavy web app in the browser tab.

**Status:** design complete, nothing implemented. See [CURRENT_STATE.md](CURRENT_STATE.md).

## Why

The official Gmail web app uses roughly 500-600 MB of RAM per tab. With three accounts open that is 1.5 GB or more for reading mail. Most of what makes Gmail good is server-side and reachable through the Gmail API, so a thin client can reuse it.

The goal is total RAM, not features. If the first prototype is not dramatically lighter, the project stops (see [docs/MILESTONE_1.md](docs/MILESTONE_1.md), "Kill criteria").

## What it is

- One small backend process on `localhost`, serving all accounts.
- One URL per account (`/work/`, `/personal/`, ...). Each account lives in its own tab. There is no unified inbox.
- Server-rendered HTML with a small amount of htmx. No SPA framework.
- Gmail is the source of truth. The local SQLite cache can be deleted at any time and is rebuilt from the API.

## What it is not

- Not hosted. It binds to loopback only and is used by one person on their own machine.
- Not an IMAP client and not a generic webmail. It is Gmail-only by design.
- Not a replacement for the official Gmail apps on the phone. Both keep working side by side.

## Stack

| Part | Choice |
|---|---|
| Backend | C# / ASP.NET Core Razor Pages, .NET 10 |
| Gmail access | `Google.Apis.Gmail.v1` (official client) |
| UI | Razor templates + htmx |
| Cache | SQLite (`Microsoft.Data.Sqlite`) |
| HTML mail | `HtmlSanitizer` + sandboxed iframe + CSP |
| Target OS | Linux (Arch), single user |

## Documents

Read in this order:

| File | What it holds |
|---|---|
| [CURRENT_STATE.md](CURRENT_STATE.md) | Where things stand and what to do next |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Components, data model, sync, rendering, security |
| [docs/GMAIL_API.md](docs/GMAIL_API.md) | Which Gmail features are reused and how; quota costs; scopes |
| [docs/MILESTONE_1.md](docs/MILESTONE_1.md) | The read-only prototype: scope, sessions, acceptance, kill criteria |
| [docs/OAUTH_SETUP.md](docs/OAUTH_SETUP.md) | One-time Google Cloud setup and adding accounts |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Milestones after the prototype |
| [DECISIONS.md](DECISIONS.md) | Each decision with its reasoning and the alternatives rejected |
| [DISCOVERIES.md](DISCOVERIES.md) | Verified facts, dead ends and open questions |
| [CLAUDE.md](CLAUDE.md) | Working rules for Claude Code sessions |

## Running

Not runnable yet. The intended shape, once milestone 1 exists:

```sh
dotnet run --project src/Mailpane.Web      # serves http://localhost:8425
```

Then open `http://localhost:8425/`, add an account, and bookmark `http://localhost:8425/<account>/`.

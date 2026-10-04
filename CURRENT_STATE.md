# Current state

Update this file at the end of every session. Keep it short: what works, what is next, what the next session must know.

**Last updated:** 2026-10-04
**Milestone:** 1 (read-only prototype), not started

## What exists

- Design documents only. No code, no solution file, no Google Cloud project.

## What was settled before coding

- Build a thin Gmail-API client; existing projects do not fit (D-001).
- Web UI served by a local C# backend, one URL per account (D-003, D-004, D-005).
- Read-only first, then measure RAM, then decide whether to continue (D-009, D-014).
- Quota numbers and scope classes were checked against Google's documentation on 2026-10-04 (`DISCOVERIES.md`, V-01 to V-05).

## Next actions

1. **You, by hand:** create the Google Cloud project and OAuth client, parts 1 to 4 of `docs/OAUTH_SETUP.md`.
2. **You, by hand:** take the baseline RAM reading of the three official Gmail tabs now, using the method in `docs/MILESTONE_1.md`, and put it in the table in `DISCOVERIES.md`. It is the number everything is compared against, and it is easiest to take before anything changes.
3. **Session S0:** the discovery spike in `docs/MILESTONE_1.md`. Its job is to turn the open questions in `DISCOVERIES.md` into answers.
4. **Session S1** onwards, in order.

## Things the next session must know

- A lot of `docs/GMAIL_API.md` is marked "recall". Those rows are what S0 is for. Do not design around them until confirmed.
- The quota figures are for projects created after 2026-05-01 and are much tighter per call than older write-ups say. `threads.get` costs 40 units against 6,000 per user per minute.
- The repo and project name `mailpane` is a placeholder. If it is renamed, rename the namespaces, the paths in `CLAUDE.md` and the systemd unit in `docs/ARCHITECTURE.md` section 12.

## Open decisions for the user

- The kill thresholds in `docs/MILESTONE_1.md` are proposals. Confirm or change them before S7.
- OAuth client type, Web application or Desktop app, is settled in S0 (Q-08).
- Whether any of the three accounts is a Google Workspace account. If so, check Q-09 early, because an admin block would remove that account from scope.

## Session log

| Date | Session | Result |
|---|---|---|
| 2026-10-04 | Design | Documentation set written. No code |

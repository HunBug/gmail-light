# Current state

Update this file at the end of every session. Keep it short: what works, what is next, what the next session must know.

**Last updated:** 2026-10-04
**Milestone:** 1 (read-only prototype). S0 written but not run (needs the OAuth client). S1 done apart from the checks that need a real account. Parts of S2 done.

## What exists

- Solution `Mailpane.sln` on .NET 10: `src/Mailpane.Core`, `src/Mailpane.Gmail`, `src/Mailpane.Web`, `tests/Mailpane.Tests`, `tools/Spike`. Shared settings in `Directory.Build.props` (nullable, warnings as errors, invariant globalization, Linux only).
- **S0 spike** (`tools/Spike`): `auth`, `basics`, `query`, `thread`, `history` commands. Dumps raw JSON to `~/.local/share/mailpane/spike/` (mode 700/600). Builds; never run.
- **S1 backend:** Kestrel on `127.0.0.1:8425` only. Host allow-list `localhost`, enforced in code. GET/HEAD only (405 otherwise). App-page security headers. `accounts.db` created with mode 600 in a 700 directory. Pages: `/` (accounts), `/accounts/add` (slug, then redirect to Google), `/oauth/callback` (single-use state; checks the granted scope is exactly `gmail.readonly`; `getProfile` for the address; stores the account), and a placeholder `/<slug>/` that calls `getProfile` to show the token works.
- Tokens: `AccountTokenDataStore` implements Google's `IDataStore`. Access tokens stay in memory, refresh tokens go to `accounts.db`.
- **From S2:** quota limiter (two-priority token bucket with backoff) with tests. `IGmailGateway` with only `GetProfileAsync` so far, going through the limiter. Cache database with the full schema and rebuild on version mismatch.
- **Tests:** 93 passing. Cover the limiter, slugs, the account store and file modes, OAuth state, the cache schema, B3, B5, the security headers, the full add-account flow against a fake OAuth client, B7 log capture, and a source scan for B1, B2, rule 3 and rule 7.

## Checked by hand (2026-10-04)

- B6: `ss -ltnp` shows only `127.0.0.1:8425`.
- B5 over real HTTP: Host `127.0.0.1:8425` and `evil.example` get 400.
- Idle RSS is about 100 MB (V-09).

## Next actions

1. **You:** `docs/OAUTH_SETUP.md` parts 1 to 4, ending with `~/.config/mailpane/client_secret.json`.
2. **You:** the baseline RAM reading of the three official Gmail tabs (`docs/MILESTONE_1.md`, Measurement), into `DISCOVERIES.md`.
3. **Run S0:** `dotnet run --project tools/Spike -- auth`, then `basics`, then `query` for the thread kinds listed in `docs/MILESTONE_1.md`, then change a label in Gmail and run `history`. Answer Q-01 to Q-05 and Q-08 to Q-13 from the dumps.
4. **Finish S1 by hand:** add an account through `http://localhost:8425/accounts/add`, restart the backend, and check `/<slug>/` still works (A1). Check the granted scope on that page (B1).
5. **S2:** gateway methods `ListLabels` and `GetLabel` (shaped by what S0 finds for Q-11), label sync into the cache, sidebar, page layout, htmx in `/static`.

## Things the next session must know

- Do not trust `docs/GMAIL_API.md` rows marked "recall" until S0 has run. The gateway so far only uses `getProfile`, on purpose.
- `IOAuthClient` and `IGmailGateways` are the seams the Web tests replace. Nothing outside `Mailpane.Gmail` may reference Google types; `SourceRulesTests` enforces this.
- `SourceRulesTests` also fails on any `Users.…​.Modify/Trash/Send/...` call, any `Drafts` or `Settings` reference, and any scope other than `gmail.readonly`. Milestone 2 will need to adjust it deliberately, together with a DECISIONS entry.
- Request logging must stay at Warning (V-10). If you add logging, never log addresses, subjects, bodies, tokens or URLs with query strings.
- Account colours are CSS classes (`app.css`), because the app CSP blocks inline styles. A new palette colour needs a new class.
- Setting `Mailpane__DataDir` and `Mailpane__ConfigDir` points the backend at scratch directories.
- Deviations from the architecture document are recorded in place (5.2, 7.4, 8, 10). The spike's exemption from hard rule 7 is D-015.

## Open decisions for the user

- The kill thresholds in `docs/MILESTONE_1.md` are proposals. Confirm or change them before S7.
- Whether any of the three accounts is a Google Workspace account (Q-09).

## Session log

| Date | Session | Result |
|---|---|---|
| 2026-10-04 | Design | Documentation set written. No code |
| 2026-10-04 | S0 code, S1, part of S2 | Solution, spike, backend skeleton with accounts and OAuth, quota limiter, cache schema; 93 tests. Real-account checks wait for the OAuth client |

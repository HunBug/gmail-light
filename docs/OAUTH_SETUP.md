# OAuth setup

One-time setup in Google Cloud, then adding each Gmail account. Do parts 1 to 4 before session S0. Part 5 needs the backend from session S1; the S0 spike does its own consent with the same client.

Menu names in the Google Cloud console change often. The names below come from write-ups dated 2026 and were not checked click by click; look for the nearest equivalent if a label differs.

## What you end up with

- One Google Cloud project with the Gmail API enabled.
- One OAuth client, used for every account (D-008).
- `~/.config/mailpane/client_secret.json` on the laptop.
- One refresh token per Gmail account, stored by the app in `accounts.db`.

It does not matter which Google account owns the Cloud project. The client ID identifies the app, not a mailbox.

## Part 1: Google Cloud project

1. Go to <https://console.cloud.google.com> and sign in with any of your Google accounts.
2. Create a new project, for example `mailpane`.
3. **APIs & Services > Library:** find "Gmail API" and enable it.

## Part 2: Consent screen

Open **Google Auth Platform** (older consoles: **APIs & Services > OAuth consent screen**).

4. **Branding:** app name `mailpane`, your address as support email and developer contact.
5. **Audience:** user type **External**.
6. **Data Access:** add the scope `https://www.googleapis.com/auth/gmail.readonly`. Nothing else for milestone 1.

## Part 3: OAuth client

7. **Clients > Create client.**
   - Application type: **Web application**
   - Name: `mailpane local`
   - Authorized redirect URI: `http://localhost:8425/oauth/callback`
8. Download the client JSON and save it:

   ```sh
   mkdir -p ~/.config/mailpane
   mv ~/Downloads/client_secret_*.json ~/.config/mailpane/client_secret.json
   chmod 600 ~/.config/mailpane/client_secret.json
   ```

The redirect URI must match exactly, including `http`, `localhost` (not `127.0.0.1`), the port and the path. If you change the port in `config.json`, change it here too.

## Part 4: Publish

9. **Audience > Publish app**, so the publishing status reads **In production**.

Do this before adding accounts. While the status is **Testing**, Google expires each authorisation seven days after consent, and only addresses listed as test users can consent at all (V-06).

You do not need to submit the app for verification. It will show as unverified, and that is fine for your own accounts.

## Part 5: Add each account

With the backend running:

10. Open `http://localhost:8425/accounts/add`.
11. Enter a slug for the account, such as `work`. It becomes the URL: `http://localhost:8425/work/`.
12. On Google's page, **pick the Gmail account you want to add**. This is the step where it is easy to choose the wrong one if several are signed in.
13. Google shows "Google hasn't verified this app". Choose **Advanced**, then **Go to mailpane (unsafe)**.
14. Approve the requested access.
15. You land on the account's inbox. Bookmark it.

Repeat for each account.

## Part 6: Check on day eight

A week after adding the first account, confirm it still works without re-consenting. If it asks again, the app is still in Testing status (Q-10).

## When the scope changes

Milestone 2 replaces `gmail.readonly` with `gmail.modify`:

1. **Data Access:** add the new scope.
2. In the app, use the "reconnect" link for each account and approve again.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `redirect_uri_mismatch` | The URI registered in Part 3 differs from what the app sends | Make them identical, including port and path |
| `access_denied`, or "app is being tested" | Status is Testing and the account is not a test user | Publish (Part 4) |
| Works for a week, then `invalid_grant` | Status was Testing when the account was added | Publish, then reconnect the account |
| No refresh token after consent | Google only returns one on first consent unless asked | The app must send `prompt=consent` and `access_type=offline` (Q-08) |
| A company account cannot approve | The Workspace admin restricts unverified apps | Ask the admin to allow the client ID, or leave that account in official Gmail (Q-09) |
| Wrong mailbox appears under a slug | The wrong Google account was picked in step 12 | Remove the account in the app and add it again |

## Revoking access

In the Google Account for that mailbox, open the security settings and remove `mailpane` from third-party connections. The stored refresh token stops working at once. Then delete the account in the app.

## What to keep secret

| Item | If it leaks |
|---|---|
| `client_secret.json` | Someone can impersonate the app, but still needs a user to consent. Rotate the secret in **Clients** |
| `accounts.db` | Mailbox access within the granted scope for every account in it. Revoke each account as above |

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;

namespace Mailpane.Gmail;

/// <summary>
/// The OAuth flow for the app's single client (D-008). Loaded on first use, so the backend starts and
/// can explain the setup before client_secret.json exists.
/// </summary>
public sealed class GoogleFlowProvider(string clientSecretPath, AccountTokenDataStore tokens)
{
    // Milestone 1 is read-only (D-009, hard rule 1). This is the only scope in the code.
    public static readonly string Scope = GmailService.Scope.GmailReadonly;

    private readonly Lock _lock = new();
    private GoogleAuthorizationCodeFlow? _flow;

    public bool IsConfigured => File.Exists(clientSecretPath);

    public GoogleAuthorizationCodeFlow Flow
    {
        get
        {
            lock (_lock)
            {
                if (_flow is not null)
                    return _flow;
                if (!IsConfigured)
                    throw new InvalidOperationException("client_secret.json is missing. See docs/OAUTH_SETUP.md.");
                _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = GoogleClientSecrets.FromFile(clientSecretPath).Secrets,
                    Scopes = [Scope],
                    DataStore = tokens,
                    // Always ask, so Google always returns a refresh token (Q-08).
                    Prompt = "consent",
                });
                return _flow;
            }
        }
    }
}

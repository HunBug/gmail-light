using Google.Apis.Auth.OAuth2.Responses;
using Mailpane.Core.Auth;
using Mailpane.Core.Gmail;

namespace Mailpane.Gmail;

/// <summary>Consent for adding an account (ARCHITECTURE.md 9).</summary>
public sealed class GoogleOAuthClient(GoogleFlowProvider flows, GmailGateways gateways, AccountTokenDataStore tokens, int port) : IOAuthClient
{
    // Must match the URI registered on the OAuth client exactly (OAUTH_SETUP.md part 3).
    private string RedirectUri => $"http://localhost:{port}/oauth/callback";

    public bool IsConfigured => flows.IsConfigured;

    public Uri BuildAuthorizationUrl(string state)
    {
        var request = flows.Flow.CreateAuthorizationCodeRequest(RedirectUri);
        request.State = state;
        return request.Build();
    }

    public async Task<OAuthGrant> CompleteAsync(string slug, string code, CancellationToken ct)
    {
        TokenResponse token;
        try
        {
            token = await flows.Flow.ExchangeCodeForTokenAsync(slug, code, RedirectUri, ct);
        }
        catch (TokenResponseException e)
        {
            throw new OAuthException($"Google refused the code exchange: {e.Error?.Error ?? "unknown error"}.");
        }

        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new OAuthException("Google returned no refresh token. Remove mailpane from the Google account's third-party access and try again.");

        // B1: the token must carry gmail.readonly and nothing else.
        var granted = (token.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (granted is not [var only] || only != GoogleFlowProvider.Scope)
            throw new OAuthException("Google granted different scopes than gmail.readonly. The account was not added.");

        var profile = await gateways.Create(slug, token).GetProfileAsync(CallPriority.Interactive, ct);
        return new OAuthGrant(profile.Email, token.RefreshToken, token.Scope!);
    }

    public void Forget(string slug) => tokens.DeleteAsync<TokenResponse>(slug);
}

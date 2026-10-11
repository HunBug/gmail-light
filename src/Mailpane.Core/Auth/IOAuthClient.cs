namespace Mailpane.Core.Auth;

/// <summary>Google consent, without Google types leaking into Core or Web.</summary>
public interface IOAuthClient
{
    /// <summary>False when client_secret.json is missing; the UI then explains the setup.</summary>
    bool IsConfigured { get; }

    Uri BuildAuthorizationUrl(string state);

    /// <summary>Exchanges the code and looks up which mailbox granted it.</summary>
    Task<OAuthGrant> CompleteAsync(string slug, string code, CancellationToken ct);

    /// <summary>Drops tokens held in memory for a consent that did not end in a stored account.</summary>
    void Forget(string slug);
}

/// <summary>Never log <see cref="RefreshToken"/> or <see cref="Email"/>.</summary>
public sealed record OAuthGrant(string Email, string RefreshToken, string Scopes)
{
    public override string ToString() => "OAuthGrant { ... }";
}

public sealed class OAuthException(string message) : Exception(message);

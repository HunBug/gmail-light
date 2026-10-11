namespace Mailpane.Core.Accounts;

/// <summary>A row of accounts.db. Never log <see cref="RefreshToken"/> or <see cref="Email"/>.</summary>
public sealed record Account(
    string Slug,
    string Email,
    string Color,
    string RefreshToken,
    string Scopes,
    DateTimeOffset AddedAt)
{
    // Records print every property; keep the token and address out of logs and exception messages.
    public override string ToString() => $"Account {{ Slug = {Slug} }}";
}

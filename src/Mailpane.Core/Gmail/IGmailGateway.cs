namespace Mailpane.Core.Gmail;

/// <summary>
/// Thin, one-to-one map of the Gmail API methods milestone 1 uses (ARCHITECTURE.md 3). Read-only:
/// mutating methods are added in milestone 2, not before. Every implementation goes through the
/// account's <see cref="Quota.QuotaLimiter"/>.
/// </summary>
public interface IGmailGateway
{
    Task<MailboxProfile> GetProfileAsync(CallPriority priority, CancellationToken ct);
}

/// <summary>Result of <c>users.getProfile</c>. Never log <see cref="Email"/>.</summary>
public sealed record MailboxProfile(string Email, ulong HistoryId, long MessagesTotal, long ThreadsTotal)
{
    public override string ToString() => $"MailboxProfile {{ HistoryId = {HistoryId} }}";
}

public enum CallPriority
{
    /// <summary>A request the user is waiting on.</summary>
    Interactive,

    /// <summary>Sync. Waits whenever an interactive call is queued.</summary>
    Background,
}

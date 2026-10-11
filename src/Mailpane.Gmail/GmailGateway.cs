using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using Mailpane.Core.Gmail;
using Mailpane.Core.Quota;

namespace Mailpane.Gmail;

/// <summary>
/// <see cref="IGmailGateway"/> over Google.Apis.Gmail.v1. Every call takes its units from the account's
/// limiter first (hard rule 7) and retries rate-limit errors after the limiter's backoff.
/// Read-only: only get and list methods appear here (hard rule 1).
/// </summary>
public sealed class GmailGateway(GmailService service, QuotaLimiter limiter) : IGmailGateway
{
    private const int MaxRateLimitRetries = 5;

    public Task<MailboxProfile> GetProfileAsync(CallPriority priority, CancellationToken ct) =>
        Call(QuotaCost.GetProfile, priority, async token =>
        {
            var p = await service.Users.GetProfile("me").ExecuteAsync(token);
            return new MailboxProfile(p.EmailAddress, p.HistoryId ?? 0, p.MessagesTotal ?? 0, p.ThreadsTotal ?? 0);
        }, ct);

    private async Task<T> Call<T>(int cost, CallPriority priority, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await limiter.AcquireAsync(cost, priority, ct);
            try
            {
                var result = await call(ct);
                limiter.ReportSuccess();
                return result;
            }
            catch (GoogleApiException e) when (IsRateLimit(e) && attempt < MaxRateLimitRetries)
            {
                limiter.ReportRateLimited();
            }
        }
    }

    private static bool IsRateLimit(GoogleApiException e) =>
        e.HttpStatusCode == HttpStatusCode.TooManyRequests
        || (e.HttpStatusCode == HttpStatusCode.Forbidden
            && e.Error?.Errors?.Any(x => x.Reason is "rateLimitExceeded" or "userRateLimitExceeded") == true);
}

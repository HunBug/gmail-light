using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Mailpane.Core.Quota;

/// <summary>One <see cref="QuotaLimiter"/> per account, created on first use.</summary>
public sealed class QuotaLimiters(int unitsPerMinute, TimeProvider time, ILoggerFactory loggers) : IDisposable
{
    private readonly ConcurrentDictionary<string, QuotaLimiter> _limiters = new(StringComparer.Ordinal);

    public QuotaLimiter For(string slug) =>
        _limiters.GetOrAdd(slug, s => new QuotaLimiter(s, unitsPerMinute, time, loggers.CreateLogger<QuotaLimiter>()));

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
    }
}

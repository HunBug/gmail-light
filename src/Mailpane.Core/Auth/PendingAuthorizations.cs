using System.Security.Cryptography;

namespace Mailpane.Core.Auth;

/// <summary>
/// OAuth <c>state</c> values for consents in progress (ARCHITECTURE.md 9). Memory only, single use,
/// ten minutes. Bounded, because starting a consent is a GET and anything can trigger GETs.
/// </summary>
public sealed class PendingAuthorizations(TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxPending = 16;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, (string Slug, DateTimeOffset Expires)> _pending = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public string Begin(string slug)
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            Prune(now);
            if (_pending.Count >= MaxPending)
                _pending.Remove(_pending.MinBy(p => p.Value.Expires).Key);
            _pending[state] = (slug, now + Lifetime);
        }
        return state;
    }

    /// <summary>Returns the slug the consent was started for, or null. A state works once.</summary>
    public string? Complete(string? state)
    {
        if (string.IsNullOrEmpty(state))
            return null;
        lock (_lock)
        {
            Prune(_time.GetUtcNow());
            return _pending.Remove(state, out var entry) ? entry.Slug : null;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _pending.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToList())
            _pending.Remove(key);
    }
}

using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Mailpane.Core.Accounts;

namespace Mailpane.Gmail;

/// <summary>
/// Token storage for the Google library, keyed by account slug. Access tokens live in memory only
/// (ARCHITECTURE.md 9). Refresh tokens are persisted to accounts.db once the account row exists; until
/// then (during the OAuth callback) they are only held here.
/// </summary>
public sealed class AccountTokenDataStore(AccountStore accounts) : IDataStore
{
    private readonly ConcurrentDictionary<string, TokenResponse> _live = new(StringComparer.Ordinal);

    public TokenResponse? Load(string slug)
    {
        if (_live.TryGetValue(slug, out var token))
            return token;
        return accounts.Find(slug) is { } account ? new TokenResponse { RefreshToken = account.RefreshToken } : null;
    }

    public Task StoreAsync<T>(string key, T value)
    {
        if (value is TokenResponse token)
        {
            _live[key] = token;
            if (token.RefreshToken is { Length: > 0 } refresh)
                accounts.UpdateRefreshToken(key, refresh);
        }
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key) =>
        Task.FromResult(typeof(T) == typeof(TokenResponse) && Load(key) is { } token ? (T)(object)token : default!);

    public Task DeleteAsync<T>(string key)
    {
        _live.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _live.Clear();
        return Task.CompletedTask;
    }
}

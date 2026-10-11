using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Mailpane.Core.Gmail;
using Mailpane.Core.Quota;

namespace Mailpane.Gmail;

/// <summary>One <see cref="GmailGateway"/> per account, built from its stored refresh token.</summary>
public sealed class GmailGateways(GoogleFlowProvider flows, AccountTokenDataStore tokens, QuotaLimiters limiters) : IGmailGateways
{
    private readonly ConcurrentDictionary<string, GmailGateway> _gateways = new(StringComparer.Ordinal);

    public IGmailGateway For(string slug) =>
        _gateways.GetOrAdd(slug, s => Create(s, tokens.Load(s)
            ?? throw new InvalidOperationException("No stored token for this account.")));

    /// <summary>A gateway for a token that is not in accounts.db yet (the OAuth callback).</summary>
    internal GmailGateway Create(string slug, TokenResponse token)
    {
        var credential = new UserCredential(flows.Flow, slug, token);
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "mailpane",
            // The library would otherwise retry 503s itself, spending units the limiter never saw.
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
        });
        return new GmailGateway(service, limiters.For(slug));
    }
}

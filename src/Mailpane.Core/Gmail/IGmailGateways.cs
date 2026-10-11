namespace Mailpane.Core.Gmail;

/// <summary>Hands out the gateway for an account that is in accounts.db.</summary>
public interface IGmailGateways
{
    IGmailGateway For(string slug);
}

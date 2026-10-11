using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Mailpane.Core.Storage;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailpane.Web.Pages;

public sealed class IndexModel(AccountStore accounts, IOAuthClient oauth, MailpanePaths paths) : PageModel
{
    public IReadOnlyList<Account> Accounts { get; private set; } = [];
    public bool OAuthConfigured => oauth.IsConfigured;
    public string ClientSecretFile => paths.ClientSecretFile;

    public void OnGet() => Accounts = accounts.List();
}

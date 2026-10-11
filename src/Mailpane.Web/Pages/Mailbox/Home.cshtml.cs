using Mailpane.Core.Accounts;
using Mailpane.Core.Gmail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailpane.Web.Pages.Mailbox;

public sealed class HomeModel(AccountStore accounts, IGmailGateways gateways, ILogger<HomeModel> logger) : PageModel
{
    public Mailpane.Core.Accounts.Account Account { get; private set; } = null!;
    public MailboxProfile? Profile { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        if (!AccountSlug.IsValid(slug) || accounts.Find(slug) is not { } account)
            return NotFound();
        Account = account;

        try
        {
            Profile = await gateways.For(slug).GetProfileAsync(CallPriority.Interactive, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("getProfile failed for {Slug} with {Error}", slug, e.GetType().Name);
            Error = "Could not reach Gmail for this account.";
        }
        return Page();
    }
}

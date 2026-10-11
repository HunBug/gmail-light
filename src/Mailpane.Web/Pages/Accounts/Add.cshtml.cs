using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailpane.Web.Pages.Accounts;

/// <summary>
/// Asks for a slug, then redirects to Google. A GET that only creates an in-memory, bounded, expiring
/// OAuth state; nothing is stored until Google redirects back with a code for that state.
/// </summary>
public sealed class AddModel(AccountStore accounts, PendingAuthorizations pending, IOAuthClient oauth) : PageModel
{
    // Messages are chosen by code, so a crafted link cannot put arbitrary text on this page.
    private static readonly Dictionary<string, string> Errors = new()
    {
        ["state"] = "That sign-in link has expired or was already used. Start again.",
        ["denied"] = "Google did not grant access.",
    };

    public string? Slug { get; private set; }
    public string? Message { get; private set; }
    public bool OAuthConfigured => oauth.IsConfigured;

    public IActionResult OnGet(string? slug, string? error)
    {
        if (error is not null)
            Message = Errors.GetValueOrDefault(error);

        if (slug is null || !oauth.IsConfigured)
            return Page();

        Slug = slug;
        if (!AccountSlug.IsValid(slug))
        {
            Message = "Use lowercase letters, digits and single hyphens, at most 32 characters. "
                + "accounts, oauth and static are reserved.";
            return Page();
        }
        if (accounts.Find(slug) is not null)
        {
            Message = "That name is already used by another account.";
            return Page();
        }

        return Redirect(oauth.BuildAuthorizationUrl(pending.Begin(slug)).AbsoluteUri);
    }
}

using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailpane.Web.Pages.OAuth;

/// <summary>
/// Google's redirect after consent (ARCHITECTURE.md 9). The one GET that writes local state; it only
/// does so for a valid, single-use state that this process issued in the last ten minutes.
/// </summary>
public sealed class CallbackModel(
    AccountStore accounts,
    PendingAuthorizations pending,
    IOAuthClient oauth,
    ILogger<CallbackModel> logger) : PageModel
{
    public string Message { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string? state, string? code, string? error, CancellationToken ct)
    {
        var slug = pending.Complete(state);
        if (slug is null)
            return Redirect("/accounts/add?error=state");
        if (error is not null || string.IsNullOrEmpty(code))
            return Redirect("/accounts/add?error=denied");

        OAuthGrant grant;
        try
        {
            grant = await oauth.CompleteAsync(slug, code, ct);
        }
        catch (OAuthException e)
        {
            oauth.Forget(slug);
            logger.LogWarning("Adding account {Slug} failed: {Reason}", slug, e.Message);
            Message = e.Message;
            return Page();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            oauth.Forget(slug);
            // Type only: messages from the Google library can echo request details.
            logger.LogError("Adding account {Slug} failed with {Error}", slug, e.GetType().Name);
            Message = "Something went wrong while talking to Google. Try again.";
            return Page();
        }

        var result = accounts.Add(slug, grant.Email, grant.RefreshToken, grant.Scopes);
        if (result != AddResult.Added)
        {
            oauth.Forget(slug);
            Message = result switch
            {
                AddResult.SlugTaken => "That name was taken by another account in the meantime.",
                AddResult.EmailTaken => "This Gmail account is already added under another name.",
                _ => "The account name is not valid.",
            };
            return Page();
        }

        logger.LogInformation("Account {Slug} added", slug);
        return Redirect($"/{slug}/");
    }
}

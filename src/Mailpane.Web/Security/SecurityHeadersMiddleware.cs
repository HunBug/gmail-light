namespace Mailpane.Web.Security;

/// <summary>
/// Headers for app pages (ARCHITECTURE.md 7.4). An endpoint that sets its own
/// Content-Security-Policy keeps it; the email body endpoint (7.2) relies on that.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string AppPagePolicy =
        "default-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy"))
                headers.ContentSecurityPolicy = AppPagePolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });
        return next(context);
    }
}

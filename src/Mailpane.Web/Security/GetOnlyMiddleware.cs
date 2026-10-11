namespace Mailpane.Web.Security;

/// <summary>
/// Milestone 1 accepts nothing but GET and HEAD (acceptance B3). Rejects before routing, so no page
/// handler or endpoint can be reached by another method by accident. Milestone 2 replaces this with
/// POST plus antiforgery plus a Sec-Fetch-Site check (D-011).
/// </summary>
public sealed class GetOnlyMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD";
        return Task.CompletedTask;
    }
}

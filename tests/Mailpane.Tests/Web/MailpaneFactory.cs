using Mailpane.Core.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mailpane.Tests.Web;

/// <summary>The real app on a test server, with its files in a temp dir and Google replaced by a fake.</summary>
public sealed class MailpaneFactory : WebApplicationFactory<Program>
{
    public TempDir Dir { get; } = new();
    public FakeOAuthClient OAuth { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mailpane:DataDir", Path.Combine(Dir.Path, "data"));
        builder.UseSetting("Mailpane:ConfigDir", Path.Combine(Dir.Path, "config"));
        builder.ConfigureTestServices(services => services.AddSingleton<IOAuthClient>(OAuth));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Dir.Dispose();
    }
}

public sealed class FakeOAuthClient : IOAuthClient
{
    public bool IsConfigured { get; set; } = true;
    public Func<string, string, OAuthGrant> Grant { get; set; } =
        (slug, code) => new OAuthGrant($"{slug}@example.com", "refresh-" + code, "https://www.googleapis.com/auth/gmail.readonly");
    public List<string> Forgotten { get; } = [];

    public Uri BuildAuthorizationUrl(string state) =>
        new("https://accounts.google.com/o/oauth2/v2/auth?state=" + Uri.EscapeDataString(state));

    public Task<OAuthGrant> CompleteAsync(string slug, string code, CancellationToken ct) => Task.FromResult(Grant(slug, code));

    public void Forget(string slug) => Forgotten.Add(slug);
}

using System.Net;
using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Mailpane.Core.Gmail;
using Mailpane.Core.Quota;
using Mailpane.Core.Storage;
using Mailpane.Gmail;
using Mailpane.Web.Security;
using Microsoft.AspNetCore.HostFiltering;

// Content root is the binary's directory, so appsettings.json and wwwroot are found whatever the
// working directory is.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Hard rule 6 / B7: request logs carry URLs, and URLs carry OAuth codes and search queries.
// In code, so it holds even if appsettings.json is missing.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);

// Mailpane:ConfigDir and Mailpane:DataDir override the XDG defaults (tests, trying things out).
var defaults = MailpanePaths.Default();
var paths = new MailpanePaths(
    builder.Configuration["Mailpane:ConfigDir"] ?? defaults.ConfigDir,
    builder.Configuration["Mailpane:DataDir"] ?? defaults.DataDir);
builder.Configuration.AddJsonFile(paths.ConfigFile, optional: true, reloadOnChange: false);
var port = builder.Configuration.GetValue("port", 8425);
var quotaUnitsPerMinute = builder.Configuration.GetValue("quotaUnitsPerMinute", 4500);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    // Hard rule 5: loopback only.
    kestrel.Listen(IPAddress.Loopback, port);
});

// Hard rule 5: the DNS-rebinding defence. Set in code so no configuration file can widen it.
builder.Services.PostConfigure<HostFilteringOptions>(o =>
{
    o.AllowedHosts = ["localhost"];
    o.AllowEmptyHosts = false;
    o.IncludeFailureMessage = false;
});

builder.Services.AddRazorPages();

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new AccountStore(paths.AccountsDb));
builder.Services.AddSingleton<PendingAuthorizations>();
builder.Services.AddSingleton(sp => new QuotaLimiters(quotaUnitsPerMinute, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<AccountTokenDataStore>();
builder.Services.AddSingleton(sp => new GoogleFlowProvider(paths.ClientSecretFile, sp.GetRequiredService<AccountTokenDataStore>()));
builder.Services.AddSingleton<GmailGateways>();
builder.Services.AddSingleton<IGmailGateways>(sp => sp.GetRequiredService<GmailGateways>());
builder.Services.AddSingleton<IOAuthClient>(sp => new GoogleOAuthClient(
    sp.GetRequiredService<GoogleFlowProvider>(), sp.GetRequiredService<GmailGateways>(),
    sp.GetRequiredService<AccountTokenDataStore>(), port));

var app = builder.Build();

// Fail at startup, not on the first request, if accounts.db cannot be created with the right mode.
app.Services.GetRequiredService<AccountStore>();

app.UseMiddleware<GetOnlyMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/static" });
app.MapRazorPages();

app.Logger.LogInformation("mailpane listening on http://localhost:{Port}/", port);
app.Run();

public partial class Program;

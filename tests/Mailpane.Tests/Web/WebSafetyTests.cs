using System.Net;
using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Mailpane.Web.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Mailpane.Tests.Web;

public sealed class WebSafetyTests : IDisposable
{
    private readonly MailpaneFactory _factory = new();
    private readonly HttpClient _client;

    public WebSafetyTests() => _client = _factory.Client();

    public void Dispose() => _factory.Dispose();

    private Task<HttpResponseMessage> Get(string path, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (host is not null)
            request.Headers.Host = host;
        return _client.SendAsync(request);
    }

    [Fact]
    public void Account_database_is_created_in_the_configured_data_dir()
    {
        _ = _factory.Services.GetRequiredService<AccountStore>();
        Assert.True(File.Exists(Path.Combine(_factory.Dir.Path, "data", "accounts.db")));
    }

    // B5: DNS-rebinding defence.
    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:8425")]
    [InlineData("127.0.0.1:8425")]
    [InlineData("localhost.evil.example")]
    public async Task Other_hosts_get_400(string host)
    {
        var response = await Get("/", host);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:8425")]
    public async Task Localhost_is_served(string host)
    {
        var response = await Get("/", host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // B3: nothing but GET (and HEAD).
    [Theory]
    [InlineData("POST", "/")]
    [InlineData("POST", "/accounts/add")]
    [InlineData("POST", "/oauth/callback")]
    [InlineData("PUT", "/")]
    [InlineData("DELETE", "/")]
    [InlineData("PATCH", "/")]
    [InlineData("OPTIONS", "/")]
    [InlineData("POST", "/work/")]
    [InlineData("POST", "/static/app.css")]
    public async Task Non_get_methods_get_405(string method, string path)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("GET, HEAD", response.Content.Headers.Allow.Count > 0
            ? string.Join(", ", response.Content.Headers.Allow)
            : response.Headers.GetValues("Allow").Single());
    }

    [Fact]
    public async Task No_cors_headers_are_sent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await _client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ARCHITECTURE.md 7.4
    [Theory]
    [InlineData("/")]
    [InlineData("/accounts/add")]
    [InlineData("/no-such-account/")]
    [InlineData("/static/app.css")]
    public async Task App_pages_send_security_headers(string path)
    {
        var response = await Get(path);
        Assert.Equal(SecurityHeadersMiddleware.AppPagePolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Home_explains_setup_when_there_is_no_oauth_client()
    {
        _factory.OAuth.IsConfigured = false;
        var html = await (await Get("/")).Content.ReadAsStringAsync();
        Assert.Contains("No OAuth client yet", html);
        Assert.DoesNotContain("/accounts/add\"", html);
    }

    [Fact]
    public async Task Unknown_account_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/nobody/")).StatusCode);
    }
}

public sealed class AddAccountFlowTests : IDisposable
{
    private readonly MailpaneFactory _factory = new();
    private readonly HttpClient _client;

    public AddAccountFlowTests() => _client = _factory.Client();

    public void Dispose() => _factory.Dispose();

    private AccountStore Accounts => _factory.Services.GetRequiredService<AccountStore>();

    private async Task<string> StartConsent(string slug)
    {
        var response = await _client.GetAsync($"/accounts/add?slug={slug}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("accounts.google.com", location.Host);
        return Uri.UnescapeDataString(location.Query.Split("state=")[1]);
    }

    [Fact]
    public async Task Consent_then_callback_stores_the_account_and_redirects_to_it()
    {
        var state = await StartConsent("work");
        var response = await _client.GetAsync($"/oauth/callback?state={Uri.EscapeDataString(state)}&code=abc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/work/", response.Headers.Location!.OriginalString);
        var account = Accounts.Find("work")!;
        Assert.Equal("work@example.com", account.Email);
        Assert.Equal("refresh-abc", account.RefreshToken);
    }

    [Fact]
    public async Task Callback_with_unknown_state_stores_nothing()
    {
        await StartConsent("work");
        var response = await _client.GetAsync("/oauth/callback?state=forged&code=abc");
        Assert.Equal("/accounts/add?error=state", response.Headers.Location!.OriginalString);
        Assert.Empty(Accounts.List());
    }

    [Fact]
    public async Task A_state_cannot_be_replayed()
    {
        var state = Uri.EscapeDataString(await StartConsent("work"));
        await _client.GetAsync($"/oauth/callback?state={state}&code=abc");
        var replay = await _client.GetAsync($"/oauth/callback?state={state}&code=abc");
        Assert.Equal("/accounts/add?error=state", replay.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Denied_consent_stores_nothing()
    {
        var state = Uri.EscapeDataString(await StartConsent("work"));
        var response = await _client.GetAsync($"/oauth/callback?state={state}&error=access_denied");
        Assert.Equal("/accounts/add?error=denied", response.Headers.Location!.OriginalString);
        Assert.Empty(Accounts.List());
    }

    [Fact]
    public async Task Same_mailbox_under_a_second_name_is_refused()
    {
        _factory.OAuth.Grant = (_, code) => new OAuthGrant("same@example.com", code, "https://www.googleapis.com/auth/gmail.readonly");
        var first = Uri.EscapeDataString(await StartConsent("work"));
        await _client.GetAsync($"/oauth/callback?state={first}&code=a");
        var second = Uri.EscapeDataString(await StartConsent("other"));
        var response = await _client.GetAsync($"/oauth/callback?state={second}&code=b");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already added", await response.Content.ReadAsStringAsync());
        Assert.Equal(["work"], Accounts.List().Select(a => a.Slug));
        Assert.Contains("other", _factory.OAuth.Forgotten);
    }

    [Fact]
    public async Task Failed_exchange_shows_the_reason_and_stores_nothing()
    {
        _factory.OAuth.Grant = (_, _) => throw new OAuthException("Google granted different scopes than gmail.readonly.");
        var state = Uri.EscapeDataString(await StartConsent("work"));
        var response = await _client.GetAsync($"/oauth/callback?state={state}&code=a");

        Assert.Contains("different scopes", await response.Content.ReadAsStringAsync());
        Assert.Empty(Accounts.List());
        Assert.Contains("work", _factory.OAuth.Forgotten);
    }

    [Theory]
    [InlineData("Bad")]
    [InlineData("accounts")]
    [InlineData("a%2Fb")]
    public async Task Invalid_slug_does_not_start_consent(string slug)
    {
        var response = await _client.GetAsync($"/accounts/add?slug={slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("lowercase letters", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Taken_slug_does_not_start_consent()
    {
        Accounts.Add("work", "w@example.com", "rt", "scope");
        var response = await _client.GetAsync("/accounts/add?slug=work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already used", await response.Content.ReadAsStringAsync());
    }

    // B7: URLs carry OAuth codes, and later search queries; request logging must not record them.
    [Fact]
    public async Task Logs_contain_no_codes_tokens_or_addresses()
    {
        var state = Uri.EscapeDataString(await StartConsent("work"));
        await _client.GetAsync($"/oauth/callback?state={state}&code=SECRETCODE");
        await _client.GetAsync("/work/?q=from:someone@example.com");

        var lines = _factory.Logs.Lines.ToList();
        Assert.Contains(lines, l => l.Contains("Account work added")); // the capture works
        Assert.DoesNotContain(lines, l => l.Contains("SECRETCODE"));
        Assert.DoesNotContain(lines, l => l.Contains("refresh-"));
        Assert.DoesNotContain(lines, l => l.Contains("@example.com"));
        Assert.DoesNotContain(lines, l => l.Contains(Uri.UnescapeDataString(state)));
    }

    [Fact]
    public async Task Error_codes_map_to_fixed_messages_only()
    {
        var html = await (await _client.GetAsync("/accounts/add?error=%3Cb%3Einjected%3C%2Fb%3E")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("injected", html);
    }
}

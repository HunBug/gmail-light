// S0 discovery spike. Throwaway. See docs/MILESTONE_1.md, session S0.
//
// Uses GmailService directly instead of the gateway and limiter (hard rule 7 exception for this
// tool only). Calls are few and sequential, far below the 6,000 units per minute cap.
//
// Read-only: requests gmail.readonly and calls only get/list methods.
//
// Everything it writes contains real mail and goes OUTSIDE the repo, to
// ~/.local/share/mailpane/spike/ (directory 700, files 600). Record only conclusions in DISCOVERIES.md.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

const string RedirectUri = "http://localhost:8425/oauth/callback";
const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
const UnixFileMode PrivateDir = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var clientSecretPath = Path.Combine(home, ".config", "mailpane", "client_secret.json");
var outDir = Path.Combine(home, ".local", "share", "mailpane", "spike");
var tokenPath = Path.Combine(outDir, "token.json");
var historyIdPath = Path.Combine(outDir, "history_id.txt");

if (args.Length == 0)
{
    Console.WriteLine("""
        usage: dotnet run --project tools/Spike -- <command>

          auth                       consent in the browser; stores a refresh token in the spike dir
          basics                     getProfile, labels.list, labels.get for every label,
                                     threads.list for INBOX; remembers the historyId
          query "<gmail query>" [n]  threads.list for the query, then threads.get metadata+full
                                     for the first n results (default 3)
          thread <id> [<id>...]      threads.get metadata+full for the given thread ids
          history                    history.list from the historyId remembered by 'basics'

        Output goes to ~/.local/share/mailpane/spike/. It contains real mail. Never copy it into the repo.
        """);
    return 1;
}

if (!File.Exists(clientSecretPath))
{
    Console.Error.WriteLine($"Missing {clientSecretPath}. Do parts 1 to 4 of docs/OAUTH_SETUP.md first.");
    return 1;
}

Directory.CreateDirectory(outDir, PrivateDir);
File.SetUnixFileMode(outDir, PrivateDir);

var secrets = GoogleClientSecrets.FromFile(clientSecretPath).Secrets;
var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
{
    ClientSecrets = secrets,
    Scopes = [GmailService.Scope.GmailReadonly],
    Prompt = "consent",
});

switch (args[0])
{
    case "auth":
        await Authorize();
        return 0;
    case "basics":
        await Basics(Connect());
        return 0;
    case "query" when args.Length >= 2:
        await Query(Connect(), args[1], args.Length >= 3 ? int.Parse(args[2]) : 3);
        return 0;
    case "thread" when args.Length >= 2:
        var service = Connect();
        foreach (var id in args.Skip(1))
            await DumpThread(service, id);
        return 0;
    case "history":
        await History(Connect());
        return 0;
    default:
        Console.Error.WriteLine("Unknown command or missing argument. Run without arguments for usage.");
        return 1;
}

async Task Authorize()
{
    var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    var request = flow.CreateAuthorizationCodeRequest(RedirectUri);
    request.State = state;
    var url = request.Build().AbsoluteUri;

    using var listener = new HttpListener();
    listener.Prefixes.Add("http://localhost:8425/oauth/");
    listener.Start();

    Console.WriteLine("Open this URL in the browser and pick the Gmail account to inspect:");
    Console.WriteLine();
    Console.WriteLine(url);
    Console.WriteLine();
    TryOpenBrowser(url);

    while (true)
    {
        var ctx = await listener.GetContextAsync();
        var query = ctx.Request.QueryString;
        if (ctx.Request.Url?.AbsolutePath != "/oauth/callback")
        {
            await Respond(ctx, 404, "Not found");
            continue;
        }
        if (query["state"] != state)
        {
            await Respond(ctx, 400, "State mismatch.");
            continue;
        }
        if (query["error"] is { } error)
        {
            await Respond(ctx, 400, "Consent failed: " + error);
            Console.Error.WriteLine("Consent failed: " + error);
            return;
        }

        var token = await flow.ExchangeCodeForTokenAsync("spike", query["code"], RedirectUri, CancellationToken.None);
        await Respond(ctx, 200, "Done. You can close this tab.");

        // [Q-08] Did we get a refresh token, and which scopes were granted?
        Console.WriteLine($"Refresh token returned: {token.RefreshToken is not null}");
        Console.WriteLine($"Granted scope: {token.Scope}");
        Console.WriteLine($"Access token lifetime (s): {token.ExpiresInSeconds}");

        WritePrivate(tokenPath, JsonSerializer.Serialize(new { refresh_token = token.RefreshToken, scope = token.Scope }));
        Console.WriteLine($"Stored refresh token in {tokenPath}");
        return;
    }
}

GmailService Connect()
{
    if (!File.Exists(tokenPath))
    {
        Console.Error.WriteLine("No token yet. Run 'auth' first.");
        Environment.Exit(1);
    }
    using var doc = JsonDocument.Parse(File.ReadAllText(tokenPath));
    var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString();
    var credential = new UserCredential(flow, "spike", new TokenResponse { RefreshToken = refreshToken });
    return new GmailService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "mailpane-spike",
    });
}

async Task Basics(GmailService gmail)
{
    var profile = await Dump("profile", gmail.Users.GetProfile("me"));
    var historyId = profile.RootElement.GetProperty("historyId").GetString()!;
    File.WriteAllText(historyIdPath, historyId);
    Console.WriteLine($"Remembered historyId {historyId}. Now change a label in official Gmail, then run 'history'.");

    // [Q-11] Does labels.list carry counts and colours, or only labels.get?
    var labels = await Dump("labels.list", gmail.Users.Labels.List("me"));
    foreach (var label in labels.RootElement.GetProperty("labels").EnumerateArray())
    {
        var id = label.GetProperty("id").GetString()!;
        await Dump($"labels.get.{Safe(id)}", gmail.Users.Labels.Get("me", id));
    }

    // [Q-13] Fields per thread and ordering.
    var list = gmail.Users.Threads.List("me");
    list.LabelIds = "INBOX";
    list.MaxResults = 20;
    await Dump("threads.list.INBOX", list);
}

async Task Query(GmailService gmail, string q, int n)
{
    var list = gmail.Users.Threads.List("me");
    list.Q = q;
    list.MaxResults = 25;
    var result = await Dump($"threads.list.q.{Safe(q)}", list);
    if (!result.RootElement.TryGetProperty("threads", out var threads))
    {
        Console.WriteLine("No threads matched.");
        return;
    }
    foreach (var t in threads.EnumerateArray().Take(n))
        await DumpThread(gmail, t.GetProperty("id").GetString()!);
}

async Task DumpThread(GmailService gmail, string id)
{
    // [Q-03] What metadata format returns. [Q-01] [Q-02] Encoding of bodies and headers in full format.
    var meta = gmail.Users.Threads.Get("me", id);
    meta.Format = UsersResource.ThreadsResource.GetRequest.FormatEnum.Metadata;
    meta.MetadataHeaders = new[] { "Subject", "From", "To", "Cc", "Date", "Content-Type" };
    await Dump($"threads.get.{Safe(id)}.metadata", meta);

    var full = gmail.Users.Threads.Get("me", id);
    full.Format = UsersResource.ThreadsResource.GetRequest.FormatEnum.Full;
    await Dump($"threads.get.{Safe(id)}.full", full);
}

async Task History(GmailService gmail)
{
    if (!File.Exists(historyIdPath))
    {
        Console.Error.WriteLine("Run 'basics' first so there is a historyId to start from.");
        return;
    }
    var request = gmail.Users.History.List("me");
    request.StartHistoryId = ulong.Parse(File.ReadAllText(historyIdPath).Trim());
    await Dump("history.list", request);
}

// Writes the raw HTTP response body, so fields the library model might drop are still visible.
async Task<JsonDocument> Dump<T>(string name, GmailBaseServiceRequest<T> request)
{
    var sw = Stopwatch.StartNew();
    await using var stream = await request.ExecuteAsStreamAsync();
    using var reader = new StreamReader(stream);
    var json = await reader.ReadToEndAsync();
    var doc = JsonDocument.Parse(json);
    var path = Path.Combine(outDir, name + ".dump.json");
    WritePrivate(path, JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{name}: {json.Length:N0} bytes, {sw.ElapsedMilliseconds} ms");
    return doc;
}

static string Safe(string s) =>
    new(s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').Take(60).ToArray());

static void WritePrivate(string path, string content)
{
    var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = PrivateFile };
    using (var fs = new FileStream(path, options))
    using (var writer = new StreamWriter(fs))
        writer.Write(content);
    File.SetUnixFileMode(path, PrivateFile);
}

static async Task Respond(HttpListenerContext ctx, int status, string text)
{
    ctx.Response.StatusCode = status;
    ctx.Response.ContentType = "text/plain; charset=utf-8";
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    await ctx.Response.OutputStream.WriteAsync(bytes);
    ctx.Response.Close();
}

static void TryOpenBrowser(string url)
{
    try { Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false })?.Dispose(); }
    catch { /* printing the URL is enough */ }
}

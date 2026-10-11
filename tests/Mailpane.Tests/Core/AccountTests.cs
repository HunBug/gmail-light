using Mailpane.Core.Accounts;
using Mailpane.Core.Auth;
using Mailpane.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Mailpane.Tests.Core;

public sealed class AccountSlugTests
{
    [Theory]
    [InlineData("work")]
    [InlineData("personal-2")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void Accepts(string slug) => Assert.True(AccountSlug.IsValid(slug));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Work")]
    [InlineData("-work")]
    [InlineData("work-")]
    [InlineData("wo--rk")]
    [InlineData("wo rk")]
    [InlineData("work/x")]
    [InlineData("..")]
    [InlineData("work.db")]
    [InlineData("accounts")]
    [InlineData("oauth")]
    [InlineData("static")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Rejects(string? slug) => Assert.False(AccountSlug.IsValid(slug));
}

public sealed class AccountStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string DbPath => Path.Combine(_dir.Path, "data", "accounts.db");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Creates_the_file_with_mode_600_and_the_directory_with_700()
    {
        _ = new AccountStore(DbPath);
        Assert.Equal(PrivateFiles.FileMode, File.GetUnixFileMode(DbPath));
        Assert.Equal(PrivateFiles.DirectoryMode, File.GetUnixFileMode(Path.GetDirectoryName(DbPath)!));
    }

    [Fact]
    public void Tightens_a_file_that_is_too_open()
    {
        _ = new AccountStore(DbPath);
        File.SetUnixFileMode(DbPath, PrivateFiles.FileMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        _ = new AccountStore(DbPath);
        Assert.Equal(PrivateFiles.FileMode, File.GetUnixFileMode(DbPath));
    }

    [Fact]
    public void Journal_files_are_not_more_open_than_the_database()
    {
        var store = new AccountStore(DbPath);
        store.Add("work", "w@example.com", "rt", "scope");
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(DbPath)!))
            Assert.Equal(PrivateFiles.FileMode, File.GetUnixFileMode(f));
    }

    [Fact]
    public void Adds_and_finds_accounts_and_survives_reopening()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var store = new AccountStore(DbPath, time);
        Assert.Equal(AddResult.Added, store.Add("work", "w@example.com", "rt-1", "scope"));
        Assert.Equal(AddResult.Added, store.Add("home", "h@example.com", "rt-2", "scope"));

        var reopened = new AccountStore(DbPath);
        var work = reopened.Find("work")!;
        Assert.Equal("w@example.com", work.Email);
        Assert.Equal("rt-1", work.RefreshToken);
        Assert.Equal(time.GetUtcNow(), work.AddedAt);
        Assert.Equal(["work", "home"], reopened.List().Select(a => a.Slug));
        Assert.NotEqual(work.Color, reopened.Find("home")!.Color);
        Assert.Null(reopened.Find("other"));
    }

    [Fact]
    public void Refuses_duplicate_slug_or_address_and_invalid_slug()
    {
        var store = new AccountStore(DbPath);
        store.Add("work", "w@example.com", "rt", "scope");
        Assert.Equal(AddResult.SlugTaken, store.Add("work", "other@example.com", "rt", "scope"));
        Assert.Equal(AddResult.EmailTaken, store.Add("other", "w@example.com", "rt", "scope"));
        Assert.Equal(AddResult.InvalidSlug, store.Add("Bad Slug", "x@example.com", "rt", "scope"));
        Assert.Single(store.List());
    }

    [Fact]
    public void Updates_the_refresh_token()
    {
        var store = new AccountStore(DbPath);
        store.Add("work", "w@example.com", "rt-1", "scope");
        Assert.False(store.UpdateRefreshToken("work", "rt-1"));
        Assert.True(store.UpdateRefreshToken("work", "rt-2"));
        Assert.False(store.UpdateRefreshToken("missing", "rt-2"));
        Assert.Equal("rt-2", store.Find("work")!.RefreshToken);
    }

    [Fact]
    public void ToString_does_not_leak_token_or_address()
    {
        var account = new Account("work", "w@example.com", "#000", "secret-token", "scope", DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain("secret-token", account.ToString());
        Assert.DoesNotContain("w@example.com", account.ToString());
    }
}

public sealed class PendingAuthorizationsTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void A_state_works_once()
    {
        var pending = new PendingAuthorizations(_time);
        var state = pending.Begin("work");
        Assert.Equal("work", pending.Complete(state));
        Assert.Null(pending.Complete(state));
    }

    [Fact]
    public void Unknown_or_empty_state_is_refused()
    {
        var pending = new PendingAuthorizations(_time);
        pending.Begin("work");
        Assert.Null(pending.Complete("guess"));
        Assert.Null(pending.Complete(""));
        Assert.Null(pending.Complete(null));
    }

    [Fact]
    public void A_state_expires_after_ten_minutes()
    {
        var pending = new PendingAuthorizations(_time);
        var state = pending.Begin("work");
        _time.Advance(PendingAuthorizations.Lifetime);
        Assert.Null(pending.Complete(state));
    }

    [Fact]
    public void Only_a_bounded_number_are_kept_and_the_oldest_goes_first()
    {
        var pending = new PendingAuthorizations(_time);
        var first = pending.Begin("first");
        _time.Advance(TimeSpan.FromSeconds(1));
        var states = Enumerable.Range(0, PendingAuthorizations.MaxPending).Select(i => pending.Begin($"s{i}")).ToList();
        Assert.Null(pending.Complete(first));
        Assert.Equal("s0", pending.Complete(states[0]));
    }

    [Fact]
    public void States_are_unguessable_and_url_safe()
    {
        var pending = new PendingAuthorizations(_time);
        var a = pending.Begin("work");
        var b = pending.Begin("work");
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
    }
}

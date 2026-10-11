using System.Text.RegularExpressions;

namespace Mailpane.Tests.Rules;

/// <summary>
/// Hard rules from CLAUDE.md checked against the source tree, so a violation fails the build instead
/// of waiting for review. Acceptance B1 and B2 in docs/MILESTONE_1.md.
/// </summary>
public sealed partial class SourceRulesTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mailpane.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Mailpane.sln not found above the test output.");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string relativeDir, string pattern = "*.cs") =>
        Directory.EnumerateFiles(Path.Combine(Root, relativeDir), pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(Root, p), File.ReadAllText(p)));

    private static IEnumerable<(string Path, string Text)> AllCode() => Sources("src").Concat(Sources("tools"));

    // Any call reached through the Users resource whose final method mutates the mailbox.
    [GeneratedRegex(@"Users\s*\.(\s*\w+\s*\.)*\s*(Modify|BatchModify|BatchDelete|Delete|Trash|Untrash|Send|Import|Insert|Create|Update|Patch|Watch|Stop)\s*\(")]
    private static partial Regex MutatingCall();

    [GeneratedRegex(@"\.\s*(Drafts|Settings)\b")]
    private static partial Regex WriteCapableResource();

    [GeneratedRegex(@"GmailService\s*\.\s*Scope\s*\.\s*(?!GmailReadonly\b)\w+")]
    private static partial Regex OtherScopeConstant();

    [GeneratedRegex(@"auth/gmail\.(?!readonly"")\w+|mail\.google\.com/""")]
    private static partial Regex OtherScopeLiteral();

    [Fact]
    public void B2_no_mutating_gmail_method_is_referenced()
    {
        var hits = AllCode().Where(f => MutatingCall().IsMatch(f.Text) || WriteCapableResource().IsMatch(f.Text))
            .Select(f => f.Path).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void B1_no_scope_other_than_gmail_readonly_is_requested()
    {
        var hits = AllCode().Where(f => OtherScopeConstant().IsMatch(f.Text) || OtherScopeLiteral().IsMatch(f.Text))
            .Select(f => f.Path).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Rule7_only_the_gateway_project_uses_google_types()
    {
        var hits = Sources("src")
            .Where(f => !f.Path.StartsWith(Path.Combine("src", "Mailpane.Gmail")))
            .Where(f => Regex.IsMatch(f.Text, @"\bGoogle\.Apis\b|\bGmailService\b"))
            .Select(f => f.Path).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Core_has_no_aspnet_or_google_dependency()
    {
        var csproj = File.ReadAllText(Path.Combine(Root, "src", "Mailpane.Core", "Mailpane.Core.csproj"));
        Assert.DoesNotContain("Google.", csproj);
        Assert.DoesNotContain("Microsoft.AspNetCore", csproj);
        Assert.DoesNotContain("Sdk.Web", csproj);
        Assert.DoesNotContain(Sources(Path.Combine("src", "Mailpane.Core")), f => f.Text.Contains("Microsoft.AspNetCore"));
    }

    [Fact]
    public void Rule3_views_never_use_Html_Raw()
    {
        var hits = Sources(Path.Combine("src", "Mailpane.Web"), "*.cshtml")
            .Where(f => Regex.IsMatch(f.Text, @"Html\s*\.\s*Raw\s*\(")).Select(f => f.Path).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Rule3_no_iframe_sandbox_allows_scripts()
    {
        var hits = Sources(Path.Combine("src", "Mailpane.Web"), "*.*")
            .Where(f => f.Text.Contains("allow-scripts")).Select(f => f.Path).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Rule_detectors_actually_detect()
    {
        Assert.Matches(MutatingCall(), "service.Users.Messages.Trash(\"me\", id)");
        Assert.Matches(MutatingCall(), "gmail.Users.Threads.Modify(body, \"me\", id)");
        Assert.Matches(MutatingCall(), "Users.Messages.Send(msg, \"me\")");
        Assert.DoesNotMatch(MutatingCall(), "service.Users.Threads.Get(\"me\", id)");
        Assert.Matches(WriteCapableResource(), "service.Users.Drafts.List(\"me\")");
        Assert.Matches(OtherScopeConstant(), "GmailService.Scope.GmailModify");
        Assert.DoesNotMatch(OtherScopeConstant(), "GmailService.Scope.GmailReadonly");
        Assert.Matches(OtherScopeLiteral(), "\"https://www.googleapis.com/auth/gmail.modify\"");
        Assert.Matches(OtherScopeLiteral(), "\"https://mail.google.com/\"");
        Assert.DoesNotMatch(OtherScopeLiteral(), "\"https://www.googleapis.com/auth/gmail.readonly\"");
    }
}

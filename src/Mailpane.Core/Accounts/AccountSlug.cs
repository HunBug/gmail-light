using System.Text.RegularExpressions;

namespace Mailpane.Core.Accounts;

/// <summary>
/// The URL prefix of an account (ARCHITECTURE.md 4). Also used as a file name for the cache, so the
/// character set matters for safety, not only for looks.
/// </summary>
public static partial class AccountSlug
{
    public const int MaxLength = 32;

    private static readonly HashSet<string> Reserved = ["accounts", "oauth", "static"];

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? slug) =>
        slug is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(slug) && !Reserved.Contains(slug);
}

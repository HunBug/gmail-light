namespace Mailpane.Core.Storage;

/// <summary>Local file locations (CLAUDE.md, "Local paths").</summary>
public sealed record MailpanePaths(string ConfigDir, string DataDir)
{
    public string ClientSecretFile => Path.Combine(ConfigDir, "client_secret.json");
    public string ConfigFile => Path.Combine(ConfigDir, "config.json");
    public string AccountsDb => Path.Combine(DataDir, "accounts.db");
    public string CacheDir => Path.Combine(DataDir, "cache");
    public string CacheDb(string slug) => Path.Combine(CacheDir, slug + ".db");

    /// <summary>XDG defaults: ~/.config/mailpane and ~/.local/share/mailpane.</summary>
    public static MailpanePaths Default()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(home, ".local", "share");
        return new MailpanePaths(Path.Combine(config, "mailpane"), Path.Combine(data, "mailpane"));
    }
}

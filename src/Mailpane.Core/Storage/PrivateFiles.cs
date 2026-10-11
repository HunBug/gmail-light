namespace Mailpane.Core.Storage;

/// <summary>
/// Creates directories and files readable only by the current user. Used for everything under the data
/// directory: accounts.db holds refresh tokens and the cache holds subjects and addresses.
/// </summary>
public static class PrivateFiles
{
    public const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    public const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path, DirectoryMode);
        if (File.GetUnixFileMode(path) != DirectoryMode)
            File.SetUnixFileMode(path, DirectoryMode);
    }

    /// <summary>
    /// Makes sure <paramref name="path"/> exists with mode 600 before SQLite opens it. SQLite gives its
    /// journal and WAL files the same mode as the database file.
    /// </summary>
    public static void EnsureFile(string path)
    {
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path))
        {
            var options = new FileStreamOptions
            {
                Mode = System.IO.FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = FileMode,
            };
            try { new FileStream(path, options).Dispose(); }
            catch (IOException) when (File.Exists(path)) { /* created concurrently; fixed below */ }
        }
        if (File.GetUnixFileMode(path) != FileMode)
            File.SetUnixFileMode(path, FileMode);
    }
}

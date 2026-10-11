using Microsoft.Data.Sqlite;

namespace Mailpane.Tests;

/// <summary>A scratch directory, removed on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("mailpane-test-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

using Mailpane.Core.Cache;
using Mailpane.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Mailpane.Tests.Core;

public sealed class CacheDatabaseTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string DbPath => Path.Combine(_dir.Path, "cache", "work.db");

    public void Dispose() => _dir.Dispose();

    private static List<string> Tables(CacheDatabase cache)
    {
        using var db = cache.Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        var names = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            names.Add(r.GetString(0));
        return names;
    }

    private static void InsertThread(CacheDatabase cache, string id)
    {
        using var db = cache.Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO threads (id, history_id, participants, message_count, last_message_at, fetched_at) VALUES ($id, '1', '[]', 1, 0, 0)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static long CountThreads(CacheDatabase cache)
    {
        using var db = cache.Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM threads";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Creates_the_schema_with_mode_600()
    {
        var cache = CacheDatabase.Open(DbPath);
        Assert.Equal(["labels", "message_labels", "messages", "meta", "thread_labels", "threads"], Tables(cache));
        Assert.Equal(CacheDatabase.SchemaVersion.ToString(), cache.GetMeta("schema_version"));
        Assert.Equal(PrivateFiles.FileMode, File.GetUnixFileMode(DbPath));
    }

    [Fact]
    public void Keeps_data_when_the_schema_matches()
    {
        InsertThread(CacheDatabase.Open(DbPath), "t1");
        Assert.Equal(1, CountThreads(CacheDatabase.Open(DbPath)));
    }

    [Fact]
    public void Rebuilds_when_the_schema_version_differs()
    {
        var cache = CacheDatabase.Open(DbPath);
        InsertThread(cache, "t1");
        cache.SetMeta("schema_version", "0");

        var reopened = CacheDatabase.Open(DbPath);
        Assert.Equal(0, CountThreads(reopened));
        Assert.Equal(CacheDatabase.SchemaVersion.ToString(), reopened.GetMeta("schema_version"));
    }

    [Fact]
    public void Rebuilds_a_file_that_is_not_a_database()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        File.WriteAllText(DbPath, "this is not sqlite, just some text that is long enough to have a header");
        var cache = CacheDatabase.Open(DbPath);
        Assert.Equal(CacheDatabase.SchemaVersion.ToString(), cache.GetMeta("schema_version"));
    }

    [Fact]
    public void Deleting_a_thread_cascades_to_its_messages_and_labels()
    {
        var cache = CacheDatabase.Open(DbPath);
        InsertThread(cache, "t1");
        using var db = cache.Connect();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO messages (id, thread_id, internal_date) VALUES ('m1', 't1', 0);
                INSERT INTO message_labels (message_id, label_id) VALUES ('m1', 'INBOX');
                INSERT INTO thread_labels (label_id, thread_id, last_message_at) VALUES ('INBOX', 't1', 0);
                DELETE FROM threads WHERE id = 't1';
                """;
            cmd.ExecuteNonQuery();
        }
        foreach (var table in new[] { "messages", "message_labels", "thread_labels" })
        {
            using var count = db.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table}";
            Assert.Equal(0L, (long)count.ExecuteScalar()!);
        }
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        var cache = CacheDatabase.Open(DbPath);
        using var db = cache.Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO messages (id, thread_id, internal_date) VALUES ('m1', 'missing', 0)";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }
}

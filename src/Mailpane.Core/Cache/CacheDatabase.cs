using Mailpane.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Mailpane.Core.Cache;

/// <summary>
/// cache/&lt;slug&gt;.db (ARCHITECTURE.md 5.2). A rebuildable projection of Gmail (D-006): there are no
/// migrations. A different <c>schema_version</c>, or a file that is not a readable database, means the
/// file is deleted and created again.
/// </summary>
public sealed class CacheDatabase
{
    /// <summary>Bump whenever <see cref="Schema"/> changes.</summary>
    public const int SchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE meta (
          key   TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );

        CREATE TABLE labels (
          id              TEXT PRIMARY KEY,
          name            TEXT NOT NULL,
          type            TEXT NOT NULL,
          bg_color        TEXT,
          text_color      TEXT,
          list_visibility TEXT,
          threads_total   INTEGER,
          threads_unread  INTEGER,
          fetched_at      INTEGER NOT NULL
        );

        CREATE TABLE threads (
          id              TEXT PRIMARY KEY,
          history_id      TEXT NOT NULL,
          subject         TEXT,
          snippet         TEXT,
          participants    TEXT NOT NULL,
          message_count   INTEGER NOT NULL,
          last_message_at INTEGER NOT NULL,
          fetched_at      INTEGER NOT NULL
        );

        CREATE TABLE messages (
          id            TEXT PRIMARY KEY,
          thread_id     TEXT NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
          internal_date INTEGER NOT NULL,
          from_addr     TEXT,
          to_addrs      TEXT,
          cc_addrs      TEXT,
          subject       TEXT,
          snippet       TEXT,
          size_estimate INTEGER
        );
        CREATE INDEX messages_thread ON messages (thread_id);

        CREATE TABLE message_labels (
          message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
          label_id   TEXT NOT NULL,
          PRIMARY KEY (message_id, label_id)
        );

        CREATE TABLE thread_labels (
          label_id        TEXT NOT NULL,
          thread_id       TEXT NOT NULL REFERENCES threads(id) ON DELETE CASCADE,
          last_message_at INTEGER NOT NULL,
          PRIMARY KEY (label_id, thread_id)
        );
        CREATE INDEX thread_labels_order ON thread_labels (label_id, last_message_at DESC);
        CREATE INDEX thread_labels_thread ON thread_labels (thread_id);
        """;

    private readonly string _path;
    private readonly string _connectionString;

    private CacheDatabase(string path)
    {
        _path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
        }.ToString();
    }

    public string Path => _path;

    /// <summary>Opens the cache at <paramref name="path"/>, creating or rebuilding it as needed.</summary>
    public static CacheDatabase Open(string path)
    {
        var cache = new CacheDatabase(path);
        if (!cache.HasCurrentSchema())
            cache.Recreate();
        return cache;
    }

    public SqliteConnection Connect()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    public string? GetMeta(string key)
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private bool HasCurrentSchema()
    {
        if (!File.Exists(_path))
            return false;
        try
        {
            return GetMeta("schema_version") == SchemaVersion.ToString();
        }
        catch (SqliteException)
        {
            return false; // missing table, or not a database at all
        }
    }

    private void Recreate()
    {
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
        PrivateFiles.EnsureDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            File.Delete(_path + suffix);
        PrivateFiles.EnsureFile(_path);

        using var db = Connect();
        using var tx = db.BeginTransaction();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO meta (key, value) VALUES ('schema_version', $v)";
            cmd.Parameters.AddWithValue("$v", SchemaVersion.ToString());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}

using Mailpane.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Mailpane.Core.Accounts;

/// <summary>
/// accounts.db (ARCHITECTURE.md 5.1): the only precious local state. Created with mode 600.
/// </summary>
public sealed class AccountStore
{
    // Distinct, readable as tab favicon and header colours. Assigned in order of adding.
    private static readonly string[] Palette = ["#1a73e8", "#d93025", "#188038", "#e37400", "#9334e6", "#007b83"];

    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public AccountStore(string path, TimeProvider? time = null)
    {
        PrivateFiles.EnsureFile(path);
        // ReadWrite, not ReadWriteCreate: the file must already exist with the mode set above.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString();
        _time = time ?? TimeProvider.System;

        using var db = Open();
        Execute(db, """
            CREATE TABLE IF NOT EXISTS accounts (
              id            INTEGER PRIMARY KEY,
              slug          TEXT NOT NULL UNIQUE,
              email         TEXT NOT NULL UNIQUE,
              color         TEXT NOT NULL,
              refresh_token TEXT NOT NULL,
              scopes        TEXT NOT NULL,
              added_at      INTEGER NOT NULL
            );
            """);
    }

    public IReadOnlyList<Account> List()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT slug, email, color, refresh_token, scopes, added_at FROM accounts ORDER BY id";
        return ReadAll(cmd);
    }

    public Account? Find(string slug)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT slug, email, color, refresh_token, scopes, added_at FROM accounts WHERE slug = $slug";
        cmd.Parameters.AddWithValue("$slug", slug);
        return ReadAll(cmd).SingleOrDefault();
    }

    /// <summary>Adds an account and picks its colour. Fails if the slug or the address is taken.</summary>
    public AddResult Add(string slug, string email, string refreshToken, string scopes)
    {
        if (!AccountSlug.IsValid(slug))
            return AddResult.InvalidSlug;

        using var db = Open();
        using var tx = db.BeginTransaction();

        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT slug = $slug FROM accounts WHERE slug = $slug OR email = $email LIMIT 1";
            check.Parameters.AddWithValue("$slug", slug);
            check.Parameters.AddWithValue("$email", email);
            var hit = check.ExecuteScalar();
            if (hit is long sameSlug)
                return sameSlug == 1 ? AddResult.SlugTaken : AddResult.EmailTaken;
        }

        long count;
        using (var countCmd = db.CreateCommand())
        {
            countCmd.Transaction = tx;
            countCmd.CommandText = "SELECT COUNT(*) FROM accounts";
            count = (long)countCmd.ExecuteScalar()!;
        }

        using (var insert = db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO accounts (slug, email, color, refresh_token, scopes, added_at)
                VALUES ($slug, $email, $color, $token, $scopes, $added)
                """;
            insert.Parameters.AddWithValue("$slug", slug);
            insert.Parameters.AddWithValue("$email", email);
            insert.Parameters.AddWithValue("$color", Palette[count % Palette.Length]);
            insert.Parameters.AddWithValue("$token", refreshToken);
            insert.Parameters.AddWithValue("$scopes", scopes);
            insert.Parameters.AddWithValue("$added", _time.GetUtcNow().ToUnixTimeSeconds());
            insert.ExecuteNonQuery();
        }

        tx.Commit();
        return AddResult.Added;
    }

    /// <summary>Google may rotate the refresh token on refresh; keep the newest one.</summary>
    public bool UpdateRefreshToken(string slug, string refreshToken)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET refresh_token = $token WHERE slug = $slug AND refresh_token <> $token";
        cmd.Parameters.AddWithValue("$slug", slug);
        cmd.Parameters.AddWithValue("$token", refreshToken);
        return cmd.ExecuteNonQuery() > 0;
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<Account> ReadAll(SqliteCommand cmd)
    {
        var result = new List<Account>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5))));
        return result;
    }
}

public enum AddResult { Added, InvalidSlug, SlugTaken, EmailTaken }

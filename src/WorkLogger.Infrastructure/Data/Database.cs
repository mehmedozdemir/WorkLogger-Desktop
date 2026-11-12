using Dapper;
using Microsoft.Data.Sqlite;

namespace WorkLogger.Infrastructure.Data;

/// <summary>
/// SQLite bağlantı fabrikası ve şema göçleri. Agent ve Desktop aynı dosyayı WAL modunda paylaşır;
/// her işlem kısa ömürlü bir bağlantı açar (havuzlama açık).
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 15,
        }.ToString();
    }

    public string Path { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL;");
        return connection;
    }

    public int SchemaVersion
    {
        get
        {
            using var c = Open();
            return c.ExecuteScalar<int>("PRAGMA user_version");
        }
    }

    /// <summary>Eksik göçleri sırayla uygular. Her göç kendi transaction'ında çalışır.</summary>
    public void Migrate()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        var current = connection.ExecuteScalar<int>("PRAGMA user_version");
        for (var version = current + 1; version <= Migrations.Count; version++)
        {
            using var tx = connection.BeginTransaction();
            connection.Execute(Migrations[version - 1], transaction: tx);
            connection.Execute($"PRAGMA user_version = {version}", transaction: tx);
            tx.Commit();
        }
    }

    private static readonly IReadOnlyList<string> Migrations =
    [
        // v1 — temel şema
        """
        CREATE TABLE segments (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            day       TEXT    NOT NULL,
            start_ms  INTEGER NOT NULL,
            end_ms    INTEGER NOT NULL,
            app       TEXT    NOT NULL,
            title     TEXT    NOT NULL,
            idle      INTEGER NOT NULL DEFAULT 0,
            source    TEXT    NOT NULL DEFAULT 'agent'
        );
        CREATE INDEX ix_segments_start ON segments(start_ms);
        CREATE INDEX ix_segments_day   ON segments(day);

        CREATE TABLE commits (
            sha       TEXT PRIMARY KEY,
            repo      TEXT    NOT NULL,
            branch    TEXT    NOT NULL DEFAULT '',
            message   TEXT    NOT NULL DEFAULT '',
            ts_ms     INTEGER NOT NULL,
            day       TEXT    NOT NULL,
            files     INTEGER NOT NULL DEFAULT 0,
            ins       INTEGER NOT NULL DEFAULT 0,
            del       INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_commits_ts ON commits(ts_ms);

        CREATE TABLE agent_state (
            key        TEXT PRIMARY KEY,
            value      TEXT,
            updated_ms INTEGER NOT NULL
        );
        """,
    ];
}

using Dapper;
using Microsoft.Data.Sqlite;
using WorkLogger.Core.Model;
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Infrastructure.Import;

public sealed record ImportResult(int Segments, int Commits, DateOnly? FirstDay, DateOnly? LastDay);

/// <summary>
/// Önceki Python sürümünün (tempo-tracker) activity.db dosyasını içe aktarır.
/// Tekrar çalıştırılabilir: önceki içe aktarımı silip yeniden yazar, commit'ler sha ile tekilleşir.
/// </summary>
public sealed class LegacyTrackerImporter(Database db, ActivityRepository activity)
{
    public ImportResult Import(string legacyDbPath)
    {
        if (!File.Exists(legacyDbPath))
            throw new FileNotFoundException("Eski veritabanı bulunamadı.", legacyDbPath);

        var source = new SqliteConnectionStringBuilder { DataSource = legacyDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        List<LegacySegment> segments;
        List<LegacyCommit> commits;
        using (var legacy = new SqliteConnection(source))
        {
            legacy.Open();
            segments = legacy.Query<LegacySegment>(
                "SELECT start_ts AS StartTs, end_ts AS EndTs, app AS App, title AS Title, idle AS Idle FROM segments ORDER BY start_ts").ToList();
            commits = HasTable(legacy, "commits")
                ? legacy.Query<LegacyCommit>("SELECT repo AS Repo, sha AS Sha, branch AS Branch, message AS Message, ts AS Ts FROM commits").ToList()
                : [];
        }

        using (var c = db.Open())
        using (var tx = c.BeginTransaction())
        {
            c.Execute("DELETE FROM segments WHERE source = @source", new { source = ActivityRepository.SourcePythonImport }, tx);
            foreach (var s in segments)
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds((long)(s.StartTs * 1000)).ToLocalTime();
                var end = DateTimeOffset.FromUnixTimeMilliseconds((long)(s.EndTs * 1000)).ToLocalTime();
                if (end <= start)
                    continue;
                c.Execute(
                    "INSERT INTO segments (day, start_ms, end_ms, app, title, idle, source) VALUES (@day, @start, @end, @app, @title, @idle, @source)",
                    new
                    {
                        day = ActivityRepository.DayKey(start),
                        start = start.ToUnixTimeMilliseconds(),
                        end = end.ToUnixTimeMilliseconds(),
                        app = s.App ?? "",
                        title = s.Title ?? "",
                        idle = s.Idle != 0 ? 1 : 0,
                        source = ActivityRepository.SourcePythonImport,
                    },
                    tx);
            }
            tx.Commit();
        }

        var imported = 0;
        foreach (var commit in commits)
        {
            var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)(commit.Ts * 1000)).ToLocalTime();
            if (activity.UpsertCommit(new CommitInfo(commit.Repo ?? "", commit.Sha ?? "", commit.Branch ?? "", commit.Message ?? "", ts)))
                imported++;
        }

        DateOnly? first = segments.Count > 0 ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(segments[0].StartTs * 1000)).LocalDateTime) : null;
        DateOnly? last = segments.Count > 0 ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(segments[^1].StartTs * 1000)).LocalDateTime) : null;
        return new ImportResult(segments.Count, imported, first, last);
    }

    private static bool HasTable(SqliteConnection c, string name) =>
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name", new { name }) > 0;

    private sealed class LegacySegment
    {
        public double StartTs { get; set; }
        public double EndTs { get; set; }
        public string? App { get; set; }
        public string? Title { get; set; }
        public long Idle { get; set; }
    }

    private sealed class LegacyCommit
    {
        public string? Repo { get; set; }
        public string? Sha { get; set; }
        public string? Branch { get; set; }
        public string? Message { get; set; }
        public double Ts { get; set; }
    }
}

using System.Globalization;
using Dapper;
using WorkLogger.Core.Model;

namespace WorkLogger.Infrastructure.Data;

public sealed class ActivityRepository(Database db)
{
    public const string SourceAgent = "agent";
    public const string SourcePythonImport = "python-import";

    // ------------------------------------------------------------------ segments

    /// <summary>Yeni (açık) segment ekler; bitiş, izleme sürdükçe güncellenir.</summary>
    public long InsertSegment(DateTimeOffset start, DateTimeOffset end, string app, string title, bool idle, string source = SourceAgent)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>(
            "INSERT INTO segments (day, start_ms, end_ms, app, title, idle, source) " +
            "VALUES (@day, @start, @end, @app, @title, @idle, @source); SELECT last_insert_rowid();",
            new { day = DayKey(start), start = start.ToUnixTimeMilliseconds(), end = end.ToUnixTimeMilliseconds(), app, title, idle = idle ? 1 : 0, source });
    }

    public void UpdateSegmentEnd(long id, DateTimeOffset end)
    {
        using var c = db.Open();
        c.Execute("UPDATE segments SET end_ms = @end WHERE id = @id", new { id, end = end.ToUnixTimeMilliseconds() });
    }

    /// <summary>Segmenti kapatır; bir saniyeden kısa kalan segment gürültü sayılıp silinir.</summary>
    public void CloseSegment(long id, DateTimeOffset end)
    {
        using var c = db.Open();
        c.Execute(
            "UPDATE segments SET end_ms = @end WHERE id = @id; " +
            "DELETE FROM segments WHERE id = @id AND end_ms - start_ms < 1000;",
            new { id, end = end.ToUnixTimeMilliseconds() });
    }

    public IReadOnlyList<ActivitySegment> Segments(DateTimeOffset from, DateTimeOffset to)
    {
        using var c = db.Open();
        return c.Query<SegmentRow>(
                "SELECT id AS Id, start_ms AS StartMs, end_ms AS EndMs, app AS App, title AS Title, idle AS Idle " +
                "FROM segments WHERE end_ms > @from AND start_ms < @to ORDER BY start_ms",
                new { from = from.ToUnixTimeMilliseconds(), to = to.ToUnixTimeMilliseconds() })
            .Select(r => r.ToModel())
            .ToList();
    }

    public IReadOnlyList<ActivitySegment> SegmentsForDays(DateOnly from, DateOnly to) =>
        Segments(StartOf(from), StartOf(to.AddDays(1)));

    public DateOnly? FirstDay()
    {
        using var c = db.Open();
        var value = c.ExecuteScalar<string?>("SELECT MIN(day) FROM segments");
        return value is null ? null : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public long CountSegments(string? source = null)
    {
        using var c = db.Open();
        return source is null
            ? c.ExecuteScalar<long>("SELECT COUNT(*) FROM segments")
            : c.ExecuteScalar<long>("SELECT COUNT(*) FROM segments WHERE source = @source", new { source });
    }

    // ------------------------------------------------------------------ commits

    /// <summary>Commit yoksa ekler. Yeni eklendiyse true döner.</summary>
    public bool UpsertCommit(CommitInfo commit)
    {
        using var c = db.Open();
        var inserted = c.Execute(
            "INSERT OR IGNORE INTO commits (sha, repo, branch, message, ts_ms, day, files, ins, del) " +
            "VALUES (@Sha, @Repo, @Branch, @Message, @ts, @day, @FilesChanged, @Insertions, @Deletions)",
            new
            {
                commit.Sha,
                commit.Repo,
                commit.Branch,
                commit.Message,
                ts = commit.Timestamp.ToUnixTimeMilliseconds(),
                day = DayKey(commit.Timestamp),
                commit.FilesChanged,
                commit.Insertions,
                commit.Deletions,
            });
        return inserted > 0;
    }

    public IReadOnlyList<CommitInfo> Commits(DateTimeOffset from, DateTimeOffset to)
    {
        using var c = db.Open();
        return c.Query<CommitRow>(
                "SELECT sha AS Sha, repo AS Repo, branch AS Branch, message AS Message, ts_ms AS TsMs, " +
                "files AS Files, ins AS Ins, del AS Del FROM commits WHERE ts_ms >= @from AND ts_ms < @to ORDER BY ts_ms",
                new { from = from.ToUnixTimeMilliseconds(), to = to.ToUnixTimeMilliseconds() })
            .Select(r => r.ToModel())
            .ToList();
    }

    public IReadOnlyList<CommitInfo> CommitsForDays(DateOnly from, DateOnly to) =>
        Commits(StartOf(from), StartOf(to.AddDays(1)));

    // ------------------------------------------------------------------ yardımcılar

    public static string DayKey(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTimeOffset StartOf(DateOnly day)
    {
        var dt = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    private sealed class SegmentRow
    {
        public long Id { get; set; }
        public long StartMs { get; set; }
        public long EndMs { get; set; }
        public string App { get; set; } = "";
        public string Title { get; set; } = "";
        public long Idle { get; set; }

        public ActivitySegment ToModel() => new(
            Id,
            DateTimeOffset.FromUnixTimeMilliseconds(StartMs).ToLocalTime(),
            DateTimeOffset.FromUnixTimeMilliseconds(EndMs).ToLocalTime(),
            App,
            Title,
            Idle != 0);
    }

    private sealed class CommitRow
    {
        public string Sha { get; set; } = "";
        public string Repo { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Message { get; set; } = "";
        public long TsMs { get; set; }
        public long Files { get; set; }
        public long Ins { get; set; }
        public long Del { get; set; }

        public CommitInfo ToModel() => new(
            Repo,
            Sha,
            Branch,
            Message,
            DateTimeOffset.FromUnixTimeMilliseconds(TsMs).ToLocalTime(),
            (int)Files,
            (int)Ins,
            (int)Del);
    }
}

using System.Globalization;
using Dapper;
using WorkLogger.Core.Classification;

namespace WorkLogger.Infrastructure.Data;

public sealed record WorklogDraft(DateOnly Day, string IssueKey, int Minutes, string StartTime, string Description, bool Manual);

public sealed record DayClosure(DateOnly Day, DateTimeOffset ClosedAt, int ActiveMinutes, int WorklogMinutes, string Summary);

/// <summary>
/// Kullanıcının gün sonu emeği: worklog taslakları, elle yapılan atamalar ve gün kapanışları.
/// Ham izleme verisine hiç dokunmaz; analiz bu kayıtları üstüne uygular.
/// </summary>
public sealed class JournalRepository(Database db)
{
    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ------------------------------------------------------------------ worklog taslakları

    public IReadOnlyList<WorklogDraft> Drafts(DateOnly day)
    {
        using var c = db.Open();
        return c.Query<DraftRow>(
                "SELECT issue_key AS IssueKey, minutes AS Minutes, start_time AS StartTime, description AS Description, manual AS Manual " +
                "FROM worklog_drafts WHERE day = @day ORDER BY issue_key",
                new { day = Key(day) })
            .Select(r => new WorklogDraft(day, r.IssueKey, (int)r.Minutes, r.StartTime, r.Description, r.Manual != 0))
            .ToList();
    }

    public void SaveDraft(WorklogDraft draft)
    {
        using var c = db.Open();
        c.Execute(
            "INSERT INTO worklog_drafts (day, issue_key, minutes, start_time, description, manual, updated_ms) " +
            "VALUES (@day, @IssueKey, @Minutes, @StartTime, @Description, @manual, @now) " +
            "ON CONFLICT(day, issue_key) DO UPDATE SET minutes = excluded.minutes, start_time = excluded.start_time, " +
            "description = excluded.description, manual = excluded.manual, updated_ms = excluded.updated_ms",
            new { day = Key(draft.Day), draft.IssueKey, draft.Minutes, draft.StartTime, draft.Description, manual = draft.Manual ? 1 : 0, now = Now });
    }

    public void DeleteDraft(DateOnly day, string issueKey)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM worklog_drafts WHERE day = @day AND issue_key = @issueKey", new { day = Key(day), issueKey });
    }

    public void ClearDrafts(DateOnly day)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM worklog_drafts WHERE day = @day", new { day = Key(day) });
    }

    // ------------------------------------------------------------------ elle atamalar

    public Dictionary<GroupRef, string> Assignments(DateOnly day)
    {
        using var c = db.Open();
        return c.Query<AssignmentRow>(
                "SELECT app AS App, group_key AS GroupKey, issue_key AS IssueKey FROM assignments WHERE day = @day",
                new { day = Key(day) })
            .ToDictionary(r => new GroupRef(r.App, r.GroupKey), r => r.IssueKey);
    }

    public void Assign(DateOnly day, GroupRef group, string issueKey)
    {
        using var c = db.Open();
        c.Execute(
            "INSERT INTO assignments (day, app, group_key, issue_key, created_ms) VALUES (@day, @App, @GroupKey, @issueKey, @now) " +
            "ON CONFLICT(day, app, group_key) DO UPDATE SET issue_key = excluded.issue_key, created_ms = excluded.created_ms",
            new { day = Key(day), group.App, group.GroupKey, issueKey, now = Now });
    }

    public void Unassign(DateOnly day, GroupRef group)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM assignments WHERE day = @day AND app = @App AND group_key = @GroupKey", new { day = Key(day), group.App, group.GroupKey });
    }

    // ------------------------------------------------------------------ gün kapanışları

    public DayClosure? Closure(DateOnly day)
    {
        using var c = db.Open();
        var row = c.QueryFirstOrDefault<ClosureRow>(
            "SELECT day AS Day, closed_ms AS ClosedMs, active_minutes AS ActiveMinutes, worklog_minutes AS WorklogMinutes, summary AS Summary " +
            "FROM day_closures WHERE day = @day",
            new { day = Key(day) });
        return row?.ToModel();
    }

    public HashSet<DateOnly> ClosedDays(DateOnly from, DateOnly to)
    {
        using var c = db.Open();
        return c.Query<string>("SELECT day FROM day_closures WHERE day BETWEEN @from AND @to", new { from = Key(from), to = Key(to) })
            .Select(ParseDay)
            .ToHashSet();
    }

    public void Close(DayClosure closure)
    {
        using var c = db.Open();
        c.Execute(
            "INSERT INTO day_closures (day, closed_ms, active_minutes, worklog_minutes, summary) VALUES (@day, @ms, @ActiveMinutes, @WorklogMinutes, @Summary) " +
            "ON CONFLICT(day) DO UPDATE SET closed_ms = excluded.closed_ms, active_minutes = excluded.active_minutes, " +
            "worklog_minutes = excluded.worklog_minutes, summary = excluded.summary",
            new { day = Key(closure.Day), ms = closure.ClosedAt.ToUnixTimeMilliseconds(), closure.ActiveMinutes, closure.WorklogMinutes, closure.Summary });
    }

    public void Reopen(DateOnly day)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM day_closures WHERE day = @day", new { day = Key(day) });
    }

    private sealed class DraftRow
    {
        public string IssueKey { get; set; } = "";
        public long Minutes { get; set; }
        public string StartTime { get; set; } = "";
        public string Description { get; set; } = "";
        public long Manual { get; set; }
    }

    private sealed class AssignmentRow
    {
        public string App { get; set; } = "";
        public string GroupKey { get; set; } = "";
        public string IssueKey { get; set; } = "";
    }

    private sealed class ClosureRow
    {
        public string Day { get; set; } = "";
        public long ClosedMs { get; set; }
        public long ActiveMinutes { get; set; }
        public long WorklogMinutes { get; set; }
        public string Summary { get; set; } = "";

        public DayClosure ToModel() => new(
            ParseDay(Day),
            DateTimeOffset.FromUnixTimeMilliseconds(ClosedMs).ToLocalTime(),
            (int)ActiveMinutes,
            (int)WorklogMinutes,
            Summary);
    }
}

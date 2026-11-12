using WorkLogger.Core.Model;

namespace WorkLogger.Core.Analysis;

public sealed record DayTotals(
    DateOnly Day,
    TimeSpan Active,
    TimeSpan Classified,
    TimeSpan Idle,
    TimeSpan Focus,
    int ContextSwitches,
    DateTimeOffset? FirstActivity,
    DateTimeOffset? LastActivity,
    int CommitCount);

public sealed record ProjectTotal(string Project, TimeSpan Duration, int IssueCount);

public sealed record IssueTotal(string IssueKey, TimeSpan Duration, int DaysWorked, DateOnly FirstDay, DateOnly LastDay, int CommitCount);

public sealed class PeriodAnalysis
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public IReadOnlyList<DayTotals> Days { get; init; } = [];
    public IReadOnlyList<ProjectTotal> Projects { get; init; } = [];
    public IReadOnlyList<IssueTotal> Issues { get; init; } = [];
    public IReadOnlyList<AppUsage> Apps { get; init; } = [];

    /// <summary>[haftanın günü (0 = Pazartesi), saat] → aktif süre.</summary>
    public TimeSpan[,] WeekdayHour { get; init; } = new TimeSpan[7, 24];

    public TimeSpan Active => Days.Aggregate(TimeSpan.Zero, (s, d) => s + d.Active);
    public TimeSpan Classified => Days.Aggregate(TimeSpan.Zero, (s, d) => s + d.Classified);
    public TimeSpan Focus => Days.Aggregate(TimeSpan.Zero, (s, d) => s + d.Focus);
    public int DaysWorked => Days.Count(d => d.Active >= TimeSpan.FromMinutes(30));
    public TimeSpan AveragePerWorkedDay => DaysWorked > 0 ? Active / DaysWorked : TimeSpan.Zero;
    public double ClassifiedRatio => Active > TimeSpan.Zero ? Classified / Active : 0;
    public int CommitCount => Days.Sum(d => d.CommitCount);
}

/// <summary>Birden çok günü, her gün için <see cref="DayAnalyzer"/> çalıştırarak özetler.</summary>
public static class PeriodAnalyzer
{
    public static PeriodAnalysis Analyze(
        DayAnalyzer analyzer,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ActivitySegment> segments,
        IReadOnlyList<CommitInfo> commits)
    {
        var segmentsByDay = segments.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.ToList());
        var commitsByDay = commits.GroupBy(c => c.Day).ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<DayTotals>();
        var projects = new Dictionary<string, (TimeSpan Time, HashSet<string> Issues)>(StringComparer.Ordinal);
        var issues = new Dictionary<string, IssueAccumulator>(StringComparer.Ordinal);
        var apps = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var weekdayHour = new TimeSpan[7, 24];

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var daySegments = segmentsByDay.GetValueOrDefault(day) ?? [];
            // Gece yarısını aşan segment varsa önceki günün listesinde de olabilir; DayAnalyzer kırpar.
            if (segmentsByDay.TryGetValue(day.AddDays(-1), out var previous))
                daySegments = [.. previous.Where(s => s.End.LocalDateTime.Date > s.Start.LocalDateTime.Date), .. daySegments];

            var a = analyzer.Analyze(day, daySegments, commitsByDay.GetValueOrDefault(day) ?? []);
            days.Add(new DayTotals(day, a.Active, a.Classified, a.Idle, a.FocusTotal, a.ContextSwitches,
                a.FirstActivity, a.LastActivity, a.Commits.Count));

            var weekday = ((int)day.DayOfWeek + 6) % 7;
            for (var hour = 0; hour < 24; hour++)
                weekdayHour[weekday, hour] += a.Hourly[hour];

            foreach (var app in a.Apps)
                apps[app.App] = apps.GetValueOrDefault(app.App) + app.Duration;

            foreach (var issue in a.Issues)
            {
                if (!projects.TryGetValue(issue.Project, out var p))
                    p = (TimeSpan.Zero, new HashSet<string>(StringComparer.Ordinal));
                p.Issues.Add(issue.Key);
                projects[issue.Project] = (p.Time + issue.Duration, p.Issues);

                if (!issues.TryGetValue(issue.Key, out var acc))
                    issues[issue.Key] = acc = new IssueAccumulator(day);
                acc.Add(day, issue.Duration, issue.Commits.Count);
            }
        }

        return new PeriodAnalysis
        {
            From = from,
            To = to,
            Days = days,
            Projects = projects
                .Select(kv => new ProjectTotal(kv.Key, kv.Value.Time, kv.Value.Issues.Count))
                .OrderByDescending(p => p.Duration)
                .ToList(),
            Issues = issues
                .Select(kv => new IssueTotal(kv.Key, kv.Value.Time, kv.Value.Days, kv.Value.First, kv.Value.Last, kv.Value.Commits))
                .OrderByDescending(i => i.Duration)
                .ToList(),
            Apps = apps.Select(kv => new AppUsage(kv.Key, kv.Value)).OrderByDescending(a => a.Duration).ToList(),
            WeekdayHour = weekdayHour,
        };
    }

    private sealed class IssueAccumulator(DateOnly first)
    {
        public TimeSpan Time { get; private set; }
        public int Days { get; private set; }
        public int Commits { get; private set; }
        public DateOnly First { get; } = first;
        public DateOnly Last { get; private set; } = first;

        public void Add(DateOnly day, TimeSpan time, int commits)
        {
            Time += time;
            Commits += commits;
            if (time > TimeSpan.Zero || commits > 0)
                Days++;
            Last = day;
        }
    }
}

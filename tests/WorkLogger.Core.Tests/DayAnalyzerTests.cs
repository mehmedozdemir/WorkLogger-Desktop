using WorkLogger.Core.Analysis;
using WorkLogger.Core.Classification;
using WorkLogger.Core.Model;
using WorkLogger.Core.Settings;
using WorkLogger.Core.Summaries;

namespace WorkLogger.Core.Tests;

public class DayAnalyzerTests
{
    internal static readonly DateOnly Day = new(2026, 11, 12); // Perşembe

    /// <summary>Python sürümünün smoke testindeki sentetik gün.</summary>
    internal static readonly (int Minutes, string App, string Title, bool Idle)[] Fixture =
    [
        (45, "devenv.exe", "PAY-1042 kart dogrulama - Odeme.sln - Microsoft Visual Studio", false),
        (20, "chrome.exe", "[PAY-1042] Sanal kart dogrulama - Jira - Google Chrome", false),
        (35, "chrome.exe", "EF Core Oracle mapping - Stack Overflow - Google Chrome", false),
        (18, "ms-teams.exe", "Gunluk toplanti | Microsoft Teams", false),
        (25, "devenv.exe", "CRM-880 lisans modulu - CRM.sln - Microsoft Visual Studio", false),
        (40, "(boşta)", "", true),
        (30, "dbeaver.exe", "PAY-1042 - proc_validate - DBeaver", false),
        (12, "OUTLOOK.EXE", "Gelen Kutusu - Outlook", false),
        (55, "devenv.exe", "PAY-1109 iade akisi - Odeme.sln - Microsoft Visual Studio", false),
        (8, "explorer.exe", "Indirilenler", false),
    ];

    internal static DateTimeOffset At(int hour, int minute = 0)
    {
        var dt = Day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    internal static List<ActivitySegment> Segments()
    {
        var list = new List<ActivitySegment>();
        var cursor = At(9);
        long id = 1;
        foreach (var (minutes, app, title, idle) in Fixture)
        {
            var end = cursor.AddMinutes(minutes);
            list.Add(new ActivitySegment(id++, cursor, end, app, title, idle));
            cursor = end;
        }
        return list;
    }

    internal static List<CommitInfo> Commits() =>
    [
        new("odeme-api", "abc123", "feature/PAY-1042-kart", "PAY-1042 kart dogrulama eklendi", At(11, 30), 3, 120, 8),
        new("crm", "def456", "feature/CRM-880", "CRM-880 lisans kontrolu", At(14, 10), 1, 10, 2),
        new("odeme-api", "fff999", "feature/PAY-2000-rapor", "PAY-2000 rapor iskeleti", At(16, 0), 2, 40, 0),
    ];

    internal static DayAnalyzer Analyzer() => new(new IssueClassifier(new ClassificationSettings()), new TrackingSettings());

    [Fact]
    public void Finds_issue_keys_and_durations()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        var keys = a.Issues.Select(i => i.Key).ToHashSet();
        Assert.Contains("PAY-1042", keys);
        Assert.Contains("PAY-1109", keys);
        Assert.Contains("CRM-880", keys);

        var pay = a.Issues.Single(i => i.Key == "PAY-1042");
        Assert.Equal(TimeSpan.FromMinutes(95), pay.Duration);
        Assert.Single(pay.Commits);
        Assert.Equal("PAY-1042", a.Issues[0].Key); // en uzun önce
    }

    [Fact]
    public void Separates_idle_and_counts_all_active_time()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        Assert.Equal(TimeSpan.FromMinutes(40), a.Idle);
        Assert.Equal(TimeSpan.FromMinutes(248), a.Active); // kısa segmentler de dahil, kayıp yok
        Assert.Equal(a.Active, a.Classified + a.UnclassifiedTotal);
    }

    [Fact]
    public void Commit_only_issue_has_zero_duration_and_no_phantom_worklog()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        var only = a.Issues.Single(i => i.Key == "PAY-2000");
        Assert.True(only.CommitOnly);
        var suggestion = WorklogPlanner.Suggest(a, new WorklogSettings()).Single(s => s.IssueKey == "PAY-2000");
        Assert.Equal(0, suggestion.SuggestedMinutes);
    }

    [Fact]
    public void Hourly_buckets_split_long_segments()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        Assert.All(a.Hourly, h => Assert.True(h <= TimeSpan.FromHours(1)));
        Assert.Equal(a.Active, a.Hourly.Aggregate(TimeSpan.Zero, (s, h) => s + h));
    }

    [Fact]
    public void Timeline_blocks_cover_the_day_in_order()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        // VS ve Chrome'daki art arda PAY-1042 segmentleri tek blokta birleşir.
        Assert.Equal(Fixture.Length - 1, a.Timeline.Count);
        for (var i = 1; i < a.Timeline.Count; i++)
            Assert.True(a.Timeline[i].Start >= a.Timeline[i - 1].End);
        Assert.Contains(a.Timeline, b => b.Kind == TimelineKind.Idle);
    }

    [Fact]
    public void Focus_blocks_and_context_switches()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        // 45+20 dk PAY-1042 kesintisiz → odak bloğu; 55 dk PAY-1109 → odak bloğu; 35 dk Chrome → odak bloğu.
        Assert.Contains(a.FocusBlocks, f => f.Label == "PAY-1042" && f.Duration == TimeSpan.FromMinutes(65));
        Assert.Contains(a.FocusBlocks, f => f.Label == "PAY-1109");
        Assert.True(a.ContextSwitches >= 5);
    }

    [Fact]
    public void Short_interruptions_do_not_break_focus()
    {
        var segs = new List<ActivitySegment>
        {
            new(1, At(9), At(9, 20), "devenv.exe", "PAY-1 a", false),
            new(2, At(9, 20), At(9, 21), "OUTLOOK.EXE", "Gelen kutusu", false),
            new(3, At(9, 21), At(9, 40), "devenv.exe", "PAY-1 b", false),
        };
        var a = Analyzer().Analyze(Day, segs, []);
        var focus = Assert.Single(a.FocusBlocks);
        Assert.Equal(TimeSpan.FromMinutes(39), focus.Duration);
        Assert.Equal(0, a.ContextSwitches);
    }

    [Fact]
    public void Idle_outside_working_span_is_not_counted()
    {
        var segs = new List<ActivitySegment>
        {
            new(1, At(0), At(8), "(boşta)", "", true),            // gece açık kalan bilgisayar
            new(2, At(9), At(10), "devenv.exe", "PAY-1 a", false),
            new(3, At(10), At(10, 30), "(boşta)", "", true),       // mola
            new(4, At(10, 30), At(11), "devenv.exe", "PAY-1 b", false),
            new(5, At(11), At(23), "(boşta)", "", true),           // mesai sonrası
        };
        var a = Analyzer().Analyze(Day, segs, []);
        Assert.Equal(TimeSpan.FromMinutes(30), a.Idle);
        Assert.Equal(At(9), a.Timeline[0].Start);
        Assert.Equal(At(11), a.Timeline[^1].End);
    }

    [Fact]
    public void Segments_outside_day_are_clipped()
    {
        var prev = new DateTimeOffset(Day.AddDays(-1).ToDateTime(new TimeOnly(23, 30)), At(9).Offset);
        var segs = new List<ActivitySegment> { new(1, prev, At(0, 30), "devenv.exe", "PAY-1", false) };
        var a = Analyzer().Analyze(Day, segs, []);
        Assert.Equal(TimeSpan.FromMinutes(30), a.Active);
    }

    [Fact]
    public void Summary_mentions_top_issue_and_is_deterministic()
    {
        var a = Analyzer().Analyze(Day, Segments(), Commits());
        var s1 = DaySummarizer.Summarize(a, new WorklogSettings());
        var s2 = DaySummarizer.Summarize(Analyzer().Analyze(Day, Segments(), Commits()), new WorklogSettings());
        Assert.Equal(s1.ToPlainText(Day), s2.ToPlainText(Day));
        Assert.Contains("PAY-1042", s1.Narrative[1]);
        Assert.Contains(s1.Items, i => i.IssueKey == "PAY-2000" && i.Lines.Any(l => l.Contains("commit", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Dün:", s1.Standup);
    }

    [Fact]
    public void Empty_day_has_empty_summary()
    {
        var a = Analyzer().Analyze(Day, [], []);
        Assert.False(a.HasData);
        Assert.Same(DaySummary.Empty, DaySummarizer.Summarize(a, new WorklogSettings()));
    }
}

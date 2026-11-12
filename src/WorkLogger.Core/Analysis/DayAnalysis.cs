using WorkLogger.Core.Classification;
using WorkLogger.Core.Model;

namespace WorkLogger.Core.Analysis;

public enum TimelineKind
{
    Issue,
    Unclassified,
    Idle,
}

public sealed record TimelineBlock(
    DateTimeOffset Start,
    DateTimeOffset End,
    TimelineKind Kind,
    string Label,
    string? IssueKey,
    string AppLabel,
    string Title)
{
    public TimeSpan Duration => End - Start;
}

public sealed record AppUsage(string App, TimeSpan Duration);

public sealed record FocusBlock(DateTimeOffset Start, DateTimeOffset End, string Label, TimeSpan Duration);

public sealed record TopicTime(string Text, TimeSpan Duration);

public sealed class IssueActivity
{
    public required string Key { get; init; }
    public string Project => IssueClassifier.ProjectOf(Key);
    public TimeSpan Duration { get; internal set; }
    public DateTimeOffset? FirstStart { get; internal set; }
    public DateTimeOffset? LastEnd { get; internal set; }
    public ClassificationSource Source { get; internal set; }
    public IReadOnlyList<AppUsage> Apps { get; internal set; } = [];
    public IReadOnlyList<TopicTime> Topics { get; internal set; } = [];
    public List<CommitInfo> Commits { get; } = [];
    public bool CommitOnly => Duration == TimeSpan.Zero && Commits.Count > 0;
}

public sealed class UnclassifiedActivity
{
    public required string Label { get; init; }
    public required string AppLabel { get; init; }
    public TimeSpan Duration { get; internal set; }
    public DateTimeOffset? FirstStart { get; internal set; }
    public IReadOnlyList<TopicTime> Titles { get; internal set; } = [];
}

public sealed class DayAnalysis
{
    public required DateOnly Day { get; init; }
    public TimeSpan Active { get; internal set; }
    public TimeSpan Idle { get; internal set; }
    public TimeSpan OutsideWorkHours { get; internal set; }
    public DateTimeOffset? FirstActivity { get; internal set; }
    public DateTimeOffset? LastActivity { get; internal set; }
    public IReadOnlyList<IssueActivity> Issues { get; internal set; } = [];
    public IReadOnlyList<UnclassifiedActivity> Unclassified { get; internal set; } = [];
    public IReadOnlyList<AppUsage> Apps { get; internal set; } = [];
    public IReadOnlyList<TimelineBlock> Timeline { get; internal set; } = [];
    public IReadOnlyList<FocusBlock> FocusBlocks { get; internal set; } = [];
    public IReadOnlyList<CommitInfo> Commits { get; internal set; } = [];

    /// <summary>Saat başına aktif süre (0-23).</summary>
    public IReadOnlyList<TimeSpan> Hourly { get; internal set; } = new TimeSpan[24];

    /// <summary>Kısa (&lt;3 dk) kesintiler ayıklandıktan sonraki iş/etiket değişimi sayısı.</summary>
    public int ContextSwitches { get; internal set; }

    public TimeSpan Classified => Issues.Aggregate(TimeSpan.Zero, (sum, i) => sum + i.Duration);
    public TimeSpan UnclassifiedTotal => Active - Classified;
    public double ClassifiedRatio => Active > TimeSpan.Zero ? Classified / Active : 0;
    public TimeSpan FocusTotal => FocusBlocks.Aggregate(TimeSpan.Zero, (sum, f) => sum + f.Duration);
    public bool HasData => Active > TimeSpan.Zero || Idle > TimeSpan.Zero;

    public double SwitchesPerHour => Active.TotalHours >= 0.5 ? ContextSwitches / Active.TotalHours : 0;
}

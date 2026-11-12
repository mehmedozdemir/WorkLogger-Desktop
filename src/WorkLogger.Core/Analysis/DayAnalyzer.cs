using WorkLogger.Core.Classification;
using WorkLogger.Core.Model;
using WorkLogger.Core.Settings;

namespace WorkLogger.Core.Analysis;

/// <summary>
/// Bir günün ham segmentlerini ve commit'lerini analiz eder. Tüm sayılar burada, deterministik olarak üretilir.
/// </summary>
public sealed class DayAnalyzer
{
    /// <summary>Bu süreden kısa bir araya giren iş, bağlam değişimi sayılmaz.</summary>
    public static readonly TimeSpan ShortInterruption = TimeSpan.FromMinutes(3);

    /// <summary>Odak bloğu sayılması için gereken kesintisiz süre.</summary>
    public static readonly TimeSpan FocusThreshold = TimeSpan.FromMinutes(25);

    /// <summary>Aynı işin parçaları arasında bu kadar boşluk varsa yine tek parça sayılır.</summary>
    public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(2);

    /// <summary>Sınıflandırılmamış gruplarda bundan kısa olanlar "Diğer" altında toplanır.</summary>
    public static readonly TimeSpan MinUnclassifiedGroup = TimeSpan.FromMinutes(2);

    public const string OtherLabel = "Diğer kısa işler";

    private readonly IssueClassifier _classifier;
    private readonly TrackingSettings _tracking;

    public DayAnalyzer(IssueClassifier classifier, TrackingSettings tracking)
    {
        _classifier = classifier;
        _tracking = tracking;
    }

    public IssueClassifier Classifier => _classifier;

    public DayAnalysis Analyze(DateOnly day, IEnumerable<ActivitySegment> segments, IEnumerable<CommitInfo> commits)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        var nextDay = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var dayEnd = new DateTimeOffset(nextDay, TimeZoneInfo.Local.GetUtcOffset(nextDay));

        var clipped = segments
            .Select(s => s.ClipTo(dayStart, dayEnd))
            .OfType<ActivitySegment>()
            .OrderBy(s => s.Start)
            .ToList();

        var result = new DayAnalysis { Day = day };
        var issues = new Dictionary<string, IssueBuilder>(StringComparer.Ordinal);
        var unclassified = new Dictionary<string, UnclassifiedBuilder>(StringComparer.Ordinal);
        var apps = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var hourly = new TimeSpan[24];
        var labelled = new List<(ActivitySegment Segment, string Label, string? Key, string AppLabel)>();

        foreach (var seg in clipped)
        {
            if (seg.Idle)
                continue; // boşta süre aşağıda, çalışma aralığıyla sınırlanarak hesaplanır

            var appLabel = _classifier.AppLabel(seg.App);
            var cls = _classifier.Classify(seg.App, seg.Title);

            result.Active += seg.Duration;
            result.FirstActivity ??= seg.Start;
            result.LastActivity = seg.End;
            apps[appLabel] = apps.GetValueOrDefault(appLabel) + seg.Duration;
            AddHourly(hourly, seg);
            result.OutsideWorkHours += OutsideWorkHours(seg);

            if (cls.IssueKey is { } key)
            {
                if (!issues.TryGetValue(key, out var builder))
                    issues[key] = builder = new IssueBuilder(key, cls.Source);
                builder.Add(seg, appLabel);
                labelled.Add((seg, key, key, appLabel));
            }
            else
            {
                var group = TitleNormalizer.GroupKey(seg.Title);
                var label = group.Length > 0 ? $"{appLabel} · {group}" : appLabel;
                if (!unclassified.TryGetValue(label, out var builder))
                    unclassified[label] = builder = new UnclassifiedBuilder(label, appLabel);
                builder.Add(seg);
                labelled.Add((seg, appLabel, null, appLabel));
            }
        }

        // Git: commit mesajı veya branch adındaki issue key'ler o issue'ya bağlanır.
        var dayCommits = commits.Where(c => c.Timestamp >= dayStart && c.Timestamp < dayEnd).OrderBy(c => c.Timestamp).ToList();
        foreach (var commit in dayCommits)
        {
            var key = _classifier.ExtractKey(commit.Branch) ?? _classifier.ExtractKey(commit.Message);
            if (key is null)
                continue;
            if (!issues.TryGetValue(key, out var builder))
                issues[key] = builder = new IssueBuilder(key, ClassificationSource.Git);
            builder.Commits.Add(commit);
        }

        result.Issues = issues.Values
            .Select(b => b.Build())
            .OrderByDescending(i => i.Duration)
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToList();
        result.Unclassified = BuildUnclassified(unclassified.Values);
        result.Apps = apps.Select(kv => new AppUsage(kv.Key, kv.Value)).OrderByDescending(a => a.Duration).ToList();
        result.Hourly = hourly;
        result.Commits = dayCommits;

        // Boşta süre: yalnızca günün ilk ve son aktivitesi arasındaki aralar. Gece ya da mesai
        // sonrası açık kalan bilgisayarın boşta süresi "mola" sayılmaz.
        if (result.FirstActivity is { } firstActive && result.LastActivity is { } lastActive)
        {
            foreach (var seg in clipped.Where(s => s.Idle))
                result.Idle += seg.ClipTo(firstActive, lastActive)?.Duration ?? TimeSpan.Zero;
        }

        // Zaman çizelgesi de aynı aralıkla sınırlanır.
        result.Timeline = BuildTimeline(result.FirstActivity is { } f && result.LastActivity is { } l
            ? clipped.Select(s => s.ClipTo(f, l)).OfType<ActivitySegment>().ToList()
            : []);

        var units = WorkUnits(labelled);
        result.ContextSwitches = Math.Max(0, units.Count - 1);
        result.FocusBlocks = units
            .Where(u => u.Duration >= FocusThreshold)
            .Select(u => new FocusBlock(u.Start, u.End, u.Label, u.Duration))
            .ToList();
        return result;
    }

    // ------------------------------------------------------------------ yardımcılar

    private TimeSpan OutsideWorkHours(ActivitySegment seg)
    {
        var local = seg.Start.ToLocalTime();
        var isWorkday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        if (!isWorkday)
            return seg.Duration;

        var date = DateOnly.FromDateTime(local.DateTime);
        var workStart = ToOffset(date, _tracking.WorkdayStart);
        var workEnd = ToOffset(date, _tracking.WorkdayEnd);
        var inside = seg.ClipTo(workStart, workEnd)?.Duration ?? TimeSpan.Zero;
        return seg.Duration - inside;
    }

    private static DateTimeOffset ToOffset(DateOnly date, TimeOnly time)
    {
        var dt = date.ToDateTime(time);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    private static void AddHourly(TimeSpan[] hourly, ActivitySegment seg)
    {
        var cursor = seg.Start.ToLocalTime();
        var end = seg.End.ToLocalTime();
        while (cursor < end)
        {
            var hourEnd = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Offset).AddHours(1);
            var sliceEnd = hourEnd < end ? hourEnd : end;
            hourly[cursor.Hour] += sliceEnd - cursor;
            cursor = sliceEnd;
        }
    }

    private static List<UnclassifiedActivity> BuildUnclassified(IEnumerable<UnclassifiedBuilder> builders)
    {
        var list = new List<UnclassifiedActivity>();
        UnclassifiedBuilder? other = null;
        foreach (var builder in builders)
        {
            if (builder.Duration >= MinUnclassifiedGroup)
            {
                list.Add(builder.Build());
                continue;
            }
            other ??= new UnclassifiedBuilder(OtherLabel, "");
            other.Absorb(builder);
        }
        list.Sort((a, b) => b.Duration.CompareTo(a.Duration));
        if (other is not null)
            list.Add(other.Build());
        return list;
    }

    private List<TimelineBlock> BuildTimeline(List<ActivitySegment> segments)
    {
        var blocks = new List<TimelineBlock>();
        foreach (var seg in segments)
        {
            TimelineBlock block;
            var appLabel = _classifier.AppLabel(seg.App);
            if (seg.Idle)
            {
                block = new TimelineBlock(seg.Start, seg.End, TimelineKind.Idle, "Boşta", null, appLabel, "");
            }
            else if (_classifier.Classify(seg.App, seg.Title).IssueKey is { } key)
            {
                block = new TimelineBlock(seg.Start, seg.End, TimelineKind.Issue, key, key, appLabel, seg.Title);
            }
            else
            {
                block = new TimelineBlock(seg.Start, seg.End, TimelineKind.Unclassified, appLabel, null, appLabel, seg.Title);
            }

            if (blocks.Count > 0)
            {
                var last = blocks[^1];
                if (last.Kind == block.Kind && last.Label == block.Label && block.Start - last.End <= TimeSpan.FromMinutes(1))
                {
                    blocks[^1] = last with { End = block.End };
                    continue;
                }
            }
            blocks.Add(block);
        }
        return blocks;
    }

    private sealed record Unit(string Label, DateTimeOffset Start, DateTimeOffset End, TimeSpan Duration);

    /// <summary>
    /// Aktif segmentleri "iş birimlerine" indirger: aynı etiketin yakın parçaları birleşir,
    /// kısa araya girmeler (ör. 30 sn'lik Outlook bakışı) ayıklanır.
    /// </summary>
    private static List<Unit> WorkUnits(List<(ActivitySegment Segment, string Label, string? Key, string AppLabel)> labelled)
    {
        static List<Unit> Merge(IEnumerable<Unit> source)
        {
            var merged = new List<Unit>();
            foreach (var unit in source)
            {
                if (merged.Count > 0 && merged[^1].Label == unit.Label && unit.Start - merged[^1].End <= MergeGap)
                {
                    var last = merged[^1];
                    merged[^1] = last with { End = unit.End, Duration = last.Duration + unit.Duration };
                }
                else
                {
                    merged.Add(unit);
                }
            }
            return merged;
        }

        var units = Merge(labelled.Select(l => new Unit(l.Label, l.Segment.Start, l.Segment.End, l.Segment.Duration)));
        // Kısa birimleri at, ardından iki yanındaki aynı etiketli birimleri yeniden birleştir.
        // Atılan kısa birimler arada boşluk bıraktığı için tolerans genişletilir; ama uzun bir mola
        // (boşta segmentleri listede yok, yalnızca boşluk olarak görünür) iki parçayı ayrı tutar.
        var bridgeGap = ShortInterruption * 2;
        var filtered = units.Where(u => u.Duration >= ShortInterruption).ToList();
        var result = new List<Unit>();
        foreach (var unit in filtered)
        {
            if (result.Count > 0 && result[^1].Label == unit.Label && unit.Start - result[^1].End <= bridgeGap)
            {
                var last = result[^1];
                result[^1] = last with { End = unit.End, Duration = last.Duration + unit.Duration };
            }
            else
            {
                result.Add(unit);
            }
        }
        return result;
    }

    private static List<T> TopN<T>(Dictionary<string, TimeSpan> source, int n, Func<string, TimeSpan, T> map) =>
        source.OrderByDescending(kv => kv.Value).Take(n).Select(kv => map(kv.Key, kv.Value)).ToList();

    private sealed class IssueBuilder(string key, ClassificationSource source)
    {
        private readonly Dictionary<string, TimeSpan> _apps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TimeSpan> _topics = new(StringComparer.Ordinal);
        private TimeSpan _duration;
        private DateTimeOffset? _first;
        private DateTimeOffset? _last;
        private ClassificationSource _source = source;

        public List<CommitInfo> Commits { get; } = [];

        public void Add(ActivitySegment seg, string appLabel)
        {
            _duration += seg.Duration;
            _apps[appLabel] = _apps.GetValueOrDefault(appLabel) + seg.Duration;
            var topic = TitleNormalizer.Topic(seg.Title);
            if (topic.Length > 0)
                _topics[topic] = _topics.GetValueOrDefault(topic) + seg.Duration;
            if (_first is null || seg.Start < _first)
                _first = seg.Start;
            if (_last is null || seg.End > _last)
                _last = seg.End;
            if (_source == ClassificationSource.Git)
                _source = ClassificationSource.Title;
        }

        public IssueActivity Build()
        {
            var activity = new IssueActivity
            {
                Key = key,
                Duration = _duration,
                FirstStart = _first ?? Commits.FirstOrDefault()?.Timestamp,
                LastEnd = _last ?? Commits.LastOrDefault()?.Timestamp,
                Source = _source,
                Apps = TopN(_apps, 4, (k, v) => new AppUsage(k, v)),
                Topics = TopN(_topics, 6, (k, v) => new TopicTime(k, v)),
            };
            activity.Commits.AddRange(Commits);
            return activity;
        }
    }

    private sealed class UnclassifiedBuilder(string label, string appLabel)
    {
        private readonly Dictionary<string, TimeSpan> _titles = new(StringComparer.Ordinal);
        private DateTimeOffset? _first;

        public TimeSpan Duration { get; private set; }

        public void Add(ActivitySegment seg)
        {
            Duration += seg.Duration;
            var title = TitleNormalizer.StripAppSuffix(seg.Title);
            if (title.Length > 0)
                _titles[title] = _titles.GetValueOrDefault(title) + seg.Duration;
            if (_first is null || seg.Start < _first)
                _first = seg.Start;
        }

        public void Absorb(UnclassifiedBuilder other)
        {
            Duration += other.Duration;
            foreach (var (title, time) in other._titles)
                _titles[title] = _titles.GetValueOrDefault(title) + time;
            if (other._first is { } f && (_first is null || f < _first))
                _first = f;
        }

        public UnclassifiedActivity Build() => new()
        {
            Label = label,
            AppLabel = appLabel,
            Duration = Duration,
            FirstStart = _first,
            Titles = TopN(_titles, 5, (k, v) => new TopicTime(k, v)),
        };
    }
}

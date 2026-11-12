using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Analysis;
using WorkLogger.Core.Classification;
using WorkLogger.Core.Formatting;
using WorkLogger.Core.Summaries;
using WorkLogger.Desktop.Controls;
using WorkLogger.Desktop.Services;

namespace WorkLogger.Desktop.ViewModels;

public sealed record IssueRow(string Key, string DurationText, double Share, IBrush Brush, string Topic, string Meta);

public sealed record AppRow(string Name, string DurationText, double Share, IBrush Brush);

public sealed record UnclassifiedRow(string Label, string DurationText, string Detail);

public sealed record CommitRow(string Time, string Repo, string Branch, string Message, string Stats);

public sealed record HighlightRow(string Text, bool IsPositive, bool IsWarning);

public partial class WorklogRow : ObservableObject
{
    public required string IssueKey { get; init; }
    public required string TrackedText { get; init; }
    public required IBrush Brush { get; init; }
    public bool CommitOnly { get; init; }

    [ObservableProperty] private decimal? _minutes;
    [ObservableProperty] private string _start = "";
    [ObservableProperty] private string _description = "";

    public int MinutesValue => (int)(Minutes ?? 0);
}

public partial class DayViewModel : ObservableObject
{
    private readonly AppServices _services;
    private DayAnalysis? _analysis;
    private DaySummary _summary = DaySummary.Empty;
    private List<WorklogSuggestion> _suggestions = [];
    private DateTimeOffset _lastAutoRefresh = DateTimeOffset.MinValue;

    public DayViewModel(AppServices services)
    {
        _services = services;
        _ = LoadAsync(DateOnly.FromDateTime(DateTime.Now));
    }

    [ObservableProperty] private DateOnly _day;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string? _error;

    // KPI
    [ObservableProperty] private string _activeText = "0 dk";
    [ObservableProperty] private string _activeDetail = "";
    [ObservableProperty] private string _classifiedText = "–";
    [ObservableProperty] private string _classifiedDetail = "";
    [ObservableProperty] private string _focusText = "0 dk";
    [ObservableProperty] private string _focusDetail = "";
    [ObservableProperty] private string _targetText = "";
    [ObservableProperty] private string _targetDetail = "";
    [ObservableProperty] private double _targetProgress;

    // Grafikler
    [ObservableProperty] private IReadOnlyList<TimelineBlock> _timeline = [];
    [ObservableProperty] private DateTimeOffset _rangeStart;
    [ObservableProperty] private DateTimeOffset _rangeEnd;
    [ObservableProperty] private IReadOnlyList<IssueRow> _issues = [];
    [ObservableProperty] private IReadOnlyList<ChartSlice> _appSlices = [];
    [ObservableProperty] private IReadOnlyList<AppRow> _apps = [];
    [ObservableProperty] private string _appsCenter = "";
    [ObservableProperty] private IReadOnlyList<BarItem> _hourly = [];
    [ObservableProperty] private IReadOnlyList<UnclassifiedRow> _unclassified = [];
    [ObservableProperty] private IReadOnlyList<CommitRow> _commits = [];

    // Özet
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private IReadOnlyList<string> _narrative = [];
    [ObservableProperty] private IReadOnlyList<HighlightRow> _highlights = [];

    // Worklog
    public ObservableCollection<WorklogRow> Worklogs { get; } = [];
    [ObservableProperty] private string _worklogTotal = "";
    [ObservableProperty] private string? _toast;

    public bool HasIssues => Issues.Count > 0;
    public bool HasUnclassified => Unclassified.Count > 0;
    public bool HasCommits => Commits.Count > 0;
    public bool HasWorklogs => Worklogs.Count > 0;
    public bool HasHighlights => Highlights.Count > 0;

    partial void OnIssuesChanged(IReadOnlyList<IssueRow> value) => OnPropertyChanged(nameof(HasIssues));
    partial void OnUnclassifiedChanged(IReadOnlyList<UnclassifiedRow> value) => OnPropertyChanged(nameof(HasUnclassified));
    partial void OnCommitsChanged(IReadOnlyList<CommitRow> value) => OnPropertyChanged(nameof(HasCommits));
    partial void OnHighlightsChanged(IReadOnlyList<HighlightRow> value) => OnPropertyChanged(nameof(HasHighlights));

    public Task ReloadAsync() => LoadAsync(Day == default ? DateOnly.FromDateTime(DateTime.Now) : Day);

    /// <summary>Bugün görüntüleniyorsa dakikada bir sessizce yenile.</summary>
    public void OnClockTick()
    {
        if (!IsToday || IsLoading || DateTimeOffset.Now - _lastAutoRefresh < TimeSpan.FromSeconds(60))
            return;
        _ = LoadAsync(Day, keepWorklogEdits: true);
    }

    public async Task LoadAsync(DateOnly day, bool keepWorklogEdits = false)
    {
        IsLoading = true;
        Error = null;
        _lastAutoRefresh = DateTimeOffset.Now;
        try
        {
            var analyzer = _services.CreateAnalyzer();
            var settings = _services.Settings;
            var (analysis, summary) = await Task.Run(() =>
            {
                var segments = _services.Activity.SegmentsForDays(day, day);
                var commits = _services.Activity.CommitsForDays(day, day);
                var a = analyzer.Analyze(day, segments, commits);
                return (a, DaySummarizer.Summarize(a, settings.Worklog));
            });

            var edits = keepWorklogEdits && Day == day
                ? Worklogs.ToDictionary(w => w.IssueKey, StringComparer.Ordinal)
                : null;
            Day = day;
            _analysis = analysis;
            _summary = summary;
            Apply(analysis, summary, edits);
        }
        catch (Exception ex)
        {
            Error = "Veri yüklenemedi: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(DayAnalysis a, DaySummary summary, Dictionary<string, WorklogRow>? edits)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        IsToday = a.Day == today;
        Title = a.Day == today ? "Bugün" : a.Day == today.AddDays(-1) ? "Dün" : a.Day.ToString("d MMMM", DurationText.Turkish);
        Subtitle = DurationText.DayTitle(a.Day);
        HasData = a.HasData;
        NextDayCommand.NotifyCanExecuteChanged();

        // KPI
        ActiveText = DurationText.Format(a.Active);
        ActiveDetail = a.FirstActivity is null ? "Kayıt yok" : $"{DurationText.Clock(a.FirstActivity)} – {DurationText.Clock(a.LastActivity)} · boşta {DurationText.Format(a.Idle)}";
        ClassifiedText = a.Active > TimeSpan.Zero ? DurationText.Percent(a.ClassifiedRatio) : "–";
        ClassifiedDetail = $"{DurationText.Format(a.Classified)} issue · {DurationText.Format(a.UnclassifiedTotal)} diğer";
        FocusText = DurationText.Format(a.FocusTotal);
        FocusDetail = a.FocusBlocks.Count == 0 ? "25 dk+ kesintisiz blok yok" : $"{a.FocusBlocks.Count} blok · {a.ContextSwitches} iş değişimi";
        var target = TimeSpan.FromHours(_services.Settings.Worklog.DailyTargetHours);
        TargetProgress = target > TimeSpan.Zero ? Math.Min(100, a.Active / target * 100) : 0;
        TargetText = target > TimeSpan.Zero ? $"{DurationText.Hours(a.Active)} / {DurationText.Hours(target, 0)}" : "–";
        TargetDetail = target <= TimeSpan.Zero ? "Hedef tanımlı değil" : a.Active >= target ? "Hedef tamamlandı" : $"{DurationText.Format(target - a.Active)} kaldı";

        // Zaman çizelgesi: en az 08:00–18:00; aktif çalışma bu aralığın dışına taşıyorsa genişler.
        // Uzun boşta segmentleri (ör. gece) aralığı genişletmez.
        var dayStart = Infrastructure.Data.ActivityRepository.StartOf(a.Day);
        var start = dayStart.AddHours(8);
        var end = dayStart.AddHours(18);
        if (a.FirstActivity is { } first && a.LastActivity is { } last)
        {
            if (first < start)
                start = new DateTimeOffset(first.Year, first.Month, first.Day, first.Hour, 0, 0, first.Offset);
            if (last > end)
                end = new DateTimeOffset(last.Year, last.Month, last.Day, last.Hour, 0, 0, last.Offset).AddHours(1);
        }
        RangeStart = start;
        RangeEnd = end;
        Timeline = a.Timeline;

        // Issue'lar
        var maxIssue = a.Issues.Count > 0 ? a.Issues.Max(i => i.Duration.TotalSeconds) : 1;
        Issues = a.Issues.Select(i => new IssueRow(
            i.Key,
            i.CommitOnly ? "yalnız commit" : DurationText.Format(i.Duration),
            maxIssue > 0 ? i.Duration.TotalSeconds / maxIssue : 0,
            IssuePalette.BrushFor(i.Key),
            i.Topics.FirstOrDefault()?.Text ?? (i.Commits.FirstOrDefault()?.Message ?? ""),
            IssueMeta(i))).ToList();

        // Uygulamalar
        var topApps = a.Apps.Take(6).ToList();
        var other = a.Apps.Skip(6).Aggregate(TimeSpan.Zero, (s, x) => s + x.Duration);
        var slices = topApps.Select((x, idx) => new ChartSlice(x.App, x.Duration.TotalMinutes, IssuePalette.Sequential(idx), DurationText.Format(x.Duration))).ToList();
        if (other > TimeSpan.Zero)
            slices.Add(new ChartSlice("Diğer", other.TotalMinutes, IssuePalette.Unclassified, DurationText.Format(other)));
        AppSlices = slices;
        Apps = slices.Select(s => new AppRow(s.Label, s.Detail ?? "", a.Active.TotalMinutes > 0 ? s.Value / a.Active.TotalMinutes : 0, new ImmutableSolidColorBrush(s.Color))).ToList();
        AppsCenter = a.Apps.Count.ToString(CultureInfo.InvariantCulture);

        Hourly = Enumerable.Range(0, 24)
            .Select(h => new BarItem($"{h:00}", a.Hourly[h].TotalMinutes, 0, $"{h:00}:00–{h + 1:00}:00\n{DurationText.Format(a.Hourly[h])} aktif"))
            .ToList();

        Unclassified = a.Unclassified.Take(12).Select(u => new UnclassifiedRow(
            u.Label,
            DurationText.Format(u.Duration),
            string.Join(" · ", u.Titles.Take(2).Select(t => t.Text)))).ToList();

        Commits = a.Commits.Select(c => new CommitRow(
            DurationText.Clock(c.Timestamp),
            c.Repo,
            c.Branch,
            c.Message,
            c.FilesChanged > 0 ? $"{c.FilesChanged} dosya · +{c.Insertions} −{c.Deletions}" : "")).ToList();

        // Özet
        Headline = summary.Headline;
        Narrative = summary.Narrative;
        Highlights = summary.Highlights.Select(h => new HighlightRow(h.Text, h.Tone == HighlightTone.Positive, h.Tone == HighlightTone.Warning)).ToList();

        // Worklog önerileri (kullanıcının düzenlemeleri otomatik yenilemede korunur)
        _suggestions = WorklogPlanner.Suggest(a, _services.Settings.Worklog);
        foreach (var row in Worklogs)
            row.PropertyChanged -= OnWorklogChanged;
        Worklogs.Clear();
        foreach (var s in _suggestions)
        {
            var row = new WorklogRow
            {
                IssueKey = s.IssueKey,
                TrackedText = s.CommitOnly ? "commit" : DurationText.Format(s.Tracked),
                Brush = IssuePalette.BrushFor(s.IssueKey),
                CommitOnly = s.CommitOnly,
                Minutes = s.SuggestedMinutes,
                Start = s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                Description = s.Description,
            };
            if (edits is not null && edits.TryGetValue(s.IssueKey, out var previous))
            {
                row.Minutes = previous.Minutes;
                row.Start = previous.Start;
                row.Description = previous.Description;
            }
            row.PropertyChanged += OnWorklogChanged;
            Worklogs.Add(row);
        }
        OnPropertyChanged(nameof(HasWorklogs));
        UpdateWorklogTotal();
    }

    private static string IssueMeta(IssueActivity i)
    {
        var parts = new List<string>();
        if (i.Apps.Count > 0)
            parts.Add(string.Join(", ", i.Apps.Take(3).Select(x => x.App)));
        if (i.Commits.Count > 0)
            parts.Add($"{i.Commits.Count} commit");
        if (i.Source == ClassificationSource.Rule)
            parts.Add("kuralla eşlendi");
        return string.Join(" · ", parts);
    }

    private void OnWorklogChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorklogRow.Minutes))
            UpdateWorklogTotal();
    }

    private void UpdateWorklogTotal()
    {
        var total = Worklogs.Sum(w => w.MinutesValue);
        var target = _services.Settings.Worklog.DailyTargetHours * 60;
        WorklogTotal = target > 0
            ? $"Toplam {DurationText.Format(TimeSpan.FromMinutes(total))} · hedef {DurationText.Format(TimeSpan.FromMinutes(target))}"
            : $"Toplam {DurationText.Format(TimeSpan.FromMinutes(total))}";
    }

    // ------------------------------------------------------------------ komutlar

    [RelayCommand]
    private Task PreviousDay() => LoadAsync(Day.AddDays(-1));

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextDay() => LoadAsync(Day.AddDays(1));

    private bool CanGoNext() => Day < DateOnly.FromDateTime(DateTime.Now);

    [RelayCommand]
    private Task GoToday() => LoadAsync(DateOnly.FromDateTime(DateTime.Now));

    [RelayCommand]
    private Task Refresh() => LoadAsync(Day, keepWorklogEdits: true);

    [RelayCommand]
    private async Task CopySummary()
    {
        await UiServices.CopyTextAsync(_summary.ToPlainText(Day));
        ShowToast("Özet panoya kopyalandı.");
    }

    [RelayCommand]
    private async Task CopyStandup()
    {
        if (string.IsNullOrEmpty(_summary.Standup))
            return;
        await UiServices.CopyTextAsync(_summary.Standup);
        ShowToast("Standup metni panoya kopyalandı.");
    }

    [RelayCommand]
    private async Task CopyWorklogs()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Issue\tTarih\tBaşlangıç\tSüre (dk)\tSüre (sa)\tAçıklama");
        foreach (var w in Worklogs.Where(w => w.MinutesValue > 0))
        {
            sb.Append(w.IssueKey).Append('\t')
              .Append(Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\t')
              .Append(w.Start).Append('\t')
              .Append(w.MinutesValue).Append('\t')
              .Append((w.MinutesValue / 60.0).ToString("0.##", DurationText.Turkish)).Append('\t')
              .AppendLine(w.Description.Replace('\t', ' '));
        }
        await UiServices.CopyTextAsync(sb.ToString());
        ShowToast("Worklog tablosu panoya kopyalandı (Excel'e yapıştırılabilir).");
    }

    [RelayCommand]
    private void BalanceWorklogs()
    {
        var settings = _services.Settings.Worklog;
        var target = (int)Math.Round(settings.DailyTargetHours * 60);
        var current = Worklogs.Select(w => new WorklogSuggestion(w.IssueKey, TimeSpan.Zero, w.MinutesValue, default, w.Description, w.CommitOnly)).ToList();
        var balanced = WorklogPlanner.BalanceTo(current, target, Math.Max(1, settings.RoundingMinutes));
        foreach (var (row, b) in Worklogs.Zip(balanced))
            row.Minutes = b.SuggestedMinutes;
        ShowToast("Süreler günlük hedefe orantılı olarak dağıtıldı.");
    }

    [RelayCommand]
    private void ResetWorklogs()
    {
        if (_analysis is not null)
            Apply(_analysis, _summary, edits: null);
    }

    private async void ShowToast(string message)
    {
        Toast = message;
        await Task.Delay(2500);
        if (Toast == message)
            Toast = null;
    }
}

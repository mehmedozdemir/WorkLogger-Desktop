using System.Collections.ObjectModel;
using System.ComponentModel;
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
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Desktop.ViewModels;

public partial class DayViewModel : ObservableObject
{
    private readonly AppServices _services;
    private DayAnalysis? _analysis;
    private DaySummary _summary = DaySummary.Empty;
    private DateTimeOffset _lastAutoRefresh = DateTimeOffset.MinValue;
    private bool _populating;

    public DayViewModel(AppServices services)
    {
        _services = services;
        _ = LoadAsync(DateOnly.FromDateTime(DateTime.Now));
    }

    /// <summary>Aralık özeti penceresini açma isteği (görünüm katmanı dinler).</summary>
    public event EventHandler? RangeSummaryRequested;

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
    [ObservableProperty] private IReadOnlyList<UnclassifiedItem> _unclassified = [];
    [ObservableProperty] private IReadOnlyList<AssignmentRow> _assignments = [];
    [ObservableProperty] private IReadOnlyList<CommitRow> _commits = [];

    // Özet
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private IReadOnlyList<string> _narrative = [];
    [ObservableProperty] private IReadOnlyList<HighlightRow> _highlights = [];

    // Worklog
    public ObservableCollection<WorklogRow> Worklogs { get; } = [];
    [ObservableProperty] private string _worklogTotal = "";
    [ObservableProperty] private string _newIssueKey = "";
    [ObservableProperty] private string? _toast;

    // Gün kapanışı ve hatırlatma
    [ObservableProperty] private bool _isClosed;
    [ObservableProperty] private string _closedText = "";
    [ObservableProperty] private PendingDay? _pendingDay;

    public bool HasIssues => Issues.Count > 0;
    public bool HasUnclassified => Unclassified.Count > 0;
    public bool HasAssignments => Assignments.Count > 0;
    public bool HasCommits => Commits.Count > 0;
    public bool HasWorklogs => Worklogs.Count > 0;
    public bool HasHighlights => Highlights.Count > 0;

    partial void OnIssuesChanged(IReadOnlyList<IssueRow> value) => OnPropertyChanged(nameof(HasIssues));
    partial void OnUnclassifiedChanged(IReadOnlyList<UnclassifiedItem> value) => OnPropertyChanged(nameof(HasUnclassified));
    partial void OnAssignmentsChanged(IReadOnlyList<AssignmentRow> value) => OnPropertyChanged(nameof(HasAssignments));
    partial void OnCommitsChanged(IReadOnlyList<CommitRow> value) => OnPropertyChanged(nameof(HasCommits));
    partial void OnHighlightsChanged(IReadOnlyList<HighlightRow> value) => OnPropertyChanged(nameof(HasHighlights));

    public Task ReloadAsync() => LoadAsync(Day == default ? DateOnly.FromDateTime(DateTime.Now) : Day);

    /// <summary>Bugün görüntüleniyorsa dakikada bir sessizce yenile.</summary>
    public void OnClockTick()
    {
        if (!IsToday || IsLoading || DateTimeOffset.Now - _lastAutoRefresh < TimeSpan.FromSeconds(60))
            return;
        _ = LoadAsync(Day);
    }

    public async Task LoadAsync(DateOnly day)
    {
        IsLoading = true;
        Error = null;
        _lastAutoRefresh = DateTimeOffset.Now;
        try
        {
            var analyzer = _services.CreateAnalyzer();
            var settings = _services.Settings;
            var journal = _services.Journal;
            var data = await Task.Run(() =>
            {
                var assignments = journal.Assignments(day);
                var a = analyzer.Analyze(day, _services.Activity.SegmentsForDays(day, day), _services.Activity.CommitsForDays(day, day), assignments);
                return (
                    Analysis: a,
                    Summary: DaySummarizer.Summarize(a, settings.Worklog),
                    Assignments: assignments,
                    Drafts: journal.Drafts(day),
                    Closure: journal.Closure(day),
                    Pending: FindPendingDay(day));
            });

            Day = day;
            _analysis = data.Analysis;
            _summary = data.Summary;
            Apply(data.Analysis, data.Summary, data.Assignments, data.Drafts, data.Closure);
            PendingDay = data.Pending;
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

    /// <summary>Son 7 günde, en az 30 dk çalışılmış ama kapatılmamış en yakın iş günü (bugün hariç).</summary>
    private PendingDay? FindPendingDay(DateOnly viewing)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-7);
        var minutes = _services.Activity.ActiveMinutesByDay(from, today.AddDays(-1));
        var closed = _services.Journal.ClosedDays(from, today);
        var workdays = _services.Settings.Worklog.Workdays;
        var pending = minutes
            .Where(kv => kv.Value >= 30 && !closed.Contains(kv.Key) && kv.Key != viewing
                         && workdays.Contains(((int)kv.Key.DayOfWeek + 6) % 7 + 1))
            .OrderByDescending(kv => kv.Key)
            .FirstOrDefault();
        return pending.Key == default
            ? null
            : new PendingDay(pending.Key, $"{DurationText.DayTitle(pending.Key)} kapatılmadı ({DurationText.Format(TimeSpan.FromMinutes(pending.Value))} çalışma).");
    }

    private void Apply(
        DayAnalysis a,
        DaySummary summary,
        IReadOnlyDictionary<GroupRef, string> assignments,
        IReadOnlyList<WorklogDraft> drafts,
        DayClosure? closure)
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
        var dayStart = ActivityRepository.StartOf(a.Day);
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

        // Sınıflandırılmamış işler ve atamalar
        var quickKeys = a.Issues.Where(i => i.Duration > TimeSpan.Zero).Take(5).Select(i => i.Key).ToList();
        Unclassified = a.Unclassified.Take(15).Select(u => new UnclassifiedItem(AssignAsync, PreviewAsync)
        {
            Label = u.Label,
            DurationText = DurationText.Format(u.Duration),
            Detail = string.Join(" · ", u.Titles.Take(2).Select(t => t.Text)),
            Group = u.Group,
            SuggestedKey = u.SuggestedKey,
            QuickKeys = quickKeys,
            KeyInput = u.SuggestedKey ?? "",
        }).ToList();
        Assignments = assignments.Select(kv => new AssignmentRow(
            kv.Key,
            $"{_services.CreateAnalyzer().Classifier.AppLabel(kv.Key.App)} · {kv.Key.GroupKey} → {kv.Value}",
            new AsyncRelayCommand(() => UnassignAsync(kv.Key)))).ToList();

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

        // Worklog: öneriler + kullanıcının kalıcı taslakları (taslak varsa o kazanır)
        PopulateWorklogs(a, drafts);

        // Kapanış
        IsClosed = closure is not null;
        ClosedText = closure is null
            ? ""
            : $"Gün kapatıldı · {closure.ClosedAt.ToString("d MMM HH:mm", DurationText.Turkish)} · {DurationText.Format(TimeSpan.FromMinutes(closure.WorklogMinutes))} worklog";
    }

    private void PopulateWorklogs(DayAnalysis a, IReadOnlyList<WorklogDraft> drafts)
    {
        _populating = true;
        try
        {
            foreach (var row in Worklogs)
                row.PropertyChanged -= OnWorklogChanged;
            Worklogs.Clear();

            var draftMap = drafts.ToDictionary(d => d.IssueKey, StringComparer.Ordinal);
            foreach (var s in WorklogPlanner.Suggest(a, _services.Settings.Worklog))
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
                if (draftMap.Remove(s.IssueKey, out var draft))
                    ApplyDraft(row, draft);
                Add(row);
            }

            // İzlenmemiş ama kullanıcının elle eklediği satırlar
            foreach (var draft in draftMap.Values)
            {
                var row = new WorklogRow
                {
                    IssueKey = draft.IssueKey,
                    TrackedText = "elle",
                    Brush = IssuePalette.BrushFor(draft.IssueKey),
                    Manual = true,
                    RemoveCommand = new RelayCommand(() => RemoveManualRow(draft.IssueKey)),
                };
                ApplyDraft(row, draft);
                Add(row);
            }
        }
        finally
        {
            _populating = false;
        }
        OnPropertyChanged(nameof(HasWorklogs));
        UpdateWorklogTotal();

        void Add(WorklogRow row)
        {
            row.PropertyChanged += OnWorklogChanged;
            Worklogs.Add(row);
        }

        static void ApplyDraft(WorklogRow row, WorklogDraft draft)
        {
            row.Minutes = draft.Minutes;
            row.Start = draft.StartTime;
            row.Description = draft.Description;
            row.IsEdited = true;
        }
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
        if (i.Source == ClassificationSource.Manual)
            parts.Add("elle atandı");
        return string.Join(" · ", parts);
    }

    private void OnWorklogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorklogRow.Minutes))
            UpdateWorklogTotal();
        if (_populating || sender is not WorklogRow row)
            return;
        if (e.PropertyName is nameof(WorklogRow.Minutes) or nameof(WorklogRow.Start) or nameof(WorklogRow.Description))
            SaveDraft(row);
    }

    private void SaveDraft(WorklogRow row)
    {
        try
        {
            _services.Journal.SaveDraft(new WorklogDraft(Day, row.IssueKey, row.MinutesValue, row.Start.Trim(), row.Description, row.Manual));
            row.IsEdited = true;
        }
        catch (Exception ex)
        {
            Error = "Taslak kaydedilemedi: " + ex.Message;
        }
    }

    private void UpdateWorklogTotal()
    {
        var total = Worklogs.Sum(w => w.MinutesValue);
        var target = _services.Settings.Worklog.DailyTargetHours * 60;
        WorklogTotal = target > 0
            ? $"Toplam {DurationText.Format(TimeSpan.FromMinutes(total))} · hedef {DurationText.Format(TimeSpan.FromMinutes(target))}"
            : $"Toplam {DurationText.Format(TimeSpan.FromMinutes(total))}";
    }

    // ------------------------------------------------------------------ atama

    private async Task AssignAsync(UnclassifiedItem item)
    {
        if (item.Group is not { } group)
            return;
        var key = _services.CreateAnalyzer().Classifier.NormalizeKey(item.KeyInput);
        if (key is null)
        {
            item.Error = "Geçerli bir issue key girin (ör. PAY-1042).";
            return;
        }
        try
        {
            _services.Journal.Assign(Day, group, key);
            if (item.CreateRule)
            {
                _services.AddRule(group.ToRulePattern(), key); // ayar değişikliği tüm ekranları yeniler
                ShowToast($"{item.Label} → {key} atandı ve kural olarak kaydedildi.");
            }
            else
            {
                ShowToast($"{item.Label} → {key} atandı.");
                await LoadAsync(Day);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            item.Error = ex.Message;
        }
    }

    private async Task<string> PreviewAsync(UnclassifiedItem item)
    {
        if (item.Group is not { } group)
            return "";
        var analyzer = _services.CreateAnalyzer();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = await Task.Run(() => RulePreview.Measure(
            group.ToRulePattern(),
            _services.Activity.SegmentsForDays(today.AddDays(-30), today),
            analyzer.Classifier));
        return result.Captured <= TimeSpan.Zero
            ? "Son 30 günde başka eşleşme yok."
            : $"Kural son 30 günde {DurationText.Format(result.Captured)} çalışmayı ({result.Days} gün) bu issue'ya bağlar.";
    }

    private async Task UnassignAsync(GroupRef group)
    {
        _services.Journal.Unassign(Day, group);
        await LoadAsync(Day);
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
    private Task Refresh() => LoadAsync(Day);

    [RelayCommand]
    private Task OpenPendingDay() => PendingDay is { } p ? LoadAsync(p.Day) : Task.CompletedTask;

    [RelayCommand]
    private void OpenRangeSummary() => RangeSummaryRequested?.Invoke(this, EventArgs.Empty);

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
    private async Task ResetWorklogs()
    {
        _services.Journal.ClearDrafts(Day);
        await LoadAsync(Day);
        ShowToast("Worklog düzenlemeleri silindi; öneriler yeniden hesaplandı.");
    }

    [RelayCommand]
    private void AddWorklog()
    {
        var key = _services.CreateAnalyzer().Classifier.NormalizeKey(NewIssueKey);
        if (key is null)
        {
            ShowToast("Geçerli bir issue key girin (ör. PAY-1042).");
            return;
        }
        if (Worklogs.Any(w => w.IssueKey == key))
        {
            ShowToast($"{key} zaten listede.");
            return;
        }
        var row = new WorklogRow
        {
            IssueKey = key,
            TrackedText = "elle",
            Brush = IssuePalette.BrushFor(key),
            Manual = true,
            Minutes = _services.Settings.Worklog.RoundingMinutes > 0 ? _services.Settings.Worklog.RoundingMinutes : 15,
            Start = "09:00",
            RemoveCommand = new RelayCommand(() => RemoveManualRow(key)),
        };
        row.PropertyChanged += OnWorklogChanged;
        Worklogs.Add(row);
        SaveDraft(row);
        NewIssueKey = "";
        OnPropertyChanged(nameof(HasWorklogs));
        UpdateWorklogTotal();
    }

    private void RemoveManualRow(string issueKey)
    {
        _services.Journal.DeleteDraft(Day, issueKey);
        var row = Worklogs.FirstOrDefault(w => w.IssueKey == issueKey);
        if (row is not null)
        {
            row.PropertyChanged -= OnWorklogChanged;
            Worklogs.Remove(row);
        }
        UpdateWorklogTotal();
    }

    [RelayCommand]
    private async Task CloseDay()
    {
        if (_analysis is null)
            return;
        _services.Journal.Close(new DayClosure(
            Day,
            DateTimeOffset.Now,
            (int)Math.Round(_analysis.Active.TotalMinutes),
            Worklogs.Sum(w => w.MinutesValue),
            _summary.ToPlainText(Day)));
        await LoadAsync(Day);
        ShowToast("Gün kapatıldı.");
    }

    [RelayCommand]
    private async Task ReopenDay()
    {
        _services.Journal.Reopen(Day);
        await LoadAsync(Day);
    }

    private async void ShowToast(string message)
    {
        Toast = message;
        await Task.Delay(2800);
        if (Toast == message)
            Toast = null;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Formatting;
using WorkLogger.Core.Settings;
using WorkLogger.Core.Summaries;
using WorkLogger.Desktop.Services;
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Desktop.ViewModels;

public sealed record RangeIssueRow(string Key, string DurationText, string Detail, Avalonia.Media.IBrush Brush);

/// <summary>"Son X saatte ne yaptım?" sorusunun cevabı. Gün analizinin aynısı, keyfi bir aralığa uygulanır.</summary>
public partial class RangeSummaryViewModel(AppServices services) : ObservableObject
{
    private DaySummary _summary = DaySummary.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Is1h), nameof(Is2h), nameof(Is4h), nameof(IsToday))]
    private int _hours = 2;

    public bool Is1h => Hours == 1;
    public bool Is2h => Hours == 2;
    public bool Is4h => Hours == 4;
    public bool IsToday => Hours == 0;

    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private IReadOnlyList<string> _narrative = [];
    [ObservableProperty] private IReadOnlyList<RangeIssueRow> _issues = [];
    [ObservableProperty] private string _otherText = "";
    [ObservableProperty] private string? _toast;

    [RelayCommand]
    public async Task Select(string hours)
    {
        Hours = int.Parse(hours, System.Globalization.CultureInfo.InvariantCulture);
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var from = Hours == 0 ? ActivityRepository.StartOf(today) : now.AddHours(-Hours);
        var analyzer = services.CreateAnalyzer();
        // Aralık özetinde günlük hedef anlamsız; hedef vurgusu kapatılır.
        var worklog = new WorklogSettings { DailyTargetHours = 0 };

        var (analysis, summary) = await Task.Run(() =>
        {
            var day = DateOnly.FromDateTime(from.LocalDateTime);
            var a = analyzer.AnalyzeRange(
                day,
                from,
                now,
                services.Activity.Segments(from, now),
                services.Activity.Commits(from, now),
                services.Journal.Assignments(today));
            return (a, DaySummarizer.Summarize(a, worklog));
        });

        _summary = summary;
        RangeText = $"{DurationText.Clock(from)} – {DurationText.Clock(now)}";
        Headline = summary.Headline;
        Narrative = summary.Narrative;
        Issues = analysis.Issues
            .Where(i => i.Duration >= DaySummarizer.MinMention)
            .Select(i => new RangeIssueRow(
                i.Key,
                DurationText.Format(i.Duration),
                string.Join(" · ", i.Topics.Take(2).Select(t => t.Text)),
                IssuePalette.BrushFor(i.Key)))
            .ToList();
        OtherText = analysis.UnclassifiedTotal > TimeSpan.Zero
            ? $"Issue'ya bağlanmayan: {DurationText.Format(analysis.UnclassifiedTotal)} (" +
              string.Join(", ", analysis.Unclassified.Take(3).Select(u => u.Label)) + ")"
            : "";
    }

    [RelayCommand]
    private async Task Copy()
    {
        var text = $"{RangeText}\n" + _summary.ToPlainText(DateOnly.FromDateTime(DateTime.Now));
        await UiServices.CopyTextAsync(text);
        Toast = "Panoya kopyalandı.";
        await Task.Delay(2000);
        Toast = null;
    }
}

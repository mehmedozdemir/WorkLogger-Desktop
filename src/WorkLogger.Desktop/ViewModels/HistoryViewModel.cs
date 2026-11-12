using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Analysis;
using WorkLogger.Core.Formatting;
using WorkLogger.Desktop.Controls;
using WorkLogger.Desktop.Services;

namespace WorkLogger.Desktop.ViewModels;

public enum HistoryRange
{
    Week,
    Month,
    Quarter,
    Year,
    All,
}

public sealed record ProjectRow(string Project, string DurationText, double Share, IBrush Brush, string Detail);

public sealed record IssueTotalRow(string Key, string DurationText, double Share, IBrush Brush, string Detail);

public partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<DateOnly> _openDay;
    private PeriodAnalysis? _period;

    public HistoryViewModel(AppServices services, Action<DateOnly> openDay)
    {
        _services = services;
        _openDay = openDay;
    }

    public bool IsLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeek), nameof(IsMonth), nameof(IsQuarter), nameof(IsYear), nameof(IsAll))]
    private HistoryRange _range = HistoryRange.Month;

    public bool IsWeek => Range == HistoryRange.Week;
    public bool IsMonth => Range == HistoryRange.Month;
    public bool IsQuarter => Range == HistoryRange.Quarter;
    public bool IsYear => Range == HistoryRange.Year;
    public bool IsAll => Range == HistoryRange.All;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private string? _message;

    [ObservableProperty] private string _totalText = "–";
    [ObservableProperty] private string _totalDetail = "";
    [ObservableProperty] private string _averageText = "–";
    [ObservableProperty] private string _averageDetail = "";
    [ObservableProperty] private string _classifiedText = "–";
    [ObservableProperty] private string _classifiedDetail = "";
    [ObservableProperty] private string _focusText = "–";
    [ObservableProperty] private string _focusDetail = "";

    [ObservableProperty] private IReadOnlyList<BarItem> _dailyBars = [];
    [ObservableProperty] private int _labelEvery = 1;
    [ObservableProperty] private double _targetHours;
    [ObservableProperty] private IReadOnlyList<HeatCell> _heatCells = [];
    [ObservableProperty] private double[,]? _weekHour;
    [ObservableProperty] private IReadOnlyList<ProjectRow> _projects = [];
    [ObservableProperty] private IReadOnlyList<IssueTotalRow> _topIssues = [];
    [ObservableProperty] private IReadOnlyList<AppRow> _apps = [];

    [RelayCommand]
    private Task SelectRange(HistoryRange range)
    {
        Range = range;
        return ReloadAsync();
    }

    [RelayCommand]
    private void OpenDay(object? value)
    {
        if (value is DateOnly day)
            _openDay(day);
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        Message = null;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var firstDay = await Task.Run(_services.Activity.FirstDay) ?? today;
            var from = Range switch
            {
                HistoryRange.Week => today.AddDays(-6),
                HistoryRange.Month => today.AddDays(-29),
                HistoryRange.Quarter => today.AddDays(-89),
                HistoryRange.Year => new DateOnly(today.Year, 1, 1),
                _ => firstDay,
            };
            if (Range == HistoryRange.All && from > today)
                from = today;

            var analyzer = _services.CreateAnalyzer();
            var period = await Task.Run(() => PeriodAnalyzer.Analyze(
                analyzer,
                from,
                today,
                _services.Activity.SegmentsForDays(from.AddDays(-1), today),
                _services.Activity.CommitsForDays(from, today)));
            _period = period;
            Apply(period);
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            Message = "Geçmiş yüklenemedi: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(PeriodAnalysis p)
    {
        HasData = p.Active > TimeSpan.Zero;
        RangeText = $"{p.From.ToString("d MMMM yyyy", DurationText.Turkish)} – {p.To.ToString("d MMMM yyyy", DurationText.Turkish)}";
        TargetHours = _services.Settings.Worklog.DailyTargetHours;

        TotalText = DurationText.Hours(p.Active, 0);
        TotalDetail = $"{p.DaysWorked} çalışma günü · {p.CommitCount} commit";
        AverageText = DurationText.Format(p.AveragePerWorkedDay);
        var worked = p.Days.Where(d => d.Active >= TimeSpan.FromMinutes(30)).ToList();
        var avgStart = AverageClock(worked.Select(d => d.FirstActivity));
        var avgEnd = AverageClock(worked.Select(d => d.LastActivity));
        AverageDetail = worked.Count > 0 ? $"ortalama {avgStart} – {avgEnd}" : "Yeterli veri yok";
        ClassifiedText = p.Active > TimeSpan.Zero ? DurationText.Percent(p.ClassifiedRatio) : "–";
        ClassifiedDetail = $"{p.Issues.Count} issue · {p.Projects.Count} proje";
        FocusText = DurationText.Format(p.Focus);
        FocusDetail = p.Active > TimeSpan.Zero ? $"aktif sürenin {DurationText.Percent(p.Focus / p.Active)} kadarı odak" : "";

        var accent = Color.Parse("#4C6EF5");
        DailyBars = p.Days.Select(d => new BarItem(
            p.Days.Count <= 14 ? d.Day.ToString("ddd", DurationText.Turkish) : d.Day.ToString("d.MM", CultureInfo.InvariantCulture),
            d.Classified.TotalHours,
            (d.Active - d.Classified).TotalHours,
            $"{DurationText.DayTitle(d.Day)}\nAktif {DurationText.Format(d.Active)} · issue {DurationText.Format(d.Classified)}\nOdak {DurationText.Format(d.Focus)} · {d.CommitCount} commit",
            d.Day,
            accent)).ToList();
        LabelEvery = p.Days.Count switch
        {
            <= 14 => 1,
            <= 31 => 3,
            <= 92 => 7,
            _ => 30,
        };

        HeatCells = p.Days.Select(d => new HeatCell(d.Day, d.Active.TotalMinutes,
            $"{DurationText.DayTitle(d.Day)}\n{DurationText.Format(d.Active)} aktif")).ToList();

        var matrix = new double[7, 24];
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 24; c++)
                matrix[r, c] = p.WeekdayHour[r, c].TotalMinutes;
        }
        WeekHour = matrix;

        var maxProject = p.Projects.Count > 0 ? p.Projects[0].Duration.TotalSeconds : 1;
        Projects = p.Projects.Take(10).Select(x => new ProjectRow(
            x.Project,
            DurationText.Format(x.Duration),
            maxProject > 0 ? x.Duration.TotalSeconds / maxProject : 0,
            IssuePalette.BrushFor(x.Project),
            $"{x.IssueCount} issue · pay {DurationText.Percent(p.Classified > TimeSpan.Zero ? x.Duration / p.Classified : 0)}")).ToList();

        var maxIssue = p.Issues.Count > 0 ? p.Issues[0].Duration.TotalSeconds : 1;
        TopIssues = p.Issues.Take(12).Select(x => new IssueTotalRow(
            x.IssueKey,
            DurationText.Format(x.Duration),
            maxIssue > 0 ? x.Duration.TotalSeconds / maxIssue : 0,
            IssuePalette.BrushFor(x.IssueKey),
            $"{x.DaysWorked} gün · {ShortDate(x.FirstDay)} – {ShortDate(x.LastDay)}" + (x.CommitCount > 0 ? $" · {x.CommitCount} commit" : ""))).ToList();

        Apps = p.Apps.Take(8).Select((x, i) => new AppRow(
            x.App,
            DurationText.Format(x.Duration),
            p.Active > TimeSpan.Zero ? x.Duration / p.Active : 0,
            new ImmutableSolidColorBrush(IssuePalette.Sequential(i)))).ToList();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (_period is null)
            return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var daysFile = Path.Combine(_services.Paths.ExportsDirectory, $"gunler-{stamp}.csv");
        var issuesFile = Path.Combine(_services.Paths.ExportsDirectory, $"issuelar-{stamp}.csv");
        var tr = DurationText.Turkish;

        var days = new StringBuilder("Tarih;Aktif (sa);Issue (sa);Boşta (sa);Odak (sa);İş değişimi;İlk;Son;Commit\n");
        foreach (var d in _period.Days)
        {
            days.AppendLine(string.Join(';',
                d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                d.Active.TotalHours.ToString("0.00", tr),
                d.Classified.TotalHours.ToString("0.00", tr),
                d.Idle.TotalHours.ToString("0.00", tr),
                d.Focus.TotalHours.ToString("0.00", tr),
                d.ContextSwitches,
                DurationText.Clock(d.FirstActivity),
                DurationText.Clock(d.LastActivity),
                d.CommitCount));
        }
        var issues = new StringBuilder("Issue;Proje;Süre (sa);Gün sayısı;İlk gün;Son gün;Commit\n");
        foreach (var i in _period.Issues)
        {
            issues.AppendLine(string.Join(';',
                i.IssueKey,
                Core.Classification.IssueClassifier.ProjectOf(i.IssueKey),
                i.Duration.TotalHours.ToString("0.00", tr),
                i.DaysWorked,
                i.FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                i.LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                i.CommitCount));
        }
        // Excel'in Türkçe karakterleri doğru açması için UTF-8 BOM.
        File.WriteAllText(daysFile, days.ToString(), new UTF8Encoding(true));
        File.WriteAllText(issuesFile, issues.ToString(), new UTF8Encoding(true));
        Message = $"Dışa aktarıldı: {Path.GetFileName(daysFile)}, {Path.GetFileName(issuesFile)}";
        AppServices.OpenPath(_services.Paths.ExportsDirectory);
    }

    private static string ShortDate(DateOnly d) => d.ToString("d MMM", DurationText.Turkish);

    private static string AverageClock(IEnumerable<DateTimeOffset?> times)
    {
        var minutes = times.OfType<DateTimeOffset>().Select(t => t.ToLocalTime().TimeOfDay.TotalMinutes).ToList();
        if (minutes.Count == 0)
            return "–";
        var avg = TimeSpan.FromMinutes(minutes.Average());
        return $"{(int)avg.TotalHours:00}:{avg.Minutes:00}";
    }
}

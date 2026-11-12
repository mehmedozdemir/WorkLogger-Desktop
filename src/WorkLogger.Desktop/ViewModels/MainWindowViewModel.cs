using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Formatting;
using WorkLogger.Core.Tracking;
using WorkLogger.Desktop.Services;

namespace WorkLogger.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _statusTimer;
    private DateOnly? _dayEndShownFor;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        Day = new DayViewModel(services);
        History = new HistoryViewModel(services, OpenDay);
        Settings = new SettingsViewModel(services);
        _currentPage = Day;

        services.SettingsChanged += (_, _) =>
        {
            _ = Day.ReloadAsync();
            _ = History.ReloadAsync();
        };

        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Tick());
        _statusTimer.Start();
        Tick();
    }

    public DayViewModel Day { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Gün sonu saati geldiğinde pencerenin öne getirilmesi istenir.</summary>
    public event EventHandler? DayEndReached;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayPage), nameof(IsHistoryPage), nameof(IsSettingsPage))]
    private ObservableObject _currentPage;

    public bool IsDayPage => CurrentPage == Day;
    public bool IsHistoryPage => CurrentPage == History;
    public bool IsSettingsPage => CurrentPage == Settings;

    [ObservableProperty] private bool _isTracking;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isAgentDown;
    [ObservableProperty] private string _statusText = "Bağlanıyor…";
    [ObservableProperty] private string _currentActivity = "";
    [ObservableProperty] private string _pauseButtonText = "Duraklat";

    public string VersionText { get; } = "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    [RelayCommand]
    private void ShowDay() => CurrentPage = Day;

    [RelayCommand]
    private async Task ShowToday()
    {
        CurrentPage = Day;
        await Day.LoadAsync(DateOnly.FromDateTime(DateTime.Now));
    }

    [RelayCommand]
    private void ShowHistory()
    {
        CurrentPage = History;
        if (!History.IsLoaded)
            _ = History.ReloadAsync();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        Settings.Load();
        CurrentPage = Settings;
    }

    [RelayCommand]
    private void TogglePause()
    {
        _services.SetPaused(!IsPaused);
        Tick();
    }

    [RelayCommand]
    private void StartAgent()
    {
        _services.EnsureAgentRunning();
        StatusText = "Başlatılıyor…";
    }

    private void OpenDay(DateOnly day)
    {
        CurrentPage = Day;
        _ = Day.LoadAsync(day);
    }

    private void Tick()
    {
        try
        {
            var snapshot = _services.AgentSnapshot();
            IsAgentDown = !snapshot.Running;
            IsPaused = snapshot.Running && snapshot.Paused;
            IsTracking = snapshot.Running && !snapshot.Paused;
            PauseButtonText = IsPaused ? "Sürdür" : "Duraklat";
            StatusText = !snapshot.Running ? "İzleyici çalışmıyor" : snapshot.Paused ? "Duraklatıldı" : snapshot.Idle ? "Boşta" : "İzleniyor";

            if (snapshot.Running && !snapshot.Paused && !snapshot.Idle && snapshot.App is { } app && app != TrackingStateMachine.IdleApp)
            {
                var label = _services.CreateAnalyzer().Classifier.AppLabel(app);
                var since = snapshot.CurrentSince is { } s ? $" · {DurationText.Format(DateTimeOffset.Now - s)}" : "";
                CurrentActivity = $"{label}{since}";
            }
            else
            {
                CurrentActivity = "";
            }
        }
        catch (Exception)
        {
            StatusText = "Durum okunamadı";
        }

        CheckDayEnd();
        Day.OnClockTick();
    }

    private void CheckDayEnd()
    {
        var settings = _services.Settings.Worklog;
        if (!settings.OpenDayEndAutomatically || !TimeOnly.TryParse(settings.DayEndTime, out var dayEnd))
            return;
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var isoDay = ((int)now.DayOfWeek + 6) % 7 + 1; // 1 = Pazartesi
        if (_dayEndShownFor == today || !settings.Workdays.Contains(isoDay))
            return;
        var time = TimeOnly.FromDateTime(now);
        if (time < dayEnd || time > dayEnd.AddHours(4))
            return;

        _dayEndShownFor = today;
        CurrentPage = Day;
        _ = Day.LoadAsync(today);
        DayEndReached?.Invoke(this, EventArgs.Empty);
    }
}

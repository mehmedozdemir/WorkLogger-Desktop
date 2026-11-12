using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WorkLogger.Core.Settings;
using WorkLogger.Core.Tracking;
using WorkLogger.Infrastructure;
using WorkLogger.Infrastructure.Data;
using WorkLogger.Infrastructure.Win32;

namespace WorkLogger.Agent;

/// <summary>
/// Ön plan penceresini örnekler ve <see cref="TrackingStateMachine"/> çıktısını veritabanına yazar.
/// Açık segment periyodik olarak kaydedilir; süreç çökse bile en fazla <see cref="CheckpointInterval"/> kaybolur.
/// </summary>
public sealed class TrackerService(
    ActivityRepository activity,
    AgentStateRepository state,
    SettingsStore settingsStore,
    IHostApplicationLifetime lifetime,
    ILogger<TrackerService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettingsCheckInterval = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly WindowProbe _probe = new();
    private TrackingStateMachine _machine = null!;
    private WorkLoggerSettings _settings = new();
    private long? _currentId;
    private DateTimeOffset _lastCheckpoint;
    private DateTimeOffset _lastHeartbeat;
    private DateTime _settingsStamp;
    private DateTimeOffset _lastSettingsCheck;
    private volatile bool _locked;
    private bool _paused;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadSettings();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        state.Set(new Dictionary<string, string?>
        {
            [AgentStateRepository.ProcessId] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            [AgentStateRepository.Version] = typeof(TrackerService).Assembly.GetName().Version?.ToString(3),
            [AgentStateRepository.ShutdownRequested] = null,
        });
        logger.LogInformation("İzleyici başladı (örnekleme {Interval}s, boşta eşiği {Idle}s)",
            _settings.Tracking.PollIntervalSeconds, _settings.Tracking.IdleThresholdSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Örnekleme hatası");
                }
                await Task.Delay(TimeSpan.FromSeconds(_settings.Tracking.PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            StopTracking(DateTimeOffset.Now, "kapanış");
            state.Set(AgentStateRepository.Heartbeat, null);
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (now - _lastSettingsCheck >= SettingsCheckInterval)
        {
            _lastSettingsCheck = now;
            if (settingsStore.LastWriteTimeUtc != _settingsStamp)
                LoadSettings();
        }

        var commands = state.GetAll();
        if (commands.GetValueOrDefault(AgentStateRepository.ShutdownRequested) == "1")
        {
            // Desktop "izlemeyi durdur ve çık" dedi: segmenti temiz kapatıp süreci sonlandır.
            state.Set(AgentStateRepository.ShutdownRequested, null);
            logger.LogInformation("Kapanma isteği alındı");
            lifetime.StopApplication();
            return;
        }

        var pauseRequested = commands.GetValueOrDefault(AgentStateRepository.PauseRequested) == "1";
        if (pauseRequested != _paused)
        {
            _paused = pauseRequested;
            if (_paused)
                StopTracking(now, "duraklatıldı");
            logger.LogInformation(_paused ? "İzleme duraklatıldı" : "İzleme sürdürülüyor");
        }

        if (_paused)
        {
            Heartbeat(now, force: false);
            return;
        }

        var window = _probe.Foreground();
        var observation = new Observation(now, window.App, window.Title, WindowProbe.IdleTime(), _locked);

        lock (_gate)
        {
            var step = _machine.Observe(observation);
            if (step.Closed is { } closed && _currentId is { } closedId)
                activity.CloseSegment(closedId, closed.LastSeen);

            if (step.Opened is { } opened)
            {
                _currentId = activity.InsertSegment(opened.Start, opened.LastSeen, opened.App, opened.Title, opened.Idle);
                _lastCheckpoint = now;
                PublishCurrent(opened);
            }
            else if (_currentId is { } id && _machine.Current is { } current && now - _lastCheckpoint >= CheckpointInterval)
            {
                activity.UpdateSegmentEnd(id, current.LastSeen);
                _lastCheckpoint = now;
            }
        }

        Heartbeat(now, force: false);
    }

    private void StopTracking(DateTimeOffset at, string reason)
    {
        lock (_gate)
        {
            if (_machine?.Stop(at) is { } closed && _currentId is { } id)
            {
                activity.CloseSegment(id, closed.LastSeen);
                logger.LogInformation("Segment kapatıldı ({Reason})", reason);
            }
            _currentId = null;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            // Uyku öncesi segmenti son görülme anında kapat; uyku süresi hiçbir segmente yazılmaz.
            var lastSeen = _machine.Current?.LastSeen ?? DateTimeOffset.Now;
            StopTracking(lastSeen, "uyku");
        }
        else if (e.Mode == PowerModes.Resume)
        {
            logger.LogInformation("Uykudan dönüldü");
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        _locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect;
        logger.LogInformation("Oturum durumu: {Reason}", e.Reason);
    }

    private void PublishCurrent(OpenSegment segment) =>
        state.Set(new Dictionary<string, string?>
        {
            [AgentStateRepository.CurrentApp] = segment.App,
            [AgentStateRepository.CurrentTitle] = segment.Title,
            [AgentStateRepository.CurrentIdle] = segment.Idle ? "1" : "0",
            [AgentStateRepository.CurrentSince] = segment.Start.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            [AgentStateRepository.Heartbeat] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        });

    private void Heartbeat(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastHeartbeat < HeartbeatInterval)
            return;
        _lastHeartbeat = now;
        state.Set(AgentStateRepository.Heartbeat, now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
    }

    private void LoadSettings()
    {
        var (settings, error) = settingsStore.Load();
        if (error is not null)
            logger.LogWarning("{Error}", error);
        _settingsStamp = settingsStore.LastWriteTimeUtc;

        var interval = TimeSpan.FromSeconds(settings.Tracking.PollIntervalSeconds);
        var maxGap = interval * 3 + TimeSpan.FromSeconds(15);
        lock (_gate)
        {
            var current = _machine?.Current;
            _machine = new TrackingStateMachine(TimeSpan.FromSeconds(settings.Tracking.IdleThresholdSeconds), maxGap, settings.Tracking.IgnoredApps);
            if (current is not null && _currentId is { } id)
            {
                // Yeni makine boş başlar; açık segmenti kapatıp bir sonraki gözlemde temiz başlarız.
                activity.CloseSegment(id, current.LastSeen);
                _currentId = null;
            }
        }
        _settings = settings;
    }
}

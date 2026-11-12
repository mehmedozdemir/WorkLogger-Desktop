using System.Diagnostics;
using WorkLogger.Agent;
using WorkLogger.Core.Analysis;
using WorkLogger.Core.Classification;
using WorkLogger.Core.Settings;
using WorkLogger.Infrastructure;
using WorkLogger.Infrastructure.Data;

namespace WorkLogger.Desktop.Services;

/// <summary>Desktop sürecinin paylaşılan servisleri. Basit bir kompozisyon kökü.</summary>
public sealed class AppServices
{
    public AppServices()
    {
        Paths = new AppPaths();
        Database = new Database(Paths.DatabaseFile);
        Database.Migrate();
        Activity = new ActivityRepository(Database);
        AgentState = new AgentStateRepository(Database);
        SettingsStore = new SettingsStore(Paths);
        ReloadSettings();
    }

    public AppPaths Paths { get; }
    public Database Database { get; }
    public ActivityRepository Activity { get; }
    public AgentStateRepository AgentState { get; }
    public SettingsStore SettingsStore { get; }
    public WorkLoggerSettings Settings { get; private set; } = new();
    public string? SettingsError { get; private set; }

    public event EventHandler? SettingsChanged;

    public void ReloadSettings()
    {
        (Settings, SettingsError) = SettingsStore.Load();
    }

    public void SaveSettings(WorkLoggerSettings settings)
    {
        SettingsStore.Save(settings);
        ReloadSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public DayAnalyzer CreateAnalyzer() =>
        new(new IssueClassifier(Settings.Classification), Settings.Tracking);

    // ------------------------------------------------------------------ agent

    public AgentSnapshot AgentSnapshot() =>
        Infrastructure.Data.AgentSnapshot.From(AgentState.GetAll(), TimeSpan.FromSeconds(40));

    /// <summary>Agent çalışmıyorsa aynı exe'yi "agent" argümanıyla ayrı süreç olarak başlatır.</summary>
    public bool EnsureAgentRunning()
    {
        if (AgentHost.IsRunning())
            return true;
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, AgentHost.Argument)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public void SetPaused(bool paused) => AgentState.Set(AgentStateRepository.PauseRequested, paused ? "1" : "0");

    public static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}

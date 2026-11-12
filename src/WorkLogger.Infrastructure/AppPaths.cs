namespace WorkLogger.Infrastructure;

/// <summary>Veri, ayar ve log dosyalarının yerleri. WORKLOGGER_HOME ortam değişkeni ile değiştirilebilir (testler).</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root
            ?? Environment.GetEnvironmentVariable("WORKLOGGER_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkLogger");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }

    public string Root { get; }
    public string DatabaseFile => Path.Combine(Root, "worklogger.db");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ExportsDirectory => Path.Combine(Root, "exports");

    /// <summary>Önceki Python sürümünün (tempo-tracker) veritabanı.</summary>
    public static string LegacyPythonDatabase =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TempoTracker", "activity.db");
}

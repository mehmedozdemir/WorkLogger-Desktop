using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Settings;
using WorkLogger.Desktop.Services;
using WorkLogger.Infrastructure;
using WorkLogger.Infrastructure.Data;
using WorkLogger.Infrastructure.Import;
using AutoStartRegistry = WorkLogger.Infrastructure.Platform.AutoStart;

namespace WorkLogger.Desktop.ViewModels;

public partial class SettingsViewModel(AppServices services) : ObservableObject
{
    // Genel
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _dayEndTime = "18:00";
    [ObservableProperty] private decimal? _dailyTargetHours = 8;
    [ObservableProperty] private bool _openDayEndAutomatically = true;

    // İzleme
    [ObservableProperty] private decimal? _idleMinutes = 5;
    [ObservableProperty] private string _workdayStart = "09:00";
    [ObservableProperty] private string _workdayEnd = "18:00";
    [ObservableProperty] private string _ignoredApps = "";

    // Sınıflandırma
    [ObservableProperty] private string _issueKeyPattern = "";
    [ObservableProperty] private string _ignoredKeys = "";
    [ObservableProperty] private string _allowedProjects = "";
    [ObservableProperty] private string _rules = "";

    // Worklog
    [ObservableProperty] private decimal? _roundingMinutes = 15;
    [ObservableProperty] private decimal? _minWorklogMinutes = 15;

    // Git
    [ObservableProperty] private string _repoRoots = "";
    [ObservableProperty] private string _authorEmail = "";
    [ObservableProperty] private decimal? _scanIntervalMinutes = 30;

    // Veri
    [ObservableProperty] private string _dataFolder = "";
    [ObservableProperty] private string _databaseInfo = "";
    [ObservableProperty] private bool _legacyAvailable;
    [ObservableProperty] private string _legacyPath = "";

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _isError;

    public void Load()
    {
        var s = services.Settings;
        AutoStart = AutoStart_IsEnabled();
        DayEndTime = s.Worklog.DayEndTime;
        DailyTargetHours = (decimal)s.Worklog.DailyTargetHours;
        OpenDayEndAutomatically = s.Worklog.OpenDayEndAutomatically;

        IdleMinutes = Math.Round((decimal)s.Tracking.IdleThresholdSeconds / 60, 1);
        WorkdayStart = s.Tracking.WorkdayStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        WorkdayEnd = s.Tracking.WorkdayEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        IgnoredApps = string.Join(Environment.NewLine, s.Tracking.IgnoredApps);

        IssueKeyPattern = s.Classification.IssueKeyPattern;
        IgnoredKeys = string.Join(", ", s.Classification.IgnoredIssueKeys);
        AllowedProjects = string.Join(", ", s.Classification.AllowedProjects);
        Rules = string.Join(Environment.NewLine, s.Classification.Rules.Select(r => $"{(r.Enabled ? "" : "# ")}{r.Pattern} => {r.IssueKey}"));

        RoundingMinutes = s.Worklog.RoundingMinutes;
        MinWorklogMinutes = s.Worklog.MinWorklogMinutes;

        RepoRoots = string.Join(Environment.NewLine, s.Git.RepoRoots);
        AuthorEmail = s.Git.AuthorEmail;
        ScanIntervalMinutes = s.Git.ScanIntervalMinutes;

        DataFolder = services.Paths.Root;
        LegacyPath = AppPaths.LegacyPythonDatabase;
        LegacyAvailable = File.Exists(LegacyPath);
        RefreshDatabaseInfo();

        if (services.SettingsError is { } error)
            ShowMessage(error, isError: true);
        else
            Message = null;
    }

    private static bool AutoStart_IsEnabled()
    {
        try
        {
            return AutoStartRegistry.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RefreshDatabaseInfo()
    {
        try
        {
            var count = services.Activity.CountSegments();
            var first = services.Activity.FirstDay();
            var size = new FileInfo(services.Paths.DatabaseFile).Length / 1024.0 / 1024.0;
            DatabaseInfo = $"{count:N0} segment · ilk kayıt {(first?.ToString("d MMMM yyyy", Core.Formatting.DurationText.Turkish) ?? "–")} · {size:0.0} MB";
        }
        catch (Exception ex)
        {
            DatabaseInfo = "Okunamadı: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var s = SettingsStore.Clone(services.Settings);
            s.Worklog.DayEndTime = DayEndTime.Trim();
            s.Worklog.DailyTargetHours = (double)(DailyTargetHours ?? 0);
            s.Worklog.OpenDayEndAutomatically = OpenDayEndAutomatically;
            s.Worklog.RoundingMinutes = (int)(RoundingMinutes ?? 0);
            s.Worklog.MinWorklogMinutes = (int)(MinWorklogMinutes ?? 0);

            s.Tracking.IdleThresholdSeconds = (int)Math.Round((IdleMinutes ?? 5) * 60);
            s.Tracking.WorkdayStart = ParseTime(WorkdayStart, "Mesai başlangıcı");
            s.Tracking.WorkdayEnd = ParseTime(WorkdayEnd, "Mesai bitişi");
            s.Tracking.IgnoredApps = Lines(IgnoredApps);

            s.Classification.IssueKeyPattern = IssueKeyPattern.Trim();
            s.Classification.IgnoredIssueKeys = Csv(IgnoredKeys);
            s.Classification.AllowedProjects = Csv(AllowedProjects).Select(x => x.ToUpperInvariant()).ToList();
            s.Classification.Rules = ParseRules(Rules);

            s.Git.RepoRoots = Lines(RepoRoots);
            s.Git.AuthorEmail = AuthorEmail.Trim();
            s.Git.ScanIntervalMinutes = (int)(ScanIntervalMinutes ?? 30);

            services.SaveSettings(s);

            var exe = Environment.ProcessPath;
            if (exe is not null && AutoStart != AutoStart_IsEnabled())
                AutoStartRegistry.Set(AutoStart, exe);

            ShowMessage("Ayarlar kaydedildi. İzleyici yeni ayarları en geç 30 saniye içinde uygular.", isError: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void Revert() => Load();

    [RelayCommand]
    private void OpenDataFolder() => AppServices.OpenPath(services.Paths.Root);

    [RelayCommand]
    private void OpenLogs() => AppServices.OpenPath(services.Paths.LogsDirectory);

    [RelayCommand]
    private async Task ImportLegacy()
    {
        try
        {
            ShowMessage("İçe aktarılıyor…", isError: false);
            var importer = new LegacyTrackerImporter(services.Database, services.Activity);
            var result = await Task.Run(() => importer.Import(LegacyPath));
            RefreshDatabaseInfo();
            ShowMessage(
                $"{result.Segments:N0} segment ve {result.Commits:N0} yeni commit içe aktarıldı ({result.FirstDay:dd.MM.yyyy} – {result.LastDay:dd.MM.yyyy}). Tekrar çalıştırmak güvenlidir.",
                isError: false);
        }
        catch (Exception ex)
        {
            ShowMessage("İçe aktarma başarısız: " + ex.Message, isError: true);
        }
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        IsError = isError;
    }

    private static TimeOnly ParseTime(string value, string field) =>
        TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : throw new FormatException($"{field} SS:DD biçiminde olmalı (ör. 09:00).");

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> Csv(string text) =>
        text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>"desen => KEY" satırları; '#' ile başlayan satır devre dışı kuraldır.</summary>
    internal static List<ClassificationRule> ParseRules(string text)
    {
        var rules = new List<ClassificationRule>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var enabled = !raw.StartsWith('#');
            var line = enabled ? raw : raw.TrimStart('#').Trim();
            var arrow = line.LastIndexOf("=>", StringComparison.Ordinal);
            if (arrow <= 0)
                throw new FormatException($"Kural satırı 'desen => ISSUE-1' biçiminde olmalı: {raw}");
            rules.Add(new ClassificationRule
            {
                Pattern = line[..arrow].Trim(),
                IssueKey = line[(arrow + 2)..].Trim().ToUpperInvariant(),
                Enabled = enabled,
            });
        }
        return rules;
    }
}

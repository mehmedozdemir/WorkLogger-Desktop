using System.Text.RegularExpressions;

namespace WorkLogger.Core.Settings;

/// <summary>
/// Kullanıcı ayarları. JSON olarak saklanır; dosyada olmayan alanlar buradaki varsayılanları korur.
/// </summary>
public sealed class WorkLoggerSettings
{
    public TrackingSettings Tracking { get; set; } = new();
    public ClassificationSettings Classification { get; set; } = new();
    public WorklogSettings Worklog { get; set; } = new();
    public GitSettings Git { get; set; } = new();
    public JiraSettings Jira { get; set; } = new();

    /// <summary>Ayarları doğrular; hata yoksa boş liste döner.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Tracking.PollIntervalSeconds is < 1 or > 60)
            errors.Add("Örnekleme aralığı 1-60 saniye arasında olmalı.");
        if (Tracking.IdleThresholdSeconds is < 30 or > 3600)
            errors.Add("Boşta eşiği 30-3600 saniye arasında olmalı.");
        if (!TimeOnly.TryParse(Worklog.DayEndTime, out _))
            errors.Add($"Gün sonu saati geçersiz: '{Worklog.DayEndTime}' (örnek: 18:00).");
        if (Worklog.RoundingMinutes is < 0 or > 60)
            errors.Add("Yuvarlama adımı 0-60 dakika arasında olmalı.");
        if (Worklog.DailyTargetHours is < 0 or > 24)
            errors.Add("Günlük hedef 0-24 saat arasında olmalı.");
        if (!IsValidRegex(Classification.IssueKeyPattern))
            errors.Add("Issue key deseni geçerli bir düzenli ifade değil.");
        foreach (var rule in Classification.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern) || !IsValidRegex(rule.Pattern))
                errors.Add($"Kural deseni geçersiz: '{rule.Pattern}'.");
            if (string.IsNullOrWhiteSpace(rule.IssueKey))
                errors.Add($"Kuralın issue key'i boş: '{rule.Pattern}'.");
        }
        return errors;
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class TrackingSettings
{
    public int PollIntervalSeconds { get; set; } = 2;
    public int IdleThresholdSeconds { get; set; } = 300;
    public bool Paused { get; set; }

    /// <summary>Bu uygulamalarda geçen süre boşta sayılır (kilit ekranı, arama kutusu vb.).</summary>
    public List<string> IgnoredApps { get; set; } =
        ["LockApp.exe", "LogonUI.exe", "SearchHost.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe"];

    public TimeOnly WorkdayStart { get; set; } = new(9, 0);
    public TimeOnly WorkdayEnd { get; set; } = new(18, 0);
}

public sealed class ClassificationSettings
{
    public string IssueKeyPattern { get; set; } = @"\b[A-Z][A-Z0-9]{1,9}-\d{1,6}\b";

    /// <summary>Issue key'e benzeyen ama issue olmayan ifadeler (UTF-8, SHA-256 ...). Önek de olabilir.</summary>
    public List<string> IgnoredIssueKeys { get; set; } =
        ["UTF-8", "UTF-16", "ISO-8859", "SHA-1", "SHA-256", "SHA-512", "HTTP-1", "RFC-2616", "WIN-10", "WIN-11", "COVID-19"];

    /// <summary>Boş değilse yalnızca bu proje öneklerine sahip key'ler kabul edilir (ör. PAY, CRM).</summary>
    public List<string> AllowedProjects { get; set; } = [];

    public List<ClassificationRule> Rules { get; set; } = [];

    public Dictionary<string, string> AppAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["devenv.exe"] = "Visual Studio",
        ["Code.exe"] = "VS Code",
        ["rider64.exe"] = "Rider",
        ["idea64.exe"] = "IntelliJ IDEA",
        ["chrome.exe"] = "Chrome",
        ["msedge.exe"] = "Edge",
        ["firefox.exe"] = "Firefox",
        ["WindowsTerminal.exe"] = "Terminal",
        ["powershell.exe"] = "PowerShell",
        ["pwsh.exe"] = "PowerShell",
        ["cmd.exe"] = "Komut İstemi",
        ["dbeaver.exe"] = "DBeaver",
        ["Ssms.exe"] = "SQL Server Management Studio",
        ["Postman.exe"] = "Postman",
        ["Teams.exe"] = "Microsoft Teams",
        ["ms-teams.exe"] = "Microsoft Teams",
        ["OUTLOOK.EXE"] = "Outlook",
        ["slack.exe"] = "Slack",
        ["Docker Desktop.exe"] = "Docker Desktop",
        ["explorer.exe"] = "Dosya Gezgini",
        ["notepad++.exe"] = "Notepad++",
        ["WINWORD.EXE"] = "Word",
        ["EXCEL.EXE"] = "Excel",
        ["WorkLogger.exe"] = "WorkLogger",
    };
}

public sealed class ClassificationRule
{
    /// <summary>"uygulama.exe pencere başlığı" metninde aranacak düzenli ifade (büyük/küçük harf duyarsız).</summary>
    public string Pattern { get; set; } = "";
    public string IssueKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class WorklogSettings
{
    public int RoundingMinutes { get; set; } = 15;
    public int MinWorklogMinutes { get; set; } = 15;
    public double DailyTargetHours { get; set; } = 8;
    public string DayEndTime { get; set; } = "18:00";
    public bool OpenDayEndAutomatically { get; set; } = true;
    public List<int> Workdays { get; set; } = [1, 2, 3, 4, 5]; // DayOfWeek: 1 = Pazartesi
}

public sealed class GitSettings
{
    public List<string> RepoRoots { get; set; } = [];
    public int ScanDepth { get; set; } = 3;
    public string AuthorEmail { get; set; } = "";
    public int ScanIntervalMinutes { get; set; } = 30;
}

/// <summary>Kurum içi Jira/Tempo entegrasyonu. Varsayılan olarak kapalıdır.</summary>
public sealed class JiraSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "";
}

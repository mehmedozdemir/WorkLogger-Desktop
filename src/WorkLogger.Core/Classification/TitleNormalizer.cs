using System.Text.RegularExpressions;

namespace WorkLogger.Core.Classification;

/// <summary>
/// Pencere başlıklarını insan okuyabilir "bağlam" metnine indirger:
/// uygulama son eklerini, değişiklik işaretlerini ve issue key'leri temizler.
/// </summary>
public static partial class TitleNormalizer
{
    [GeneratedRegex(@"\s+[-—|]\s+(Google Chrome|Mozilla Firefox|Microsoft​?\s?Edge|Opera|Brave)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BrowserSuffix();

    [GeneratedRegex(@"\s+-\s+(Microsoft Visual Studio|Visual Studio Code|JetBrains Rider|DBeaver|Notepad\+\+).*$", RegexOptions.IgnoreCase)]
    private static partial Regex IdeSuffix();

    [GeneratedRegex(@"\s+(and\s+\d+\s+more\s+pages?|ve\s+\d+\s+sayfa\s+daha)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MorePages();

    [GeneratedRegex(@"^[●\*\s]+")]
    private static partial Regex DirtyMarker();

    [GeneratedRegex(@"\s+[-–—|]\s+")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\[?\b[A-Z][A-Z0-9]{1,9}-\d{1,6}\b\]?:?")]
    private static partial Regex IssueKey();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    /// <summary>Başlığın uygulama son ekleri atılmış hali (ör. "PAY-1042 kart doğrulama - Odeme.sln").</summary>
    public static string StripAppSuffix(string title)
    {
        var cleaned = BrowserSuffix().Replace(title, "");
        cleaned = IdeSuffix().Replace(cleaned, "");
        cleaned = MorePages().Replace(cleaned, " ");
        cleaned = DirtyMarker().Replace(cleaned, "");
        return cleaned.Trim();
    }

    /// <summary>Gruplama anahtarı: genelde solution/proje/site adı olan son parça.</summary>
    public static string GroupKey(string title)
    {
        var parts = Parts(StripAppSuffix(title));
        var key = parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => parts[^1],
        };
        return Truncate(key, 60);
    }

    /// <summary>Özetlerde kullanılacak konu metni: issue key ve tekrar eden son parça olmadan.</summary>
    public static string Topic(string title)
    {
        var parts = Parts(StripAppSuffix(title));
        if (parts.Count == 0)
            return "";
        var first = IssueKey().Replace(parts[0], "").Trim(' ', ':', '-', '[', ']');
        first = MultiSpace().Replace(first, " ");
        if (first.Length == 0 && parts.Count > 1)
            first = parts[1];
        return Truncate(first, 80);
    }

    private static List<string> Parts(string text) =>
        Separator().Split(text).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1).TrimEnd(), "…");
}

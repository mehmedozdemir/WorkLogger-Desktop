using System.Text.RegularExpressions;
using WorkLogger.Core.Settings;

namespace WorkLogger.Core.Analysis;

public sealed record WorklogSuggestion(
    string IssueKey,
    TimeSpan Tracked,
    int SuggestedMinutes,
    TimeOnly StartTime,
    string Description,
    bool CommitOnly);

/// <summary>Analizden Tempo/Jira'ya girilebilir worklog önerileri üretir.</summary>
public static partial class WorklogPlanner
{
    /// <summary>Bu süreden az izlenen issue için öneri 0 dakikadır (kullanıcı isterse elle girer).</summary>
    public static readonly TimeSpan MinTracked = TimeSpan.FromMinutes(5);

    [GeneratedRegex(@"^\s*\[?[A-Z][A-Z0-9]{1,9}-\d{1,6}\]?\s*[:\-–]?\s*")]
    private static partial Regex LeadingKey();

    public static List<WorklogSuggestion> Suggest(DayAnalysis analysis, WorklogSettings settings)
    {
        var list = new List<WorklogSuggestion>();
        foreach (var issue in analysis.Issues)
        {
            var minutes = issue.Duration < MinTracked
                ? 0
                : RoundMinutes(issue.Duration, settings.RoundingMinutes, settings.MinWorklogMinutes);
            var start = issue.FirstStart is { } f ? TimeOnly.FromDateTime(f.LocalDateTime) : new TimeOnly(9, 0);
            list.Add(new WorklogSuggestion(issue.Key, issue.Duration, minutes, start, Describe(issue), issue.CommitOnly));
        }
        return list;
    }

    public static int RoundMinutes(TimeSpan tracked, int step, int minimum)
    {
        var minutes = tracked.TotalMinutes;
        var rounded = step <= 0
            ? (int)Math.Round(minutes, MidpointRounding.AwayFromZero)
            : (int)(Math.Round(minutes / step, MidpointRounding.AwayFromZero) * step);
        return Math.Max(minimum, rounded);
    }

    /// <summary>
    /// Önerileri, oranlarını koruyarak hedef toplam dakikaya ölçekler (en büyük kalan yöntemi).
    /// Sonuçlar yuvarlama adımının katıdır; 0 dakikalık satırlar 0 kalır.
    /// </summary>
    public static List<WorklogSuggestion> BalanceTo(IReadOnlyList<WorklogSuggestion> items, int targetMinutes, int step)
    {
        step = Math.Max(1, step);
        var active = items.Where(i => i.SuggestedMinutes > 0).ToList();
        var total = active.Sum(i => i.SuggestedMinutes);
        if (total == 0 || targetMinutes <= 0)
            return [.. items];

        var targetUnits = targetMinutes / step;
        var shares = active
            .Select(i => (Item: i, Exact: (double)i.SuggestedMinutes / total * targetUnits))
            .Select(x => (x.Item, x.Exact, Units: Math.Max(1, (int)Math.Floor(x.Exact))))
            .ToList();
        var remaining = targetUnits - shares.Sum(s => s.Units);
        foreach (var share in shares.OrderByDescending(s => s.Exact - Math.Floor(s.Exact)).ToList())
        {
            if (remaining <= 0)
                break;
            var index = shares.FindIndex(s => ReferenceEquals(s.Item, share.Item));
            shares[index] = (share.Item, share.Exact, shares[index].Units + 1);
            remaining--;
        }

        var map = shares.ToDictionary(s => s.Item.IssueKey, s => s.Units * step, StringComparer.Ordinal);
        return items.Select(i => map.TryGetValue(i.IssueKey, out var m) ? i with { SuggestedMinutes = m } : i).ToList();
    }

    /// <summary>Deterministik açıklama: commit mesajları varsa onlar, yoksa en çok çalışılan konu başlıkları.</summary>
    public static string Describe(IssueActivity issue)
    {
        var parts = new List<string>();
        foreach (var commit in issue.Commits.Take(3))
        {
            var message = LeadingKey().Replace(commit.Message, "").Trim();
            if (message.Length > 0 && !parts.Contains(message, StringComparer.OrdinalIgnoreCase))
                parts.Add(message);
        }
        if (parts.Count == 0)
        {
            foreach (var topic in issue.Topics.Take(2))
            {
                if (!parts.Contains(topic.Text, StringComparer.OrdinalIgnoreCase))
                    parts.Add(topic.Text);
            }
        }
        var text = parts.Count > 0 ? string.Join("; ", parts) : "Geliştirme çalışması";
        return text.Length <= 250 ? text : text[..249] + "…";
    }
}

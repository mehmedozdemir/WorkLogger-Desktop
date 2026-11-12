using System.Text;
using WorkLogger.Core.Analysis;
using WorkLogger.Core.Formatting;
using WorkLogger.Core.Settings;

namespace WorkLogger.Core.Summaries;

public enum HighlightKind
{
    Focus,
    Fragmented,
    Overtime,
    Unclassified,
    LongIdle,
    TargetMet,
    TargetMissed,
    CommitOnly,
}

public enum HighlightTone
{
    Positive,
    Neutral,
    Warning,
}

public sealed record Highlight(HighlightKind Kind, HighlightTone Tone, string Text);

public sealed record IssueSummary(string IssueKey, TimeSpan Duration, double Share, IReadOnlyList<string> Lines);

public sealed record DaySummary(
    string Headline,
    IReadOnlyList<string> Narrative,
    IReadOnlyList<IssueSummary> Items,
    IReadOnlyList<Highlight> Highlights,
    string Standup)
{
    public static readonly DaySummary Empty = new("Bu gün için kayıt yok.", [], [], [], "");

    /// <summary>Panoya kopyalanacak düz metin.</summary>
    public string ToPlainText(DateOnly day)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Gün sonu özeti — {DurationText.DayTitle(day)}");
        sb.AppendLine(Headline);
        sb.AppendLine();
        foreach (var line in Narrative)
            sb.AppendLine(line);
        if (Items.Count > 0)
        {
            sb.AppendLine();
            foreach (var item in Items)
            {
                sb.AppendLine($"• {item.IssueKey} — {DurationText.Format(item.Duration)}");
                foreach (var line in item.Lines)
                    sb.AppendLine($"    {line}");
            }
        }
        if (Highlights.Count > 0)
        {
            sb.AppendLine();
            foreach (var h in Highlights)
                sb.AppendLine($"– {h.Text}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Günü kural tabanlı olarak yorumlar. Yapay zekâ kullanmaz: tüm cümleler analizdeki ölçümlerden
/// şablonlarla kurulur, bu yüzden her ifade doğrudan bir sayıya ya da kayda dayanır.
/// </summary>
public static class DaySummarizer
{
    /// <summary>Bundan kısa issue'lar anlatıda ve listede yer almaz (ör. 20 sn'lik bir sekme geçişi).</summary>
    public static readonly TimeSpan MinMention = TimeSpan.FromMinutes(1);

    public static DaySummary Summarize(DayAnalysis a, WorklogSettings worklog)
    {
        if (a.Active <= TimeSpan.Zero && a.Issues.Count == 0)
            return DaySummary.Empty;

        var headline = Headline(a);
        var narrative = Narrative(a);
        var items = a.Issues
            .Where(i => i.Duration >= MinMention || i.Commits.Count > 0)
            .Select(i => Item(i, a.Active))
            .ToList();
        var highlights = Highlights(a, worklog);
        return new DaySummary(headline, narrative, items, highlights, Standup(a));
    }

    private static string Headline(DayAnalysis a)
    {
        var parts = new List<string> { $"{DurationText.Format(a.Active)} aktif çalışma" };
        var issueCount = a.Issues.Count(i => i.Duration >= MinMention);
        if (issueCount > 0)
            parts.Add($"{issueCount} issue");
        if (a.Active > TimeSpan.Zero)
            parts.Add($"eşlenme {DurationText.Percent(a.ClassifiedRatio)}");
        if (a.Commits.Count > 0)
            parts.Add($"{a.Commits.Count} commit");
        return string.Join(" · ", parts);
    }

    private static List<string> Narrative(DayAnalysis a)
    {
        var lines = new List<string>();

        if (a.FirstActivity is { } first && a.LastActivity is { } last)
        {
            var span = $"Çalışma {DurationText.Clock(first)} – {DurationText.Clock(last)} arasında sürdü";
            span += a.Idle >= TimeSpan.FromMinutes(5)
                ? $"; arada toplam {DurationText.Format(a.Idle)} boşta geçti."
                : ".";
            lines.Add(span);
        }

        var worked = a.Issues.Where(i => i.Duration >= MinMention).ToList();
        if (worked.Count > 0)
        {
            var top = worked[0];
            var share = a.Active > TimeSpan.Zero ? top.Duration / a.Active : 0;
            var lead = share >= 0.5
                ? $"Günün büyük bölümü {top.Key} üzerinde geçti ({DurationText.Format(top.Duration)}, pay {DurationText.Percent(share)})"
                : $"En çok zaman {top.Key} işine ayrıldı ({DurationText.Format(top.Duration)})";
            var topic = top.Topics.FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(topic))
                lead += $"; odak konu: “{topic}”";
            lines.Add(lead + ".");

            var others = worked.Skip(1).Take(3).Select(i => $"{i.Key} ({DurationText.Format(i.Duration)})").ToList();
            if (others.Count > 0)
            {
                var rest = worked.Count - 1 - others.Count;
                var tail = rest > 0 ? $" ve {rest} issue daha" : "";
                lines.Add($"Diğer işler: {JoinTr(others)}{tail}.");
            }
        }
        else if (a.Active > TimeSpan.Zero)
        {
            lines.Add("Pencere başlıklarında issue key bulunamadı; çalışmalar sınıflandırılmamış görünüyor.");
        }

        if (a.UnclassifiedTotal >= TimeSpan.FromMinutes(10))
        {
            var topApps = a.Unclassified
                .Where(u => u.AppLabel.Length > 0)
                .GroupBy(u => u.AppLabel)
                .Select(g => (App: g.Key, Time: g.Aggregate(TimeSpan.Zero, (s, u) => s + u.Duration)))
                .OrderByDescending(x => x.Time)
                .Take(3)
                .Select(x => $"{x.App} ({DurationText.Format(x.Time)})")
                .ToList();
            var detail = topApps.Count > 0 ? $"; çoğunlukla {JoinTr(topApps)}" : "";
            lines.Add($"Issue'ya bağlanamayan süre {DurationText.Format(a.UnclassifiedTotal)}{detail}.");
        }

        if (a.FocusBlocks.Count > 0)
        {
            var longest = a.FocusBlocks.MaxBy(f => f.Duration)!;
            lines.Add(
                $"{a.FocusBlocks.Count} odak bloğu oluştu (toplam {DurationText.Format(a.FocusTotal)}); en uzunu " +
                $"{DurationText.Clock(longest.Start)}–{DurationText.Clock(longest.End)} arası {longest.Label} ({DurationText.Format(longest.Duration)}).");
        }

        if (a.Commits.Count > 0)
        {
            var repos = a.Commits.Select(c => c.Repo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var changes = a.Commits.Sum(c => c.Insertions + c.Deletions);
            var repoText = repos.Count == 1 ? repos[0] : $"{repos.Count} depo";
            var changeText = changes > 0 ? $", {changes:N0} satır değişiklik" : "";
            lines.Add($"{repoText} üzerinde {a.Commits.Count} commit yapıldı{changeText}.");
        }

        return lines;
    }

    private static IssueSummary Item(IssueActivity issue, TimeSpan active)
    {
        var lines = new List<string>();
        var topics = issue.Topics.Take(3).Select(t => t.Text).ToList();
        if (topics.Count > 0)
            lines.Add("Konu: " + string.Join("; ", topics));
        if (issue.Apps.Count > 0)
            lines.Add("Araçlar: " + string.Join(", ", issue.Apps.Select(x => $"{x.App} {DurationText.Format(x.Duration)}")));
        foreach (var commit in issue.Commits.Take(5))
            lines.Add($"Commit ({commit.Repo}): {commit.Message}");
        if (issue.CommitOnly)
            lines.Add("Pencere süresi yok; yalnızca commit ile tespit edildi.");
        var share = active > TimeSpan.Zero ? issue.Duration / active : 0;
        return new IssueSummary(issue.Key, issue.Duration, share, lines);
    }

    private static List<Highlight> Highlights(DayAnalysis a, WorklogSettings worklog)
    {
        var list = new List<Highlight>();

        if (worklog.DailyTargetHours > 0 && a.Active > TimeSpan.Zero)
        {
            var target = TimeSpan.FromHours(worklog.DailyTargetHours);
            if (a.Active >= target)
                list.Add(new(HighlightKind.TargetMet, HighlightTone.Positive,
                    $"Günlük hedef ({DurationText.Format(target)}) tamamlandı."));
            else if (a.LastActivity is { } last && last.ToLocalTime().TimeOfDay >= TimeSpan.FromHours(16))
                list.Add(new(HighlightKind.TargetMissed, HighlightTone.Neutral,
                    $"Günlük hedefe {DurationText.Format(target - a.Active)} eksik."));
        }

        if (a.FocusTotal >= TimeSpan.FromHours(2))
            list.Add(new(HighlightKind.Focus, HighlightTone.Positive,
                $"Güçlü odak: {DurationText.Format(a.FocusTotal)} kesintisiz çalışma."));

        if (a.SwitchesPerHour >= 6)
            list.Add(new(HighlightKind.Fragmented, HighlightTone.Warning,
                $"Parçalı gün: saatte ortalama {a.SwitchesPerHour:0.#} iş değişimi."));

        if (a.Active > TimeSpan.Zero && a.UnclassifiedTotal / a.Active >= 0.25 && a.UnclassifiedTotal >= TimeSpan.FromMinutes(30))
            list.Add(new(HighlightKind.Unclassified, HighlightTone.Warning,
                $"Issue'ya bağlanamayan pay {DurationText.Percent(a.UnclassifiedTotal / a.Active)}; kural eklemeyi düşünün."));

        if (a.OutsideWorkHours >= TimeSpan.FromMinutes(30))
            list.Add(new(HighlightKind.Overtime, HighlightTone.Neutral,
                $"Mesai saatleri dışında {DurationText.Format(a.OutsideWorkHours)} çalışıldı."));

        var commitOnly = a.Issues.Where(i => i.CommitOnly).Select(i => i.Key).ToList();
        if (commitOnly.Count > 0)
            list.Add(new(HighlightKind.CommitOnly, HighlightTone.Neutral,
                $"Yalnızca commit ile görülen issue'lar: {string.Join(", ", commitOnly)}."));

        return list;
    }

    private static string Standup(DayAnalysis a)
    {
        var worked = a.Issues.Where(i => i.Duration >= TimeSpan.FromMinutes(10) || i.Commits.Count > 0).Take(6).ToList();
        if (worked.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine("Dün:");
        foreach (var issue in worked)
        {
            var what = issue.Commits.Count > 0
                ? WorklogPlanner.Describe(issue)
                : issue.Topics.FirstOrDefault()?.Text ?? "çalışma";
            sb.AppendLine($"• {issue.Key}: {what}");
        }
        sb.AppendLine("Bugün:");
        sb.AppendLine("• ");
        sb.AppendLine("Engeller:");
        sb.Append("• Yok");
        return sb.ToString();
    }

    private static string JoinTr(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " ve " + parts[^1],
    };
}

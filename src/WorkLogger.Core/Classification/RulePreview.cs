using System.Text.RegularExpressions;
using WorkLogger.Core.Model;

namespace WorkLogger.Core.Classification;

public sealed record RulePreviewResult(TimeSpan Captured, int Days, int Segments);

/// <summary>Yeni bir kuralın geçmiş veride ne kadar süreyi yakalayacağını hesaplar.</summary>
public static class RulePreview
{
    /// <summary>
    /// Yalnızca şu an sınıflandırılmamış, boşta olmayan segmentler sayılır; yani sonuç kuralın
    /// gerçekten "kazandıracağı" süredir.
    /// </summary>
    public static RulePreviewResult Measure(string pattern, IEnumerable<ActivitySegment> segments, IssueClassifier classifier)
    {
        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return new RulePreviewResult(TimeSpan.Zero, 0, 0);
        }

        var total = TimeSpan.Zero;
        var days = new HashSet<DateOnly>();
        var count = 0;
        foreach (var seg in segments)
        {
            if (seg.Idle || classifier.Classify(seg.App, seg.Title).IsClassified)
                continue;
            try
            {
                if (!regex.IsMatch($"{seg.App} {seg.Title}"))
                    continue;
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            total += seg.Duration;
            days.Add(seg.Day);
            count++;
        }
        return new RulePreviewResult(total, days.Count, count);
    }
}

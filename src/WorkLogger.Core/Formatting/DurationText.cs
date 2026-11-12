using System.Globalization;

namespace WorkLogger.Core.Formatting;

public static class DurationText
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"2 sa 05 dk", "45 dk", "0 dk".</summary>
    public static string Format(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60)
            return $"{minutes} dk";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} sa" : $"{hours} sa {rest:00} dk";
    }

    /// <summary>"6,7 sa".</summary>
    public static string Hours(TimeSpan span, int decimals = 1) =>
        span.TotalHours.ToString("N" + decimals, Turkish) + " sa";

    /// <summary>"%87".</summary>
    public static string Percent(double ratio) =>
        "%" + Math.Round(ratio * 100).ToString("0", Turkish);

    public static string Clock(DateTimeOffset? t) => t?.ToLocalTime().ToString("HH:mm", Turkish) ?? "–";

    public static string DayTitle(DateOnly day) => day.ToString("d MMMM yyyy, dddd", Turkish);

    public static string ShortDay(DateOnly day) => day.ToString("d MMM ddd", Turkish);
}

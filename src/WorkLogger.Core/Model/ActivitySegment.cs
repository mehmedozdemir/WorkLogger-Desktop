namespace WorkLogger.Core.Model;

/// <summary>
/// Ön plandaki pencerenin (veya boşta durumunun) değişmeden kaldığı kesintisiz zaman dilimi.
/// </summary>
public sealed record ActivitySegment(
    long Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string App,
    string Title,
    bool Idle)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    public DateOnly Day => DateOnly.FromDateTime(Start.LocalDateTime);

    /// <summary>Segmentin verilen aralıkla kesişen kısmı; kesişim yoksa null.</summary>
    public ActivitySegment? ClipTo(DateTimeOffset from, DateTimeOffset to)
    {
        var start = Start > from ? Start : from;
        var end = End < to ? End : to;
        return end > start ? this with { Start = start, End = end } : null;
    }
}

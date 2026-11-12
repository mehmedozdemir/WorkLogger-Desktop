namespace WorkLogger.Core.Tracking;

/// <summary>İzleyicinin tek bir andaki gözlemi.</summary>
/// <param name="Time">Gözlem zamanı.</param>
/// <param name="App">Ön plandaki sürecin adı (ör. devenv.exe). Pencere yoksa null.</param>
/// <param name="Title">Pencere başlığı.</param>
/// <param name="IdleFor">Son klavye/fare girdisinden bu yana geçen süre.</param>
/// <param name="Locked">Oturum kilitli mi.</param>
public readonly record struct Observation(DateTimeOffset Time, string? App, string Title, TimeSpan IdleFor, bool Locked = false);

/// <summary>Açık (henüz kapanmamış) segment.</summary>
public sealed record OpenSegment(DateTimeOffset Start, DateTimeOffset LastSeen, string App, string Title, bool Idle);

/// <summary>Bir gözlemin sonucu: en fazla bir segment kapanır, en fazla bir segment açılır.</summary>
public readonly record struct TrackingStep(OpenSegment? Closed, OpenSegment? Opened)
{
    public static readonly TrackingStep None = default;
}

/// <summary>
/// Gözlemleri segmentlere çeviren saf durum makinesi. İşletim sistemi bağımlılığı yoktur, bu yüzden
/// uyku, kilit, gün değişimi ve boşta geri tarihleme gibi kenar durumları birim testle doğrulanabilir.
/// </summary>
public sealed class TrackingStateMachine
{
    public const string IdleApp = "(boşta)";
    public const string LockedApp = "(kilitli)";

    private readonly TimeSpan _idleThreshold;
    private readonly TimeSpan _maxGap;
    private readonly IReadOnlySet<string> _ignoredApps;

    public TrackingStateMachine(TimeSpan idleThreshold, TimeSpan maxGap, IEnumerable<string>? ignoredApps = null)
    {
        _idleThreshold = idleThreshold;
        _maxGap = maxGap;
        _ignoredApps = new HashSet<string>(ignoredApps ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public OpenSegment? Current { get; private set; }

    public TrackingStep Observe(Observation o)
    {
        var (app, title, idle) = Interpret(o);

        if (Current is null)
            return Open(o.Time, app, title, idle);

        // 1) Uyku/hazırda bekleme ya da donma: son görülme ile şimdi arasında beklenmedik boşluk varsa
        //    segment son görülme anında kapanır. Aradaki süre hiçbir segmente yazılmaz.
        if (o.Time - Current.LastSeen > _maxGap)
        {
            var closed = Current;
            Current = null;
            var opened = Open(o.Time, app, title, idle).Opened;
            return new TrackingStep(closed, opened);
        }

        // 2) Gün değişimi: segment gece yarısında bölünür ki her gün kendi süresini taşısın.
        var midnight = NextMidnight(Current.Start);
        if (o.Time >= midnight)
        {
            var closed = Current with { LastSeen = midnight };
            Current = Current with { Start = midnight, LastSeen = o.Time };
            // Aynı gözlemde pencere de değiştiyse değişim bir sonraki gözlemde işlenir;
            // gece yarısında en fazla bir örnekleme aralığı önceki pencereye yazılır.
            return new TrackingStep(closed, Current);
        }

        var changed = Current.App != app || Current.Title != title || Current.Idle != idle;
        if (!changed)
        {
            Current = Current with { LastSeen = o.Time };
            return TrackingStep.None;
        }

        // 3) Boşta geçiş: kullanıcı aslında IdleFor kadar önce durmuştu. Aktif segmenti o ana geri
        //    tarihleyerek kapatırız; böylece boşta eşiği kadar süre yanlışlıkla aktif sayılmaz.
        var switchAt = o.Time;
        if (idle && !Current.Idle)
        {
            var idleSince = o.Time - o.IdleFor;
            if (idleSince > Current.Start && idleSince < o.Time)
                switchAt = idleSince;
            else if (idleSince <= Current.Start)
                switchAt = Current.Start;
        }

        var closedSegment = Current with { LastSeen = switchAt };
        Current = null;
        var openedSegment = Open(switchAt, app, title, idle).Opened! with { LastSeen = o.Time };
        Current = openedSegment;
        return new TrackingStep(closedSegment, openedSegment);
    }

    /// <summary>İzleme durduğunda (duraklatma, uyku, kapanış) açık segmenti verilen anda kapatır.</summary>
    public OpenSegment? Stop(DateTimeOffset at)
    {
        if (Current is null)
            return null;
        var end = at < Current.LastSeen ? Current.LastSeen : at;
        if (end - Current.LastSeen > _maxGap)
            end = Current.LastSeen;
        var closed = Current with { LastSeen = end };
        Current = null;
        return closed;
    }

    private TrackingStep Open(DateTimeOffset at, string app, string title, bool idle)
    {
        Current = new OpenSegment(at, at, app, title, idle);
        return new TrackingStep(null, Current);
    }

    private (string App, string Title, bool Idle) Interpret(Observation o)
    {
        if (o.Locked)
            return (LockedApp, "", true);
        if (string.IsNullOrEmpty(o.App) || _ignoredApps.Contains(o.App))
            return (IdleApp, "", true);
        if (o.IdleFor >= _idleThreshold)
            return (IdleApp, "", true);
        return (o.App, o.Title, false);
    }

    private static DateTimeOffset NextMidnight(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        var next = local.Date.AddDays(1);
        return new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next));
    }
}

using Avalonia;
using Avalonia.Media;
using WorkLogger.Core.Analysis;
using WorkLogger.Core.Formatting;
using WorkLogger.Desktop.Services;

namespace WorkLogger.Desktop.Controls;

/// <summary>Günün zaman çizelgesi: issue'lar renkli, sınıflandırılmamış iş gri, boşta süre açık ton.</summary>
public sealed class TimelineBar : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<TimelineBlock>?> BlocksProperty =
        AvaloniaProperty.Register<TimelineBar, IReadOnlyList<TimelineBlock>?>(nameof(Blocks));

    public static readonly StyledProperty<DateTimeOffset> RangeStartProperty =
        AvaloniaProperty.Register<TimelineBar, DateTimeOffset>(nameof(RangeStart));

    public static readonly StyledProperty<DateTimeOffset> RangeEndProperty =
        AvaloniaProperty.Register<TimelineBar, DateTimeOffset>(nameof(RangeEnd));

    public static readonly StyledProperty<IBrush?> IdleBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(IdleBrush), Brushes.Gainsboro);

    public static readonly StyledProperty<IBrush?> UnclassifiedBrushProperty =
        AvaloniaProperty.Register<TimelineBar, IBrush?>(nameof(UnclassifiedBrush), Brushes.SlateGray);

    private const double BarHeight = 34;
    private const double AxisHeight = 20;
    private readonly List<Rect> _rects = [];

    static TimelineBar()
    {
        AffectsRender<TimelineBar>(BlocksProperty, RangeStartProperty, RangeEndProperty, IdleBrushProperty, UnclassifiedBrushProperty);
    }

    public IReadOnlyList<TimelineBlock>? Blocks { get => GetValue(BlocksProperty); set => SetValue(BlocksProperty, value); }
    public DateTimeOffset RangeStart { get => GetValue(RangeStartProperty); set => SetValue(RangeStartProperty, value); }
    public DateTimeOffset RangeEnd { get => GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }
    public IBrush? IdleBrush { get => GetValue(IdleBrushProperty); set => SetValue(IdleBrushProperty, value); }
    public IBrush? UnclassifiedBrush { get => GetValue(UnclassifiedBrushProperty); set => SetValue(UnclassifiedBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, BarHeight + AxisHeight + 4);

    public override void Render(DrawingContext ctx)
    {
        var width = Bounds.Width;
        var track = new Rect(0, 0, width, BarHeight);
        ctx.DrawRectangle(TrackBrush, null, track, 8, 8);
        _rects.Clear();

        var span = (RangeEnd - RangeStart).TotalSeconds;
        if (span <= 0 || width <= 0)
            return;

        double X(DateTimeOffset t) => Math.Clamp((t - RangeStart).TotalSeconds / span, 0, 1) * width;

        using (ctx.PushClip(new RoundedRect(track, 8)))
        {
            var blocks = Blocks ?? [];
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                var x0 = X(b.Start);
                var x1 = Math.Max(X(b.End), x0 + 1);
                var rect = new Rect(x0, 0, x1 - x0, BarHeight);
                _rects.Add(rect);
                IBrush? brush = b.Kind switch
                {
                    TimelineKind.Issue => IssuePalette.BrushFor(b.Label),
                    TimelineKind.Unclassified => UnclassifiedBrush,
                    _ => IdleBrush,
                };
                if (i == HoverIndex && b.Kind != TimelineKind.Idle)
                    rect = rect.Inflate(new Thickness(0, -2));
                ctx.DrawRectangle(brush, null, rect);
                if (i == HoverIndex)
                    ctx.DrawRectangle(null, new Pen(TextBrush, 1.5), rect.Deflate(0.75));
            }
        }

        // Saat ekseni
        var first = RangeStart.ToLocalTime();
        var hour = new DateTimeOffset(first.Year, first.Month, first.Day, first.Hour, 0, 0, first.Offset);
        if (hour < first)
            hour = hour.AddHours(1);
        var gridPen = new Pen(GridBrush, 1);
        var step = span > 14 * 3600 ? 2 : 1;
        for (var t = hour; t <= RangeEnd; t = t.AddHours(step))
        {
            var x = X(t);
            ctx.DrawLine(gridPen, new Point(x, BarHeight + 2), new Point(x, BarHeight + 6));
            var label = Text(t.ToString("HH:mm", DurationText.Turkish), 11, MutedBrush);
            var anchor = x < 20 ? TextAnchor.Left : x > width - 20 ? TextAnchor.Right : TextAnchor.Center;
            DrawText(ctx, label, new Point(x, BarHeight + 6), anchor);
        }
    }

    protected override int HitTestItem(Point point)
    {
        if (point.Y > BarHeight)
            return -1;
        for (var i = 0; i < _rects.Count; i++)
        {
            if (point.X >= _rects[i].Left && point.X <= _rects[i].Right)
                return i;
        }
        return -1;
    }

    protected override string? TooltipFor(int index)
    {
        if (Blocks is null || index >= Blocks.Count)
            return null;
        var b = Blocks[index];
        var range = $"{DurationText.Clock(b.Start)}–{DurationText.Clock(b.End)} · {DurationText.Format(b.Duration)}";
        return b.Kind switch
        {
            TimelineKind.Idle => $"Boşta\n{range}",
            TimelineKind.Issue => $"{b.Label} · {b.AppLabel}\n{range}\n{Shorten(b.Title)}",
            _ => $"{b.AppLabel} (sınıflandırılmamış)\n{range}\n{Shorten(b.Title)}",
        };
    }

    private static string Shorten(string text) => text.Length <= 90 ? text : text[..89] + "…";
}

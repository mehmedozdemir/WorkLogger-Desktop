using Avalonia;
using Avalonia.Media;
using WorkLogger.Core.Formatting;

namespace WorkLogger.Desktop.Controls;

public sealed record HeatCell(DateOnly Day, double Value, string Tooltip);

/// <summary>GitHub tarzı takvim ısı haritası: sütunlar haftalar, satırlar Pazartesi–Pazar.</summary>
public sealed class CalendarHeatmap : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<HeatCell>?> CellsProperty =
        AvaloniaProperty.Register<CalendarHeatmap, IReadOnlyList<HeatCell>?>(nameof(Cells));

    private const double LeftLabels = 30;
    private const double TopLabels = 18;
    private const double Gap = 3;
    private readonly List<Rect> _rects = [];
    private double _cell = 14;

    static CalendarHeatmap()
    {
        AffectsRender<CalendarHeatmap>(CellsProperty);
        AffectsMeasure<CalendarHeatmap>(CellsProperty);
    }

    public IReadOnlyList<HeatCell>? Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }

    private static int WeekdayIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    private int WeekCount()
    {
        var cells = Cells;
        if (cells is null || cells.Count == 0)
            return 0;
        var first = cells[0].Day.AddDays(-WeekdayIndex(cells[0].Day));
        return (cells[^1].Day.DayNumber - first.DayNumber) / 7 + 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var weeks = Math.Max(1, WeekCount());
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        _cell = Math.Clamp((width - LeftLabels) / weeks - Gap, 6, 26);
        return new Size(width, TopLabels + 7 * (_cell + Gap));
    }

    public override void Render(DrawingContext ctx)
    {
        _rects.Clear();
        var cells = Cells ?? [];
        if (cells.Count == 0)
            return;

        var weeks = Math.Max(1, WeekCount());
        _cell = Math.Clamp((Bounds.Width - LeftLabels) / weeks - Gap, 6, 26);
        var start = cells[0].Day.AddDays(-WeekdayIndex(cells[0].Day));
        var max = Math.Max(cells.Max(c => c.Value), 0.0001);
        var accent = ColorOf(AccentBrush, Colors.RoyalBlue);

        string[] dayNames = ["Pzt", "", "Çar", "", "Cum", "", ""];
        for (var r = 0; r < 7; r++)
        {
            if (dayNames[r].Length == 0)
                continue;
            var t = Text(dayNames[r], 10, MutedBrush);
            DrawText(ctx, t, new Point(0, TopLabels + r * (_cell + Gap) + (_cell - t.Height) / 2));
        }

        var lastMonth = -1;
        foreach (var cell in cells)
        {
            var week = (cell.Day.DayNumber - start.DayNumber) / 7;
            var row = WeekdayIndex(cell.Day);
            var rect = new Rect(LeftLabels + week * (_cell + Gap), TopLabels + row * (_cell + Gap), _cell, _cell);
            _rects.Add(rect);

            IBrush? fill = cell.Value <= 0
                ? TrackBrush
                : WithOpacity(accent, 0.22 + 0.78 * Math.Sqrt(cell.Value / max));
            ctx.DrawRectangle(fill, null, rect, 3, 3);
            if (_rects.Count - 1 == HoverIndex)
                ctx.DrawRectangle(null, new Pen(TextBrush, 1.4), rect, 3, 3);

            // Ay etiketi, ayın görünen ilk haftasının sütununa yazılır.
            if ((row == 0 || lastMonth == -1) && cell.Day.Month != lastMonth)
            {
                var label = Text(cell.Day.ToString("MMM", DurationText.Turkish), 10.5, MutedBrush);
                DrawText(ctx, label, new Point(rect.Left, 0));
                lastMonth = cell.Day.Month;
            }
        }
    }

    protected override int HitTestItem(Point point)
    {
        for (var i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].Inflate(Gap / 2).Contains(point))
                return i;
        }
        return -1;
    }

    protected override string? TooltipFor(int index) => Cells is { } c && index < c.Count ? c[index].Tooltip : null;

    protected override object? TagFor(int index) => Cells is { } c && index < c.Count ? c[index].Day : null;
}

/// <summary>Haftanın günü × saat yoğunluk matrisi.</summary>
public sealed class MatrixHeatmap : ChartBase
{
    public static readonly StyledProperty<double[,]?> ValuesProperty =
        AvaloniaProperty.Register<MatrixHeatmap, double[,]?>(nameof(Values));

    private const double LeftLabels = 34;
    private const double BottomLabels = 18;
    private const double Gap = 2;
    private double _cw;
    private double _ch;

    static MatrixHeatmap()
    {
        AffectsRender<MatrixHeatmap>(ValuesProperty);
    }

    /// <summary>[satır (0 = Pazartesi), saat] → dakika.</summary>
    public double[,]? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    private static readonly string[] RowNames = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];

    public override void Render(DrawingContext ctx)
    {
        var values = Values;
        if (values is null)
            return;
        var rows = values.GetLength(0);
        var cols = values.GetLength(1);
        _cw = (Bounds.Width - LeftLabels) / cols - Gap;
        _ch = (Bounds.Height - BottomLabels) / rows - Gap;
        if (_cw <= 0 || _ch <= 0)
            return;

        double max = 0;
        foreach (var v in values)
            max = Math.Max(max, v);
        max = Math.Max(max, 0.0001);
        var accent = ColorOf(AccentBrush, Colors.RoyalBlue);

        for (var r = 0; r < rows; r++)
        {
            var label = Text(RowNames[r % 7], 10.5, MutedBrush);
            DrawText(ctx, label, new Point(0, r * (_ch + Gap) + (_ch - label.Height) / 2));
            for (var c = 0; c < cols; c++)
            {
                var rect = new Rect(LeftLabels + c * (_cw + Gap), r * (_ch + Gap), _cw, _ch);
                var value = values[r, c];
                IBrush? fill = value <= 0 ? TrackBrush : WithOpacity(accent, 0.15 + 0.85 * (value / max));
                ctx.DrawRectangle(fill, null, rect, 2.5, 2.5);
                if (r * cols + c == HoverIndex)
                    ctx.DrawRectangle(null, new Pen(TextBrush, 1.2), rect, 2.5, 2.5);
            }
        }
        for (var c = 0; c < cols; c += 3)
        {
            var label = Text($"{c:00}", 10, MutedBrush);
            DrawText(ctx, label, new Point(LeftLabels + c * (_cw + Gap), rows * (_ch + Gap) + 2));
        }
    }

    protected override int HitTestItem(Point point)
    {
        var values = Values;
        if (values is null || _cw <= 0 || _ch <= 0 || point.X < LeftLabels)
            return -1;
        var c = (int)((point.X - LeftLabels) / (_cw + Gap));
        var r = (int)(point.Y / (_ch + Gap));
        return r >= 0 && r < values.GetLength(0) && c >= 0 && c < values.GetLength(1) ? r * values.GetLength(1) + c : -1;
    }

    protected override string? TooltipFor(int index)
    {
        var values = Values;
        if (values is null)
            return null;
        var cols = values.GetLength(1);
        var r = index / cols;
        var c = index % cols;
        return $"{RowNames[r % 7]} {c:00}:00–{c + 1:00}:00\n{DurationText.Format(TimeSpan.FromMinutes(values[r, c]))}";
    }
}

/// <summary>Satır içi oran çubuğu (0-1).</summary>
public sealed class ShareBar : ChartBase
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ShareBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(BarBrush));

    static ShareBar()
    {
        AffectsRender<ShareBar>(ValueProperty, BarBrushProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, 6);

    public override void Render(DrawingContext ctx)
    {
        var h = Bounds.Height;
        ctx.DrawRectangle(TrackBrush, null, new Rect(0, 0, Bounds.Width, h), h / 2, h / 2);
        var w = Math.Clamp(Value, 0, 1) * Bounds.Width;
        if (w > 0.5)
            ctx.DrawRectangle(BarBrush ?? AccentBrush, null, new Rect(0, 0, Math.Max(w, h), h), h / 2, h / 2);
    }
}

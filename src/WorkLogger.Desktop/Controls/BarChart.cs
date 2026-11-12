using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace WorkLogger.Desktop.Controls;

/// <summary>Bir çubuk: alt kısım (Primary) vurgulu, üst kısım (Secondary) açık tonda yığılır.</summary>
public sealed record BarItem(string Label, double Primary, double Secondary, string Tooltip, object? Tag = null, Color? Color = null);

/// <summary>Dikey yığılmış çubuk grafik; isteğe bağlı hedef çizgisi ve tıklanabilir çubuklar.</summary>
public sealed class BarChart : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<BarItem>?> ItemsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<BarItem>?>(nameof(Items));

    public static readonly StyledProperty<double> TargetProperty =
        AvaloniaProperty.Register<BarChart, double>(nameof(Target));

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<BarChart, string>(nameof(Unit), "");

    public static readonly StyledProperty<int> LabelEveryProperty =
        AvaloniaProperty.Register<BarChart, int>(nameof(LabelEvery), 1);

    private const double AxisWidth = 34;
    private const double LabelHeight = 20;
    private readonly List<Rect> _bars = [];

    static BarChart()
    {
        AffectsRender<BarChart>(ItemsProperty, TargetProperty, UnitProperty, LabelEveryProperty);
    }

    public IReadOnlyList<BarItem>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public double Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public int LabelEvery { get => GetValue(LabelEveryProperty); set => SetValue(LabelEveryProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        _bars.Clear();
        var items = Items ?? [];
        var plot = new Rect(AxisWidth, 8, Math.Max(0, Bounds.Width - AxisWidth), Math.Max(0, Bounds.Height - LabelHeight - 8));
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var max = Math.Max(items.Count > 0 ? items.Max(i => i.Primary + i.Secondary) : 0, Target);
        max = NiceCeiling(max <= 0 ? 1 : max * 1.08);

        // Yatay kılavuz çizgileri
        var gridPen = new Pen(GridBrush, 1);
        const int lines = 4;
        for (var i = 0; i <= lines; i++)
        {
            var value = max / lines * i;
            var y = plot.Bottom - value / max * plot.Height;
            ctx.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(Format(value) + Unit, 10.5, MutedBrush);
            DrawText(ctx, label, new Point(AxisWidth - 6, y - label.Height / 2), TextAnchor.Right);
        }

        if (items.Count == 0)
            return;

        var slot = plot.Width / items.Count;
        var barWidth = Math.Clamp(slot * 0.62, 2, 28);
        var accent = ColorOf(AccentBrush, Colors.RoyalBlue);
        var every = Math.Max(1, LabelEvery);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var color = item.Color ?? accent;
            var x = plot.Left + slot * i + (slot - barWidth) / 2;
            var h1 = item.Primary / max * plot.Height;
            var h2 = item.Secondary / max * plot.Height;
            var hover = i == HoverIndex;
            var radius = Math.Min(4, barWidth / 2);

            if (h2 > 0.5)
                ctx.DrawRectangle(WithOpacity(color, hover ? 0.45 : 0.30), null, new Rect(x, plot.Bottom - h1 - h2, barWidth, h2), radius, radius);
            if (h1 > 0.5)
                ctx.DrawRectangle(WithOpacity(color, hover ? 1 : 0.88), null, new Rect(x, plot.Bottom - h1, barWidth, h1), radius, radius);

            _bars.Add(new Rect(plot.Left + slot * i, plot.Top, slot, plot.Height));

            if (i % every == 0 || hover)
            {
                var label = Text(item.Label, 10.5, hover ? TextBrush : MutedBrush);
                DrawText(ctx, label, new Point(x + barWidth / 2, plot.Bottom + 4), TextAnchor.Center);
            }
        }

        if (Target > 0)
        {
            var y = plot.Bottom - Target / max * plot.Height;
            var pen = new Pen(AccentBrush, 1.2, new DashStyle([4, 3], 0));
            ctx.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text("hedef", 10.5, AccentBrush);
            DrawText(ctx, label, new Point(plot.Right, y - label.Height - 1), TextAnchor.Right);
        }
    }

    protected override int HitTestItem(Point point)
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            if (_bars[i].Contains(point))
                return i;
        }
        return -1;
    }

    protected override string? TooltipFor(int index) => Items is { } items && index < items.Count ? items[index].Tooltip : null;

    protected override object? TagFor(int index) => Items is { } items && index < items.Count ? items[index].Tag : null;

    private static string Format(double value) =>
        value >= 10 || Math.Abs(value - Math.Round(value)) < 0.01
            ? Math.Round(value).ToString("0", CultureInfo.GetCultureInfo("tr-TR"))
            : value.ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"));

    private static double NiceCeiling(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 4, 5, 8, 10 })
        {
            if (step * magnitude >= value)
                return step * magnitude;
        }
        return 10 * magnitude;
    }
}

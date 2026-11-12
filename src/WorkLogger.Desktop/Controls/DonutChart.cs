using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WorkLogger.Desktop.Controls;

/// <summary>Halka grafik: ortada toplam değer, dilimlerde ipucu.</summary>
public sealed class DonutChart : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<ChartSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<ChartSlice>?>(nameof(Slices));

    public static readonly StyledProperty<string?> CenterTextProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterText));

    public static readonly StyledProperty<string?> CenterCaptionProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterCaption));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(Thickness), 18);

    private readonly List<(double Start, double Sweep)> _angles = [];

    static DonutChart()
    {
        AffectsRender<DonutChart>(SlicesProperty, CenterTextProperty, CenterCaptionProperty, ThicknessProperty);
    }

    public IReadOnlyList<ChartSlice>? Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    public string? CenterText { get => GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }
    public string? CenterCaption { get => GetValue(CenterCaptionProperty); set => SetValue(CenterCaptionProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double Radius => Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - Thickness / 2 - 4);

    public override void Render(DrawingContext ctx)
    {
        _angles.Clear();
        var r = Radius;
        if (r <= 0)
            return;

        ctx.DrawEllipse(null, new Pen(TrackBrush, Thickness), Center, r, r);

        var slices = Slices ?? [];
        var total = slices.Sum(s => Math.Max(0, s.Value));
        if (total > 0)
        {
            const double gap = 0.012; // dilimler arasında ince boşluk (radyan)
            var angle = -Math.PI / 2;
            for (var i = 0; i < slices.Count; i++)
            {
                var sweep = Math.Max(0, slices[i].Value) / total * Math.PI * 2;
                _angles.Add((angle, sweep));
                var drawSweep = slices.Count > 1 ? Math.Max(0.001, sweep - gap) : sweep - 0.0001;
                var thickness = i == HoverIndex ? Thickness + 5 : Thickness;
                DrawArc(ctx, new Pen(new ImmutableSolidColorBrush(slices[i].Color), thickness), angle, drawSweep, r);
                angle += sweep;
            }
        }

        if (!string.IsNullOrEmpty(CenterText))
        {
            var value = Text(CenterText, 20, TextBrush, bold: true);
            var caption = Text(CenterCaption ?? "", 11.5, MutedBrush);
            var top = Center.Y - (value.Height + caption.Height) / 2;
            DrawText(ctx, value, new Point(Center.X, top), TextAnchor.Center);
            DrawText(ctx, caption, new Point(Center.X, top + value.Height), TextAnchor.Center);
        }
    }

    private void DrawArc(DrawingContext ctx, Pen pen, double start, double sweep, double r)
    {
        var c = Center;
        var p0 = new Point(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start));
        var p1 = new Point(c.X + r * Math.Cos(start + sweep), c.Y + r * Math.Sin(start + sweep));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(p0, false);
            g.ArcTo(p1, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geometry);
    }

    protected override int HitTestItem(Point point)
    {
        var v = point - Center;
        var distance = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (Math.Abs(distance - Radius) > Thickness / 2 + 4)
            return -1;
        var angle = Math.Atan2(v.Y, v.X);
        for (var i = 0; i < _angles.Count; i++)
        {
            var (start, sweep) = _angles[i];
            var a = angle;
            while (a < start)
                a += Math.PI * 2;
            if (a - start <= sweep)
                return i;
        }
        return -1;
    }

    protected override string? TooltipFor(int index)
    {
        var slices = Slices;
        if (slices is null || index >= slices.Count)
            return null;
        var s = slices[index];
        var total = slices.Sum(x => x.Value);
        var share = total > 0 ? s.Value / total : 0;
        return $"{s.Label}\n{s.Detail} · %{Math.Round(share * 100)}";
    }
}

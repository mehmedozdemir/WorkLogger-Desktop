using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WorkLogger.Desktop.Controls;

public sealed record ChartSlice(string Label, double Value, Color Color, string? Detail = null);

/// <summary>
/// Özel çizilen grafiklerin ortak tabanı: tema fırçaları, metin çizimi ve fareyle üzerine gelince ipucu.
/// Fırçalar Styles.axaml'da DynamicResource ile atanır; böylece tema değişince grafik de yenilenir.
/// </summary>
public abstract class ChartBase : Control
{
    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(TextBrush), Brushes.Black);

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(MutedBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(TrackBrush), Brushes.LightGray);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(GridBrush), Brushes.LightGray);

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(AccentBrush), Brushes.RoyalBlue);

    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<ChartBase, IBrush?>(nameof(SurfaceBrush), Brushes.White);

    public static readonly StyledProperty<ICommand?> ItemCommandProperty =
        AvaloniaProperty.Register<ChartBase, ICommand?>(nameof(ItemCommand));

    protected static readonly Typeface Font = new("Segoe UI");
    protected static readonly Typeface FontBold = new("Segoe UI", FontStyle.Normal, FontWeight.SemiBold);

    static ChartBase()
    {
        AffectsRender<ChartBase>(TextBrushProperty, MutedBrushProperty, TrackBrushProperty, GridBrushProperty, AccentBrushProperty, SurfaceBrushProperty);
    }

    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? MutedBrush { get => GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    /// <summary>Bir öğeye tıklanınca öğenin Tag değeriyle çalıştırılır (ör. güne git).</summary>
    public ICommand? ItemCommand { get => GetValue(ItemCommandProperty); set => SetValue(ItemCommandProperty, value); }

    protected int HoverIndex { get; private set; } = -1;

    /// <summary>Verilen noktadaki öğenin indeksi; yoksa -1.</summary>
    protected virtual int HitTestItem(Point point) => -1;

    /// <summary>Öğenin ipucu metni.</summary>
    protected virtual string? TooltipFor(int index) => null;

    /// <summary>Öğeye tıklanınca komuta verilecek değer.</summary>
    protected virtual object? TagFor(int index) => null;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = HitTestItem(e.GetPosition(this));
        if (index == HoverIndex)
            return;
        HoverIndex = index;
        var tip = index >= 0 ? TooltipFor(index) : null;
        ToolTip.SetTip(this, tip);
        ToolTip.SetIsOpen(this, tip is not null);
        Cursor = index >= 0 && ItemCommand is not null && TagFor(index) is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoverIndex = -1;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var index = HitTestItem(e.GetPosition(this));
        if (index < 0)
            return;
        var tag = TagFor(index);
        if (tag is not null && ItemCommand?.CanExecute(tag) == true)
            ItemCommand.Execute(tag);
    }

    protected static FormattedText Text(string text, double size, IBrush? brush, bool bold = false) =>
        new(text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight, bold ? FontBold : Font, size, brush ?? Brushes.Gray);

    protected static void DrawText(DrawingContext ctx, FormattedText text, Point at, TextAnchor anchor = TextAnchor.Left)
    {
        var x = anchor switch
        {
            TextAnchor.Center => at.X - text.Width / 2,
            TextAnchor.Right => at.X - text.Width,
            _ => at.X,
        };
        ctx.DrawText(text, new Point(x, at.Y));
    }

    protected static IBrush WithOpacity(Color color, double opacity) =>
        new ImmutableSolidColorBrush(color, opacity);

    protected static Color ColorOf(IBrush? brush, Color fallback) =>
        brush is ISolidColorBrush solid ? solid.Color : fallback;
}

public enum TextAnchor
{
    Left,
    Center,
    Right,
}

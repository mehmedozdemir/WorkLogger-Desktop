using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WorkLogger.Desktop.Services;

/// <summary>
/// Her issue'ya kalıcı bir renk verir: aynı key tüm grafiklerde ve her açılışta aynı renkte görünür.
/// Renkler açık ve koyu temada okunabilir olacak şekilde seçildi.
/// </summary>
public static class IssuePalette
{
    private static readonly Color[] Colors =
    [
        Color.Parse("#4C6EF5"),
        Color.Parse("#12B886"),
        Color.Parse("#F59F00"),
        Color.Parse("#E64980"),
        Color.Parse("#7950F2"),
        Color.Parse("#15AABF"),
        Color.Parse("#FA5252"),
        Color.Parse("#40C057"),
        Color.Parse("#FD7E14"),
        Color.Parse("#228BE6"),
        Color.Parse("#BE4BDB"),
        Color.Parse("#82C91E"),
    ];

    private static readonly Dictionary<string, IBrush> Brushes = [];

    public static readonly Color Unclassified = Color.Parse("#94A3B8");

    public static Color For(string key)
    {
        // string.GetHashCode süreçten sürece değişir; renk kalıcı olsun diye FNV-1a kullanıyoruz.
        var hash = 2166136261u;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return Colors[hash % (uint)Colors.Length];
    }

    public static IBrush BrushFor(string key)
    {
        if (!Brushes.TryGetValue(key, out var brush))
            Brushes[key] = brush = new ImmutableSolidColorBrush(For(key));
        return brush;
    }

    /// <summary>Uygulama dağılımı gibi sıralı listeler için sırayla renk.</summary>
    public static Color Sequential(int index) => Colors[index % Colors.Length];
}

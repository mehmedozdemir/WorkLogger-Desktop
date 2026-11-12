using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace WorkLogger.Desktop.Services;

/// <summary>ViewModel'lerin görünüm katmanına bağımlı olmadan kullandığı küçük UI servisleri.</summary>
public static class UiServices
{
    private static TopLevel? _topLevel;

    public static void Attach(TopLevel topLevel) => _topLevel = topLevel;

    public static async Task CopyTextAsync(string text)
    {
        if (_topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}

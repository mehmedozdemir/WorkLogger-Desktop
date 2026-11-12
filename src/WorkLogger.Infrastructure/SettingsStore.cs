using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkLogger.Core.Settings;

namespace WorkLogger.Infrastructure;

/// <summary>Ayarları JSON dosyasında tutar. Bozuk dosya uygulamayı çökertmez; varsayılanlarla devam edilir.</summary>
public sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => paths.SettingsFile;

    public DateTime LastWriteTimeUtc => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    /// <summary>Okuma hatası varsa varsayılan ayarları ve hata mesajını döner.</summary>
    public (WorkLoggerSettings Settings, string? Error) Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new WorkLoggerSettings();
            Save(defaults);
            return (defaults, null);
        }
        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<WorkLoggerSettings>(json, Options) ?? new WorkLoggerSettings();
            var errors = settings.Validate();
            return errors.Count == 0
                ? (settings, null)
                : (new WorkLoggerSettings(), "Ayarlar geçersiz, varsayılanlar kullanılıyor: " + string.Join(" ", errors));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new WorkLoggerSettings(), $"Ayar dosyası okunamadı ({ex.Message}); varsayılanlar kullanılıyor.");
        }
    }

    public void Save(WorkLoggerSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static WorkLoggerSettings Clone(WorkLoggerSettings settings) =>
        JsonSerializer.Deserialize<WorkLoggerSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
}

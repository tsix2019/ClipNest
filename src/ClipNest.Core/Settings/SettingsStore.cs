using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipNest.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="AppSettings"/>. A missing or corrupt file yields defaults.</summary>
public sealed class SettingsStore(string path)
{
    public string FilePath { get; } = path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                var settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
                if (settings is not null) return Sanitize(settings);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // fall through to defaults; the next Save() rewrites a valid file
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var tmp = FilePath + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        s.IgnoredApps ??= [];
        if (!Enum.IsDefined(s.Retention)) s.Retention = RetentionPeriod.Month;
        s.PanelHeight = Math.Clamp(double.IsFinite(s.PanelHeight) ? s.PanelHeight : 320, 200, 520);
        if (!HotkeyGesture.TryParse(s.PanelHotkey, out _)) s.PanelHotkey = "Ctrl+Shift+V";
        if (!HotkeyGesture.TryParse(s.StackHotkey, out _)) s.StackHotkey = "Ctrl+Shift+C";
        return s;
    }
}

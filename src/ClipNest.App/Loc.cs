using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClipNest.Core;
using Microsoft.UI.Xaml.Markup;

namespace ClipNest.App;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class StringsJsonContext : JsonSerializerContext;

/// <summary>
/// UI strings from embedded <c>Strings/&lt;lang&gt;.json</c> tables. English is the fallback for missing keys.
/// </summary>
public static class Loc
{
    public static readonly string[] Supported = ["en-US", "zh-CN"];

    private static Dictionary<string, string> _strings = [];
    private static Dictionary<string, string> _fallback = [];

    public static string Language { get; private set; } = "en-US";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <param name="setting">"system" or a language tag from <see cref="Supported"/>.</param>
    public static void Initialize(string setting)
    {
        Language = Resolve(setting);
        Culture = CultureInfo.GetCultureInfo(Language);
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        _fallback = Load("en-US");
        _strings = Language == "en-US" ? _fallback : Load(Language);
    }

    public static string Resolve(string setting)
    {
        if (Supported.Contains(setting)) return setting;
        var ui = CultureInfo.InstalledUICulture.Name;
        return ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    }

    public static string Get(string key) =>
        _strings.TryGetValue(key, out var s) || _fallback.TryGetValue(key, out s) ? s : key;

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    public static string KindName(ClipKind kind) => Get("Kind_" + kind);

    public static string RelativeTime(DateTimeOffset time, DateTimeOffset now)
    {
        var v = Core.RelativeTime.Describe(time, now);
        return v.Key == "Time_Date"
            ? time.ToLocalTime().ToString(Get("Time_DateFormat"), Culture)
            : Format(v.Key, v.Value);
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Strings.{language}.json");
        if (stream is null) return [];
        return JsonSerializer.Deserialize(stream, StringsJsonContext.Default.DictionaryStringString) ?? [];
    }
}

/// <summary>XAML: <c>Text="{l:Tr Key=Panel_Search}"</c>.</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Tr : MarkupExtension
{
    public string Key { get; set; } = "";

    protected override object ProvideValue() => Loc.Get(Key);
}

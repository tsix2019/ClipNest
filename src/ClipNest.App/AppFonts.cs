using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ClipNest.App;

/// <summary>
/// The interface font chosen in Settings. Controls and text styles read <c>ContentControlThemeFontFamily</c>,
/// so overriding that one resource before any window exists changes the font everywhere.
/// </summary>
public static class AppFonts
{
    private const string ResourceKey = "ContentControlThemeFontFamily";

    private static readonly string[] TextStyles =
    [
        "CaptionTextBlockStyle", "BodyTextBlockStyle", "BodyStrongTextBlockStyle", "SubtitleTextBlockStyle",
        "TitleTextBlockStyle", "TitleLargeTextBlockStyle", "DisplayTextBlockStyle",
    ];

    /// <summary>The interface font (the system font unless the user picked one).</summary>
    public static FontFamily Ui { get; private set; } = FontFamily.XamlAutoFontFamily;

    /// <summary>Call once at launch, before the first window; later changes need a restart.</summary>
    public static void Initialize(Application app, string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        // The chosen family first; CJK text it lacks still falls back to the system font.
        Ui = new FontFamily($"{family}, Segoe UI Variable Text, Microsoft YaHei UI");
        // A merged dictionary added last wins over XamlControlsResources' definition.
        var overrides = new ResourceDictionary { [ResourceKey] = Ui };
        // The built-in text styles captured the default font when they were loaded: replace them with
        // copies that use the chosen one.
        foreach (var key in TextStyles)
        {
            if (!app.Resources.TryGetValue(key, out var value) || value is not Style original) continue;
            var style = new Style(typeof(Microsoft.UI.Xaml.Controls.TextBlock)) { BasedOn = original };
            style.Setters.Add(new Setter(Microsoft.UI.Xaml.Controls.TextBlock.FontFamilyProperty, Ui));
            overrides[key] = style;
        }
        app.Resources.MergedDictionaries.Add(overrides);
    }
}

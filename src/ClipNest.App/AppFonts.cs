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

    /// <summary>The interface font (the system font unless the user picked one).</summary>
    public static FontFamily Ui { get; private set; } = FontFamily.XamlAutoFontFamily;

    /// <summary>Call once at startup, before the first window; later changes need a restart.</summary>
    public static void Initialize(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        // The chosen family first; CJK text it lacks still falls back to the system font.
        Ui = new FontFamily($"{family}, Segoe UI Variable Text, Microsoft YaHei UI");
        Application.Current.Resources[ResourceKey] = Ui;
    }
}

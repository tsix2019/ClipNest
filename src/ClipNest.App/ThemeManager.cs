using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ClipNest.App;

/// <summary>
/// Light / dark / follow-system appearance for every ClipNest window, switchable at runtime.
/// XAML <c>{ThemeResource}</c> references update automatically; code-built UI uses <see cref="Brush"/>.
/// </summary>
public static class ThemeManager
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private static readonly List<Window> Windows = [];
    private static readonly UISettings UiSettings = new();
    private static readonly AccessibilitySettings Accessibility = new();
    private static Microsoft.UI.Dispatching.DispatcherQueue? _ui;

    public static string Mode { get; private set; } = System;

    /// <summary>Raised on the UI thread when the effective theme may have changed.</summary>
    public static event Action? Changed;

    public static ElementTheme Requested => Mode switch
    {
        Light => ElementTheme.Light,
        Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static bool IsDark => Requested switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => SystemIsDark(),
    };

    public static void Initialize(string mode)
    {
        Mode = Normalize(mode);
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        // Windows light/dark switch while following the system.
        // Fires for Windows light/dark and high-contrast switches. (AccessibilitySettings.HighContrastChanged
        // cannot be subscribed to from a desktop app: it needs a CoreWindow.)
        UiSettings.ColorValuesChanged += (_, _) => _ui?.TryEnqueue(() => Changed?.Invoke());
    }

    public static string Normalize(string? mode) => mode is Light or Dark ? mode : System;

    public static void Register(Window window)
    {
        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);
        Apply(window);
    }

    public static void SetMode(string mode)
    {
        Mode = Normalize(mode);
        foreach (var window in Windows.ToArray()) Apply(window);
        Changed?.Invoke();
    }

    private static void Apply(Window window)
    {
        if (window.Content is FrameworkElement root) root.RequestedTheme = Requested;
        try
        {
            window.AppWindow.TitleBar.PreferredTheme = Requested switch
            {
                ElementTheme.Light => TitleBarTheme.Light,
                ElementTheme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode,
            };
        }
        catch (Exception)
        {
            // older runtimes without PreferredTheme: caption buttons keep the system colors
        }
    }

    private static bool IsHighContrast()
    {
        try
        {
            return Accessibility.HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SystemIsDark()
    {
        var bg = UiSettings.GetColorValue(UIColorType.Background);
        return bg.R + bg.G + bg.B < 384;
    }

    /// <summary>
    /// Resolves a theme brush for the effective ClipNest theme. Needed for brushes created in code,
    /// because <c>Application.Resources[key]</c> resolves against the app theme, not a forced window theme.
    /// </summary>
    public static Brush Brush(string key)
    {
        // High contrast overrides light/dark, as it does for {ThemeResource} in XAML.
        var themeKey = IsHighContrast() ? "HighContrast" : IsDark ? "Dark" : "Light";
        if (Find(Application.Current.Resources, key, themeKey) is Brush b) return b;
        return (Brush)Application.Current.Resources[key];
    }

    private static object? Find(ResourceDictionary dictionary, string key, string themeKey)
    {
        // WinUI's own dictionaries name the dark theme "Default".
        foreach (var name in themeKey == "Dark" ? new[] { "Dark", "Default" } : [themeKey])
        {
            if (dictionary.ThemeDictionaries.TryGetValue(name, out var t) && t is ResourceDictionary theme && theme.TryGetValue(key, out var value))
                return value;
        }
        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, key, themeKey) is { } found) return found;
        }
        return null;
    }
}

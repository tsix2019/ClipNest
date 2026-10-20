namespace ClipNest.Core.Settings;

/// <summary>User preferences persisted as <c>settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>"system", "en-US" or "zh-CN". Applied on next launch.</summary>
    public string Language { get; set; } = "system";

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    public bool LaunchAtStartup { get; set; } = true;
    public RetentionPeriod Retention { get; set; } = RetentionPeriod.Month;

    /// <summary>True: Enter pastes into the previously active app. False: only copies to the clipboard.</summary>
    public bool PasteToActiveApp { get; set; } = true;

    public bool AlwaysPlainText { get; set; }
    public bool FetchLinkTitles { get; set; } = true;
    public bool OcrImages { get; set; } = true;

    public string PanelHotkey { get; set; } = "Ctrl+Shift+V";
    public string StackHotkey { get; set; } = "Ctrl+Shift+C";

    /// <summary>Executable paths (case-insensitive) whose copies are never recorded.</summary>
    public List<string> IgnoredApps { get; set; } = [];

    /// <summary>Skip content that apps flag as sensitive (password managers etc.).</summary>
    public bool IgnoreConfidential { get; set; } = true;

    /// <summary>Skip text that looks like a credential (private keys, API tokens, JWTs).</summary>
    public bool DetectSecrets { get; set; } = true;

    /// <summary>Record copied images (screenshots etc.).</summary>
    public bool RecordImages { get; set; } = true;

    /// <summary>Hide ClipNest windows from screenshots, recordings and screen sharing.</summary>
    public bool HideFromCapture { get; set; } = true;

    /// <summary>Panel height in device-independent pixels.</summary>
    public double PanelHeight { get; set; } = 320;

    public bool OnboardingDone { get; set; }

    public bool IsIgnored(string? exePath) =>
        exePath is not null && IgnoredApps.Exists(p => string.Equals(p, exePath, StringComparison.OrdinalIgnoreCase));
}

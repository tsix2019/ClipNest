using ClipNest.Platform.Native;

namespace ClipNest.Platform;

/// <summary>Registered clipboard format ids used by ClipNest.</summary>
internal static class ClipboardFormats
{
    public static readonly uint Rtf = User32.RegisterClipboardFormatW("Rich Text Format");
    public static readonly uint Html = User32.RegisterClipboardFormatW("HTML Format");
    public static readonly uint Png = User32.RegisterClipboardFormatW("PNG");
    public static readonly uint PreferredDropEffect = User32.RegisterClipboardFormatW("Preferred DropEffect");

    /// <summary>Marker placed on everything ClipNest writes so the monitor does not record it again.</summary>
    public static readonly uint Internal = User32.RegisterClipboardFormatW("ClipNest.Internal");

    // Privacy conventions honoured by Windows clipboard history and password managers.
    public static readonly uint ExcludeFromMonitor = User32.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    public static readonly uint ViewerIgnore = User32.RegisterClipboardFormatW("Clipboard Viewer Ignore");
    public static readonly uint CanIncludeInHistory = User32.RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    public static readonly uint CanUploadToCloud = User32.RegisterClipboardFormatW("CanUploadToCloudClipboard");
}

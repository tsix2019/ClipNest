using ClipNest.Platform.Native;
using Microsoft.Win32;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>"Launch at startup" via the per-user Run key.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipNest";
    public const string AutostartArgument = "--autostart";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" {AutostartArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Ensures one ClipNest per user session; a second launch asks the first to show its panel.</summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Posted to the running instance's <see cref="MessageWindow"/>.</summary>
    public static readonly uint ActivateMessage = RegisterWindowMessageW("ClipNest.Activate");

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var created);
        if (created) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static void SignalExisting()
    {
        var hwnd = FindWindowW(MessageWindow.ClassName, null);
        if (hwnd != 0) PostMessageW(hwnd, ActivateMessage, 0, 0);
    }

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}

/// <summary>Physical-pixel rectangle.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class DisplayHelper
{
    /// <summary>Mouse position in physical screen pixels.</summary>
    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var pt);
        return (pt.X, pt.Y);
    }

    /// <summary>Work area (excluding the taskbar) and DPI scale of the monitor under the mouse cursor.</summary>
    public static (PixelRect WorkArea, double Scale) GetCursorMonitor()
    {
        GetCursorPos(out var pt);
        var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        var dpi = Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 ? dx : 96u;
        var r = info.rcWork;
        return (new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), dpi / 96.0);
    }
}

public static class WindowStyler
{
    /// <summary>Gives the window keyboard focus (allowed right after a hotkey or tray click).</summary>
    public static void BringToFront(nint hwnd)
    {
        if (GetForegroundWindow() != hwnd) SetForegroundWindow(hwnd);
    }

    /// <summary>Hides the window from the taskbar and Alt+Tab.</summary>
    public static void MakeToolWindow(nint hwnd)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }

    /// <summary>Clicking the window will not activate it (keeps focus in the panel).</summary>
    public static void MakeNoActivate(nint hwnd)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
    }

    /// <summary>Windows 11 rounded corners; ignored on Windows 10.</summary>
    public static void SetRoundedCorners(nint hwnd)
    {
        var pref = Dwmapi.DWMWCP_ROUND;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    /// <summary>Removes the 1px accent border Windows 11 draws around windows.</summary>
    public static void HideBorder(nint hwnd)
    {
        var none = Dwmapi.DWMWA_COLOR_NONE;
        Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_BORDER_COLOR, ref none, sizeof(uint));
    }

    /// <summary>Keeps the window out of screenshots, recordings and screen sharing.</summary>
    public static void SetCaptureExcluded(nint hwnd, bool exclude)
    {
        if (!SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE) && exclude)
            SetWindowDisplayAffinity(hwnd, WDA_MONITOR); // pre-2004 Windows 10: shows black instead
    }
}

public static class MemoryTrimmer
{
    /// <summary>Compacts the managed heap and returns unused pages to the OS.</summary>
    public static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Kernel32.SetProcessWorkingSetSize(Kernel32.GetCurrentProcess(), -1, -1);
    }
}

public static class NativeIcon
{
    /// <summary>Loads an .ico file at the given pixel size (caller destroys with <see cref="Destroy"/>).</summary>
    public static nint Load(string icoPath, int size) =>
        LoadImageW(0, icoPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

    public static void Destroy(nint icon)
    {
        if (icon != 0) DestroyIcon(icon);
    }

    public static int SmallIconSize() =>
        GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
}

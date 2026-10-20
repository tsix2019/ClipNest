using System.Runtime.InteropServices;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

public enum PasteOutcome
{
    Pasted,
    NoTarget,
    /// <summary>Target runs elevated; Windows (UIPI) drops our synthetic input.</summary>
    Blocked,
    FocusFailed,
}

/// <summary>
/// Remembers the app window the user last worked in and pastes into it. Foreground changes are tracked
/// continuously, so opening the panel from the tray (taskbar in front) still targets the real app.
/// Must be created on a thread that pumps messages.
/// </summary>
public sealed class PasteInjector : IDisposable
{
    /// <summary>Tag on our synthetic input so our own keyboard hook can recognize it.</summary>
    public const nint InjectedTag = 0x434E5354; // "CNST"

    private static readonly ushort[] Modifiers = [VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN];

    /// <summary>Shell surfaces that are never paste targets.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame",
    };

    private static PasteInjector? _instance;
    private readonly nint _hook;
    private nint _lastAppWindow;

    public unsafe PasteInjector()
    {
        _instance = this;
        Remember(GetForegroundWindow());
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public nint Target { get; private set; }

    /// <summary>Call right before showing the panel.</summary>
    public void CaptureTarget()
    {
        Remember(GetForegroundWindow());
        Target = _lastAppWindow != 0 && IsWindow(_lastAppWindow) ? _lastAppWindow : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnForeground(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time) =>
        _instance?.Remember(hwnd);

    private void Remember(nint hwnd)
    {
        if (IsAppWindow(hwnd)) _lastAppWindow = hwnd;
    }

    internal static unsafe bool IsAppWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd) || ProcessInfo.IsOwnWindow(hwnd)) return false;
        var buffer = stackalloc char[128];
        var len = GetClassNameW(hwnd, buffer, 128);
        return len <= 0 || !ShellClasses.Contains(new string(buffer, 0, len));
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    public async Task<PasteOutcome> PasteAsync()
    {
        var target = Target;
        if (target == 0 || !IsWindow(target)) return PasteOutcome.NoTarget;
        if (!ProcessInfo.IsCurrentProcessElevated && ProcessInfo.IsElevated(target)) return PasteOutcome.Blocked;

        if (!await ActivateAsync(target)) return PasteOutcome.FocusFailed;

        // Wait briefly for the user to let go of modifiers (e.g. after Ctrl+3), then lift any still down.
        for (var i = 0; i < 15 && AnyModifierDown(); i++) await Task.Delay(10);
        ReleaseModifiers();
        SendCtrlV();
        return PasteOutcome.Pasted;
    }

    /// <summary>Brings <paramref name="target"/> to the foreground; returns true once it actually is.</summary>
    public static async Task<bool> ActivateAsync(nint target)
    {
        if (IsIconic(target)) ShowWindow(target, SW_RESTORE);
        if (GetForegroundWindow() != target && !SetForegroundWindow(target))
        {
            // Fallback: temporarily share input state with the current foreground thread.
            var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var self = Kernel32.GetCurrentThreadId();
            AttachThreadInput(self, fgThread, true);
            BringWindowToTop(target);
            SetForegroundWindow(target);
            AttachThreadInput(self, fgThread, false);
        }

        for (var i = 0; i < 20; i++)
        {
            if (GetForegroundWindow() == target)
            {
                await Task.Delay(25); // let the target finish processing its activation
                return true;
            }
            await Task.Delay(10);
        }
        return false;
    }

    private static bool AnyModifierDown()
    {
        foreach (var vk in Modifiers)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        }
        return false;
    }

    private static unsafe void ReleaseModifiers()
    {
        var ups = stackalloc INPUT[Modifiers.Length];
        var n = 0;
        foreach (var vk in Modifiers)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) ups[n++] = Key(vk, up: true);
        }
        if (n > 0) SendInput((uint)n, ups, sizeof(INPUT));
    }

    public static unsafe void SendCtrlV()
    {
        var inputs = stackalloc INPUT[4];
        inputs[0] = Key(VK_CONTROL, up: false);
        inputs[1] = Key(VK_V, up: false);
        inputs[2] = Key(VK_V, up: true);
        inputs[3] = Key(VK_CONTROL, up: true);
        var sent = SendInput(4, inputs, sizeof(INPUT));
        if (sent != 4) Log.Warn($"SendInput sent {sent}/4");
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT
        {
            wVk = vk,
            wScan = (ushort)MapVirtualKeyW(vk, 0),
            dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (vk is VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN ? KEYEVENTF_EXTENDEDKEY : 0),
            dwExtraInfo = InjectedTag,
        },
    };
}

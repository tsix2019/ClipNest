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

/// <summary>Remembers the window that was active before the panel opened and pastes into it.</summary>
public sealed class PasteInjector
{
    /// <summary>Tag on our synthetic input so our own keyboard hook can recognize it.</summary>
    public const nint InjectedTag = 0x434E5354; // "CNST"

    private static readonly ushort[] Modifiers = [VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN];

    public nint Target { get; private set; }

    /// <summary>Call right before showing the panel.</summary>
    public void CaptureTarget()
    {
        var fg = GetForegroundWindow();
        if (fg != 0 && !ProcessInfo.IsOwnWindow(fg)) Target = fg;
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

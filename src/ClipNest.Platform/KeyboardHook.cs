using System.Runtime.InteropServices;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>
/// Low-level keyboard hook that reports when the user pastes (Ctrl+V released) in another app.
/// Installed only while Paste Stack is open. Must be created on a thread that pumps messages.
/// </summary>
public sealed unsafe class KeyboardHook : IDisposable
{
    private static KeyboardHook? _active;
    private nint _hook;
    private bool _vDownWithCtrl;

    /// <summary>Raised (asynchronously, on the hook thread's queue) after the user pasted with Ctrl+V.</summary>
    public event Action? PasteDetected;

    public KeyboardHook()
    {
        _active = this;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, Kernel32.GetModuleHandleW(null), 0);
        if (_hook == 0) Log.Warn($"SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}");
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        if (code >= 0 && self is not null)
        {
            try
            {
                self.Handle((uint)wParam, (KBDLLHOOKSTRUCT*)lParam);
            }
            catch (Exception e)
            {
                Log.Error("KeyboardHook", e);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void Handle(uint message, KBDLLHOOKSTRUCT* data)
    {
        if (data->vkCode != VK_V || data->dwExtraInfo == PasteInjector.InjectedTag) return;
        if (ProcessInfo.IsOwnWindow(GetForegroundWindow())) return;

        var ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if (ctrl) _vDownWithCtrl = true;
        }
        else if (message is WM_KEYUP or WM_SYSKEYUP && _vDownWithCtrl)
        {
            _vDownWithCtrl = false;
            // Never block the hook: Windows removes hooks that take too long.
            var handler = PasteDetected;
            if (handler is not null) ThreadPool.QueueUserWorkItem(_ => handler());
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        if (ReferenceEquals(_active, this)) _active = null;
    }
}

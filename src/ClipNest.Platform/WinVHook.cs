using System.Runtime.InteropServices;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>
/// Takes Win+V over from Windows' clipboard history. The shell owns that hotkey, so RegisterHotKey fails;
/// a low-level keyboard hook sees the key first and swallows it. Installed only while the setting is on
/// (removing the hook gives Win+V back to Windows). Must be created on a thread that pumps messages.
/// </summary>
public sealed unsafe class WinVHook : IDisposable
{
    private const ushort VK_NONE = 0xFF; // unassigned: tapping it stops the Win key from opening Start

    private static WinVHook? _active;
    private nint _hook;
    private bool _swallowingV;

    /// <summary>Raised on the hook's thread after Win+V was pressed; keep handlers short (post work).</summary>
    public event Action? Pressed;

    public WinVHook()
    {
        _active = this;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, Kernel32.GetModuleHandleW(null), 0);
        if (_hook == 0) Log.Warn($"Win+V hook: SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}");
    }

    public bool IsInstalled => _hook != 0;

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        var self = _active;
        if (code >= 0 && self is not null)
        {
            try
            {
                if (self.Handle((uint)wParam, (KBDLLHOOKSTRUCT*)lParam)) return 1; // swallow
            }
            catch (Exception e)
            {
                Log.Error("WinVHook", e);
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>True when the key event must not reach other apps.</summary>
    private bool Handle(uint message, KBDLLHOOKSTRUCT* data)
    {
        if (data->vkCode != VK_V || data->dwExtraInfo == PasteInjector.InjectedTag) return false;
        var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;

        if (!down)
        {
            // The V release that belongs to a swallowed press is swallowed too.
            if (!_swallowingV) return false;
            _swallowingV = false;
            return true;
        }

        var win = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        if (!win) return false;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
            || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) return false; // Win+Ctrl+V etc. stay with Windows

        if (_swallowingV) return true; // auto-repeat while held
        _swallowingV = true;
        PasteInjector.TapKey(VK_NONE);
        Pressed?.Invoke();
        return true;
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

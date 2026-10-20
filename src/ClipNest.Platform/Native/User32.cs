using System.Runtime.InteropServices;

namespace ClipNest.Platform.Native;

internal static unsafe partial class User32
{
    private const string Lib = "user32.dll";

    // ---- messages ----
    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_APP = 0x8000;
    public const uint WM_USER = 0x0400;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    // ---- window styles ----
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;
    public const nint WS_EX_APPWINDOW = 0x00040000;
    public const nint WS_EX_NOACTIVATE = 0x08000000;
    public const nint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_POPUP = 0x80000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_RESTORE = 9;

    // ---- clipboard ----
    public const uint CF_TEXT = 1;
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;
    public const uint CF_DIBV5 = 17;

    // ---- hotkeys ----
    public const uint MOD_NOREPEAT = 0x4000;

    // ---- input ----
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_RSHIFT = 0xA1;
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_V = 0x56;

    // ---- menus ----
    public const uint MF_STRING = 0x0000;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_POPUP = 0x0010;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_BOTTOMALIGN = 0x0020;

    // ---- display affinity ----
    public const uint WDA_NONE = 0x0;
    public const uint WDA_MONITOR = 0x1;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;

    public const int WH_KEYBOARD_LL = 13;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    /// <summary>INPUT with the union sized for MOUSEINPUT (the largest member) on 64-bit.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [LibraryImport(Lib, SetLastError = true)] public static partial ushort RegisterClassExW(in WNDCLASSEXW wc);
    [LibraryImport(Lib, SetLastError = true)] public static partial int UnregisterClassW(char* className, nint hInstance);
    [LibraryImport(Lib, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(nint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint hInstance, nint param);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyWindow(nint hwnd);
    [LibraryImport(Lib)] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowW(string? className, string? windowName);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport(Lib, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AddClipboardFormatListener(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool RemoveClipboardFormatListener(nint hwnd);
    [LibraryImport(Lib, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool OpenClipboard(nint hwndOwner);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseClipboard();
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool EmptyClipboard();
    [LibraryImport(Lib)] public static partial nint GetClipboardData(uint format);
    [LibraryImport(Lib, SetLastError = true)] public static partial nint SetClipboardData(uint format, nint hMem);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsClipboardFormatAvailable(uint format);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterClipboardFormatW(string name);
    [LibraryImport(Lib)] public static partial uint GetClipboardSequenceNumber();
    [LibraryImport(Lib)] public static partial nint GetClipboardOwner();

    [LibraryImport(Lib, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport(Lib)] public static partial nint GetForegroundWindow();
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BringWindowToTop(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AllowSetForegroundWindow(uint processId);
    [LibraryImport(Lib)] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindow(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsIconic(nint hwnd);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport(Lib)] public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport(Lib, SetLastError = true)] public static partial uint SendInput(uint count, INPUT* inputs, int size);
    [LibraryImport(Lib)] public static partial short GetAsyncKeyState(int vk);
    [LibraryImport(Lib)] public static partial uint MapVirtualKeyW(uint code, uint mapType);

    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT pt);
    [LibraryImport(Lib)] public static partial nint MonitorFromPoint(POINT pt, uint flags);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [LibraryImport(Lib, EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport(Lib, EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport(Lib)] public static partial nint CreatePopupMenu();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AppendMenuW(nint menu, uint flags, nint idOrSubmenu, string? text);
    [LibraryImport(Lib)] public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyMenu(nint menu);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial nint LoadImageW(nint hInst, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(nint hIcon);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetIconInfo(nint hIcon, out ICONINFO info);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);
    [LibraryImport(Lib)] public static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport(Lib)] public static partial uint GetDpiForSystem();
    [LibraryImport(Lib)] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport(Lib)] public static partial nint GetDC(nint hwnd);
    [LibraryImport(Lib)] public static partial int ReleaseDC(nint hwnd, nint hdc);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [LibraryImport(Lib)] public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmod, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> fn, uint processId, uint threadId, uint flags);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWinEvent(nint hook);
    [LibraryImport(Lib)] public static partial int GetClassNameW(nint hwnd, char* name, int max);

    [LibraryImport(Lib, SetLastError = true)] public static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> fn, nint hMod, uint threadId);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport(Lib)] public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
}

using System.Runtime.InteropServices;

namespace ClipNest.Platform.Native;

internal static unsafe partial class Kernel32
{
    private const string Lib = "kernel32.dll";

    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport(Lib, SetLastError = true)] public static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport(Lib)] public static partial void* GlobalLock(nint hMem);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GlobalUnlock(nint hMem);
    [LibraryImport(Lib)] public static partial nuint GlobalSize(nint hMem);
    [LibraryImport(Lib)] public static partial nint GlobalFree(nint hMem);

    [LibraryImport(Lib)] public static partial uint GetCurrentThreadId();
    [LibraryImport(Lib)] public static partial uint GetCurrentProcessId();
    [LibraryImport(Lib)] public static partial nint GetCurrentProcess();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);
    [LibraryImport(Lib, SetLastError = true)] public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(nint handle);
    [LibraryImport(Lib, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* buffer, ref uint size);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetProcessWorkingSetSize(nint process, nint min, nint max);
}

internal static unsafe partial class Shell32
{
    private const string Lib = "shell32.dll";

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x01, NIIF_NOSOUND = 0x10;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x0400;
    public const uint NIN_KEYSELECT = 0x0401;

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [LibraryImport(Lib)] public static partial uint DragQueryFileW(nint hDrop, uint index, char* file, uint size);
}

internal static unsafe partial class Gdi32
{
    private const string Lib = "gdi32.dll";

    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [LibraryImport(Lib)] public static partial int GetObjectW(nint h, int size, void* obj);
    [LibraryImport(Lib)] public static partial int GetDIBits(nint hdc, nint hbm, uint start, uint lines, void* bits, BITMAPINFOHEADER* bmi, uint usage);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint obj);
}

internal static partial class Dwmapi
{
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, uint attr, ref int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, uint attr, ref uint value, int size);
}

internal static partial class Shcore
{
    public const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}

internal static unsafe partial class Advapi32
{
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int infoClass, void* info, uint length, out uint returnLength);
}

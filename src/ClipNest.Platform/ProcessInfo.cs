using ClipNest.Platform.Native;

namespace ClipNest.Platform;

/// <summary>Process lookups by window handle.</summary>
public static unsafe class ProcessInfo
{
    public static uint CurrentProcessId { get; } = Kernel32.GetCurrentProcessId();

    public static uint ProcessIdOf(nint hwnd)
    {
        if (hwnd == 0) return 0;
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static bool IsOwnWindow(nint hwnd) => hwnd != 0 && ProcessIdOf(hwnd) == CurrentProcessId;

    /// <summary>Full executable path of the process owning <paramref name="hwnd"/>, or null.</summary>
    public static string? ExePathOf(nint hwnd)
    {
        var pid = ProcessIdOf(hwnd);
        return pid == 0 ? null : ExePathOf(pid);
    }

    public static string? ExePathOf(uint pid)
    {
        var process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return Kernel32.QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    /// <summary>True when the process owning <paramref name="hwnd"/> runs elevated (UIPI blocks input from us then).</summary>
    public static bool IsElevated(nint hwnd)
    {
        var pid = ProcessIdOf(hwnd);
        if (pid == 0) return false;
        var process = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return true; // cannot even query it: assume a protected/elevated process
        try
        {
            return IsElevatedProcess(process);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    public static bool IsCurrentProcessElevated { get; } = IsElevatedProcess(Kernel32.GetCurrentProcess());

    private static bool IsElevatedProcess(nint process)
    {
        if (!Advapi32.OpenProcessToken(process, Advapi32.TOKEN_QUERY, out var token)) return false;
        try
        {
            uint elevated = 0;
            return Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }
}

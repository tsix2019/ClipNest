using System.Text;

namespace ClipNest.Platform;

/// <summary>
/// Minimal rolling file log in <c>%LocalAppData%\ClipNest\logs</c> (5 files × 1 MB). Never uploads anything.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 5;
    private static readonly Lock Gate = new();
    private static string? _dir;

    public static void Initialize(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string context, Exception e) => Write("ERROR", $"{context}: {e}");

    private static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        if (_dir is null) return;
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(_dir, "clipnest.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) Rotate(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // logging must never take the app down
        }
    }

    private static void Rotate(string path)
    {
        for (var i = MaxFiles - 1; i >= 1; i--)
        {
            var src = i == 1 ? path : $"{path}.{i - 1}";
            var dst = $"{path}.{i}";
            if (File.Exists(src)) File.Move(src, dst, overwrite: true);
        }
    }
}

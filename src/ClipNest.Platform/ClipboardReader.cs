using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using ClipNest.Core;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>Reads the current clipboard into a <see cref="ClipboardSnapshot"/>, honouring privacy markers.</summary>
public sealed unsafe class ClipboardReader(ImageProcessor images, Func<bool> ignoreConfidential)
{
    /// <summary>Formats larger than this are skipped.</summary>
    public const int MaxFormatBytes = 50 * 1024 * 1024;

    /// <summary>Returns null when the clipboard is empty, unreadable, flagged private, or written by ClipNest.</summary>
    public ClipboardSnapshot? Read()
    {
        string? text = null, rtf = null, html = null, sourcePath;
        byte[]? png = null;
        int width = 0, height = 0;
        List<string>? files = null;
        byte[]? bgra = null;

        if (!TryOpen()) return null;
        try
        {
            if (IsClipboardFormatAvailable(ClipboardFormats.Internal)) return null;
            if (IsPrivate()) return null;

            sourcePath = ProcessInfo.ExePathOf(GetClipboardOwner()) ?? ProcessInfo.ExePathOf(GetForegroundWindow());

            if (IsClipboardFormatAvailable(CF_HDROP)) files = ReadFiles();
            if (IsClipboardFormatAvailable(CF_UNICODETEXT)) text = ReadUnicode(CF_UNICODETEXT);
            if (IsClipboardFormatAvailable(ClipboardFormats.Rtf)) rtf = ReadAnsi(ClipboardFormats.Rtf, Encoding.Latin1);
            if (IsClipboardFormatAvailable(ClipboardFormats.Html)) html = ReadAnsi(ClipboardFormats.Html, Encoding.UTF8);

            if (files is null)
            {
                if (IsClipboardFormatAvailable(ClipboardFormats.Png))
                {
                    png = ReadBytes(ClipboardFormats.Png);
                    if (png is not null && !TryPngSize(png, out width, out height)) png = null;
                }
                if (png is null && IsClipboardFormatAvailable(CF_BITMAP))
                {
                    bgra = ReadBitmap(out width, out height);
                }
            }
        }
        finally
        {
            CloseClipboard();
        }

        // Encode outside the clipboard lock so other apps are not blocked.
        if (bgra is not null)
        {
            try
            {
                png = images.EncodePng(bgra, width, height);
            }
            catch (Exception e)
            {
                Log.Error("Encode clipboard bitmap", e);
            }
        }

        var snapshot = new ClipboardSnapshot
        {
            Text = text,
            Rtf = rtf,
            Html = html,
            Png = png,
            ImageWidth = width,
            ImageHeight = height,
            Files = files,
            SourceAppPath = sourcePath,
        };
        return snapshot.HasText || snapshot.HasImage || snapshot.HasFiles ? snapshot : null;
    }

    private static bool TryOpen()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0)) return true;
            Thread.Sleep(20);
        }
        Log.Warn("Clipboard busy; skipped one update");
        return false;
    }

    private bool IsPrivate()
    {
        if (IsClipboardFormatAvailable(ClipboardFormats.ExcludeFromMonitor)) return true;
        if (IsClipboardFormatAvailable(ClipboardFormats.ViewerIgnore)) return true;
        if (!ignoreConfidential()) return false;
        if (ReadDword(ClipboardFormats.CanIncludeInHistory) == 0) return true;
        if (ReadDword(ClipboardFormats.CanUploadToCloud) == 0) return true;
        return false;
    }

    /// <summary>Reads a DWORD-valued format; returns null when absent.</summary>
    private static uint? ReadDword(uint format)
    {
        if (!IsClipboardFormatAvailable(format)) return null;
        var bytes = ReadBytes(format);
        return bytes is { Length: >= 4 } ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : null;
    }

    private static byte[]? ReadBytes(uint format)
    {
        var h = GetClipboardData(format);
        if (h == 0) return null;
        var size = (long)Kernel32.GlobalSize(h);
        if (size <= 0 || size > MaxFormatBytes) return null;
        var p = Kernel32.GlobalLock(h);
        if (p == null) return null;
        try
        {
            return new ReadOnlySpan<byte>(p, (int)size).ToArray();
        }
        finally
        {
            Kernel32.GlobalUnlock(h);
        }
    }

    private static string? ReadUnicode(uint format)
    {
        var h = GetClipboardData(format);
        if (h == 0) return null;
        var size = (long)Kernel32.GlobalSize(h);
        if (size <= 0 || size > MaxFormatBytes) return null;
        var p = (char*)Kernel32.GlobalLock(h);
        if (p == null) return null;
        try
        {
            var span = new ReadOnlySpan<char>(p, (int)(size / 2));
            var end = span.IndexOf('\0');
            return new string(end >= 0 ? span[..end] : span);
        }
        finally
        {
            Kernel32.GlobalUnlock(h);
        }
    }

    private static string? ReadAnsi(uint format, Encoding encoding)
    {
        var h = GetClipboardData(format);
        if (h == 0) return null;
        var size = (long)Kernel32.GlobalSize(h);
        if (size <= 0 || size > MaxFormatBytes) return null;
        var p = (byte*)Kernel32.GlobalLock(h);
        if (p == null) return null;
        try
        {
            var span = new ReadOnlySpan<byte>(p, (int)size);
            var end = span.IndexOf((byte)0);
            return encoding.GetString(end >= 0 ? span[..end] : span);
        }
        finally
        {
            Kernel32.GlobalUnlock(h);
        }
    }

    private static List<string>? ReadFiles()
    {
        var hDrop = GetClipboardData(CF_HDROP);
        if (hDrop == 0) return null;
        var count = Shell32.DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
        if (count == 0) return null;
        var list = new List<string>((int)count);
        var buffer = new char[32768];
        fixed (char* b = buffer)
        {
            for (uint i = 0; i < count; i++)
            {
                var len = Shell32.DragQueryFileW(hDrop, i, b, (uint)buffer.Length);
                if (len > 0) list.Add(new string(b, 0, (int)len));
            }
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>Reads CF_BITMAP as top-down 32-bit BGRA with opaque alpha.</summary>
    private static byte[]? ReadBitmap(out int width, out int height)
    {
        width = height = 0;
        var hbm = GetClipboardData(CF_BITMAP);
        if (hbm == 0) return null;

        Gdi32.BITMAP bm = default;
        if (Gdi32.GetObjectW(hbm, sizeof(Gdi32.BITMAP), &bm) == 0) return null;
        width = bm.bmWidth;
        height = Math.Abs(bm.bmHeight);
        if (width <= 0 || height <= 0 || (long)width * height * 4 > MaxFormatBytes) return null;

        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // negative = top-down rows
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Gdi32.BI_RGB,
        };
        var pixels = new byte[width * height * 4];
        var hdc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
            {
                // BITMAPINFO = header followed by color table; 32bpp BI_RGB has none, but GDI may write
                // up to 3 masks after the header, so pass a buffer with room for them.
                var bmi = stackalloc byte[sizeof(Gdi32.BITMAPINFOHEADER) + 16];
                *(Gdi32.BITMAPINFOHEADER*)bmi = header;
                if (Gdi32.GetDIBits(hdc, hbm, 0, (uint)height, p, (Gdi32.BITMAPINFOHEADER*)bmi, Gdi32.DIB_RGB_COLORS) == 0)
                    return null;
            }
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return pixels;
    }

    /// <summary>Reads width/height from a PNG IHDR chunk.</summary>
    internal static bool TryPngSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature)) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        return width > 0 && height > 0;
    }
}

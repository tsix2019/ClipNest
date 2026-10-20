using System.Runtime.InteropServices;
using System.Text;
using ClipNest.Core;
using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>A fully materialized payload ready to be placed on the clipboard.</summary>
public sealed class ClipboardPayload
{
    public string? Text { get; init; }
    public string? Rtf { get; init; }
    public string? Html { get; init; }
    public byte[]? Png { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
}

/// <summary>Writes payloads to the clipboard, tagged with <see cref="ClipboardFormats.Internal"/>.</summary>
public sealed unsafe class ClipboardWriter(MessageWindow owner, ImageProcessor images)
{
    /// <summary>Places the payload on the clipboard. Returns false if the clipboard could not be opened.</summary>
    public bool Write(ClipboardPayload payload, bool plainText)
    {
        // Prepare everything (including PNG decoding) before taking the clipboard lock.
        byte[]? dib = null;
        if (!plainText && payload.Png is { } png)
        {
            try
            {
                var (bgra, w, h) = images.DecodeBgra(png);
                dib = BuildDib(bgra, w, h);
            }
            catch (Exception e)
            {
                Log.Error("Decode PNG for clipboard", e);
            }
        }

        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();

            if (payload.Text is { } text)
                SetUnicode(text);

            if (!plainText)
            {
                if (payload.Rtf is { } rtf) SetBytes(ClipboardFormats.Rtf, Encoding.Latin1.GetBytes(rtf + "\0"));
                if (payload.Html is { } html) SetBytes(ClipboardFormats.Html, Encoding.UTF8.GetBytes(html + "\0"));
                if (payload.Png is { } p) SetBytes(ClipboardFormats.Png, p);
                if (dib is not null) SetBytes(CF_DIB, dib);
                if (payload.Files is { Count: > 0 } files)
                {
                    SetBytes(CF_HDROP, BuildDropFiles(files));
                    SetBytes(ClipboardFormats.PreferredDropEffect, BitConverter.GetBytes(1u)); // DROPEFFECT_COPY
                }
            }
            else if (payload.Text is null && payload.Files is { Count: > 0 } files)
            {
                SetUnicode(string.Join("\r\n", files));
            }

            SetBytes(ClipboardFormats.Internal, [1]);
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private bool TryOpen()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner.Handle)) return true;
            Thread.Sleep(15);
        }
        Log.Warn("Clipboard busy; write failed");
        return false;
    }

    private static void SetUnicode(string text)
    {
        var normalized = text.Contains('\n') && !text.Contains("\r\n") ? text.Replace("\n", "\r\n") : text;
        var bytes = new byte[(normalized.Length + 1) * 2];
        MemoryMarshal.AsBytes(normalized.AsSpan()).CopyTo(bytes);
        SetBytes(CF_UNICODETEXT, bytes);
    }

    private static void SetBytes(uint format, ReadOnlySpan<byte> data)
    {
        var h = Kernel32.GlobalAlloc(Kernel32.GMEM_MOVEABLE, (nuint)Math.Max(1, data.Length));
        if (h == 0) return;
        var p = Kernel32.GlobalLock(h);
        if (p == null)
        {
            Kernel32.GlobalFree(h);
            return;
        }
        data.CopyTo(new Span<byte>(p, data.Length));
        Kernel32.GlobalUnlock(h);
        if (SetClipboardData(format, h) == 0) Kernel32.GlobalFree(h); // on success the system owns the memory
    }

    /// <summary>CF_DIB: BITMAPINFOHEADER + bottom-up 32-bit BGRA rows.</summary>
    internal static byte[] BuildDib(byte[] bgraTopDown, int width, int height)
    {
        var headerSize = sizeof(Gdi32.BITMAPINFOHEADER);
        var stride = width * 4;
        var result = new byte[headerSize + stride * height];
        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)headerSize,
            biWidth = width,
            biHeight = height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Gdi32.BI_RGB,
            biSizeImage = (uint)(stride * height),
        };
        MemoryMarshal.Write(result, in header);
        for (var y = 0; y < height; y++)
        {
            bgraTopDown.AsSpan(y * stride, stride).CopyTo(result.AsSpan(headerSize + (height - 1 - y) * stride, stride));
        }
        return result;
    }

    /// <summary>CF_HDROP: DROPFILES header followed by a double-null-terminated UTF-16 path list.</summary>
    internal static byte[] BuildDropFiles(IReadOnlyList<string> files)
    {
        const int headerSize = 20; // DWORD pFiles; POINT pt; BOOL fNC; BOOL fWide
        var list = string.Join('\0', files) + "\0\0";
        var result = new byte[headerSize + list.Length * 2];
        BitConverter.TryWriteBytes(result.AsSpan(0), headerSize);
        BitConverter.TryWriteBytes(result.AsSpan(16), 1); // fWide
        MemoryMarshal.AsBytes(list.AsSpan()).CopyTo(result.AsSpan(headerSize));
        return result;
    }
}

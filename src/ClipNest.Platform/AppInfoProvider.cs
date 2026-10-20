using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ClipNest.Core;
using ClipNest.Core.Abstractions;
using ClipNest.Platform.Native;

namespace ClipNest.Platform;

/// <summary>Resolves an executable to its display name, cached icon PNG and card header color.</summary>
public sealed unsafe class AppInfoProvider(string iconDirectory, IClipRepository repository, ImageProcessor images)
{
    private const int IconSize = 48;
    private readonly ConcurrentDictionary<string, AppInfo> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AppInfo? Resolve(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (_cache.TryGetValue(exePath, out var cached)) return cached;

        var info = repository.GetApp(exePath);
        if (info is null || (info.IconFile is not null && !File.Exists(info.IconFile)))
        {
            info = Build(exePath);
            if (info is not null) repository.UpsertApp(info);
        }
        if (info is not null) _cache[exePath] = info;
        return info;
    }

    private AppInfo? Build(string exePath)
    {
        try
        {
            var name = DisplayName(exePath);
            var color = ColorMath.NeutralHeader;
            string? iconFile = null;

            var pixels = ExtractIconPixels(exePath, IconSize);
            if (pixels is not null)
            {
                color = ColorMath.EnsureHeaderContrast(ColorMath.DominantColor(pixels));
                Directory.CreateDirectory(iconDirectory);
                iconFile = Path.Combine(iconDirectory, KeyFor(exePath) + ".png");
                File.WriteAllBytes(iconFile, images.EncodePng(pixels, IconSize, IconSize));
            }
            return new AppInfo(exePath, name, color, iconFile);
        }
        catch (Exception e)
        {
            Log.Error($"AppInfo for {exePath}", e);
            return new AppInfo(exePath, Path.GetFileNameWithoutExtension(exePath), ColorMath.NeutralHeader, null);
        }
    }

    public static string DisplayName(string exePath)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(exePath);
            var name = v.FileDescription?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40) name = v.ProductName?.Trim();
            if (!string.IsNullOrEmpty(name) && name.Length <= 40) return name;
        }
        catch (FileNotFoundException)
        {
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static string KeyFor(string path) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16];

    /// <summary>Extracts the exe's main icon as top-down BGRA pixels of <paramref name="size"/>².</summary>
    internal static byte[]? ExtractIconPixels(string exePath, int size)
    {
        nint hIcon;
        uint id;
        if (User32.PrivateExtractIconsW(exePath, 0, size, size, &hIcon, &id, 1, 0) == 0 || hIcon == 0) return null;
        try
        {
            if (!User32.GetIconInfo(hIcon, out var ii)) return null;
            try
            {
                return BitmapToBgra(ii.hbmColor, size);
            }
            finally
            {
                if (ii.hbmColor != 0) Gdi32.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != 0) Gdi32.DeleteObject(ii.hbmMask);
            }
        }
        finally
        {
            User32.DestroyIcon(hIcon);
        }
    }

    private static byte[]? BitmapToBgra(nint hbm, int size)
    {
        if (hbm == 0) return null;
        var header = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Gdi32.BI_RGB,
        };
        var pixels = new byte[size * size * 4];
        var hdc = User32.GetDC(0);
        try
        {
            var bmi = stackalloc byte[sizeof(Gdi32.BITMAPINFOHEADER) + 16];
            *(Gdi32.BITMAPINFOHEADER*)bmi = header;
            fixed (byte* p = pixels)
            {
                if (Gdi32.GetDIBits(hdc, hbm, 0, (uint)size, p, (Gdi32.BITMAPINFOHEADER*)bmi, Gdi32.DIB_RGB_COLORS) == 0) return null;
            }
        }
        finally
        {
            User32.ReleaseDC(0, hdc);
        }

        // Legacy icons without an alpha channel: treat every pixel as opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { hasAlpha = true; break; }
        }
        if (!hasAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }
        else
        {
            // GDI returns premultiplied BGRA for 32-bit icons; un-premultiply for PNG (straight alpha).
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a is 0 or 255) continue;
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }
        return pixels;
    }
}

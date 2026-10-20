using System.Runtime.InteropServices.WindowsRuntime;
using ClipNest.Core.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipNest.Platform;

/// <summary>
/// PNG encode/decode and thumbnails via the OS imaging stack (WIC through <c>Windows.Graphics.Imaging</c>).
/// The synchronous wrappers run the async work on the thread pool, so they are deadlock-free on any thread
/// (but still block the caller — avoid them on the UI thread).
/// </summary>
public sealed class ImageProcessor : IImageProcessor
{
    public byte[] EncodePng(byte[] bgraTopDown, int width, int height) =>
        Task.Run(() => EncodePngAsync(bgraTopDown, width, height)).GetAwaiter().GetResult();

    public byte[] CreateThumbnailPng(byte[] png, int maxSide) =>
        Task.Run(() => CreateThumbnailAsync(png, maxSide)).GetAwaiter().GetResult();

    public (byte[] Bgra, int Width, int Height) DecodeBgra(byte[] png) =>
        Task.Run(() => DecodeAsync(png, 0)).GetAwaiter().GetResult();

    /// <summary>Decodes and downsizes so the longest side is at most <paramref name="maxSide"/> (0 = original size).</summary>
    public (byte[] Bgra, int Width, int Height) DecodeBgra(byte[] png, int maxSide) =>
        Task.Run(() => DecodeAsync(png, maxSide)).GetAwaiter().GetResult();

    private static async Task<byte[]> EncodePngAsync(byte[] bgra, int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync();
        return await ReadAllAsync(stream);
    }

    private static async Task<byte[]> CreateThumbnailAsync(byte[] png, int maxSide)
    {
        var (bgra, w, h) = await DecodeAsync(png, maxSide);
        return await EncodePngAsync(bgra, w, h);
    }

    private static async Task<(byte[], int, int)> DecodeAsync(byte[] png, int maxSide)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(png.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);

        uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
        var transform = new BitmapTransform();
        if (maxSide > 0 && Math.Max(w, h) > maxSide)
        {
            var scale = (double)maxSide / Math.Max(w, h);
            w = Math.Max(1, (uint)Math.Round(w * scale));
            h = Math.Max(1, (uint)Math.Round(h * scale));
            transform.ScaledWidth = w;
            transform.ScaledHeight = h;
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        return (data.DetachPixelData(), (int)w, (int)h);
    }

    private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        var size = (uint)stream.Size;
        var buffer = new Windows.Storage.Streams.Buffer(size);
        stream.Seek(0);
        var read = await stream.ReadAsync(buffer, size, InputStreamOptions.None);
        return read.ToArray();
    }
}

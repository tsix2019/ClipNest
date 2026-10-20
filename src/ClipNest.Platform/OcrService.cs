using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ClipNest.Platform;

/// <summary>On-device text recognition with the Windows OCR engine (user profile languages).</summary>
public sealed partial class OcrService
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public bool IsAvailable => _engine is not null;

    /// <summary>Recognizes text in a PNG. Returns "" when nothing is found or OCR is unavailable.</summary>
    public async Task<string> RecognizeAsync(byte[] png)
    {
        if (_engine is null) return "";

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);

        var max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        uint w = decoder.PixelWidth, h = decoder.PixelHeight;
        if (Math.Max(w, h) > max)
        {
            var scale = (double)max / Math.Max(w, h);
            transform.ScaledWidth = Math.Max(1, (uint)(w * scale));
            transform.ScaledHeight = Math.Max(1, (uint)(h * scale));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await _engine.RecognizeAsync(bitmap);
        var lines = result.Lines.Select(l => JoinCjk(l.Text)).Where(t => t.Length > 0);
        return string.Join('\n', lines);
    }

    /// <summary>The engine separates CJK characters with spaces; remove them so "剪贴板" is searchable.</summary>
    internal static string JoinCjk(string text) => CjkSpaces().Replace(text.Trim(), "");

    [GeneratedRegex(@"(?<=[぀-ヿ㐀-鿿豈-﫿가-힯＀-￯])\s+(?=[぀-ヿ㐀-鿿豈-﫿가-힯＀-￯])")]
    private static partial Regex CjkSpaces();
}

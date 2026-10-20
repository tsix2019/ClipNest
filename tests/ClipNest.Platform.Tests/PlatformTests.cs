using System.Buffers.Binary;
using System.Text;
using ClipNest.Platform;

namespace ClipNest.Platform.Tests;

public class ImageTests
{
    private static readonly ImageProcessor Images = new();

    private static byte[] Gradient(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            px[i] = (byte)x; px[i + 1] = (byte)y; px[i + 2] = 128; px[i + 3] = 255;
        }
        return px;
    }

    [Fact]
    public void Encode_then_read_png_size()
    {
        var png = Images.EncodePng(Gradient(300, 120), 300, 120);
        Assert.True(ClipboardReader.TryPngSize(png, out var w, out var h));
        Assert.Equal((300, 120), (w, h));
    }

    [Fact]
    public void Png_roundtrip_keeps_pixels()
    {
        var src = Gradient(40, 30);
        var (bgra, w, h) = Images.DecodeBgra(Images.EncodePng(src, 40, 30));
        Assert.Equal((40, 30), (w, h));
        Assert.Equal(src, bgra);
    }

    [Fact]
    public void Thumbnail_fits_max_side_and_keeps_aspect()
    {
        var png = Images.EncodePng(Gradient(1200, 600), 1200, 600);
        var thumb = Images.CreateThumbnailPng(png, 360);
        Assert.True(ClipboardReader.TryPngSize(thumb, out var w, out var h));
        Assert.Equal((360, 180), (w, h));
    }

    [Fact]
    public void Png_size_rejects_garbage() => Assert.False(ClipboardReader.TryPngSize([1, 2, 3, 4], out _, out _));
}

public class ClipboardFormatTests
{
    [Fact]
    public void Dib_is_bottom_up_with_header()
    {
        // 1×2 image: top pixel red, bottom pixel blue (BGRA)
        byte[] topDown = [0, 0, 255, 255, 255, 0, 0, 255];
        var dib = ClipboardWriter.BuildDib(topDown, 1, 2);
        Assert.Equal(40 + 8, dib.Length);
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(dib));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)));  // positive height = bottom-up
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dib[40..44]);               // first row stored = bottom (blue)
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, dib[44..48]);
    }

    [Fact]
    public void Drop_files_layout()
    {
        var data = ClipboardWriter.BuildDropFiles([@"C:\a.txt", @"D:\文件.png"]);
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(data));        // pFiles
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16))); // fWide
        var list = Encoding.Unicode.GetString(data, 20, data.Length - 20);
        Assert.Equal("C:\\a.txt\0D:\\文件.png\0\0", list);
    }
}

public class TextExtractionTests
{
    [Theory]
    [InlineData("<html><head><title>  Hello &amp; World </title></head></html>", "Hello & World")]
    [InlineData("<meta property=\"og:title\" content=\"OG Title\"><title>Plain</title>", "OG Title")]
    [InlineData("<meta content='Reversed' property='og:title'>", "Reversed")]
    [InlineData("<TITLE>\n多行\n标题\n</TITLE>", "多行 标题")]
    [InlineData("<p>no title</p>", null)]
    [InlineData("<title>   </title>", null)]
    public void Extract_title(string html, string? expected) => Assert.Equal(expected, LinkTitleFetcher.ExtractTitle(html));

    [Fact]
    public void Long_titles_are_truncated()
    {
        var title = LinkTitleFetcher.ExtractTitle("<title>" + new string('x', 500) + "</title>")!;
        Assert.Equal(201, title.Length);
        Assert.EndsWith("…", title);
    }

    [Theory]
    [InlineData("剪 贴 板 历 史", "剪贴板历史")]
    [InlineData("Hello World", "Hello World")]
    [InlineData("发 票 INV 2026 编 号", "发票 INV 2026 编号")]
    [InlineData("  ク リ ッ プ  ", "クリップ")]
    public void Ocr_cjk_spaces_are_joined(string input, string expected) => Assert.Equal(expected, OcrService.JoinCjk(input));
}

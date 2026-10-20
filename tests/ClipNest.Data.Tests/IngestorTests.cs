using ClipNest.Core;
using ClipNest.Core.Abstractions;

namespace ClipNest.Data.Tests;

public sealed class IngestorTests : IDisposable
{
    private sealed class FakeImages : IImageProcessor
    {
        public int Calls;
        public byte[] CreateThumbnailPng(byte[] png, int maxSide)
        {
            Calls++;
            return [9];
        }
    }

    private readonly TestDb _t = new();
    private readonly FakeImages _images = new();
    private readonly ClipIngestor _ingestor;
    private static readonly DateTimeOffset T0 = TestDb.T0;

    public IngestorTests()
    {
        _ingestor = new ClipIngestor(_t.Repo, _t.Blobs, _images,
            path => new AppInfo(path, "Pretty " + Path.GetFileNameWithoutExtension(path), 0xFF112233, null));
    }

    public void Dispose() => _t.Dispose();

    [Fact]
    public void New_text()
    {
        var r = _ingestor.Ingest(new ClipboardSnapshot { Text = "hello\r\nworld", SourceAppPath = @"C:\n\notepad.exe" }, T0)!;
        Assert.True(r.IsNew);
        Assert.Equal(ClipKind.Text, r.Item.Kind);
        Assert.Equal("hello\nworld", r.Item.Text);
        Assert.Equal(11, r.Item.CharCount);
        Assert.Equal("Pretty notepad", r.Item.SourceAppName);
        Assert.NotNull(_t.Repo.Get(r.Item.Id));
    }

    [Fact]
    public void Duplicate_moves_existing_to_front()
    {
        var first = _ingestor.Ingest(new ClipboardSnapshot { Text = "dup" }, T0)!;
        _ingestor.Ingest(new ClipboardSnapshot { Text = "other" }, T0.AddMinutes(1));
        var again = _ingestor.Ingest(new ClipboardSnapshot { Text = "dup", SourceAppPath = @"C:\w\word.exe" }, T0.AddMinutes(2))!;

        Assert.False(again.IsNew);
        Assert.Equal(first.Item.Id, again.Item.Id);
        Assert.Equal(T0.AddMinutes(2), again.Item.CreatedAt);
        Assert.Equal(2, _t.Repo.Count());
        var top = _t.Repo.Query(new ClipNest.Core.Abstractions.ClipQuery(SearchQuery.Empty))[0];
        Assert.Equal(first.Item.Id, top.Id);
        Assert.Equal("Pretty word", top.SourceAppName);
    }

    [Fact]
    public void Large_image_writes_png_and_thumb()
    {
        var r = _ingestor.Ingest(new ClipboardSnapshot { Png = [1, 2, 3], ImageWidth = 1920, ImageHeight = 1080 }, T0)!;
        Assert.Equal(ClipKind.Image, r.Item.Kind);
        Assert.Equal(BlobFlags.Png | BlobFlags.Thumb, r.Item.Blobs);
        Assert.Equal(1920, r.Item.ImageWidth);
        Assert.Equal(new byte[] { 1, 2, 3 }, _t.Blobs.Read(r.Item.ContentHash, BlobFlags.Png));
        Assert.Equal(new byte[] { 9 }, _t.Blobs.Read(r.Item.ContentHash, BlobFlags.Thumb));
    }

    [Fact]
    public void Small_image_skips_thumb()
    {
        var r = _ingestor.Ingest(new ClipboardSnapshot { Png = [1], ImageWidth = 64, ImageHeight = 64 }, T0)!;
        Assert.Equal(BlobFlags.Png, r.Item.Blobs);
        Assert.Equal(0, _images.Calls);
    }

    [Fact]
    public void Rich_text_writes_blobs()
    {
        var r = _ingestor.Ingest(new ClipboardSnapshot { Text = "bold", Rtf = "{\\rtf1 \\b bold}", Html = "<b>bold</b>" }, T0)!;
        Assert.Equal(ClipKind.RichText, r.Item.Kind);
        Assert.Equal(BlobFlags.Rtf | BlobFlags.Html, r.Item.Blobs);
        Assert.Equal("<b>bold</b>", _t.Blobs.ReadText(r.Item.ContentHash, BlobFlags.Html));
    }

    [Fact]
    public void Files()
    {
        var r = _ingestor.Ingest(new ClipboardSnapshot { Files = [@"C:\a.txt", @"C:\b.png"] }, T0)!;
        Assert.Equal(ClipKind.File, r.Item.Kind);
        Assert.Equal(2, r.Item.FileCount);
        Assert.Equal([@"C:\a.txt", @"C:\b.png"], r.Item.FilePaths);
    }

    [Fact]
    public void Color_and_link_are_normalized()
    {
        Assert.Equal("#00AA00", _ingestor.Ingest(new ClipboardSnapshot { Text = " #00aa00 " }, T0)!.Item.Text);
        Assert.Equal("https://a.com/x", _ingestor.Ingest(new ClipboardSnapshot { Text = " https://a.com/x\n" }, T0)!.Item.Text);
    }

    [Fact]
    public void Nothing_usable_returns_null()
    {
        Assert.Null(_ingestor.Ingest(new ClipboardSnapshot(), T0));
        Assert.Null(_ingestor.Ingest(new ClipboardSnapshot { Text = " \r\n\t" }, T0));
    }
}

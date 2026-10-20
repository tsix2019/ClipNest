using ClipNest.Core;

namespace ClipNest.Data.Tests;

/// <summary>A throwaway data folder with a fresh database.</summary>
public sealed class TestDb : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "clipnest-tests", Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Root, "clipnest.db");
    public ClipRepository Repo { get; }
    public BlobStore Blobs { get; }

    public static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public TestDb()
    {
        Directory.CreateDirectory(Root);
        Repo = ClipRepository.Open(DbPath, out _);
        Blobs = new BlobStore(Root);
    }

    public ClipItem AddText(string text, DateTimeOffset? at = null, long? pinboard = null, string? app = null)
    {
        var item = new ClipItem
        {
            Kind = ClipKind.Text,
            Text = text,
            ContentHash = ContentHasher.ForText(ClipKind.Text, text),
            CreatedAt = at ?? T0,
            CharCount = text.Length,
            PinboardId = pinboard,
            SourceAppName = app,
            SourceAppPath = app is null ? null : $@"C:\Apps\{app}.exe",
        };
        Repo.Insert(item);
        return item;
    }

    public void Dispose()
    {
        Repo.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

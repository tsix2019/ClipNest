using System.IO.Compression;
using ClipNest.Core;

namespace ClipNest.Data.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly TestDb _source = new();
    private readonly string _target = Path.Combine(Path.GetTempPath(), "clipnest-tests", "restore-" + Guid.NewGuid().ToString("N"));
    private readonly string _zip = Path.Combine(Path.GetTempPath(), $"clipnest-backup-{Guid.NewGuid():N}.zip");

    public void Dispose()
    {
        _source.Dispose();
        try { Directory.Delete(_target, recursive: true); } catch (IOException) { }
        File.Delete(_zip);
    }

    [Fact]
    public void Export_then_import_restores_items_and_blobs()
    {
        var pb = _source.Repo.AddPinboard("Keep", 1);
        _source.AddText("backed up text", pinboard: pb);
        _source.Blobs.Write("abc", BlobFlags.Png, [1, 2, 3]);

        BackupService.Export(_source.Repo, _source.Root, _zip);
        using (var zip = ZipFile.OpenRead(_zip))
        {
            Assert.NotNull(zip.GetEntry("clipnest.db"));
            Assert.NotNull(zip.GetEntry("blobs/abc.png"));
        }

        // An existing installation with different content gets replaced; its db is kept aside.
        Directory.CreateDirectory(_target);
        using (var existing = ClipRepository.Open(Path.Combine(_target, "clipnest.db"), out _)) existing.Insert(new ClipItem { Kind = ClipKind.Text, Text = "old", ContentHash = "old", CreatedAt = DateTimeOffset.Now });

        BackupService.StageImport(_zip, _target);
        Assert.True(BackupService.HasPendingImport(_target));
        Assert.True(BackupService.ApplyPendingImport(_target));
        Assert.False(BackupService.HasPendingImport(_target));

        using var restored = ClipRepository.Open(Path.Combine(_target, "clipnest.db"), out var recovered);
        Assert.False(recovered);
        Assert.Equal(1, restored.Count());
        Assert.Equal("Keep", restored.GetPinboards().Single().Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_target, "blobs", "abc.png")));
        Assert.Single(Directory.GetFiles(_target, "clipnest.before-import-*.db"));
    }

    [Fact]
    public void Rejects_foreign_zip()
    {
        using (var zip = ZipFile.Open(_zip, ZipArchiveMode.Create)) zip.CreateEntry("hello.txt");
        Assert.Throws<InvalidDataException>(() => BackupService.StageImport(_zip, _target));
    }

    [Fact]
    public void No_pending_import_is_noop() => Assert.False(BackupService.ApplyPendingImport(_target));
}

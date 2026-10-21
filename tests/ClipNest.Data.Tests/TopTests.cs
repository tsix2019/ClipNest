using ClipNest.Core;
using ClipNest.Core.Abstractions;
using ClipNest.Data.Sqlite;

namespace ClipNest.Data.Tests;

/// <summary>"Pin to top": top items lead every list and survive retention.</summary>
public sealed class TopTests : IDisposable
{
    private readonly TestDb _t = new();
    private ClipRepository Repo => _t.Repo;
    private static readonly DateTimeOffset T0 = TestDb.T0;

    public void Dispose() => _t.Dispose();

    private IReadOnlyList<ClipItem> All(long? pinboard = null) =>
        Repo.Query(new ClipQuery(SearchQuery.Parse("", T0), pinboard));

    [Fact]
    public void Top_items_come_first_most_recently_pinned_first()
    {
        var old = _t.AddText("old", T0.AddHours(-3));
        var older = _t.AddText("older", T0.AddHours(-4));
        _t.AddText("new", T0);
        Repo.SetTop(older.Id, 100);
        Repo.SetTop(old.Id, 200);

        Assert.Equal(["old", "older", "new"], All().Select(i => i.Text));
        Assert.Equal(200, Repo.Get(old.Id)!.TopAt);
        Assert.True(Repo.Get(old.Id)!.IsTop);

        Repo.SetTop(old.Id, 0);
        Assert.Equal(["older", "new", "old"], All().Select(i => i.Text));
    }

    [Fact]
    public void Retention_keeps_top_items()
    {
        var top = _t.AddText("keep", T0.AddDays(-90));
        _t.AddText("drop", T0.AddDays(-90));
        Repo.SetTop(top.Id, 1);

        Repo.PurgeOlderThan(T0.AddDays(-30));
        Assert.Equal(["keep"], All().Select(i => i.Text));

        _t.AddText("a", T0);
        Repo.EnforceCap(1);
        Assert.Contains("keep", All().Select(i => i.Text));

        Repo.ClearHistory();
        Assert.Equal(["keep"], All().Select(i => i.Text));
    }

    [Fact]
    public void Restore_keeps_top()
    {
        var item = _t.AddText("x");
        Repo.SetTop(item.Id, 5);
        item = Repo.Get(item.Id)!;
        Repo.Delete(item.Id);
        Repo.Restore(item);
        Assert.Equal(5, Repo.Get(item.Id)!.TopAt);
    }

    [Fact]
    public void Version_1_database_is_migrated()
    {
        var path = Path.Combine(_t.Root, "v1.db");
        using (var conn = SqliteConnection.Open(path))
        {
            conn.Execute("CREATE TABLE schema_version(v INTEGER NOT NULL); INSERT INTO schema_version(v) VALUES (1);");
            conn.Execute("""
                CREATE TABLE pinboards(id INTEGER PRIMARY KEY, name TEXT NOT NULL, color INTEGER NOT NULL DEFAULT 0, sort INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE items(id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, title TEXT, text TEXT, hash TEXT NOT NULL UNIQUE,
                    app_path TEXT, app_name TEXT, created_at INTEGER NOT NULL, pinboard_id INTEGER, char_count INTEGER NOT NULL DEFAULT 0,
                    img_w INTEGER, img_h INTEGER, blobs INTEGER NOT NULL DEFAULT 0, ocr_text TEXT, link_title TEXT, file_count INTEGER);
                CREATE TABLE apps(path TEXT PRIMARY KEY, name TEXT NOT NULL, color INTEGER NOT NULL, icon_file TEXT);
                INSERT INTO items(kind, text, hash, created_at) VALUES (0, 'legacy', 'h1', 0);
                """);
        }
        using var repo = ClipRepository.Open(path, out _);
        var item = Assert.Single(repo.Query(new ClipQuery(SearchQuery.Parse("", T0))));
        Assert.Equal("legacy", item.Text);
        Assert.False(item.IsTop);
    }
}

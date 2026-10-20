using ClipNest.Core;
using ClipNest.Core.Abstractions;
using ClipNest.Data.Sqlite;

namespace ClipNest.Data.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly TestDb _t = new();
    private ClipRepository Repo => _t.Repo;
    private static readonly DateTimeOffset T0 = TestDb.T0;

    public void Dispose() => _t.Dispose();

    private IReadOnlyList<ClipItem> Search(string text, long? pinboard = null) =>
        Repo.Query(new ClipQuery(SearchQuery.Parse(text, T0), pinboard));

    [Fact]
    public void Insert_and_get_roundtrip_all_columns()
    {
        var item = new ClipItem
        {
            Kind = ClipKind.Image, Title = "截图", Text = null, ContentHash = new string('a', 64),
            SourceAppPath = @"C:\x\snip.exe", SourceAppName = "Snip", CreatedAt = T0,
            CharCount = 0, ImageWidth = 1920, ImageHeight = 1080, Blobs = BlobFlags.Png | BlobFlags.Thumb,
            OcrText = "hello", LinkTitle = null, FileCount = null,
        };
        var id = Repo.Insert(item);
        Assert.True(id > 0);

        var got = Repo.Get(id)!;
        Assert.Equal(ClipKind.Image, got.Kind);
        Assert.Equal("截图", got.Title);
        Assert.Null(got.Text);
        Assert.Equal(item.ContentHash, got.ContentHash);
        Assert.Equal("Snip", got.SourceAppName);
        Assert.Equal(T0, got.CreatedAt);
        Assert.Equal(1920, got.ImageWidth);
        Assert.Equal(1080, got.ImageHeight);
        Assert.Equal(BlobFlags.Png | BlobFlags.Thumb, got.Blobs);
        Assert.Equal("hello", got.OcrText);
        Assert.Null(got.PinboardId);
        Assert.Null(got.FileCount);
    }

    [Fact]
    public void Duplicate_hash_rejected()
    {
        _t.AddText("same");
        Assert.Throws<SqliteException>(() => _t.AddText("same"));
    }

    [Fact]
    public void Find_by_hash()
    {
        var a = _t.AddText("alpha");
        Assert.Equal(a.Id, Repo.FindByHash(a.ContentHash)!.Id);
        Assert.Null(Repo.FindByHash("nope"));
    }

    [Fact]
    public void Query_is_newest_first_and_touch_moves_to_front()
    {
        var a = _t.AddText("first", T0);
        var b = _t.AddText("second", T0.AddMinutes(1));
        Assert.Equal([b.Id, a.Id], Search("").Select(i => i.Id));

        Repo.Touch(a.Id, T0.AddMinutes(2), @"C:\n.exe", "Notepad");
        var list = Search("");
        Assert.Equal([a.Id, b.Id], list.Select(i => i.Id));
        Assert.Equal("Notepad", list[0].SourceAppName);
    }

    [Fact]
    public void Search_english_and_chinese()
    {
        _t.AddText("The quick brown fox", T0);
        _t.AddText("这是剪贴板历史记录", T0.AddSeconds(1));
        _t.AddText("另一段内容", T0.AddSeconds(2));

        Assert.Single(Search("QUICK"));
        Assert.Single(Search("剪贴板"));
        Assert.Single(Search("剪贴"));          // 2 chars → LIKE fallback
        Assert.Single(Search("fox quick"));     // AND of terms, any order
        Assert.Empty(Search("fox 剪贴板"));
        Assert.Equal(3, Search("").Count);
    }

    [Fact]
    public void Search_special_characters_are_literal()
    {
        _t.AddText("100% done_now", T0);
        _t.AddText("1000 donenow", T0.AddSeconds(1));
        Assert.Single(Search("0%"));
        Assert.Single(Search("e_n"));
        Assert.Empty(Search("\"quoted\" OR"));   // FTS operators/quotes are literal: no throw, no match
    }

    [Fact]
    public void Filters_kind_app_date()
    {
        _t.AddText("old note", T0.AddDays(-10), app: "Notepad");
        _t.AddText("new note", T0, app: "Chrome");
        Repo.Insert(new ClipItem { Kind = ClipKind.Link, Text = "https://a.com", ContentHash = "h-link", CreatedAt = T0, SourceAppName = "Chrome" });

        Assert.Single(Search("type:link"));
        Assert.Equal(2, Search("app:chrome").Count);
        Assert.Single(Search("note app:chrome"));
        Assert.Equal(2, Search("date:7d").Count);
    }

    [Fact]
    public void Pinboard_view_shows_only_its_items_history_shows_all()
    {
        var pb = Repo.AddPinboard("Work", 2);
        _t.AddText("pinned", T0, pinboard: pb);
        _t.AddText("loose", T0.AddSeconds(1));

        Assert.Single(Search("", pb));
        Assert.Equal(2, Search("").Count);
        Assert.Single(Search("pin:work"));
        Assert.Equal(1, Repo.Count(pb));
        Assert.Equal(2, Repo.Count());
    }

    [Fact]
    public void Purge_keeps_pinned_and_recent()
    {
        var pb = Repo.AddPinboard("Keep", 0);
        var oldPinned = _t.AddText("old pinned", T0.AddDays(-60), pinboard: pb);
        var oldLoose = _t.AddText("old loose", T0.AddDays(-60));
        _t.AddText("recent", T0);

        var removed = Repo.PurgeOlderThan(T0.AddMonths(-1));
        Assert.Equal([oldLoose.ContentHash], removed);
        Assert.NotNull(Repo.Get(oldPinned.Id));
        Assert.Equal(2, Repo.Count());
    }

    [Fact]
    public void Enforce_cap_keeps_newest_unpinned()
    {
        var pb = Repo.AddPinboard("P", 0);
        _t.AddText("p", T0.AddDays(-100), pinboard: pb);
        _t.AddText("a", T0.AddSeconds(1));
        var b = _t.AddText("b", T0.AddSeconds(2));
        var c = _t.AddText("c", T0.AddSeconds(3));

        var removed = Repo.EnforceCap(2);
        Assert.Single(removed);
        Assert.Equal(["c", "b", "p"], Search("").Select(i => i.Text));
        Assert.NotNull(Repo.Get(b.Id));
        Assert.NotNull(Repo.Get(c.Id));
    }

    [Fact]
    public void Clear_history_keeps_pinned()
    {
        var pb = Repo.AddPinboard("P", 0);
        _t.AddText("p", T0, pinboard: pb);
        _t.AddText("x", T0);
        Assert.Single(Repo.ClearHistory());
        Assert.Equal(1, Repo.Count());
    }

    [Fact]
    public void Delete_pinboard_cascades_and_returns_hashes()
    {
        var pb = Repo.AddPinboard("Gone", 0);
        var a = _t.AddText("a", T0, pinboard: pb);
        _t.AddText("b", T0);
        Assert.Equal([a.ContentHash], Repo.DeletePinboard(pb));
        Assert.Empty(Repo.GetPinboards());
        Assert.Equal(1, Repo.Count());
        Assert.Empty(Search("a"));
    }

    [Fact]
    public void Pinboards_order_and_update()
    {
        var a = Repo.AddPinboard("A", 0);
        var b = Repo.AddPinboard("B", 1);
        Assert.Equal(["A", "B"], Repo.GetPinboards().Select(p => p.Name));
        Repo.ReorderPinboards([b, a]);
        Assert.Equal(["B", "A"], Repo.GetPinboards().Select(p => p.Name));
        Repo.UpdatePinboard(Repo.GetPinboards()[0] with { Name = "Bee", ColorIndex = 5 });
        Assert.Equal(("Bee", 5), (Repo.GetPinboards()[0].Name, Repo.GetPinboards()[0].ColorIndex));
    }

    [Fact]
    public void Paging()
    {
        for (var i = 0; i < 25; i++) _t.AddText("item " + i, T0.AddSeconds(i));
        var page1 = Repo.Query(new ClipQuery(SearchQuery.Empty, Limit: 10));
        var page3 = Repo.Query(new ClipQuery(SearchQuery.Empty, Offset: 20, Limit: 10));
        Assert.Equal(10, page1.Count);
        Assert.Equal("item 24", page1[0].Text);
        Assert.Equal(5, page3.Count);
        Assert.Equal("item 0", page3[^1].Text);
    }

    [Fact]
    public void Delete_and_restore_keeps_id_and_search()
    {
        var a = _t.AddText("restore me");
        Repo.Delete(a.Id);
        Assert.Null(Repo.Get(a.Id));
        Assert.Empty(Search("restore"));
        Repo.Restore(a);
        Assert.Equal("restore me", Repo.Get(a.Id)!.Text);
        Assert.Single(Search("restore"));
    }

    [Fact]
    public void Rename_and_search_by_title()
    {
        var a = _t.AddText("body text");
        Repo.Rename(a.Id, "  My Snippet ");
        Assert.Equal("My Snippet", Repo.Get(a.Id)!.Title);
        Assert.Single(Search("snippet"));
        Repo.Rename(a.Id, "  ");
        Assert.Null(Repo.Get(a.Id)!.Title);
    }

    [Fact]
    public void Update_text_rehashes_and_merges_duplicates()
    {
        var a = _t.AddText("draft");
        var b = _t.AddText("final");
        var hash = ContentHasher.ForText(ClipKind.Text, "final");
        Repo.UpdateText(a.Id, ClipKind.Text, "final", hash, 5);
        Assert.Null(Repo.Get(b.Id));
        Assert.Equal("final", Repo.Get(a.Id)!.Text);
        Assert.Single(Search("final"));
    }

    [Fact]
    public void Ocr_and_link_title_are_searchable()
    {
        var img = new ClipItem { Kind = ClipKind.Image, ContentHash = "img", CreatedAt = T0 };
        Repo.Insert(img);
        Assert.Single(Repo.ImagesWithoutOcr(10));
        Repo.SetOcrText(img.Id, "发票编号 INV-2026");
        Assert.Empty(Repo.ImagesWithoutOcr(10));
        Assert.Single(Search("发票编号"));
        Assert.Single(Search("INV-2026"));

        var link = new ClipItem { Kind = ClipKind.Link, Text = "https://x.com", ContentHash = "lnk", CreatedAt = T0 };
        Repo.Insert(link);
        Repo.SetLinkTitle(link.Id, "Example Domain");
        Assert.Single(Search("example"));
    }

    [Fact]
    public void Apps_upsert_case_insensitive()
    {
        Repo.UpsertApp(new AppInfo(@"C:\A\Code.exe", "VS Code", 0xFF007ACCu, @"C:\i.png"));
        var got = Repo.GetApp(@"c:\a\code.EXE")!;
        Assert.Equal("VS Code", got.Name);
        Assert.Equal(0xFF007ACCu, got.Color);
        Repo.UpsertApp(got with { Name = "Code" });
        Assert.Equal("Code", Repo.GetApp(@"C:\A\Code.exe")!.Name);
    }

    [Fact]
    public void Recent_app_names()
    {
        _t.AddText("1", T0, app: "Old");
        _t.AddText("2", T0.AddMinutes(1), app: "New");
        Assert.Equal(["New", "Old"], Repo.RecentAppNames(5));
    }

    [Fact]
    public void Reopen_is_idempotent()
    {
        _t.AddText("persist");
        _t.Repo.Dispose();
        using var again = ClipRepository.Open(_t.DbPath, out var recovered);
        Assert.False(recovered);
        Assert.Equal(1, again.Count());
    }

    [Fact]
    public void Corrupt_file_is_set_aside()
    {
        var dir = Path.Combine(_t.Root, "corrupt");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clipnest.db");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x42, 8192).ToArray());

        using var repo = ClipRepository.Open(path, out var recovered);
        Assert.True(recovered);
        Assert.Equal(0, repo.Count());
        Assert.Single(Directory.GetFiles(dir, "clipnest.corrupt-*.db"));
    }
}

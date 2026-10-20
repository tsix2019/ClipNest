using System.Text;
using ClipNest.Core;
using ClipNest.Core.Abstractions;
using ClipNest.Data.Sqlite;

namespace ClipNest.Data;

/// <summary>
/// SQLite-backed <see cref="IClipRepository"/>. A single connection is shared and every call is serialized
/// with a lock; individual operations take well under a millisecond, so contention is negligible.
/// </summary>
public sealed class ClipRepository : IClipRepository, IDisposable
{
    private const string Columns =
        "id, kind, title, text, hash, app_path, app_name, created_at, pinboard_id, char_count, img_w, img_h, blobs, ocr_text, link_title, file_count";

    private readonly SqliteConnection _db;
    private readonly Lock _lock = new();

    public ClipRepository(SqliteConnection connection) => _db = connection;

    public static ClipRepository Open(string path, out bool recoveredFromCorruption)
    {
        var (conn, recovered) = ClipDatabase.Open(path);
        recoveredFromCorruption = recovered;
        return new ClipRepository(conn);
    }

    // ---------- items ----------

    public ClipItem? Get(long id)
    {
        lock (_lock) return Single($"SELECT {Columns} FROM items WHERE id = ?", id);
    }

    public ClipItem? FindByHash(string hash)
    {
        lock (_lock) return Single($"SELECT {Columns} FROM items WHERE hash = ?", hash);
    }

    public long Insert(ClipItem item)
    {
        lock (_lock)
        {
            _db.Execute(
                "INSERT INTO items(kind, title, text, hash, app_path, app_name, created_at, pinboard_id, char_count, img_w, img_h, blobs, ocr_text, link_title, file_count) " +
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                ItemArgs(item));
            item.Id = _db.LastInsertRowId;
            return item.Id;
        }
    }

    public void Restore(ClipItem item)
    {
        lock (_lock)
        {
            var args = new object?[] { item.Id }.Concat(ItemArgs(item)).ToArray();
            _db.Execute(
                "INSERT OR REPLACE INTO items(id, kind, title, text, hash, app_path, app_name, created_at, pinboard_id, char_count, img_w, img_h, blobs, ocr_text, link_title, file_count) " +
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                args);
        }
    }

    private static object?[] ItemArgs(ClipItem i) =>
    [
        (int)i.Kind, i.Title, i.Text, i.ContentHash, i.SourceAppPath, i.SourceAppName, i.CreatedAt,
        i.PinboardId, i.CharCount, i.ImageWidth, i.ImageHeight, (int)i.Blobs, i.OcrText, i.LinkTitle, i.FileCount,
    ];

    public void Touch(long id, DateTimeOffset at, string? appPath, string? appName)
    {
        lock (_lock)
            _db.Execute("UPDATE items SET created_at = ?, app_path = COALESCE(?, app_path), app_name = COALESCE(?, app_name) WHERE id = ?",
                at, appPath, appName, id);
    }

    public IReadOnlyList<ClipItem> Query(ClipQuery query)
    {
        var where = new List<string>();
        var args = new List<object?>();
        var search = query.Search;
        var from = "items";

        if (query.PinboardId is { } pin)
        {
            where.Add("items.pinboard_id = ?");
            args.Add(pin);
        }

        // Every whitespace-separated term must match (AND). Terms of 3+ chars use the trigram index,
        // shorter ones fall back to LIKE because trigram cannot match them.
        var terms = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var ftsTerms = terms.Where(UseFts).ToList();
        if (ftsTerms.Count > 0)
        {
            from = "items JOIN items_fts ON items_fts.rowid = items.id";
            where.Add("items_fts MATCH ?");
            args.Add(string.Join(' ', ftsTerms.Select(FtsPhrase)));
        }
        foreach (var term in terms.Where(t => !UseFts(t)))
        {
            where.Add("(items.text LIKE ? ESCAPE '\\' OR items.title LIKE ? ESCAPE '\\' OR items.link_title LIKE ? ESCAPE '\\' OR items.ocr_text LIKE ? ESCAPE '\\')");
            var like = "%" + EscapeLike(term) + "%";
            args.Add(like);
            args.Add(like);
            args.Add(like);
            args.Add(like);
        }

        if (search.Kind is { } kind)
        {
            where.Add("items.kind = ?");
            args.Add((int)kind);
        }
        if (!string.IsNullOrWhiteSpace(search.App))
        {
            where.Add("(items.app_name LIKE ? ESCAPE '\\' OR items.app_path LIKE ? ESCAPE '\\')");
            var like = "%" + EscapeLike(search.App) + "%";
            args.Add(like);
            args.Add(like);
        }
        if (search.Since is { } since)
        {
            where.Add("items.created_at >= ?");
            args.Add(since);
        }
        if (search.Until is { } until)
        {
            where.Add("items.created_at < ?");
            args.Add(until);
        }
        if (!string.IsNullOrWhiteSpace(search.Pinboard))
        {
            where.Add("items.pinboard_id IN (SELECT id FROM pinboards WHERE name LIKE ? ESCAPE '\\')");
            args.Add("%" + EscapeLike(search.Pinboard) + "%");
        }

        var sql = new StringBuilder($"SELECT {Prefixed()} FROM {from}");
        if (where.Count > 0) sql.Append(" WHERE ").AppendJoin(" AND ", where);
        sql.Append(" ORDER BY items.created_at DESC, items.id DESC LIMIT ? OFFSET ?");
        args.Add(query.Limit);
        args.Add(query.Offset);

        lock (_lock) return Many(sql.ToString(), args.ToArray());
    }

    public int Count(long? pinboardId = null)
    {
        lock (_lock)
        {
            return pinboardId is { } id
                ? (int)_db.Scalar<long>("SELECT COUNT(*) FROM items WHERE pinboard_id = ?", id)
                : (int)_db.Scalar<long>("SELECT COUNT(*) FROM items");
        }
    }

    public void Delete(long id)
    {
        lock (_lock) _db.Execute("DELETE FROM items WHERE id = ?", id);
    }

    public void Rename(long id, string? title)
    {
        lock (_lock) _db.Execute("UPDATE items SET title = ? WHERE id = ?", string.IsNullOrWhiteSpace(title) ? null : title.Trim(), id);
    }

    public void UpdateText(long id, ClipKind kind, string text, string hash, int charCount)
    {
        lock (_lock)
        {
            _db.InTransaction(() =>
            {
                // Editing can make an item identical to another one: the edited item wins.
                _db.Execute("DELETE FROM items WHERE hash = ? AND id <> ?", hash, id);
                _db.Execute("UPDATE items SET kind = ?, text = ?, hash = ?, char_count = ?, blobs = blobs & ? WHERE id = ?",
                    (int)kind, text, hash, charCount, (int)~(BlobFlags.Rtf | BlobFlags.Html), id);
            });
        }
    }

    public void SetPinboard(long id, long? pinboardId)
    {
        lock (_lock) _db.Execute("UPDATE items SET pinboard_id = ? WHERE id = ?", pinboardId, id);
    }

    public void SetOcrText(long id, string text)
    {
        lock (_lock) _db.Execute("UPDATE items SET ocr_text = ? WHERE id = ?", text, id);
    }

    public void SetLinkTitle(long id, string title)
    {
        lock (_lock) _db.Execute("UPDATE items SET link_title = ? WHERE id = ?", title, id);
    }

    public IReadOnlyList<ClipItem> ImagesWithoutOcr(int limit)
    {
        lock (_lock)
            return Many($"SELECT {Columns} FROM items WHERE kind = ? AND ocr_text IS NULL ORDER BY created_at DESC LIMIT ?",
                (int)ClipKind.Image, limit);
    }

    public IReadOnlyList<string> PurgeOlderThan(DateTimeOffset cutoff) =>
        DeleteWhere("pinboard_id IS NULL AND created_at < ?", cutoff);

    public IReadOnlyList<string> EnforceCap(int max) =>
        DeleteWhere("pinboard_id IS NULL AND id NOT IN (SELECT id FROM items WHERE pinboard_id IS NULL ORDER BY created_at DESC, id DESC LIMIT ?)", max);

    public IReadOnlyList<string> ClearHistory() => DeleteWhere("pinboard_id IS NULL");

    private List<string> DeleteWhere(string condition, params object?[] args)
    {
        lock (_lock)
        {
            return _db.InTransaction(() =>
            {
                var hashes = Strings($"SELECT hash FROM items WHERE {condition}", args);
                if (hashes.Count > 0) _db.Execute($"DELETE FROM items WHERE {condition}", args);
                return hashes;
            });
        }
    }

    // ---------- pinboards ----------

    public IReadOnlyList<Pinboard> GetPinboards()
    {
        lock (_lock)
        {
            var list = new List<Pinboard>();
            var stmt = _db.Cached("SELECT id, name, color, sort FROM pinboards ORDER BY sort, id");
            try
            {
                stmt.Bind();
                while (stmt.Step())
                    list.Add(new Pinboard(stmt.GetInt64(0), stmt.GetString(1) ?? "", stmt.GetInt32(2), stmt.GetInt32(3)));
            }
            finally
            {
                stmt.Reset();
            }
            return list;
        }
    }

    public long AddPinboard(string name, int colorIndex)
    {
        lock (_lock)
        {
            var sort = _db.Scalar<long>("SELECT COALESCE(MAX(sort), -1) + 1 FROM pinboards");
            _db.Execute("INSERT INTO pinboards(name, color, sort) VALUES (?,?,?)", name.Trim(), colorIndex, sort);
            return _db.LastInsertRowId;
        }
    }

    public void UpdatePinboard(Pinboard pinboard)
    {
        lock (_lock)
            _db.Execute("UPDATE pinboards SET name = ?, color = ?, sort = ? WHERE id = ?",
                pinboard.Name.Trim(), pinboard.ColorIndex, pinboard.SortOrder, pinboard.Id);
    }

    public IReadOnlyList<string> DeletePinboard(long id)
    {
        lock (_lock)
        {
            return _db.InTransaction(() =>
            {
                var hashes = Strings("SELECT hash FROM items WHERE pinboard_id = ?", id);
                _db.Execute("DELETE FROM items WHERE pinboard_id = ?", id);
                _db.Execute("DELETE FROM pinboards WHERE id = ?", id);
                return hashes;
            });
        }
    }

    public void ReorderPinboards(IReadOnlyList<long> orderedIds)
    {
        lock (_lock)
        {
            _db.InTransaction(() =>
            {
                for (var i = 0; i < orderedIds.Count; i++)
                    _db.Execute("UPDATE pinboards SET sort = ? WHERE id = ?", i, orderedIds[i]);
            });
        }
    }

    // ---------- apps ----------

    public AppInfo? GetApp(string path)
    {
        lock (_lock)
        {
            var stmt = _db.Cached("SELECT path, name, color, icon_file FROM apps WHERE path = ?");
            try
            {
                stmt.Bind(path);
                return stmt.Step()
                    ? new AppInfo(stmt.GetString(0)!, stmt.GetString(1)!, (uint)stmt.GetInt64(2), stmt.GetString(3))
                    : null;
            }
            finally
            {
                stmt.Reset();
            }
        }
    }

    public void UpsertApp(AppInfo app)
    {
        lock (_lock)
            _db.Execute("INSERT OR REPLACE INTO apps(path, name, color, icon_file) VALUES (?,?,?,?)",
                app.Path, app.Name, (long)app.Color, app.IconFile);
    }

    public IReadOnlyList<string> RecentAppNames(int limit)
    {
        lock (_lock)
            return Strings("SELECT app_name FROM items WHERE app_name IS NOT NULL GROUP BY app_name ORDER BY MAX(created_at) DESC LIMIT ?", limit);
    }

    public bool IsHashReferenced(string hash)
    {
        lock (_lock) return _db.Scalar<long>("SELECT EXISTS(SELECT 1 FROM items WHERE hash = ?)", hash) != 0;
    }

    // ---------- helpers ----------

    /// <summary>Trigram FTS needs at least 3 characters per term; shorter input uses LIKE.</summary>
    internal static bool UseFts(string text) => text.Length >= 3;

    /// <summary>Quotes input as a single FTS5 phrase so punctuation and operators are matched literally.</summary>
    internal static string FtsPhrase(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    internal static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Prefixed() =>
        string.Join(", ", Columns.Split(", ").Select(c => "items." + c));

    private ClipItem? Single(string sql, params object?[] args)
    {
        var stmt = _db.Cached(sql);
        try
        {
            stmt.Bind(args);
            return stmt.Step() ? Read(stmt) : null;
        }
        finally
        {
            stmt.Reset();
        }
    }

    private List<ClipItem> Many(string sql, params object?[] args)
    {
        var list = new List<ClipItem>();
        var stmt = _db.Cached(sql);
        try
        {
            stmt.Bind(args);
            while (stmt.Step()) list.Add(Read(stmt));
        }
        finally
        {
            stmt.Reset();
        }
        return list;
    }

    private List<string> Strings(string sql, params object?[] args)
    {
        var list = new List<string>();
        var stmt = _db.Cached(sql);
        try
        {
            stmt.Bind(args);
            while (stmt.Step())
            {
                var s = stmt.GetString(0);
                if (s is not null) list.Add(s);
            }
        }
        finally
        {
            stmt.Reset();
        }
        return list;
    }

    private static ClipItem Read(SqliteStatement s) => new()
    {
        Id = s.GetInt64(0),
        Kind = (ClipKind)s.GetInt32(1),
        Title = s.GetString(2),
        Text = s.GetString(3),
        ContentHash = s.GetString(4) ?? "",
        SourceAppPath = s.GetString(5),
        SourceAppName = s.GetString(6),
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(s.GetInt64(7)).ToLocalTime(),
        PinboardId = s.GetInt64OrNull(8),
        CharCount = s.GetInt32(9),
        ImageWidth = s.GetInt32OrNull(10),
        ImageHeight = s.GetInt32OrNull(11),
        Blobs = (BlobFlags)s.GetInt32(12),
        OcrText = s.GetString(13),
        LinkTitle = s.GetString(14),
        FileCount = s.GetInt32OrNull(15),
    };

    public void Dispose() => _db.Dispose();
}

using ClipNest.Data.Sqlite;

namespace ClipNest.Data;

/// <summary>Opens <c>clipnest.db</c>, applies migrations and recovers from corruption.</summary>
public static class ClipDatabase
{
    public const int SchemaVersion = 1;

    /// <summary>Opens (creating if needed) the database. Returns the connection and whether a corrupt file was set aside.</summary>
    public static (SqliteConnection Connection, bool RecoveredFromCorruption) Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            var conn = OpenAndCheck(path);
            return (conn, false);
        }
        catch (SqliteException e) when (e.Code is 11 or 26) // SQLITE_CORRUPT, SQLITE_NOTADB
        {
            SetAside(path);
            return (OpenAndCheck(path), true);
        }
    }

    private static SqliteConnection OpenAndCheck(string path)
    {
        var conn = SqliteConnection.Open(path);
        try
        {
            conn.Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY;");
            var check = conn.Scalar<string>("PRAGMA quick_check");
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                throw new SqliteException(11, "quick_check failed: " + check);
            Migrate(conn);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static void SetAside(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(Path.GetDirectoryName(path)!, $"clipnest.corrupt-{stamp}.db");
        File.Move(path, target);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix)) File.Move(path + suffix, target + suffix);
        }
    }

    private static void Migrate(SqliteConnection conn)
    {
        conn.Execute("CREATE TABLE IF NOT EXISTS schema_version(v INTEGER NOT NULL)");
        var version = conn.Scalar<long>("SELECT v FROM schema_version LIMIT 1");
        if (version >= SchemaVersion) return;

        conn.InTransaction(() =>
        {
            if (version < 1) conn.Execute(V1);
            conn.Execute("DELETE FROM schema_version");
            conn.Execute("INSERT INTO schema_version(v) VALUES (?)", SchemaVersion);
        });
    }

    private const string V1 = """
        CREATE TABLE pinboards(
            id    INTEGER PRIMARY KEY,
            name  TEXT NOT NULL,
            color INTEGER NOT NULL DEFAULT 0,
            sort  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE items(
            id          INTEGER PRIMARY KEY,
            kind        INTEGER NOT NULL,
            title       TEXT,
            text        TEXT,
            hash        TEXT NOT NULL UNIQUE,
            app_path    TEXT,
            app_name    TEXT,
            created_at  INTEGER NOT NULL,
            pinboard_id INTEGER REFERENCES pinboards(id) ON DELETE CASCADE,
            char_count  INTEGER NOT NULL DEFAULT 0,
            img_w       INTEGER,
            img_h       INTEGER,
            blobs       INTEGER NOT NULL DEFAULT 0,
            ocr_text    TEXT,
            link_title  TEXT,
            file_count  INTEGER
        );
        CREATE INDEX ix_items_created ON items(created_at DESC);
        CREATE INDEX ix_items_pin ON items(pinboard_id, created_at DESC);

        CREATE TABLE apps(
            path      TEXT PRIMARY KEY COLLATE NOCASE,
            name      TEXT NOT NULL,
            color     INTEGER NOT NULL,
            icon_file TEXT
        );

        CREATE VIRTUAL TABLE items_fts USING fts5(
            title, text, ocr_text, link_title,
            content='items', content_rowid='id', tokenize='trigram'
        );

        CREATE TRIGGER items_ai AFTER INSERT ON items BEGIN
            INSERT INTO items_fts(rowid, title, text, ocr_text, link_title)
            VALUES (new.id, new.title, new.text, new.ocr_text, new.link_title);
        END;
        CREATE TRIGGER items_ad AFTER DELETE ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, text, ocr_text, link_title)
            VALUES ('delete', old.id, old.title, old.text, old.ocr_text, old.link_title);
        END;
        CREATE TRIGGER items_au AFTER UPDATE OF title, text, ocr_text, link_title ON items BEGIN
            INSERT INTO items_fts(items_fts, rowid, title, text, ocr_text, link_title)
            VALUES ('delete', old.id, old.title, old.text, old.ocr_text, old.link_title);
            INSERT INTO items_fts(rowid, title, text, ocr_text, link_title)
            VALUES (new.id, new.title, new.text, new.ocr_text, new.link_title);
        END;
        """;
}

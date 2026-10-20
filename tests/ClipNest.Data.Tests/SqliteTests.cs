using ClipNest.Data.Sqlite;

namespace ClipNest.Data.Tests;

public sealed class SqliteTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"clipnest-sqlite-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _db;

    public SqliteTests()
    {
        _db = SqliteConnection.Open(_path);
        _db.Execute("CREATE TABLE t(id INTEGER PRIMARY KEY, s TEXT, n INTEGER, b BLOB, UNIQUE(s))");
    }

    public void Dispose()
    {
        _db.Dispose();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Version_supports_trigram() => Assert.True(Version.Parse(SqliteConnection.LibVersion) >= new Version(3, 34));

    [Fact]
    public void Roundtrip_values()
    {
        _db.Execute("INSERT INTO t(s, n, b) VALUES (?,?,?)", "剪贴板 🎉 test", 1234567890123L, new byte[] { 1, 2, 3 });
        _db.Execute("INSERT INTO t(s, n, b) VALUES (?,?,?)", "", null, Array.Empty<byte>());
        using var st = _db.Prepare("SELECT s, n, b FROM t ORDER BY id");
        st.Bind();
        Assert.True(st.Step());
        Assert.Equal("剪贴板 🎉 test", st.GetString(0));
        Assert.Equal(1234567890123L, st.GetInt64(1));
        Assert.Equal(new byte[] { 1, 2, 3 }, st.GetBlob(2));
        Assert.True(st.Step());
        Assert.Equal("", st.GetString(0));
        Assert.True(st.IsNull(1));
        Assert.False(st.Step());
    }

    [Fact]
    public void Scalar_and_changes()
    {
        _db.Execute("INSERT INTO t(s) VALUES (?)", "a");
        _db.Execute("INSERT INTO t(s) VALUES (?)", "b");
        Assert.Equal(2L, _db.Scalar<long>("SELECT COUNT(*) FROM t"));
        Assert.Equal("b", _db.Scalar<string>("SELECT s FROM t WHERE id = ?", _db.LastInsertRowId));
        _db.Execute("DELETE FROM t");
        Assert.Equal(2, _db.Changes);
    }

    [Fact]
    public void Constraint_violation_throws_code_19()
    {
        _db.Execute("INSERT INTO t(s) VALUES (?)", "dup");
        var e = Assert.Throws<SqliteException>(() => _db.Execute("INSERT INTO t(s) VALUES (?)", "dup"));
        Assert.Equal(19, e.Code);
        // the cached statement is still usable afterwards
        _db.Execute("INSERT INTO t(s) VALUES (?)", "other");
    }

    [Fact]
    public void Transaction_rolls_back_on_exception()
    {
        Assert.Throws<InvalidOperationException>(() => _db.InTransaction(() =>
        {
            _db.Execute("INSERT INTO t(s) VALUES (?)", "x");
            throw new InvalidOperationException();
        }));
        Assert.Equal(0L, _db.Scalar<long>("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Fts5_trigram_matches_chinese_substring()
    {
        _db.Execute("CREATE VIRTUAL TABLE f USING fts5(x, tokenize='trigram')");
        _db.Execute("INSERT INTO f(x) VALUES (?)", "这是剪贴板历史");
        _db.Execute("INSERT INTO f(x) VALUES (?)", "Hello World");
        Assert.Equal(1L, _db.Scalar<long>("SELECT COUNT(*) FROM f WHERE f MATCH ?", "\"剪贴板\""));
        Assert.Equal(1L, _db.Scalar<long>("SELECT COUNT(*) FROM f WHERE f MATCH ?", "\"WORLD\""));
    }
}

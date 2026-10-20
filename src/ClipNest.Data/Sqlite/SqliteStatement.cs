using System.Text;
using static ClipNest.Data.Sqlite.Sqlite3Native;

namespace ClipNest.Data.Sqlite;

/// <summary>A prepared statement. Parameters are positional (<c>?</c>), 0-based in <see cref="Bind"/>.</summary>
public sealed unsafe class SqliteStatement : IDisposable
{
    private readonly SqliteConnection _conn;
    private nint _stmt;

    internal SqliteStatement(SqliteConnection conn, string sql)
    {
        _conn = conn;
        var bytes = SqliteConnection.Utf8Z(sql);
        fixed (byte* p = bytes)
        {
            var rc = sqlite3_prepare_v2(conn.Handle, p, bytes.Length, out _stmt, out _);
            if (rc != SQLITE_OK) throw conn.Error(rc);
        }
    }

    public SqliteStatement Bind(params object?[] args)
    {
        sqlite3_reset(_stmt);
        sqlite3_clear_bindings(_stmt);
        for (var i = 0; i < args.Length; i++)
        {
            var index = i + 1;
            var rc = args[i] switch
            {
                null => sqlite3_bind_null(_stmt, index),
                long l => sqlite3_bind_int64(_stmt, index, l),
                int n => sqlite3_bind_int64(_stmt, index, n),
                bool b => sqlite3_bind_int64(_stmt, index, b ? 1 : 0),
                double d => sqlite3_bind_double(_stmt, index, d),
                DateTimeOffset t => sqlite3_bind_int64(_stmt, index, t.ToUnixTimeMilliseconds()),
                Enum e => sqlite3_bind_int64(_stmt, index, Convert.ToInt64(e)),
                string s => BindText(index, s),
                byte[] blob => BindBlob(index, blob),
                _ => throw new ArgumentException($"Unsupported parameter type {args[i]!.GetType()}"),
            };
            if (rc != SQLITE_OK) throw _conn.Error(rc);
        }
        return this;
    }

    private int BindText(int index, string s)
    {
        var len = Encoding.UTF8.GetByteCount(s);
        Span<byte> buf = len <= 512 ? stackalloc byte[len] : new byte[len];
        Encoding.UTF8.GetBytes(s, buf);
        fixed (byte* p = buf)
        {
            return sqlite3_bind_text(_stmt, index, p == null ? (byte*)1 : p, len, SQLITE_TRANSIENT);
        }
    }

    private int BindBlob(int index, byte[] blob)
    {
        fixed (byte* p = blob)
        {
            return sqlite3_bind_blob(_stmt, index, p == null ? (byte*)1 : p, blob.Length, SQLITE_TRANSIENT);
        }
    }

    /// <summary>Advances; true when a row is available.</summary>
    public bool Step()
    {
        var rc = sqlite3_step(_stmt);
        return rc switch
        {
            SQLITE_ROW => true,
            SQLITE_DONE => false,
            _ => throw _conn.Error(rc),
        };
    }

    public void Reset() => sqlite3_reset(_stmt);

    public bool IsNull(int col) => sqlite3_column_type(_stmt, col) == SQLITE_NULL;
    public long GetInt64(int col) => sqlite3_column_int64(_stmt, col);
    public int GetInt32(int col) => (int)sqlite3_column_int64(_stmt, col);
    public double GetDouble(int col) => sqlite3_column_double(_stmt, col);
    public long? GetInt64OrNull(int col) => IsNull(col) ? null : GetInt64(col);
    public int? GetInt32OrNull(int col) => IsNull(col) ? null : GetInt32(col);

    public string? GetString(int col)
    {
        var p = sqlite3_column_text(_stmt, col);
        if (p == null) return null;
        var len = sqlite3_column_bytes(_stmt, col);
        return Encoding.UTF8.GetString(p, len);
    }

    public byte[]? GetBlob(int col)
    {
        var p = sqlite3_column_blob(_stmt, col);
        if (p == null) return IsNull(col) ? null : [];
        var len = sqlite3_column_bytes(_stmt, col);
        return new ReadOnlySpan<byte>(p, len).ToArray();
    }

    public void Dispose()
    {
        if (_stmt != 0)
        {
            sqlite3_finalize(_stmt);
            _stmt = 0;
        }
    }
}

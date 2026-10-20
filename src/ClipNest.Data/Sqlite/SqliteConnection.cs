using System.Runtime.InteropServices;
using System.Text;
using static ClipNest.Data.Sqlite.Sqlite3Native;

namespace ClipNest.Data.Sqlite;

/// <summary>A thin, allocation-light wrapper over a SQLite database handle. Not thread-safe: one per thread.</summary>
public sealed unsafe class SqliteConnection : IDisposable
{
    private nint _db;
    private readonly Dictionary<string, SqliteStatement> _cache = new(StringComparer.Ordinal);

    private SqliteConnection(nint db) => _db = db;

    public static string LibVersion => Marshal.PtrToStringUTF8(sqlite3_libversion()) ?? "?";

    public static SqliteConnection Open(string path, bool readOnly = false)
    {
        var flags = (readOnly ? SQLITE_OPEN_READONLY : SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE) | SQLITE_OPEN_FULLMUTEX;
        var bytes = Utf8Z(path);
        int rc;
        nint db;
        fixed (byte* p = bytes)
        {
            rc = sqlite3_open_v2(p, out db, flags, null);
        }
        if (rc != SQLITE_OK)
        {
            var msg = db != 0 ? Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) : null;
            if (db != 0) sqlite3_close_v2(db);
            throw new SqliteException(rc, $"Cannot open '{path}': {msg ?? rc.ToString()}");
        }
        sqlite3_busy_timeout(db, 3000);
        return new SqliteConnection(db);
    }

    internal nint Handle => _db != 0 ? _db : throw new ObjectDisposedException(nameof(SqliteConnection));

    public long LastInsertRowId => sqlite3_last_insert_rowid(Handle);
    public int Changes => sqlite3_changes(Handle);

    /// <summary>Runs SQL. Without arguments, multiple ';'-separated statements are allowed.</summary>
    public void Execute(string sql, params object?[] args)
    {
        if (args.Length == 0)
        {
            var bytes = Utf8Z(sql);
            fixed (byte* p = bytes)
            {
                var rc = sqlite3_exec(Handle, p, 0, 0, out var err);
                if (rc != SQLITE_OK)
                {
                    var msg = Marshal.PtrToStringUTF8(err) ?? "error";
                    sqlite3_free(err);
                    throw new SqliteException(rc, msg);
                }
            }
            return;
        }

        var stmt = Cached(sql);
        try
        {
            stmt.Bind(args);
            while (stmt.Step()) { }
        }
        finally
        {
            stmt.Reset();
        }
    }

    public T? Scalar<T>(string sql, params object?[] args)
    {
        var stmt = Cached(sql);
        try
        {
            stmt.Bind(args);
            if (!stmt.Step() || stmt.IsNull(0)) return default;
            // Each branch is boxed separately: a shared conditional type would widen long to double.
            object value;
            if (typeof(T) == typeof(string)) value = stmt.GetString(0)!;
            else if (typeof(T) == typeof(double)) value = stmt.GetDouble(0);
            else if (typeof(T) == typeof(int)) value = stmt.GetInt32(0);
            else value = stmt.GetInt64(0);
            return (T)value;
        }
        finally
        {
            stmt.Reset();
        }
    }

    /// <summary>Prepares a statement owned by the caller (dispose it).</summary>
    public SqliteStatement Prepare(string sql) => new(this, sql);

    /// <summary>Returns a connection-owned, reusable statement. Callers must <see cref="SqliteStatement.Reset"/> it.</summary>
    public SqliteStatement Cached(string sql)
    {
        if (!_cache.TryGetValue(sql, out var stmt))
        {
            stmt = new SqliteStatement(this, sql);
            _cache[sql] = stmt;
        }
        return stmt;
    }

    public void InTransaction(Action body)
    {
        Execute("BEGIN IMMEDIATE");
        try
        {
            body();
            Execute("COMMIT");
        }
        catch
        {
            try { Execute("ROLLBACK"); } catch (SqliteException) { /* already rolled back */ }
            throw;
        }
    }

    public T InTransaction<T>(Func<T> body)
    {
        T result = default!;
        InTransaction(() => { result = body(); });
        return result;
    }

    internal SqliteException Error(int rc) =>
        new(sqlite3_extended_errcode(_db) is var ext && ext != 0 ? ext : rc, Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? rc.ToString());

    internal static byte[] Utf8Z(string s)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, bytes);
        return bytes;
    }

    public void Dispose()
    {
        foreach (var stmt in _cache.Values) stmt.Dispose();
        _cache.Clear();
        if (_db != 0)
        {
            sqlite3_close_v2(_db);
            _db = 0;
        }
    }
}

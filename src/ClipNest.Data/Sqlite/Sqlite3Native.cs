using System.Runtime.InteropServices;

namespace ClipNest.Data.Sqlite;

/// <summary>
/// Bindings to the SQLite build that ships with Windows (<c>%WINDIR%\System32\winsqlite3.dll</c>).
/// Using the in-box library keeps ClipNest free of native dependencies.
/// </summary>
internal static unsafe partial class Sqlite3Native
{
    private const string Lib = "winsqlite3.dll";

    public const int SQLITE_OK = 0;
    public const int SQLITE_ROW = 100;
    public const int SQLITE_DONE = 101;

    public const int SQLITE_INTEGER = 1;
    public const int SQLITE_FLOAT = 2;
    public const int SQLITE_TEXT = 3;
    public const int SQLITE_BLOB = 4;
    public const int SQLITE_NULL = 5;

    public const int SQLITE_OPEN_READONLY = 0x00000001;
    public const int SQLITE_OPEN_READWRITE = 0x00000002;
    public const int SQLITE_OPEN_CREATE = 0x00000004;
    public const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

    /// <summary>SQLITE_TRANSIENT: SQLite copies bound data before the call returns.</summary>
    public static readonly nint SQLITE_TRANSIENT = -1;

    [LibraryImport(Lib)] public static partial int sqlite3_open_v2(byte* filename, out nint db, int flags, byte* vfs);
    [LibraryImport(Lib)] public static partial int sqlite3_close_v2(nint db);
    [LibraryImport(Lib)] public static partial int sqlite3_extended_errcode(nint db);
    [LibraryImport(Lib)] public static partial nint sqlite3_errmsg(nint db);
    [LibraryImport(Lib)] public static partial nint sqlite3_libversion();
    [LibraryImport(Lib)] public static partial int sqlite3_busy_timeout(nint db, int ms);
    [LibraryImport(Lib)] public static partial long sqlite3_last_insert_rowid(nint db);
    [LibraryImport(Lib)] public static partial int sqlite3_changes(nint db);
    [LibraryImport(Lib)] public static partial int sqlite3_exec(nint db, byte* sql, nint callback, nint arg, out nint errmsg);
    [LibraryImport(Lib)] public static partial void sqlite3_free(nint p);

    [LibraryImport(Lib)] public static partial int sqlite3_prepare_v2(nint db, byte* sql, int nByte, out nint stmt, out nint tail);
    [LibraryImport(Lib)] public static partial int sqlite3_step(nint stmt);
    [LibraryImport(Lib)] public static partial int sqlite3_reset(nint stmt);
    [LibraryImport(Lib)] public static partial int sqlite3_clear_bindings(nint stmt);
    [LibraryImport(Lib)] public static partial int sqlite3_finalize(nint stmt);

    [LibraryImport(Lib)] public static partial int sqlite3_bind_null(nint stmt, int index);
    [LibraryImport(Lib)] public static partial int sqlite3_bind_int64(nint stmt, int index, long value);
    [LibraryImport(Lib)] public static partial int sqlite3_bind_double(nint stmt, int index, double value);
    [LibraryImport(Lib)] public static partial int sqlite3_bind_text(nint stmt, int index, byte* text, int nBytes, nint destructor);
    [LibraryImport(Lib)] public static partial int sqlite3_bind_blob(nint stmt, int index, byte* data, int nBytes, nint destructor);
    [LibraryImport(Lib)] public static partial int sqlite3_bind_parameter_count(nint stmt);

    [LibraryImport(Lib)] public static partial int sqlite3_column_count(nint stmt);
    [LibraryImport(Lib)] public static partial int sqlite3_column_type(nint stmt, int col);
    [LibraryImport(Lib)] public static partial long sqlite3_column_int64(nint stmt, int col);
    [LibraryImport(Lib)] public static partial double sqlite3_column_double(nint stmt, int col);
    [LibraryImport(Lib)] public static partial byte* sqlite3_column_text(nint stmt, int col);
    [LibraryImport(Lib)] public static partial void* sqlite3_column_blob(nint stmt, int col);
    [LibraryImport(Lib)] public static partial int sqlite3_column_bytes(nint stmt, int col);
}

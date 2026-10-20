namespace ClipNest.Data.Sqlite;

/// <summary>A SQLite error. <see cref="Code"/> is the primary result code (e.g. 19 = constraint, 11 = corrupt).</summary>
public sealed class SqliteException(int code, string message) : Exception(message)
{
    public int Code { get; } = code & 0xFF;
    public int ExtendedCode { get; } = code;
}

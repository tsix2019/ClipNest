using System.IO.Compression;

namespace ClipNest.Data;

/// <summary>
/// Local backups: a zip with a consistent database snapshot plus the blob, thumbnail and icon folders.
/// Restoring is staged and applied at the next start, before the database is opened.
/// </summary>
public static class BackupService
{
    public const string DatabaseEntry = "clipnest.db";
    private const string PendingFolder = "import-pending";
    private static readonly string[] Folders = ["blobs", "thumbs", "icons"];

    public static void Export(ClipRepository repository, string dataDirectory, string zipPath)
    {
        var snapshot = Path.Combine(Path.GetTempPath(), $"clipnest-backup-{Guid.NewGuid():N}.db");
        try
        {
            repository.VacuumInto(snapshot);
            var tmpZip = zipPath + ".tmp";
            using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, DatabaseEntry, CompressionLevel.Optimal);
                foreach (var folder in Folders)
                {
                    var dir = Path.Combine(dataDirectory, folder);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        // PNGs are already compressed.
                        var level = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                        zip.CreateEntryFromFile(file, $"{folder}/{Path.GetFileName(file)}", level);
                    }
                }
            }
            File.Move(tmpZip, zipPath, overwrite: true);
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    /// <summary>Validates <paramref name="zipPath"/> and extracts it for <see cref="ApplyPendingImport"/>. Throws <see cref="InvalidDataException"/> when it is not a ClipNest backup.</summary>
    public static void StageImport(string zipPath, string dataDirectory)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry(DatabaseEntry) is null) throw new InvalidDataException("Not a ClipNest backup (clipnest.db missing).");

        var pending = Path.Combine(dataDirectory, PendingFolder);
        if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
        Directory.CreateDirectory(pending);
        var root = Path.GetFullPath(pending) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var target = Path.GetFullPath(Path.Combine(pending, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public static bool HasPendingImport(string dataDirectory) =>
        File.Exists(Path.Combine(dataDirectory, PendingFolder, DatabaseEntry));

    /// <summary>
    /// Replaces the database with the staged one (the old database is kept as clipnest.before-import-*.db)
    /// and merges the blob folders (content-addressed, so merging is safe). Call before opening the database.
    /// </summary>
    public static bool ApplyPendingImport(string dataDirectory)
    {
        var pending = Path.Combine(dataDirectory, PendingFolder);
        if (!HasPendingImport(dataDirectory)) return false;

        var db = Path.Combine(dataDirectory, DatabaseEntry);
        if (File.Exists(db))
        {
            var keep = Path.Combine(dataDirectory, $"clipnest.before-import-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Move(db, keep);
        }
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(db + suffix)) File.Delete(db + suffix);
        }
        File.Move(Path.Combine(pending, DatabaseEntry), db);

        foreach (var folder in Folders)
        {
            var src = Path.Combine(pending, folder);
            if (!Directory.Exists(src)) continue;
            var dst = Path.Combine(dataDirectory, folder);
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.EnumerateFiles(src))
                File.Move(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        }
        Directory.Delete(pending, recursive: true);
        return true;
    }
}

using System.Text;
using ClipNest.Core;

namespace ClipNest.Data;

/// <summary>
/// Stores large payloads as files keyed by content hash: <c>blobs\{hash}.png|.rtf|.html</c> and
/// <c>thumbs\{hash}.png</c>. Content-addressed, so identical content is stored once.
/// </summary>
public sealed class BlobStore
{
    private readonly string _blobDir;
    private readonly string _thumbDir;

    public BlobStore(string root)
    {
        _blobDir = Path.Combine(root, "blobs");
        _thumbDir = Path.Combine(root, "thumbs");
        Directory.CreateDirectory(_blobDir);
        Directory.CreateDirectory(_thumbDir);
    }

    public string PathFor(string hash, BlobFlags kind) => kind switch
    {
        BlobFlags.Png => Path.Combine(_blobDir, hash + ".png"),
        BlobFlags.Rtf => Path.Combine(_blobDir, hash + ".rtf"),
        BlobFlags.Html => Path.Combine(_blobDir, hash + ".html"),
        BlobFlags.Thumb => Path.Combine(_thumbDir, hash + ".png"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Exactly one blob kind is required."),
    };

    public bool Exists(string hash, BlobFlags kind) => File.Exists(PathFor(hash, kind));

    public void Write(string hash, BlobFlags kind, ReadOnlySpan<byte> data)
    {
        var path = PathFor(hash, kind);
        if (File.Exists(path)) return; // content-addressed: same hash, same bytes
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public void WriteText(string hash, BlobFlags kind, string text) => Write(hash, kind, Encoding.UTF8.GetBytes(text));

    public byte[]? Read(string hash, BlobFlags kind)
    {
        var path = PathFor(hash, kind);
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public string? ReadText(string hash, BlobFlags kind) =>
        Read(hash, kind) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    public void DeleteAll(string hash)
    {
        foreach (var kind in new[] { BlobFlags.Png, BlobFlags.Rtf, BlobFlags.Html, BlobFlags.Thumb })
        {
            try
            {
                File.Delete(PathFor(hash, kind));
            }
            catch (IOException)
            {
                // in use (e.g. thumbnail being displayed); orphan sweep on next start removes it
            }
        }
    }

    /// <summary>Deletes blob files whose hash is no longer referenced. Returns the number of files removed.</summary>
    public int SweepOrphans(Func<string, bool> isReferenced)
    {
        var removed = 0;
        foreach (var dir in new[] { _blobDir, _thumbDir })
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || !isReferenced(name))
                {
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
        return removed;
    }
}

using ClipNest.Core;
using ClipNest.Core.Abstractions;

namespace ClipNest.Data;

/// <param name="Item">The stored item (new, or the existing one moved to the front).</param>
/// <param name="IsNew">False when the content was already in the database.</param>
public sealed record IngestResult(ClipItem Item, bool IsNew);

/// <summary>Turns a clipboard snapshot into a stored <see cref="ClipItem"/>: classify, hash, dedupe, persist blobs.</summary>
public sealed class ClipIngestor(
    IClipRepository repository,
    BlobStore blobs,
    IImageProcessor images,
    Func<string, AppInfo?> resolveApp)
{
    public const int ThumbnailMaxSide = 360;

    /// <summary>Returns null when the snapshot contains nothing worth recording.</summary>
    public IngestResult? Ingest(ClipboardSnapshot snapshot, DateTimeOffset now)
    {
        if (ClipClassifier.TryClassify(snapshot) is not { } kind) return null;

        var hash = ContentHasher.Compute(kind, snapshot);
        var app = snapshot.SourceAppPath is { Length: > 0 } path ? resolveApp(path) : null;
        var appName = app?.Name ?? (snapshot.SourceAppPath is { } p ? Path.GetFileNameWithoutExtension(p) : null);

        if (repository.FindByHash(hash) is { } existing)
        {
            repository.Touch(existing.Id, now, snapshot.SourceAppPath, appName);
            existing.CreatedAt = now;
            existing.SourceAppPath = snapshot.SourceAppPath ?? existing.SourceAppPath;
            existing.SourceAppName = appName ?? existing.SourceAppName;
            return new IngestResult(existing, false);
        }

        var item = new ClipItem
        {
            Kind = kind,
            ContentHash = hash,
            SourceAppPath = snapshot.SourceAppPath,
            SourceAppName = appName,
            CreatedAt = now,
        };

        switch (kind)
        {
            case ClipKind.File:
                var files = snapshot.Files!;
                item.Text = string.Join('\n', files);
                item.FileCount = files.Count;
                break;

            case ClipKind.Image:
                StoreImage(item, snapshot);
                break;

            case ClipKind.Color:
                ClipClassifier.TryParseColor(snapshot.Text!, out var hex);
                item.Text = hex;
                item.CharCount = hex.Length;
                break;

            case ClipKind.Link:
                item.Text = snapshot.Text!.Trim();
                item.CharCount = item.Text.Length;
                break;

            case ClipKind.RichText:
                item.Text = ContentHasher.NormalizeText(snapshot.Text!);
                item.CharCount = item.Text.Length;
                if (!string.IsNullOrEmpty(snapshot.Rtf))
                {
                    blobs.WriteText(hash, BlobFlags.Rtf, snapshot.Rtf);
                    item.Blobs |= BlobFlags.Rtf;
                }
                if (!string.IsNullOrEmpty(snapshot.Html))
                {
                    blobs.WriteText(hash, BlobFlags.Html, snapshot.Html);
                    item.Blobs |= BlobFlags.Html;
                }
                break;

            default:
                item.Text = ContentHasher.NormalizeText(snapshot.Text!);
                item.CharCount = item.Text.Length;
                break;
        }

        try
        {
            repository.Insert(item);
        }
        catch (Sqlite.SqliteException e) when (e.Code == 19 && repository.FindByHash(hash) is { } raced)
        {
            // Same content was stored concurrently (undo, edit): treat as a re-copy instead of losing it.
            repository.Touch(raced.Id, now, snapshot.SourceAppPath, appName);
            raced.CreatedAt = now;
            return new IngestResult(raced, false);
        }
        return new IngestResult(item, true);
    }

    private void StoreImage(ClipItem item, ClipboardSnapshot snapshot)
    {
        var png = snapshot.Png!;
        blobs.Write(item.ContentHash, BlobFlags.Png, png);
        item.Blobs |= BlobFlags.Png;
        item.ImageWidth = snapshot.ImageWidth > 0 ? snapshot.ImageWidth : null;
        item.ImageHeight = snapshot.ImageHeight > 0 ? snapshot.ImageHeight : null;

        // Small images are their own thumbnail.
        if (Math.Max(snapshot.ImageWidth, snapshot.ImageHeight) is > 0 and <= ThumbnailMaxSide) return;

        try
        {
            var thumb = images.CreateThumbnailPng(png, ThumbnailMaxSide);
            blobs.Write(item.ContentHash, BlobFlags.Thumb, thumb);
            item.Blobs |= BlobFlags.Thumb;
        }
        catch (Exception)
        {
            // A broken thumbnail must not lose the copy; the card falls back to the full image.
        }
    }
}

namespace ClipNest.Core;

/// <summary>A single clipboard history entry.</summary>
public sealed class ClipItem
{
    public long Id { get; set; }
    public ClipKind Kind { get; set; }

    /// <summary>User-assigned title; null means "show the kind name".</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Plain-text payload: the text for Text/RichText, the URL for Link, the "#RRGGBB" hex for Color,
    /// newline-joined paths for File. Null for pure images.
    /// </summary>
    public string? Text { get; set; }

    public string ContentHash { get; set; } = "";
    public string? SourceAppPath { get; set; }
    public string? SourceAppName { get; set; }

    /// <summary>Most recent time this content was copied (re-copying refreshes it).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Pinboard the item belongs to, or null when it only lives in history.</summary>
    public long? PinboardId { get; set; }

    public int CharCount { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public BlobFlags Blobs { get; set; }
    public string? OcrText { get; set; }
    public string? LinkTitle { get; set; }
    public int? FileCount { get; set; }

    /// <summary>"Pin to top" time (Unix ms); 0 = not pinned to the top.</summary>
    public long TopAt { get; set; }

    public bool IsPinned => PinboardId is not null;

    public bool IsTop => TopAt != 0;

    public IReadOnlyList<string> FilePaths =>
        Kind == ClipKind.File && Text is not null
            ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];

    public ClipItem Clone() => (ClipItem)MemberwiseClone();
}

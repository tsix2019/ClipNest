namespace ClipNest.Core;

/// <summary>Raw data read from the system clipboard at one point in time.</summary>
public sealed class ClipboardSnapshot
{
    public string? Text { get; init; }

    /// <summary>Contents of the "Rich Text Format" clipboard format.</summary>
    public string? Rtf { get; init; }

    /// <summary>Contents of the "HTML Format" clipboard format (CF_HTML with its header).</summary>
    public string? Html { get; init; }

    /// <summary>PNG-encoded image, if the clipboard held a bitmap.</summary>
    public byte[]? Png { get; init; }

    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }

    public IReadOnlyList<string>? Files { get; init; }

    public string? SourceAppPath { get; init; }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasImage => Png is { Length: > 0 };
    public bool HasFiles => Files is { Count: > 0 };
}

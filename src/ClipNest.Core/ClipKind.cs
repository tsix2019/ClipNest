namespace ClipNest.Core;

/// <summary>What a clipboard item primarily contains. Values are persisted — never renumber.</summary>
public enum ClipKind
{
    Text = 0,
    RichText = 1,
    Image = 2,
    File = 3,
    Link = 4,
    Color = 5,
}

/// <summary>Extra payloads stored on disk next to an item (see <c>BlobStore</c>). Persisted as an int.</summary>
[Flags]
public enum BlobFlags
{
    None = 0,
    Rtf = 1,
    Html = 2,
    Png = 4,
    Thumb = 8,
}

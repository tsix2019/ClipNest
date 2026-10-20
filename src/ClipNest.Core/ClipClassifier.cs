namespace ClipNest.Core;

/// <summary>Decides the <see cref="ClipKind"/> of a snapshot, following Paste's rules.</summary>
public static class ClipClassifier
{
    public static ClipKind Classify(ClipboardSnapshot s) =>
        TryClassify(s) ?? throw new ArgumentException("Snapshot has no usable content.", nameof(s));

    /// <summary>Returns null when the snapshot holds nothing worth recording.</summary>
    public static ClipKind? TryClassify(ClipboardSnapshot s)
    {
        if (s.HasFiles) return ClipKind.File;
        if (s.HasImage && !s.HasText) return ClipKind.Image;
        if (!s.HasText) return null;

        var text = s.Text!;
        if (IsUrl(text)) return ClipKind.Link;
        if (TryParseColor(text, out _)) return ClipKind.Color;
        if (!string.IsNullOrEmpty(s.Rtf) || !string.IsNullOrEmpty(s.Html)) return ClipKind.RichText;
        return ClipKind.Text;
    }

    /// <summary>
    /// A color is a lone 6- or 8-digit hex value. Without a leading '#', it must contain at least one
    /// A–F letter so that numeric codes such as "235442" stay text.
    /// </summary>
    public static bool TryParseColor(string text, out string hex)
    {
        hex = "";
        var t = text.AsSpan().Trim();
        var hasHash = t.Length > 0 && t[0] == '#';
        if (hasHash) t = t[1..];
        if (t.Length is not (6 or 8)) return false;

        var hasLetter = false;
        foreach (var c in t)
        {
            if (char.IsAsciiDigit(c)) continue;
            if (char.IsAsciiHexDigit(c)) { hasLetter = true; continue; }
            return false;
        }
        if (!hasHash && !hasLetter) return false;

        hex = "#" + t.ToString().ToUpperInvariant();
        return true;
    }

    /// <summary>True when the whole (trimmed) text is a single absolute http(s) URL.</summary>
    public static bool IsUrl(string text)
    {
        var t = text.Trim();
        if (t.Length < 8 || t.Length > 4096) return false;
        foreach (var c in t)
        {
            if (char.IsWhiteSpace(c)) return false;
        }
        return Uri.TryCreate(t, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host);
    }
}

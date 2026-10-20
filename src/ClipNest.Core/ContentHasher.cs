using System.Security.Cryptography;
using System.Text;

namespace ClipNest.Core;

/// <summary>Computes the de-duplication key of clipboard content.</summary>
public static class ContentHasher
{
    public static string Compute(ClipKind kind, ClipboardSnapshot s) => kind switch
    {
        ClipKind.Image => Hash(kind, s.Png ?? []),
        ClipKind.File => ForFiles(s.Files ?? []),
        ClipKind.Color => ForText(kind, ClipClassifier.TryParseColor(s.Text ?? "", out var hex) ? hex : s.Text ?? ""),
        ClipKind.Link => ForText(kind, (s.Text ?? "").Trim()),
        _ => ForText(kind, s.Text ?? ""),
    };

    public static string ForText(ClipKind kind, string text) =>
        Hash(kind, Encoding.UTF8.GetBytes(NormalizeText(text)));

    public static string ForFiles(IEnumerable<string> files)
    {
        var joined = string.Join('\n', files.Select(f => f.ToLowerInvariant()).Order(StringComparer.Ordinal));
        return Hash(ClipKind.File, Encoding.UTF8.GetBytes(joined));
    }

    /// <summary>Unifies line endings so the same text copied from different apps dedupes.</summary>
    public static string NormalizeText(string s) =>
        s.Contains('\r') ? s.Replace("\r\n", "\n").Replace('\r', '\n') : s;

    private static string Hash(ClipKind kind, ReadOnlySpan<byte> payload)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> prefix = [(byte)kind, (byte)':'];
        h.AppendData(prefix);
        h.AppendData(payload);
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }
}

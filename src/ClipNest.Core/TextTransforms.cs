using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipNest.Core;

/// <summary>"Paste as…" transformations. Values are used as localization keys (<c>Transform_*</c>).</summary>
public enum TextTransform
{
    Upper,
    Lower,
    Title,
    Sentence,
    Camel,
    Pascal,
    Snake,
    Kebab,
    Trim,
    SingleLine,
    SortLines,
    UniqueLines,
    JsonPretty,
    JsonMinify,
    UrlEncode,
    UrlDecode,
    Base64Encode,
    Base64Decode,
    HtmlDecode,
}

public static partial class TextTransforms
{
    /// <summary>Transform groups for the menu (a separator between groups).</summary>
    public static readonly TextTransform[][] Groups =
    [
        [TextTransform.Upper, TextTransform.Lower, TextTransform.Title, TextTransform.Sentence],
        [TextTransform.Camel, TextTransform.Pascal, TextTransform.Snake, TextTransform.Kebab],
        [TextTransform.Trim, TextTransform.SingleLine, TextTransform.SortLines, TextTransform.UniqueLines],
        [TextTransform.JsonPretty, TextTransform.JsonMinify],
        [TextTransform.UrlEncode, TextTransform.UrlDecode, TextTransform.Base64Encode, TextTransform.Base64Decode, TextTransform.HtmlDecode],
    ];

    /// <summary>Applies <paramref name="transform"/>; returns null when the input is not valid for it (e.g. not JSON).</summary>
    public static string? Apply(TextTransform transform, string text) => transform switch
    {
        TextTransform.Upper => text.ToUpperInvariant(),
        TextTransform.Lower => text.ToLowerInvariant(),
        TextTransform.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
        TextTransform.Sentence => SentenceCase(text),
        TextTransform.Camel => JoinWords(text, camel: true),
        TextTransform.Pascal => JoinWords(text, camel: false),
        TextTransform.Snake => string.Join('_', Words(text)).ToLowerInvariant(),
        TextTransform.Kebab => string.Join('-', Words(text)).ToLowerInvariant(),
        TextTransform.Trim => TrimLines(text),
        TextTransform.SingleLine => Whitespace().Replace(text, " ").Trim(),
        TextTransform.SortLines => string.Join('\n', Lines(text).Order(StringComparer.CurrentCulture)),
        TextTransform.UniqueLines => string.Join('\n', Lines(text).Distinct()),
        TextTransform.JsonPretty => Json(text, indented: true),
        TextTransform.JsonMinify => Json(text, indented: false),
        TextTransform.UrlEncode => Uri.EscapeDataString(text),
        TextTransform.UrlDecode => Try(() => Uri.UnescapeDataString(text.Replace('+', ' '))),
        TextTransform.Base64Encode => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
        TextTransform.Base64Decode => Base64Decode(text),
        TextTransform.HtmlDecode => WebUtility.HtmlDecode(text),
        _ => text,
    };

    private static string[] Lines(string text) => ContentHasher.NormalizeText(text).Split('\n');

    private static string TrimLines(string text) =>
        string.Join('\n', Lines(text).Select(l => l.TrimEnd())).Trim();

    private static string SentenceCase(string text)
    {
        var lower = text.ToLowerInvariant();
        var chars = lower.ToCharArray();
        var capitalize = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (capitalize && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalize = false;
            }
            else if (chars[i] is '.' or '!' or '?' or '\n')
            {
                capitalize = true;
            }
        }
        return new string(chars);
    }

    /// <summary>Splits identifiers and phrases into words: "helloWorld_foo-bar baz" → hello World foo bar baz.</summary>
    internal static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text).Select(m => m.Value);

    private static string JoinWords(string text, bool camel)
    {
        var sb = new StringBuilder();
        foreach (var word in Words(text))
        {
            var lower = word.ToLowerInvariant();
            if (sb.Length == 0 && camel) sb.Append(lower);
            else sb.Append(char.ToUpperInvariant(lower[0])).Append(lower.AsSpan(1));
        }
        return sb.ToString();
    }

    private static string? Json(string text, bool indented)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                doc.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Base64Decode(string text)
    {
        var s = text.Trim().Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            var bytes = Convert.FromBase64String(s);
            var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return decoded;
        }
        catch (Exception e) when (e is FormatException or DecoderFallbackException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Try(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception e) when (e is UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Lower-case run, Capitalized word, ALLCAPS run (stopping before a Capitalized word), digits, or a CJK run.
    [GeneratedRegex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+|[\p{L}-[A-Za-z]]+")]
    private static partial Regex WordPattern();
}

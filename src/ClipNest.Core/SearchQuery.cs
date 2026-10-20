namespace ClipNest.Core;

/// <summary>
/// A parsed search box input. Supported filter tokens: <c>type:</c>, <c>app:</c>, <c>date:</c>, <c>pin:</c>;
/// everything else is free text.
/// </summary>
public sealed record SearchQuery(
    string Text,
    ClipKind? Kind = null,
    string? App = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    string? Pinboard = null)
{
    public static readonly SearchQuery Empty = new("");

    public bool IsEmpty =>
        Text.Length == 0 && Kind is null && App is null && Since is null && Until is null && Pinboard is null;

    private static readonly Dictionary<string, ClipKind> KindAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = ClipKind.Text, ["文本"] = ClipKind.Text,
        ["rich"] = ClipKind.RichText, ["富文本"] = ClipKind.RichText,
        ["image"] = ClipKind.Image, ["img"] = ClipKind.Image, ["图片"] = ClipKind.Image,
        ["file"] = ClipKind.File, ["文件"] = ClipKind.File,
        ["link"] = ClipKind.Link, ["url"] = ClipKind.Link, ["链接"] = ClipKind.Link,
        ["color"] = ClipKind.Color, ["颜色"] = ClipKind.Color,
    };

    public static SearchQuery Parse(string input, DateTimeOffset now)
    {
        ClipKind? kind = null;
        string? app = null, pin = null;
        DateTimeOffset? since = null, until = null;
        var words = new List<string>();

        foreach (var word in input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = word.IndexOf(':');
            if (colon > 0 && colon < word.Length - 1)
            {
                var key = word[..colon].ToLowerInvariant();
                var value = word[(colon + 1)..];
                switch (key)
                {
                    case "type" when KindAliases.TryGetValue(value, out var k):
                        kind = k;
                        continue;
                    case "app":
                        app = value;
                        continue;
                    case "pin":
                        pin = value;
                        continue;
                    case "date" when TryParseDate(value, now, out var s, out var u):
                        since = s;
                        until = u;
                        continue;
                }
            }
            words.Add(word);
        }

        return new SearchQuery(string.Join(' ', words), kind, app, since, until, pin);
    }

    private static bool TryParseDate(string value, DateTimeOffset now, out DateTimeOffset? since, out DateTimeOffset? until)
    {
        var midnight = new DateTimeOffset(now.Date, now.Offset);
        until = null;
        switch (value.ToLowerInvariant())
        {
            case "today" or "今天":
                since = midnight;
                return true;
            case "yesterday" or "昨天":
                since = midnight.AddDays(-1);
                until = midnight;
                return true;
        }

        // "<n>d" / "<n>h" relative windows
        if (value.Length >= 2 && int.TryParse(value.AsSpan(0, value.Length - 1), out var n) && n > 0)
        {
            switch (char.ToLowerInvariant(value[^1]))
            {
                case 'd':
                    since = now.AddDays(-n);
                    return true;
                case 'h':
                    since = now.AddHours(-n);
                    return true;
            }
        }

        since = null;
        return false;
    }
}

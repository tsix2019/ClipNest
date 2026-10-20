using System.Globalization;
using System.Text.RegularExpressions;

namespace ClipNest.Core;

/// <summary>
/// Template placeholders expanded when a pinned snippet is pasted:
/// <c>{date}</c>, <c>{time}</c>, <c>{datetime}</c>, <c>{date:yyyyMMdd}</c> (custom format), <c>{clipboard}</c>.
/// Unknown placeholders are left untouched; <c>{{</c> and <c>}}</c> produce literal braces.
/// </summary>
public static partial class Placeholders
{
    public static bool HasAny(string? text) => text is not null && Pattern().IsMatch(text);

    public static string Expand(string text, DateTimeOffset now, Func<string?> currentClipboard, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string? clipboard = null;
        var expanded = Pattern().Replace(text, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var format = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            try
            {
                return name switch
                {
                    "date" => now.ToString(format ?? "yyyy-MM-dd", culture),
                    "time" => now.ToString(format ?? "HH:mm", culture),
                    "datetime" => now.ToString(format ?? "yyyy-MM-dd HH:mm", culture),
                    "clipboard" => clipboard ??= currentClipboard() ?? "",
                    _ => m.Value,
                };
            }
            catch (FormatException)
            {
                return m.Value;
            }
        });
        return expanded.Replace("{{", "{").Replace("}}", "}");
    }

    [GeneratedRegex(@"(?<!\{)\{(?<name>date|time|datetime|clipboard)(?::(?<fmt>[^{}]+))?\}(?!\})", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}

/// <summary>Alternative textual forms of a color item.</summary>
public static class ColorFormats
{
    /// <summary>Returns HEX, rgb(), hsl() and (when translucent) rgba() forms of a "#RRGGBB[AA]" value.</summary>
    public static IReadOnlyList<string> All(string hex)
    {
        if (!ClipClassifier.TryParseColor(hex, out var h)) return [];
        var v = h.TrimStart('#');
        int r = Convert.ToInt32(v[..2], 16), g = Convert.ToInt32(v[2..4], 16), b = Convert.ToInt32(v[4..6], 16);
        var a = v.Length == 8 ? Convert.ToInt32(v[6..8], 16) / 255.0 : 1.0;
        var (hue, sat, light) = ToHsl(r, g, b);
        var list = new List<string>
        {
            h,
            h.ToLowerInvariant(),
            $"rgb({r}, {g}, {b})",
            FormattableString.Invariant($"hsl({hue:0}, {sat:0}%, {light:0}%)"),
        };
        if (a < 1) list.Add(FormattableString.Invariant($"rgba({r}, {g}, {b}, {a:0.##})"));
        return list;
    }

    private static (double h, double s, double l) ToHsl(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        var l = (max + min) / 2;
        double h = 0, s = 0;
        var d = max - min;
        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rf) h = (gf - bf) / d + (gf < bf ? 6 : 0);
            else if (max == gf) h = (bf - rf) / d + 2;
            else h = (rf - gf) / d + 4;
            h *= 60;
        }
        return (h, s * 100, l * 100);
    }
}

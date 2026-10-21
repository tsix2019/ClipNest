using System.Text.Json;

namespace ClipNest.Platform;

/// <summary>A newer release on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string PageUrl);

/// <summary>
/// Asks GitHub for the latest release (one small JSON request, only when the user clicks "Check for updates").
/// Nothing is downloaded: a newer version is shown with a link to its release page.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "tsix2019/ClipNest";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>The newer release, null when <paramref name="current"/> is up to date. Throws on network errors.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancel = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClipNest/{current.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancel);
        return Parse(json, current);
    }

    /// <summary>Reads <c>tag_name</c>/<c>html_url</c> of a GitHub release and compares it with <paramref name="current"/>.</summary>
    internal static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagName) return null;
        if (!TryParseTag(tagName, out var version) || !IsNewer(version, current)) return null;
        var url = root.TryGetProperty("html_url", out var page) && page.GetString() is { } u ? u : ReleasesUrl;
        return new UpdateInfo(version, url);
    }

    /// <summary>"v1.2.3" / "1.2" → version (missing parts are 0).</summary>
    internal static bool TryParseTag(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0) text = text[..dash];
        if (Version.TryParse(text.Contains('.') ? text : text + ".0", out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }
        version = new Version(0, 0);
        return false;
    }

    /// <summary>Compares major.minor.build only (the assembly revision is not part of a release tag).</summary>
    internal static bool IsNewer(Version candidate, Version current) =>
        new Version(candidate.Major, candidate.Minor, Math.Max(0, candidate.Build))
        > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}

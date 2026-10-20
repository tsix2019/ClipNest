using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipNest.Platform;

/// <summary>Fetches a page's title (og:title or &lt;title&gt;) reading at most 256 KB.</summary>
public sealed partial class LinkTitleFetcher : IDisposable
{
    private const int MaxBytes = 256 * 1024;

    private readonly HttpClient _http;

    static LinkTitleFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public LinkTitleFetcher()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(4),
        })
        {
            Timeout = TimeSpan.FromSeconds(6),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClipNest/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
    }

    public async Task<string?> FetchAsync(string url, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentType?.MediaType is { } type && !type.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            var buffer = new byte[MaxBytes];
            var total = 0;
            int read;
            while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), cancel)) > 0) total += read;

            var encoding = PickEncoding(response.Content.Headers.ContentType?.CharSet, buffer.AsSpan(0, Math.Min(total, 4096)));
            return ExtractTitle(encoding.GetString(buffer, 0, total));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string? ExtractTitle(string html)
    {
        var m = OgTitle().Match(html);
        if (!m.Success) m = OgTitleReversed().Match(html);
        if (!m.Success) m = TitleTag().Match(html);
        if (!m.Success) return null;
        var title = WebUtility.HtmlDecode(m.Groups["t"].Value);
        title = Whitespace().Replace(title, " ").Trim();
        if (title.Length == 0) return null;
        return title.Length > 200 ? title[..200] + "…" : title;
    }

    private static Encoding PickEncoding(string? headerCharset, ReadOnlySpan<byte> head)
    {
        var name = headerCharset;
        if (string.IsNullOrEmpty(name))
        {
            var m = MetaCharset().Match(Encoding.ASCII.GetString(head));
            if (m.Success) name = m.Groups["c"].Value;
        }
        try
        {
            return string.IsNullOrEmpty(name) ? Encoding.UTF8 : Encoding.GetEncoding(name.Trim('"', '\''));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    [GeneratedRegex("""<meta[^>]+property\s*=\s*["']og:title["'][^>]*content\s*=\s*["'](?<t>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitle();

    [GeneratedRegex("""<meta[^>]+content\s*=\s*["'](?<t>[^"']*)["'][^>]*property\s*=\s*["']og:title["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitleReversed();

    [GeneratedRegex(@"<title[^>]*>(?<t>[\s\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?(?<c>[\w\-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public void Dispose() => _http.Dispose();
}

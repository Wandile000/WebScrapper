using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;

namespace WebScrapper;

public sealed class DdgDiscovery : IDisposable
{
    private const string Endpoint = "https://html.duckduckgo.com/html/";
    private const string UserAgent =
        "Mozilla/5.0 (compatible; WebScrapper/1.0; +https://example.com/webscrapper)";

    private readonly HttpClient _http;

    public DdgDiscovery()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public async Task<List<string>> SearchAsync(string query, int maxResults)
    {
        using var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", query),
            new KeyValuePair<string, string>("kl", "us-en"),
        });

        using var response = await _http.PostAsync(Endpoint, form);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@class,'result__a') or contains(@class,'result__url')]");
        if (anchors is null) return urls;

        foreach (var a in anchors)
        {
            if (urls.Count >= maxResults) break;
            var href = a.GetAttributeValue("href", "");
            var resolved = ResolveDdgHref(href);
            if (string.IsNullOrEmpty(resolved)) continue;
            if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri)) continue;
            if (uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) continue;

            var root = $"{uri.Scheme}://{uri.Host}/";
            if (seen.Add(root))
                urls.Add(root);
        }

        return urls;
    }

    private static string? ResolveDdgHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("//")) href = "https:" + href;

        var match = Regex.Match(href, @"[?&]uddg=([^&]+)");
        if (match.Success)
            return HttpUtility.UrlDecode(match.Groups[1].Value);

        if (Uri.TryCreate(href, UriKind.Absolute, out _))
            return href;
        return null;
    }

    public void Dispose() => _http.Dispose();
}

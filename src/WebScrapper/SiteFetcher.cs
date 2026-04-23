using System.Net;
using System.Net.Http.Headers;

namespace WebScrapper;

public sealed class SiteFetcher : IDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (compatible; WebScrapper/1.0; +https://example.com/webscrapper)";

    private const long MaxContentBytes = 2 * 1024 * 1024;

    private readonly HttpClient _http;

    public SiteFetcher(int timeoutSeconds)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 6,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en;q=0.9");
    }

    public async Task<FetchResult> FetchAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return FetchResult.Fail($"http {(int)response.StatusCode}");

            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (media.Length > 0 && !media.Contains("html", StringComparison.OrdinalIgnoreCase))
                return FetchResult.Fail($"non-html ({media})");

            var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                total += read;
                if (total > MaxContentBytes) break;
                buffer.Write(chunk, 0, read);
            }

            var encoding = GetEncoding(response.Content.Headers.ContentType?.CharSet);
            var html = encoding.GetString(buffer.ToArray());
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            return FetchResult.Ok(finalUrl, html, (int)response.StatusCode);
        }
        catch (TaskCanceledException)
        {
            return FetchResult.Fail("timeout");
        }
        catch (HttpRequestException ex)
        {
            return FetchResult.Fail($"http error: {ex.Message}");
        }
        catch (UriFormatException)
        {
            return FetchResult.Fail("bad url");
        }
    }

    private static System.Text.Encoding GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return System.Text.Encoding.UTF8;
        try { return System.Text.Encoding.GetEncoding(charset.Trim('"')); }
        catch { return System.Text.Encoding.UTF8; }
    }

    public void Dispose() => _http.Dispose();
}

public sealed record FetchResult(bool Success, string? FinalUrl, string? Html, int StatusCode, string? Error)
{
    public static FetchResult Ok(string finalUrl, string html, int status) =>
        new(true, finalUrl, html, status, null);

    public static FetchResult Fail(string error) =>
        new(false, null, null, 0, error);
}

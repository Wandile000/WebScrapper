using System.Collections.Concurrent;
using WebScrapper;

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputFile))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!);

await using var db = await ScraperDb.OpenAsync(options.DatabasePath);

var candidates = new List<string>();

if (options.Seeds is { Count: > 0 })
{
    foreach (var seedFile in options.Seeds)
    {
        if (!File.Exists(seedFile))
        {
            Console.Error.WriteLine($"Seed file not found: {seedFile}");
            continue;
        }
        foreach (var line in await File.ReadAllLinesAsync(seedFile))
        {
            var url = line.Trim();
            if (url.Length == 0 || url.StartsWith('#')) continue;
            candidates.Add(NormalizeUrl(url));
        }
    }
}

if (options.SearchQueries is { Count: > 0 })
{
    using var discovery = new DdgDiscovery();
    foreach (var query in options.SearchQueries)
    {
        Console.WriteLine($"[discover] {query}");
        var found = await discovery.SearchAsync(query, options.SearchMaxResults);
        Console.WriteLine($"[discover] {found.Count} results for \"{query}\"");
        candidates.AddRange(found.Select(NormalizeUrl));
    }
}

candidates = candidates
    .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

if (candidates.Count == 0)
{
    Console.Error.WriteLine("No candidate URLs. Provide --seeds <file> and/or --search \"<query>\".");
    return 2;
}

Console.WriteLine($"[scan] {candidates.Count} unique candidate URL(s), threshold={options.Threshold}, concurrency={options.Concurrency}");

using var fetcher = new SiteFetcher(options.TimeoutSeconds);
var scorer = new PoorSiteScorer();
var qualifying = new ConcurrentBag<(string Url, int Score, string Reasons)>();

using var gate = new SemaphoreSlim(options.Concurrency);
var tasks = candidates.Select(async url =>
{
    await gate.WaitAsync();
    try
    {
        var fetchStart = DateTimeOffset.UtcNow;
        var fetch = await fetcher.FetchAsync(url);
        var elapsed = (int)(DateTimeOffset.UtcNow - fetchStart).TotalMilliseconds;

        if (!fetch.Success)
        {
            Console.WriteLine($"[skip]  {url}  ({fetch.Error})");
            await db.RecordFailureAsync(url, fetch.Error ?? "unknown", elapsed);
            return;
        }

        var result = scorer.Score(fetch.FinalUrl!, fetch.Html!, elapsed);
        await db.RecordScanAsync(fetch.FinalUrl!, result.Score, result.Reasons, elapsed, fetch.StatusCode);

        var tag = result.Score >= options.Threshold ? "HIT " : "ok  ";
        Console.WriteLine($"[{tag}] {fetch.FinalUrl}  score={result.Score}  [{string.Join(", ", result.Reasons)}]");

        if (result.Score >= options.Threshold)
        {
            qualifying.Add((fetch.FinalUrl!, result.Score, string.Join("; ", result.Reasons)));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] {url}  {ex.GetType().Name}: {ex.Message}");
        await db.RecordFailureAsync(url, ex.Message, 0);
    }
    finally
    {
        gate.Release();
    }
});

await Task.WhenAll(tasks);

var ordered = qualifying.OrderByDescending(q => q.Score).ToList();

await using (var writer = new StreamWriter(options.OutputFile, append: false))
{
    await writer.WriteLineAsync($"# Poorly-built site candidates. Generated {DateTimeOffset.UtcNow:u}.");
    await writer.WriteLineAsync($"# Threshold: {options.Threshold}. {ordered.Count} hit(s) of {candidates.Count} scanned.");
    await writer.WriteLineAsync("# Format: <score>\t<url>\t<reasons>");
    foreach (var hit in ordered)
    {
        await writer.WriteLineAsync($"{hit.Score}\t{hit.Url}\t{hit.Reasons}");
    }
}

Console.WriteLine();
Console.WriteLine($"[done]  {ordered.Count}/{candidates.Count} hit(s) written to {options.OutputFile}");
Console.WriteLine($"[done]  Full scan history stored in {options.DatabasePath}");
return 0;

static string NormalizeUrl(string url)
{
    url = url.Trim();
    if (url.StartsWith("//")) url = "https:" + url;
    if (!url.Contains("://")) url = "https://" + url;
    return url;
}

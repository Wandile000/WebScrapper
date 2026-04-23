using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace WebScrapper;

public sealed class PoorSiteScorer
{
    private static readonly string[] ObsoleteTags =
        { "font", "center", "marquee", "blink", "frameset", "frame", "applet", "basefont", "big", "tt" };

    private static readonly string[] ObsoletePresentationalAttrs =
        { "bgcolor", "background", "bordercolor", "cellpadding", "cellspacing", "valign", "align" };

    public ScoreResult Score(string url, string html, int elapsedMs)
    {
        var score = 0;
        var reasons = new List<string>();

        void Add(int points, string reason)
        {
            score += points;
            reasons.Add($"+{points} {reason}");
        }

        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps)
            Add(10, "no https");

        var doc = new HtmlDocument { OptionFixNestedTags = true };
        doc.LoadHtml(html);

        var head = doc.DocumentNode.SelectSingleNode("//head");
        var body = doc.DocumentNode.SelectSingleNode("//body");

        var doctypeMatch = Regex.Match(html, @"<!doctype[^>]*>", RegexOptions.IgnoreCase);
        if (!doctypeMatch.Success)
            Add(15, "no doctype");
        else if (Regex.IsMatch(doctypeMatch.Value, @"html\s+public|xhtml|transitional|4\.0|4\.01",
                     RegexOptions.IgnoreCase))
            Add(12, "legacy doctype (html4/xhtml)");

        if (head is null || head.SelectSingleNode(".//meta[translate(@name,'VIEWPORT','viewport')='viewport']") is null)
            Add(20, "no viewport meta (not mobile-friendly)");

        if (head is null || head.SelectSingleNode(".//meta[translate(@name,'DESCRIPTION','description')='description']") is null)
            Add(5, "no meta description");

        if (head is null ||
            head.SelectSingleNode(".//link[contains(translate(@rel,'ICON','icon'),'icon')]") is null)
            Add(3, "no favicon");

        var externalCssCount = head?.SelectNodes(".//link[translate(@rel,'STYLESHEET','stylesheet')='stylesheet']")?.Count ?? 0;
        var internalCssCount = doc.DocumentNode.SelectNodes("//style")?.Count ?? 0;
        if (externalCssCount == 0 && internalCssCount == 0)
            Add(12, "no css at all");
        else if (externalCssCount == 0)
            Add(5, "no external stylesheet");

        foreach (var tag in ObsoleteTags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes is { Count: > 0 })
                Add(tag switch { "frameset" or "frame" => 20, "applet" => 25, _ => 10 },
                    $"obsolete <{tag}> x{nodes.Count}");
        }

        var flashObjects = doc.DocumentNode.SelectNodes(
            "//object[contains(translate(@type,'FLASH','flash'),'flash')] | //embed[contains(translate(@type,'FLASH','flash'),'flash')]");
        if (flashObjects is { Count: > 0 })
            Add(20, "flash embeds");

        var tableLayoutCount = doc.DocumentNode.SelectNodes("//table//table")?.Count ?? 0;
        if (tableLayoutCount >= 2)
            Add(10, $"nested tables for layout x{tableLayoutCount}");

        var presentationalHits = 0;
        foreach (var attr in ObsoletePresentationalAttrs)
        {
            presentationalHits += doc.DocumentNode.SelectNodes($"//*[@{attr}]")?.Count ?? 0;
        }
        if (presentationalHits >= 5)
            Add(8, $"presentational attrs x{presentationalHits}");

        var inlineStyled = doc.DocumentNode.SelectNodes("//*[@style]")?.Count ?? 0;
        if (inlineStyled >= 15)
            Add(5, $"inline styles x{inlineStyled}");

        var imgs = doc.DocumentNode.SelectNodes("//img")?.Count ?? 0;
        var imgsWithAlt = doc.DocumentNode.SelectNodes("//img[@alt]")?.Count ?? 0;
        if (imgs >= 4 && imgsWithAlt * 2 < imgs)
            Add(5, $"missing alt on images ({imgsWithAlt}/{imgs})");

        if (body is not null)
        {
            var visibleText = Regex.Replace(body.InnerText ?? "", @"\s+", " ").Trim();
            if (visibleText.Length < 400)
                Add(6, $"very little text ({visibleText.Length} chars)");
        }

        var ogCount = head?.SelectNodes(".//meta[starts-with(translate(@property,'OG:','og:'),'og:')]")?.Count ?? 0;
        if (ogCount == 0)
            Add(4, "no open graph tags");

        var currentYear = DateTime.UtcNow.Year;
        var years = Regex.Matches(html, @"(?:©|&copy;|copyright)[^0-9]{0,20}(\d{4})",
            RegexOptions.IgnoreCase);
        var latestFooterYear = 0;
        foreach (Match m in years)
        {
            if (int.TryParse(m.Groups[1].Value, out var y) && y is >= 1995 and <= 2100)
                latestFooterYear = Math.Max(latestFooterYear, y);
        }
        if (latestFooterYear > 0 && currentYear - latestFooterYear >= 3)
            Add(10, $"stale copyright ({latestFooterYear})");

        if (elapsedMs >= 5000)
            Add(5, $"slow response ({elapsedMs}ms)");

        var mailtoOrTelCount =
            (doc.DocumentNode.SelectNodes("//a[starts-with(@href,'mailto:') or starts-with(@href,'tel:')]")?.Count ?? 0);
        if (mailtoOrTelCount == 0 && body is not null)
            Add(3, "no contact link (mailto/tel)");

        if (html.Length < 1500)
            Add(5, $"tiny html ({html.Length} bytes)");

        return new ScoreResult(score, reasons);
    }
}

public sealed record ScoreResult(int Score, List<string> Reasons);

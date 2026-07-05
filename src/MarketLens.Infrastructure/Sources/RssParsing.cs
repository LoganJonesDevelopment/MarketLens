using System.Text;
using System.Security.Cryptography;
using System.Xml.Linq;
using MarketLens.Core.Models;

namespace MarketLens.Infrastructure.Sources;

public static class RssParsing
{
    public static IEnumerable<IngestedArticle> ParseItems(XDocument doc, RssFeedConfig config)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var rssItems = doc.Descendants("item");
        foreach (var item in rssItems)
        {
            var title = (string?)item.Element("title") ?? string.Empty;
            var link = (string?)item.Element("link") ?? string.Empty;
            var guid = (string?)item.Element("guid") ?? link;
            var desc = (string?)item.Element("description");
            var pubDate = (string?)item.Element("pubDate");
            var publisher = (string?)item.Element("author") ?? (string?)item.Element("source");
            var published = ParseDate(pubDate);

            var categoryTickers = item.Elements("category")
                .Select(c => (string?)c)
                .Where(v => v != null && (v.Contains("NYSE:") || v.Contains("NASDAQ:")))
                .ToList();
            var summaryWithTickers = categoryTickers.Count > 0
                ? (desc is null ? null : StripHtml(desc)) + " " + string.Join(" ", categoryTickers)
                : (desc is null ? null : StripHtml(desc));

            yield return new IngestedArticle(
                Source: config.Source,
                SourceId: StableSourceId(config.Url, guid),
                Symbol: config.Symbol,
                Headline: HtmlDecode(title),
                Summary: summaryWithTickers,
                Url: link,
                Publisher: publisher,
                PublishedAt: published,
                RawJson: BuildRawJson(title, link, desc, pubDate, publisher));
        }

        var atomEntries = doc.Descendants(atom + "entry");
        foreach (var entry in atomEntries)
        {
            var title = (string?)entry.Element(atom + "title") ?? string.Empty;
            var link = entry.Element(atom + "link")?.Attribute("href")?.Value ?? string.Empty;
            var id = (string?)entry.Element(atom + "id") ?? link;
            var summary = (string?)entry.Element(atom + "summary") ?? (string?)entry.Element(atom + "content");
            var updated = (string?)entry.Element(atom + "updated") ?? (string?)entry.Element(atom + "published");
            var published = ParseDate(updated);

            yield return new IngestedArticle(
                Source: config.Source,
                SourceId: StableSourceId(config.Url, id),
                Symbol: config.Symbol,
                Headline: HtmlDecode(title),
                Summary: summary is null ? null : StripHtml(summary),
                Url: link,
                Publisher: null,
                PublishedAt: published,
                RawJson: BuildRawJson(title, link, summary, updated, null));
        }
    }

    public static DateTime ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.UtcNow;
        if (DateTimeOffset.TryParse(raw, out var dto)) return dto.UtcDateTime;

        // .NET does not parse US timezone abbreviations (EDT, EST, CDT, CST, PDT, PST).
        // Replace them with fixed offsets so standard parsing succeeds.
        var normalized = raw
            .Replace(" EDT", " -04:00")
            .Replace(" EST", " -05:00")
            .Replace(" CDT", " -05:00")
            .Replace(" CST", " -06:00")
            .Replace(" PDT", " -07:00")
            .Replace(" PST", " -08:00")
            .Replace(" MDT", " -06:00")
            .Replace(" MST", " -07:00");
        if (DateTimeOffset.TryParse(normalized, out dto)) return dto.UtcDateTime;

        return DateTime.UtcNow;
    }

    public static string HtmlDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

    public static string StableSourceId(string feedUrl, string rawId)
    {
        var value = $"{feedUrl}|{rawId}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string StripHtml(string s) =>
        System.Text.RegularExpressions.Regex.Replace(HtmlDecode(s), "<.*?>", string.Empty).Trim();

    public static string BuildRawJson(string? title, string? link, string? desc, string? date, string? publisher)
    {
        var sb = new StringBuilder("{");
        sb.Append($"\"title\":{System.Text.Json.JsonSerializer.Serialize(title ?? string.Empty)},");
        sb.Append($"\"link\":{System.Text.Json.JsonSerializer.Serialize(link ?? string.Empty)},");
        sb.Append($"\"description\":{System.Text.Json.JsonSerializer.Serialize(desc ?? string.Empty)},");
        sb.Append($"\"date\":{System.Text.Json.JsonSerializer.Serialize(date ?? string.Empty)},");
        sb.Append($"\"publisher\":{System.Text.Json.JsonSerializer.Serialize(publisher ?? string.Empty)}");
        sb.Append('}');
        return sb.ToString();
    }
}

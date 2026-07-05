namespace MarketLens.Core.Models;

public record IngestedArticle(
    string Source,
    string SourceId,
    string? Symbol,
    string Headline,
    string? Summary,
    string? Url,
    string? Publisher,
    DateTime PublishedAt,
    string RawJson
)
{
    public bool NeedsBodyFetch { get; init; }
    public int BodyFetchDelayMs { get; init; }
}

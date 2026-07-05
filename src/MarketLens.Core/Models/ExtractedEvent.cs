namespace MarketLens.Core.Models;

public record ExtractedEvent(
    string Summary,
    decimal Sentiment,
    string SlotsJson,
    decimal MagnitudeSignal,
    string ModelName,
    string PromptVersion
);

public record ClusterContext(
    string EventType,
    string? Symbol,
    int MemberCount,
    string DominantSourceTier,
    IReadOnlyList<ClusterMember> Members
);

public record ClusterMember(
    string Source,
    string SourceTier,
    string Headline,
    string? Summary,
    string? Publisher,
    DateTime PublishedAt
);

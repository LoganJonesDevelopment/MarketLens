namespace MarketLens.Core.Models;

public sealed record ThesisPlan(
    string Summary,
    IReadOnlyList<TrackedEntity> TrackedEntities,
    IReadOnlyList<ThesisSubTrack> SubTracks,
    IReadOnlyList<string> ConfirmingSignals,
    IReadOnlyList<string> RefutingSignals,
    int CorpusContextSize,
    string Verdict,
    string Leaning,
    string Coverage,
    IReadOnlyList<Guid> StrongestSupportClusterIds,
    IReadOnlyList<Guid> StrongestContradictClusterIds);

public sealed record TrackedEntity(
    string Name,
    string? Symbol,
    string Rationale);

public sealed record ThesisSubTrack(
    string Name,
    string Question,
    string ExpectedDirection,
    IReadOnlyList<string> AssetTerms,
    IReadOnlyList<string> ConceptTerms,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<string> ExcludeTerms);

public sealed record ThesisPlanDigestCluster(
    Guid ClusterId,
    string? Symbol,
    string Headline,
    string? Summary,
    string SourceTier,
    string? EventType,
    decimal? Importance,
    decimal? Sentiment,
    DateTime LastSeenAt,
    decimal Similarity);

public sealed record ThesisPlanContext(
    string ThesisName,
    string ThesisStatement,
    IReadOnlyList<ThesisPlanDigestCluster> Corpus);

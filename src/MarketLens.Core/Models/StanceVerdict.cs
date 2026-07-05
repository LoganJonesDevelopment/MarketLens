namespace MarketLens.Core.Models;

public sealed record StanceContext(
    string ThesisName,
    string ThesisStatement,
    string EventType,
    string Summary,
    string? Symbol,
    int MemberCount,
    string DominantSourceTier,
    IReadOnlyList<ClusterMember> Members);

public sealed record StanceVerdict(
    string Stance,
    decimal Confidence,
    string Rationale,
    string ModelName,
    string PromptVersion);

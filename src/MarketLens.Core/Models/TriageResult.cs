namespace MarketLens.Core.Models;

public record TriageResult(
    string? EventType,
    decimal Confidence,
    IReadOnlyDictionary<string, decimal> AllScores
);

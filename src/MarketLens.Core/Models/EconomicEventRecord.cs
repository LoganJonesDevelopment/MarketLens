namespace MarketLens.Core.Models;

public sealed record EconomicEventRecord(
    string Source,
    string SourceId,
    string EventType,
    string? Symbol,
    string Label,
    DateTime ScheduledAt,
    bool IsTimeSpecific,
    string Status,
    string? Notes,
    string RawJson);

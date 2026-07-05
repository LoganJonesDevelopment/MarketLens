namespace MarketLens.Core.Domain;

public static class ThesisStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Exploration = "exploration";
    public const string Watching = "watching";
    public const string Paused = "paused";
    public const string Validated = "validated";
    public const string Invalidated = "invalidated";
    public const string Archived = "archived";
}

public static class StanceValues
{
    public const string Supports = "supports";
    public const string Contradicts = "contradicts";
    public const string Neutral = "neutral";
    public const string Unknown = "unknown";
}

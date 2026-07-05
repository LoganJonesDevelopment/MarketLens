using MarketLens.Core.Entities;

namespace MarketLens.Api.Services.Ideas;

internal sealed record IdeaEventRow(
    string Symbol,
    Guid ClusterId,
    string EventType,
    string Summary,
    decimal Importance,
    decimal Sentiment,
    string SourceTier,
    int MemberCount,
    DateTime LastSeenAt,
    int PrimaryMembers,
    int WireMembers,
    int TradePressMembers,
    int LowTrustMembers,
    string? TopSource,
    string? TopPublisher,
    string? TopHeadline,
    string? TopUrl,
    decimal? ReactionScore,
    decimal? MovePercent,
    decimal? RelativeMovePercent,
    decimal? RelativeVolume);

internal sealed record IdeaSourceRow(
    string Symbol,
    string Source,
    string SourceTier,
    int Count,
    DateTime LastPublishedAt);

internal sealed record IdeaInsiderRow(
    string Symbol,
    string OwnerName,
    string? OfficerTitle,
    DateTime? TransactionDate,
    string TransactionCode,
    string AcquiredDisposedCode,
    decimal Shares,
    decimal PricePerShare,
    bool IsOpenMarketTrade)
{
    public decimal DollarValue => Shares * PricePerShare;
}

internal sealed record IdeaPriceRow(
    string Symbol,
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long? Volume);

internal sealed record IdeaCalendarRow(
    string Symbol,
    string EventType,
    string Label,
    DateTime ScheduledAt,
    string Status,
    string Source);

internal sealed record IdeaScoutScores(
    decimal EventIntensity,
    decimal SourceQuality,
    decimal PriceAction,
    decimal InsiderSignal,
    decimal HypeRisk,
    decimal MarketReaction);

internal sealed record IdeaPriceDigest(
    decimal? Return7d,
    decimal? Return30d,
    decimal? Return90d,
    decimal? Return1y,
    decimal? LatestClose,
    DateTime? LatestPriceDate);

internal sealed record IdeaValuationDigest(
    bool HasFundamentals,
    decimal? MarketCap,
    decimal? PeTtm,
    decimal? ForwardPe,
    decimal? PsTtm,
    decimal? EvRevenueTtm,
    decimal? RevenueGrowthTtmYoy,
    decimal? EpsGrowthTtmYoy,
    decimal? ValuationRisk,
    DateTime? UpdatedAt);

internal sealed record IdeaEvidenceDigest(
    int EventCount,
    int PrimaryEventCount,
    int SourceCount,
    int PrimarySourceCount,
    int WireSourceCount,
    int TradePressCount,
    int LowTrustCount,
    string? TopEventType,
    decimal MaxImportance);

internal sealed record IdeaInsiderDigest(
    int OpenMarketTransactions,
    decimal NetDollars,
    decimal GrossDollars,
    DateTime? LatestTransactionAt);

internal sealed record IdeaRadarItem(
    string Symbol,
    string Name,
    string Category,
    decimal InterestScore,
    decimal HypeRisk,
    decimal QualityScore,
    string Stance,
    DateTime? LatestSignalAt,
    IdeaScoutScores Scouts,
    IdeaValuationDigest Valuation,
    IdeaPriceDigest Price,
    IdeaEvidenceDigest Evidence,
    IdeaInsiderDigest Insiders,
    IReadOnlyList<string> WhyNow,
    IReadOnlyList<string> HypeCheck,
    IReadOnlyList<string> WatchNext);

internal sealed record ForwardIdeaUniverse(
    IReadOnlyList<ForwardIdeaContext> Contexts,
    int CandidateCount,
    int EventRowCount,
    int SymbolsWithPrices,
    int SymbolsWithFundamentals);

internal sealed record IdeaMarketInputGroups(
    Dictionary<string, List<IdeaEventRow>> Events,
    Dictionary<string, List<IdeaSourceRow>> Sources,
    Dictionary<string, List<IdeaInsiderRow>> Insiders,
    Dictionary<string, List<IdeaPriceRow>> Prices,
    Dictionary<string, List<IdeaCalendarRow>> Calendar,
    Dictionary<string, CompanyFundamentals> Fundamentals);

internal sealed record ForwardThesisSpec(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<ForwardSymbolGroup> Groups);

internal sealed record ForwardSymbolGroup(
    string Key,
    string Label,
    string SetupType,
    decimal Weight,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<ForwardSubcategory>? Subcategories = null,
    IReadOnlyList<string>? Benchmarks = null);

internal sealed record ForwardSubcategory(
    string Label,
    IReadOnlyList<string> Symbols);

internal sealed record ForwardPipelineModule(
    string Key,
    string Label,
    string Description,
    decimal Weight);

internal sealed record ForwardIdeaContext(
    string Symbol,
    IdeaRadarItem Radar,
    List<IdeaEventRow> Events,
    List<IdeaSourceRow> Sources,
    List<IdeaInsiderRow> Insiders,
    List<IdeaPriceRow> Prices,
    List<IdeaCalendarRow> Calendar,
    CompanyFundamentals? Fundamentals);

internal sealed record ForwardModuleResult(
    string Key,
    string Label,
    decimal Score,
    decimal Weight,
    decimal Contribution,
    string Rationale,
    IReadOnlyList<string> Inputs);

internal sealed record ForwardIdeaItem(
    string Symbol,
    string Name,
    string SetupType,
    string? Group,
    string TradeIntent,
    decimal PipelineScore,
    decimal ThesisFit,
    string Actionability,
    decimal CrowdingRisk,
    DateTime? LatestSignalAt,
    IReadOnlyList<ForwardModuleResult> Modules,
    IReadOnlyList<string> Rationale,
    IReadOnlyList<string> NextChecks,
    IReadOnlyList<string> Invalidates,
    IdeaRadarItem Current);

public class ForwardIdeasOptions
{
    public string DefaultPipelineKey { get; set; } = "ai-infrastructure";
    public List<ForwardPipelineOptions> Pipelines { get; set; } = [];
    public List<ForwardPipelineModuleOptions> Modules { get; set; } = [];
}

public class ForwardPipelineOptions
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    public List<ForwardSymbolGroupOptions> Groups { get; set; } = [];
}

public class ForwardSymbolGroupOptions
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SetupType { get; set; } = string.Empty;
    public decimal? Weight { get; set; }
    public List<string> Symbols { get; set; } = [];
    public List<ForwardSubcategoryOptions>? Subcategories { get; set; }
    public List<string>? Benchmarks { get; set; }
}

public class ForwardSubcategoryOptions
{
    public string Label { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = [];
}

public class ForwardPipelineModuleOptions
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Weight { get; set; }
}

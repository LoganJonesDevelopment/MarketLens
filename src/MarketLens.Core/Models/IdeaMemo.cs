namespace MarketLens.Core.Models;

public sealed record IdeaMemoContext(
    string Symbol,
    string? CompanyName,
    int WindowDays,
    DateTime GeneratedAt,
    string EvidenceHash,
    IdeaMemoFundamentalsContext? Fundamentals,
    IdeaMemoPriceContext Price,
    IdeaMemoScoreContext Scores,
    IReadOnlyList<IdeaMemoEventContext> Events,
    IReadOnlyList<IdeaMemoArticleContext> Articles,
    IReadOnlyList<IdeaMemoInsiderContext> Insiders,
    IReadOnlyList<IdeaMemoFilingContext> FilingChunks,
    IReadOnlyList<IdeaMemoTranscriptContext> TranscriptSegments,
    IReadOnlyList<IdeaMemoThesisContext> Theses,
    IReadOnlyList<IdeaMemoCatalystContext> Catalysts,
    IReadOnlyList<string> DataGaps);

public sealed record IdeaMemoFundamentalsContext(
    string EvidenceId,
    string Source,
    DateTime IngestedAt,
    string? Industry,
    string? Currency,
    decimal? MarketCap,
    decimal? EnterpriseValue,
    decimal? PeTtm,
    decimal? ForwardPe,
    decimal? PegTtm,
    decimal? PsTtm,
    decimal? EvRevenueTtm,
    decimal? EvEbitdaTtm,
    decimal? PriceToFreeCashFlowTtm,
    decimal? RevenueGrowthTtmYoy,
    decimal? EpsGrowthTtmYoy,
    decimal? GrossMarginTtm,
    decimal? OperatingMarginTtm,
    decimal? NetMarginTtm,
    decimal? RoeTtm,
    decimal? DebtToEquityQuarterly,
    decimal? Beta);

public sealed record IdeaMemoPriceContext(
    decimal? LatestClose,
    DateTime? LatestDate,
    decimal? Return7d,
    decimal? Return30d,
    decimal? Return90d,
    decimal? Return1y,
    decimal? YtdReturn,
    decimal? RangePosition);

public sealed record IdeaMemoScoreContext(
    decimal InterestScore,
    decimal HypeRisk,
    decimal SourceQuality,
    string Category,
    string Stance);

public sealed record IdeaMemoEventContext(
    string EvidenceId,
    Guid ClusterId,
    string EventType,
    string Summary,
    decimal Importance,
    decimal Sentiment,
    string SourceTier,
    int MemberCount,
    DateTime LastSeenAt,
    decimal? MovePercent,
    decimal? RelativeMovePercent,
    decimal? RelativeVolume,
    decimal? ReactionScore,
    string? TopSource,
    string? TopHeadline,
    string? TopUrl);

public sealed record IdeaMemoArticleContext(
    string EvidenceId,
    Guid ArticleId,
    string Source,
    string SourceTier,
    string? Publisher,
    string Headline,
    string? Summary,
    string? Url,
    DateTime PublishedAt);

public sealed record IdeaMemoInsiderContext(
    string EvidenceId,
    string OwnerName,
    string? OfficerTitle,
    DateTime? TransactionDate,
    string TransactionCode,
    string AcquiredDisposedCode,
    decimal Shares,
    decimal PricePerShare,
    decimal DollarValue,
    bool IsOpenMarketTrade);

public sealed record IdeaMemoFilingContext(
    string EvidenceId,
    Guid ChunkId,
    string? Section,
    int ChunkIndex,
    string Text,
    string FilingHeadline,
    string? FilingUrl,
    DateTime FilingPublishedAt);

public sealed record IdeaMemoTranscriptContext(
    string EvidenceId,
    Guid SegmentId,
    string? CallType,
    DateTime? CallDate,
    int SegmentIndex,
    string? Speaker,
    string Text);

public sealed record IdeaMemoThesisContext(
    string EvidenceId,
    Guid ThesisId,
    string Name,
    string Status,
    string? Summary,
    int Supports,
    int Contradicts,
    int Neutral,
    int Unknown,
    int Total,
    DateTime? LastEvidenceAt);

public sealed record IdeaMemoCatalystContext(
    string EvidenceId,
    string EventType,
    string Label,
    DateTime ScheduledAt,
    string Status,
    string Source);

public sealed record IdeaMemoGenerationResult(
    string MemoJson,
    string ModelName,
    string PromptVersion);

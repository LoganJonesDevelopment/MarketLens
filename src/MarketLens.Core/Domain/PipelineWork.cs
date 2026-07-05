namespace MarketLens.Core.Domain;

public static class PipelineWorkStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string DeadLetter = "dead_letter";
}

public static class PipelineWorkAttemptStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Expired = "expired";
}

public static class PipelineWorkTypes
{
    public const string ArticleIngestion = "article_ingestion";
    public const string ArticleBodyEnrichment = "article_body_enrichment";
    public const string TranscriptIngestion = "transcript_ingestion";
    public const string FilingChunkExtraction = "filing_chunk_extraction";
    public const string Form4Processing = "form4_processing";
    public const string EventExtraction = "event_extraction";
    public const string ResearchMatching = "research_matching";
    public const string StanceClassification = "stance_classification";
    public const string IdeaMemo = "idea_memo";
    public const string EconomicCalendar = "economic_calendar";
    public const string EarningsCalendar = "earnings_calendar";
    public const string FundamentalsRefresh = "fundamentals_refresh";
    public const string ThesisPlanRefresh = "thesis_plan_refresh";
    public const string ThesisBootstrap = "thesis_bootstrap";
    public const string ResearchSnapshot = "research_snapshot";
    public const string MarketQuote = "market_quote";
    public const string PriceBarBackfill = "price_bar_backfill";
    public const string MarketSnapshot = "market_snapshot";
}

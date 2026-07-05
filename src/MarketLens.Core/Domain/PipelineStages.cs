namespace MarketLens.Core.Domain;

public static class PipelineStages
{
    public const string ArticleIngestion = "article_ingestion";
    public const string EventExtraction = "event_extraction";
    public const string ResearchMatcher = "research_matcher";
    public const string StanceClassification = "stance_classification";
    public const string MarketSnapshots = "market_snapshots";
    public const string MarketQuotes = "market_quotes";
    public const string PriceBarBackfill = "price_bar_backfill";
    public const string Fundamentals = "fundamentals";
    public const string IdeaMemo = "idea_memo";
    public const string EconomicCalendar = "economic_calendar";
    public const string EarningsCalendar = "earnings_calendar";
    public const string ThesisPlanRefresh = "thesis_plan_refresh";
    public const string ResearchSnapshot = "research_snapshot";
}

public static class PipelineRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string SucceededWithErrors = "succeeded_with_errors";
    public const string Failed = "failed";
    public const string DeadLetter = "dead_letter";
}

public static class PipelineTriggers
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";
    public const string Backfill = "backfill";
}

public static class PipelineErrorCategories
{
    public const string Transient = "transient";
    public const string Database = "database";
    public const string Cancelled = "cancelled";
    public const string Unexpected = "unexpected";
}

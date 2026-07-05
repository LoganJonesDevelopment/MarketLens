using MarketLens.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace MarketLens.Infrastructure.Data;

public class MarketLensDbContext(DbContextOptions<MarketLensDbContext> options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<SuppressionRecord> Suppressions => Set<SuppressionRecord>();
    public DbSet<MarketSnapshot> MarketSnapshots => Set<MarketSnapshot>();
    public DbSet<ResearchAsset> ResearchAssets => Set<ResearchAsset>();
    public DbSet<ResearchThesis> ResearchTheses => Set<ResearchThesis>();
    public DbSet<ThesisAsset> ThesisAssets => Set<ThesisAsset>();
    public DbSet<ThesisRule> ThesisRules => Set<ThesisRule>();
    public DbSet<ResearchEvidence> ResearchEvidence => Set<ResearchEvidence>();
    public DbSet<ResearchSnapshot> ResearchSnapshots => Set<ResearchSnapshot>();
    public DbSet<PriceBar> PriceBars => Set<PriceBar>();
    public DbSet<PriceBarFetchState> PriceBarFetchStates => Set<PriceBarFetchState>();
    public DbSet<EconomicEvent> EconomicEvents => Set<EconomicEvent>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<ArticleChunk> ArticleChunks => Set<ArticleChunk>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PipelineMaterialization> PipelineMaterializations => Set<PipelineMaterialization>();
    public DbSet<PipelineWorkItem> PipelineWorkItems => Set<PipelineWorkItem>();
    public DbSet<PipelineWorkAttempt> PipelineWorkAttempts => Set<PipelineWorkAttempt>();
    public DbSet<MarketQuote> MarketQuotes => Set<MarketQuote>();
    public DbSet<InsiderTransaction> InsiderTransactions => Set<InsiderTransaction>();
    public DbSet<IdeaMemo> IdeaMemos => Set<IdeaMemo>();
    public DbSet<CompanyFundamentals> CompanyFundamentals => Set<CompanyFundamentals>();
    public DbSet<SourceCursorState> SourceCursorStates => Set<SourceCursorState>();
    public DbSet<LocalFetchCacheEntry> LocalFetchCacheEntries => Set<LocalFetchCacheEntry>();
    public DbSet<ThesisCatalyst> ThesisCatalysts => Set<ThesisCatalyst>();
    public DbSet<ThesisKillCriterion> ThesisKillCriteria => Set<ThesisKillCriterion>();
    public DbSet<ThesisPosition> ThesisPositions => Set<ThesisPosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("articles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SourceTier).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Headline).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(2048);
            entity.Property(e => e.Publisher).HasMaxLength(256);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("vector(1024)");

            entity.HasIndex(e => new { e.Source, e.SourceId }).IsUnique();
            entity.HasIndex(e => e.PublishedAt);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.ClusterId);

            entity.HasOne(e => e.Cluster)
                  .WithMany(c => c.Articles)
                  .HasForeignKey(e => e.ClusterId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Cluster>(entity =>
        {
            entity.ToTable("clusters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.DominantSourceTier).HasMaxLength(32).IsRequired();
            entity.Property(e => e.TopSourceWeight).HasPrecision(4, 3);
            entity.Property(e => e.TriageEventType).HasMaxLength(64);
            entity.Property(e => e.TriageConfidence).HasPrecision(4, 3);

            entity.HasIndex(e => e.FirstSeenAt);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.TriageEventType);
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(e => e.ClusterId);
            entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Summary).IsRequired();
            entity.Property(e => e.Sentiment).HasPrecision(4, 3);
            entity.Property(e => e.Importance).HasPrecision(4, 3);
            entity.Property(e => e.SourceWeight).HasPrecision(4, 3);
            entity.Property(e => e.NoveltyWeight).HasPrecision(4, 3);
            entity.Property(e => e.EventClassPrior).HasPrecision(4, 3);
            entity.Property(e => e.MagnitudeSignal).HasPrecision(4, 3);
            entity.Property(e => e.Slots).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ModelName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PromptVersion).HasMaxLength(32).IsRequired();

            entity.HasOne(e => e.Cluster)
                  .WithOne(c => c.Event)
                  .HasForeignKey<Event>(e => e.ClusterId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Importance);
        });

        modelBuilder.Entity<SuppressionRecord>(entity =>
        {
            entity.ToTable("suppressions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Stage).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(64);
            entity.Property(e => e.Confidence).HasPrecision(4, 3);
            entity.Property(e => e.Headline).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(2048);
            entity.Property(e => e.Publisher).HasMaxLength(256);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.SuppressedAt);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.Reason);
            entity.HasIndex(e => e.Stage);
            entity.HasIndex(e => new { e.Source, e.SourceId, e.Stage, e.Reason }).IsUnique();
        });

        modelBuilder.Entity<MarketSnapshot>(entity =>
        {
            entity.ToTable("market_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.LastPrice).HasPrecision(18, 6);
            entity.Property(e => e.PreviousClose).HasPrecision(18, 6);
            entity.Property(e => e.OpenPrice).HasPrecision(18, 6);
            entity.Property(e => e.HighPrice).HasPrecision(18, 6);
            entity.Property(e => e.LowPrice).HasPrecision(18, 6);
            entity.Property(e => e.MovePercent).HasPrecision(9, 4);
            entity.Property(e => e.BenchmarkSymbol).HasMaxLength(32);
            entity.Property(e => e.BenchmarkMovePercent).HasPrecision(9, 4);
            entity.Property(e => e.RelativeMovePercent).HasPrecision(9, 4);
            entity.Property(e => e.RelativeVolume).HasPrecision(9, 4);
            entity.Property(e => e.ReactionScore).HasPrecision(6, 4);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.CapturedAt);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => new { e.ClusterId, e.CapturedAt });

            entity.HasOne(e => e.Event)
                  .WithMany(e => e.MarketSnapshots)
                  .HasForeignKey(e => e.ClusterId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResearchAsset>(entity =>
        {
            entity.ToTable("research_assets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Keywords).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.Kind);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Symbol);
        });

        modelBuilder.Entity<ResearchThesis>(entity =>
        {
            entity.ToTable("research_theses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.PositionIntent).HasMaxLength(32).IsRequired().HasDefaultValue("none");
            entity.Property(e => e.PositionThesis);
            entity.Property(e => e.ThesisText).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("vector(1024)");
            entity.Property(e => e.Plan).HasColumnType("jsonb");
            entity.Property(e => e.PlanModel).HasMaxLength(128);
            entity.Property(e => e.PlanPromptVersion).HasMaxLength(32);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasIndex(e => e.PositionIntent);
        });

        modelBuilder.Entity<ThesisAsset>(entity =>
        {
            entity.ToTable("thesis_assets");
            entity.HasKey(e => new { e.ThesisId, e.AssetId });
            entity.Property(e => e.Role).HasMaxLength(64).IsRequired();

            entity.HasOne(e => e.Thesis)
                  .WithMany(t => t.ThesisAssets)
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Asset)
                  .WithMany(a => a.ThesisAssets)
                  .HasForeignKey(e => e.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ThesisRule>(entity =>
        {
            entity.ToTable("thesis_rules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.AssetKeywords).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ConceptKeywords).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.EventTypes).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.SourceNames).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.SourceTiers).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ExcludeTerms).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.MinArticleSimilarity).HasPrecision(6, 5);

            entity.HasIndex(e => new { e.ThesisId, e.IsEnabled });

            entity.HasOne(e => e.Thesis)
                  .WithMany(t => t.Rules)
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResearchEvidence>(entity =>
        {
            entity.ToTable("research_evidence");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EvidenceType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.MatchKind).HasMaxLength(32).IsRequired();
            entity.Property(e => e.MatchReason).HasMaxLength(512);
            entity.Property(e => e.Similarity).HasPrecision(6, 5);
            entity.Property(e => e.Stance).HasMaxLength(32).IsRequired();
            entity.Property(e => e.StanceConfidence).HasPrecision(6, 5);
            entity.Property(e => e.StanceRationale).HasMaxLength(2048);
            entity.Property(e => e.OriginalStance).HasMaxLength(32);
            entity.Property(e => e.OriginalStanceConfidence).HasPrecision(6, 5);
            entity.Property(e => e.StanceModel).HasMaxLength(128);
            entity.Property(e => e.StancePromptVersion).HasMaxLength(32);
            entity.Property(e => e.ReviewStatus).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ReviewerNote).HasMaxLength(2048);

            entity.HasIndex(e => e.MatchedAt);
            entity.HasIndex(e => new { e.ThesisId, e.ReviewStatus });
            entity.HasIndex(e => new { e.ThesisId, e.Stance });
            entity.HasIndex(e => new { e.ThesisId, e.ThesisRuleId, e.ArticleId })
                  .IsUnique()
                  .AreNullsDistinct(false)
                  .HasFilter("\"ArticleId\" IS NOT NULL");
            entity.HasIndex(e => new { e.ThesisId, e.ThesisRuleId, e.ClusterId })
                  .IsUnique()
                  .AreNullsDistinct(false)
                  .HasFilter("\"ClusterId\" IS NOT NULL");
            entity.HasIndex(e => new { e.ThesisId, e.TranscriptSegmentId })
                  .IsUnique()
                  .HasFilter("\"TranscriptSegmentId\" IS NOT NULL");
            entity.HasIndex(e => new { e.ThesisId, e.ArticleChunkId })
                  .IsUnique()
                  .HasFilter("\"ArticleChunkId\" IS NOT NULL");

            entity.HasOne(e => e.Thesis)
                  .WithMany(t => t.Evidence)
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ThesisRule)
                  .WithMany(r => r.Evidence)
                  .HasForeignKey(e => e.ThesisRuleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Article)
                  .WithMany()
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Cluster)
                  .WithMany()
                  .HasForeignKey(e => e.ClusterId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TranscriptSegment)
                  .WithMany()
                  .HasForeignKey(e => e.TranscriptSegmentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ResearchSnapshot>(entity =>
        {
            entity.ToTable("research_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Summary).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => new { e.ThesisId, e.SnapshotAt });

            entity.HasOne(e => e.Thesis)
                  .WithMany(t => t.Snapshots)
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceBar>(entity =>
        {
            entity.ToTable("price_bars");
            entity.HasKey(e => new { e.Symbol, e.Interval, e.Timestamp });
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Interval).HasMaxLength(8).IsRequired();
            entity.Property(e => e.Open).HasPrecision(18, 6);
            entity.Property(e => e.High).HasPrecision(18, 6);
            entity.Property(e => e.Low).HasPrecision(18, 6);
            entity.Property(e => e.Close).HasPrecision(18, 6);
            entity.Property(e => e.Source).HasMaxLength(64).IsRequired();

            entity.HasIndex(e => new { e.Symbol, e.Interval, e.Timestamp });
        });

        modelBuilder.Entity<PriceBarFetchState>(entity =>
        {
            entity.ToTable("price_bar_fetch_states");
            entity.HasKey(e => new { e.Symbol, e.Interval, e.Provider });
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Interval).HasMaxLength(8).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ProviderSymbol).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(2048);

            entity.HasIndex(e => e.NextAttemptAt);
            entity.HasIndex(e => new { e.Interval, e.NextAttemptAt });
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
        });

        modelBuilder.Entity<EconomicEvent>(entity =>
        {
            entity.ToTable("economic_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Label).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1024);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => new { e.Source, e.SourceId }).IsUnique();
            entity.HasIndex(e => e.ScheduledAt);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => new { e.Symbol, e.ScheduledAt });

            entity.HasOne(e => e.Cluster)
                  .WithMany()
                  .HasForeignKey(e => e.ClusterId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Transcript>(entity =>
        {
            entity.ToTable("transcripts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.CallType).HasMaxLength(64);
            entity.Property(e => e.AudioUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.CallDate);

            entity.HasOne(e => e.Article)
                  .WithMany()
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TranscriptSegment>(entity =>
        {
            entity.ToTable("transcript_segments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Speaker).HasMaxLength(128);
            entity.Property(e => e.Text).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("vector(1024)");

            entity.HasIndex(e => new { e.TranscriptId, e.SegmentIndex });

            entity.HasOne(e => e.Transcript)
                  .WithMany(t => t.Segments)
                  .HasForeignKey(e => e.TranscriptId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArticleChunk>(entity =>
        {
            entity.ToTable("article_chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Section).HasMaxLength(256);
            entity.Property(e => e.Text).IsRequired();
            entity.Property(e => e.Embedding).HasColumnType("vector(1024)");

            entity.HasIndex(e => e.ArticleId);
            entity.HasIndex(e => new { e.ArticleId, e.ChunkIndex });

            entity.HasOne(e => e.Article)
                  .WithMany()
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineRun>(entity =>
        {
            entity.ToTable("pipeline_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Stage).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ScopeType).HasMaxLength(64);
            entity.Property(e => e.ScopeKey).HasMaxLength(256);
            entity.Property(e => e.Trigger).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ErrorCategory).HasMaxLength(64);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Stage, e.StartedAt });
            entity.HasIndex(e => new { e.Stage, e.Status, e.StartedAt });
            entity.HasIndex(e => new { e.ScopeType, e.ScopeKey });
        });

        modelBuilder.Entity<PipelineMaterialization>(entity =>
        {
            entity.ToTable("pipeline_materializations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssetType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.AssetKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PartitionKey).HasMaxLength(256);
            entity.Property(e => e.DataVersion).HasMaxLength(128);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.MaterializedAt);
            entity.HasIndex(e => new { e.AssetKey, e.PartitionKey, e.MaterializedAt });
            entity.HasIndex(e => new { e.RunId, e.AssetKey });

            entity.HasOne(e => e.Run)
                  .WithMany(r => r.Materializations)
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PipelineWorkItem>(entity =>
        {
            entity.ToTable("pipeline_work_items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WorkType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.NaturalKey).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ClaimedBy).HasMaxLength(128);
            entity.Property(e => e.LastError).HasMaxLength(2048);

            entity.HasIndex(e => new { e.WorkType, e.NaturalKey })
                  .IsUnique()
                  .HasFilter("\"Status\" IN ('queued', 'running')");
            entity.HasIndex(e => new { e.WorkType, e.Status, e.AvailableAt, e.Priority });
            entity.HasIndex(e => e.LeaseExpiresAt);
            entity.HasIndex(e => e.CurrentAttemptId);
        });

        modelBuilder.Entity<PipelineWorkAttempt>(entity =>
        {
            entity.ToTable("pipeline_work_attempts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.WorkerId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);

            entity.HasIndex(e => new { e.WorkItemId, e.AttemptNumber }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.LeaseExpiresAt });
            entity.HasIndex(e => e.WorkerId);

            entity.HasOne(e => e.WorkItem)
                  .WithMany(i => i.Attempts)
                  .HasForeignKey(e => e.WorkItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceCursorState>(entity =>
        {
            entity.ToTable("source_cursor_states");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SourceKey).HasMaxLength(256).IsRequired();
            entity.Property(e => e.CursorJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.LastItemId).HasMaxLength(512);
            entity.Property(e => e.LastError).HasMaxLength(2048);

            entity.HasIndex(e => new { e.SourceName, e.SourceKey }).IsUnique();
            entity.HasIndex(e => e.NextEligibleRunAt);
            entity.HasIndex(e => new { e.SourceName, e.NextEligibleRunAt });
        });

        modelBuilder.Entity<LocalFetchCacheEntry>(entity =>
        {
            entity.ToTable("local_fetch_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CacheKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(256);
            entity.Property(e => e.ETag).HasMaxLength(512);
            entity.Property(e => e.LastModified).HasMaxLength(256);
            entity.Property(e => e.ErrorText).HasMaxLength(2048);

            entity.HasIndex(e => e.CacheKey).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => new { e.Source, e.ExpiresAt });
        });

        modelBuilder.Entity<ResearchEvidence>()
            .HasOne(e => e.ArticleChunk)
            .WithMany()
            .HasForeignKey(e => e.ArticleChunkId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ThesisCatalyst>(entity =>
        {
            entity.ToTable("thesis_catalysts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2048);
            entity.Property(e => e.Metal).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CatalystType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Outcome).HasMaxLength(2048);

            entity.HasIndex(e => e.CatalystDate);
            entity.HasIndex(e => new { e.ThesisId, e.CatalystDate });

            entity.HasOne(e => e.Thesis)
                  .WithMany()
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ThesisKillCriterion>(entity =>
        {
            entity.ToTable("thesis_kill_criteria");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Scenario).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.MonitoringKeywords).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.ThreatLevel).HasMaxLength(32).IsRequired();
            entity.Property(e => e.LastTriggeredReason).HasMaxLength(2048);

            entity.HasIndex(e => e.ThesisId);
            entity.HasIndex(e => e.ThreatLevel);

            entity.HasOne(e => e.Thesis)
                  .WithMany()
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ThesisPosition>(entity =>
        {
            entity.ToTable("thesis_positions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Metal).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TargetAllocationPct).HasPrecision(6, 3);
            entity.Property(e => e.DeployedPct).HasPrecision(6, 3);
            entity.Property(e => e.EntryPrice).HasPrecision(18, 6);
            entity.Property(e => e.ScaleInTriggerPrice).HasPrecision(18, 6);
            entity.Property(e => e.ScaleInNotes).HasMaxLength(2048);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => e.ThesisId);
            entity.HasIndex(e => e.Symbol);
            entity.HasIndex(e => e.Metal);

            entity.HasOne(e => e.Thesis)
                  .WithMany()
                  .HasForeignKey(e => e.ThesisId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketQuote>(entity =>
        {
            entity.ToTable("market_quotes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(256);
            entity.Property(e => e.InstrumentType).HasMaxLength(64);
            entity.Property(e => e.Exchange).HasMaxLength(64);
            entity.Property(e => e.Currency).HasMaxLength(16);
            entity.Property(e => e.Last).HasPrecision(18, 6);
            entity.Property(e => e.PreviousClose).HasPrecision(18, 6);
            entity.Property(e => e.Change).HasPrecision(18, 6);
            entity.Property(e => e.ChangePercent).HasPrecision(9, 4);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(512);

            entity.HasIndex(e => new { e.Provider, e.Symbol }).IsUnique();
            entity.HasIndex(e => e.AsOf);
        });

        modelBuilder.Entity<CompanyFundamentals>(entity =>
        {
            entity.ToTable("company_fundamentals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(512);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.Exchange).HasMaxLength(128);
            entity.Property(e => e.Industry).HasMaxLength(128);
            entity.Property(e => e.Currency).HasMaxLength(16);
            entity.Property(e => e.WebUrl).HasMaxLength(2048);
            entity.Property(e => e.MarketCapitalizationMillion).HasPrecision(20, 4);
            entity.Property(e => e.ShareOutstandingMillion).HasPrecision(20, 4);
            entity.Property(e => e.EnterpriseValueMillion).HasPrecision(20, 4);
            entity.Property(e => e.PeTtm).HasPrecision(18, 4);
            entity.Property(e => e.ForwardPe).HasPrecision(18, 4);
            entity.Property(e => e.PegTtm).HasPrecision(18, 6);
            entity.Property(e => e.PsTtm).HasPrecision(18, 4);
            entity.Property(e => e.EvRevenueTtm).HasPrecision(18, 4);
            entity.Property(e => e.EvEbitdaTtm).HasPrecision(18, 4);
            entity.Property(e => e.PriceToBook).HasPrecision(18, 4);
            entity.Property(e => e.PriceToFreeCashFlowTtm).HasPrecision(18, 4);
            entity.Property(e => e.RevenueGrowthTtmYoy).HasPrecision(18, 4);
            entity.Property(e => e.EpsGrowthTtmYoy).HasPrecision(18, 4);
            entity.Property(e => e.GrossMarginTtm).HasPrecision(18, 4);
            entity.Property(e => e.OperatingMarginTtm).HasPrecision(18, 4);
            entity.Property(e => e.NetMarginTtm).HasPrecision(18, 4);
            entity.Property(e => e.RoeTtm).HasPrecision(18, 4);
            entity.Property(e => e.DebtToEquityQuarterly).HasPrecision(18, 4);
            entity.Property(e => e.Beta).HasPrecision(18, 6);
            entity.Property(e => e.Week52High).HasPrecision(18, 6);
            entity.Property(e => e.Week52Low).HasPrecision(18, 6);
            entity.Property(e => e.Week52PriceReturnDaily).HasPrecision(18, 4);
            entity.Property(e => e.RawProfileJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.RawMetricJson).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => new { e.Provider, e.Symbol }).IsUnique();
            entity.HasIndex(e => new { e.Symbol, e.IngestedAt });
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<InsiderTransaction>(entity =>
        {
            entity.ToTable("insider_transactions");
            entity.HasKey(e => new { e.ArticleId, e.LineNumber });

            entity.Property(e => e.IssuerCik).HasMaxLength(16).IsRequired();
            entity.Property(e => e.IssuerName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.IssuerSymbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.OwnerCik).HasMaxLength(16).IsRequired();
            entity.Property(e => e.OwnerName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.OfficerTitle).HasMaxLength(256);
            entity.Property(e => e.SecurityTitle).HasMaxLength(256);
            entity.Property(e => e.TransactionCode).HasMaxLength(8).IsRequired();
            entity.Property(e => e.AcquiredDisposedCode).HasMaxLength(4).IsRequired();
            entity.Property(e => e.Shares).HasPrecision(20, 4);
            entity.Property(e => e.PricePerShare).HasPrecision(18, 6);
            entity.Property(e => e.SharesOwnedFollowing).HasPrecision(20, 4);
            entity.Property(e => e.DirectOrIndirectOwnership).HasMaxLength(8);

            entity.HasIndex(e => e.IssuerSymbol);
            entity.HasIndex(e => e.OwnerName);
            entity.HasIndex(e => e.TransactionDate);
            entity.HasIndex(e => new { e.IssuerSymbol, e.TransactionDate });
            entity.HasIndex(e => new { e.IssuerSymbol, e.IsOpenMarketTrade, e.TransactionDate });

            entity.HasOne(e => e.Article)
                  .WithMany()
                  .HasForeignKey(e => e.ArticleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdeaMemo>(entity =>
        {
            entity.ToTable("idea_memos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(e => e.EvidenceHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.EvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.MemoJson).HasColumnType("jsonb");
            entity.Property(e => e.ModelName).HasMaxLength(128);
            entity.Property(e => e.PromptVersion).HasMaxLength(32);
            entity.Property(e => e.Error).HasMaxLength(2048);

            entity.HasIndex(e => new { e.Symbol, e.WindowDays, e.EvidenceHash }).IsUnique();
            entity.HasIndex(e => new { e.Symbol, e.WindowDays, e.UpdatedAt });
            entity.HasIndex(e => new { e.Status, e.RequestedAt });
        });
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services;

public sealed record IdeaMemoDto(
    string Symbol,
    int WindowDays,
    string EvidenceHash,
    string CurrentEvidenceHash,
    string Status,
    string CurrentStatus,
    bool IsCurrent,
    DateTime RequestedAt,
    DateTime? StartedAt,
    DateTime? GeneratedAt,
    DateTime? CompletedAt,
    string? ModelName,
    string? PromptVersion,
    JsonElement? Memo,
    string? Error);

public sealed record IdeaMemoBuildResult(
    IdeaMemoContext Context,
    string EvidenceJson);

public sealed record IdeaEvidenceDto(
    string Symbol,
    int WindowDays,
    string EvidenceId,
    string EvidenceType,
    string Title,
    string? Subtitle,
    string? Summary,
    object Data);

public class IdeaMemoService(
    MarketLensDbContext db,
    IIdeaMemoGenerator generator,
    ILogger<IdeaMemoService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdeaMemoDto> GetOrQueueAsync(
        string symbol,
        int windowDays,
        CancellationToken cancellationToken)
    {
        var build = await BuildContextAsync(symbol, windowDays, cancellationToken);
        var current = await FindMemoAsync(build.Context.Symbol, build.Context.WindowDays, build.Context.EvidenceHash, cancellationToken);
        if (current is null)
        {
            current = NewMemo(build.Context, build.EvidenceJson, IdeaMemoStatuses.Pending);
            db.IdeaMemos.Add(current);
            await db.SaveChangesAsync(cancellationToken);
        }

        var returned = current.Status == IdeaMemoStatuses.Ready
            ? current
            : await FindLatestReadyAsync(build.Context.Symbol, build.Context.WindowDays, cancellationToken) ?? current;

        return ToDto(returned, current);
    }

    public async Task<IdeaMemoDto> RefreshAsync(
        string symbol,
        int windowDays,
        bool force,
        CancellationToken cancellationToken)
    {
        var memo = await GenerateAndStoreAsync(symbol, windowDays, force, cancellationToken);
        return ToDto(memo, memo);
    }

    public async Task<IdeaMemo> GenerateAndStoreAsync(
        string symbol,
        int windowDays,
        bool force,
        CancellationToken cancellationToken)
    {
        var build = await BuildContextAsync(symbol, windowDays, cancellationToken);
        var memo = await FindMemoAsync(build.Context.Symbol, build.Context.WindowDays, build.Context.EvidenceHash, cancellationToken);
        if (memo is not null &&
            memo.Status == IdeaMemoStatuses.Ready &&
            !force &&
            string.Equals(memo.PromptVersion, generator.PromptVersion, StringComparison.Ordinal))
            return memo;

        var now = DateTime.UtcNow;
        if (memo is null)
        {
            memo = NewMemo(build.Context, build.EvidenceJson, IdeaMemoStatuses.Running);
            db.IdeaMemos.Add(memo);
        }
        else
        {
            PrepareForGeneration(memo, build.EvidenceJson, now);
        }

        memo.StartedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return await GenerateIntoMemoAsync(memo, build.Context, cancellationToken);
    }

    public async Task<int> ProcessPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        var pendingIds = await db.IdeaMemos
            .Where(m => m.Status == IdeaMemoStatuses.Pending)
            .OrderBy(m => m.RequestedAt)
            .Take(Math.Max(1, maxCount))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in pendingIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var memo = await ProcessPendingMemoAsync(id, cancellationToken);
            if (memo is not null) processed++;
        }

        return processed;
    }

    public async Task<IdeaMemo?> ProcessPendingMemoAsync(Guid memoId, CancellationToken cancellationToken)
    {
        var memo = await db.IdeaMemos
            .FirstOrDefaultAsync(m => m.Id == memoId, cancellationToken);
        if (memo is null || memo.Status != IdeaMemoStatuses.Pending) return null;

        var build = await BuildContextAsync(memo.Symbol, memo.WindowDays, cancellationToken);
        if (!string.Equals(memo.EvidenceHash, build.Context.EvidenceHash, StringComparison.Ordinal))
        {
            await SupersedePendingMemoAsync(memo, build, cancellationToken);
            return memo;
        }

        PrepareForGeneration(memo, build.EvidenceJson, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GenerateIntoMemoAsync(memo, build.Context, cancellationToken);
    }

    private async Task<IdeaMemo> GenerateIntoMemoAsync(
        IdeaMemo memo,
        IdeaMemoContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await generator.GenerateAsync(context, cancellationToken);
            memo.MemoJson = NormalizeMemoCitations(result.MemoJson, context);
            memo.ModelName = result.ModelName;
            memo.PromptVersion = result.PromptVersion;
            memo.GeneratedAt = DateTime.UtcNow;
            memo.CompletedAt = memo.GeneratedAt;
            memo.Status = IdeaMemoStatuses.Ready;
            memo.Error = null;
            memo.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return memo;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Idea memo generation failed for {Symbol}", context.Symbol);
            memo.Status = IdeaMemoStatuses.Failed;
            memo.CompletedAt = DateTime.UtcNow;
            memo.Error = Truncate(ex.Message, 2048);
            memo.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return memo;
        }
    }

    private async Task SupersedePendingMemoAsync(
        IdeaMemo memo,
        IdeaMemoBuildResult current,
        CancellationToken cancellationToken)
    {
        var replacement = await FindMemoAsync(
            current.Context.Symbol,
            current.Context.WindowDays,
            current.Context.EvidenceHash,
            cancellationToken);

        var now = DateTime.UtcNow;
        memo.Status = IdeaMemoStatuses.Superseded;
        memo.CompletedAt = now;
        memo.Error = $"Superseded by newer evidence hash {current.Context.EvidenceHash}.";
        memo.UpdatedAt = now;

        if (replacement is null)
        {
            db.IdeaMemos.Add(NewMemo(current.Context, current.EvidenceJson, IdeaMemoStatuses.Pending));
        }

        logger.LogInformation(
            "Superseded stale idea memo for {Symbol} {WindowDays}d: {OldHash} -> {NewHash}",
            memo.Symbol,
            memo.WindowDays,
            memo.EvidenceHash,
            current.Context.EvidenceHash);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> LoadMemoCandidateSymbolsAsync(
        int windowDays,
        int take,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(windowDays, 7, 365));
        return await db.Events
            .AsNoTracking()
            .Where(e => e.Cluster != null && e.Cluster.Symbol != null && e.Cluster.LastSeenAt >= cutoff)
            .GroupBy(e => e.Cluster!.Symbol!, e => new { e.Importance, e.Cluster!.LastSeenAt })
            .Select(g => new
            {
                Symbol = g.Key,
                Score = g.Sum(e => e.Importance + 0.05m),
                Latest = g.Max(e => e.LastSeenAt),
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Latest)
            .Take(Math.Clamp(take, 1, 50))
            .Select(x => x.Symbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<IdeaMemoBuildResult> BuildContextAsync(
        string rawSymbol,
        int rawWindowDays,
        CancellationToken cancellationToken)
    {
        var symbol = rawSymbol.Trim().ToUpperInvariant();
        if (!IsEquitySymbol(symbol))
            throw new ArgumentException("equity symbol required", nameof(rawSymbol));

        var windowDays = Math.Clamp(rawWindowDays, 7, 365);
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-windowDays);
        var priceStart = now.AddDays(-390);
        var insiderStart = now.AddDays(-Math.Max(180, windowDays));
        var meta = TickerMetadata.Lookup(symbol);

        var rawEvents = await LoadEventContextRowsAsync(symbol, windowStart, cancellationToken);
        var events = FilterCompanyContext(symbol, meta, rawEvents)
            .OrderByDescending(e => e.Importance)
            .ThenByDescending(e => e.LastSeenAt)
            .Take(16)
            .ToList();
        var articles = await LoadArticleContextsAsync(symbol, windowStart, cancellationToken);
        var insiders = await LoadInsiderContextsAsync(symbol, insiderStart, cancellationToken);
        var filings = await LoadFilingContextsAsync(symbol, windowStart, cancellationToken);
        var transcripts = await LoadTranscriptContextsAsync(symbol, cancellationToken);
        var theses = await LoadThesisContextsAsync(symbol, cancellationToken);
        var catalysts = await LoadCatalystContextsAsync(symbol, now.AddDays(-7), now.AddDays(120), cancellationToken);
        var prices = await LoadPriceRowsAsync(symbol, priceStart, cancellationToken);
        var fundamentals = await LoadFundamentalsContextAsync(symbol, cancellationToken);

        var price = BuildPriceContext(prices);
        var scores = BuildScoreContext(events, articles, insiders, price, fundamentals);
        var dataGaps = BuildDataGaps(price, fundamentals, filings, transcripts, theses);

        var fingerprint = new
        {
            symbol,
            windowDays,
            fundamentals = fundamentals is null
                ? null
                : new { fundamentals.IngestedAt, fundamentals.MarketCap, fundamentals.PeTtm, fundamentals.ForwardPe, fundamentals.PsTtm, fundamentals.EvRevenueTtm, fundamentals.RevenueGrowthTtmYoy, fundamentals.GrossMarginTtm },
            price,
            scores,
            events = events.Select(e => new { e.ClusterId, e.LastSeenAt, e.Importance, e.Summary }),
            articles = articles.Select(a => new { a.ArticleId, a.PublishedAt, a.SourceTier, a.Headline }),
            insiders = insiders.Select(i => new { i.EvidenceId, i.TransactionDate, i.AcquiredDisposedCode, i.DollarValue }),
            filings = filings.Select(f => new { f.ChunkId, f.Section, f.ChunkIndex, f.FilingPublishedAt }),
            transcripts = transcripts.Select(t => new { t.SegmentId, t.CallDate, t.SegmentIndex }),
            theses = theses.Select(t => new { t.ThesisId, t.Total, t.Supports, t.Contradicts, t.LastEvidenceAt }),
            catalysts = catalysts.Select(c => new { c.EventType, c.Label, c.ScheduledAt, c.Status }),
        };
        var evidenceHash = ComputeHash(fingerprint);

        var context = new IdeaMemoContext(
            Symbol: symbol,
            CompanyName: meta?.CompanyName,
            WindowDays: windowDays,
            GeneratedAt: now,
            EvidenceHash: evidenceHash,
            Fundamentals: fundamentals,
            Price: price,
            Scores: scores,
            Events: events,
            Articles: articles,
            Insiders: insiders,
            FilingChunks: filings,
            TranscriptSegments: transcripts,
            Theses: theses,
            Catalysts: catalysts,
            DataGaps: dataGaps);

        return new IdeaMemoBuildResult(context, JsonSerializer.Serialize(context, JsonOptions));
    }

    public async Task<IdeaEvidenceDto?> ResolveEvidenceAsync(
        string rawSymbol,
        int rawWindowDays,
        string rawEvidenceId,
        string? evidenceHash,
        CancellationToken cancellationToken)
    {
        var id = rawEvidenceId.Trim();
        if (string.IsNullOrWhiteSpace(id)) return null;

        var symbol = rawSymbol.Trim().ToUpperInvariant();
        var windowDays = Math.Clamp(rawWindowDays, 7, 365);
        var context = await LoadStoredContextAsync(symbol, windowDays, evidenceHash, cancellationToken)
            ?? (await BuildContextAsync(symbol, windowDays, cancellationToken)).Context;

        if (string.Equals(id, "price", StringComparison.OrdinalIgnoreCase))
        {
            return Evidence(context, id, "price", "Price Context", context.Symbol, null, context.Price);
        }

        if (string.Equals(id, "scores", StringComparison.OrdinalIgnoreCase))
        {
            return Evidence(context, id, "scores", "Research Scores", context.Symbol, null, context.Scores);
        }

        if (string.Equals(id, "fundamentals", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Fundamentals is null) return null;
            return Evidence(context, id, "fundamentals", "Fundamentals", context.CompanyName ?? context.Symbol, null, context.Fundamentals);
        }

        if (string.Equals(id, "dataGaps", StringComparison.OrdinalIgnoreCase))
        {
            return Evidence(context, id, "dataGaps", "Missing Inputs", context.Symbol, null, context.DataGaps);
        }

        var evt = context.Events.FirstOrDefault(e => IdEquals(e.EvidenceId, id));
        if (evt is not null)
        {
            return Evidence(context, evt.EvidenceId, "event", evt.EventType.Replace('_', ' '), evt.TopHeadline, evt.Summary, evt);
        }

        var article = context.Articles.FirstOrDefault(a => IdEquals(a.EvidenceId, id));
        if (article is not null)
        {
            return Evidence(context, article.EvidenceId, "article", article.Headline, article.Publisher ?? article.Source, article.Summary, article);
        }

        var insider = context.Insiders.FirstOrDefault(i => IdEquals(i.EvidenceId, id))
            ?? context.Insiders.FirstOrDefault(i => i.EvidenceId.StartsWith(id + ":", StringComparison.OrdinalIgnoreCase));
        if (insider is not null)
        {
            var side = insider.AcquiredDisposedCode == "A" ? "buy/acquire" : "sell/dispose";
            return Evidence(context, insider.EvidenceId, "insider", insider.OwnerName, insider.OfficerTitle ?? side, side, insider);
        }

        var filing = context.FilingChunks.FirstOrDefault(f => IdEquals(f.EvidenceId, id));
        if (filing is not null)
        {
            return Evidence(context, filing.EvidenceId, "filing", filing.Section ?? "Filing chunk", filing.FilingHeadline, filing.Text, filing);
        }

        var segment = context.TranscriptSegments.FirstOrDefault(s => IdEquals(s.EvidenceId, id));
        if (segment is not null)
        {
            return Evidence(context, segment.EvidenceId, "transcript", segment.Speaker ?? "Transcript segment", segment.CallType, segment.Text, segment);
        }

        var thesis = context.Theses.FirstOrDefault(t => IdEquals(t.EvidenceId, id));
        if (thesis is not null)
        {
            return Evidence(context, thesis.EvidenceId, "thesis", thesis.Name, thesis.Status, thesis.Summary, thesis);
        }

        var catalyst = context.Catalysts.FirstOrDefault(c => IdEquals(c.EvidenceId, id));
        if (catalyst is not null)
        {
            return Evidence(context, catalyst.EvidenceId, "catalyst", catalyst.Label, catalyst.Status, catalyst.EventType, catalyst);
        }

        return null;
    }

    private async Task<IdeaMemoContext?> LoadStoredContextAsync(
        string symbol,
        int windowDays,
        string? evidenceHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evidenceHash)) return null;

        var row = await db.IdeaMemos
            .AsNoTracking()
            .Where(m => m.Symbol == symbol && m.WindowDays == windowDays && m.EvidenceHash == evidenceHash)
            .OrderByDescending(m => m.GeneratedAt ?? m.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;
        return JsonSerializer.Deserialize<IdeaMemoContext>(row.EvidenceJson, JsonOptions);
    }

    private async Task<IdeaMemo?> FindMemoAsync(
        string symbol,
        int windowDays,
        string evidenceHash,
        CancellationToken cancellationToken) =>
        await db.IdeaMemos
            .FirstOrDefaultAsync(m =>
                m.Symbol == symbol &&
                m.WindowDays == windowDays &&
                m.EvidenceHash == evidenceHash,
                cancellationToken);

    private static IdeaEvidenceDto Evidence(
        IdeaMemoContext context,
        string evidenceId,
        string type,
        string title,
        string? subtitle,
        string? summary,
        object data) =>
        new(
            Symbol: context.Symbol,
            WindowDays: context.WindowDays,
            EvidenceId: evidenceId,
            EvidenceType: type,
            Title: title,
            Subtitle: subtitle,
            Summary: summary,
            Data: data);

    private static bool IdEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private async Task<IdeaMemo?> FindLatestReadyAsync(
        string symbol,
        int windowDays,
        CancellationToken cancellationToken) =>
        await db.IdeaMemos
            .Where(m => m.Symbol == symbol && m.WindowDays == windowDays && m.Status == IdeaMemoStatuses.Ready)
            .OrderByDescending(m => m.GeneratedAt ?? m.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static IdeaMemo NewMemo(IdeaMemoContext context, string evidenceJson, string status)
    {
        var now = DateTime.UtcNow;
        return new IdeaMemo
        {
            Id = Guid.NewGuid(),
            Symbol = context.Symbol,
            WindowDays = context.WindowDays,
            EvidenceHash = context.EvidenceHash,
            Status = status,
            EvidenceJson = evidenceJson,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static void PrepareForGeneration(IdeaMemo memo, string evidenceJson, DateTime now)
    {
        memo.Status = IdeaMemoStatuses.Running;
        memo.EvidenceJson = evidenceJson;
        memo.StartedAt = now;
        memo.CompletedAt = null;
        memo.Error = null;
        memo.UpdatedAt = now;
    }

    private static IdeaMemoDto ToDto(IdeaMemo returned, IdeaMemo current)
    {
        return new IdeaMemoDto(
            Symbol: current.Symbol,
            WindowDays: current.WindowDays,
            EvidenceHash: returned.EvidenceHash,
            CurrentEvidenceHash: current.EvidenceHash,
            Status: returned.Status,
            CurrentStatus: current.Status,
            IsCurrent: returned.EvidenceHash == current.EvidenceHash && returned.Status == IdeaMemoStatuses.Ready,
            RequestedAt: returned.RequestedAt,
            StartedAt: returned.StartedAt,
            GeneratedAt: returned.GeneratedAt,
            CompletedAt: returned.CompletedAt,
            ModelName: returned.ModelName,
            PromptVersion: returned.PromptVersion,
            Memo: ParseMemo(returned.MemoJson),
            Error: returned.Error ?? current.Error);
    }

    private static JsonElement? ParseMemo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task<List<IdeaMemoEventContext>> LoadEventContextRowsAsync(
        string symbol,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await db.Events
            .AsNoTracking()
            .Where(e => e.Cluster != null && e.Cluster.Symbol == symbol && e.Cluster.LastSeenAt >= windowStart)
            .OrderByDescending(e => e.Importance)
            .ThenByDescending(e => e.Cluster!.LastSeenAt)
            .Take(80)
            .Select(e => new
            {
                Event = e,
                TopArticle = e.Cluster!.Articles
                    .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                    .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => new { a.Source, a.Headline, a.Url })
                    .FirstOrDefault(),
            })
            .Select(x => new IdeaMemoEventContext(
                "EVT:" + x.Event.ClusterId,
                x.Event.ClusterId,
                x.Event.EventType,
                x.Event.Summary,
                x.Event.Importance,
                x.Event.Sentiment,
                x.Event.Cluster!.DominantSourceTier,
                x.Event.Cluster.MemberCount,
                x.Event.Cluster.LastSeenAt,
                x.Event.MarketSnapshots.OrderByDescending(s => s.CapturedAt).Select(s => s.MovePercent).FirstOrDefault(),
                x.Event.MarketSnapshots.OrderByDescending(s => s.CapturedAt).Select(s => s.RelativeMovePercent).FirstOrDefault(),
                x.Event.MarketSnapshots.OrderByDescending(s => s.CapturedAt).Select(s => s.RelativeVolume).FirstOrDefault(),
                x.Event.MarketSnapshots.OrderByDescending(s => s.CapturedAt).Select(s => (decimal?)s.ReactionScore).FirstOrDefault(),
                x.TopArticle == null ? null : x.TopArticle.Source,
                x.TopArticle == null ? null : x.TopArticle.Headline,
                x.TopArticle == null ? null : x.TopArticle.Url))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoArticleContext>> LoadArticleContextsAsync(
        string symbol,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await db.Articles
            .AsNoTracking()
            .Where(a => a.Symbol == symbol && a.PublishedAt >= windowStart)
            .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
            .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
            .ThenByDescending(a => a.PublishedAt)
            .Take(28)
            .Select(a => new IdeaMemoArticleContext(
                "ART:" + a.Id,
                a.Id,
                a.Source,
                a.SourceTier,
                a.Publisher,
                a.Headline,
                a.Summary == null ? null : Truncate(a.Summary, 500),
                a.Url,
                a.PublishedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoInsiderContext>> LoadInsiderContextsAsync(
        string symbol,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await db.InsiderTransactions
            .AsNoTracking()
            .Where(i => i.IssuerSymbol == symbol && i.TransactionDate >= windowStart && i.Shares != null && i.PricePerShare != null)
            .OrderByDescending(i => i.TransactionDate)
            .Take(16)
            .Select(i => new IdeaMemoInsiderContext(
                "INS:" + i.ArticleId + ":" + i.LineNumber,
                i.OwnerName,
                i.OfficerTitle,
                i.TransactionDate,
                i.TransactionCode,
                i.AcquiredDisposedCode,
                i.Shares ?? 0m,
                i.PricePerShare ?? 0m,
                (i.Shares ?? 0m) * (i.PricePerShare ?? 0m),
                i.IsOpenMarketTrade))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoFilingContext>> LoadFilingContextsAsync(
        string symbol,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await db.ArticleChunks
            .AsNoTracking()
            .Where(c => c.Article != null && c.Article.Symbol == symbol && c.Article.PublishedAt >= windowStart)
            .OrderByDescending(c => c.Article!.PublishedAt)
            .ThenBy(c => c.ChunkIndex)
            .Take(10)
            .Select(c => new IdeaMemoFilingContext(
                "CHK:" + c.Id,
                c.Id,
                c.Section,
                c.ChunkIndex,
                Truncate(c.Text, 900),
                c.Article!.Headline,
                c.Article.Url,
                c.Article.PublishedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoTranscriptContext>> LoadTranscriptContextsAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        return await db.TranscriptSegments
            .AsNoTracking()
            .Where(s => s.Transcript != null && s.Transcript.Symbol == symbol && s.Transcript.Status == TranscriptStatus.Completed)
            .OrderByDescending(s => s.Transcript!.CallDate ?? s.Transcript.CompletedAt ?? s.Transcript.IngestedAt)
            .ThenBy(s => s.SegmentIndex)
            .Take(14)
            .Select(s => new IdeaMemoTranscriptContext(
                "SEG:" + s.Id,
                s.Id,
                s.Transcript!.CallType,
                s.Transcript.CallDate,
                s.SegmentIndex,
                s.Speaker,
                Truncate(s.Text, 700)))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoThesisContext>> LoadThesisContextsAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        return await db.ResearchTheses
            .AsNoTracking()
            .Where(t => t.ThesisAssets.Any(ta => ta.Asset!.Symbol == symbol))
            .OrderByDescending(t => t.Evidence.Count)
            .Take(8)
            .Select(t => new IdeaMemoThesisContext(
                "THESIS:" + t.Id,
                t.Id,
                t.Name,
                t.Status,
                t.Summary,
                t.Evidence.Count(e => e.Stance == StanceValues.Supports),
                t.Evidence.Count(e => e.Stance == StanceValues.Contradicts),
                t.Evidence.Count(e => e.Stance == StanceValues.Neutral),
                t.Evidence.Count(e => e.Stance == StanceValues.Unknown),
                t.Evidence.Count,
                t.Evidence.OrderByDescending(e => e.MatchedAt).Select(e => (DateTime?)e.MatchedAt).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoCatalystContext>> LoadCatalystContextsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        return await db.EconomicEvents
            .AsNoTracking()
            .Where(e => e.Symbol == symbol && e.ScheduledAt >= from && e.ScheduledAt <= to)
            .OrderBy(e => e.ScheduledAt)
            .Take(10)
            .Select(e => new IdeaMemoCatalystContext(
                "CAT:" + e.Id,
                e.EventType,
                e.Label,
                e.ScheduledAt,
                e.Status,
                e.Source))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<IdeaMemoPriceRow>> LoadPriceRowsAsync(
        string symbol,
        DateTime from,
        CancellationToken cancellationToken)
    {
        return await db.PriceBars
            .AsNoTracking()
            .Where(b => b.Symbol == symbol && b.Interval == "1d" && b.Timestamp >= from)
            .OrderBy(b => b.Timestamp)
            .Select(b => new IdeaMemoPriceRow(b.Timestamp, b.Open, b.High, b.Low, b.Close))
            .ToListAsync(cancellationToken);
    }

    private async Task<IdeaMemoFundamentalsContext?> LoadFundamentalsContextAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var row = await db.CompanyFundamentals
            .AsNoTracking()
            .Where(f => f.Symbol == symbol && f.Status == "ok")
            .OrderByDescending(f => f.IngestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        return new IdeaMemoFundamentalsContext(
            EvidenceId: "fundamentals",
            Source: row.Provider,
            IngestedAt: row.IngestedAt,
            Industry: row.Industry,
            Currency: row.Currency,
            MarketCap: ToDollars(row.MarketCapitalizationMillion),
            EnterpriseValue: ToDollars(row.EnterpriseValueMillion),
            PeTtm: row.PeTtm,
            ForwardPe: row.ForwardPe,
            PegTtm: row.PegTtm,
            PsTtm: row.PsTtm,
            EvRevenueTtm: row.EvRevenueTtm,
            EvEbitdaTtm: row.EvEbitdaTtm,
            PriceToFreeCashFlowTtm: row.PriceToFreeCashFlowTtm,
            RevenueGrowthTtmYoy: row.RevenueGrowthTtmYoy,
            EpsGrowthTtmYoy: row.EpsGrowthTtmYoy,
            GrossMarginTtm: row.GrossMarginTtm,
            OperatingMarginTtm: row.OperatingMarginTtm,
            NetMarginTtm: row.NetMarginTtm,
            RoeTtm: row.RoeTtm,
            DebtToEquityQuarterly: row.DebtToEquityQuarterly,
            Beta: row.Beta);
    }

    private static List<IdeaMemoEventContext> FilterCompanyContext(
        string symbol,
        TickerMetadataEntry? meta,
        List<IdeaMemoEventContext> rows)
    {
        if (rows.Count == 0) return rows;
        var contextual = rows
            .Where(e => HasCompanyContext(symbol, meta, e.Summary, e.TopHeadline))
            .ToList();
        if (contextual.Count > 0) return contextual;
        return rows.Where(e => e.TopSource is SourceNames.Edgar or SourceNames.IrFeed or SourceNames.Transcript).ToList();
    }

    private static bool HasCompanyContext(string symbol, TickerMetadataEntry? meta, params string?[] texts)
    {
        var text = string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (ContainsTickerToken(text, symbol)) return true;
        if (text.Contains($"${symbol}", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains($"({symbol})", StringComparison.OrdinalIgnoreCase)) return true;
        return CompanyNeedles(symbol, meta).Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsTickerToken(string text, string symbol)
    {
        var index = text.IndexOf(symbol, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + symbol.Length;
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk) return true;
            index = text.IndexOf(symbol, index + symbol.Length, StringComparison.Ordinal);
        }

        return false;
    }

    private static IEnumerable<string> CompanyNeedles(string symbol, TickerMetadataEntry? meta)
    {
        if (meta is null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in meta.Aliases.Concat([meta.CompanyName]))
        {
            var trimmed = alias.Trim();
            if (trimmed.Length >= 4 && !string.Equals(trimmed, symbol, StringComparison.OrdinalIgnoreCase) && seen.Add(trimmed))
                yield return trimmed;
            foreach (var token in CompanyTokens(trimmed))
            {
                if (!string.Equals(token, symbol, StringComparison.OrdinalIgnoreCase) && seen.Add(token))
                    yield return token;
            }
        }
    }

    private static IEnumerable<string> CompanyTokens(string name)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "inc", "corp", "corporation", "company", "co", "ltd", "limited", "plc",
            "holdings", "holding", "group", "class", "ordinary", "technologies", "technology",
        };

        foreach (var token in name.Split([' ', '.', ',', '-', '&', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 4 && !stop.Contains(token))
                yield return token;
        }
    }

    private static IdeaMemoPriceContext BuildPriceContext(List<IdeaMemoPriceRow> rows)
    {
        var latest = rows.LastOrDefault();
        if (latest is null)
            return new IdeaMemoPriceContext(null, null, null, null, null, null, null, null);

        var high = rows.Max(p => p.High);
        var low = rows.Min(p => p.Low);
        var ytdStart = rows.Where(p => p.Timestamp.Year == DateTime.UtcNow.Year).OrderBy(p => p.Timestamp).FirstOrDefault();
        return new IdeaMemoPriceContext(
            LatestClose: latest.Close,
            LatestDate: latest.Timestamp,
            Return7d: ReturnSince(rows, 7),
            Return30d: ReturnSince(rows, 30),
            Return90d: ReturnSince(rows, 90),
            Return1y: ReturnSince(rows, 365),
            YtdReturn: ytdStart is null || ytdStart.Close == 0 ? null : Math.Round((latest.Close / ytdStart.Close - 1m) * 100m, 2),
            RangePosition: high == low ? null : Math.Round((latest.Close - low) / (high - low) * 100m, 1));
    }

    private const decimal EventIntensityPerEventImportanceCap = 0.75m;
    private const decimal EventIntensityImportanceSumWeight = 0.40m;
    private const decimal EventIntensityEventCountWeight = 0.035m;
    private const decimal EventIntensityMaxImportanceWeight = 0.55m;

    private const decimal SourceQualityPrimaryRatioWeight = 0.78m;
    private const decimal SourceQualityWireRatioWeight = 0.42m;
    private const decimal SourceQualityPrimaryEventWeight = 0.035m;
    private const decimal SourceQualityMaxImportanceWeight = 0.28m;

    private const decimal PriceActionReturn7dScale = 18m;
    private const decimal PriceActionReturn7dWeight = 0.25m;
    private const decimal PriceActionReturn30dScale = 38m;
    private const decimal PriceActionReturn30dWeight = 0.45m;
    private const decimal PriceActionReturn90dScale = 75m;
    private const decimal PriceActionReturn90dWeight = 0.30m;

    private const double InsiderSignalLogOffset = 10d;
    private const decimal InsiderSignalLogDivisor = 8m;

    private const int NoveltyFallbackSignalAgeDays = 365;
    private const decimal NoveltyDecayWindowDays = 14m;

    private const decimal InterestEventIntensityWeight = 0.34m;
    private const decimal InterestSourceQualityWeight = 0.21m;
    private const decimal InterestReactionWeight = 0.15m;
    private const decimal InterestPriceActionWeight = 0.14m;
    private const decimal InterestInsiderSignalWeight = 0.10m;
    private const decimal InterestNoveltyWeight = 0.06m;

    private const decimal HypeCheckInterestThreshold = 50m;
    private const decimal HypeCheckHypeRiskThreshold = 0.62m;
    private const decimal DeepDiveInterestThreshold = 58m;
    private const decimal DeepDiveSourceQualityThreshold = 0.42m;
    private const decimal WatchInterestThreshold = 38m;

    private const decimal HypePriceHeatReturn30dScale = 45m;
    private const decimal HypePriceHeatReturn30dWeight = 0.55m;
    private const decimal HypePriceHeatReturn90dScale = 95m;
    private const decimal HypePriceHeatReturn90dWeight = 0.45m;
    private const decimal HypeLowPrimaryRatioFloor = 0.32m;
    private const decimal HypeChatterLowTrustMultiplier = 1.25m;
    private const decimal HypeChatterAnalystActionWeight = 0.08m;
    private const decimal HypeThinEventImportanceFloor = 0.35m;
    private const decimal HypePriceHeatWeight = 0.30m;
    private const decimal HypeChatterWeight = 0.20m;
    private const decimal HypeLowPrimaryPenaltyWeight = 0.18m;
    private const decimal HypeThinEventReactionWeight = 0.14m;
    private const decimal HypeValuationRiskWeight = 0.18m;

    private const decimal PeTtmFairMultiple = 18m;
    private const decimal PeTtmStretchedMultiple = 45m;
    private const decimal PeTtmExtremeMultiple = 90m;
    private const decimal ForwardPeFairMultiple = 16m;
    private const decimal ForwardPeStretchedMultiple = 35m;
    private const decimal ForwardPeExtremeMultiple = 65m;
    private const decimal PsTtmFairMultiple = 3m;
    private const decimal PsTtmStretchedMultiple = 9m;
    private const decimal PsTtmExtremeMultiple = 18m;
    private const decimal EvRevenueFairMultiple = 3m;
    private const decimal EvRevenueStretchedMultiple = 9m;
    private const decimal EvRevenueExtremeMultiple = 18m;
    private const decimal PriceToFcfFairMultiple = 18m;
    private const decimal PriceToFcfStretchedMultiple = 45m;
    private const decimal PriceToFcfExtremeMultiple = 90m;
    private const decimal StretchedBandHeatCeiling = 0.65m;
    private const decimal ExtremeBandHeatShare = 0.35m;

    private const decimal ValuationPriceHeatReturn30dScale = 35m;
    private const decimal ValuationPriceHeatReturn30dWeight = 0.45m;
    private const decimal ValuationPriceHeatReturn90dScale = 80m;
    private const decimal ValuationPriceHeatReturn90dWeight = 0.55m;
    private const decimal GrowthSupportRevenueScale = 40m;
    private const decimal GrowthSupportRevenueWeight = 0.62m;
    private const decimal GrowthSupportEpsScale = 65m;
    private const decimal GrowthSupportEpsWeight = 0.38m;
    private const decimal QualitySupportGrossMarginScale = 65m;
    private const decimal QualitySupportGrossMarginWeight = 0.55m;
    private const decimal QualitySupportOperatingMarginScale = 35m;
    private const decimal QualitySupportOperatingMarginWeight = 0.45m;
    private const decimal LeveragePenaltyDebtToEquityBaseline = 0.75m;
    private const decimal LeveragePenaltyDebtToEquityRange = 2m;
    private const decimal BetaPenaltyBaseline = 1.2m;
    private const decimal BetaPenaltyRange = 1.8m;
    private const decimal ValuationMultipleHeatWeight = 0.58m;
    private const decimal ValuationPriceHeatWeight = 0.18m;
    private const decimal ValuationLeveragePenaltyWeight = 0.08m;
    private const decimal ValuationBetaPenaltyWeight = 0.06m;
    private const decimal ValuationGrowthSupportWeight = 0.14m;
    private const decimal ValuationQualitySupportWeight = 0.08m;

    private static IdeaMemoScoreContext BuildScoreContext(
        List<IdeaMemoEventContext> events,
        List<IdeaMemoArticleContext> articles,
        List<IdeaMemoInsiderContext> insiders,
        IdeaMemoPriceContext price,
        IdeaMemoFundamentalsContext? fundamentals)
    {
        var maxImportance = events.Count == 0 ? 0m : events.Max(e => e.Importance);
        var eventIntensity = Clamp01(
            events.Sum(e => Math.Min(e.Importance, EventIntensityPerEventImportanceCap)) * EventIntensityImportanceSumWeight
            + events.Count * EventIntensityEventCountWeight
            + maxImportance * EventIntensityMaxImportanceWeight);
        var totalArticles = Math.Max(1, articles.Count);
        var primaryRatio = (decimal)articles.Count(a => a.SourceTier == SourceTiers.Primary) / totalArticles;
        var wireRatio = (decimal)articles.Count(a => a.SourceTier == SourceTiers.Wire) / totalArticles;
        var sourceQuality = Clamp01(
            primaryRatio * SourceQualityPrimaryRatioWeight
            + wireRatio * SourceQualityWireRatioWeight
            + events.Count(e => e.SourceTier == SourceTiers.Primary) * SourceQualityPrimaryEventWeight
            + maxImportance * SourceQualityMaxImportanceWeight);
        var lowTrustRatio = (decimal)articles.Count(a => a.SourceTier is SourceTiers.Aggregator or SourceTiers.Opinion) / totalArticles;
        var analystCount = events.Count(e => e.EventType == EventTypes.AnalystAction);
        var reaction = events.Select(e => e.ReactionScore ?? 0m).DefaultIfEmpty(0m).Max();
        var priceAction = Clamp01(
            Abs(price.Return7d) / PriceActionReturn7dScale * PriceActionReturn7dWeight
            + Abs(price.Return30d) / PriceActionReturn30dScale * PriceActionReturn30dWeight
            + Abs(price.Return90d) / PriceActionReturn90dScale * PriceActionReturn90dWeight);
        var open = insiders.Where(i => i.IsOpenMarketTrade).ToList();
        var netInsiderDollars = open.Sum(i => i.DollarValue * (i.AcquiredDisposedCode == "A" ? 1m : -1m));
        var insiderSignal = Clamp01((decimal)Math.Log10((double)Math.Abs(netInsiderDollars) + InsiderSignalLogOffset) / InsiderSignalLogDivisor);
        var latestSignal = events.Select(e => (DateTime?)e.LastSeenAt)
            .Concat(articles.Select(a => (DateTime?)a.PublishedAt))
            .Concat(open.Select(i => i.TransactionDate))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(DateTime.UtcNow.AddDays(-NoveltyFallbackSignalAgeDays))
            .Max();
        var novelty = Clamp01(1m - (decimal)Math.Max(0, (DateTime.UtcNow - latestSignal).TotalDays) / NoveltyDecayWindowDays);
        var valuationRisk = ComputeValuationRisk(fundamentals, price.Return30d, price.Return90d);
        var hypeRisk = ComputeHypeRisk(price.Return30d, price.Return90d, lowTrustRatio, primaryRatio, analystCount, maxImportance, reaction, valuationRisk);
        var interest = Math.Round(100m * Clamp01(
            eventIntensity * InterestEventIntensityWeight
            + sourceQuality * InterestSourceQualityWeight
            + reaction * InterestReactionWeight
            + priceAction * InterestPriceActionWeight
            + insiderSignal * InterestInsiderSignalWeight
            + novelty * InterestNoveltyWeight), 1);
        var category = interest >= HypeCheckInterestThreshold && hypeRisk >= HypeCheckHypeRiskThreshold ? "hype-check"
            : interest >= DeepDiveInterestThreshold && sourceQuality >= DeepDiveSourceQualityThreshold ? "deep-dive"
            : interest >= WatchInterestThreshold ? "watch"
            : "thin";
        var stance = DirectionFor(price.Return30d, netInsiderDollars, events);

        return new IdeaMemoScoreContext(
            InterestScore: interest,
            HypeRisk: Math.Round(hypeRisk * 100m, 1),
            SourceQuality: Math.Round(sourceQuality * 100m, 1),
            Category: category,
            Stance: stance);
    }

    private static IReadOnlyList<string> BuildDataGaps(
        IdeaMemoPriceContext price,
        IdeaMemoFundamentalsContext? fundamentals,
        IReadOnlyList<IdeaMemoFilingContext> filings,
        IReadOnlyList<IdeaMemoTranscriptContext> transcripts,
        IReadOnlyList<IdeaMemoThesisContext> theses)
    {
        var gaps = new List<string>();
        if (fundamentals is null)
            gaps.Add("Structured fundamentals and valuation multiples are not ingested yet; overpricing analysis is based on price action, source quality, events, and insider flow.");
        else if (fundamentals.PeTtm is null && fundamentals.ForwardPe is null && fundamentals.PsTtm is null && fundamentals.EvRevenueTtm is null)
            gaps.Add("Fundamentals are cached, but the source did not return usable valuation multiples.");
        if (price.LatestClose is null) gaps.Add("No daily price history is available.");
        if (filings.Count == 0) gaps.Add("No filing chunks are available in this evidence window.");
        if (transcripts.Count == 0) gaps.Add("No completed transcript segments are available.");
        if (theses.Count == 0) gaps.Add("No formal thesis is attached; this is discovery mode only.");
        return gaps;
    }

    private static decimal? ReturnSince(List<IdeaMemoPriceRow> rows, int days)
    {
        if (rows.Count < 2) return null;
        var latest = rows.Last();
        var target = latest.Timestamp.AddDays(-days);
        var prior = rows.Where(p => p.Timestamp <= target).OrderBy(p => p.Timestamp).LastOrDefault()
                    ?? rows.FirstOrDefault();
        if (prior is null || prior.Close == 0m || prior.Timestamp == latest.Timestamp) return null;
        return Math.Round((latest.Close / prior.Close - 1m) * 100m, 2);
    }

    private static decimal ComputeHypeRisk(
        decimal? ret30,
        decimal? ret90,
        decimal lowTrustRatio,
        decimal primaryRatio,
        int analystCount,
        decimal maxImportance,
        decimal reaction,
        decimal? valuationRisk)
    {
        var priceHeat = Clamp01(
            Math.Max(0m, ret30 ?? 0m) / HypePriceHeatReturn30dScale * HypePriceHeatReturn30dWeight
            + Math.Max(0m, ret90 ?? 0m) / HypePriceHeatReturn90dScale * HypePriceHeatReturn90dWeight);
        var lowPrimaryPenalty = Clamp01((HypeLowPrimaryRatioFloor - primaryRatio) / HypeLowPrimaryRatioFloor);
        var chatter = Clamp01(lowTrustRatio * HypeChatterLowTrustMultiplier + analystCount * HypeChatterAnalystActionWeight);
        var thinEvent = Clamp01((HypeThinEventImportanceFloor - maxImportance) / HypeThinEventImportanceFloor);
        var valuation = valuationRisk ?? 0m;
        return Clamp01(
            priceHeat * HypePriceHeatWeight
            + chatter * HypeChatterWeight
            + lowPrimaryPenalty * HypeLowPrimaryPenaltyWeight
            + thinEvent * reaction * HypeThinEventReactionWeight
            + valuation * HypeValuationRiskWeight);
    }

    private static decimal? ComputeValuationRisk(
        IdeaMemoFundamentalsContext? fundamentals,
        decimal? ret30,
        decimal? ret90)
    {
        if (fundamentals is null) return null;

        var peHeat = MultipleHeat(fundamentals.PeTtm, PeTtmFairMultiple, PeTtmStretchedMultiple, PeTtmExtremeMultiple);
        var forwardPeHeat = MultipleHeat(fundamentals.ForwardPe, ForwardPeFairMultiple, ForwardPeStretchedMultiple, ForwardPeExtremeMultiple);
        var psHeat = MultipleHeat(fundamentals.PsTtm, PsTtmFairMultiple, PsTtmStretchedMultiple, PsTtmExtremeMultiple);
        var evSalesHeat = MultipleHeat(fundamentals.EvRevenueTtm, EvRevenueFairMultiple, EvRevenueStretchedMultiple, EvRevenueExtremeMultiple);
        var fcfHeat = MultipleHeat(fundamentals.PriceToFreeCashFlowTtm, PriceToFcfFairMultiple, PriceToFcfStretchedMultiple, PriceToFcfExtremeMultiple);
        var multipleHeat = new[] { peHeat, forwardPeHeat, psHeat, evSalesHeat, fcfHeat }
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0m)
            .Average();

        var priceHeat = Clamp01(
            Math.Max(0m, ret30 ?? 0m) / ValuationPriceHeatReturn30dScale * ValuationPriceHeatReturn30dWeight
            + Math.Max(0m, ret90 ?? 0m) / ValuationPriceHeatReturn90dScale * ValuationPriceHeatReturn90dWeight);
        var growthSupport = Clamp01(Math.Max(0m, fundamentals.RevenueGrowthTtmYoy ?? 0m) / GrowthSupportRevenueScale * GrowthSupportRevenueWeight
            + Math.Max(0m, fundamentals.EpsGrowthTtmYoy ?? 0m) / GrowthSupportEpsScale * GrowthSupportEpsWeight);
        var qualitySupport = Clamp01(Math.Max(0m, fundamentals.GrossMarginTtm ?? 0m) / QualitySupportGrossMarginScale * QualitySupportGrossMarginWeight
            + Math.Max(0m, fundamentals.OperatingMarginTtm ?? 0m) / QualitySupportOperatingMarginScale * QualitySupportOperatingMarginWeight);
        var leveragePenalty = Clamp01(((fundamentals.DebtToEquityQuarterly ?? 0m) - LeveragePenaltyDebtToEquityBaseline) / LeveragePenaltyDebtToEquityRange);
        var betaPenalty = Clamp01(((fundamentals.Beta ?? 1m) - BetaPenaltyBaseline) / BetaPenaltyRange);

        return Clamp01(
            multipleHeat * ValuationMultipleHeatWeight
            + priceHeat * ValuationPriceHeatWeight
            + leveragePenalty * ValuationLeveragePenaltyWeight
            + betaPenalty * ValuationBetaPenaltyWeight
            - growthSupport * ValuationGrowthSupportWeight
            - qualitySupport * ValuationQualitySupportWeight);
    }

    private static decimal? MultipleHeat(decimal? value, decimal fair, decimal stretched, decimal extreme)
    {
        if (!value.HasValue || value.Value <= 0m) return null;
        if (value.Value <= fair) return 0m;
        if (value.Value >= extreme) return 1m;
        if (value.Value <= stretched)
            return (value.Value - fair) / (stretched - fair) * StretchedBandHeatCeiling;
        return StretchedBandHeatCeiling + (value.Value - stretched) / (extreme - stretched) * ExtremeBandHeatShare;
    }

    private static string DirectionFor(decimal? ret30, decimal netInsiderDollars, List<IdeaMemoEventContext> events)
    {
        var avgSentiment = events.Count == 0 ? 0m : events.Average(e => e.Sentiment);
        var score = avgSentiment;
        if ((ret30 ?? 0m) > 8m) score += 0.15m;
        if ((ret30 ?? 0m) < -8m) score -= 0.15m;
        if (netInsiderDollars > 500_000m) score += 0.15m;
        if (netInsiderDollars < -500_000m) score -= 0.15m;
        return score switch
        {
            > 0.25m => "constructive",
            < -0.25m => "caution",
            _ => "mixed",
        };
    }

    private static string NormalizeMemoCitations(string memoJson, IdeaMemoContext context)
    {
        var root = JsonNode.Parse(memoJson) ?? throw new InvalidOperationException("Idea memo JSON could not be parsed");
        var allowed = BuildEvidenceIdSet(context);
        var aliases = BuildEvidenceAliases(context, allowed);
        NormalizeEvidenceIdArrays(root, allowed, aliases);
        return root.ToJsonString(JsonOptions);
    }

    private static HashSet<string> BuildEvidenceIdSet(IdeaMemoContext context)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "price",
            "scores",
            "fundamentals",
            "dataGaps",
        };

        foreach (var item in context.Events) ids.Add(item.EvidenceId);
        foreach (var item in context.Articles) ids.Add(item.EvidenceId);
        foreach (var item in context.Insiders) ids.Add(item.EvidenceId);
        foreach (var item in context.FilingChunks) ids.Add(item.EvidenceId);
        foreach (var item in context.TranscriptSegments) ids.Add(item.EvidenceId);
        foreach (var item in context.Theses) ids.Add(item.EvidenceId);
        foreach (var item in context.Catalysts) ids.Add(item.EvidenceId);
        return ids;
    }

    private static Dictionary<string, string> BuildEvidenceAliases(IdeaMemoContext context, HashSet<string> allowed)
    {
        var aliases = allowed.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);

        foreach (var item in context.Insiders)
        {
            var suffixIndex = item.EvidenceId.LastIndexOf(':');
            if (suffixIndex > "INS:".Length)
            {
                aliases.TryAdd(item.EvidenceId[..suffixIndex], item.EvidenceId);
            }
        }

        return aliases;
    }

    private static void NormalizeEvidenceIdArrays(
        JsonNode node,
        HashSet<string> allowed,
        Dictionary<string, string> aliases)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["evidenceIds"] is JsonArray evidenceIds)
                {
                    obj["evidenceIds"] = NormalizeEvidenceIdArray(evidenceIds, allowed, aliases);
                }

                foreach (var child in obj.Select(property => property.Value).Where(value => value is not null).ToList())
                {
                    NormalizeEvidenceIdArrays(child!, allowed, aliases);
                }

                break;

            case JsonArray array:
                foreach (var child in array.Where(value => value is not null).ToList())
                {
                    NormalizeEvidenceIdArrays(child!, allowed, aliases);
                }

                break;
        }
    }

    private static JsonArray NormalizeEvidenceIdArray(
        JsonArray evidenceIds,
        HashSet<string> allowed,
        Dictionary<string, string> aliases)
    {
        var normalized = new List<string>();
        foreach (var item in evidenceIds)
        {
            var raw = item?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var key = raw.Trim();
            if (!aliases.TryGetValue(key, out var canonical) || !allowed.Contains(canonical)) continue;
            if (normalized.Contains(canonical, StringComparer.OrdinalIgnoreCase)) continue;
            normalized.Add(canonical);
        }

        var array = new JsonArray();
        foreach (var item in normalized)
        {
            array.Add(item);
        }

        return array;
    }

    private static string ComputeHash(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static decimal Abs(decimal? value) => Math.Abs(value ?? 0m);

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    private static decimal? ToDollars(decimal? millions) =>
        millions.HasValue ? Math.Round(millions.Value * 1_000_000m, 0) : null;

    private static bool IsEquitySymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var s = symbol.Trim();
        if (s.Contains(':') || s.Contains('=') || s.Contains("-USD", StringComparison.OrdinalIgnoreCase)) return false;
        return s.All(c => char.IsLetterOrDigit(c) || c == '.');
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}

internal sealed record IdeaMemoPriceRow(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);

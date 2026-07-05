using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Ideas;

public class IdeaMarketDataLoader(MarketLensDbContext db)
{
    internal async Task<List<IdeaEventRow>> LoadEventRowsAsync(DateTime windowStart, CancellationToken ct, string? symbol = null)
    {
        var q = db.Events
            .AsNoTracking()
            .Where(e => e.Cluster != null && e.Cluster.Symbol != null && e.Cluster.LastSeenAt >= windowStart);

        if (!string.IsNullOrWhiteSpace(symbol))
            q = q.Where(e => e.Cluster!.Symbol == symbol);

        return await q
            .OrderByDescending(e => e.Importance)
            .Take(symbol is null ? 5000 : 500)
            .Select(e => new IdeaEventRow(
                e.Cluster!.Symbol!,
                e.ClusterId,
                e.EventType,
                e.Summary,
                e.Importance,
                e.Sentiment,
                e.Cluster.DominantSourceTier,
                e.Cluster.MemberCount,
                e.Cluster.LastSeenAt,
                e.Cluster.Articles.Count(a => a.SourceTier == SourceTiers.Primary),
                e.Cluster.Articles.Count(a => a.SourceTier == SourceTiers.Wire),
                e.Cluster.Articles.Count(a => a.SourceTier == SourceTiers.TradePress),
                e.Cluster.Articles.Count(a => a.SourceTier == SourceTiers.Aggregator || a.SourceTier == SourceTiers.Opinion),
                e.Cluster.Articles
                    .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                    .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => a.Source)
                    .FirstOrDefault(),
                e.Cluster.Articles
                    .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                    .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => a.Publisher)
                    .FirstOrDefault(),
                e.Cluster.Articles
                    .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                    .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => a.Headline)
                    .FirstOrDefault(),
                e.Cluster.Articles
                    .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                    .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => a.Url)
                    .FirstOrDefault(),
                e.MarketSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .Select(s => (decimal?)s.ReactionScore)
                    .FirstOrDefault(),
                e.MarketSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .Select(s => s.MovePercent)
                    .FirstOrDefault(),
                e.MarketSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .Select(s => s.RelativeMovePercent)
                    .FirstOrDefault(),
                e.MarketSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .Select(s => s.RelativeVolume)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    internal async Task<List<IdeaSourceRow>> LoadArticleSourceRowsAsync(DateTime windowStart, CancellationToken ct, string? symbol = null)
    {
        var q = db.Articles
            .AsNoTracking()
            .Where(a => a.Symbol != null && a.PublishedAt >= windowStart);

        if (!string.IsNullOrWhiteSpace(symbol))
            q = q.Where(a => a.Symbol == symbol);

        return await q
            .GroupBy(a => new { Symbol = a.Symbol!, a.Source, a.SourceTier })
            .Select(g => new IdeaSourceRow(g.Key.Symbol, g.Key.Source, g.Key.SourceTier, g.Count(), g.Max(a => a.PublishedAt)))
            .ToListAsync(ct);
    }

    internal async Task<List<IdeaInsiderRow>> LoadInsiderRowsAsync(DateTime windowStart, CancellationToken ct, string? symbol = null)
    {
        var q = db.InsiderTransactions
            .AsNoTracking()
            .Where(i => i.TransactionDate >= windowStart && i.Shares != null && i.PricePerShare != null);

        if (!string.IsNullOrWhiteSpace(symbol))
            q = q.Where(i => i.IssuerSymbol == symbol);

        return await q
            .Select(i => new IdeaInsiderRow(
                i.IssuerSymbol,
                i.OwnerName,
                i.OfficerTitle,
                i.TransactionDate,
                i.TransactionCode,
                i.AcquiredDisposedCode,
                i.Shares ?? 0m,
                i.PricePerShare ?? 0m,
                i.IsOpenMarketTrade))
            .ToListAsync(ct);
    }

    internal async Task<List<IdeaPriceRow>> LoadPriceRowsAsync(IReadOnlyCollection<string> symbols, DateTime from, CancellationToken ct)
    {
        if (symbols.Count == 0) return [];
        return await db.PriceBars
            .AsNoTracking()
            .Where(b => b.Interval == "1d" && symbols.Contains(b.Symbol) && b.Timestamp >= from)
            .OrderBy(b => b.Timestamp)
            .Select(b => new IdeaPriceRow(b.Symbol, b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume))
            .ToListAsync(ct);
    }

    internal async Task<List<IdeaCalendarRow>> LoadCalendarRowsAsync(
        IReadOnlyCollection<string> symbols,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        if (symbols.Count == 0) return [];
        return await db.EconomicEvents
            .AsNoTracking()
            .Where(e => e.Symbol != null && symbols.Contains(e.Symbol) && e.ScheduledAt >= from && e.ScheduledAt <= to)
            .OrderBy(e => e.ScheduledAt)
            .Select(e => new IdeaCalendarRow(e.Symbol!, e.EventType, e.Label, e.ScheduledAt, e.Status, e.Source))
            .ToListAsync(ct);
    }

    internal async Task<List<CompanyFundamentals>> LoadFundamentalsAsync(IReadOnlyCollection<string> symbols, CancellationToken ct)
    {
        if (symbols.Count == 0) return [];
        return await db.CompanyFundamentals
            .AsNoTracking()
            .Where(f => symbols.Contains(f.Symbol) && f.Status == "ok")
            .OrderByDescending(f => f.IngestedAt)
            .ToListAsync(ct);
    }

    internal async Task<List<object>> LoadThesisRowsAsync(string symbol, CancellationToken ct)
    {
        return await db.ResearchTheses
            .AsNoTracking()
            .Where(t => t.ThesisAssets.Any(ta => ta.Asset!.Symbol == symbol))
            .OrderByDescending(t => t.Evidence.Count)
            .Take(10)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Status,
                t.Summary,
                supports = t.Evidence.Count(e => e.Stance == "supports"),
                contradicts = t.Evidence.Count(e => e.Stance == "contradicts"),
                neutral = t.Evidence.Count(e => e.Stance == "neutral"),
                unknown = t.Evidence.Count(e => e.Stance == "unknown"),
                total = t.Evidence.Count,
                lastEvidenceAt = t.Evidence
                    .OrderByDescending(e => e.MatchedAt)
                    .Select(e => (DateTime?)e.MatchedAt)
                    .FirstOrDefault(),
            })
            .Cast<object>()
            .ToListAsync(ct);
    }

    internal async Task<List<object>> LoadTranscriptRowsAsync(string symbol, CancellationToken ct)
    {
        return await db.Transcripts
            .AsNoTracking()
            .Where(t => t.Symbol == symbol)
            .OrderByDescending(t => t.CallDate ?? t.CompletedAt ?? t.IngestedAt)
            .Take(6)
            .Select(t => new
            {
                t.Id,
                t.CallType,
                t.CallDate,
                t.Status,
                t.SegmentCount,
                t.DurationSeconds,
                t.CompletedAt,
                t.AudioUrl,
            })
            .Cast<object>()
            .ToListAsync(ct);
    }

    internal async Task<List<object>> LoadFilingChunkRowsAsync(string symbol, DateTime windowStart, CancellationToken ct)
    {
        return await db.ArticleChunks
            .AsNoTracking()
            .Where(c => c.Article != null && c.Article.Symbol == symbol && c.Article.PublishedAt >= windowStart)
            .OrderByDescending(c => c.Article!.PublishedAt)
            .ThenBy(c => c.ChunkIndex)
            .Take(10)
            .Select(c => new
            {
                c.Id,
                c.Section,
                c.ChunkIndex,
                text = c.Text.Length > 700 ? c.Text.Substring(0, 700) : c.Text,
                filingHeadline = c.Article!.Headline,
                filingUrl = c.Article.Url,
                filingPublishedAt = c.Article.PublishedAt,
            })
            .Cast<object>()
            .ToListAsync(ct);
    }
}

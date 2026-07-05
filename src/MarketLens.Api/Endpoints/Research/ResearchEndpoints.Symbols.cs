using MarketLens.Core.Domain;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapSymbolEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/insiders", async (
            MarketLensDbContext db,
            int? lookbackDays,
            int? minDollars,
            int? take,
            CancellationToken ct) =>
        {
            var window = Math.Clamp(lookbackDays ?? 30, 1, 365);
            var minDollarsFilter = (decimal)Math.Max(0, minDollars ?? 100_000);
            var limit = Math.Clamp(take ?? 100, 1, 500);
            var cutoff = DateTime.UtcNow.AddDays(-window);

            var rows = await db.InsiderTransactions
                .AsNoTracking()
                .Where(i => i.IsOpenMarketTrade
                            && i.TransactionDate >= cutoff
                            && i.Shares != null
                            && i.PricePerShare != null)
                .Select(i => new
                {
                    i.IssuerSymbol, i.OwnerName, i.OfficerTitle,
                    i.IsDirector, i.IsTenPercentOwner,
                    i.TransactionDate, i.AcquiredDisposedCode,
                    i.Shares, i.PricePerShare, i.SharesOwnedFollowing,
                })
                .ToListAsync(ct);

            var withDollars = rows.Select(r => new
            {
                r.IssuerSymbol, r.OwnerName, r.OfficerTitle,
                r.IsDirector, r.IsTenPercentOwner,
                r.TransactionDate,
                acquired = r.AcquiredDisposedCode == "A",
                shares = r.Shares ?? 0m,
                price = r.PricePerShare ?? 0m,
                postShares = r.SharesOwnedFollowing,
                dollars = (r.Shares ?? 0m) * (r.PricePerShare ?? 0m),
            }).ToList();

            var bySymbol = withDollars
                .GroupBy(r => r.IssuerSymbol)
                .Select(g => new
                {
                    symbol = g.Key,
                    transactions = g.Count(),
                    distinctInsiders = g.Select(x => x.OwnerName).Distinct().Count(),
                    grossBought = g.Where(x => x.acquired).Sum(x => x.dollars),
                    grossSold = g.Where(x => !x.acquired).Sum(x => x.dollars),
                    netDollars = g.Sum(x => x.dollars * (x.acquired ? 1m : -1m)),
                    netShares = g.Sum(x => x.shares * (x.acquired ? 1m : -1m)),
                    lastTransactionDate = g.Max(x => x.TransactionDate),
                })
                .Where(x => Math.Abs(x.netDollars) >= minDollarsFilter)
                .ToList();

            var topAccumulators = bySymbol
                .OrderByDescending(x => x.netDollars)
                .Take(limit)
                .ToList();
            var topDistributors = bySymbol
                .OrderBy(x => x.netDollars)
                .Take(limit)
                .ToList();

            var biggestTransactions = withDollars
                .OrderByDescending(x => x.dollars)
                .Take(limit)
                .Select(x => new
                {
                    x.IssuerSymbol, x.OwnerName,
                    role = x.OfficerTitle ?? (x.IsDirector ? "Director" : x.IsTenPercentOwner ? "10% Owner" : "Insider"),
                    x.TransactionDate, x.acquired,
                    x.shares, x.price, x.dollars, x.postShares,
                })
                .ToList();

            return Results.Ok(new
            {
                windowDays = window,
                minDollarsFilter,
                symbolCount = bySymbol.Count,
                totalTransactions = withDollars.Count,
                topAccumulators,
                topDistributors,
                biggestTransactions,
            });
        });

        group.MapGet("/symbol/{symbol}", async (
            MarketLensDbContext db,
            string symbol,
            int? eventDays,
            int? insiderDays,
            int? articleDays,
            int? eventTake,
            int? insiderTake,
            CancellationToken ct) =>
        {
            var s = symbol.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(s)) return Results.BadRequest(new { error = "symbol required" });

            var nowUtc = DateTime.UtcNow;
            var eventCutoff = nowUtc.AddDays(-Math.Clamp(eventDays ?? 30, 1, 365));
            var insiderCutoff = nowUtc.AddDays(-Math.Clamp(insiderDays ?? 90, 1, 365));
            var articleCutoff = nowUtc.AddDays(-Math.Clamp(articleDays ?? 30, 1, 365));
            var eventLimit = Math.Clamp(eventTake ?? 25, 1, 200);
            var insiderLimit = Math.Clamp(insiderTake ?? 25, 1, 200);

            var meta = TickerMetadata.Lookup(s);
            var asset = await db.ResearchAssets
                .AsNoTracking()
                .Where(a => a.Symbol == s)
                .Select(a => new { a.Id, a.Symbol, a.Name, a.Kind, a.Keywords })
                .FirstOrDefaultAsync(ct);

            var thesesRaw = await db.ResearchTheses
                .AsNoTracking()
                .Where(t => t.ThesisAssets.Any(ta => ta.Asset!.Symbol == s))
                .OrderByDescending(t => t.UpdatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Status,
                    t.Summary,
                    plan = t.Plan,
                    role = t.ThesisAssets.Where(ta => ta.Asset!.Symbol == s).Select(ta => ta.Role).FirstOrDefault(),
                    supports = t.Evidence.Count(e => e.Stance == StanceValues.Supports),
                    contradicts = t.Evidence.Count(e => e.Stance == StanceValues.Contradicts),
                    neutral = t.Evidence.Count(e => e.Stance == StanceValues.Neutral),
                    pending = t.Evidence.Count(e => e.ReviewStatus == "pending"),
                    total = t.Evidence.Count,
                    lastEvidenceAt = t.Evidence
                        .OrderByDescending(e => e.MatchedAt)
                        .Select(e => (DateTime?)e.MatchedAt)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var theses = thesesRaw.Select(t => new
            {
                t.Id, t.Name, t.Status, t.Summary, t.role,
                planAdequacy = PlanAdequacy.From(t.plan),
                evidence = new { t.supports, t.contradicts, t.neutral, t.pending, t.total },
                t.lastEvidenceAt,
            });

            var events = await db.Events
                .AsNoTracking()
                .Where(e => e.Cluster!.Symbol == s && e.Cluster.LastSeenAt >= eventCutoff)
                .OrderByDescending(e => e.Importance)
                .Take(eventLimit)
                .Select(e => new
                {
                    clusterId = e.ClusterId,
                    eventType = e.EventType,
                    importance = e.Importance,
                    sentiment = e.Sentiment,
                    summary = e.Summary,
                    components = new
                    {
                        sourceWeight = e.SourceWeight,
                        noveltyWeight = e.NoveltyWeight,
                        eventClassPrior = e.EventClassPrior,
                        magnitudeSignal = e.MagnitudeSignal,
                    },
                    cluster = new
                    {
                        memberCount = e.Cluster!.MemberCount,
                        dominantSourceTier = e.Cluster.DominantSourceTier,
                        firstSeenAt = e.Cluster.FirstSeenAt,
                        lastSeenAt = e.Cluster.LastSeenAt,
                    },
                })
                .ToListAsync(ct);

            var insiderRows = await db.InsiderTransactions
                .AsNoTracking()
                .Where(i => i.IssuerSymbol == s && i.TransactionDate >= insiderCutoff)
                .OrderByDescending(i => i.TransactionDate)
                .ToListAsync(ct);

            var openMarket = insiderRows.Where(i => i.IsOpenMarketTrade).ToList();
            var netSharesAcquired = openMarket
                .Sum(i => (i.Shares ?? 0m) * (i.AcquiredDisposedCode == "A" ? 1m : -1m));
            var netDollarsAcquired = openMarket
                .Sum(i => (i.Shares ?? 0m) * (i.PricePerShare ?? 0m) * (i.AcquiredDisposedCode == "A" ? 1m : -1m));
            var grossDollarsBought = openMarket
                .Where(i => i.AcquiredDisposedCode == "A")
                .Sum(i => (i.Shares ?? 0m) * (i.PricePerShare ?? 0m));
            var grossDollarsSold = openMarket
                .Where(i => i.AcquiredDisposedCode == "D")
                .Sum(i => (i.Shares ?? 0m) * (i.PricePerShare ?? 0m));

            var topInsiders = openMarket
                .GroupBy(i => i.OwnerName)
                .Select(g => new
                {
                    ownerName = g.Key,
                    role = g.Select(i => i.OfficerTitle).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                           ?? (g.Any(i => i.IsDirector) ? "Director"
                               : g.Any(i => i.IsTenPercentOwner) ? "10% Owner" : "Insider"),
                    transactions = g.Count(),
                    sharesNet = g.Sum(i => (i.Shares ?? 0m) * (i.AcquiredDisposedCode == "A" ? 1m : -1m)),
                    dollarsNet = g.Sum(i => (i.Shares ?? 0m) * (i.PricePerShare ?? 0m) * (i.AcquiredDisposedCode == "A" ? 1m : -1m)),
                    lastTransactionDate = g.Max(i => i.TransactionDate),
                })
                .OrderByDescending(x => Math.Abs(x.dollarsNet))
                .Take(10)
                .ToList();

            var recentTransactions = insiderRows.Take(insiderLimit).Select(i => new
            {
                i.OwnerName,
                role = i.OfficerTitle ?? (i.IsDirector ? "Director" : i.IsTenPercentOwner ? "10% Owner" : "Insider"),
                i.TransactionDate,
                i.TransactionCode,
                acquired = i.AcquiredDisposedCode == "A",
                shares = i.Shares,
                pricePerShare = i.PricePerShare,
                dollarValue = (i.Shares ?? 0m) * (i.PricePerShare ?? 0m),
                postShares = i.SharesOwnedFollowing,
                isOpenMarket = i.IsOpenMarketTrade,
            }).ToList();

            var articleStats = await db.Articles
                .AsNoTracking()
                .Where(a => a.Symbol == s && a.PublishedAt >= articleCutoff)
                .GroupBy(a => a.SourceTier)
                .Select(g => new { tier = g.Key, count = g.Count() })
                .ToListAsync(ct);

            var quote = await db.MarketQuotes
                .AsNoTracking()
                .Where(q => q.Symbol == s)
                .Select(q => new
                {
                    q.Provider, q.DisplayName, q.InstrumentType, q.Currency,
                    q.Last, q.PreviousClose, q.Change, q.ChangePercent,
                    q.AsOf, q.Status,
                })
                .FirstOrDefaultAsync(ct);

            var bars = await db.PriceBars
                .AsNoTracking()
                .Where(b => b.Symbol == s && b.Interval == "1d" && b.Timestamp >= nowUtc.AddYears(-1))
                .OrderBy(b => b.Timestamp)
                .Select(b => new { b.Timestamp, b.Close })
                .ToListAsync(ct);

            object? priceSummary = null;
            if (bars.Count > 0)
            {
                var first = bars[0];
                var last = bars[^1];
                var ytdStart = bars.FirstOrDefault(b => b.Timestamp.Year == nowUtc.Year);
                var high = bars.Max(b => b.Close);
                var low = bars.Min(b => b.Close);
                priceSummary = new
                {
                    lastClose = last.Close,
                    lastDate = last.Timestamp,
                    ytdReturnPct = ytdStart is not null && ytdStart.Close != 0
                        ? (double)((last.Close / ytdStart.Close - 1m) * 100m)
                        : (double?)null,
                    yoyReturnPct = first.Close != 0
                        ? (double)((last.Close / first.Close - 1m) * 100m)
                        : (double?)null,
                    yearHigh = high,
                    yearLow = low,
                    pctFromYearHigh = high != 0 ? (double)((last.Close / high - 1m) * 100m) : (double?)null,
                    barCount = bars.Count,
                };
            }

            return Results.Ok(new
            {
                symbol = s,
                onWatchlist = meta is not null,
                metadata = meta is null ? null : new
                {
                    companyName = meta.CompanyName,
                    cik = meta.Cik,
                    aliases = meta.Aliases,
                    irFeed = meta.IrFeedUrl,
                },
                asset,
                theses,
                events,
                insiders = new
                {
                    windowDays = (nowUtc - insiderCutoff).Days,
                    totalTransactions = insiderRows.Count,
                    openMarketTransactions = openMarket.Count,
                    netSharesAcquired,
                    netDollarsAcquired,
                    grossDollarsBought,
                    grossDollarsSold,
                    topInsiders,
                    recentTransactions,
                },
                articles = new
                {
                    windowDays = (nowUtc - articleCutoff).Days,
                    byTier = articleStats,
                    total = articleStats.Sum(x => x.count),
                },
                quote,
                price = priceSummary,
            });
        });
    }
}

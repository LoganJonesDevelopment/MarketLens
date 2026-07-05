using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/reattribute", async (
            MarketLensDbContext db,
            IWatchlistProvider watchlistProvider,
            bool? dryRun,
            CancellationToken ct) =>
        {
            var watched = await watchlistProvider.GetWatchedTickersAsync(ct);
            var bySymbol = watched.ToDictionary(w => w.Symbol, StringComparer.OrdinalIgnoreCase);

            var inspected = 0;
            var articleSymbolsCleared = 0;
            var clusterSymbolsAdjusted = 0;
            var byOldSymbol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var samples = new List<object>();

            const int batchSize = 1000;
            var lastId = Guid.Empty;
            while (!ct.IsCancellationRequested)
            {
                var batch = await db.Articles
                    .Where(a => a.Symbol != null && a.Id > lastId)
                    .OrderBy(a => a.Id)
                    .Take(batchSize)
                    .ToListAsync(ct);
                if (batch.Count == 0) break;

                foreach (var article in batch)
                {
                    inspected++;
                    if (article.Symbol is null) continue;
                    if (!bySymbol.TryGetValue(article.Symbol, out var entry))
                    {
                        continue;
                    }

                    if (!WatchlistMatcher.Mentions(entry, article.Headline, article.Summary))
                    {
                        var bad = article.Symbol;
                        byOldSymbol[bad] = byOldSymbol.GetValueOrDefault(bad) + 1;

                        if (samples.Count < 25)
                        {
                            samples.Add(new
                            {
                                articleId = article.Id,
                                source = article.Source,
                                clearedSymbol = bad,
                                headline = article.Headline?.Length > 110 ? article.Headline[..110] : article.Headline,
                            });
                        }

                        if (dryRun != true) article.Symbol = null;
                        articleSymbolsCleared++;
                    }
                }

                if (dryRun != true) await db.SaveChangesAsync(ct);

                lastId = batch[^1].Id;
                if (batch.Count < batchSize) break;
            }

            if (dryRun != true)
            {
                var clusters = await db.Clusters
                    .Include(c => c.Articles)
                    .Where(c => c.Symbol != null)
                    .ToListAsync(ct);

                foreach (var cluster in clusters)
                {
                    if (cluster.Symbol is null) continue;
                    var stillSupported = cluster.Articles.Any(a => a.Symbol == cluster.Symbol);
                    if (stillSupported) continue;

                    var topSym = cluster.Articles
                        .Where(a => a.Symbol != null)
                        .GroupBy(a => a.Symbol!)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    cluster.Symbol = topSym;
                    clusterSymbolsAdjusted++;
                }

                if (clusterSymbolsAdjusted > 0) await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new
            {
                dryRun = dryRun ?? false,
                inspected,
                articleSymbolsCleared,
                clusterSymbolsAdjusted,
                byOldSymbol = byOldSymbol.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                samples,
            });
        });
    }
}

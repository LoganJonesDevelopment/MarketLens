using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MarketLens.Api.Endpoints;

public static class ChartEndpoints
{
    public static void MapChartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chart");

        group.MapGet("/symbols/search", async (
            MarketLensDbContext db,
            string? query,
            int? take,
            CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 25, 1, 100);
            var term = query?.Trim().ToUpperInvariant();

            var fromArticles = db.Articles
                .Where(a => a.Symbol != null)
                .Select(a => a.Symbol!)
                .Distinct();

            var fromAssets = db.ResearchAssets
                .Where(a => a.Symbol != null)
                .Select(a => a.Symbol!)
                .Distinct();

            var combined = fromArticles.Union(fromAssets);
            if (!string.IsNullOrWhiteSpace(term))
                combined = combined.Where(s => s.StartsWith(term));

            var symbols = await combined.OrderBy(s => s).Take(limit).ToListAsync(ct);

            var knownMatches = Array.Empty<TickerMetadataEntry>();
            if (!string.IsNullOrWhiteSpace(term))
            {
                knownMatches = TickerMetadata.Known
                    .Where(t => t.Ticker.StartsWith(term, StringComparison.OrdinalIgnoreCase)
                                || t.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || t.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .ToArray();
            }

            var watchedAssets = await db.ResearchAssets
                .Where(a => a.Symbol != null && a.Kind == "ticker" && symbols.Contains(a.Symbol!))
                .ToDictionaryAsync(a => a.Symbol!, a => a.Name, ct);

            var anyAssets = await db.ResearchAssets
                .Where(a => a.Symbol != null && symbols.Contains(a.Symbol!))
                .ToDictionaryAsync(a => a.Symbol!, a => a.Name, ct);

            var results = symbols
                .Concat(knownMatches.Select(t => t.Ticker))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => watchedAssets.ContainsKey(s))
                .ThenBy(s => s)
                .Take(limit)
                .Select(s =>
                {
                    var meta = TickerMetadata.Lookup(s);
                    return new
                    {
                        symbol = s,
                        description = watchedAssets.TryGetValue(s, out var w) ? w
                            : anyAssets.TryGetValue(s, out var a) ? a
                            : meta?.CompanyName ?? s,
                        watched = watchedAssets.ContainsKey(s),
                        source = watchedAssets.ContainsKey(s) ? "watchlist"
                            : anyAssets.ContainsKey(s) ? "registry"
                            : meta is not null ? "known"
                            : "preview",
                    };
                });

            return Results.Ok(results);
        });

        group.MapPost("/watch", async (
            MarketLensDbContext db,
            WatchSymbolRequest request,
            CancellationToken ct) =>
        {
            var symbol = request.Symbol?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { error = "symbol is required" });

            var existing = await db.ResearchAssets
                .FirstOrDefaultAsync(a => a.Symbol == symbol && a.Kind == "ticker", ct);
            if (existing is not null)
                return Results.Ok(new { existing.Id, existing.Symbol, existing.Name, watched = true });

            var meta = TickerMetadata.Lookup(symbol);
            var now = DateTime.UtcNow;
            var asset = new ResearchAsset
            {
                Id = Guid.NewGuid(),
                Kind = "ticker",
                Name = string.IsNullOrWhiteSpace(request.Name) ? (meta?.CompanyName ?? symbol) : request.Name.Trim(),
                Symbol = symbol,
                Keywords = JsonSerializer.Serialize(meta?.Aliases ?? new[] { symbol }),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ResearchAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/assets/{asset.Id}", new { asset.Id, asset.Symbol, asset.Name, watched = true });
        });

        group.MapDelete("/watch/{symbol}", async (
            MarketLensDbContext db,
            string symbol,
            CancellationToken ct) =>
        {
            var s = symbol.Trim().ToUpperInvariant();
            var asset = await db.ResearchAssets
                .FirstOrDefaultAsync(a => a.Symbol == s && a.Kind == "ticker", ct);
            if (asset is null) return Results.NotFound();

            db.ResearchAssets.Remove(asset);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/watchlist", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var assets = await db.ResearchAssets
                .AsNoTracking()
                .Where(a => a.Kind == "ticker" && a.Symbol != null)
                .OrderBy(a => a.Symbol)
                .Select(a => new
                {
                    a.Id,
                    a.Symbol,
                    a.Name,
                    a.CreatedAt,
                })
                .ToListAsync(ct);

            var symbols = assets.Select(a => a.Symbol!).Distinct().ToList();
            var quotes = await db.MarketQuotes
                .AsNoTracking()
                .Where(q => symbols.Contains(q.Symbol))
                .OrderByDescending(q => q.IngestedAt)
                .ToListAsync(ct);

            var quoteMap = quotes
                .GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var items = assets.Select(a =>
            {
                quoteMap.TryGetValue(a.Symbol!, out var quote);
                return new
                {
                    a.Id,
                    a.Symbol,
                    a.Name,
                    a.CreatedAt,
                    last = quote?.Last,
                    change = quote?.Change,
                    changePercent = quote?.ChangePercent,
                    quoteStatus = quote?.Status,
                    quoteAsOf = quote?.AsOf,
                    quoteIngestedAt = quote?.IngestedAt,
                };
            });
            return Results.Ok(items);
        });

        group.MapGet("/symbols/{symbol}", async (
            MarketLensDbContext db,
            string symbol,
            CancellationToken ct) =>
        {
            var s = symbol.Trim().ToUpperInvariant();
            var asset = await db.ResearchAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Symbol == s, ct);

            var coverage = await db.PriceBars
                .Where(b => b.Symbol == s)
                .GroupBy(b => b.Interval)
                .Select(g => new
                {
                    interval = g.Key,
                    earliest = g.Min(b => b.Timestamp),
                    latest = g.Max(b => b.Timestamp),
                    count = g.Count(),
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                symbol = s,
                description = asset?.Name ?? s,
                kind = asset?.Kind,
                timezone = "America/New_York",
                sessionStart = "09:30",
                sessionEnd = "16:00",
                coverage,
            });
        });

        group.MapGet("/bars", async (
            MarketLensDbContext db,
            IPriceBarSource priceSource,
            string symbol,
            string interval,
            DateTime? from,
            DateTime? to,
            int? take,
            CancellationToken ct) =>
        {
            var s = symbol.Trim().ToUpperInvariant();
            var iv = PriceBarIntervals.Normalize(interval) ?? "1d";
            var limit = Math.Clamp(take ?? 5000, 1, 20000);

            from = NormalizeUtc(from);
            to = NormalizeUtc(to);

            var sourceInterval = PriceBarIntervals.SourceInterval(iv);

            async Task<List<PriceBarValue>> LoadRowsAsync()
            {
                var q = db.PriceBars
                    .AsNoTracking()
                    .Where(b => b.Symbol == s && b.Interval == sourceInterval);

                if (from.HasValue) q = q.Where(b => b.Timestamp >= from.Value);
                if (to.HasValue) q = q.Where(b => b.Timestamp <= to.Value);

                return await q
                    .OrderBy(b => b.Timestamp)
                    .Select(b => new PriceBarValue(b.Timestamp, b.Open, b.High, b.Low, b.Close, b.Volume, b.IngestedAt))
                    .ToListAsync(ct);
            }

            var nowUtc = DateTime.UtcNow;
            var rows = await LoadRowsAsync();

            if (ShouldRefreshBars(rows, sourceInterval, to, nowUtc) &&
                !await PriceBarStore.IsDeferredAsync(db, s, sourceInterval, priceSource.Name, nowUtc, ct))
            {
                using var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                fetchCts.CancelAfter(TimeSpan.FromSeconds(15));

                var canonicalRows = PriceBarAggregation.Canonicalize(sourceInterval, rows);
                var fetchFrom = canonicalRows.Count > 0
                    ? canonicalRows[^1].Timestamp.AddSeconds(-1)
                    : from ?? DefaultFetchFrom(sourceInterval, nowUtc);
                var fetchTo = to ?? nowUtc;

                var batch = await priceSource.FetchAsync(s, sourceInterval, fetchFrom, fetchTo, fetchCts.Token);
                if (batch?.Bars.Count > 0)
                {
                    await PriceBarStore.UpsertBarsAsync(db, batch, nowUtc, ct);
                    rows = await LoadRowsAsync();
                }

                await PriceBarStore.RecordFetchStateAsync(db, s, sourceInterval, priceSource.Name, batch, nowUtc, ct);
            }

            if (sourceInterval == "1d")
            {
                var latestRowTs = rows.Count > 0
                    ? PriceBarAggregation.Canonicalize(sourceInterval, rows)[^1].Timestamp
                    : (DateTime?)null;
                var liveBar = await BuildLiveDailyBarAsync(db, s, latestRowTs, to, ct);
                if (liveBar is not null) rows.Add(liveBar);
            }

            var bars = PriceBarAggregation.Build(iv, rows, limit).Select(ProjectBar).ToList<object>();

            return Results.Ok(new
            {
                symbol = s,
                interval = iv,
                bars,
            });
        });

        group.MapGet("/marks", async (
            MarketLensDbContext db,
            string symbol,
            DateTime? from,
            DateTime? to,
            Guid? thesisId,
            int? take,
            CancellationToken ct) =>
        {
            var s = symbol.Trim().ToUpperInvariant();
            var limit = Math.Clamp(take ?? 500, 1, 2000);

            var fromUtc = NormalizeUtc(from) ?? DateTime.UtcNow.AddYears(-2);
            var toUtc = NormalizeUtc(to) ?? DateTime.UtcNow.AddDays(1);

            var q = db.Events
                .AsNoTracking()
                .Where(e => e.Cluster!.Symbol == s
                            && e.Cluster.LastSeenAt >= fromUtc
                            && e.Cluster.LastSeenAt <= toUtc);

            var marks = await q
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.Cluster!.LastSeenAt)
                .Take(limit)
                .Select(e => new
                {
                    clusterId = e.ClusterId,
                    t = new DateTimeOffset(DateTime.SpecifyKind(e.Cluster!.LastSeenAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                    eventType = e.EventType,
                    importance = e.Importance,
                    sentiment = e.Sentiment,
                    summary = e.Summary,
                    sourceTier = e.Cluster.DominantSourceTier,
                    memberCount = e.Cluster.MemberCount,
                })
                .ToListAsync(ct);

            object? thesisOverlay = null;
            if (thesisId.HasValue)
            {
                var stanceMap = await db.ResearchEvidence
                    .AsNoTracking()
                    .Where(ev => ev.ThesisId == thesisId.Value
                                 && ev.ClusterId != null
                                 && ev.Cluster!.Symbol == s
                                 && ev.MatchedAt >= fromUtc
                                 && ev.MatchedAt <= toUtc)
                    .Select(ev => new
                    {
                        clusterId = ev.ClusterId!.Value,
                        stance = ev.Stance,
                        stanceConfidence = ev.StanceConfidence,
                        reviewStatus = ev.ReviewStatus,
                        isPinned = ev.IsPinned,
                    })
                    .ToListAsync(ct);

                thesisOverlay = stanceMap;
            }

            return Results.Ok(new
            {
                symbol = s,
                marks,
                thesisOverlay,
            });
        });

        static DateTime? NormalizeUtc(DateTime? value) => value is null
            ? null
            : value.Value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            };

        static object ProjectBar(PriceBarValue b)
        {
            var t = new DateTimeOffset(DateTime.SpecifyKind(b.Timestamp, DateTimeKind.Utc)).ToUnixTimeSeconds();
            if (b.Live)
                return new { t, o = b.Open, h = b.High, l = b.Low, c = b.Close, v = b.Volume, live = true };

            return new { t, o = b.Open, h = b.High, l = b.Low, c = b.Close, v = b.Volume };
        }

        static bool ShouldRefreshBars(
            IReadOnlyList<PriceBarValue> rows,
            string sourceInterval,
            DateTime? toFilter,
            DateTime nowUtc)
        {
            if (rows.Count == 0) return true;

            var refreshThreshold = PriceBarIntervals.RefreshThreshold(sourceInterval);
            if (toFilter.HasValue && toFilter.Value < nowUtc.Subtract(refreshThreshold))
                return false;

            var latestIngestedAt = rows
                .Where(row => row.IngestedAt.HasValue)
                .Select(row => row.IngestedAt!.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return nowUtc - latestIngestedAt > refreshThreshold;
        }

        static DateTime DefaultFetchFrom(string sourceInterval, DateTime nowUtc)
            => PriceBarIntervals.Normalize(sourceInterval) switch
            {
                "1d" => nowUtc.AddYears(-5),
                "1h" => nowUtc.AddDays(-60),
                "1mo" => nowUtc.AddYears(-20),
                _ => nowUtc.AddDays(-14),
            };

        static async Task<PriceBarValue?> BuildLiveDailyBarAsync(
            MarketLensDbContext db,
            string symbol,
            DateTime? latestRowTs,
            DateTime? toFilter,
            CancellationToken ct)
        {
            var quote = await db.MarketQuotes
                .AsNoTracking()
                .Where(x => x.Symbol == symbol && x.Last != null)
                .OrderByDescending(x => x.IngestedAt)
                .Select(x => new { x.Last, x.AsOf, x.IngestedAt, x.PreviousClose })
                .FirstOrDefaultAsync(ct);

            var snapshot = await db.MarketSnapshots
                .AsNoTracking()
                .Where(x => x.Symbol == symbol && x.LastPrice != null)
                .OrderByDescending(x => x.CapturedAt)
                .Select(x => new { x.LastPrice, x.QuoteTime, x.CapturedAt, x.PreviousClose, x.OpenPrice, x.HighPrice, x.LowPrice })
                .FirstOrDefaultAsync(ct);

            decimal? last = null;
            decimal? open = null;
            decimal? high = null;
            decimal? low = null;
            DateTime? observedAt = null;

            var quoteTs = quote?.AsOf ?? quote?.IngestedAt;
            var snapshotTs = snapshot?.QuoteTime ?? snapshot?.CapturedAt;

            if (quoteTs is not null && (snapshotTs is null || quoteTs >= snapshotTs))
            {
                last = quote!.Last;
                observedAt = quoteTs;
            }
            else if (snapshotTs is not null)
            {
                last = snapshot!.LastPrice;
                open = snapshot.OpenPrice;
                high = snapshot.HighPrice;
                low = snapshot.LowPrice;
                observedAt = snapshotTs;
            }

            if (last is null || observedAt is null) return null;
            if (toFilter.HasValue && observedAt > toFilter.Value) return null;
            if (latestRowTs.HasValue && observedAt <= latestRowTs.Value) return null;

            var ts = DateTime.SpecifyKind(observedAt.Value, DateTimeKind.Utc);
            var o = open ?? last.Value;
            var h = high ?? last.Value;
            var l = low ?? last.Value;
            if (h < last.Value) h = last.Value;
            if (l > last.Value) l = last.Value;

            return new PriceBarValue(ts, o, h, l, last.Value, null, observedAt, Live: true);
        }

        group.MapGet("/calendar", async (
            MarketLensDbContext db,
            string? symbol,
            DateTime? from,
            DateTime? to,
            string? eventType,
            int? take,
            CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 500, 1, 2000);
            var fromUtc = NormalizeUtc(from) ?? DateTime.UtcNow.AddDays(-30);
            var toUtc = NormalizeUtc(to) ?? DateTime.UtcNow.AddDays(90);

            var q = db.EconomicEvents
                .AsNoTracking()
                .Where(e => e.ScheduledAt >= fromUtc && e.ScheduledAt <= toUtc);

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.Trim().ToUpperInvariant();
                q = q.Where(e => e.Symbol == s);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
                q = q.Where(e => e.EventType == eventType);

            var items = await q
                .OrderBy(e => e.ScheduledAt)
                .Take(limit)
                .Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.Symbol,
                    e.Label,
                    t = new DateTimeOffset(DateTime.SpecifyKind(e.ScheduledAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                    e.IsTimeSpecific,
                    e.Status,
                    e.Notes,
                    e.Source,
                    e.ClusterId,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });
    }
}

public sealed record WatchSymbolRequest(string? Symbol, string? Name);

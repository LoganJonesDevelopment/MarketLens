using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    public static void MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/research");

        MapThesisEndpoints(group);
        MapExplorationEndpoints(group);
        MapSymbolEndpoints(group);
        MapAssetEndpoints(group);
        MapRuleEndpoints(group);
        MapEvidenceEndpoints(group);
    }

    private static async Task AutoBindAssetsAsync(MarketLensDbContext db, Guid thesisId, string text, CancellationToken ct)
    {
        var detectedSymbols = DetectTickers(text);
        var detectedCommodities = CommodityMetadata.DetectNames(text);
        if (detectedSymbols.Count == 0 && detectedCommodities.Count == 0) return;

        var assets = await db.ResearchAssets
            .Where(a =>
                (a.Symbol != null && detectedSymbols.Contains(a.Symbol)) ||
                (a.Kind == "commodity" && detectedCommodities.Contains(a.Name)))
            .Select(a => new { a.Id, a.Symbol, a.Kind, a.Name })
            .ToListAsync(ct);

        if (assets.Count == 0) return;

        var existingBindings = await db.ThesisAssets
            .Where(ta => ta.ThesisId == thesisId)
            .Select(ta => ta.AssetId)
            .ToListAsync(ct);
        var existingSet = new HashSet<Guid>(existingBindings);

        var now = DateTime.UtcNow;
        var role = "subject";
        var added = false;
        foreach (var asset in assets)
        {
            if (existingSet.Contains(asset.Id)) continue;
            db.ThesisAssets.Add(new ThesisAsset
            {
                ThesisId = thesisId,
                AssetId = asset.Id,
                Role = role,
                CreatedAt = now,
            });
            role = "peer";
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }

    private static IReadOnlySet<string> DetectTickers(string text)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return matches;

        foreach (var meta in TickerMetadata.Known)
        {
            var watched = new WatchedTicker(
                Guid.Empty,
                meta.Ticker,
                meta.CompanyName,
                string.IsNullOrWhiteSpace(meta.Cik) ? null : meta.Cik,
                meta.IrFeedUrl,
                meta.Aliases);

            if (WatchlistMatcher.Mentions(watched, text, null))
                matches.Add(meta.Ticker);
        }
        return matches;
    }

    private static string? NormalizeSymbol(string? symbol)
    {
        var value = EmptyToNull(symbol);
        return value?.ToUpperInvariant();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ToJsonArray(IReadOnlyCollection<string>? values)
    {
        var normalized = values?
            .Select(EmptyToNull)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return JsonSerializer.Serialize(normalized);
    }

    private static async Task<Vector?> TryEmbedAsync(
        IEmbeddingClient embedder,
        string text,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var embedding = await embedder.EmbedAsync(text, cancellationToken);
            return new Vector(embedding);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("MarketLens.Api.Endpoints.Research")
                .LogWarning(ex, "Failed to embed research thesis text");
            return null;
        }
    }
}

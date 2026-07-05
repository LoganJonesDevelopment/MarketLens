using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Infrastructure.Services;

public static class AssetRegistrySeed
{
    public static async Task EnsureCanonicalTickersAsync(MarketLensDbContext db, CancellationToken cancellationToken = default)
    {
        var canonicalSymbols = TickerMetadata.Known.Select(k => k.Ticker).ToList();
        var existing = await db.ResearchAssets
            .Where(a => a.Symbol != null && canonicalSymbols.Contains(a.Symbol))
            .Select(a => a.Symbol!)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var meta in TickerMetadata.Known)
        {
            if (existingSet.Contains(meta.Ticker)) continue;
            db.ResearchAssets.Add(new ResearchAsset
            {
                Id = Guid.NewGuid(),
                Kind = "ticker",
                Name = meta.CompanyName,
                Symbol = meta.Ticker,
                Keywords = JsonSerializer.Serialize(meta.Aliases),
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        await EnsureCanonicalCommoditiesAsync(db, cancellationToken);
        await EnsureCommodityThesisBindingsAsync(db, cancellationToken);
        await RepairWeakCommodityBindingsAsync(db, cancellationToken);
        await RepairInvalidAmbiguousTickerBindingsAsync(db, cancellationToken);
        await NormalizeThesisAssetRolesAsync(db, cancellationToken);
    }

    private static async Task EnsureCanonicalCommoditiesAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var commodityNames = CommodityMetadata.Known.Select(c => c.Name).ToList();
        var existing = await db.ResearchAssets
            .Where(a => a.Kind == "commodity" && commodityNames.Contains(a.Name))
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var commodity in CommodityMetadata.Known)
        {
            if (existingSet.Contains(commodity.Name)) continue;
            db.ResearchAssets.Add(new ResearchAsset
            {
                Id = Guid.NewGuid(),
                Kind = "commodity",
                Name = commodity.Name,
                Symbol = null,
                Keywords = JsonSerializer.Serialize(commodity.Keywords),
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCommodityThesisBindingsAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var commodityAssets = await db.ResearchAssets
            .Where(a => a.Kind == "commodity")
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(cancellationToken);
        if (commodityAssets.Count == 0) return;

        var assetByName = commodityAssets.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var theses = await db.ResearchTheses
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.ThesisText,
                ExistingAssetIds = t.ThesisAssets.Select(ta => ta.AssetId).ToList(),
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var thesis in theses)
        {
            var detected = CommodityMetadata.DetectPrimaryNames(thesis.Name, thesis.ThesisText);
            if (detected.Count == 0) continue;

            var existingSet = thesis.ExistingAssetIds.ToHashSet();
            var role = thesis.ExistingAssetIds.Count == 0 ? "subject" : "context";

            foreach (var name in detected)
            {
                if (!assetByName.TryGetValue(name, out var asset)) continue;
                if (existingSet.Contains(asset.Id)) continue;

                db.ThesisAssets.Add(new ThesisAsset
                {
                    ThesisId = thesis.Id,
                    AssetId = asset.Id,
                    Role = role,
                    CreatedAt = now,
                });
                existingSet.Add(asset.Id);
                added++;
            }
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RepairInvalidAmbiguousTickerBindingsAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var links = await db.ThesisAssets
            .Include(ta => ta.Thesis)
            .Include(ta => ta.Asset)
            .Where(ta => ta.Asset!.Symbol != null)
            .ToListAsync(cancellationToken);

        var invalid = new List<(Guid ThesisId, Guid AssetId, string Symbol)>();
        foreach (var link in links)
        {
            var asset = link.Asset;
            var thesis = link.Thesis;
            if (asset?.Symbol is null || thesis is null) continue;
            if (!WatchlistMatcher.IsAmbiguousBareSymbol(asset.Symbol)) continue;

            var meta = TickerMetadata.Lookup(asset.Symbol);
            var watched = new WatchedTicker(
                asset.Id,
                asset.Symbol,
                asset.Name,
                meta?.Cik,
                meta?.IrFeedUrl,
                ParseKeywords(asset.Keywords));

            if (WatchlistMatcher.Mentions(watched, thesis.Name, thesis.ThesisText))
                continue;

            invalid.Add((link.ThesisId, link.AssetId, asset.Symbol));
            db.ThesisAssets.Remove(link);
        }

        if (invalid.Count == 0) return;

        foreach (var group in invalid.GroupBy(x => new { x.ThesisId, x.Symbol }))
        {
            var symbol = group.Key.Symbol;
            var thesisId = group.Key.ThesisId;
            var evidence = await db.ResearchEvidence
                .Include(e => e.Article)
                .Where(e =>
                    e.ThesisId == thesisId &&
                    e.MatchKind == "matcher" &&
                    e.ReviewStatus == "pending" &&
                    e.Article != null &&
                    e.Article.Symbol == symbol)
                .ToListAsync(cancellationToken);

            db.ResearchEvidence.RemoveRange(evidence);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RepairWeakCommodityBindingsAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var links = await db.ThesisAssets
            .Include(ta => ta.Thesis)
            .Include(ta => ta.Asset)
            .Where(ta => ta.Role == "context" && ta.Asset!.Kind == "commodity")
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var link in links)
        {
            if (link.Thesis is null || link.Asset is null) continue;
            var strong = CommodityMetadata.DetectPrimaryNames(link.Thesis.Name, link.Thesis.ThesisText);
            if (strong.Contains(link.Asset.Name)) continue;
            db.ThesisAssets.Remove(link);
            removed++;
        }

        if (removed > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task NormalizeThesisAssetRolesAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var thesisIds = await db.ThesisAssets
            .Select(ta => ta.ThesisId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var thesisId in thesisIds)
        {
            var links = await db.ThesisAssets
                .Include(ta => ta.Asset)
                .Where(ta => ta.ThesisId == thesisId)
                .OrderBy(ta => ta.CreatedAt)
                .ToListAsync(cancellationToken);

            if (links.Any(l => l.Role == "subject")) continue;

            var subject = links.FirstOrDefault(l => l.Asset?.Kind == "ticker") ?? links.FirstOrDefault();
            if (subject is null) continue;

            subject.Role = "subject";
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ParseKeywords(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

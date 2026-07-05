using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketLens.Infrastructure.Services;

public class DbWatchlistProvider(IServiceScopeFactory scopeFactory) : IWatchlistProvider
{
    public async Task<IReadOnlyList<WatchedTicker>> GetWatchedTickersAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();

        var assets = await db.ResearchAssets
            .AsNoTracking()
            .Where(a => a.Kind == "ticker" && a.Symbol != null)
            .OrderBy(a => a.Symbol)
            .ToListAsync(cancellationToken);

        var results = new List<WatchedTicker>(assets.Count);
        foreach (var a in assets)
        {
            var symbol = a.Symbol!.Trim().ToUpperInvariant();
            var meta = TickerMetadata.Lookup(symbol);
            var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol };
            if (meta is not null)
                foreach (var al in meta.Aliases) aliasSet.Add(al);
            foreach (var k in ParseKeywords(a.Keywords)) aliasSet.Add(k);
            if (!string.IsNullOrWhiteSpace(a.Name)) aliasSet.Add(a.Name);

            results.Add(new WatchedTicker(
                AssetId: a.Id,
                Symbol: symbol,
                Name: string.IsNullOrWhiteSpace(a.Name) ? (meta?.CompanyName ?? symbol) : a.Name,
                Cik: meta?.Cik,
                IrFeedUrl: meta?.IrFeedUrl,
                Aliases: aliasSet.ToList()));
        }
        return results;
    }

    private static IEnumerable<string> ParseKeywords(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var v = item.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) yield return v.Trim();
                    }
            }
        }
        else
        {
            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part;
        }
    }
}

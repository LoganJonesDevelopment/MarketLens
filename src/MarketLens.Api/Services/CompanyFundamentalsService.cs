using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services;

public class CompanyFundamentalsService(
    MarketLensDbContext db,
    ICompanyFundamentalsSource source,
    ILogger<CompanyFundamentalsService> logger)
{
    public async Task<CompanyFundamentals?> GetLatestAsync(
        string rawSymbol,
        CancellationToken cancellationToken = default)
    {
        var symbol = Normalize(rawSymbol);
        return await db.CompanyFundamentals
            .AsNoTracking()
            .Where(f => f.Symbol == symbol)
            .OrderByDescending(f => f.IngestedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanyFundamentals?> GetOrRefreshAsync(
        string rawSymbol,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var symbol = Normalize(rawSymbol);
        var latest = await db.CompanyFundamentals
            .Where(f => f.Symbol == symbol && f.Provider == source.Name)
            .OrderByDescending(f => f.IngestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null &&
            latest.Status == "ok" &&
            DateTime.UtcNow - latest.IngestedAt <= maxAge)
            return latest;

        return await RefreshAsync(symbol, cancellationToken) ?? latest;
    }

    public async Task<CompanyFundamentals?> RefreshAsync(
        string rawSymbol,
        CancellationToken cancellationToken = default)
    {
        var symbol = Normalize(rawSymbol);
        if (!IsEquitySymbol(symbol)) return null;

        var snapshot = await source.FetchAsync(symbol, cancellationToken);
        if (snapshot is null)
        {
            logger.LogDebug("Fundamentals source returned no snapshot for {Symbol}", symbol);
            return await GetLatestAsync(symbol, cancellationToken);
        }

        var row = await db.CompanyFundamentals
            .Where(f => f.Provider == snapshot.Provider && f.Symbol == snapshot.Symbol)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            row = new CompanyFundamentals
            {
                Id = Guid.NewGuid(),
                Provider = snapshot.Provider,
                Symbol = snapshot.Symbol,
            };
            db.CompanyFundamentals.Add(row);
        }

        Apply(row, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<Dictionary<string, CompanyFundamentals>> LoadLatestForSymbolsAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0) return new Dictionary<string, CompanyFundamentals>(StringComparer.OrdinalIgnoreCase);

        var normalized = symbols
            .Select(Normalize)
            .Where(IsEquitySymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = await db.CompanyFundamentals
            .AsNoTracking()
            .Where(f => normalized.Contains(f.Symbol) && f.Status == "ok")
            .OrderByDescending(f => f.IngestedAt)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> LoadCandidateSymbolsAsync(
        int windowDays,
        int take,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(windowDays, 7, 120));
        var rows = await db.Events
            .AsNoTracking()
            .Where(e => e.Cluster != null && e.Cluster.Symbol != null && e.Cluster.LastSeenAt >= cutoff)
            .Select(e => new
            {
                Symbol = e.Cluster!.Symbol!,
                e.Importance,
                Latest = e.Cluster!.LastSeenAt,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(e => IsEquitySymbol(e.Symbol))
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Symbol = g.Key,
                Score = g.Sum(e => e.Importance) + g.Count() * 0.05m,
                Latest = g.Max(e => e.Latest),
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Latest)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => x.Symbol)
            .ToList();
    }

    private static void Apply(CompanyFundamentals row, CompanyFundamentalsSnapshot snapshot)
    {
        row.Status = snapshot.Status;
        row.Error = snapshot.Error;
        row.Name = snapshot.Name;
        row.Exchange = snapshot.Exchange;
        row.Industry = snapshot.Industry;
        row.Currency = snapshot.Currency;
        row.WebUrl = snapshot.WebUrl;
        row.IpoDate = snapshot.IpoDate;
        row.MarketCapitalizationMillion = snapshot.MarketCapitalizationMillion;
        row.ShareOutstandingMillion = snapshot.ShareOutstandingMillion;
        row.EnterpriseValueMillion = snapshot.EnterpriseValueMillion;
        row.PeTtm = snapshot.PeTtm;
        row.ForwardPe = snapshot.ForwardPe;
        row.PegTtm = snapshot.PegTtm;
        row.PsTtm = snapshot.PsTtm;
        row.EvRevenueTtm = snapshot.EvRevenueTtm;
        row.EvEbitdaTtm = snapshot.EvEbitdaTtm;
        row.PriceToBook = snapshot.PriceToBook;
        row.PriceToFreeCashFlowTtm = snapshot.PriceToFreeCashFlowTtm;
        row.RevenueGrowthTtmYoy = snapshot.RevenueGrowthTtmYoy;
        row.EpsGrowthTtmYoy = snapshot.EpsGrowthTtmYoy;
        row.GrossMarginTtm = snapshot.GrossMarginTtm;
        row.OperatingMarginTtm = snapshot.OperatingMarginTtm;
        row.NetMarginTtm = snapshot.NetMarginTtm;
        row.RoeTtm = snapshot.RoeTtm;
        row.DebtToEquityQuarterly = snapshot.DebtToEquityQuarterly;
        row.Beta = snapshot.Beta;
        row.Week52High = snapshot.Week52High;
        row.Week52Low = snapshot.Week52Low;
        row.Week52PriceReturnDaily = snapshot.Week52PriceReturnDaily;
        row.RawProfileJson = snapshot.RawProfileJson;
        row.RawMetricJson = snapshot.RawMetricJson;
        row.IngestedAt = snapshot.IngestedAt;
        row.UpdatedAt = DateTime.UtcNow;
    }

    private static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();

    private static bool IsEquitySymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var s = symbol.Trim();
        if (s.Contains(':') || s.Contains('=') || s.Contains("-USD", StringComparison.OrdinalIgnoreCase)) return false;
        return s.All(c => char.IsLetterOrDigit(c) || c == '.');
    }
}

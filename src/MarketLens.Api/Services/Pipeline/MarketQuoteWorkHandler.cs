using System.Text.Json;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record MarketQuoteWorkResult(string Symbol, bool Written, bool Error);

public sealed class MarketQuoteWorkHandler(
    MarketLensDbContext db,
    IQuoteSource source,
    YahooQuoteSource? yahoo)
{
    private static readonly Dictionary<string, string> YahooSymbolMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["I:NDX"] = "^NDX",
        ["I:SPX"] = "^GSPC",
        ["I:DJI"] = "^DJI",
        ["I:RUT"] = "^RUT",
        ["I:VIX"] = "^VIX",
    };

    public async Task<MarketQuoteWorkResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var symbol = NormalizeSymbol(payload.Symbol) ?? NormalizeSymbol(naturalKey);
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidOperationException($"Unsupported market quote work item '{naturalKey}'.");

        var snapshot = (await source.FetchAsync([symbol], cancellationToken)).FirstOrDefault();
        if (snapshot is null)
            return new MarketQuoteWorkResult(symbol, Written: false, Error: true);

        snapshot = await ApplyYahooFallbackAsync(snapshot, cancellationToken);

        var row = await db.MarketQuotes
            .SingleOrDefaultAsync(q => q.Provider == source.Name && q.Symbol == symbol, cancellationToken);

        if (row is null)
        {
            row = new MarketQuote
            {
                Id = Guid.NewGuid(),
                Provider = source.Name,
                Symbol = symbol,
            };
            db.MarketQuotes.Add(row);
        }

        row.DisplayName = snapshot.DisplayName ?? payload.DisplayName;
        row.InstrumentType = snapshot.InstrumentType;
        row.Exchange = snapshot.Exchange;
        row.Currency = snapshot.Currency;
        row.Last = snapshot.Last;
        row.PreviousClose = snapshot.PreviousClose;
        row.Change = snapshot.Change;
        row.ChangePercent = snapshot.ChangePercent;
        row.AsOf = snapshot.AsOf;
        row.IngestedAt = DateTime.UtcNow;
        row.Status = snapshot.Status;
        row.Error = snapshot.Error;

        await db.SaveChangesAsync(cancellationToken);
        return new MarketQuoteWorkResult(symbol, Written: true, Error: snapshot.Status == "error");
    }

    private async Task<QuoteSnapshot> ApplyYahooFallbackAsync(
        QuoteSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (yahoo is null)
            return snapshot;
        if (!IsStale(snapshot) && snapshot.Status != "error" && snapshot.Last is not null)
            return snapshot;

        var yahooSymbol = YahooSymbolMap.TryGetValue(snapshot.Symbol, out var mapped)
            ? mapped
            : snapshot.Symbol;

        var yahooSnapshot = (await yahoo.FetchAsync([yahooSymbol], cancellationToken)).FirstOrDefault();
        if (yahooSnapshot?.Last is null || yahooSnapshot.Status == "error")
            return snapshot;

        return yahooSnapshot with
        {
            Symbol = snapshot.Symbol,
            Status = "ok:yahoo_fallback",
        };
    }

    private static bool IsStale(QuoteSnapshot snapshot)
    {
        if (snapshot.Status?.StartsWith("ok:eod", StringComparison.Ordinal) != true) return false;
        if (snapshot.AsOf is null) return true;
        return snapshot.AsOf.Value < DateTime.UtcNow.AddHours(-18);
    }

    private static string? NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();

    private static MarketQuotePayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new MarketQuotePayload();

        try
        {
            return JsonSerializer.Deserialize<MarketQuotePayload>(
                payloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new MarketQuotePayload();
        }
        catch (JsonException)
        {
            return new MarketQuotePayload();
        }
    }

    private sealed class MarketQuotePayload
    {
        public string? Symbol { get; set; }
        public string? DisplayName { get; set; }
    }
}

using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FinnhubMarketDataClient(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubMarketDataClient> logger) : IMarketDataClient
{
    private readonly FinnhubOptions _options = options.Value;

    public async Task<MarketDataQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return null;

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
            return null;

        try
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/quote?symbol={Uri.EscapeDataString(normalizedSymbol)}&token={Uri.EscapeDataString(_options.ApiKey)}";
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Finnhub quote returned {Status} for {Symbol}", response.StatusCode, normalizedSymbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var lastPrice = GetDecimal(root, "c");
            var previousClose = GetDecimal(root, "pc");
            if (lastPrice is null && previousClose is null)
                return null;

            var quoteTime = GetUnixSeconds(root, "t");

            return new MarketDataQuote(
                normalizedSymbol,
                "finnhub",
                DateTime.UtcNow,
                quoteTime,
                lastPrice,
                previousClose,
                GetDecimal(root, "o"),
                GetDecimal(root, "h"),
                GetDecimal(root, "l"),
                null,
                null,
                json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Finnhub quote fetch failed for {Symbol}", symbol);
            return null;
        }
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
            return null;

        return property.TryGetDecimal(out var value) ? value : null;
    }

    private static DateTime? GetUnixSeconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
            return null;

        return property.TryGetInt64(out var value)
            ? DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime
            : null;
    }
}

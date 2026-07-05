using System.Text.Json;
using MarketLens.Api.Services;

namespace MarketLens.Api.Services.Pipeline;

public sealed record FundamentalsRefreshResult(bool Processed, bool Refreshed);

public sealed class FundamentalsRefreshHandler(
    CompanyFundamentalsService fundamentals,
    ILogger<FundamentalsRefreshHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FundamentalsRefreshResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var symbol = string.IsNullOrWhiteSpace(payload.Symbol) ? naturalKey : payload.Symbol!;
        symbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return new FundamentalsRefreshResult(false, false);

        var row = await fundamentals.GetOrRefreshAsync(
            symbol,
            TimeSpan.FromHours(Math.Max(1, payload.MaxAgeHours)),
            cancellationToken);

        logger.LogInformation("Processed fundamentals refresh for {Symbol}", symbol);
        return new FundamentalsRefreshResult(true, row is not null);
    }

    private static FundamentalsRefreshPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new FundamentalsRefreshPayload();

        try
        {
            return JsonSerializer.Deserialize<FundamentalsRefreshPayload>(payloadJson, JsonOptions)
                ?? new FundamentalsRefreshPayload();
        }
        catch (JsonException)
        {
            return new FundamentalsRefreshPayload();
        }
    }

    private sealed class FundamentalsRefreshPayload
    {
        public string? Symbol { get; set; }
        public int MaxAgeHours { get; set; } = 24;
    }
}

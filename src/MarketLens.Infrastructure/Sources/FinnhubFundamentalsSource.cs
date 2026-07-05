using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FinnhubFundamentalsSource(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubFundamentalsSource> logger) : ICompanyFundamentalsSource
{
    private readonly FinnhubOptions _options = options.Value;

    public string Name => "finnhub";

    public async Task<CompanyFundamentalsSnapshot?> FetchAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return null;

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
            return null;

        try
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var profileUrl = $"{baseUrl}/stock/profile2?symbol={Uri.EscapeDataString(normalizedSymbol)}&token={Uri.EscapeDataString(_options.ApiKey)}";
            var metricUrl = $"{baseUrl}/stock/metric?symbol={Uri.EscapeDataString(normalizedSymbol)}&metric=all&token={Uri.EscapeDataString(_options.ApiKey)}";

            var profileResponse = await httpClient.GetAsync(profileUrl, cancellationToken);
            if (!profileResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Finnhub profile returned {Status} for {Symbol}", profileResponse.StatusCode, normalizedSymbol);
                return Failure(normalizedSymbol, $"profile:{profileResponse.StatusCode}");
            }

            var profileJson = await profileResponse.Content.ReadAsStringAsync(cancellationToken);

            var metricResponse = await httpClient.GetAsync(metricUrl, cancellationToken);
            if (!metricResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Finnhub metrics returned {Status} for {Symbol}", metricResponse.StatusCode, normalizedSymbol);
                return Failure(normalizedSymbol, $"metric:{metricResponse.StatusCode}", profileJson);
            }

            var metricJson = await metricResponse.Content.ReadAsStringAsync(cancellationToken);
            using var profileDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(profileJson) ? "{}" : profileJson);
            using var metricDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(metricJson) ? "{}" : metricJson);

            var profile = profileDoc.RootElement;
            var metric = metricDoc.RootElement.TryGetProperty("metric", out var metricElement)
                ? metricElement
                : default;

            var hasMetric = metric.ValueKind == JsonValueKind.Object && metric.EnumerateObject().Any();
            var hasProfile = profile.ValueKind == JsonValueKind.Object && profile.EnumerateObject().Any();
            if (!hasMetric && !hasProfile)
                return Failure(normalizedSymbol, "empty fundamentals response", profileJson, metricJson);

            return new CompanyFundamentalsSnapshot(
                Provider: Name,
                Symbol: normalizedSymbol,
                Status: "ok",
                Error: null,
                Name: ReadString(profile, "name"),
                Exchange: ReadString(profile, "exchange"),
                Industry: ReadString(profile, "finnhubIndustry"),
                Currency: ReadString(profile, "currency"),
                WebUrl: ReadString(profile, "weburl"),
                IpoDate: ReadDateOnly(profile, "ipo"),
                MarketCapitalizationMillion: ReadDecimal(profile, "marketCapitalization") ?? ReadDecimal(metric, "marketCapitalization"),
                ShareOutstandingMillion: ReadDecimal(profile, "shareOutstanding") ?? ReadDecimal(metric, "shareOutstanding"),
                EnterpriseValueMillion: ReadDecimal(metric, "enterpriseValue"),
                PeTtm: ReadDecimal(metric, "peTTM"),
                ForwardPe: ReadDecimal(metric, "forwardPE"),
                PegTtm: ReadDecimal(metric, "pegTTM"),
                PsTtm: ReadDecimal(metric, "psTTM"),
                EvRevenueTtm: ReadDecimal(metric, "evRevenueTTM"),
                EvEbitdaTtm: ReadDecimal(metric, "evEbitdaTTM"),
                PriceToBook: ReadDecimal(metric, "pb"),
                PriceToFreeCashFlowTtm: ReadDecimal(metric, "pfcfShareTTM"),
                RevenueGrowthTtmYoy: ReadDecimal(metric, "revenueGrowthTTMYoy"),
                EpsGrowthTtmYoy: ReadDecimal(metric, "epsGrowthTTMYoy"),
                GrossMarginTtm: ReadDecimal(metric, "grossMarginTTM"),
                OperatingMarginTtm: ReadDecimal(metric, "operatingMarginTTM"),
                NetMarginTtm: ReadDecimal(metric, "netProfitMarginTTM"),
                RoeTtm: ReadDecimal(metric, "roeTTM"),
                DebtToEquityQuarterly: ReadDecimal(metric, "totalDebt/totalEquityQuarterly"),
                Beta: ReadDecimal(metric, "beta"),
                Week52High: ReadDecimal(metric, "52WeekHigh"),
                Week52Low: ReadDecimal(metric, "52WeekLow"),
                Week52PriceReturnDaily: ReadDecimal(metric, "52WeekPriceReturnDaily"),
                RawProfileJson: string.IsNullOrWhiteSpace(profileJson) ? "{}" : profileJson,
                RawMetricJson: string.IsNullOrWhiteSpace(metricJson) ? "{}" : metricJson,
                IngestedAt: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Finnhub fundamentals fetch failed for {Symbol}", normalizedSymbol);
            return Failure(normalizedSymbol, ex.Message);
        }
    }

    private CompanyFundamentalsSnapshot Failure(
        string symbol,
        string error,
        string rawProfileJson = "{}",
        string rawMetricJson = "{}") =>
        new(
            Provider: Name,
            Symbol: symbol,
            Status: "failed",
            Error: error.Length > 512 ? error[..512] : error,
            Name: null,
            Exchange: null,
            Industry: null,
            Currency: null,
            WebUrl: null,
            IpoDate: null,
            MarketCapitalizationMillion: null,
            ShareOutstandingMillion: null,
            EnterpriseValueMillion: null,
            PeTtm: null,
            ForwardPe: null,
            PegTtm: null,
            PsTtm: null,
            EvRevenueTtm: null,
            EvEbitdaTtm: null,
            PriceToBook: null,
            PriceToFreeCashFlowTtm: null,
            RevenueGrowthTtmYoy: null,
            EpsGrowthTtmYoy: null,
            GrossMarginTtm: null,
            OperatingMarginTtm: null,
            NetMarginTtm: null,
            RoeTtm: null,
            DebtToEquityQuarterly: null,
            Beta: null,
            Week52High: null,
            Week52Low: null,
            Week52PriceReturnDaily: null,
            RawProfileJson: string.IsNullOrWhiteSpace(rawProfileJson) ? "{}" : rawProfileJson,
            RawMetricJson: string.IsNullOrWhiteSpace(rawMetricJson) ? "{}" : rawMetricJson,
            IngestedAt: DateTime.UtcNow);

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
            return null;

        return property.TryGetDecimal(out var value) ? value : null;
    }
}

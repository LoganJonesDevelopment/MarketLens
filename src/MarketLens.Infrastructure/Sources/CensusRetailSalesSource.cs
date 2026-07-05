using System.Globalization;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class CensusRetailSalesOptions
{
    public string BaseUrl { get; set; } = "https://api.census.gov/data/timeseries/eits/mrtsadv";
    public string ApiKey { get; set; } = string.Empty;
    public int LookbackYears { get; set; } = 2;
    public string DataTypeCode { get; set; } = "SM";
    public string SeasonallyAdjusted { get; set; } = "yes";
    public CensusRetailSalesCategory[] Categories { get; set; } =
    [
        new() { CategoryCode = "44X72", Label = "Retail and food services sales" },
        new() { CategoryCode = "44000", Label = "Retail trade sales" },
    ];
}

public class CensusRetailSalesCategory
{
    public string CategoryCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class CensusRetailSalesSource(
    HttpClient httpClient,
    IOptions<CensusRetailSalesOptions> options,
    ILogger<CensusRetailSalesSource> logger) : INewsSource
{
    private readonly CensusRetailSalesOptions _options = options.Value;

    public string Name => SourceNames.Census;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("Census API key not configured; skipping Census retail sales ingestion");
            return [];
        }

        var categories = _options.Categories
            .Where(c => !string.IsNullOrWhiteSpace(c.CategoryCode))
            .ToDictionary(c => c.CategoryCode, c => c.Label, StringComparer.OrdinalIgnoreCase);
        if (categories.Count == 0) return [];

        var rows = new List<CensusRetailSalesRow>();
        var currentYear = DateTime.UtcNow.Year;
        var startYear = Math.Max(1900, currentYear - Math.Max(1, _options.LookbackYears));

        foreach (var year in Enumerable.Range(startYear, currentYear - startYear + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await httpClient.GetStringAsync(BuildUrl(year), cancellationToken);
                rows.AddRange(ParseRows(json));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Census retail sales fetch failed for {Year}", year);
            }
        }

        return rows
            .Where(r => categories.ContainsKey(r.CategoryCode))
            .GroupBy(r => $"{r.CategoryCode}|{r.DataTypeCode}|{r.SeasonallyAdjusted}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.Period).First())
            .OrderByDescending(r => r.Period)
            .Select(r => ToArticle(r, categories))
            .ToList();
    }

    private string BuildUrl(int year)
    {
        var query = new Dictionary<string, string>
        {
            ["get"] = "data_type_code,time_slot_id,time_slot_date,time_slot_name,seasonally_adj,category_code,cell_value,error_data",
            ["for"] = "us:*",
            ["time"] = year.ToString(CultureInfo.InvariantCulture),
            ["data_type_code"] = _options.DataTypeCode,
            ["seasonally_adj"] = _options.SeasonallyAdjusted,
            ["key"] = _options.ApiKey,
        };

        var qs = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{_options.BaseUrl.TrimEnd('/')}?{qs}";
    }

    private static IReadOnlyList<CensusRetailSalesRow> ParseRows(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var arrays = doc.RootElement.EnumerateArray().ToList();
        if (arrays.Count < 2) return [];

        var headers = arrays[0].EnumerateArray()
            .Select(h => h.GetString() ?? string.Empty)
            .ToArray();

        var parsed = new List<CensusRetailSalesRow>();
        foreach (var row in arrays.Skip(1))
        {
            var cells = row.EnumerateArray().Select(c => c.GetString() ?? string.Empty).ToArray();
            string Get(params string[] names)
            {
                var index = IndexOf(headers, names);
                return index >= 0 && index < cells.Length ? cells[index] : string.Empty;
            }

            var periodText = FirstNonEmpty(Get("time_slot_date"), Get("time"));
            if (!TryParsePeriod(periodText, Get("time_slot_id"), out var period)) continue;

            parsed.Add(new CensusRetailSalesRow(
                CategoryCode: Get("category_code"),
                DataTypeCode: Get("data_type_code"),
                SeasonallyAdjusted: Get("seasonally_adj"),
                CellValue: Get("cell_value"),
                ErrorData: Get("error_data"),
                Period: period,
                PeriodLabel: FirstNonEmpty(Get("time_slot_name"), period.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
                RawJson: row.GetRawText()));
        }

        return parsed;
    }

    private static IngestedArticle ToArticle(
        CensusRetailSalesRow row,
        IReadOnlyDictionary<string, string> categoryLabels)
    {
        var label = categoryLabels.TryGetValue(row.CategoryCode, out var configuredLabel) &&
            !string.IsNullOrWhiteSpace(configuredLabel)
            ? configuredLabel
            : row.CategoryCode;
        var value = string.IsNullOrWhiteSpace(row.CellValue) ? "N/A" : row.CellValue;
        var adjustment = string.Equals(row.SeasonallyAdjusted, "yes", StringComparison.OrdinalIgnoreCase)
            ? "seasonally adjusted"
            : "not seasonally adjusted";
        var period = row.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        return new IngestedArticle(
            Source: SourceNames.Census,
            SourceId: $"census:retail-sales:{row.CategoryCode}:{row.DataTypeCode}:{row.SeasonallyAdjusted}:{period}:{value}",
            Symbol: null,
            Headline: $"Census {label}: {value} for {period}",
            Summary: $"U.S. Census Bureau advance retail sales data for {label}: {value} for {period} ({adjustment}, {row.DataTypeCode}).",
            Url: "https://www.census.gov/retail/sales.html",
            Publisher: "U.S. Census Bureau",
            PublishedAt: row.Period,
            RawJson: row.RawJson);
    }

    private static int IndexOf(string[] headers, params string[] names)
    {
        foreach (var name in names)
        {
            var index = Array.FindIndex(headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }

        return -1;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static bool TryParsePeriod(string periodText, string slotId, out DateTime period)
    {
        period = default;
        if (DateTime.TryParseExact(
                periodText,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exactMonth))
        {
            period = new DateTime(exactMonth.Year, exactMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(periodText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            period = new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        var compact = new string(slotId.Where(char.IsDigit).ToArray());
        if (compact.Length >= 6 &&
            int.TryParse(compact[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
            int.TryParse(compact[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var month) &&
            month is >= 1 and <= 12)
        {
            period = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private sealed record CensusRetailSalesRow(
        string CategoryCode,
        string DataTypeCode,
        string SeasonallyAdjusted,
        string CellValue,
        string ErrorData,
        DateTime Period,
        string PeriodLabel,
        string RawJson);
}

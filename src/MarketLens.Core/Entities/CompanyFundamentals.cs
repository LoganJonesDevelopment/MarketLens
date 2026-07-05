namespace MarketLens.Core.Entities;

public class CompanyFundamentals
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = "ok";
    public string? Error { get; set; }
    public string? Name { get; set; }
    public string? Exchange { get; set; }
    public string? Industry { get; set; }
    public string? Currency { get; set; }
    public string? WebUrl { get; set; }
    public DateOnly? IpoDate { get; set; }
    public decimal? MarketCapitalizationMillion { get; set; }
    public decimal? ShareOutstandingMillion { get; set; }
    public decimal? EnterpriseValueMillion { get; set; }
    public decimal? PeTtm { get; set; }
    public decimal? ForwardPe { get; set; }
    public decimal? PegTtm { get; set; }
    public decimal? PsTtm { get; set; }
    public decimal? EvRevenueTtm { get; set; }
    public decimal? EvEbitdaTtm { get; set; }
    public decimal? PriceToBook { get; set; }
    public decimal? PriceToFreeCashFlowTtm { get; set; }
    public decimal? RevenueGrowthTtmYoy { get; set; }
    public decimal? EpsGrowthTtmYoy { get; set; }
    public decimal? GrossMarginTtm { get; set; }
    public decimal? OperatingMarginTtm { get; set; }
    public decimal? NetMarginTtm { get; set; }
    public decimal? RoeTtm { get; set; }
    public decimal? DebtToEquityQuarterly { get; set; }
    public decimal? Beta { get; set; }
    public decimal? Week52High { get; set; }
    public decimal? Week52Low { get; set; }
    public decimal? Week52PriceReturnDaily { get; set; }
    public string RawProfileJson { get; set; } = "{}";
    public string RawMetricJson { get; set; } = "{}";
    public DateTime IngestedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

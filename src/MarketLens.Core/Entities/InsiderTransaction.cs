namespace MarketLens.Core.Entities;

public class InsiderTransaction
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public int LineNumber { get; set; }

    public string IssuerCik { get; set; } = string.Empty;
    public string IssuerName { get; set; } = string.Empty;
    public string IssuerSymbol { get; set; } = string.Empty;

    public string OwnerCik { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public bool IsDirector { get; set; }
    public bool IsOfficer { get; set; }
    public bool IsTenPercentOwner { get; set; }
    public bool IsOther { get; set; }
    public string? OfficerTitle { get; set; }

    public string? SecurityTitle { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string AcquiredDisposedCode { get; set; } = string.Empty;
    public decimal? Shares { get; set; }
    public decimal? PricePerShare { get; set; }
    public decimal? SharesOwnedFollowing { get; set; }
    public string? DirectOrIndirectOwnership { get; set; }

    public bool IsOpenMarketTrade { get; set; }
    public bool IsDerivative { get; set; }

    public DateTime ParsedAt { get; set; }
}

using System.Globalization;
using System.Xml.Linq;

namespace MarketLens.Infrastructure.Sources;

public record Form4ReportingOwner(
    string OwnerCik,
    string OwnerName,
    bool IsDirector,
    bool IsOfficer,
    bool IsTenPercentOwner,
    bool IsOther,
    string? OfficerTitle);

public record Form4Transaction(
    int LineNumber,
    bool IsDerivative,
    string? SecurityTitle,
    DateTime? TransactionDate,
    string TransactionCode,
    string AcquiredDisposedCode,
    decimal? Shares,
    decimal? PricePerShare,
    decimal? SharesOwnedFollowing,
    string? DirectOrIndirectOwnership)
{
    public bool IsOpenMarketTrade =>
        !IsDerivative && TransactionCode is "P" or "S";

    public decimal? DollarValue =>
        Shares.HasValue && PricePerShare.HasValue
            ? Shares.Value * PricePerShare.Value
            : null;
}

public record Form4Document(
    string IssuerCik,
    string IssuerName,
    string IssuerSymbol,
    Form4ReportingOwner Owner,
    IReadOnlyList<Form4Transaction> Transactions)
{
    public decimal NetSharesAcquired =>
        Transactions
            .Where(t => t.AcquiredDisposedCode == "A" && t.Shares.HasValue)
            .Sum(t => t.Shares!.Value);

    public decimal NetSharesDisposed =>
        Transactions
            .Where(t => t.AcquiredDisposedCode == "D" && t.Shares.HasValue)
            .Sum(t => t.Shares!.Value);

    public decimal AcquisitionDollarValue =>
        Transactions
            .Where(t => t.AcquiredDisposedCode == "A" && t.DollarValue.HasValue)
            .Sum(t => t.DollarValue!.Value);

    public decimal DispositionDollarValue =>
        Transactions
            .Where(t => t.AcquiredDisposedCode == "D" && t.DollarValue.HasValue)
            .Sum(t => t.DollarValue!.Value);

    public bool HasOpenMarketTrade =>
        Transactions.Any(t => t.IsOpenMarketTrade);
}

/// <summary>
/// SEC Form 4 ownershipDocument XML parser. Defensive against missing optional
/// elements — returns null fields rather than throwing on incomplete filings.
/// </summary>
public static class Form4Parser
{
    public static Form4Document? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }

        var root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "ownershipDocument", StringComparison.Ordinal))
            return null;

        var issuer = root.Element("issuer");
        var issuerCik = ValueOf(issuer?.Element("issuerCik")) ?? string.Empty;
        var issuerName = ValueOf(issuer?.Element("issuerName")) ?? string.Empty;
        var issuerSymbol = ValueOf(issuer?.Element("issuerTradingSymbol")) ?? string.Empty;

        // Some filings have multiple reportingOwner elements (e.g. joint filers); take the first.
        var ownerEl = root.Element("reportingOwner");
        if (ownerEl is null) return null;

        var ownerIdEl = ownerEl.Element("reportingOwnerId");
        var ownerCik = ValueOf(ownerIdEl?.Element("rptOwnerCik")) ?? string.Empty;
        var ownerName = ValueOf(ownerIdEl?.Element("rptOwnerName")) ?? string.Empty;

        var relEl = ownerEl.Element("reportingOwnerRelationship");
        var isDirector = ParseBool(ValueOf(relEl?.Element("isDirector")));
        var isOfficer = ParseBool(ValueOf(relEl?.Element("isOfficer")));
        var isTenPercent = ParseBool(ValueOf(relEl?.Element("isTenPercentOwner")));
        var isOther = ParseBool(ValueOf(relEl?.Element("isOther")));
        var officerTitle = ValueOf(relEl?.Element("officerTitle"));

        var owner = new Form4ReportingOwner(
            ownerCik, ownerName, isDirector, isOfficer, isTenPercent, isOther, officerTitle);

        var transactions = new List<Form4Transaction>();
        var line = 0;

        var nonDerivative = root.Element("nonDerivativeTable");
        if (nonDerivative is not null)
        {
            foreach (var t in nonDerivative.Elements("nonDerivativeTransaction"))
                transactions.Add(ParseTransaction(t, ++line, isDerivative: false));
        }

        var derivative = root.Element("derivativeTable");
        if (derivative is not null)
        {
            foreach (var t in derivative.Elements("derivativeTransaction"))
                transactions.Add(ParseTransaction(t, ++line, isDerivative: true));
        }

        return new Form4Document(issuerCik, issuerName, issuerSymbol, owner, transactions);
    }

    private static Form4Transaction ParseTransaction(XElement t, int lineNumber, bool isDerivative)
    {
        var securityTitle = ValueOf(t.Element("securityTitle"));
        var dateStr = ValueOf(t.Element("transactionDate"));
        DateTime? transactionDate = null;
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            transactionDate = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        var coding = t.Element("transactionCoding");
        var transactionCode = ValueOfDirect(coding?.Element("transactionCode")) ?? string.Empty;

        var amounts = t.Element("transactionAmounts");
        var shares = ParseDecimal(ValueOf(amounts?.Element("transactionShares")));
        var price = ParseDecimal(ValueOf(amounts?.Element("transactionPricePerShare")));
        var ad = ValueOf(amounts?.Element("transactionAcquiredDisposedCode")) ?? string.Empty;

        var post = t.Element("postTransactionAmounts");
        var sharesOwnedFollowing = ParseDecimal(ValueOf(post?.Element("sharesOwnedFollowingTransaction")));

        var ownership = t.Element("ownershipNature");
        var direct = ValueOf(ownership?.Element("directOrIndirectOwnership"));

        return new Form4Transaction(
            LineNumber: lineNumber,
            IsDerivative: isDerivative,
            SecurityTitle: securityTitle,
            TransactionDate: transactionDate,
            TransactionCode: transactionCode,
            AcquiredDisposedCode: ad,
            Shares: shares,
            PricePerShare: price,
            SharesOwnedFollowing: sharesOwnedFollowing,
            DirectOrIndirectOwnership: direct);
    }

    /// <summary>
    /// Most Form 4 leaves wrap the value in a child &lt;value&gt; element.
    /// Falls back to the element's own text for elements that don't.
    /// </summary>
    private static string? ValueOf(XElement? element)
    {
        if (element is null) return null;
        var inner = element.Element("value");
        if (inner is not null)
        {
            var v = inner.Value?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
        var direct = element.Value?.Trim();
        return string.IsNullOrEmpty(direct) ? null : direct;
    }

    /// <summary>
    /// Reads the element's own text without checking for a wrapped &lt;value&gt; child.
    /// Used for leaves like &lt;transactionCode&gt; that are never wrapped.
    /// </summary>
    private static string? ValueOfDirect(XElement? element)
    {
        if (element is null) return null;
        var v = element.Value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static bool ParseBool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "1") return true;
        return false;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }
}

public static class Form4HeadlineBuilder
{
    /// <summary>
    /// Build an informative headline summarizing a Form 4 filing.
    /// e.g. "CVNA Form 4: Insider GILL DANIEL J. (Chief Product Officer) acquired 19,879 shares (grant, $0); disposed 10,095 shares at $396.59 (tax withhold, $4.0M)"
    /// </summary>
    public static string Build(string symbolOrIssuerName, Form4Document doc)
    {
        var who = string.IsNullOrWhiteSpace(doc.Owner.OwnerName) ? "Insider" : doc.Owner.OwnerName;
        var role = OwnerRole(doc.Owner);

        var header = $"{symbolOrIssuerName} Form 4: {who}";
        if (!string.IsNullOrWhiteSpace(role)) header += $" ({role})";

        // Roll up by acquired/disposed buckets
        var nondv = doc.Transactions.Where(t => !t.IsDerivative).ToList();
        if (nondv.Count == 0)
        {
            return $"{header} — derivative transaction(s) only";
        }

        var parts = new List<string>();
        var acquired = nondv.Where(t => t.AcquiredDisposedCode == "A" && t.Shares.HasValue).ToList();
        var disposed = nondv.Where(t => t.AcquiredDisposedCode == "D" && t.Shares.HasValue).ToList();

        if (acquired.Count > 0)
        {
            var shares = acquired.Sum(t => t.Shares!.Value);
            var dollars = acquired.Sum(t => t.DollarValue ?? 0m);
            var codes = string.Join("/", acquired.Select(t => t.TransactionCode).Distinct());
            parts.Add($"acquired {Format(shares)} shares ({DescribeCodes(codes)}, {FormatDollars(dollars)})");
        }

        if (disposed.Count > 0)
        {
            var shares = disposed.Sum(t => t.Shares!.Value);
            var dollars = disposed.Sum(t => t.DollarValue ?? 0m);
            var codes = string.Join("/", disposed.Select(t => t.TransactionCode).Distinct());
            // Average price across dispositions when prices are non-zero
            var pricedShares = disposed.Where(t => t.PricePerShare > 0 && t.Shares.HasValue).Sum(t => t.Shares!.Value);
            var weightedPrice = pricedShares > 0
                ? disposed.Where(t => t.PricePerShare > 0 && t.Shares.HasValue)
                    .Sum(t => t.Shares!.Value * t.PricePerShare!.Value) / pricedShares
                : (decimal?)null;
            var atPrice = weightedPrice.HasValue ? $" at ${weightedPrice.Value:F2}" : string.Empty;
            parts.Add($"disposed {Format(shares)} shares{atPrice} ({DescribeCodes(codes)}, {FormatDollars(dollars)})");
        }

        if (parts.Count == 0)
        {
            return $"{header} — {nondv.Count} non-derivative transaction(s)";
        }

        return $"{header} {string.Join("; ", parts)}";
    }

    private static string OwnerRole(Form4ReportingOwner owner)
    {
        if (owner.IsOfficer && !string.IsNullOrWhiteSpace(owner.OfficerTitle))
            return owner.OfficerTitle!;
        var roles = new List<string>();
        if (owner.IsDirector) roles.Add("director");
        if (owner.IsOfficer) roles.Add("officer");
        if (owner.IsTenPercentOwner) roles.Add("10% owner");
        if (owner.IsOther) roles.Add("other");
        return string.Join(", ", roles);
    }

    private static string DescribeCodes(string codes)
    {
        if (string.IsNullOrWhiteSpace(codes)) return "n/a";
        // Translate single codes inline; multi-code combos pass through as-is.
        return codes switch
        {
            "P" => "open-market buy",
            "S" => "open-market sale",
            "A" => "grant",
            "M" => "exercise",
            "F" => "tax withhold",
            "G" => "gift",
            "D" => "sale to issuer",
            "X" => "option exercise",
            _ => $"codes {codes}",
        };
    }

    private static string Format(decimal n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatDollars(decimal n)
    {
        if (n == 0m) return "$0";
        if (Math.Abs(n) >= 1_000_000m)
            return $"${n / 1_000_000m:F1}M";
        if (Math.Abs(n) >= 1_000m)
            return $"${n / 1_000m:F1}K";
        return $"${n:F0}";
    }
}

using MarketLens.Infrastructure.Sources;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class Form4ParserTests
{
    private static string ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_OpenMarketSale_ExtractsAllNonDerivativeRows()
    {
        var xml = ReadFixture("nvda_form4_sale.xml");

        var doc = Form4Parser.Parse(xml);

        Assert.NotNull(doc);
        Assert.Equal("0001045810", doc!.IssuerCik);
        Assert.Equal("NVIDIA CORP", doc.IssuerName);
        Assert.Equal("NVDA", doc.IssuerSymbol);

        Assert.Equal("Shah Aarti S.", doc.Owner.OwnerName);
        Assert.Equal("0001725292", doc.Owner.OwnerCik);
        Assert.True(doc.Owner.IsDirector);
        Assert.False(doc.Owner.IsOfficer);
        Assert.False(doc.Owner.IsTenPercentOwner);
        Assert.Null(doc.Owner.OfficerTitle);

        Assert.Equal(3, doc.Transactions.Count);
        Assert.All(doc.Transactions, t => Assert.Equal("S", t.TransactionCode));
        Assert.All(doc.Transactions, t => Assert.Equal("D", t.AcquiredDisposedCode));
        Assert.All(doc.Transactions, t => Assert.True(t.IsOpenMarketTrade));
        Assert.All(doc.Transactions, t => Assert.False(t.IsDerivative));

        var t1 = doc.Transactions[0];
        Assert.Equal(8516m, t1.Shares);
        Assert.Equal(176.2743m, t1.PricePerShare);
        Assert.Equal(46491m, t1.SharesOwnedFollowing);
        Assert.Equal("Common", t1.SecurityTitle);
        Assert.Equal(new DateTime(2026, 3, 19, 0, 0, 0, DateTimeKind.Utc), t1.TransactionDate);

        // All three rows are dispositions; net acquired = 0
        Assert.Equal(0m, doc.NetSharesAcquired);
        Assert.Equal(8516m + 10282m + 202m, doc.NetSharesDisposed);

        // Dispositions dollar value
        var expectedDollars =
            8516m * 176.2743m + 10282m * 177.0565m + 202m * 177.7335m;
        Assert.Equal(expectedDollars, doc.DispositionDollarValue);
        Assert.True(doc.HasOpenMarketTrade);
    }

    [Fact]
    public void Parse_OpenMarketBuy_ExtractsCodeP()
    {
        var xml = ReadFixture("synthetic_form4_buy.xml");

        var doc = Form4Parser.Parse(xml);

        Assert.NotNull(doc);
        Assert.Equal("INTC", doc!.IssuerSymbol);
        Assert.Equal("SMITH JANE A.", doc.Owner.OwnerName);
        Assert.True(doc.Owner.IsDirector);

        Assert.Single(doc.Transactions);
        var tx = doc.Transactions[0];
        Assert.Equal("P", tx.TransactionCode);
        Assert.Equal("A", tx.AcquiredDisposedCode);
        Assert.Equal(50000m, tx.Shares);
        Assert.Equal(22.50m, tx.PricePerShare);
        Assert.Equal(175000m, tx.SharesOwnedFollowing);
        Assert.True(tx.IsOpenMarketTrade);
        Assert.Equal(50000m * 22.50m, tx.DollarValue);

        Assert.Equal(50000m, doc.NetSharesAcquired);
        Assert.Equal(0m, doc.NetSharesDisposed);
        Assert.Equal(50000m * 22.50m, doc.AcquisitionDollarValue);
        Assert.True(doc.HasOpenMarketTrade);
    }

    [Fact]
    public void Parse_GrantPlusTaxWithhold_ExtractsBothCodesAndOfficerTitle()
    {
        var xml = ReadFixture("cvna_form4_grant_and_tax.xml");

        var doc = Form4Parser.Parse(xml);

        Assert.NotNull(doc);
        Assert.Equal("CVNA", doc!.IssuerSymbol);
        Assert.Equal("CARVANA CO.", doc.IssuerName);

        Assert.Equal("GILL DANIEL J.", doc.Owner.OwnerName);
        Assert.True(doc.Owner.IsOfficer);
        Assert.False(doc.Owner.IsDirector);
        Assert.Equal("Chief Product Officer", doc.Owner.OfficerTitle);

        Assert.Equal(2, doc.Transactions.Count);

        var grant = doc.Transactions[0];
        Assert.Equal("A", grant.TransactionCode);
        Assert.Equal("A", grant.AcquiredDisposedCode);
        Assert.Equal(19879m, grant.Shares);
        Assert.Equal(0m, grant.PricePerShare);
        Assert.False(grant.IsOpenMarketTrade);  // grant, not P/S
        Assert.Equal(202345m, grant.SharesOwnedFollowing);

        var withhold = doc.Transactions[1];
        Assert.Equal("F", withhold.TransactionCode);
        Assert.Equal("D", withhold.AcquiredDisposedCode);
        Assert.Equal(10095m, withhold.Shares);
        Assert.Equal(396.59m, withhold.PricePerShare);
        Assert.False(withhold.IsOpenMarketTrade);
        Assert.Equal(192250m, withhold.SharesOwnedFollowing);

        Assert.Equal(19879m, doc.NetSharesAcquired);
        Assert.Equal(10095m, doc.NetSharesDisposed);
        Assert.False(doc.HasOpenMarketTrade);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(Form4Parser.Parse(""));
        Assert.Null(Form4Parser.Parse("   "));
        Assert.Null(Form4Parser.Parse(null!));
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsNull()
    {
        Assert.Null(Form4Parser.Parse("<not><well><formed"));
    }

    [Fact]
    public void Parse_WrongRoot_ReturnsNull()
    {
        Assert.Null(Form4Parser.Parse("<somethingElse><foo/></somethingElse>"));
    }

    [Fact]
    public void Parse_MissingOptionalFields_ProducesNulls_NotThrows()
    {
        const string xml = """
            <ownershipDocument>
                <documentType>4</documentType>
                <issuer>
                    <issuerCik>0000000001</issuerCik>
                    <issuerName>TEST CO.</issuerName>
                </issuer>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0000000002</rptOwnerCik>
                        <rptOwnerName>DOE JOHN</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                <nonDerivativeTable>
                    <nonDerivativeTransaction>
                        <transactionCoding>
                            <transactionCode>S</transactionCode>
                        </transactionCoding>
                    </nonDerivativeTransaction>
                </nonDerivativeTable>
            </ownershipDocument>
            """;

        var doc = Form4Parser.Parse(xml);

        Assert.NotNull(doc);
        Assert.Equal(string.Empty, doc!.IssuerSymbol);
        Assert.False(doc.Owner.IsDirector);
        Assert.False(doc.Owner.IsOfficer);
        Assert.Null(doc.Owner.OfficerTitle);

        Assert.Single(doc.Transactions);
        var t = doc.Transactions[0];
        Assert.Equal("S", t.TransactionCode);
        Assert.Equal(string.Empty, t.AcquiredDisposedCode);
        Assert.Null(t.Shares);
        Assert.Null(t.PricePerShare);
        Assert.Null(t.TransactionDate);
        Assert.Null(t.SecurityTitle);
        Assert.Null(t.SharesOwnedFollowing);
        Assert.Null(t.DirectOrIndirectOwnership);
    }

    [Fact]
    public void HeadlineBuilder_SaleOnly_ProducesDisposedSummaryWithWeightedPrice()
    {
        var xml = ReadFixture("nvda_form4_sale.xml");
        var doc = Form4Parser.Parse(xml)!;

        var headline = Form4HeadlineBuilder.Build("NVDA", doc);

        Assert.Contains("NVDA Form 4", headline);
        Assert.Contains("Shah Aarti S.", headline);
        Assert.Contains("director", headline);
        Assert.Contains("disposed", headline);
        Assert.Contains("19,000", headline);  // 8516+10282+202 = 19,000
        Assert.Contains("open-market sale", headline);
    }

    [Fact]
    public void HeadlineBuilder_BuyOnly_LabelsAsOpenMarketBuy()
    {
        var xml = ReadFixture("synthetic_form4_buy.xml");
        var doc = Form4Parser.Parse(xml)!;

        var headline = Form4HeadlineBuilder.Build("INTC", doc);

        Assert.Contains("INTC Form 4", headline);
        Assert.Contains("SMITH JANE A.", headline);
        Assert.Contains("acquired", headline);
        Assert.Contains("50,000", headline);
        Assert.Contains("open-market buy", headline);
        Assert.Contains("$1.1M", headline);
    }

    [Fact]
    public void HeadlineBuilder_GrantPlusWithhold_LabelsBothBuckets()
    {
        var xml = ReadFixture("cvna_form4_grant_and_tax.xml");
        var doc = Form4Parser.Parse(xml)!;

        var headline = Form4HeadlineBuilder.Build("CVNA", doc);

        Assert.Contains("CVNA Form 4", headline);
        Assert.Contains("GILL DANIEL J.", headline);
        Assert.Contains("Chief Product Officer", headline);
        Assert.Contains("acquired", headline);
        Assert.Contains("19,879", headline);
        Assert.Contains("grant", headline);
        Assert.Contains("disposed", headline);
        Assert.Contains("10,095", headline);
        Assert.Contains("tax withhold", headline);
        Assert.Contains("$396.59", headline);
    }
}

using MarketLens.Core.Domain;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class CommodityMetadataTests
{
    [Fact]
    public void DetectNames_FindsMetalsWithoutTickerSymbols()
    {
        var detected = CommodityMetadata.DetectNames(
            "Copper concentrate remains tight while uranium contracting and lithium carbonate prices recover.");

        Assert.Contains("Copper", detected);
        Assert.Contains("Uranium", detected);
        Assert.Contains("Lithium", detected);
    }

    [Fact]
    public void DetectNames_FindsUnderservedMetalsForResearchAssets()
    {
        var detected = CommodityMetadata.DetectNames(
            "Silver, aluminum, nickel and platinum group metals are part of the watchlist.");

        Assert.Contains("Silver", detected);
        Assert.Contains("Aluminum", detected);
        Assert.Contains("Nickel", detected);
        Assert.Contains("Platinum", detected);
        Assert.Contains("Palladium", detected);
    }

    [Fact]
    public void DetectPrimaryNames_IgnoresOneOffComparisonMentions()
    {
        var detected = CommodityMetadata.DetectPrimaryNames(
            "Copper deficit widens on AI infrastructure demand",
            "Copper prices are high, making this a higher-risk entry than uranium.");

        Assert.Contains("Copper", detected);
        Assert.DoesNotContain("Uranium", detected);
    }
}

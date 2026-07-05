using MarketLens.Infrastructure.Sources;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class YahooPriceBarSourceTests
{
    [Theory]
    [InlineData("BRK.B", "BRK-B")]
    [InlineData("BF.B", "BF-B")]
    [InlineData("DX-Y.NYB", "DX-Y.NYB")]
    [InlineData(" nvda ", "NVDA")]
    public void MapSymbol_UsesYahooClassShareSymbols(string input, string expected)
    {
        Assert.Equal(expected, YahooPriceBarSource.MapSymbol(input));
    }
}

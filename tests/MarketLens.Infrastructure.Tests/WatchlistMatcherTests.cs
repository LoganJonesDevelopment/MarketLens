using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class WatchlistMatcherTests
{
    private static readonly WatchedTicker Ups = new(
        Guid.NewGuid(),
        "UPS",
        "United Parcel Service",
        "0001090727",
        null,
        ["UPS", "United Parcel Service"]);

    private static readonly WatchedTicker Amp = new(
        Guid.NewGuid(),
        "AMP",
        "Ameriprise Financial",
        "0000820027",
        null,
        ["AMP", "Ameriprise"]);

    [Fact]
    public void Mentions_DoesNotTreatUpsSystemsAsUpsTicker()
    {
        Assert.False(WatchlistMatcher.Mentions(
            Ups,
            "Eaton backlog is supported by switchgear and UPS systems",
            null));
    }

    [Theory]
    [InlineData("$UPS shares rallied after earnings")]
    [InlineData("United Parcel Service raised its guidance")]
    [InlineData("Logistics earnings: (UPS) beats expectations")]
    [InlineData("NYSE:UPS volume rose into the close")]
    public void Mentions_AllowsExplicitUpsTickerOrCompanyContext(string text)
    {
        Assert.True(WatchlistMatcher.Mentions(Ups, text, null));
    }

    [Fact]
    public void Mentions_DoesNotTreatAmpAsBareTicker()
    {
        Assert.False(WatchlistMatcher.Mentions(
            Amp,
            "Cerebras details AMP inference performance for enterprise AI workloads",
            null));
    }

    [Theory]
    [InlineData("$AMP shares rallied after results")]
    [InlineData("(AMP) raised its dividend")]
    [InlineData("NYSE:AMP volume rose into the close")]
    [InlineData("Ameriprise reported quarterly earnings")]
    public void Mentions_AllowsExplicitAmpTickerOrCompanyContext(string text)
    {
        Assert.True(WatchlistMatcher.Mentions(Amp, text, null));
    }
}

using MarketLens.Core.Domain;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class PriceBarAggregationTests
{
    [Fact]
    public void Canonicalize_DailyRowsUseUtcMidnightAndLatestIngestedDuplicate()
    {
        var rows = new[]
        {
            new PriceBarValue(
                new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc),
                10m,
                12m,
                9m,
                11m,
                100,
                new DateTime(2026, 5, 8, 16, 0, 0, DateTimeKind.Utc)),
            new PriceBarValue(
                new DateTime(2026, 5, 8, 20, 0, 2, DateTimeKind.Utc),
                10.5m,
                13m,
                10m,
                12.5m,
                200,
                new DateTime(2026, 5, 8, 23, 0, 0, DateTimeKind.Utc)),
        };

        var canonical = PriceBarAggregation.Canonicalize("1d", rows);

        var row = Assert.Single(canonical);
        Assert.Equal(new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc), row.Timestamp);
        Assert.Equal(12.5m, row.Close);
        Assert.Equal(200, row.Volume);
    }

    [Fact]
    public void Build_WeeklyBarsAreAnchoredToCalendarWeeks()
    {
        var rows = new[]
        {
            Daily(2026, 5, 5, 10, 11, 9, 10.5m, 100),
            Daily(2026, 5, 8, 10.5m, 12, 10, 11.5m, 125),
            Daily(2026, 5, 11, 12, 14, 11, 13, 150),
        };

        var weekly = PriceBarAggregation.Build("1w", rows, limit: 10).ToArray();

        Assert.Equal(2, weekly.Length);
        Assert.Equal(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), weekly[0].Timestamp);
        Assert.Equal(10m, weekly[0].Open);
        Assert.Equal(12m, weekly[0].High);
        Assert.Equal(9m, weekly[0].Low);
        Assert.Equal(11.5m, weekly[0].Close);
        Assert.Equal(225, weekly[0].Volume);
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), weekly[1].Timestamp);
    }

    [Fact]
    public void Canonicalize_IntradayRowsUseIntervalStartAndLatestIngestedDuplicate()
    {
        var rows = new[]
        {
            new PriceBarValue(
                new DateTime(2026, 5, 11, 17, 0, 0, DateTimeKind.Utc),
                220m,
                221m,
                219m,
                220.5m,
                1000,
                new DateTime(2026, 5, 11, 17, 5, 0, DateTimeKind.Utc)),
            new PriceBarValue(
                new DateTime(2026, 5, 11, 17, 41, 48, DateTimeKind.Utc),
                220.25m,
                222m,
                220m,
                221.5m,
                1200,
                new DateTime(2026, 5, 11, 17, 42, 0, DateTimeKind.Utc),
                Live: true),
        };

        var canonical = PriceBarAggregation.Canonicalize("1h", rows);

        var row = Assert.Single(canonical);
        Assert.Equal(new DateTime(2026, 5, 11, 17, 0, 0, DateTimeKind.Utc), row.Timestamp);
        Assert.Equal(221.5m, row.Close);
        Assert.Equal(1200, row.Volume);
        Assert.True(row.Live);
    }

    [Fact]
    public void Build_ThreeDayBarsUseStableEpochBuckets()
    {
        var rows = new[]
        {
            Daily(1970, 1, 1, 10, 12, 9, 11, 100),
            Daily(1970, 1, 2, 11, 13, 10, 12, 100),
            Daily(1970, 1, 3, 12, 14, 11, 13, 100),
            Daily(1970, 1, 4, 13, 15, 12, 14, 100, live: true),
        };

        var grouped = PriceBarAggregation.Build("3d", rows, limit: 10).ToArray();

        Assert.Equal(2, grouped.Length);
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), grouped[0].Timestamp);
        Assert.Equal(10m, grouped[0].Open);
        Assert.Equal(14m, grouped[0].High);
        Assert.Equal(9m, grouped[0].Low);
        Assert.Equal(13m, grouped[0].Close);
        Assert.Equal(300, grouped[0].Volume);
        Assert.False(grouped[0].Live);
        Assert.Equal(new DateTime(1970, 1, 4, 0, 0, 0, DateTimeKind.Utc), grouped[1].Timestamp);
        Assert.True(grouped[1].Live);
    }

    private static PriceBarValue Daily(
        int year,
        int month,
        int day,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        bool live = false)
        => new(
            new DateTime(year, month, day, 13, 30, 0, DateTimeKind.Utc),
            open,
            high,
            low,
            close,
            volume,
            new DateTime(year, month, day, 21, 0, 0, DateTimeKind.Utc),
            live);
}

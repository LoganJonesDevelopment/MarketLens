using MarketLens.Core.Domain;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class LocalFetchCachePolicyTests
{
    [Fact]
    public void BuildCacheKey_NormalizesSourceAndTrimsUrl()
    {
        var first = LocalFetchCachePolicy.BuildCacheKey(" RSS ", " https://example.test/feed ");
        var second = LocalFetchCachePolicy.BuildCacheKey("rss", "https://example.test/feed");

        Assert.Equal(first, second);
        Assert.StartsWith("rss:", first);
    }

    [Fact]
    public void BuildCacheKey_IncludesVaryInputs()
    {
        var first = LocalFetchCachePolicy.BuildCacheKey("api", "https://example.test/data", "symbol=NVDA");
        var second = LocalFetchCachePolicy.BuildCacheKey("api", "https://example.test/data", "symbol=AMD");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CalculateExpiresAt_UsesSuccessTtlForSuccessfulResponses()
    {
        var fetchedAt = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

        var expiresAt = LocalFetchCachePolicy.CalculateExpiresAt(
            fetchedAt,
            success: true,
            successTtl: TimeSpan.FromHours(6),
            negativeTtl: TimeSpan.FromMinutes(15));

        Assert.Equal(fetchedAt.AddHours(6), expiresAt);
    }

    [Fact]
    public void CalculateExpiresAt_UsesNegativeTtlForFailedResponses()
    {
        var fetchedAt = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

        var expiresAt = LocalFetchCachePolicy.CalculateExpiresAt(
            fetchedAt,
            success: false,
            successTtl: TimeSpan.FromHours(6),
            negativeTtl: TimeSpan.FromMinutes(15));

        Assert.Equal(fetchedAt.AddMinutes(15), expiresAt);
    }

    [Fact]
    public void IsFresh_RequiresExpirationAfterNow()
    {
        var now = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(LocalFetchCachePolicy.IsFresh(now.AddTicks(1), now));
        Assert.False(LocalFetchCachePolicy.IsFresh(now, now));
    }
}

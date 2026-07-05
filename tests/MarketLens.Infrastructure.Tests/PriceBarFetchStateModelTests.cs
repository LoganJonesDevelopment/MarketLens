using MarketLens.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class PriceBarFetchStateModelTests
{
    [Fact]
    public void PriceBarFetchStateHasProviderScopedIdentityAndRetryIndexes()
    {
        using var db = TestDbContextFactory.CreateModelContext();
        var entity = db.Model.FindEntityType(typeof(PriceBarFetchState));

        Assert.NotNull(entity);
        Assert.Equal("price_bar_fetch_states", entity.GetTableName());
        Assert.Equal(32, entity.FindProperty(nameof(PriceBarFetchState.Symbol))!.GetMaxLength());
        Assert.Equal(8, entity.FindProperty(nameof(PriceBarFetchState.Interval))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(PriceBarFetchState.Provider))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(PriceBarFetchState.ProviderSymbol))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(PriceBarFetchState.Status))!.GetMaxLength());
        Assert.Equal(2048, entity.FindProperty(nameof(PriceBarFetchState.LastError))!.GetMaxLength());

        Assert.Equal(
            [
                nameof(PriceBarFetchState.Symbol),
                nameof(PriceBarFetchState.Interval),
                nameof(PriceBarFetchState.Provider),
            ],
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name));

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(PriceBarFetchState.NextAttemptAt),
        ]));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(PriceBarFetchState.Interval),
            nameof(PriceBarFetchState.NextAttemptAt),
        ]));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(PriceBarFetchState.Status),
            nameof(PriceBarFetchState.NextAttemptAt),
        ]));
    }
}

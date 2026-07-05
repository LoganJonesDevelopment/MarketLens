using MarketLens.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class SourceCursorAndFetchCacheModelTests
{
    [Fact]
    public void SourceCursorStateHasDurableIdentityAndEligibilityIndexes()
    {
        using var db = TestDbContextFactory.CreateModelContext();
        var entity = db.Model.FindEntityType(typeof(SourceCursorState));

        Assert.NotNull(entity);
        Assert.Equal("source_cursor_states", entity.GetTableName());
        Assert.Equal(128, entity.FindProperty(nameof(SourceCursorState.SourceName))!.GetMaxLength());
        Assert.Equal(256, entity.FindProperty(nameof(SourceCursorState.SourceKey))!.GetMaxLength());
        Assert.Equal("jsonb", entity.FindProperty(nameof(SourceCursorState.CursorJson))!.GetColumnType());
        Assert.Equal(512, entity.FindProperty(nameof(SourceCursorState.LastItemId))!.GetMaxLength());
        Assert.Equal(2048, entity.FindProperty(nameof(SourceCursorState.LastError))!.GetMaxLength());

        var identityIndex = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
            [
                nameof(SourceCursorState.SourceName),
                nameof(SourceCursorState.SourceKey),
            ]));

        Assert.True(identityIndex.IsUnique);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(SourceCursorState.NextEligibleRunAt),
        ]));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(SourceCursorState.SourceName),
            nameof(SourceCursorState.NextEligibleRunAt),
        ]));
    }

    [Fact]
    public void LocalFetchCacheHasStableLookupAndExpirationIndexes()
    {
        using var db = TestDbContextFactory.CreateModelContext();
        var entity = db.Model.FindEntityType(typeof(LocalFetchCacheEntry));

        Assert.NotNull(entity);
        Assert.Equal("local_fetch_cache", entity.GetTableName());
        Assert.Equal(128, entity.FindProperty(nameof(LocalFetchCacheEntry.CacheKey))!.GetMaxLength());
        Assert.Equal(2048, entity.FindProperty(nameof(LocalFetchCacheEntry.Url))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(LocalFetchCacheEntry.Source))!.GetMaxLength());
        Assert.Equal(256, entity.FindProperty(nameof(LocalFetchCacheEntry.ContentType))!.GetMaxLength());
        Assert.Equal(512, entity.FindProperty(nameof(LocalFetchCacheEntry.ETag))!.GetMaxLength());
        Assert.Equal(256, entity.FindProperty(nameof(LocalFetchCacheEntry.LastModified))!.GetMaxLength());
        Assert.Equal(2048, entity.FindProperty(nameof(LocalFetchCacheEntry.ErrorText))!.GetMaxLength());

        var cacheKeyIndex = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
            [
                nameof(LocalFetchCacheEntry.CacheKey),
            ]));

        Assert.True(cacheKeyIndex.IsUnique);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(LocalFetchCacheEntry.ExpiresAt),
        ]));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(LocalFetchCacheEntry.Source),
            nameof(LocalFetchCacheEntry.ExpiresAt),
        ]));
    }
}

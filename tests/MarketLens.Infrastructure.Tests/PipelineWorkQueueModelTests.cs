using MarketLens.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class PipelineWorkQueueModelTests
{
    [Fact]
    public void PipelineWorkItemsDedupeOnlyActiveWork()
    {
        using var db = TestDbContextFactory.CreateModelContext();
        var entity = db.Model.FindEntityType(typeof(PipelineWorkItem));

        Assert.NotNull(entity);

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
            [
                nameof(PipelineWorkItem.WorkType),
                nameof(PipelineWorkItem.NaturalKey),
            ]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"Status\" IN ('queued', 'running')", index.GetFilter());
    }
}

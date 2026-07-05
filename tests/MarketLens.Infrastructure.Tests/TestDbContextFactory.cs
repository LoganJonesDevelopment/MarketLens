using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace MarketLens.Infrastructure.Tests;

internal static class TestDbContextFactory
{
    public static MarketLensDbContext CreateModelContext()
    {
        var options = new DbContextOptionsBuilder<MarketLensDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=marketlens_model_test",
                npgsql => npgsql.UseVector())
            .Options;

        return new MarketLensDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MarketLens.Infrastructure.Data;

public class MarketLensDbContextFactory : IDesignTimeDbContextFactory<MarketLensDbContext>
{
    public MarketLensDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MarketLensDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5434;Database=marketlens;Username=marketlens;Password=marketlens",
                npgsql => npgsql.UseVector())
            .Options;

        return new MarketLensDbContext(options);
    }
}

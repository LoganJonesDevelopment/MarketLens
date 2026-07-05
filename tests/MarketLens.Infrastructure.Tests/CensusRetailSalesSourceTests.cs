using System.Net;
using MarketLens.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class CensusRetailSalesSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsLatestRetailSalesRows()
    {
        const string json = """
            [
              ["data_type_code","time_slot_id","time_slot_date","time_slot_name","seasonally_adj","category_code","cell_value","error_data","time","us"],
              ["SM","202601","2026-01","Jan. 2026","yes","44X72","720100","no","2026","1"],
              ["SM","202604","2026-04","Apr. 2026","yes","44X72","724100","no","2026","1"],
              ["SM","202604","2026-04","Apr. 2026","yes","44000","640200","no","2026","1"]
            ]
            """;

        var source = new CensusRetailSalesSource(
            new HttpClient(new StaticResponseHandler(json)),
            Options.Create(new CensusRetailSalesOptions
            {
                BaseUrl = "https://api.census.test/data/timeseries/eits/mrtsadv",
                ApiKey = "test-key",
                LookbackYears = 1,
                Categories =
                [
                    new CensusRetailSalesCategory { CategoryCode = "44X72", Label = "Retail and food services sales" },
                    new CensusRetailSalesCategory { CategoryCode = "44000", Label = "Retail trade sales" },
                ],
            }),
            NullLogger<CensusRetailSalesSource>.Instance);

        var items = await source.FetchAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal("census", item.Source);
            Assert.Null(item.Symbol);
            Assert.Contains("2026-04", item.SourceId);
            Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), item.PublishedAt);
        });
        Assert.Contains(items, item => item.Headline.Contains("Retail and food services sales", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, item => item.Headline.Contains("Retail trade sales", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
    }
}

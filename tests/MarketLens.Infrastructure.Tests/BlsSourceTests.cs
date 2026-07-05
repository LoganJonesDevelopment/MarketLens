using System.Net;
using MarketLens.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class BlsSourceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsLatestObservationPerConfiguredSeries()
    {
        const string baseUrl = "https://api.bls.test/publicAPI/v2/timeseries/data";
        const string json = """
            {
              "status": "REQUEST_SUCCEEDED",
              "Results": {
                "series": [
                  {
                    "seriesID": "CUSR0000SA0",
                    "data": [
                      { "year": "2026", "period": "M04", "periodName": "April", "value": "332.407" },
                      { "year": "2026", "period": "M03", "periodName": "March", "value": "331.123" },
                      { "year": "2025", "period": "M04", "periodName": "April", "value": "318.992" }
                    ]
                  }
                ]
              }
            }
            """;

        var source = new BlsSource(
            new HttpClient(new StaticResponseHandler(json)),
            Options.Create(new BlsOptions
            {
                BaseUrl = baseUrl,
                Series =
                [
                    new BlsSeriesConfig
                    {
                        SeriesId = "CUSR0000SA0",
                        Label = "CPI-U all items",
                        Release = "Consumer Price Index",
                        Url = "https://www.bls.gov/news.release/cpi.htm",
                    },
                ],
            }),
            NullLogger<BlsSource>.Instance);

        var item = Assert.Single(await source.FetchAsync());

        Assert.Equal("bls", item.Source);
        Assert.Null(item.Symbol);
        Assert.Equal("bls-api:CUSR0000SA0:2026:M04:332.407", item.SourceId);
        Assert.Contains("CPI-U all items", item.Headline);
        Assert.Contains("month-over-month", item.Summary);
        Assert.Contains("year-over-year", item.Summary);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), item.PublishedAt);
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

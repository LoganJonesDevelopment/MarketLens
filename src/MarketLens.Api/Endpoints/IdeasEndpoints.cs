using MarketLens.Api.Services;
using MarketLens.Api.Services.Ideas;

namespace MarketLens.Api.Endpoints;

public static class IdeasEndpoints
{
    public static void MapIdeasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ideas");

        group.MapGet("/forward/pipelines", (ForwardIdeasService ideas) =>
            Results.Ok(ideas.ListPipelines()));

        group.MapGet("/forward", async (
            ForwardIdeasService ideas,
            string? thesis,
            string? modules,
            int? windowDays,
            int? take,
            bool? includeCrowded,
            CancellationToken ct) =>
            Results.Ok(await ideas.GetForwardAsync(thesis, modules, windowDays, take, includeCrowded, ct)));

        group.MapGet("/radar", async (
            IdeaMarketDataService ideas,
            int? windowDays,
            int? take,
            CancellationToken ct) =>
            Results.Ok(await ideas.GetRadarAsync(windowDays, take, ct)));

        group.MapGet("/symbols/{symbol}", async (
            IdeaMarketDataService ideas,
            string symbol,
            int? windowDays,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await ideas.GetSymbolBriefAsync(symbol, windowDays, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/symbols/{symbol}/memo", async (
            IdeaMemoService memoService,
            string symbol,
            int? windowDays,
            CancellationToken ct) =>
        {
            try
            {
                var memo = await memoService.GetOrQueueAsync(symbol, Math.Clamp(windowDays ?? 90, 7, 365), ct);
                return Results.Ok(memo);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/symbols/{symbol}/evidence", async (
            IdeaMemoService memoService,
            string symbol,
            string evidenceId,
            string? evidenceHash,
            int? windowDays,
            CancellationToken ct) =>
        {
            try
            {
                var evidence = await memoService.ResolveEvidenceAsync(
                    symbol,
                    Math.Clamp(windowDays ?? 90, 7, 365),
                    evidenceId,
                    evidenceHash,
                    ct);

                return evidence is null
                    ? Results.NotFound(new { error = "evidence not found", evidenceId })
                    : Results.Ok(evidence);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPost("/symbols/{symbol}/memo/refresh", async (
            IdeaMemoService memoService,
            string symbol,
            int? windowDays,
            bool? force,
            CancellationToken ct) =>
        {
            try
            {
                var memo = await memoService.RefreshAsync(
                    symbol,
                    Math.Clamp(windowDays ?? 90, 7, 365),
                    force ?? true,
                    ct);
                return Results.Ok(memo);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

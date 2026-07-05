using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapExplorationEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/explorations", async (
            MarketLensDbContext db,
            IEmbeddingClient embedder,
            ThesisBootstrapper bootstrapper,
            ILoggerFactory loggerFactory,
            CreateExplorationRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ThesisText))
                return Results.BadRequest(new { error = "thesisText is required" });

            var hypothesisText = request.ThesisText.Trim();
            var name = EmptyToNull(request.Name) ?? DeriveName(hypothesisText);
            var now = DateTime.UtcNow;
            var exploration = new ResearchThesis
            {
                Id = Guid.NewGuid(),
                Name = name,
                Status = ThesisStatuses.Exploration,
                ThesisText = hypothesisText,
                CreatedAt = now,
                UpdatedAt = now,
                Embedding = await TryEmbedAsync(embedder, hypothesisText, loggerFactory, ct),
            };

            db.ResearchTheses.Add(exploration);
            await db.SaveChangesAsync(ct);

            await AutoBindAssetsAsync(db, exploration.Id, $"{name} {hypothesisText}", ct);

            var bootstrapResult = await bootstrapper.BootstrapAsync(exploration.Id, ct);
            return Results.Ok(new
            {
                explorationId = exploration.Id,
                bootstrap = bootstrapResult,
            });
        });
    }

    private static string DeriveName(string thesisText)
    {
        var firstLine = thesisText.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        if (firstLine.Length <= 80) return firstLine.Length == 0 ? "Untitled exploration" : firstLine;
        var truncated = firstLine[..80];
        var lastSpace = truncated.LastIndexOf(' ');
        return (lastSpace > 40 ? truncated[..lastSpace] : truncated).TrimEnd(',', ';', '.', ':') + "…";
    }
}

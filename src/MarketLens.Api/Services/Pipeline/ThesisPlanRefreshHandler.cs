using System.Text.Json;
using MarketLens.Api.HostedServices;

namespace MarketLens.Api.Services.Pipeline;

public sealed record ThesisPlanRefreshResult(bool Processed, bool Generated, string? Error);

public sealed class ThesisPlanRefreshHandler(
    ThesisBootstrapper bootstrapper,
    ILogger<ThesisPlanRefreshHandler> logger)
{
    public async Task<ThesisPlanRefreshResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var thesisId = ParseThesisId(naturalKey, payloadJson);
        if (thesisId is null)
            return new ThesisPlanRefreshResult(false, false, "invalid thesis id");

        var result = await bootstrapper.BootstrapAsync(thesisId.Value, cancellationToken);
        if (result.Error is not null)
        {
            logger.LogWarning("Plan refresh failed for {ThesisId}: {Error}", thesisId, result.Error);
            return new ThesisPlanRefreshResult(true, false, result.Error);
        }

        logger.LogInformation(
            "Refreshed plan for {ThesisId}: {SubTracks} sub-tracks against {Corpus} clusters",
            thesisId,
            result.SubTracksCreated,
            result.CorpusContextSize);

        return new ThesisPlanRefreshResult(true, result.PlanGenerated, null);
    }

    private static Guid? ParseThesisId(string naturalKey, string payloadJson)
    {
        if (Guid.TryParse(naturalKey, out var id))
            return id;

        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("thesisId", out var thesisId) &&
                Guid.TryParse(thesisId.GetString(), out id))
                return id;
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

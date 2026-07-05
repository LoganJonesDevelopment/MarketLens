using System.Text.Json;
using MarketLens.Api.HostedServices;

namespace MarketLens.Api.Services.Pipeline;

public sealed record ThesisBootstrapWorkResult(bool Processed, bool PlanGenerated, int EvidenceAdded, string? Error);

public sealed class ThesisBootstrapWorkHandler(
    ThesisBootstrapper bootstrapper,
    ResearchMatcher matcher,
    ILogger<ThesisBootstrapWorkHandler> logger)
{
    private const int InitialScanLookbackHours = 720;

    public async Task<ThesisBootstrapWorkResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var thesisId = ParseThesisId(naturalKey, payloadJson);
        if (thesisId is null)
            return new ThesisBootstrapWorkResult(false, false, 0, "invalid thesis id");

        var bootstrap = await bootstrapper.BootstrapAsync(thesisId.Value, cancellationToken);
        if (bootstrap.Error is not null)
            logger.LogWarning("Bootstrap failed for thesis {ThesisId}: {Error}", thesisId, bootstrap.Error);

        var scan = await matcher.ScanAsync(
            new ResearchScanRequest(
                ThesisId: thesisId.Value,
                ActiveOnly: false,
                LookbackHours: InitialScanLookbackHours),
            cancellationToken);

        logger.LogInformation(
            "Bootstrapped thesis {ThesisId}: {SubTracks} sub-tracks against {Corpus} clusters, initial scan added {EvidenceAdded} evidence rows",
            thesisId,
            bootstrap.SubTracksCreated,
            bootstrap.CorpusContextSize,
            scan.EvidenceAdded);

        return new ThesisBootstrapWorkResult(true, bootstrap.PlanGenerated, scan.EvidenceAdded, bootstrap.Error);
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

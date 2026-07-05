using MarketLens.Api.HostedServices;

namespace MarketLens.Api.Services.Pipeline;

public sealed record ResearchMatchThesisResult(
    Guid ThesisId,
    int ArticlesScanned,
    int EventsScanned,
    int SegmentsScanned,
    int ChunksScanned,
    int EvidenceAdded);

public sealed class ResearchMatchThesisHandler(
    ResearchMatcher matcher,
    ILogger<ResearchMatchThesisHandler> logger)
{
    public async Task<ResearchMatchThesisResult> ProcessAsync(
        Guid thesisId,
        ResearchScanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await matcher.ScanAsync(
            request with { ThesisId = thesisId },
            cancellationToken);

        logger.LogInformation(
            "Research matcher scanned thesis {ThesisId}: {EvidenceAdded} evidence added",
            thesisId,
            result.EvidenceAdded);

        return new ResearchMatchThesisResult(
            thesisId,
            result.ArticlesScanned,
            result.EventsScanned,
            result.SegmentsScanned,
            result.ChunksScanned,
            result.EvidenceAdded);
    }
}

using System.Text.Json;

namespace MarketLens.Api.Endpoints.Research;

public sealed record CreateExplorationRequest(
    string? Name,
    string? ThesisText);

public sealed record CreateThesisRequest(
    string? Name,
    string? ThesisText,
    string? Status,
    string? Summary,
    IReadOnlyCollection<string>? AssetKeywords,
    IReadOnlyCollection<string>? ConceptKeywords,
    IReadOnlyCollection<string>? EventTypes,
    IReadOnlyCollection<string>? SourceNames,
    IReadOnlyCollection<string>? SourceTiers,
    IReadOnlyCollection<string>? ExcludeTerms,
    decimal? MinArticleSimilarity);

public sealed record ScanThesisRequest(
    int? LookbackHours,
    int? LookbackDays,
    int? BatchSize);

public sealed record UpdateThesisRequest(
    string? Name,
    string? ThesisText,
    string? Status,
    string? Summary,
    string? PositionIntent,
    string? PositionThesis);

public sealed record UpsertAssetRequest(
    string? Kind,
    string? Name,
    string? Symbol,
    IReadOnlyCollection<string>? Keywords);

public sealed record LinkAssetRequest(
    Guid AssetId,
    string? Role);

public sealed record UpsertRuleRequest(
    string? Name,
    bool? IsEnabled,
    IReadOnlyCollection<string>? AssetKeywords,
    IReadOnlyCollection<string>? ConceptKeywords,
    IReadOnlyCollection<string>? EventTypes,
    IReadOnlyCollection<string>? SourceNames,
    IReadOnlyCollection<string>? SourceTiers,
    IReadOnlyCollection<string>? ExcludeTerms,
    decimal? MinArticleSimilarity);

public sealed record AttachEvidenceRequest(
    string? Reason,
    string? Notes,
    string? Stance);

public sealed record ReviewEvidenceRequest(
    string? ReviewStatus,
    string? Stance,
    bool? IsPinned,
    string? ReviewerNote);

internal static class PlanAdequacy
{
    public static object? From(string? planJson)
    {
        if (string.IsNullOrWhiteSpace(planJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(planJson);
            var root = doc.RootElement;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? Int(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
            int? Count(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : null;

            var coverage = Str("coverage");
            var leaning = Str("leaning");
            var verdict = Str("verdict");
            var corpusContextSize = Int("corpusContextSize");
            var subTrackCount = Count("subTracks");
            var supportClusterCount = Count("strongestSupportClusterIds");
            var contradictClusterCount = Count("strongestContradictClusterIds");

            var dataThin = string.Equals(coverage, "thin", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(leaning, "insufficient", StringComparison.OrdinalIgnoreCase)
                           || (corpusContextSize.HasValue && corpusContextSize.Value < 25);

            return new
            {
                coverage,
                leaning,
                verdict,
                corpusContextSize,
                subTrackCount,
                supportClusterCount,
                contradictClusterCount,
                dataThin,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

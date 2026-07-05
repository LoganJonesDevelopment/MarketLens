using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class OllamaStanceClassifier(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    OllamaConcurrencyGate gate) : IStanceClassifier
{
    private readonly OllamaOptions _options = options.Value;
    public const string PromptVersion = "stance-v1";

    private const string SystemPrompt = """
        You judge whether a corroborating cluster of financial-news articles supports, contradicts, or is neutral toward a stated thesis.
        You will be given the thesis (name and statement) and the cluster (event type, summary, member articles).
        Return four fields:
          stance     : one of "supports", "contradicts", "neutral", "unknown"
                       supports   = the cluster's evidence strengthens the thesis as stated
                       contradicts= the cluster's evidence undermines the thesis as stated
                       neutral    = the cluster is on-topic but does not move the needle either way
                       unknown    = the cluster is off-topic or you cannot tell
          confidence : 0.0 to 1.0 — how confident you are in the stance call
                       low confidence (<0.4) signals you are guessing; reserve >0.8 for clear-cut calls
          rationale  : one or two sentences citing the specific facts from the articles that drive the stance
          relevance  : 0.0 to 1.0 — how on-topic the cluster is relative to the thesis as stated
                       low relevance (<0.3) means the cluster is essentially noise for this thesis
        Do not invent facts not present in the source articles. Do not summarize the thesis itself in the rationale.
        Be willing to say "neutral" or "unknown" when the cluster is genuinely ambiguous; over-confident misclassification is worse than admitting uncertainty.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            stance = new { type = "string", @enum = new[] { "supports", "contradicts", "neutral", "unknown" } },
            confidence = new { type = "number" },
            rationale = new { type = "string" },
            relevance = new { type = "number" },
        },
        required = new[] { "stance", "confidence", "rationale", "relevance" },
    };

    public async Task<StanceVerdict> ClassifyAsync(StanceContext context, CancellationToken cancellationToken = default)
    {
        var userContent = BuildUserPrompt(context);

        var request = new
        {
            model = _options.Model,
            stream = false,
            think = false,
            keep_alive = _options.KeepAlive,
            format = ResponseSchema,
            options = new { temperature = 0.1 },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userContent },
            },
        };

        using var lease = await gate.AcquireAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await httpClient.PostAsJsonAsync($"{_options.BaseUrl}/api/chat", request, cts.Token);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
        var content = payload.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama returned empty content for stance classification");

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var stance = NormalizeStance(root.TryGetProperty("stance", out var s) ? s.GetString() : null);
        var confidence = ReadDecimal(root, "confidence");
        var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? string.Empty : string.Empty;
        var relevance = ReadDecimal(root, "relevance");

        var combinedConfidence = Math.Clamp(confidence * relevance switch
        {
            >= 0.7m => 1m,
            >= 0.4m => 0.85m,
            >= 0.2m => 0.6m,
            _ => 0.3m,
        }, 0m, 1m);

        if (relevance < 0.2m)
            stance = "unknown";

        return new StanceVerdict(
            Stance: stance,
            Confidence: combinedConfidence,
            Rationale: rationale,
            ModelName: _options.Model,
            PromptVersion: PromptVersion);
    }

    private static string NormalizeStance(string? raw)
    {
        return (raw ?? string.Empty).ToLowerInvariant() switch
        {
            "supports" => "supports",
            "support" => "supports",
            "contradicts" => "contradicts",
            "contradict" => "contradicts",
            "neutral" => "neutral",
            _ => "unknown",
        };
    }

    private static string BuildUserPrompt(StanceContext context)
    {
        var lines = new List<string>
        {
            $"Thesis name: {context.ThesisName}",
            $"Thesis statement: {context.ThesisStatement}",
            "",
            $"Cluster event type: {context.EventType}",
            $"Cluster subject: {context.Symbol ?? "(none)"}",
            $"Cluster size: {context.MemberCount} corroborating sources, top tier: {context.DominantSourceTier}",
            $"Cluster summary: {context.Summary}",
            "",
            "Cluster member articles (highest tier first):",
        };
        foreach (var m in context.Members.OrderBy(TierRank).Take(8))
        {
            lines.Add($"- [{m.SourceTier}] {m.Publisher ?? m.Source} ({m.PublishedAt:yyyy-MM-dd}): {m.Headline}");
            if (!string.IsNullOrWhiteSpace(m.Summary))
                lines.Add($"  {m.Summary}");
        }
        return string.Join("\n", lines);
    }

    private static int TierRank(ClusterMember m) => m.SourceTier switch
    {
        "primary" => 0,
        "wire" => 1,
        "trade_press" => 2,
        "aggregator" => 3,
        "opinion" => 4,
        _ => 5,
    };

    private static decimal ReadDecimal(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var prop)) return 0m;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(prop.GetString(), out var v) => v,
            _ => 0m,
        };
    }
}

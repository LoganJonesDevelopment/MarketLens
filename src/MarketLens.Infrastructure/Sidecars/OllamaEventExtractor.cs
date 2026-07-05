using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:27b";
    public int TimeoutSeconds { get; set; } = 240;
    public string KeepAlive { get; set; } = "30m";
    public int MaxConcurrency { get; set; } = 1;
    public int MinIntervalSeconds { get; set; } = 10;
}

public class OllamaEventExtractor(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    OllamaConcurrencyGate gate) : IEventExtractor
{
    private readonly OllamaOptions _options = options.Value;
    public const string PromptVersion = "v3";

    private const string SystemPrompt = """
        You extract structured information from clusters of corroborating financial news articles.
        The cluster has been pre-classified into a known event type. Your job is to extract the event details.
        Return all five fields:
          summary    : one sentence stating what happened, who did it, and when
          sentiment  : market sentiment toward the affected entity, from -1.0 to 1.0
          slots      : an object with event-specific structured fields populated from the articles (parties, amounts, percentages, dates, counterparties, deal_size, eps, surprise, etc.)
          magnitude  : 0.0 to 1.0 indicating the size or scale of the event relative to typical events of this type
        Do not invent facts not present in the source articles. If the cluster contains conflicting facts, prefer the highest-tier source.
        Do not classify or re-categorize - the event type is already determined.
        For analyst_action, only extract a finding if the articles name a broker, analyst, rating action, or price-target action. Otherwise set summary to "No specific analyst action was reported." and magnitude to 0.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            summary = new { type = "string" },
            sentiment = new { type = "number" },
            slots = new { type = "object" },
            magnitude = new { type = "number" },
        },
        required = new[] { "summary", "sentiment", "slots", "magnitude" },
    };

    public async Task<ExtractedEvent> ExtractAsync(ClusterContext context, CancellationToken cancellationToken = default)
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
        {
            throw new InvalidOperationException("Ollama returned empty content");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var sentiment = ReadDecimal(root, "sentiment");
        var magnitude = ReadDecimal(root, "magnitude");
        var slotsJson = root.TryGetProperty("slots", out var sl) ? sl.GetRawText() : "{}";

        return new ExtractedEvent(
            Summary: summary,
            Sentiment: Math.Clamp(sentiment, -1m, 1m),
            SlotsJson: slotsJson,
            MagnitudeSignal: Math.Clamp(magnitude, 0m, 1m),
            ModelName: _options.Model,
            PromptVersion: PromptVersion);
    }

    private static string BuildUserPrompt(ClusterContext context)
    {
        var lines = new List<string>
        {
            $"Event type: {context.EventType}",
            $"Subject ticker: {context.Symbol ?? "(none)"}",
            $"Cluster size: {context.MemberCount} corroborating sources, top tier: {context.DominantSourceTier}",
            "",
            "Source articles (highest tier first):",
        };
        foreach (var m in context.Members.OrderBy(TierRank))
        {
            lines.Add($"- [{m.SourceTier}] {m.Publisher ?? m.Source} ({m.PublishedAt:yyyy-MM-dd HH:mm}Z): {m.Headline}");
            if (!string.IsNullOrWhiteSpace(m.Summary))
            {
                lines.Add($"  {m.Summary}");
            }
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

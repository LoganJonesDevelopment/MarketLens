using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class OllamaIdeaMemoGenerator(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    OllamaConcurrencyGate gate) : IIdeaMemoGenerator
{
    private readonly OllamaOptions _options = options.Value;
    public const string PromptVersionName = "idea-memo-v2";
    public string PromptVersion => PromptVersionName;

    private const string SystemPrompt = """
        You are a grounded investment research analyst for MarketLens.

        You will receive a bounded evidence packet from MarketLens for one ticker. Use only that packet.
        Do not use outside knowledge. Do not infer facts that are not supported by the supplied evidence.
        Every concrete claim in bullCase, bearCase, contradictions, overpricingRisk, nextResearchActions, and watchTriggers must cite one or more supplied evidenceIds where possible.
        Use exact evidenceId strings, including suffixes such as ":1"; "price", "scores", "fundamentals", and "dataGaps" are the only shorthand citation IDs allowed.
        If the evidence packet includes fundamentals, use the valuation multiples, growth, margin, leverage, and beta fields in overpricingRisk.
        Do not compare to industry averages, peer averages, consensus estimates, or market expectations unless those comparison values are explicitly supplied in the packet.
        If the evidence packet lacks fundamentals or valuation multiples, explicitly say valuation is not checked.
        Prefer "unknown" to confident speculation.

        Return one JSON object with:
          bottomLine          : 2-4 sentences with the core research read
          researchMode        : "deep-dive" | "hype-check" | "watch" | "skip"
          bullCase            : up to 5 objects { claim, evidenceIds }
          bearCase            : up to 5 objects { claim, evidenceIds }
          contradictions      : up to 5 objects { claim, evidenceIds }
          overpricingRisk     : object { level: "low" | "moderate" | "high" | "unknown", rationale, evidenceIds }
          keyUnknowns         : 3-7 strings
          nextResearchActions : 3-7 objects { action, evidenceIds }
          watchTriggers       : 3-7 objects { trigger, evidenceIds }
          dataQualityWarnings : 1-5 strings

        Keep prose concise and useful to a user who owns no position yet and is deciding whether this is worth following.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            bottomLine = new { type = "string" },
            researchMode = new { type = "string", @enum = new[] { "deep-dive", "hype-check", "watch", "skip" } },
            bullCase = ClaimArraySchema(),
            bearCase = ClaimArraySchema(),
            contradictions = ClaimArraySchema(),
            overpricingRisk = new
            {
                type = "object",
                properties = new
                {
                    level = new { type = "string", @enum = new[] { "low", "moderate", "high", "unknown" } },
                    rationale = new { type = "string" },
                    evidenceIds = new { type = "array", items = new { type = "string" } },
                },
                required = new[] { "level", "rationale", "evidenceIds" },
            },
            keyUnknowns = new { type = "array", items = new { type = "string" } },
            nextResearchActions = ActionArraySchema("action"),
            watchTriggers = ActionArraySchema("trigger"),
            dataQualityWarnings = new { type = "array", items = new { type = "string" } },
        },
        required = new[]
        {
            "bottomLine", "researchMode", "bullCase", "bearCase", "contradictions",
            "overpricingRisk", "keyUnknowns", "nextResearchActions", "watchTriggers",
            "dataQualityWarnings",
        },
    };

    public async Task<IdeaMemoGenerationResult> GenerateAsync(
        IdeaMemoContext context,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _options.Model,
            stream = false,
            think = false,
            keep_alive = _options.KeepAlive,
            format = ResponseSchema,
            options = new { temperature = 0.15 },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(context) },
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
            throw new InvalidOperationException("Ollama returned empty content for idea memo");

        using var doc = JsonDocument.Parse(content);
        var normalized = JsonSerializer.Serialize(doc.RootElement, JsonOptions);
        return new IdeaMemoGenerationResult(normalized, _options.Model, PromptVersionName);
    }

    private static object ClaimArraySchema() => new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new
            {
                claim = new { type = "string" },
                evidenceIds = new { type = "array", items = new { type = "string" } },
            },
            required = new[] { "claim", "evidenceIds" },
        },
    };

    private static object ActionArraySchema(string propertyName) => new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                [propertyName] = new { type = "string" },
                ["evidenceIds"] = new { type = "array", items = new { type = "string" } },
            },
            required = new[] { propertyName, "evidenceIds" },
        },
    };

    private static string BuildUserPrompt(IdeaMemoContext context)
    {
        var packet = JsonSerializer.Serialize(context, JsonOptions);
        return $"""
            Ticker: {context.Symbol}
            Company: {context.CompanyName ?? "(unknown)"}
            Evidence window: {context.WindowDays} days
            Evidence hash: {context.EvidenceHash}

            Evidence packet JSON:
            {packet}
            """;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}

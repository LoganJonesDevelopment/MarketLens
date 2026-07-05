using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class OllamaThesisPlanner(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    OllamaConcurrencyGate gate) : IThesisPlanner
{
    private readonly OllamaOptions _options = options.Value;
    public const string PromptVersion = "plan-v1";

    private const string SystemPrompt = """
        You evaluate whether an investment thesis is worth tracking, grounded in a curated corpus of recent financial news clusters.

        Read the thesis statement and the supplied corpus (numbered clusters the system has already ingested that are semantically close to the thesis). Produce both a viability read (verdict, leaning, coverage, citations) and a tracking plan that would be used if the user promotes this to a tracked thesis.

        Return one JSON object with these fields:
          verdict           : one paragraph (3-5 sentences), plain English, deciding whether this thesis is worth promoting to a tracked artifact. Lead with the bottom line ("worth tracking" / "thin signal — wait" / "consensus already with you, low edge"). Cite specific cluster numbers in brackets like [3] or [3,7] to ground claims in the supplied corpus
          leaning           : one of "supports" | "contradicts" | "mixed" | "insufficient" — what the corpus currently says about the thesis as stated. "insufficient" if the corpus is too thin to read
          coverage          : one of "thick" | "moderate" | "thin" — how much corpus signal exists for this thesis. Thick = many clusters genuinely on-topic. Moderate = partial coverage. Thin = the system has little related news ingested and the verdict is mostly extrapolation
          strongestSupports : list of up to 3 cluster numbers (1-indexed, matching the supplied corpus order) that most strongly support the thesis. Empty list if none. Numbers only — do NOT repeat headlines
          strongestContradicts : list of up to 3 cluster numbers that most strongly contradict. Empty list if none
          summary           : 1-2 sentence plain-English restatement of what the thesis claims
          trackedEntities   : named entities implicated by the thesis. For each: name (e.g. "Nvidia"), symbol (ticker if a public company, else null), rationale (one short sentence). Only include entities clearly implicated by the thesis or appearing in the corpus
          subTracks         : sub-questions the thesis decomposes into; each is a separately monitorable lever. For each: name (short label), question, expectedDirection (one of "confirms_if", "contradicts_if", "neutral_lever"), assetTerms, conceptTerms, eventTypes, excludeTerms
          confirmingSignals : 3-6 short bullet phrases — kinds of news that would strengthen the thesis
          refutingSignals   : 3-6 short bullet phrases — kinds of news that would weaken the thesis

        Controlled event-type values for eventTypes (use only these exact strings, omit if unsure):
          earnings, analyst_action, product_launch, material_agreement,
          acquisition_disposition, regulation_fd_disclosure, macro_release, officer_change,
          regulatory_action, litigation, material_impairment, delisting, restatement,
          vote_result, other_material_event

        Hard rules:
          - The verdict is the load-bearing piece. Be specific. "Worth tracking — corpus shows recent capex acceleration at MSFT and AMZN [2,5] but a counter-signal from analyst downgrades [11]; outcome over 6-12mo turns on whether AI revenue disclosures keep pace." If coverage is thin, say so explicitly.
          - Cluster citations [N] must reference numbers actually present in the supplied corpus (1..N where N is the corpus size). Do not invent numbers.
          - Sub-tracks should be orthogonal — don't repeat the same lever twice.
          - Tickers go in symbol fields, not name fields. Use "Nvidia" as name and "NVDA" as symbol.
          - Do not invent companies or tickers that are not in the corpus and not obviously implied by the thesis prose.
          - 3-6 sub-tracks is typical. Fewer is fine for narrow theses; do not pad.
          - Keep terms short and lowercase where natural. Tickers stay uppercase.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            verdict = new { type = "string" },
            leaning = new { type = "string", @enum = new[] { "supports", "contradicts", "mixed", "insufficient" } },
            coverage = new { type = "string", @enum = new[] { "thick", "moderate", "thin" } },
            strongestSupports = new { type = "array", items = new { type = "integer" } },
            strongestContradicts = new { type = "array", items = new { type = "integer" } },
            summary = new { type = "string" },
            trackedEntities = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        symbol = new { type = new[] { "string", "null" } },
                        rationale = new { type = "string" },
                    },
                    required = new[] { "name", "rationale" },
                },
            },
            subTracks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        question = new { type = "string" },
                        expectedDirection = new { type = "string", @enum = new[] { "confirms_if", "contradicts_if", "neutral_lever" } },
                        assetTerms = new { type = "array", items = new { type = "string" } },
                        conceptTerms = new { type = "array", items = new { type = "string" } },
                        eventTypes = new { type = "array", items = new { type = "string" } },
                        excludeTerms = new { type = "array", items = new { type = "string" } },
                    },
                    required = new[] { "name", "question", "expectedDirection", "assetTerms", "conceptTerms", "eventTypes", "excludeTerms" },
                },
            },
            confirmingSignals = new { type = "array", items = new { type = "string" } },
            refutingSignals = new { type = "array", items = new { type = "string" } },
        },
        required = new[] { "verdict", "leaning", "coverage", "summary", "trackedEntities", "subTracks", "confirmingSignals", "refutingSignals" },
    };

    public async Task<ThesisPlanResult> PlanAsync(ThesisPlanContext context, CancellationToken cancellationToken = default)
    {
        var userContent = BuildUserPrompt(context);

        var request = new
        {
            model = _options.Model,
            stream = false,
            think = false,
            keep_alive = _options.KeepAlive,
            format = ResponseSchema,
            options = new { temperature = 0.2 },
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
            throw new InvalidOperationException("Ollama returned empty content for thesis plan");

        var plan = ParsePlan(content, context);
        return new ThesisPlanResult(plan, _options.Model, PromptVersion);
    }

    private static ThesisPlan ParsePlan(string json, ThesisPlanContext context)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var verdict = ReadString(root, "verdict");
        var leaning = NormalizeLeaning(ReadString(root, "leaning"));
        var coverage = NormalizeCoverage(ReadString(root, "coverage"), context.Corpus.Count);
        var supportNumbers = ReadIntArray(root, "strongestSupports");
        var contradictNumbers = ReadIntArray(root, "strongestContradicts");
        var supportIds = ResolveClusterIds(supportNumbers, context.Corpus);
        var contradictIds = ResolveClusterIds(contradictNumbers, context.Corpus);

        var summary = ReadString(root, "summary");
        var entities = ReadArray(root, "trackedEntities", el => new TrackedEntity(
            Name: ReadString(el, "name"),
            Symbol: NormalizeSymbol(TryReadString(el, "symbol")),
            Rationale: ReadString(el, "rationale")));
        var subTracks = ReadArray(root, "subTracks", el => new ThesisSubTrack(
            Name: ReadString(el, "name"),
            Question: ReadString(el, "question"),
            ExpectedDirection: NormalizeDirection(ReadString(el, "expectedDirection")),
            AssetTerms: ReadStringArray(el, "assetTerms"),
            ConceptTerms: ReadStringArray(el, "conceptTerms"),
            EventTypes: ReadStringArray(el, "eventTypes"),
            ExcludeTerms: ReadStringArray(el, "excludeTerms")));
        var confirming = ReadStringArray(root, "confirmingSignals");
        var refuting = ReadStringArray(root, "refutingSignals");

        return new ThesisPlan(
            Summary: summary,
            TrackedEntities: entities,
            SubTracks: subTracks,
            ConfirmingSignals: confirming,
            RefutingSignals: refuting,
            CorpusContextSize: context.Corpus.Count,
            Verdict: verdict,
            Leaning: leaning,
            Coverage: coverage,
            StrongestSupportClusterIds: supportIds,
            StrongestContradictClusterIds: contradictIds);
    }

    private static IReadOnlyList<Guid> ResolveClusterIds(IReadOnlyList<int> numbers, IReadOnlyList<ThesisPlanDigestCluster> corpus)
    {
        var seen = new HashSet<Guid>();
        var result = new List<Guid>();
        foreach (var n in numbers)
        {
            if (n < 1 || n > corpus.Count) continue;
            var id = corpus[n - 1].ClusterId;
            if (seen.Add(id)) result.Add(id);
            if (result.Count == 3) break;
        }
        return result;
    }

    private static IReadOnlyList<int> ReadIntArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<int>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n)) list.Add(n);
            else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var n2)) list.Add(n2);
        }
        return list;
    }

    private static string NormalizeLeaning(string raw) => raw switch
    {
        "supports" or "contradicts" or "mixed" or "insufficient" => raw,
        _ => "insufficient",
    };

    private static string NormalizeCoverage(string raw, int corpusSize) => raw switch
    {
        "thick" or "moderate" or "thin" => raw,
        _ => corpusSize switch
        {
            >= 25 => "thick",
            >= 10 => "moderate",
            _ => "thin",
        },
    };

    private static string BuildUserPrompt(ThesisPlanContext context)
    {
        var lines = new List<string>
        {
            $"Thesis name: {context.ThesisName}",
            "Thesis statement:",
            context.ThesisStatement,
            "",
        };

        if (context.Corpus.Count == 0)
        {
            lines.Add("Corpus context: none — the system has no semantically related clusters yet. Decompose from prose alone and flag low-coverage sub-tracks.");
            return string.Join("\n", lines);
        }

        lines.Add($"Corpus context: {context.Corpus.Count} most-similar clusters from the system's recent ingestion (closest first):");
        var idx = 1;
        foreach (var c in context.Corpus)
        {
            var sim = c.Similarity.ToString("F3");
            var sym = string.IsNullOrWhiteSpace(c.Symbol) ? "—" : c.Symbol;
            var et = string.IsNullOrWhiteSpace(c.EventType) ? "(unclassified)" : c.EventType;
            var imp = c.Importance.HasValue ? $" imp {c.Importance.Value:F2}" : string.Empty;
            var sent = c.Sentiment.HasValue ? $" sent {c.Sentiment.Value:F2}" : string.Empty;
            lines.Add($"{idx}. [{sim}] [{c.SourceTier}] {sym} · {et}{imp}{sent} · {c.LastSeenAt:yyyy-MM-dd}");
            lines.Add($"   {c.Headline}");
            if (!string.IsNullOrWhiteSpace(c.Summary))
                lines.Add($"   {c.Summary}");
            idx++;
        }
        return string.Join("\n", lines);
    }

    private static string ReadString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string? TryReadString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
        }
        return list;
    }

    private static IReadOnlyList<T> ReadArray<T>(JsonElement el, string property, Func<JsonElement, T> map)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<T>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            list.Add(map(item));
        }
        return list;
    }

    private static string? NormalizeSymbol(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    private static string NormalizeDirection(string raw) => raw switch
    {
        "confirms_if" or "contradicts_if" or "neutral_lever" => raw,
        _ => "neutral_lever",
    };
}

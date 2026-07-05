using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class TriageOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5436";
    public decimal DefaultThreshold { get; set; } = 0.40m;
}

public class TriageClient(HttpClient httpClient, IOptions<TriageOptions> options) : ITriageClient
{
    private readonly TriageOptions _options = options.Value;

    public async Task<TriageResult> ClassifyAsync(string text, decimal threshold, CancellationToken cancellationToken = default)
    {
        var request = new { text, threshold };
        var response = await httpClient.PostAsJsonAsync($"{_options.BaseUrl}/classify", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        return ReadResult(payload);
    }

    public async Task<IReadOnlyList<TriageResult>> ClassifyBatchAsync(
        IReadOnlyList<string> texts,
        decimal threshold,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];

        var request = new { texts, threshold };
        var response = await httpClient.PostAsJsonAsync($"{_options.BaseUrl}/classify-batch", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        var results = new List<TriageResult>();
        foreach (var item in payload.EnumerateArray())
        {
            results.Add(ReadResult(item));
        }

        if (results.Count != texts.Count)
        {
            throw new InvalidOperationException($"Triage service returned {results.Count} results for {texts.Count} inputs");
        }

        return results;
    }

    private static TriageResult ReadResult(JsonElement payload)
    {
        var eventType = payload.GetProperty("event_type").ValueKind == JsonValueKind.String
            ? payload.GetProperty("event_type").GetString()
            : null;
        var confidence = payload.GetProperty("confidence").GetDecimal();

        var allScores = new Dictionary<string, decimal>();
        foreach (var prop in payload.GetProperty("all_scores").EnumerateObject())
        {
            allScores[prop.Name] = prop.Value.GetDecimal();
        }

        return new TriageResult(eventType, confidence, allScores);
    }
}

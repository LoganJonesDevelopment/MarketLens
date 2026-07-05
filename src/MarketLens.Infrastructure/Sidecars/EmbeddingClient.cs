using System.Net.Http.Json;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class EmbeddingOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5435";
    public string Model { get; set; } = "BAAI/bge-large-en-v1.5";
    public int Dimensions { get; set; } = 1024;
    public int MaxBatchSize { get; set; } = 32;
}

public class EmbeddingClient(HttpClient httpClient, IOptions<EmbeddingOptions> options) : IEmbeddingClient
{
    private readonly EmbeddingOptions _options = options.Value;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];

        var maxBatchSize = Math.Max(1, _options.MaxBatchSize);
        if (texts.Count > maxBatchSize)
        {
            var batches = new List<float[]>(texts.Count);
            for (var offset = 0; offset < texts.Count; offset += maxBatchSize)
            {
                var batch = texts.Skip(offset).Take(maxBatchSize).ToList();
                batches.AddRange(await EmbedBatchAsync(batch, cancellationToken));
            }
            return batches;
        }

        var request = new { model = _options.Model, input = texts };
        var response = await httpClient.PostAsJsonAsync($"{_options.BaseUrl}/v1/embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var data = payload.GetProperty("data")
            .EnumerateArray()
            .OrderBy(e => e.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0)
            .ToList();

        if (data.Count != texts.Count)
        {
            throw new InvalidOperationException($"Embedding service returned {data.Count} vectors for {texts.Count} inputs");
        }

        var results = new List<float[]>(data.Count);
        foreach (var item in data)
        {
            var arr = item.GetProperty("embedding");
            var result = new float[arr.GetArrayLength()];
            for (int i = 0; i < result.Length; i++) result[i] = arr[i].GetSingle();

            if (_options.Dimensions > 0 && result.Length != _options.Dimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding service returned {result.Length} dimensions; expected {_options.Dimensions}");
            }

            results.Add(result);
        }

        return results;
    }
}

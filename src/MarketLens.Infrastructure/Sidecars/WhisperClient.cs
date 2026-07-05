using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sidecars;

public class WhisperOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5437";
}

public record WhisperSegment(int Index, float Start, float End, string Text);

public record WhisperResult(string Language, float Duration, IReadOnlyList<WhisperSegment> Segments);

public class WhisperClient(HttpClient httpClient, IOptions<WhisperOptions> options)
{
    private readonly WhisperOptions _options = options.Value;

    public async Task<WhisperResult> TranscribeAsync(string audioUrl, string? language, CancellationToken cancellationToken = default)
    {
        var request = language is null
            ? new { audio_url = audioUrl, language = (string?)null }
            : new { audio_url = audioUrl, language = (string?)language };

        var response = await httpClient.PostAsJsonAsync($"{_options.BaseUrl}/transcribe", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        var lang = payload.GetProperty("language").GetString() ?? "unknown";
        var duration = payload.GetProperty("duration").GetSingle();

        var segments = new List<WhisperSegment>();
        foreach (var seg in payload.GetProperty("segments").EnumerateArray())
        {
            segments.Add(new WhisperSegment(
                Index: seg.GetProperty("index").GetInt32(),
                Start: seg.GetProperty("start").GetSingle(),
                End: seg.GetProperty("end").GetSingle(),
                Text: seg.GetProperty("text").GetString() ?? string.Empty
            ));
        }

        return new WhisperResult(lang, duration, segments);
    }
}

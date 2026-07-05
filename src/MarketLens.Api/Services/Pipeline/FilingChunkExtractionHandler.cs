using System.Text.Json;
using System.Text.RegularExpressions;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using SmartReader;
using Article = MarketLens.Core.Entities.Article;
using ArticleChunk = MarketLens.Core.Entities.ArticleChunk;

namespace MarketLens.Api.Services.Pipeline;

public sealed record FilingChunkExtractionItemResult(Guid ArticleId, bool Processed, int ChunksCreated);

public sealed class FilingChunkExtractionHandler(
    MarketLensDbContext db,
    IEmbeddingClient embedder,
    IConfiguration configuration,
    IOptions<FilingChunkExtractionOptions> options,
    ILogger<FilingChunkExtractionHandler> logger)
{
    private static readonly HashSet<string> ChunkableForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "10-K", "10-K/A", "10-Q", "10-Q/A", "S-1", "S-1/A", "DEF 14A", "PRE 14A",
        "8-K", "8-K/A",
    };

    private static readonly Regex Ex99FilenamePattern = new(
        @"ex.*?99",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecItemPattern = new(
        @"(?:^|\n)\s*(?:Item\s+(\d+[A-Z]?(?:\.\d+)?)[.\s\u2014\u2013-]+(.{5,120}))(?:\r?\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex HeadingPattern = new(
        @"(?:^|\n)([A-Z][A-Z\s,&]{8,80})(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly FilingChunkExtractionOptions _options = options.Value;

    public async Task<FilingChunkExtractionItemResult> ProcessAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        var article = await db.Articles
            .SingleOrDefaultAsync(a => a.Id == articleId, cancellationToken);

        if (article is null)
            return new FilingChunkExtractionItemResult(articleId, Processed: false, ChunksCreated: 0);

        var alreadyChunked = await db.ArticleChunks
            .AnyAsync(c => c.ArticleId == article.Id, cancellationToken);
        if (alreadyChunked)
            return new FilingChunkExtractionItemResult(article.Id, Processed: false, ChunksCreated: 0);

        var form = ExtractForm(article.RawPayload);
        if (!IsChunkableForm(form))
            return new FilingChunkExtractionItemResult(article.Id, Processed: false, ChunksCreated: 0);

        var chunkCount = await ChunkArticleAsync(article, form, cancellationToken);
        if (chunkCount > 0)
        {
            logger.LogInformation("FilingChunkExtractionService: {Form} {Symbol} -> {Count} chunks",
                form, article.Symbol, chunkCount);
        }

        return new FilingChunkExtractionItemResult(
            article.Id,
            Processed: true,
            ChunksCreated: chunkCount);
    }

    public static string ExtractForm(string rawPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            if (doc.RootElement.TryGetProperty("form", out var f))
                return f.GetString() ?? string.Empty;
        }
        catch { /* fall through */ }
        return string.Empty;
    }

    public static bool IsChunkableForm(string form) =>
        !string.IsNullOrWhiteSpace(form) && ChunkableForms.Contains(form);

    private async Task<int> ChunkArticleAsync(
        Article article,
        string form,
        CancellationToken cancellationToken)
    {
        var is8K = form.StartsWith("8-K", StringComparison.OrdinalIgnoreCase);

        var chunks = is8K
            ? await Chunk8KAsync(article, cancellationToken)
            : await ChunkPrimaryDocAsync(article, form, cancellationToken);

        if (chunks.Count == 0)
        {
            if (is8K)
            {
                db.ArticleChunks.Add(new ArticleChunk
                {
                    Id = Guid.NewGuid(),
                    ArticleId = article.Id,
                    ChunkIndex = 0,
                    Section = "no-press-release",
                    Text = "8-K filing: no press release exhibit (ex-99) found in this filing.",
                    Embedding = null,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return 0;
        }

        var texts = chunks.Select(c => c.Text).ToList();
        IReadOnlyList<float[]> embeddings;
        try
        {
            embeddings = await embedder.EmbedBatchAsync(texts, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FilingChunkExtractionService: embed batch failed for article {Id}, falling back to individual", article.Id);
            var fallback = new List<float[]>();
            foreach (var text in texts)
            {
                try { fallback.Add(await embedder.EmbedAsync(text, cancellationToken)); }
                catch { fallback.Add([]); }
            }
            embeddings = fallback;
        }

        var now = DateTime.UtcNow;
        for (var i = 0; i < chunks.Count; i++)
        {
            var (section, chunkText) = chunks[i];
            var embedding = i < embeddings.Count && embeddings[i].Length > 0
                ? new Vector(embeddings[i])
                : null;

            db.ArticleChunks.Add(new ArticleChunk
            {
                Id = Guid.NewGuid(),
                ArticleId = article.Id,
                ChunkIndex = i,
                Section = section,
                Text = chunkText,
                Embedding = embedding,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return chunks.Count;
    }

    private async Task<List<(string? Section, string Text)>> ChunkPrimaryDocAsync(
        Article article,
        string form,
        CancellationToken cancellationToken)
    {
        var html = await FetchHtmlAsync(article.Url!, cancellationToken);
        if (string.IsNullOrWhiteSpace(html)) return [];

        var text = await ExtractTextAsync(article.Url!, html);
        if (string.IsNullOrWhiteSpace(text) || text.Length < 200) return [];

        return SplitIntoChunks(text, form, _options.TokensPerChunk);
    }

    private async Task<List<(string? Section, string Text)>> Chunk8KAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        var indexUrl = BuildFilingIndexUrl(article);
        if (indexUrl is null) return [];

        var indexHtml = await FetchHtmlAsync(indexUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(indexHtml)) return [];

        var exhibitUrls = ExtractEx99Urls(indexHtml, indexUrl);
        if (exhibitUrls.Count == 0)
        {
            logger.LogDebug("FilingChunkExtractionService: no ex-99 exhibits found at {Url}", indexUrl);
            return [];
        }

        var allChunks = new List<(string? Section, string Text)>();
        foreach (var (exhibitUrl, label) in exhibitUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var html = await FetchHtmlAsync(exhibitUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var text = await ExtractTextAsync(exhibitUrl, html);
                if (string.IsNullOrWhiteSpace(text) || text.Length < 200) continue;

                var chunks = SplitIntoChunks(text, label, _options.TokensPerChunk, defaultSection: label);
                allChunks.AddRange(chunks);

                logger.LogDebug("FilingChunkExtractionService: {Label} at {Url} -> {Count} chunks", label, exhibitUrl, chunks.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FilingChunkExtractionService: exhibit fetch failed {Url}", exhibitUrl);
            }

            await Task.Delay(Random.Shared.Next(200, 300), cancellationToken);
        }

        return allChunks;
    }

    private static string? BuildFilingIndexUrl(Article article)
    {
        try
        {
            using var doc = JsonDocument.Parse(article.RawPayload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("cik", out var cikEl) || !root.TryGetProperty("accession", out var accEl))
                return null;

            var cik = cikEl.GetString();
            var accession = accEl.GetString();
            if (string.IsNullOrWhiteSpace(cik) || string.IsNullOrWhiteSpace(accession)) return null;

            var cikLong = long.Parse(cik.TrimStart('0'));
            var accNoDashes = accession.Replace("-", "");
            return $"https://www.sec.gov/Archives/edgar/data/{cikLong}/{accNoDashes}/";
        }
        catch
        {
            return null;
        }
    }

    private static List<(string Url, string Label)> ExtractEx99Urls(string indexHtml, string indexUrl)
    {
        var baseUri = new Uri(indexUrl);
        var results = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hrefPattern = new Regex(@"href=""(/Archives[^""]+)""", RegexOptions.IgnoreCase);
        foreach (Match match in hrefPattern.Matches(indexHtml))
        {
            var path = match.Groups[1].Value;
            var filename = path.Split('/').LastOrDefault() ?? string.Empty;

            if (!Ex99FilenamePattern.IsMatch(filename)) continue;

            var ext = Path.GetExtension(filename).ToLowerInvariant();
            if (ext is not (".htm" or ".html" or ".txt")) continue;

            var fullUrl = new Uri(baseUri, path).ToString();
            if (!seen.Add(fullUrl)) continue;

            var label = InferExhibitLabel(filename);
            results.Add((fullUrl, label));
        }

        return results;
    }

    private static string InferExhibitLabel(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
        if (name.Contains("992") || name.Contains("ex99-2") || name.Contains("ex99_2"))
            return "Exhibit 99.2";
        if (name.Contains("991") || name.Contains("ex99-1") || name.Contains("ex99_1") || name.Contains("ex99.1"))
            return "Press Release";
        return "Press Release";
    }

    private async Task<string?> FetchHtmlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme is not ("http" or "https")) return null;

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var userAgent = configuration["Edgar:UserAgent"]
                ?? throw new InvalidOperationException("Edgar:UserAgent is not configured; EDGAR requires a User-Agent with a real contact email");
            http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("FilingChunkExtractionService: HTTP {Status} for {Url}", (int)response.StatusCode, url);
                return null;
            }
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "FilingChunkExtractionService: fetch failed for {Url}", url);
            return null;
        }
    }

    private static async Task<string> ExtractTextAsync(string url, string html)
    {
        try
        {
            var reader = new Reader(url, html);
            var article = await reader.GetArticleAsync();
            if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
            {
                var cleaned = Regex.Replace(article.TextContent.Trim(), @"\s{3,}", "\n\n");
                return cleaned.Length > 120_000 ? cleaned[..120_000] : cleaned;
            }
        }
        catch { /* fall through to raw text extraction */ }

        var stripped = Regex.Replace(html, "<[^>]+>", " ");
        stripped = Regex.Replace(stripped, @"\s{3,}", "\n\n").Trim();
        return stripped.Length > 120_000 ? stripped[..120_000] : stripped;
    }

    private static List<(string? Section, string Text)> SplitIntoChunks(string text, string form, int tokensPerChunk, string? defaultSection = null)
    {
        var result = new List<(string? Section, string Text)>();

        var isAnnualOrQuarterly = form.StartsWith("10-K", StringComparison.OrdinalIgnoreCase)
            || form.StartsWith("10-Q", StringComparison.OrdinalIgnoreCase);

        if (isAnnualOrQuarterly)
        {
            var sections = SplitBySecItems(text);
            if (sections.Count > 1)
            {
                foreach (var (section, sectionText) in sections)
                    SplitByTokens(sectionText, section, tokensPerChunk, result);
                return result;
            }
        }

        var headings = SplitByHeadings(text);
        if (headings.Count > 1)
        {
            foreach (var (section, sectionText) in headings)
                SplitByTokens(sectionText, section ?? defaultSection, tokensPerChunk, result);
            return result;
        }

        SplitByTokens(text, defaultSection, tokensPerChunk, result);
        return result;
    }

    private static List<(string Section, string Text)> SplitBySecItems(string text)
    {
        var matches = SecItemPattern.Matches(text);
        if (matches.Count < 2) return [];

        var sections = new List<(string Section, string Text)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var label = $"Item {match.Groups[1].Value}";
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var chunk = text[start..end].Trim();
            if (chunk.Length > 80)
                sections.Add((label, chunk));
        }
        return sections;
    }

    private static List<(string? Section, string Text)> SplitByHeadings(string text)
    {
        var matches = HeadingPattern.Matches(text);
        if (matches.Count < 2) return [];

        var sections = new List<(string? Section, string Text)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var label = match.Groups[1].Value.Trim();
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var chunk = text[start..end].Trim();
            if (chunk.Length > 80)
                sections.Add((label, chunk));
        }
        return sections;
    }

    private static void SplitByTokens(
        string text,
        string? section,
        int tokensPerChunk,
        List<(string? Section, string Text)> result)
    {
        var charsPerChunk = (int)(tokensPerChunk / 0.75 * 4.5);
        var overlap = charsPerChunk / 5;

        if (text.Length <= charsPerChunk)
        {
            if (text.Length > 80)
                result.Add((section, text));
            return;
        }

        var paragraphs = text.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var current = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length + paragraph.Length + 2 > charsPerChunk && current.Length > 0)
            {
                var chunk = current.ToString().Trim();
                if (chunk.Length > 80)
                    result.Add((section, chunk));

                var tail = chunk.Length > overlap ? chunk[^overlap..] : chunk;
                current.Clear();
                current.Append(tail);
                current.Append("\n\n");
            }
            current.Append(paragraph);
            current.Append("\n\n");
        }

        var last = current.ToString().Trim();
        if (last.Length > 80)
            result.Add((section, last));
    }
}

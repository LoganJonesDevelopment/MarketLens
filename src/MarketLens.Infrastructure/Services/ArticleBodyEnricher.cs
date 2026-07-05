using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReader;

namespace MarketLens.Infrastructure.Services;

public class ArticleBodyCacheOptions
{
    public bool Enabled { get; set; } = true;
    public int SuccessTtlHours { get; set; } = 24 * 14;
    public int NegativeTtlMinutes { get; set; } = 12 * 60;
}

public class ArticleBodyEnricher(
    HttpClient httpClient,
    ILocalFetchCache cache,
    IOptions<ArticleBodyCacheOptions> cacheOptions,
    ILogger<ArticleBodyEnricher> logger)
{
    private readonly ArticleBodyCacheOptions _cacheOptions = cacheOptions.Value;

    public async Task<IngestedArticle> EnrichAsync(
        IngestedArticle article,
        CancellationToken cancellationToken = default)
    {
        if (!article.NeedsBodyFetch || !IsThinSummary(article.Summary) || string.IsNullOrWhiteSpace(article.Url))
            return article;

        var body = await TryFetchBodyAsync(article.Url, cancellationToken);
        if (article.BodyFetchDelayMs > 0)
            await Task.Delay(article.BodyFetchDelayMs, cancellationToken);

        return string.IsNullOrWhiteSpace(body)
            ? article
            : article with { Summary = body };
    }

    private static bool IsThinSummary(string? summary) =>
        string.IsNullOrWhiteSpace(summary) || summary.Trim().Length < 200;

    private async Task<string?> TryFetchBodyAsync(string url, CancellationToken cancellationToken)
    {
        var cacheKey = LocalFetchCachePolicy.BuildCacheKey("article_body", url);
        if (_cacheOptions.Enabled)
        {
            var cached = await cache.GetFreshAsync(cacheKey, cancellationToken: cancellationToken);
            if (cached is not null)
            {
                if (cached.Success)
                    return cached.ResponseText;

                logger.LogDebug("Body fetch skipped by negative cache for {Url}: {Error}", url, cached.ErrorText);
                return null;
            }
        }

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme is not ("http" or "https")) return null;

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Body fetch {StatusCode} for {Url}", (int)response.StatusCode, url);
                await StoreCacheAsync(
                    cacheKey,
                    url,
                    success: false,
                    statusCode: (int)response.StatusCode,
                    contentType: response.Content.Headers.ContentType?.MediaType,
                    responseText: null,
                    errorText: $"HTTP {(int)response.StatusCode}",
                    cancellationToken);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("html") && !contentType.Contains("text/plain"))
            {
                await StoreCacheAsync(
                    cacheKey,
                    url,
                    success: false,
                    statusCode: (int)response.StatusCode,
                    contentType: contentType,
                    responseText: null,
                    errorText: $"Unsupported content type {contentType}",
                    cancellationToken);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var reader = new Reader(url, html);
            var readableArticle = await reader.GetArticleAsync();

            if (!readableArticle.IsReadable || string.IsNullOrWhiteSpace(readableArticle.TextContent))
            {
                await StoreCacheAsync(
                    cacheKey,
                    url,
                    success: false,
                    statusCode: (int)response.StatusCode,
                    contentType: contentType,
                    responseText: null,
                    errorText: "Reader did not find article body.",
                    cancellationToken);
                return null;
            }

            var text = NormalizeWhitespace(readableArticle.TextContent);
            var body = text.Length > 8000 ? text[..8000] : text;
            await StoreCacheAsync(
                cacheKey,
                url,
                success: true,
                statusCode: (int)response.StatusCode,
                contentType: "text/plain",
                responseText: body,
                errorText: null,
                cancellationToken);
            return body;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Body fetch failed for {Url}", url);
            await StoreCacheAsync(
                cacheKey,
                url,
                success: false,
                statusCode: null,
                contentType: null,
                responseText: null,
                errorText: ex.Message,
                cancellationToken);
            return null;
        }
    }

    private async Task StoreCacheAsync(
        string cacheKey,
        string url,
        bool success,
        int? statusCode,
        string? contentType,
        string? responseText,
        string? errorText,
        CancellationToken cancellationToken)
    {
        if (!_cacheOptions.Enabled) return;

        try
        {
            await cache.StoreAsync(
                new StoreLocalFetchCacheRequest(
                    CacheKey: cacheKey,
                    Url: url,
                    Source: "article_body",
                    Success: success,
                    SuccessTtl: TimeSpan.FromHours(Math.Max(1, _cacheOptions.SuccessTtlHours)),
                    NegativeTtl: TimeSpan.FromMinutes(Math.Max(1, _cacheOptions.NegativeTtlMinutes)),
                    StatusCode: statusCode,
                    ContentType: contentType,
                    ResponseText: responseText,
                    ErrorText: errorText),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to store body fetch cache entry for {Url}", url);
        }
    }

    private static string NormalizeWhitespace(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s{3,}", "\n\n");
}

using System.Text.Json;
using System.Text.RegularExpressions;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public enum Form4ProcessingOutcome
{
    Ok,
    Skipped,
    NoXml,
    ParseFailed,
}

public sealed record Form4ProcessingItemResult(Guid ArticleId, Form4ProcessingOutcome Outcome)
{
    public bool Parsed => Outcome == Form4ProcessingOutcome.Ok;
}

public sealed class Form4ProcessingHandler(
    MarketLensDbContext db,
    IConfiguration configuration,
    ILogger<Form4ProcessingHandler> logger)
{
    private static readonly Regex Form4HeadlinePattern = new(
        @"\sForm 4:\s|\s4:\s|\s4/A:\s",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<Form4ProcessingItemResult> ProcessAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        var article = await db.Articles
            .SingleOrDefaultAsync(a => a.Id == articleId, cancellationToken);

        if (article is null)
            return new Form4ProcessingItemResult(articleId, Form4ProcessingOutcome.Skipped);

        var alreadyParsed = await db.InsiderTransactions
            .AnyAsync(t => t.ArticleId == article.Id, cancellationToken);
        if (alreadyParsed)
            return new Form4ProcessingItemResult(article.Id, Form4ProcessingOutcome.Skipped);

        if (!IsForm4HeadlineOrForm(article))
            return new Form4ProcessingItemResult(article.Id, Form4ProcessingOutcome.Skipped);

        using var http = BuildHttpClient();
        var outcome = await ProcessOneInternalAsync(http, article, cancellationToken);
        return new Form4ProcessingItemResult(article.Id, outcome);
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

    public static bool IsForm4(string form) =>
        form.Equals("4", StringComparison.OrdinalIgnoreCase) ||
        form.Equals("4/A", StringComparison.OrdinalIgnoreCase);

    public static bool IsForm4HeadlineOrForm(Article article)
        => IsForm4HeadlineOrForm(article.RawPayload, article.Headline);

    public static bool IsForm4HeadlineOrForm(string rawPayload, string headline)
    {
        var form = ExtractForm(rawPayload);
        if (IsForm4(form)) return true;
        return Form4HeadlinePattern.IsMatch(headline);
    }

    private async Task<Form4ProcessingOutcome> ProcessOneInternalAsync(
        HttpClient http,
        Article article,
        CancellationToken cancellationToken)
    {
        var xmlUrl = await ResolveForm4XmlUrlAsync(http, article, cancellationToken);
        if (xmlUrl is null) return Form4ProcessingOutcome.NoXml;

        string? xml;
        try
        {
            using var response = await http.GetAsync(xmlUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Form4ProcessingService: HTTP {Status} for {Url}", (int)response.StatusCode, xmlUrl);
                return Form4ProcessingOutcome.NoXml;
            }
            xml = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Form4ProcessingService: fetch failed for {Url}", xmlUrl);
            return Form4ProcessingOutcome.NoXml;
        }

        var parsed = Form4Parser.Parse(xml);
        if (parsed is null) return Form4ProcessingOutcome.ParseFailed;

        if (parsed.Transactions.Count == 0) return Form4ProcessingOutcome.Skipped;

        var symbol = !string.IsNullOrWhiteSpace(article.Symbol)
            ? article.Symbol!
            : (!string.IsNullOrWhiteSpace(parsed.IssuerSymbol) ? parsed.IssuerSymbol : parsed.IssuerName);

        var now = DateTime.UtcNow;
        foreach (var tx in parsed.Transactions)
        {
            db.InsiderTransactions.Add(new InsiderTransaction
            {
                ArticleId = article.Id,
                LineNumber = tx.LineNumber,
                IssuerCik = parsed.IssuerCik,
                IssuerName = parsed.IssuerName,
                IssuerSymbol = parsed.IssuerSymbol,
                OwnerCik = parsed.Owner.OwnerCik,
                OwnerName = parsed.Owner.OwnerName,
                IsDirector = parsed.Owner.IsDirector,
                IsOfficer = parsed.Owner.IsOfficer,
                IsTenPercentOwner = parsed.Owner.IsTenPercentOwner,
                IsOther = parsed.Owner.IsOther,
                OfficerTitle = parsed.Owner.OfficerTitle,
                SecurityTitle = tx.SecurityTitle,
                TransactionDate = tx.TransactionDate,
                TransactionCode = tx.TransactionCode,
                AcquiredDisposedCode = tx.AcquiredDisposedCode,
                Shares = tx.Shares,
                PricePerShare = tx.PricePerShare,
                SharesOwnedFollowing = tx.SharesOwnedFollowing,
                DirectOrIndirectOwnership = tx.DirectOrIndirectOwnership,
                IsOpenMarketTrade = tx.IsOpenMarketTrade,
                IsDerivative = tx.IsDerivative,
                ParsedAt = now,
            });
        }

        article.Headline = Form4HeadlineBuilder.Build(symbol, parsed);
        article.Summary = BuildSummary(parsed);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Form4ProcessingOutcome.Ok;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            logger.LogDebug("Form4ProcessingService: duplicate insider rows for article {Id}, skipping", article.Id);
            return Form4ProcessingOutcome.Skipped;
        }
    }

    private static string BuildSummary(Form4Document doc)
    {
        var parts = new List<string>();
        foreach (var transaction in doc.Transactions.Where(t => !t.IsDerivative))
        {
            var date = transaction.TransactionDate?.ToString("yyyy-MM-dd") ?? "?";
            var price = transaction.PricePerShare.HasValue ? $"${transaction.PricePerShare.Value:F2}" : "$0";
            var shares = transaction.Shares?.ToString("N0") ?? "?";
            parts.Add($"{date} code {transaction.TransactionCode} {transaction.AcquiredDisposedCode} {shares} sh @ {price}");
        }
        return string.Join("; ", parts);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<string?> ResolveForm4XmlUrlAsync(
        HttpClient http,
        Article article,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(article.Url) && article.Url.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return StripXslSegment(article.Url);

        var (cikLong, accNoDashes) = ExtractFilingIds(article);
        if (cikLong is null || accNoDashes is null) return null;

        var indexJsonUrl = $"https://www.sec.gov/Archives/edgar/data/{cikLong}/{accNoDashes}/index.json";
        try
        {
            using var resp = await http.GetAsync(indexJsonUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("directory", out var dir)) return null;
            if (!dir.TryGetProperty("item", out var items)) return null;

            string? candidate = null;
            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("FilingSummary.xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("form", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("ownership", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("wk-form4", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("primary_doc", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = name;
                    break;
                }
                candidate ??= name;
            }

            return candidate is null ? null : $"https://www.sec.gov/Archives/edgar/data/{cikLong}/{accNoDashes}/{candidate}";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Form4ProcessingService: index.json fetch failed for {Url}", indexJsonUrl);
            return null;
        }
    }

    private static string StripXslSegment(string url)
    {
        return Regex.Replace(url, @"/xslF345X0\d/", "/", RegexOptions.IgnoreCase);
    }

    private static (long? Cik, string? AccNoDashes) ExtractFilingIds(Article article)
    {
        try
        {
            using var doc = JsonDocument.Parse(article.RawPayload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("cik", out var cikEl) || !root.TryGetProperty("accession", out var accEl))
                return (null, null);
            var cik = cikEl.GetString();
            var accession = accEl.GetString();
            if (string.IsNullOrWhiteSpace(cik) || string.IsNullOrWhiteSpace(accession)) return (null, null);
            return (long.Parse(cik.TrimStart('0')), accession.Replace("-", ""));
        }
        catch
        {
            return (null, null);
        }
    }

    private HttpClient BuildHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var userAgent = configuration["Edgar:UserAgent"]
            ?? throw new InvalidOperationException("Edgar:UserAgent is not configured; EDGAR requires a User-Agent with a real contact email");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }
}

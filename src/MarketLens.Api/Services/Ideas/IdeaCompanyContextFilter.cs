using MarketLens.Core.Domain;

namespace MarketLens.Api.Services.Ideas;

internal static class IdeaCompanyContextFilter
{
    internal static List<IdeaEventRow> Filter(string symbol, List<IdeaEventRow> rows)
    {
        if (rows.Count == 0) return rows;

        var meta = TickerMetadata.Lookup(symbol);
        var contextual = rows
            .Where(e => HasCompanyContext(symbol, meta, e.Summary, e.TopHeadline, e.TopPublisher))
            .ToList();
        if (contextual.Count > 0) return contextual;

        return rows
            .Where(e => e.TopSource is SourceNames.Edgar or SourceNames.IrFeed or SourceNames.Transcript)
            .ToList();
    }

    private static bool HasCompanyContext(string symbol, TickerMetadataEntry? meta, params string?[] texts)
    {
        var text = string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (ContainsTickerToken(text, symbol)) return true;
        if (text.Contains($"${symbol}", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains($"({symbol})", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var needle in CompanyNeedles(symbol, meta))
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsTickerToken(string text, string symbol)
    {
        var index = text.IndexOf(symbol, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + symbol.Length;
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk) return true;
            index = text.IndexOf(symbol, index + symbol.Length, StringComparison.Ordinal);
        }

        return false;
    }

    private static IEnumerable<string> CompanyNeedles(string symbol, TickerMetadataEntry? meta)
    {
        if (meta is null) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in meta.Aliases.Concat([meta.CompanyName]))
        {
            var trimmed = alias.Trim();
            if (trimmed.Length >= 4 && !string.Equals(trimmed, symbol, StringComparison.OrdinalIgnoreCase) && seen.Add(trimmed))
                yield return trimmed;

            foreach (var token in CompanyTokens(trimmed))
            {
                if (!string.Equals(token, symbol, StringComparison.OrdinalIgnoreCase) && seen.Add(token))
                    yield return token;
            }
        }
    }

    private static IEnumerable<string> CompanyTokens(string name)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "inc", "corp", "corporation", "company", "co", "ltd", "limited", "plc",
            "holdings", "holding", "group", "class", "ordinary", "technologies", "technology",
        };

        foreach (var token in name.Split([' ', '.', ',', '-', '&', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 4 && !stop.Contains(token))
                yield return token;
        }
    }
}

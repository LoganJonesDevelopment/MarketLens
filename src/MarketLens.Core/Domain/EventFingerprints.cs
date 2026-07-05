namespace MarketLens.Core.Domain;

public static class EventFingerprints
{
    public static string? CoarseTopic(string? symbol, string headline, string? summary)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var text = $"{headline}\n{summary}";
        if (ContainsAny(text,
            "earnings", "results", "quarterly", "quarter", "revenue", "eps",
            "operating income", "net income", "fiscal q", "q1", "q2", "q3", "q4"))
        {
            return $"{symbol.ToUpperInvariant()}:earnings";
        }

        if (ContainsAny(text, "dividend", "buyback", "repurchase"))
        {
            return $"{symbol.ToUpperInvariant()}:capital_return";
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

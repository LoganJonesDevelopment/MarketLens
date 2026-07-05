using System.Text.RegularExpressions;
using MarketLens.Core.Interfaces;

namespace MarketLens.Core.Domain;

public static class WatchlistMatcher
{
    private static readonly HashSet<string> AmbiguousBareTickerDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "AN","ON","NOW","IT","AS","IS","OR","AT","BE","GO","NO","SO","TO","WE","DO","BY","ME","MY","UP","US","HE",
        "ALL","AND","ANY","ARE","FOR","GET","HAD","HAS","HER","HIM","HIS","HOW","ITS","NEW","ONE","OUR","OUT","PUT",
        "SAY","SHE","THE","TWO","WAS","WHO","WHY","YOU","TOO","WAY","DID","OFF","OWN","OLD","SET","TOP","USE","WHO","WAS",
        "AMP","UPS",
    };

    private static readonly Regex CorporateSuffix = new(
        @"(\s*,)?\s*(Inc\.?|Incorporated|Corp\.?|Corporation|Co\.?|Company|plc|p\.?l\.?c\.?|N\.?V\.?|S\.?A\.?|AG|Limited|Ltd\.?)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? MatchSymbol(IReadOnlyList<WatchedTicker> watched, string? headline, string? summary)
    {
        var text = ((headline ?? string.Empty) + " " + (summary ?? string.Empty)).Trim();
        if (text.Length == 0) return null;

        foreach (var entry in watched)
            if (Mentions(entry, text))
                return entry.Symbol;

        return null;
    }

    public static bool Mentions(WatchedTicker entry, string? headline, string? summary)
    {
        var text = ((headline ?? string.Empty) + " " + (summary ?? string.Empty)).Trim();
        return text.Length > 0 && Mentions(entry, text);
    }

    public static bool IsAmbiguousBareSymbol(string? symbol)
        => !string.IsNullOrWhiteSpace(symbol) && AmbiguousBareTickerDenylist.Contains(symbol.Trim());

    private const string TrailingGuard = @"(?!'[\p{Lu}])";

    private static bool Mentions(WatchedTicker entry, string text)
    {
        var sym = entry.Symbol;

        if (Regex.IsMatch(text, $@"\${Regex.Escape(sym)}\b{TrailingGuard}", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(text, $@"\({Regex.Escape(sym)}\)", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(text, $@"\b(NASDAQ|NYSE|NYSEARCA|AMEX|OTC)\s*:\s*{Regex.Escape(sym)}\b{TrailingGuard}", RegexOptions.IgnoreCase)) return true;

        var distinctive = StripCorporateSuffix(entry.Name);
        if (distinctive.Length >= 4 &&
            Regex.IsMatch(text, $@"\b{Regex.Escape(distinctive)}\b{TrailingGuard}", RegexOptions.IgnoreCase))
            return true;

        foreach (var alias in entry.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;
            if (alias.Length < 3) continue;
            if (AmbiguousBareTickerDenylist.Contains(alias)) continue;
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(alias)}\b{TrailingGuard}", RegexOptions.IgnoreCase))
                return true;
        }

        if (sym.Length >= 3 && !AmbiguousBareTickerDenylist.Contains(sym) &&
            Regex.IsMatch(text, $@"\b{Regex.Escape(sym)}\b{TrailingGuard}", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static string StripCorporateSuffix(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var stripped = CorporateSuffix.Replace(name, string.Empty);
        return stripped.TrimEnd(',', '.', ' ').Trim();
    }
}

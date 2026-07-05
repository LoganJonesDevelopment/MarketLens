using System.Text.RegularExpressions;

namespace MarketLens.Core.Domain;

public sealed record CommodityMetadataEntry(
    string Name,
    IReadOnlyList<string> Keywords);

public static class CommodityMetadata
{
    public static readonly IReadOnlyList<CommodityMetadataEntry> Known =
    [
        new("Copper", ["copper", "copper concentrate", "LME copper", "COMEX copper"]),
        new("Uranium", ["uranium", "U3O8", "yellowcake", "nuclear fuel"]),
        new("Lithium", ["lithium", "spodumene", "lithium carbonate", "lithium hydroxide", "LCE"]),
        new("Gold", ["gold", "gold bullion", "gold price", "gold miner", "gold miners"]),
        new("Silver", ["silver", "silver bullion", "silver price", "silver miner", "silver miners"]),
        new("Aluminum", ["aluminum", "aluminium", "alumina", "bauxite"]),
        new("Nickel", ["nickel", "nickel matte", "nickel sulfate", "nickel sulphate"]),
        new("Platinum", ["platinum", "PGM", "platinum group metals"]),
        new("Palladium", ["palladium", "PGM", "platinum group metals"]),
        new("Rare earths", ["rare earth", "rare earths", "REE", "neodymium", "praseodymium"]),
        new("Steel", ["steel", "steelmaking", "iron ore", "metallurgical coal"]),
        new("Grid infrastructure", ["grid infrastructure", "electrical infrastructure", "switchgear", "transformer", "power distribution"]),
    ];

    public static IReadOnlySet<string> DetectNames(string text)
    {
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return detected;

        foreach (var commodity in Known)
        {
            if (commodity.Keywords.Any(keyword => ContainsTerm(text, keyword)))
                detected.Add(commodity.Name);
        }

        return detected;
    }

    public static IReadOnlySet<string> DetectPrimaryNames(string name, string text)
    {
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var title = name ?? string.Empty;
        var body = text ?? string.Empty;

        foreach (var commodity in Known)
        {
            var inTitle = commodity.Keywords.Any(keyword => ContainsTerm(title, keyword));
            var bodyMentions = commodity.Keywords.Count(keyword => ContainsTerm(body, keyword));
            if (inTitle || bodyMentions >= 2)
                detected.Add(commodity.Name);
        }

        return detected;
    }

    private static bool ContainsTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(term)}(?![A-Za-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}

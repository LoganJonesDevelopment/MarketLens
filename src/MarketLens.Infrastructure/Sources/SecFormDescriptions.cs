namespace MarketLens.Infrastructure.Sources;

public static class SecFormDescriptions
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8-K"]      = "current report",
        ["8-K/A"]    = "current report (amended)",
        ["10-K"]     = "annual report",
        ["10-K/A"]   = "annual report (amended)",
        ["10-Q"]     = "quarterly report",
        ["10-Q/A"]   = "quarterly report (amended)",
        ["S-1"]      = "registration statement",
        ["S-1/A"]    = "registration statement (amended)",
        ["S-3"]      = "shelf registration",
        ["S-3/A"]    = "shelf registration (amended)",
        ["S-4"]      = "merger/acquisition registration",
        ["424B1"]    = "prospectus",
        ["424B2"]    = "prospectus",
        ["424B3"]    = "prospectus",
        ["424B4"]    = "prospectus",
        ["424B5"]    = "prospectus",
        ["DEF 14A"]  = "proxy statement",
        ["DEFA14A"]  = "additional proxy material",
        ["PRE 14A"]  = "preliminary proxy statement",
        ["4"]        = "insider transaction",
        ["4/A"]      = "insider transaction (amended)",
        ["3"]        = "initial insider ownership",
        ["5"]        = "annual insider statement",
        ["SC 13D"]   = "5%+ active stake",
        ["SC 13D/A"] = "5%+ active stake (amended)",
        ["SC 13G"]   = "5%+ passive stake",
        ["SC 13G/A"] = "5%+ passive stake (amended)",
    };

    public static bool IsTracked(string? form) =>
        !string.IsNullOrWhiteSpace(form) && Map.ContainsKey(form);

    public static string Describe(string form) =>
        Map.TryGetValue(form, out var d) ? d : form;
}

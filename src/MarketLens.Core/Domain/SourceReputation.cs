namespace MarketLens.Core.Domain;

public static class SourceReputation
{
    private static readonly Dictionary<string, (string Tier, decimal Weight)> Map = new()
    {
        [SourceNames.Edgar]         = (SourceTiers.Primary,    1.00m),
        [SourceNames.Fred]          = (SourceTiers.Primary,    1.00m),
        [SourceNames.Census]        = (SourceTiers.Primary,    1.00m),
        [SourceNames.IrFeed]        = (SourceTiers.Primary,    0.72m),
        [SourceNames.BusinessWire]  = (SourceTiers.Wire,       0.90m),
        [SourceNames.GlobeNewswire] = (SourceTiers.Wire,       0.90m),
        [SourceNames.PrNewswire]    = (SourceTiers.Wire,       0.90m),
        [SourceNames.Finnhub]       = (SourceTiers.Aggregator, 0.40m),
        [SourceNames.MiningCom]     = (SourceTiers.TradePress, 0.65m),
        [SourceNames.FedSpeeches]   = (SourceTiers.Primary,    0.95m),
        [SourceNames.FedPress]      = (SourceTiers.Primary,    0.95m),
        [SourceNames.Bls]           = (SourceTiers.Primary,    1.00m),
        [SourceNames.Bea]           = (SourceTiers.Primary,    1.00m),
        [SourceNames.CourtListener] = (SourceTiers.Primary,    0.85m),
        [SourceNames.SecEnforcement]= (SourceTiers.Primary,    0.95m),
        [SourceNames.Ftc]           = (SourceTiers.Primary,    0.90m),
        [SourceNames.DojAntitrust]  = (SourceTiers.Primary,    0.90m),
        [SourceNames.Transcript]     = (SourceTiers.Primary,    1.00m),
        [SourceNames.EarningsCall]   = (SourceTiers.Primary,    0.95m),
        [SourceNames.Bis]            = (SourceTiers.Primary,    1.00m),
        [SourceNames.IndustryAnalyst]= (SourceTiers.TradePress, 0.85m),
        [SourceNames.Reddit]         = (SourceTiers.Aggregator, 0.30m),
        [SourceNames.TechPress]      = (SourceTiers.TradePress, 0.55m),
        [SourceNames.Cnbc]           = (SourceTiers.TradePress, 0.55m),
        [SourceNames.NbcNews]        = (SourceTiers.TradePress, 0.50m),
        [SourceNames.Cnn]            = (SourceTiers.TradePress, 0.50m),
        [SourceNames.CbsNews]        = (SourceTiers.TradePress, 0.50m),
        [SourceNames.FoxBusiness]    = (SourceTiers.TradePress, 0.50m),
        [SourceNames.SeekingAlpha]   = (SourceTiers.Opinion,    0.45m),
        [SourceNames.Npr]            = (SourceTiers.Primary,    0.85m),
        [SourceNames.PewResearch]    = (SourceTiers.Primary,    0.95m),
        [SourceNames.WhiteHouse]     = (SourceTiers.Primary,    0.95m),
        [SourceNames.CryptoPress]    = (SourceTiers.TradePress, 0.45m),
        [SourceNames.AiAnalyst]      = (SourceTiers.TradePress, 0.55m),
        [SourceNames.Bbc]            = (SourceTiers.Primary,    0.90m),
        [SourceNames.Upi]            = (SourceTiers.Wire,       0.90m),
        [SourceNames.Eia]            = (SourceTiers.Primary,    1.00m),
        [SourceNames.Usgs]           = (SourceTiers.Primary,    1.00m),
        [SourceNames.DoeNuclear]     = (SourceTiers.Primary,    0.95m),
        [SourceNames.NuclearPress]   = (SourceTiers.TradePress, 0.80m),
        [SourceNames.EvPress]        = (SourceTiers.TradePress, 0.55m),
    };

    public static (string Tier, decimal Weight) For(string source) =>
        Map.TryGetValue(source, out var v) ? v : (SourceTiers.Aggregator, 0.30m);
}

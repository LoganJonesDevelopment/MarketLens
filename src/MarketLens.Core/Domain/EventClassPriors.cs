namespace MarketLens.Core.Domain;

public static class EventClassPriors
{
    private static readonly Dictionary<string, decimal> Priors = new()
    {
        [EventTypes.AcquisitionDisposition]  = 0.95m,
        [EventTypes.Earnings]                = 0.85m,
        [EventTypes.Restatement]             = 0.90m,
        [EventTypes.MaterialImpairment]      = 0.80m,
        [EventTypes.Delisting]               = 0.85m,
        [EventTypes.MaterialAgreement]       = 0.78m,
        [EventTypes.OfficerChange]           = 0.65m,
        [EventTypes.RegulatoryAction]        = 0.70m,
        [EventTypes.Litigation]              = 0.55m,
        [EventTypes.AnalystAction]           = 0.38m,
        [EventTypes.ProductLaunch]           = 0.30m,
        [EventTypes.MacroRelease]            = 0.75m,
        [EventTypes.RegulationFdDisclosure]  = 0.35m,
        [EventTypes.VoteResult]              = 0.40m,
        [EventTypes.OtherMaterialEvent]      = 0.40m,
        [EventTypes.ProductionGuidance]      = 0.80m,
        [EventTypes.SupplyDisruption]        = 0.90m,
        [EventTypes.TradeRestriction]        = 0.85m,
    };

    public static decimal For(string eventType) =>
        Priors.TryGetValue(eventType, out var v) ? v : 0.35m;
}

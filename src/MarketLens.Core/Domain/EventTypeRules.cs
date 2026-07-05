namespace MarketLens.Core.Domain;

public static class EventTypeRules
{
    public static (string EventType, decimal Confidence)? ClassifyDeterministically(
        string source,
        string headline,
        string? summary)
    {
        var text = $"{headline}\n{summary}".ToUpperInvariant();

        if (source == SourceNames.Edgar)
        {
            if (ContainsAny(text, "ITEM 2.02")) return (EventTypes.Earnings, 0.99m);
            if (ContainsAny(text, "ITEM 2.01")) return (EventTypes.AcquisitionDisposition, 0.99m);
            if (ContainsAny(text, "ITEM 2.06")) return (EventTypes.MaterialImpairment, 0.99m);
            if (ContainsAny(text, "ITEM 3.01")) return (EventTypes.Delisting, 0.99m);
            if (ContainsAny(text, "ITEM 4.02")) return (EventTypes.Restatement, 0.99m);
            if (ContainsAny(text, "ITEM 5.02")) return (EventTypes.OfficerChange, 0.99m);
            if (ContainsAny(text, "ITEM 5.07")) return (EventTypes.VoteResult, 0.99m);
            if (ContainsAny(text, "ITEM 1.01", "ITEM 1.02")) return (EventTypes.MaterialAgreement, 0.99m);
            if (ContainsAny(text, "ITEM 7.01")) return (EventTypes.RegulationFdDisclosure, 0.95m);
            if (ContainsAny(text, "ITEM 8.01")) return (EventTypes.OtherMaterialEvent, 0.90m);
        }

        if (source == SourceNames.IrFeed)
        {
            if (ContainsAny(text, "DIVIDEND", "MINI-TENDER", "SHARE REPURCHASE", "BUYBACK"))
            {
                return (EventTypes.OtherMaterialEvent, 0.90m);
            }
        }

        // Wire feeds announcing dividends are shareholder events regardless of event type from classifier.
        if (source is SourceNames.BusinessWire or SourceNames.GlobeNewswire or SourceNames.PrNewswire)
        {
            if (ContainsAny(text, "DIVIDEND", "DECLARES DIVIDEND", "ANNOUNCES DIVIDEND"))
                return (EventTypes.OtherMaterialEvent, 0.90m);
        }

        // Official statistical and Fed release sources publish macro data releases.
        if (source == SourceNames.Bls)
            return (EventTypes.MacroRelease, 0.95m);

        if (source == SourceNames.Bea)
            return (EventTypes.MacroRelease, 0.95m);

        if (source == SourceNames.Census)
            return (EventTypes.MacroRelease, 0.95m);

        if (source == SourceNames.FedPress)
        {
            if (ContainsAny(text, "MINUTES OF THE FEDERAL OPEN MARKET COMMITTEE", "MINUTES OF THE BOARD",
                "FOMC", "FEDERAL OPEN MARKET COMMITTEE", "DISCOUNT RATE"))
                return (EventTypes.MacroRelease, 0.95m);
        }

        if (source == SourceNames.FedSpeeches)
        {
            // Fed speeches are macro events — any speech from a Fed governor is policy-relevant.
            return (EventTypes.MacroRelease, 0.85m);
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(text.Contains);
}

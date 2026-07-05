namespace MarketLens.Core.Domain;

public static class SuppressionStages
{
    public const string Triage = "triage";
    public const string Extraction = "extraction";
}

public static class SuppressionReasons
{
    public const string ClassifierRejected = "classifier_rejected";
    public const string NonFindingExtraction = "non_finding_extraction";
    public const string ImmaterialExtraction = "immaterial_extraction";
    public const string MacroDataBypass = "macro_data_bypass";
}

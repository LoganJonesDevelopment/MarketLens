namespace MarketLens.Core.Domain;

public static class EventTypes
{
    public const string Earnings = "earnings";
    public const string AcquisitionDisposition = "acquisition_disposition";
    public const string MaterialAgreement = "material_agreement";
    public const string MaterialImpairment = "material_impairment";
    public const string Delisting = "delisting";
    public const string Restatement = "restatement";
    public const string OfficerChange = "officer_change";
    public const string VoteResult = "vote_result";
    public const string RegulationFdDisclosure = "regulation_fd_disclosure";
    public const string AnalystAction = "analyst_action";
    public const string ProductLaunch = "product_launch";
    public const string Litigation = "litigation";
    public const string RegulatoryAction = "regulatory_action";
    public const string MacroRelease = "macro_release";
    public const string OtherMaterialEvent = "other_material_event";
    public const string ProductionGuidance = "production_guidance";
    public const string SupplyDisruption = "supply_disruption";
    public const string TradeRestriction = "trade_restriction";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Earnings, AcquisitionDisposition, MaterialAgreement, MaterialImpairment,
        Delisting, Restatement, OfficerChange, VoteResult, RegulationFdDisclosure,
        AnalystAction, ProductLaunch, Litigation, RegulatoryAction, MacroRelease,
        OtherMaterialEvent, ProductionGuidance, SupplyDisruption, TradeRestriction,
    };
}

public static class SourceTiers
{
    public const string Primary = "primary";
    public const string Wire = "wire";
    public const string TradePress = "trade_press";
    public const string Aggregator = "aggregator";
    public const string Opinion = "opinion";
}

public static class SourceNames
{
    public const string Edgar = "edgar";
    public const string BusinessWire = "business_wire";
    public const string GlobeNewswire = "globe_newswire";
    public const string PrNewswire = "pr_newswire";
    public const string IrFeed = "ir_feed";
    public const string Fred = "fred";
    public const string Census = "census";
    public const string Finnhub = "finnhub";
    public const string MiningCom = "mining_com";
    public const string FedSpeeches = "fed_speeches";
    public const string FedPress = "fed_press";
    public const string Bls = "bls";
    public const string Bea = "bea";
    public const string CourtListener = "courtlistener";
    public const string SecEnforcement = "sec_enforcement";
    public const string Ftc = "ftc";
    public const string DojAntitrust = "doj_antitrust";
    public const string Transcript = "transcript";
    public const string EarningsCall = "earnings_call";
    public const string Bis = "bis";
    public const string IndustryAnalyst = "industry_analyst";
    public const string Reddit = "reddit";
    public const string TechPress = "tech_press";
    public const string Cnbc = "cnbc";
    public const string NbcNews = "nbc_news";
    public const string Cnn = "cnn";
    public const string CbsNews = "cbs_news";
    public const string FoxBusiness = "fox_business";
    public const string SeekingAlpha = "seeking_alpha";
    public const string Npr = "npr";
    public const string PewResearch = "pew_research";
    public const string WhiteHouse = "white_house";
    public const string CryptoPress = "crypto_press";
    public const string AiAnalyst = "ai_analyst";
    public const string Bbc = "bbc";
    public const string Upi = "upi";
    public const string Eia = "eia";
    public const string Usgs = "usgs";
    public const string DoeNuclear = "doe_nuclear";
    public const string NuclearPress = "nuclear_press";
    public const string EvPress = "ev_press";
}

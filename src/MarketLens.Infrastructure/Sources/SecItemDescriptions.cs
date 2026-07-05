namespace MarketLens.Infrastructure.Sources;

public static class SecItemDescriptions
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["1.01"] = "Entry into a Material Definitive Agreement",
        ["1.02"] = "Termination of a Material Definitive Agreement",
        ["1.03"] = "Bankruptcy or Receivership",
        ["2.01"] = "Completion of Acquisition or Disposition of Assets",
        ["2.02"] = "Results of Operations and Financial Condition (earnings)",
        ["2.03"] = "Creation of a Material Direct Financial Obligation",
        ["2.04"] = "Triggering Events that Accelerate or Increase a Direct Financial Obligation",
        ["2.05"] = "Costs Associated with Exit or Disposal Activities",
        ["2.06"] = "Material Impairments",
        ["3.01"] = "Notice of Delisting or Failure to Satisfy a Listing Rule",
        ["3.02"] = "Unregistered Sales of Equity Securities",
        ["3.03"] = "Material Modification to Rights of Security Holders",
        ["4.01"] = "Changes in Registrant's Certifying Accountant",
        ["4.02"] = "Non-Reliance on Previously Issued Financial Statements (restatement)",
        ["5.01"] = "Changes in Control of Registrant",
        ["5.02"] = "Departure or Election of Directors or Principal Officers",
        ["5.03"] = "Amendments to Articles of Incorporation or Bylaws",
        ["5.07"] = "Submission of Matters to a Vote of Security Holders",
        ["5.08"] = "Shareholder Director Nominations",
        ["7.01"] = "Regulation FD Disclosure",
        ["8.01"] = "Other Material Events",
        ["9.01"] = "Financial Statements and Exhibits",
    };

    public static string Describe(string item) =>
        Map.TryGetValue(item, out var d) ? d : $"Item {item}";
}

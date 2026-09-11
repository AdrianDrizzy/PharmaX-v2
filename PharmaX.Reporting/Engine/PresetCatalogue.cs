using PharmaX.Application.Reporting;
using PharmaX.Domain.Security;

namespace PharmaX.Reporting.Engine;

/// <summary>
/// Chapter 14 of the specification requires forty-eight incumbent report
/// names to be reachable while only a handful of report classes exist.
/// This class is the mechanism: every name below is a name the staff
/// already use, taken from the incumbent dropdown (specification
/// Appendix B), bound to a definition and a fixed set of slicer values.
/// </summary>
/// <remarks>
/// Adding a report the client asks for after go-live is, in most cases,
/// one entry in this array.
/// </remarks>
public sealed class PresetCatalogue : IPresetCatalogue
{
    private static readonly ReportPreset[] Presets =
    {
        new("pos.transactions.all", "All Transactions",
            "Transactions",
            new Dictionary<string, string?>(),
            new[] { "tender", "operator", "taxCode", "mode" },
            PermissionCode.ReportOperational),

        new("pos.transactions.all.rated", "Rated - All Transactions",
            "Transactions",
            new Dictionary<string, string?> { ["ratedOnly"] = "true" },
            new[] { "tender", "operator", "mode" },
            PermissionCode.ReportOperational),

        new("pos.transactions.by-tender-by-operator",
            "Transactions (By Tender By Operator)",
            "Transactions",
            new Dictionary<string, string?>
                { ["groupBy"] = "tender,operator" },
            new[] { "mode" },
            PermissionCode.ReportOperational),

        new("pos.sales.by-till-operator", "Sales (By Till Operator)",
            "SalesAnalysis",
            new Dictionary<string, string?> { ["groupBy"] = "operator" },
            new[] { "department", "category", "mode" },
            PermissionCode.ReportOperational),

        new("pos.sales.summary.by-markup", "Sales Summary (By Markup %)",
            "SalesAnalysis",
            new Dictionary<string, string?>
                { ["groupBy"] = "markupBand", ["mode"] = "summary" },
            new[] { "department" },
            PermissionCode.ReportManagement),

        new("pos.vat.detailed.above-zero", "V.A.T Detailed (Above Zero)",
            "Vat",
            new Dictionary<string, string?>
                { ["mode"] = "detail", ["aboveZeroOnly"] = "true" },
            new[] { "tender", "transactionType" },
            PermissionCode.ReportFinancial),

        new("pos.vat.summary", "V.A.T Summary",
            "Vat",
            new Dictionary<string, string?> { ["mode"] = "summary" },
            new[] { "transactionType" },
            PermissionCode.ReportFinancial),

        new("pos.cash.declaration", "Cash Declaration Form",
            "CashUp",
            new Dictionary<string, string?>(),
            new[] { "session" },
            PermissionCode.ReportOperational),

        new("inv.stock.on-hand", "Stock On Hand",
            "StockOnHand",
            new Dictionary<string, string?>(),
            new[] { "department", "belowReorderOnly" },
            PermissionCode.ReportOperational),

        new("inv.stock.expiring", "Stock Expiring",
            "StockOnHand",
            new Dictionary<string, string?> { ["expiringOnly"] = "true" },
            new[] { "withinDays" },
            PermissionCode.ReportOperational),

        new("dsp.scripts.reversed", "Reversed Scripts",
            "ReversedScripts",
            new Dictionary<string, string?>(),
            new[] { "pharmacist", "reason" },
            PermissionCode.ReportClinical)

        // ... one entry per name in specification Appendix B.
    };

    /// <summary>
    /// The slicers a preset may expose, and where the value list comes
    /// from. An empty option list means the values are loaded at run time.
    /// </summary>
    private static readonly Dictionary<string, SlicerDto> Slicers =
        new(StringComparer.Ordinal)
        {
            ["tender"] = new("tender", "Tender", "lookup", new[]
            {
                new SlicerOptionDto("Cash", "Cash"),
                new SlicerOptionDto("Card", "Card"),
                new SlicerOptionDto("Account", "Account"),
                new SlicerOptionDto("MedicalAid", "Medical aid"),
                new SlicerOptionDto("Voucher", "Voucher"),
                new SlicerOptionDto("Eft", "EFT")
            }),

            ["operator"] = new("operator", "Operator", "user",
                Array.Empty<SlicerOptionDto>()),

            ["taxCode"] = new("taxCode", "Tax code", "lookup", new[]
            {
                new SlicerOptionDto("A", "A - standard rated"),
                new SlicerOptionDto("B", "B - zero rated"),
                new SlicerOptionDto("C", "C - exempt")
            }),

            ["mode"] = new("mode", "Detail", "lookup", new[]
            {
                new SlicerOptionDto("detail", "Detail"),
                new SlicerOptionDto("summary", "Summary")
            }),

            ["department"] = new("department", "Department", "lookup",
                Array.Empty<SlicerOptionDto>()),

            ["category"] = new("category", "Category", "lookup",
                Array.Empty<SlicerOptionDto>()),

            ["session"] = new("session", "Till session", "lookup",
                Array.Empty<SlicerOptionDto>()),

            ["pharmacist"] = new("pharmacist", "Pharmacist", "user",
                Array.Empty<SlicerOptionDto>()),

            ["reason"] = new("reason", "Reason", "text",
                Array.Empty<SlicerOptionDto>()),

            ["transactionType"] = new("transactionType", "Transaction type",
                "lookup", Array.Empty<SlicerOptionDto>()),

            ["belowReorderOnly"] = new("belowReorderOnly",
                "Below reorder level only", "flag",
                Array.Empty<SlicerOptionDto>()),

            ["withinDays"] = new("withinDays", "Expiring within (days)",
                "number", Array.Empty<SlicerOptionDto>())
        };

    public IReadOnlyList<ReportPresetDto> All() =>
        Presets
            .Select(p => new ReportPresetDto(p.Key, p.DisplayName,
                GroupOf(p.Key), p.PermissionCode))
            .ToList();

    public IReadOnlyList<SlicerDto> SlicersFor(string presetKey) =>
        Resolve(presetKey).UserSlicers
            .Where(Slicers.ContainsKey)
            .Select(k => Slicers[k])
            .ToList();

    public ReportPreset Resolve(string presetKey) =>
        Array.Find(Presets, p =>
            string.Equals(p.Key, presetKey, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException(
            $"No report preset '{presetKey}'. The preset catalogue and the "
            + "report list on the screen have drifted apart.");

    /// The tree heading the preset appears under, taken from its key.
    private static string GroupOf(string key) => key.Split('.')[0] switch
    {
        "pos" => "Point of sale",
        "inv" => "Inventory",
        "dsp" => "Dispensing",
        "fin" => "Finance",
        _ => "Other"
    };
}

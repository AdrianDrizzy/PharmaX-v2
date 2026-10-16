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

        // Turnover by day and by month: management figures, and so held
        // to the management permission - a cashier must not see the
        // branch's takings (the role matrix, section 4).
        new("pos.sales.daily", "Daily Sales",
            "SalesAnalysis",
            new Dictionary<string, string?> { ["groupBy"] = "day" },
            new[] { "department", "category" },
            PermissionCode.ReportManagement),

        new("pos.sales.monthly", "Monthly Sales",
            "SalesAnalysis",
            new Dictionary<string, string?> { ["groupBy"] = "month" },
            new[] { "department", "category" },
            PermissionCode.ReportManagement),

        // The same days and months, transaction by transaction and line
        // by line, with a total for each day and month. Held to the same
        // permission as the totals they add up to.
        new("pos.sales.daily.lines", "Daily Sales (Line Items)",
            "SalesLines",
            new Dictionary<string, string?> { ["groupBy"] = "day" },
            new[] { "operator", "tender", "department", "category" },
            PermissionCode.ReportManagement),

        new("pos.sales.monthly.lines", "Monthly Sales (Line Items)",
            "SalesLines",
            new Dictionary<string, string?> { ["groupBy"] = "month" },
            new[] { "operator", "tender", "department", "category" },
            PermissionCode.ReportManagement),

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
            new[] { "department", "category", "belowReorderOnly" },
            PermissionCode.ReportOperational),

        // Every batch on the shelf, with how much is left and when it
        // stops being sellable. StockOnHand answers what the stock is
        // worth, a row per product; this answers what is actually in the
        // dispensary, a row per batch.
        new("inv.stock.by-batch", "Stock On Hand by Batch",
            "StockByBatch",
            new Dictionary<string, string?>(),
            new[] { "department", "category", "withinDays", "includeZero" },
            PermissionCode.ReportOperational),

        // Named StockOnHand and given expiringOnly and withinDays, which
        // that definition has never read: it has no expiry to filter on,
        // because expiry belongs to a batch and it reports products. So
        // this preset returned the plain stock list and quietly ignored
        // both of its own parameters. It points at the batch report now,
        // which is the one that can answer it.
        new("inv.stock.expiring", "Stock Expiring",
            "StockByBatch",
            new Dictionary<string, string?> { ["expiringOnly"] = "true" },
            new[] { "withinDays" },
            PermissionCode.ReportOperational),

        new("dsp.scripts.reversed", "Reversed Scripts",
            "ReversedScripts",
            new Dictionary<string, string?>(),
            new[] { "pharmacist", "reason" },
            PermissionCode.ReportClinical),

        // The stock ledger, sliced three ways. These are the Inventory
        // menu's Transfers, Breakages and Stock entries: the staff know
        // them by those names, and they are the same rows under
        // different movement types.
        new("inv.stock.transactions", "Stock Transactions",
            "StockMovements",
            new Dictionary<string, string?>(),
            new[] { "movementType" },
            PermissionCode.ReportOperational),

        new("inv.stock.transfers", "Transfers",
            "StockMovements",
            new Dictionary<string, string?>
                { ["movementTypes"] = "TransferIn,TransferOut" },
            Array.Empty<string>(),
            PermissionCode.ReportOperational),

        new("inv.stock.supplier-returns", "Returns to Supplier",
            "StockMovements",
            new Dictionary<string, string?>
                { ["movementTypes"] = "ReturnToSupplier" },
            Array.Empty<string>(),
            PermissionCode.ReportOperational),

        new("inv.stock.breakages", "Breakages and Disposals",
            "StockMovements",
            new Dictionary<string, string?>
                { ["movementTypes"] = "Disposal,Adjustment" },
            Array.Empty<string>(),
            PermissionCode.ReportOperational),

        new("inv.price.changes", "Price Changes",
            "PriceChanges",
            new Dictionary<string, string?>(),
            new[] { "operator" },
            PermissionCode.ReportManagement),

        // Over any period, the imported history included: it is held in
        // the same tables as PharmaX's own trading.
        new("pos.history.top-sellers", "Top Sellers",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Sale", ["groupBy"] = "product" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("pos.history.daily", "Sales by Day",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Sale", ["groupBy"] = "day" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("pos.history.monthly", "Sales by Month",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Sale", ["groupBy"] = "month" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("pos.history.tender", "Sales by Tender",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Sale", ["groupBy"] = "party" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("pos.history.overrides", "Price Overrides",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Override", ["groupBy"] = "none" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("fin.history.cashups", "Cash-ups",
            "History",
            new Dictionary<string, string?> { ["kind"] = "DayEnd", ["groupBy"] = "none" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),


        new("pos.history.nosale", "Drawer Opened Without a Sale",
            "History",
            new Dictionary<string, string?> { ["kind"] = "NoSale", ["groupBy"] = "none" },
            Array.Empty<string>(),
            PermissionCode.ReportManagement),

        new("dsp.history.doctor", "Dispensing by Doctor",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Script", ["groupBy"] = "party" },
            Array.Empty<string>(),
            PermissionCode.ReportClinical),

        new("dsp.history.monthly", "Dispensing by Month",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Script", ["groupBy"] = "month" },
            Array.Empty<string>(),
            PermissionCode.ReportClinical),

        new("dsp.history.top", "Most Dispensed",
            "History",
            new Dictionary<string, string?> { ["kind"] = "Script", ["groupBy"] = "product" },
            Array.Empty<string>(),
            PermissionCode.ReportClinical),

        // The costs every margin stands on, where one does not look like
        // the cost of the pack it is priced as. Management's, because it
        // shows what the pharmacy pays for everything.
        new("inv.cost.check", "Cost Prices to Check",
            "CostCheck",
            new Dictionary<string, string?>(),
            new[] { "department", "category" },
            PermissionCode.ReportManagement)

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

            // Where the VAT came from: the till's sales, refunds given back,
            // or the medical aid schemes' share of what was dispensed.
            ["transactionType"] = new("transactionType", "Transaction type",
                "lookup", new[]
                {
                    new SlicerOptionDto("Sale", "Sales"),
                    new SlicerOptionDto("Refund", "Refunds"),
                    new SlicerOptionDto("Scheme", "Medical aid share")
                }),

            ["belowReorderOnly"] = new("belowReorderOnly",
                "Below reorder level only", "flag",
                Array.Empty<SlicerOptionDto>()),

            ["withinDays"] = new("withinDays", "Expiring within (days)",
                "number", Array.Empty<SlicerOptionDto>()),

            ["includeZero"] = new("includeZero",
                "Include empty batches", "flag",
                Array.Empty<SlicerOptionDto>()),

            ["movementType"] = new("movementType", "Movement type",
                "lookup", new[]
                {
                    new SlicerOptionDto("Receipt", "Receipt"),
                    new SlicerOptionDto("Issue", "Sale or issue"),
                    new SlicerOptionDto("Return", "Return"),
                    new SlicerOptionDto("Adjustment", "Adjustment"),
                    new SlicerOptionDto("TransferOut", "Transfer out"),
                    new SlicerOptionDto("TransferIn", "Transfer in"),
                    new SlicerOptionDto("StockTake", "Stock take"),
                    new SlicerOptionDto("Disposal", "Breakage or disposal")
                })
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

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// The screens a how-to answer names, as buttons under it. Longest name
/// first, so "End-of-Day Cash-Up" is not also offered as "Cash Up".
/// </summary>
internal static class AssistantOpens
{
    private static readonly (string Phrase, string Key, string Caption)[] Screens =
    {
        ("End-of-Day Cash-Up", "end-of-day", "End of day"),
        ("Returns and Credit Notes", "returns", "Returns"),
        ("Scripts to Collect", "collect", "Scripts to collect"),
        ("Receipts (Reprint)", "receipts", "Receipts"),
        ("Re-Print Script", "reprint-script", "Re-print script"),
        ("Script Maintenance", "script-maintenance", "Script maintenance"),
        ("Dispensary Quote", "dispensary-quote", "Dispensary quote"),
        ("Merge Patient Files", "merge-patients", "Merge patients"),
        ("Medical Aid Societies", "schemes", "Medical aid societies"),
        ("Medical Aid Claims", "claims", "Medical aid claims"),
        ("Dosage Codes", "dosage-codes", "Dosage codes"),
        ("Move Stock and Pre-packs", "move-stock", "Move stock"),
        ("Return to Supplier", "supplier-return", "Return to supplier"),
        ("Stock Adjustments", "stock-adjust", "Stock adjustments"),
        ("Purchase Orders", "purchase-orders", "Purchase orders"),
        ("Goods Receipt", "goods-receipt", "Goods receipt"),
        ("Stock Alerts", "alerts", "Stock alerts"),
        ("Stock on hand", "stock", "Stock on hand"),
        ("Mass Price Change", "mass-price", "Mass price change"),
        ("Chart of Accounts", "chart-of-accounts", "Chart of accounts"),
        ("Patient Accounts", "accounts-receivable", "Patient accounts"),
        ("Day-End Posting", "day-end", "Day-end posting"),
        ("Trial Balance", "trial-balance", "Trial balance"),
        ("Journal Entry", "journal", "Journal entry"),
        ("System Setup", "system-setup", "System setup"),
        ("Printer Setup", "printer-setup", "Printer setup"),
        ("Receipt Template", "receipt-template", "Receipt template"),
        ("Import Data", "data-import", "Import data"),
        ("Till Session", "till", "Till session"),
        ("Cash Up", "cash-up", "Cash Up"),
        ("Dispensing", "dispensing", "Dispensing"),
        ("Pre-packs", "prepack", "Pre-packs"),
        ("Drug Returns", "drug-returns", "Drug returns"),
        ("To Follow", "to-follow", "To follow"),
        ("Quotation", "quotation", "Quotation"),
        ("Patients", "patients", "Patients"),
        ("Products", "products", "Products"),
        ("Suppliers", "suppliers", "Suppliers"),
        ("Labels", "labels", "Labels"),
        ("Reports", "reports", "Reports"),
        ("Point of Sale > Sell", "pos", "Sell"),
        ("Users", "users", "Users")
    };

    public static IReadOnlyList<(string Key, string Caption)> In(string text)
    {
        var found = new List<(string Key, string Caption)>();
        var taken = new List<(int Start, int Length)>();

        foreach (var screen in Screens.OrderByDescending(s => s.Phrase.Length))
        {
            var at = 0;

            while (at <= text.Length - screen.Phrase.Length)
            {
                var foundAt = text.IndexOf(screen.Phrase, at, StringComparison.OrdinalIgnoreCase);
                if (foundAt < 0) break;

                var inside = taken.Any(t =>
                    foundAt >= t.Start && foundAt < t.Start + t.Length);

                if (!inside)
                {
                    found.Add((screen.Key, screen.Caption));
                    taken.Add((foundAt, screen.Phrase.Length));
                    break;
                }

                at = foundAt + screen.Phrase.Length;
            }
        }

        return found;
    }
}

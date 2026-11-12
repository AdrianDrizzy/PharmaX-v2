using PharmaX.Domain.Security;

namespace PharmaX.Application.Security;

/// <summary>
/// Which screens a user may open, by the menu key the shell uses for each
/// one: the single answer the menu, the home tiles, the keyboard shortcuts
/// and the shell's own guard all read.
/// </summary>
/// <remarks>
/// <para>
/// The shell used to enable whole headings on one permission each -
/// Dispensary on the pharmacist's clinical authority, Inventory on reading
/// a product - so an assistant who needed Patients could not reach it, a
/// cashier could open Stocktake, and a shortcut under a disabled heading
/// still opened its screen (DEF-0015, DEF-0027). Each entry is now judged
/// on its own, on the permission the screen's service actually demands,
/// so nothing is offered that the service will then refuse and nothing the
/// service allows is hidden.
/// </para>
/// <para>
/// This is the front door only. Every service still demands its own
/// permission, so a screen reached some other way is refused all the
/// same.
/// </para>
/// </remarks>
public static class ScreenAccess
{
    private static readonly Dictionary<string, Func<IPermissionService, bool>>
        Rules = new(StringComparer.Ordinal)
        {
            // Every signed-in user.
            ["printer-setup"] = _ => true,
            ["change-password"] = _ => true,
            ["logoff"] = _ => true,
            ["exit"] = _ => true,
            ["about"] = _ => true,

            // Ask PharmaX: general questions, no pharmacy data behind it.
            ["assistant"] = _ => true,

            // The day's receipts, to give a customer a copy: whoever is
            // at the counter. Copies are marked and recorded.
            ["receipts"] = _ => true,

            // The drawer belongs to whoever sells from it.
            ["till"] = p => p.Has(PermissionCode.SaleCreate),

            // A cashier cashes up their own shift; the pharmacist, the
            // assistant, the manager and the administrator any cashier's,
            // and they see the whole day (as does whoever reads the books).
            ["cash-up"] = p => p.HasAny(PermissionCode.SessionCashUp,
                                        PermissionCode.SessionCloseOtherUser),
            ["end-of-day"] = p => p.HasAny(PermissionCode.SessionCloseOtherUser,
                                           PermissionCode.ReportFinancial),

            // Dispensary.
            ["dispensing"] = p => p.Has(PermissionCode.ScriptDispense),
            ["patients"] = p => p.Has(PermissionCode.PatientMaintain),
            ["script-maintenance"] = p =>
                p.Has(PermissionCode.ScriptCapture)
                && p.Has(PermissionCode.PatientMaintain),
            ["reprint-script"] = p =>
                p.Has(PermissionCode.ScriptDispense)
                && p.Has(PermissionCode.PatientMaintain),
            ["labels"] = p => p.Has(PermissionCode.ScriptDispense),
            ["to-follow"] = p => p.Has(PermissionCode.ScriptDispense),

            // Priced by whoever captures or dispenses scripts.
            ["dispensary-quote"] = p =>
                p.HasAny(PermissionCode.ScriptCapture,
                         PermissionCode.ScriptDispense),
            ["tariffs"] = p => p.Has(PermissionCode.PriceChange),
            ["merge-patients"] = p =>
                p.Has(PermissionCode.ScriptAuthorise)
                && p.Has(PermissionCode.PatientMaintain),
            ["quotation"] = p => p.Has(PermissionCode.SaleCreate),
            ["schemes"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["prescribers"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["dosage-codes"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["drug-returns"] = p =>
                p.Has(PermissionCode.ScriptDispense) && MayRefund(p),

            // Point of sale.
            ["pos"] = p => p.Has(PermissionCode.SaleCreate),
            ["collect"] = p => p.Has(PermissionCode.SaleCreate),
            ["returns"] = MayRefund,

            // Inventory. The lists are maintained on these screens, so
            // they are offered to the people who maintain them.
            ["alerts"] = ManagesStock,
            ["departments"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["categories"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["manufacturers"] = p => p.Has(PermissionCode.ReferenceMaintain),
            ["suppliers"] = p => p.Has(PermissionCode.SupplierMaintain),
            ["purchase-orders"] = p =>
                p.HasAny(PermissionCode.PurchaseOrderRaise,
                         PermissionCode.GoodsReceive),
            ["goods-receipt"] = p => p.Has(PermissionCode.GoodsReceive),

            // A cashier looks prices up here; ProductService shows them the
            // over-the-counter catalogue only.
            ["products"] = p => p.Has(PermissionCode.ProductRead),

            // Menu entries that open a report preset carry that preset's
            // permission (PresetCatalogue).
            ["stock"] = p => p.Has(PermissionCode.ReportOperational),
            ["stock-adjust"] = p => p.Has(PermissionCode.StockTakePost),
            ["supplier-return"] = p => p.Has(PermissionCode.StockTakePost),
            ["move-stock"] = p => p.Has(PermissionCode.StockTakePost),
            ["prepack"] = p => p.Has(PermissionCode.StockPrepack),
            ["transfers"] = p => p.Has(PermissionCode.ReportOperational),
            ["breakages"] = p => p.Has(PermissionCode.ReportOperational),
            ["expired-stock"] = p => p.Has(PermissionCode.ReportOperational),
            ["price-changes"] = p => p.Has(PermissionCode.ReportManagement),
            ["mass-price"] = p => p.Has(PermissionCode.PriceChange),
            ["vat-codes"] = p => p.Has(PermissionCode.ReportFinancial),
            ["transaction-types"] = p =>
                p.Has(PermissionCode.ReportOperational),

            // Accounts.
            ["trial-balance"] = p => p.Has(PermissionCode.LedgerRead),
            ["journal"] = p => p.Has(PermissionCode.LedgerPost),
            ["accounts-receivable"] = p => p.Has(PermissionCode.LedgerRead),
            ["claims"] = p => p.Has(PermissionCode.LedgerRead),
            ["chart-of-accounts"] = p => p.Has(PermissionCode.LedgerRead),
            ["day-end"] = p =>
                p.Has(PermissionCode.LedgerPost)
                && p.Has(PermissionCode.ReportFinancial),

            // Reports: the screen lists only the presets the user may run.
            ["reports"] = p => p.HasAny(PermissionCode.ReportOperational,
                PermissionCode.ReportManagement,
                PermissionCode.ReportFinancial,
                PermissionCode.ReportClinical),

            // Stocktake: counting is not posting.
            ["stocktake-new"] = p => p.Has(PermissionCode.StockTakeCapture),
            ["stocktake-capture"] = p =>
                p.Has(PermissionCode.StockTakeCapture),
            ["stocktake-variance"] = p =>
                p.Has(PermissionCode.StockTakeCapture),
            ["stocktake-post"] = p => p.Has(PermissionCode.StockTakePost),

            // Administration.
            ["users"] = p => p.Has(PermissionCode.UserRoleAssign),
            ["role-permissions"] = p => p.Has(PermissionCode.RolePermissions),
            ["branches"] = p => p.Has(PermissionCode.BranchMaintain),
            ["system-setup"] = p => p.Has(PermissionCode.SettingsChange),
            ["receipt-template"] = p => p.Has(PermissionCode.ReceiptTemplate),
            ["data-import"] = p => p.Has(PermissionCode.DataImport),
            ["backup"] = p => p.Has(PermissionCode.Backup),
            ["system-console"] = p => p.Has(PermissionCode.SystemConsole),
            ["control-variables"] = p => p.Has(PermissionCode.SystemConsole)
        };

    /// Every menu key the policy knows.
    public static IReadOnlyCollection<string> Keys => Rules.Keys;

    /// <summary>
    /// Whether the user may open the screen behind a menu key. An unknown
    /// key is refused: a screen nobody has decided about is not open to
    /// everyone by default.
    /// </summary>
    public static bool CanOpen(string menuKey, IPermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return menuKey is not null
               && Rules.TryGetValue(menuKey, out var rule)
               && rule(permissions);
    }

    /// <summary>
    /// A refund needs a till user, and either the authority or a
    /// supervisor who has it: the returns screen is where the supervisor
    /// is asked for.
    /// </summary>
    private static bool MayRefund(IPermissionService p) =>
        p.Has(PermissionCode.SaleCreate)
        && p.Check(PermissionCode.ReturnAuthorise) != PermissionOutcome.Denied;

    /// People who look after stock: count it, order it or receive it.
    private static bool ManagesStock(IPermissionService p) =>
        p.Has(PermissionCode.ProductRead)
        && p.HasAny(PermissionCode.StockTakeCapture,
                    PermissionCode.PurchaseOrderRaise,
                    PermissionCode.GoodsReceive);
}

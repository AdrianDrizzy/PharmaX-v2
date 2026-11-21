namespace PharmaX.Domain.Security;

/// <summary>
/// The starting grant for each of the six standard roles: the agreed role
/// matrix, in one place the seeder, the screens and the tests all read.
/// </summary>
/// <remarks>
/// It lived as a private switch inside the seeder, which meant the only
/// way to find out what a cashier could do was to seed a database and sign
/// in as one - and the end-to-end run that finally did found the roles
/// both too narrow (an assistant could not open a patient file) and too
/// wide (a cashier could start a stock count). A public table can be
/// checked by a unit test against the matrix the pharmacy signed off.
///
/// Deliberately narrow still: widening a role on request is a five-minute
/// conversation, and narrowing one after staff have used it for a month
/// is not.
/// </remarks>
public static class StandardRoleGrants
{
    public static IReadOnlyList<string> For(string role) => role switch
    {
        // Everything, including the pharmacist's clinical authority, so
        // that the owner or the IT person can finish any job a member of
        // staff has walked away from.
        Role.Standard.Administrator => PermissionCode.All,

        Role.Standard.Manager => new[]
        {
            PermissionCode.SaleCreate,
            PermissionCode.SaleTakePayment,
            PermissionCode.SaleDiscountAboveLimit,
            PermissionCode.ReturnAuthorise,
            PermissionCode.SessionCloseOtherUser,

            // Money out of the drawer, as every role with a till records
            // it (23 October 2026).
            PermissionCode.SessionPayOut,

            // Taking a printed receipt off the Receipts list, with a
            // reason; the sale stays (25 October 2026).
            PermissionCode.ReceiptHide,

            // The branch manager looks patients up for account queries
            // and complaints, but does not capture or dispense scripts.
            PermissionCode.PatientMaintain,
            PermissionCode.PatientDeactivate,

            // Charging a dispensed item at something other than its
            // price, as the pharmacist and the assistant do (24 October
            // 2026). Not dispensing, the manager authorises it at the
            // dispensing screen for whoever is.
            PermissionCode.DispensingChargeAdjust,

            PermissionCode.ProductRead,
            PermissionCode.ProductDispensary,
            PermissionCode.ProductMaintain,
            PermissionCode.ProductDeactivate,
            PermissionCode.PriceChange,
            PermissionCode.StockTakeCapture,
            PermissionCode.StockTakePost,

            // Clearing expired and out-of-stock lines from Stock alerts,
            // as the pharmacist and the assistant do (18 October 2026).
            PermissionCode.StockRemove,
            PermissionCode.SupplierMaintain,
            PermissionCode.ReferenceMaintain,
            PermissionCode.PurchaseOrderRaise,
            PermissionCode.GoodsReceive,
            PermissionCode.ReportOperational,
            PermissionCode.ReportManagement,
            PermissionCode.ReportFinancial,

            // The books are a manager's job. A cashier posting a journal
            // is how a till shortage quietly becomes a rounding
            // adjustment.
            PermissionCode.LedgerRead,
            PermissionCode.LedgerPost,
            PermissionCode.AccountDeactivate,

            // Staff come and go at branch level. UserService stops a
            // manager handing out a role wider than their own, so this
            // does not make a manager an administrator.
            PermissionCode.UserRoleAssign,

            // Match the stock in this till's printer (16 November 2026).
            PermissionCode.PrintSizes
        },

        Role.Standard.Pharmacist => new[]
        {
            PermissionCode.SaleCreate,
            PermissionCode.SaleTakePayment,
            PermissionCode.PatientMaintain,
            PermissionCode.PatientDeactivate,
            PermissionCode.PrintSizes,

            // The authorisations a till asks for - a refund, a discount
            // over the cashier's limit, another person's till - are given
            // by whoever is senior on the floor: the pharmacist, an
            // assistant, the manager or the administrator (16 October
            // 2026). Held, they are also the holder's own to use.
            PermissionCode.ReturnAuthorise,
            PermissionCode.SaleDiscountAboveLimit,
            PermissionCode.SessionCloseOtherUser,
            PermissionCode.SessionPayOut,
            PermissionCode.ScriptCapture,
            PermissionCode.ScriptDispense,
            PermissionCode.ScriptAuthorise,
            PermissionCode.ScriptReverse,
            PermissionCode.LabelDeactivate,

            // Correcting what was handed over and charged, after the
            // issue, and pre-packing bulk stock: the dispensary's own.
            PermissionCode.DispensingAmend,
            PermissionCode.StockPrepack,

            // Charging an item at something other than its price, with a
            // reason (24 October 2026).
            PermissionCode.DispensingChargeAdjust,

            // And clears expired and out-of-stock lines from Stock alerts
            // (18 October 2026).
            PermissionCode.StockRemove,

            // The dispensary orders and books in its own stock (16
            // October 2026).
            PermissionCode.PurchaseOrderRaise,
            PermissionCode.GoodsReceive,

            PermissionCode.ProductRead,
            PermissionCode.ProductDispensary,

            // Keeps the products up to date - prices, stock on hand and
            // the rest of the product - as the assistant and the
            // administrator do (23 October 2026). Stock is corrected
            // with a reason, as an adjustment; stocktakes, write-offs
            // and transfers stay with inv.stocktake.post.
            PermissionCode.ProductMaintain,
            PermissionCode.ProductDeactivate,
            PermissionCode.PriceChange,
            PermissionCode.StockCorrect,

            PermissionCode.StockTakeCapture,
            PermissionCode.ReportOperational,
            PermissionCode.ReportClinical,
            PermissionCode.HistoryView
        },

        // Captures patients and scripts, prepares and issues the
        // dispensing, orders stock and books deliveries in - hard-stop
        // overrides and controlled medicines included, without a
        // pharmacist signing in (DispensingService.ActsOnOwnAuthority).
        // Merging patient files stays a pharmacist's.
        Role.Standard.PharmacistAssistant => new[]
        {
            PermissionCode.SaleCreate,
            PermissionCode.PatientMaintain,
            PermissionCode.PatientDeactivate,
            PermissionCode.PrintSizes,

            // The till's authorisations, as the pharmacist has them.
            PermissionCode.ReturnAuthorise,
            PermissionCode.SaleDiscountAboveLimit,
            PermissionCode.SessionCloseOtherUser,
            PermissionCode.SessionPayOut,
            PermissionCode.ScriptCapture,
            PermissionCode.ScriptDispense,
            PermissionCode.DispensingAmend,
            PermissionCode.DispensingChargeAdjust,
            PermissionCode.StockPrepack,
            PermissionCode.StockRemove,
            PermissionCode.LabelDeactivate,
            PermissionCode.ProductRead,
            PermissionCode.ProductDispensary,

            // The products, as the pharmacist keeps them.
            PermissionCode.ProductMaintain,
            PermissionCode.ProductDeactivate,
            PermissionCode.PriceChange,
            PermissionCode.StockCorrect,

            PermissionCode.StockTakeCapture,
            PermissionCode.PurchaseOrderRaise,
            PermissionCode.GoodsReceive,
            PermissionCode.ReportOperational,
            PermissionCode.HistoryView
        },

        // Over-the-counter trade only. No dispensary stock, no stock
        // counts, no prices, no reports.
        Role.Standard.Cashier => new[]
        {
            PermissionCode.SaleCreate,
            PermissionCode.SaleTakePayment,
            PermissionCode.SessionCashUp,
            PermissionCode.SessionPayOut,
            PermissionCode.ProductRead,
            PermissionCode.PrintSizes
        },

        Role.Standard.StockController => new[]
        {
            PermissionCode.ProductRead,
            PermissionCode.ProductDispensary,
            PermissionCode.PrintSizes,

            // Adds the products the deliveries bring in. The selling
            // price of an existing product stays with inv.price.change,
            // which is the one thing a stock controller is deliberately
            // not given.
            PermissionCode.ProductMaintain,
            PermissionCode.ProductDeactivate,

            PermissionCode.StockTakeCapture,
            PermissionCode.StockTakePost,
            PermissionCode.SupplierMaintain,

            // The lists stock is filed against. A stock controller
            // files stock all day and could not add the department or
            // category to file it in while maintaining those demanded
            // the price-change permission.
            PermissionCode.ReferenceMaintain,

            PermissionCode.PurchaseOrderRaise,
            PermissionCode.GoodsReceive,
            PermissionCode.ReportOperational
        },

        _ => Array.Empty<string>()
    };
}

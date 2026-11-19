using System.Diagnostics.CodeAnalysis;

namespace PharmaX.Domain.Security;

/// <summary>
/// The permission codes the application demands by name. Constants rather
/// than literals so that a typo is a compile error instead of a silent
/// denial at a till.
/// </summary>
public static class PermissionCode
{
    public const string SaleCreate = "pos.sale.create";

    /// Taking the money for a sale. Every selling role except the
    /// pharmacist assistant, who holds the basket for the cashier.
    public const string SaleTakePayment = "pos.sale.take-payment";

    public const string SaleDiscountAboveLimit = "pos.sale.discount.above-limit";
    public const string ReturnAuthorise = "pos.return.authorise";
    public const string SessionCloseOtherUser = "pos.session.close.other-user";

    /// End-of-day cash-up and close, on this till's own computer.
    /// A cashier or an administrator.
    public const string SessionCashUp = "pos.session.cash-up";

    /// <summary>
    /// Recording money taken out of the drawer during a shift - drawings,
    /// lunch, another expense - with the amount and a note, so the cash-up
    /// expects that much less and the Z-read says where it went.
    /// </summary>
    /// <remarks>
    /// Every role with a till (23 October 2026). Its own permission so a
    /// pharmacy can take it off a role without taking the till with it.
    /// </remarks>
    public const string SessionPayOut = "pos.session.pay-out";

    /// <summary>File > Cash Up, on the menu.</summary>
    /// <remarks>
    /// The administrator only for now (19 November 2026). The services
    /// behind the screen still demand their own permissions.
    /// </remarks>
    public const string CashUpScreen = "pos.screen.cash-up";

    /// <summary>File > End-of-Day Cash-Up, on the menu.</summary>
    /// <remarks>The administrator only for now (19 November 2026).</remarks>
    public const string EndOfDayScreen = "pos.screen.end-of-day";

    /// <summary>
    /// Taking a printed receipt off the Receipts list, with a reason, and
    /// putting one back. The sale itself is untouched.
    /// </summary>
    /// <remarks>
    /// The manager and the administrator (25 October 2026).
    /// </remarks>
    public const string ReceiptHide = "pos.receipt.hide";

    /// <summary>
    /// A pharmacist's own clinical authority: overriding a hard-stop
    /// clinical warning, releasing a controlled medicine, merging two
    /// patient files. Not needed to capture or issue an ordinary script.
    /// </summary>
    public const string ScriptAuthorise = "dsp.script.authorise";
    public const string ScriptReverse = "dsp.script.reverse";

    /// <summary>
    /// Deleting an uncollected script from Scripts to collect, putting
    /// its medicine back on the shelf.
    /// </summary>
    /// <remarks>
    /// Administrator only by default (16 November 2026). Its own
    /// permission so a pharmacy can hand it to a manager without
    /// giving them clinical reverse, and so a cashier with the till
    /// cannot clear the queue.
    /// </remarks>
    public const string ScriptDelete = "dsp.script.delete";

    /// <summary>
    /// Capturing a prescription into Script Maintenance.
    /// </summary>
    /// <remarks>
    /// Its own permission rather than dsp.script.authorise, which is
    /// what capture used to demand. That tied the most routine job in
    /// the dispensary to the one authority only a pharmacist holds, so
    /// an assistant could not type in the script the patient had just
    /// handed over, and every script became a supervisor prompt.
    /// </remarks>
    public const string ScriptCapture = "dsp.script.capture";

    /// <summary>
    /// Working the dispensing screen: picking the script, adding the
    /// medicines, issuing and labelling. The clinical overrides inside it
    /// still need dsp.script.authorise.
    /// </summary>
    public const string ScriptDispense = "dsp.script.dispense";

    /// <summary>
    /// Correcting a dispensing after it was issued: the quantity handed
    /// over and the amount charged for an item.
    /// </summary>
    /// <remarks>
    /// Its own permission because it moves stock and changes what the
    /// patient owes after the labels are on the bottles. Changing an item
    /// still being dispensed is part of dispensing it.
    /// </remarks>
    public const string DispensingAmend = "dsp.dispensing.amend";

    /// <summary>
    /// Charging an item at something other than what it is priced at -
    /// 1.00 for fourteen tablets priced at 0.93 - while it is being
    /// dispensed or after the issue, with a reason kept on the record.
    /// </summary>
    /// <remarks>
    /// The pharmacist, the assistant, the manager and the administrator
    /// (24 October 2026). A supervisor may authorise it on the spot for
    /// someone dispensing who does not hold it, which is how a manager,
    /// who does not dispense, adjusts a charge at the dispensing screen.
    /// </remarks>
    public const string DispensingChargeAdjust = "dsp.charge.adjust";

    /// <summary>
    /// Finding, adding and editing patient files.
    /// </summary>
    public const string PatientMaintain = "dsp.patient.maintain";

    /// <summary>
    /// Taking a patient file off searches, and putting one back. History
    /// stays; the file is never deleted.
    /// </summary>
    /// <remarks>
    /// Its own permission so maintain can stay with the counter while
    /// retiring a file stays with whoever the pharmacy ticks (16
    /// November 2026).
    /// </remarks>
    public const string PatientDeactivate = "dsp.patient.deactivate";

    public const string ProductRead = "inv.product.read";

    /// <summary>
    /// Seeing and handling dispensary stock: anything scheduled that
    /// needs a pharmacist (Product.RequiresPharmacist).
    /// </summary>
    /// <remarks>
    /// Without it a user sees the over-the-counter catalogue only - in
    /// product searches, at the till, on the product list and in stock
    /// alerts. The filter is applied in the query itself, so a cashier
    /// typing an exact dispensary code gets "no product" rather than a
    /// product the screen then hides.
    /// </remarks>
    public const string ProductDispensary = "inv.product.dispensary";

    /// <summary>
    /// Taking a product off the catalogue. Sales history stays; the
    /// product is never deleted.
    /// </summary>
    /// <remarks>
    /// Separate from inv.product.maintain so a pharmacy can let staff
    /// edit a product without letting them discontinue it (16 November
    /// 2026).
    /// </remarks>
    public const string ProductDeactivate = "inv.product.deactivate";

    /// <summary>
    /// Adding a product and changing its description, filing, reorder
    /// level and pack. Changing the selling price of an existing product
    /// is inv.price.change.
    /// </summary>
    public const string ProductMaintain = "inv.product.maintain";

    public const string PriceChange = "inv.price.change";

    /// <summary>
    /// Starting a stock count and capturing what is on the shelf.
    /// Posting the variances is inv.stocktake.post.
    /// </summary>
    public const string StockTakeCapture = "inv.stocktake.capture";
    public const string StockTakePost = "inv.stocktake.post";

    /// <summary>
    /// Counting bulk stock into pre-packs: a tin of a thousand into
    /// twenty-five packs of forty, each its own item on the shelf.
    /// </summary>
    /// <remarks>
    /// Apart from inv.stocktake.post, which every other stock adjustment
    /// needs: pre-packing is the dispensary's daily work, and posting a
    /// stocktake is not.
    /// </remarks>
    public const string StockPrepack = "inv.stock.prepack";

    /// <summary>
    /// Deleting what Stock alerts lists as expired or out of stock: an
    /// expired batch is written off in full, an out-of-stock product is
    /// discontinued.
    /// </summary>
    /// <remarks>
    /// Its own permission, apart from inv.stocktake.post and
    /// inv.product.maintain, so the pharmacist and the assistant can clear
    /// the dead stock the dispensary finds without being handed stock
    /// counts or the catalogue (18 October 2026).
    /// </remarks>
    public const string StockRemove = "inv.stock.remove";

    /// <summary>
    /// Correcting a product's quantity on hand from the product editor or
    /// the product list, with a reason: posted as a stock adjustment, so
    /// the movement report and the audit trail say what changed and why.
    /// </summary>
    /// <remarks>
    /// Its own permission, apart from inv.stocktake.post, so the
    /// pharmacist and the assistant can put a product's stock right
    /// without being handed stocktakes, write-offs and transfers (23
    /// October 2026). Whoever posts stocktakes may correct it too.
    /// </remarks>
    public const string StockCorrect = "inv.stock.correct";

    public const string SupplierMaintain = "inv.supplier.maintain";

    /// <summary>
    /// Maintaining the lists a product is filed against:
    /// departments, categories, manufacturers, branches, societies.
    /// </summary>
    /// <remarks>
    /// Its own permission rather than borrowing inv.price.change,
    /// which is what these screens used to demand. Renaming a
    /// department and repricing the shelf are different authorities,
    /// and tying them together meant a stock controller who files
    /// stock all day could not add the department to file it in,
    /// while anyone who could had also been handed the prices.
    /// </remarks>
    public const string ReferenceMaintain = "inv.reference.maintain";
    public const string PurchaseOrderRaise = "inv.purchase.order";
    public const string GoodsReceive = "inv.goods.receive";

    public const string ReportOperational = "rpt.report.run.operational";
    public const string ReportManagement = "rpt.report.run.management";
    public const string ReportFinancial = "rpt.report.run.financial";
    public const string ReportClinical = "rpt.report.run.clinical";

    public const string LedgerRead = "fin.ledger.read";
    public const string LedgerPost = "fin.journal.post";

    public const string UserRoleAssign = "adm.user.role.assign";

    /// <summary>
    /// Changing which permissions each role holds. The administrator,
    /// and whoever they tick this for.
    /// </summary>
    public const string RolePermissions = "adm.role.permissions";

    /// <summary>System Setup: currency, logo, colours, markup and rounding.</summary>
    public const string SettingsChange = "adm.settings.change";

    // Each administration screen on its own, so one can be handed to a
    // role without the rest (12 November 2026). They were all System settings.
    public const string BranchMaintain = "adm.branches";
    public const string ReceiptTemplate = "adm.receipt.template";

    /// <summary>
    /// Print sizes for labels, receipts and claim forms on this computer.
    /// </summary>
    /// <remarks>
    /// Every standard role holds it by default (16 November 2026), so a
    /// till can match the stock in its printer without an administrator.
    /// Untick it on Role permissions to lock the screen down.
    /// </remarks>
    public const string PrintSizes = "adm.print.sizes";

    public const string DataImport = "adm.data.import";
    public const string Backup = "adm.backup";

    /// <summary>System Console, Control Variables and the pharmacy's particulars.</summary>
    public const string SystemConsole = "adm.system.console";

    /// <summary>The Templates tab on Labels: saved sticker layouts.</summary>
    public const string LabelTemplates = "dsp.label.templates";

    /// <summary>
    /// Deactivating or deleting a saved pre-pack or template sticker on
    /// Labels.
    /// </summary>
    public const string LabelDeactivate = "dsp.label.deactivate";

    /// <summary>
    /// Taking a ledger account off the chart. The books keep it.
    /// </summary>
    public const string AccountDeactivate = "fin.account.deactivate";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        SaleCreate, SaleTakePayment, SaleDiscountAboveLimit, ReturnAuthorise,
        SessionCloseOtherUser, SessionCashUp, ScriptAuthorise, ScriptReverse,
        ScriptDelete, ScriptCapture, ScriptDispense, PatientMaintain,
        PatientDeactivate, ProductRead, ProductDispensary, ProductMaintain,
        ProductDeactivate, PriceChange,
        StockTakeCapture, StockTakePost, SupplierMaintain,
        ReferenceMaintain, PurchaseOrderRaise, GoodsReceive,
        ReportOperational, ReportManagement, ReportFinancial,
        ReportClinical, LedgerRead, LedgerPost, AccountDeactivate,
        UserRoleAssign, RolePermissions, SettingsChange, DispensingAmend, StockPrepack,
        StockRemove, StockCorrect, SessionPayOut, DispensingChargeAdjust,
        ReceiptHide, BranchMaintain, ReceiptTemplate, PrintSizes, DataImport, Backup,
        SystemConsole, LabelTemplates, LabelDeactivate, CashUpScreen, EndOfDayScreen
    };

    /// <summary>What the role-permissions screen shows beside the code.</summary>
    public static string Label(string code) => code switch
    {
        SaleCreate => "Sell",
        SaleTakePayment => "Take payment",
        SaleDiscountAboveLimit => "Discount over the limit",
        ReturnAuthorise => "Authorise a refund",
        SessionCloseOtherUser => "Close someone else's till",
        SessionCashUp => "Cash up and close",
        SessionPayOut => "Pay money out of the till",
        CashUpScreen => "File > Cash Up",
        EndOfDayScreen => "File > End-of-Day Cash-Up",
        ReceiptHide => "Hide a receipt",
        ScriptAuthorise => "Clinical authority",
        ScriptReverse => "Reverse a script",
        ScriptDelete => "Delete a script from collect",
        ScriptCapture => "Capture a script",
        ScriptDispense => "Dispense",
        DispensingAmend => "Correct a dispensed item",
        DispensingChargeAdjust => "Charge other than the price",
        PatientMaintain => "Patients",
        PatientDeactivate => "Deactivate a patient",
        ProductRead => "Look up products",
        ProductDispensary => "Dispensary stock",
        ProductMaintain => "Maintain products",
        ProductDeactivate => "Deactivate a product",
        PriceChange => "Change a price",
        StockTakeCapture => "Count stock",
        StockTakePost => "Post a stocktake",
        StockPrepack => "Pre-pack stock",
        StockRemove => "Clear expired or out-of-stock lines",
        StockCorrect => "Correct stock on hand",
        SupplierMaintain => "Suppliers",
        ReferenceMaintain => "Departments and lists",
        PurchaseOrderRaise => "Raise a purchase order",
        GoodsReceive => "Receive stock",
        ReportOperational => "Operational reports",
        ReportManagement => "Management reports",
        ReportFinancial => "Financial reports",
        ReportClinical => "Clinical reports",
        LedgerRead => "Read the books",
        LedgerPost => "Post a journal",
        AccountDeactivate => "Deactivate a ledger account",
        UserRoleAssign => "Users and roles",
        RolePermissions => "Role permissions",
        SettingsChange => "System setup",
        BranchMaintain => "Branches",
        ReceiptTemplate => "Receipt template",
        PrintSizes => "Print sizes",
        DataImport => "Import data",
        Backup => "Back up now",
        SystemConsole => "System console, control variables and pharmacy details",
        LabelTemplates => "Label templates",
        LabelDeactivate => "Deactivate or delete a label sticker",
        _ => code
    };
}

/// <summary>
/// A permission as stored. Escalatable permissions are the ones a
/// supervisor may authorise on the spot rather than simply refusing.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The reserved suffix applies to Code Access Security permission "
                  + "types. This is the domain entity the specification calls a "
                  + "permission, and renaming it would make the code and the "
                  + "specification disagree.")]
public sealed class Permission
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Code { get; private set; } = "";
    public string? Description { get; private set; }
    public bool IsSupervisorEscalatable { get; private set; }

    private Permission() { }

    public static Permission Create(string code, string? description = null,
                                    bool isSupervisorEscalatable = false) =>
        new()
        {
            Code = code,
            Description = description,
            IsSupervisorEscalatable = isSupervisorEscalatable
        };
}

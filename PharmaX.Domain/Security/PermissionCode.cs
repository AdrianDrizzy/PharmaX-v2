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
    public const string SaleDiscountAboveLimit = "pos.sale.discount.above-limit";
    public const string ReturnAuthorise = "pos.return.authorise";
    public const string SessionCloseOtherUser = "pos.session.close.other-user";

    /// <summary>
    /// A pharmacist's own clinical authority: overriding a hard-stop
    /// clinical warning, releasing a controlled medicine, merging two
    /// patient files. Not needed to capture or issue an ordinary script.
    /// </summary>
    public const string ScriptAuthorise = "dsp.script.authorise";
    public const string ScriptReverse = "dsp.script.reverse";

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
    /// Finding, adding and editing patient files.
    /// </summary>
    public const string PatientMaintain = "dsp.patient.maintain";

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
    public const string SettingsChange = "adm.settings.change";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        SaleCreate, SaleDiscountAboveLimit, ReturnAuthorise,
        SessionCloseOtherUser, ScriptAuthorise, ScriptReverse,
        ScriptCapture, ScriptDispense, PatientMaintain,
        ProductRead, ProductDispensary, ProductMaintain, PriceChange,
        StockTakeCapture, StockTakePost, SupplierMaintain,
        ReferenceMaintain, PurchaseOrderRaise, GoodsReceive,
        ReportOperational, ReportManagement, ReportFinancial,
        ReportClinical, LedgerRead, LedgerPost,
        UserRoleAssign, SettingsChange, DispensingAmend, StockPrepack
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

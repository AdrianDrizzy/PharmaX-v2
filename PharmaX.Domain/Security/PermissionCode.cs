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

    public const string ScriptAuthorise = "dsp.script.authorise";
    public const string ScriptReverse = "dsp.script.reverse";

    public const string ProductRead = "inv.product.read";
    public const string PriceChange = "inv.price.change";
    public const string StockTakePost = "inv.stocktake.post";
    public const string SupplierMaintain = "inv.supplier.maintain";
    public const string PurchaseOrderRaise = "inv.purchase.order";
    public const string GoodsReceive = "inv.goods.receive";

    public const string ReportOperational = "rpt.report.run.operational";
    public const string ReportManagement = "rpt.report.run.management";
    public const string ReportFinancial = "rpt.report.run.financial";
    public const string ReportClinical = "rpt.report.run.clinical";

    public const string UserRoleAssign = "adm.user.role.assign";
    public const string SettingsChange = "adm.settings.change";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        SaleCreate, SaleDiscountAboveLimit, ReturnAuthorise,
        SessionCloseOtherUser, ScriptAuthorise, ScriptReverse,
        ProductRead, PriceChange, StockTakePost, SupplierMaintain,
        PurchaseOrderRaise, GoodsReceive,
        ReportOperational, ReportManagement, ReportFinancial,
        ReportClinical, UserRoleAssign, SettingsChange
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

using FluentAssertions;
using PharmaX.Application.Security;
using PharmaX.Desktop.Shell;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Security;

/// <summary>
/// The agreed role matrix, checked against the grants each standard role
/// is seeded with and the screen policy the shell reads. The end-to-end
/// run found the roles both too narrow and too wide (DEF-0015); this is
/// what notices if they drift again.
/// </summary>
public class RoleMatrixTests
{
    private static IPermissionService As(string role) =>
        new PermissionService(new FakeCurrentUser(),
            new FakePermissionRepository(StandardRoleGrants.For(role),
                escalatable: new[]
                {
                    PermissionCode.SaleDiscountAboveLimit,
                    PermissionCode.ReturnAuthorise,
                    PermissionCode.SessionCloseOtherUser,
                    PermissionCode.PriceChange
                }),
            new FakeAuditWriter());

    public static TheoryData<string, string> MustOpen => new()
    {
        { Role.Standard.Administrator, "script-maintenance" },
        { Role.Standard.Administrator, "dispensing" },
        { Role.Standard.Administrator, "patients" },
        { Role.Standard.Administrator, "users" },
        { Role.Standard.Administrator, "role-permissions" },
        { Role.Standard.Administrator, "system-setup" },
        { Role.Standard.Administrator, "control-variables" },
        { Role.Standard.Administrator, "goods-receipt" },

        { Role.Standard.Manager, "users" },
        { Role.Standard.Manager, "patients" },
        { Role.Standard.Manager, "day-end" },
        { Role.Standard.Manager, "stocktake-capture" },
        { Role.Standard.Manager, "price-changes" },
        { Role.Standard.Manager, "trial-balance" },
        { Role.Standard.Manager, "reports" },
        { Role.Standard.Manager, "goods-receipt" },

        { Role.Standard.Pharmacist, "dispensing" },
        { Role.Standard.Pharmacist, "labels" },
        { Role.Standard.Pharmacist, "patients" },
        { Role.Standard.Pharmacist, "script-maintenance" },
        { Role.Standard.Pharmacist, "dispensary-quote" },
        { Role.Standard.Pharmacist, "pos" },
        { Role.Standard.Pharmacist, "purchase-orders" },
        { Role.Standard.Pharmacist, "goods-receipt" },

        { Role.Standard.PharmacistAssistant, "patients" },
        { Role.Standard.PharmacistAssistant, "script-maintenance" },
        { Role.Standard.PharmacistAssistant, "dispensary-quote" },
        { Role.Standard.PharmacistAssistant, "dispensing" },
        { Role.Standard.PharmacistAssistant, "goods-receipt" },
        { Role.Standard.PharmacistAssistant, "purchase-orders" },
        { Role.Standard.PharmacistAssistant, "pos" },
        { Role.Standard.PharmacistAssistant, "till" },

        { Role.Standard.Cashier, "pos" },
        { Role.Standard.Cashier, "till" },
        { Role.Standard.Cashier, "returns" },
        { Role.Standard.Cashier, "collect" },
        { Role.Standard.Cashier, "products" },
        { Role.Standard.Cashier, "quotation" },

        { Role.Standard.StockController, "goods-receipt" },
        { Role.Standard.StockController, "purchase-orders" },
        { Role.Standard.StockController, "products" },
        { Role.Standard.StockController, "stocktake-capture" },
        { Role.Standard.StockController, "stocktake-post" },
        { Role.Standard.StockController, "alerts" },
        { Role.Standard.StockController, "suppliers" }
    };

    public static TheoryData<string, string> MustRefuse => new()
    {
        { Role.Standard.Manager, "system-setup" },
        { Role.Standard.Manager, "role-permissions" },
        { Role.Standard.Manager, "dispensing" },

        { Role.Standard.Pharmacist, "users" },
        { Role.Standard.Pharmacist, "role-permissions" },
        { Role.Standard.Pharmacist, "price-changes" },
        { Role.Standard.Pharmacist, "system-setup" },
        { Role.Standard.Pharmacist, "day-end" },

        { Role.Standard.PharmacistAssistant, "users" },
        { Role.Standard.PharmacistAssistant, "role-permissions" },
        { Role.Standard.PharmacistAssistant, "price-changes" },
        { Role.Standard.PharmacistAssistant, "system-setup" },
        { Role.Standard.PharmacistAssistant, "day-end" },
        { Role.Standard.PharmacistAssistant, "merge-patients" },

        // A cashier sells over the counter and nothing else.
        { Role.Standard.Cashier, "dispensing" },
        { Role.Standard.Cashier, "patients" },
        { Role.Standard.Cashier, "script-maintenance" },
        { Role.Standard.Cashier, "labels" },
        { Role.Standard.Cashier, "reprint-script" },
        { Role.Standard.Cashier, "dispensary-quote" },
        { Role.Standard.Cashier, "drug-returns" },
        { Role.Standard.Cashier, "goods-receipt" },
        { Role.Standard.Cashier, "purchase-orders" },
        { Role.Standard.Cashier, "alerts" },
        { Role.Standard.Cashier, "stock" },
        { Role.Standard.Cashier, "stocktake-new" },
        { Role.Standard.Cashier, "stocktake-capture" },
        { Role.Standard.Cashier, "price-changes" },
        { Role.Standard.Cashier, "reports" },
        { Role.Standard.Cashier, "users" },
        { Role.Standard.Cashier, "role-permissions" },
        { Role.Standard.Cashier, "system-setup" },
        { Role.Standard.Cashier, "control-variables" },
        { Role.Standard.Cashier, "day-end" },
        { Role.Standard.Cashier, "trial-balance" },

        { Role.Standard.StockController, "users" },
        { Role.Standard.StockController, "role-permissions" },
        { Role.Standard.StockController, "system-setup" },
        { Role.Standard.StockController, "day-end" },
        { Role.Standard.StockController, "dispensing" },
        { Role.Standard.StockController, "pos" },
        { Role.Standard.StockController, "returns" }
    };

    [Theory]
    [MemberData(nameof(MustOpen))]
    public void The_role_can_open_what_its_job_needs(string role, string screen) =>
        ScreenAccess.CanOpen(screen, As(role)).Should().BeTrue(
            $"a {role} must be able to open {screen}");

    [Theory]
    [MemberData(nameof(MustRefuse))]
    public void The_role_is_kept_out_of_what_is_not_its_job(string role,
                                                            string screen) =>
        ScreenAccess.CanOpen(screen, As(role)).Should().BeFalse(
            $"a {role} must be kept out of {screen}");

    [Fact]
    public void A_cashier_sees_the_over_the_counter_catalogue_only()
    {
        As(Role.Standard.Cashier).ProductScope()
            .Should().Be(PharmaX.Application.Repositories.ProductScope.OverTheCounter);

        foreach (var role in Role.Standard.All.Where(r =>
                     r != Role.Standard.Cashier))
            As(role).ProductScope().Should()
                .Be(PharmaX.Application.Repositories.ProductScope.All, role);
    }

    [Theory]
    [InlineData(Role.Standard.Pharmacist)]
    [InlineData(Role.Standard.PharmacistAssistant)]
    [InlineData(Role.Standard.Administrator)]
    public void Script_capture_and_issue_need_no_supervisor(string role)
    {
        var permissions = As(role);

        permissions.Has(PermissionCode.ScriptCapture).Should().BeTrue();
        permissions.Has(PermissionCode.ScriptDispense).Should().BeTrue();
        permissions.Has(PermissionCode.PatientMaintain).Should().BeTrue();
    }

    [Fact]
    public void Only_a_pharmacist_or_the_administrator_holds_clinical_authority()
    {
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.ScriptAuthorise))
            .Should().BeEquivalentTo(Role.Standard.Pharmacist,
                                     Role.Standard.Administrator);
    }

    /// <summary>
    /// Deleting from Scripts to collect puts medicine back on the shelf.
    /// Administrator only by default; tick it on for another role on Role
    /// permissions (16 November 2026).
    /// </summary>
    [Fact]
    public void Only_the_administrator_deletes_an_uncollected_script() =>
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.ScriptDelete))
            .Should().BeEquivalentTo(Role.Standard.Administrator);

    /// <summary>
    /// What a till asks a second person for - a refund, a discount over the
    /// cashier's limit, another person's till - any of the senior roles can
    /// give, and nobody below them (16 October 2026).
    /// </summary>
    [Theory]
    [InlineData(PermissionCode.ReturnAuthorise)]
    [InlineData(PermissionCode.SaleDiscountAboveLimit)]
    [InlineData(PermissionCode.SessionCloseOtherUser)]
    public void The_pharmacist_assistant_manager_and_administrator_authorise_the_till(
        string authorisation) =>
        Role.Standard.All
            .Where(r => As(r).Has(authorisation))
            .Should().BeEquivalentTo(
                Role.Standard.Pharmacist, Role.Standard.PharmacistAssistant,
                Role.Standard.Manager, Role.Standard.Administrator);

    /// <summary>
    /// The dispensary orders and books in its own stock, as the manager
    /// and the stock controller do (16 October 2026).
    /// </summary>
    [Theory]
    [InlineData(PermissionCode.PurchaseOrderRaise)]
    [InlineData(PermissionCode.GoodsReceive)]
    public void Everyone_but_the_cashier_orders_and_receives_stock(
        string permission) =>
        Role.Standard.All
            .Where(r => As(r).Has(permission))
            .Should().BeEquivalentTo(
                Role.Standard.Pharmacist, Role.Standard.PharmacistAssistant,
                Role.Standard.Manager, Role.Standard.StockController,
                Role.Standard.Administrator);

    [Fact]
    public void Everyone_who_sells_except_the_assistant_can_take_payment() =>
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.SaleTakePayment))
            .Should().BeEquivalentTo(
                Role.Standard.Cashier, Role.Standard.Pharmacist,
                Role.Standard.Manager, Role.Standard.Administrator);

    [Fact]
    public void Only_a_cashier_or_the_administrator_can_cash_up_and_close() =>
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.SessionCashUp))
            .Should().BeEquivalentTo(
                Role.Standard.Cashier, Role.Standard.Administrator);

    /// <summary>
    /// Deleting expired and out-of-stock lines from Stock alerts is for
    /// the pharmacist, the assistant, the manager and the administrator
    /// (18 October 2026).
    /// </summary>
    [Fact]
    public void The_pharmacist_assistant_manager_and_administrator_clear_dead_stock() =>
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.StockRemove))
            .Should().BeEquivalentTo(
                Role.Standard.Pharmacist, Role.Standard.PharmacistAssistant,
                Role.Standard.Manager, Role.Standard.Administrator);

    /// <summary>
    /// The pharmacist, the assistant and the administrator keep the
    /// products: the catalogue, the selling price and the stock on hand
    /// (23 October 2026). Stock is corrected with a reason; stocktakes,
    /// write-offs and transfers stay with whoever posts stocktakes.
    /// </summary>
    [Theory]
    [InlineData(Role.Standard.Pharmacist)]
    [InlineData(Role.Standard.PharmacistAssistant)]
    [InlineData(Role.Standard.Administrator)]
    public void The_pharmacist_assistant_and_administrator_edit_products(string role)
    {
        var permissions = As(role);

        permissions.Has(PermissionCode.ProductMaintain).Should().BeTrue();
        permissions.Has(PermissionCode.PriceChange).Should().BeTrue();
        permissions.Has(PermissionCode.StockCorrect).Should().BeTrue();
        ScreenAccess.CanOpen("products", permissions).Should().BeTrue();
    }

    [Fact]
    public void Correcting_stock_from_the_product_is_not_posting_a_stocktake()
    {
        foreach (var role in new[] { Role.Standard.Pharmacist, Role.Standard.PharmacistAssistant })
        {
            As(role).Has(PermissionCode.StockTakePost).Should().BeFalse(role);
            ScreenAccess.CanOpen("stocktake-post", As(role)).Should().BeFalse(role);
        }

        Role.Standard.All.Where(r => As(r).Has(PermissionCode.StockCorrect))
            .Should().BeEquivalentTo(Role.Standard.Pharmacist,
                Role.Standard.PharmacistAssistant, Role.Standard.Administrator);
    }

    /// <summary>
    /// Every role with a till records the money taken out of its drawer:
    /// drawings, lunch, other expenses (23 October 2026).
    /// </summary>
    [Fact]
    public void Everyone_with_a_till_records_money_out_of_the_drawer() =>
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.SessionPayOut))
            .Should().BeEquivalentTo(Role.Standard.All
                .Where(r => As(r).Has(PermissionCode.SaleCreate)));

    /// <summary>
    /// The pharmacist, the assistant, the manager and the administrator
    /// change what a dispensed item is charged (24 October 2026). The
    /// manager does not dispense, so authorises it for whoever is.
    /// </summary>
    [Fact]
    public void Four_roles_adjust_the_charge_when_dispensing()
    {
        Role.Standard.All
            .Where(r => As(r).Has(PermissionCode.DispensingChargeAdjust))
            .Should().BeEquivalentTo(Role.Standard.Administrator,
                Role.Standard.Pharmacist, Role.Standard.PharmacistAssistant,
                Role.Standard.Manager);

        As(Role.Standard.Manager).Has(PermissionCode.ScriptDispense)
            .Should().BeFalse("the manager still does not dispense");
    }

    [Fact]
    public void A_cashier_holds_no_report_permission_at_all()
    {
        var cashier = As(Role.Standard.Cashier);

        new[]
        {
            PermissionCode.ReportOperational, PermissionCode.ReportManagement,
            PermissionCode.ReportFinancial, PermissionCode.ReportClinical
        }.Should().OnlyContain(code => !cashier.Has(code));
    }

    /// <summary>
    /// Every entry on the shell's menu and every home tile is decided by
    /// the policy: a screen nobody has decided about is refused rather
    /// than open to everyone.
    /// </summary>
    [Fact]
    public void Every_menu_entry_on_the_shell_has_an_access_rule()
    {
        using var shell = new frmMain();

        var menu = shell.Controls.OfType<MenuStrip>().Single();
        var keys = MenuKeys(menu.Items).ToList();

        keys.Should().NotBeEmpty();
        keys.Should().BeSubsetOf(ScreenAccess.Keys);
    }

    /// <summary>
    /// A permission a role is granted must exist, or the grant is a typo
    /// that silently denies.
    /// </summary>
    [Fact]
    public void Every_granted_permission_is_a_known_permission()
    {
        foreach (var role in Role.Standard.All)
            StandardRoleGrants.For(role).Should()
                .BeSubsetOf(PermissionCode.All, role);
    }

    private static IEnumerable<string> MenuKeys(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem { HasDropDownItems: true } menu)
            {
                foreach (var key in MenuKeys(menu.DropDownItems))
                    yield return key;
            }
            else if (item.Tag is string key)
            {
                yield return key;
            }
        }
    }
}

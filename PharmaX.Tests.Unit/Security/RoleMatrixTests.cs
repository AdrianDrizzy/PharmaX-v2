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
        { Role.Standard.Manager, "dispensing" },

        { Role.Standard.Pharmacist, "users" },
        { Role.Standard.Pharmacist, "price-changes" },
        { Role.Standard.Pharmacist, "system-setup" },
        { Role.Standard.Pharmacist, "day-end" },

        { Role.Standard.PharmacistAssistant, "users" },
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
        { Role.Standard.Cashier, "system-setup" },
        { Role.Standard.Cashier, "control-variables" },
        { Role.Standard.Cashier, "day-end" },
        { Role.Standard.Cashier, "trial-balance" },

        { Role.Standard.StockController, "users" },
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

using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Tills;
using PharmaX.Persistence.Numbering;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// A fresh installation with no data cannot be logged into, which makes
/// seeding a functional requirement rather than a convenience. Every
/// method is idempotent: safe on every start-up, adds only what is
/// missing, never removes or overwrites.
/// </summary>
public sealed class ReferenceDataSeeder
{
    private readonly PharmaXDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly PharmaXOptions _options;

    public ReferenceDataSeeder(PharmaXDbContext db, IPasswordHasher hasher,
                               PharmaXOptions options)
    {
        _db = db;
        _hasher = hasher;
        _options = options;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SeedAdministratorAsync(ct);
        await SeedDenominationsAsync(ct);
        await SeedFirstNumberBlockAsync(ct);
        await SeedChartOfAccountsAsync(ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The standard chart, so that the first sale of the day has
    /// somewhere to post to.
    /// </summary>
    /// <remarks>
    /// Adds only what is missing and renames nothing. A pharmacy that
    /// has called account 4000 something other than "Sales" has made a
    /// decision, and a seeder that put its own name back every morning
    /// would be overruling it.
    /// </remarks>
    private async Task SeedChartOfAccountsAsync(CancellationToken ct)
    {
        var existing = await _db.LedgerAccounts
            .Select(a => a.Code)
            .ToListAsync(ct);

        foreach (var (code, name, type) in LedgerCode.Standard)
        {
            if (existing.Contains(code, StringComparer.Ordinal)) continue;

            _db.LedgerAccounts.Add(LedgerAccount.Create(code, name, type));
        }
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions
            .Select(p => p.Code)
            .ToListAsync(ct);

        foreach (var code in PermissionCode.All.Except(existing,
                     StringComparer.Ordinal))
            _db.Add(Permission.Create(code));
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var existing = await _db.Roles.ToListAsync(ct);

        foreach (var name in Role.Standard.All)
        {
            var role = existing.Find(r => string.Equals(r.Name, name,
                                                        StringComparison.Ordinal));

            if (role is null)
            {
                role = Role.Create(name, isSystemRole: true);
                _db.Roles.Add(role);
            }

            // Additive, on an existing role as well as a new one. A
            // permission added in a later release would otherwise only
            // ever reach installations seeded after it, and the sites
            // that have been running longest would be the ones missing
            // it. Grant is idempotent, and nothing is revoked here: a
            // site that has narrowed a role keeps its decision.
            foreach (var code in PermissionsFor(name))
                role.Grant(code);
        }
    }

    /// <summary>
    /// The starting grant for each standard role. Deliberately narrow:
    /// widening a role on request is a five-minute conversation, and
    /// narrowing one after staff have used it for a month is not.
    /// </summary>
    private static IEnumerable<string> PermissionsFor(string role) =>
        role switch
        {
            Role.Standard.Administrator => PermissionCode.All,

            Role.Standard.Manager => new[]
            {
                PermissionCode.SaleCreate,
                PermissionCode.SaleDiscountAboveLimit,
                PermissionCode.ReturnAuthorise,
                PermissionCode.SessionCloseOtherUser,
                PermissionCode.ProductRead,
                PermissionCode.PriceChange,
                PermissionCode.StockTakePost,
                PermissionCode.SupplierMaintain,
                PermissionCode.PurchaseOrderRaise,
                PermissionCode.GoodsReceive,
                PermissionCode.ReportOperational,
                PermissionCode.ReportManagement,
                PermissionCode.ReportFinancial,

                // The books are a manager's job. A cashier posting a
                // journal is how a till shortage quietly becomes a
                // rounding adjustment.
                PermissionCode.LedgerRead,
                PermissionCode.LedgerPost
            },

            Role.Standard.Pharmacist => new[]
            {
                PermissionCode.SaleCreate,
                PermissionCode.ScriptAuthorise,
                PermissionCode.ScriptReverse,
                PermissionCode.ProductRead,
                PermissionCode.ReportOperational,
                PermissionCode.ReportClinical
            },

            Role.Standard.PharmacistAssistant => new[]
            {
                PermissionCode.SaleCreate,
                PermissionCode.ProductRead,
                PermissionCode.ReportOperational
            },

            Role.Standard.Cashier => new[]
            {
                PermissionCode.SaleCreate,
                PermissionCode.ProductRead
            },

            Role.Standard.StockController => new[]
            {
                PermissionCode.ProductRead,
                PermissionCode.StockTakePost,
                PermissionCode.SupplierMaintain,
                PermissionCode.PurchaseOrderRaise,
                PermissionCode.GoodsReceive,
                PermissionCode.ReportOperational
            },

            _ => Array.Empty<string>()
        };

    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct)) return;

        var hash = _hasher.Hash("ChangeMe#1");

        var admin = User.Create(
            username: "admin",
            displayName: "Initial Administrator",
            credential: hash,
            mustChangePassword: true);      // enforced at first login

        admin.AssignRole(Role.Standard.Administrator);
        admin.SetDiscountLimit(100m);

        _db.Users.Add(admin);
    }

    /// <summary>
    /// The notes and coins of the currency this installation trades in,
    /// largest first, so the cash-up grid counts down the way a drawer
    /// is actually counted.
    /// </summary>
    /// <remarks>
    /// Keyed on the currency rather than on "are there any rows yet", so
    /// that changing the trading currency in system setup and restarting
    /// gives the till the right drawer. The old currency's rows are left
    /// alone: a declaration already taken against them still resolves.
    /// </remarks>
    private async Task SeedDenominationsAsync(CancellationToken ct)
    {
        var currency = Currencies.ByCode(Money.DefaultCurrency);

        var existing = await _db.Denominations
            .Where(d => d.Currency == currency.Code)
            .Select(d => d.Key)
            .ToListAsync(ct);

        foreach (var denomination in Denomination.For(currency))
        {
            if (!existing.Contains(denomination.Key, StringComparer.Ordinal))
                _db.Denominations.Add(denomination);
        }
    }

    private async Task SeedFirstNumberBlockAsync(CancellationToken ct)
    {
        var till = _options.Workstation.TillNumber;

        // JNL is here because the books now number themselves: a sale
        // finalising takes a SALE number for the receipt and a JNL
        // number for the journal it posts, and a series with no block
        // stops the transaction that needed it.
        foreach (var series in new[]
                 { "SALE", "DISP", "RET", "PAT", "SCR", "GRN", "PO", "STK",
                   "JNL" })
        {
            var exists = await _db.DocumentNumberBlocks
                .AnyAsync(b => b.Series == series
                               && b.WorkstationCode == till, ct);

            if (exists) continue;

            // The first block, so a brand-new workstation can trade before
            // it has ever reached the host (NET-021). Later blocks are
            // issued by the host and are not seeded.
            _db.DocumentNumberBlocks.Add(new DocumentNumberBlock
            {
                Series = series,
                WorkstationCode = till,
                RangeFrom = 1,
                RangeTo = 100_000,
                NextValue = 1
            });
        }
    }
}

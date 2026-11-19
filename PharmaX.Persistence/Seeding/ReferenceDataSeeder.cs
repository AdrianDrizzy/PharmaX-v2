using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
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
        var newPermissions = await SeedPermissionsAsync(ct);
        await SeedRolesAsync(newPermissions, ct);
        await SeedAdministratorAsync(ct);
        await SeedDenominationsAsync(ct);
        await SeedFirstNumberBlockAsync(ct);
        await SeedChartOfAccountsAsync(ct);
        await SeedMedicalAidSocietiesAsync(ct);
        await SeedDosageCodesAsync(ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The medical aid societies a Zimbabwean pharmacy bills, so that a
    /// patient's membership can be recorded on the first day.
    /// </summary>
    /// <remarks>
    /// Adds only codes that are missing, as the chart does: a society
    /// the pharmacy has renamed or retired is its decision to keep.
    /// </remarks>
    /// <summary>
    /// The dosage shorthand every dispensary shares, added where missing
    /// and never over a meaning the pharmacy has changed.
    /// </summary>
    private async Task SeedDosageCodesAsync(CancellationToken ct)
    {
        var existing = await _db.DosageCodes.Select(d => d.Code)
            .ToListAsync(ct);

        foreach (var (code, meaning) in Application.Reference.DosageShorthand.Standard)
        {
            if (existing.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
            _db.DosageCodes.Add(Domain.Reference.DosageCode.Create(code, meaning));
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedMedicalAidSocietiesAsync(CancellationToken ct)
    {
        var existing = await _db.MedicalAidSchemes
            .Select(s => s.Code)
            .ToListAsync(ct);

        foreach (var (code, name, detail) in ZimbabweMedicalAidSocieties.All)
        {
            if (existing.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;

            _db.MedicalAidSchemes.Add(MedicalAidScheme.Create(code, name, detail));
        }
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

    private async Task<IReadOnlyList<string>> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions
            .Select(p => p.Code)
            .ToListAsync(ct);

        var added = PermissionCode.All.Except(existing, StringComparer.Ordinal).ToList();

        foreach (var code in added)
            _db.Add(Permission.Create(code));

        return added;
    }

    private async Task SeedRolesAsync(IReadOnlyList<string> newPermissions, CancellationToken ct)
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

            // shortcut: an edited role does not gain permissions added in
            // a later release, tick those on Role permissions (or drop the
            // mark for a column) if an upgrade must fill them in.
            // A permission new in this release reaches the administrator even
            // on an edited role, so a screen gated on it is never locked to all.
            if (name == Role.Standard.Administrator && existing.Count > 0)
                foreach (var code in newPermissions)
                    role.Grant(code);

            if (role.Has(Role.EditedMark)) continue;

            // Additive until the pharmacy edits the role. A permission
            // added in a later release then reaches sites already running.
            foreach (var code in StandardRoleGrants.For(name))
                role.Grant(code);
        }
    }

    /// The first administrator's sign-in, as installed.
    public const string AdministratorUsername = "admin";

#if PHARMAX_TEST_BUILD
    // A test build only (installer\build.ps1 -TestBuild): a password as
    // easy to type at every PC being tried out, and no change forced at
    // the first sign-in. The pharmacy's own installer is built without it.
    private const string AdministratorPassword = "changeme#1";
    private const bool AdministratorMustChangePassword = false;
#else
    private const string AdministratorPassword = "ChangeMe#1";
    private const bool AdministratorMustChangePassword = true;
#endif

    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct)) return;

        _db.Users.Add(NewAdministrator());
    }

    private User NewAdministrator()
    {
        var admin = User.Create(
            username: AdministratorUsername,
            displayName: "Initial Administrator",
            credential: _hasher.Hash(AdministratorPassword),
            mustChangePassword: AdministratorMustChangePassword); // enforced at first login

        admin.AssignRole(Role.Standard.Administrator);
        admin.SetDiscountLimit(100m);
        return admin;
    }

    /// <summary>
    /// Puts the first administrator back as installed: the starting
    /// password, to be changed at the next sign-in, unlocked, active and
    /// an administrator. Made again if it has gone.
    /// </summary>
    /// <remarks>
    /// For an owner locked out with nobody else who can reset a password:
    /// <c>PharmaX.Desktop.exe --reset-admin</c>, run at the machine.
    /// </remarks>
    public async Task ResetAdministratorAsync(CancellationToken ct = default)
    {
        var admin = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == AdministratorUsername, ct);

        if (admin is null)
        {
            _db.Users.Add(NewAdministrator());
        }
        else
        {
            admin.ChangeCredential(_hasher.Hash(AdministratorPassword),
                                   DateTime.UtcNow);
#if !PHARMAX_TEST_BUILD
            admin.RequirePasswordChange();
#endif
            admin.Unlock();
            admin.Reactivate();
            admin.AssignRole(Role.Standard.Administrator);
        }

        await _db.SaveChangesAsync(ct);
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

        // DocumentSeries.All, never a list repeated here. This method
        // held its own copy and had fallen two series behind it: CLM and
        // ACC were missing, so opening a patient account and raising a
        // medical aid claim both died at "no document numbers left" -
        // the fourth time this exact drift has shipped.
        foreach (var series in DocumentSeries.All)
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// One account per standard role, with a known password, so that the
/// permission and authorisation paths can be walked without an
/// administrator hand-building six users first.
/// </summary>
/// <remarks>
/// Never runs unless Development:SeedTestUsers is explicitly true, which
/// only appsettings.Development.json sets - a file .gitignore excludes
/// and the installer does not write. It also logs a warning every time it
/// finds work to do, so an accidentally enabled flag is visible in the
/// log rather than only in the user list. Delete these accounts before
/// go-live and record that you have: a known credential on a pharmacy
/// till is exactly the kind of finding that turns a good system into an
/// audit problem.
/// </remarks>
public sealed class TestUserSeeder
{
    /// The same password for all six, because typing six different ones
    /// during a test pass is friction with no security benefit: none of
    /// these accounts may exist outside a development machine.
    public const string SharedPassword = "Test#1234";

    private readonly PharmaXDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly PharmaXOptions _options;
    private readonly ILogger<TestUserSeeder> _log;

    public TestUserSeeder(PharmaXDbContext db, IPasswordHasher hasher,
                          PharmaXOptions options,
                          ILogger<TestUserSeeder> log)
    {
        _db = db;
        _hasher = hasher;
        _options = options;
        _log = log;
    }

    /// <summary>
    /// The accounts, and the discount limit each one carries. The limits
    /// differ on purpose: a cashier at 5 per cent is what makes the
    /// supervisor authorisation path reachable, and a manager at 50 is
    /// what makes it satisfiable.
    /// </summary>
    private static readonly (string Username, string DisplayName,
        string Role, decimal DiscountLimit, string? Registration)[] Accounts =
    {
        ("test.admin", "Test Administrator", Role.Standard.Administrator,
            100m, null),
        ("test.manager", "Test Manager", Role.Standard.Manager, 50m, null),
        ("test.pharmacist", "Test Pharmacist", Role.Standard.Pharmacist,
            10m, "P00000"),
        ("test.assistant", "Test Assistant",
            Role.Standard.PharmacistAssistant, 5m, null),
        ("test.cashier", "Test Cashier", Role.Standard.Cashier, 5m, null),
        ("test.stock", "Test Stock Controller",
            Role.Standard.StockController, 0m, null)
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_options.Development.SeedTestUsers) return;

        var existing = await _db.Users
            .Select(u => u.Username)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var wanted = Accounts
            .Where(a => !existing.Contains(a.Username, StringComparer.Ordinal))
            .ToList();

        if (wanted.Count == 0) return;

        _log.LogWarning(
            "Development:SeedTestUsers is on: creating {Count} accounts "
            + "with a known shared password. This must never be enabled "
            + "at a site.", wanted.Count);

        // Hashed once and reused. Each hash carries its own salt, so the
        // stored credentials still differ from one another.
        foreach (var account in wanted)
        {
            var user = User.Create(
                username: account.Username,
                displayName: account.DisplayName,
                credential: _hasher.Hash(SharedPassword),

                // Deliberately not forced: a password change on every
                // one of six accounts would make the flag useless.
                mustChangePassword: false);

            user.AssignRole(account.Role);
            user.SetDiscountLimit(account.DiscountLimit);

            if (account.Registration is not null)
                user.SetRegistrationNumber(account.Registration);

            _db.Users.Add(user);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Security;

/// <summary>
/// The wall clock. Everything that needs the time asks for IClock instead,
/// so a test can decide what "now" is rather than waiting for it.
/// </summary>
public sealed class SystemClock : IClock, IClockCorrection
{
    // How far this PC is behind the database server, measured at sign-in,
    // so every till stamps sales with the same clock.
    private long _offsetTicks;

    public DateTime UtcNow => DateTime.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    // Local, not UTC: an expiry date and a trading day are both local
    // facts, and at 01:00 in Johannesburg the UTC date is yesterday.
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.ToLocalTime());

    public void Correct(TimeSpan offset) => Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
}

/// <summary>
/// Who is signed in on this workstation. One instance for the life of the
/// process, because a Windows Forms application has exactly one operator
/// at the keyboard.
/// </summary>
public sealed class UserSession : ICurrentUser, ISessionContext
{
    private readonly object _gate = new();
    private IReadOnlyList<string> _roles = Array.Empty<string>();

    public Guid Id { get; private set; } = Guid.Empty;
    public string Username { get; private set; } = "";
    public Guid? TillSessionId { get; private set; }
    public decimal DiscountLimitPercent { get; private set; }

    public bool IsAuthenticated => Id != Guid.Empty;

    public bool IsInRole(string role)
    {
        lock (_gate)
            return _roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public void SignIn(Guid userId, string username,
                       IReadOnlyList<string> roles,
                       decimal discountLimitPercent)
    {
        lock (_gate)
        {
            Id = userId;
            Username = username;
            _roles = roles ?? Array.Empty<string>();
            DiscountLimitPercent = discountLimitPercent;
        }
    }

    public void SignOut()
    {
        lock (_gate)
        {
            Id = Guid.Empty;
            Username = "";
            _roles = Array.Empty<string>();
            DiscountLimitPercent = 0m;

            // The till session deliberately survives a sign-out: a shift
            // change hands the same open drawer to the next operator.
        }
    }

    public void AttachTillSession(Guid? tillSessionId)
    {
        lock (_gate) TillSessionId = tillSessionId;
    }
}

/// <summary>
/// Writes audit rows into the current unit of work. Like the outbox it
/// never saves an applied entry: that commits with the change it
/// describes, in the same transaction (SEC-031).
/// </summary>
/// <remarks>
/// A refusal is the exception. It describes something that did not
/// happen, so there is no change for it to commit with: it was added to
/// the unit of work that the refusal itself then abandoned, and so it was
/// never saved - a cashier's refused refund and a stock controller's
/// refused product save left no trace at all (DEF-0026). Refusals are
/// written through a context of their own and committed at once.
/// </remarks>
public sealed class AuditWriter : IAuditWriter
{
    private readonly PharmaXDbContext _db;
    private readonly IDbContextFactory<PharmaXDbContext> _contexts;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;

    public AuditWriter(PharmaXDbContext db,
                       IDbContextFactory<PharmaXDbContext> contexts,
                       ICurrentUser user, IClock clock, PharmaXOptions options)
    {
        _db = db;
        _contexts = contexts;
        _user = user;
        _clock = clock;
        _options = options;
    }

    public void Applied(string action, string entityType, Guid entityId,
                        string? reason = null) =>
        _db.AuditEntries.Add(Entry(action, entityType, entityId, "Applied",
                                   reason));

    public void Refused(string action, string entityType, Guid entityId,
                        string? reason = null)
    {
        var entry = Entry(action, entityType, entityId, "Refused", reason);

        try
        {
            using var own = _contexts.CreateDbContext();
            own.AuditEntries.Add(entry);
            own.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // The refusal still stands; losing its audit row to a database
            // fault must not turn a "no" into a crash.
        }
        catch (InvalidOperationException)
        {
            // As above: the database cannot be reached.
        }
    }

    private AuditEntry Entry(string action, string entityType, Guid entityId,
                             string outcome, string? reason) =>
        AuditEntry.Create(
            occurredAtUtc: _clock.UtcNow,
            userId: _user.Id,
            workstation: _options.Workstation.TillNumber,
            action: action,
            entityType: entityType,
            entityId: entityId,
            outcome: outcome,
            reason: reason);
}

/// <summary>
/// PBKDF2 with HMAC-SHA256. Chosen over anything from a package because
/// PharmaX.Persistence should not take a dependency for one primitive the
/// framework already ships, and because the parameters are visible here
/// rather than buried in a library's defaults.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int CurrentIterations = 210_000;
    private const string Prefix = "pbkdf2-sha256";

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, CurrentIterations);

        return string.Join('$', Prefix, CurrentIterations,
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string encodedHash)
    {
        if (!TryParse(encodedHash, out var iterations, out var salt,
                      out var expected))
            return false;

        var actual = Derive(password, salt, iterations);

        // Fixed-time comparison: a hasher that returns early on the first
        // wrong byte leaks how much of the hash was right.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encodedHash) =>
        !TryParse(encodedHash, out var iterations, out _, out _)
        || iterations < CurrentIterations;

    private static byte[] Derive(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations,
                                     HashAlgorithmName.SHA256, HashBytes);

    private static bool TryParse(string encodedHash, out int iterations,
                                 out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(encodedHash)) return false;

        var parts = encodedHash.Split('$');

        if (parts.Length != 4
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out iterations))
            return false;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return true;
        }
        catch (FormatException)
        {
            // A credential that is not a hash this class produced. Treated
            // as a failed verification, never as an exception at a till.
            return false;
        }
    }
}

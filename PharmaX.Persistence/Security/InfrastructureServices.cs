using System.Security.Cryptography;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Security;

/// <summary>
/// The wall clock. Everything that needs the time asks for IClock instead,
/// so a test can decide what "now" is rather than waiting for it.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    // Local, not UTC: an expiry date and a trading day are both local
    // facts, and at 01:00 in Johannesburg the UTC date is yesterday.
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
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
/// never saves: the entry commits with the change it describes, in the
/// same transaction (SEC-031).
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly PharmaXDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;
    
    public AuditWriter(PharmaXDbContext db, ICurrentUser user, IClock clock,
                       PharmaXOptions options)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _options = options;
    }

    public void Applied(string action, string entityType, Guid entityId,
                        string? reason = null) =>
        Write(action, entityType, entityId, "Applied", reason);

    public void Refused(string action, string entityType, Guid entityId,
                        string? reason = null) =>
        Write(action, entityType, entityId, "Refused", reason);

    private void Write(string action, string entityType, Guid entityId,
                       string outcome, string? reason) =>
        _db.AuditEntries.Add(AuditEntry.Create(
            occurredAtUtc: _clock.UtcNow,
            userId: _user.Id,
            workstation: _options.Workstation.TillNumber,
            action: action,
            entityType: entityType,
            entityId: entityId,
            outcome: outcome,
            reason: reason));
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

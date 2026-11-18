using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Security;

public enum PermissionOutcome { Granted, Denied, RequiresSupervisor }

public interface IPermissionService
{
    PermissionOutcome Check(string permissionCode);
    void Demand(string permissionCode);

    /// <summary>
    /// Whether a particular user holds a permission: the supervisor or
    /// pharmacist who has just typed their own credentials in at this
    /// till, as opposed to the operator signed in at it.
    /// </summary>
    bool UserHolds(Guid userId, string permissionCode);

    /// <summary>
    /// Demands the permission of the operator, or - where the operator
    /// needs a supervisor for it - of the person who authorised it.
    /// </summary>
    /// <remarks>
    /// The check a supervisor-authorised action needs. Demand alone asks
    /// the operator, and an operator who needed a supervisor is by
    /// definition one who does not hold the permission: that is how a
    /// cashier's refund was refused after the manager had already typed
    /// their password in to authorise it. Here the operator's own grant
    /// is enough, and otherwise the authoriser must be someone else,
    /// must hold the permission, and must have been asked for.
    /// </remarks>
    void DemandOrAuthorised(string permissionCode, Guid? authoriserId);
}

public sealed class PermissionService : IPermissionService
{
    private readonly ICurrentUser _user;
    private readonly IPermissionRepository _permissions;
    private readonly IAuditWriter _audit;
    private readonly Licensing.ILicenceGate? _licence;

    public const string ReadOnlyMessage =
        "PharmaX is read-only until the membership is renewed.";

    public PermissionService(ICurrentUser user,
                             IPermissionRepository permissions,
                             IAuditWriter audit,
                             Licensing.ILicenceGate? licence = null)
    {
        _user = user;
        _permissions = permissions;
        _audit = audit;
        _licence = licence;
    }

    private bool Lapsed(string permissionCode) =>
        _licence is not null && !_licence.Allows(permissionCode);

    public PermissionOutcome Check(string permissionCode)
    {
        // Not RequiresSupervisor: no supervisor can override a lapsed membership.
        if (Lapsed(permissionCode)) return PermissionOutcome.Denied;

        if (_permissions.UserHas(_user.Id, permissionCode))
            return PermissionOutcome.Granted;

        return _permissions.IsSupervisorEscalatable(permissionCode)
            ? PermissionOutcome.RequiresSupervisor
            : PermissionOutcome.Denied;
    }

    public void Demand(string permissionCode)
    {
        if (Check(permissionCode) == PermissionOutcome.Granted) return;

        _audit.Refused(permissionCode, "Permission", _user.Id);

        throw new PermissionDeniedException(Lapsed(permissionCode)
            ? ReadOnlyMessage
            : "You do not have permission for this action.");
    }

    public bool UserHolds(Guid userId, string permissionCode) =>
        userId != Guid.Empty && !Lapsed(permissionCode)
        && _permissions.UserHas(userId, permissionCode);

    public void DemandOrAuthorised(string permissionCode, Guid? authoriserId)
    {
        var outcome = Check(permissionCode);

        if (outcome == PermissionOutcome.Granted) return;

        if (outcome == PermissionOutcome.RequiresSupervisor)
        {
            if (authoriserId is not Guid authoriser)
            {
                _audit.Refused(permissionCode, "Permission", _user.Id,
                    "No supervisor authorisation was given.");

                throw new PermissionDeniedException(
                    "A supervisor must authorise this.");
            }

            // Authorising yourself is not a second pair of eyes.
            if (authoriser == _user.Id)
            {
                _audit.Refused(permissionCode, "Permission", _user.Id,
                    "The operator tried to authorise their own action.");

                throw new PermissionDeniedException(
                    "You cannot authorise your own action. Ask a "
                    + "supervisor.");
            }

            if (UserHolds(authoriser, permissionCode)) return;

            _audit.Refused(permissionCode, "Permission", authoriser,
                "The authoriser does not hold this permission.");

            throw new PermissionDeniedException(
                "The person who authorised this does not have permission "
                + "to authorise it.");
        }

        _audit.Refused(permissionCode, "Permission", _user.Id);

        throw new PermissionDeniedException(
            "You do not have permission for this action.");
    }
}

public static class PermissionServiceExtensions
{
    /// True where the signed-in user holds the permission outright.
    public static bool Has(this IPermissionService permissions,
                           string permissionCode)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Check(permissionCode) == PermissionOutcome.Granted;
    }

    /// True where the user holds any one of the permissions outright.
    public static bool HasAny(this IPermissionService permissions,
                              params string[] permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(permissionCodes);
        return Array.Exists(permissionCodes, permissions.Has);
    }

    /// <summary>
    /// Which part of the catalogue this user may see: all of it, or only
    /// what sells over the counter.
    /// </summary>
    public static Repositories.ProductScope ProductScope(
        this IPermissionService permissions) =>
        permissions.Has(PermissionCode.ProductDispensary)
            ? Repositories.ProductScope.All
            : Repositories.ProductScope.OverTheCounter;
}

public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message) : base(message) { }

    public PermissionDeniedException(string message, Exception inner)
        : base(message, inner) { }

    public PermissionDeniedException() { }
}

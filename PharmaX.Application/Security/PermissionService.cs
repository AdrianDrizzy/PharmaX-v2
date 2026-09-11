using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;

namespace PharmaX.Application.Security;

public enum PermissionOutcome { Granted, Denied, RequiresSupervisor }

public interface IPermissionService
{
    PermissionOutcome Check(string permissionCode);
    void Demand(string permissionCode);
}

public sealed class PermissionService : IPermissionService
{
    private readonly ICurrentUser _user;
    private readonly IPermissionRepository _permissions;
    private readonly IAuditWriter _audit;

    public PermissionService(ICurrentUser user,
                             IPermissionRepository permissions,
                             IAuditWriter audit)
    {
        _user = user;
        _permissions = permissions;
        _audit = audit;
    }

    public PermissionOutcome Check(string permissionCode)
    {
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

        throw new PermissionDeniedException(
            "You do not have permission for this action.");
    }
}

public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message) : base(message) { }

    public PermissionDeniedException(string message, Exception inner)
        : base(message, inner) { }

    public PermissionDeniedException() { }
}

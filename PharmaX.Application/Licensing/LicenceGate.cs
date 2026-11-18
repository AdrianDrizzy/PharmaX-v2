using PharmaX.Domain.Security;

namespace PharmaX.Application.Licensing;

/// <summary>
/// The membership as it stands now, held in memory so the permission check
/// - which every screen and service goes through - can answer at once.
/// </summary>
public interface ILicenceGate
{
    LicenceSnapshot Current { get; }

    void Update(LicenceSnapshot snapshot);

    bool Allows(string permissionCode);
}

public sealed class LicenceGate : ILicenceGate
{
    // Read-only still looks things up, runs reports, and lets an
    // administrator enter a code and take a backup.
    private static readonly HashSet<string> ReadOnlyAllowed = new(StringComparer.Ordinal)
    {
        PermissionCode.ProductRead,
        PermissionCode.ProductDispensary,
        PermissionCode.ReportOperational,
        PermissionCode.ReportManagement,
        PermissionCode.ReportFinancial,
        PermissionCode.ReportClinical,
        PermissionCode.LedgerRead,
        PermissionCode.SettingsChange,
        PermissionCode.Backup,
        // Opens patient look-up; PatientService refuses the save itself.
        PermissionCode.PatientMaintain,
    };

    // Nothing is blocked until the membership has been read.
    public LicenceSnapshot Current { get; private set; } =
        new(LicenceStatus.Active, null, null, false);

    public void Update(LicenceSnapshot snapshot) => Current = snapshot;

    public bool Allows(string permissionCode) =>
        !Current.IsReadOnly || ReadOnlyAllowed.Contains(permissionCode);
}

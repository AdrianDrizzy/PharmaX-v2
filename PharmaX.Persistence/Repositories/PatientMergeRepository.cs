using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Patients;

namespace PharmaX.Persistence.Repositories;

/// <summary>
/// Moves the clinical history attached to one patient file onto
/// another.
/// </summary>
/// <remarks>
/// The rows are loaded and changed through their aggregates rather than
/// updated in bulk with ExecuteUpdate. Bulk update goes straight to the
/// database and past the change tracker, which would take the audit and
/// row-version interceptors with it - and a merge is exactly the
/// operation whose audit trail matters.
/// </remarks>
public sealed class PatientMergeRepository : IPatientMergeRepository
{
    private readonly PharmaXDbContext _db;

    public PatientMergeRepository(PharmaXDbContext db) => _db = db;

    public async Task<int> CountScriptsAsync(Guid patientId,
        CancellationToken ct = default) =>
        await _db.Scripts.CountAsync(s => s.PatientId == patientId, ct);

    public async Task<int> CountDispensingsAsync(Guid patientId,
        CancellationToken ct = default) =>
        await _db.Dispensings.CountAsync(d => d.PatientId == patientId, ct);

    public async Task<(int Scripts, int Dispensings)> ReassignAsync(
        Guid fromPatientId, Guid toPatientId, CancellationToken ct = default)
    {
        var scripts = await _db.Scripts
            .Where(s => s.PatientId == fromPatientId)
            .ToListAsync(ct);

        foreach (var script in scripts) script.ReassignTo(toPatientId);

        var dispensings = await _db.Dispensings
            .Where(d => d.PatientId == fromPatientId)
            .ToListAsync(ct);

        foreach (var dispensing in dispensings)
            dispensing.ReassignTo(toPatientId);

        return (scripts.Count, dispensings.Count);
    }
}

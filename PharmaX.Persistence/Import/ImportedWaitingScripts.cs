using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Dispensing;

namespace PharmaX.Persistence.Import;

/// <summary>
/// Takes prescriptions the previous system left on Scripts to Collect
/// off that queue. The dispensing is kept, so the patient's history
/// still shows it.
/// </summary>
/// <remarks>
/// A number PharmaX issued looks like D-001-000001 and may still be
/// waiting to be paid, so it is left. Anything else waiting was brought
/// across with no till receipt. The flag means a later start does not
/// read that queue again. Two tills upgrading together can both try;
/// the second's save loses, sees the flag, and stops.
/// </remarks>
public static class ImportedWaitingScripts
{
    public const string DoneKey = "dispensing.imported-waiting-cleared";

    public static async Task<int> ClearOnceAsync(PharmaXDbContext db,
        IPharmacySettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(settings);

        if (await settings.GetAsync(DoneKey, ct) == "1")
            return 0;

        var broughtAcross = await db.Dispensings
            .Include(d => d.Lines)
            .Where(d => d.Status == DispensingStatus.Issued)
            .Where(d => d.SaleId == null && d.CollectedAtUtc == null)
            .Where(d => d.DocumentNumber != null
                        && !d.DocumentNumber.StartsWith("D-"))
            .ToListAsync(ct);

        var cleared = 0;

        foreach (var dispensing in broughtAcross)
        {
            // Nothing to pay was never in the queue. Marking it collected
            // would hide a later correction that does ask the patient to pay.
            if (!dispensing.AwaitsCollection) continue;

            dispensing.SettledBeforePharmaX(
                dispensing.IssuedAtUtc ?? DateTime.UtcNow);
            cleared++;
        }

        try
        {
            // Saves the dispensings with the flag: one commit.
            await settings.SetAsync(DoneKey, "1", ct);
        }
        catch (DbUpdateException)
        {
            // The other till committed the same rows, or inserted the flag
            // first. A failure that left the flag unset is a real one.
            db.ChangeTracker.Clear();
            if (await settings.GetAsync(DoneKey, ct) == "1")
                return 0;

            throw;
        }

        return cleared;
    }
}

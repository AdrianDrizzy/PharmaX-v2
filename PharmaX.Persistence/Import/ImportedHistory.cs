using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence.Import;

/// <summary>
/// Whether this database holds trading brought across from another
/// system.
/// </summary>
/// <remarks>
/// A sale or dispensing PharmaX made carries one of its own numbers,
/// S-001-000001 or D-001-000001. One brought across keeps the old
/// system's receipt or script number, which has no such prefix - the same
/// rule <see cref="ImportedWaitingScripts"/> goes by.
/// </remarks>
public static class ImportedHistory
{
    public static async Task<bool> ExistsAsync(PharmaXDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var sale = DocumentSeries.PrefixOf(DocumentSeries.Sale) + "-";
        var dispensing = DocumentSeries.PrefixOf(DocumentSeries.Dispensing) + "-";

        // A draft sale has no number yet: NULL in the table, not imported.
        return await db.Sales.AnyAsync(s => s.DocumentNumber != null && s.DocumentNumber != ""
                                            && !s.DocumentNumber.StartsWith(sale), ct)
                   .ConfigureAwait(false)
               || await db.Dispensings.AnyAsync(d => d.DocumentNumber != null && d.DocumentNumber != ""
                                                     && !d.DocumentNumber.StartsWith(dispensing), ct)
                   .ConfigureAwait(false);
    }
}

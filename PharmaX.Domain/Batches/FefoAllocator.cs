using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Batches;

public sealed record Allocation(Guid BatchId, DateOnly Expiry, decimal Quantity);

public static class FefoAllocator
{
    /// <param name="unitsPerPack">
    /// How many of the units <paramref name="required"/> is counted in
    /// one pack on the shelf holds. Batches are counted in packs; a
    /// dispensing in tablets passes the pack size, and the allocations
    /// come back in tablets.
    /// </param>
    public static IReadOnlyList<Allocation> Allocate(
        IEnumerable<Batch> batches, decimal required, DateOnly today,
        decimal unitsPerPack = 1m)
    {
        ArgumentNullException.ThrowIfNull(batches);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(unitsPerPack, 0m);

        var candidates = batches
            .Where(b => b.Status == BatchStatus.Available)
            .Where(b => b.QuantityOnHand > 0m)
            .Where(b => b.ExpiryDate >= today)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.ReceivedAtUtc)
            .ToList();

        var result = new List<Allocation>();
        var outstanding = required;

        foreach (var batch in candidates)
        {
            if (outstanding <= 0m) break;

            var take = Math.Min(batch.QuantityOnHand * unitsPerPack,
                                outstanding);
            result.Add(new Allocation(batch.Id, batch.ExpiryDate, take));
            outstanding -= take;
        }

        if (outstanding > 0m)
            throw new DomainException(
                $"Insufficient unexpired stock: {outstanding} short.");

        return result;
    }
}

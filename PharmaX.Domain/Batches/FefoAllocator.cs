using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Batches;

public sealed record Allocation(Guid BatchId, DateOnly Expiry, decimal Quantity);

public static class FefoAllocator
{
    public static IReadOnlyList<Allocation> Allocate(
        IEnumerable<Batch> batches, decimal required, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(batches);

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

            var take = Math.Min(batch.QuantityOnHand, outstanding);
            result.Add(new Allocation(batch.Id, batch.ExpiryDate, take));
            outstanding -= take;
        }

        if (outstanding > 0m)
            throw new DomainException(
                $"Insufficient unexpired stock: {outstanding} short.");

        return result;
    }
}

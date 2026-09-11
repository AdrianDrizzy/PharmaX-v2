namespace PharmaX.Domain.Batches;

/// <summary>
/// Only Available batches are candidates for FEFO allocation. Everything
/// else is physically present but not sellable, and the distinction has to
/// be in the data rather than in someone's head.
/// </summary>
public enum BatchStatus
{
    Available = 1,
    Quarantined = 2,
    Expired = 3,
    Recalled = 4,
    Disposed = 5
}

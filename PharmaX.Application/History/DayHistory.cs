using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.History;

/// <summary>Which day's records the history screen is showing.</summary>
public enum HistorySubject
{
    Received,
    Orders,
    Collected,
    Dispensed,
    Patients,
    Returns,
    Products,
    Sales,
    Accounts
}

/// <summary>
/// One row of a day's history. A document is the transaction; an item
/// row under it is a product, or another fact that belongs to it.
/// </summary>
/// <param name="When">The local date and time, to the second.</param>
/// <param name="Amount">Money, already formatted, where the row has any.</param>
/// <param name="IsItem">True on a product or detail row under a document.</param>
/// <param name="Id">On a document, the record its own window opens: the
/// script, patient or product.</param>
/// <param name="OwnerId">On a document, whose it is: the patient, supplier
/// or account.</param>
public sealed record HistoryLine(string Reference, string Detail,
    string? When = null, string? Amount = null, bool IsItem = false,
    Guid? Id = null, Guid? OwnerId = null);

public interface IDayHistoryStore
{
    Task<IReadOnlyList<HistoryLine>> ForAsync(HistorySubject subject,
        DateOnly day, CancellationToken ct = default);
}

public interface IDayHistory
{
    Task<Result<IReadOnlyList<HistoryLine>>> ForAsync(HistorySubject subject,
        DateOnly day, CancellationToken ct = default);
}

public sealed class DayHistory : IDayHistory
{
    private readonly IDayHistoryStore _store;
    private readonly IPermissionService _permissions;

    public DayHistory(IDayHistoryStore store, IPermissionService permissions)
    {
        _store = store;
        _permissions = permissions;
    }

    public async Task<Result<IReadOnlyList<HistoryLine>>> ForAsync(
        HistorySubject subject, DateOnly day, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.HistoryView);
        var lines = await _store.ForAsync(subject, day, ct);
        return Result<IReadOnlyList<HistoryLine>>.Ok(lines);
    }
}

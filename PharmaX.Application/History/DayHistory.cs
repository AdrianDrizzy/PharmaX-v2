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

public sealed record HistoryLine(string Reference, string Detail);

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

using PharmaX.Application.Abstractions;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Reference;

/// <summary>
/// Reads and writes the four maintained lists. One repository call per
/// kind so a screen never learns which table it is on.
/// </summary>
public interface IReferenceRepository
{
    Task<IReadOnlyList<ReferenceEntry>> ListAsync(ReferenceKind kind,
        string term, bool includeInactive, CancellationToken ct = default);

    Task<ReferenceEntry?> GetAsync(ReferenceKind kind, Guid id,
        CancellationToken ct = default);

    Task<ReferenceEntry?> FindByCodeAsync(ReferenceKind kind, string code,
        CancellationToken ct = default);

    Task AddAsync(ReferenceKind kind, ReferenceEntry entry,
        CancellationToken ct = default);
}

public interface IReferenceDataService
{
    Task<Result<IReadOnlyList<ReferenceItemDto>>> ListAsync(
        ReferenceKind kind, string term = "", bool includeInactive = true,
        CancellationToken ct = default);

    Task<Result<ReferenceItemDto>> GetAsync(ReferenceKind kind, Guid id,
        CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ReferenceKind kind, ReferenceEditDto edit,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(ReferenceKind kind, Guid id,
        ReferenceEditDto edit, CancellationToken ct = default);

    Task<Result> SetActiveAsync(ReferenceKind kind, Guid id, bool active,
        CancellationToken ct = default);
}

public sealed class ReferenceDataService : IReferenceDataService
{
    private readonly IReferenceRepository _repository;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ReferenceDataService(IReferenceRepository repository,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow)
    {
        _repository = repository;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<IReadOnlyList<ReferenceItemDto>>> ListAsync(
        ReferenceKind kind, string term = "", bool includeInactive = true,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var entries = await _repository.ListAsync(kind, term, includeInactive,
                                                  ct);

        IReadOnlyList<ReferenceItemDto> items =
            entries.Select(ReferenceItemDto.From).ToList();

        return Result<IReadOnlyList<ReferenceItemDto>>.Ok(items);
    }

    public async Task<Result<ReferenceItemDto>> GetAsync(ReferenceKind kind,
        Guid id, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var entry = await _repository.GetAsync(kind, id, ct);

        return entry is null
            ? Result<ReferenceItemDto>.Fail("REF-000", NotFound(kind))
            : Result<ReferenceItemDto>.Ok(ReferenceItemDto.From(entry));
    }

    public async Task<Result<Guid>> CreateAsync(ReferenceKind kind,
        ReferenceEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // Maintaining a list is a supervisor's job, not a cashier's.
        _permissions.Demand(PermissionCode.ReferenceMaintain);

        var clash = await _repository.FindByCodeAsync(kind, edit.Code, ct);

        if (clash is not null)
            return Result<Guid>.Fail("REF-002",
                $"Code {edit.Code.Trim().ToUpperInvariant()} is already in "
                + $"use by {clash.Name}.");

        var entry = New(kind, edit);

        await _repository.AddAsync(kind, entry, ct);

        _audit.Applied($"ref.{Slug(kind)}.create", kind.ToString(), entry.Id);
        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(entry.Id);
    }

    public async Task<Result> UpdateAsync(ReferenceKind kind, Guid id,
        ReferenceEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.ReferenceMaintain);

        var entry = await _repository.GetAsync(kind, id, ct);
        if (entry is null) return Result.Fail("REF-000", NotFound(kind));

        // The code is what every other record points at by name, so it
        // is not editable once the entry exists. Renaming is.
        entry.Rename(edit.Name, edit.Detail);

        _audit.Applied($"ref.{Slug(kind)}.update", kind.ToString(), entry.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> SetActiveAsync(ReferenceKind kind, Guid id,
        bool active, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ReferenceMaintain);

        var entry = await _repository.GetAsync(kind, id, ct);
        if (entry is null) return Result.Fail("REF-000", NotFound(kind));

        // Retired, never deleted: a department removed outright takes
        // every sale that referenced it off the reports already printed.
        if (active) entry.Reactivate(); else entry.Deactivate();

        _audit.Applied(
            $"ref.{Slug(kind)}.{(active ? "reactivate" : "deactivate")}",
            kind.ToString(), entry.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private static ReferenceEntry New(ReferenceKind kind,
        ReferenceEditDto e) => kind switch
    {
        ReferenceKind.Department =>
            Department.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.Category =>
            Category.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.Manufacturer =>
            Manufacturer.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.Branch =>
            Branch.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.MedicalAidScheme =>
            MedicalAidScheme.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.Prescriber =>
            Prescriber.Create(e.Code, e.Name, e.Detail),
        ReferenceKind.DosageCode =>
            DosageCode.Create(e.Code, e.Name, e.Detail),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string NotFound(ReferenceKind kind) =>
        $"That {ReferenceLabels.For(kind).Singular} no longer exists.";

    private static string Slug(ReferenceKind kind) =>
        kind.ToString().ToLowerInvariant();
}

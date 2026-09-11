using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Patients;

/// <summary>
/// What merging two files would move, worked out before anything is
/// moved so the pharmacist can see it and say no.
/// </summary>
public sealed record MergePreviewDto(
    Guid KeepId, string KeepFileNumber, string KeepName,
    Guid MergeId, string MergeFileNumber, string MergeName,
    int Scripts, int Dispensings, bool MovesAccount,
    bool KeepAlreadyHasAccount);

public interface IPatientMergeRepository
{
    Task<int> CountScriptsAsync(Guid patientId, CancellationToken ct = default);

    Task<int> CountDispensingsAsync(Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Moves every script and dispensing from one file to the other and
    /// returns how many of each moved.
    /// </summary>
    Task<(int Scripts, int Dispensings)> ReassignAsync(Guid fromPatientId,
        Guid toPatientId, CancellationToken ct = default);
}

public interface IPatientMergeService
{
    Task<Result<MergePreviewDto>> PreviewAsync(Guid keepId, Guid mergeId,
        CancellationToken ct = default);

    /// <summary>
    /// Moves the duplicate's history onto the file being kept and
    /// retires the duplicate.
    /// </summary>
    Task<Result<MergePreviewDto>> MergeAsync(Guid keepId, Guid mergeId,
        string reason, CancellationToken ct = default);
}

/// <summary>
/// Merges two patient files that turned out to be the same person.
/// </summary>
/// <remarks>
/// The duplicate is retired, never deleted. Its file number appears on
/// scripts already printed and on account lines already sent out, and a
/// file number that resolves to nothing is worse than one that resolves
/// to a retired record saying where its history went.
/// </remarks>
public sealed class PatientMergeService : IPatientMergeService
{
    private readonly IPatientRepository _patients;
    private readonly IPatientMergeRepository _merge;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public PatientMergeService(IPatientRepository patients,
        IPatientMergeRepository merge, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow)
    {
        _patients = patients;
        _merge = merge;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<MergePreviewDto>> PreviewAsync(Guid keepId,
        Guid mergeId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        return await BuildPreviewAsync(keepId, mergeId, ct);
    }

    public async Task<Result<MergePreviewDto>> MergeAsync(Guid keepId,
        Guid mergeId, string reason, CancellationToken ct = default)
    {
        // Merging rewrites clinical history across two files, so it sits
        // behind the same permission as authorising a script.
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        if (string.IsNullOrWhiteSpace(reason))
            return Result<MergePreviewDto>.Fail("PAT-013",
                "A merge requires a reason: it is not reversible from the "
                + "screen that did it.");

        var preview = await BuildPreviewAsync(keepId, mergeId, ct);
        if (preview.IsFailure) return preview;

        var keep = await _patients.GetAsync(keepId, ct);
        var duplicate = await _patients.GetAsync(mergeId, ct);

        if (keep is null || duplicate is null)
            return Result<MergePreviewDto>.Fail("PAT-000",
                "That patient no longer exists.");

        var moved = await _merge.ReassignAsync(mergeId, keepId, ct);

        // The account follows only where the file being kept has none of
        // its own. Two accounts are two balances, and silently picking
        // one would lose money that somebody is owed.
        if (duplicate.AccountId is Guid account && keep.AccountId is null)
            keep.LinkAccount(account);

        duplicate.Deactivate();

        _audit.Applied("pat.merge", "Patient", keepId,
            $"{duplicate.FileNumber} merged into {keep.FileNumber}: "
            + $"{moved.Scripts} scripts, {moved.Dispensings} dispensings. "
            + reason.Trim());

        // Recorded against the retired file too, so looking up the old
        // file number explains where its history went.
        _audit.Applied("pat.merged-away", "Patient", mergeId,
            $"Merged into {keep.FileNumber}. {reason.Trim()}");

        await _uow.SaveChangesAsync(ct);

        return Result<MergePreviewDto>.Ok(preview.Value with
        {
            Scripts = moved.Scripts,
            Dispensings = moved.Dispensings
        });
    }

    private async Task<Result<MergePreviewDto>> BuildPreviewAsync(Guid keepId,
        Guid mergeId, CancellationToken ct)
    {
        if (keepId == mergeId)
            return Result<MergePreviewDto>.Fail("PAT-011",
                "Choose two different files.");

        var keep = await _patients.GetAsync(keepId, ct);
        if (keep is null)
            return Result<MergePreviewDto>.Fail("PAT-000",
                "The file to keep no longer exists.");

        var duplicate = await _patients.GetAsync(mergeId, ct);
        if (duplicate is null)
            return Result<MergePreviewDto>.Fail("PAT-000",
                "The duplicate file no longer exists.");

        if (!duplicate.IsActive)
            return Result<MergePreviewDto>.Fail("PAT-012",
                $"File {duplicate.FileNumber} has already been merged or "
                + "retired.");

        return Result<MergePreviewDto>.Ok(new MergePreviewDto(
            keep.Id, keep.FileNumber, keep.FullName,
            duplicate.Id, duplicate.FileNumber, duplicate.FullName,
            await _merge.CountScriptsAsync(mergeId, ct),
            await _merge.CountDispensingsAsync(mergeId, ct),
            MovesAccount: duplicate.AccountId is not null
                          && keep.AccountId is null,
            KeepAlreadyHasAccount: keep.AccountId is not null
                                   && duplicate.AccountId is not null));
    }
}

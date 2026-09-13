using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Patients;

/// <summary>
/// One scan in a patient's file, as a screen needs to show it.
/// </summary>
/// <param name="IsOnThisWorkstation">
/// False where the row has reached this till but the image has not.
/// The bytes do not sync, so a scan filed at till 2 is a name and a
/// date at till 1 until someone fetches it.
/// </param>
public sealed record AttachmentDto(Guid Id, Guid PatientId, Guid? ScriptId,
    AttachmentKind Kind, string Description, DateTime CapturedAtUtc,
    long SizeBytes, string ContentType, bool IsOnThisWorkstation)
{
    public string SizeForDisplay => SizeBytes < 1024L * 1024L
        ? $"{SizeBytes / 1024d:0} KB"
        : $"{SizeBytes / (1024d * 1024d):0.0} MB";
}

public interface IAttachmentService
{
    Task<Result<IReadOnlyList<AttachmentDto>>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Files a scan against a patient, optionally against a script.
    /// </summary>
    Task<Result<Guid>> AddAsync(Guid patientId, string sourcePath,
        AttachmentKind kind, string description, Guid? scriptId = null,
        CancellationToken ct = default);

    /// The full path of a stored scan, for opening or previewing it.
    Task<Result<string>> LocateAsync(Guid attachmentId,
        CancellationToken ct = default);

    Task<Result> RemoveAsync(Guid attachmentId,
                             CancellationToken ct = default);
}

public sealed class AttachmentService : IAttachmentService
{
    private readonly IPatientRepository _patients;
    private readonly IAttachmentStore _store;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AttachmentService(IPatientRepository patients,
        IAttachmentStore store, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _patients = patients;
        _store = store;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ForPatientAsync(
        Guid patientId, CancellationToken ct = default)
    {
        // A patient's scans are clinical information, held to the same
        // permission as the rest of the file.
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var stored = await _patients.AttachmentsAsync(patientId, ct);

        return Result<IReadOnlyList<AttachmentDto>>.Ok(
            stored.Select(Describe).ToList());
    }

    public async Task<Result<Guid>> AddAsync(Guid patientId,
        string sourcePath, AttachmentKind kind, string description,
        Guid? scriptId = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var patient = await _patients.GetAsync(patientId, ct);

        if (patient is null)
            return Result<Guid>.Fail("PAT-000", "Patient not found.");

        StoredFile stored;

        try
        {
            stored = await _store.SaveAsync(patientId, sourcePath, ct);
        }
        catch (FileNotFoundException)
        {
            return Result<Guid>.Fail("PAT-041",
                "That file is no longer where it was chosen from.");
        }
        catch (IOException ex)
        {
            return Result<Guid>.Fail("PAT-042",
                "The file could not be filed: " + ex.Message);
        }

        var attachment = PatientAttachment.File(
            patientId: patientId,
            kind: kind,
            description: description,
            relativePath: stored.RelativePath,
            contentType: stored.ContentType,
            sizeBytes: stored.SizeBytes,
            capturedAtUtc: _clock.UtcNow,
            capturedByUserId: _user.Id,
            scriptId: scriptId);

        await _patients.AddAttachmentAsync(attachment, ct);

        _audit.Applied("patient.attachment.add", "PatientAttachment",
                       attachment.Id);

        // The row syncs; the image does not. The host learns that a scan
        // exists and where it was taken, which is what a later request
        // for it needs to know.
        _outbox.Append("PatientAttachment", attachment.Id,
                       $"{{\"patientId\":\"{patientId}\"}}");

        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(attachment.Id);
    }

    public async Task<Result<string>> LocateAsync(Guid attachmentId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var attachment = await _patients.AttachmentAsync(attachmentId, ct);

        if (attachment is null)
            return Result<string>.Fail("PAT-043", "Attachment not found.");

        if (!_store.Exists(attachment.RelativePath))
            return Result<string>.Fail("PAT-044",
                "This scan was filed on another workstation and its image "
                + "is not on this one.");

        return Result<string>.Ok(_store.Resolve(attachment.RelativePath));
    }

    public async Task<Result> RemoveAsync(Guid attachmentId,
        CancellationToken ct = default)
    {
        // Unfiling a clinical document is a supervisor's act, not a
        // dispenser's.
        _permissions.Demand(PermissionCode.UserRoleAssign);

        var attachment = await _patients.AttachmentAsync(attachmentId, ct);

        if (attachment is null)
            return Result.Fail("PAT-043", "Attachment not found.");

        _patients.RemoveAttachment(attachment);
        _audit.Applied("patient.attachment.remove", "PatientAttachment",
                       attachmentId);

        await _uow.SaveChangesAsync(ct);

        // Only once the row is gone. A file deleted before a failed save
        // would leave a record pointing at nothing.
        _store.Delete(attachment.RelativePath);

        return Result.Ok();
    }

    private AttachmentDto Describe(PatientAttachment a) => new(
        a.Id, a.PatientId, a.ScriptId, a.Kind, a.Description,
        a.CapturedAtUtc, a.SizeBytes, a.ContentType,
        _store.Exists(a.RelativePath));
}

using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// One issued dispensing, as the label screen lists it.
/// </summary>
public sealed record DispensedItemDto(Guid DispensingId, string ScriptNumber,
    string DocumentNumber, string PatientName, DateOnly DispensedOn,
    int Labels);

public interface ILabelReprintService
{
    /// <summary>
    /// What has been dispensed to a patient and can therefore have its
    /// labels printed again.
    /// </summary>
    Task<Result<IReadOnlyList<DispensedItemDto>>> ForPatientAsync(
        Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the labels for an issued dispensing so they can be
    /// printed again - the bottle went missing, or the printer jammed on
    /// the first attempt.
    /// </summary>
    Task<Result<IReadOnlyList<LabelDto>>> BuildAsync(Guid dispensingId,
        CancellationToken ct = default);

    /// <summary>
    /// Prints them, and says plainly when there is no label printer
    /// rather than reporting success to a machine that printed nothing.
    /// </summary>
    Task<Result<int>> ReprintAsync(Guid dispensingId,
        CancellationToken ct = default);
}

public sealed class LabelReprintService : ILabelReprintService
{
    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly ILabelPrinter _printer;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public LabelReprintService(IDispensingRepository dispensings,
        IScriptRepository scripts, IPatientRepository patients,
        IUserRepository users, ILabelPrinter printer,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow)
    {
        _dispensings = dispensings;
        _scripts = scripts;
        _patients = patients;
        _users = users;
        _printer = printer;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<IReadOnlyList<DispensedItemDto>>>
        ForPatientAsync(Guid patientId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var patient = await _patients.GetAsync(patientId, ct);
        if (patient is null)
            return Result<IReadOnlyList<DispensedItemDto>>.Fail("DSP-000",
                "That patient no longer exists.");

        var issued = await _dispensings.ForPatientAsync(patientId, ct);

        var rows = new List<DispensedItemDto>();

        foreach (var dispensing in issued
                     .Where(d => d.Status == DispensingStatus.Issued)
                     .OrderByDescending(d => d.IssuedAtUtc))
        {
            var script = await _scripts.GetAsync(dispensing.ScriptId, ct);

            rows.Add(new DispensedItemDto(
                dispensing.Id,
                script?.ScriptNumber ?? "",
                dispensing.DocumentNumber,
                patient.FullName,
                DateOnly.FromDateTime(
                    (dispensing.IssuedAtUtc ?? DateTime.UtcNow).ToLocalTime()),
                dispensing.Lines.Count));
        }

        return Result<IReadOnlyList<DispensedItemDto>>.Ok(rows);
    }

    public async Task<Result<IReadOnlyList<LabelDto>>> BuildAsync(
        Guid dispensingId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<IReadOnlyList<LabelDto>>.Fail("DSP-000",
                "That dispensing no longer exists.");

        // Only an issued dispensing has labels: one still in progress
        // has not been checked by a pharmacist, and a label is the
        // pharmacist's instruction to the patient.
        if (dispensing.Status != DispensingStatus.Issued)
            return Result<IReadOnlyList<LabelDto>>.Fail("DSP-006",
                "Labels are printed when the dispensing is issued. This "
                + "one has not been issued.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var patient = await _patients.GetAsync(dispensing.PatientId, ct);

        var pharmacist = dispensing.PharmacistId is Guid id
            ? await _users.GetAsync(id, ct)
            : null;

        IReadOnlyList<LabelDto> labels = dispensing.Lines.Select(l =>
            new LabelDto(
                PatientName: patient?.FullName ?? "",
                ProductDescription: l.Description,
                Quantity: l.Quantity,
                Directions: l.Directions,
                BatchNumber: l.BatchNumber,
                Expiry: l.BatchExpiry,
                PharmacistName: pharmacist?.DisplayName ?? "",
                ScriptNumber: script?.ScriptNumber ?? "",
                DispensedOn: DateOnly.FromDateTime(
                    (dispensing.IssuedAtUtc ?? DateTime.UtcNow).ToLocalTime()),
                Cautions: Array.Empty<string>())).ToList();

        return Result<IReadOnlyList<LabelDto>>.Ok(labels);
    }

    public async Task<Result<int>> ReprintAsync(Guid dispensingId,
        CancellationToken ct = default)
    {
        var built = await BuildAsync(dispensingId, ct);
        if (built.IsFailure) return Result<int>.Fail(built.Error.Code,
                                                     built.Error.Message);

        if (!_printer.IsAvailable)
            return Result<int>.Fail("DSP-007",
                "No label printer is set for this workstation. Set one in "
                + "Printer Setup.");

        foreach (var label in built.Value) _printer.Print(label);

        // A reprinted label is a second instruction to a patient with
        // the same script number on it, so the trail says it happened.
        _audit.Applied("dsp.label.reprint", "Dispensing", dispensingId,
            $"{built.Value.Count} label(s) reprinted.");

        await _uow.SaveChangesAsync(ct);

        return Result<int>.Ok(built.Value.Count);
    }
}

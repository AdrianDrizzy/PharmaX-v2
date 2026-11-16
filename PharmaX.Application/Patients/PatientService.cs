using PharmaX.Application.Abstractions;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Patients;

public interface IPatientService
{
    /// <param name="skip">Matches passed over first, for a later page.</param>
    Task<Result<IReadOnlyList<PatientListItemDto>>> SearchAsync(string term,
        int take = 100, int skip = 0, bool includeInactive = false,
        CancellationToken ct = default);

    /// How many patients the same search matches, across every page.
    Task<Result<int>> CountAsync(string term, bool includeInactive = false,
                                 CancellationToken ct = default);

    Task<Result<PatientDto>> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(PatientEditDto edit,
                                   CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, PatientEditDto edit,
                             CancellationToken ct = default);

    Task<Result> DeactivateAsync(Guid id, CancellationToken ct = default);

    /// A deactivated patient back on the searches.
    Task<Result> ReactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Files already open that may be the patient being captured: the
    /// same names and date of birth, or the same telephone number. Asked
    /// before a new file is created, so the counter can open the existing
    /// one instead.
    /// </summary>
    Task<Result<IReadOnlyList<PatientListItemDto>>> PossibleDuplicatesAsync(
        PatientEditDto edit, Guid? except = null,
        CancellationToken ct = default);
}

public sealed class PatientService : IPatientService
{
    private readonly IPatientRepository _patients;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public PatientService(IPatientRepository patients,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow, IClock clock)
    {
        _patients = patients;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<PatientListItemDto>>>
        PossibleDuplicatesAsync(PatientEditDto edit, Guid? except = null,
            CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PatientMaintain);

        // The telephone number identifies a patient at the counter as
        // surely as the name does. Asked about, not refused: a family
        // often shares one number.
        var found = await _patients.LikelySameAsync(edit.Surname,
            edit.FirstNames, edit.DateOfBirth, idNumber: null, except,
            telephone: edit.Telephone, ct: ct);

        IReadOnlyList<PatientListItemDto> items = found
            .Select(p => PatientListItemDto.From(p, _clock.Today, false))
            .ToList();

        return Result<IReadOnlyList<PatientListItemDto>>.Ok(items);
    }

    /// <summary>
    /// Refuses a second file for an ID number already on one.
    /// </summary>
    /// <remarks>
    /// Two files for one person split their history: the allergy recorded
    /// on one is not on the other, which is exactly how a warning is
    /// missed at the dispensary. Nothing checked, so the same patient
    /// could be registered twice (ADM-OBS-16). A national ID belongs to
    /// one person, so a match is refused outright; a matching name and
    /// birthday is only likely, and is the presenter's to ask about.
    /// </remarks>
    private async Task<Error?> SameIdNumberAsync(PatientEditDto edit,
        Guid? except, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(edit.IdNumber)) return null;

        var same = await _patients.LikelySameAsync("", "", null,
            edit.IdNumber, except, ct: ct);

        return same.Count == 0
            ? null
            : new Error("PAT-014",
                $"ID number {edit.IdNumber.Trim()} is already on file "
                + $"{same[0].FileNumber} for {same[0].FullName}. Open that "
                + "file rather than starting a second one.");
    }

    public async Task<Result<IReadOnlyList<PatientListItemDto>>> SearchAsync(
        string term, int take = 100, int skip = 0, bool includeInactive = false,
        CancellationToken ct = default)
    {
        // A patient list is clinical information, so reading it needs the
        // dispensing permission rather than the product one.
        _permissions.Demand(PermissionCode.PatientMaintain);

        var patients = await _patients.SearchAsync(term, take, skip, includeInactive, ct);

        var allergic = await _patients
            .WithAllergiesAsync(patients.Select(p => p.Id), ct);

        IReadOnlyList<PatientListItemDto> items = patients
            .Select(p => PatientListItemDto.From(p, _clock.Today,
                                                 allergic.Contains(p.Id)))
            .ToList();

        return Result<IReadOnlyList<PatientListItemDto>>.Ok(items);
    }

    public async Task<Result<int>> CountAsync(string term, bool includeInactive = false,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PatientMaintain);

        return Result<int>.Ok(await _patients.CountAsync(term, includeInactive, ct));
    }

    public async Task<Result<PatientDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PatientMaintain);

        var patient = await _patients.GetAsync(id, ct);
        if (patient is null)
            return Result<PatientDto>.Fail("PAT-000", "Patient not found.");

        var profile = await _patients.ClinicalProfileAsync(id, ct);

        return Result<PatientDto>.Ok(
            PatientDto.From(patient, profile, _clock.Today));
    }

    public async Task<Result<Guid>> CreateAsync(PatientEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PatientMaintain);

        if (Validate(edit, _clock.Today) is Error problem)
            return Result<Guid>.Fail(problem.Code, problem.Message);

        if (await SameIdNumberAsync(edit, except: null, ct) is Error taken)
            return Result<Guid>.Fail(taken.Code, taken.Message);

        // Allocated, not typed: a file number the operator invents is a
        // number two patients eventually share.
        var fileNumber = await _numbering.NextAsync(DocumentSeries.Patient, ct);

        var patient = Patient.Create(fileNumber, edit.Surname,
            edit.FirstNames, edit.DateOfBirth, edit.IdNumber);

        patient.UpdateContact(edit.Telephone, edit.Email);
        patient.SetAddress(edit.Address);

        // Who pays. Set here rather than on a screen of its own, because
        // the membership number is captured at the counter at the same
        // moment as the telephone number.
        patient.SetMedicalAid(edit.MedicalAidSchemeId,
                              edit.MembershipNumber, edit.DependantCode);

        await _patients.AddAsync(patient, ct);

        var profile = ClinicalProfile.For(patient.Id);
        Apply(profile, edit);
        patient.LinkClinicalProfile(profile.Id);

        await _patients.AddClinicalProfileAsync(profile, ct);

        _audit.Applied("pat.patient.create", "Patient", patient.Id,
            $"File {fileNumber}");
        _outbox.Append("Patient", patient.Id,
            $"{{\"id\":\"{patient.Id}\",\"file\":\"{fileNumber}\"}}");

        await _uow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(patient.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, PatientEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PatientMaintain);

        if (Validate(edit, _clock.Today) is Error problem)
            return Result.Fail(problem.Code, problem.Message);

        var patient = await _patients.GetAsync(id, ct);
        if (patient is null) return Result.Fail("PAT-000", "Patient not found.");

        if (await SameIdNumberAsync(edit, except: id, ct) is Error taken)
            return Result.Fail(taken.Code, taken.Message);

        patient.Amend(edit.Surname, edit.FirstNames, edit.IdNumber,
                      edit.DateOfBirth);
        patient.UpdateContact(edit.Telephone, edit.Email);

        if (edit.Address is not null) patient.SetAddress(edit.Address);

        // Who pays. Set here rather than on a screen of its own, because
        // the membership number is captured at the counter at the same
        // moment as the telephone number.
        patient.SetMedicalAid(edit.MedicalAidSchemeId,
                              edit.MembershipNumber, edit.DependantCode);

        var profile = await _patients.ClinicalProfileAsync(id, ct);

        if (profile is null)
        {
            profile = ClinicalProfile.For(patient.Id);
            patient.LinkClinicalProfile(profile.Id);
            await _patients.AddClinicalProfileAsync(profile, ct);
        }

        Apply(profile, edit);

        // Recorded because a changed allergy list changes what the
        // dispensing screen will and will not stop (DSP-007).
        _audit.Applied("pat.patient.update", "Patient", patient.Id,
            $"{edit.Allergies.Count} allergy record(s)");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PatientDeactivate);

        var patient = await _patients.GetAsync(id, ct);
        if (patient is null) return Result.Fail("PAT-000", "Patient not found.");

        // Never deleted: a deleted patient takes their dispensing history
        // with them, and that history is a legal record.
        patient.Deactivate();

        _audit.Applied("pat.patient.deactivate", "Patient", patient.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> ReactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PatientDeactivate);

        var patient = await _patients.GetAsync(id, ct);
        if (patient is null) return Result.Fail("PAT-000", "Patient not found.");

        patient.Reactivate();

        _audit.Applied("pat.patient.reactivate", "Patient", patient.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    private static void Apply(ClinicalProfile profile, PatientEditDto edit)
    {
        profile.UpdateVitals(edit.WeightKg, edit.IsPregnant,
                             edit.IsBreastfeeding);
        profile.UpdateNotes(edit.ClinicalNotes);
        profile.ReplaceAllergies(edit.Allergies
            .Select(a => (a.Substance, a.Severity, a.Reaction)));
        profile.ReplaceChronicConditions(edit.ChronicConditions);
    }

    private static Error? Validate(PatientEditDto edit, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(edit.Surname))
            return new Error("PAT-001", "A patient needs a surname.");

        if (string.IsNullOrWhiteSpace(edit.FirstNames))
            return new Error("PAT-002", "A patient needs a first name.");

        if (edit.DateOfBirth is DateOnly born && born > today)
            return new Error("PAT-003",
                "A date of birth cannot be in the future.");

        if (edit.WeightKg is decimal weight && weight is <= 0m or > 500m)
            return new Error("PAT-004",
                "A weight must be between 0 and 500 kilograms.");

        // Severity drives whether an assistant may acknowledge a warning
        // or whether a pharmacist has to, so it cannot be free text.
        var severities = new[] { "Mild", "Moderate", "Severe" };

        var wrong = edit.Allergies
            .Where(a => !severities.Contains(a.Severity,
                                             StringComparer.Ordinal))
            .Select(a => a.Substance)
            .ToList();

        if (wrong.Count > 0)
            return new Error("PAT-005",
                $"Allergy severity must be Mild, Moderate or Severe: "
                + $"{string.Join(", ", wrong)}.");

        return null;
    }
}

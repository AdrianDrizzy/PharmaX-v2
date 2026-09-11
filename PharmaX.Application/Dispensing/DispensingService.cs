using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;

namespace PharmaX.Application.Dispensing;

public interface IDispensingService
{
    Task<Result<DispensingStateDto>> BeginAsync(Guid scriptId,
        CancellationToken ct = default);

    Task<Result<DispensingStateDto>> AddItemAsync(Guid dispensingId,
        string scannedCode, DirectionsDto directions,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClinicalWarningDto>> PendingWarningsAsync(
        Guid dispensingId, CancellationToken ct = default);

    Task<Result> AcknowledgeWarningAsync(Guid warningId, string? reason,
        CancellationToken ct = default);

    Task<Result<IssueResultDto>> IssueAsync(Guid dispensingId,
        Guid pharmacistId, CancellationToken ct = default);
}

/// <summary>
/// Prints a dispensing label. Implemented in PharmaX.Adapters; declared
/// here so that nothing in the presentation layer has to know which
/// printer is attached.
/// </summary>
public interface ILabelPrinter
{
    void Print(LabelDto label);
    bool IsAvailable { get; }
}

public sealed class DispensingService : IDispensingService
{
    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDocumentNumbering _numbering;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public DispensingService(IDispensingRepository dispensings,
        IScriptRepository scripts, IProductRepository products,
        IBatchRepository batches, IPatientRepository patients,
        IUserRepository users, IDocumentNumbering numbering,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _dispensings = dispensings;
        _scripts = scripts;
        _products = products;
        _batches = batches;
        _patients = patients;
        _users = users;
        _numbering = numbering;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<DispensingStateDto>> BeginAsync(Guid scriptId,
        CancellationToken ct = default)
    {
        var script = await _scripts.GetAsync(scriptId, ct);
        if (script is null)
            return Result<DispensingStateDto>.Fail("DSP-000",
                "Script not found.");

        if (!script.IsValidOn(_clock.Today))
            return Result<DispensingStateDto>.Fail("DSP-002",
                $"Script {script.ScriptNumber} is no longer valid.");

        if (script.RepeatsRemaining < 0)
            return Result<DispensingStateDto>.Fail("DSP-011",
                $"Script {script.ScriptNumber} has no repeats left.");

        var dispensing = Domain.Dispensing.Dispensing.Begin(
            script.Id, script.PatientId, _user.Id, script.RepeatsUsed);

        await _dispensings.AddAsync(dispensing, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<DispensingStateDto>.Ok(
            DispensingStateDto.From(dispensing));
    }

    public async Task<Result<DispensingStateDto>> AddItemAsync(
        Guid dispensingId, string scannedCode, DirectionsDto directions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(directions);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<DispensingStateDto>.Fail("DSP-000",
                "Dispensing not found.");

        var product = await _products.FindByCodeOrBarcodeAsync(scannedCode, ct);
        if (product is null)
            return Result<DispensingStateDto>.Fail("DSP-001",
                $"No product for code {scannedCode}.");

        Guid? batchId = null;
        string? batchNumber = null;
        DateOnly? expiry = null;

        if (product.IsBatchTracked)
        {
            var batches = await _batches.ForProductAsync(product.Id, ct);
            var allocation = FefoAllocator
                .Allocate(batches, directions.Quantity, _clock.Today)[0];

            var batch = batches.Single(b => b.Id == allocation.BatchId);
            batchId = batch.Id;
            batchNumber = batch.BatchNumber;
            expiry = batch.ExpiryDate;
        }

        var line = dispensing.AddLine(DispensingLine.Create(
            productId: product.Id,
            description: product.Description,
            quantity: directions.Quantity,
            directions: directions.Directions,
            tariffAmount: product.ResolvePrice(dispensing.PatientId).Amount,
            batchId: batchId,
            batchNumber: batchNumber,
            batchExpiry: expiry));

        // Warnings are computed here, from the clinical profile and what is
        // already on the dispensing. The presenter only routes them.
        var profile = await _patients.ClinicalProfileAsync(
            dispensing.PatientId, ct);

        foreach (var warning in ScreenClinically(dispensing, line, profile,
                                                 product.Schedule))
            dispensing.RaiseWarning(warning);

        await _uow.SaveChangesAsync(ct);

        return Result<DispensingStateDto>.Ok(
            DispensingStateDto.From(dispensing));
    }

    public async Task<IReadOnlyList<ClinicalWarningDto>> PendingWarningsAsync(
        Guid dispensingId, CancellationToken ct = default)
    {
        var dispensing = await _dispensings.GetAsync(dispensingId, ct);

        return dispensing is null
            ? Array.Empty<ClinicalWarningDto>()
            : dispensing.PendingWarnings
                        .Select(ClinicalWarningDto.From)
                        .ToList();
    }

    public async Task<Result> AcknowledgeWarningAsync(Guid warningId,
        string? reason, CancellationToken ct = default)
    {
        var dispensing = await _dispensings.ByWarningAsync(warningId, ct);
        if (dispensing is null)
            return Result.Fail("DSP-003", "Warning not found.");

        dispensing.AcknowledgeWarning(warningId, _user.Id, reason);

        // Recorded whether or not it was an override: a pattern of
        // acknowledged interactions is clinical information (DSP-007).
        _audit.Applied("dsp.warning.acknowledge", "ClinicalWarning",
            warningId, reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<IssueResultDto>> IssueAsync(Guid dispensingId,
        Guid pharmacistId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<IssueResultDto>.Fail("DSP-000",
                "Dispensing not found.");

        // Idempotency: a retry after a printer or comms failure must not
        // issue the medicine twice (NET-006).
        if (dispensing.Status == DispensingStatus.Issued)
            return Result<IssueResultDto>.Fail("DSP-005",
                "This dispensing has already been issued.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        if (script is null)
            return Result<IssueResultDto>.Fail("DSP-000", "Script not found.");

        var number = await _numbering.NextAsync("DISP", ct);
        dispensing.Issue(pharmacistId, number, _clock.UtcNow);
        script.RecordDispensing(_clock.Today);

        foreach (var line in dispensing.Lines.Where(l => l.BatchId is not null))
        {
            var batch = await _batches.GetAsync(line.BatchId!.Value, ct);
            batch?.Issue(line.Quantity);
        }

        var patient = await _patients.GetAsync(dispensing.PatientId, ct);
        var pharmacist = await _users.GetAsync(pharmacistId, ct);

        var labels = dispensing.Lines.Select(l => new LabelDto(
            PatientName: patient?.FullName ?? "",
            ProductDescription: l.Description,
            Quantity: l.Quantity,
            Directions: l.Directions,
            BatchNumber: l.BatchNumber,
            Expiry: l.BatchExpiry,
            PharmacistName: pharmacist?.DisplayName ?? "",
            ScriptNumber: script.ScriptNumber,
            DispensedOn: _clock.Today,
            Cautions: Array.Empty<string>())).ToList();

        _outbox.Append("Dispensing", dispensing.Id,
            $"{{\"id\":\"{dispensing.Id}\",\"script\":\"{script.ScriptNumber}\"}}");
        _audit.Applied("dsp.script.issue", "Dispensing", dispensing.Id);

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<IssueResultDto>.Ok(new IssueResultDto(
            script.ScriptNumber, dispensing.DocumentNumber,
            dispensing.CoPaymentTotal.Amount, labels));
    }

    /// <summary>
    /// The clinical screen. Deliberately a pure function of what is on the
    /// dispensing and what is on the profile, so that it can be tested
    /// exhaustively without a database.
    /// </summary>
    private static IEnumerable<ClinicalWarning> ScreenClinically(
        Domain.Dispensing.Dispensing dispensing, DispensingLine line,
        ClinicalProfile? profile, int schedule)
    {
        if (profile is not null && profile.IsAllergicTo(line.Description))
            yield return ClinicalWarning.Raise("Allergy",
                WarningSeverity.HardStop,
                $"Patient is recorded as allergic to {line.Description}.",
                line.Id);

        var duplicate = dispensing.Lines
            .Any(l => l.Id != line.Id && l.ProductId == line.ProductId);

        if (duplicate)
            yield return ClinicalWarning.Raise("Duplication",
                WarningSeverity.Caution,
                $"{line.Description} already appears on this dispensing.",
                line.Id);

        if (profile?.IsPregnant == true && schedule >= 3)
            yield return ClinicalWarning.Raise("Pregnancy",
                WarningSeverity.Caution,
                $"Patient is recorded as pregnant. Check {line.Description} "
                + "against the pregnancy category.",
                line.Id);
    }
}

public interface IDispensingRepository
{
    Task<Domain.Dispensing.Dispensing?> GetAsync(Guid id,
        CancellationToken ct = default);

    /// Loads the dispensing that owns a warning, so acknowledging one does
    /// not need the screen to remember which dispensing it came from.
    Task<Domain.Dispensing.Dispensing?> ByWarningAsync(Guid warningId,
        CancellationToken ct = default);

    Task AddAsync(Domain.Dispensing.Dispensing dispensing,
                  CancellationToken ct = default);
}

public interface IScriptRepository
{
    Task<Script?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Script>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default);

    Task AddAsync(Script script, CancellationToken ct = default);
}

using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Finance;
using PharmaX.Application.Inventory;
using PharmaX.Application.Reference;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Dispensing;

public interface IDispensingService
{
    Task<Result<DispensingStateDto>> BeginAsync(Guid scriptId,
        CancellationToken ct = default);

    /// <summary>
    /// Puts every item on the script onto the dispensing, with the
    /// quantity and directions the prescriber wrote.
    /// </summary>
    /// <remarks>
    /// The normal case: the patient has handed over a script and wants
    /// what is on it. Items already on the dispensing are skipped, and an
    /// item that cannot go on - out of stock, discontinued - is reported
    /// rather than stopping the others.
    /// </remarks>
    Task<Result<ScriptItemsAddedDto>> AddScriptItemsAsync(Guid dispensingId,
        CancellationToken ct = default);

    /// <summary>
    /// What the script says to hand over for a scanned product, so the
    /// screen can offer the prescriber's quantity and directions instead
    /// of asking for them again.
    /// </summary>
    Task<Result<ItemProposalDto>> ProposeItemAsync(Guid dispensingId,
        string scannedCode, CancellationToken ct = default);

    Task<Result<DispensingStateDto>> AddItemAsync(Guid dispensingId,
        string scannedCode, DirectionsDto directions,
        CancellationToken ct = default);

    /// Takes an item off a dispensing that has not been issued yet,
    /// together with any warning it raised.
    Task<Result<DispensingStateDto>> RemoveItemAsync(Guid dispensingId,
        int lineNo, CancellationToken ct = default);

    /// The dispensing as it stands, for a screen that opens one from the
    /// patient's history.
    Task<Result<DispensingStateDto>> GetAsync(Guid dispensingId,
        CancellationToken ct = default);

    /// <summary>
    /// Changes an item's quantity and amount, while it is being dispensed
    /// or after it was issued.
    /// </summary>
    /// <remarks>
    /// After the issue the stock moves by the difference - more off the
    /// shelf, or back onto the batch it came from - and it needs
    /// dsp.dispensing.amend and a reason. One claimed from a medical aid
    /// is reversed and dispensed again instead: the claim and its journals
    /// were raised on the old figures.
    /// </remarks>
    /// <param name="amount">
    /// What the item is charged. Null to price it for the new quantity:
    /// as a new item would be before the issue, pro rata after it. Any
    /// other amount is a charge adjustment: dsp.charge.adjust and a reason.
    /// </param>
    /// <param name="reason">
    /// Why an issued item is changed, or why the charge is adjusted.
    /// </param>
    /// <param name="authorisedBy">
    /// Who authorised a charge adjustment the operator may not make on
    /// their own; refused with <see cref="DispensingService.ChargeNeedsAuthority"/>
    /// where it was needed and not given.
    /// </param>
    Task<Result<ItemAmendedDto>> AmendItemAsync(Guid dispensingId, int lineNo,
        decimal quantity, decimal? amount, string? reason,
        Guid? authorisedBy = null, CancellationToken ct = default);

    Task<IReadOnlyList<ClinicalWarningDto>> PendingWarningsAsync(
        Guid dispensingId, CancellationToken ct = default);

    /// <summary>
    /// Whether a warning's override needs a pharmacist's own credentials:
    /// a hard stop, where the operator does not hold that authority.
    /// </summary>
    Task<bool> OverrideNeedsPharmacistAsync(Guid warningId,
        CancellationToken ct = default);

    /// <param name="authorisedBy">
    /// The pharmacist who released a hard stop the operator could not
    /// release themselves. Ignored for a softer warning.
    /// </param>
    Task<Result> AcknowledgeWarningAsync(Guid warningId, string? reason,
        Guid? authorisedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Whether the operator may issue this dispensing on their own
    /// authority, or needs a pharmacist to authorise it.
    /// </summary>
    Task<Result<IssueAuthorityDto>> IssueAuthorityAsync(Guid dispensingId,
        CancellationToken ct = default);

    /// <param name="authorisedBy">
    /// Null where the operator issues on their own authority; otherwise
    /// the pharmacist who authorised the issue at this till.
    /// </param>
    Task<Result<IssueResultDto>> IssueAsync(Guid dispensingId,
        Guid? authorisedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Takes back an issued dispensing that has not been collected: the
    /// stock goes back to its batches, the repeat back to the script, and
    /// any claim on the scheme is withdrawn.
    /// </summary>
    Task<Result> ReverseAsync(Guid dispensingId, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Takes a script nobody came to pay for off Scripts to collect, the
    /// same way as ReverseAsync: the stock goes back on the shelf. Open to
    /// everyone who works the Collect screen, and to nothing but a script
    /// still waiting there.
    /// </summary>
    Task<Result> ReverseUncollectedAsync(Guid dispensingId, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Sets aside a dispensing begun and not issued, for a screen closed
    /// on it. Nothing to do for one already issued.
    /// </summary>
    Task<Result> AbandonAsync(Guid dispensingId,
        CancellationToken ct = default);


    /// Everything owed to patients and not yet handed over.
    Task<IReadOnlyList<ToFollowDto>> ToFollowAsync(CancellationToken ct = default);

    /// Hands over what was owed on one line, off the shelf, and returns
    /// the label for what was handed over.
    Task<Result<LabelDto>> GiveOwedAsync(Guid dispensingId, int lineNo,
        CancellationToken ct = default);

    /// <summary>
    /// A batch-tracked item with no batch in stock: the batch and expiry
    /// read off the box in the pharmacist's hand become a batch, so the
    /// label carries them. Loose stock goes onto it first; anything
    /// beyond that is recorded as found at dispensing.
    /// </summary>
    /// <param name="units">What is being handed over, in the dispensing's
    /// units; whole packs of the product's own size go onto the batch.</param>
    Task<Result> RecordFoundBatchAsync(Guid productId, string batchNumber,
        DateOnly expiry, decimal units, CancellationToken ct = default);

    /// <summary>
    /// What the pharmacist decided for a label that had no expiry, item n
    /// of the bag: an expiry typed from the box is kept on the line, so a
    /// reprint carries it; none is recorded as printed without one. Both
    /// go on the audit trail under the signed-in pharmacist's name.
    /// </summary>
    Task<Result> RecordLabelExpiryAsync(Guid dispensingId, int itemNumber,
        DateOnly? expiry, CancellationToken ct = default);

    /// Scripts begun within the last 24 hours and kept to finish later;
    /// older ones are set aside first.
    Task<IReadOnlyList<SuspendedDispensingDto>> SuspendedAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Prints a dispensing label. Implemented in PharmaX.Adapters; declared
/// here so that nothing in the presentation layer has to know which
/// printer is attached.
/// </summary>
public interface ILabelPrinter
{
    /// <summary>
    /// Prints one label and says honestly whether it did.
    /// </summary>
    /// <remarks>
    /// Asynchronous for the same reason as the receipt printer: the
    /// label printer is read from the database and the document goes to
    /// the spooler, and waiting for either from the user-interface
    /// thread froze the dispensing screen. It returns an outcome rather
    /// than nothing: a label that did not print used to be logged and
    /// forgotten, and the screen said "Issued" as though it had.
    /// </remarks>
    Task<Sales.PrintResult> PrintAsync(LabelDto label,
                                       CancellationToken ct = default);

    /// <summary>
    /// Prints the same label several times over, in one job: the stickers
    /// for a run of pre-packs.
    /// </summary>
    Task<Sales.PrintResult> PrintCopiesAsync(LabelDto label, int copies,
                                             CancellationToken ct = default);

    /// <summary>
    /// Prints the trailer label that follows a script's own labels, on
    /// the same printer, and says whether it did.
    /// </summary>
    Task<Sales.PrintResult> PrintTrailerAsync(TrailerLabelDto trailer,
                                              CancellationToken ct = default);

    bool IsAvailable { get; }
}

public sealed class DispensingService : IDispensingService
{
    /// <summary>
    /// Schedule 6 and above are controlled medicines: kept in a register,
    /// and named on the audit trail of whoever issued them.
    /// </summary>
    public const int ControlledSchedule = 6;

    /// <summary>
    /// The refusal of a charge adjustment that someone holding
    /// dsp.charge.adjust may authorise: the screen asks for them and
    /// tries again.
    /// </summary>
    public const string ChargeNeedsAuthority = "DSP-056";

    /// <summary>
    /// Whether the operator releases a hard stop or issues a controlled
    /// medicine on their own authority.
    /// </summary>
    /// <remarks>
    /// Anyone who may dispense, a pharmacist assistant included. An
    /// assistant used to need a pharmacist's credentials for both; the
    /// pharmacy dropped that on 12 October 2026. A hard stop still needs a
    /// written reason, and both are still audited under the operator's
    /// name. Put ScriptAuthorise back here to bring the pharmacist's
    /// sign-off back.
    /// </remarks>
    private bool ActsOnOwnAuthority =>
        _permissions.Has(PermissionCode.ScriptDispense);

    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IClaimRepository _claims;
    private readonly IReferenceRepository _reference;
    private readonly ITariffRepository _tariffs;
    private readonly IJournalPoster _journals;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    // The step a cash charge is rounded up to. Absent in the tests built
    // before it, which then charge to the cent as they always did.
    private readonly IPharmacySettings? _settings;

    private async Task<decimal> RoundToAsync(CancellationToken ct) =>
        _settings is null ? 0.01m : await DispensingCharge.RoundToAsync(_settings, ct);

    public DispensingService(IDispensingRepository dispensings,
        IScriptRepository scripts, IProductRepository products,
        IBatchRepository batches, IPatientRepository patients,
        IUserRepository users, IClaimRepository claims,
        IReferenceRepository reference, ITariffRepository tariffs,
        IJournalPoster journals, IStockService stock,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock, IPharmacySettings? settings = null)
    {
        _settings = settings;
        _claims = claims;
        _reference = reference;
        _tariffs = tariffs;
        _journals = journals;
        _stock = stock;
        _dispensings = dispensings;
        _scripts = scripts;
        _products = products;
        _batches = batches;
        _patients = patients;
        _users = users;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<DispensingStateDto>> BeginAsync(Guid scriptId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

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

        // One kept to finish later is picked up where it was left - if it
        // was begun within the last day; an older one is set aside and the
        // script begun again.
        await ClearStaleAsync(ct);

        if ((await _dispensings.ForPatientAsync(script.PatientId, ct))
            .FirstOrDefault(d => d.ScriptId == script.Id
                                 && d.Status == DispensingStatus.InProgress) is { } kept
            && await _dispensings.GetAsync(kept.Id, ct) is { } resumed)
        {
            // Picked up again: its 24 hours start over (ClearStaleAsync), so
            // it is not set aside under the person finishing it.
            resumed.UpdatedAtUtc = _clock.UtcNow;
            await _uow.SaveChangesAsync(ct);

            return Result<DispensingStateDto>.Ok(DispensingStateDto.From(resumed));
        }

        var dispensing = Domain.Dispensing.Dispensing.Begin(
            script.Id, script.PatientId, _user.Id, script.RepeatsUsed);

        await _dispensings.AddAsync(dispensing, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<DispensingStateDto>.Ok(
            DispensingStateDto.From(dispensing));
    }

    public async Task<Result<ScriptItemsAddedDto>> AddScriptItemsAsync(
        Guid dispensingId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<ScriptItemsAddedDto>.Fail("DSP-000",
                "Dispensing not found.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        if (script is null)
            return Result<ScriptItemsAddedDto>.Fail("DSP-000",
                "Script not found.");

        var problems = new List<string>();
        var added = 0;

        foreach (var item in script.Items.OrderBy(i => i.LineNo))
        {
            if (dispensing.Lines.Any(l => l.ScriptItemId == item.Id))
                continue;

            var product = await _products.GetAsync(item.ProductId, ct);

            if (product is null || !product.IsActive)
            {
                problems.Add($"{item.Description}: no longer stocked.");
                continue;
            }

            var outcome = await AddLineAsync(dispensing, product,
                new DirectionsDto(item.Quantity, item.Directions,
                                  item.AllowSubstitution),
                item.Id, ct);

            if (outcome is Error problem) problems.Add(problem.Message);
            else added++;
        }

        await _uow.SaveChangesAsync(ct);

        return Result<ScriptItemsAddedDto>.Ok(new ScriptItemsAddedDto(
            DispensingStateDto.From(dispensing), added, problems));
    }

    public async Task<Result<ItemProposalDto>> ProposeItemAsync(
        Guid dispensingId, string scannedCode, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<ItemProposalDto>.Fail("DSP-000",
                "Dispensing not found.");

        var product = await _products.FindByCodeOrBarcodeAsync(scannedCode,
            _permissions.ProductScope(), ct);

        if (product is null)
            return Result<ItemProposalDto>.Fail("DSP-001",
                $"No product for code {scannedCode}.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var item = NextScriptItemFor(script, dispensing, product.Id);

        // What the dispenser types and how it will be charged, so the
        // prompt can say so and work the charge out as it is typed.
        var (packPrice, unitsInPack) = PackFor(product, item, dispensing.PatientId);
        var patient = await _patients.GetAsync(dispensing.PatientId, ct);
        var tariffed = patient is { IsOnMedicalAid: true }
                       && await _tariffs.EffectiveAsync(patient.MedicalAidSchemeId!.Value,
                           product.Id, _clock.Today, ct) is not null;

        var proposal = item is null
            ? new ItemProposalDto(product.Id, product.Code,
                product.Description, 1m, "", FromScript: false)
            : new ItemProposalDto(product.Id, product.Code,
                product.Description, item.Quantity, item.Directions,
                FromScript: true);

        // Nothing on a batch to hand over: the label would print with no
        // expiry, so the screen asks for the one on the box instead.
        var needsBatch = product.IsBatchTracked
            && !(await _batches.ForProductAsync(product.Id, ct))
                .Any(b => b.IsAvailableOn(_clock.Today) && b.QuantityOnHand > 0m);

        return Result<ItemProposalDto>.Ok(proposal with
        {
            NeedsBatchFromBox = needsBatch,
            Unit = Assistant.Guided.Directions.UnitOf(product.PackSize, product.Description),
            UnitsInPack = unitsInPack,
            PackPrice = tariffed ? null : packPrice,
            RoundTo = await RoundToAsync(ct)
        });
    }

    public async Task<Result<DispensingStateDto>> AddItemAsync(
        Guid dispensingId, string scannedCode, DirectionsDto directions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(directions);

        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<DispensingStateDto>.Fail("DSP-000",
                "Dispensing not found.");

        var product = await _products.FindByCodeOrBarcodeAsync(scannedCode,
            _permissions.ProductScope(), ct);

        if (product is null)
            return Result<DispensingStateDto>.Fail("DSP-001",
                $"No product for code {scannedCode}.");

        if (!product.IsActive)
            return Result<DispensingStateDto>.Fail("DSP-013",
                $"{product.Description} is discontinued and cannot be "
                + "dispensed.");

        // The script item this answers, so the line carries the
        // prescription it came from and the same item is not offered
        // twice.
        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var item = NextScriptItemFor(script, dispensing, product.Id);

        if (await AddLineAsync(dispensing, product, directions, item?.Id, ct)
            is Error refused)
            return Result<DispensingStateDto>.Fail(refused.Code,
                                                   refused.Message);

        await _uow.SaveChangesAsync(ct);

        return Result<DispensingStateDto>.Ok(
            DispensingStateDto.From(dispensing));
    }

    public async Task<Result<DispensingStateDto>> RemoveItemAsync(
        Guid dispensingId, int lineNo, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<DispensingStateDto>.Fail("DSP-000",
                "Dispensing not found.");

        var line = dispensing.Lines.SingleOrDefault(l => l.LineNo == lineNo);
        if (line is null)
            return Result<DispensingStateDto>.Fail("DSP-014",
                $"There is no item {lineNo} on this dispensing.");

        dispensing.RemoveLine(lineNo);

        _audit.Applied("dsp.item.remove", "Dispensing", dispensing.Id,
            $"{line.Description} x{line.Quantity:0.###}");

        await _uow.SaveChangesAsync(ct);

        return Result<DispensingStateDto>.Ok(
            DispensingStateDto.From(dispensing));
    }

    public async Task<Result<DispensingStateDto>> GetAsync(Guid dispensingId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        return await _dispensings.GetAsync(dispensingId, ct) is { } dispensing
            ? Result<DispensingStateDto>.Ok(DispensingStateDto.From(dispensing))
            : Result<DispensingStateDto>.Fail("DSP-000", "Dispensing not found.");
    }

    public async Task<Result<ItemAmendedDto>> AmendItemAsync(Guid dispensingId,
        int lineNo, decimal quantity, decimal? amount, string? reason,
        Guid? authorisedBy = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        static Result<ItemAmendedDto> Fail(string code, string message) =>
            Result<ItemAmendedDto>.Fail(code, message);

        if (quantity <= 0m)
            return Fail("DSP-024", "The quantity must be more than zero.");

        if (amount < 0m)
            return Fail("DSP-050", "The amount cannot be negative.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null) return Fail("DSP-000", "Dispensing not found.");

        var line = dispensing.Lines.SingleOrDefault(l => l.LineNo == lineNo);
        if (line is null)
            return Fail("DSP-014", $"There is no item {lineNo} on this dispensing.");

        var issued = dispensing.Status == DispensingStatus.Issued;

        if (issued)
        {
            if (!_permissions.Has(PermissionCode.DispensingAmend))
                return Fail("DSP-051", "Changing an item after it was issued "
                    + "needs a pharmacist or a pharmacist assistant.");

            if (string.IsNullOrWhiteSpace(reason))
                return Fail("DSP-052", "Say why the issued item is being "
                    + "changed: it is kept on the record.");

            if (await _claims.ForDispensingAsync(dispensing.Id, ct) is
                { Status: not Domain.Claims.ClaimStatus.Withdrawn } claim)
                return Fail("DSP-053", $"{dispensing.DocumentNumber} was "
                    + $"claimed from the medical aid ({claim.ClaimNumber}). "
                    + "Reverse it and dispense it again to change it.");
        }

        // Pro rata to what it was charged, as the screen suggests it.
        var proRata = Math.Round(line.TariffAmount.Amount * quantity / line.Quantity,
                                 2, MidpointRounding.AwayFromZero);

        // Before the issue an amount at all is the operator's own; after
        // it, one other than the pro rata the screen offered.
        var adjusting = issued ? amount is decimal given && given != proRata
                               : amount is not null;
        Guid? adjustedFor = null;

        if (adjusting)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return Fail("DSP-055", "Say why the charge is being changed: it "
                    + "is kept on the record.");

            switch (_permissions.Check(PermissionCode.DispensingChargeAdjust))
            {
                case PermissionOutcome.Granted:
                    break;

                case PermissionOutcome.RequiresSupervisor when authorisedBy is null:
                    return Fail(ChargeNeedsAuthority, "Changing the charge needs "
                        + "a pharmacist, an assistant, a manager or the "
                        + "administrator to authorise it.");

                // Someone else, who holds it: authorising yourself is not a
                // second pair of eyes.
                case PermissionOutcome.RequiresSupervisor
                    when authorisedBy is Guid by && by != _user.Id
                         && _permissions.UserHolds(by, PermissionCode.DispensingChargeAdjust):
                    adjustedFor = by;
                    break;

                default:
                    // Out of the transaction first: the refusal is written on
                    // its own connection.
                    await tx.DisposeAsync();
                    _audit.Refused(PermissionCode.DispensingChargeAdjust,
                        "Dispensing", dispensing.Id,
                        authorisedBy is null
                            ? "Not held."
                            : "The authoriser may not change a charge.");

                    return Fail("DSP-057", authorisedBy is null
                        ? "You do not have permission to change the charge."
                        : "The person who authorised this may not change a charge.");
            }
        }

        var before = dispensing.CoPaymentTotal.Amount;
        var was = $"{line.Description} x{line.Quantity:0.###} at "
                  + $"{line.TariffAmount.Amount:0.00}";

        // What the item comes to without the adjustment, for the record.
        var priced = proRata;

        try
        {
            if (issued)
            {
                var charged = amount ?? proRata;
                var units = quantity - line.Quantity;

                dispensing.AmendLine(lineNo, quantity,
                    Money.Of(charged, line.TariffAmount.Currency));
                await _stock.ApplyDispensingAmendAsync(dispensing, line, units, ct);
            }
            else
            {
                if (await ReplaceLineAsync(dispensing, line, quantity, ct)
                    is Error refused)
                {
                    _uow.Forget();
                    return Fail(refused.Code, refused.Message);
                }

                line = dispensing.Lines.MaxBy(l => l.LineNo)!;
                priced = line.TariffAmount.Amount;

                if (amount is decimal charged)
                    dispensing.AmendLine(line.LineNo, quantity,
                        Money.Of(charged, line.TariffAmount.Currency));
            }
        }
        catch (DomainException ex)
        {
            _uow.Forget();
            return Fail("DSP-054", ex.Message);
        }

        _audit.Applied("dsp.item.amend", "Dispensing", dispensing.Id,
            $"{was} -> x{line.Quantity:0.###} at {line.TariffAmount.Amount:0.00}"
            + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}"));

        // Its own entry, so the charges changed by hand - and who let them -
        // can be read off the audit trail without the quantity changes.
        if (adjusting)
        {
            var authoriser = adjustedFor is Guid by
                ? (await _users.GetAsync(by, ct))?.Username ?? by.ToString()
                : null;

            _audit.Applied(PermissionCode.DispensingChargeAdjust, "Dispensing",
                dispensing.Id,
                $"{line.Description} x{line.Quantity:0.###} priced "
                + $"{priced:0.00}, charged {line.TariffAmount.Amount:0.00}: "
                + reason!.Trim()
                + (authoriser is null ? "" : $" (authorised by {authoriser})"));
        }

        if (issued)
            _outbox.Append("Dispensing", dispensing.Id,
                $"{{\"id\":\"{dispensing.Id}\",\"amended\":{lineNo}}}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<ItemAmendedDto>.Ok(new ItemAmendedDto(
            DispensingStateDto.From(dispensing), issued, before,
            dispensing.CoPaymentTotal.Amount,
            // A till sale, or a script taken off the queue without one
            // (settled before PharmaX). Still waiting means neither.
            dispensing.SaleId is not null || dispensing.CollectedAtUtc is not null));
    }

    /// <summary>
    /// An item still being dispensed, taken off and put on again at the
    /// new quantity: priced, allocated, owed and screened as a new one is.
    /// </summary>
    private async Task<Error?> ReplaceLineAsync(
        Domain.Dispensing.Dispensing dispensing, DispensingLine line,
        decimal quantity, CancellationToken ct)
    {
        var product = await _products.GetAsync(line.ProductId, ct);
        if (product is null)
            return new Error("DSP-001",
                $"{line.Description} is no longer in the catalogue.");

        dispensing.RemoveLine(line.LineNo);

        if (await AddLineAsync(dispensing, product,
                new DirectionsDto(quantity, line.Directions),
                line.ScriptItemId, ct) is Error refused)
            return refused;

        if (line.SubstitutedForProductId is Guid original)
            dispensing.Lines.MaxBy(l => l.LineNo)!.MarkSubstitution(original);

        return null;
    }

    /// <summary>
    /// Prices, allocates and screens one line, and puts it on the
    /// dispensing. Returns the reason instead where it cannot go on.
    /// </summary>
    /// <summary>
    /// The pack a charge is worked from: the one set on the script for
    /// this item, otherwise the product's own at this patient's price.
    /// </summary>
    private static (decimal Price, decimal Units) PackFor(Domain.Inventory.Product product,
        ScriptItem? scriptItem, Guid patientId) =>
        scriptItem is { PackUnits: decimal units, PackPrice: Money price }
            ? (price.Amount, units)
            : (product.ResolvePrice(patientId).Amount.Amount, product.UnitsInPack);

    private async Task<Error?> AddLineAsync(
        Domain.Dispensing.Dispensing dispensing,
        Domain.Inventory.Product product, DirectionsDto directions,
        Guid? scriptItemId, CancellationToken ct)
    {
        if (directions.Quantity <= 0m)
            return new Error("DSP-024",
                $"{product.Description}: the quantity must be more than "
                + "zero.");

        if (string.IsNullOrWhiteSpace(directions.Directions))
            return new Error("DSP-025",
                $"{product.Description}: directions are mandatory, because "
                + "they are printed on the label (DSP-004).");

        // FEFO across as many batches as the quantity needs, counting what
        // earlier lines of this dispensing have already claimed, and done
        // again at issue against the stock as it is then. What the shelf
        // is short of is owed to the patient - To follow - charged with
        // the rest and handed over when the stock comes in.
        IReadOnlyList<BatchAllocation>? allocation = null;
        decimal shortfall;

        // Counted in the script's units - tablets, capsules, millilitres -
        // against a shelf counted in packs, each pack holding the
        // product's units per pack (ADM-DEF-15).
        var perPack = product.UnitsInPack;

        if (product.IsBatchTracked)
        {
            var batches = await _batches.ForProductAsync(product.Id, ct);

            var claimed = dispensing.Lines
                .SelectMany(l => l.Batches)
                .GroupBy(b => b.BatchId)
                .ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));

            var available = batches
                .Where(b => b.IsAvailableOn(_clock.Today))
                .Sum(b => b.QuantityOnHand * perPack
                          - claimed.GetValueOrDefault(b.Id));

            shortfall = Math.Max(directions.Quantity - Math.Max(available, 0m), 0m);

            if (shortfall < directions.Quantity)
                allocation = Allocate(batches, claimed,
                                      directions.Quantity - shortfall, perPack);
        }
        else
        {
            // Untracked stock is held to the shelf as well, the same rule
            // the till keeps (ADM-DEF-12).
            var claimed = dispensing.Lines
                .Where(l => l.ProductId == product.Id)
                .Sum(l => l.Quantity - (l.Owed ?? 0m));

            var available = product.QuantityOnHand * perPack - claimed;

            shortfall = Math.Max(directions.Quantity - Math.Max(available, 0m), 0m);
        }

        // What the society has agreed to pay for this product today.
        // Null where the patient pays for themselves, or where the
        // society has no tariff on file for what is being handed over -
        // in both of which cases the shelf price stands and the whole
        // amount is the patient's.
        var patient = await _patients.GetAsync(dispensing.PatientId, ct);

        var tariff = patient is { IsOnMedicalAid: true }
            ? await _tariffs.EffectiveAsync(patient.MedicalAidSchemeId!.Value,
                product.Id, _clock.Today, ct)
            : null;

        // Priced for the units actually handed over, pro rata to the
        // pack, with the society's dispensing fee once per item (Section
        // 7.8, DEF-0021). A pack and price set on the script for this
        // line stand in for the product's - but only for the product
        // prescribed: a generic substitute keeps its own.
        var scriptItem = scriptItemId is Guid itemId
            ? (await _scripts.GetAsync(dispensing.ScriptId, ct))?.Items
                .FirstOrDefault(i => i.Id == itemId && i.ProductId == product.Id)
            : null;

        // A cash charge is rounded up to the pharmacy's step - a dollar
        // unless System setup says otherwise - as the previous system
        // charged (DispensingCharge). The tariff below never is.
        var (packPrice, unitsInPack) = PackFor(product, scriptItem, dispensing.PatientId);
        var shelf = Money.Of(DispensingCharge.For(packPrice, unitsInPack, directions.Quantity,
                                                  await RoundToAsync(ct)),
                             product.RetailPrice.Currency);

        var contribution = tariff is null
            ? (Money?)null
            : product.PriceForUnits(tariff.Price, directions.Quantity);

        var line = dispensing.AddLine(DispensingLine.Create(
            productId: product.Id,
            description: product.Description,
            quantity: directions.Quantity,
            directions: await _reference.ExpandDirectionsAsync(
                directions.Directions, ct),
            tariffAmount: contribution is Money share
                ? share + tariff!.DispensingFee
                : shelf,
            scriptItemId: scriptItemId));

        if (shortfall > 0m) dispensing.Owe(line.LineNo, shortfall);
        if (allocation is not null) line.AllocateBatches(allocation);

        // The society's own share. SetSchemeContribution once had no
        // caller outside three unit tests, so no claim could ever be
        // raised: the contribution was always zero.
        if (contribution is Money owed) line.SetSchemeContribution(owed);

        // Warnings are computed here, from the clinical profile and what
        // is already on the dispensing. The presenter only routes them.
        var profile = await _patients.ClinicalProfileAsync(
            dispensing.PatientId, ct);

        foreach (var warning in ScreenClinically(dispensing, line, profile,
                     product))
            dispensing.RaiseWarning(warning);

        return null;
    }

    /// <remarks>
    /// In the dispensing's units: a batch of so many packs holds so many
    /// tablets, less what earlier lines have claimed from it.
    /// </remarks>
    private List<BatchAllocation> Allocate(
        IReadOnlyList<Batch> batches, IReadOnlyDictionary<Guid, decimal> claimed,
        decimal quantity, decimal perPack)
    {
        var remaining = quantity;
        var result = new List<BatchAllocation>();

        foreach (var batch in batches
                     .Where(b => b.IsAvailableOn(_clock.Today))
                     .OrderBy(b => b.ExpiryDate)
                     .ThenBy(b => b.ReceivedAtUtc))
        {
            if (remaining <= 0m) break;

            var free = batch.QuantityOnHand * perPack
                       - claimed.GetValueOrDefault(batch.Id);
            if (free <= 0m) continue;

            var take = Math.Min(free, remaining);
            result.Add(new BatchAllocation(batch.Id, batch.BatchNumber,
                                           batch.ExpiryDate, take));
            remaining -= take;
        }

        return result;
    }

    /// <summary>
    /// The first item on the script for this product that the dispensing
    /// has not already answered.
    /// </summary>
    private static ScriptItem? NextScriptItemFor(Script? script,
        Domain.Dispensing.Dispensing dispensing, Guid productId) =>
        script?.Items
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.LineNo)
            .FirstOrDefault(i => dispensing.Lines
                .All(l => l.ScriptItemId != i.Id));

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

    public async Task<bool> OverrideNeedsPharmacistAsync(Guid warningId,
        CancellationToken ct = default)
    {
        var dispensing = await _dispensings.ByWarningAsync(warningId, ct);
        var warning = dispensing?.Warnings.SingleOrDefault(w => w.Id == warningId);

        return warning is { IsHardStop: true }
               && !ActsOnOwnAuthority;
    }

    public async Task<Result> AcknowledgeWarningAsync(Guid warningId,
        string? reason, Guid? authorisedBy = null,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.ByWarningAsync(warningId, ct);
        if (dispensing is null)
            return Result.Fail("DSP-003", "Warning not found.");

        var warning = dispensing.Warnings.Single(w => w.Id == warningId);
        Guid? releasedBy = null;

        // A hard stop is released on the operator's own written reason
        // where they act on their own authority (ActsOnOwnAuthority);
        // otherwise a pharmacist's credentials are needed as well.
        if (warning.IsHardStop && !ActsOnOwnAuthority)
        {
            if (authorisedBy is not Guid pharmacist
                || pharmacist == _user.Id
                || !_permissions.UserHolds(pharmacist,
                                           PermissionCode.ScriptAuthorise))
            {
                _audit.Refused("dsp.warning.override", "ClinicalWarning",
                    warningId, "No pharmacist authorised the override.");

                return Result.Fail("DSP-032",
                    "A pharmacist must authorise overriding this warning.");
            }

            releasedBy = pharmacist;
        }

        if (warning.IsHardStop && string.IsNullOrWhiteSpace(reason))
            return Result.Fail("DSP-033",
                "Give the reason for overriding this warning. It is kept "
                + "on the clinical record (DSP-007).");

        dispensing.AcknowledgeWarning(warningId, _user.Id, reason, releasedBy);

        // Named, so a clinical audit reads who released it without a lookup.
        var releasedByName = releasedBy is Guid by
            ? (await _users.GetAsync(by, ct))?.Username ?? by.ToString()
            : null;

        // Recorded whether or not it was an override: a pattern of
        // acknowledged interactions is clinical information (DSP-007).
        _audit.Applied(
            warning.IsHardStop ? "dsp.warning.override" : "dsp.warning.acknowledge",
            "ClinicalWarning", warningId,
            releasedByName is not null
                ? $"{reason} (authorised by {releasedByName})"
                : reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<IssueAuthorityDto>> IssueAuthorityAsync(
        Guid dispensingId, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result<IssueAuthorityDto>.Fail("DSP-000",
                "Dispensing not found.");

        var controlled = await ControlledItemsAsync(dispensing, ct);

        return Result<IssueAuthorityDto>.Ok(
            controlled.Count == 0 || ActsOnOwnAuthority
                ? new IssueAuthorityDto(true, null)
                : new IssueAuthorityDto(false,
                    $"{string.Join(", ", controlled)} "
                    + (controlled.Count == 1 ? "is a" : "are")
                    + " controlled medicine"
                    + (controlled.Count == 1 ? "" : "s")
                    + ". A pharmacist must authorise the issue."));
    }

    public async Task<Result<IssueResultDto>> IssueAsync(Guid dispensingId,
        Guid? authorisedBy = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

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

        // Who answers for the issue. The operator, on their own
        // authority, unless something on it needs a pharmacist: then the
        // pharmacist who authorised it, whose own grant is checked here
        // rather than trusted from the screen.
        var responsible = authorisedBy ?? _user.Id;

        if (responsible != Guid.Empty && responsible != _user.Id
            && !_permissions.UserHolds(responsible, PermissionCode.ScriptAuthorise))
        {
            _audit.Refused("dsp.script.issue", "Dispensing", dispensing.Id,
                "The authoriser is not a pharmacist.");

            return Result<IssueResultDto>.Fail("DSP-031",
                "The person who authorised this issue cannot authorise "
                + "dispensing. A pharmacist must authorise it.");
        }

        if (responsible == _user.Id
            && !ActsOnOwnAuthority
            && await ControlledItemsAsync(dispensing, ct) is { Count: > 0 } controlled)
        {
            return Result<IssueResultDto>.Fail("DSP-030",
                $"{string.Join(", ", controlled)}: a controlled medicine. A "
                + "pharmacist must authorise the issue.");
        }

        var number = await _numbering.NextAsync(DocumentSeries.Dispensing, ct);
        dispensing.Issue(responsible, number, _clock.UtcNow);
        script.RecordDispensing(_clock.Today);

        // The stock leaves the shelf now, batch by batch and on the
        // product's own on-hand, with a movement for every batch touched:
        // the same path a sale takes. It used to reduce the batch alone,
        // so the product and its batches drifted apart and the stock
        // ledger never heard of a dispensing (DEF-0008).
        await _stock.ApplyDispensingAsync(dispensing, ct);

        var patient = await _patients.GetAsync(dispensing.PatientId, ct);
        var pharmacist = await _users.GetAsync(responsible, ct);
        var scheme = patient?.MedicalAidSchemeId is Guid schemeId
            ? (await _reference.GetAsync(ReferenceKind.MedicalAidScheme, schemeId, ct))?.Name
            : null;

        // Counted in the bag, 1 of n: a line taken off before the issue
        // leaves a gap in the line numbers that a label must not show.
        var labels = dispensing.Lines.OrderBy(l => l.LineNo).Select((l, i) =>
            new LabelDto(
                PatientName: patient?.FullName ?? "",
                ProductDescription: l.Description,
                Quantity: l.Quantity,
                Directions: l.Directions,
                BatchNumber: l.BatchSummary,
                Expiry: l.BatchExpiry,
                PharmacistName: pharmacist?.DisplayName ?? "",
                ScriptNumber: script.ScriptNumber,
                DispensedOn: _clock.Today,
                Cautions: Array.Empty<string>(),
                ItemNumber: i + 1,
                ItemCount: dispensing.Lines.Count,
                PrescriberName: script.PrescriberName,
                PrescriberNumber: script.PrescriberNumber,
                SchemeName: scheme)).ToList();

        // The scheme's share of the bill, recognised now rather than
        // whenever somebody remembers to submit a batch. Inside the issue
        // transaction, so a dispensing that fails to save takes its claim
        // with it.
        var claimNumber = await RaiseClaimAsync(dispensing, patient, ct);

        _outbox.Append("Dispensing", dispensing.Id,
            $"{{\"id\":\"{dispensing.Id}\",\"script\":\"{script.ScriptNumber}\"}}");
        _audit.Applied("dsp.script.issue", "Dispensing", dispensing.Id,
            responsible == _user.Id
                ? null
                : $"Authorised by {pharmacist?.Username ?? responsible.ToString()}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<IssueResultDto>.Ok(new IssueResultDto(
            script.ScriptNumber, dispensing.DocumentNumber,
            dispensing.CoPaymentTotal.Amount, labels, claimNumber,
            TrailerLabels.For(dispensing, script, patient, _clock.Today)));
    }

    /// <remarks>
    /// The domain could reverse a dispensing and nothing called it, so a
    /// medicine issued to the wrong patient could not be taken back and
    /// the Reversed Scripts report was always empty (ADM-DEF-22).
    /// </remarks>
    public async Task<Result> ReverseAsync(Guid dispensingId, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptReverse);

        return await ReverseCoreAsync(dispensingId, reason, uncollectedOnly: false, ct);
    }

    /// <remarks>
    /// The cashier's permission, not the pharmacist's: a script left on
    /// Collect is the till's queue to tidy, and the medicine is still in
    /// the pharmacy to go back on the shelf.
    /// </remarks>
    public async Task<Result> ReverseUncollectedAsync(Guid dispensingId,
        string reason, CancellationToken ct = default)
    {
        if (!_permissions.Has(PermissionCode.ScriptReverse))
            _permissions.Demand(PermissionCode.SaleCreate);

        return await ReverseCoreAsync(dispensingId, reason, uncollectedOnly: true, ct);
    }

    private async Task<Result> ReverseCoreAsync(Guid dispensingId, string reason,
        bool uncollectedOnly, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("DSP-034", "A reversal needs a reason.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null)
            return Result.Fail("DSP-000", "Dispensing not found.");

        // Another till may have just taken the money for it.
        if (uncollectedOnly && !dispensing.AwaitsCollection)
            return Result.Fail("DSP-058",
                $"{dispensing.DocumentNumber} is no longer waiting to be collected.");

        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var claim = await _claims.ForDispensingAsync(dispensing.Id, ct);

        try
        {
            dispensing.Reverse(reason, _user.Id, _clock.UtcNow);
            script?.UndoDispensing();
            claim?.Withdraw($"{dispensing.DocumentNumber} reversed: {reason}",
                            _clock.UtcNow);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Fail("DSP-035", ex.Message);
        }

        // The packs that left, back to the batches they left from.
        await _stock.ApplyDispensingReversalAsync(dispensing, ct);

        // The scheme's revenue and debt come off the books with the claim.
        if (claim is not null)
        {
            var patient = await _patients.GetAsync(dispensing.PatientId, ct);

            var scheme = await _reference.GetAsync(
                ReferenceKind.MedicalAidScheme, claim.SchemeId, ct);

            var rates = new Dictionary<Guid, decimal>();

            foreach (var line in dispensing.Lines)
            {
                if (rates.ContainsKey(line.ProductId)) continue;

                var product = await _products.GetAsync(line.ProductId, ct);
                rates[line.ProductId] = product?.VatRatePercent ?? 0m;
            }

            await _journals.PostClaimReversalAsync(claim,
                scheme?.Name ?? patient?.FullName ?? "scheme",
                ClaimService.SplitOf(dispensing, rates), ct);
        }

        _audit.Applied("dsp.dispensing.reverse", "Dispensing", dispensing.Id,
            reason.Trim());

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result.Ok();
    }

    public async Task<Result> AbandonAsync(Guid dispensingId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        if (dispensing is null
            || dispensing.Status != DispensingStatus.InProgress)
            return Result.Ok();

        dispensing.Abandon();

        _audit.Applied("dsp.dispensing.abandon", "Dispensing", dispensing.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }


    public async Task<IReadOnlyList<ToFollowDto>> ToFollowAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var owed = new List<ToFollowDto>();

        foreach (var d in await _dispensings.OwingAsync(ct))
        {
            var patient = await _patients.GetAsync(d.PatientId, ct);

            owed.AddRange(d.Lines
                .Where(l => l.Owed is not null && l.OwedGivenAtUtc is null)
                .Select(l => new ToFollowDto(d.Id, l.LineNo,
                    patient?.FullName ?? "", patient?.Telephone ?? "",
                    l.Description, l.Owed!.Value,
                    d.IssuedAtUtc?.ToLocalTime() ?? default, d.DocumentNumber)));
        }

        return owed.OrderBy(o => o.Since).ToList();
    }

    public async Task<Result> RecordLabelExpiryAsync(Guid dispensingId,
        int itemNumber, DateOnly? expiry, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        var line = dispensing?.Lines.OrderBy(l => l.LineNo)
            .ElementAtOrDefault(itemNumber - 1);
        if (line is null) return Result.Fail("DSP-000", "Dispensing not found.");

        if (expiry is DateOnly typed)
        {
            if (typed <= _clock.Today)
                return Result.Fail("DSP-061",
                    $"{typed:dd/MM/yyyy} has passed. Do not hand over expired stock.");

            line.RecordLabelExpiry(typed);
            _audit.Applied("disp.label.expiry-entered", "Dispensing", dispensingId,
                $"{line.Description}: expiry {typed:dd/MM/yyyy} typed from the box");
        }
        else
        {
            _audit.Applied("disp.label.no-expiry", "Dispensing", dispensingId,
                $"{line.Description}: label printed without an expiry");
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> RecordFoundBatchAsync(Guid productId,
        string batchNumber, DateOnly expiry, decimal units,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        var number = (batchNumber ?? "").Trim();
        if (number.Length is < 1 or > 50)
            return Result.Fail("DSP-061", "Enter the batch number printed on the box.");

        if (expiry <= _clock.Today)
            return Result.Fail("DSP-061",
                $"Batch {number} expired on {expiry:dd/MM/yyyy}. Do not hand it over.");

        if (units <= 0m)
            return Result.Fail("DSP-061", "Say how many are being handed over.");

        var product = await _products.GetAsync(productId, ct);
        if (product is null) return Result.Fail("DSP-001", "Product not found.");

        // A recalled or quarantined batch in hand must not be handed over,
        // and allocation would not take it anyway.
        if ((await _batches.ForProductAsync(product.Id, ct)).FirstOrDefault(b =>
                b.ExpiryDate == expiry && b.Status != Domain.Batches.BatchStatus.Available
                && string.Equals(b.BatchNumber, number, StringComparison.OrdinalIgnoreCase))
            is { } blocked)
            return Result.Fail("DSP-061",
                $"Batch {number} is {blocked.Status.ToString().ToLowerInvariant()}"
                + (blocked.StatusReason is null ? "" : $" ({blocked.StatusReason})")
                + ". Do not hand it over.");

        // In the product's own packs: the script's pack size can differ.
        var packs = Math.Ceiling(units / Math.Max(product.UnitsInPack, 1m));

        await _stock.RecordFoundBatchAsync(product, number, expiry, packs, ct);

        _audit.Applied("inv.stock.adjust", "Product", product.Id,
            $"Found at dispensing: batch {number}, expiry {expiry:dd/MM/yyyy}, "
            + $"{packs:0.###} pack(s)");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<LabelDto>> GiveOwedAsync(Guid dispensingId,
        int lineNo, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);
        var line = dispensing?.Lines.SingleOrDefault(l => l.LineNo == lineNo);

        if (dispensing is not { Status: DispensingStatus.Issued } || line is null)
            return Result<LabelDto>.Fail("DSP-000", "Dispensing not found.");

        var before = line.Batches.Count;

        try
        {
            await _stock.ApplyOwedAsync(dispensing, line, ct);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result<LabelDto>.Fail("DSP-041", ex.Message);
        }

        _audit.Applied("dsp.tofollow.give", "Dispensing", dispensing.Id,
            $"{line.Description} x{line.Owed:0.###}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        // Labelled for what is handed over now, from the batches it came off.
        var given = line.Batches.Skip(before).OrderBy(b => b.ExpiryDate).ToList();
        var script = await _scripts.GetAsync(dispensing.ScriptId, ct);
        var patient = await _patients.GetAsync(dispensing.PatientId, ct);
        var pharmacist = await _users.GetAsync(_user.Id, ct);

        return Result<LabelDto>.Ok(new LabelDto(
            PatientName: patient?.FullName ?? "",
            ProductDescription: line.Description,
            Quantity: line.Owed ?? 0m,
            Directions: line.Directions,
            BatchNumber: given.Count == 0 ? null : string.Join(", ", given.Select(b => b.BatchNumber)),
            Expiry: given.Count == 0 ? null : given[0].ExpiryDate,
            PharmacistName: pharmacist?.DisplayName ?? "",
            ScriptNumber: script?.ScriptNumber ?? "",
            DispensedOn: _clock.Today,
            Cautions: Array.Empty<string>(),
            // Numbered as on the bag, so an expiry typed for it finds its line.
            ItemNumber: dispensing.Lines.OrderBy(l => l.LineNo).ToList().IndexOf(line) + 1,
            ItemCount: dispensing.Lines.Count,
            PrescriberName: script?.PrescriberName,
            PrescriberNumber: script?.PrescriberNumber));
    }

    /// <summary>
    /// How long a script begun and not issued is kept to finish later.
    /// </summary>
    private static readonly TimeSpan KeptFor = TimeSpan.FromHours(24);

    /// <summary>
    /// Sets aside every dispensing begun or last picked up more than a day
    /// ago and never issued, those already on file included. Nothing left
    /// the shelf for them, so nothing is put back.
    /// </summary>
    /// <remarks>
    /// The time gone by since it was begun or resumed (UpdatedAtUtc), both
    /// ends in UTC, so a script begun late in the evening still has its
    /// full 24 hours, and one picked up again is not set aside while it is
    /// being finished. Issued scripts waiting to be collected are not in
    /// progress and are not touched.
    /// </remarks>
    private async Task ClearStaleAsync(CancellationToken ct)
    {
        var stale = (await _dispensings.InProgressAsync(ct))
            .Where(d => _clock.UtcNow - d.UpdatedAtUtc > KeptFor)
            .ToList();

        if (stale.Count == 0) return;

        foreach (var dispensing in stale)
        {
            dispensing.Abandon();
            _audit.Applied("dsp.dispensing.abandon", "Dispensing", dispensing.Id,
                "Not finished within 24 hours of being begun.");
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SuspendedDispensingDto>> SuspendedAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptDispense);

        // Only today's: one begun more than a day ago is set aside first.
        await ClearStaleAsync(ct);

        var kept = new List<SuspendedDispensingDto>();

        foreach (var d in await _dispensings.InProgressAsync(ct))
        {
            var patient = await _patients.GetAsync(d.PatientId, ct);
            var script = await _scripts.GetAsync(d.ScriptId, ct);

            kept.Add(new SuspendedDispensingDto(d.PatientId, d.ScriptId,
                patient?.FullName ?? "", script?.ScriptNumber ?? "", d.Lines.Count));
        }

        return kept;
    }

    /// <summary>
    /// The controlled medicines on a dispensing, by description.
    /// </summary>
    private async Task<IReadOnlyList<string>> ControlledItemsAsync(
        Domain.Dispensing.Dispensing dispensing, CancellationToken ct)
    {
        var controlled = new List<string>();

        foreach (var line in dispensing.Lines)
        {
            var product = await _products.GetAsync(line.ProductId, ct);

            if (product is { Schedule: >= ControlledSchedule })
                controlled.Add(line.Description);
        }

        return controlled;
    }

    /// <summary>
    /// The clinical screen. Deliberately a pure function of what is on the
    /// dispensing and what is on the profile, so that it can be tested
    /// exhaustively without a database.
    /// </summary>
    private static IEnumerable<ClinicalWarning> ScreenClinically(
        Domain.Dispensing.Dispensing dispensing, DispensingLine line,
        ClinicalProfile? profile, Domain.Inventory.Product product)
    {
        // By drug class, not by spelling: a penicillin allergy stops
        // amoxicillin (DEF-0006). The product's generic name is read too,
        // where the catalogue has one.
        if (profile is not null)
        {
            foreach (var match in profile.AllergiesAgainst(product.Description,
                                                           product.GenericName))
            {
                yield return ClinicalWarning.Raise("Allergy",
                    match.IsHardStop
                        ? WarningSeverity.HardStop
                        : WarningSeverity.Caution,
                    match.Explanation, line.Id);
            }
        }

        var duplicate = dispensing.Lines
            .Any(l => l.Id != line.Id && l.ProductId == line.ProductId);

        if (duplicate)
            yield return ClinicalWarning.Raise("Duplication",
                WarningSeverity.Caution,
                $"{line.Description} already appears on this dispensing.",
                line.Id);

        if (profile?.IsPregnant == true && product.Schedule >= 3)
            yield return ClinicalWarning.Raise("Pregnancy",
                WarningSeverity.Caution,
                $"Patient is recorded as pregnant. Check {line.Description} "
                + "against the pregnancy category.",
                line.Id);
    }

    /// <summary>
    /// Bills the scheme for its share of what has just been dispensed.
    /// </summary>
    /// <remarks>
    /// Quiet where there is nothing to claim: a cash patient, or a
    /// script the scheme contributes nothing to. Quiet too where a claim
    /// already exists, which is what a retry of a part-completed issue
    /// looks like - the unique index on DispensingId would refuse the
    /// second one, and refusing to issue medicine over a duplicate
    /// claim would be the wrong way round.
    /// </remarks>
    /// <returns>
    /// The claim's number, so the dispensary can print the claim form
    /// for it; null where nothing is claimed.
    /// </returns>
    private async Task<string?> RaiseClaimAsync(
        Domain.Dispensing.Dispensing dispensing,
        Domain.Patients.Patient? patient, CancellationToken ct)
    {
        if (patient is null || !patient.IsOnMedicalAid) return null;
        if (dispensing.ContributionTotal.IsZero) return null;

        if (await _claims.ForDispensingAsync(dispensing.Id, ct) is { } raised)
            return raised.ClaimNumber;

        var number = await _numbering.NextAsync(DocumentSeries.Claim, ct);

        var claim = ClaimService.Build(number, patient, dispensing,
                                       _clock.Today);

        if (claim is null) return null;

        await _claims.AddAsync(claim, ct);

        // Per product, because a script can mix VAT rates and one rate
        // across the claim would misstate what is owed on every mixed
        // script the pharmacy dispenses.
        var rates = new Dictionary<Guid, decimal>();

        foreach (var line in dispensing.Lines)
        {
            if (rates.ContainsKey(line.ProductId)) continue;

            var product = await _products.GetAsync(line.ProductId, ct);
            rates[line.ProductId] = product?.VatRatePercent ?? 0m;
        }

        // The scheme's name, not its id: the journal narration is read
        // by a person reconciling a remittance, and a row of hex would
        // tell them nothing.
        var scheme = await _reference.GetAsync(
            ReferenceKind.MedicalAidScheme,
            patient.MedicalAidSchemeId!.Value, ct);

        await _journals.PostClaimAsync(claim, scheme?.Name ?? "scheme",
            ClaimService.SplitOf(dispensing, rates), ct);

        return claim.ClaimNumber;
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

    /// Everything dispensed to one patient, which is what a reprint
    /// starts from: the person, then which of their items to print again.
    Task<IReadOnlyList<Domain.Dispensing.Dispensing>> ForPatientAsync(
        Guid patientId, CancellationToken ct = default);

    /// Issued during a UTC range, newest first. The day's labels.
    Task<IReadOnlyList<Domain.Dispensing.Dispensing>> IssuedBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>
    /// Dispensings that have been issued and still owe a co-payment,
    /// oldest first - the queue a cashier works from.
    /// </summary>
    Task<IReadOnlyList<Domain.Dispensing.Dispensing>> AwaitingCollectionAsync(
        CancellationToken ct = default);

    /// Issued, with something still owed to the patient.
    Task<IReadOnlyList<Domain.Dispensing.Dispensing>> OwingAsync(
        CancellationToken ct = default);

    /// Begun and not yet issued or set aside: kept to finish later.
    Task<IReadOnlyList<Domain.Dispensing.Dispensing>> InProgressAsync(
        CancellationToken ct = default);

    Task AddAsync(Domain.Dispensing.Dispensing dispensing,
                  CancellationToken ct = default);
}

public interface IScriptRepository
{
    Task<Script?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Script>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default);

    /// Every patient's scripts captured in the window and whose number
    /// has the text in it, newest first. Either may be left open.
    Task<IReadOnlyList<Script>> FindAsync(string term, DateTime? fromUtc,
        DateTime? toUtc, int take, CancellationToken ct = default);

    Task AddAsync(Script script, CancellationToken ct = default);
}

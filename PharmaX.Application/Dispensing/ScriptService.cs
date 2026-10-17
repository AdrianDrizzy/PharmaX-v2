using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Dispensing;

/// <param name="PackUnits">
/// The units in the pack the line is priced by, set for this script;
/// null is the product's own.
/// </param>
/// <param name="PackPrice">
/// That pack's price for this script; null is the shelf price. The
/// patient pays PackPrice x Quantity / PackUnits for the line.
/// </param>
public sealed record ScriptItemEditDto(string ProductCode, decimal Quantity,
                                       string Directions,
                                       bool AllowSubstitution = true,
                                       decimal? PackUnits = null,
                                       decimal? PackPrice = null);

/// <param name="QuotationId">
/// The dispensary quote the script was captured from, which is marked
/// taken up in the same save so it cannot become a second script.
/// </param>
public sealed record ScriptEditDto(
    Guid PatientId, string PrescriberName, string? PrescriberNumber,
    DateOnly PrescribedOn, DateOnly? ValidUntil, int RepeatsAuthorised,
    IReadOnlyList<ScriptItemEditDto> Items, Guid? QuotationId = null);

public sealed record ScriptItemDto(int LineNo, Guid ProductId,
    string Description, decimal Quantity, string Directions,
    bool AllowSubstitution, string? ProductCode = null,
    decimal? PackUnits = null, decimal? PackPrice = null)
{
    public static ScriptItemDto From(ScriptItem i, string? productCode = null)
    {
        ArgumentNullException.ThrowIfNull(i);

        return new ScriptItemDto(i.LineNo, i.ProductId, i.Description,
            i.Quantity, i.Directions, i.AllowSubstitution, productCode,
            i.PackUnits, i.PackPrice?.Amount);
    }
}

/// <param name="PrescriberNumber">The prescriber's registration.</param>
/// <param name="IsEditable">
/// Whether the script can still be corrected: captured, and nothing
/// dispensed or being dispensed against it.
/// </param>
public sealed record ScriptDetailDto(ScriptDto Script,
    IReadOnlyList<ScriptItemDto> Items, string? PrescriberNumber = null,
    bool IsEditable = false);

/// <summary>
/// One item a patient has had before: what was last handed over against a
/// script, or what it prescribed where it was never dispensed.
/// </summary>
/// <param name="PackUnits">The pack and price set on the script it came
/// from, carried onto the new one; null where that script used the
/// product's own.</param>
public sealed record SuppliedItemDto(string ProductCode, string Description,
    decimal Quantity, string Directions, bool AllowSubstitution, DateOnly On,
    string ScriptNumber, decimal? PackUnits = null, decimal? PackPrice = null)
{
    public ScriptItemEditDto ToEdit() =>
        new(ProductCode, Quantity, Directions, AllowSubstitution,
            PackUnits, PackPrice);
}

public interface IScriptService
{
    Task<Result<IReadOnlyList<ScriptDto>>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default);

    Task<Result<ScriptDetailDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<Guid>> CaptureAsync(ScriptEditDto edit,
        CancellationToken ct = default);

    /// <summary>
    /// A new script drafted from an earlier one: the same prescriber and
    /// items, dated today, at the quantities last handed over against it.
    /// Nothing is saved until the draft is captured.
    /// </summary>
    Task<Result<ScriptEditDto>> RepeatAsync(Guid scriptId,
        CancellationToken ct = default);

    /// <summary>
    /// Everything the patient has had, newest first and imported history
    /// included, to pick onto a script being captured.
    /// </summary>
    Task<Result<IReadOnlyList<SuppliedItemDto>>> SuppliedAsync(Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Corrects a script captured and not yet dispensed against.
    /// </summary>
    Task<Result> UpdateAsync(Guid id, ScriptEditDto edit,
        CancellationToken ct = default);

    /// <summary>
    /// The script as a copy prints: what was prescribed, and what has
    /// been dispensed against it.
    /// </summary>
    Task<Result<Sales.PrintableDocument>> PrintoutAsync(Guid id,
        CancellationToken ct = default);

    Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default);
}

public sealed class ScriptService : IScriptService
{
    private readonly IScriptRepository _scripts;
    private readonly IDispensingRepository _dispensings;
    private readonly IPatientRepository _patients;
    private readonly IProductRepository _products;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    private readonly Sales.IQuotationRepository _quotations;

    // Writes dosage shorthand out in full; absent in tests that do not
    // care about directions.
    private readonly Reference.IReferenceRepository? _reference;

    public ScriptService(IScriptRepository scripts,
        IDispensingRepository dispensings,
        IPatientRepository patients, IProductRepository products,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow, IClock clock,
        Sales.IQuotationRepository quotations,
        Reference.IReferenceRepository? reference = null)
    {
        _quotations = quotations;
        _reference = reference;
        _scripts = scripts;
        _dispensings = dispensings;
        _patients = patients;
        _products = products;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ScriptDto>>> ForPatientAsync(
        Guid patientId, CancellationToken ct = default)
    {
        // Reading a patient's scripts is part of capturing one and of
        // dispensing against one, so either grant will do.
        DemandScriptAccess();

        var scripts = await _scripts.ForPatientAsync(patientId, ct);

        IReadOnlyList<ScriptDto> items =
            scripts.Select(ScriptDto.From).ToList();

        return Result<IReadOnlyList<ScriptDto>>.Ok(items);
    }

    public async Task<Result<ScriptDetailDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        DemandScriptAccess();

        var script = await _scripts.GetAsync(id, ct);

        if (script is null)
            return Result<ScriptDetailDto>.Fail("DSP-000", "Script not found.");

        // The code, so an editor can put back what was typed.
        var items = new List<ScriptItemDto>();

        foreach (var item in script.Items.OrderBy(i => i.LineNo))
            items.Add(ScriptItemDto.From(item,
                (await _products.GetAsync(item.ProductId, ct))?.Code));

        return Result<ScriptDetailDto>.Ok(new ScriptDetailDto(
            ScriptDto.From(script), items, script.PrescriberNumber,
            await IsEditableAsync(script, ct)));
    }

    /// Captured, and no dispensing under way or issued against it.
    private async Task<bool> IsEditableAsync(Script script,
        CancellationToken ct) =>
        script.IsEditable
        && !(await _dispensings.ForPatientAsync(script.PatientId, ct))
            .Any(d => d.ScriptId == script.Id
                      && d.Status is DispensingStatus.InProgress
                          or DispensingStatus.Issued);

    public async Task<Result<Guid>> CaptureAsync(ScriptEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // Capture is its own permission. It demanded the pharmacist's
        // clinical authority, so an assistant could not type in the
        // script the patient had just handed over and every capture
        // became a supervisor prompt.
        _permissions.Demand(PermissionCode.ScriptCapture);

        if (await _patients.GetAsync(edit.PatientId, ct) is null)
            return Result<Guid>.Fail("DSP-020", "Patient not found.");

        if (edit.Items.Count == 0)
            return Result<Guid>.Fail("DSP-021",
                "A script with no items is not a script.");

        var checkedEdit = await ResolveAsync(edit, ct);

        if (checkedEdit.Problem is Error problem)
            return Result<Guid>.Fail(problem.Code, problem.Message);

        // A script captured from a quote takes the quote up. Asked here,
        // before anything is written, whether it still can be.
        Domain.Sales.Quotation? quote = null;

        if (edit.QuotationId is Guid quotationId)
        {
            quote = await _quotations.GetAsync(quotationId, ct);

            if (quote is null)
                return Result<Guid>.Fail("DSP-036", "That quote is no longer on file.");

            try
            {
                quote.EnsureConvertibleToScript(_clock.Today);
            }
            catch (Domain.Exceptions.DomainException ex)
            {
                return Result<Guid>.Fail("DSP-037", ex.Message);
            }
        }

        // Everything that could refuse the script has been asked above,
        // and the number is claimed inside the same transaction as the
        // script it names. It used to be taken first, so each refused
        // save - no directions, then no prescriber - used a number that
        // then existed nowhere (ADM-DEF-13).
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var number = await _numbering.NextAsync(DocumentSeries.Script, ct);

        var script = Script.Capture(number, edit.PatientId,
            edit.PrescriberName, edit.PrescribedOn, edit.RepeatsAuthorised,
            edit.ValidUntil, edit.PrescriberNumber);

        foreach (var item in checkedEdit.Items)
            script.AddItem(item);

        await _scripts.AddAsync(script, ct);

        if (quote is not null)
        {
            quote.ConvertToScript(script.Id, edit.PatientId, _clock.Today);

            _audit.Applied("dsp.quote.convert", "Quotation", quote.Id,
                $"{quote.QuotationNumber} became script {number}");
        }

        _audit.Applied("dsp.script.capture", "Script", script.Id,
            $"{checkedEdit.Items.Count} item(s), {edit.RepeatsAuthorised} "
            + "repeat(s)");
        _outbox.Append("Script", script.Id,
            $"{{\"id\":\"{script.Id}\",\"number\":\"{number}\"}}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<Guid>.Ok(script.Id);
    }

    public async Task<Result<ScriptEditDto>> RepeatAsync(Guid scriptId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptCapture);

        var script = await _scripts.GetAsync(scriptId, ct);
        if (script is null)
            return Result<ScriptEditDto>.Fail("DSP-000", "Script not found.");

        var items = await SuppliedFromAsync(script,
            await _dispensings.ForPatientAsync(script.PatientId, ct),
            new Dictionary<Guid, string?>(), ct);

        if (items.Count == 0)
            return Result<ScriptEditDto>.Fail("DSP-021",
                $"Nothing on script {script.ScriptNumber} is still in the "
                + "catalogue to use again.");

        return Result<ScriptEditDto>.Ok(new ScriptEditDto(script.PatientId,
            script.PrescriberName, script.PrescriberNumber, _clock.Today,
            ValidUntil: null, RepeatsAuthorised: 0,
            items.Select(i => i.ToEdit()).ToList()));
    }

    public async Task<Result<IReadOnlyList<SuppliedItemDto>>> SuppliedAsync(
        Guid patientId, CancellationToken ct = default)
    {
        DemandScriptAccess();

        var dispensings = await _dispensings.ForPatientAsync(patientId, ct);
        var codes = new Dictionary<Guid, string?>();
        var rows = new List<SuppliedItemDto>();

        foreach (var script in await _scripts.ForPatientAsync(patientId, ct))
        {
            if (script.Status == ScriptStatus.Cancelled) continue;
            rows.AddRange(await SuppliedFromAsync(script, dispensings, codes, ct));
        }

        return Result<IReadOnlyList<SuppliedItemDto>>.Ok(
            rows.OrderByDescending(r => r.On).ToList());
    }

    /// <summary>
    /// What one script supplied, item by item: as last handed over where
    /// it was dispensed - the quantity the pharmacist actually gave, which
    /// may differ from the prescriber's - else as it was prescribed. An
    /// item no longer in the catalogue is left out: it cannot go on a new
    /// script.
    /// </summary>
    private async Task<List<SuppliedItemDto>> SuppliedFromAsync(Script script,
        IReadOnlyList<Domain.Dispensing.Dispensing> dispensings,
        Dictionary<Guid, string?> codes, CancellationToken ct)
    {
        async Task<string?> CodeAsync(Guid productId)
        {
            if (!codes.TryGetValue(productId, out var code))
                codes[productId] = code =
                    (await _products.GetAsync(productId, ct))?.Code;

            return code;
        }

        var last = dispensings
            .Where(d => d.ScriptId == script.Id
                        && d.Status == DispensingStatus.Issued)
            .MaxBy(d => d.IssuedAtUtc);

        var rows = new List<SuppliedItemDto>();

        if (last is not null)
        {
            var on = DateOnly.FromDateTime(last.IssuedAtUtc!.Value.ToLocalTime());

            foreach (var line in last.Lines.OrderBy(l => l.LineNo))
            {
                if (await CodeAsync(line.ProductId) is not string code) continue;

                var item = script.Items.FirstOrDefault(i => i.Id == line.ScriptItemId);

                // The pack and price set on the script go with the line
                // only while it is the product prescribed: a substitute
                // is packed and priced its own way.
                var priced = item?.ProductId == line.ProductId ? item : null;

                rows.Add(new SuppliedItemDto(code, line.Description,
                    line.Quantity, line.Directions,
                    item?.AllowSubstitution ?? true, on, script.ScriptNumber,
                    priced?.PackUnits, priced?.PackPrice?.Amount));
            }

            return rows;
        }

        foreach (var item in script.Items.OrderBy(i => i.LineNo))
        {
            if (await CodeAsync(item.ProductId) is not string code) continue;

            rows.Add(new SuppliedItemDto(code, item.Description, item.Quantity,
                item.Directions, item.AllowSubstitution, script.PrescribedOn,
                script.ScriptNumber, item.PackUnits, item.PackPrice?.Amount));
        }

        return rows;
    }

    public async Task<Result> UpdateAsync(Guid id, ScriptEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.ScriptCapture);

        var script = await _scripts.GetAsync(id, ct);
        if (script is null) return Result.Fail("DSP-000", "Script not found.");

        if (edit.PatientId != script.PatientId)
            return Result.Fail("DSP-027",
                "A script stays with the patient it was written for.");

        if (!await IsEditableAsync(script, ct))
            return Result.Fail("DSP-028",
                $"Script {script.ScriptNumber} is being or has been "
                + "dispensed against, or is cancelled, so it can no longer "
                + "be changed. Cancel it and capture it again.");

        var checkedEdit = await ResolveAsync(edit, ct);

        if (checkedEdit.Problem is Error problem)
            return Result.Fail(problem.Code, problem.Message);

        script.Amend(edit.PrescriberName, edit.PrescriberNumber,
            edit.PrescribedOn, edit.RepeatsAuthorised, edit.ValidUntil,
            checkedEdit.Items);

        _audit.Applied("dsp.script.amend", "Script", script.Id,
            $"{checkedEdit.Items.Count} item(s), {edit.RepeatsAuthorised} "
            + "repeat(s)");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Checks a script as captured or corrected, and turns its lines into
    /// items. Nothing is created or numbered until this passes.
    /// </summary>
    private async Task<(List<ScriptItem> Items, Error? Problem)> ResolveAsync(
        ScriptEditDto edit, CancellationToken ct)
    {
        var items = new List<ScriptItem>();

        if (await _patients.GetAsync(edit.PatientId, ct) is null)
            return (items, new Error("DSP-020", "Patient not found."));

        if (edit.Items.Count == 0)
            return (items, new Error("DSP-021",
                "A script with no items is not a script."));

        if (edit.PrescribedOn > _clock.Today)
            return (items, new Error("DSP-022",
                "A script cannot be prescribed in the future."));

        if (Script.Refuse(edit.PrescriberName, edit.PrescribedOn,
                edit.RepeatsAuthorised, edit.ValidUntil) is string particulars)
            return (items, new Error("DSP-029", particulars));

        foreach (var item in edit.Items)
        {
            var product = await _products
                .FindByCodeOrBarcodeAsync(item.ProductCode,
                    _permissions.ProductScope(), ct);

            if (product is null)
                return (items, new Error("DSP-023",
                    $"No product for code {item.ProductCode}."));

            if (item.Quantity <= 0m)
                return (items, new Error("DSP-024",
                    $"{product.Description}: quantity must be more than zero."));

            if (string.IsNullOrWhiteSpace(item.Directions))
                return (items, new Error("DSP-025",
                    $"{product.Description}: directions are mandatory, "
                    + "because they are printed on the label (DSP-004)."));

            if (item.PackUnits is decimal units && units <= 0m)
                return (items, new Error("DSP-055",
                    $"{product.Description}: the pack must hold more than "
                    + "nothing, or be left blank for the product's own."));

            if (item.PackPrice is decimal price && price < 0m)
                return (items, new Error("DSP-056",
                    $"{product.Description}: the price cannot be less than "
                    + "nothing."));

            var directions = _reference is null
                ? item.Directions.Trim()
                : await Reference.DosageShorthand.ExpandDirectionsAsync(
                    _reference, item.Directions, ct);

            // A pack or price the same as the product's is no change, and
            // is not kept: the script follows the product if it is repriced.
            var packUnits = item.PackUnits == product.UnitsInPack ? null : item.PackUnits;
            var packPrice = item.PackPrice is decimal amount
                            && amount != product.RetailPrice.Amount
                ? Domain.Primitives.Money.Of(amount, product.RetailPrice.Currency)
                : (Domain.Primitives.Money?)null;

            // Both kept once either differs, so the line is priced by the
            // pack it was set against.
            if (packUnits is not null || packPrice is not null)
            {
                packUnits ??= item.PackUnits ?? product.UnitsInPack;
                packPrice ??= Domain.Primitives.Money.Of(
                    item.PackPrice ?? product.RetailPrice.Amount,
                    product.RetailPrice.Currency);
            }

            items.Add(ScriptItem.Create(product.Id, product.Description,
                item.Quantity, directions, item.AllowSubstitution,
                packUnits, packPrice));
        }

        return (items, null);
    }

    /// <remarks>
    /// Re-Print Script opened the same list as Script Maintenance and
    /// printed nothing (ADM-DEF-17). This is the page it prints: the
    /// prescription as written, and each dispensing against it.
    /// </remarks>
    public async Task<Result<Sales.PrintableDocument>> PrintoutAsync(Guid id,
        CancellationToken ct = default)
    {
        DemandScriptAccess();

        var script = await _scripts.GetAsync(id, ct);
        if (script is null)
            return Result<Sales.PrintableDocument>.Fail("DSP-000",
                "Script not found.");

        var patient = await _patients.GetAsync(script.PatientId, ct);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        var lines = new List<string>
        {
            $"Patient: {patient?.FullName ?? ""}"
            + (patient is null ? "" : $" ({patient.FileNumber})"),
            $"Prescriber: {script.PrescriberName}"
            + (script.PrescriberNumber is null
                ? ""
                : $"  Reg {script.PrescriberNumber}"),
            string.Format(culture, "Written {0:dd/MM/yyyy}, valid to {1:dd/MM/yyyy}",
                script.PrescribedOn, script.ValidUntil),
            string.Format(culture, "Dispensed {0} of {1} time(s)   {2}",
                script.RepeatsUsed, script.RepeatsAuthorised + 1,
                script.Status),
            new string('-', 42)
        };

        foreach (var item in script.Items.OrderBy(i => i.LineNo))
        {
            lines.Add(string.Format(culture, "{0}. {1}  x{2:0.###}",
                item.LineNo, item.Description, item.Quantity));
            lines.Add("   " + item.Directions);
        }

        var dispensed = (await _dispensings.ForPatientAsync(script.PatientId, ct))
            .Where(d => d.ScriptId == script.Id
                        && d.Status is DispensingStatus.Issued
                            or DispensingStatus.Reversed)
            .OrderBy(d => d.IssuedAtUtc)
            .ToList();

        if (dispensed.Count > 0)
        {
            lines.Add(new string('-', 42));
            lines.Add("Dispensed against it:");

            foreach (var d in dispensed)
                lines.Add(string.Format(culture, "  {0}  {1:dd/MM/yyyy}{2}",
                    d.DocumentNumber, d.IssuedAtUtc?.ToLocalTime(),
                    d.Status == DispensingStatus.Reversed ? "  REVERSED" : ""));
        }

        _audit.Applied("dsp.script.reprint", "Script", script.Id);
        await _uow.SaveChangesAsync(ct);

        return Result<Sales.PrintableDocument>.Ok(new Sales.PrintableDocument(
            $"Script {script.ScriptNumber}", lines));
    }

    private void DemandScriptAccess()
    {
        if (_permissions.HasAny(PermissionCode.ScriptCapture,
                                PermissionCode.ScriptDispense))
            return;

        _permissions.Demand(PermissionCode.ScriptCapture);
    }

    public async Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default)
    {
        // Cancelling is a reversal, so it needs the reversal permission
        // rather than the capture one.
        _permissions.Demand(PermissionCode.ScriptReverse);

        var script = await _scripts.GetAsync(id, ct);
        if (script is null) return Result.Fail("DSP-000", "Script not found.");

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("DSP-026", "Cancelling a script needs a reason.");

        script.Cancel(reason);

        _audit.Applied("dsp.script.reverse", "Script", script.Id, reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }
}

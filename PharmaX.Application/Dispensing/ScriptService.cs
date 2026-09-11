using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Dispensing;

public sealed record ScriptItemEditDto(string ProductCode, decimal Quantity,
                                       string Directions,
                                       bool AllowSubstitution = true);

public sealed record ScriptEditDto(
    Guid PatientId, string PrescriberName, string? PrescriberNumber,
    DateOnly PrescribedOn, DateOnly? ValidUntil, int RepeatsAuthorised,
    IReadOnlyList<ScriptItemEditDto> Items);

public sealed record ScriptItemDto(int LineNo, Guid ProductId,
    string Description, decimal Quantity, string Directions,
    bool AllowSubstitution)
{
    public static ScriptItemDto From(ScriptItem i)
    {
        ArgumentNullException.ThrowIfNull(i);

        return new ScriptItemDto(i.LineNo, i.ProductId, i.Description,
            i.Quantity, i.Directions, i.AllowSubstitution);
    }
}

public sealed record ScriptDetailDto(ScriptDto Script,
    IReadOnlyList<ScriptItemDto> Items);

public interface IScriptService
{
    Task<Result<IReadOnlyList<ScriptDto>>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default);

    Task<Result<ScriptDetailDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<Guid>> CaptureAsync(ScriptEditDto edit,
        CancellationToken ct = default);

    Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default);
}

public sealed class ScriptService : IScriptService
{
    private readonly IScriptRepository _scripts;
    private readonly IPatientRepository _patients;
    private readonly IProductRepository _products;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ScriptService(IScriptRepository scripts,
        IPatientRepository patients, IProductRepository products,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow, IClock clock)
    {
        _scripts = scripts;
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
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var scripts = await _scripts.ForPatientAsync(patientId, ct);

        IReadOnlyList<ScriptDto> items =
            scripts.Select(ScriptDto.From).ToList();

        return Result<IReadOnlyList<ScriptDto>>.Ok(items);
    }

    public async Task<Result<ScriptDetailDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ScriptAuthorise);

        var script = await _scripts.GetAsync(id, ct);

        return script is null
            ? Result<ScriptDetailDto>.Fail("DSP-000", "Script not found.")
            : Result<ScriptDetailDto>.Ok(new ScriptDetailDto(
                ScriptDto.From(script),
                script.Items.Select(ScriptItemDto.From).ToList()));
    }

    public async Task<Result<Guid>> CaptureAsync(ScriptEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.ScriptAuthorise);

        if (await _patients.GetAsync(edit.PatientId, ct) is null)
            return Result<Guid>.Fail("DSP-020", "Patient not found.");

        if (edit.Items.Count == 0)
            return Result<Guid>.Fail("DSP-021",
                "A script with no items is not a script.");

        if (edit.PrescribedOn > _clock.Today)
            return Result<Guid>.Fail("DSP-022",
                "A script cannot be prescribed in the future.");

        // Resolved before anything is created, so a mistyped code does
        // not leave a half-captured script behind.
        var resolved = new List<(Domain.Inventory.Product Product,
                                 ScriptItemEditDto Item)>();

        foreach (var item in edit.Items)
        {
            var product = await _products
                .FindByCodeOrBarcodeAsync(item.ProductCode, ct);

            if (product is null)
                return Result<Guid>.Fail("DSP-023",
                    $"No product for code {item.ProductCode}.");

            if (item.Quantity <= 0m)
                return Result<Guid>.Fail("DSP-024",
                    $"{product.Description}: quantity must be more than zero.");

            if (string.IsNullOrWhiteSpace(item.Directions))
                return Result<Guid>.Fail("DSP-025",
                    $"{product.Description}: directions are mandatory, "
                    + "because they are printed on the label (DSP-004).");

            resolved.Add((product, item));
        }

        var number = await _numbering.NextAsync("SCR", ct);

        var script = Script.Capture(number, edit.PatientId,
            edit.PrescriberName, edit.PrescribedOn, edit.RepeatsAuthorised,
            edit.ValidUntil);

        foreach (var (product, item) in resolved)
            script.AddItem(ScriptItem.Create(product.Id, product.Description,
                item.Quantity, item.Directions, item.AllowSubstitution));

        await _scripts.AddAsync(script, ct);

        _audit.Applied("dsp.script.capture", "Script", script.Id,
            $"{resolved.Count} item(s), {edit.RepeatsAuthorised} repeat(s)");
        _outbox.Append("Script", script.Id,
            $"{{\"id\":\"{script.Id}\",\"number\":\"{number}\"}}");

        await _uow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(script.Id);
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

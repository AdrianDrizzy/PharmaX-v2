using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

/// <summary>
/// One dispensing waiting at a till for the patient to pay for it.
/// </summary>
public sealed record CollectionDto(Guid DispensingId, string DocumentNumber,
    string PatientName, string ScriptNumber, int Items, decimal CoPayment,
    DateTime IssuedAtUtc, string PharmacistName);

/// <summary>
/// The hand-off from the dispensary to the till.
/// </summary>
/// <remarks>
/// The pharmacist issues and the patient walks to a cashier. Between
/// those two moments the medicine has left the shelf and no money has
/// been taken, and every till in the pharmacy needs to see that. This is
/// what puts it in front of them.
///
/// It works because the pharmacy runs on one shared database: the
/// pharmacist's issue is committed on the machine holding it, and the
/// tills read the same table. On a standalone installation the queue is
/// simply that workstation's own dispensings, which is still correct -
/// there is only one of it.
/// </remarks>
public interface ICollectionService
{
    /// Everything issued and still owing a co-payment, oldest first.
    Task<Result<IReadOnlyList<CollectionDto>>> WaitingAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Opens a till sale for one waiting dispensing, priced at what the
    /// patient owes. Finalise it through ISaleService like any other
    /// sale; that is what takes it out of the queue.
    /// </summary>
    Task<Result<SaleDto>> StartAsync(Guid dispensingId,
                                     CancellationToken ct = default);

    /// <summary>
    /// Deletes scripts from the queue that nobody is coming to pay for:
    /// each is reversed, so its medicine goes back into stock, and any
    /// payment a till had begun on it is voided. Returns how many went.
    /// </summary>
    Task<Result<int>> RemoveAsync(IReadOnlyCollection<Guid> dispensingIds,
                                  CancellationToken ct = default);
}

public sealed class CollectionService : ICollectionService
{
    private readonly IDispensingRepository _dispensings;
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IPatientRepository _patients;
    private readonly IScriptRepository _scripts;
    private readonly IUserRepository _users;
    private readonly IDispensingService _dispensing;
    private readonly IPermissionService _permissions;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    /// <summary>
    /// How long a script waits on Collect before it is taken off by itself.
    /// </summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromHours(24);

    public CollectionService(IDispensingRepository dispensings,
        ISaleRepository sales, IProductRepository products,
        IPatientRepository patients, IScriptRepository scripts,
        IUserRepository users, IDispensingService dispensing,
        IPermissionService permissions,
        IUnitOfWork uow, ICurrentUser user, IClock clock)
    {
        _dispensings = dispensings;
        _sales = sales;
        _products = products;
        _patients = patients;
        _scripts = scripts;
        _users = users;
        _dispensing = dispensing;
        _permissions = permissions;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<CollectionDto>>> WaitingAsync(
        CancellationToken ct = default)
    {
        // A cashier's permission rather than a pharmacist's: taking the
        // money is the whole point of the screen.
        _permissions.Demand(PermissionCode.SaleCreate);

        var waiting = await _dispensings.AwaitingCollectionAsync(ct);

        // Whoever opens the queue first after a script's 24 hours are up
        // takes it off, on every till, for every user: the patient is not
        // coming, and the medicine belongs back on the shelf.
        var expired = waiting
            .Where(d => _clock.UtcNow - (d.IssuedAtUtc ?? _clock.UtcNow) > KeptFor)
            .ToList();

        if (expired.Count > 0)
        {
            foreach (var d in expired)
                await TakeOffAsync(d.Id, "Not collected within 24 hours.", ct);

            waiting = await _dispensings.AwaitingCollectionAsync(ct);
        }
        var described = new List<CollectionDto>(waiting.Count);

        foreach (var d in waiting)
        {
            var patient = await _patients.GetAsync(d.PatientId, ct);
            var script = await _scripts.GetAsync(d.ScriptId, ct);

            var pharmacist = d.PharmacistId is Guid id
                ? await _users.GetAsync(id, ct)
                : null;

            described.Add(new CollectionDto(
                DispensingId: d.Id,
                DocumentNumber: d.DocumentNumber,
                PatientName: patient?.FullName ?? "(unknown patient)",
                ScriptNumber: script?.ScriptNumber ?? "",
                Items: d.Lines.Count,
                CoPayment: d.CoPaymentTotal.Amount,
                IssuedAtUtc: d.IssuedAtUtc ?? _clock.UtcNow,
                PharmacistName: pharmacist?.DisplayName ?? ""));
        }

        return Result<IReadOnlyList<CollectionDto>>.Ok(described);
    }

    public async Task<Result<SaleDto>> StartAsync(Guid dispensingId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        // The co-payment goes in the drawer it is counted out of at the
        // cash-up, so it is taken on a till, by somebody who takes money.
        if (!_permissions.Has(PermissionCode.SaleTakePayment))
            return Result<SaleDto>.Fail("POS-028",
                "The cashier takes this payment on the till with the "
                + "drawer. The script stays in the queue until then.");

        if (_user.TillSessionId is null)
            return Result<SaleDto>.Fail("POS-013",
                "Open a till session before taking payment.");

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);

        if (dispensing is null)
            return Result<SaleDto>.Fail("DSP-030", "Dispensing not found.");

        // Two cashiers can be looking at the same queue, so this is a
        // real race rather than a defensive nicety. The message says so,
        // because "already paid" out of nowhere reads like a fault.
        if (dispensing.SaleId is not null)
            return Result<SaleDto>.Fail("DSP-031",
                $"{dispensing.DocumentNumber} has already been paid for. "
                + "Another till may have just taken it.");

        if (!dispensing.AwaitsCollection)
            return Result<SaleDto>.Fail("DSP-032",
                $"{dispensing.DocumentNumber} is not waiting for payment.");

        // A cashier who opened this script and was called away left a
        // draft behind, and only one sale may hold a dispensing. Carry
        // on with theirs: starting a second is refused by the database,
        // and the patient could then never pay at all.
        if (await _sales.OpenCollectionForAsync(dispensing.Id, ct)
            is Sale resumed)
        {
            // At what the script costs now. The draft was priced when the
            // till first opened it; a pharmacist who has since corrected
            // the quantity or the charge left the queue showing the new
            // figure and the till charging the old, higher one.
            if (!ChargesAsDispensed(resumed, dispensing))
            {
                foreach (var stale in resumed.Lines.Where(l => l.IsScript && !l.IsVoided).ToList())
                    resumed.VoidLine(stale.LineNo,
                        "The dispensary changed the script's charge.", _user.Id);

                await AddScriptLinesAsync(resumed, dispensing, ct);
                await _uow.SaveChangesAsync(ct);
            }

            return Result<SaleDto>.Ok(SaleDto.From(resumed));
        }

        var sale = Sale.Open(
            branchId: _sales.CurrentBranchId,
            workstationId: _sales.CurrentWorkstationId,
            tillSessionId: _user.TillSessionId.Value,
            operatorId: _user.Id,
            occurredAtUtc: _clock.UtcNow);

        // Before any line: the sale has to know it is a collection so
        // that finalising it does not issue the stock a second time.
        sale.CollectFor(dispensing.Id);

        await AddScriptLinesAsync(sale, dispensing, ct);

        await _sales.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<SaleDto>.Ok(SaleDto.From(sale));
    }

    public async Task<Result<int>> RemoveAsync(IReadOnlyCollection<Guid> dispensingIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispensingIds);
        _permissions.Demand(PermissionCode.SaleCreate);

        if (dispensingIds.Count == 0)
            return Result<int>.Fail("DSP-059", "Choose the scripts to delete first.");

        var removed = 0;
        var refused = new List<string>();

        foreach (var id in dispensingIds.Distinct())
        {
            var result = await TakeOffAsync(id, "Deleted from Scripts to collect.", ct);

            if (result.IsSuccess) removed++;
            else refused.Add(result.Error.Message);
        }

        // The rest are gone either way; the message says which were not.
        return refused.Count == 0
            ? Result<int>.Ok(removed)
            : Result<int>.Fail("DSP-060", $"{removed} deleted. Not deleted: "
                                          + string.Join(" ", refused));
    }

    /// <summary>
    /// Voids any payment a till had begun on the script, then reverses it.
    /// </summary>
    /// <remarks>
    /// The draft goes first: left open, it would still hold the dispensing,
    /// and a cashier finishing it later would be charging for medicine
    /// that is back on the shelf.
    /// </remarks>
    private async Task<Result> TakeOffAsync(Guid dispensingId, string reason,
                                            CancellationToken ct)
    {
        if (await _sales.OpenCollectionForAsync(dispensingId, ct) is Sale draft)
        {
            draft.Abandon(reason);
            await _uow.SaveChangesAsync(ct);
        }

        return await _dispensing.ReverseUncollectedAsync(dispensingId, reason, ct);
    }

    /// Whether a draft's script lines still charge what the dispensing does.
    private static bool ChargesAsDispensed(Sale sale, Domain.Dispensing.Dispensing dispensing) =>
        sale.Lines.Where(l => l.IsScript && !l.IsVoided)
            .Select(l => (l.ProductId, l.UnitPrice.Amount))
            .SequenceEqual(dispensing.Lines.Where(l => !l.CoPayment.IsZero)
                .Select(l => (l.ProductId, l.CoPayment.Amount)));

    private async Task AddScriptLinesAsync(Sale sale, Domain.Dispensing.Dispensing dispensing,
                                           CancellationToken ct)
    {
        foreach (var line in dispensing.Lines)
        {
            // A fully covered item owes nothing and has no business on a
            // receipt as a zero line.
            if (line.CoPayment.IsZero) continue;

            sale.AddLine(SaleLine.Create(
                productId: line.ProductId,
                description: Describe(line),

                // Quantity one, priced at the whole co-payment for the
                // item. Dividing the co-payment by thirty tablets and
                // multiplying it back would round the patient's total
                // away from the figure the pharmacist quoted, and the
                // dispensed quantity belongs in the description anyway:
                // what is being charged for is the item, once.
                quantity: 1m,
                unitPrice: line.CoPayment,

                // The product's own rate, not a blanket fifteen percent.
                // Co-payments are VAT-inclusive, so the rate splits the
                // figure rather than adding to it, and a good many
                // medicines are not rated like a tube of hand cream.
                vatRatePercent: await VatRateOf(line.ProductId, ct),
                taxCode: "A",

                // Rated only where a society's tariff priced the item,
                // which is where it contributes. Every collected script was
                // marked rated, so a private patient's shelf-priced script
                // showed RATED PRICING at the till and was counted in the
                // rated-transactions report.
                priceSource: line.SchemeContribution.IsZero
                    ? PriceSource.StandardRetail
                    : PriceSource.MedicalAidTariff,
                isScript: true));

            // Deliberately no batch on the line. The dispensing already
            // records which batch the patient was given, and that is the
            // record a recall reads; repeating it here against a
            // quantity of one would misstate how much of the batch moved
            // to anything totalling sale lines.
        }
    }

    /// The item and how much of it, since the line is priced as one.
    private static string Describe(Domain.Dispensing.DispensingLine line) =>
        line.Quantity == 1m
            ? line.Description
            : $"{line.Description} x {line.Quantity:0.##}";

    private readonly Dictionary<Guid, decimal> _vatRates = new();

    /// <summary>
    /// The product's VAT rate, looked up once per product.
    /// </summary>
    /// <remarks>
    /// A dispensing line does not carry the rate, so it has to come from
    /// the catalogue. Cached because a script with several items of the
    /// same product should not be several queries.
    /// </remarks>
    private async Task<decimal> VatRateOf(Guid productId,
                                          CancellationToken ct)
    {
        if (_vatRates.TryGetValue(productId, out var known)) return known;

        var product = await _products.GetAsync(productId, ct);
        var rate = product?.VatRatePercent ?? 0m;

        _vatRates[productId] = rate;
        return rate;
    }
}

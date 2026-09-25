using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Finance;
using PharmaX.Application.Inventory;
using PharmaX.Application.Patients;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

/// <summary>
/// What recalling a quotation at the till found on the way: what went on
/// the sale, and what had changed since it was quoted.
/// </summary>
/// <param name="Changes">
/// Prices that have moved, items that are discontinued or short, and a
/// quotation for a customer the till could not attach - each one a line
/// the cashier reads to the customer before taking the money.
/// </param>
public sealed record QuotationConversionDto(string QuotationNumber,
    string CustomerName, int LinesAdded, IReadOnlyList<string> Changes);

public interface ISaleService
{
    Task<Result<SaleDto>> OpenAsync(CancellationToken ct = default);

    Task<Result<SaleDto>> GetAsync(Guid saleId, CancellationToken ct = default);

    Task<Result<SaleLineDto>> AddLineAsync(Guid saleId, string scannedCode,
        decimal quantity, CancellationToken ct = default);

    Task<Result> SetLineQuantityAsync(Guid saleId, int lineNo,
        decimal quantity, CancellationToken ct = default);

    Task<Result> VoidLineAsync(Guid saleId, int lineNo, string reason,
        CancellationToken ct = default);

    Task<Result> ApplyLineDiscountAsync(Guid saleId, int lineNo,
        decimal amount, Guid? authoriserId, CancellationToken ct = default);

    /// <summary>
    /// Puts the sale on a patient's account, so it can be tendered to
    /// one. Refuses an account already on hold rather than letting the
    /// cashier find out at the payment screen with a queue behind them.
    /// </summary>
    Task<Result<AccountDto>> AssignAccountAsync(Guid saleId, Guid accountId,
        CancellationToken ct = default);

    Task<Result<FinaliseDto>> FinaliseAsync(Guid saleId,
        IReadOnlyList<PaymentDto> payments, CancellationToken ct = default);

    /// <summary>
    /// Parks the sale, lines, customer and discounts included, so the
    /// till can serve the next customer.
    /// </summary>
    Task<Result> HoldAsync(Guid saleId, CancellationToken ct = default);

    /// Every basket held at this branch, oldest first.
    Task<Result<IReadOnlyList<HeldSaleDto>>> HeldAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Takes a held basket back up on this till, abandoning the empty
    /// sale that was open in its place.
    /// </summary>
    Task<Result<SaleDto>> RecallAsync(Guid heldSaleId,
        Guid? replacingSaleId = null, CancellationToken ct = default);

    /// <summary>
    /// Abandons an unfinished sale: an empty basket left behind when the
    /// till closes, or one the customer walked away from.
    /// </summary>
    Task<Result> AbandonAsync(Guid saleId, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Brings a quotation's lines onto the open sale, re-checked against
    /// today's catalogue, prices and stock.
    /// </summary>
    Task<Result<QuotationConversionDto>> ConvertQuotationAsync(Guid saleId,
        string quotationNumber, CancellationToken ct = default);
}

public sealed class SaleService : ISaleService
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IDispensingRepository _dispensings;
    private readonly IQuotationRepository _quotations;
    private readonly IUserRepository _users;
    private readonly IJournalPoster _journals;
    private readonly IAccountRepository _accounts;
    private readonly IAccountService _accountService;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public SaleService(ISaleRepository sales, IProductRepository products,
        IBatchRepository batches, IDispensingRepository dispensings,
        IQuotationRepository quotations, IUserRepository users,
        IJournalPoster journals, IAccountRepository accounts,
        IAccountService accountService, IStockService stock,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _sales = sales; _products = products; _batches = batches;
        _dispensings = dispensings; _quotations = quotations;
        _users = users; _journals = journals;
        _accounts = accounts; _accountService = accountService;
        _stock = stock; _numbering = numbering; _permissions = permissions;
        _audit = audit; _outbox = outbox; _uow = uow;
        _user = user; _clock = clock;
    }

    public async Task<Result<SaleDto>> OpenAsync(CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        if (_user.TillSessionId is null)
            return Result<SaleDto>.Fail("POS-013",
                "Open a till session before selling.");

        var sale = Sale.Open(
            branchId: _sales.CurrentBranchId,
            workstationId: _sales.CurrentWorkstationId,
            tillSessionId: _user.TillSessionId.Value,
            operatorId: _user.Id,
            occurredAtUtc: _clock.UtcNow);

        await _sales.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<SaleDto>.Ok(SaleDto.From(sale));
    }

    public async Task<Result<SaleDto>> GetAsync(Guid saleId,
        CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);

        return sale is null
            ? Result<SaleDto>.Fail("POS-000", "Sale not found.")
            : Result<SaleDto>.Ok(SaleDto.From(sale));
    }

    public async Task<Result<SaleLineDto>> AddLineAsync(Guid saleId,
        string scannedCode, decimal quantity, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null)
            return Result<SaleLineDto>.Fail("POS-000", "Sale not found.");

        // A collection is the co-payment for one script and nothing
        // else. Scanning a bottle of shampoo onto it would put stock
        // through a sale that deliberately does not move any.
        if (sale.IsCollection)
            return Result<SaleLineDto>.Fail("POS-014",
                "This sale is collecting a script. Take the co-payment, "
                + "then start a new sale for anything else.");

        // The till sells what this user may see. A cashier's catalogue is
        // the over-the-counter one, filtered in the query itself, so an
        // exact dispensary code reads as "no product" rather than as a
        // product the screen then declines to show.
        var product = await _products.FindByCodeOrBarcodeAsync(scannedCode,
            _permissions.ProductScope(), ct);

        if (product is null)
            return Result<SaleLineDto>.Fail("POS-001",
                $"No product for code {scannedCode}.");

        if (!product.IsActive)
            return Result<SaleLineDto>.Fail("POS-002",
                $"{product.Description} is discontinued and cannot be sold.");

        if (quantity <= 0m)
            return Result<SaleLineDto>.Fail("POS-005",
                "The quantity must be more than zero.");

        var added = await AddProductAsync(sale, product, quantity, ct);

        if (added.IsFailure)
            return Result<SaleLineDto>.Fail(added.Error.Code,
                                            added.Error.Message);

        await _uow.SaveChangesAsync(ct);

        return Result<SaleLineDto>.Ok(SaleLineDto.From(added.Value));
    }

    /// <summary>
    /// Puts a product on the sale, a line per batch where a batch-tracked
    /// quantity has to come from more than one.
    /// </summary>
    /// <remarks>
    /// The till used to take the first batch FEFO offered for the whole
    /// quantity, which the finalise then refused when that batch did not
    /// hold enough. Splitting keeps every line honest about the batch -
    /// and the expiry - of what went into the bag.
    /// </remarks>
    private async Task<Result<SaleLine>> AddProductAsync(Sale sale,
        Domain.Inventory.Product product, decimal quantity,
        CancellationToken ct)
    {
        var price = product.ResolvePrice(sale.PatientId);

        if (!product.IsBatchTracked)
            return Result<SaleLine>.Ok(sale.AddLine(SaleLine.Create(
                productId: product.Id,
                description: product.Description,
                quantity: quantity,
                unitPrice: price.Amount,
                vatRatePercent: product.VatRatePercent,
                taxCode: product.TaxCode,
                priceSource: price.Source)));

        var allocation = await AllocateAsync(sale, product, quantity,
                                             exceptLine: null, ct);

        if (allocation.IsFailure)
            return Result<SaleLine>.Fail(allocation.Error.Code,
                                         allocation.Error.Message);

        SaleLine? last = null;

        foreach (var (batch, take) in allocation.Value)
            last = sale.AddLine(SaleLine.Create(
                productId: product.Id,
                description: product.Description,
                quantity: take,
                unitPrice: price.Amount,
                vatRatePercent: product.VatRatePercent,
                taxCode: product.TaxCode,
                priceSource: price.Source,
                batchId: batch.Id,
                batchExpiry: batch.ExpiryDate,
                batchNumber: batch.BatchNumber));

        return Result<SaleLine>.Ok(last!);
    }

    /// <summary>
    /// FEFO across the product's unexpired batches, counting what the rest
    /// of this sale has already taken from each.
    /// </summary>
    private async Task<Result<IReadOnlyList<(Batch Batch, decimal Quantity)>>>
        AllocateAsync(Sale sale, Domain.Inventory.Product product,
                      decimal quantity, SaleLine? exceptLine,
                      CancellationToken ct)
    {
        var batches = await _batches.ForProductAsync(product.Id, ct);

        var claimed = sale.Lines
            .Where(l => !l.IsVoided && l.BatchId is not null && l != exceptLine)
            .GroupBy(l => l.BatchId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var candidates = batches
            .Where(b => b.IsAvailableOn(_clock.Today))
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.ReceivedAtUtc)
            .Select(b => (Batch: b,
                          Free: b.QuantityOnHand - claimed.GetValueOrDefault(b.Id)))
            .Where(x => x.Free > 0m)
            .ToList();

        var available = candidates.Sum(x => x.Free);

        if (available < quantity)
            return Result<IReadOnlyList<(Batch, decimal)>>.Fail("POS-003",
                $"{product.Description}: only {available:0.###} unexpired in "
                + $"stock, so {quantity:0.###} cannot be sold.");

        var result = new List<(Batch, decimal)>();
        var remaining = quantity;

        foreach (var (batch, free) in candidates)
        {
            if (remaining <= 0m) break;

            var take = Math.Min(free, remaining);
            result.Add((batch, take));
            remaining -= take;
        }

        return Result<IReadOnlyList<(Batch, decimal)>>.Ok(result);
    }

    public async Task<Result> SetLineQuantityAsync(Guid saleId, int lineNo,
        decimal quantity, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        var line = sale.Lines.SingleOrDefault(l => l.LineNo == lineNo);
        if (line is null)
            return Result.Fail("POS-004", $"Line {lineNo} not found.");

        if (quantity <= 0m)
            return Result.Fail("POS-005", "The quantity must be more than zero.");

        // Re-run FEFO before accepting the change: a larger quantity may
        // need more unexpired stock than the batch originally picked
        // holds, and then it comes from the next batch on a line of its
        // own rather than being refused at the payment screen.
        if (line.BatchId is not null)
        {
            var product = await _products.GetAsync(line.ProductId, ct);
            if (product is null)
                return Result.Fail("POS-001", "That product is no longer "
                                              + "in the catalogue.");

            var allocation = await AllocateAsync(sale, product, quantity,
                                                 exceptLine: line, ct);

            if (allocation.IsFailure)
                return Result.Fail(allocation.Error.Code,
                                   allocation.Error.Message);

            var first = allocation.Value[0];

            line.ChangeQuantity(first.Quantity);
            line.ChangeBatch(first.Batch.Id, first.Batch.BatchNumber,
                             first.Batch.ExpiryDate);

            foreach (var (batch, take) in allocation.Value.Skip(1))
                sale.AddLine(SaleLine.Create(
                    productId: line.ProductId,
                    description: line.Description,
                    quantity: take,
                    unitPrice: line.UnitPrice,
                    vatRatePercent: line.VatRatePercent,
                    taxCode: line.TaxCode,
                    priceSource: line.PriceSource,
                    batchId: batch.Id,
                    batchExpiry: batch.ExpiryDate,
                    batchNumber: batch.BatchNumber));
        }
        else
        {
            line.ChangeQuantity(quantity);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> VoidLineAsync(Guid saleId, int lineNo,
        string reason, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        sale.VoidLine(lineNo, reason, _user.Id);

        var line = sale.Lines.Single(l => l.LineNo == lineNo);
        _audit.Applied("pos.sale.line.void", "SaleLine", line.Id, reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ApplyLineDiscountAsync(Guid saleId, int lineNo,
        decimal amount, Guid? authoriserId, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        var line = sale.Lines.Single(l => l.LineNo == lineNo);

        // Over the operator's own limit, the supervisor who authorised it
        // must actually hold the authority - checked here rather than
        // trusted from the screen.
        var limit = line.GrossBeforeDiscount
            .Percentage(_user.DiscountLimitPercent);

        if (amount > limit.Amount)
            _permissions.DemandOrAuthorised(
                PermissionCode.SaleDiscountAboveLimit, authoriserId);

        line.ApplyDiscount(
            Domain.Primitives.Money.Of(amount),
            _user.DiscountLimitPercent,
            authoriserId);

        _audit.Applied("pos.sale.discount", "SaleLine", line.Id,
            reason: authoriserId is null ? null : "Supervisor authorised");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<AccountDto>> AssignAccountAsync(Guid saleId,
        Guid accountId, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);

        if (sale is null)
            return Result<AccountDto>.Fail("POS-000", "Sale not found.");

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result<AccountDto>.Fail("ACC-000", "Account not found.");

        // Said now rather than at the payment screen. A cashier with a
        // queue behind them should learn that an account is on hold
        // while the customer is still deciding what to buy.
        if (account.IsOnHold)
            return Result<AccountDto>.Fail("ACC-011",
                $"Account {account.AccountNumber} is on hold: "
                + $"{account.HoldReason}");

        sale.AssignPatient(account.PatientId ?? Guid.Empty, account.Id);

        await _uow.SaveChangesAsync(ct);

        return Result<AccountDto>.Ok(new AccountDto(account.Id,
            account.AccountNumber, account.Name, account.PatientId,
            account.Balance.Amount, account.CreditLimit.Amount,
            account.Available.Amount, account.IsOnHold, account.HoldReason,
            account.TermsDays));
    }

    public async Task<Result<FinaliseDto>> FinaliseAsync(Guid saleId,
        IReadOnlyList<PaymentDto> payments, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payments);

        _permissions.Demand(PermissionCode.SaleCreate);

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null)
            return Result<FinaliseDto>.Fail("POS-000", "Sale not found.");

        // Idempotency guard: a retry after a communications failure must
        // not create a second sale (NET-006).
        if (sale.Status == SaleStatus.Finalised)
            return Result<FinaliseDto>.Ok(new FinaliseDto(
                sale.DocumentNumber, sale.Total.Amount, sale.Change.Amount));

        if (sale.Status != SaleStatus.Open)
            return Result<FinaliseDto>.Fail("POS-015",
                "This sale is not open. Recall it before taking payment.");

        // Change from cash where more was tendered than the sale comes
        // to (DEF-0019); the payments keep what stayed in the drawer.
        sale.TakePayments(payments.Select(p => SalePayment.Create(
            p.TenderType, Domain.Primitives.Money.Of(p.Amount), p.Reference)));

        var number = await _numbering.NextAsync(DocumentSeries.Sale, ct);
        sale.Finalise(number, _clock.UtcNow);

        // Anything tendered to an account has to land on somebody's
        // account. Before this, "Account" was in the till's tender list
        // and charged nobody: the ledger recorded a debtor and no
        // patient owed it, so the pharmacy could give away stock on
        // credit with no record of who had it and no limit on how much.
        if (await ChargeAccountAsync(sale, ct) is Error refused)
            return Result<FinaliseDto>.Fail(refused.Code, refused.Message);

        // Stock leaves the shelf once, here, whichever way the lines
        // arrived - scanned, recalled from a hold or brought across from
        // a quotation.
        await _stock.ApplyIssueAsync(sale, ct);

        // Paying for a collection is what takes the script out of the
        // queue the other tills are watching, so it happens here, in the
        // same transaction as the sale. Left to the screen to do
        // afterwards, a crash between the two would bill the patient and
        // still show their script as waiting.
        await MarkCollectedAsync(sale, ct);

        // A quotation is used up by the sale that paid for it, and only
        // then: a basket abandoned half-way leaves it available.
        if (await MarkQuotationSoldAsync(sale, ct) is Error sold)
            return Result<FinaliseDto>.Fail(sold.Code, sold.Message);

        // The books, in the same transaction as the sale: money taken
        // and revenue recognised, or neither.
        await _journals.PostSaleAsync(sale, ct);

        _outbox.Append("Sale", sale.Id, SaleDto.From(sale).ToJson());
        _audit.Applied("pos.sale.create", "Sale", sale.Id,
            sale.Change.IsZero ? null : $"Change given {sale.Change}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<FinaliseDto>.Ok(new FinaliseDto(
            sale.DocumentNumber, sale.Total.Amount, sale.Change.Amount));
    }

    public async Task<Result> HoldAsync(Guid saleId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        sale.Hold();

        _audit.Applied("pos.sale.hold", "Sale", sale.Id,
            $"{sale.Lines.Count(l => !l.IsVoided)} line(s), {sale.Total}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<HeldSaleDto>>> HeldAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var held = await _sales.HeldAsync(ct);
        var rows = new List<HeldSaleDto>();

        foreach (var sale in held)
        {
            var who = await _users.GetAsync(sale.OperatorId, ct);

            string? customer = null;

            if (sale.AccountId is Guid accountId
                && await _accounts.GetAsync(accountId, ct) is { } account)
                customer = account.Name;

            rows.Add(new HeldSaleDto(sale.Id,
                sale.OccurredAtUtc.ToLocalTime(),
                who?.DisplayName ?? "",
                sale.Lines.Count(l => !l.IsVoided),
                sale.Total.Amount, customer));
        }

        return Result<IReadOnlyList<HeldSaleDto>>.Ok(rows);
    }

    public async Task<Result<SaleDto>> RecallAsync(Guid heldSaleId,
        Guid? replacingSaleId = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        if (_user.TillSessionId is not Guid session)
            return Result<SaleDto>.Fail("POS-013",
                "Open a till session before recalling a sale.");

        var sale = await _sales.GetAsync(heldSaleId, ct);
        if (sale is null)
            return Result<SaleDto>.Fail("POS-000", "That held sale has gone.");

        if (sale.Status != SaleStatus.Held)
            return Result<SaleDto>.Fail("POS-016",
                "That sale is no longer held - another till may have "
                + "recalled it.");

        // The empty basket the till had open in the meantime goes, so a
        // recall does not leave a stray open sale behind (DEF-0030).
        if (replacingSaleId is Guid replacing && replacing != heldSaleId
            && await _sales.GetAsync(replacing, ct) is { } empty
            && empty.Status == SaleStatus.Open
            && !empty.Lines.Any(l => !l.IsVoided))
            empty.Abandon("Replaced by a recalled held sale.");

        sale.Resume(session, _user.Id);

        _audit.Applied("pos.sale.recall", "Sale", sale.Id);

        await _uow.SaveChangesAsync(ct);
        return Result<SaleDto>.Ok(SaleDto.From(sale));
    }

    public async Task<Result> AbandonAsync(Guid saleId, string reason,
        CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        if (sale.Status is SaleStatus.Finalised or SaleStatus.Voided)
            return Result.Ok();

        sale.Abandon(reason);

        if (sale.Lines.Any(l => !l.IsVoided))
            _audit.Applied("pos.sale.abandon", "Sale", sale.Id, reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<QuotationConversionDto>> ConvertQuotationAsync(
        Guid saleId, string quotationNumber, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null)
            return Result<QuotationConversionDto>.Fail("POS-000",
                "Sale not found.");

        if (sale.Status != SaleStatus.Open || sale.IsCollection)
            return Result<QuotationConversionDto>.Fail("QUO-020",
                "A quotation is brought onto an open sale at the till.");

        if (sale.Lines.Any(l => !l.IsVoided))
            return Result<QuotationConversionDto>.Fail("QUO-021",
                "Start from an empty sale: finish or hold this one first.");

        var quotation = await _quotations.ByNumberAsync(quotationNumber, ct);
        if (quotation is null)
            return Result<QuotationConversionDto>.Fail("QUO-022",
                $"No quotation {quotationNumber}.");

        if (quotation.Status == QuotationStatus.Accepted
            || quotation.SaleId is not null)
            return Result<QuotationConversionDto>.Fail("QUO-023",
                $"Quotation {quotation.QuotationNumber} has already been "
                + "sold.");

        if (quotation.Status != QuotationStatus.Issued)
            return Result<QuotationConversionDto>.Fail("QUO-024",
                $"Quotation {quotation.QuotationNumber} was never issued to "
                + "the customer.");

        if (quotation.HasExpiredOn(_clock.Today))
            return Result<QuotationConversionDto>.Fail("QUO-025",
                $"Quotation {quotation.QuotationNumber} lapsed on "
                + $"{quotation.ValidUntil:dd/MM/yyyy}. Quote again at "
                + "today's prices.");

        if (await _sales.OpenForQuotationAsync(quotation.Id, ct) is { } other
            && other.Id != sale.Id)
            return Result<QuotationConversionDto>.Fail("QUO-026",
                $"Quotation {quotation.QuotationNumber} is already on "
                + "another open or held sale. Recall that one instead.");

        sale.ConvertFrom(quotation.Id);

        var changes = new List<string>();
        var added = 0;

        foreach (var line in quotation.Lines.OrderBy(l => l.LineNo))
        {
            var product = await _products.GetAsync(line.ProductId, ct);

            // Revalidated line by line: still stocked, still something
            // this user may sell, still in stock, and at today's price.
            if (product is null || !product.IsActive)
            {
                changes.Add($"{line.Description}: discontinued, not added.");
                continue;
            }

            if (product.RequiresPharmacist
                && _permissions.ProductScope() != ProductScope.All)
            {
                changes.Add($"{line.Description}: dispensary stock, not "
                            + "added. Ask the dispensary.");
                continue;
            }

            var current = product.ResolvePrice(sale.PatientId).Amount;

            if (current.Amount != line.UnitPrice.Amount)
                changes.Add(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    "{0}: price now {1:N2} (quoted {2:N2}).",
                    line.Description, current.Amount, line.UnitPrice.Amount));

            var result = await AddProductAsync(sale, product, line.Quantity, ct);

            if (result.IsFailure)
            {
                changes.Add(result.Error.Message);
                continue;
            }

            added++;
        }

        if (quotation.PatientId is not null && sale.AccountId is null)
            changes.Add($"Quoted to {quotation.CustomerName}: press F3 to "
                        + "put the sale on their account if it is going on "
                        + "one.");

        _audit.Applied("pos.quotation.convert", "Sale", sale.Id,
            $"{quotation.QuotationNumber}: {added} line(s)");

        await _uow.SaveChangesAsync(ct);

        return Result<QuotationConversionDto>.Ok(new QuotationConversionDto(
            quotation.QuotationNumber, quotation.CustomerName, added, changes));
    }

    private async Task<Error?> MarkQuotationSoldAsync(Sale sale,
        CancellationToken ct)
    {
        if (sale.QuotationId is not Guid quotationId) return null;

        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null) return null;

        try
        {
            quotation.AcceptInto(sale.Id, _clock.Today);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            // Sold on another till a moment ago: the stock must not go
            // twice, so this sale is refused and rolls back.
            return new Error("QUO-027", ex.Message);
        }

        _audit.Applied("pos.quotation.accept", "Quotation", quotationId,
            sale.DocumentNumber);

        return null;
    }

    /// <summary>
    /// Puts the account-tendered part of a sale on the patient's account.
    /// </summary>
    /// <remarks>
    /// Returns the reason rather than throwing, so that a refused charge
    /// gives the cashier something to read - "over the credit limit" or
    /// "on hold, and why" - instead of a stack trace. The sale rolls
    /// back either way, which is the right order: better a till that
    /// will not complete the sale than a patient owing money nobody
    /// recorded.
    /// </remarks>
    private async Task<Error?> ChargeAccountAsync(Sale sale,
        CancellationToken ct)
    {
        var onAccount = sale.Payments
            .Where(p => p.Tender == TenderType.Account)
            .Aggregate(Domain.Primitives.Money.Zero(),
                       (sum, p) => sum + p.Amount);

        if (onAccount.IsZero) return null;

        if (sale.AccountId is not Guid accountId)
            return new Error("POS-020",
                "This sale is tendered to an account but no account has "
                + "been chosen. Press F3 and find the customer.");

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return new Error("ACC-000", "Account not found.");

        try
        {
            await _accountService.ChargeAsync(account, sale, onAccount, ct);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            // On hold, or past the credit limit. Both are the account
            // doing its job, and both are things the cashier can act on.
            return new Error("ACC-010", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Ties a finalised collection back to the dispensing it paid for.
    /// </summary>
    /// <remarks>
    /// Quiet where the dispensing has gone or has already been marked:
    /// the money has been taken and the drawer is open, and throwing at
    /// that point would roll back a sale the customer has paid for over
    /// a bookkeeping link. The unpaid script left behind is visible on
    /// the collection screen, which is the recoverable failure.
    /// </remarks>
    private async Task MarkCollectedAsync(Sale sale, CancellationToken ct)
    {
        if (sale.DispensingId is not Guid dispensingId) return;

        var dispensing = await _dispensings.GetAsync(dispensingId, ct);

        if (dispensing is null || dispensing.SaleId is not null) return;

        dispensing.RecordCollection(sale.Id, _clock.UtcNow);
        _audit.Applied("dispensing.collected", "Dispensing", dispensingId);
    }
}

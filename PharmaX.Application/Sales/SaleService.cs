using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

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

    Task<Result<FinaliseDto>> FinaliseAsync(Guid saleId,
        IReadOnlyList<PaymentDto> payments, CancellationToken ct = default);
}

public sealed class SaleService : ISaleService
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public SaleService(ISaleRepository sales, IProductRepository products,
        IBatchRepository batches, IStockService stock,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _sales = sales; _products = products; _batches = batches;
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
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null)
            return Result<SaleLineDto>.Fail("POS-000", "Sale not found.");

        var product = await _products.FindByCodeOrBarcodeAsync(scannedCode, ct);
        if (product is null)
            return Result<SaleLineDto>.Fail("POS-001",
                $"No product for code {scannedCode}.");

        if (!product.IsActive)
            return Result<SaleLineDto>.Fail("POS-002",
                $"{product.Description} is discontinued and cannot be sold.");

        Guid? batchId = null;
        string? batchNumber = null;
        DateOnly? expiry = null;

        if (product.IsBatchTracked)
        {
            var batches = await _batches.ForProductAsync(product.Id, ct);
            // FEFO returns the batches in expiry order; the till takes
            // the first, and a quantity spanning two batches is refused
            // upstream by the allocator rather than split silently.
            var allocation = FefoAllocator.Allocate(
                batches, quantity, _clock.Today)[0];

            var batch = batches.Single(b => b.Id == allocation.BatchId);
            batchId = batch.Id;
            batchNumber = batch.BatchNumber;
            expiry = batch.ExpiryDate;
        }

        var price = product.ResolvePrice(sale.PatientId);

        var line = sale.AddLine(SaleLine.Create(
            productId: product.Id,
            description: product.Description,
            quantity: quantity,
            unitPrice: price.Amount,
            vatRatePercent: product.VatRatePercent,
            taxCode: product.TaxCode,
            priceSource: price.Source,
            batchId: batchId,
            batchExpiry: expiry,
            batchNumber: batchNumber));

        await _uow.SaveChangesAsync(ct);

        return Result<SaleLineDto>.Ok(SaleLineDto.From(line));
    }

    public async Task<Result> SetLineQuantityAsync(Guid saleId, int lineNo,
        decimal quantity, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null) return Result.Fail("POS-000", "Sale not found.");

        var line = sale.Lines.SingleOrDefault(l => l.LineNo == lineNo);
        if (line is null)
            return Result.Fail("POS-004", $"Line {lineNo} not found.");

        // Re-run FEFO before accepting the change: a larger quantity may
        // need more unexpired stock than the batch originally picked holds.
        if (line.BatchId is not null)
        {
            var batches = await _batches.ForProductAsync(line.ProductId, ct);
            FefoAllocator.Allocate(batches, quantity, _clock.Today);
        }

        line.ChangeQuantity(quantity);

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

        line.ApplyDiscount(
            Domain.Primitives.Money.Of(amount),
            _user.DiscountLimitPercent,
            authoriserId);

        _audit.Applied("pos.sale.discount", "SaleLine", line.Id,
            reason: authoriserId is null ? null : "Supervisor authorised");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<FinaliseDto>> FinaliseAsync(Guid saleId,
        IReadOnlyList<PaymentDto> payments, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payments);

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var sale = await _sales.GetAsync(saleId, ct);
        if (sale is null)
            return Result<FinaliseDto>.Fail("POS-000", "Sale not found.");

        // Idempotency guard: a retry after a communications failure must
        // not create a second sale (NET-006).
        if (sale.Status == SaleStatus.Finalised)
            return Result<FinaliseDto>.Ok(
                new FinaliseDto(sale.DocumentNumber, sale.Total.Amount));

        foreach (var p in payments)
            sale.AddPayment(SalePayment.Create(p.TenderType,
                Domain.Primitives.Money.Of(p.Amount), p.Reference));

        var number = await _numbering.NextAsync("SALE", ct);
        sale.Finalise(number, _clock.UtcNow);

        await _stock.ApplyIssueAsync(sale, ct);
        _outbox.Append("Sale", sale.Id, SaleDto.From(sale).ToJson());
        _audit.Applied("pos.sale.create", "Sale", sale.Id);

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<FinaliseDto>.Ok(
            new FinaliseDto(sale.DocumentNumber, sale.Total.Amount));
    }
}

using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Application.Suppliers;

public interface IReceivingService
{
    Task<Result<IReadOnlyList<GoodsReceiptListItemDto>>> RecentAsync(
        CancellationToken ct = default);

    Task<Result<GoodsReceiptDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Captures and posts in one act. A receipt saved unposted would be
    /// stock the shelf has and the system does not, which is the state
    /// that makes a stock take unreconcilable.
    /// </summary>
    Task<Result<GoodsReceiptDto>> ReceiveAsync(GoodsReceiptEditDto edit,
        CancellationToken ct = default);

    /// <summary>
    /// Checks the lines before anything is written, so the screen can
    /// show every problem at once rather than one per attempt.
    /// </summary>
    Task<Result<IReadOnlyList<GoodsReceiptLineDto>>> PreviewAsync(
        GoodsReceiptEditDto edit, CancellationToken ct = default);
}

public sealed class ReceivingService : IReceivingService
{
    private readonly IGoodsReceiptRepository _receipts;
    private readonly ISupplierRepository _suppliers;
    private readonly IPurchaseOrderRepository _orders;
    private readonly IProductRepository _products;
    private readonly IJournalPoster _journals;
    private readonly IStockService _stock;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ReceivingService(IGoodsReceiptRepository receipts,
        ISupplierRepository suppliers, IPurchaseOrderRepository orders,
        IProductRepository products, IJournalPoster journals,
        IStockService stock, IDocumentNumbering numbering,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow, ICurrentUser user, IClock clock)
    {
        _receipts = receipts;
        _suppliers = suppliers;
        _orders = orders;
        _products = products;
        _journals = journals;
        _stock = stock;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<GoodsReceiptListItemDto>>>
        RecentAsync(CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var receipts = await _receipts.RecentAsync(ct: ct);
        var suppliers = await _suppliers.ListAsync(includeInactive: true, ct);

        var names = suppliers.ToDictionary(s => s.Id, s => s.Name);

        IReadOnlyList<GoodsReceiptListItemDto> items = receipts
            .Select(r => new GoodsReceiptListItemDto(
                r.Id, r.ReceiptNumber,
                names.GetValueOrDefault(r.SupplierId, "Unknown"),
                r.SupplierInvoiceNumber, r.ReceivedOn, r.Lines.Count,
                r.Total.Amount, r.IsPosted))
            .ToList();

        return Result<IReadOnlyList<GoodsReceiptListItemDto>>.Ok(items);
    }

    public async Task<Result<GoodsReceiptDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var receipt = await _receipts.GetAsync(id, ct);
        if (receipt is null)
            return Result<GoodsReceiptDto>.Fail("GRN-000",
                "Goods receipt not found.");

        var supplier = await _suppliers.GetAsync(receipt.SupplierId, ct);

        return Result<GoodsReceiptDto>.Ok(
            GoodsReceiptDto.From(receipt, supplier?.Name ?? "Unknown"));
    }

    public async Task<Result<IReadOnlyList<GoodsReceiptLineDto>>>
        PreviewAsync(GoodsReceiptEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.ProductRead);

        var resolved = await ResolveAsync(edit, ct);

        if (resolved.Problem is Error problem)
            return Result<IReadOnlyList<GoodsReceiptLineDto>>
                .Fail(problem.Code, problem.Message);

        IReadOnlyList<GoodsReceiptLineDto> preview = resolved.Lines
            .Select((r, i) => new GoodsReceiptLineDto(
                i + 1, r.Product.Id, r.Product.Description, r.Line.Quantity,
                r.Line.UnitCost, r.Line.Quantity * r.Line.UnitCost,
                r.Line.BatchNumber, r.Line.ExpiryDate))
            .ToList();

        return Result<IReadOnlyList<GoodsReceiptLineDto>>.Ok(preview);
    }

    public async Task<Result<GoodsReceiptDto>> ReceiveAsync(
        GoodsReceiptEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.GoodsReceive);

        var supplier = await _suppliers.GetAsync(edit.SupplierId, ct);
        if (supplier is null)
            return Result<GoodsReceiptDto>.Fail("GRN-001",
                "Choose a supplier.");

        var resolved = await ResolveAsync(edit, ct);

        if (resolved.Problem is Error problem)
            return Result<GoodsReceiptDto>.Fail(problem.Code, problem.Message);

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var number = await _numbering.NextAsync("GRN", ct);

        var receipt = GoodsReceipt.Create(number, supplier.Id,
            edit.ReceivedOn, _user.Id, edit.PurchaseOrderId,
            edit.SupplierInvoiceNumber);

        foreach (var (product, line) in resolved.Lines)
            receipt.AddLine(product.Id, product.Description, line.Quantity,
                Money.Of(line.UnitCost), product.IsBatchTracked,
                line.BatchNumber, line.ExpiryDate,
                line.PurchaseOrderLineNo);

        if (edit.InvoiceTotal is decimal invoiced)
            receipt.RecordInvoiceTotal(Money.Of(invoiced));

        receipt.Post();

        await _receipts.AddAsync(receipt, ct);

        // Posting is the point at which stock moves, and it happens
        // inside this transaction: a receipt that fails to save leaves
        // no batches and no movements behind.
        await _stock.ApplyReceiptAsync(receipt, ct);

        // Stock bought on credit, expensed on arrival. Same transaction
        // as the delivery, so the books cannot owe a supplier for goods
        // that never landed.
        await _journals.PostReceiptAsync(receipt, ct);

        await CloseOffOrderLinesAsync(edit, receipt, ct);

        _audit.Applied("inv.goods.receive", "GoodsReceipt", receipt.Id,
            $"{receipt.Lines.Count} line(s), {receipt.Total} from "
            + supplier.Name);

        _outbox.Append("GoodsReceipt", receipt.Id,
            $"{{\"id\":\"{receipt.Id}\",\"number\":\"{number}\"}}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<GoodsReceiptDto>.Ok(
            GoodsReceiptDto.From(receipt, supplier.Name));
    }

    /// <summary>
    /// Records the delivery against the order it came from, so a short
    /// delivery leaves the order open rather than looking complete.
    /// </summary>
    private async Task CloseOffOrderLinesAsync(GoodsReceiptEditDto edit,
        GoodsReceipt receipt, CancellationToken ct)
    {
        if (edit.PurchaseOrderId is not Guid orderId) return;

        var order = await _orders.GetAsync(orderId, ct);
        if (order is null) return;

        foreach (var line in receipt.Lines
                     .Where(l => l.PurchaseOrderLineNo is not null))
            order.RecordReceipt(line.PurchaseOrderLineNo!.Value,
                                line.Quantity);
    }

    /// <summary>
    /// Turns codes into products and refuses the whole receipt on the
    /// first problem it finds. Nothing is written until every line
    /// resolves, so a mistyped code cannot leave half a delivery posted.
    /// </summary>
    private async Task<(List<(Domain.Inventory.Product Product,
        GoodsReceiptLineEditDto Line)> Lines, Error? Problem)> ResolveAsync(
        GoodsReceiptEditDto edit, CancellationToken ct)
    {
        var resolved = new List<(Domain.Inventory.Product,
                                GoodsReceiptLineEditDto)>();

        if (edit.Lines.Count == 0)
            return (resolved, new Error("GRN-002",
                "A receipt with no lines receives nothing."));

        if (edit.ReceivedOn > _clock.Today)
            return (resolved, new Error("GRN-003",
                "Stock cannot be received in the future."));

        foreach (var line in edit.Lines)
        {
            var product = await _products
                .FindByCodeOrBarcodeAsync(line.ProductCode, ct);

            if (product is null)
                return (resolved, new Error("GRN-004",
                    $"No product for code {line.ProductCode}."));

            if (line.Quantity <= 0m)
                return (resolved, new Error("GRN-005",
                    $"{product.Description}: quantity must be more than zero."));

            if (line.UnitCost < 0m)
                return (resolved, new Error("GRN-006",
                    $"{product.Description}: a unit cost cannot be negative."));

            // BR-INV-02, checked here as well as in the aggregate so the
            // screen can name the offending line before anything is
            // written rather than after.
            if (product.IsBatchTracked)
            {
                if (string.IsNullOrWhiteSpace(line.BatchNumber))
                    return (resolved, new Error("GRN-007",
                        $"{product.Description} is batch tracked: it needs "
                        + "a batch number."));

                if (line.ExpiryDate is null)
                    return (resolved, new Error("GRN-008",
                        $"{product.Description} is batch tracked: it needs "
                        + "an expiry date."));

                // Taking expired stock in is how it ends up on a shelf
                // and then in front of a patient.
                if (line.ExpiryDate <= _clock.Today)
                    return (resolved, new Error("GRN-009",
                        $"{product.Description}: batch {line.BatchNumber} "
                        + $"expires {line.ExpiryDate:yyyy-MM-dd}. Do not "
                        + "take expired stock onto the shelf."));
            }

            resolved.Add((product, line));
        }

        return (resolved, null);
    }
}

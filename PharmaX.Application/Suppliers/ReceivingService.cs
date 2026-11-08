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
using PurchaseOrder = PharmaX.Domain.Suppliers.PurchaseOrder;

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

    /// <summary>
    /// The product each captured line names, line for line, or null where
    /// a code names nothing - whether or not the lines would post. The
    /// screen shows it beside the code while another line still has a
    /// problem to put right.
    /// </summary>
    Task<Result<IReadOnlyList<GoodsReceiptProductDto?>>> IdentifyAsync(
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
    private readonly IPharmacySettings _settings;

    public ReceivingService(IGoodsReceiptRepository receipts,
        ISupplierRepository suppliers, IPurchaseOrderRepository orders,
        IProductRepository products, IJournalPoster journals,
        IStockService stock, IDocumentNumbering numbering,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow, ICurrentUser user, IClock clock,
        IPharmacySettings settings)
    {
        _settings = settings;
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
        DemandReceivingAccess();

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
        DemandReceivingAccess();

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

        // Previewing is the first half of receiving, and a user who may
        // not receive has no business rehearsing it.
        _permissions.Demand(PermissionCode.GoodsReceive);

        var resolved = await ResolveAsync(edit, ct);

        if (resolved.Problem is Error problem)
            return Result<IReadOnlyList<GoodsReceiptLineDto>>
                .Fail(problem.Code, problem.Message);

        // Line by line, as posting prices them, so a free line shows what
        // the paid line before it set and a mistyped cost shows its price.
        var (markup, step) = await Pricing.ReadAsync(_settings, ct);
        var sellAt = new Dictionary<Guid, decimal>();

        foreach (var (product, line) in resolved.Lines)
        {
            var now = sellAt.GetValueOrDefault(product.Id, product.RetailPrice.Amount);
            sellAt[product.Id] = Pricing.NewRetail(now, line.UnitCost, markup, step) ?? now;
        }

        IReadOnlyList<GoodsReceiptLineDto> preview = resolved.Lines
            .Select((r, i) => new GoodsReceiptLineDto(
                i + 1, r.Product.Id, r.Product.Description, r.Line.Quantity,
                r.Line.UnitCost, r.Line.Quantity * r.Line.UnitCost,
                r.Product.IsBatchTracked ? r.Line.BatchNumber : null,
                r.Product.IsBatchTracked ? r.Line.ExpiryDate : null,
                r.Product.Code, sellAt[r.Product.Id]))
            .ToList();

        return Result<IReadOnlyList<GoodsReceiptLineDto>>.Ok(preview);
    }

    public async Task<Result<IReadOnlyList<GoodsReceiptProductDto?>>>
        IdentifyAsync(GoodsReceiptEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.GoodsReceive);

        var order = edit.PurchaseOrderId is Guid orderId
            ? await _orders.GetAsync(orderId, ct)
            : null;

        var identified = new List<GoodsReceiptProductDto?>();

        foreach (var line in edit.Lines)
        {
            var orderLine = line.PurchaseOrderLineNo is int lineNo
                ? order?.Lines.FirstOrDefault(l => l.LineNo == lineNo)
                : null;

            var product = await FindProductAsync(line, orderLine, ct);

            identified.Add(product is null
                ? null
                : new GoodsReceiptProductDto(product.Id, product.Code,
                    product.Description, product.CostPrice.Amount,
                    product.IsBatchTracked));
        }

        return Result<IReadOnlyList<GoodsReceiptProductDto?>>.Ok(identified);
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

        var number = await _numbering.NextAsync(DocumentSeries.GoodsReceipt, ct);

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
        // Every delivery prices the item at its cost plus the markup:
        // cost x 1.5 unless System setup says otherwise.
        var (markup, step) = await Pricing.ReadAsync(_settings, ct);

        foreach (var line in receipt.Lines)
            if (await _products.GetAsync(line.ProductId, ct) is { } product
                && Pricing.NewRetail(product.RetailPrice.Amount, line.UnitCost.Amount,
                       markup, step) is decimal retail)
                Pricing.ChangeRetail(product, retail, _audit, _outbox,
                    $" (delivery {number} at {line.UnitCost})");

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
    /// Who may look at receipts: the people who book deliveries in, and
    /// the buyers who raised the orders they answer.
    /// </summary>
    private void DemandReceivingAccess()
    {
        if (_permissions.HasAny(PermissionCode.GoodsReceive,
                                PermissionCode.PurchaseOrderRaise))
            return;

        _permissions.Demand(PermissionCode.GoodsReceive);
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
    /// Turns the captured lines into products and refuses the whole
    /// receipt on the first problem it finds. Nothing is written until
    /// every line resolves, so a mistyped code cannot leave half a
    /// delivery posted.
    /// </summary>
    /// <remarks>
    /// A line brought across from a purchase order is resolved by the
    /// product the order names - its identity - and checked against that
    /// order line. The screen used to put the order line's description in
    /// the code column, which no product code ever matched, so every line
    /// of every ordered delivery had to be re-keyed before it would post
    /// (DEF-0013).
    /// </remarks>
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

        PurchaseOrder? order = null;

        if (edit.PurchaseOrderId is Guid orderId)
        {
            order = await _orders.GetAsync(orderId, ct);

            if (order is null)
                return (resolved, new Error("GRN-010",
                    "That purchase order no longer exists."));

            if (edit.SupplierId != Guid.Empty
                && order.SupplierId != edit.SupplierId)
                return (resolved, new Error("GRN-011",
                    $"Order {order.OrderNumber} is from a different "
                    + "supplier."));

            if (order.Status is PurchaseOrderStatus.Cancelled
                or PurchaseOrderStatus.Received)
                return (resolved, new Error("GRN-012",
                    $"Order {order.OrderNumber} is "
                    + $"{order.Status.ToString().ToLowerInvariant()} and "
                    + "cannot be received against."));
        }

        foreach (var line in edit.Lines)
        {
            Domain.Inventory.Product? product;
            PurchaseOrderLine? orderLine = null;

            if (line.PurchaseOrderLineNo is int lineNo)
            {
                if (order is null)
                    return (resolved, new Error("GRN-013",
                        "A line refers to an order line but no order is "
                        + "chosen."));

                orderLine = order.Lines.FirstOrDefault(l => l.LineNo == lineNo);

                if (orderLine is null)
                    return (resolved, new Error("GRN-014",
                        $"Order {order.OrderNumber} has no line {lineNo}."));
            }

            product = await FindProductAsync(line, orderLine, ct);

            if (product is null)
                return (resolved, new Error("GRN-004",
                    $"No product for code {line.ProductCode}."));

            if (orderLine is not null && orderLine.ProductId != product.Id)
                return (resolved, new Error("GRN-015",
                    $"{product.Code} is not what order line "
                    + $"{orderLine.LineNo} ordered ({orderLine.Description})."));

            if (line.Quantity <= 0m)
                return (resolved, new Error("GRN-005",
                    $"{product.Description}: quantity must be more than zero."));

            // Checked here, before anything is written, against what the
            // order line still expects less what earlier lines of this
            // delivery already put against it. An over-delivery went
            // through and left the line "received" for more than was
            // ever ordered (ADM-DEF-07).
            if (orderLine is not null)
            {
                var already = resolved
                    .Where(r => r.Item2.PurchaseOrderLineNo == orderLine.LineNo)
                    .Sum(r => r.Item2.Quantity);

                var left = orderLine.Outstanding - already;

                if (line.Quantity > left)
                    return (resolved, new Error("GRN-016",
                        $"{product.Description}: {line.Quantity:0.###} "
                        + $"received against order line {orderLine.LineNo}, "
                        + $"which has only {Math.Max(left, 0m):0.###} still to "
                        + "come. Receive the extra on a line of its own, "
                        + "without the order line, if it is being kept."));
            }

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

    /// <summary>
    /// The product a line names: the order's own product while the line
    /// still carries it, otherwise whatever the typed code or barcode is.
    /// </summary>
    private async Task<Domain.Inventory.Product?> FindProductAsync(
        GoodsReceiptLineEditDto line, PurchaseOrderLine? orderLine,
        CancellationToken ct)
    {
        if (line.ProductId is Guid productId)
            return await _products.GetAsync(productId, ct);

        if (orderLine is not null && string.IsNullOrWhiteSpace(line.ProductCode))
            return await _products.GetAsync(orderLine.ProductId, ct);

        return await _products.FindByCodeOrBarcodeAsync(line.ProductCode,
                                                        ct: ct);
    }
}

using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Application.Suppliers;

public interface IPurchasingService
{
    Task<Result<IReadOnlyList<PurchaseOrderDto>>> OutstandingAsync(
        Guid? supplierId = null, CancellationToken ct = default);

    Task<Result<PurchaseOrderDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<PurchaseOrderDto>> RaiseAsync(PurchaseOrderEditDto edit,
        CancellationToken ct = default);

    Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Saves an order as a draft: numbered and counted as coming, but not
    /// sent. Nothing reaches a supplier until somebody looks it over and
    /// sends it (SendDraftAsync).
    /// </summary>
    Task<Result<PurchaseOrderDto>> DraftAsync(PurchaseOrderEditDto edit,
        CancellationToken ct = default);

    /// The drafts waiting to be looked over and sent, oldest first.
    Task<Result<IReadOnlyList<PurchaseOrderDto>>> DraftsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Sends a draft, with whatever quantities the buyer settled on.
    /// </summary>
    /// <param name="quantities">
    /// The quantity for each line by line number; nought takes the line off.
    /// A line not named keeps what the draft had.
    /// </param>
    Task<Result<PurchaseOrderDto>> SendDraftAsync(Guid id,
        IReadOnlyDictionary<int, decimal> quantities,
        CancellationToken ct = default);

    /// <summary>
    /// What to order, taken from the reorder levels. The first thing a
    /// stock controller wants on a Monday, and the reason the reorder
    /// level is on the product at all.
    /// </summary>
    Task<Result<IReadOnlyList<PurchaseOrderLineDto>>> SuggestAsync(
        CancellationToken ct = default);

    /// The same suggestions, only for what a supplier usually supplies.
    Task<Result<IReadOnlyList<PurchaseOrderLineDto>>> SuggestForAsync(
        Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// How a product is bought: on order now, its usual supplier, and the
    /// supplier and cost of its last delivery.
    /// </summary>
    Task<Result<ProductBuyingDto>> BuyingAsync(Guid productId,
        CancellationToken ct = default);
}

public sealed class PurchasingService : IPurchasingService
{
    private readonly IPurchaseOrderRepository _orders;
    private readonly ISupplierRepository _suppliers;
    private readonly IProductRepository _products;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IGoodsReceiptRepository? _receipts;

    public PurchasingService(IPurchaseOrderRepository orders,
        ISupplierRepository suppliers, IProductRepository products,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IClock clock,
        IGoodsReceiptRepository? receipts = null)
    {
        _receipts = receipts;
        _orders = orders;
        _suppliers = suppliers;
        _products = products;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<PurchaseOrderDto>>>
        OutstandingAsync(Guid? supplierId = null,
                         CancellationToken ct = default)
    {
        DemandOrderAccess();

        var orders = await _orders.OutstandingAsync(supplierId, ct);
        var suppliers = await _suppliers.ListAsync(includeInactive: true, ct: ct);

        var names = suppliers.ToDictionary(s => s.Id, s => s.Name);
        var codes = await CodesAsync(orders.SelectMany(o => o.Lines), ct);

        IReadOnlyList<PurchaseOrderDto> items = orders
            .Select(o => PurchaseOrderDto.From(o,
                names.GetValueOrDefault(o.SupplierId, "Unknown"), codes))
            .ToList();

        return Result<IReadOnlyList<PurchaseOrderDto>>.Ok(items);
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        DemandOrderAccess();

        var order = await _orders.GetAsync(id, ct);
        if (order is null)
            return Result<PurchaseOrderDto>.Fail("PO-000", "Order not found.");

        var supplier = await _suppliers.GetAsync(order.SupplierId, ct);

        // Each line carries its product's own code, which is what goods
        // receipt posts against - not the description (DEF-0013).
        return Result<PurchaseOrderDto>.Ok(PurchaseOrderDto.From(order,
            supplier?.Name ?? "Unknown", await CodesAsync(order.Lines, ct)));
    }

    public Task<Result<PurchaseOrderDto>> RaiseAsync(
        PurchaseOrderEditDto edit, CancellationToken ct = default) =>
        CreateAsync(edit, send: true, ct);

    public Task<Result<PurchaseOrderDto>> DraftAsync(
        PurchaseOrderEditDto edit, CancellationToken ct = default) =>
        CreateAsync(edit, send: false, ct);

    public async Task<Result<IReadOnlyList<PurchaseOrderDto>>> DraftsAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PurchaseOrderRaise);

        var orders = await _orders.DraftsAsync(ct);
        var suppliers = await _suppliers.ListAsync(includeInactive: true, ct: ct);

        var names = suppliers.ToDictionary(s => s.Id, s => s.Name);
        var codes = await CodesAsync(orders.SelectMany(o => o.Lines), ct);

        IReadOnlyList<PurchaseOrderDto> items = orders
            .Select(o => PurchaseOrderDto.From(o,
                names.GetValueOrDefault(o.SupplierId, "Unknown"), codes))
            .ToList();

        return Result<IReadOnlyList<PurchaseOrderDto>>.Ok(items);
    }

    public async Task<Result<PurchaseOrderDto>> SendDraftAsync(Guid id,
        IReadOnlyDictionary<int, decimal> quantities,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(quantities);

        _permissions.Demand(PermissionCode.PurchaseOrderRaise);

        var order = await _orders.GetAsync(id, ct);
        if (order is null)
            return Result<PurchaseOrderDto>.Fail("PO-000", "Order not found.");

        if (order.Status != PurchaseOrderStatus.Draft)
            return Result<PurchaseOrderDto>.Fail("PO-006",
                $"Order {order.OrderNumber} has already been sent.");

        if (quantities.Values.Any(q => q < 0m))
            return Result<PurchaseOrderDto>.Fail("PO-004",
                "An order quantity cannot be negative.");

        // Checked before anything changes, so a refusal leaves the draft
        // as it was.
        if (order.Lines.All(l => quantities.GetValueOrDefault(l.LineNo,
                                     l.QuantityOrdered) == 0m))
            return Result<PurchaseOrderDto>.Fail("PO-002",
                "An order with no lines orders nothing. Discard the draft instead.");

        foreach (var line in order.Lines.ToList())
        {
            if (quantities.TryGetValue(line.LineNo, out var quantity)
                && line.QuantityOrdered != quantity)
                order.ChangeQuantity(line.LineNo, quantity);
        }

        order.Send();

        var supplier = await _suppliers.GetAsync(order.SupplierId, ct);

        _audit.Applied("inv.purchase.order.send", "PurchaseOrder", order.Id,
            $"{order.Lines.Count} line(s), {order.Total} to {supplier?.Name}");

        await _uow.SaveChangesAsync(ct);

        return Result<PurchaseOrderDto>.Ok(PurchaseOrderDto.From(order,
            supplier?.Name ?? "Unknown", await CodesAsync(order.Lines, ct)));
    }

    private async Task<Result<PurchaseOrderDto>> CreateAsync(
        PurchaseOrderEditDto edit, bool send, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.PurchaseOrderRaise);

        var supplier = await _suppliers.GetAsync(edit.SupplierId, ct);
        if (supplier is null)
            return Result<PurchaseOrderDto>.Fail("PO-001",
                "Choose a supplier.");

        if (edit.Lines.Count == 0)
            return Result<PurchaseOrderDto>.Fail("PO-002",
                "An order with no lines orders nothing.");

        var resolved = new List<(Domain.Inventory.Product Product,
                                 PurchaseOrderLineEditDto Line)>();

        foreach (var line in edit.Lines)
        {
            var product = await _products
                .FindByCodeOrBarcodeAsync(line.ProductCode, ct: ct);

            if (product is null)
                return Result<PurchaseOrderDto>.Fail("PO-003",
                    $"No product for code {line.ProductCode}.");

            if (line.Quantity <= 0m)
                return Result<PurchaseOrderDto>.Fail("PO-004",
                    $"{product.Description}: order more than zero.");

            resolved.Add((product, line));
        }

        var number = await _numbering.NextAsync(DocumentSeries.PurchaseOrder, ct);

        // Expected date defaults to the supplier's own lead time, which
        // is what that field on the supplier record is for.
        var expected = edit.ExpectedOn
                       ?? _clock.Today.AddDays(supplier.LeadTimeDays);

        var order = PurchaseOrder.Raise(number, supplier.Id, _clock.Today,
                                        expected);

        foreach (var (product, line) in resolved)
            order.AddLine(product.Id, product.Description, line.Quantity,
                          Money.Of(line.UnitCost));

        // Raised and sent in one act from the order screen: an order
        // sitting in draft is an order the supplier has not seen and the
        // shelf is waiting on. A draft is left for a person to send.
        if (send) order.Send();

        await _orders.AddAsync(order, ct);

        _audit.Applied(send ? "inv.purchase.order" : "inv.purchase.order.draft",
            "PurchaseOrder", order.Id,
            $"{order.Lines.Count} line(s), {order.Total} to {supplier.Name}");

        await _uow.SaveChangesAsync(ct);

        return Result<PurchaseOrderDto>.Ok(PurchaseOrderDto.From(order,
            supplier.Name, resolved.ToDictionary(r => r.Product.Id,
                                                 r => r.Product.Code)));
    }

    /// <summary>
    /// Who may look at orders: the buyers who raise them and the people
    /// who receive the deliveries against them.
    /// </summary>
    private void DemandOrderAccess()
    {
        if (_permissions.HasAny(PermissionCode.PurchaseOrderRaise,
                                PermissionCode.GoodsReceive))
            return;

        _permissions.Demand(PermissionCode.PurchaseOrderRaise);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> CodesAsync(
        IEnumerable<PurchaseOrderLine> lines, CancellationToken ct)
    {
        var codes = new Dictionary<Guid, string>();

        foreach (var productId in lines.Select(l => l.ProductId).Distinct())
        {
            if (await _products.GetAsync(productId, ct) is { } product)
                codes[productId] = product.Code;
        }

        return codes;
    }

    public async Task<Result> CancelAsync(Guid id, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.PurchaseOrderRaise);

        var order = await _orders.GetAsync(id, ct);
        if (order is null) return Result.Fail("PO-000", "Order not found.");

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("PO-005",
                "Cancelling an order needs a reason.");

        order.Cancel(reason);

        _audit.Applied("inv.purchase.order.cancel", "PurchaseOrder",
            order.Id, reason);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public Task<Result<IReadOnlyList<PurchaseOrderLineDto>>>
        SuggestAsync(CancellationToken ct = default) =>
        SuggestAsync(null, ct);

    public Task<Result<IReadOnlyList<PurchaseOrderLineDto>>>
        SuggestForAsync(Guid supplierId, CancellationToken ct = default) =>
        SuggestAsync(supplierId, ct);

    private async Task<Result<IReadOnlyList<PurchaseOrderLineDto>>>
        SuggestAsync(Guid? supplierId, CancellationToken ct)
    {
        _permissions.Demand(PermissionCode.PurchaseOrderRaise);

        var products = (await _products.SearchAsync("", take: int.MaxValue, ct: ct))
            .Where(p => supplierId is null || p.DefaultSupplierId == supplierId)
            .ToList();
        var onOrder = await _orders.OnOrderAsync(ct);

        // Orders up to twice the reorder level, which is the simplest
        // rule that does not reorder the same item next week. A proper
        // usage-based calculation belongs in a later release, against
        // real movement history.
        //
        // What is already on an order still to arrive counts as coming.
        // Ignoring it suggested the same 24 again straight after an order
        // for 50 was raised, which is a second order for stock already on
        // its way (ADM-DEF-06).
        IReadOnlyList<PurchaseOrderLineDto> suggestions = products
            .Where(p => p.IsActive && p.ReorderLevel > 0m)
            .Select(p => (Product: p,
                          Coming: onOrder.GetValueOrDefault(p.Id)))
            .Where(x => x.Product.QuantityOnHand + x.Coming
                        <= x.Product.ReorderLevel)
            .Select(x => (x.Product,
                          Quantity: x.Product.ReorderLevel * 2m
                                    - x.Product.QuantityOnHand - x.Coming))
            .Where(x => x.Quantity > 0m)
            .Select((x, i) => new PurchaseOrderLineDto(
                i + 1, x.Product.Id, x.Product.Description,
                QuantityOrdered: x.Quantity,
                QuantityReceived: 0m,
                Outstanding: x.Quantity,
                UnitCost: x.Product.CostPrice.Amount,
                LineTotal: x.Quantity * x.Product.CostPrice.Amount,
                ProductCode: x.Product.Code))
            .ToList();

        return Result<IReadOnlyList<PurchaseOrderLineDto>>.Ok(suggestions);
    }

    public async Task<Result<ProductBuyingDto>> BuyingAsync(Guid productId,
        CancellationToken ct = default)
    {
        DemandOrderAccess();

        var product = await _products.GetAsync(productId, ct);
        if (product is null)
            return Result<ProductBuyingDto>.Fail("PO-007", "Product not found.");

        var onOrder = (await _orders.OnOrderAsync(ct)).GetValueOrDefault(productId);
        var last = _receipts is null ? null : await _receipts.LastPurchaseAsync(productId, ct);

        var usual = product.DefaultSupplierId is Guid usualId
            ? await _suppliers.GetAsync(usualId, ct)
            : null;
        var lastFrom = last is null ? null : await _suppliers.GetAsync(last.SupplierId, ct);

        return Result<ProductBuyingDto>.Ok(new ProductBuyingDto(productId, onOrder,
            usual?.Id, usual?.Name, lastFrom?.Id, lastFrom?.Name, last?.UnitCost,
            last?.ReceivedOn));
    }
}
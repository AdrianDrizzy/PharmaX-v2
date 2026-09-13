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
    /// What to order, taken from the reorder levels. The first thing a
    /// stock controller wants on a Monday, and the reason the reorder
    /// level is on the product at all.
    /// </summary>
    Task<Result<IReadOnlyList<PurchaseOrderLineDto>>> SuggestAsync(
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

    public PurchasingService(IPurchaseOrderRepository orders,
        ISupplierRepository suppliers, IProductRepository products,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IClock clock)
    {
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
        _permissions.Demand(PermissionCode.ProductRead);

        var orders = await _orders.OutstandingAsync(supplierId, ct);
        var suppliers = await _suppliers.ListAsync(includeInactive: true, ct);

        var names = suppliers.ToDictionary(s => s.Id, s => s.Name);

        IReadOnlyList<PurchaseOrderDto> items = orders
            .Select(o => PurchaseOrderDto.From(o,
                names.GetValueOrDefault(o.SupplierId, "Unknown")))
            .ToList();

        return Result<IReadOnlyList<PurchaseOrderDto>>.Ok(items);
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var order = await _orders.GetAsync(id, ct);
        if (order is null)
            return Result<PurchaseOrderDto>.Fail("PO-000", "Order not found.");

        var supplier = await _suppliers.GetAsync(order.SupplierId, ct);

        return Result<PurchaseOrderDto>.Ok(
            PurchaseOrderDto.From(order, supplier?.Name ?? "Unknown"));
    }

    public async Task<Result<PurchaseOrderDto>> RaiseAsync(
        PurchaseOrderEditDto edit, CancellationToken ct = default)
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
                .FindByCodeOrBarcodeAsync(line.ProductCode, ct);

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

        // Raised and sent in one act: an order sitting in draft is an
        // order the supplier has not seen and the shelf is waiting on.
        order.Send();

        await _orders.AddAsync(order, ct);

        _audit.Applied("inv.purchase.order", "PurchaseOrder", order.Id,
            $"{order.Lines.Count} line(s), {order.Total} to {supplier.Name}");

        await _uow.SaveChangesAsync(ct);

        return Result<PurchaseOrderDto>.Ok(
            PurchaseOrderDto.From(order, supplier.Name));
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

    public async Task<Result<IReadOnlyList<PurchaseOrderLineDto>>>
        SuggestAsync(CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var products = await _products.SearchAsync("", take: 2000, ct);

        // Orders up to twice the reorder level, which is the simplest
        // rule that does not reorder the same item next week. A proper
        // usage-based calculation belongs in a later release, against
        // real movement history.
        IReadOnlyList<PurchaseOrderLineDto> suggestions = products
            .Where(p => p.IsActive && p.IsBelowReorderLevel)
            .Where(p => p.ReorderLevel > 0m)
            .Select((p, i) => new PurchaseOrderLineDto(
                i + 1, p.Id, p.Description,
                QuantityOrdered: p.ReorderLevel * 2m - p.QuantityOnHand,
                QuantityReceived: 0m,
                Outstanding: p.ReorderLevel * 2m - p.QuantityOnHand,
                UnitCost: p.CostPrice.Amount,
                LineTotal: (p.ReorderLevel * 2m - p.QuantityOnHand)
                           * p.CostPrice.Amount))
            .ToList();

        return Result<IReadOnlyList<PurchaseOrderLineDto>>.Ok(suggestions);
    }
}

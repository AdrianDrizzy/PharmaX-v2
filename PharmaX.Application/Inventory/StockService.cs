using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Sales;

namespace PharmaX.Application.Inventory;

public interface IStockService
{
    /// <summary>
    /// Takes the sold quantities off the shelf and writes the matching
    /// movements. Called inside the sale's transaction, so a sale that
    /// fails to save takes its stock movements with it.
    /// </summary>
    Task ApplyIssueAsync(Sale sale, CancellationToken ct = default);

    /// <summary>
    /// Takes an issued dispensing off the shelf: FEFO across as many
    /// batches as each line needs, the product's own on-hand brought down
    /// to match, and a movement for every batch touched. Called inside the
    /// issue's transaction, so an issue that fails moves no stock.
    /// </summary>
    Task ApplyDispensingAsync(Domain.Dispensing.Dispensing dispensing,
                              CancellationToken ct = default);

    /// Hands over what a line owed, off the shelf as the issue would.
    Task ApplyOwedAsync(Domain.Dispensing.Dispensing dispensing,
                        Domain.Dispensing.DispensingLine line,
                        CancellationToken ct = default);

    /// <summary>
    /// An issued item's quantity corrected by so many units: more off the
    /// shelf, or (negative) put back on it, written against the
    /// dispensing so a later reversal returns only what is still out.
    /// </summary>
    Task ApplyDispensingAmendAsync(Domain.Dispensing.Dispensing dispensing,
                                   Domain.Dispensing.DispensingLine line,
                                   decimal units, CancellationToken ct = default);

    /// <summary>
    /// Puts the restockable lines of a return back on the shelf and
    /// writes the rest off. Called inside the return's transaction.
    /// </summary>
    /// <summary>
    /// Puts back on the shelf exactly what a reversed dispensing took off
    /// it, batch by batch.
    /// </summary>
    Task ApplyDispensingReversalAsync(Domain.Dispensing.Dispensing dispensing,
        CancellationToken ct = default);

    Task ApplyReturnAsync(SaleReturn saleReturn,
                          CancellationToken ct = default);

    Task<decimal> OnHandAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Brings a posted goods receipt onto the shelf: a batch per
    /// batch-tracked line, a movement per line, and the product's own
    /// quantity brought up to match. Called inside the receipt's
    /// transaction, so a receipt that fails to save moves no stock.
    /// </summary>
    Task ApplyReceiptAsync(Domain.Suppliers.GoodsReceipt receipt,
                           CancellationToken ct = default);

    /// <summary>
    /// Brings the system's figures up to what was counted. Writes an
    /// adjustment movement per varying line, carrying the signed
    /// difference, so the stock ledger explains the change rather than
    /// the balance simply being different tomorrow.
    /// </summary>
    Task ApplyStocktakeAsync(Stocktake stocktake,
                             CancellationToken ct = default);
}

public sealed class StockService : IStockService
{
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IStockMovementWriter _movements;
    private readonly IClock _clock;

    public StockService(IProductRepository products, IBatchRepository batches,
                        IStockMovementWriter movements, IClock clock)
    {
        _products = products;
        _batches = batches;
        _movements = movements;
        _clock = clock;
    }

    public async Task ApplyIssueAsync(Sale sale, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        // A collection's script lines take money for medicine that already
        // left the shelf when the pharmacist issued the dispensing.
        // Applying them again here would issue the same stock twice and
        // leave every one of those products short by the amount dispensed
        // - a discrepancy nobody could explain at the next stocktake.
        foreach (var line in sale.Lines.Where(l => !l.IsVoided && !l.IsScript))
        {
            var product = await _products.GetAsync(line.ProductId, ct);
            if (product is null) continue;

            if (line.BatchId is Guid batchId)
            {
                var batch = await _batches.GetAsync(batchId, ct);
                batch?.Issue(line.Quantity);
            }

            var balance = product.QuantityOnHand - line.Quantity;
            product.SetQuantityOnHand(balance);

            _movements.Add(StockMovement.Record(
                productId: line.ProductId,
                batchId: line.BatchId,
                movementType: MovementType.Issue,
                quantity: line.Quantity,
                balanceAfter: balance,
                occurredAtUtc: _clock.UtcNow,
                sourceDocumentType: "Sale",
                sourceDocumentId: sale.Id));
        }
    }

    public async Task ApplyDispensingAsync(
        Domain.Dispensing.Dispensing dispensing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispensing);

        // What is owed stays on the shelf's books until it is handed over.
        foreach (var line in dispensing.Lines)
        {
            var now = line.Quantity - (line.Owed ?? 0m);
            if (now <= 0m) continue;

            if (await IssueAsync(dispensing.Id, line, now, ct) is { } batches)
                line.AllocateBatches(batches);
        }
    }

    public async Task ApplyOwedAsync(Domain.Dispensing.Dispensing dispensing,
        Domain.Dispensing.DispensingLine line, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispensing);
        ArgumentNullException.ThrowIfNull(line);

        line.GiveOwed(await IssueAsync(dispensing.Id, line, line.Owed ?? 0m, ct),
                      _clock.UtcNow);
    }

    public async Task ApplyDispensingAmendAsync(Domain.Dispensing.Dispensing dispensing,
        Domain.Dispensing.DispensingLine line, decimal units, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispensing);
        ArgumentNullException.ThrowIfNull(line);

        if (units > 0m)
        {
            if (await IssueAsync(dispensing.Id, line, units, ct) is { } taken)
                line.TakeMore(taken);

            return;
        }

        if (units == 0m) return;

        var product = await _products.GetAsync(line.ProductId, ct)
            ?? throw new Domain.Exceptions.DomainException(
                $"{line.Description} is no longer in the catalogue.");

        // A tracked product handed over before PharmaX recorded batches -
        // an imported dispensing - goes back onto the batch the line names,
        // or else its latest-expiry batch, so the shelf and its batches
        // still agree.
        Batch? fallback = null;

        if (product.IsBatchTracked)
            fallback = (line.BatchId is Guid named ? await _batches.GetAsync(named, ct) : null)
                ?? (await _batches.ForProductAsync(product.Id, ct))
                    .OrderByDescending(b => b.ExpiryDate)
                    .FirstOrDefault();

        var balance = product.QuantityOnHand;

        foreach (var (batchId, back) in line.GiveBack(-units))
        {
            var packs = product.PacksFor(back);
            var batch = batchId is Guid id ? await _batches.GetAsync(id, ct) : fallback;

            batch?.Return(packs);
            balance += packs;

            _movements.Add(StockMovement.Record(
                productId: product.Id,
                batchId: batch?.Id,
                movementType: MovementType.Return,
                quantity: packs,
                balanceAfter: balance,
                occurredAtUtc: _clock.UtcNow,
                reasonCode: "DISPENSING-AMENDED",
                sourceDocumentType: "Dispensing",
                sourceDocumentId: dispensing.Id));
        }

        product.SetQuantityOnHand(balance);
    }

    /// Takes a dispensed quantity off the shelf: the batches it came from
    /// for a batch-tracked product, null for one that is not.
    private async Task<IReadOnlyList<Domain.Dispensing.BatchAllocation>?> IssueAsync(
        Guid dispensingId, Domain.Dispensing.DispensingLine line,
        decimal quantity, CancellationToken ct)
    {
        var product = await _products.GetAsync(line.ProductId, ct)
            ?? throw new Domain.Exceptions.DomainException(
                $"{line.Description} is no longer in the catalogue.");

        // A dispensing is counted in tablets, capsules or millilitres
        // and the shelf in packs (ADM-DEF-15).
        if (!product.IsBatchTracked)
        {
            var packs = product.PacksFor(quantity);
            var after = product.QuantityOnHand - packs;
            product.SetQuantityOnHand(after);

            _movements.Add(StockMovement.Record(
                productId: product.Id,
                batchId: null,
                movementType: MovementType.Issue,
                quantity: packs,
                balanceAfter: after,
                occurredAtUtc: _clock.UtcNow,
                sourceDocumentType: "Dispensing",
                sourceDocumentId: dispensingId));

            return null;
        }

        // Allocated again against the stock as it is now, inside the
        // transaction. The batches are tracked, so a second line of
        // the same product sees what the first has already taken.
        var batches = await _batches.ForProductAsync(product.Id, ct);

        IReadOnlyList<Allocation> allocations;

        // In the dispensing's own units, so the label's batch split
        // reads in tablets; each batch then gives up the packs that
        // many tablets come to.
        try
        {
            allocations = FefoAllocator.Allocate(batches, quantity,
                _clock.Today, product.UnitsInPack);
        }
        catch (Domain.Exceptions.DomainException)
        {
            var held = batches.Where(b => b.IsAvailableOn(_clock.Today))
                              .Sum(b => b.QuantityOnHand)
                       * product.UnitsInPack;

            throw new Domain.Exceptions.DomainException(
                $"{line.Description}: only {held:0.###} unexpired in "
                + $"stock, so {quantity:0.###} cannot be issued.");
        }

        var taken = allocations.Select(a =>
        {
            var batch = batches.Single(b => b.Id == a.BatchId);
            return new Domain.Dispensing.BatchAllocation(batch.Id,
                batch.BatchNumber, batch.ExpiryDate, a.Quantity);
        }).ToList();

        var balance = product.QuantityOnHand;

        foreach (var allocation in allocations)
        {
            var batch = batches.Single(b => b.Id == allocation.BatchId);

            // A batch emptied gives up exactly what it held, so no
            // thousandth of a pack is left behind by the rounding.
            var packs = allocation.Quantity
                        >= batch.QuantityOnHand * product.UnitsInPack
                ? batch.QuantityOnHand
                : product.PacksFor(allocation.Quantity);

            batch.Issue(packs);

            balance -= packs;

            _movements.Add(StockMovement.Record(
                productId: product.Id,
                batchId: batch.Id,
                movementType: MovementType.Issue,
                quantity: packs,
                balanceAfter: balance,
                occurredAtUtc: _clock.UtcNow,
                sourceDocumentType: "Dispensing",
                sourceDocumentId: dispensingId));
        }

        product.SetQuantityOnHand(balance);
        return taken;
    }

    /// <remarks>
    /// Read off the movements the issue wrote rather than worked out
    /// again from the tablets, so a broken pack goes back to the
    /// thousandth it came off at. What a correction already put back is
    /// taken off first, so nothing comes back twice.
    /// </remarks>
    public async Task ApplyDispensingReversalAsync(
        Domain.Dispensing.Dispensing dispensing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dispensing);

        var moved = await _movements.ForSourceAsync("Dispensing",
            dispensing.Id, ct);

        foreach (var group in moved
                     .Where(m => m.MovementType is MovementType.Issue or MovementType.Return)
                     .GroupBy(m => m.ProductId))
        {
            var product = await _products.GetAsync(group.Key, ct);
            if (product is null) continue;

            // Still out, batch by batch: issued less corrected back.
            var still = group.GroupBy(m => m.BatchId)
                .Select(b => (BatchId: b.Key, Packs: b.Sum(m =>
                    m.MovementType == MovementType.Issue ? Math.Abs(m.Quantity) : -Math.Abs(m.Quantity))))
                .ToList();

            var corrected = still.Where(s => s.Packs < 0m).Sum(s => -s.Packs);
            var balance = product.QuantityOnHand;

            foreach (var (batchId, issued) in still.Where(s => s.Packs > 0m))
            {
                // A correction put back on another batch than the one the
                // packs left from still comes off the total once.
                var less = Math.Min(corrected, issued);
                var packs = issued - less;
                corrected -= less;

                if (packs <= 0m) continue;

                if (batchId is Guid id && await _batches.GetAsync(id, ct) is { } batch)
                    batch.Return(packs);

                balance += packs;

                _movements.Add(StockMovement.Record(
                    productId: product.Id,
                    batchId: batchId,
                    movementType: MovementType.Return,
                    quantity: packs,
                    balanceAfter: balance,
                    occurredAtUtc: _clock.UtcNow,
                    reasonCode: "DISPENSING-REVERSED",
                    sourceDocumentType: "DispensingReversal",
                    sourceDocumentId: dispensing.Id));
            }

            product.SetQuantityOnHand(balance);
        }
    }

    public async Task ApplyReturnAsync(SaleReturn saleReturn,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(saleReturn);

        foreach (var line in saleReturn.Lines)
        {
            var product = await _products.GetAsync(line.ProductId, ct);
            if (product is null) continue;

            if (!line.RestockToShelf)
            {
                // Credited but not restocked: the goods are gone, so the
                // movement records the write-off rather than a return,
                // and the shelf figure does not change.
                _movements.Add(StockMovement.Record(
                    productId: line.ProductId,
                    batchId: line.BatchId,
                    movementType: MovementType.Disposal,
                    quantity: line.Quantity,
                    balanceAfter: product.QuantityOnHand,
                    occurredAtUtc: _clock.UtcNow,
                    reasonCode: "RETURN-UNFIT",
                    sourceDocumentType: "SaleReturn",
                    sourceDocumentId: saleReturn.Id));

                continue;
            }

            // Returned stock goes back to the batch it came from, not to
            // the batch FEFO would pick now: expiry follows the box.
            if (line.BatchId is Guid batchId)
            {
                var batch = await _batches.GetAsync(batchId, ct);
                batch?.Return(line.Quantity);
            }

            var balance = product.QuantityOnHand + line.Quantity;
            product.SetQuantityOnHand(balance);

            _movements.Add(StockMovement.Record(
                productId: line.ProductId,
                batchId: line.BatchId,
                movementType: MovementType.Return,
                quantity: line.Quantity,
                balanceAfter: balance,
                occurredAtUtc: _clock.UtcNow,
                sourceDocumentType: "SaleReturn",
                sourceDocumentId: saleReturn.Id));
        }
    }

    public async Task<decimal> OnHandAsync(Guid productId,
                                           CancellationToken ct = default)
    {
        var product = await _products.GetAsync(productId, ct);
        return product?.QuantityOnHand ?? 0m;
    }

    public async Task ApplyReceiptAsync(Domain.Suppliers.GoodsReceipt receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        foreach (var line in receipt.Lines)
        {
            var product = await _products.GetAsync(line.ProductId, ct);
            if (product is null) continue;

            Guid? batchId = null;

            if (product.IsBatchTracked && line.BatchNumber is not null
                && line.ExpiryDate is DateOnly expiry)
            {
                // The same batch number from the same supplier twice is
                // one batch with more units in it, not two rows
                // competing for the same place in FEFO order.
                var batches = await _batches.ForProductAsync(product.Id, ct);

                var existing = batches.FirstOrDefault(b =>
                    string.Equals(b.BatchNumber, line.BatchNumber,
                                  StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    existing.Return(line.Quantity);
                    batchId = existing.Id;
                }
                else
                {
                    var batch = Batch.Receive(product.Id, line.BatchNumber,
                        expiry, line.Quantity, line.UnitCost, _clock.UtcNow,
                        receipt.SupplierId);

                    await _batches.AddAsync(batch, ct);
                    batchId = batch.Id;
                }
            }

            // The last price paid becomes the cost price, which is what
            // the margin on every subsequent sale is measured against.
            product.ChangeCostPrice(line.UnitCost);

            var balance = product.QuantityOnHand + line.Quantity;
            product.SetQuantityOnHand(balance);

            _movements.Add(StockMovement.Record(
                productId: line.ProductId,
                batchId: batchId,
                movementType: MovementType.Receipt,
                quantity: line.Quantity,
                balanceAfter: balance,
                occurredAtUtc: _clock.UtcNow,
                sourceDocumentType: "GoodsReceipt",
                sourceDocumentId: receipt.Id));
        }
    }

    public async Task ApplyStocktakeAsync(Stocktake stocktake,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stocktake);

        // Grouped by product because a batch-tracked item has a line per
        // batch, and the product's own on-hand moves by the sum of the
        // corrections across all of them.
        foreach (var group in stocktake.Lines.GroupBy(l => l.ProductId))
        {
            var product = await _products.GetAsync(group.Key, ct);
            if (product is null) continue;

            var correction = 0m;

            foreach (var line in group.Where(l => l.Variance != 0m))
            {
                // The variance, applied as a delta to whatever is on
                // hand now. Not the counted figure as an absolute:
                // trading carries on while a shelf is counted, and
                // overwriting would silently reverse those sales.
                if (line.BatchId is Guid batchId)
                {
                    var batch = await _batches.GetAsync(batchId, ct);
                    batch?.AdjustBy(line.Variance);
                }

                correction += line.Variance;

                _movements.Add(StockMovement.Record(
                    productId: line.ProductId,
                    batchId: line.BatchId,
                    movementType: MovementType.StockTake,
                    quantity: line.Variance,
                    balanceAfter: product.QuantityOnHand + correction,
                    occurredAtUtc: _clock.UtcNow,
                    reasonCode: line.Variance > 0m ? "COUNT-OVER"
                                                   : "COUNT-SHORT",
                    sourceDocumentType: "Stocktake",
                    sourceDocumentId: stocktake.Id));
            }

            // A count that agreed writes nothing at all, which is the
            // common case and the cheapest one.
            if (correction == 0m) continue;

            product.SetQuantityOnHand(product.QuantityOnHand + correction);
        }
    }
}

/// <summary>
/// Appends a movement to the current unit of work. Separate from the
/// repositories because a movement is written, never queried by the till.
/// </summary>
public interface IStockMovementWriter
{
    void Add(StockMovement movement);

    /// The movements one document wrote, in the order they were written.
    Task<IReadOnlyList<StockMovement>> ForSourceAsync(string sourceType,
        Guid sourceId, CancellationToken ct = default);
}

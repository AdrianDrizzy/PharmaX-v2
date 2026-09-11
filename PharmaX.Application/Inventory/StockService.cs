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
    /// Puts the restockable lines of a return back on the shelf and
    /// writes the rest off. Called inside the return's transaction.
    /// </summary>
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

        foreach (var line in sale.Lines.Where(l => !l.IsVoided))
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
}

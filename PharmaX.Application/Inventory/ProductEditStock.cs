using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

/// <summary>
/// The stock half of the product editor: the quantity on hand, and each
/// batch's number, expiry and quantity.
/// </summary>
/// <remarks>
/// Planned in full before anything is applied, so that a refused row
/// cannot leave the others half written. A quantity is a count, posted
/// as an adjustment the way the list's own cells post one, so the stock
/// ledger still explains every unit on the shelf.
/// </remarks>
public sealed partial class ProductService
{
    /// <param name="onHand">The product's quantity on hand now.</param>
    /// <param name="batches">Every batch of the product, emptied ones too.</param>
    private static Result<StockPlan> PlanStock(decimal onHand,
        IReadOnlyList<Batch> batches, ProductEditDto edit)
    {
        var plan = new StockPlan();
        var rows = edit.Batches ?? Array.Empty<ProductBatchEditDto>();

        // What each batch will be called once this edit is applied, so a
        // rename cannot land on a number another batch already carries.
        var numbers = batches.ToDictionary(b => b.Id, b => b.BatchNumber);

        foreach (var row in rows.Where(r => r.BatchId is not null))
        {
            var batch = batches.FirstOrDefault(b => b.Id == row.BatchId);

            if (batch is null)
                return Refuse("INV-032",
                    "A batch on this product has changed since it was "
                    + "opened. Close it and open it again.");

            if (Check(row) is { } problem) return problem;

            var number = row.BatchNumber.Trim();

            numbers[batch.Id] = number;

            plan.Changes.Add(new BatchChange(batch, number,
                row.Expiry!.Value, row.QuantityOnHand!.Value
                                   - batch.QuantityOnHand));
        }

        // Stock that is on the product but on no batch: a product counted
        // before it was batch tracked, or brought over from the old
        // system. A batch added here is that stock being given its pack
        // identity, so the new batches take it over and only the
        // difference is a count.
        var held = batches.Where(b => b.QuantityOnHand > 0m).ToList();
        var loose = Math.Max(0m, onHand - held.Sum(b => b.QuantityOnHand));
        var added = new List<string>();

        foreach (var row in rows.Where(r => r.BatchId is null))
        {
            if (Check(row) is { } problem) return problem;

            var number = row.BatchNumber.Trim();

            if (row.QuantityOnHand is not > 0m)
                return Refuse("INV-031",
                    $"Enter how many of batch {number} are on the shelf.");

            var quantity = row.QuantityOnHand.Value;
            var taken = Math.Min(loose, quantity);

            loose -= taken;
            added.Add(number);

            plan.Openings.Add(new BatchOpening(number, row.Expiry!.Value,
                quantity, quantity - taken));
        }

        // Whatever loose stock the new batches did not account for was
        // not found when the shelf was looked at.
        if (plan.Openings.Count > 0) plan.LooseShort = -loose;

        foreach (var change in plan.Changes.Where(c => !string.Equals(
                     c.Number, c.Batch.BatchNumber,
                     StringComparison.OrdinalIgnoreCase)))
        {
            if (numbers.Any(n => n.Key != change.Batch.Id
                    && Same(n.Value, change.Number))
                || added.Any(n => Same(n, change.Number)))
                return Refuse("INV-032",
                    $"Batch {change.Number} is already on this product.");
        }

        foreach (var number in added)
        {
            if (numbers.Values.Any(n => Same(n, number))
                || added.Count(n => Same(n, number)) > 1)
                return Refuse("INV-032",
                    $"Batch {number} is already on this product.");
        }

        if (edit.QuantityOnHand is decimal count && count != onHand)
        {
            if (count < 0m)
                return Refuse("INV-031",
                    "Quantity on hand cannot be negative.");

            if (held.Count > 0 || plan.Openings.Count > 0)
                return Refuse("INV-031",
                    "This product's stock is on batches. Change the "
                    + "quantity on each batch.");

            plan.ProductCount = count - onHand;
        }

        var final = onHand + plan.Changes.Sum(c => c.Delta)
                           + plan.Openings.Sum(o => o.Counted)
                           + plan.LooseShort + plan.ProductCount;

        if (final < 0m)
            return Refuse("INV-031",
                "That would take the quantity on hand below zero.");

        plan.LeavesStockOnABatch = plan.Openings.Count > 0
            || batches.Any(b => b.QuantityOnHand
                + (plan.Changes.FirstOrDefault(c => c.Batch.Id == b.Id)
                       ?.Delta ?? 0m) > 0m);

        // A shelf figure changed with no reason is a loss or a gain
        // nobody can explain at the next stocktake.
        plan.Reason = (edit.StockReason ?? "").Trim();

        if (plan.MovesStock && plan.Reason.Length == 0)
            return Refuse("INV-033", NoReason);

        if (plan.Reason.Length > ReasonLength)
            plan.Reason = plan.Reason[..ReasonLength];

        return Result<StockPlan>.Ok(plan);

        static Result<StockPlan>? Check(ProductBatchEditDto row)
        {
            var number = (row.BatchNumber ?? "").Trim();

            if (number.Length is < 1 or > 50)
                return Refuse("INV-032",
                    "A batch number is 1 to 50 characters.");

            if (row.Expiry is null)
                return Refuse("INV-032",
                    $"Enter the expiry date for batch {number}.");

            if (row.QuantityOnHand is null or < 0m)
                return Refuse("INV-031",
                    $"Enter the quantity on hand for batch {number}, "
                    + "zero or more.");

            return null;
        }

        static bool Same(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static Result<StockPlan> Refuse(string code, string message) =>
            Result<StockPlan>.Fail(code, message);
    }

    /// <summary>
    /// Writes a plan PlanStock accepted. Nothing here can refuse: the
    /// checks, and the permission the counts need, have already been made.
    /// </summary>
    private async Task ApplyStockAsync(Product product, StockPlan plan,
        CancellationToken ct)
    {
        var invariant = CultureInfo.InvariantCulture;
        var balance = product.QuantityOnHand;
        var before = balance;

        foreach (var change in plan.Changes)
        {
            var batch = change.Batch;

            if (!string.Equals(change.Number, batch.BatchNumber,
                    StringComparison.Ordinal))
            {
                batch.SetBatchNumber(change.Number);
                _audit.Applied("inv.batch.correct", "Batch", batch.Id,
                    change.Number);
            }

            if (change.Expiry != batch.ExpiryDate)
            {
                batch.SetExpiry(change.Expiry);
                _audit.Applied("inv.batch.correct", "Batch", batch.Id,
                    change.Expiry.ToString("yyyy-MM-dd", invariant));
            }

            if (change.Delta == 0m) continue;

            batch.AdjustBy(change.Delta);
            balance += change.Delta;
            RecordCount(product, batch.Id, change.Delta, balance, plan.Reason);
        }

        foreach (var opening in plan.Openings)
        {
            var batch = Batch.Receive(product.Id, opening.Number,
                opening.Expiry, opening.Quantity, product.CostPrice,
                _clock.UtcNow);

            await _batches.AddAsync(batch, ct);
            _audit.Applied("inv.batch.record", "Batch", batch.Id,
                opening.Number);

            if (opening.Counted == 0m) continue;

            balance += opening.Counted;
            RecordCount(product, batch.Id, opening.Counted, balance, plan.Reason);
        }

        // Stock on a batch has to be picked by batch at the till.
        if (plan.Openings.Count > 0) product.SetBatchTracked(true);

        foreach (var delta in new[] { plan.LooseShort, plan.ProductCount })
        {
            if (delta == 0m) continue;

            balance += delta;
            RecordCount(product, null, delta, balance, plan.Reason);
        }

        if (balance == product.QuantityOnHand) return;

        product.SetQuantityOnHand(balance);

        _audit.Applied("inv.stock.adjust", "Product", product.Id,
            StockAudit(product, before, balance, plan.Reason));
    }

    /// The most a stock reason keeps; the prompt takes no more.
    private const int ReasonLength = 200;

    private const string NoReason =
        "Say why the stock on hand changed - counted, damaged, found, "
        + "or a mistake put right.";

    /// <summary>
    /// A count from the product itself - the editor or the list - is the
    /// pharmacist's and the assistant's own correction, and whoever posts
    /// stocktakes may make it too.
    /// </summary>
    private void DemandStockCorrection()
    {
        if (!_permissions.Has(PermissionCode.StockCorrect))
            _permissions.Demand(PermissionCode.StockTakePost);
    }

    private void RecordCount(Product product, Guid? batchId, decimal delta,
        decimal balance, string reason) =>
        _movements.Add(StockMovement.Record(
            productId: product.Id,
            batchId: batchId,
            movementType: MovementType.Adjustment,
            quantity: delta,
            balanceAfter: balance,
            occurredAtUtc: _clock.UtcNow,
            reasonCode: CountReason(delta, reason),
            sourceDocumentType: "Product",
            sourceDocumentId: product.Id));

    /// <summary>
    /// What the movement reports print in their Reason column: over or
    /// short, as a stocktake's count says it, then why.
    /// </summary>
    /// <remarks>
    /// The column holds thirty; the whole reason is on the audit entry.
    /// </remarks>
    internal static string CountReason(decimal delta, string reason)
    {
        var why = (reason ?? "").Trim();
        var code = delta > 0m ? "COUNT-OVER" : "COUNT-SHORT";

        if (why.Length > 0) code += ": " + why;

        return code.Length <= 30 ? code : code[..30];
    }

    private static string StockAudit(Product product, decimal before,
        decimal after, string reason) =>
        string.Format(CultureInfo.InvariantCulture,
            "{0} on hand {1:0.###} to {2:0.###}. {3}", product.Code, before,
            after, reason).Trim();

    private sealed class StockPlan
    {
        public List<BatchChange> Changes { get; } = new();
        public List<BatchOpening> Openings { get; } = new();

        /// Why the stock changed, as the operator gave it.
        public string Reason { get; set; } = "";

        /// Loose stock the new batches did not account for, as a
        /// negative count. Zero when no batch was added.
        public decimal LooseShort { get; set; }

        /// A count of a product that holds no batches.
        public decimal ProductCount { get; set; }

        public bool LeavesStockOnABatch { get; set; }

        public bool MovesStock =>
            ProductCount != 0m || LooseShort != 0m
            || Changes.Any(c => c.Delta != 0m)
            || Openings.Any(o => o.Counted != 0m);
    }

    private sealed record BatchChange(Batch Batch, string Number,
        DateOnly Expiry, decimal Delta);

    /// <param name="Counted">
    /// The part of the quantity that was not already loose on the
    /// product: a count over.
    /// </param>
    private sealed record BatchOpening(string Number, DateOnly Expiry,
        decimal Quantity, decimal Counted);
}

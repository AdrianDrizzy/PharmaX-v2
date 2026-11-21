using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

public sealed partial class ProductService
{
    /// Batch tracking turned on while stock sits on no batch.
    public const string NeedsBatchForStock = "INV-044";

    private static string NeedsBatchMessage(Product product) =>
        $"{product.Description} has {product.QuantityOnHand:0.###} in stock. "
        + "Enter the batch number and expiry of that stock first: it then "
        + "becomes batch tracked.";

    public async Task<Result> ApplyCellEditAsync(ProductCellEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // The list stays open while the product editor, a delivery or a
        // till changes the same rows, so each cell starts from what is in
        // the database now rather than from what the list read earlier.
        _uow.Forget();

        var product = await _products.GetAsync(edit.ProductId, ct);

        if (product is null || !InScope(product))
            return Result.Fail("INV-000", "Product not found.");

        return edit.Column switch
        {
            ProductCell.Code => await EditCodeAsync(product, edit.Value, ct),
            ProductCell.Description =>
                await EditDescriptionAsync(product, edit.Value, ct),
            ProductCell.RetailPrice =>
                await EditRetailAsync(product, edit.Value, ct),
            ProductCell.QuantityOnHand =>
                await EditQuantityAsync(product, edit.BatchId, edit.Value,
                    edit.Extra, ct),
            ProductCell.Schedule =>
                await EditScheduleAsync(product, edit.Value, ct),
            ProductCell.BatchTracked =>
                await EditBatchTrackedAsync(product, edit.Value, ct),
            ProductCell.Active => await EditActiveAsync(product, edit.Value, ct),
            ProductCell.BatchNumber =>
                await EditBatchNumberAsync(product, edit.BatchId, edit.Value, ct),
            ProductCell.Expiry =>
                await EditExpiryAsync(product, edit.BatchId, edit.Value, ct),
            ProductCell.DaysLeft =>
                await EditDaysLeftAsync(product, edit.BatchId, edit.Value, ct),
            ProductCell.OpenBatch =>
                await OpenBatchAsync(product, edit.Value, edit.Extra, ct),
            _ => Result.Fail("INV-030", "That column cannot be edited.")
        };
    }

    private async Task<Result> EditCodeAsync(Product product, string? value,
        CancellationToken ct)
    {
        var code = (value ?? "").Trim().ToUpperInvariant();

        if (code == product.Code) return Result.Ok();

        if (code.Length is < 1 or > 30)
            return Result.Fail("INV-030",
                "A product code is 1 to 30 characters.");

        var other = await _products.FindByCodeOrBarcodeAsync(code, ct: ct);

        if (other is not null && other.Id != product.Id)
            return Result.Fail("INV-002",
                $"Product code {code} is already in use.");

        _permissions.Demand(PermissionCode.ProductMaintain);
        product.ChangeCode(code);

        _audit.Applied("inv.product.maintain", "Product", product.Id,
            $"Code {code}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditDescriptionAsync(Product product,
        string? value, CancellationToken ct)
    {
        var description = (value ?? "").Trim();

        if (description == product.Description) return Result.Ok();

        if (description.Length is < 1 or > 200)
            return Result.Fail("INV-030",
                "A description is required, up to 200 characters.");

        _permissions.Demand(PermissionCode.ProductMaintain);
        product.Rename(description);

        _audit.Applied("inv.product.maintain", "Product", product.Id);
        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditRetailAsync(Product product, string? value,
        CancellationToken ct)
    {
        if (!decimal.TryParse(value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var price))
            return Result.Fail("INV-030", "Enter a retail price.");

        if (price == product.RetailPrice.Amount) return Result.Ok();

        if (price < 0m)
            return Result.Fail("INV-030",
                "A retail price cannot be negative.");

        _permissions.Demand(PermissionCode.ProductMaintain);
        _permissions.Demand(PermissionCode.PriceChange);

        Pricing.ChangeRetail(product, price, _audit, _outbox);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<int>> RepriceAsync(Guid? departmentId,
        decimal percent, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductMaintain);
        _permissions.Demand(PermissionCode.PriceChange);

        if (percent <= -100m)
            return Result<int>.Fail("INV-031", "A price cannot come down by 100% or more.");

        var (_, step) = await Pricing.ReadAsync(_settings, ct);
        var changed = 0;

        // ponytail: every active product in memory; fine for a pharmacy's
        // few thousand lines.
        foreach (var product in await _products.SearchAsync("", int.MaxValue, ct: ct))
        {
            if (departmentId is Guid d && product.DepartmentId != d) continue;
            if (product.RetailPrice.Amount <= 0m) continue;

            var price = Pricing.RoundUp(product.RetailPrice.Amount * (1m + percent / 100m), step);
            if (price == product.RetailPrice.Amount) continue;

            Pricing.ChangeRetail(product, price, _audit, _outbox,
                $" ({percent:+0.##;-0.##}% across the board)");
            changed++;
        }

        await _uow.SaveChangesAsync(ct);
        return Result<int>.Ok(changed);
    }

    private async Task<Result> EditScheduleAsync(Product product,
        string? value, CancellationToken ct)
    {
        if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var schedule))
            return Result.Fail("INV-035",
                "Schedule is a whole number from 0 to 8.");

        if (schedule == product.Schedule) return Result.Ok();

        if (schedule is < 0 or > 8)
            return Result.Fail("INV-035",
                "Schedule must be between 0 and 8.");

        if (schedule >= 3
            && _permissions.Check(PermissionCode.ProductDispensary)
                != PermissionOutcome.Granted)
            return Result.Fail("INV-037",
                "Schedule 3 and above needs the dispensary permission.");

        _permissions.Demand(PermissionCode.ProductMaintain);
        product.SetSchedule(schedule);

        _audit.Applied("inv.product.maintain", "Product", product.Id,
            $"Schedule {schedule}");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditBatchTrackedAsync(Product product,
        string? value, CancellationToken ct)
    {
        if (ParseYesNo(value) is not bool tracked)
            return Result.Fail("INV-036", "Enter Yes or No for batch tracking.");

        if (tracked == product.IsBatchTracked) return Result.Ok();

        if (!tracked && await HoldsStockOnABatchAsync(product.Id, ct))
            return Result.Fail("INV-036",
                "Stock is still on a batch, so batch tracking has to stay on.");

        // Loose stock would sit on no batch and print labels with no expiry.
        // The batch row under the product turns tracking on as it records one.
        if (tracked && product.QuantityOnHand > 0m)
            return Result.Fail(NeedsBatchForStock, NeedsBatchMessage(product));

        _permissions.Demand(PermissionCode.ProductMaintain);
        product.SetBatchTracked(tracked);

        _audit.Applied("inv.product.maintain", "Product", product.Id,
            tracked ? "Batch tracked" : "Not batch tracked");

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditActiveAsync(Product product, string? value,
        CancellationToken ct)
    {
        if (ParseYesNo(value) is not bool active)
            return Result.Fail("INV-030", "Enter Yes or No for active.");

        if (active == product.IsActive) return Result.Ok();

        _permissions.Demand(PermissionCode.ProductMaintain);

        if (active) product.Reactivate();
        else product.Deactivate();

        _audit.Applied(
            active ? "inv.product.reactivate" : "inv.product.deactivate",
            "Product", product.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <param name="reason">Why the stock changed, as the list asked for it.</param>
    private async Task<Result> EditQuantityAsync(Product product,
        Guid? batchId, string? value, string? reason, CancellationToken ct)
    {
        if (!decimal.TryParse(value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var quantity))
            return Result.Fail("INV-031", "Enter a quantity.");

        if (quantity < 0m)
            return Result.Fail("INV-031",
                "Quantity on hand cannot be negative.");

        var batches = await _batches.ForProductAsync(product.Id, ct);
        var held = batches.Where(b => b.QuantityOnHand > 0m).ToList();

        Batch? batch = null;

        if (batchId is Guid id)
        {
            batch = batches.FirstOrDefault(b => b.Id == id);

            if (batch is null)
                return Result.Fail("INV-032",
                    "That batch is no longer on the shelf.");
        }
        else if (product.IsBatchTracked && held.Count > 1)
        {
            return Result.Fail("INV-031",
                "This product is split across batches. Tick Show batches "
                + "and expiry, and change the quantity on each batch.");
        }
        else if (held.Count == 1)
        {
            batch = held[0];
        }

        var delta = batch is not null && batchId is not null
            ? quantity - batch.QuantityOnHand
            : quantity - product.QuantityOnHand;

        if (delta == 0m) return Result.Ok();

        if (batch is not null && batch.QuantityOnHand + delta < 0m)
            return Result.Fail("INV-031",
                "That would take the batch below zero.");

        if (product.QuantityOnHand + delta < 0m)
            return Result.Fail("INV-031",
                "Quantity on hand cannot be negative.");

        var why = (reason ?? "").Trim();

        if (why.Length == 0) return Result.Fail("INV-033", NoReason);
        if (why.Length > ReasonLength) why = why[..ReasonLength];

        _permissions.Demand(PermissionCode.ProductMaintain);
        DemandStockCorrection();

        if (batch is not null) batch.AdjustBy(delta);

        var before = product.QuantityOnHand;
        var balance = before + delta;
        product.SetQuantityOnHand(balance);

        RecordCount(product, batch?.Id, delta, balance, why);

        _audit.Applied("inv.stock.adjust", "Product", product.Id,
            StockAudit(product, before, balance, why));

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditBatchNumberAsync(Product product,
        Guid? batchId, string? value, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(product.Id, batchId, ct);

        if (batch is null)
            return Result.Fail("INV-032",
                "That batch is no longer on the shelf.");

        var number = (value ?? "").Trim();

        if (string.Equals(number, batch.BatchNumber,
                StringComparison.OrdinalIgnoreCase))
            return Result.Ok();

        if (number.Length is < 1 or > 50)
            return Result.Fail("INV-032",
                "A batch number is 1 to 50 characters.");

        var siblings = await _batches.ForProductAsync(product.Id, ct);

        if (siblings.Any(b => b.Id != batch.Id
                && string.Equals(b.BatchNumber, number,
                    StringComparison.OrdinalIgnoreCase)))
            return Result.Fail("INV-032",
                $"Batch {number} is already on this product.");

        _permissions.Demand(PermissionCode.ProductMaintain);
        batch.SetBatchNumber(number);

        _audit.Applied("inv.batch.correct", "Batch", batch.Id, number);
        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Result> EditExpiryAsync(Product product, Guid? batchId,
        string? value, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var expiry))
            return Result.Fail("INV-032", "Enter the expiry as a date.");

        return await SetExpiryAsync(product, batchId, expiry, ct);
    }

    private async Task<Result> EditDaysLeftAsync(Product product,
        Guid? batchId, string? value, CancellationToken ct)
    {
        if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var days))
            return Result.Fail("INV-032", "Days left is a whole number.");

        DateOnly expiry;

        try
        {
            expiry = _clock.Today.AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result.Fail("INV-032",
                "That many days is not a real date.");
        }

        return await SetExpiryAsync(product, batchId, expiry, ct);
    }

    private async Task<Result> SetExpiryAsync(Product product, Guid? batchId,
        DateOnly expiry, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(product.Id, batchId, ct);

        if (batch is null)
            return Result.Fail("INV-032",
                "That batch is no longer on the shelf.");

        if (expiry == batch.ExpiryDate) return Result.Ok();

        _permissions.Demand(PermissionCode.ProductMaintain);
        batch.SetExpiry(expiry);

        _audit.Applied("inv.batch.correct", "Batch", batch.Id,
            expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Gives stock that was only a product balance a batch number and
    /// an expiry. The quantity does not change: the stock was already
    /// on the shelf, it just had no pack identity.
    /// </summary>
    private async Task<Result> OpenBatchAsync(Product product, string? number,
        string? expiryText, CancellationToken ct)
    {
        var batchNumber = (number ?? "").Trim();

        if (batchNumber.Length is < 1 or > 50)
            return Result.Fail("INV-032",
                "A batch number is 1 to 50 characters.");

        if (!DateOnly.TryParseExact(expiryText, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var expiry))
            return Result.Fail("INV-032",
                "Enter the expiry date for this batch.");

        if (product.QuantityOnHand <= 0m)
            return Result.Fail("INV-032",
                "There is no stock on hand to put onto a batch.");

        var existing = await _batches.ForProductAsync(product.Id, ct);

        if (existing.Any(b => b.QuantityOnHand > 0m))
            return Result.Fail("INV-032",
                "This product already has a batch. Edit that row.");

        if (existing.Any(b => string.Equals(b.BatchNumber, batchNumber,
                StringComparison.OrdinalIgnoreCase)))
            return Result.Fail("INV-032",
                $"Batch {batchNumber} is already on this product.");

        _permissions.Demand(PermissionCode.ProductMaintain);

        var batch = Batch.Receive(product.Id, batchNumber, expiry,
            product.QuantityOnHand, product.CostPrice, _clock.UtcNow);

        product.SetBatchTracked(true);
        await _batches.AddAsync(batch, ct);

        _audit.Applied("inv.batch.record", "Batch", batch.Id, batchNumber);
        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private async Task<Batch?> RequireBatchAsync(Guid productId, Guid? batchId,
        CancellationToken ct)
    {
        if (batchId is not Guid id) return null;

        var batch = await _batches.GetAsync(id, ct);

        return batch is null || batch.ProductId != productId ? null : batch;
    }

    private async Task<bool> HoldsStockOnABatchAsync(Guid productId,
        CancellationToken ct)
    {
        var batches = await _batches.ForProductAsync(productId, ct);
        return batches.Any(b => b.QuantityOnHand > 0m);
    }

    private static bool? ParseYesNo(string? value)
    {
        var text = (value ?? "").Trim();

        if (text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("y", StringComparison.OrdinalIgnoreCase)
            || text == "1")
            return true;

        if (text.Equals("no", StringComparison.OrdinalIgnoreCase)
            || text.Equals("false", StringComparison.OrdinalIgnoreCase)
            || text.Equals("n", StringComparison.OrdinalIgnoreCase)
            || text == "0")
            return false;

        return null;
    }
}

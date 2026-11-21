using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

public interface IProductService
{
    /// <param name="skip">Matches passed over first, for a later page.</param>
    /// <param name="includeInactive">False hides deactivated products.</param>
    Task<Result<ProductPageDto>> SearchAsync(string term, int take = 200,
                                             int skip = 0,
                                             bool includeInactive = true,
                                             ListFilter? filter = null,
                                             CancellationToken ct = default);

    /// <summary>
    /// The same search, expanded a batch at a time: what is on the shelf,
    /// which batch it came in on, how much is left and when it expires.
    /// </summary>
    Task<Result<ProductBatchPageDto>> SearchBatchesAsync(string term,
        int take = 200, int skip = 0, ListFilter? filter = null,
        CancellationToken ct = default);

    Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken ct = default);

    /// The product a code or barcode belongs to, as the till would scan
    /// it; null where none does.
    Task<Result<ProductListItemDto?>> FindAsync(string codeOrBarcode,
                                                CancellationToken ct = default);

    /// The next number for a new product: one past the highest all-digit
    /// code, the way the catalogue has been numbered.
    Task<Result<string>> NextCodeAsync(CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(ProductEditDto edit,
                                   CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, ProductEditDto edit,
                             CancellationToken ct = default);

    Task<Result> DeactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Commits one cell of the product list. Catalogue columns need
    /// inv.product.maintain, the selling price also needs
    /// inv.price.change, and the quantity on hand also needs
    /// inv.stock.correct (or inv.stocktake.post) and a reason. Schedule 3
    /// and above needs inv.product.dispensary.
    /// </summary>
    Task<Result> ApplyCellEditAsync(ProductCellEditDto edit,
                                     CancellationToken ct = default);

    /// Every price in a department - or all of them - up or down by a
    /// percentage, rounded as System setup says. How many changed.
    Task<Result<int>> RepriceAsync(Guid? departmentId, decimal percent,
                                   CancellationToken ct = default);
}

public sealed partial class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IStockMovementWriter _movements;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IPharmacySettings _settings;

    public ProductService(IProductRepository products,
        IBatchRepository batches, IStockMovementWriter movements,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow, IClock clock, IPharmacySettings settings)
    {
        _settings = settings;
        _products = products;
        _batches = batches;
        _movements = movements;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<ProductPageDto>> SearchAsync(string term,
        int take = 200, int skip = 0, bool includeInactive = true,
        ListFilter? filter = null, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        // A cashier's catalogue is the over-the-counter one, filtered in
        // the query rather than on the screen.
        var scope = _permissions.ProductScope();

        var products = await _products.SearchAsync(term, take, scope,
                                                   skip, includeInactive, filter, ct);
        var total = await _products.CountAsync(term, scope, includeInactive, filter, ct);

        IReadOnlyList<ProductListItemDto> items =
            products.Select(ProductListItemDto.From).ToList();

        return Result<ProductPageDto>.Ok(new ProductPageDto(items, total));
    }

    public async Task<Result<ProductBatchPageDto>> SearchBatchesAsync(
        string term, int take = 200, int skip = 0, ListFilter? filter = null,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var scope = _permissions.ProductScope();

        var products = await _products.SearchAsync(term, take, scope,
                                                   skip, filter: filter, ct: ct);
        var total = await _products.CountAsync(term, scope, filter: filter, ct: ct);

        var batches = await _batches.ForProductsAsync(
            products.Select(p => p.Id).ToList(), ct);

        var byProduct = batches.ToLookup(b => b.ProductId);
        var today = _clock.Today;
        var rows = new List<ProductBatchListItemDto>();

        foreach (var product in products)
        {
            // Only what is still on the shelf. A batch counted down to
            // nothing is history, and the movement report is where
            // history is read.
            var held = byProduct[product.Id]
                .Where(b => b.QuantityOnHand > 0m)
                .OrderBy(b => b.ExpiryDate)
                .ToList();

            if (held.Count == 0)
            {
                // Not batch tracked, or tracked and nothing left. Either
                // way the product keeps its place in the list rather than
                // disappearing because a box was ticked.
                rows.Add(new ProductBatchListItemDto(
                    product.Id, product.Code, product.Description,
                    BatchNumber: null, Expiry: null, DaysToExpiry: null,
                    product.QuantityOnHand, product.IsBatchTracked,
                    product.IsActive, BatchId: null));

                continue;
            }

            foreach (var batch in held)
            {
                rows.Add(new ProductBatchListItemDto(
                    product.Id, product.Code, product.Description,
                    batch.BatchNumber, batch.ExpiryDate,
                    batch.ExpiryDate.DayNumber - today.DayNumber,
                    batch.QuantityOnHand, product.IsBatchTracked,
                    product.IsActive, batch.Id));
            }
        }

        return Result<ProductBatchPageDto>.Ok(
            new ProductBatchPageDto(rows, total));
    }

    public async Task<Result<ProductDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var product = await _products.GetAsync(id, ct);

        // Out of scope reads exactly like absent: saying "you may not see
        // that one" confirms it exists.
        if (product is null || !InScope(product))
            return Result<ProductDto>.Fail("INV-000", "Product not found.");

        // The batches still on the shelf, so the editor can show and
        // correct them alongside the rest of the product.
        var batches = await _batches.ForProductAsync(product.Id, ct);
        var today = _clock.Today;

        IReadOnlyList<ProductBatchDto> held = batches
            .Where(b => b.QuantityOnHand > 0m)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber, StringComparer.OrdinalIgnoreCase)
            .Select(b => new ProductBatchDto(b.Id, b.BatchNumber,
                b.ExpiryDate, b.ExpiryDate.DayNumber - today.DayNumber,
                b.QuantityOnHand))
            .ToList();

        return Result<ProductDto>.Ok(
            ProductDto.From(product) with { Batches = held });
    }

    public async Task<Result<ProductListItemDto?>> FindAsync(
        string codeOrBarcode, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var product = await _products.FindByCodeOrBarcodeAsync(codeOrBarcode,
            _permissions.ProductScope(), ct);

        return Result<ProductListItemDto?>.Ok(
            product is null ? null : ProductListItemDto.From(product));
    }

    public async Task<Result<string>> NextCodeAsync(CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductMaintain);

        var codes = await _products.CodesAsync(ct);

        var highest = codes
            .Where(c => c.Length is > 0 and < 10 && c.All(char.IsAsciiDigit))
            .Select(c => int.Parse(c, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();

        return Result<string>.Ok((highest + 1).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    private bool InScope(Product product) =>
        !product.RequiresPharmacist
        || _permissions.ProductScope() == ProductScope.All;

    public async Task<Result<Guid>> CreateAsync(ProductEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit = await WithMarkupAsync(edit, ct);

        // Adding a product sets its first price, which is not a price
        // change: there was no price before it. Maintaining the catalogue
        // is its own permission, so a stock controller can add what a
        // delivery brought in without being handed the shelf prices.
        _permissions.Demand(PermissionCode.ProductMaintain);

        if (edit.Schedule >= 3)
            _permissions.Demand(PermissionCode.ProductDispensary);

        var existing = await _products.FindByCodeOrBarcodeAsync(edit.Code,
            ct: ct);
        if (existing is not null)
            return Result<Guid>.Fail("INV-002",
                $"Product code {edit.Code} is already in use.");

        // Opening stock, where the form was given some. It is a count onto
        // a product that had none, so it needs the permission any other
        // count from the product does, and says it was the opening stock
        // unless the operator said otherwise.
        var stock = PlanStock(0m, Array.Empty<Batch>(), edit with
        {
            StockReason = string.IsNullOrWhiteSpace(edit.StockReason)
                ? "Opening stock"
                : edit.StockReason
        });

        if (stock.IsFailure)
            return Result<Guid>.Fail(stock.Error.Code, stock.Error.Message);

        if (stock.Value.MovesStock) DemandStockCorrection();

        var product = Product.Create(
            code: edit.Code,
            description: edit.Description,
            retailPrice: Money.Of(edit.RetailPrice),
            vatRatePercent: edit.VatRatePercent,
            taxCode: edit.TaxCode,
            schedule: edit.Schedule,
            isBatchTracked: edit.IsBatchTracked);

        product.ChangeCostPrice(Money.Of(edit.CostPrice));
        product.SetReorderLevel(edit.ReorderLevel);
        product.SetGenericName(edit.GenericName);
        product.SetPackSize(edit.PackSize);
        product.SetUnitsPerPack(edit.UnitsPerPack is > 0m
            ? edit.UnitsPerPack
            : null);

        product.Classify(edit.DepartmentId, edit.CategoryId,
                         edit.ManufacturerId);

        if (await ApplyBarcodesAsync(product, edit.Barcodes, ct)
                is Error barcode)
            return Result<Guid>.Fail(barcode.Code, barcode.Message);

        await _products.AddAsync(product, ct);

        _audit.Applied("inv.product.create", "Product", product.Id);

        if (edit.IsActive == false)
        {
            product.Deactivate();
            _audit.Applied("inv.product.deactivate", "Product", product.Id);
        }

        await ApplyStockAsync(product, stock.Value, ct);

        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(product.Id);
    }

    /// An item saved with a cost and no price takes the default markup.
    private async Task<ProductEditDto> WithMarkupAsync(ProductEditDto edit,
        CancellationToken ct)
    {
        if (edit.RetailPrice > 0m || edit.CostPrice <= 0m) return edit;

        var (markup, step) = await Pricing.ReadAsync(_settings, ct);

        return Pricing.NewRetail(0m, edit.CostPrice, markup, step) is decimal retail
            ? edit with { RetailPrice = retail }
            : edit;
    }

    public async Task<Result> UpdateAsync(Guid id, ProductEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit = await WithMarkupAsync(edit, ct);

        var product = await _products.GetAsync(id, ct);
        if (product is null || !InScope(product))
            return Result.Fail("INV-000", "Product not found.");

        // Editing the catalogue at all needs the maintenance permission.
        // Nothing was demanded here before, so anyone who could read a
        // product - a cashier - could rename it or change its cost.
        _permissions.Demand(PermissionCode.ProductMaintain);

        // The code the shelf and the till know it by. A sale keeps the
        // product's id, so a new code does not rewrite printed receipts,
        // but it still has to be one no other product answers to.
        var code = (edit.Code ?? "").Trim().ToUpperInvariant();

        if (code != product.Code)
        {
            if (code.Length is < 1 or > 30)
                return Result.Fail("INV-030",
                    "A product code is 1 to 30 characters.");

            var other = await _products.FindByCodeOrBarcodeAsync(code,
                ct: ct);

            if (other is not null && other.Id != product.Id)
                return Result.Fail("INV-002",
                    $"Product code {code} is already in use.");
        }

        if (edit.Schedule is < 0 or > 8)
            return Result.Fail("INV-035",
                "Schedule must be between 0 and 8.");

        if (edit.Schedule != product.Schedule && edit.Schedule >= 3
            && _permissions.Check(PermissionCode.ProductDispensary)
                != PermissionOutcome.Granted)
            return Result.Fail("INV-037",
                "Schedule 3 and above needs the dispensary permission.");

        // The stock is checked in full before anything is changed, so a
        // refused batch row does not leave the rest of the edit applied.
        var batches = await _batches.ForProductAsync(product.Id, ct);
        var stock = PlanStock(product.QuantityOnHand, batches, edit);

        if (stock.IsFailure)
            return Result.Fail(stock.Error.Code, stock.Error.Message);

        // Judged on the shelf as it will be once this edit is applied, so
        // counting the last batch down to nothing and switching tracking
        // off can be done in one save.
        if (!edit.IsBatchTracked && product.IsBatchTracked
            && stock.Value.LeavesStockOnABatch)
            return Result.Fail("INV-036",
                "Stock is still on a batch, so batch tracking has to stay on.");

        if (edit.IsBatchTracked && !product.IsBatchTracked
            && product.QuantityOnHand > 0m && !stock.Value.LeavesStockOnABatch)
            return Result.Fail(NeedsBatchForStock, NeedsBatchMessage(product));

        if (stock.Value.MovesStock) DemandStockCorrection();

        // Only a price change needs the price permission on top.
        if (product.RetailPrice.Amount != edit.RetailPrice)
        {
            _permissions.Demand(PermissionCode.PriceChange);

            _audit.Applied("inv.price.change", "Product", product.Id,
                $"{product.RetailPrice} to {Money.Of(edit.RetailPrice)}");

            product.ChangeRetailPrice(Money.Of(edit.RetailPrice));
            _outbox.Append("ProductPrice", product.Id,
                $"{{\"productId\":\"{product.Id}\",\"retail\":{edit.RetailPrice}}}");
        }

        if (code != product.Code)
        {
            product.ChangeCode(code);
            _audit.Applied("inv.product.maintain", "Product", product.Id,
                $"Code {code}");
        }

        product.Rename(edit.Description);
        product.SetSchedule(edit.Schedule);
        product.SetBatchTracked(edit.IsBatchTracked);
        product.ChangeCostPrice(Money.Of(edit.CostPrice));
        product.SetReorderLevel(edit.ReorderLevel);
        product.SetGenericName(edit.GenericName);
        product.SetPackSize(edit.PackSize);
        product.SetUnitsPerPack(edit.UnitsPerPack is > 0m
            ? edit.UnitsPerPack
            : null);

        product.Classify(edit.DepartmentId, edit.CategoryId,
                         edit.ManufacturerId);

        if (await ApplyBarcodesAsync(product, edit.Barcodes, ct)
                is Error barcode)
            return Result.Fail(barcode.Code, barcode.Message);

        // Discontinued, never deleted, as DeactivateAsync does it.
        if (edit.IsActive is bool active && active != product.IsActive)
        {
            if (active) product.Reactivate();
            else product.Deactivate();

            _audit.Applied(
                active ? "inv.product.reactivate" : "inv.product.deactivate",
                "Product", product.Id);
        }

        // Last, after everything that can still refuse the edit.
        await ApplyStockAsync(product, stock.Value, ct);

        _audit.Applied("inv.product.maintain", "Product", product.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Sets the barcodes the form sent, where it sent any.
    /// </summary>
    /// <remarks>
    /// No screen could add a barcode, so a product created in PharmaX
    /// could not be scanned at the till or the dispensary however it was
    /// packed (ADM-DEF-04). A barcode another product already answers to
    /// is refused, because the scanner would then ring up whichever one
    /// the query found first.
    /// </remarks>
    private async Task<Error?> ApplyBarcodesAsync(Product product,
        IReadOnlyList<string>? barcodes, CancellationToken ct)
    {
        if (barcodes is null) return null;

        foreach (var barcode in barcodes
                     .Where(b => !string.IsNullOrWhiteSpace(b))
                     .Select(b => b.Trim()))
        {
            var other = await _products.FindByCodeOrBarcodeAsync(barcode,
                ct: ct);

            if (other is not null && other.Id != product.Id)
                return new Error("INV-010",
                    $"Barcode {barcode} already belongs to {other.Code} "
                    + $"{other.Description}.");
        }

        try
        {
            product.ReplaceBarcodes(barcodes);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return new Error("INV-011", ex.Message);
        }

        return null;
    }

    public async Task<Result> DeactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductDeactivate);

        var product = await _products.GetAsync(id, ct);
        if (product is null) return Result.Fail("INV-000", "Product not found.");

        // Discontinued, never deleted: a deleted product takes its sales
        // history off every report that has already been printed.
        product.Deactivate();

        _audit.Applied("inv.product.deactivate", "Product", product.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}

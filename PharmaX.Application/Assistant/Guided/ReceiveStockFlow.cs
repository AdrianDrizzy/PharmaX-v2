using System.Globalization;
using System.Text;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Receive stock from Pharmanova": a delivery booked in line by line,
/// against an open order or not, checked against the invoice, then posted
/// through the receiving service.
/// </summary>
public sealed class ReceiveStockFlow : GuidedFlow
{
    private readonly IReceivingService _receiving;
    private readonly IPurchasingService _purchasing;
    private readonly ISupplierService _suppliers;
    private readonly IProductService _products;
    private readonly IPharmacySettings _settings;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;

    /// One line of the delivery as captured.
    /// <param name="OrderLineNo">The order line it answers, if any.</param>
    /// <param name="Free">Bonus stock, at no cost.</param>
    public sealed record Line(Guid ProductId, string Code, string Description, decimal Quantity, decimal Cost,
                              string? Batch, DateOnly? Expiry, int? OrderLineNo, bool Free = false)
    {
        public decimal Total => Quantity * Cost;
    }

    private string? _supplierSaid;
    private SupplierListItemDto? _supplier;
    private IReadOnlyList<SupplierListItemDto> _supplierMatches = Array.Empty<SupplierListItemDto>();
    private IReadOnlyList<PurchaseOrderDto> _orders = Array.Empty<PurchaseOrderDto>();
    private PurchaseOrderDto? _order;
    private readonly Queue<PurchaseOrderLineDto> _toReceive = new();
    private string? _invoiceNumber;
    private DateOnly _invoiceDate;
    private decimal? _invoiceTotal;
    private string? _differenceReason;
    private readonly List<Line> _lines = new();
    private readonly Dictionary<Guid, decimal> _newPrices = new();

    // The line being captured.
    private ProductDto? _product;
    private PurchaseOrderLineDto? _orderLine;
    private decimal _quantity;
    private decimal _extra;
    private decimal _cost;
    private decimal? _lastCost;
    private string? _batch;
    private DateOnly? _expiry;
    private IReadOnlyList<ProductListItemDto> _productMatches = Array.Empty<ProductListItemDto>();
    private (decimal Markup, decimal Rounding)? _pricing;

    /// An expiry closer than this is said out loud.
    public const int ShortExpiryDays = 183;

    /// A cost further than this from the last one is questioned.
    public const decimal CostTolerance = 0.10m;

    public ReceiveStockFlow(string? supplier, IReceivingService receiving, IPurchasingService purchasing,
                            ISupplierService suppliers, IProductService products, IPharmacySettings settings,
                            IPermissionService permissions, IClock clock)
    {
        _receiving = receiving;
        _purchasing = purchasing;
        _suppliers = suppliers;
        _products = products;
        _settings = settings;
        _permissions = permissions;
        _clock = clock;
        _supplierSaid = string.IsNullOrWhiteSpace(supplier) ? null : supplier.Trim();
        _invoiceDate = clock.Today;

        Add("supplier", SupplierPromptAsync, Supplier);
        Add("order", OrderPromptAsync, Order);
        Add("invoice-number", () => Join(_noOrders, "Invoice number?"), InvoiceNumber);
        Add("invoice-date", () => $"Invoice date? Press Enter for today, {Answers.Show(_clock.Today)}.", InvoiceDate);
        Add("invoice-total", () => "Invoice total, including VAT? (used to check the capture at the end)", InvoiceTotal);
        Add("product", ProductPromptAsync, ProductAsync);
        Add("quantity", QuantityPrompt, Quantity);
        Add("cost", CostPrompt, CostAsync);
        Add("batch", () => "Batch number?", BatchNumber, () => _product?.IsBatchTracked == true);
        Add("expiry", () => "Expiry date? (dd/mm/yyyy, or month and year as printed)", Expiry,
            () => _product?.IsBatchTracked == true);
        Add("bonus", () => "Bonus or free stock on this line? (how many, or Enter for none)", Bonus);
        Add("price", PricePromptAsync, PriceAsync, () => Squeezed && MayChangePrice);
        Add("difference", DifferencePrompt, Difference, () => Difference() != 0m);
        AddConfirm(_ => Task.FromResult(Summary()), "Post the receipt and update stock?", PostAsync,
                   changeable: false);
    }

    protected override string NothingDone => "Nothing was received.";

    public IReadOnlyList<Line> Lines => _lines;

    public decimal Captured => _lines.Sum(l => l.Total);

    private decimal Difference() => _invoiceTotal is decimal invoiced ? invoiced - Captured : 0m;

    protected override Task<string> IntroAsync(CancellationToken ct) =>
        Task.FromResult("Booking in a delivery. Say \"cancel\" at any time; nothing is posted until you say so.");

    // ---- Supplier ---------------------------------------------------------

    private async Task<string> SupplierPromptAsync(CancellationToken ct)
    {
        var list = await _suppliers.ListAsync(includeInactive: false, ct);
        var suppliers = list.IsSuccess ? list.Value : Array.Empty<SupplierListItemDto>();

        if (_supplierSaid is null) return "Which supplier?";

        _supplierMatches = Matching(_supplierSaid, suppliers);
        _supplierSaid = null;

        if (_supplierMatches.Count == 1)
        {
            _supplier = _supplierMatches[0];
            return "";
        }

        return _supplierMatches.Count == 0
            ? "No supplier matches that name. Which supplier?"
            : "Which one?" + Environment.NewLine + Answers.Numbered(_supplierMatches, s => $"{s.Name} ({s.Code})");
    }

    private static List<SupplierListItemDto> Matching(string said, IReadOnlyList<SupplierListItemDto> all)
    {
        var exact = all.Where(s => string.Equals(s.Name.Trim(), said, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) return exact;

        return all.Where(s => Answers.Pick(said, new[] { s }, x => x.Name) is not null).Take(12).ToList();
    }

    private Reply Supplier(string answer)
    {
        if (_supplierMatches.Count > 0 && Answers.Pick(answer, _supplierMatches, s => s.Name) is { } picked)
        {
            _supplier = picked;
            return Reply.Ok();
        }

        _supplierSaid = answer.Trim();
        return new Reply.Jump("supplier");
    }

    // ---- Against an order -------------------------------------------------

    private async Task<string> OrderPromptAsync(CancellationToken ct)
    {
        var open = await _purchasing.OutstandingAsync(_supplier!.Id, ct);
        _orders = open.IsSuccess ? open.Value.Where(o => o.Status != "Draft").ToList() : Array.Empty<PurchaseOrderDto>();

        var intro = $"Goods receipt from {_supplier.Name}.";
        if (_orders.Count == 0)
        {
            // Nothing to choose: said with the next question instead.
            _noOrders = Join(intro, "There are no open orders for them, so each line is typed in.");
            return "";
        }

        return Join(intro, "Against an open order? Choose one, or say no." + Environment.NewLine
            + Answers.Numbered(_orders, o =>
                $"{o.OrderNumber}, raised {o.OrderedOn.ToString("dd/MM", CultureInfo.InvariantCulture)}, "
                + $"{o.Lines.Count(l => l.Outstanding > 0m)} line(s) outstanding"));
    }

    /// No orders: said with the invoice question, as this one is passed over.
    private string _noOrders = "";

    private Reply Order(string answer)
    {
        if (Answers.IsSkip(answer) || Answers.IsNo(answer)) return Reply.Ok("No order: type each line in.");

        var order = Answers.Pick(answer, _orders, o => o.OrderNumber);
        if (order is null) return Reply.Retry("Choose an order by its number in the list, or say no.");

        _order = order;
        _toReceive.Clear();
        foreach (var line in order.Lines.Where(l => l.Outstanding > 0m)) _toReceive.Enqueue(line);

        return Reply.Ok($"{order.OrderNumber}: {_toReceive.Count} line(s) to receive. Each one is asked in turn.");
    }

    // ---- The invoice ------------------------------------------------------

    private Reply InvoiceNumber(string answer)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("The supplier's invoice number, as printed on it.");

        _invoiceNumber = answer.Trim().ToUpperInvariant();
        _noOrders = "";
        return Reply.Ok();
    }

    private Reply InvoiceDate(string answer)
    {
        if (answer.Length == 0)
        {
            _invoiceDate = _clock.Today;
            return Reply.Ok();
        }

        if (Answers.Date(answer) is not DateOnly date) return Reply.Retry("Say the date as dd/mm/yyyy.");
        if (date > _clock.Today) return Reply.Retry("Stock can't be received on a date still to come.");

        _invoiceDate = date;
        return Reply.Ok();
    }

    private Reply InvoiceTotal(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _invoiceTotal = null;
            return Reply.Ok("No invoice total: the capture can't be checked against it.");
        }

        if (Answers.Money(answer) is not decimal total) return Reply.Retry("The invoice total, e.g. 1,250.00.");

        _invoiceTotal = total;
        return Reply.Ok();
    }

    // ---- Each line --------------------------------------------------------

    private void StartLine()
    {
        _product = null;
        _orderLine = null;
        _quantity = 0m;
        _extra = 0m;
        _cost = 0m;
        _batch = null;
        _expiry = null;
    }

    private async Task<string> ProductPromptAsync(CancellationToken ct)
    {
        StartLine();

        // The order's lines first, one at a time.
        if (_toReceive.Count > 0)
        {
            _orderLine = _toReceive.Dequeue();
            var detail = await _products.GetAsync(_orderLine.ProductId, ct);

            if (detail.IsSuccess)
            {
                _product = detail.Value;
                await ReadLastCostAsync(ct);
                return "";
            }
        }

        if (_productMatches.Count > 1)
            return "Which one?" + Environment.NewLine + Answers.Numbered(_productMatches,
                p => $"{p.Description} ({p.Code}){(string.IsNullOrWhiteSpace(p.PackSize) ? "" : " " + p.PackSize)}");

        return _lines.Count == 0 ? "Product? Its name, code or barcode." : "Next line? Name the product, or say done.";
    }

    private async Task ReadLastCostAsync(CancellationToken ct)
    {
        var buying = await _purchasing.BuyingAsync(_product!.Id, ct);
        _lastCost = buying.IsSuccess && buying.Value.LastCost is decimal last ? last : _product.CostPrice;
    }

    private async Task<Reply> ProductAsync(string answer, CancellationToken ct)
    {
        var words = Answers.Words(answer);
        if (_lines.Count > 0 && (words.Contains("done") || Answers.IsSkip(answer) || Answers.IsNo(answer)))
        {
            _productMatches = Array.Empty<ProductListItemDto>();
            return new Reply.Jump("difference");
        }

        if (Answers.IsSkip(answer)) return Reply.Retry("Which product?");

        if (Answers.IsOffList(answer, _productMatches.Count))
            return Reply.Retry($"Choose 1 to {_productMatches.Count}.");

        ProductListItemDto? chosen = _productMatches.Count > 1
            ? Answers.Pick(answer, _productMatches, p => p.Description)
            : null;

        if (chosen is null)
        {
            var scanned = await _products.FindAsync(answer.Trim(), ct);
            chosen = scanned.IsSuccess ? scanned.Value : null;
        }

        if (chosen is null)
        {
            var found = await _products.SearchAsync(answer.Trim(), take: 8, includeInactive: false, ct: ct);
            _productMatches = found.IsSuccess ? found.Value.Items : Array.Empty<ProductListItemDto>();

            if (_productMatches.Count == 0) return Reply.Retry($"Nothing matches \"{answer.Trim()}\".");
            if (_productMatches.Count > 1) return new Reply.Jump("product");

            chosen = _productMatches[0];
        }

        _productMatches = Array.Empty<ProductListItemDto>();

        var detail = await _products.GetAsync(chosen.Id, ct);
        if (detail.IsFailure) return Reply.Retry(detail.Error.Message);

        _product = detail.Value;
        await ReadLastCostAsync(ct);
        return Reply.Ok();
    }

    private string QuantityPrompt()
    {
        var name = $"{_product!.Description} ({_product.Code})";

        return _orderLine is { } line
            ? $"{name}: {Answers.Quantity(line.QuantityOrdered)} ordered, {Answers.Quantity(line.Outstanding)} still "
              + $"to come. Quantity received? Press Enter for {Answers.Quantity(line.Outstanding)}."
            : $"{name}: quantity received?";
    }

    private Reply Quantity(string answer)
    {
        decimal? quantity = answer.Length == 0 && _orderLine is not null ? _orderLine.Outstanding : Answers.Number(answer);
        if (quantity is not > 0m) return Reply.Retry("How many packs came, more than nought?");

        if (_orderLine is { } line && quantity > line.Outstanding)
        {
            var over = quantity.Value - line.Outstanding;
            return Reply.YesNo(
                $"That is {Answers.Quantity(over)} more than ordered ({Answers.Quantity(line.Outstanding)} still to "
                + "come). Receive the extra too, on a line of its own?",
                _ =>
                {
                    _quantity = line.Outstanding;
                    _extra = over;
                    return Task.FromResult(Reply.Ok());
                },
                "Then how many came?");
        }

        _quantity = quantity.Value;
        _extra = 0m;
        return Reply.Ok();
    }

    private decimal DefaultCost() => _orderLine?.UnitCost ?? _lastCost ?? _product!.CostPrice;

    private string CostPrompt() => $"Cost price per pack? Press Enter for {Answers.Show(DefaultCost())}.";

    private async Task<Reply> CostAsync(string answer, CancellationToken ct)
    {
        decimal? cost = answer.Length == 0 ? DefaultCost() : Answers.Money(answer);
        if (cost is null) return Reply.Retry("What does one pack cost, e.g. 4.50?");

        var note = await SqueezeNoteAsync(cost.Value, ct);

        if (_lastCost is decimal last && last > 0m && Math.Abs(cost.Value - last) / last > CostTolerance)
        {
            var change = (cost.Value - last) / last;
            return Reply.YesNo(
                $"{Answers.Show(cost.Value)} is {Math.Abs(change).ToString("0%", CultureInfo.InvariantCulture)} "
                + $"{(change > 0 ? "more" : "less")} than the last cost of {Answers.Show(last)}. Is that right?",
                _ =>
                {
                    _cost = cost.Value;
                    return Task.FromResult(Reply.Ok(note));
                },
                "Then what does one pack cost?");
        }

        _cost = cost.Value;
        return Reply.Ok(note);
    }

    /// The new cost puts the cost up on a product with a price.
    private bool Squeezed => _product is { RetailPrice: > 0m, CostPrice: > 0m } p && _cost > p.CostPrice;

    private bool MayChangePrice => _permissions.Has(PermissionCode.PriceChange);

    private async Task<decimal?> AutomaticPriceAsync(CancellationToken ct)
    {
        _pricing ??= await Pricing.ReadAsync(_settings, ct);
        return Pricing.NewRetail(_product!.RetailPrice, _product.CostPrice, _cost, _pricing.Value.Markup,
                                 _pricing.Value.Rounding);
    }

    /// Said where the price will move by itself and the operator may not change it.
    private async Task<string?> SqueezeNoteAsync(decimal cost, CancellationToken ct)
    {
        if (_product is not { RetailPrice: > 0m } p || cost <= p.CostPrice || MayChangePrice) return null;

        var keep = _cost;
        _cost = cost;
        var automatic = await AutomaticPriceAsync(ct);
        _cost = keep;

        return automatic is decimal price
            ? $"The cost is up, so the selling price goes up to {Answers.Show(price)} when this is posted."
            : $"The cost is up and the selling price stays {Answers.Show(p.RetailPrice)}.";
    }

    private Reply BatchNumber(string answer)
    {
        if (Answers.IsSkip(answer) || answer.Trim().Length > 50)
            return Reply.Retry("Enter the batch number printed on the pack.");

        _batch = answer.Trim().ToUpperInvariant();
        return Reply.Ok();
    }

    private Reply Expiry(string answer) =>
        ExpiryCheck.Read(answer, _clock.Today, ShortExpiryDays, date => _expiry = date);

    private Reply Bonus(string answer)
    {
        decimal free;

        if (Answers.IsSkip(answer)) free = 0m;
        else if (Answers.Number(answer) is decimal n && n >= 0m) free = n;
        else return Reply.Retry("How many came free, or Enter for none?");

        StoreLine(free);
        return Squeezed && MayChangePrice ? Reply.Ok() : NextLine();
    }

    private void StoreLine(decimal free)
    {
        var p = _product!;

        _lines.Add(new Line(p.Id, p.Code, p.Description, _quantity, _cost, _batch, _expiry, _orderLine?.LineNo));

        if (_extra > 0m)
            _lines.Add(new Line(p.Id, p.Code, p.Description, _extra, _cost, _batch, _expiry, null));

        if (free > 0m)
            _lines.Add(new Line(p.Id, p.Code, p.Description, free, 0m, _batch, _expiry, null, Free: true));
    }

    private Reply.Jump NextLine() =>
        new Reply.Jump("product", $"Line taken: {Answers.Quantity(_quantity + _extra)} x {_product!.Description} "
                                  + $"at {Answers.Show(_cost)}. Captured so far: {Answers.Show(Captured)}.");

    private async Task<string> PricePromptAsync(CancellationToken ct)
    {
        var p = _product!;
        var automatic = await AutomaticPriceAsync(ct);

        var said = $"The cost goes up from {Answers.Show(p.CostPrice)} to {Answers.Show(_cost)}, so the margin on "
                   + $"{Answers.Show(p.RetailPrice)} falls from {NewProductFlow.Margin(p.CostPrice, p.RetailPrice)} "
                   + $"to {NewProductFlow.Margin(_cost, p.RetailPrice)}.";

        return automatic is decimal price
            ? $"{said} Change the selling price? Press Enter for {Answers.Show(price)}, which keeps the margin, "
              + "or say another price."
            : $"{said} Change the selling price? Say a new price, or press Enter to keep it.";
    }

    private async Task<Reply> PriceAsync(string answer, CancellationToken ct)
    {
        var automatic = await AutomaticPriceAsync(ct);

        if (answer.Length == 0 || Answers.IsNo(answer) || Answers.IsYes(answer))
        {
            // The receipt puts the price up by itself where the rule says.
            _newPrices.Remove(_product!.Id);
            return NextLine();
        }

        if (Answers.Money(answer) is not decimal price || price <= 0m)
            return Reply.Retry("Say the new selling price, e.g. 6.50, or press Enter.");

        if (price < _cost)
            return Reply.Retry($"{Answers.Show(price)} is below the cost of {Answers.Show(_cost)}.");

        _newPrices[_product!.Id] = price;
        return NextLine();
    }

    // ---- The totals -------------------------------------------------------

    private string DifferencePrompt() =>
        $"Captured {Answers.Show(Captured)} against the invoice's {Answers.Show(_invoiceTotal ?? 0m)}: a difference of "
        + $"{Answers.Show(Difference())}. Why? e.g. credit note to follow, short delivered.";

    private Reply Difference(string answer)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("The totals differ, so a reason is needed before posting.");

        _differenceReason = answer.Trim();
        return Reply.Ok();
    }

    public string Summary()
    {
        var text = new StringBuilder(
            $"Goods receipt from {_supplier?.Name}, invoice {_invoiceNumber} of {Answers.Show(_invoiceDate)}"
            + (_order is null ? ":" : $", against {_order.OrderNumber}:"));

        foreach (var (line, i) in _lines.Select((l, i) => (l, i)))
            text.Append(
                $"{Environment.NewLine}{i + 1}. {line.Description} ({line.Code}) {Answers.Quantity(line.Quantity)} x "
                + $"{Answers.Show(line.Cost)} = {Answers.Show(line.Total)}"
                + (line.Batch is null ? "" : $", batch {line.Batch}")
                + (line.Expiry is DateOnly e ? $", expires {Answers.Show(e)}" : "")
                + (line.Free ? " (free)" : "")
                + (line.OrderLineNo is int n ? $" [order line {n}]" : ""));

        text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Captured: {Answers.Show(Captured)}");

        if (_invoiceTotal is decimal invoiced)
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}Invoice total: {Answers.Show(invoiced)}, difference "
                + $"{Answers.Show(Difference())}{(_differenceReason is null ? "" : $" ({_differenceReason})")}");

        foreach (var (id, price) in _newPrices)
            text.Append(
                $"{Environment.NewLine}New selling price for {_lines.First(l => l.ProductId == id).Description}: "
                + Answers.Show(price));

        return text.ToString();
    }

    public GoodsReceiptEditDto Edit() => new(
        _supplier?.Id ?? Guid.Empty, _order?.Id, _invoiceNumber, _invoiceDate, _invoiceTotal,
        _lines.Select(l => new GoodsReceiptLineEditDto(l.Code, l.Quantity, l.Cost, l.Batch, l.Expiry,
            l.OrderLineNo, l.ProductId)).ToList());

    private async Task<Reply> PostAsync(CancellationToken ct)
    {
        if (_lines.Count == 0) return Reply.Finish("No lines were captured, so nothing was received.");

        var posted = await _receiving.ReceiveAsync(Edit(), ct);
        if (posted.IsFailure) return Reply.Retry($"It was not posted: {posted.Error.Message}");

        Result = new GuidedResult(AppTaskKind.ReceiveStock, posted.Value.Id, posted.Value.ReceiptNumber);

        var notes = new List<string> { $"Posted {posted.Value.ReceiptNumber}: stock is updated." };

        foreach (var (id, price) in _newPrices)
            notes.Add(await ChangePriceAsync(id, price, ct));

        return Reply.Finish(Join(notes.ToArray()));
    }

    /// A price the operator chose over the rule's, through the product service.
    private async Task<string> ChangePriceAsync(Guid id, decimal price, CancellationToken ct)
    {
        var product = await _products.GetAsync(id, ct);
        if (product.IsFailure) return product.Error.Message;

        var p = product.Value;
        var changed = await _products.UpdateAsync(id, new ProductEditDto(
            p.Code, p.Description, p.GenericName, price, p.CostPrice, p.VatRatePercent, p.TaxCode, p.Schedule,
            p.IsBatchTracked, p.ReorderLevel, p.PackSize, p.DepartmentId, p.CategoryId, p.ManufacturerId,
            p.UnitsPerPack, p.Barcodes), ct);

        return changed.IsSuccess
            ? $"{p.Description} now sells at {Answers.Show(price)}."
            : $"The price of {p.Description} was not changed: {changed.Error.Message}";
    }
}

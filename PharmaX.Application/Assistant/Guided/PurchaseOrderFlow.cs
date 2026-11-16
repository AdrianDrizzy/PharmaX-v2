using System.Globalization;
using System.Text;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reporting;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Create a purchase order for 20 panado": the product, how many and who
/// from, asked one at a time, then raised through the purchasing service.
/// Batches and expiries are not asked: they come with the delivery.
/// </summary>
public sealed class PurchaseOrderFlow : GuidedFlow
{
    private readonly IProductService _products;
    private readonly IPurchasingService _purchasing;
    private readonly ISupplierService _suppliers;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly IPdfWriter? _pdf;
    private readonly IWorkbookWriter? _workbook;

    /// One line of the order as it is built.
    public sealed record Line(ProductListItemDto Product, decimal Quantity, decimal Cost);

    private readonly List<Line> _lines = new();
    private string? _term;
    private decimal? _askedQuantity;
    private string? _supplierSaid;

    // The line being asked about.
    private IReadOnlyList<(ProductListItemDto Product, ProductDto Detail, ProductBuyingDto Buying)> _matches =
        Array.Empty<(ProductListItemDto, ProductDto, ProductBuyingDto)>();
    private (ProductListItemDto Product, ProductDto Detail, ProductBuyingDto Buying)? _item;
    private decimal _quantity;

    private SupplierListItemDto? _supplier;
    private IReadOnlyList<SupplierListItemDto>? _supplierList;
    private IReadOnlyList<PurchaseOrderLineDto> _belowReorder = Array.Empty<PurchaseOrderLineDto>();
    private DateOnly? _expected;
    private string? _notes;
    private PurchaseOrderDto? _raised;

    /// Most products listed for "which".
    public const int MostMatches = 8;

    public PurchaseOrderFlow(string? item, decimal? quantity, string? supplier, IProductService products,
                             IPurchasingService purchasing, ISupplierService suppliers, IClock clock,
                             ICurrentUser user, IPdfWriter? pdf = null, IWorkbookWriter? workbook = null)
    {
        _products = products;
        _purchasing = purchasing;
        _suppliers = suppliers;
        _clock = clock;
        _user = user;
        _pdf = pdf;
        _workbook = workbook;
        _term = string.IsNullOrWhiteSpace(item) ? null : item.Trim();
        _askedQuantity = quantity;
        _supplierSaid = string.IsNullOrWhiteSpace(supplier) ? null : supplier.Trim();

        Add("product", ProductPromptAsync, ProductAsync);
        Add("quantity", QuantityPrompt, Quantity);
        Add("supplier", SupplierPromptAsync, SupplierAsync, () => _supplier is null);
        Add("cost", CostPrompt, Cost);
        Add("more", MorePromptAsync, MoreAsync);
        Add("expected", ExpectedPrompt, Expected);
        Add("notes", () => "Any notes for the supplier? (Enter for none)", Notes);
        AddConfirm(_ => Task.FromResult(Summary()), "Raise the order?", RaiseAsync, changeable: false);
        Add("export", () => "Save as PDF or Excel to send? (Enter for neither)", Export);
    }

    protected override string NothingDone => "No order was raised.";

    public IReadOnlyList<Line> Lines => _lines;

    protected override Task<string> IntroAsync(CancellationToken ct) =>
        Task.FromResult("A new purchase order. Say \"cancel\" at any time; nothing is raised until you say so.");

    // ---- Which product ----------------------------------------------------

    private async Task FindAsync(string term, CancellationToken ct)
    {
        var page = await _products.SearchAsync(term, take: MostMatches, includeInactive: false, ct: ct);
        var found = new List<(ProductListItemDto, ProductDto, ProductBuyingDto)>();

        foreach (var product in page.IsSuccess ? page.Value.Items : Array.Empty<ProductListItemDto>())
        {
            var detail = await _products.GetAsync(product.Id, ct);
            var buying = await _purchasing.BuyingAsync(product.Id, ct);
            if (detail.IsSuccess && buying.IsSuccess) found.Add((product, detail.Value, buying.Value));
        }

        _matches = found;
    }

    private async Task<string> ProductPromptAsync(CancellationToken ct)
    {
        _item = null;
        if (_term is null) return "Which product?";

        await FindAsync(_term, ct);

        if (_matches.Count == 0) return $"Nothing matches \"{_term}\". Which product?";

        if (_matches.Count == 1)
        {
            // Nothing to choose between.
            _item = _matches[0];
            Say($"{_item.Value.Product.Description} ({_item.Value.Product.Code}).");
            return "";
        }

        return $"Which {_term}?" + Environment.NewLine + Answers.Numbered(_matches, m =>
            $"{m.Product.Description} ({m.Product.Code}) - {Pack(m.Product)}{Answers.Quantity(m.Product.QuantityOnHand)} on hand, "
            + $"reorder at {Answers.Quantity(m.Detail.ReorderLevel)}, {Answers.Quantity(m.Buying.OnOrder)} on order");
    }

    private static string Pack(ProductListItemDto p) =>
        string.IsNullOrWhiteSpace(p.PackSize) ? "" : p.PackSize + ", ";

    private async Task<Reply> ProductAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("Which product? Say its name or code.");
        if (Answers.IsOffList(answer, _matches.Count)) return Reply.Retry($"Choose 1 to {_matches.Count}.");

        var chosen = _matches.Count > 0
            ? Answers.Pick(answer, _matches.Select(m => m.Product).ToList(), p => p.Description)
            : null;

        if (chosen is not null)
        {
            _item = _matches.First(m => m.Product.Id == chosen.Id);
            return Reply.Ok();
        }

        // Another name: look again.
        _term = answer.Trim();
        return new Reply.Jump("product");
    }

    // ---- How many ---------------------------------------------------------

    private string Stock()
    {
        var (product, detail, buying) = _item!.Value;
        return $"You have {Answers.Quantity(product.QuantityOnHand)} on hand, your reorder level is "
               + $"{Answers.Quantity(detail.ReorderLevel)}, and {Answers.Quantity(buying.OnOrder)} are on order.";
    }

    private string QuantityPrompt()
    {
        var name = _item!.Value.Product.Description;

        return _askedQuantity is decimal asked
            ? $"{name}: {Answers.Quantity(asked)} packs? {Stock()} Enter to keep it, or say how many."
            : $"{name}: how many packs? {Stock()}";
    }

    private Reply Quantity(string answer)
    {
        decimal? quantity = answer.Length == 0 || Answers.IsYes(answer) ? _askedQuantity : Answers.Number(answer);

        if (quantity is not > 0m) return Reply.Retry("How many packs, more than nought?");

        _quantity = quantity.Value;
        _askedQuantity = null;
        return Reply.Ok();
    }

    // ---- Who from ---------------------------------------------------------

    private async Task<IReadOnlyList<SupplierListItemDto>> SuppliersAsync(CancellationToken ct)
    {
        if (_supplierList is not null) return _supplierList;

        var list = await _suppliers.ListAsync(includeInactive: false, ct: ct);
        _supplierList = list.IsSuccess ? list.Value : Array.Empty<SupplierListItemDto>();
        return _supplierList;
    }

    private SupplierListItemDto? Usual(IReadOnlyList<SupplierListItemDto> suppliers)
    {
        var buying = _item!.Value.Buying;
        var id = buying.DefaultSupplierId ?? buying.LastSupplierId;
        return suppliers.FirstOrDefault(s => s.Id == id);
    }

    private async Task<string> SupplierPromptAsync(CancellationToken ct)
    {
        var suppliers = await SuppliersAsync(ct);

        // "... from Pharmanova" in the request.
        if (_supplierSaid is not null)
        {
            var said = Answers.Pick(_supplierSaid, suppliers, s => s.Name);
            _supplierSaid = null;

            if (said is not null)
            {
                _supplier = said;
                return "";
            }
        }

        var buying = _item!.Value.Buying;
        var text = new StringBuilder("Supplier?");

        if (Usual(suppliers) is { } usual)
            text.Append(CultureInfo.InvariantCulture, $" Press Enter for {usual.Name}, the usual supplier.");

        if (buying.LastSupplierName is not null && buying.LastCost is decimal cost)
            text.Append(
                $" It last came from {buying.LastSupplierName} at {Answers.Show(cost)}"
                + (buying.LastReceivedOn is DateOnly on ? $" on {Answers.Show(on)}." : "."));

        return text.ToString();
    }

    private async Task<Reply> SupplierAsync(string answer, CancellationToken ct)
    {
        var suppliers = await SuppliersAsync(ct);

        if (answer.Length == 0)
        {
            _supplier = Usual(suppliers);
            return _supplier is null ? Reply.Retry("There is no usual supplier. Which supplier?") : Reply.Ok();
        }

        var matches = suppliers.Where(s => Answers.Pick(answer, new[] { s }, x => x.Name) is not null).ToList();

        if (matches.Count == 1)
        {
            _supplier = matches[0];
            return Reply.Ok();
        }

        return matches.Count == 0
            ? Reply.Retry($"No supplier matches \"{answer.Trim()}\".")
            : Reply.Retry($"\"{answer.Trim()}\" could be {string.Join(", ", matches.Take(6).Select(s => s.Name))}. "
                          + "Say more of the name.");
    }

    // ---- At what cost -----------------------------------------------------

    private decimal DefaultCost()
    {
        var (_, detail, buying) = _item!.Value;

        return buying.LastCost is decimal last && buying.LastSupplierId == _supplier?.Id
            ? last
            : detail.CostPrice;
    }

    private string CostPrompt() =>
        $"Cost price per pack? Press Enter for {Answers.Show(DefaultCost())}.";

    private Reply Cost(string answer)
    {
        decimal? cost = answer.Length == 0 ? DefaultCost() : Answers.Money(answer);
        if (cost is null) return Reply.Retry("What does one pack cost, e.g. 4.50?");

        if (cost.Value == 0m)
            return Reply.YesNo("Order it at no cost? The supplier would be sent a price of 0.00.",
                _ => Task.FromResult(TakeLine(0m)), "Then what does one pack cost?");

        return TakeLine(cost.Value);
    }

    private Reply TakeLine(decimal cost)
    {
        var product = _item!.Value.Product;
        _lines.RemoveAll(l => l.Product.Id == product.Id);
        _lines.Add(new Line(product, _quantity, cost));

        return Reply.Ok($"{Answers.Quantity(_quantity)} x {product.Description} at {Answers.Show(cost)}.");
    }

    // ---- Anything else ----------------------------------------------------

    private async Task<string> MorePromptAsync(CancellationToken ct)
    {
        var suggested = _supplier is null
            ? null
            : await _purchasing.SuggestForAsync(_supplier.Id, ct);

        _belowReorder = suggested is { IsSuccess: true }
            ? suggested.Value.Where(s => _lines.All(l => l.Product.Id != s.ProductId)).ToList()
            : Array.Empty<PurchaseOrderLineDto>();

        if (_belowReorder.Count == 0)
            return "Anything else on this order? Name it, or press Enter if that's all.";

        return $"Anything else on this order? {_belowReorder.Count} other item(s) from {_supplier!.Name} are "
               + "below their reorder level:" + Environment.NewLine
               + Answers.Numbered(_belowReorder.Take(15), s =>
                   $"{s.Description} ({s.ProductCode}) - {Answers.Quantity(s.QuantityOrdered)} to bring it back up")
               + (_belowReorder.Count > 15 ? $"{Environment.NewLine}... and {_belowReorder.Count - 15} more" : "")
               + Environment.NewLine + "Say \"add them\", name another item, or press Enter if that's all.";
    }

    private async Task<Reply> MoreAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer) || Answers.IsNo(answer)) return Reply.Ok();

        var words = Answers.Words(answer);
        if (_belowReorder.Count > 0 && (Answers.IsYes(answer) || words.Contains("add") || words.Contains("all")))
        {
            foreach (var s in _belowReorder)
            {
                var product = await _products.GetAsync(s.ProductId, ct);
                if (product.IsFailure) continue;

                var p = product.Value;
                _lines.Add(new Line(new ProductListItemDto(p.Id, p.Code, p.Description, p.RetailPrice,
                    p.QuantityOnHand, p.Schedule, p.IsBatchTracked, p.IsActive, true, p.PackSize, p.UnitsPerPack),
                    s.QuantityOrdered, s.UnitCost));
            }

            return Reply.Ok($"Added {_belowReorder.Count} item(s). The order has {_lines.Count} lines.");
        }

        _term = answer.Trim();
        return new Reply.Jump("product");
    }

    // ---- When, and notes --------------------------------------------------

    private DateOnly DefaultExpected() =>
        _clock.Today.AddDays(_supplier?.LeadTimeDays ?? 0);

    private string ExpectedPrompt() =>
        $"Expected delivery date? Press Enter for {Answers.Show(DefaultExpected())}"
        + (_supplier is null ? "." : $", the supplier's lead time of {_supplier.LeadTimeDays} day(s).");

    private Reply Expected(string answer)
    {
        if (answer.Length == 0)
        {
            _expected = DefaultExpected();
            return Reply.Ok();
        }

        if (Answers.Date(answer) is not DateOnly date) return Reply.Retry("Say the date as dd/mm/yyyy.");
        if (date < _clock.Today) return Reply.Retry("That date has already passed.");

        _expected = date;
        return Reply.Ok();
    }

    private Reply Notes(string answer)
    {
        _notes = Answers.IsSkip(answer) ? null : answer.Trim();
        return Reply.Ok(_notes is null ? null : "The notes print on the PDF or Excel copy you send.");
    }

    // ---- Raise ------------------------------------------------------------

    public decimal Total => _lines.Sum(l => l.Quantity * l.Cost);

    public string Summary()
    {
        var text = new StringBuilder($"Purchase order to {_supplier?.Name}:");

        foreach (var (line, i) in _lines.Select((l, i) => (l, i)))
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}{i + 1}. {line.Product.Description} ({line.Product.Code}) "
                + $"{Answers.Quantity(line.Quantity)} x {Answers.Show(line.Cost)} = {Answers.Show(line.Quantity * line.Cost)}");

        text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Total: {Answers.Show(Total)}");
        text.Append(CultureInfo.InvariantCulture,
            $"{Environment.NewLine}Expected: {(_expected is DateOnly e ? Answers.Show(e) : "-")}");
        if (_notes is not null) text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Notes: {_notes}");

        return text.ToString();
    }

    public PurchaseOrderEditDto Edit() => new(
        _supplier?.Id ?? Guid.Empty, _expected,
        _lines.Select(l => new PurchaseOrderLineEditDto(l.Product.Code, l.Quantity, l.Cost)).ToList());

    private async Task<Reply> RaiseAsync(CancellationToken ct)
    {
        var raised = await _purchasing.RaiseAsync(Edit(), ct);
        if (raised.IsFailure) return Reply.Retry($"It was not raised: {raised.Error.Message}");

        _raised = raised.Value;
        Result = new GuidedResult(AppTaskKind.PurchaseOrder, raised.Value.Id, raised.Value.OrderNumber);

        return new Reply.Jump("export", $"Raised {raised.Value.OrderNumber} to {raised.Value.SupplierName}, "
                                        + $"{Answers.Show(raised.Value.Total)}.");
    }

    /// Where an exported order is saved: the Downloads folder.
    public static string Downloads =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private Reply Export(string answer)
    {
        var words = Answers.Words(answer);
        var pdf = words.Contains("pdf") || words.Contains("both");
        var excel = words.Contains("excel") || words.Contains("xlsx") || words.Contains("both");

        if (Answers.IsSkip(answer) || Answers.IsNo(answer)) return Reply.Finish("Done.");
        if (!pdf && !excel) return Reply.Retry("PDF, Excel, both, or Enter for neither?");
        if (_raised is null) return Reply.Finish("Done.");

        var report = PurchaseOrderReport.Build(_raised, _user.Username, DateTime.Now);
        if (_notes is not null)
            report = report with
            {
                Figures = report.Figures.Append(new ReportFigure("Notes", _notes)).ToList()
            };

        var saved = new List<string>();
        var name = Path.Combine(Downloads, $"Purchase order {_raised.OrderNumber} {Safe(_raised.SupplierName)}");

        try
        {
            Directory.CreateDirectory(Downloads);

            if (pdf && _pdf is not null)
            {
                _pdf.WriteTo(report, name + ".pdf");
                saved.Add(name + ".pdf");
            }

            if (excel && _workbook is not null)
            {
                _workbook.WriteTo(report, name + ".xlsx");
                saved.Add(name + ".xlsx");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Reply.Retry($"It could not be saved: {ex.Message} Close the file if it is open, and try again.");
        }

        return Reply.Finish(saved.Count == 0
            ? "Exporting isn't available here. Export it from Purchase orders."
            : "Saved " + string.Join(" and ", saved) + ".");
    }

    private static string Safe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
}

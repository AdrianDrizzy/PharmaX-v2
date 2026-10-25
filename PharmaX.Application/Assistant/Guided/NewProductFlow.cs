using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Add a new product called Vitamin C 1000mg": the product editor's
/// fields asked one at a time, then saved through the product service.
/// </summary>
public sealed partial class NewProductFlow : GuidedFlow
{
    private readonly IProductService _products;
    private readonly IReferenceDataService _reference;
    private readonly IPharmacySettings _settings;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;

    // The answers so far.
    private string? _description;
    private string _code = "";
    private string? _generic;
    private string? _packSize;
    private decimal? _unitsPerPack;
    private readonly List<string> _barcodes = new();
    private ReferenceItemDto? _department;
    private ReferenceItemDto? _category;
    private ReferenceItemDto? _manufacturer;
    private int _schedule;
    private string _taxCode = "A";
    private decimal _vatRate = 15m;
    private decimal _cost;
    private decimal _price;
    private decimal _reorderLevel;
    private bool? _batchTracked;
    private decimal _openingStock;
    private string? _batchNumber;
    private DateOnly? _expiry;
    private string? _reason;

    // Looked up once, when first needed.
    private string? _nextCode;
    private IReadOnlyList<ReferenceItemDto>? _departments;
    private IReadOnlyList<ReferenceItemDto>? _categories;
    private IReadOnlyList<ReferenceItemDto>? _manufacturers;
    private (decimal Markup, decimal Rounding)? _pricing;

    /// Expiry closer than this is said out loud.
    public const int ShortExpiryDays = 91;

    public NewProductFlow(string? description, IProductService products, IReferenceDataService reference,
                          IPharmacySettings settings, IPermissionService permissions, IClock clock)
    {
        _products = products;
        _reference = reference;
        _settings = settings;
        _permissions = permissions;
        _clock = clock;
        _description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var named = _description is not null;

        Add("name", () => "What is the product called?", Name, () => !named);
        Add("code", CodePromptAsync, CodeAsync);
        Add("generic", GenericPrompt, Generic);
        Add("pack", () => "Pack size, e.g. 30 tablets? And units per pack? (Enter to skip)", Pack);
        Add("barcode", () => "Barcode? Scan it now or skip.", BarcodeAsync);
        Add("department-category", FilingPromptAsync, FilingAsync);
        Add("manufacturer", ManufacturerPromptAsync, ManufacturerAsync);
        Add("schedule", () => "Schedule? Press Enter for 0 (over the counter).", Schedule);
        Add("tax", () => "Tax code? Standard VAT 15% or exempt?", Tax);
        Add("cost", () => "Cost price per pack?", Cost);
        Add("price", PricePromptAsync, PriceAsync);
        Add("reorder", () => "Reorder level? How few before it should alert you? (Enter for none)", Reorder);
        Add("batch-tracked", BatchTrackedPrompt, BatchTracked);
        Add("stock", () => "Opening stock: how many packs are on the shelf now? (Enter for none)", Stock,
            () => MayCountStock);
        Add("batch-number", () => "Batch number?", BatchNumber, () => _openingStock > 0m && _batchTracked == true);
        Add("expiry", () => "Expiry date? (dd/mm/yyyy, or month and year as printed)", Expiry,
            () => _openingStock > 0m && _batchTracked == true);
        Add("reason", () => "Reason for the stock? Press Enter for \"Opening stock\".", Reason,
            () => _openingStock > 0m);
        Add("duplicate", DuplicatePromptAsync, Duplicate);
        AddConfirm(_ => Task.FromResult(Summary()), "Save it?", SaveAsync);
    }

    protected override string ResumeKey => "duplicate";

    private static readonly char[] BarcodeSeparators = { ',', ';', ' ' };

    private bool MayCountStock =>
        _permissions.Has(PermissionCode.StockCorrect) || _permissions.Has(PermissionCode.StockTakePost);

    /// Schedule 1 and up, or anything with an active ingredient, is a medicine.
    private bool IsMedicine => _schedule >= 1 || _generic is not null;

    protected override Task<string> IntroAsync(CancellationToken ct) =>
        Task.FromResult(_description is null
            ? "Let's add a new product. Say \"cancel\" at any time; nothing is saved until you say so."
            : $"New product: {_description}. I'll ask a few questions; say \"cancel\" at any time, "
              + "and nothing is saved until you say so.");

    private Reply Name(string answer)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("A product needs a name.");
        _description = answer.Trim();
        return Reply.Ok();
    }

    private async Task<string> CodePromptAsync(CancellationToken ct)
    {
        if (_nextCode is null)
        {
            var next = await _products.NextCodeAsync(ct);
            _nextCode = next.IsSuccess ? next.Value : "";
        }

        return _nextCode.Length > 0
            ? $"Product code? Press Enter to let PharmaX number it ({_nextCode})."
            : "Product code?";
    }

    private async Task<Reply> CodeAsync(string answer, CancellationToken ct)
    {
        var code = Answers.IsSkip(answer) ? _nextCode ?? "" : answer.Trim().ToUpperInvariant();

        if (code.Length == 0) return Reply.Retry("A product needs a code.");
        if (code.Length > 30) return Reply.Retry("A product code is 1 to 30 characters.");
        if (code.Contains(' ', StringComparison.Ordinal)) return Reply.Retry("A product code has no spaces.");

        var taken = await _products.FindAsync(code, ct);
        if (taken.IsSuccess && taken.Value is { } other)
            return Reply.Retry($"{code} is already {other.Description} ({other.Code}).");

        _code = code;
        return Reply.Ok($"Code {code}.");
    }

    private string GenericPrompt()
    {
        var suggestion = Ingredients.For(_description);
        return suggestion is null
            ? "Generic name or active ingredient? (Enter to skip)"
            : $"Generic name or active ingredient? Press Enter for \"{suggestion}\", or say none.";
    }

    private Reply Generic(string answer)
    {
        if (answer.Length == 0)
        {
            _generic = Ingredients.For(_description);
            return Reply.Ok();
        }

        _generic = Answers.IsSkip(answer) ? null : answer.Trim().ToLowerInvariant();
        return Reply.Ok();
    }

    private Reply Pack(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _packSize = null;
            _unitsPerPack = null;
            return Reply.Ok();
        }

        if (PackSize.Read(answer) is not { } pack)
            return Reply.Retry("Say it as e.g. \"30 tablets\", \"100 ml\" or \"TAB 30, 30\".");

        _packSize = pack.Label;
        _unitsPerPack = pack.Units;

        return Reply.Ok(pack.Units is decimal units
            ? $"Pack {pack.Label}, {Answers.Quantity(units)} units a pack."
            : $"Pack {pack.Label}.");
    }

    private async Task<Reply> BarcodeAsync(string answer, CancellationToken ct)
    {
        _barcodes.Clear();
        if (Answers.IsSkip(answer)) return Reply.Ok();

        foreach (var barcode in answer.Split(BarcodeSeparators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var other = await _products.FindAsync(barcode, ct);
            if (other.IsSuccess && other.Value is { } taken)
            {
                _barcodes.Clear();
                return Reply.Retry($"Barcode {barcode} already belongs to {taken.Description} ({taken.Code}).");
            }

            _barcodes.Add(barcode);
        }

        return Reply.Ok();
    }

    private async Task<IReadOnlyList<ReferenceItemDto>> ListAsync(ReferenceKind kind, CancellationToken ct)
    {
        var result = await _reference.ListAsync(kind, includeInactive: false, ct: ct);

        // One of each name: an imported list can carry the same one twice.
        return result.IsSuccess
            ? result.Value.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<ReferenceItemDto>();
    }

    private async Task<string> FilingPromptAsync(CancellationToken ct)
    {
        _departments ??= await ListAsync(ReferenceKind.Department, ct);
        _categories ??= await ListAsync(ReferenceKind.Category, ct);

        return "Department and category? (Enter to skip)" + Environment.NewLine
               + "Departments: " + Names(_departments) + Environment.NewLine
               + "Categories: " + Names(_categories);
    }

    private static string Names(IReadOnlyList<ReferenceItemDto> items) =>
        items.Count == 0 ? "(none set up)" : string.Join(", ", items.Select(i => i.Name));

    private async Task<Reply> FilingAsync(string answer, CancellationToken ct)
    {
        _departments ??= await ListAsync(ReferenceKind.Department, ct);
        _categories ??= await ListAsync(ReferenceKind.Category, ct);

        _department = null;
        _category = null;
        if (Answers.IsSkip(answer)) return Reply.Ok();

        foreach (var part in Answers.List(answer))
        {
            if (_department is null && Answers.Pick(part, _departments, d => d.Name) is { } department)
                _department = department;
            else if (_category is null && Answers.Pick(part, _categories, c => c.Name) is { } category)
                _category = category;
            else
                return Reply.Retry($"I can't find \"{part}\" in the departments or categories.");
        }

        return Reply.Ok(Join(_department is null ? null : $"Department {_department.Name}.",
                            _category is null ? null : $"Category {_category.Name}."));
    }

    private async Task<string> ManufacturerPromptAsync(CancellationToken ct)
    {
        _manufacturers ??= await ListAsync(ReferenceKind.Manufacturer, ct);

        return _manufacturers.Count == 0
            ? ""
            : "Manufacturer? (Enter to skip)" + Environment.NewLine
              + Answers.Numbered(_manufacturers, m => m.Name);
    }

    private async Task<Reply> ManufacturerAsync(string answer, CancellationToken ct)
    {
        _manufacturers ??= await ListAsync(ReferenceKind.Manufacturer, ct);
        _manufacturer = null;

        if (Answers.IsSkip(answer)) return Reply.Ok();

        _manufacturer = Answers.Pick(answer, _manufacturers, m => m.Name);
        return _manufacturer is null
            ? Reply.Retry("Choose one by its number or name, or press Enter to skip.")
            : Reply.Ok();
    }

    private Reply Schedule(string answer)
    {
        if (answer.Length == 0)
        {
            _schedule = 0;
            return Reply.Ok();
        }

        if (Answers.Number(answer) is not decimal n || n != Math.Floor(n) || n is < 0 or > 8)
            return Reply.Retry("The schedule is a whole number from 0 to 8.");

        var schedule = (int)n;

        if (schedule < 3)
        {
            _schedule = schedule;
            return Reply.Ok();
        }

        if (!_permissions.Has(PermissionCode.ProductDispensary))
            return Reply.Retry("Schedule 3 and above needs the dispensary permission. "
                              + "Choose 0 to 2, or ask a pharmacist to add it.");

        return Reply.YesNo(
            $"Schedule {schedule} makes it pharmacist only: it can't be sold without a pharmacist's "
            + "authority, and cashiers won't see it. Is that right?",
            _ =>
            {
                _schedule = schedule;
                return Task.FromResult(Reply.Ok());
            },
            "Then which schedule?");
    }

    private Reply Tax(string answer)
    {
        var words = Answers.Words(answer);

        if (words.Any(w => w is "standard" or "vat" or "15" or "a" or "s" or "vatable" or "taxable"))
        {
            (_taxCode, _vatRate) = ("A", 15m);
            return Reply.Ok();
        }

        if (words.Any(w => w is "exempt" or "zero" or "z" or "e" or "0" or "none" or "no"))
        {
            (_taxCode, _vatRate) = ("Z", 0m);
            return Reply.Ok();
        }

        return Reply.Retry("Say standard (VAT 15%) or exempt.");
    }

    private Reply Cost(string answer)
    {
        if (Answers.Money(answer) is not decimal cost) return Reply.Retry("What does one pack cost you, e.g. 4.50?");

        _cost = cost;
        if (cost > 0m) return Reply.Ok();

        return Reply.YesNo("No cost price means no margin can be worked out. Leave it at 0?",
            _ => Task.FromResult(Reply.Ok()), "Then what does one pack cost?");
    }

    private async Task<decimal?> SuggestedPriceAsync(CancellationToken ct)
    {
        _pricing ??= await Pricing.ReadAsync(_settings, ct);
        var (markup, Reply) = _pricing.Value;

        return Pricing.NewRetail(0m, 0m, _cost, markup, Reply);
    }

    private async Task<string> PricePromptAsync(CancellationToken ct)
    {
        if (await SuggestedPriceAsync(ct) is not decimal suggested)
            return _pricing!.Value.Markup > 0m
                ? "Selling price?"
                : "Selling price? (No usual markup is set in System setup, so there is none to offer.)";

        return $"Selling price? Press Enter for {Answers.Show(suggested)}: cost plus the usual "
               + $"{_pricing!.Value.Markup.ToString("0.##", CultureInfo.InvariantCulture)}% markup, "
               + $"a margin of {Margin(_cost, suggested)}.";
    }

    private async Task<Reply> PriceAsync(string answer, CancellationToken ct)
    {
        decimal? price = answer.Length == 0 ? await SuggestedPriceAsync(ct) : Answers.Money(answer);

        if (price is not > 0m) return Reply.Retry("What does one pack sell for, e.g. 6.50?");

        var chosen = price.Value;
        var note = _cost > 0m ? $"{Answers.Show(chosen)}, a margin of {Margin(_cost, chosen)}." : null;

        if (_cost > 0m && chosen < _cost)
            return Reply.YesNo(
                $"{Answers.Show(chosen)} is below the cost of {Answers.Show(_cost)}: a loss of "
                + $"{Answers.Show(_cost - chosen)} on every pack. Keep it?",
                _ =>
                {
                    _price = chosen;
                    return Task.FromResult(Reply.Ok());
                },
                "Then what does it sell for?");

        _price = chosen;
        return Reply.Ok(note);
    }

    /// The share of the selling price kept: (price - cost) / price.
    public static string Margin(decimal cost, decimal price) =>
        price <= 0m
            ? "-"
            : ((price - cost) / price).ToString("0.#%", CultureInfo.InvariantCulture);

    private Reply Reorder(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _reorderLevel = 0m;
            return Reply.Ok();
        }

        if (Answers.Number(answer) is not decimal level || level < 0m)
            return Reply.Retry("Say how many packs, e.g. 5, or press Enter for none.");

        _reorderLevel = level;
        return Reply.Ok();
    }

    private string BatchTrackedPrompt() =>
        IsMedicine
            ? "Is it batch tracked, with an expiry date? Press Enter for yes."
            : "Is it batch tracked, with an expiry date? Press Enter for no.";

    private Reply BatchTracked(string answer)
    {
        if (answer.Length == 0) _batchTracked = IsMedicine;
        else if (Answers.IsYes(answer)) _batchTracked = true;
        else if (Answers.IsNo(answer)) _batchTracked = false;
        else return Reply.Retry("Yes or no?");

        return Reply.Ok();
    }

    private Reply Stock(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _openingStock = 0m;
            return Reply.Ok();
        }

        if (Answers.Number(answer) is not decimal packs || packs < 0m)
            return Reply.Retry("How many packs, e.g. 12? Or press Enter for none.");

        _openingStock = packs;
        return Reply.Ok();
    }

    private Reply BatchNumber(string answer)
    {
        if (Answers.IsSkip(answer) || answer.Trim().Length > 50)
            return Reply.Retry("Enter the batch number printed on the pack.");

        _batchNumber = answer.Trim().ToUpperInvariant();
        return Reply.Ok();
    }

    private Reply Expiry(string answer) =>
        ExpiryCheck.Read(answer, _clock.Today, ShortExpiryDays, date => _expiry = date);

    private Reply Reason(string answer)
    {
        _reason = Answers.IsSkip(answer) ? "Opening stock" : answer.Trim();
        return Reply.Ok();
    }

    /// <summary>
    /// The products already called the same, or scanning by one of the
    /// same barcodes.
    /// </summary>
    private async Task<IReadOnlyList<ProductListItemDto>> DuplicatesAsync(CancellationToken ct)
    {
        var found = new List<ProductListItemDto>();
        var name = Normalised(_description);

        var named = await _products.SearchAsync(_description ?? "", take: 50, includeInactive: true, ct: ct);
        if (named.IsSuccess)
            found.AddRange(named.Value.Items.Where(p => Normalised(p.Description) == name));

        foreach (var barcode in _barcodes)
        {
            var other = await _products.FindAsync(barcode, ct);
            if (other.IsSuccess && other.Value is { } same && found.All(f => f.Id != same.Id))
                found.Add(same);
        }

        return found;
    }

    private static string Normalised(string? text) =>
        Spaces().Replace((text ?? "").Trim().ToUpperInvariant(), " ");

    private async Task<string> DuplicatePromptAsync(CancellationToken ct)
    {
        var same = await DuplicatesAsync(ct);
        if (same.Count == 0) return "";

        var list = string.Join(", ", same.Select(p => $"{p.Description} ({p.Code})"));
        return $"There is already {list}. Add anyway?";
    }

    private static Reply Duplicate(string answer)
    {
        if (Answers.IsYes(answer)) return Reply.Ok();
        if (Answers.IsNo(answer))
            return Reply.Finish("Nothing was saved. Open the existing product from Products if it needs changing.");

        return Reply.Retry("Yes to add it anyway, or no to stop.");
    }

    public string Summary()
    {
        var text = new StringBuilder();

        void Line(string label, string? value) =>
            text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}{label}: {value ?? "-"}");

        text.Append("The new product:");
        Line("Description", _description);
        Line("Code", _code);
        Line("Generic name", _generic);
        Line("Pack size", _packSize is null
            ? null
            : _unitsPerPack is decimal u ? $"{_packSize}, {Answers.Quantity(u)} units a pack" : _packSize);
        Line("Barcode", _barcodes.Count == 0 ? null : string.Join(", ", _barcodes));
        Line("Department", _department?.Name);
        Line("Category", _category?.Name);
        Line("Manufacturer", _manufacturer?.Name);
        Line("Schedule", _schedule >= 3 ? $"{_schedule} (pharmacist only)" : $"{_schedule}");
        Line("Tax", _taxCode == "A" ? "Standard VAT 15%" : "Exempt");
        Line("Cost price", Answers.Show(_cost));
        Line("Selling price", $"{Answers.Show(_price)} (margin {Margin(_cost, _price)})");
        Line("Reorder level", _reorderLevel > 0m ? Answers.Quantity(_reorderLevel) : "none");
        Line("Batch tracked", _batchTracked == true ? "yes" : "no");

        if (!MayCountStock)
            Line("Opening stock", "none - counting stock needs the stock permission");
        else if (_openingStock <= 0m)
            Line("Opening stock", "none");
        else
            Line("Opening stock", _batchTracked == true
                ? $"{Answers.Quantity(_openingStock)} packs, batch {_batchNumber}, expires "
                  + $"{(_expiry is DateOnly e ? Answers.Show(e) : "-")} ({_reason})"
                : $"{Answers.Quantity(_openingStock)} packs ({_reason})");

        text.Append(Environment.NewLine + "Say \"change\" and what to change, e.g. \"change the price\".");
        return text.ToString();
    }

    /// The edit the product editor would send for the same answers.
    public ProductEditDto Edit()
    {
        var batched = _openingStock > 0m && _batchTracked == true;

        return new ProductEditDto(
            Code: _code,
            Description: _description ?? "",
            GenericName: _generic,
            RetailPrice: _price,
            CostPrice: _cost,
            VatRatePercent: _vatRate,
            TaxCode: _taxCode,
            Schedule: _schedule,
            IsBatchTracked: _batchTracked == true,
            ReorderLevel: _reorderLevel,
            PackSize: _packSize,
            DepartmentId: _department?.Id,
            CategoryId: _category?.Id,
            ManufacturerId: _manufacturer?.Id,
            UnitsPerPack: _unitsPerPack,
            Barcodes: _barcodes.ToList(),
            IsActive: true,
            QuantityOnHand: _openingStock > 0m && !batched ? _openingStock : null,
            Batches: batched
                ? new[] { new ProductBatchEditDto(null, _batchNumber ?? "", _expiry, _openingStock) }
                : null,
            StockReason: _openingStock > 0m ? _reason : null);
    }

    private async Task<Reply> SaveAsync(CancellationToken ct)
    {
        var saved = await _products.CreateAsync(Edit(), ct);

        if (saved.IsFailure)
            return Reply.Retry($"It was not saved: {saved.Error.Message}");

        Result = new GuidedResult(AppTaskKind.NewProduct, saved.Value, _code);
        return Reply.Finish($"Saved {_description} as product {_code}.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

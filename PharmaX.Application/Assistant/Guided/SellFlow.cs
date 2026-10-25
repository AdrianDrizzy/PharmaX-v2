using System.Globalization;
using System.Text;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Sell 2 paracetamol": the till, asked one question at a time. Items are
/// rung up on an open sale as the till does it, so the sale service prices,
/// allocates batches and refuses short stock line by line; nothing is
/// finalised until the operator says yes, and a stopped sale is abandoned.
/// </summary>
public sealed class SellFlow : GuidedFlow
{
    private readonly ISaleService _sales;
    private readonly ITillSessionService _tills;
    private readonly IProductService _products;
    private readonly IAccountService _accounts;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IGuidedPrompts _prompts;

    private string? _term;
    private decimal? _askedQuantity;
    private string? _customerSaid;

    private bool _needsSession;
    private Guid? _saleId;
    private SaleDto? _sale;
    private IReadOnlyList<ProductListItemDto> _matches = Array.Empty<ProductListItemDto>();
    private ProductListItemDto? _product;
    private IReadOnlyList<AccountDto> _accountMatches = Array.Empty<AccountDto>();
    private AccountDto? _account;
    private decimal _discount;
    private readonly List<PaymentDto> _payments = new();
    private decimal _tendered;

    /// Most products listed for "which".
    public const int MostMatches = 8;

    public SellFlow(string? item, decimal? quantity, string? customer, ISaleService sales, ITillSessionService tills,
                    IProductService products, IAccountService accounts, ICurrentUser user,
                    IPermissionService permissions, IGuidedPrompts prompts)
    {
        _sales = sales;
        _tills = tills;
        _products = products;
        _accounts = accounts;
        _user = user;
        _permissions = permissions;
        _prompts = prompts;
        _term = string.IsNullOrWhiteSpace(item) ? null : item.Trim();
        _askedQuantity = quantity;
        _customerSaid = string.IsNullOrWhiteSpace(customer) ? null : customer.Trim();

        Add("float", () => "No till session is open. Open a till session with what float?", FloatAsync,
            () => _needsSession);
        Add("product", ProductPromptAsync, ProductAsync);
        Add("quantity", QuantityPromptAsync, QuantityAsync);
        Add("more", () => "Anything else? Name it, or press Enter if that's all.", More);
        Add("customer", CustomerPromptAsync, CustomerAsync);
        Add("discount", DiscountPrompt, DiscountAsync, () => MayDiscount);
        Add("tender", TenderPromptAsync, Tender, () => TakesPayment);
        Add("tendered", () => $"Cash due {Answers.Show(CashDue)}. Amount tendered?", Tendered,
            () => TakesPayment && CashDue > 0m);
        AddConfirm(_ => Task.FromResult(Summary()),
            TakesPayment ? "Complete the sale and print the receipt?" : "Hold it for the cashier to take payment?",
            CompleteAsync, changeable: false);
    }

    protected override string NothingDone => "No sale was made.";

    private bool TakesPayment => _permissions.Has(PermissionCode.SaleTakePayment);

    /// Only where the user may give one: a limit of their own, or the authority above it.
    private bool MayDiscount =>
        _user.DiscountLimitPercent > 0m || _permissions.Has(PermissionCode.SaleDiscountAboveLimit);

    private decimal Total => _sale?.Totals.Total ?? 0m;

    private decimal CashDue => _payments.Where(p => p.TenderType == "Cash").Sum(p => p.Amount);

    public SaleDto? Sale => _sale;

    protected override async Task<string> IntroAsync(CancellationToken ct)
    {
        // A cashier needs a session; an assistant's basket is held for one.
        _needsSession = TakesPayment && await _tills.CurrentAsync(ct) is null;
        return "The till. Say \"cancel\" at any time; nothing is sold until you say so.";
    }

    protected override async Task AbandonAsync(CancellationToken ct)
    {
        if (_saleId is Guid id) await _sales.AbandonAsync(id, "Stopped in Ask PharmaX", ct);
    }

    // ---- Till session -----------------------------------------------------

    private async Task<Reply> FloatAsync(string answer, CancellationToken ct)
    {
        if (Answers.Money(answer) is not decimal opening) return Reply.Retry("Say the float in the drawer, e.g. 20.00.");

        var opened = await _tills.OpenAsync(opening, ct);
        return opened.IsFailure
            ? Reply.Retry(opened.Error.Message)
            : Reply.Ok($"Till session opened with a float of {Answers.Show(opening)}.");
    }

    // ---- Which, and how many ----------------------------------------------

    private async Task<string> ProductPromptAsync(CancellationToken ct)
    {
        _product = null;
        if (_term is null) return "What is being sold?";

        var found = await _products.SearchAsync(_term, take: 30, includeInactive: false, ct: ct);
        _matches = found.IsSuccess
            ? found.Value.Items.Where(p => p.QuantityOnHand > 0m).Take(MostMatches).ToList()
            : Array.Empty<ProductListItemDto>();

        if (_matches.Count == 0) return $"Nothing called \"{_term}\" is in stock. What is being sold?";

        if (_matches.Count == 1 && _matches[0].Schedule < 3)
        {
            _product = _matches[0];
            Say($"{_product.Description} - {Answers.Show(_product.RetailPrice)} - "
                + $"{Answers.Quantity(_product.QuantityOnHand)} on hand.");
            return "";
        }

        return $"Which {_term}?" + Environment.NewLine + Answers.Numbered(_matches, p =>
            $"{p.Description}{(string.IsNullOrWhiteSpace(p.PackSize) ? "" : " " + p.PackSize)} - "
            + $"{Answers.Show(p.RetailPrice)} - {Answers.Quantity(p.QuantityOnHand)} on hand"
            + (p.Schedule >= 3 ? " (pharmacist only)" : ""));
    }

    private async Task<Reply> ProductAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("Which product?");

        if (Answers.IsOffList(answer, _matches.Count)) return Reply.Retry($"Choose 1 to {_matches.Count}.");

        var chosen = _matches.Count > 0 ? Answers.Pick(answer, _matches, p => p.Description) : null;

        if (chosen is null)
        {
            var scanned = await _products.FindAsync(answer.Trim(), ct);
            chosen = scanned.IsSuccess ? scanned.Value : null;
        }

        if (chosen is null)
        {
            _term = answer.Trim();
            return new Reply.Jump("product");
        }

        // Schedule 3 and up is never sold at the till: the sale service
        // refuses it, because it goes out against a script (ADM-DEF-11).
        if (chosen.Schedule >= 3)
            return Reply.Retry($"{chosen.Description} is schedule {chosen.Schedule}, pharmacist only. It is dispensed "
                               + "against a script, not sold at the till - ask Ask PharmaX to dispense it instead. "
                               + "Choose something else, or say cancel.");

        _product = chosen;
        return Reply.Ok();
    }

    private bool SoldInUnits => _product?.UnitsPerPack is > 1m;

    private string UnitWord => Directions.UnitOf(_product?.PackSize, _product?.Description ?? "") switch
    {
        "capsule" => "capsules",
        "ml" => "ml",
        _ => "tablets"
    };

    private async Task<string> QuantityPromptAsync(CancellationToken ct)
    {
        var name = _product!.Description;

        if (_askedQuantity is decimal asked)
        {
            if (SoldInUnits)
                return $"{name}: {Answers.Quantity(asked)} packs or {Answers.Quantity(asked)} {UnitWord}? "
                       + $"(a pack is {Answers.Quantity(_product.UnitsPerPack!.Value)})";

            // Sold only by the pack: rung up at the quantity asked, with
            // nothing to ask - unless the sale refuses it.
            var rung = await RingUpAsync(asked, ct);
            if (rung is Reply.Taken taken)
            {
                Say(taken.Note);
                return "";
            }

            _askedQuantity = null;
            return Join((rung as Reply.Again)?.Why, $"{name}: how many?");
        }

        return SoldInUnits
            ? $"{name}: how many? Say packs or {UnitWord}, e.g. \"2 packs\" or \"10 {UnitWord}\"."
            : $"{name}: how many?";
    }

    private async Task<Reply> QuantityAsync(string answer, CancellationToken ct)
    {
        var words = Answers.Words(answer);
        var number = Answers.Number(answer) ?? _askedQuantity;

        if (number is not > 0m) return Reply.Retry("How many, more than nought?");

        var inUnits = SoldInUnits && words.Any(w => w.StartsWith("tab", StringComparison.Ordinal)
            || w.StartsWith("cap", StringComparison.Ordinal) || w.StartsWith("unit", StringComparison.Ordinal)
            || w == "ml" || w == "loose");
        var inPacks = words.Any(w => w.StartsWith("pack", StringComparison.Ordinal) || w.StartsWith("box", StringComparison.Ordinal)
            || w.StartsWith("bottle", StringComparison.Ordinal));

        if (SoldInUnits && _askedQuantity is not null && !inUnits && !inPacks)
            return Reply.Retry("Packs or " + UnitWord + "?");

        var quantity = inUnits ? Math.Round(number.Value / _product!.UnitsPerPack!.Value, 4) : number.Value;

        return await RingUpAsync(quantity, ct, inUnits ? $"{Answers.Quantity(number.Value)} {UnitWord} of" : null);
    }

    /// <summary>
    /// The line onto the sale, opening it with the first item. Done now, as
    /// the till does, so stock and price come back from the sale service.
    /// </summary>
    private async Task<Reply> RingUpAsync(decimal quantity, CancellationToken ct, string? said = null)
    {
        if (_saleId is null)
        {
            var opened = await _sales.OpenAsync(ct);
            if (opened.IsFailure) return Reply.Finish(opened.Error.Message);
            _saleId = opened.Value.Id;
        }

        var added = await _sales.AddLineAsync(_saleId.Value, _product!.Code, quantity, ct);
        if (added.IsFailure) return Reply.Retry(added.Error.Message);

        _askedQuantity = null;
        await RefreshAsync(ct);

        return Reply.Ok($"Rung up {said ?? Answers.Quantity(quantity) + " x"} {_product.Description}: "
                        + $"{Answers.Show(added.Value.LineTotal)}. Total so far {Answers.Show(Total)}.");
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var sale = await _sales.GetAsync(_saleId!.Value, ct);
        if (sale.IsSuccess) _sale = sale.Value;
    }

    private Reply More(string answer)
    {
        if (Answers.IsSkip(answer) || Answers.IsNo(answer) || Answers.Words(answer).Contains("done")) return Reply.Ok();

        // "and 3 panado": the quantity rides along with the name.
        _askedQuantity = Answers.Number(answer);
        _term = System.Text.RegularExpressions.Regex.Replace(answer, @"^\s*(and\s+)?\d+(\.\d+)?\s*(x\s+)?", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        return new Reply.Jump("product");
    }

    // ---- Who for ----------------------------------------------------------

    private async Task<string> CustomerPromptAsync(CancellationToken ct)
    {
        if (_customerSaid is not null)
        {
            var said = _customerSaid;
            _customerSaid = null;
            var reply = await CustomerAsync(said, ct);
            if (reply is Reply.Taken taken)
            {
                Say(taken.Note);
                return "";
            }

            if (reply is Reply.Again again) Say(again.Why);
        }

        return _accountMatches.Count > 1
            ? "Which account?" + Environment.NewLine + Answers.Numbered(_accountMatches,
                a => $"{a.Name} ({a.AccountNumber}), {Answers.Show(a.Available)} available")
            : "Customer? Name a patient's account, or say cash.";
    }

    private async Task<Reply> CustomerAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer) || Answers.Words(answer).Any(w => w is "cash" or "walk" or "walkin"))
            return Reply.Ok();

        var chosen = _accountMatches.Count > 1 ? Answers.Pick(answer, _accountMatches, a => a.Name) : null;

        if (chosen is null)
        {
            var found = await _accounts.SearchAsync(answer.Trim(), ct);
            _accountMatches = found.IsSuccess ? found.Value.Take(MostMatches).ToList() : Array.Empty<AccountDto>();

            if (_accountMatches.Count == 0)
                return Reply.Retry($"No account matches \"{answer.Trim()}\". A patient without an account pays cash "
                                   + "here; say cash, or another name.");
            if (_accountMatches.Count > 1) return new Reply.Jump("customer");

            chosen = _accountMatches[0];
        }

        var assigned = await _sales.AssignAccountAsync(_saleId!.Value, chosen.Id, ct);
        if (assigned.IsFailure) return Reply.Retry(assigned.Error.Message);

        _account = assigned.Value;
        _accountMatches = Array.Empty<AccountDto>();
        await RefreshAsync(ct);

        return Reply.Ok($"On account {_account.AccountNumber}, {_account.Name}: {Answers.Show(_account.Available)} "
                        + "available.");
    }

    // ---- Discount ---------------------------------------------------------

    private string DiscountPrompt() =>
        $"Total {Answers.Show(Total)}. Any discount? An amount or a percentage (Enter for none). Your limit is "
        + $"{_user.DiscountLimitPercent.ToString("0.##", CultureInfo.InvariantCulture)}%.";

    private async Task<Reply> DiscountAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Ok();

        if (Answers.Number(answer.Replace("%", "", StringComparison.Ordinal)) is not decimal n || n <= 0m)
            return Reply.Retry("Say an amount, e.g. 1.50, or a percentage, e.g. 10%.");

        var lines = _sale!.Lines.Where(l => !l.IsVoided && !l.IsScript).ToList();
        var gross = lines.Sum(l => l.Quantity * l.UnitPrice);
        if (gross <= 0m) return Reply.Ok();

        var percent = answer.Contains('%', StringComparison.Ordinal) ? n : n / gross * 100m;
        if (percent >= 100m) return Reply.Retry("A discount can't be the whole sale.");

        Guid? authoriser = null;

        if (percent > _user.DiscountLimitPercent && !_permissions.Has(PermissionCode.SaleDiscountAboveLimit))
        {
            authoriser = await _prompts.AuthoriseAsync(PermissionCode.SaleDiscountAboveLimit,
                $"A discount of {percent.ToString("0.#", CultureInfo.InvariantCulture)}% is over "
                + $"{_user.Username}'s limit of {_user.DiscountLimitPercent:0.##}%. A supervisor must authorise it.", ct);

            if (authoriser is null)
                return Reply.Retry("Not authorised, so no discount over your limit. A smaller one, or Enter for none?");
        }

        // Spread over the lines in proportion, as the sale only discounts lines.
        foreach (var line in lines)
        {
            var amount = Math.Round(line.Quantity * line.UnitPrice * percent / 100m, 2);
            if (amount <= 0m) continue;

            var applied = await _sales.ApplyLineDiscountAsync(_saleId!.Value, line.LineNo, amount, authoriser, ct);
            if (applied.IsFailure) return Reply.Retry(applied.Error.Message);
        }

        await RefreshAsync(ct);
        _discount = _sale!.Totals.Discount;
        return Reply.Ok($"Discount {Answers.Show(_discount)}.");
    }

    // ---- Payment ----------------------------------------------------------

    private async Task<string> TenderPromptAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        return $"Total {Answers.Show(Total)}. How is it paid? Cash, card, EcoCash, account, or split "
               + "(e.g. \"10 cash, rest card\")?";
    }

    private static string? TenderOf(string word) => word switch
    {
        "cash" => "Cash",
        "card" or "swipe" or "visa" or "mastercard" => "Card",
        "ecocash" or "eco" => "EcoCash",
        "account" or "acc" => "Account",
        "eft" or "transfer" => "Eft",
        _ => null
    };

    private Reply Tender(string answer)
    {
        _payments.Clear();
        var parts = answer.Split(',', ';', '+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var left = Total;

        foreach (var part in parts)
        {
            var tender = Answers.Words(part).Select(TenderOf).FirstOrDefault(t => t is not null);
            if (tender is null) return Reply.Retry($"I can't tell how \"{part}\" is paid. Cash, card, EcoCash or account?");

            if (tender == "Account" && _account is null)
                return Reply.Retry("The sale isn't on an account. Pay another way, or say cancel and start again.");

            var said = Answers.Words(part).Contains("rest") || parts.Count == 1 ? null : Answers.Money(part);
            var amount = said is decimal a ? Math.Min(a, left) : left;

            _payments.Add(new PaymentDto(tender, amount, null));
            left -= amount;
        }

        if (left > 0.004m)
            return Reply.Retry($"That leaves {Answers.Show(left)} unpaid. Say how the rest is paid.");

        return Reply.Ok();
    }

    private Reply Tendered(string answer)
    {
        if (Answers.Money(answer) is not decimal cash) return Reply.Retry("How much cash was handed over?");
        if (cash < CashDue) return Reply.Retry($"That is short: {Answers.Show(CashDue)} is due in cash.");

        _tendered = cash;
        return Reply.Ok($"Change: {Answers.Show(cash - CashDue)}.");
    }

    /// The payments as finalised: cash at what was handed over, so the
    /// sale works out the change.
    public IReadOnlyList<PaymentDto> Payments() =>
        _payments.Select(p => p.TenderType == "Cash" && _tendered > 0m ? p with { Amount = _tendered } : p).ToList();

    public string Summary()
    {
        var text = new StringBuilder("The sale:");

        foreach (var line in _sale?.Lines.Where(l => !l.IsVoided) ?? Enumerable.Empty<SaleLineDto>())
            text.Append(
                $"{Environment.NewLine}{line.Description} {Answers.Quantity(line.Quantity)} x {Answers.Show(line.UnitPrice)}"
                + (line.Discount > 0m ? $" less {Answers.Show(line.Discount)}" : "")
                + $" = {Answers.Show(line.LineTotal)}");

        if (_account is not null)
            text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Account: {_account.Name} ({_account.AccountNumber})");

        text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Total: {Answers.Show(Total)}");

        if (TakesPayment)
        {
            text.Append($"{Environment.NewLine}Paid: "
                + string.Join(", ", _payments.Select(p => $"{p.TenderType} {Answers.Show(p.Amount)}")));

            if (CashDue > 0m)
                text.Append(CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}Tendered: {Answers.Show(_tendered)}, change {Answers.Show(_tendered - CashDue)}");
        }

        return text.ToString();
    }

    private async Task<Reply> CompleteAsync(CancellationToken ct)
    {
        if (_saleId is not Guid id) return Reply.Finish("Nothing was rung up.");

        if (!TakesPayment)
        {
            var held = await _sales.HoldAsync(id, ct);
            if (held.IsFailure) return Reply.Retry(held.Error.Message);

            Result = new GuidedResult(AppTaskKind.Sell, id, "");
            return Reply.Finish("Held for the cashier: they recall it with F9 on the till and take payment there.");
        }

        var finalised = await _sales.FinaliseAsync(id, Payments(), ct);
        if (finalised.IsFailure) return Reply.Retry($"It was not completed: {finalised.Error.Message}");

        Result = new GuidedResult(AppTaskKind.Sell, id, finalised.Value.DocumentNumber);
        return Reply.Finish($"Sale {finalised.Value.DocumentNumber} complete, {Answers.Show(finalised.Value.Total)}."
                            + (finalised.Value.Change > 0m ? $" Change: {Answers.Show(finalised.Value.Change)}." : "")
                            + " Printing the receipt.");
    }
}

using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Controls;

/// <summary>
/// How each kind of record reads in the suggestions under a search box,
/// written once so a patient looks the same wherever one is looked up.
/// </summary>
public static class Suggest
{
    /// <summary>
    /// A patient as the counter looks one up: surname first, then the
    /// first names, then what tells two of the same name apart.
    /// </summary>
    public static Suggestion Patient(PatientListItemDto patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        return new Suggestion(patient.Id,
            $"{patient.Surname}, {patient.FirstNames}",
            Details(patient.FileNumber, patient.Telephone,
                    OneLine(patient.Address)));
    }

    public static Suggestion Account(AccountDto account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new Suggestion(account.Id, account.Name,
            Details(account.AccountNumber,
                    $"owes {Format.Money(account.Balance)}",
                    account.IsOnHold ? "on hold" : null));
    }

    public static Suggestion Product(ProductListItemDto product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new Suggestion(product.Code, product.Description,
            Details(product.Code, Format.Money(product.RetailPrice),
                    $"{Format.Quantity(product.QuantityOnHand)} on hand"));
    }

    /// <summary>
    /// A product as a script is taken down against it: what it is, and
    /// then everything the pharmacist checks before writing it down - its
    /// code, how it is packed, what a pack costs, how much is on the
    /// shelf, and whether it is dispensary stock.
    /// </summary>
    /// <remarks>
    /// The value is the product itself rather than its code, so the screen
    /// that takes it can show those details beside the item as well.
    /// </remarks>
    public static Suggestion ProductInFull(ProductListItemDto product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new Suggestion(product, product.Description,
                              ProductDetails(product));
    }

    /// <summary>
    /// "1850   ·   TAB 100   ·   $12.00 a pack   ·   8.7 on hand   ·
    /// schedule 3": the details of a product on one line.
    /// </summary>
    /// <param name="withCode">
    /// False beside a code already on show, where it would say it twice.
    /// </param>
    public static string ProductDetails(ProductListItemDto product,
                                        bool withCode = true)
    {
        ArgumentNullException.ThrowIfNull(product);

        var pack = !string.IsNullOrWhiteSpace(product.PackSize)
            ? product.PackSize
            : product.UnitsPerPack is decimal units
                ? $"{Format.Quantity(units)} per pack"
                : null;

        return Details(withCode ? product.Code : null, pack,
                   $"{Format.Money(product.RetailPrice)} a pack",
                   $"{Format.Quantity(product.QuantityOnHand)} on hand",
                   product.Schedule >= 3
                       ? $"schedule {product.Schedule}"
                       : "over the counter")!;
    }

    /// <summary>
    /// Products for a script's items: every active product, dispensary
    /// stock included, each with its details.
    /// </summary>
    /// <remarks>
    /// Looked up in a scope of their own, as the scan bars' are.
    /// </remarks>
    /// <summary>
    /// Doctors and clinics from the Prescribers list, by name, code or
    /// practice number, with the practice number beside each.
    /// </summary>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Prescribers(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var found = await scope.ServiceProvider
                .GetRequiredService<PharmaX.Application.Reference.IReferenceRepository>()
                .ListAsync(PharmaX.Application.Reference.ReferenceKind.Prescriber, term,
                           includeInactive: false, ct);

            return found.Take(8)
                .Select(p => new Suggestion(p, p.Name, p.Detail ?? p.Code))
                .ToList();
        };
    }

    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> ProductsInFull(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var found = await scope.ServiceProvider
                .GetRequiredService<IProductService>()
                .SearchAsync(term, take: 30, ct: ct);

            return found.IsSuccess
                ? found.Value.Items
                    .Where(p => p.IsActive)
                    .Take(8)
                    .Select(ProductInFull)
                    .ToList()
                : Array.Empty<Suggestion>();
        };
    }

    /// <summary>
    /// What a code cell or a code box was given, read as a code first and
    /// as a name after: the product with that code, or else the one active
    /// product whose name has every word typed.
    /// </summary>
    /// <returns>
    /// The product, or null - with how many products the words matched,
    /// so the screen can offer the list to choose from rather than say
    /// nothing was found when several were.
    /// </returns>
    public static async Task<(ProductListItemDto? Product, int Matches)> ResolveProductAsync(
        IServiceScopeFactory scopes, string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var wanted = (text ?? "").Trim();
        if (wanted.Length == 0) return (null, 0);

        using var scope = scopes.CreateScope();

        var found = await scope.ServiceProvider
            .GetRequiredService<IProductService>()
            .SearchAsync(wanted, take: 10, ct: ct);

        if (found.IsFailure) return (null, 0);

        var active = found.Value.Items.Where(p => p.IsActive).ToList();

        var exact = active.FirstOrDefault(p =>
            string.Equals(p.Code, wanted, StringComparison.OrdinalIgnoreCase));

        if (exact is not null) return (exact, 1);

        return active.Count == 1 ? (active[0], 1) : (null, active.Count);
    }

    /// <summary>
    /// A script by its number, then whose it is, when it was written and
    /// what is on it. The value is the script itself.
    /// </summary>
    public static Suggestion Script(Application.Dispensing.Dtos.ScriptDto script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return new Suggestion(script, script.ScriptNumber,
            Details(script.Patient,
                script.PrescribedOn.ToString("dd/MM/yyyy",
                    System.Globalization.CultureInfo.CurrentCulture),
                script.Items.Replace(Environment.NewLine, ", ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Every patient's scripts whose number has what is typed, newest
    /// first. Nothing for someone who may not read scripts.
    /// </summary>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Scripts(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            try
            {
                var found = await scope.ServiceProvider
                    .GetRequiredService<Application.Dispensing.IScriptService>()
                    .FindAsync(term, capturedOn: null, ct);

                return found.IsSuccess
                    ? found.Value.Take(8).Select(Script).ToList()
                    : Array.Empty<Suggestion>();
            }
            catch (Application.Security.PermissionDeniedException)
            {
                return Array.Empty<Suggestion>();
            }
        };
    }

    /// <summary>
    /// The past year's receipts whose number or cashier has what is
    /// typed, newest first; the value is the receipt number.
    /// </summary>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Receipts(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var today = DateOnly.FromDateTime(DateTime.Today);
            var page = await scope.ServiceProvider
                .GetRequiredService<Application.Sales.IReceiptHistoryService>()
                .PageAsync(new Application.Sales.ReceiptQuery(today.AddYears(-1), today,
                    term, IncludeHidden: false, Skip: 0, Take: 8), ct);

            return page.Items
                .Select(r => new Suggestion(r.DocumentNumber, r.DocumentNumber,
                    Details(r.OccurredAtLocal.ToString("dd/MM/yyyy HH:mm",
                                System.Globalization.CultureInfo.CurrentCulture),
                            r.Operator, Format.Money(r.Total))))
                .ToList();
        };
    }

    /// <summary>
    /// Branches from the Branches list, by code or name; the value is
    /// the branch's code.
    /// </summary>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Branches(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var found = await scope.ServiceProvider
                .GetRequiredService<PharmaX.Application.Reference.IReferenceRepository>()
                .ListAsync(PharmaX.Application.Reference.ReferenceKind.Branch, term,
                           includeInactive: false, ct);

            return found.Take(8)
                .Select(b => new Suggestion(b.Code, b.Name, Details(b.Code, b.Detail)))
                .ToList();
        };
    }

    /// <summary>
    /// Patients by name, file number or telephone, for a box that takes
    /// a name; the value is the patient.
    /// </summary>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Patients(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var found = await scope.ServiceProvider
                .GetRequiredService<IPatientService>()
                .SearchAsync(term, ct: ct);

            return found.IsSuccess
                ? found.Value.Take(8)
                    .Select(p => Patient(p) with { Value = p })
                    .ToList()
                : Array.Empty<Suggestion>();
        };
    }

    /// <summary>
    /// A supplier as an order is raised to one: its name, then its code,
    /// who to speak to and the telephone.
    /// </summary>
    public static Suggestion Supplier(Application.Suppliers.Dtos.SupplierListItemDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return new Suggestion(supplier.Id, supplier.Name,
            Details(supplier.Code, supplier.ContactName, supplier.Telephone));
    }

    /// <summary>
    /// The active suppliers whose name, code, contact or telephone has
    /// every word typed.
    /// </summary>
    /// <remarks>
    /// Matched in memory: a pharmacy buys from tens of suppliers, not
    /// thousands, and the supplier list has no search of its own.
    /// </remarks>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Suppliers(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var found = await scope.ServiceProvider
                .GetRequiredService<Application.Suppliers.ISupplierService>()
                .ListAsync(includeInactive: false, ct);

            return found.IsSuccess
                ? found.Value
                    .Where(s => Application.Abstractions.SearchTerms.Matches(term,
                        s.Name, s.Code, s.ContactName, s.Telephone))
                    .Take(8)
                    .Select(Supplier)
                    .ToList()
                : Array.Empty<Suggestion>();
        };
    }

    /// <summary>
    /// The active product a code belongs to, or null: for a code typed
    /// straight in rather than chosen from the suggestions.
    /// </summary>
    public static async Task<ProductListItemDto?> FindProductAsync(
        IServiceScopeFactory scopes, string code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var wanted = (code ?? "").Trim();
        if (wanted.Length == 0) return null;

        using var scope = scopes.CreateScope();

        var found = await scope.ServiceProvider
            .GetRequiredService<IProductService>()
            .SearchAsync(wanted, take: 10, ct: ct);

        return found.IsSuccess
            ? found.Value.Items.FirstOrDefault(p => p.IsActive
                && string.Equals(p.Code, wanted, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>
    /// Products for a box with no list of its own: the till's and the
    /// dispensary's scan bars, a stock adjustment's code.
    /// </summary>
    /// <param name="overTheCounter">
    /// Only what may be sold without a script, for the till: schedule 3
    /// and above is refused there, so offering it is offering a refusal.
    /// </param>
    /// <remarks>
    /// Looked up in a scope of their own. The screen's database context
    /// may be halfway through writing a sale when the typing pauses, and
    /// one context cannot answer two questions at once.
    /// </remarks>
    public static Func<string, CancellationToken,
        Task<IReadOnlyList<Suggestion>>> Products(IServiceScopeFactory scopes,
            bool overTheCounter = false)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        return async (term, ct) =>
        {
            using var scope = scopes.CreateScope();

            var products = scope.ServiceProvider
                .GetRequiredService<IProductService>();

            // More than are shown, so the ones left out do not leave the
            // list short.
            var found = await products.SearchAsync(term, take: 30, ct: ct);

            return found.IsSuccess
                ? found.Value.Items
                    .Where(p => p.IsActive)
                    .Where(p => !overTheCounter || p.Schedule < 3)
                    .Take(8)
                    .Select(Product)
                    .ToList()
                : Array.Empty<Suggestion>();
        };
    }

    /// <summary>
    /// Whether what was typed is a name rather than a code being scanned.
    /// </summary>
    /// <remarks>
    /// A scanner sends a barcode's digits and its own Enter within a few
    /// milliseconds. Nothing is offered for digits alone, so a list never
    /// opens under a scan in progress.
    /// </remarks>
    public static bool IsName(string text) =>
        (text ?? "").Any(char.IsLetter);

    /// <summary>
    /// Moves a list to the row a suggestion named, and the keyboard with
    /// it. False where the row is not on the list.
    /// </summary>
    public static bool SelectRow(DataGridView grid, Func<object?, bool> match)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(match);

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!match(row.DataBoundItem)) continue;

            var cell = row.Cells.Cast<DataGridViewCell>()
                .FirstOrDefault(c => c.Visible);

            if (cell is null) return false;

            grid.CurrentCell = cell;
            grid.Focus();
            return true;
        }

        return false;
    }

    /// The parts that are there, separated evenly.
    public static string? Details(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p))
                           .Select(p => p!.Trim())
                           .ToList();

        return present.Count == 0 ? null : string.Join("   ·   ", present);
    }

    /// An address typed over several lines, on one.
    public static string? OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : string.Join(", ", text.Split('\n', '\r')
                .Select(p => p.Trim()).Where(p => p.Length > 0));
}

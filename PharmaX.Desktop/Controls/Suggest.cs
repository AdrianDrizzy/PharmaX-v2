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
            Details(Format.FileNumber(patient.FileNumber), patient.Telephone,
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

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
            var found = await products.SearchAsync(term, take: 30, ct);

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

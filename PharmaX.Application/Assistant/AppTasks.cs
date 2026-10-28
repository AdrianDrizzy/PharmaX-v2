using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant;

public enum AppTaskKind
{
    NewProduct,
    NewPatient,
    Sell,
    ReceiveStock,
    PurchaseOrder,
    Dispense
}

/// <summary>
/// A job asked of Ask PharmaX in plain words, read far enough to open the
/// right screen already filled in.
/// </summary>
/// <param name="Item">The product named, if any: "panado".</param>
/// <param name="Quantity">How many, where a number was given.</param>
/// <param name="Person">The patient or new product's name, or the supplier.</param>
public sealed record AppTask(AppTaskKind Kind, string? Item = null,
                             decimal? Quantity = null, string? Person = null);

/// <summary>
/// Reads "sell 2 panado", "add a patient called John Moyo", "dispense
/// amoxicillin for Mary Banda" and the like.
/// </summary>
/// <remarks>
/// Patterns rather than the model: the request names the job in a handful
/// of words, the answer must be right every time, and it comes back at
/// once rather than after a few seconds of the model thinking. Nothing is
/// ever saved from here - the screen opens filled in and the operator
/// finishes it, so every permission and check on that screen still holds.
/// </remarks>
public static partial class AppTasks
{
    public static AppTask? Read(string request)
    {
        var text = Regex.Replace((request ?? "").Trim().TrimEnd('.', '!', '?'), @"\s+", " ");
        if (text.Length == 0) return null;

        // Polite openings change nothing: "can you please sell...".
        text = Politeness().Replace(text, "");

        if (NewPatient().Match(text) is { Success: true } patient)
            return new AppTask(AppTaskKind.NewPatient, Person: Clean(patient.Groups["name"].Value));

        if (NewProduct().Match(text) is { Success: true } product)
            return new AppTask(AppTaskKind.NewProduct, Item: Clean(product.Groups["name"].Value));

        if (Dispense().Match(text) is { Success: true } dispense)
            return new AppTask(AppTaskKind.Dispense, Clean(dispense.Groups["item"].Value),
                Number(dispense.Groups["qty"].Value), Clean(dispense.Groups["person"].Value));

        if (PurchaseOrder().Match(text) is { Success: true } order)
            return new AppTask(AppTaskKind.PurchaseOrder, Clean(order.Groups["item"].Value),
                Number(order.Groups["qty"].Value), Clean(order.Groups["supplier"].Value));

        if (Receive().Match(text) is { Success: true } receive)
            return new AppTask(AppTaskKind.ReceiveStock, Person: Clean(receive.Groups["supplier"].Value));

        if (Sell().Match(text) is { Success: true } sell)
            return new AppTask(AppTaskKind.Sell, Clean(sell.Groups["item"].Value),
                Number(sell.Groups["qty"].Value), Clean(sell.Groups["person"].Value));

        return null;
    }

    /// "Opening a new sale with 2 x panado." - said before the screen opens.
    public static string Describe(AppTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var what = task.Quantity is decimal q && task.Item is not null
            ? $"{q.ToString("0.##", CultureInfo.CurrentCulture)} x {task.Item}"
            : task.Item;

        return task.Kind switch
        {
            AppTaskKind.NewPatient => "Opening a new patient" + With(task.Person, "for"),
            AppTaskKind.NewProduct => "Opening a new product" + With(task.Item, "called"),
            AppTaskKind.Sell => "Opening the till" + With(what, "with"),
            AppTaskKind.ReceiveStock => "Opening goods receipt" + With(task.Person, "for a delivery from"),
            AppTaskKind.PurchaseOrder => "Opening a new purchase order" + With(what, "for"),
            AppTaskKind.Dispense => "Opening dispensing" + With(task.Person, "for")
                                    + (what is null ? "" : $", then {what}"),
            _ => "Opening the screen"
        } + ". Check it and finish it there - nothing is saved until you do.";
    }

    private static string With(string? value, string word) =>
        string.IsNullOrWhiteSpace(value) ? "" : $" {word} {value}";

    private static string? Clean(string value)
    {
        var cleaned = Leading().Replace(value.Trim(' ', ',', '.', '"', '\''), "").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static decimal? Number(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;

    [GeneratedRegex(@"^(please\s+|can you\s+|could you\s+|i want to\s+|i need to\s+|help me\s+|let's\s+)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Politeness();

    [GeneratedRegex(@"^(a|an|the|some|called|named|for|of)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Leading();

    // "add a patient called John Moyo", "register new patient Mary Banda"
    [GeneratedRegex(@"^(add|create|register|capture|new)\b.*?\bpatient\b\s*(called|named|:)?\s*(?<name>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NewPatient();

    // "add a new product called Panado 500mg", "create product Vitamin C"
    [GeneratedRegex(@"^(add|create|capture|new)\b.*?\b(product|item|stock item)\b\s*(called|named|:)?\s*(?<name>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NewProduct();

    // "dispense 30 amoxicillin 500mg for Mary Banda", "dispense for John Moyo"
    [GeneratedRegex(@"^dispense\b\s*(?<qty>\d+(\.\d+)?)?\s*(x\s+)?(?<item>.*?)(\s*\b(for|to)\s+(?<person>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Dispense();

    // "create a purchase order for 20 panado from Pharmanova", "order 10 x ibuprofen"
    [GeneratedRegex(@"^((create|raise|make|new|start)\s+(an?\s+)?)?(purchase order|po|order)\b\s*(for\s+)?(?<qty>\d+(\.\d+)?)?\s*(x\s+)?(?<item>.*?)(\s*\bfrom\s+(?<supplier>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PurchaseOrder();

    // "receive stock from Pharmanova", "book in a delivery"
    [GeneratedRegex(@"^(receive|receiving|book in|capture)\b.*?\b(stock|delivery|goods|order|invoice)\b(.*?\bfrom\s+(?<supplier>.+))?$|^(receive|book in)\s+(a\s+)?delivery$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Receive();

    // "sell 2 panado", "create a sale for 3 x ibuprofen to John Moyo", "ring up vitamin c"
    [GeneratedRegex(@"^((create|make|start|new|open)\s+(an?\s+)?(sale|sell)\b\s*(for|of)?|sell|ring up)\s*(?<qty>\d+(\.\d+)?)?\s*(x\s+)?(?<item>.*?)(\s*\bto\s+(?<person>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sell();
}

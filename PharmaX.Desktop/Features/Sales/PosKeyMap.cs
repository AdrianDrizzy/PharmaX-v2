namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The function keys the till uses, in one place. Named constants rather
/// than a switch full of Keys.F4, because the labels on the buttons and
/// the keys that trigger them must not be able to drift apart.
/// </summary>
public static class PosKeyMap
{
    public const Keys Search = Keys.F2;

    /// <summary>
    /// Puts the sale on a patient's account.
    /// </summary>
    /// <remarks>
    /// F3 because it is free at the till and sits next to Find item on
    /// the bar. It is the Sell shortcut on the shell's menu, but the
    /// shell is not open while the till is.
    /// </remarks>
    public const Keys Customer = Keys.F3;
    public const Keys Quantity = Keys.F4;
    public const Keys LineDiscount = Keys.F5;
    public const Keys Payment = Keys.F6;
    public const Keys Close = Keys.F7;
    public const Keys Hold = Keys.F8;

    /// Takes a held basket back up, at any till.
    public const Keys Recall = Keys.F9;

    /// Brings an issued quotation onto the sale.
    public const Keys Quotation = Keys.F10;

    /// Prints the last receipt again, for the customer who needs a copy
    /// or the printer that jammed.
    public const Keys Reprint = Keys.F12;

    public const Keys Lock = Keys.F1;
    public const Keys NoSale = Keys.F11;
    public const Keys VoidLine = Keys.Escape;

    /// <summary>
    /// The keys with a button on the bar along the foot. The others have
    /// theirs where they belong, so the bar's buttons stay wide enough to
    /// read on a small till screen and nothing is hidden behind a menu:
    /// Customer beside the customer's name, Void line on the line itself
    /// (right-click, or Delete), and Reprint, Lock and Close in the header.
    /// </summary>
    public static IReadOnlySet<Keys> OnBar { get; } = new HashSet<Keys>
    {
        Search, Quantity, LineDiscount, Payment, Hold, Recall, Quotation, NoSale
    };

    /// The keys whose buttons sit in the header, in the order shown.
    public static IReadOnlyList<Keys> InHeader { get; } = new[] { Reprint, Lock, Close };

    /// <summary>The label for a key, as the bar and its buttons show it.</summary>
    public static string LabelFor(Keys key) => Bar.First(b => b.Key == key).Label;

    /// <summary>
    /// What the function bar shows, in the order it shows it: key order,
    /// so that the bar reads the way the keyboard is laid out.
    /// </summary>
    public static IReadOnlyList<(Keys Key, string Label)> Bar { get; } = new[]
    {
        (Search, "F2 Find item"),
        (Customer, "F3 Customer"),
        (Quantity, "F4 Quantity"),
        (LineDiscount, "F5 Discount"),
        (Payment, "F6 Pay"),
        (Close, "F7 Close"),
        (Hold, "F8 Hold"),
        (Recall, "F9 Recall"),
        (Quotation, "F10 Quotation"),
        (Reprint, "F12 Reprint"),
        (Lock, "F1 Lock"),
        (NoSale, "F11 No sale"),
        (VoidLine, "Esc Void line")
    };
}

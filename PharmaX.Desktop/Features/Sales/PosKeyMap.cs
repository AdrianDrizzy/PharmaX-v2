namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The function keys the till uses, in one place. Named constants rather
/// than a switch full of Keys.F4, because the labels on the buttons and
/// the keys that trigger them must not be able to drift apart.
/// </summary>
public static class PosKeyMap
{
    public const Keys Quantity = Keys.F4;
    public const Keys LineDiscount = Keys.F5;
    public const Keys Payment = Keys.F6;
    public const Keys Close = Keys.F7;
    public const Keys Hold = Keys.F8;
    public const Keys VoidLine = Keys.Escape;

    /// What the function bar shows, in the order it shows it.
    public static IReadOnlyList<(Keys Key, string Label)> Bar { get; } = new[]
    {
        (Quantity, "F4 Quantity"),
        (LineDiscount, "F5 Discount"),
        (Payment, "F6 Pay"),
        (Hold, "F8 Hold"),
        (Close, "F7 Close"),
        (VoidLine, "Esc Void line")
    };
}

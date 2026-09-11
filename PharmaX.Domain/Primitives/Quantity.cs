using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Primitives;

/// <summary>
/// A dispensed or sold amount. Held to three decimal places because a
/// pharmacy splits packs: 0.5 of a 100ml bottle is a real line, and
/// rounding it to a whole number is a dispensing error.
/// </summary>
public readonly record struct Quantity
{
    public decimal Value { get; }
    public string Unit { get; }

    private Quantity(decimal value, string unit)
    {
        Value = value;
        Unit = unit;
    }

    public static Quantity Of(decimal value, string unit = "ea")
    {
        if (value < 0m)
            throw new DomainException("Quantity cannot be negative.");

        return new Quantity(Math.Round(value, 3, MidpointRounding.AwayFromZero),
                            unit);
    }

    public static Quantity Zero(string unit = "ea") => Of(0m, unit);

    public bool IsZero => Value == 0m;

    public static Quantity operator +(Quantity a, Quantity b)
    {
        Assert(a, b);
        return Of(a.Value + b.Value, a.Unit);
    }

    public static Quantity operator -(Quantity a, Quantity b)
    {
        Assert(a, b);
        return Of(a.Value - b.Value, a.Unit);
    }

    private static void Assert(Quantity a, Quantity b)
    {
        if (a.Unit != b.Unit)
            throw new DomainException(
                $"Cannot combine a quantity in {a.Unit} with one in {b.Unit}.");
    }

    public override string ToString() => $"{Value:0.###} {Unit}";

    public static Quantity Add(Quantity left, Quantity right) => left + right;
    public static Quantity Subtract(Quantity left, Quantity right) => left - right;
}

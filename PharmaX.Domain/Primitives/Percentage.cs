using PharmaX.Domain.Exceptions;

namespace PharmaX.Domain.Primitives;

/// <summary>
/// A rate expressed out of one hundred. Exists so that a method taking a
/// discount cannot be handed 0.15 when it wanted 15.
/// </summary>
public readonly record struct Percentage
{
    public decimal Value { get; }

    private Percentage(decimal value) => Value = value;

    public static Percentage Of(decimal value)
    {
        if (value < 0m)
            throw new DomainException("A percentage cannot be negative.");

        return new Percentage(Math.Round(value, 4, MidpointRounding.AwayFromZero));
    }

    public static readonly Percentage Zero = new(0m);

    public decimal AsFraction => Value / 100m;

    public Money AppliedTo(Money amount) => amount.Percentage(Value);

    public override string ToString() => $"{Value:0.##}%";
}

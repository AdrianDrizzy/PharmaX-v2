using System.Globalization;

namespace PharmaX.Domain.Primitives;

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>
    /// The currency this installation trades in, and the code stamped on
    /// every amount that does not name one.
    /// </summary>
    /// <remarks>
    /// Mutable static state, deliberately, and set exactly once at
    /// start-up from the configured currency. A pharmacy trades in one
    /// currency; threading that one value through every call site that
    /// builds a Money would put a parameter on several hundred methods
    /// to express something that never varies within a run. Money still
    /// carries the code on every instance and still refuses to add two
    /// currencies together, so the invariant is enforced where it
    /// matters rather than assumed here.
    /// </remarks>
    public static string DefaultCurrency { get; private set; } = "USD";

    public static void UseCurrency(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3)
            return;

        DefaultCurrency = code.Trim().ToUpperInvariant();
    }

    public static Money Of(decimal amount, string? currency = null)
    {
        var code = currency ?? DefaultCurrency;

        if (code.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter code.");

        return new Money(Round(amount), code.ToUpperInvariant());
    }

    public static Money Zero(string? currency = null) => Of(0m, currency);

    // Rounding happens here and nowhere else. Half away from zero, minor
    // unit (specification Section 6.4).
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static Money operator +(Money a, Money b)
    {
        Assert(a, b);
        return new Money(Round(a.Amount + b.Amount), a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        Assert(a, b);
        return new Money(Round(a.Amount - b.Amount), a.Currency);
    }

    public static Money operator *(Money m, decimal factor) =>
        new(Round(m.Amount * factor), m.Currency);

    public Money Percentage(decimal percent) => this * (percent / 100m);

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    private static void Assert(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException(
                $"Cannot combine {a.Currency} with {b.Currency}.");
    }

    // Current culture on purpose: InvariantGlobalization is false so that
    // money formats to the workstation locale (specification Section 17.5).
    public override string ToString() =>
        Amount.ToString("N2", CultureInfo.CurrentCulture);

    // Named alternatives to the operators, so that a caller in a language
    // without operator overloading is not locked out.
    public static Money Add(Money left, Money right) => left + right;
    public static Money Subtract(Money left, Money right) => left - right;
    public static Money Multiply(Money left, decimal factor) => left * factor;
}

using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Tills;

/// <summary>
/// One note or coin the drawer can hold. Seeded per currency so that a
/// cash-up grid is data rather than a hard-coded list of South African
/// notes that breaks the first time the system crosses a border.
/// </summary>
public sealed class Denomination
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// The grid key, for example "R200" or "50c".
    public string Key { get; private set; } = "";

    public decimal Value { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;
    public bool IsNote { get; private set; }
    public int SortOrder { get; private set; }

    private Denomination() { }

    public static Denomination Create(string key, decimal value, bool isNote,
                                      int sortOrder, string? currency = null) =>
        new()
        {
            Key = key,
            Value = value,
            IsNote = isNote,
            SortOrder = sortOrder,
            Currency = currency ?? Money.DefaultCurrency
        };

    /// The drawer for one currency, in the order a drawer is counted.
    public static IEnumerable<Denomination> For(CurrencyOption currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        for (var i = 0; i < currency.Denominations.Count; i++)
        {
            var d = currency.Denominations[i];
            yield return Create(d.Key, d.Value, d.IsNote, i, currency.Code);
        }
    }

    public Money Extended(int count) => Money.Of(Value * count, Currency);
}

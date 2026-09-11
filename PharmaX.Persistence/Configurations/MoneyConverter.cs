using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PharmaX.Domain.Primitives;

namespace PharmaX.Persistence.Configurations;

/// <summary>
/// Maps a Money to a single decimal column.
/// </summary>
/// <remarks>
/// Preferred form is a two-column complex property, which keeps the
/// currency. This converter exists for the two places EF Core 8 does not
/// allow one: a nullable Money, and a Money inside an owned collection.
/// Both are inside a till declaration, which is denominated in the till's
/// own currency by definition, so nothing is lost there that the rest of
/// the model does not already know.
/// </remarks>
public sealed class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter()
        : base(money => money.Amount,
               amount => Money.Of(amount, null))
    {
    }

    // No constant here any more: the code comes from the currency the
    // installation trades in, which system setup can change.
}

public sealed class NullableMoneyConverter : ValueConverter<Money?, decimal?>
{
    public NullableMoneyConverter()
        : base(money => money == null ? null : money.Value.Amount,
               amount => amount == null
                   ? null
                   : Money.Of(amount.Value, null))
    {
    }
}

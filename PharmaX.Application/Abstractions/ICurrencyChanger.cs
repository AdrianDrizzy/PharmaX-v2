using PharmaX.Domain.Primitives;

namespace PharmaX.Application.Abstractions;

/// <summary>
/// Switches the currency this installation trades in.
/// </summary>
/// <remarks>
/// Three things have to move together or the till breaks: the code
/// stamped on amounts created from now on, the notes and coins the
/// cash-up grid offers, and the code on every amount already stored.
/// Miss the last and the first cash-up that mixes an old sale with a
/// new payment throws, because Money refuses to add two currencies.
/// </remarks>
public interface ICurrencyChanger
{
    /// <summary>
    /// Applies the currency across the data. The presentation layer
    /// changes its own formatting; that is not this interface's job.
    /// </summary>
    Task ApplyAsync(CurrencyOption currency, CancellationToken ct = default);
}

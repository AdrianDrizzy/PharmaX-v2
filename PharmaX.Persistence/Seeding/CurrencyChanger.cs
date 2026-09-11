using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Tills;

namespace PharmaX.Persistence.Seeding;

/// <inheritdoc />
public sealed class CurrencyChanger : ICurrencyChanger
{
    private readonly PharmaXDbContext _db;
    private readonly CurrencyNormaliser _normaliser;

    public CurrencyChanger(PharmaXDbContext db, CurrencyNormaliser normaliser)
    {
        _db = db;
        _normaliser = normaliser;
    }

    public async Task ApplyAsync(CurrencyOption currency,
                                 CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // The code new amounts are stamped with, first: everything below
        // creates or compares Money.
        Money.UseCurrency(currency.Code);

        await AddDrawerAsync(currency, ct).ConfigureAwait(false);

        await _normaliser.NormaliseAsync(currency.Code, ct)
                         .ConfigureAwait(false);
    }

    /// <summary>
    /// Gives the till the drawer this currency is counted in.
    /// </summary>
    /// <remarks>
    /// The old currency's notes and coins stay on the table. A
    /// declaration already taken references them by key, and deleting
    /// them would leave a closed session's cash-up unreadable.
    /// </remarks>
    private async Task AddDrawerAsync(CurrencyOption currency,
                                      CancellationToken ct)
    {
        var existing = await _db.Denominations
            .Where(d => d.Currency == currency.Code)
            .Select(d => d.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var added = false;

        foreach (var denomination in Denomination.For(currency))
        {
            if (existing.Contains(denomination.Key, StringComparer.Ordinal))
                continue;

            _db.Denominations.Add(denomination);
            added = true;
        }

        if (added) await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

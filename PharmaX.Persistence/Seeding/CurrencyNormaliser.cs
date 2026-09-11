using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// Brings every stored amount onto the currency this installation
/// trades in.
/// </summary>
/// <remarks>
/// Money refuses to add two currencies together, and rightly so. That
/// means the moment the trading currency is changed, every figure
/// already on disk carries the old code and the first cash-up that
/// mixes an old sale with a new payment throws. This relabels them.
///
/// A relabel, not a conversion: nobody's totals move. That is the
/// correct behaviour for the case this exists to serve - a site that
/// was installed with the wrong currency set and is correcting it
/// before or early in trading - and the setup screen says so in those
/// words before it asks the operator to confirm. A pharmacy genuinely
/// changing the money it trades in needs a priced revaluation, which is
/// a different job and not one a settings screen should pretend to do.
/// </remarks>
public sealed class CurrencyNormaliser
{
    private readonly PharmaXDbContext _db;
    private readonly ILogger<CurrencyNormaliser> _log;

    public CurrencyNormaliser(PharmaXDbContext db,
                              ILogger<CurrencyNormaliser> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Relabels every currency column that disagrees with <paramref
    /// name="code"/>. Returns the number of rows touched.
    /// </summary>
    public async Task<int> NormaliseAsync(string code,
                                          CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var sql = _db.GetService<ISqlGenerationHelper>();
        var touched = 0;

        foreach (var (table, column) in CurrencyColumns())
        {
            var statement =
                $"UPDATE {sql.DelimitIdentifier(table)} "
                + $"SET {sql.DelimitIdentifier(column)} = {{0}} "
                + $"WHERE {sql.DelimitIdentifier(column)} <> {{0}}";

            touched += await _db.Database
                .ExecuteSqlRawAsync(statement, new object[] { code }, ct)
                .ConfigureAwait(false);
        }

        if (touched > 0)
            _log.LogWarning(
                "Relabelled {Rows} stored amount(s) to {Currency}. Figures "
                + "are unchanged; only the currency code they carry is.",
                touched, code);

        return touched;
    }

    /// <summary>
    /// Every column that holds a currency code, read off the model
    /// rather than listed by hand.
    /// </summary>
    /// <remarks>
    /// Listing them would mean this class silently stopped covering the
    /// next entity somebody gives a Money to, and the failure would
    /// surface as an exception at a till months later.
    ///
    /// Complex types are walked as well as entities, and that is the
    /// whole job rather than a detail: a Money is mapped as a complex
    /// property almost everywhere in PharmaX, and those columns do not
    /// appear in an entity type's own properties. Reading only the top
    /// level found four columns out of thirty and left every price
    /// behind.
    /// </remarks>
    private IEnumerable<(string Table, string Column)> CurrencyColumns()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in _db.Model.GetEntityTypes())
        {
            // A denomination's currency says which drawer it belongs to,
            // not what an amount is worth. Relabelling those would leave
            // a till counting rand notes under a dollar heading.
            if (entity.ClrType == typeof(Domain.Tills.Denomination))
                continue;

            var table = entity.GetTableName();
            if (table is null) continue;

            var identifier = StoreObjectIdentifier.Table(
                table, entity.GetSchema());

            foreach (var column in CurrencyColumnsOf(entity, identifier))
            {
                if (seen.Add($"{table}.{column}"))
                    yield return (table, column);
            }
        }
    }

    private static IEnumerable<string> CurrencyColumnsOf(ITypeBase type,
        StoreObjectIdentifier identifier)
    {
        foreach (var property in type.GetProperties())
        {
            if (property.ClrType != typeof(string)) continue;

            var column = property.GetColumnName(identifier);

            if (column is not null
                && column.EndsWith("Currency", StringComparison.Ordinal))
                yield return column;
        }

        foreach (var complex in type.GetComplexProperties())
        {
            foreach (var column in CurrencyColumnsOf(complex.ComplexType,
                                                     identifier))
                yield return column;
        }
    }
}

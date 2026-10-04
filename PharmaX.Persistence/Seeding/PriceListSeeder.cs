using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// Loads the pharmacy's own price list into the product catalogue.
/// </summary>
/// <remarks>
/// The list is an embedded CSV rather than several hundred lines of C#,
/// because it is data and it changes: the next list the wholesaler sends
/// replaces the file and the seeder adds its new codes on the next
/// start-up. Codes are the identity, so a product that is already there
/// is not added twice, and its prices and description are the
/// pharmacy's: the list never overwrites them.
///
/// Stock is deliberately untouched. A price list says what things cost,
/// not what is on the shelf, and a seeder that reset quantities every
/// morning would wipe out the counting the staff had just done.
///
/// What the list says about a pack - how many units it holds - is
/// written where a product has nothing of its own yet, and never over
/// something the pharmacy set on the product form.
/// </remarks>
public sealed class PriceListSeeder
{
    private const string ResourceName =
        "PharmaX.Persistence.Seeding.Data.pricelist.csv";

    private readonly PharmaXDbContext _db;
    private readonly ILogger<PriceListSeeder> _log;

    public PriceListSeeder(PharmaXDbContext db, ILogger<PriceListSeeder> log)
    {
        _db = db;
        _log = log;
    }

    /// <param name="CostsToCheck">
    /// Products whose cost does not look like the cost of the pack they
    /// are priced as (see Product.CostNeedsChecking).
    /// </param>
    /// <param name="StockWithoutBatch">
    /// Dispensary products holding stock on no batch, which stay untracked
    /// until somebody puts that stock on a batch.
    /// </param>
    public sealed record Outcome(int Added, int Updated, int Unchanged,
                                 int Skipped, int CostsToCheck = 0,
                                 int StockWithoutBatch = 0)
    {
        public int Total => Added + Updated + Unchanged + Skipped;
    }

    public async Task<Outcome> SeedAsync(CancellationToken ct = default)
    {
        var rows = ReadEmbedded().ToList();

        if (rows.Count == 0)
        {
            _log.LogWarning("The embedded price list is empty or missing.");
            return new Outcome(0, 0, 0, 0);
        }

        var codes = rows.Select(r => r.Code).ToList();

        var existing = await _db.Products
            .Where(p => codes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        // Products that have never moved and never had a batch are still
        // as this list left them, so how they are tracked is still the
        // list's to say. Anything that has traded is the pharmacy's.
        var traded = (await _db.StockMovements
                .Select(m => m.ProductId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Concat(await _db.Batches
                .Select(b => b.ProductId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        int added = 0, updated = 0, unchanged = 0, skipped = 0;
        var unbatched = 0;

        foreach (var row in rows)
        {
            if (row.SellingPrice <= 0m)
            {
                // A product with no selling price cannot be sold, and
                // guessing one at a till is how a pharmacy gives stock
                // away. Left out, and counted so the log says how many.
                skipped++;
                continue;
            }

            if (existing.TryGetValue(row.Code, out var product))
            {
                if (Update(product, row, traded.Contains(product.Id)))
                    updated++;
                else
                    unchanged++;

                if (row.RequiresPharmacist && !product.IsBatchTracked
                    && product.QuantityOnHand > 0m)
                    unbatched++;

                continue;
            }

            _db.Products.Add(Create(row));
            added++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var toCheck = await CostsToCheckAsync(codes, ct).ConfigureAwait(false);

        var outcome = new Outcome(added, updated, unchanged, skipped,
                                  toCheck, unbatched);

        _log.LogInformation(
            "Price list: {Added} added, {Updated} updated, {Unchanged} "
            + "unchanged, {Skipped} skipped of {Total}.",
            outcome.Added, outcome.Updated, outcome.Unchanged,
            outcome.Skipped, outcome.Total);

        if (outcome.CostsToCheck > 0)
            _log.LogWarning(
                "Price list: {Count} product(s) have a cost of nothing, under "
                + "a tenth of the price, or at or above it - most likely the "
                + "cost of one unit against the price of a pack. Reports > "
                + "Inventory > Cost Prices to Check lists them.",
                outcome.CostsToCheck);

        if (outcome.StockWithoutBatch > 0)
            _log.LogWarning(
                "Price list: {Count} dispensary product(s) hold stock on no "
                + "batch, so their labels carry no batch or expiry. Put the "
                + "stock on a batch from the product list to track it.",
                outcome.StockWithoutBatch);

        return outcome;
    }

    /// The list's products whose cost needs a second look, counted.
    private async Task<int> CostsToCheckAsync(IReadOnlyList<string> codes,
                                              CancellationToken ct) =>
        (await _db.Products
            .AsNoTracking()
            .Where(p => codes.Contains(p.Code))
            .ToListAsync(ct)
            .ConfigureAwait(false))
        .Count(p => p.CostNeedsChecking);

    private static Product Create(PriceListRow row)
    {
        var product = Product.Create(
            code: row.Code,
            description: row.FullDescription,
            retailPrice: Money.Of(row.SellingPrice),

            // The dispensary is prescription stock, so it is held behind
            // the dispensing screen rather than sellable at the till.
            // Deliberately the cautious way round: refusing to sell a
            // prescription over the counter is a recoverable annoyance,
            // and selling one is not. Adjustable per product afterwards.
            schedule: row.RequiresPharmacist ? 3 : 0,

            // Dispensary stock is batch-tracked: its label has to carry
            // the batch and the expiry, FEFO has to choose what goes in
            // the bag, and the alert centre has to see it running out of
            // date. Untracked, none of that happened and the label said
            // nothing of either (OCT-DEF-02). It is dispensed once it has
            // been received against a batch, which is how prescription
            // stock arrives. Counter stock stays untracked: the list
            // brings no stock, and a tracked line with no batch could not
            // be sold until somebody received it.
            isBatchTracked: row.RequiresPharmacist);

        product.ChangeCostPrice(Money.Of(row.CostPrice));

        if (row.PackDescription.Length > 0)
            product.SetPackSize(row.PackDescription);

        if (row.UnitsPerPack is decimal units)
            product.SetUnitsPerPack(units);

        return product;
    }

    /// <summary>
    /// Fills in what an existing product is missing from the list. Returns
    /// true where something actually changed, so the log can distinguish
    /// an update from a no-op.
    /// </summary>
    /// <param name="traded">
    /// Whether the product has ever moved stock or held a batch, after
    /// which how it is tracked is the pharmacy's decision, not the list's.
    /// </param>
    private static bool Update(Product product, PriceListRow row, bool traded)
    {
        var changed = false;

        // How many tablets, capsules or millilitres the priced pack holds.
        // The list has always said - "TAB 100" - and none of it reached
        // the product, so a script for thirty tablets was read as thirty
        // packs: refused against ten on the shelf, and charged as thirty
        // had there been enough (OCT-DEF-01). Filled in where the product
        // has none; a count set on the product form is left alone.
        if (product.UnitsPerPack is null && row.UnitsPerPack is decimal units)
        {
            product.SetUnitsPerPack(units);
            changed = true;
        }

        // Dispensary lines the list loaded untracked before it tracked
        // them, and that nobody has traded since: tracked now, as a new
        // one would be. Stock already on the shelf with no batch is left
        // as it is - a batch number and expiry cannot be made up - and
        // counted for the log.
        if (row.RequiresPharmacist && !product.IsBatchTracked && !traded
            && product.QuantityOnHand == 0m)
        {
            product.SetBatchTracked(true);
            changed = true;
        }

        // Prices and the name are deliberately not touched. This runs at
        // every start-up, so a list that overwrote them would put back
        // every price the pharmacy changed on the product form the next
        // time the till was switched on.
        // ponytail: a new wholesaler list only prices new codes; reprice
        // existing ones through a deliberate import if that is ever needed.

        return changed;
    }

    private static IEnumerable<PriceListRow> ReadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName);

        if (stream is null) yield break;

        using var reader = new StreamReader(stream);

        // The header names the columns; the reader only needs to skip it.
        _ = reader.ReadLine();

        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0) continue;

            var field = Split(line);
            if (field.Length < 8) continue;

            var code = field[0].Trim();
            if (code.Length == 0) continue;

            yield return new PriceListRow(
                Code: code,
                Description: field[1].Trim(),
                Strength: field[2].Trim(),
                Unit: field[3].Trim(),
                PackSize: field[4].Trim(),
                CostPrice: Amount(field[5]),
                SellingPrice: Amount(field[6]),
                Department: field[7].Trim());
        }
    }

    /// <summary>
    /// Splits one CSV line, honouring quoted fields.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than pulling CsvHelper into the persistence
    /// layer for one embedded file. Product names carry commas and the
    /// odd pair of quotes, which is the whole of what this has to cope
    /// with.
    /// </remarks>
    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (quoted)
            {
                if (ch != '"') { value.Append(ch); continue; }

                // A doubled quote inside a quoted field is one quote.
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',')
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else value.Append(ch);
        }

        fields.Add(value.ToString());
        return fields.ToArray();
    }

    private static decimal Amount(string text) =>
        decimal.TryParse(text, NumberStyles.Number,
                         CultureInfo.InvariantCulture, out var value)
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
            : 0m;

    private sealed record PriceListRow(string Code, string Description,
        string Strength, string Unit, string PackSize, decimal CostPrice,
        decimal SellingPrice, string Department)
    {
        /// The name as a pharmacist reads it: product then strength.
        public string FullDescription => Strength.Length > 0
            ? $"{Description} {Strength}"
            : Description;

        /// "TAB 1000", or just the form where the pack is a single.
        public string PackDescription =>
            PackSize is "1" or "" ? Unit : $"{Unit} {PackSize}".Trim();

        /// <summary>
        /// The units the priced pack holds - 100 for "TAB 100" - or null
        /// where the pack is a single, which is dispensed as itself.
        /// </summary>
        public decimal? UnitsPerPack =>
            decimal.TryParse(PackSize, NumberStyles.Number,
                             CultureInfo.InvariantCulture, out var units)
            && units > 1m
                ? units
                : null;

        public bool RequiresPharmacist =>
            Department.Equals("DISPENSARY", StringComparison.OrdinalIgnoreCase);
    }
}

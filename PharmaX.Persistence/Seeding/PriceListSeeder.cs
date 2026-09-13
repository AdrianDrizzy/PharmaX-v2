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
/// replaces the file and the seeder brings the changes in on the next
/// start-up. Codes are the identity, so a product that is already there
/// has its description and prices brought up to date rather than being
/// added twice.
///
/// Stock is deliberately untouched. A price list says what things cost,
/// not what is on the shelf, and a seeder that reset quantities every
/// morning would wipe out the counting the staff had just done.
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

    public sealed record Outcome(int Added, int Updated, int Unchanged,
                                 int Skipped)
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

        int added = 0, updated = 0, unchanged = 0, skipped = 0;

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
                if (Update(product, row)) updated++; else unchanged++;
                continue;
            }

            _db.Products.Add(Create(row));
            added++;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var outcome = new Outcome(added, updated, unchanged, skipped);

        _log.LogInformation(
            "Price list: {Added} added, {Updated} repriced, {Unchanged} "
            + "unchanged, {Skipped} skipped of {Total}.",
            outcome.Added, outcome.Updated, outcome.Unchanged,
            outcome.Skipped, outcome.Total);

        return outcome;
    }

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

            // Not batch-tracked on the way in. A batch-tracked product
            // with no batches cannot be sold at all, and this list
            // brings in no stock, so switching it on here would leave
            // the whole catalogue unsellable until someone received it.
            isBatchTracked: false);

        product.ChangeCostPrice(Money.Of(row.CostPrice));

        if (row.PackDescription.Length > 0)
            product.SetPackSize(row.PackDescription);

        return product;
    }

    /// <summary>
    /// Brings an existing product into line with the list. Returns true
    /// where something actually changed, so the log can distinguish a
    /// reprice from a no-op.
    /// </summary>
    private static bool Update(Product product, PriceListRow row)
    {
        var changed = false;

        if (product.RetailPrice.Amount != row.SellingPrice)
        {
            product.ChangeRetailPrice(Money.Of(row.SellingPrice));
            changed = true;
        }

        if (product.CostPrice.Amount != row.CostPrice)
        {
            product.ChangeCostPrice(Money.Of(row.CostPrice));
            changed = true;
        }

        if (!string.Equals(product.Description, row.FullDescription,
                           StringComparison.Ordinal))
        {
            product.Rename(row.FullDescription);
            changed = true;
        }

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

        public bool RequiresPharmacist =>
            Department.Equals("DISPENSARY", StringComparison.OrdinalIgnoreCase);
    }
}

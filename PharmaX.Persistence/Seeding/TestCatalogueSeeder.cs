using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// A small shelf of stock, two suppliers and two patients, so that the
/// selling, dispensing, receiving and counting paths can be walked on a
/// fresh machine.
/// </summary>
/// <remarks>
/// Gated on the same Development:SeedTestUsers flag as the test
/// accounts, for the same reason: none of this may exist at a site.
/// Seeded accounts with nothing to sell let a tester sign in and then
/// stop, which is half a test environment.
/// </remarks>
public sealed class TestCatalogueSeeder
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<TestCatalogueSeeder> _log;

    public TestCatalogueSeeder(PharmaXDbContext db, PharmaXOptions options,
                               IClock clock,
                               ILogger<TestCatalogueSeeder> log)
    {
        _db = db;
        _options = options;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Code, description, retail price, schedule, batch tracking and the
    /// units on the shelf. The mix is deliberate: schedule 0 items sell
    /// straight over the counter, schedule 4 items force the dispensing
    /// path, and the batch-tracked ones make FEFO allocation reachable.
    /// </summary>
    private static readonly (string Code, string Description, decimal Price,
        int Schedule, bool Batched, decimal Quantity, string? Barcode)[]
        Catalogue =
        {
            ("PAN500", "Panado 500mg tablets 20s", 34.95m, 0, false, 120m,
                "6001234000017"),
            ("DIS200", "Disprin 300mg tablets 24s", 28.50m, 0, false, 90m,
                "6001234000024"),
            ("VITC1G", "Vitamin C 1000mg effervescent 10s", 49.99m, 0, false,
                60m, "6001234000031"),
            ("ALLERG", "Allergex 4mg tablets 30s", 42.00m, 2, false, 45m,
                "6001234000048"),
            ("COUGH1", "Cough syrup 100ml", 76.00m, 2, true, 40m,
                "6001234000055"),
            ("AMOX50", "Amoxicillin 500mg capsules 21s", 118.40m, 4, true,
                36m, null),
            ("METF85", "Metformin 850mg tablets 60s", 154.75m, 4, true, 24m,
                null),
            ("INSUL1", "Insulin pen refill 3ml", 412.30m, 4, true, 12m, null),
            ("PLAST1", "Adhesive plasters assorted 40s", 39.90m, 0, false,
                80m, "6001234000062"),
            ("SURGM1", "Surgical face masks 50s", 89.00m, 0, false, 30m,
                "6001234000079")
        };

    private static readonly (string Code, string Name, int Terms, int Lead)[]
        Suppliers =
        {
            ("UPD", "United Pharmaceutical Distributors", 30, 1),
            ("ALPHA", "Alpha Pharm Wholesalers", 60, 2)
        };

    private static readonly (string File, string Surname, string Names,
        int BirthYear)[] Patients =
        {
            ("P000001", "Ndlovu", "Thandiwe", 1984),
            ("P000002", "Van Wyk", "Johan", 1957)
        };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_options.Development.SeedTestUsers) return;

        var added = await SeedSuppliersAsync(ct).ConfigureAwait(false);
        added += await SeedProductsAsync(ct).ConfigureAwait(false);
        added += await SeedPatientsAsync(ct).ConfigureAwait(false);

        if (added == 0) return;

        _log.LogWarning(
            "Development:SeedTestUsers is on: added {Count} demonstration "
            + "records (products, stock, suppliers, patients). This must "
            + "never be enabled at a site.", added);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> SeedSuppliersAsync(CancellationToken ct)
    {
        var existing = await _db.Suppliers.Select(s => s.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        var added = 0;

        foreach (var (code, name, terms, lead) in Suppliers)
        {
            if (existing.Contains(code, StringComparer.Ordinal)) continue;

            _db.Suppliers.Add(Supplier.Create(code, name, terms, lead));
            added++;
        }

        return added;
    }

    /// <summary>
    /// Each batch-tracked product gets two batches with different expiry
    /// dates, because one batch never proves that FEFO picked the right
    /// one.
    /// </summary>
    private async Task<int> SeedProductsAsync(CancellationToken ct)
    {
        var existing = await _db.Products.Select(p => p.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var added = 0;

        foreach (var item in Catalogue)
        {
            if (existing.Contains(item.Code, StringComparer.Ordinal))
                continue;

            var product = Product.Create(item.Code, item.Description,
                Money.Of(item.Price), schedule: item.Schedule,
                isBatchTracked: item.Batched);

            if (item.Barcode is not null) product.AddBarcode(item.Barcode);

            product.ChangeCostPrice(Money.Of(
                Math.Round(item.Price * 0.62m, 2,
                           MidpointRounding.AwayFromZero)));

            product.SetReorderLevel(Math.Ceiling(item.Quantity / 4m));
            product.SetQuantityOnHand(item.Quantity);

            _db.Products.Add(product);
            added++;

            if (!item.Batched) continue;

            // The near batch holds a third of the stock and expires
            // first, so a sale of more than a third has to draw from
            // both and the allocation is visible on the receipt.
            var near = Math.Round(item.Quantity / 3m, 0,
                                  MidpointRounding.AwayFromZero);

            _db.Batches.Add(Batch.Receive(product.Id,
                $"{item.Code}-A", today.AddMonths(4), near,
                product.CostPrice, now));

            _db.Batches.Add(Batch.Receive(product.Id,
                $"{item.Code}-B", today.AddMonths(14),
                item.Quantity - near, product.CostPrice, now));

            added += 2;
        }

        return added;
    }

    private async Task<int> SeedPatientsAsync(CancellationToken ct)
    {
        var existing = await _db.Patients.Select(p => p.FileNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        var added = 0;

        foreach (var (file, surname, names, year) in Patients)
        {
            if (existing.Contains(file, StringComparer.Ordinal)) continue;

            _db.Patients.Add(Patient.Create(file, surname, names,
                new DateOnly(year, 6, 15)));

            added++;
        }

        return added;
    }
}

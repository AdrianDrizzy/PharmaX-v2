using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Import;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Common;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Persistence.Import;

/// <summary>
/// Loads another system's data into PharmaX, each record into the table
/// PharmaX keeps it in.
/// </summary>
/// <remarks>
/// <para>
/// Departments, suppliers, products with their prices, VAT, schedule and
/// batches, patients with their medical aid, and prescribers become
/// PharmaX's own records. The trading history becomes PharmaX's own
/// history too: prescriptions are scripts and the dispensings made
/// against them, till receipts are sales with their lines and tenders,
/// refunds are returns against the sale they refunded, cash-ups are till
/// sessions and their declarations, deliveries are goods receipts, cycle
/// counts are stocktakes, and every one of them is in the stock ledger.
/// Price changes and till overrides go to the audit trail, where
/// PharmaX's own are kept.
/// </para>
/// <para>
/// History is recorded, not replayed. Nothing posts to the books - they
/// start at go-live - and nothing moves the stock on the shelf, which is
/// what the files say is there now. The ledger is written backwards from
/// that figure, so its "on hand after" column ends where the shelf is.
/// </para>
/// <para>
/// Quantities may come in units - tablets, millilitres - against a price
/// for a pack: where a pack size is given they are divided by it on the
/// way in, because PharmaX counts packs. A product held at two pack sizes
/// becomes two products, since PharmaX prices one pack per product.
/// </para>
/// <para>
/// Safe to run again. Records are found by their code and updated. The
/// history and the stock come in only while the database has none of its
/// own - no sales, dispensings, deliveries, counts or till sessions - so
/// a second run, or a run into a pharmacy that is already trading,
/// updates the lists and leaves the trading alone. A dry run does all of
/// it inside a transaction and rolls it back, so its report is the real
/// one.
/// </para>
/// </remarks>
public sealed partial class DataImporter : IDataImporter
{
    private const string Opening = "OPENING";
    private const string NotRecorded = "Not recorded";

    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;
    private readonly IDocumentNumbering _numbering;
    private readonly PharmaXOptions _options;
    private readonly ILogger<DataImporter> _log;

    public DataImporter(PharmaXDbContext db, IClock clock, IPasswordHasher hasher,
        IDocumentNumbering numbering, PharmaXOptions options, ILogger<DataImporter> log)
    {
        _db = db;
        _clock = clock;
        _hasher = hasher;
        _numbering = numbering;
        _options = options;
        _log = log;
    }

    public async Task<ImportResult> ImportAsync(IReadOnlyList<string> paths, bool dryRun,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var report = new ImportReport(dryRun, string.Join("; ", paths));

        void Step(string text)
        {
            progress?.Report(text);
            _log.LogInformation("Data import: {Step}", text);
        }

        Step("Reading the files");
        var source = ImportSource.Read(paths);
        var tables = source.Tables.Select(t => (Table: t, Kind: ImportTables.Recognise(t))).ToList();

        foreach (var (table, kind) in tables)
            report.Source(Describe(kind), table.Source,
                kind is TableKind.Unknown or TableKind.Summary ? null : table.Rows.Count);

        foreach (var file in source.Unread) report.Note("Not read: " + file);

        foreach (var (table, kind) in tables.Where(t => t.Kind is TableKind.Summary or TableKind.Unknown))
            report.Note(kind == TableKind.Summary
                ? $"{table.Name}: totals worked out from other tables, not stored - PharmaX's reports work them out again from what is imported."
                : $"{table.Name}: not recognised as anything PharmaX keeps ({string.Join(", ", table.Columns.Take(8))}{(table.Columns.Count > 8 ? ", ..." : "")}).");

        foreach (var (table, _) in tables.Where(t => t.Kind == TableKind.BranchStock))
            report.Note($"{table.Name}: stock by branch, not stored - PharmaX keeps one figure per product, taken from the stock on the shelf.");

        if (!tables.Any(t => t.Kind is not (TableKind.Unknown or TableKind.Summary
                                             or TableKind.Controls or TableKind.BranchStock)))
            return report.Fail("Nothing in what was chosen is a table PharmaX can import: "
                               + "a product list, patients, prescribers, suppliers, sales, "
                               + "prescriptions, stock or its movements.");

        IReadOnlyList<SourceRow> Rows(TableKind kind) =>
            tables.Where(t => t.Kind == kind).SelectMany(t => t.Table.Rows).ToList();

        var controls = Controls(Rows(TableKind.Controls));

        foreach (var (table, kind) in tables.Where(t => t.Kind is not (TableKind.Unknown
                     or TableKind.Summary or TableKind.Controls)))
            CheckRead(table, controls, report);

        var traded = await HasTradedAsync(ct);
        var now = _clock.UtcNow;

        if (traded)
            report.Note("This database already holds sales, dispensings, deliveries, counts or "
                        + "till sessions, so the lists were brought up to date and the stock and "
                        + "history were left as they are.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var run = new Run(now, report, traded);

            Step("Departments and suppliers");
            await DepartmentsAsync(run, Rows(TableKind.Departments), ct);
            await SuppliersAsync(run, Rows(TableKind.Suppliers), ct);
            await SaveAsync(ct);

            Step("Products, prices and stock");
            await ProductsAsync(run, Rows(TableKind.Products), Rows(TableKind.Stock),
                Rows(TableKind.SaleLines), controls, ct);
            await SaveAsync(ct);

            Step("Patients");
            await PatientsAsync(run, Rows(TableKind.Patients), ct);
            await SaveAsync(ct);

            Step("Prescribers");
            await PrescribersAsync(run, Rows(TableKind.Prescribers), ct);
            await SaveAsync(ct);

            if (!traded)
                await HistoryAsync(run, Rows, Step, ct);

            if (dryRun)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                Step("Dry run finished: nothing was kept");
            }
            else
            {
                await tx.CommitAsync(ct);
                Step("Import finished");
            }
        }
#pragma warning disable CA1031 // The report carries the failure; the transaction undoes it.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _log.LogError(ex, "Data import failed");
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return report.Fail("The import stopped and nothing was kept: "
                               + (ex.InnerException?.Message ?? ex.Message));
        }

        return report.Done();
    }

    /// <summary>What one import run has brought in so far, by code.</summary>
    private sealed class Run
    {
        public Run(DateTime now, ImportReport report, bool traded)
        {
            Now = now;
            Report = report;
            Traded = traded;
        }

        public DateTime Now { get; }
        public ImportReport Report { get; }
        public bool Traded { get; }

        /// By the file's id for a department, by its code and by its name.
        public Dictionary<string, Guid> Departments { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DepartmentNames { get; } = new();

        /// By code and by name.
        public Dictionary<string, Guid> Suppliers { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// By the file's code, and by "code|pack" for a second pack size.
        public Dictionary<string, ProductRef> Products { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// By "member|dependant" and by file number.
        public Dictionary<string, Guid> Patients { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// By username and by display name.
        public Dictionary<string, Guid> Staff { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// "product|batch number" to the batch on the shelf.
        public Dictionary<string, Guid> Batches { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> NewStaff { get; } = new();
        public List<string> NewProducts { get; } = new();
    }

    /// <summary>A product as the history needs it.</summary>
    private sealed record ProductRef(Guid Id, string Code, string Description,
        decimal Vat, string TaxCode, decimal UnitsInPack, decimal OnHand);

    private async Task<bool> HasTradedAsync(CancellationToken ct) =>
        await _db.Sales.AnyAsync(ct) || await _db.Dispensings.AnyAsync(ct)
        || await _db.GoodsReceipts.AnyAsync(ct) || await _db.Stocktakes.AnyAsync(ct)
        || await _db.TillSessions.AnyAsync(ct);

    private async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private static string Describe(TableKind kind) => kind switch
    {
        TableKind.Departments => "Departments",
        TableKind.Suppliers => "Suppliers",
        TableKind.Products => "Products",
        TableKind.Stock => "Stock on the shelf",
        TableKind.BranchStock => "Stock by branch (not stored)",
        TableKind.Patients => "Patients",
        TableKind.Prescribers => "Prescribers",
        TableKind.Prescriptions => "Prescription lines",
        TableKind.SaleLines => "Till sale lines",
        TableKind.Receipts => "Till receipts",
        TableKind.StockMovements => "Stock movements",
        TableKind.StockCounts => "Stock counts",
        TableKind.PriceChanges => "Price changes",
        TableKind.PriceOverrides => "Till price overrides",
        TableKind.CashUps => "Cash-ups",
        TableKind.ZReads => "Z-reads",
        TableKind.Controls => "The files' own totals",
        TableKind.Summary => "Summary (not stored)",
        _ => "Not recognised"
    };

    // ---- Departments and suppliers ---------------------------------------

    private async Task DepartmentsAsync(Run run, IReadOnlyList<SourceRow> rows, CancellationToken ct)
    {
        var existing = await _db.Departments.ToDictionaryAsync(d => d.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var d in existing.Values) Remember(run, d);

        var seen = 0;

        foreach (var row in rows)
        {
            var id = row.Clean(F.DeptId);
            var name = row.Clean(F.DeptName) ?? (id is null ? null : $"Department {id}");
            if (name is null) continue;

            var code = Limit(name.ToUpperInvariant(), 30)!;

            if (existing.TryGetValue(code, out var department))
                department.Rename(Title(name)!, department.Detail);
            else
            {
                department = Department.Create(code, Title(name)!);
                department.CreatedAtUtc = Since(row);
                _db.Departments.Add(department);
                existing[code] = department;
            }

            Remember(run, department);
            if (id is not null) run.Departments[id] = department.Id;
            seen++;
        }

        run.Report.Count("Departments", seen);
    }

    private static void Remember(Run run, Department department)
    {
        run.Departments[department.Code] = department.Id;
        run.Departments[department.Name] = department.Id;
        run.DepartmentNames[department.Id.ToString()] = department.Code;
    }

    /// The department a product names, by the file's id, code or name;
    /// added where the files name one their department list does not hold.
    private Guid? DepartmentFor(Run run, string? key)
    {
        if (key is null) return null;
        if (run.Departments.TryGetValue(key, out var id)) return id;

        if (key.All(char.IsDigit)) return null;     // an id nobody listed

        var department = Department.Create(Limit(key.ToUpperInvariant(), 30)!, Title(key)!);
        _db.Departments.Add(department);
        Remember(run, department);
        return department.Id;
    }

    private async Task SuppliersAsync(Run run, IReadOnlyList<SourceRow> rows, CancellationToken ct)
    {
        var existing = await _db.Suppliers.ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var s in existing.Values)
        {
            run.Suppliers[s.Code] = s.Id;
            run.Suppliers.TryAdd(s.Name, s.Id);
        }

        var seen = 0;

        foreach (var row in rows)
        {
            var code = row.Clean(F.SupplierCode)?.ToUpperInvariant();
            var name = Limit(row.Clean(F.SupplierName), 150);
            code ??= name is null ? null : Limit(name.ToUpperInvariant(), 20);
            if (code is null) continue;

            name ??= $"Supplier {code}";

            if (existing.TryGetValue(code, out var supplier)) supplier.Rename(name);
            else
            {
                supplier = Supplier.Create(code, name);
                supplier.CreatedAtUtc = Since(row);
                _db.Suppliers.Add(supplier);
                existing[code] = supplier;
            }

            supplier.UpdateContact(Limit(row.Clean(F.Contact), 150) ?? supplier.ContactName,
                                   Limit(row.Clean(F.Telephone), 30) ?? supplier.Telephone,
                                   Limit(row.Clean(F.Email), 150) ?? supplier.Email);

            if (row.Clean(F.Account) is string account) supplier.SetAccountNumber(Limit(account, 50));

            run.Suppliers[code] = supplier.Id;
            run.Suppliers.TryAdd(name, supplier.Id);
            seen++;
        }

        run.Report.Count("Suppliers", seen);
        run.Report.Review("Suppliers sharing a name (merge them under Inventory > Suppliers)",
            rows.GroupBy(r => r.Text(F.SupplierName).Trim().ToUpperInvariant())
                .Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => $"{g.Key}: codes {string.Join(", ", g.Select(r => r.Text(F.SupplierCode)))}"));
    }

    /// The supplier a delivery or a product names, by code or by name;
    /// added where the supplier list does not hold it.
    private Guid? SupplierFor(Run run, string? key)
    {
        if (key is null) return null;
        if (run.Suppliers.TryGetValue(key, out var id)) return id;

        var code = Limit(key.ToUpperInvariant(), 20)!;
        if (run.Suppliers.TryGetValue(code, out id)) return id;

        var supplier = Supplier.Create(code, Limit(key, 150)!);
        _db.Suppliers.Add(supplier);
        run.Suppliers[code] = supplier.Id;
        run.Suppliers[key] = supplier.Id;
        return supplier.Id;
    }

    // ---- Products and stock ----------------------------------------------

    private async Task ProductsAsync(Run run, IReadOnlyList<SourceRow> productRows,
        IReadOnlyList<SourceRow> stockRows, IReadOnlyList<SourceRow> salesLines,
        IReadOnlyDictionary<string, decimal> controls, CancellationToken ct)
    {
        // A product may be described by more than one table - a catalogue
        // and a stock list joined to it - so each field is taken from the
        // first table that has it.
        var catalogue = productRows
            .Where(r => r.Clean(F.ProductCode) is not null)
            .GroupBy(r => r.Text(F.ProductCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // The shelf: one row per product, pack size and batch. A product
        // list that carries quantities is the shelf too, where no table of
        // its own says otherwise.
        var shelf = new Dictionary<string, SourceRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in stockRows.Concat(productRows.Where(r => r.Text(F.Quantity).Length > 0)))
        {
            if (row.Clean(F.ProductCode) is not string code) continue;
            shelf.TryAdd($"{code}|{Pack(row):0.###}|{row.Clean(F.BatchNo)}", row);
        }

        var stock = shelf.Values
            .GroupBy(r => r.Text(F.ProductCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(Pack).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var code in stock.Keys.Where(c => !catalogue.ContainsKey(c)).ToList())
            catalogue[code] = stock[code];

        var demand = Demand(salesLines);
        var overTheCounter = salesLines
            .Where(l => l.Clean(F.ScriptNo) is null or "0")
            .Select(l => l.Text(F.ProductCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await _db.Products.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        var today = DateOnly.FromDateTime(run.Now);

        // The shelf is replaced only by files that say what is on it: a
        // price list with no quantities leaves the stock alone.
        var setsStock = !run.Traded && shelf.Count > 0;

        if (!run.Traded && !setsStock && catalogue.Count > 0)
            run.Report.Note("The files hold no stock on the shelf, so stock on hand was left as it is.");

        int added = 0, updated = 0, inactive = 0, batches = 0, extraPacks = 0;
        decimal unitsIn = 0m, negativeUnits = 0m, valueAtCost = 0m, valueAtPrice = 0m;
        var imported = new List<Guid>();
        var opening = new List<(Product Product, decimal Packs, Batch? Batch)>();
        var noPrice = new List<string>();
        var negative = new List<string>();
        var expired = new List<string>();
        var unbatched = new List<string>();
        var scheduled = new List<string>();
        var priced = new List<string>();

        foreach (var (baseCode, rows) in catalogue)
        {
            string? Pick(string[] field) => rows.Select(r => r.Clean(field)).FirstOrDefault(v => v is not null);
            decimal PickNumber(string[] field) => rows.Select(r => r.Number(field)).FirstOrDefault(v => v != 0m);

            var packs = stock.GetValueOrDefault(baseCode) ?? new List<SourceRow>();

            // One product per pack size: PharmaX prices one pack a product.
            var packGroups = packs.GroupBy(Pack).OrderBy(g => g.Key).ToList();

            for (var i = 0; i < Math.Max(1, packGroups.Count); i++)
            {
                var group = i < packGroups.Count ? packGroups[i].ToList() : new List<SourceRow>();
                var pack = i < packGroups.Count ? packGroups[i].Key : Math.Max(1m, PickNumber(F.PackSize));
                var code = i == 0 ? baseCode : $"{baseCode}-{pack:0.###}";
                if (i > 0) extraPacks++;

                // What the files say about this product. A product already in
                // PharmaX keeps every field they do not mention: a price list
                // of code, name and price changes the price and nothing else.
                bool Has(string[] field) => rows.Concat(group).Any(r => r.Text(field).Length > 0);

                var isNew = !existing.TryGetValue(code, out var product);
                var units = group.Sum(s => s.Number(F.Quantity));
                var priceRow = group.FirstOrDefault(s => s.Number(F.SellingPrice) != 0m);
                var price = priceRow?.Number(F.SellingPrice) ?? PickNumber(F.SellingPrice);
                var priceGiven = Has(F.SellingPrice);
                var cost = group.Select(s => s.Number(F.CostPrice)).FirstOrDefault(c => c != 0m);
                if (cost == 0m) cost = PickNumber(F.CostPrice);

                var name = Pick(F.ProductName) ?? product?.Description ?? code;
                var strength = Pick(F.Strength);
                var description = Limit(strength is null
                    || name.Contains(strength, StringComparison.OrdinalIgnoreCase)
                        ? name : $"{name} {strength}", 200)!;

                var departmentId = DepartmentFor(run, Pick(F.Department));
                var inDispensary = departmentId is Guid d
                    && run.DepartmentNames.GetValueOrDefault(d.ToString()) == "DISPENSARY";
                var given = Pick(F.Schedule)?.Trim().ToUpperInvariant() ?? "";
                var scheduleGiven = isNew || inDispensary || given.Length > 0;
                var soldOverCounter = !inDispensary && overTheCounter.Contains(baseCode);
                var schedule = !scheduleGiven ? product!.Schedule
                    : inDispensary ? Math.Max(3, Schedule(given)) : Schedule(given);

                // A prescription class the pharmacy nevertheless sold at the
                // till - Dettol, Listerine, gripe water - stays sellable as it
                // was, and is listed for the pharmacist to confirm rather than
                // stopped at the till on day one.
                if (scheduleGiven && schedule >= 3 && soldOverCounter)
                {
                    schedule = 2;
                    scheduled.Add($"{code} {description} (scheduled {given} in the files)");
                }

                var (vat, taxCode) = Vat(Pick(F.TaxCode));
                var withBatch = group.Where(s => s.Clean(F.BatchNo) is not null
                                                 && s.Date(F.BatchExpiry) is not null
                                                 && s.Number(F.Quantity) > 0m).ToList();
                var onHand = Math.Round(Math.Max(units, 0m) / pack, 3);

                // Stock on the shelf with no batch stays untracked: its batch
                // and expiry cannot be made up. Empty dispensary lines are
                // tracked, so the next delivery is received against a batch.
                var tracked = units > 0m
                    ? withBatch.Sum(s => s.Number(F.Quantity)) >= units
                    : schedule >= 3;

                if (isNew)
                {
                    product = Product.Create(code, description, Money.Of(Math.Max(price, 0m)), vat,
                                             taxCode, schedule, tracked);
                    product.CreatedAtUtc = rows.Concat(group).Select(Since).FirstOrDefault(d => d != default);
                    _db.Products.Add(product);
                    existing[product.Code] = product;
                    added++;
                }
                else
                {
                    if (priceGiven && product!.RetailPrice.Amount != Money.Of(price).Amount)
                        priced.Add(code);

                    product!.Rename(description);
                    if (priceGiven) product.ChangeRetailPrice(Money.Of(Math.Max(price, 0m)));
                    if (Has(F.TaxCode)) product.SetVat(vat, taxCode);
                    product.SetSchedule(schedule);
                    if (setsStock) product.SetBatchTracked(tracked);
                    updated++;
                }

                if (isNew || Has(F.CostPrice)) product.ChangeCostPrice(Money.Of(cost));

                if (isNew || group.Count > 0 || Has(F.PackSize))
                    product.SetUnitsPerPack(pack > 1m ? pack : null);

                if (isNew || group.Count > 0 || Has(F.PackSize) || Has(F.Form) || Has(F.Unit))
                    product.SetPackSize(PackText(Pick(F.Form) ?? Pick(F.Unit), pack));

                if (Has(F.GenericName)) product.SetGenericName(Limit(Pick(F.GenericName), 200));
                if (departmentId is not null) product.Classify(departmentId, product.CategoryId, product.ManufacturerId);

                // Only a supplier the files list: a code nobody listed is
                // not worth inventing a supplier for.
                if (rows.Select(r => r.Clean(F.Supplier))
                        .FirstOrDefault(s => s is not null && run.Suppliers.ContainsKey(s)) is string supplier)
                    product.SetDefaultSupplier(run.Suppliers[supplier]);

                if (Pick(F.Barcode) is string barcode && BarcodeShape().IsMatch(barcode))
                    product.ReplaceBarcodes(new[] { barcode });

                var reorder = PickNumber(F.ReorderLevel);
                decimal? reorderLevel = reorder > 0m ? Math.Ceiling(reorder / pack)
                    : demand.TryGetValue(baseCode, out var units90) ? Math.Ceiling(units90 / 90m * 14m / pack)
                    : Has(F.ReorderLevel) ? 0m : null;
                if (reorderLevel is decimal level) product.SetReorderLevel(level);

                var discontinued = Pick(F.Discontinued) is string flag
                                   && flag.ToUpperInvariant() is not ("FALSE" or "0" or "NO" or "N");
                var shelved = setsStock ? units : product.QuantityOnHand;

                if (discontinued || ((isNew || priceGiven) && price <= 0m && shelved <= 0m))
                {
                    product.Deactivate();
                    inactive++;
                }
                else if (isNew || Has(F.Discontinued) || priceGiven) product.Reactivate();

                run.Products[code] = Ref(product, setsStock ? onHand : product.QuantityOnHand);
                if (i == 0) run.Products[baseCode] = run.Products[code];
                else run.Products[$"{baseCode}|{pack:0.###}"] = run.Products[code];

                if (!setsStock) continue;

                product.SetQuantityOnHand(onHand);
                imported.Add(product.Id);
                unitsIn += onHand * pack;
                valueAtCost += onHand * cost;
                valueAtPrice += onHand * price;

                if (units < 0m)
                {
                    negativeUnits += units;
                    negative.Add($"{code} {description}: {units:0.###} units, set to 0");
                }

                if (price <= 0m && units > 0m && !discontinued)
                    noPrice.Add($"{code} {description}: {units:0.###} units on hand, no selling price");

                if (units > 0m && !tracked && schedule >= 3)
                    unbatched.Add($"{code} {description}: {onHand:0.###} packs on no batch");

                if (onHand <= 0m) continue;

                if (!tracked)
                {
                    opening.Add((product, onHand, null));
                    continue;
                }

                foreach (var batchGroup in withBatch.GroupBy(s => s.Clean(F.BatchNo)!, StringComparer.OrdinalIgnoreCase))
                {
                    var first = batchGroup.First();
                    var packsInBatch = Math.Round(batchGroup.Sum(s => s.Number(F.Quantity)) / pack, 3);
                    if (packsInBatch <= 0m) continue;

                    var expiry = DateOnly.FromDateTime(first.Date(F.BatchExpiry)!.Value);
                    var batch = Batch.Receive(product.Id, Limit(batchGroup.Key, 50)!, expiry, packsInBatch,
                        Money.Of(cost), run.Now, product.DefaultSupplierId);

                    if (expiry < today)
                        expired.Add($"{code} {description}: batch {batchGroup.Key} expired {expiry:dd/MM/yyyy}, {packsInBatch:0.###} packs");

                    opening.Add((product, packsInBatch, batch));
                    batches++;
                }
            }
        }

        // The stock on the shelf replaces whatever an earlier run left:
        // its batches and its opening movements, for these products only.
        await _db.SaveChangesAsync(ct);

        if (setsStock)
        {
            await _db.StockMovements.Where(m => m.ReasonCode == Opening).ExecuteDeleteAsync(ct);
            await _db.Batches.Where(b => imported.Contains(b.ProductId)).ExecuteDeleteAsync(ct);

            foreach (var (product, packs, batch) in opening)
            {
                if (batch is null) continue;
                _db.Batches.Add(batch);
                run.Batches[$"{product.Id}|{batch.BatchNumber}"] = batch.Id;
            }

            OpeningStock = opening.GroupBy(o => o.Product.Id)
                .ToDictionary(g => g.Key, g => g.Select(o => (o.Packs, o.Batch?.Id)).ToList());
        }

        run.Report.Count("Products added", added);
        run.Report.Count("Products updated (already in PharmaX)", updated);
        run.Report.Count("Products imported inactive (discontinued, or no price and no stock)", inactive);
        run.Report.Count("Products split out for a second pack size", extraPacks);

        if (setsStock)
        {
            run.Report.Count("Batches on the shelf", batches);
            run.Report.Count("Products with stock on hand", opening.Select(o => o.Product.Id).Distinct().Count());
            run.Report.Figure("Stock at cost (packs x pack cost)", valueAtCost);
            run.Report.Figure("Stock at selling price", valueAtPrice);

            if (stock.Count > 0)
                run.Report.Check("Stock in units (negatives counted as set to 0)",
                    shelf.Values.Sum(r => r.Number(F.Quantity)),
                    unitsIn + negativeUnits,
                    tolerance: shelf.Values.Sum(Pack) * 0.0005m + 0.01m);
        }

        run.Report.Check("Products", catalogue.Count, added + updated - extraPacks);

        if (run.Traded)
            run.Report.Review("Selling prices changed by the import", priced);

        run.Report.Review("Products with stock but no selling price (price them before selling)", noPrice);
        run.Report.Review("Negative stock in the files, set to nothing (count these)", negative);
        run.Report.Review("Expired batches still on the shelf (write them off)", expired);
        run.Report.Review("Dispensary stock on no batch (labels will carry no batch or expiry)", unbatched);
        run.Report.Review("Scheduled for dispensing in the files but sold over the counter - kept at the till; confirm or move to the dispensary (schedule 3 on the product)", scheduled);
    }

    /// Packs on the shelf by product, with their batch, for the ledger.
    private Dictionary<Guid, List<(decimal Packs, Guid? BatchId)>> OpeningStock { get; set; } = new();

    private static ProductRef Ref(Product p, decimal onHand) =>
        new(p.Id, p.Code, p.Description, p.VatRatePercent, p.TaxCode, p.UnitsInPack, onHand);

    private static decimal Pack(SourceRow row) => Math.Max(1m, row.Number(F.PackSize));

    /// Units of each product sold over the last 90 days of the files.
    private static Dictionary<string, decimal> Demand(IReadOnlyList<SourceRow> salesLines)
    {
        var sales = salesLines
            .Where(l => !IsReturn(l))
            .Select(l => (Code: l.Text(F.ProductCode), Day: l.Date(F.SaleDate), Units: l.Number(F.Quantity)))
            .Where(l => l.Day is not null)
            .ToList();

        if (sales.Count == 0) return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var from = sales.Max(l => l.Day!.Value).AddDays(-90);

        return sales.Where(l => l.Day >= from)
                    .GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.Units), StringComparer.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// A number is the schedule itself: "3", "S4", "Schedule 5". The
    /// letters are the Zimbabwe classes: PP, and the PP10 and PID
    /// variants, are prescription preparations; P is a pharmacy medicine
    /// a pharmacist may sell over the counter; a household remedy, and
    /// anything unclassified, is general sale.
    /// </remarks>
    private static int Schedule(string given)
    {
        var digits = new string(given.Where(char.IsDigit).ToArray());

        if (given is "PP" or "PP10" or "PID" or "POM" or "RX") return 3;
        if (given is "P") return 2;

        return digits.Length is 1 && int.TryParse(digits, NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out var schedule) && schedule <= 8
            ? schedule
            : 0;
    }

    /// <remarks>
    /// A rate where one is given ("15", "15%"), otherwise the code: "A",
    /// "S", "Y" or 2 is standard-rated, "Z", "E", "N" or 1 zero-rated.
    /// </remarks>
    private static (decimal Rate, string Code) Vat(string? given)
    {
        var text = (given ?? "").Trim().TrimEnd('%').ToUpperInvariant();

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 2m)
            return rate <= 100m ? (rate, "A") : (15m, "A");

        return text switch
        {
            "2" or "A" or "S" or "Y" or "YES" or "TRUE" or "STANDARD" or "V" => (15m, "A"),
            "" => (15m, "A"),
            _ => (0m, "Z")
        };
    }

    private static string? PackText(string? form, decimal pack) =>
        Limit(pack > 1m ? $"{form} {pack:0.###}".Trim() : form, 50);

    // ---- Patients and prescribers ----------------------------------------

    private async Task PatientsAsync(Run run, IReadOnlyList<SourceRow> rows, CancellationToken ct)
    {
        var existing = await _db.Patients.ToDictionaryAsync(p => p.FileNumber, StringComparer.OrdinalIgnoreCase, ct);
        var schemes = await _db.MedicalAidSchemes.ToListAsync(ct);

        foreach (var p in existing.Values) run.Patients[p.FileNumber] = p.Id;

        int added = 0, updated = 0, noSurname = 0, droppedIds = 0;
        var noMembership = new List<string>();
        var clinical = new List<string>();
        var owing = new List<string>();

        foreach (var row in rows)
        {
            var (surname, first) = Names(row);

            if (surname is null)
            {
                surname = "(no surname)";
                noSurname++;
            }

            var id = row.Clean(F.IdNumber);
            var born = row.Date(F.BirthDate) is DateTime b ? DateOnly.FromDateTime(b) : (DateOnly?)null;

            if (id is not null && !IdShape().IsMatch(id))
            {
                id = null;
                droppedIds++;
            }

            var member = row.Clean(F.Member);
            var file = member is null ? null : FileNumber(member, row.Text(F.Dependant));

            // A list with no file numbers is matched on name and birth date
            // and numbered here. Without a birth date a name says too little:
            // two John Moyos are two patients until somebody merges them.
            var patient = file is not null && existing.TryGetValue(file, out var found) ? found
                : file is null && born is not null ? existing.Values.FirstOrDefault(p => p.Surname == surname
                                     && p.FirstNames == first && p.DateOfBirth == born)
                : null;

            if (patient is not null)
            {
                patient.Amend(surname, first, id ?? patient.IdNumber, born ?? patient.DateOfBirth);
                updated++;
            }
            else
            {
                file ??= await _numbering.NextAsync(DocumentSeries.Patient, ct);
                patient = Patient.Create(file, surname, first, born, id);
                patient.CreatedAtUtc = Since(row);
                _db.Patients.Add(patient);
                existing[file] = patient;
                added++;
            }

            patient.UpdateContact(
                Limit(row.First(F.Cellphone, F.Phone, F.Landline), 30) ?? patient.Telephone,
                Limit(row.Clean(F.Email), 150) ?? patient.Email);

            if (Limit(Address(row), 250) is string address) patient.SetAddress(address);

            if (row.Clean(F.MedicalAid) is string schemeName)
            {
                var scheme = schemes.FirstOrDefault(s =>
                                 s.Code.Equals(schemeName, StringComparison.OrdinalIgnoreCase)
                                 || s.Name.Equals(schemeName, StringComparison.OrdinalIgnoreCase));

                if (scheme is null)
                {
                    scheme = MedicalAidScheme.Create(Limit(schemeName.ToUpperInvariant(), 30)!, Title(schemeName)!);
                    _db.MedicalAidSchemes.Add(scheme);
                    schemes.Add(scheme);
                }

                if (row.Clean(F.Membership) is string membership)
                    patient.SetMedicalAid(scheme.Id, Limit(membership, 40), Limit(row.Clean(F.DependantCode), 10));
                else
                    noMembership.Add($"{patient.FileNumber}: {scheme.Name}");
            }

            if (row.Clean(F.Allergies) is not null || row.Clean(F.Conditions) is not null)
                clinical.Add(patient.FileNumber);

            if (row.Number(F.Owing) > 0m)
                owing.Add($"{patient.FileNumber}: {row.Number(F.Owing):N2}");

            run.Patients[patient.FileNumber] = patient.Id;
            if (member is not null) run.Patients[$"{member}|{Dependant(row.Text(F.Dependant))}"] = patient.Id;
        }

        run.Report.Count("Patients added", added);
        run.Report.Count("Patients updated", updated);
        run.Report.Count("Patients with no surname in the files", noSurname);
        run.Report.Count("ID numbers dropped as not ID numbers", droppedIds);
        run.Report.Check("Patients", rows.Count, added + updated);
        run.Report.Review("On a medical aid with no membership number (add it under Patients)", noMembership);
        run.Report.Review("Allergies or conditions in the files (enter them on the patient's clinical profile)", clinical);
        run.Report.Review("Owing in the files (open an account and bring the balance forward)", owing);
        run.Report.Review("Possibly the same person twice - same name and phone (merge under Dispensary > Merge Patient Files)",
            rows.Where(r => r.First(F.Cellphone, F.Phone, F.Landline) is not null && r.Clean(F.Member) is not null)
                .GroupBy(r => $"{r.Text(F.FirstName).ToUpperInvariant()}|{r.Text(F.Surname).ToUpperInvariant()}|{r.First(F.Cellphone, F.Phone, F.Landline)}")
                .Where(g => g.Count() > 1)
                .Select(g => string.Join(" and ", g.Select(r => FileNumber(r.Text(F.Member), r.Text(F.Dependant))))));
    }

    /// Surname and first names, from columns of their own or one full name.
    private static (string? Surname, string First) Names(SourceRow row)
    {
        var surname = Limit(Title(row.Clean(F.Surname)), 100);
        var first = Limit(Title(row.Clean(F.FirstName)), 150);

        if (surname is null && first is null && row.Clean(F.FullName) is string full)
        {
            var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            surname = Limit(Title(words[^1]), 100);
            first = Limit(Title(string.Join(' ', words[..^1])), 150);
        }

        return (surname, first ?? "");
    }

    /// <summary>
    /// The member's number, and the dependant's after a dash: "1757" for
    /// the principal member, "1757-2" for a dependant.
    /// </summary>
    private static string FileNumber(string member, string dependant)
    {
        var dep = Dependant(dependant);
        return Limit(dep.Length == 0 ? member : $"{member}-{dep}", 30)!;
    }

    private static string Dependant(string dependant)
    {
        var dep = dependant.Trim().TrimStart('0');
        return dep is "" or "1" ? "" : dep;
    }

    private static readonly Dictionary<string, string> Towns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BYO"] = "Bulawayo", ["HRE"] = "Harare", ["JHB"] = "Johannesburg", ["CPT"] = "Cape Town"
    };

    private static string? Address(SourceRow row)
    {
        if (row.Clean(F.Address) is string address) return address;

        var town = row.Clean(F.City) is string c && Towns.TryGetValue(c, out var full) ? full : Title(row.Clean(F.City));
        var parts = new[] { Title(row.Clean(F.Street)), Title(row.Clean(F.Area)), town }
            .Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private async Task PrescribersAsync(Run run, IReadOnlyList<SourceRow> rows, CancellationToken ct)
    {
        var existing = await _db.Prescribers.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var doubled = new List<string>();

        foreach (var row in rows)
        {
            var name = string.Join(" ", new[]
            {
                Title(row.Clean(F.Title)),
                row.Clean(F.FirstName) is string first ? Title(first) : row.Clean(F.Initials)?.ToUpperInvariant(),
                Title(row.Clean(F.Surname))
            }.Where(p => !string.IsNullOrWhiteSpace(p)));

            if (name.Length == 0) name = Title(row.Clean(F.DoctorName)) ?? "";

            var code = Limit(row.Clean(F.DoctorCode)?.ToUpperInvariant()
                             ?? (name.Length == 0 ? null : name.ToUpperInvariant()), 30);
            if (code is null) continue;

            // One code given to two prescribers keeps both, the second
            // under a code of its own rather than overwriting the first.
            if (seen.TryGetValue(code, out var times))
            {
                seen[code] = times + 1;
                doubled.Add(code);
                code = $"{code}-{times + 1}";
            }
            else seen[code] = 1;

            var practice = row.Clean(F.PracticeNo);
            var detail = Limit(string.Join(" · ", new[]
            {
                practice is null ? null : $"Practice {practice}",
                row.First(F.Phone, F.Cellphone, F.Landline),
                Title(row.Clean(F.City))
            }.Where(p => p is not null)), 400);

            name = Limit(name.Length == 0 ? code : name, 200)!;

            // What the files do not say about a prescriber already on the
            // list - the practice number, the phone - stays as it was.
            if (existing.TryGetValue(code, out var prescriber)) prescriber.Rename(name, detail ?? prescriber.Detail);
            else
            {
                prescriber = Prescriber.Create(code, name, detail);
                _db.Prescribers.Add(prescriber);
                existing[code] = prescriber;
            }
        }

        run.Report.Count("Prescribers", seen.Values.Sum());
        run.Report.Check("Prescribers", rows.Count(r => r.Clean(F.DoctorCode) is not null
                                                        || r.Clean(F.Surname) is not null
                                                        || r.Clean(F.DoctorName) is not null),
                         seen.Values.Sum());
        run.Report.Review("Codes the files give to more than one prescriber (each kept, as 0-2, 0-3...)",
            doubled.GroupBy(c => c).Select(g => $"{g.Key} - {g.Count() + 1} prescribers"));
    }

    // ---- Staff -------------------------------------------------------------

    /// <summary>
    /// The user a till operator or a pharmacist in the files was, by
    /// username or by name.
    /// </summary>
    /// <remarks>
    /// One the users list does not hold is added inactive, with a password
    /// nobody knows: their sales and dispensings carry their name, and
    /// nobody can sign in as them until an administrator activates the
    /// account and sets a password.
    /// </remarks>
    private async Task<Guid> StaffAsync(Run run, string? name, CancellationToken ct)
    {
        name = Limit(string.IsNullOrWhiteSpace(name) ? NotRecorded : name.Trim(), 100)!;

        if (run.Staff.Count == 0)
            foreach (var u in await _db.Users.AsNoTracking().ToListAsync(ct))
            {
                run.Staff[u.Username] = u.Id;
                run.Staff.TryAdd(u.DisplayName, u.Id);
            }

        if (run.Staff.TryGetValue(name, out var id)) return id;

        var username = Limit(string.Concat(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '.')).Trim('.'), 50)!;
        if (run.Staff.TryGetValue(username, out id)) return id;

        var user = User.Create(username, Title(name)!, _hasher.Hash(Guid.NewGuid().ToString("N")),
                               mustChangePassword: true);
        user.Deactivate();
        _db.Users.Add(user);

        run.Staff[username] = user.Id;
        run.Staff[name] = user.Id;
        run.NewStaff.Add(username);
        return user.Id;
    }

    // ---- Helpers ---------------------------------------------------------

    /// <summary>
    /// The files' own totals: "Sales_Lines rows", "Sales_Lines LINETOTAL",
    /// recorded by whatever exported them at the moment it read the data.
    /// </summary>
    private static Dictionary<string, decimal> Controls(IReadOnlyList<SourceRow> rows)
    {
        var controls = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
            if (row.Clean(F.Control) is string name
                && decimal.TryParse(row.Text(F.ControlValue), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var value))
                controls[name] = value;

        return controls;
    }

    /// <summary>
    /// That every row the export counted was read, and that every column
    /// it totalled adds up to the same here.
    /// </summary>
    private static void CheckRead(SourceTable table, IReadOnlyDictionary<string, decimal> controls,
                                  ImportReport report)
    {
        foreach (var (name, expected) in controls)
        {
            var space = name.LastIndexOf(' ');
            if (space <= 0 || !name[..space].Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var column = name[(space + 1)..];

            if (column.Equals("rows", StringComparison.OrdinalIgnoreCase))
                report.Check($"{table.Name}: rows read", expected, table.Rows.Count);
            else if (table.Has(new[] { SourceTable.Key(column) }))
                report.Check($"{table.Name}: {column} read", expected,
                    table.Rows.Sum(r => r.Number(new[] { SourceTable.Key(column) })),
                    tolerance: Math.Max(0.005m, Math.Abs(expected) * 0.000001m));
        }
    }

    /// <summary>The local time a till recorded, as the UTC PharmaX stores.</summary>
    private static DateTime? Utc(DateTime? local) =>
        local is DateTime l ? DateTime.SpecifyKind(l, DateTimeKind.Local).ToUniversalTime() : null;

    /// <summary>
    /// When a record was made, as the files say, for its own creation
    /// date - so a patient on file for years is not counted as added today.
    /// A spreadsheet's day nought, 30/12/1899, is no date at all.
    /// </summary>
    private static DateTime Since(SourceRow row) =>
        row.Date(F.Changed) is DateTime d && d.Year >= 1950 ? Utc(d)!.Value : default;

    /// <summary>
    /// The same identifier for the same till every run, so the sales it
    /// rang up keep their numbers apart from every other till's.
    /// </summary>
    private static Guid TillId(string till) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("pharmax-import-till:" + till))[..16]);

    private static string? Title(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Trim().ToLowerInvariant());

    private static string? Limit(string? text, int length) =>
        text is null ? null : text.Length <= length ? text : text[..length];

    [GeneratedRegex(@"^[A-Za-z0-9-]{4,48}$")]
    private static partial Regex BarcodeShape();

    /// An identity number: four to twenty letters and digits, spaces and
    /// dashes allowed - "08-123456 X 08" - and at least four digits.
    [GeneratedRegex(@"^(?=(?:\D*\d){4})[A-Za-z0-9][A-Za-z0-9 \-/]{3,19}$")]
    private static partial Regex IdShape();
}

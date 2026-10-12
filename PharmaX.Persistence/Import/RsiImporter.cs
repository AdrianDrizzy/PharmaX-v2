using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Legacy;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Legacy;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Persistence.Import;

/// <summary>
/// Loads RSI DispenseWare and Retailware's exported data into PharmaX.
/// </summary>
/// <remarks>
/// <para>
/// What the pharmacy works with every day becomes PharmaX's own records:
/// departments, suppliers, products with their prices, VAT and schedule,
/// the stock on the shelf and its batches, patients and prescribers.
/// What is history - every prescription line, till sale, receipt, stock
/// movement, price change, till override, cycle count and cash-up -
/// goes into LegacyRecord to be read, and nothing else: it moves no stock
/// and posts nothing to the books, which start at go-live.
/// </para>
/// <para>
/// RSI counts stock in units - tablets, millilitres - and prices a pack;
/// PharmaX counts packs. Quantities are divided by the pack size on the
/// way in, which is also why RSI's own "stock value at cost", units times
/// the cost of a whole pack, is not a figure to reconcile against.
/// </para>
/// <para>
/// Safe to run again: every record is found by its RSI code and updated,
/// the opening stock and the history are replaced, nothing is doubled.
/// It refuses a database that has already traded, whose stock would be
/// overwritten by the export's. A dry run does all of it inside a
/// transaction and rolls it back, so its report is the real one.
/// </para>
/// </remarks>
public sealed partial class RsiImporter : ILegacyImporter
{
    private const string Source = "RSI";
    private const string OpeningReason = "RSI-OPENING";

    private static readonly string[] Required =
    {
        "DEPARTMENTS", "SUPPLIERS", "PRODUCTS", "STOCKMAST", "Patients", "Doctors",
        "Prescription_Lines", "Sales_Lines", "Sales_Receipts", "Stock_Movements",
        "Price_History", "Till_Price_Overrides", "Cycle_Counts", "Day_End_Totals",
        "Z_Read_History"
    };

    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<RsiImporter> _log;

    public RsiImporter(PharmaXDbContext db, IClock clock, ILogger<RsiImporter> log)
    {
        _db = db;
        _clock = clock;
        _log = log;
    }

    public async Task<LegacyImportReport> ImportAsync(string folder, bool dryRun,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var report = new ImportReport(dryRun, folder);

        void Step(string text)
        {
            progress?.Report(text);
            _log.LogInformation("RSI import: {Step}", text);
        }

        RsiExport export;

        try
        {
            export = new RsiExport(folder);
        }
        catch (DirectoryNotFoundException ex)
        {
            return report.Fail(ex.Message);
        }

        var missing = Required.Where(s => !export.Has(s)).ToList();
        if (missing.Count > 0)
            return report.Fail("The export is incomplete. Missing: "
                               + string.Join(", ", missing) + ".");

        if (await _db.Sales.AnyAsync(ct) || await _db.Dispensings.AnyAsync(ct)
            || await _db.GoodsReceipts.AnyAsync(ct) || await _db.Stocktakes.AnyAsync(ct))
            return report.Fail(
                "This database has already traded: it holds sales, dispensings, "
                + "deliveries or stocktakes. The import sets stock to what RSI "
                + "says is on the shelf, which would undo all of that. Import into "
                + "a new database, before the pharmacy starts trading on it.");

        foreach (var sheet in Required)
            report.Source(sheet, export.SourceOf(sheet));

        var controls = Controls(folder);
        var now = _clock.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            Step("Departments and suppliers");
            var departments = await DepartmentsAsync(export.Rows("DEPARTMENTS"), report, ct);
            var suppliers = await SuppliersAsync(export.Rows("SUPPLIERS"), report, ct);
            await _db.SaveChangesAsync(ct);

            Step("Products, prices and stock");
            var salesLines = export.Rows("Sales_Lines");
            await ProductsAsync(export.Rows("PRODUCTS"), export.Rows("STOCKMAST"),
                departments, suppliers, salesLines, now, report, controls, ct);
            await _db.SaveChangesAsync(ct);

            Step("Patients");
            await PatientsAsync(export.Rows("Patients"), report, controls, ct);
            await _db.SaveChangesAsync(ct);

            Step("Prescribers");
            await PrescribersAsync(export.Rows("Doctors"), report, controls, ct);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();

            Step("History");
            await HistoryAsync(export, salesLines, report, controls, Step, ct);

            if (dryRun)
            {
                await tx.RollbackAsync(ct);
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
            _log.LogError(ex, "RSI import failed");
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return report.Fail("The import stopped and nothing was kept: "
                               + (ex.InnerException?.Message ?? ex.Message));
        }

        return report.Done();
    }

    // ---- Departments and suppliers ---------------------------------------

    private async Task<Dictionary<string, (Guid Id, string Name)>> DepartmentsAsync(
        IReadOnlyList<RsiRow> rows, ImportReport report, CancellationToken ct)
    {
        var existing = await _db.Departments.ToDictionaryAsync(d => d.Code, ct);
        var map = new Dictionary<string, (Guid, string)>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var name = row.Clean("DEPTNAME") ?? $"Department {row.Text("DEPTID")}";
            var code = Limit(name.ToUpperInvariant(), 30)!;
            var detail = $"RSI department {row.Text("DEPTID")}";

            if (existing.TryGetValue(code, out var department))
                department.Rename(Title(name), detail);
            else
            {
                department = Department.Create(code, Title(name), detail);
                _db.Departments.Add(department);
                existing[code] = department;
            }

            map[row.Text("DEPTID")] = (department.Id, code);
        }

        report.Count("Departments", map.Count);
        return map;
    }

    private async Task<Dictionary<string, Guid>> SuppliersAsync(
        IReadOnlyList<RsiRow> rows, ImportReport report, CancellationToken ct)
    {
        var existing = await _db.Suppliers.ToDictionaryAsync(s => s.Code, ct);
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var code = row.Text("SUPPLIERCODE").ToUpperInvariant();
            if (code.Length == 0) continue;

            var name = Limit(row.Clean("SUPPLIERNAME") ?? $"Supplier {code}", 150)!;

            if (existing.TryGetValue(code, out var supplier)) supplier.Rename(name);
            else
            {
                supplier = Supplier.Create(code, name);
                _db.Suppliers.Add(supplier);
                existing[code] = supplier;
            }

            supplier.UpdateContact(null, Limit(row.Clean("TELEPHONE"), 30),
                                   Limit(row.Clean("EMAILADDRESS"), 150));
            map[code] = supplier.Id;
        }

        report.Count("Suppliers", map.Count);
        report.Review("Suppliers sharing a name (merge them under Inventory > Suppliers)",
            rows.GroupBy(r => r.Text("SUPPLIERNAME").Trim().ToUpperInvariant())
                .Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => $"{g.Key}: codes {string.Join(", ", g.Select(r => r.Text("SUPPLIERCODE")))}"));
        return map;
    }

    // ---- Products and stock ----------------------------------------------

    private async Task ProductsAsync(IReadOnlyList<RsiRow> products,
        IReadOnlyList<RsiRow> stockRows,
        Dictionary<string, (Guid Id, string Name)> departments,
        Dictionary<string, Guid> suppliers, IReadOnlyList<RsiRow> salesLines,
        DateTime now, ImportReport report,
        IReadOnlyDictionary<string, decimal> controls, CancellationToken ct)
    {
        var stock = stockRows
            .GroupBy(r => r.Text("PRODUCTCODE"), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Number("PACKSIZE")).ToList(),
                          StringComparer.Ordinal);

        var demand = Demand(salesLines);
        var existing = await _db.Products.ToDictionaryAsync(p => p.Code, ct);

        int added = 0, updated = 0, inactive = 0, batches = 0, extraPacks = 0;
        decimal unitsIn = 0m, negativeUnits = 0m, valueAtCost = 0m, valueAtPrice = 0m;
        var imported = new List<Guid>();
        var opening = new List<(Product Product, decimal Packs, Batch? Batch)>();
        var noPrice = new List<string>();
        var negative = new List<string>();
        var expired = new List<string>();
        var unbatched = new List<string>();
        var scheduled = new List<string>();
        var otcSales = salesLines.Where(l => l.Text("SCRIPTNO") is "" or "0")
                                 .Select(l => l.Text("PRODUCTCODE"))
                                 .ToHashSet(StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(now);

        foreach (var row in products)
        {
            var baseCode = row.Text("PRODUCTCODE");
            var packs = stock.GetValueOrDefault(baseCode) ?? new List<RsiRow>();

            // A product RSI held at two pack sizes becomes two products in
            // PharmaX, which prices one pack per product.
            for (var i = 0; i < Math.Max(1, packs.Count); i++)
            {
                var s = i < packs.Count ? packs[i] : null;
                var pack = s is null ? 1m : Math.Max(1m, s.Number("PACKSIZE"));
                var code = i == 0 ? baseCode : $"{baseCode}-{pack:0.###}";
                if (i > 0) extraPacks++;

                var units = s?.Number("QUANTITY") ?? 0m;
                var price = s?.Number("SELLINGPRICE") ?? 0m;
                var cost = s?.Number("COSTPRICE") ?? 0m;
                var name = row.Clean("PRODUCTNAME") ?? code;
                var strength = row.Clean("STRENGTH");
                var description = Limit(strength is null
                    || name.Contains(strength, StringComparison.OrdinalIgnoreCase)
                        ? name : $"{name} {strength}", 200)!;

                var department = departments.GetValueOrDefault(row.Text("DEPTID"));
                var rsiSchedule = row.Text("DRUGSCHEDULE").Trim().ToUpperInvariant();

                // The dispensary's own department, or a prescription class,
                // puts a product behind the dispensing screen. Except where
                // RSI filed it outside the dispensary and sold it at the till
                // - Dettol, Listerine, gripe water carry "PP" there - which
                // stays sellable as it was, and is listed for the pharmacist
                // to confirm rather than stopped at the till on day one.
                var inDispensary = department.Name == "DISPENSARY";
                var soldOverCounter = !inDispensary && otcSales.Contains(baseCode);
                var schedule = inDispensary ? 3 : Schedule(rsiSchedule);

                if (schedule >= 3 && soldOverCounter)
                {
                    schedule = 2;
                    scheduled.Add($"{code} {description} (RSI schedule {rsiSchedule})");
                }

                var (vat, taxCode) = row.Text("TAXCODE") == "2" ? (15m, "A") : (0m, "Z");
                var batchNumber = Limit(s?.Clean("BATCHNO"), 50);
                var expiry = s?.Date("BATCHEXPIRY");
                var hasBatch = batchNumber is not null && expiry is not null;
                var onHand = Math.Round(Math.Max(units, 0m) / pack, 3);

                // Stock on the shelf with no batch stays untracked: its batch
                // and expiry cannot be made up. Empty dispensary lines are
                // tracked, so the next delivery is received against a batch.
                var tracked = units > 0m ? hasBatch : schedule >= 3;

                if (existing.TryGetValue(code.ToUpperInvariant(), out var product))
                {
                    product.Rename(description);
                    product.ChangeRetailPrice(Money.Of(price));
                    product.SetVat(vat, taxCode);
                    product.SetSchedule(schedule);
                    product.SetBatchTracked(tracked);
                    updated++;
                }
                else
                {
                    product = Product.Create(code, description, Money.Of(price), vat,
                                             taxCode, schedule, tracked);
                    _db.Products.Add(product);
                    existing[product.Code] = product;
                    added++;
                }

                product.ChangeCostPrice(Money.Of(cost));
                product.SetUnitsPerPack(pack > 1m ? pack : null);
                product.SetPackSize(PackText(row, pack));
                product.SetGenericName(row.Clean("ALT_PRODUCTNAME"));
                product.Classify(department.Id == Guid.Empty ? null : department.Id, null, null);
                product.SetDefaultSupplier(suppliers.TryGetValue(
                    row.Text("SUPPLIERCODE").ToUpperInvariant(), out var supplierId)
                        ? supplierId : null);

                if (row.Clean("BARCODE") is string barcode && BarcodeShape().IsMatch(barcode))
                    product.ReplaceBarcodes(new[] { barcode });

                product.SetQuantityOnHand(onHand);
                product.SetReorderLevel(demand.TryGetValue(baseCode, out var units90)
                    ? Math.Ceiling(units90 / 90m * 14m / pack) : 0m);

                var discontinued = row.Clean("DISCONTINUEDON") is not null;

                if (discontinued || (price <= 0m && units <= 0m))
                {
                    product.Deactivate();
                    inactive++;
                }
                else product.Reactivate();

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

                if (units > 0m && !hasBatch && schedule >= 3)
                    unbatched.Add($"{code} {description}: {onHand:0.###} packs on no batch");

                if (units > 0m && hasBatch && DateOnly.FromDateTime(expiry!.Value) < today)
                    expired.Add($"{code} {description}: batch {batchNumber} expired {expiry:dd/MM/yyyy}, {onHand:0.###} packs");

                if (onHand > 0m)
                {
                    Batch? batch = null;

                    if (tracked)
                    {
                        batch = Batch.Receive(product.Id, batchNumber!,
                            DateOnly.FromDateTime(expiry!.Value), onHand, Money.Of(cost),
                            now, product.DefaultSupplierId);
                        batches++;
                    }

                    opening.Add((product, onHand, batch));
                }
            }
        }

        // The opening stock replaces whatever an earlier run left: its
        // batches and its opening movements, for these products only.
        await _db.SaveChangesAsync(ct);
        await _db.StockMovements.Where(m => m.ReasonCode == OpeningReason)
                 .ExecuteDeleteAsync(ct);
        await _db.Batches.Where(b => imported.Contains(b.ProductId)).ExecuteDeleteAsync(ct);

        foreach (var (product, onHand, batch) in opening)
        {
            if (batch is not null) _db.Batches.Add(batch);

            _db.StockMovements.Add(StockMovement.Record(product.Id, batch?.Id,
                MovementType.Adjustment, onHand, onHand, now, OpeningReason, "RsiImport"));
        }

        report.Count("Products added", added);
        report.Count("Products updated (already in PharmaX)", updated);
        report.Count("Products imported inactive (discontinued, or no price and no stock)", inactive);
        report.Count("Products split out for a second pack size", extraPacks);
        report.Count("Batches on the shelf", batches);
        report.Count("Products with opening stock", opening.Count);
        report.Figure("Stock at cost (packs x pack cost)", valueAtCost);
        report.Figure("Stock at selling price", valueAtPrice);
        report.Figure("RSI's own 'stock value at cost' (units x pack cost - overstated, not used)",
            controls.GetValueOrDefault("Products_Stock STOCKVALUE_COST"));

        report.Check("Products", controls.GetValueOrDefault("PRODUCTS rows", products.Count),
                     added + updated - extraPacks);
        report.Check("Stock in units (negatives counted as set to 0)",
                     controls.GetValueOrDefault("Products_Stock QUANTITY",
                         stockRows.Sum(r => r.Number("QUANTITY"))),
                     unitsIn + negativeUnits,
                     tolerance: stockRows.Sum(r => Math.Max(1m, r.Number("PACKSIZE"))) * 0.0005m + 0.01m);

        report.Review("Products with stock but no selling price (price them before selling)", noPrice);
        report.Review("Negative stock in RSI, set to nothing (count these)", negative);
        report.Review("Expired batches still on the shelf (write them off)", expired);
        report.Review("Dispensary stock on no batch (labels will carry no batch or expiry)", unbatched);
        report.Review("Prescription class in RSI but sold over the counter there - kept at the till; confirm or move to the dispensary (schedule 3 on the product)", scheduled);
    }

    /// Units of each product sold over the last 90 days of the export.
    private static Dictionary<string, decimal> Demand(IReadOnlyList<RsiRow> salesLines)
    {
        var sales = salesLines.Where(l => l.Text("TYPE") == "Sales")
                              .Select(l => (Code: l.Text("PRODUCTCODE"), Day: l.Date("SALEDATE"),
                                            Units: l.Number("QUANTITY")))
                              .Where(l => l.Day is not null)
                              .ToList();

        if (sales.Count == 0) return new Dictionary<string, decimal>();

        var from = sales.Max(l => l.Day!.Value).AddDays(-90);

        return sales.Where(l => l.Day >= from)
                    .GroupBy(l => l.Code, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.Units), StringComparer.Ordinal);
    }

    /// <remarks>
    /// The Zimbabwe classes as RSI holds them. PP, and the PP10 and PID
    /// variants, are prescription preparations; P is a pharmacy medicine
    /// a pharmacist may sell over the counter; HR, a household remedy, and
    /// anything unclassified is general sale.
    /// </remarks>
    private static int Schedule(string rsi) => rsi switch
    {
        "PP" or "PP10" or "PID" => 3,
        "P" => 2,
        _ => 0
    };

    private static string? PackText(RsiRow row, decimal pack)
    {
        var form = row.Clean("FORMCODE") ?? row.Clean("UNITOFMEASURE");
        var text = pack > 1m ? $"{form} {pack:0.###}".Trim() : form;
        return Limit(text, 50);
    }

    // ---- Patients and prescribers ----------------------------------------

    private async Task PatientsAsync(IReadOnlyList<RsiRow> rows, ImportReport report,
        IReadOnlyDictionary<string, decimal> controls, CancellationToken ct)
    {
        var existing = await _db.Patients.ToDictionaryAsync(p => p.FileNumber, ct);
        int added = 0, updated = 0, noSurname = 0, droppedIds = 0;

        foreach (var row in rows)
        {
            var file = FileNumber(row.Text("MEMBERCODE"), row.Text("DEPENDENTID"));
            var surname = Limit(Title(row.Clean("SURNAME")), 100);
            var first = Limit(Title(row.Clean("FIRSTNAME")), 150) ?? "";
            var id = row.Clean("IDNO");
            var born = row.Date("DATEOFBIRTH") is DateTime b ? DateOnly.FromDateTime(b) : (DateOnly?)null;

            if (surname is null)
            {
                surname = "(no surname)";
                noSurname++;
            }

            if (id is not null && (!IdShape().IsMatch(id) || id.Length > 20))
            {
                id = null;
                droppedIds++;
            }

            if (existing.TryGetValue(file, out var patient))
            {
                patient.Amend(surname, first, id, born);
                updated++;
            }
            else
            {
                patient = Patient.Create(file, surname, first, born, id);
                _db.Patients.Add(patient);
                existing[file] = patient;
                added++;
            }

            patient.UpdateContact(Limit(row.Clean("PHONE"), 30), null);
            patient.SetAddress(Limit(Address(row), 250));
        }

        report.Count("Patients added", added);
        report.Count("Patients updated", updated);
        report.Count("Patients with no surname in RSI", noSurname);
        report.Count("ID numbers dropped as not ID numbers", droppedIds);
        report.Check("Patients", controls.GetValueOrDefault("Patients rows", rows.Count), added + updated);
        report.Review("Possibly the same person twice - same name and phone (merge under Dispensary > Merge Patient Files)",
            rows.Where(r => r.Clean("PHONE") is not null)
                .GroupBy(r => $"{r.Text("FIRSTNAME").ToUpperInvariant()}|{r.Text("SURNAME").ToUpperInvariant()}|{r.Text("PHONE")}")
                .Where(g => g.Count() > 1)
                .Select(g => string.Join(" and ", g.Select(r => FileNumber(r.Text("MEMBERCODE"), r.Text("DEPENDENTID"))))));
    }

    private static string FileNumber(string member, string dependant) =>
        dependant is "" or "1" ? $"RSI-{member}" : $"RSI-{member}-{dependant}";

    private static string Address(RsiRow row)
    {
        var city = row.Clean("CITY") is string c
            && c.Equals("BYO", StringComparison.OrdinalIgnoreCase) ? "Bulawayo" : Title(row.Clean("CITY"));

        return string.Join(", ", new[] { Title(row.Clean("STREET")), Title(row.Clean("AREA")), city }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private async Task PrescribersAsync(IReadOnlyList<RsiRow> rows, ImportReport report,
        IReadOnlyDictionary<string, decimal> controls, CancellationToken ct)
    {
        var existing = await _db.Prescribers.ToDictionaryAsync(p => p.Code, ct);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var doubled = new List<string>();

        foreach (var row in rows)
        {
            var code = "D" + row.Text("DOCTORCODE");

            // One doctor code appears twice in RSI. The second keeps its
            // details under a code of its own rather than overwriting the
            // first.
            if (seen.TryGetValue(code, out var times))
            {
                seen[code] = times + 1;
                doubled.Add(code);
                code = $"{code}-{times + 1}";
            }
            else seen[code] = 1;

            var name = string.Join(" ", new[]
            {
                Title(row.Clean("TITLE")),
                row.Clean("FIRSTNAME") is string first ? Title(first) : row.Clean("INITIALS")?.ToUpperInvariant(),
                Title(row.Clean("SURNAME"))
            }.Where(p => !string.IsNullOrWhiteSpace(p)));

            var practice = row.Clean("PRACTICENO");
            var detail = Limit(string.Join(" · ", new[]
            {
                practice is null ? null : $"Practice {practice}",
                row.Clean("PHONE1"), Title(row.Clean("CITY"))
            }.Where(p => p is not null)), 400);

            name = Limit(name.Length == 0 ? code : name, 200)!;

            if (existing.TryGetValue(code, out var prescriber)) prescriber.Rename(name, detail);
            else
            {
                prescriber = Prescriber.Create(code, name, detail);
                _db.Prescribers.Add(prescriber);
                existing[code] = prescriber;
            }
        }

        report.Count("Prescribers", seen.Values.Sum());
        report.Check("Prescribers", controls.GetValueOrDefault("Doctors rows", rows.Count), seen.Values.Sum());
        report.Review("Doctor codes RSI gives to more than one doctor (each kept, as D0-2, D0-3...)",
            doubled.GroupBy(c => c).Select(g => $"{g.Key} - {g.Count() + 1} doctors"));
    }

    // ---- History ---------------------------------------------------------

    private async Task HistoryAsync(RsiExport export, IReadOnlyList<RsiRow> salesLines,
        ImportReport report, IReadOnlyDictionary<string, decimal> controls,
        Action<string> step, CancellationToken ct)
    {
        await _db.LegacyRecords.Where(r => r.Source == Source).ExecuteDeleteAsync(ct);

        var buffer = new List<LegacyRecord>();

        async Task Flush()
        {
            if (buffer.Count == 0) return;
            _db.LegacyRecords.AddRange(buffer);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            buffer.Clear();
        }

        async Task Add(LegacyRecord record)
        {
            buffer.Add(record);
            if (buffer.Count >= 2000) await Flush();
        }

        // Prescriptions. Lines RSI deleted - duplicates, scripts issued in
        // error - are left out; the report counts them.
        step("History: prescriptions");
        var scripts = export.Rows("Prescription_Lines");
        decimal paid = 0m, paidDeleted = 0m;
        var deleted = 0;

        foreach (var r in scripts)
        {
            if (r.Flag("DELETED"))
            {
                deleted++;
                paidDeleted += r.Number("POS_AMOUNTPAID");
                continue;
            }

            paid += r.Number("POS_AMOUNTPAID");
            var dosage = r.Clean("DOSAGE");

            await Add(LegacyRecord.Create(Source, "Script", r.Text("SCRIPTNO"),
                r.When("DISPENSEDATE", "DISPENSETIME"),
                $"{r.Text("PRODUCT")} {r.Text("STRENGTH")} x{r.Number("QUANTITY"):0.###}"
                + (dosage is null ? "" : $" - {dosage}") + $" for {r.Text("PATIENT")}",
                Json(r), r.Text("PRODUCTCODE"), FileNumber(r.Text("MEMBERCODE"), r.Text("DEPENDENTID")),
                r.Text("DOCTOR"), r.Number("QUANTITY"), r.Number("POS_AMOUNTPAID"), r.Text("PHARMACIST")));
        }

        // The shorthand RSI's directions used that PharmaX has no meaning
        // for yet. Never guessed: a wrong meaning on a label is worse than
        // the code itself. The pharmacist adds them under Dosage Codes.
        var defined = (await _db.DosageCodes.Select(d => d.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        report.Review("Dosage shorthand in RSI's directions with no meaning yet (add under Dispensary > Dosage Codes)",
            scripts.Where(r => !r.Flag("DELETED"))
                .SelectMany(r => r.Text("DOSAGE").ToUpperInvariant()
                    .Split(' ', ',', StringSplitOptions.RemoveEmptyEntries))
                .Select(w => w.TrimEnd('.'))
                .Where(w => w.Any(char.IsLetter) && !defined.Contains(w) && !PlainWords.Contains(w)
                            && (w.Length <= 4 || w.Any(char.IsDigit)) && w.Length <= 6)
                .GroupBy(w => w)
                .Where(g => g.Count() >= 20)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} - used {g.Count():N0} times"));

        report.Count("Prescription lines", scripts.Count - deleted);
        report.Count("Prescription lines RSI had deleted, left out", deleted);
        report.Check("Prescription lines", controls.GetValueOrDefault("Prescription_Lines rows", scripts.Count),
                     scripts.Count);
        report.Check("Paid by patients on prescriptions",
                     controls.GetValueOrDefault("Prescription_Lines POS_AMOUNTPAID", paid + paidDeleted),
                     paid + paidDeleted);

        // Till sales.
        step("History: till sales");
        decimal sold = 0m;

        foreach (var r in salesLines)
        {
            sold += r.Number("LINETOTAL");

            await Add(LegacyRecord.Create(Source, "Sale", r.Text("TRANSNO"),
                r.When("SALEDATE", "SALETIME"),
                $"{r.Text("PRODUCTNAME")} x{r.Number("QUANTITY"):0.###} ({r.Text("TENDER")})",
                Json(r), r.Text("PRODUCTCODE"), null, r.Text("TENDER"),
                r.Number("QUANTITY"), r.Number("LINETOTAL"), r.Text("OPERATOR")));
        }

        report.Check("Till sale lines", controls.GetValueOrDefault("Sales_Lines rows", salesLines.Count), salesLines.Count);
        report.Check("Till sales value", controls.GetValueOrDefault("Sales_Lines LINETOTAL", sold), sold);

        // Receipts. Ones with no payment type and no items are not sales -
        // one of them is for $33,576 - and are listed rather than kept.
        step("History: receipts and cash-ups");
        var withLines = salesLines.Select(l => l.Text("TRANSNO")).ToHashSet(StringComparer.Ordinal);
        var receipts = export.Rows("Sales_Receipts");
        decimal taken = 0m, empty = 0m;
        var junk = new List<string>();

        foreach (var r in receipts)
        {
            if (r.Clean("TENDER") is null && !withLines.Contains(r.Text("TRANSNO")))
            {
                empty += r.Number("TRANSTOTAL");
                junk.Add($"Receipt {r.Text("TRANSNO")} of {r.Date("SALEDATE"):dd/MM/yyyy}: {r.Number("TRANSTOTAL"):0.00}");
                continue;
            }

            taken += r.Number("TRANSTOTAL");
            var rate = r.Number("TENDEREXCHANGE");

            await Add(LegacyRecord.Create(Source, "Receipt", r.Text("TRANSNO"),
                r.When("SALEDATE", "SALETIME"),
                $"{r.Text("TENDER")}: total {r.Number("TRANSTOTAL"):0.00}, tendered "
                + $"{r.Number("AMOUNTTENDERED"):0.00}" + (rate > 1m ? $" at {rate:0.####}" : "")
                + $", change {r.Number("CHANGEAMOUNT"):0.00}"
                + (r.Clean("RETURNREASON") is string reason ? $" - return: {reason}" : ""),
                Json(r), null, null, r.Text("TENDER"), null, r.Number("TRANSTOTAL"), r.Text("OPERATOR")));
        }

        report.Check("Till receipts", controls.GetValueOrDefault("Sales_Receipts rows", receipts.Count),
                     receipts.Count);
        report.Check("Till receipts value (kept plus left out)",
                     controls.GetValueOrDefault("Sales_Receipts TRANSTOTAL", taken + empty), taken + empty);
        report.Figure("Receipts with no items and no payment type, left out", empty);
        report.Review("Receipts left out: no items, no payment type", junk);

        var dayEnds = export.Rows("Day_End_Totals");

        foreach (var r in dayEnds)
            await Add(LegacyRecord.Create(Source, "DayEnd", r.Text("ZREADID"),
                r.Date("FORDAY") ?? DateTime.MinValue,
                $"{r.Text("TENDER")}: expected {r.Number("SYSTEMTOTAL"):0.00}, counted "
                + $"{r.Number("ACTUALTOTAL"):0.00}, variance {r.Number("VARIANCE"):0.00}",
                Json(r), null, null, r.Text("TENDER"), null, r.Number("VARIANCE"), r.Text("OPERATOR")));

        report.Check("Cash-up lines", controls.GetValueOrDefault("Day_End_Totals rows", dayEnds.Count), dayEnds.Count);

        var zReads = export.Rows("Z_Read_History");

        foreach (var r in zReads)
            await Add(LegacyRecord.Create(Source, "ZRead", r.Text("ZREADID"),
                r.Date("TRANSDATE") ?? DateTime.MinValue,
                $"Z-read of receipts {r.Text("TRANSFROM")} to {r.Text("TRANSTO")}, authorised by {r.Text("AUTHORIZEDBY_NAME")}",
                Json(r), null, null, null, null, null, r.Text("OPERATOR")));

        report.Check("Z-reads", controls.GetValueOrDefault("Z_Read_History rows", zReads.Count), zReads.Count);

        // Stock.
        step("History: stock movements, price changes, overrides and counts");
        var movements = export.Rows("Stock_Movements");
        decimal moved = 0m;

        foreach (var r in movements)
        {
            moved += r.Number("LINETOTAL");
            var batch = r.Clean("BATCHNO");

            await Add(LegacyRecord.Create(Source, "Stock", r.Clean("GRNO") ?? r.Text("DOCNO"),
                r.Date("MOVEDATE") ?? DateTime.MinValue,
                $"{r.Text("TYPE")}: {r.Text("PRODUCTNAME")} x{r.Number("QUANTITY"):0.###}"
                + (batch is null ? "" : $" batch {batch}")
                + (r.Clean("INVOICENO") is string invoice ? $", invoice {invoice}" : ""),
                Json(r), r.Text("PRODUCTCODE"), null, r.Text("SUPPLIER"),
                r.Number("QUANTITY"), r.Number("LINETOTAL"), r.Text("OPERATOR")));
        }

        report.Check("Stock movements", controls.GetValueOrDefault("Stock_Movements rows", movements.Count), movements.Count);
        report.Check("Stock movements value", controls.GetValueOrDefault("Stock_Movements LINETOTAL", moved), moved);

        var prices = export.Rows("Price_History");

        foreach (var r in prices)
            await Add(LegacyRecord.Create(Source, "Price", null, r.When("CHANGEDATE", "CHANGETIME"),
                $"{r.Text("PRODUCTNAME")}: price {r.Number("OLDSELLING"):0.00} to {r.Number("NEWSELLING"):0.00}, "
                + $"cost {r.Number("OLDCOST"):0.00} to {r.Number("NEWCOST"):0.00}",
                Json(r), r.Text("PRODUCTCODE"), null, null, null, r.Number("NEWSELLING"), r.Text("OPERATOR")));

        report.Check("Price changes", controls.GetValueOrDefault("Price_History rows", prices.Count), prices.Count);

        var overrides = export.Rows("Till_Price_Overrides");

        foreach (var r in overrides)
            await Add(LegacyRecord.Create(Source, "Override", r.Text("OVERIDEID"),
                r.When("OVERRIDEDATE", "OVERRIDETIME"),
                $"{r.Text("PRODUCTNAME")}: {r.Number("SELLINGPRICE"):0.00} charged as {r.Number("CHANGEDTO"):0.00}",
                Json(r), r.Text("PRODUCTCODE"), null, r.Text("AUTHORIZEDBY"),
                r.Number("QUANTITY"), r.Number("DIFFERENCE"), r.Text("OPERATOR")));

        report.Check("Till price overrides", controls.GetValueOrDefault("Till_Price_Overrides rows", overrides.Count), overrides.Count);

        var counts = export.Rows("Cycle_Counts");

        foreach (var r in counts)
            await Add(LegacyRecord.Create(Source, "Count", r.Text("CYCLECOUNTID"),
                r.Date("COUNTDATE") ?? DateTime.MinValue,
                $"{r.Text("PRODUCTNAME")}: {r.Number("PRIORQTY"):0.###} on record, {r.Number("ACTUALQTY"):0.###} counted",
                Json(r), r.Text("PRODUCTCODE"), null, null,
                r.Number("VARIANCE_QTY"), r.Number("VARIANCE_VALUE_COST"), r.Text("OPERATOR")));

        report.Check("Cycle counts", controls.GetValueOrDefault("Cycle_Counts rows", counts.Count), counts.Count);

        await Flush();

        report.Count("History records kept",
            await _db.LegacyRecords.CountAsync(r => r.Source == Source, ct));
    }

    private static string Json(RsiRow row) => JsonSerializer.Serialize(row.Values);

    /// Words that read the same on a label as they are typed.
    private static readonly HashSet<string> PlainWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "AN", "AND", "AS", "AT", "BY", "FOR", "IN", "IS", "OF", "ON", "OR", "THE",
        "TO", "UP", "PAIN", "TAKE", "GIVE", "DAY", "DAYS", "ONCE", "ONLY", "TAB", "TABS",
        "MILK", "AREA", "WITH", "EACH", "EYE", "EYES", "EAR", "EARS", "NOSE", "DROP",
        "SKIN", "WEEK", "HOUR", "MEAL", "FOOD", "THEN", "USE", "PUFF", "ONE", "TWO"
    };

    // ---- Helpers ---------------------------------------------------------

    /// The database's own totals, recorded by the export scripts in the
    /// same moment they read the data.
    private static Dictionary<string, decimal> Controls(string folder)
    {
        var controls = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(folder, "*_Controls.csv"))
        {
            foreach (var line in File.ReadAllLines(file).Skip(1))
            {
                var parts = line.Split("\",\"");
                if (parts.Length < 2) continue;

                if (decimal.TryParse(parts[1].Trim('"'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value))
                    controls[parts[0].Trim('"', '﻿')] = value;
            }
        }

        return controls;
    }

    private static string? Title(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Trim().ToLowerInvariant());

    private static string? Limit(string? text, int length) =>
        text is null ? null : text.Length <= length ? text : text[..length];

    [GeneratedRegex(@"^[A-Za-z0-9-]{4,48}$")]
    private static partial Regex BarcodeShape();

    /// A Zimbabwean national ID: 08-123456 X 08, spaces and dashes optional.
    [GeneratedRegex(@"^\d{2}-?\s?\d{6,7}\s?[A-Za-z]\s?\d{2}$")]
    private static partial Regex IdShape();
}

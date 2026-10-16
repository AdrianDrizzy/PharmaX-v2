using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Domain.Tills;

namespace PharmaX.Persistence.Import;

/// <summary>
/// The trading history: each record into the table PharmaX keeps its own
/// in, built through the same domain objects, so it reads back on every
/// screen and report exactly as PharmaX's own does.
/// </summary>
public sealed partial class DataImporter
{
    private const int Chunk = 500;

    /// <summary>One change to the stock, for the ledger written at the end.</summary>
    private sealed record Move(Guid ProductId, Guid? BatchId, MovementType Type,
        decimal Quantity, DateTime AtUtc, string? Reason, string Source, Guid? SourceId);

    /// <summary>
    /// One till receipt: its lines, and a row per tender where it was paid
    /// two ways - so many dollars in cash and the rest in rands.
    /// </summary>
    private sealed class Receipt
    {
        public string Number { get; init; } = "";
        public DateTime At { get; set; }
        public string Till { get; set; } = "1";
        public string? Operator { get; set; }
        public List<SourceRow> Tenders { get; } = new();
        public List<SourceRow> Lines { get; } = new();
        public Sale? Sale { get; set; }
        public Guid SessionId { get; set; }
    }

    private async Task HistoryAsync(Run run, Func<TableKind, IReadOnlyList<SourceRow>> rows,
        Action<string> step, CancellationToken ct)
    {
        var moves = new List<Move>();

        step("History: till sessions and cash-ups");
        var receipts = Receipts(rows(TableKind.Receipts), rows(TableKind.SaleLines));
        await SessionsAsync(run, receipts, rows(TableKind.ZReads), rows(TableKind.CashUps), ct);

        foreach (var receipt in receipts.Where(r => r.Lines.Exists(l => !IsReturn(l))))
            receipt.Sale = Sale.Open(BranchId, TillId(receipt.Till), receipt.SessionId,
                await StaffAsync(run, receipt.Operator, ct), Utc(receipt.At)!.Value);

        step("History: prescriptions and dispensing");
        await PrescriptionsAsync(run, rows(TableKind.Prescriptions), receipts, moves, ct);

        step("History: till sales and refunds");
        var overrides = rows(TableKind.PriceOverrides);
        var lineIndex = await SalesAsync(run, receipts, overrides, moves, ct);

        step("History: deliveries and stock movements");
        await MovementsAsync(run, rows(TableKind.StockMovements), moves, ct);

        step("History: stock counts");
        await CountsAsync(run, rows(TableKind.StockCounts), moves, ct);

        step("History: price changes and till overrides");
        await PricesAsync(run, rows(TableKind.PriceChanges), overrides, lineIndex, ct);

        step("History: the stock ledger");
        await LedgerAsync(run, moves, ct);

        run.Report.Count("Products found only in the history, added inactive", run.NewProducts.Count);
        run.Report.Review("Products in the history but not in the product list - added inactive under their code",
            run.NewProducts);
        run.Report.Review("Staff in the history added as inactive users (activate and set a password under "
                          + "Administration > Users before they can sign in)", run.NewStaff);
    }

    private Guid BranchId => Guid.TryParse(_options.Workstation.BranchId, out var id) ? id : Guid.Empty;

    private static bool IsReturn(SourceRow line) =>
        line.Text(F.LineType).Contains("RETURN", StringComparison.OrdinalIgnoreCase)
        || line.Text(F.LineType).Contains("REFUND", StringComparison.OrdinalIgnoreCase)
        || line.Number(F.Quantity) < 0m || line.Number(F.LineTotal) < 0m;

    // ---- Receipts and sessions -------------------------------------------

    /// <summary>
    /// The receipts, with their lines, oldest first. Where the files hold
    /// sale lines and no receipts, each receipt number is a receipt.
    /// </summary>
    /// <remarks>
    /// A receipt is its number on its day: a till that starts again at
    /// receipt 1 every morning has a receipt 1 a day, not one receipt 1
    /// holding a year of sales. A line with no date of its own goes to the
    /// receipt of that number.
    /// </remarks>
    private static List<Receipt> Receipts(IReadOnlyList<SourceRow> tenders, IReadOnlyList<SourceRow> lines)
    {
        var receipts = new Dictionary<string, Receipt>(StringComparer.OrdinalIgnoreCase);
        var byNumber = new Dictionary<string, Receipt>(StringComparer.OrdinalIgnoreCase);

        Receipt For(string number, SourceRow row)
        {
            var day = row.Date(F.SaleDate)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (day is null && byNumber.TryGetValue(number, out var any)) return any;

            if (!receipts.TryGetValue($"{number}|{day}", out var receipt))
            {
                receipt = new Receipt { Number = number, At = row.When(F.SaleDate, F.SaleTime) ?? DateTime.MinValue };
                receipts[$"{number}|{day}"] = receipt;
                byNumber.TryAdd(number, receipt);
            }

            return receipt;
        }

        foreach (var row in tenders)
            if (row.Clean(F.TransNo) is string number)
            {
                var receipt = For(number, row);
                receipt.Tenders.Add(row);
                receipt.Operator ??= row.Clean(F.Operator);
            }

        foreach (var row in lines)
            if (row.Clean(F.TransNo) is string number)
            {
                var receipt = For(number, row);
                receipt.Lines.Add(row);
                receipt.Operator ??= row.Clean(F.Operator);
                if (row.Clean(F.Till) is string till) receipt.Till = till;
                if (receipt.At == DateTime.MinValue) receipt.At = row.When(F.SaleDate, F.SaleTime) ?? DateTime.MinValue;
            }

        return receipts.Values
            .Where(r => r.At != DateTime.MinValue)
            .OrderBy(r => r.At)
            .ThenBy(r => long.TryParse(r.Number, out var n) ? n : long.MaxValue)
            .ToList();
    }

    /// <summary>
    /// A till session for every Z-read, holding the receipts its range
    /// covers and closed with the cash-up counted against it. Receipts no
    /// Z-read covers - the last ones, rung up after the final Z-read -
    /// are in a session per till and day, left suspended: nobody cashed
    /// them up.
    /// </summary>
    private async Task SessionsAsync(Run run, List<Receipt> receipts,
        IReadOnlyList<SourceRow> zReads, IReadOnlyList<SourceRow> cashUps, CancellationToken ct)
    {
        var reads = zReads
            .Where(z => z.Clean(F.ZRead) is not null)
            .GroupBy(z => z.Text(F.ZRead), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(z => (Id: z.Text(F.ZRead), From: Long(z.Text(F.FromReceipt)), To: Long(z.Text(F.ToReceipt)), Row: (SourceRow?)z))
            .ToList();

        var counted = cashUps
            .Where(c => c.Clean(F.ZRead) is not null)
            .GroupBy(c => c.Text(F.ZRead), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // A cash-up for a Z-read the files do not list still closes a session.
        foreach (var id in counted.Keys.Where(k => !reads.Exists(r => r.Id.Equals(k, StringComparison.OrdinalIgnoreCase))))
            reads.Add((id, long.MaxValue, long.MinValue, null));

        var held = new Dictionary<string, List<Receipt>>(StringComparer.OrdinalIgnoreCase);

        // Only receipts with something on them: one with no items is not
        // imported, and a stray one would stretch its session over months.
        foreach (var receipt in receipts.Where(r => r.Lines.Count > 0))
        {
            var n = Long(receipt.Number);
            var read = reads.FirstOrDefault(r => n >= r.From && n <= r.To);
            var key = read.Id is not null ? "Z|" + read.Id : $"D|{receipt.Till}|{receipt.At:yyyy-MM-dd}";

            if (!held.TryGetValue(key, out var list)) held[key] = list = new List<Receipt>();
            list.Add(receipt);
        }

        foreach (var read in reads.Where(r => !held.ContainsKey("Z|" + r.Id)))
            held["Z|" + read.Id] = new List<Receipt>();

        int closed = 0, open = 0;
        decimal expectedTotal = 0m, declaredTotal = 0m;

        foreach (var (key, list) in held)
        {
            var zRead = key.StartsWith("Z|", StringComparison.Ordinal) ? key[2..] : null;
            var row = zRead is null ? null : reads.First(r => r.Id == zRead).Row;
            var counts = zRead is null ? new List<SourceRow>() : counted.GetValueOrDefault(zRead) ?? new List<SourceRow>();

            var day = row?.Date(F.CashUpDate) ?? counts.Select(c => c.Date(F.CashUpDate)).FirstOrDefault(d => d is not null);
            var first = list.Count > 0 ? list[0].At : day ?? run.Now.ToLocalTime();
            var last = list.Count > 0 ? list[^1].At : first;
            if (day is DateTime d && d > last) last = d;

            var till = list.Count > 0 ? list[0].Till : "1";
            var openedBy = await StaffAsync(run, list.Count > 0 ? list[0].Operator : row?.Clean(F.Operator), ct);

            var session = TillSession.Open(BranchId, TillId(till), Limit(till, 10)!, openedBy, Money.Zero(), Utc(first)!.Value);
            session.CreatedAtUtc = session.OpenedAtUtc;

            if (zRead is null)
            {
                session.Suspend();
                open++;
            }
            else
            {
                var closedBy = await StaffAsync(run, row?.Clean(F.Operator)
                    ?? counts.Select(c => c.Clean(F.Operator)).FirstOrDefault(o => o is not null), ct);
                var declaration = TillDeclaration.Create(session.Id, closedBy, Utc(last)!.Value, isFinal: true);
                declaration.CreatedAtUtc = declaration.DeclaredAtUtc;

                foreach (var tender in counts.GroupBy(c => Tender(c.Text(F.Tender))))
                {
                    var expected = tender.Sum(c => c.Number(F.SystemTotal));
                    var declared = tender.Sum(c => c.Number(F.Declared));
                    declaration.AddLine(tender.Key, Money.Of(expected), Money.Of(declared));
                    expectedTotal += expected;
                    declaredTotal += declared;
                }

                session.Close(closedBy, declaration.TotalDeclared, Utc(last)!.Value,
                              Limit(row?.Clean(F.AuthorisedBy) is string by ? $"Z-read {zRead}, authorised by {by}" : null, 500));

                _db.TillDeclarations.Add(declaration);
                closed++;
            }

            _db.TillSessions.Add(session);
            foreach (var receipt in list) receipt.SessionId = session.Id;
        }

        await SaveAsync(ct);

        run.Report.Count("Till sessions closed with a cash-up", closed);
        run.Report.Count("Till sessions no cash-up covered (left suspended)", open);
        run.Report.Figure("Cash-ups: expected by the till", expectedTotal);
        run.Report.Figure("Cash-ups: counted", declaredTotal);
        run.Report.Check("Cash-up lines", cashUps.Count(c => c.Clean(F.ZRead) is not null),
            counted.Values.Sum(v => v.Count));
    }

    private static long Long(string text) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? (long)d
                : long.MinValue;

    /// <summary>How a till's tender text reads as a PharmaX tender.</summary>
    /// <remarks>
    /// Foreign cash - rands, a second currency - is cash in the drawer;
    /// the tender's own name stays on the payment as its reference.
    /// </remarks>
    private static TenderType Tender(string text)
    {
        var t = text.ToUpperInvariant();

        return t.Contains("ECO", StringComparison.Ordinal) ? TenderType.EcoCash
            : t.Contains("SWIPE", StringComparison.Ordinal) || t.Contains("CARD", StringComparison.Ordinal)
              || t.Contains("VISA", StringComparison.Ordinal) || t.Contains("MASTER", StringComparison.Ordinal) ? TenderType.Card
            : t.Contains("ACCOUNT", StringComparison.Ordinal) || t.Contains("CREDIT", StringComparison.Ordinal) ? TenderType.Account
            : t.Contains("MEDICAL", StringComparison.Ordinal) || t.Contains("AID", StringComparison.Ordinal) ? TenderType.MedicalAid
            : t.Contains("VOUCHER", StringComparison.Ordinal) ? TenderType.Voucher
            : t.Contains("EFT", StringComparison.Ordinal) || t.Contains("TRANSFER", StringComparison.Ordinal)
              || t.Contains("BANK", StringComparison.Ordinal) ? TenderType.Eft
            : TenderType.Cash;
    }

    // ---- Prescriptions ---------------------------------------------------

    /// <summary>
    /// Each prescription as a script, with the dispensing made against it
    /// and, where a till receipt took the patient's money, the sale that
    /// collected it - so none of it waits under Collect to be paid again.
    /// </summary>
    private async Task PrescriptionsAsync(Run run, IReadOnlyList<SourceRow> rows,
        List<Receipt> receipts, List<Move> moves, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        // The first receipt that carries each script's lines collected it.
        var collectedBy = new Dictionary<string, Receipt>(StringComparer.OrdinalIgnoreCase);

        foreach (var receipt in receipts.Where(r => r.Sale is not null))
            foreach (var line in receipt.Lines)
                if (line.Clean(F.ScriptNo) is string no && no != "0")
                    collectedBy.TryAdd(no, receipt);

        var deleted = rows.Where(r => r.Flag(F.Deleted)).ToList();
        var scripts = rows.Where(r => !r.Flag(F.Deleted) && r.Clean(F.ScriptNo) is not null)
            .GroupBy(r => r.Text(F.ScriptNo), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Number: g.Key, Lines: g.ToList(), At: g.First().When(F.ScriptDate, F.ScriptTime)))
            .Where(s => s.At is not null)
            .OrderBy(s => s.At)
            .ToList();

        int count = 0, lines = 0, collected = 0, noPatientFile = 0;
        decimal dispensed = 0m;
        var waiting = new List<string>();
        var zero = new List<string>();

        foreach (var (number, scriptLines, at) in scripts)
        {
            var head = scriptLines[0];
            var issuedAt = Utc(at)!.Value;
            var patientId = await PatientForAsync(run, head, ct);
            if (patientId.Created) noPatientFile++;

            var pharmacist = await StaffAsync(run, head.Clean(F.Pharmacist), ct);
            var prescribedOn = DateOnly.FromDateTime(at!.Value);

            // The repeats still to come, where the files say how many are
            // left: a script whose repeats were all collected must not
            // come in with them to give again. Each dispensing uses one, so
            // after this one exactly that many remain.
            var repeats = head.Text(F.RepeatsLeft).Length > 0
                ? head.Number(F.RepeatsLeft)
                : head.Number(F.Repeats);

            var script = Script.Capture(Limit(number, 30)!, patientId.Id,
                Limit(head.Clean(F.Prescriber), 150) ?? NotRecorded, prescribedOn,
                Math.Max(0, (int)repeats), null,
                Limit(head.Clean(F.PracticeNo), 30));
            script.CreatedAtUtc = issuedAt;

            var dispensing = Dispensing.Begin(script.Id, patientId.Id, pharmacist, 0);
            dispensing.CreatedAtUtc = issuedAt;

            foreach (var row in scriptLines)
            {
                var units = row.Number(F.Quantity);

                if (units <= 0m)
                {
                    zero.Add($"Script {number}: {row.Text(F.ProductName)} for {units:0.###}");
                    continue;
                }

                var product = ProductFor(run, row.Text(F.ProductCode), null, row.Clean(F.ProductName));
                var description = Limit(string.Join(' ', new[] { row.Clean(F.ProductName), row.Clean(F.Strength) }
                    .Where(p => p is not null)), 200) ?? product.Description;
                var directions = Limit(row.Clean(F.Directions), 500) ?? "No directions recorded";

                var item = script.AddItem(ScriptItem.Create(product.Id, description, units, directions));
                var batchNumber = Limit(row.Clean(F.BatchNo), 50);
                var expiry = row.Date(F.BatchExpiry) is DateTime e ? DateOnly.FromDateTime(e) : (DateOnly?)null;

                var line = dispensing.AddLine(DispensingLine.Create(product.Id, description, units, directions,
                    Money.Of(row.Number(F.LinePrice) + row.Number(F.Fee)), item.Id,
                    BatchFor(run, product.Id, batchNumber), batchNumber, expiry));

                line.SetSchemeContribution(Money.Of(Math.Max(0m, row.Number(F.SchemePaid))));
                dispensed += line.TariffAmount.Amount;

                moves.Add(new Move(product.Id, line.BatchId, MovementType.Issue,
                    Packs(units, product.UnitsInPack), issuedAt, null, "Dispensing", dispensing.Id));
                lines++;
            }

            if (dispensing.Lines.Count == 0) continue;

            script.RecordDispensing(prescribedOn);
            dispensing.Issue(pharmacist, Limit(number, 30)!, issuedAt);

            if (collectedBy.TryGetValue(number, out var receipt) && receipt.Sale is Sale sale)
            {
                dispensing.RecordCollection(sale.Id, sale.OccurredAtUtc);
                if (sale.DispensingId is null) sale.CollectFor(dispensing.Id);
                collected++;
            }
            else if (dispensing.AwaitsCollection)
                waiting.Add($"Script {number} of {prescribedOn:dd/MM/yyyy}: {dispensing.CoPaymentTotal}");

            _db.Scripts.Add(script);
            _db.Dispensings.Add(dispensing);

            if (++count % Chunk == 0) await SaveAsync(ct);
        }

        await SaveAsync(ct);

        run.Report.Count("Prescriptions (scripts, each with its dispensing)", count);
        run.Report.Count("Prescription lines", lines);
        run.Report.Count("Prescriptions collected by a till receipt", collected);
        run.Report.Count("Prescription lines the files mark deleted, left out", deleted.Count);
        run.Report.Count("Patients added from prescriptions (not in the patient list)", noPatientFile);
        run.Report.Check("Prescription lines", rows.Count - deleted.Count, lines + zero.Count);
        run.Report.Check("Value dispensed (price and fees)",
            scripts.SelectMany(s => s.Lines).Where(r => r.Number(F.Quantity) > 0m)
                   .Sum(r => r.Number(F.LinePrice) + r.Number(F.Fee)),
            dispensed, tolerance: lines * 0.005m + 0.005m);

        run.Report.Review("Prescriptions with no till receipt in the files - waiting under Collect until taken or reversed", waiting);
        run.Report.Review("Prescription lines with no quantity, left out", zero);
    }

    /// <summary>
    /// The patient a prescription is for, by member and dependant number;
    /// added from the prescription's own name where the patient list does
    /// not hold them.
    /// </summary>
    private async Task<(Guid Id, bool Created)> PatientForAsync(Run run, SourceRow row, CancellationToken ct)
    {
        var member = row.Clean(F.Member);
        var dependant = Dependant(row.Text(F.Dependant));

        if (member is not null && run.Patients.TryGetValue($"{member}|{dependant}", out var id)) return (id, false);
        if (member is not null && run.Patients.TryGetValue(FileNumber(member, dependant), out id)) return (id, false);

        var (surname, first) = Names(row);
        var key = $"name|{surname}|{first}";
        if (member is null && run.Patients.TryGetValue(key, out id)) return (id, false);

        var file = member is null
            ? await _numbering.NextAsync(Application.Abstractions.DocumentSeries.Patient, ct)
            : FileNumber(member, dependant);

        var patient = Patient.Create(file, surname ?? "(no surname)", first,
            row.Date(F.BirthDate) is DateTime b ? DateOnly.FromDateTime(b) : null);
        _db.Patients.Add(patient);

        run.Patients[file] = patient.Id;
        run.Patients[member is null ? key : $"{member}|{dependant}"] = patient.Id;
        return (patient.Id, true);
    }

    // ---- Till sales and refunds ------------------------------------------

    /// <summary>
    /// Each receipt as a sale - its lines at the price charged, its tenders
    /// as they were taken - and each refund as a return against the sale
    /// it refunded.
    /// </summary>
    /// <returns>The sale lines a till override may refer to, by product and day.</returns>
    private async Task<Dictionary<(Guid, DateOnly), List<(Guid LineId, DateTime At)>>> SalesAsync(
        Run run, List<Receipt> receipts, IReadOnlyList<SourceRow> overrides, List<Move> moves, CancellationToken ct)
    {
        var lineIndex = new Dictionary<(Guid, DateOnly), List<(Guid, DateTime)>>();
        var overridden = overrides.Select(o => o.Text(F.ProductCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // What can be returned: only the products a refund names, so a
        // year of sales is not held in memory for forty refunds.
        var refunded = receipts.SelectMany(r => r.Lines).Where(IsReturn)
            .Select(l => l.Text(F.ProductCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var returnable = new Dictionary<Guid, List<(Sale Sale, SaleLine Line)>>();
        var returned = new Dictionary<Guid, decimal>();

        int sales = 0, returns = 0, lines = 0, skipped = 0, done = 0;
        decimal itemsValue = 0m, saleValue = 0m, refundsValue = 0m;

        // A receipt number once per till, and a refund number once in all:
        // a number the till used on two days takes the day after it.
        var saleNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var returnNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Unique(HashSet<string> used, string scope, string number, DateTime at)
        {
            var candidate = Limit(number, 30)!;

            for (var n = 1; !used.Add($"{scope}|{candidate}"); n++)
                candidate = Limit(n == 1 ? $"{number}-{at:yyyyMMdd}" : $"{number}-{at:yyyyMMdd}-{n}", 30)!;

            return candidate;
        }
        var empty = new List<string>();
        var unbalanced = new List<string>();
        var orphanReturns = new List<string>();
        var refundDiffers = new List<string>();

        foreach (var receipt in receipts)
        {
            var at = Utc(receipt.At)!.Value;
            var day = DateOnly.FromDateTime(receipt.At);
            var operatorId = await StaffAsync(run, receipt.Operator, ct);
            var paid = receipt.Tenders.Sum(t => t.Number(F.ReceiptTotal));

            if (receipt.Lines.Count == 0)
            {
                empty.Add($"Receipt {receipt.Number} of {receipt.At:dd/MM/yyyy}: {paid:N2}"
                          + (receipt.Tenders.All(t => t.Clean(F.Tender) is null) ? ", no tender" : ""));
                continue;
            }

            if (receipt.Sale is Sale sale)
            {
                foreach (var row in receipt.Lines.Where(l => !IsReturn(l)))
                {
                    var units = row.Number(F.Quantity);
                    var total = row.Number(F.LineTotal);

                    if (units == 0m) { skipped++; continue; }

                    var isScript = row.Clean(F.ScriptNo) is string no && no != "0";
                    var pack = Math.Max(1m, row.Number(F.PackSize));
                    var product = ProductFor(run, row.Text(F.ProductCode), isScript ? null : pack, row.Clean(F.ProductName));
                    var name = Limit(row.Clean(F.ProductName), 180) ?? product.Description;
                    var batchNumber = Limit(row.Clean(F.BatchNo), 50);

                    // A collected script is charged once, at what the patient
                    // paid for it, as PharmaX's own collections are; its stock
                    // left with the dispensing.
                    var quantity = isScript ? 1m : Packs(units, pack);
                    var price = isScript ? total
                        : row.Number(F.SellingPrice) is var p and not 0m ? p : total / quantity;

                    var line = sale.AddLine(SaleLine.Create(product.Id,
                        isScript ? $"{name} x{units:0.###}" : name, quantity, Money.Of(price),
                        product.Vat, product.TaxCode, PriceSource.StandardRetail,
                        BatchFor(run, product.Id, batchNumber), null, batchNumber, isScript));

                    // Whatever the shelf price came to, the line charges what
                    // the till charged.
                    var discount = line.GrossBeforeDiscount - Money.Of(total);
                    if (!discount.IsZero)
                        line.ApplyDiscount(discount, 100m,
                            discount.Amount > line.GrossBeforeDiscount.Amount ? operatorId : null);

                    if (!isScript)
                        moves.Add(new Move(product.Id, line.BatchId, MovementType.Issue, quantity, at, null, "Sale", sale.Id));

                    if (overridden.Contains(row.Text(F.ProductCode)))
                    {
                        if (!lineIndex.TryGetValue((product.Id, day), out var list))
                            lineIndex[(product.Id, day)] = list = new List<(Guid, DateTime)>();
                        list.Add((line.Id, at));
                    }

                    if (refunded.Contains(row.Text(F.ProductCode)))
                    {
                        if (!returnable.TryGetValue(product.Id, out var list))
                            returnable[product.Id] = list = new List<(Sale, SaleLine)>();
                        list.Add((sale, line));
                    }

                    itemsValue += total;
                    lines++;
                }

                if (sale.Lines.Count == 0)
                {
                    empty.Add($"Receipt {receipt.Number} of {receipt.At:dd/MM/yyyy}: only lines of no quantity");
                    continue;
                }

                var payments = receipt.Tenders.Where(t => t.Number(F.ReceiptTotal) > 0m).ToList();

                if (payments.Count > 0 && Math.Abs(payments.Sum(t => t.Number(F.ReceiptTotal)) - sale.Total.Amount) < 0.005m)
                    sale.TakePayments(payments.Select(Payment));
                else if (!sale.Total.IsZero)
                {
                    if (receipt.Tenders.Count > 0)
                        unbalanced.Add($"Receipt {receipt.Number} of {receipt.At:dd/MM/yyyy}: tendered {paid:N2} for items of {sale.Total}");

                    var tender = receipt.Tenders.Select(t => t.Clean(F.Tender)).FirstOrDefault(t => t is not null)
                                 ?? receipt.Lines.Select(l => l.Clean(F.Tender)).FirstOrDefault(t => t is not null) ?? "";
                    sale.TakePayments(new[] { SalePayment.Create(Tender(tender), sale.Total, Reference(tender, null)) });
                }

                sale.Finalise(Unique(saleNumbers, receipt.Till, receipt.Number, receipt.At), at);
                sale.CreatedAtUtc = at;
                _db.Sales.Add(sale);
                saleValue += sale.Total.Amount;
                sales++;
            }

            // Refunds: each line against the latest earlier sale of the
            // product that still has as much to give back.
            var refundLines = receipt.Lines.Where(IsReturn).ToList();
            var againstSale = new Dictionary<Sale, List<(SaleLine Line, decimal Quantity, decimal Already, decimal Shelf)>>();

            foreach (var row in refundLines)
            {
                var units = Math.Abs(row.Number(F.Quantity));
                var product = ProductFor(run, row.Text(F.ProductCode), null, row.Clean(F.ProductName));

                // Matched to the sale in the till's own unit; back on the
                // shelf in the product's packs. A till that writes the
                // refunded quantity as its own pack - fourteen tablets as
                // a pack of fourteen - puts half a box of 28 back, not one.
                var quantity = Packs(units, Math.Max(1m, row.Number(F.PackSize)));
                var shelf = Packs(units, product.UnitsInPack);

                var original = returnable.GetValueOrDefault(product.Id)?
                    .Where(x => x.Sale.OccurredAtUtc <= at
                                && x.Line.Quantity - returned.GetValueOrDefault(x.Line.Id) >= quantity)
                    .OrderByDescending(x => x.Sale.OccurredAtUtc)
                    .Select(x => ((Sale Sale, SaleLine Line)?)x)
                    .FirstOrDefault();

                if (original is not (Sale s, SaleLine l))
                {
                    orphanReturns.Add($"Receipt {receipt.Number} of {receipt.At:dd/MM/yyyy}: {row.Text(F.ProductName)} x{units:0.###}");
                    continue;
                }

                if (!againstSale.TryGetValue(s, out var list)) againstSale[s] = list = new();

                // Two refund rows for one sold line are one line of the return.
                var prior = returned.GetValueOrDefault(l.Id);
                var same = list.FindIndex(x => x.Line.Id == l.Id);

                if (same >= 0) list[same] = (l, list[same].Quantity + quantity, list[same].Already, list[same].Shelf + shelf);
                else list.Add((l, quantity, prior, shelf));

                returned[l.Id] = prior + quantity;
            }

            decimal refunded_ = 0m;

            foreach (var (original, returning) in againstSale)
            {
                var reason = Limit(receipt.Tenders.Select(t => t.Clean(F.ReturnReason)).FirstOrDefault(r => r is not null), 500) ?? "Returned";
                var refund = SaleReturn.Against(original, ReturnKind.GoodsReturn, receipt.SessionId,
                    operatorId, operatorId, reason, at);
                refund.CreatedAtUtc = at;

                foreach (var (line, quantity, already, shelf) in returning)
                {
                    refund.AddLine(line, quantity, already, restockToShelf: true, today: day);
                    moves.Add(new Move(line.ProductId, line.BatchId, MovementType.Return, shelf, at, Limit(reason, 30), "Return", refund.Id));
                }

                var tender = receipt.Tenders.Select(t => t.Clean(F.Tender)).FirstOrDefault(t => t is not null) ?? "";
                refund.AddRefund(SalePayment.Create(Tender(tender), refund.Total, Reference(tender, null)));
                refund.Finalise(Unique(returnNumbers, "", receipt.Number, receipt.At), at);

                _db.SaleReturns.Add(refund);
                refunded_ += refund.Total.Amount;
                refundsValue += refund.Total.Amount;
                returns++;
            }

            if (againstSale.Count > 0 && receipt.Sale is null && Math.Abs(paid - refunded_) >= 0.005m)
                refundDiffers.Add($"Receipt {receipt.Number}: the till refunded {paid:N2}, the items returned come to {refunded_:N2}");

            if (++done % Chunk == 0) await SaveAsync(ct);
        }

        await SaveAsync(ct);

        run.Report.Count("Till sales", sales);
        run.Report.Count("Till sale lines", lines);
        run.Report.Count("Refunds (each a return against the sale it refunded)", returns);
        run.Report.Count("Till lines of no quantity, left out", skipped);
        run.Report.Figure("Till sales value", saleValue);
        run.Report.Figure("Refunds value", refundsValue);
        run.Report.Check("Till sales: items as charged", itemsValue, saleValue,
            tolerance: lines * 0.005m + 0.005m);

        run.Report.Review("Receipts with no items, left out", empty);
        run.Report.Review("Receipts whose tenders do not add up to their items (the items are kept, paid in the first tender)", unbalanced);
        run.Report.Review("Refunds with no earlier sale of the item to return against, left out", orphanReturns);
        run.Report.Review("Refunds where the till paid out a different amount from the items returned", refundDiffers);

        return lineIndex;
    }

    /// <summary>
    /// A tender row as a payment. Cash handed over in the till's own
    /// currency is taken at what was tendered, so the sale works out the
    /// change as the till did.
    /// </summary>
    private static SalePayment Payment(SourceRow row)
    {
        var text = row.Text(F.Tender);
        var tender = Tender(text);
        var amount = row.Number(F.ReceiptTotal);
        var tendered = row.Number(F.Tendered);
        var rate = row.Text(F.Rate).Length == 0 ? 1m : row.Number(F.Rate);

        var offered = tender == TenderType.Cash && rate == 1m && tendered > amount ? tendered : amount;
        return SalePayment.Create(tender, Money.Of(offered), Reference(text, row.Clean(F.TenderRef)));
    }

    /// The tender as the till named it, where it says more than PharmaX's own.
    private static string? Reference(string tender, string? reference)
    {
        var named = tender.Trim();
        var plain = named.Length == 0 || named.Equals(Tender(named).DisplayName(), StringComparison.OrdinalIgnoreCase);

        return Limit(string.Join(" ", new[] { plain ? null : named, reference }.Where(x => x is not null)) is { Length: > 0 } r
            ? r : null, 100);
    }

    // ---- Deliveries and other stock movements ----------------------------

    private async Task MovementsAsync(Run run, IReadOnlyList<SourceRow> rows, List<Move> moves, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var used = (await _db.GoodsReceipts.Select(g => g.ReceiptNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deliveries = new List<(SourceRow Row, MovementType Type)>();
        var counts = new Dictionary<MovementType, int>();
        decimal deliveredValue = 0m, receivedValue = 0m, rounding = 0.005m;
        var disagreeing = new List<string>();

        var undated = 0;

        foreach (var row in rows)
        {
            var units = row.Number(F.Quantity);
            if (units == 0m) continue;

            if (row.Date(F.MoveDate) is null)
            {
                undated++;
                continue;
            }

            var type = MovementOf(row, units);
            var supplier = row.Clean(F.Supplier);

            if (type == MovementType.Receipt && supplier is not null
                && (row.Clean(F.GrNo) ?? row.Clean(F.DocNo)) is not null)
            {
                deliveries.Add((row, type));
                continue;
            }

            var product = ProductFor(run, row.Text(F.ProductCode), Math.Max(1m, row.Number(F.PackSize)), row.Clean(F.ProductName));
            var packs = Packs(Math.Abs(units), Math.Max(1m, row.Number(F.PackSize)));
            var signed = type == MovementType.Adjustment && units < 0m ? -packs : packs;

            moves.Add(new Move(product.Id, BatchFor(run, product.Id, Limit(row.Clean(F.BatchNo), 50)), type, signed,
                Utc(row.Date(F.MoveDate))!.Value,
                Limit(row.Clean(F.Comment) ?? row.Clean(F.MoveType), 30), "Imported", null));
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        // Deliveries: one goods receipt per delivery note, posted.
        var notes = deliveries
            .GroupBy(d => (Number: d.Row.Clean(F.GrNo) ?? d.Row.Clean(F.DocNo)!,
                           Supplier: d.Row.Clean(F.Supplier)!.ToUpperInvariant(),
                           Day: d.Row.Date(F.MoveDate)?.Date))
            .OrderBy(g => g.Key.Day)
            .ToList();

        var receiptsMade = 0;

        foreach (var note in notes)
        {
            var first = note.First().Row;
            var number = Limit(note.Key.Number, 30)!;
            for (var n = 2; used.Contains(number); n++) number = Limit($"{note.Key.Number}-{n}", 30)!;
            used.Add(number);

            var at = Utc(note.Key.Day ?? run.Now.ToLocalTime())!.Value;
            var receipt = GoodsReceipt.Create(number, SupplierFor(run, first.Clean(F.Supplier))!.Value,
                DateOnly.FromDateTime(note.Key.Day ?? run.Now.ToLocalTime()),
                await StaffAsync(run, first.Clean(F.Operator), ct), null, Limit(first.Clean(F.InvoiceNo), 50));
            receipt.CreatedAtUtc = at;

            foreach (var (row, _) in note)
            {
                var pack = Math.Max(1m, row.Number(F.PackSize));
                var product = ProductFor(run, row.Text(F.ProductCode), pack, row.Clean(F.ProductName));
                var packs = Packs(Math.Abs(row.Number(F.Quantity)), pack);
                var batchNumber = Limit(row.Clean(F.BatchNo), 50);
                var cost = row.Number(F.CostPrice);
                if (cost == 0m && row.Number(F.LineTotal) != 0m) cost = row.Number(F.LineTotal) / packs;

                var line = receipt.AddLine(product.Id, Limit(row.Clean(F.ProductName), 200) ?? product.Description,
                    packs, Money.Of(cost), isBatchTracked: false, batchNumber,
                    row.Date(F.BatchExpiry) is DateTime e ? DateOnly.FromDateTime(e) : null);

                moves.Add(new Move(product.Id, BatchFor(run, product.Id, batchNumber), MovementType.Receipt, packs, at,
                    null, "GoodsReceipt", receipt.Id));

                // A cost to the cent, times packs to the thousandth, can be
                // a cent a pack out. Further out than that, the files' own
                // total is not their quantity times their cost: listed, and
                // kept as quantity times cost, which is what PharmaX holds.
                var fileTotal = row.Number(F.LineTotal) != 0m ? row.Number(F.LineTotal) : line.LineTotal.Amount;
                var slack = 0.005m * packs + 0.005m;

                if (Math.Abs(fileTotal - line.LineTotal.Amount) > slack)
                {
                    disagreeing.Add($"GRN {number}: {row.Text(F.ProductCode)} {row.Text(F.ProductName)} - "
                        + $"{row.Text(F.Quantity)} at {cost:0.00##} is {line.LineTotal}, the files say {fileTotal:N2}");
                    continue;
                }

                deliveredValue += fileTotal;
                receivedValue += line.LineTotal.Amount;
                rounding += slack;
            }

            receipt.Post();
            _db.GoodsReceipts.Add(receipt);

            if (++receiptsMade % Chunk == 0) await SaveAsync(ct);
        }

        await SaveAsync(ct);

        run.Report.Count("Goods receipts (deliveries, posted)", receiptsMade);
        run.Report.Count("Goods receipt lines", deliveries.Count);
        run.Report.Count("Stock movements with no date, left out", undated);

        foreach (var (type, count) in counts.OrderBy(c => c.Key))
            run.Report.Count($"Stock movements: {Describe(type)}", count);

        run.Report.Check("Deliveries at cost", deliveredValue, receivedValue, tolerance: rounding);
        run.Report.Review("Delivery lines whose total in the files is not their quantity times their cost "
                          + "(kept at quantity times cost; check the invoice)", disagreeing);
    }

    /// <summary>
    /// What a movement row was, from what the files call it: a delivery, a
    /// return to the supplier, a transfer, a write-off, or - where the name
    /// says none of these - an adjustment the way its quantity points.
    /// </summary>
    private static MovementType MovementOf(SourceRow row, decimal units)
    {
        var said = $"{row.Text(F.Category)} {row.Text(F.MoveType)}".ToUpperInvariant();

        bool Says(params string[] words) => words.Any(w => said.Contains(w, StringComparison.Ordinal));

        return Says("RETURN", "CREDIT") ? (row.Clean(F.Supplier) is null ? MovementType.Disposal : MovementType.ReturnToSupplier)
            : Says("TRANSFER IN", "IBT IN") ? MovementType.TransferIn
            : Says("TRANSFER OUT", "IBT OUT") ? MovementType.TransferOut
            : Says("WRITE", "BREAK", "EXPIR", "DISPOS", "DAMAGE") ? MovementType.Disposal
            : Says("ADJUST", "JOURNAL") ? MovementType.Adjustment
            : Says("RECEIPT", "RECEIV", "PURCHASE", "DELIVER", "GRN") && units > 0m ? MovementType.Receipt
            : MovementType.Adjustment;
    }

    private static string Describe(MovementType type) => type switch
    {
        MovementType.Receipt => "received",
        MovementType.ReturnToSupplier => "returned to the supplier",
        MovementType.Disposal => "written off",
        MovementType.TransferIn => "transferred in",
        MovementType.TransferOut => "transferred out",
        _ => "adjusted"
    };

    // ---- Stock counts ----------------------------------------------------

    /// <summary>
    /// Counts as posted stocktakes: one per count the files number, or one
    /// a day where the numbers are nearly one a line - a cycle count
    /// numbers each item, not each session.
    /// </summary>
    private async Task CountsAsync(Run run, IReadOnlyList<SourceRow> rows, List<Move> moves, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var ids = rows.Select(r => r.Clean(F.CountId)).Where(i => i is not null).Distinct().Count();
        var byId = ids > 0 && ids * 2 <= rows.Count;

        var used = (await _db.Stocktakes.Select(s => s.StocktakeNumber).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var counts = rows
            .Where(r => r.Date(F.CountDate) is not null || byId)
            .GroupBy(r => byId ? r.Text(F.CountId) : r.Date(F.CountDate)!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(g => g.Min(r => r.Date(F.CountDate) ?? DateTime.MaxValue))
            .ToList();

        int made = 0, lines = 0;
        decimal variance = 0m;

        foreach (var count in counts)
        {
            var first = count.First();
            var number = Limit(count.Key, 30)!;
            for (var n = 2; used.Contains(number); n++) number = Limit($"{count.Key}-{n}", 30)!;
            used.Add(number);

            var at = Utc(first.Date(F.CountDate) ?? run.Now.ToLocalTime())!.Value;
            var by = await StaffAsync(run, first.Clean(F.Operator), ct);
            var stocktake = Stocktake.Open(number, BranchId, by, Limit(first.Clean(F.Comment), 200) ?? "Cycle count", at);
            stocktake.CreatedAtUtc = at;

            foreach (var row in count)
            {
                var pack = Math.Max(1m, row.Number(F.PackSize));
                var product = ProductFor(run, row.Text(F.ProductCode), pack, row.Clean(F.ProductName));
                var expected = Math.Round(row.Number(F.Expected) / pack, 3);
                var counted = Math.Round(Math.Max(0m, row.Number(F.Counted)) / pack, 3);

                var line = stocktake.AddLine(StocktakeLine.For(product.Id,
                    Limit(row.Clean(F.ProductName), 200) ?? product.Description, expected,
                    Money.Of(row.Number(F.CostPrice))));
                stocktake.RecordCount(line.LineNo, counted);

                if (counted != expected)
                    moves.Add(new Move(product.Id, null, MovementType.StockTake, counted - expected, at,
                        null, "Stocktake", stocktake.Id));

                lines++;
            }

            stocktake.Post(by, at, decimal.MaxValue);
            variance += stocktake.NetVarianceValue.Amount;
            _db.Stocktakes.Add(stocktake);

            if (++made % Chunk == 0) await SaveAsync(ct);
        }

        await SaveAsync(ct);

        run.Report.Count("Stocktakes (counts, posted)", made);
        run.Report.Count("Stocktake lines", lines);
        run.Report.Figure("Stocktakes: net variance at cost", variance);
        run.Report.Check("Stock count lines", rows.Count, lines);
    }

    // ---- Price changes and till overrides --------------------------------

    /// <summary>
    /// Both to the audit trail, where PharmaX keeps its own: a price change
    /// as the product's, an override as the sale line's where the line it
    /// was rung up on can be found.
    /// </summary>
    private async Task PricesAsync(Run run, IReadOnlyList<SourceRow> changes, IReadOnlyList<SourceRow> overrides,
        Dictionary<(Guid, DateOnly), List<(Guid LineId, DateTime At)>> lineIndex, CancellationToken ct)
    {
        var till = Limit(_options.Workstation.TillNumber, 30) ?? "1";

        foreach (var row in changes)
        {
            if (row.When(F.ChangeDate, F.ChangeTime) is not DateTime when) continue;

            var product = ProductFor(run, row.Text(F.ProductCode), Math.Max(1m, row.Number(F.PackSize)), row.Clean(F.ProductName));
            var reason = $"{Money.Of(row.Number(F.OldPrice))} to {Money.Of(row.Number(F.NewPrice))}";

            if (row.Number(F.OldCost) != row.Number(F.NewCost))
                reason += $"; cost {Money.Of(row.Number(F.OldCost))} to {Money.Of(row.Number(F.NewCost))}";

            _db.AuditEntries.Add(AuditEntry.Create(Utc(when)!.Value, await StaffAsync(run, row.Clean(F.Operator), ct),
                till, "inv.price.change", "Product", product.Id, reason: reason));
        }

        await SaveAsync(ct);

        var matched = 0;

        foreach (var row in overrides)
        {
            if (row.When(F.OverrideDate, F.OverrideTime) is not DateTime when) continue;

            var at = Utc(when)!.Value;
            var product = ProductFor(run, row.Text(F.ProductCode), Math.Max(1m, row.Number(F.PackSize)), row.Clean(F.ProductName));

            // The line rung up nearest the override, within the half-hour.
            var line = lineIndex.GetValueOrDefault((product.Id, DateOnly.FromDateTime(when)))?
                .Where(l => Math.Abs((l.At - at).TotalMinutes) <= 30)
                .OrderBy(l => Math.Abs((l.At - at).TotalMinutes))
                .Select(l => (Guid?)l.LineId)
                .FirstOrDefault();

            if (line is not null) matched++;

            var reason = $"{Money.Of(row.Number(F.SellingPrice))} charged as {Money.Of(row.Number(F.ChargedAs))}"
                         + (row.Clean(F.AuthorisedBy) is string by ? $", authorised by {by}" : "");

            _db.AuditEntries.Add(AuditEntry.Create(at, await StaffAsync(run, row.Clean(F.Operator), ct), till,
                "pos.sale.discount", line is null ? "Product" : "SaleLine", line ?? product.Id, reason: Limit(reason, 500)));
        }

        await SaveAsync(ct);

        run.Report.Count("Price changes (in the audit trail)", changes.Count);
        run.Report.Count("Till price overrides (in the audit trail)", overrides.Count);
        run.Report.Count("Till price overrides matched to the sale line they were rung up on", matched);
    }

    // ---- The stock ledger ------------------------------------------------

    /// <summary>
    /// Every movement, written backwards from the stock on the shelf so
    /// that each product's last "on hand after" is what is there now. A
    /// product with stock and no history at all gets one opening entry
    /// saying where its stock came from.
    /// </summary>
    private async Task LedgerAsync(Run run, List<Move> moves, CancellationToken ct)
    {
        var onHand = run.Products.Values.DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => p.OnHand);
        var written = 0;

        foreach (var product in moves.GroupBy(m => m.ProductId))
        {
            var balance = onHand.GetValueOrDefault(product.Key);

            foreach (var move in product.OrderByDescending(m => m.AtUtc))
            {
                var movement = StockMovement.Record(move.ProductId, move.BatchId, move.Type, move.Quantity,
                    balance, move.AtUtc, move.Reason, move.Source, move.SourceId);
                movement.CreatedAtUtc = move.AtUtc;
                _db.StockMovements.Add(movement);

                balance -= movement.Quantity;
                if (++written % (Chunk * 10) == 0) await SaveAsync(ct);
            }
        }

        var moved = moves.Select(m => m.ProductId).ToHashSet();

        foreach (var (productId, stock) in OpeningStock.Where(o => !moved.Contains(o.Key)))
        {
            var balance = 0m;

            foreach (var (packs, batchId) in stock)
            {
                balance += packs;
                var movement = StockMovement.Record(productId, batchId, MovementType.Adjustment, packs, balance,
                    run.Now, Opening, "Import");
                _db.StockMovements.Add(movement);
                written++;
            }
        }

        await SaveAsync(ct);
        run.Report.Count("Stock ledger entries", written);
    }

    // ---- Lookups ---------------------------------------------------------

    /// <summary>
    /// The product a history row names, at its pack size where the files
    /// split it into one product a pack; added inactive, under its code,
    /// where the product list never held it.
    /// </summary>
    private ProductRef ProductFor(Run run, string code, decimal? pack, string? name)
    {
        code = code.Trim();

        if (pack is decimal p && run.Products.TryGetValue($"{code}|{p:0.###}", out var split)) return split;
        if (run.Products.TryGetValue(code, out var found)) return found;

        var safe = Limit(code.Length == 0 ? "UNKNOWN" : code.ToUpperInvariant(), 30)!;
        var product = Product.Create(safe, Limit(name, 200) ?? $"Product {safe}", Money.Zero());
        product.Deactivate();
        _db.Products.Add(product);

        var reference = Ref(product, 0m);
        run.Products[code] = reference;
        run.NewProducts.Add($"{safe} {product.Description}");
        return reference;
    }

    private static Guid? BatchFor(Run run, Guid productId, string? batchNumber) =>
        batchNumber is not null && run.Batches.TryGetValue($"{productId}|{batchNumber}", out var id) ? id : null;

    /// <summary>
    /// Units as packs, to the thousandth PharmaX holds stock to; never
    /// nothing, because a line of nothing is refused.
    /// </summary>
    private static decimal Packs(decimal units, decimal pack) =>
        Math.Max(0.001m, Math.Round(units / Math.Max(1m, pack), 3, MidpointRounding.AwayFromZero));
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.History;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.History;

/// <summary>
/// One calendar day of the records a pharmacist looks back over.
/// The day is this computer's local day, matching the date the screen shows.
/// Each document is followed by the products and the other facts on it,
/// with the time it happened to the second.
/// </summary>
public sealed class DayHistoryStore : IDayHistoryStore
{
    private readonly PharmaXDbContext _db;

    public DayHistoryStore(PharmaXDbContext db) => _db = db;

    public async Task<IReadOnlyList<HistoryLine>> ForAsync(HistorySubject subject,
        DateOnly day, CancellationToken ct = default)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var end = start.AddDays(1);

        return subject switch
        {
            HistorySubject.Received => await Received(day, ct),
            HistorySubject.Orders => await Orders(day, ct),
            HistorySubject.Collected => await Dispensed(start, end, collected: true, ct),
            HistorySubject.Dispensed => await Dispensed(start, end, collected: false, ct),
            HistorySubject.Patients => await Patients(start, end, ct),
            HistorySubject.Returns => await Returns(start, end, ct),
            HistorySubject.Products => await Products(start, end, ct),
            HistorySubject.Sales => await Sales(start, end, ct),
            HistorySubject.Accounts => await Accounts(day, ct),
            _ => Array.Empty<HistoryLine>()
        };
    }

    private async Task<IReadOnlyList<HistoryLine>> Received(DateOnly day, CancellationToken ct)
    {
        var receipts = await _db.GoodsReceipts.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.ReceivedOn == day)
            .OrderBy(r => r.ReceiptNumber)
            .ToListAsync(ct);

        var suppliers = await SupplierNames(receipts.Select(r => r.SupplierId), ct);
        var lines = new List<HistoryLine>();

        foreach (var receipt in receipts)
        {
            suppliers.TryGetValue(receipt.SupplierId, out var supplier);
            var invoice = string.IsNullOrWhiteSpace(receipt.SupplierInvoiceNumber)
                ? null
                : $"invoice {receipt.SupplierInvoiceNumber}";

            lines.Add(Document(receipt.CreatedAtUtc, day, receipt.ReceiptNumber,
                Join(supplier, invoice, receipt.IsPosted ? "posted" : "not posted"),
                receipt.Lines.Sum(l => l.UnitCost.Amount * l.Quantity)));

            foreach (var line in receipt.Lines.OrderBy(l => l.LineNo))
            {
                var batch = string.IsNullOrWhiteSpace(line.BatchNumber)
                    ? null
                    : line.ExpiryDate is DateOnly expiry
                        ? $"batch {line.BatchNumber} exp {expiry:yyyy-MM-dd}"
                        : $"batch {line.BatchNumber}";

                lines.Add(Item(
                    Join($"{Qty(line.Quantity)} x {line.Description}", batch,
                        $"@ {MoneyText(line.UnitCost.Amount)}"),
                    line.UnitCost.Amount * line.Quantity));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Orders(DateOnly day, CancellationToken ct)
    {
        var orders = await _db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.OrderedOn == day)
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(ct);

        var suppliers = await SupplierNames(orders.Select(o => o.SupplierId), ct);
        var lines = new List<HistoryLine>();

        foreach (var order in orders)
        {
            suppliers.TryGetValue(order.SupplierId, out var supplier);
            var expected = order.ExpectedOn is DateOnly on
                ? $"expected {on:dd/MM/yyyy}"
                : null;

            lines.Add(Document(order.CreatedAtUtc, day, order.OrderNumber,
                Join(supplier, order.Status.ToString(), expected),
                order.Lines.Sum(l => l.UnitCost.Amount * l.QuantityOrdered),
                owner: order.SupplierId));

            foreach (var line in order.Lines.OrderBy(l => l.LineNo))
            {
                var received = line.QuantityReceived > 0m
                    ? $"{Qty(line.QuantityReceived)} received"
                    : "none received";

                lines.Add(Item(
                    Join($"{Qty(line.QuantityOrdered)} ordered x {line.Description}",
                        received, $"@ {MoneyText(line.UnitCost.Amount)}"),
                    line.UnitCost.Amount * line.QuantityOrdered));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Dispensed(DateTime start, DateTime end,
        bool collected, CancellationToken ct)
    {
        var rows = await _db.Dispensings.AsNoTracking()
            .Include(d => d.Lines)
            .Where(d => collected
                ? d.CollectedAtUtc >= start && d.CollectedAtUtc < end
                : d.IssuedAtUtc >= start && d.IssuedAtUtc < end)
            .ToListAsync(ct);

        rows = collected
            ? rows.OrderBy(d => d.CollectedAtUtc).ToList()
            : rows.OrderBy(d => d.IssuedAtUtc).ToList();

        var patients = await PatientNames(rows.Select(d => d.PatientId), ct);
        var scriptIds = rows.Select(d => d.ScriptId).Distinct().ToList();
        var scripts = scriptIds.Count == 0
            ? new List<Script>()
            : await _db.Scripts.AsNoTracking()
                .Include(s => s.Items)
                .Where(s => scriptIds.Contains(s.Id))
                .ToListAsync(ct);
        var byScript = scripts.ToDictionary(s => s.Id);

        var people = await Names(rows.SelectMany(d => new[]
        {
            d.PharmacistId ?? Guid.Empty, d.CapturedBy
        }), ct);

        var lines = new List<HistoryLine>();

        foreach (var dispensing in rows)
        {
            var when = collected
                ? dispensing.CollectedAtUtc ?? dispensing.IssuedAtUtc ?? dispensing.CreatedAtUtc
                : dispensing.IssuedAtUtc ?? dispensing.CreatedAtUtc;

            patients.TryGetValue(dispensing.PatientId, out var patient);
            byScript.TryGetValue(dispensing.ScriptId, out var script);
            var whoId = dispensing.PharmacistId ?? dispensing.CapturedBy;
            people.TryGetValue(whoId, out var who);

            var number = dispensing.DocumentNumber.Length == 0
                ? "(not numbered)"
                : dispensing.DocumentNumber;

            lines.Add(Document(when, number,
                Join(patient,
                    script is null ? null : $"script {script.ScriptNumber}",
                    script is null ? null : script.PrescriberName,
                    dispensing.RepeatNumber > 0 ? $"repeat {dispensing.RepeatNumber}" : null,
                    who is null ? null : $"by {who}",
                    dispensing.Status == DispensingStatus.Reversed
                        ? Join("reversed", dispensing.ReversalReason)
                        : null),
                dispensing.Lines.Sum(l => l.CoPayment.Amount),
                dispensing.ScriptId, dispensing.PatientId));

            var matched = new HashSet<Guid>();

            if (script is not null)
            {
                foreach (var item in script.Items.OrderBy(i => i.LineNo))
                {
                    var given = dispensing.Lines
                        .FirstOrDefault(l => l.ScriptItemId == item.Id);
                    if (given is not null) matched.Add(given.Id);

                    lines.Add(Item(ScriptProduct(item, given),
                        given?.CoPayment.Amount));
                }
            }

            foreach (var line in dispensing.Lines.OrderBy(l => l.LineNo))
            {
                if (matched.Contains(line.Id)) continue;
                lines.Add(Item(GivenProduct(line), line.CoPayment.Amount));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Patients(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var rows = await _db.Patients.AsNoTracking()
            .Where(p => p.CreatedAtUtc >= start && p.CreatedAtUtc < end)
            .OrderBy(p => p.Surname)
            .ThenBy(p => p.FirstNames)
            .ToListAsync(ct);

        var schemeIds = rows
            .Where(p => p.MedicalAidSchemeId is not null)
            .Select(p => p.MedicalAidSchemeId!.Value)
            .Distinct()
            .ToList();
        var schemes = schemeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.MedicalAidSchemes.AsNoTracking()
                .Where(s => schemeIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var lines = new List<HistoryLine>();

        foreach (var patient in rows)
        {
            lines.Add(Document(patient.CreatedAtUtc, patient.FileNumber, patient.FullName,
                null, patient.Id));

            if (patient.DateOfBirth is DateOnly born)
                lines.Add(Item($"Born {born:dd/MM/yyyy}"));
            if (!string.IsNullOrWhiteSpace(patient.IdNumber))
                lines.Add(Item($"ID {patient.IdNumber}"));
            if (!string.IsNullOrWhiteSpace(patient.Telephone))
                lines.Add(Item(patient.Telephone));
            if (!string.IsNullOrWhiteSpace(patient.Email))
                lines.Add(Item(patient.Email));
            if (!string.IsNullOrWhiteSpace(patient.Address))
                lines.Add(Item(patient.Address));

            if (patient.MedicalAidSchemeId is Guid schemeId
                && schemes.TryGetValue(schemeId, out var scheme))
            {
                lines.Add(Item(Join(scheme, patient.MembershipNumber,
                    string.IsNullOrWhiteSpace(patient.DependantCode)
                        ? null
                        : $"dependant {patient.DependantCode}")));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Returns(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var rows = await _db.SaleReturns.AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.Refunds)
            .Where(r => r.OccurredAtUtc >= start && r.OccurredAtUtc < end)
            .OrderBy(r => r.OccurredAtUtc)
            .ToListAsync(ct);

        var people = await Names(rows.Select(r => r.OperatorId), ct);
        var lines = new List<HistoryLine>();

        foreach (var credit in rows)
        {
            people.TryGetValue(credit.OperatorId, out var who);
            var tenders = string.Join(", ", credit.Refunds.Select(p =>
                $"{p.Tender.DisplayName()} {MoneyText(p.Amount.Amount)}"));

            lines.Add(Document(credit.OccurredAtUtc, credit.DocumentNumber,
                Join(ReturnName(credit.Kind),
                    $"against {credit.OriginalDocumentNumber}",
                    credit.Reason,
                    who is null ? null : $"by {who}",
                    tenders),
                credit.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity)));

            foreach (var line in credit.Lines.OrderBy(l => l.LineNo))
            {
                lines.Add(Item(
                    Join($"{Qty(line.Quantity)} x {line.Description}",
                        line.RestockToShelf ? "back on the shelf" : "written off",
                        line.LineReason),
                    line.UnitPrice.Amount * line.Quantity));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Products(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var audits = await _db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == "Product" && a.Outcome == "Applied"
                        && a.OccurredAtUtc >= start && a.OccurredAtUtc < end)
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync(ct);

        var audited = audits.Select(a => a.EntityId).Distinct().ToList();
        var products = audited.Count == 0
            ? await _db.Products.AsNoTracking()
                .Where(p => p.UpdatedAtUtc >= start && p.UpdatedAtUtc < end)
                .OrderBy(p => p.Description)
                .ToListAsync(ct)
            : await _db.Products.AsNoTracking()
                .Where(p => (p.UpdatedAtUtc >= start && p.UpdatedAtUtc < end)
                            || audited.Contains(p.Id))
                .OrderBy(p => p.Description)
                .ToListAsync(ct);

        var people = await Names(audits.Select(a => a.UserId), ct);
        var byProduct = audits.GroupBy(a => a.EntityId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<HistoryLine>();

        foreach (var product in products)
        {
            var opened = product.CreatedAtUtc >= start && product.CreatedAtUtc < end
                         && (product.UpdatedAtUtc - product.CreatedAtUtc).Duration()
                            < TimeSpan.FromSeconds(2);
            var when = product.UpdatedAtUtc == default ? product.CreatedAtUtc : product.UpdatedAtUtc;

            lines.Add(Document(when, product.Code,
                Join(product.Description, opened ? "opened" : "changed"),
                product.RetailPrice.Amount, product.Id));

            var facts = new List<string>
            {
                $"cost {MoneyText(product.CostPrice.Amount)}",
                $"on hand {Qty(product.QuantityOnHand)}"
            };
            if (product.Schedule > 0) facts.Add($"schedule {product.Schedule}");
            if (!string.IsNullOrWhiteSpace(product.PackSize)) facts.Add($"pack {product.PackSize}");
            if (product.UnitsPerPack is decimal units) facts.Add($"{Qty(units)} per pack");
            if (!string.IsNullOrWhiteSpace(product.GenericName)) facts.Add(product.GenericName);
            if (!product.IsActive) facts.Add("inactive");
            lines.Add(Item(string.Join(" · ", facts)));

            if (!byProduct.TryGetValue(product.Id, out var changes)) continue;

            foreach (var change in changes)
            {
                people.TryGetValue(change.UserId, out var who);
                lines.Add(Item(Join(AuditLabel(change.Action), change.Reason,
                    who is null ? null : $"by {who}"), at: change.OccurredAtUtc));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Sales(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.Status == SaleStatus.Finalised
                        && s.OccurredAtUtc >= start && s.OccurredAtUtc < end)
            .OrderBy(s => s.OccurredAtUtc)
            .ToListAsync(ct);

        var dispensingIds = sales
            .Where(s => s.DispensingId is not null)
            .Select(s => s.DispensingId!.Value)
            .Distinct()
            .ToList();
        var dispensings = dispensingIds.Count == 0
            ? new List<Domain.Dispensing.Dispensing>()
            : await _db.Dispensings.AsNoTracking()
                .Include(d => d.Lines)
                .Where(d => dispensingIds.Contains(d.Id))
                .ToListAsync(ct);
        var byDispensing = dispensings.ToDictionary(d => d.Id);

        var scriptIds = dispensings.Select(d => d.ScriptId).Distinct().ToList();
        var scripts = scriptIds.Count == 0
            ? new Dictionary<Guid, Script>()
            : await _db.Scripts.AsNoTracking()
                .Include(s => s.Items)
                .Where(s => scriptIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        var people = await Names(sales.Select(s => s.OperatorId), ct);
        var patients = await PatientNames(
            sales.Where(s => s.PatientId is not null).Select(s => s.PatientId!.Value), ct);
        var lines = new List<HistoryLine>();

        foreach (var sale in sales)
        {
            people.TryGetValue(sale.OperatorId, out var who);
            var patient = sale.PatientId is Guid patientId
                          && patients.TryGetValue(patientId, out var name)
                ? name
                : null;

            string? scriptNo = null;
            Domain.Dispensing.Dispensing? dispensing = null;
            Script? script = null;

            if (sale.DispensingId is Guid dispensingId
                && byDispensing.TryGetValue(dispensingId, out dispensing))
            {
                if (scripts.TryGetValue(dispensing.ScriptId, out script))
                    scriptNo = $"script {script.ScriptNumber}";
            }

            var tenders = string.Join(", ", sale.Payments.Select(p =>
                $"{p.Tender.DisplayName()} {MoneyText(p.Amount.Amount)}"));
            var change = sale.ChangeGiven.Amount > 0m
                ? $"change {MoneyText(sale.ChangeGiven.Amount)}"
                : null;

            lines.Add(Document(sale.OccurredAtUtc, sale.DocumentNumber,
                Join(patient, scriptNo, who is null ? null : $"by {who}", tenders, change),
                sale.Lines.Where(l => !l.IsVoided).Sum(l => l.LineTotal.Amount)));

            var matched = new HashSet<Guid>();

            if (script is not null && dispensing is not null)
            {
                foreach (var item in script.Items.OrderBy(i => i.LineNo))
                {
                    var given = dispensing.Lines
                        .FirstOrDefault(l => l.ScriptItemId == item.Id);
                    if (given is not null) matched.Add(given.Id);
                    lines.Add(Item(ScriptProduct(item, given), given?.CoPayment.Amount));
                }

                foreach (var given in dispensing.Lines.OrderBy(l => l.LineNo))
                {
                    if (matched.Contains(given.Id)) continue;
                    lines.Add(Item(GivenProduct(given), given.CoPayment.Amount));
                }
            }

            foreach (var line in sale.Lines.OrderBy(l => l.LineNo))
            {
                if (line.IsVoided) continue;
                if (script is not null && line.IsScript) continue;

                lines.Add(Item($"{Qty(line.Quantity)} x {line.Description}",
                    line.LineTotal.Amount));
            }
        }

        return lines;
    }

    private async Task<IReadOnlyList<HistoryLine>> Accounts(DateOnly day, CancellationToken ct)
    {
        var rows = await _db.AccountTransactions.AsNoTracking()
            .Where(t => t.OccurredOn == day)
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync(ct);

        var accountIds = rows.Select(t => t.AccountId).Distinct().ToList();
        var accounts = new Dictionary<Guid, (string Number, string Name)>();

        if (accountIds.Count > 0)
        {
            var found = await _db.Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumber, a.Name })
                .ToListAsync(ct);

            foreach (var account in found)
                accounts[account.Id] = (account.AccountNumber, account.Name);
        }

        var lines = new List<HistoryLine>();

        foreach (var movement in rows)
        {
            accounts.TryGetValue(movement.AccountId, out var account);
            var signed = movement.Kind == AccountEntryKind.Charge
                ? movement.Amount.Amount
                : -movement.Amount.Amount;

            lines.Add(Document(movement.CreatedAtUtc, day,
                account.Number ?? movement.Reference ?? "",
                Join(account.Name, KindName(movement.Kind),
                    movement.Tender?.DisplayName()),
                signed, movement.AccountId));

            if (!string.IsNullOrWhiteSpace(movement.Reference))
                lines.Add(Item($"Reference {movement.Reference}"));
            if (!string.IsNullOrWhiteSpace(movement.Narration))
                lines.Add(Item(movement.Narration));
        }

        return lines;
    }

    private async Task<Dictionary<Guid, string>> Names(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, string>();

        return await _db.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private async Task<Dictionary<Guid, string>> SupplierNames(IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, string>();

        return await _db.Suppliers.AsNoTracking()
            .Where(s => wanted.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }

    private async Task<Dictionary<Guid, string>> PatientNames(IEnumerable<Guid> ids,
        CancellationToken ct)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, string>();

        var rows = await _db.Patients.AsNoTracking()
            .Where(p => wanted.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstNames, p.Surname })
            .ToListAsync(ct);

        return rows.ToDictionary(p => p.Id, p => $"{p.FirstNames} {p.Surname}".Trim());
    }

    /// Prescribed line, with what was actually handed over where it differs.
    private static string ScriptProduct(ScriptItem item, DispensingLine? given)
    {
        var prescribed = $"{Qty(item.Quantity)} x {item.Description}";

        if (given is null)
            return Join(prescribed, item.Directions, "not dispensed");

        var givenText = given.Quantity == item.Quantity && given.Description == item.Description
            ? null
            : $"given {Qty(given.Quantity)} x {given.Description}";

        return Join(prescribed, givenText, BatchOf(given), item.Directions,
            given.IsSubstitution ? "substitution" : null,
            given.Owed is decimal owed && owed > 0m ? $"{Qty(owed)} owed" : null);
    }

    private static string GivenProduct(DispensingLine line) =>
        Join($"{Qty(line.Quantity)} x {line.Description}", BatchOf(line), line.Directions,
            line.IsSubstitution ? "substitution" : null,
            line.Owed is decimal owed && owed > 0m ? $"{Qty(owed)} owed" : null);

    private static string? BatchOf(DispensingLine line)
    {
        if (string.IsNullOrWhiteSpace(line.BatchNumber) && line.BatchExpiry is null)
            return null;

        return line.BatchExpiry is DateOnly expiry
            ? $"batch {line.BatchNumber} exp {expiry:yyyy-MM-dd}"
            : $"batch {line.BatchNumber}";
    }

    private static HistoryLine Document(DateTime utc, string reference, string detail,
        decimal? amount, Guid? id = null, Guid? owner = null) =>
        new(reference, detail, Clock(utc), amount is decimal value ? MoneyText(value) : null,
            Id: id, OwnerId: owner);

    private static HistoryLine Document(DateTime utc, DateOnly day, string reference,
        string detail, decimal? amount, Guid? owner = null) =>
        new(reference, detail, ClockOrDay(utc, day),
            amount is decimal value ? MoneyText(value) : null, OwnerId: owner);

    private static HistoryLine Item(string detail, decimal? amount = null, DateTime? at = null) =>
        new("", detail, at is DateTime when ? Clock(when) : null,
            amount is decimal value ? MoneyText(value) : null, true);

    private static string Clock(DateTime utc) =>
        utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture);

    private static string ClockOrDay(DateTime utc, DateOnly day) =>
        utc.Year >= 2000 ? Clock(utc) : day.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

    private static string MoneyText(decimal amount) =>
        amount.ToString("N2", CultureInfo.CurrentCulture);

    private static string Qty(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.CurrentCulture);

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string ReturnName(ReturnKind kind) => kind switch
    {
        ReturnKind.GoodsReturn => "Goods return",
        ReturnKind.CreditNote => "Credit note",
        _ => kind.ToString()
    };

    private static string KindName(AccountEntryKind kind) => kind switch
    {
        AccountEntryKind.WriteOff => "Write-off",
        _ => kind.ToString()
    };

    private static string AuditLabel(string action) => action switch
    {
        "inv.price.change" => "Price",
        "inv.product.create" => "Created",
        "inv.product.deactivate" => "Deactivated",
        "inv.product.reactivate" => "Reactivated",
        "inv.stock.adjust" => "Stock",
        "inv.product.maintain" => "Changed",
        _ => action
    };
}

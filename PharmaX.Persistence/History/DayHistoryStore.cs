using Microsoft.EntityFrameworkCore;
using PharmaX.Application.History;
using PharmaX.Domain.Sales;

namespace PharmaX.Persistence.History;

/// <summary>
/// One calendar day of the records a pharmacist looks back over.
/// The day is this computer's local day, matching the date the screen shows.
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
        var rows = await (
            from r in _db.GoodsReceipts
            join s in _db.Suppliers on r.SupplierId equals s.Id
            where r.ReceivedOn == day
            orderby r.ReceiptNumber
            select new { r.ReceiptNumber, s.Name, r.SupplierInvoiceNumber })
            .ToListAsync(ct);

        return rows.Select(r => new HistoryLine(r.ReceiptNumber,
            string.IsNullOrWhiteSpace(r.SupplierInvoiceNumber)
                ? r.Name
                : $"{r.Name}, invoice {r.SupplierInvoiceNumber}")).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Orders(DateOnly day, CancellationToken ct)
    {
        var rows = await (
            from o in _db.PurchaseOrders
            join s in _db.Suppliers on o.SupplierId equals s.Id
            where o.OrderedOn == day
            orderby o.OrderNumber
            select new { o.OrderNumber, s.Name, o.Status })
            .ToListAsync(ct);

        return rows.Select(o => new HistoryLine(o.OrderNumber,
            $"{o.Name}, {o.Status}")).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Dispensed(DateTime start, DateTime end,
        bool collected, CancellationToken ct)
    {
        var rows = await (
            from d in _db.Dispensings
            join p in _db.Patients on d.PatientId equals p.Id
            where collected
                ? d.CollectedAtUtc >= start && d.CollectedAtUtc < end
                : d.IssuedAtUtc >= start && d.IssuedAtUtc < end
            orderby d.DocumentNumber
            select new { d.DocumentNumber, p.FirstNames, p.Surname })
            .ToListAsync(ct);

        return rows.Select(d => new HistoryLine(
            d.DocumentNumber.Length == 0 ? "(not numbered)" : d.DocumentNumber,
            $"{d.FirstNames} {d.Surname}".Trim())).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Patients(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var rows = await _db.Patients
            .Where(p => p.CreatedAtUtc >= start && p.CreatedAtUtc < end)
            .OrderBy(p => p.Surname)
            .Select(p => new { p.FileNumber, p.FirstNames, p.Surname })
            .ToListAsync(ct);

        return rows.Select(p => new HistoryLine(
            $"{p.FirstNames} {p.Surname}".Trim(),
            p.FileNumber)).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Returns(DateTime start, DateTime end,
        CancellationToken ct) 
    {
        var rows = await _db.SaleReturns
            .Where(r => r.OccurredAtUtc >= start && r.OccurredAtUtc < end)
            .OrderBy(r => r.DocumentNumber)
            .Select(r => new { r.DocumentNumber, r.OriginalDocumentNumber })
            .ToListAsync(ct);

        return rows.Select(r => new HistoryLine(r.DocumentNumber,
            $"Against {r.OriginalDocumentNumber}")).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Products(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var rows = await _db.Products
            .Where(p => p.UpdatedAtUtc >= start && p.UpdatedAtUtc < end)
            .OrderBy(p => p.Description)
            .Select(p => new { p.Code, p.Description })
            .ToListAsync(ct);

        return rows.Select(p => new HistoryLine(p.Code, p.Description)).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Sales(DateTime start, DateTime end,
        CancellationToken ct)
    {
        var rows = await _db.Sales
            .Where(s => s.Status == SaleStatus.Finalised
                        && s.OccurredAtUtc >= start && s.OccurredAtUtc < end)
            .OrderBy(s => s.DocumentNumber)
            .Select(s => s.DocumentNumber)
            .ToListAsync(ct);

        return rows.Select(n => new HistoryLine(n, "Sale")).ToList();
    }

    private async Task<IReadOnlyList<HistoryLine>> Accounts(DateOnly day, CancellationToken ct)
    {
        var rows = await (
            from t in _db.AccountTransactions
            join a in _db.Accounts on t.AccountId equals a.Id
            where t.OccurredOn == day
            orderby a.AccountNumber
            select new { a.AccountNumber, a.Name, t.Kind })
            .ToListAsync(ct);

        return rows.Select(t => new HistoryLine(t.AccountNumber,
            $"{t.Name}, {t.Kind}")).ToList();
    }
}

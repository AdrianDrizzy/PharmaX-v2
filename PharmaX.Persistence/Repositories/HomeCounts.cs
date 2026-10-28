using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Home;
using PharmaX.Application.Patients;
using PharmaX.Application.Reporting;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Persistence.Repositories;

/// <summary>The home screen's figures, counted in the database.</summary>
public sealed class HomeCounts : IHomeCounts
{
    private readonly PharmaXDbContext _db;
    private readonly IClock _clock;

    public HomeCounts(PharmaXDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<HomeCountsDto> ReadAsync(
        ProductScope products = ProductScope.All,
        CancellationToken ct = default)
    {
        var overTheCounter = products == ProductScope.OverTheCounter;

        // Today as the till and Daily Sales count it: the local day.
        var start = _clock.UtcNow.ToLocalTime().Date.ToUniversalTime();
        var end = start.AddDays(1);
        var day = DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime());

        var catalogue = _db.Products.Where(p => p.IsActive
            && (!overTheCounter || p.Schedule < 3));

        // People, not files: the same phone number and address entered
        // twice is one patient. Matched in memory, because the matching
        // normalises what SQL cannot; seven thousand short rows.
        var files = await _db.Patients
            .Where(p => p.IsActive)
            .Select(p => new { p.Telephone, p.Address })
            .ToListAsync(ct);

        // One after another: a DbContext runs one query at a time.
        return new HomeCountsDto(
            Patients: PatientIdentity.CountPeople(
                files.Select(f => (f.Telephone, f.Address))),
            PatientFiles: files.Count,
            Products: await catalogue.CountAsync(ct),
            OpenPurchaseOrders: await _db.PurchaseOrders.CountAsync(o =>
                o.Status == PurchaseOrderStatus.Draft
                || o.Status == PurchaseOrderStatus.Sent
                || o.Status == PurchaseOrderStatus.PartiallyReceived, ct),
            SalesToday: await _db.Sales.CountAsync(s =>
                s.Status == SaleStatus.Finalised
                && s.OccurredAtUtc >= start && s.OccurredAtUtc < end, ct),
            ScriptsToday: await _db.Dispensings.CountAsync(d =>
                d.Status == DispensingStatus.Issued
                && d.IssuedAtUtc >= start && d.IssuedAtUtc < end, ct),
            PatientsAddedToday: await _db.Patients.CountAsync(p => p.IsActive
                && p.CreatedAtUtc >= start && p.CreatedAtUtc < end, ct),
            ProductsAddedToday: await catalogue.CountAsync(p =>
                p.CreatedAtUtc >= start && p.CreatedAtUtc < end, ct),
            ReturnsToday: await _db.SaleReturns.CountAsync(r =>
                r.Status == ReturnStatus.Finalised
                && r.OccurredAtUtc >= start && r.OccurredAtUtc < end, ct),
            DeliveriesToday: await _db.GoodsReceipts.CountAsync(g =>
                g.IsPosted && g.ReceivedOn == day, ct),
            OpenStocktakes: await _db.Stocktakes.CountAsync(s =>
                s.Status == StocktakeStatus.Counting, ct),

            // Done means posted for a count, and raised - not cancelled -
            // for an order.
            LastStocktakeUtc: await _db.Stocktakes
                .Where(s => s.Status == StocktakeStatus.Posted)
                .MaxAsync(s => s.PostedAtUtc, ct),
            LastPurchaseOrderOn: await _db.PurchaseOrders
                .Where(o => o.Status != PurchaseOrderStatus.Cancelled)
                .MaxAsync(o => (DateOnly?)o.OrderedOn, ct),

            // Through the entity index: report runs are all filed against
            // the one empty id, apart from everything else in the trail.
            LastReportRunUtc: await _db.AuditEntries
                .Where(a => a.EntityType == ReportRuns.EntityType
                            && a.EntityId == Guid.Empty
                            && a.Action == ReportRuns.Action)
                .MaxAsync(a => (DateTime?)a.OccurredAtUtc, ct));
    }
}

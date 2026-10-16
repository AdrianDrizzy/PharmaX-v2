using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Home;
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

        // One after another: a DbContext runs one query at a time.
        return new HomeCountsDto(
            Patients: await _db.Patients.CountAsync(p => p.IsActive, ct),
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
                s.Status == StocktakeStatus.Counting, ct));
    }
}

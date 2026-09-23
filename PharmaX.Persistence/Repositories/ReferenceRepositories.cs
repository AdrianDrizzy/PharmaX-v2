using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reference;
using PharmaX.Domain.Reference;

namespace PharmaX.Persistence.Repositories;

/// <summary>
/// The four maintained lists, reached by kind. Each lives in its own
/// table, so the kind chooses the DbSet rather than filtering a column.
/// </summary>
public sealed class ReferenceRepository : IReferenceRepository
{
    private readonly PharmaXDbContext _db;

    public ReferenceRepository(PharmaXDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReferenceEntry>> ListAsync(
        ReferenceKind kind, string term, bool includeInactive,
        CancellationToken ct = default) =>
        kind switch
        {
            ReferenceKind.Department =>
                await Query(_db.Departments, term, includeInactive, ct),
            ReferenceKind.Category =>
                await Query(_db.Categories, term, includeInactive, ct),
            ReferenceKind.Manufacturer =>
                await Query(_db.Manufacturers, term, includeInactive, ct),
            ReferenceKind.Branch =>
                await Query(_db.Branches, term, includeInactive, ct),
            ReferenceKind.MedicalAidScheme =>
                await Query(_db.MedicalAidSchemes, term, includeInactive, ct),
            _ => Array.Empty<ReferenceEntry>()
        };

    public async Task<ReferenceEntry?> GetAsync(ReferenceKind kind, Guid id,
        CancellationToken ct = default) =>
        kind switch
        {
            ReferenceKind.Department =>
                await _db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct),
            ReferenceKind.Category =>
                await _db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct),
            ReferenceKind.Manufacturer =>
                await _db.Manufacturers.FirstOrDefaultAsync(x => x.Id == id, ct),
            ReferenceKind.Branch =>
                await _db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct),
            ReferenceKind.MedicalAidScheme =>
                await _db.MedicalAidSchemes.FirstOrDefaultAsync(
                    x => x.Id == id, ct),
            _ => null
        };

    public async Task<ReferenceEntry?> FindByCodeAsync(ReferenceKind kind,
        string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalised = code.Trim().ToUpperInvariant();

        return kind switch
        {
            ReferenceKind.Department => await _db.Departments
                .FirstOrDefaultAsync(x => x.Code == normalised, ct),
            ReferenceKind.Category => await _db.Categories
                .FirstOrDefaultAsync(x => x.Code == normalised, ct),
            ReferenceKind.Manufacturer => await _db.Manufacturers
                .FirstOrDefaultAsync(x => x.Code == normalised, ct),
            ReferenceKind.Branch => await _db.Branches
                .FirstOrDefaultAsync(x => x.Code == normalised, ct),
            ReferenceKind.MedicalAidScheme => await _db.MedicalAidSchemes
                .FirstOrDefaultAsync(x => x.Code == normalised, ct),
            _ => null
        };
    }

    public async Task AddAsync(ReferenceKind kind, ReferenceEntry entry,
        CancellationToken ct = default)
    {
        switch (entry)
        {
            case Department d: await _db.Departments.AddAsync(d, ct); break;
            case Category c: await _db.Categories.AddAsync(c, ct); break;
            case Manufacturer m: await _db.Manufacturers.AddAsync(m, ct); break;
            case Branch b: await _db.Branches.AddAsync(b, ct); break;
            case MedicalAidScheme s:
                await _db.MedicalAidSchemes.AddAsync(s, ct); break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }

    private static async Task<IReadOnlyList<ReferenceEntry>> Query<T>(
        DbSet<T> set, string term, bool includeInactive,
        CancellationToken ct) where T : ReferenceEntry
    {
        var query = set.AsQueryable();

        if (!includeInactive) query = query.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Code, pattern)
                                  || EF.Functions.Like(x.Name, pattern));
        }

        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }
}

public sealed class TariffRepository : ITariffRepository
{
    private readonly PharmaXDbContext _db;

    public TariffRepository(PharmaXDbContext db) => _db = db;

    public async Task<IReadOnlyList<TariffListItemDto>> ListAsync(
        Guid? schemeId, string term, CancellationToken ct = default)
    {
        var query = _db.Tariffs.AsNoTracking().AsQueryable();

        if (schemeId is Guid scheme)
            query = query.Where(t => t.SchemeId == scheme);

        // Joined rather than navigated: Tariff deliberately holds
        // identifiers so that pricing one does not drag a society and a
        // product graph along with it.
        var rows = await query
            .Join(_db.MedicalAidSchemes.AsNoTracking(),
                  t => t.SchemeId, s => s.Id, (t, s) => new { t, s })
            .Join(_db.Products.AsNoTracking(),
                  x => x.t.ProductId, p => p.Id, (x, p) => new { x.t, x.s, p })
            .Select(x => new TariffListItemDto(
                x.t.Id, x.s.Code, x.s.Name, x.p.Code, x.p.Description,
                x.t.Price.Amount, x.t.DispensingFee.Amount,
                x.t.Price.Amount + x.t.DispensingFee.Amount,
                x.t.EffectiveFrom, x.t.EffectiveTo, x.t.IsActive))
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var needle = term.Trim();

            rows = rows.Where(r =>
                r.ProductCode.Contains(needle,
                    StringComparison.OrdinalIgnoreCase)
                || r.ProductDescription.Contains(needle,
                    StringComparison.OrdinalIgnoreCase)
                || r.SchemeName.Contains(needle,
                    StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rows
            .OrderBy(r => r.SchemeName)
            .ThenBy(r => r.ProductDescription)
            .ToList();
    }

    public async Task<Tariff?> GetAsync(Guid id, CancellationToken ct = default)
        => await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tariff?> EffectiveAsync(Guid schemeId, Guid productId,
        DateOnly asAt, CancellationToken ct = default)
    {
        // Narrowed in the database, then the overlap rule is applied in
        // memory so that the one definition of "effective" lives on the
        // aggregate and is testable without a database.
        var candidates = await _db.Tariffs
            .Where(t => t.SchemeId == schemeId && t.ProductId == productId)
            .ToListAsync(ct);

        return candidates.Find(t => t.IsEffectiveOn(asAt));
    }

    public async Task AddAsync(Tariff tariff, CancellationToken ct = default)
        => await _db.Tariffs.AddAsync(tariff, ct);
}

/// <summary>
/// Quotations, newest first. Lines come with them: a quotation is read
/// to be looked at or printed, and never without its lines.
/// </summary>
public sealed class QuotationRepository : Application.Sales.IQuotationRepository
{
    private readonly PharmaXDbContext _db;

    public QuotationRepository(PharmaXDbContext db) => _db = db;

    public async Task<Domain.Sales.Quotation?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Quotations
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<IReadOnlyList<Domain.Sales.Quotation>> RecentAsync(
        int take = 100, CancellationToken ct = default) =>
        await _db.Quotations
            .Include(q => q.Lines)
            .OrderByDescending(q => q.QuotedOn)
            .ThenByDescending(q => q.QuotationNumber)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(Domain.Sales.Quotation quotation,
        CancellationToken ct = default) =>
        await _db.Quotations.AddAsync(quotation, ct);
}

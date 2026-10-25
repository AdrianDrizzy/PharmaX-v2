using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Domain.Tills;

namespace PharmaX.Persistence.Repositories;

public sealed class SaleRepository : ISaleRepository
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;

    public SaleRepository(PharmaXDbContext db, PharmaXOptions options)
    {
        _db = db;
        _options = options;
    }

    public Guid CurrentBranchId => Guid.Parse(_options.Workstation.BranchId);
    public Guid CurrentWorkstationId =>
        Guid.Parse(_options.Workstation.WorkstationId);

    public async Task<Sale?> GetAsync(Guid id, CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(Sale sale, CancellationToken ct = default) =>
        await _db.Sales.AddAsync(sale, ct);

    public async Task<IReadOnlyList<Sale>> ForSessionAsync(
        Guid tillSessionId, CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.TillSessionId == tillSessionId)
            .OrderBy(s => s.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task<Sale?> OpenCollectionForAsync(Guid dispensingId,
        CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.DispensingId == dispensingId)
            .Where(s => s.Status == SaleStatus.Open)
            .FirstOrDefaultAsync(ct);

    public async Task<Sale?> ByDocumentNumberAsync(string documentNumber,
        CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.DocumentNumber == documentNumber, ct);

    public async Task<IReadOnlyList<Sale>> FinalisedBetweenAsync(DateTime fromUtc,
        DateTime toUtc, CancellationToken ct = default) =>
        await _db.Sales
            .AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.Status == SaleStatus.Finalised
                        && s.OccurredAtUtc >= fromUtc && s.OccurredAtUtc < toUtc)
            .OrderByDescending(s => s.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Sale> Page, int Total)> ReceiptsAsync(
        DateTime fromUtc, DateTime toUtc, string? number,
        IReadOnlyCollection<Guid> operators, bool includeHidden, int skip,
        int take, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operators);

        var query = _db.Sales
            .AsNoTracking()
            .Where(s => s.Status == SaleStatus.Finalised
                        && s.DocumentNumber != null && s.DocumentNumber != ""
                        && s.OccurredAtUtc >= fromUtc && s.OccurredAtUtc < toUtc)
            .Where(s => includeHidden || s.ReceiptHiddenReason == null);

        if (!string.IsNullOrWhiteSpace(number))
        {
            var pattern = $"%{number.Trim()}%";
            query = query.Where(s => EF.Functions.Like(s.DocumentNumber!, pattern)
                                     || operators.Contains(s.OperatorId));
        }

        var total = await query.CountAsync(ct);

        var page = await query
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .OrderByDescending(s => s.OccurredAtUtc)
            .ThenByDescending(s => s.DocumentNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (page, total);
    }

    public async Task<IReadOnlyList<Sale>> HeldAsync(
        CancellationToken ct = default)
    {
        var branch = CurrentBranchId;

        return await _db.Sales
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.Status == SaleStatus.Held && s.BranchId == branch)
            .OrderBy(s => s.OccurredAtUtc)
            .ToListAsync(ct);
    }

    public async Task<Sale?> OpenForQuotationAsync(Guid quotationId,
        CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.QuotationId == quotationId)
            .Where(s => s.Status == SaleStatus.Open
                        || s.Status == SaleStatus.Held)
            .FirstOrDefaultAsync(ct);
}

public sealed class ProductRepository : IProductRepository
{
    private readonly PharmaXDbContext _db;

    public ProductRepository(PharmaXDbContext db) => _db = db;

    public async Task<Product?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Products
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product?> FindByCodeOrBarcodeAsync(string code,
        ProductScope scope = ProductScope.All,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalised = code.Trim().ToUpperInvariant();

        // The code is indexed and unique, so try it first: the great
        // majority of scans at the till are a product code.
        var byCode = await InScope(_db.Products.Include(p => p.Prices), scope)
            .FirstOrDefaultAsync(p => p.Code == normalised, ct);

        if (byCode is not null) return byCode;

        // Barcodes are a primitive collection, so this is a scan of a
        // short table rather than an index seek. Acceptable because it
        // only runs when the code lookup already missed.
        var candidates = await InScope(_db.Products.Include(p => p.Prices),
                                       scope)
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        return candidates.Find(p => p.MatchesCode(code));
    }

    /// <remarks>
    /// The code typed exactly comes first, then what starts with the
    /// first word, so "amox" puts Amoxicillin ahead of Co-Amoxiclav.
    /// </remarks>
    public async Task<IReadOnlyList<Product>> SearchAsync(string term,
        int take = 200, ProductScope scope = ProductScope.All, int skip = 0,
        bool includeInactive = true,
        CancellationToken ct = default)
    {
        var words = SearchTerms.Words(term);
        var code = words.Count == 1 ? words[0].ToUpperInvariant() : null;
        var start = words.Count > 0 ? $"{words[0]}%" : null;

        return await Matching(term, scope)
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => code != null && p.Code == code ? 0 : 1)
            .ThenBy(p => start != null
                         && EF.Functions.Like(p.Description, start) ? 0 : 1)
            .ThenBy(p => p.Description)

            // The code last, so that two products with the same name
            // keep their order from one page to the next.
            .ThenBy(p => p.Code)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(string term,
        ProductScope scope = ProductScope.All,
        bool includeInactive = true,
        CancellationToken ct = default) =>
        await Matching(term, scope)
            .Where(p => includeInactive || p.IsActive)
            .CountAsync(ct);

    /// <summary>
    /// Narrows a product query to what this caller may see.
    /// </summary>
    /// <remarks>
    /// Schedule 3 and above is dispensary stock (Product.
    /// RequiresPharmacist). Written as the column comparison rather than
    /// the property, so that it is translated into the SQL and the rows
    /// never leave the database.
    /// </remarks>
    private static IQueryable<Product> InScope(IQueryable<Product> query,
                                               ProductScope scope) =>
        scope == ProductScope.OverTheCounter
            ? query.Where(p => p.Schedule < 3)
            : query;

    /// <remarks>
    /// Filtered in the database rather than by pulling the catalogue
    /// across the network and counting in memory: every workstation runs
    /// this on a timer all day, and the pharmacy's catalogue is several
    /// hundred lines of which a handful are ever low.
    /// </remarks>
    public async Task<IReadOnlyList<Product>> BelowReorderLevelAsync(
        CancellationToken ct = default) =>
        await _db.Products
            .Where(p => p.IsActive)
            .Where(p => p.ReorderLevel > 0m)
            .Where(p => p.QuantityOnHand <= p.ReorderLevel)
            .OrderBy(p => p.Description)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> CodesAsync(
        CancellationToken ct = default) =>
        await _db.Products
            .AsNoTracking()
            .Select(p => p.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> NothingOnHandAsync(
        CancellationToken ct = default) =>
        await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Where(p => p.ReorderLevel <= 0m)
            .Where(p => p.QuantityOnHand <= 0m)
            .OrderBy(p => p.Description)
            .ToListAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct = default)
        => await _db.Products.AddAsync(product, ct);

    /// <summary>
    /// The one place the search predicate is written, so the page and
    /// the count can never disagree about what matched.
    /// </summary>
    /// <remarks>
    /// Word by word, so "amox 500" finds "Amoxicillin 500mg capsules"
    /// and the generic name finds the brands sold under it.
    /// </remarks>
    private IQueryable<Product> Matching(string term, ProductScope scope)
    {
        var query = InScope(_db.Products.AsNoTracking(), scope);

        foreach (var word in SearchTerms.Words(term))
        {
            var pattern = $"%{word}%";

            query = query.Where(p =>
                EF.Functions.Like(p.Description, pattern)
                || EF.Functions.Like(p.Code, pattern)
                || (p.GenericName != null
                    && EF.Functions.Like(p.GenericName, pattern)));
        }

        return query;
    }
}

public sealed class BatchRepository : IBatchRepository
{
    private readonly PharmaXDbContext _db;

    public BatchRepository(PharmaXDbContext db) => _db = db;

    public async Task<Batch?> GetAsync(Guid id, CancellationToken ct = default)
        => await _db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<Batch>> ForProductAsync(Guid productId,
        CancellationToken ct = default) =>
        await _db.Batches
            .Where(b => b.ProductId == productId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Batch>> ForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0) return Array.Empty<Batch>();

        // Read only, like the product search it expands. A tracked read
        // hands back the copy this context already holds, so the list
        // kept showing a batch as it was before the product editor, a
        // delivery or a sale changed it.
        return await _db.Batches
            .AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Batch>> ExpiringBeforeAsync(
        DateOnly expiringBefore, CancellationToken ct = default) =>
        await _db.Batches
            .Where(b => b.Status == BatchStatus.Available)
            .Where(b => b.QuantityOnHand > 0m)
            .Where(b => b.ExpiryDate < expiringBefore)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);

    public async Task AddAsync(Batch batch, CancellationToken ct = default) =>
        await _db.Batches.AddAsync(batch, ct);
}

public sealed class PatientRepository : IPatientRepository
{
    private readonly PharmaXDbContext _db;

    public PatientRepository(PharmaXDbContext db) => _db = db;

    public async Task<Patient?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Patient?> ByFileNumberAsync(string fileNumber,
        CancellationToken ct = default) =>
        await _db.Patients
            .FirstOrDefaultAsync(p => p.FileNumber == fileNumber, ct);

    /// <remarks>
    /// Surname and file number were all it looked at, so "Tendai", an ID
    /// number or the telephone number the patient reads out at the
    /// counter found nobody (ADM-OBS-12). Every word has to match one of
    /// the fields, so "Moyo Tendai", "Moyo, Tendai" and "mo ten" all
    /// narrow to the one person - surname first being how the counter
    /// types it, and how the matches are ranked. A telephone number is
    /// compared on its last nine digits, so 0771 234 567 finds +263 77
    /// 123 4567; the address is searched too, so "Woodstock" finds who
    /// lives there.
    /// </remarks>
    public async Task<IReadOnlyList<Patient>> SearchAsync(string term,
        int take = 100, int skip = 0, bool includeInactive = false,
        CancellationToken ct = default)
    {
        var (query, number) = Matching(term, includeInactive);

        // The counter types the surname first and the first name after
        // it, so a patient whose surname starts with the first word - and
        // whose first name starts with the second - comes top, ahead of
        // one who merely has the letters somewhere in their details.
        var words = number is null ? SearchTerms.Words(term) : [];
        var surname = words.Count > 0 ? $"{words[0]}%" : null;
        var firstName = words.Count > 1 ? $"{words[1]}%" : null;

        return await query
            .OrderBy(p => surname != null
                          && EF.Functions.Like(p.Surname, surname) ? 0 : 1)
            .ThenBy(p => firstName != null
                         && EF.Functions.Like(p.FirstNames, firstName) ? 0 : 1)
            .ThenBy(p => p.Surname).ThenBy(p => p.FirstNames)

            // The file last, so two of the same name keep their order
            // from one page to the next.
            .ThenBy(p => p.FileNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(string term, bool includeInactive = false,
        CancellationToken ct = default) =>
        await Matching(term, includeInactive).Query.CountAsync(ct);

    /// The active patients the search matches, and the telephone or ID
    /// number it was read as, where it was read as one.
    private (IQueryable<Patient> Query, string? Number) Matching(string term,
        bool includeInactive = false)
    {
        var query = _db.Patients.AsNoTracking()
            .Where(p => includeInactive || p.IsActive);

        // A number is typed with spaces as often as without, so one made
        // of nothing but digits is read whole first: "0771 234 567" is
        // one telephone number, not three words. The same digits may be
        // an ID or a membership number, so those are tried too.
        var number = SearchTerms.Telephone(term, minimumDigits: 7) is string whole
                     && (term ?? "").All(c => char.IsAsciiDigit(c)
                                              || c is ' ' or '-' or '+' or '(' or ')')
            ? whole
            : null;

        if (number is not null)
        {
            var phone = $"%{number}%";
            var compact = $"%{Compact(term!)}%";

            query = query.Where(p =>
                (p.Telephone != null
                 && EF.Functions.Like(p.Telephone.Replace(" ", "")
                     .Replace("-", "").Replace("(", "").Replace(")", ""), phone))
                || (p.IdNumber != null && EF.Functions.Like(
                        p.IdNumber.Replace(" ", "").Replace("-", ""), compact))
                || (p.MembershipNumber != null && EF.Functions.Like(
                        p.MembershipNumber.Replace(" ", "").Replace("-", ""),
                        compact)));
        }
        else
        {
            foreach (var word in SearchTerms.Words(term))
            {
                var pattern = $"%{word}%";
                var compact = $"%{Compact(word)}%";

                var digits = SearchTerms.Telephone(word);
                var dialled = digits is null ? null : $"%{digits}%";

                query = query.Where(p =>
                    EF.Functions.Like(p.Surname, pattern)
                    || EF.Functions.Like(p.FirstNames, pattern)
                    || EF.Functions.Like(p.FileNumber, pattern)
                    || (p.IdNumber != null && EF.Functions.Like(
                            p.IdNumber.Replace(" ", "").Replace("-", ""), compact))
                    || (p.MembershipNumber != null
                        && EF.Functions.Like(p.MembershipNumber, pattern))
                    || (p.Address != null && EF.Functions.Like(p.Address, pattern))
                    || (dialled != null && p.Telephone != null && EF.Functions.Like(
                            p.Telephone.Replace(" ", "").Replace("-", ""), dialled)));
            }
        }

        return (query, number);
    }

    public async Task<IReadOnlyList<Patient>> LikelySameAsync(string surname,
        string firstNames, DateOnly? dateOfBirth, string? idNumber,
        Guid? except, string? telephone = null,
        CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(idNumber) ? null : Compact(idNumber);
        var last = (surname ?? "").Trim();
        var first = (firstNames ?? "").Trim();

        // Seven digits at least, so a half-typed number or an extension
        // does not bring up every file in the town.
        var digits = SearchTerms.Telephone(telephone, minimumDigits: 7);
        var phone = digits is null ? null : $"%{digits}";

        return await _db.Patients
            .AsNoTracking()
            .Where(p => p.IsActive && p.Id != except)
            .Where(p =>
                (id != null && p.IdNumber != null
                 && p.IdNumber.Replace(" ", "").Replace("-", "") == id)
                || (dateOfBirth != null && p.DateOfBirth == dateOfBirth
                    && p.Surname == last && p.FirstNames == first)
                || (phone != null && p.Telephone != null
                    && EF.Functions.Like(p.Telephone.Replace(" ", "")
                        .Replace("-", "").Replace("(", "").Replace(")", ""),
                        phone)))
            .OrderBy(p => p.FileNumber)
            .Take(10)
            .ToListAsync(ct);
    }

    /// An ID number as it is compared: no spaces, no dashes, capitals.
    private static string Compact(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal)
             .Replace("-", "", StringComparison.Ordinal)
             .ToUpperInvariant();

    public async Task<ClinicalProfile?> ClinicalProfileAsync(Guid patientId,
        CancellationToken ct = default) =>
        await _db.ClinicalProfiles
            .Include(c => c.Allergies)
            .FirstOrDefaultAsync(c => c.PatientId == patientId, ct);

    public async Task AddAsync(Patient patient, CancellationToken ct = default)
        => await _db.Patients.AddAsync(patient, ct);

    public async Task AddClinicalProfileAsync(ClinicalProfile profile,
        CancellationToken ct = default) =>
        await _db.ClinicalProfiles.AddAsync(profile, ct);

    public async Task<IReadOnlyList<PatientAttachment>> AttachmentsAsync(
        Guid patientId, CancellationToken ct = default) =>
        await _db.PatientAttachments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.CapturedAtUtc)
            .ToListAsync(ct);

    public async Task<PatientAttachment?> AttachmentAsync(Guid attachmentId,
        CancellationToken ct = default) =>
        await _db.PatientAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);

    public async Task AddAttachmentAsync(PatientAttachment attachment,
        CancellationToken ct = default) =>
        await _db.PatientAttachments.AddAsync(attachment, ct);

    public void RemoveAttachment(PatientAttachment attachment) =>
        _db.PatientAttachments.Remove(attachment);

    public async Task<IReadOnlySet<Guid>> WithAllergiesAsync(
        IEnumerable<Guid> patientIds, CancellationToken ct = default)
    {
        var ids = patientIds.ToList();

        if (ids.Count == 0) return new HashSet<Guid>();

        // One query for the whole page rather than a profile per row: a
        // list of names must not pull a medical history each.
        var found = await _db.ClinicalProfiles
            .AsNoTracking()
            .Where(c => ids.Contains(c.PatientId))
            .Where(c => c.Allergies.Any())
            .Select(c => c.PatientId)
            .ToListAsync(ct);

        return found.ToHashSet();
    }
}

public sealed class UserRepository : IUserRepository
{
    private readonly PharmaXDbContext _db;

    public UserRepository(PharmaXDbContext db) => _db = db;

    public async Task<User?> ByUsernameAsync(string username,
        CancellationToken ct = default)
    {
        var normalised = username?.Trim().ToLowerInvariant() ?? "";

        return await _db.Users
            .FirstOrDefaultAsync(u => u.Username == normalised, ct);
    }

    public async Task<User?> GetAsync(Guid id, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public async Task<IReadOnlyList<User>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!includeInactive) query = query.Where(u => u.IsActive);

        return await query.OrderBy(u => u.Username).ToListAsync(ct);
    }
}

public sealed class TillSessionRepository : ITillSessionRepository
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;

    public TillSessionRepository(PharmaXDbContext db, PharmaXOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<TillSession?> CurrentAsync(CancellationToken ct = default)
    {
        var workstationId = Guid.Parse(_options.Workstation.WorkstationId);

        return await _db.TillSessions
            .Where(s => s.WorkstationId == workstationId)
            .Where(s => s.Status != TillSessionStatus.Closed)
            .OrderByDescending(s => s.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TillSession?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.TillSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(TillSession session,
        CancellationToken ct = default) =>
        await _db.TillSessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<Denomination>> DenominationsAsync(
        string? currency = null, CancellationToken ct = default)
    {
        var code = currency ?? Money.DefaultCurrency;

        return await _db.Denominations
            .AsNoTracking()
            .Where(d => d.Currency == code)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(ct);
    }

    public async Task AddDeclarationAsync(TillDeclaration declaration,
        CancellationToken ct = default) =>
        await _db.TillDeclarations.AddAsync(declaration, ct);

    /// <remarks>
    /// The latest rather than the one marked final: an operator can
    /// recount, and what closes the till is the last count they stood
    /// behind.
    /// </remarks>
    public async Task<TillDeclaration?> LatestDeclarationAsync(
        Guid tillSessionId, CancellationToken ct = default) =>
        await _db.TillDeclarations
            .Include(d => d.Lines)
            .Where(d => d.TillSessionId == tillSessionId)
            .OrderByDescending(d => d.DeclaredAtUtc)
            .FirstOrDefaultAsync(ct);

    /// <remarks>
    /// Read afresh, not tracked: the money-out window saves through a
    /// scope of its own, and the till window that opened it must see a
    /// void made there, not the copy it read first.
    /// </remarks>
    public async Task<IReadOnlyList<TillPayout>> PayoutsAsync(
        Guid tillSessionId, CancellationToken ct = default) =>
        await _db.TillPayouts
            .AsNoTracking()
            .Where(p => p.TillSessionId == tillSessionId)
            .OrderBy(p => p.PaidAtUtc)
            .ToListAsync(ct);

    public async Task<TillPayout?> GetPayoutAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.TillPayouts.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddPayoutAsync(TillPayout payout,
        CancellationToken ct = default) =>
        await _db.TillPayouts.AddAsync(payout, ct);

    /// <remarks>
    /// Any session still open counts, however long ago it was opened: a
    /// till left open overnight is still owed a cash-up.
    /// </remarks>
    public async Task<IReadOnlyList<TillSession>> ActiveBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await _db.TillSessions
            .AsNoTracking()
            .Where(s => s.OpenedAtUtc < toUtc
                        && (s.Status != TillSessionStatus.Closed
                            || s.ClosedAtUtc >= fromUtc))
            .OrderBy(s => s.OpenedAtUtc)
            .ToListAsync(ct);
}

public sealed class DispensingRepository : IDispensingRepository
{
    private readonly PharmaXDbContext _db;

    public DispensingRepository(PharmaXDbContext db) => _db = db;

    public async Task<Domain.Dispensing.Dispensing?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Include(d => d.Warnings)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Domain.Dispensing.Dispensing?> ByWarningAsync(
        Guid warningId, CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Include(d => d.Warnings)
            .FirstOrDefaultAsync(d => d.Warnings.Any(w => w.Id == warningId),
                                 ct);

    public async Task<IReadOnlyList<Domain.Dispensing.Dispensing>>
        ForPatientAsync(Guid patientId, CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.IssuedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Domain.Dispensing.Dispensing>>
        IssuedBetweenAsync(DateTime fromUtc, DateTime toUtc,
            CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.Issued
                        && d.IssuedAtUtc >= fromUtc
                        && d.IssuedAtUtc < toUtc)
            .OrderByDescending(d => d.IssuedAtUtc)
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Filtered in the database rather than in memory: this runs on
    /// every refresh of a screen a cashier keeps open all day, and a
    /// pharmacy with a year of dispensings behind it would otherwise
    /// pull all of them across the network to find the four that are
    /// waiting. The co-payment itself is checked on the client, because
    /// it is a sum over the lines and SQLite cannot total a decimal.
    /// </remarks>
    public async Task<IReadOnlyList<Domain.Dispensing.Dispensing>>
        AwaitingCollectionAsync(CancellationToken ct = default)
    {
        var issued = await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.Issued)
            .Where(d => d.SaleId == null && d.CollectedAtUtc == null)
            .OrderBy(d => d.IssuedAtUtc)
            .ToListAsync(ct);

        return issued.Where(d => d.AwaitsCollection).ToList();
    }

    public async Task<IReadOnlyList<Domain.Dispensing.Dispensing>>
        OwingAsync(CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines).ThenInclude(l => l.Batches)
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.Issued
                        && d.Lines.Any(l => l.Owed != null && l.OwedGivenAtUtc == null))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Domain.Dispensing.Dispensing>>
        InProgressAsync(CancellationToken ct = default) =>
        await _db.Dispensings
            .Include(d => d.Lines)
            .Where(d => d.Status == Domain.Dispensing.DispensingStatus.InProgress)
            .ToListAsync(ct);

    public async Task AddAsync(Domain.Dispensing.Dispensing dispensing,
        CancellationToken ct = default) =>
        await _db.Dispensings.AddAsync(dispensing, ct);
}

public sealed class ScriptRepository : IScriptRepository
{
    private readonly PharmaXDbContext _db;

    public ScriptRepository(PharmaXDbContext db) => _db = db;

    public async Task<Script?> GetAsync(Guid id, CancellationToken ct = default)
        => await _db.Scripts
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Script>> ForPatientAsync(Guid patientId,
        CancellationToken ct = default) =>
        await _db.Scripts
            .Include(s => s.Items)
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.PrescribedOn)
            .ToListAsync(ct);

    public async Task AddAsync(Script script, CancellationToken ct = default) =>
        await _db.Scripts.AddAsync(script, ct);
}

/// <summary>
/// Reads the role-to-permission grants. Synchronous on purpose: a
/// permission check happens on every screen open and every override, and
/// the grants for one user are small enough to hold for the session.
/// </summary>
public sealed class PermissionRepository : IPermissionRepository
{
    private readonly PharmaXDbContext _db;
    private readonly Dictionary<Guid, IReadOnlyList<string>> _cache = new();

    /// Permissions a supervisor may authorise on the spot rather than the
    /// system simply refusing. Everything else is a flat no.
    private static readonly HashSet<string> Escalatable = new(StringComparer.Ordinal)
    {
        Domain.Security.PermissionCode.SaleDiscountAboveLimit,
        Domain.Security.PermissionCode.ReturnAuthorise,
        Domain.Security.PermissionCode.SessionCloseOtherUser,
        Domain.Security.PermissionCode.PriceChange,
        Domain.Security.PermissionCode.DispensingChargeAdjust
    };

    public PermissionRepository(PharmaXDbContext db) => _db = db;

    public bool UserHas(Guid userId, string permissionCode) =>
        ForUser(userId).Contains(permissionCode, StringComparer.Ordinal);

    public bool IsSupervisorEscalatable(string permissionCode) =>
        Escalatable.Contains(permissionCode);

    public IReadOnlyList<string> ForUser(Guid userId)
    {
        if (_cache.TryGetValue(userId, out var cached)) return cached;

        var user = _db.Users.AsNoTracking()
                            .FirstOrDefault(u => u.Id == userId);

        if (user is null)
        {
            _cache[userId] = Array.Empty<string>();
            return _cache[userId];
        }

        var roles = _db.Roles.AsNoTracking().ToList();

        var granted = roles
            .Where(r => user.IsInRole(r.Name))
            .SelectMany(r => r.PermissionCodes)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _cache[userId] = granted;
        return granted;
    }
}

/// <summary>
/// Appends a stock movement to the current unit of work. It does not save:
/// the movement commits with the sale that caused it or not at all.
/// </summary>
public sealed class StockMovementWriter : IStockMovementWriter
{
    private readonly PharmaXDbContext _db;

    public StockMovementWriter(PharmaXDbContext db) => _db = db;

    public void Add(StockMovement movement) => _db.StockMovements.Add(movement);

    public async Task<IReadOnlyList<StockMovement>> ForSourceAsync(
        string sourceType, Guid sourceId, CancellationToken ct = default) =>
        await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.SourceDocumentType == sourceType
                        && m.SourceDocumentId == sourceId)
            .OrderBy(m => m.OccurredAtUtc)
            .ToListAsync(ct);
}

public sealed class SupplierRepository : ISupplierRepository
{
    private readonly PharmaXDbContext _db;

    public SupplierRepository(PharmaXDbContext db) => _db = db;

    public async Task<Supplier?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Supplier?> ByCodeAsync(string code,
        CancellationToken ct = default)
    {
        var normalised = code?.Trim().ToUpperInvariant() ?? "";

        return await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Code == normalised, ct);
    }

    public async Task<IReadOnlyList<Supplier>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsNoTracking().AsQueryable();

        if (!includeInactive) query = query.Where(s => s.IsActive);

        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task AddAsync(Supplier supplier,
        CancellationToken ct = default) =>
        await _db.Suppliers.AddAsync(supplier, ct);
}

public sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly PharmaXDbContext _db;

    public PurchaseOrderRepository(PharmaXDbContext db) => _db = db;

    public async Task<PurchaseOrder?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<PurchaseOrder>> OutstandingAsync(
        Guid? supplierId = null, CancellationToken ct = default)
    {
        var query = _db.PurchaseOrders
            .Include(o => o.Lines)
            .Where(o => o.Status == PurchaseOrderStatus.Sent
                        || o.Status == PurchaseOrderStatus.PartiallyReceived);

        if (supplierId is Guid id)
            query = query.Where(o => o.SupplierId == id);

        return await query.OrderBy(o => o.OrderedOn).ToListAsync(ct);
    }

    /// <remarks>
    /// Summed here rather than in SQL because SQLite will not total a
    /// decimal; the open orders are a handful of rows.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, decimal>> OnOrderAsync(
        CancellationToken ct = default)
    {
        var lines = await _db.PurchaseOrders
            .AsNoTracking()
            .Where(o => o.Status == PurchaseOrderStatus.Draft
                        || o.Status == PurchaseOrderStatus.Sent
                        || o.Status == PurchaseOrderStatus.PartiallyReceived)
            .SelectMany(o => o.Lines)
            .Select(l => new { l.ProductId, l.QuantityOrdered, l.QuantityReceived })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key,
                g => g.Sum(l => Math.Max(l.QuantityOrdered - l.QuantityReceived, 0m)));
    }

    public async Task<IReadOnlyList<PurchaseOrder>> DraftsAsync(
        CancellationToken ct = default) =>
        await _db.PurchaseOrders
            .Include(o => o.Lines)
            .Where(o => o.Status == PurchaseOrderStatus.Draft)
            .OrderBy(o => o.OrderedOn)
            .ThenBy(o => o.OrderNumber)
            .ToListAsync(ct);

    public async Task AddAsync(PurchaseOrder order,
        CancellationToken ct = default) =>
        await _db.PurchaseOrders.AddAsync(order, ct);
}

public sealed class GoodsReceiptRepository : IGoodsReceiptRepository
{
    private readonly PharmaXDbContext _db;

    public GoodsReceiptRepository(PharmaXDbContext db) => _db = db;

    public async Task<GoodsReceipt?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.GoodsReceipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<GoodsReceipt>> RecentAsync(int take = int.MaxValue,
        CancellationToken ct = default) =>
        await _db.GoodsReceipts
            .AsNoTracking()
            .Include(r => r.Lines)
            .OrderByDescending(r => r.ReceivedOn)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<LastPurchase?> LastPurchaseAsync(Guid productId,
        CancellationToken ct = default)
    {
        var receipt = await _db.GoodsReceipts
            .AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.IsPosted && r.Lines.Any(l => l.ProductId == productId))
            .OrderByDescending(r => r.ReceivedOn)
            .ThenByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var line = receipt?.Lines.LastOrDefault(l => l.ProductId == productId);

        return receipt is null || line is null
            ? null
            : new LastPurchase(receipt.SupplierId, line.UnitCost.Amount,
                               receipt.ReceivedOn);
    }

    public async Task AddAsync(GoodsReceipt receipt,
        CancellationToken ct = default) =>
        await _db.GoodsReceipts.AddAsync(receipt, ct);
}

public sealed class StocktakeRepository : IStocktakeRepository
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;

    public StocktakeRepository(PharmaXDbContext db, PharmaXOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<Stocktake?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Stocktakes
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Stocktake?> OpenCountAsync(
        CancellationToken ct = default)
    {
        var branchId = Guid.Parse(_options.Workstation.BranchId);

        return await _db.Stocktakes
            .Include(s => s.Lines)
            .Where(s => s.BranchId == branchId)
            .Where(s => s.Status == StocktakeStatus.Counting)
            .OrderByDescending(s => s.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Stocktake>> RecentAsync(int take = int.MaxValue,
        CancellationToken ct = default) =>
        await _db.Stocktakes
            .AsNoTracking()
            .Include(s => s.Lines)
            .OrderByDescending(s => s.OpenedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(Stocktake stocktake,
        CancellationToken ct = default) =>
        await _db.Stocktakes.AddAsync(stocktake, ct);
}

public sealed class ReturnRepository : IReturnRepository
{
    private readonly PharmaXDbContext _db;

    public ReturnRepository(PharmaXDbContext db) => _db = db;

    public async Task<SaleReturn?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.SaleReturns
            .Include(r => r.Lines)
            .Include(r => r.Refunds)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<SaleReturn>> RecentAsync(int take = int.MaxValue,
        CancellationToken ct = default) =>
        await _db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Lines)
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, decimal>>
        ReturnedQuantitiesAsync(Guid saleId, CancellationToken ct = default)
    {
        // Filtered in the database, summed here: SQLite cannot apply
        // Sum to a decimal, and the workstation database is always
        // SQLite (NET-002). The rows are one sale's returns, so there
        // is nothing to gain from pushing the arithmetic down anyway.
        var lines = await _db.SaleReturns
            .AsNoTracking()
            .Where(r => r.OriginalSaleId == saleId)
            .Where(r => r.Status == ReturnStatus.Finalised)
            .SelectMany(r => r.Lines)
            .Select(l => new { l.OriginalLineNo, l.Quantity })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.OriginalLineNo)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
    }

    public async Task<IReadOnlyList<SaleReturn>> ForSessionAsync(
        Guid tillSessionId, CancellationToken ct = default) =>
        await _db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.Refunds)
            .Where(r => r.TillSessionId == tillSessionId)
            .OrderBy(r => r.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(SaleReturn saleReturn,
        CancellationToken ct = default) =>
        await _db.SaleReturns.AddAsync(saleReturn, ct);
}

/// <summary>
/// The books.
/// </summary>
/// <remarks>
/// Journal lines carry an account code rather than a foreign key to the
/// account row, which is deliberate: a posted journal is a historical
/// record, and it must still read correctly if somebody later
/// deactivates the account it went to. The cost is that these queries
/// join on a string, which is what the index on AccountCode is for.
/// </remarks>
public sealed class LedgerRepository : ILedgerRepository
{
    private readonly PharmaXDbContext _db;

    public LedgerRepository(PharmaXDbContext db) => _db = db;

    public async Task<IReadOnlyList<LedgerAccount>> AccountsAsync(
        bool includeInactive = false, CancellationToken ct = default) =>
        await _db.LedgerAccounts
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);

    public async Task<LedgerAccount?> AccountAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.LedgerAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<LedgerAccount?> AccountByCodeAsync(string code,
        CancellationToken ct = default) =>
        await _db.LedgerAccounts.FirstOrDefaultAsync(a => a.Code == code, ct);

    public async Task AddAccountAsync(LedgerAccount account,
        CancellationToken ct = default) =>
        await _db.LedgerAccounts.AddAsync(account, ct);

    public async Task<JournalBatch?> BatchAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.JournalBatches
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<JournalBatch>> BatchesAsync(DateOnly from,
        DateOnly until, CancellationToken ct = default) =>
        await _db.JournalBatches
            .AsNoTracking()
            .Include(j => j.Lines)
            .Where(j => j.PostingDate >= from && j.PostingDate <= until)
            .OrderByDescending(j => j.PostingDate)
            .ThenByDescending(j => j.BatchNumber)
            .ToListAsync(ct);

    public async Task AddBatchAsync(JournalBatch batch,
        CancellationToken ct = default) =>
        await _db.JournalBatches.AddAsync(batch, ct);

    /// <remarks>
    /// Filtered in the database and summed here. SQLite cannot apply Sum
    /// to a decimal, and a workstation database is always SQLite, so the
    /// grouping has to happen in memory either way - which is also why
    /// the projection takes three columns rather than whole entities.
    /// </remarks>
    public async Task<IReadOnlyList<AccountTotals>> TotalsAsync(DateOnly asAt,
        CancellationToken ct = default)
    {
        var lines = await PostedLines()
            .Where(x => x.Batch.PostingDate <= asAt)
            .Select(x => new
            {
                x.Line.AccountCode,
                x.Line.IsDebit,
                x.Line.Amount.Amount
            })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.AccountCode, StringComparer.Ordinal)
            .Select(g => new AccountTotals(
                g.Key,
                g.Where(l => l.IsDebit).Sum(l => l.Amount),
                g.Where(l => !l.IsDebit).Sum(l => l.Amount)))
            .OrderBy(t => t.AccountCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<LedgerEntry>> EntriesAsync(
        string accountCode, DateOnly from, DateOnly until,
        CancellationToken ct = default)
    {
        var rows = await PostedLines()
            .Where(x => x.Line.AccountCode == accountCode)
            .Where(x => x.Batch.PostingDate >= from)
            .Where(x => x.Batch.PostingDate <= until)
            .OrderBy(x => x.Batch.PostingDate)
            .ThenBy(x => x.Batch.BatchNumber)
            .ThenBy(x => x.Line.LineNo)
            .Select(x => new
            {
                x.Batch.PostingDate,
                x.Batch.BatchNumber,
                x.Batch.Source,
                x.Line.AccountCode,
                x.Line.IsDebit,
                x.Line.Amount.Amount,

                // The line's own narration where it has one, and the
                // batch's where it has not: a hand-written journal
                // explains itself line by line, and an automatic one
                // says the same thing once at the top.
                Narration = x.Line.Narration ?? x.Batch.Narration
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new LedgerEntry(r.PostingDate, r.BatchNumber,
                r.Source, r.AccountCode,
                r.IsDebit ? r.Amount : 0m,
                r.IsDebit ? 0m : r.Amount,
                r.Narration))
            .ToList();
    }

    /// <summary>
    /// Every line of every posted batch, paired with its batch.
    /// </summary>
    /// <remarks>
    /// Unposted batches are drafts: a half-captured journal must not
    /// appear in a trial balance, and it is the one place an unbalanced
    /// set of lines is allowed to exist.
    /// </remarks>
    private IQueryable<BatchLine> PostedLines() =>
        _db.JournalBatches
            .AsNoTracking()
            .Where(j => j.IsPosted)
            .SelectMany(j => j.Lines, (batch, line) => new BatchLine
            {
                Batch = batch,
                Line = line
            });

    private sealed class BatchLine
    {
        public JournalBatch Batch { get; init; } = null!;
        public JournalLine Line { get; init; } = null!;
    }
}

/// <summary>
/// What the medical aid schemes owe.
/// </summary>
public sealed class ClaimRepository : IClaimRepository
{
    private readonly PharmaXDbContext _db;

    public ClaimRepository(PharmaXDbContext db) => _db = db;

    public async Task<MedicalAidClaim?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.MedicalAidClaims.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<MedicalAidClaim?> ByNumberAsync(string claimNumber,
        CancellationToken ct = default) =>
        await _db.MedicalAidClaims
            .FirstOrDefaultAsync(c => c.ClaimNumber == claimNumber, ct);

    public async Task<MedicalAidClaim?> ForDispensingAsync(Guid dispensingId,
        CancellationToken ct = default) =>
        await _db.MedicalAidClaims
            .FirstOrDefaultAsync(c => c.DispensingId == dispensingId, ct);

    /// <remarks>
    /// Filtered on status in the database rather than by reading every
    /// claim the pharmacy has ever made: a busy dispensary raises
    /// hundreds a month and only the open ones are ever worked.
    /// </remarks>
    public async Task<IReadOnlyList<MedicalAidClaim>> OutstandingAsync(
        Guid? schemeId = null, CancellationToken ct = default) =>
        await _db.MedicalAidClaims
            .Where(c => c.Status == ClaimStatus.Outstanding
                     || c.Status == ClaimStatus.PartPaid)
            .Where(c => schemeId == null || c.SchemeId == schemeId)
            .OrderBy(c => c.ClaimedOn)
            .ThenBy(c => c.ClaimNumber)
            .ToListAsync(ct);

    public async Task AddAsync(MedicalAidClaim claim,
        CancellationToken ct = default) =>
        await _db.MedicalAidClaims.AddAsync(claim, ct);
}

/// <summary>
/// What patients owe the pharmacy.
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly PharmaXDbContext _db;

    public AccountRepository(PharmaXDbContext db) => _db = db;

    public async Task<Account?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Account?> ByNumberAsync(string accountNumber,
        CancellationToken ct = default) =>
        await _db.Accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, ct);

    public async Task<IReadOnlyList<Account>> SearchAsync(string term,
        int take = int.MaxValue, CancellationToken ct = default)
    {
        var query = _db.Accounts.AsNoTracking();

        // Word by word, like every other search.
        foreach (var word in SearchTerms.Words(term))
        {
            var pattern = $"%{word}%";

            query = query.Where(a => EF.Functions.Like(a.AccountNumber, pattern)
                                  || EF.Functions.Like(a.Name, pattern));
        }

        return await query
            .OrderBy(a => a.Name)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <remarks>
    /// Filtered in the database and ordered here, because SQLite cannot
    /// order by a decimal it has not selected and the rows are the
    /// handful of accounts that actually owe something.
    /// </remarks>
    public async Task<IReadOnlyList<Account>> OwingAsync(
        CancellationToken ct = default)
    {
        var owing = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.Balance.Amount > 0m)
            .ToListAsync(ct);

        return owing
            .OrderByDescending(a => a.Balance.Amount)
            .ToList();
    }

    public async Task AddAsync(Account account,
        CancellationToken ct = default) =>
        await _db.Accounts.AddAsync(account, ct);

    public async Task<IReadOnlyList<AccountTransaction>> TransactionsAsync(
        Guid accountId, DateOnly from, DateOnly until,
        CancellationToken ct = default) =>
        await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .Where(t => t.OccurredOn >= from && t.OccurredOn <= until)
            .OrderBy(t => t.OccurredOn)
            .ThenBy(t => t.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddTransactionAsync(AccountTransaction transaction,
        CancellationToken ct = default) =>
        await _db.AccountTransactions.AddAsync(transaction, ct);

    public async Task<IReadOnlyList<AccountTransaction>> PaymentsForSessionAsync(
        Guid tillSessionId, CancellationToken ct = default) =>
        await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.TillSessionId == tillSessionId
                        && t.Kind == AccountEntryKind.Payment)
            .ToListAsync(ct);

    /// <remarks>
    /// Summed here rather than in SQL for the same reason as everywhere
    /// else: SQLite will not total a decimal. One account's movements
    /// are a page of rows, not a table scan.
    /// </remarks>
    public async Task<decimal> MovementTotalAsync(Guid accountId,
        CancellationToken ct = default)
    {
        var movements = await _db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .Select(t => new { t.Kind, t.Amount.Amount })
            .ToListAsync(ct);

        return movements.Sum(m =>
            m.Kind == AccountEntryKind.Charge ? m.Amount : -m.Amount);
    }
}

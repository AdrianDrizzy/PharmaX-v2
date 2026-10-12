using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Fakes;

/// <summary>
/// A clock that does not move unless a test moves it. Waiting for real
/// time to pass is the slowest possible way to test an expiry rule.
/// </summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTime? utcNow = null) =>
        UtcNow = utcNow ?? new DateTime(2026, 9, 10, 9, 30, 0,
                                        DateTimeKind.Utc);

    public DateTime UtcNow { get; private set; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    public void MoveTo(DateTime utc) => UtcNow = utc;
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Username { get; init; } = "cashier";
    public Guid? TillSessionId { get; set; } = Guid.NewGuid();
    public decimal DiscountLimitPercent { get; init; } = 10m;
    public IReadOnlyList<string> Roles { get; init; } = new[] { "Cashier" };

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Saves nothing and counts calls. The domain objects the tests build are
/// already in memory, so a fake unit of work only has to not get in the
/// way.
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }
    public int TransactionCount { get; private set; }
    public bool Committed { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }

    /// Nothing is cached: the tests' objects are the only copy there is.
    public void Forget() { }

    public Task<IAsyncDisposable> BeginTransactionAsync(
        CancellationToken ct = default)
    {
        TransactionCount++;
        return Task.FromResult<IAsyncDisposable>(new Scope(this));
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly FakeUnitOfWork _owner;
        public Scope(FakeUnitOfWork owner) => _owner = owner;

        public ValueTask DisposeAsync()
        {
            _owner.Committed = true;
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class FakeAuditWriter : IAuditWriter
{
    public List<(string Action, string Outcome, string? Reason)> Entries { get; }
        = new();

    public void Applied(string action, string entityType, Guid entityId,
                        string? reason = null) =>
        Entries.Add((action, "Applied", reason));

    public void Refused(string action, string entityType, Guid entityId,
                        string? reason = null) =>
        Entries.Add((action, "Refused", reason));

    public bool Recorded(string action, string outcome) =>
        Entries.Exists(e => e.Action == action && e.Outcome == outcome);
}

public sealed class FakeOutbox : IOutbox
{
    public List<(string EntityType, Guid EntityId, string Payload)> Entries
    { get; } = new();

    public void Append(string entityType, Guid entityId, string payloadJson) =>
        Entries.Add((entityType, entityId, payloadJson));
}

/// <summary>
/// Hands out document numbers from a single sequence, and records how
/// many it issued. The count is what proves finalising twice does not
/// consume a second number (NET-006).
/// </summary>
public sealed class FakeDocumentNumbering : IDocumentNumbering
{
    private readonly Dictionary<string, int> _issued = new(
        StringComparer.Ordinal);

    private long _next = 1;

    /// Numbers handed out across every series.
    public int IssuedCount { get; private set; }

    /// <summary>
    /// Numbers handed out for one series.
    /// </summary>
    /// <remarks>
    /// Per series because one transaction now draws from more than one:
    /// finalising a sale takes a receipt number and the journal it posts
    /// takes a journal number. A total alone cannot tell a second
    /// receipt from a first journal, and the receipt is the one that
    /// must never be issued twice.
    /// </remarks>
    public int IssuedFor(string series) =>
        _issued.GetValueOrDefault(series);

    public Task<string> NextAsync(string series,
                                  CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        IssuedCount++;
        _issued[series] = _issued.GetValueOrDefault(series) + 1;

        return Task.FromResult($"{series[..1]}-001-{_next++:D6}");
    }

    public Task<int> RemainingAsync(string series,
                                    CancellationToken ct = default) =>
        Task.FromResult(100_000);
}

public sealed class FakePermissionRepository : IPermissionRepository
{
    private readonly HashSet<string> _granted;
    private readonly HashSet<string> _escalatable;

    public FakePermissionRepository(IEnumerable<string>? granted = null,
                                    IEnumerable<string>? escalatable = null)
    {
        _granted = new HashSet<string>(
            granted ?? PharmaX.Domain.Security.PermissionCode.All,
            StringComparer.Ordinal);

        _escalatable = new HashSet<string>(
            escalatable ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public bool UserHas(Guid userId, string permissionCode) =>
        _granted.Contains(permissionCode);

    public bool IsSupervisorEscalatable(string permissionCode) =>
        _escalatable.Contains(permissionCode);

    public IReadOnlyList<string> ForUser(Guid userId) => _granted.ToList();
}

public sealed class InMemorySaleRepository : ISaleRepository
{
    private readonly Dictionary<Guid, Sale> _sales = new();

    public Guid CurrentBranchId { get; } = Guid.NewGuid();
    public Guid CurrentWorkstationId { get; } = Guid.NewGuid();

    public Task<Sale?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_sales.GetValueOrDefault(id));

    public Task AddAsync(Sale sale, CancellationToken ct = default)
    {
        _sales[sale.Id] = sale;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Sale>> ForSessionAsync(Guid tillSessionId,
        CancellationToken ct = default)
    {
        IReadOnlyList<Sale> found = _sales.Values
            .Where(s => s.TillSessionId == tillSessionId)
            .OrderBy(s => s.OccurredAtUtc)
            .ToList();

        return Task.FromResult(found);
    }

    public Task<Sale?> OpenCollectionForAsync(Guid dispensingId,
        CancellationToken ct = default) =>
        Task.FromResult(_sales.Values.FirstOrDefault(s =>
            s.DispensingId == dispensingId && s.Status == SaleStatus.Open));

    public Task<Sale?> ByDocumentNumberAsync(string documentNumber,
        CancellationToken ct = default) =>
        Task.FromResult(_sales.Values.FirstOrDefault(s =>
            string.Equals(s.DocumentNumber, documentNumber,
                          StringComparison.Ordinal)));

    public Task<IReadOnlyList<Sale>> HeldAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Sale> held = _sales.Values
            .Where(s => s.Status == SaleStatus.Held)
            .OrderBy(s => s.OccurredAtUtc)
            .ToList();

        return Task.FromResult(held);
    }

    public Task<Sale?> OpenForQuotationAsync(Guid quotationId,
        CancellationToken ct = default) =>
        Task.FromResult(_sales.Values.FirstOrDefault(s =>
            s.QuotationId == quotationId
            && s.Status is SaleStatus.Open or SaleStatus.Held));
}

public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products = new();

    public InMemoryProductRepository(params Product[] products) =>
        _products.AddRange(products);

    public Product Add(Product product)
    {
        _products.Add(product);
        return product;
    }

    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_products.Find(p => p.Id == id));

    /// The same rule as the real repository: a product with no reorder
    /// level has no reorder policy to breach.
    public Task<IReadOnlyList<Product>> BelowReorderLevelAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<Product> low = _products
            .Where(p => p.IsActive)
            .Where(p => p.ReorderLevel > 0m)
            .Where(p => p.QuantityOnHand <= p.ReorderLevel)
            .OrderBy(p => p.Description, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(low);
    }

    /// The same scope rule as the real repository: over the counter is
    /// Schedule 0 to 2.
    private static bool InScope(Product p, ProductScope scope) =>
        scope == ProductScope.All || p.Schedule < 3;

    public Task<Product?> FindByCodeOrBarcodeAsync(string code,
        ProductScope scope = ProductScope.All,
        CancellationToken ct = default) =>
        Task.FromResult(_products.Find(p => p.MatchesCode(code)
                                            && InScope(p, scope)));

    public Task<IReadOnlyList<Product>> SearchAsync(string term,
        int take = 200, ProductScope scope = ProductScope.All, int skip = 0,
        CancellationToken ct = default)
    {
        IReadOnlyList<Product> found = _products
            .Where(p => InScope(p, scope))
            .Where(p => string.IsNullOrWhiteSpace(term)
                || p.Description.Contains(term,
                    StringComparison.OrdinalIgnoreCase)
                || p.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult(found);
    }

    public Task<int> CountAsync(string term,
        ProductScope scope = ProductScope.All,
        CancellationToken ct = default) =>
        Task.FromResult(_products.Count(p => InScope(p, scope)));

    public Task AddAsync(Product product, CancellationToken ct = default)
    {
        _products.Add(product);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryBatchRepository : IBatchRepository
{
    private readonly List<Batch> _batches = new();

    public InMemoryBatchRepository(params Batch[] batches) =>
        _batches.AddRange(batches);

    public Task<Batch?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_batches.Find(b => b.Id == id));

    public Task<IReadOnlyList<Batch>> ForProductAsync(Guid productId,
        CancellationToken ct = default)
    {
        IReadOnlyList<Batch> found = _batches
            .Where(b => b.ProductId == productId).ToList();

        return Task.FromResult(found);
    }

    /// <remarks>
    /// Ordered by expiry like the real one, because the batch view reads
    /// the rows in the order it is given them.
    /// </remarks>
    public Task<IReadOnlyList<Batch>> ForProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        IReadOnlyList<Batch> found = _batches
            .Where(b => productIds.Contains(b.ProductId))
            .OrderBy(b => b.ExpiryDate)
            .ToList();

        return Task.FromResult(found);
    }

    /// <remarks>
    /// The same three filters as the real repository. A fake that
    /// returned quarantined or empty batches too would let an alert test
    /// pass against behaviour the database does not have.
    /// </remarks>
    public Task<IReadOnlyList<Batch>> ExpiringBeforeAsync(
        DateOnly expiringBefore, CancellationToken ct = default)
    {
        IReadOnlyList<Batch> found = _batches
            .Where(b => b.Status == BatchStatus.Available)
            .Where(b => b.QuantityOnHand > 0m)
            .Where(b => b.ExpiryDate < expiringBefore)
            .OrderBy(b => b.ExpiryDate)
            .ToList();

        return Task.FromResult(found);
    }

    public Batch Add(Batch batch)
    {
        _batches.Add(batch);
        return batch;
    }

    public Task AddAsync(Batch batch, CancellationToken ct = default)
    {
        Add(batch);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records what was issued without touching a product or a batch, so a
/// sale test can assert on the movement without setting up inventory.
/// </summary>
public sealed class RecordingStockService : IStockService
{
    public List<PharmaX.Domain.Dispensing.Dispensing> Reversed { get; } = new();

    public Task ApplyDispensingReversalAsync(
        PharmaX.Domain.Dispensing.Dispensing dispensing, CancellationToken ct = default)
    {
        Reversed.Add(dispensing);
        return Task.CompletedTask;
    }

    public List<(Guid SaleId, int Lines)> Issues { get; } = new();

    public Task ApplyIssueAsync(Sale sale, CancellationToken ct = default)
    {
        Issues.Add((sale.Id, sale.Lines.Count(l => !l.IsVoided)));
        return Task.CompletedTask;
    }

    public List<Guid> Dispensings { get; } = new();

    public Task ApplyDispensingAsync(
        PharmaX.Domain.Dispensing.Dispensing dispensing,
        CancellationToken ct = default)
    {
        Dispensings.Add(dispensing.Id);
        return Task.CompletedTask;
    }

    public Task ApplyOwedAsync(PharmaX.Domain.Dispensing.Dispensing dispensing,
        PharmaX.Domain.Dispensing.DispensingLine line,
        CancellationToken ct = default)
    {
        line.GiveOwed(null, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task ApplyReturnAsync(SaleReturn saleReturn,
        CancellationToken ct = default)
    {
        Returns.Add((saleReturn.Id, saleReturn.Restockable.Count));
        return Task.CompletedTask;
    }

    public List<(Guid ReturnId, int Restocked)> Returns { get; } = new();

    public Task<decimal> OnHandAsync(Guid productId,
        CancellationToken ct = default) => Task.FromResult(0m);

    public Task ApplyReceiptAsync(
        PharmaX.Domain.Suppliers.GoodsReceipt receipt,
        CancellationToken ct = default)
    {
        Receipts.Add((receipt.Id, receipt.Lines.Count));
        return Task.CompletedTask;
    }

    public Task ApplyStocktakeAsync(Stocktake stocktake,
        CancellationToken ct = default)
    {
        Stocktakes.Add((stocktake.Id, stocktake.Variances.Count));
        return Task.CompletedTask;
    }

    public List<(Guid ReceiptId, int Lines)> Receipts { get; } = new();
    public List<(Guid StocktakeId, int Variances)> Stocktakes { get; } = new();
}

/// <summary>
/// A deterministic hasher: the "hash" is a readable marker plus the
/// password, so a test can assert on it. RehashEverything drives the
/// credential-upgrade path without having to raise a real iteration
/// count.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    private int _generation;

    public bool RehashEverything { get; set; }

    public string Hash(string password) =>
        $"fake:{++_generation}:{password}";

    public bool Verify(string password, string encodedHash) =>
        encodedHash.EndsWith($":{password}", StringComparison.Ordinal);

    public bool NeedsRehash(string encodedHash) => RehashEverything;
}

public sealed class FakeSessionContext : ISessionContext
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = "";
    public Guid? TillSessionId { get; private set; }

    public void SignIn(Guid userId, string username,
                       IReadOnlyList<string> roles,
                       decimal discountLimitPercent)
    {
        Id = userId;
        Username = username;
    }

    public void SignOut()
    {
        Id = Guid.Empty;
        Username = "";
    }

    public void AttachTillSession(Guid? tillSessionId) =>
        TillSessionId = tillSessionId;
}

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<PharmaX.Domain.Security.User> _users = new();

    public InMemoryUserRepository(
        params PharmaX.Domain.Security.User[] users) => _users.AddRange(users);

    public Task<PharmaX.Domain.Security.User?> ByUsernameAsync(
        string username, CancellationToken ct = default) =>
        Task.FromResult(_users.Find(u => string.Equals(u.Username,
            username?.Trim().ToLowerInvariant(), StringComparison.Ordinal)));

    public Task<PharmaX.Domain.Security.User?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        Task.FromResult(_users.Find(u => u.Id == id));

    public Task AddAsync(PharmaX.Domain.Security.User user,
                         CancellationToken ct = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PharmaX.Domain.Security.User>> ListAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        IReadOnlyList<PharmaX.Domain.Security.User> found = _users
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.Username, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(found);
    }
}

/// <summary>
/// Dispensings held in a dictionary, for the sale tests that have to
/// prove a collection closes the script it paid for.
/// </summary>
public sealed class InMemoryDispensingRepository : IDispensingRepository
{
    private readonly Dictionary<Guid, PharmaX.Domain.Dispensing.Dispensing> _items
        = new();

    public PharmaX.Domain.Dispensing.Dispensing Add(
        PharmaX.Domain.Dispensing.Dispensing dispensing)
    {
        _items[dispensing.Id] = dispensing;
        return dispensing;
    }

    public Task<PharmaX.Domain.Dispensing.Dispensing?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        Task.FromResult(_items.GetValueOrDefault(id));

    public Task<PharmaX.Domain.Dispensing.Dispensing?> ByWarningAsync(Guid warningId,
        CancellationToken ct = default) =>
        Task.FromResult(_items.Values.FirstOrDefault(
            d => d.Warnings.Any(w => w.Id == warningId)));

    public Task<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>> ForPatientAsync(
        Guid patientId, CancellationToken ct = default)
    {
        IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing> found = _items.Values
            .Where(d => d.PatientId == patientId)
            .ToList();

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>>
        AwaitingCollectionAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing> found = _items.Values
            .Where(d => d.AwaitsCollection)
            .OrderBy(d => d.IssuedAtUtc)
            .ToList();

        return Task.FromResult(found);
    }

    public Task AddAsync(PharmaX.Domain.Dispensing.Dispensing dispensing,
        CancellationToken ct = default)
    {
        Add(dispensing);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>> OwingAsync(
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>>(_items.Values
            .Where(d => d.Status == PharmaX.Domain.Dispensing.DispensingStatus.Issued
                        && d.Lines.Any(l => l.Owed != null && l.OwedGivenAtUtc == null))
            .ToList());

    public Task<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>> InProgressAsync(
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PharmaX.Domain.Dispensing.Dispensing>>(_items.Values
            .Where(d => d.Status == PharmaX.Domain.Dispensing.DispensingStatus.InProgress)
            .ToList());
}

/// <summary>
/// The books, in a list.
/// </summary>
/// <remarks>
/// Seeded with the standard chart, so a service test posts against the
/// same account codes a real installation has.
/// </remarks>
public sealed class InMemoryLedgerRepository : ILedgerRepository
{
    private readonly List<LedgerAccount> _accounts = new();
    private readonly List<JournalBatch> _batches = new();

    public InMemoryLedgerRepository()
    {
        foreach (var (code, name, type) in LedgerCode.Standard)
            _accounts.Add(LedgerAccount.Create(code, name, type));
    }

    public IReadOnlyList<JournalBatch> Posted => _batches;

    /// Every posted batch balances, or the books are wrong.
    public bool AllBalance => _batches.TrueForAll(b => b.IsBalanced);

    /// What one account has been debited less what it has been credited.
    public decimal NetOf(string accountCode) => _batches
        .SelectMany(b => b.Lines)
        .Where(l => string.Equals(l.AccountCode, accountCode,
                                  StringComparison.Ordinal))
        .Sum(l => l.IsDebit ? l.Amount.Amount : -l.Amount.Amount);

    public Task<IReadOnlyList<LedgerAccount>> AccountsAsync(
        bool includeInactive = false, CancellationToken ct = default)
    {
        IReadOnlyList<LedgerAccount> found = _accounts
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(found);
    }

    public Task<LedgerAccount?> AccountAsync(Guid id,
        CancellationToken ct = default) =>
        Task.FromResult(_accounts.Find(a => a.Id == id));

    public Task<LedgerAccount?> AccountByCodeAsync(string code,
        CancellationToken ct = default) =>
        Task.FromResult(_accounts.Find(a =>
            string.Equals(a.Code, code, StringComparison.Ordinal)));

    public Task AddAccountAsync(LedgerAccount account,
        CancellationToken ct = default)
    {
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task<JournalBatch?> BatchAsync(Guid id,
        CancellationToken ct = default) =>
        Task.FromResult(_batches.Find(b => b.Id == id));

    public Task<IReadOnlyList<JournalBatch>> BatchesAsync(DateOnly from,
        DateOnly until, CancellationToken ct = default)
    {
        IReadOnlyList<JournalBatch> found = _batches
            .Where(b => b.PostingDate >= from && b.PostingDate <= until)
            .OrderByDescending(b => b.PostingDate)
            .ToList();

        return Task.FromResult(found);
    }

    public Task AddBatchAsync(JournalBatch batch,
        CancellationToken ct = default)
    {
        _batches.Add(batch);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountTotals>> TotalsAsync(DateOnly asAt,
        CancellationToken ct = default)
    {
        IReadOnlyList<AccountTotals> totals = _batches
            .Where(b => b.IsPosted && b.PostingDate <= asAt)
            .SelectMany(b => b.Lines)
            .GroupBy(l => l.AccountCode, StringComparer.Ordinal)
            .Select(g => new AccountTotals(g.Key,
                g.Where(l => l.IsDebit).Sum(l => l.Amount.Amount),
                g.Where(l => !l.IsDebit).Sum(l => l.Amount.Amount)))
            .ToList();

        return Task.FromResult(totals);
    }

    public Task<IReadOnlyList<LedgerEntry>> EntriesAsync(string accountCode,
        DateOnly from, DateOnly until, CancellationToken ct = default)
    {
        IReadOnlyList<LedgerEntry> entries = _batches
            .Where(b => b.IsPosted)
            .Where(b => b.PostingDate >= from && b.PostingDate <= until)
            .SelectMany(b => b.Lines, (batch, line) => new LedgerEntry(
                batch.PostingDate, batch.BatchNumber, batch.Source,
                line.AccountCode,
                line.IsDebit ? line.Amount.Amount : 0m,
                line.IsDebit ? 0m : line.Amount.Amount,
                line.Narration ?? batch.Narration))
            .Where(e => string.Equals(e.AccountCode, accountCode,
                                      StringComparison.Ordinal))
            .ToList();

        return Task.FromResult(entries);
    }
}

/// Claims in a list, for the tests that bill a scheme.
public sealed class InMemoryClaimRepository : IClaimRepository
{
    private readonly List<MedicalAidClaim> _claims = new();

    public IReadOnlyList<MedicalAidClaim> All => _claims;

    public MedicalAidClaim Add(MedicalAidClaim claim)
    {
        _claims.Add(claim);
        return claim;
    }

    public Task<MedicalAidClaim?> GetAsync(Guid id,
        CancellationToken ct = default) =>
        Task.FromResult(_claims.Find(c => c.Id == id));

    public Task<MedicalAidClaim?> ByNumberAsync(string claimNumber,
        CancellationToken ct = default) =>
        Task.FromResult(_claims.Find(c =>
            string.Equals(c.ClaimNumber, claimNumber,
                          StringComparison.Ordinal)));

    public Task<MedicalAidClaim?> ForDispensingAsync(Guid dispensingId,
        CancellationToken ct = default) =>
        Task.FromResult(_claims.Find(c => c.DispensingId == dispensingId));

    public Task<IReadOnlyList<MedicalAidClaim>> OutstandingAsync(
        Guid? schemeId = null, CancellationToken ct = default)
    {
        IReadOnlyList<MedicalAidClaim> found = _claims
            .Where(c => c.IsOpen)
            .Where(c => schemeId is null || c.SchemeId == schemeId)
            .OrderBy(c => c.ClaimedOn)
            .ToList();

        return Task.FromResult(found);
    }

    public Task AddAsync(MedicalAidClaim claim,
        CancellationToken ct = default)
    {
        Add(claim);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Patient accounts in a list, with their movements.
/// </summary>
public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly List<Account> _accounts = new();
    private readonly List<AccountTransaction> _movements = new();

    public IReadOnlyList<AccountTransaction> Movements => _movements;

    public Account Add(Account account)
    {
        _accounts.Add(account);
        return account;
    }

    public Task<Account?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_accounts.Find(a => a.Id == id));

    public Task<Account?> ByNumberAsync(string accountNumber,
        CancellationToken ct = default) =>
        Task.FromResult(_accounts.Find(a =>
            string.Equals(a.AccountNumber, accountNumber,
                          StringComparison.Ordinal)));

    public Task<IReadOnlyList<Account>> SearchAsync(string term,
        int take = 100, CancellationToken ct = default)
    {
        IReadOnlyList<Account> found = _accounts
            .Where(a => string.IsNullOrWhiteSpace(term)
                     || a.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || a.AccountNumber.Contains(term,
                            StringComparison.OrdinalIgnoreCase))
            .Take(take)
            .ToList();

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<Account>> OwingAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<Account> owing = _accounts
            .Where(a => a.Balance.Amount > 0m)
            .OrderByDescending(a => a.Balance.Amount)
            .ToList();

        return Task.FromResult(owing);
    }

    public Task AddAsync(Account account, CancellationToken ct = default)
    {
        Add(account);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountTransaction>> TransactionsAsync(
        Guid accountId, DateOnly from, DateOnly until,
        CancellationToken ct = default)
    {
        IReadOnlyList<AccountTransaction> found = _movements
            .Where(t => t.AccountId == accountId)
            .Where(t => t.OccurredOn >= from && t.OccurredOn <= until)
            .OrderBy(t => t.OccurredOn)
            .ToList();

        return Task.FromResult(found);
    }

    public Task AddTransactionAsync(AccountTransaction transaction,
        CancellationToken ct = default)
    {
        _movements.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<decimal> MovementTotalAsync(Guid accountId,
        CancellationToken ct = default) =>
        Task.FromResult(_movements
            .Where(t => t.AccountId == accountId)
            .Sum(t => t.Effect.Amount));

    public Task<IReadOnlyList<AccountTransaction>> PaymentsForSessionAsync(
        Guid tillSessionId, CancellationToken ct = default)
    {
        IReadOnlyList<AccountTransaction> found = _movements
            .Where(t => t.TillSessionId == tillSessionId
                        && t.Kind == AccountEntryKind.Payment)
            .ToList();

        return Task.FromResult(found);
    }
}

public sealed class InMemoryQuotationRepository
    : PharmaX.Application.Sales.IQuotationRepository
{
    private readonly List<Quotation> _quotations = new();

    public Quotation Add(Quotation quotation)
    {
        _quotations.Add(quotation);
        return quotation;
    }

    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_quotations.Find(q => q.Id == id));

    public Task<IReadOnlyList<Quotation>> RecentAsync(int take = 100,
        QuotationKind? kind = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Quotation>>(_quotations
            .Where(q => kind is null || q.Kind == kind)
            .Take(take).ToList());

    public Task<Quotation?> ByNumberAsync(string quotationNumber,
        CancellationToken ct = default) =>
        Task.FromResult(_quotations.Find(q => string.Equals(q.QuotationNumber,
            quotationNumber?.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Quotation quotation, CancellationToken ct = default)
    {
        _quotations.Add(quotation);
        return Task.CompletedTask;
    }
}

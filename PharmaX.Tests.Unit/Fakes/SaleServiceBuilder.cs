using PharmaX.Application.Abstractions;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Fakes;

/// <summary>
/// Assembles a SaleService with substituted dependencies. Pays for itself
/// by the fifth test: its defaults are sensible, so each test states only
/// what it cares about.
/// </summary>
public sealed class SaleServiceBuilder
{
    public InMemorySaleRepository Sales { get; } = new();
    public InMemoryProductRepository Products { get; }
    public InMemoryBatchRepository Batches { get; } = new();
    public InMemoryDispensingRepository Dispensings { get; } = new();
    public InMemoryLedgerRepository Ledger { get; } = new();
    public InMemoryAccountRepository Accounts { get; } = new();
    public InMemoryQuotationRepository Quotations { get; } = new();
    public InMemoryUserRepository Users { get; } = new();

    /// Whether trading writes journals. On, as on a real installation.
    public bool AutoPost { get; init; } = true;
    public RecordingStockService Stock { get; } = new();
    public FakeDocumentNumbering Numbering { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeOutbox Outbox { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeClock Clock { get; } = new();

    public FakeCurrentUser User { get; private set; } = new();
    public FakePermissionRepository Permissions { get; private set; } = new();

    public SaleServiceBuilder()
    {
        // One product, priced so that a single line comes to 24.00 - the
        // figure the guide's own idempotency test tenders.
        Products = new InMemoryProductRepository(
            Product.Create("PAR0500", "Paracetamol 500mg 20 tablets",
                           Money.Of(24m)));
    }

    public SaleServiceBuilder WithUser(FakeCurrentUser user)
    {
        User = user;
        return this;
    }

    public SaleServiceBuilder WithNoTillSession()
    {
        User.TillSessionId = null;
        return this;
    }

    public SaleServiceBuilder WithPermissions(
        IEnumerable<string>? granted = null,
        IEnumerable<string>? escalatable = null)
    {
        Permissions = new FakePermissionRepository(granted, escalatable);
        return this;
    }

    public IPermissionService PermissionService() =>
        new PermissionService(User, Permissions, Audit);

    /// <summary>
    /// A real journal poster rather than a substitute, so that every
    /// sale test also proves the books balanced afterwards.
    /// </summary>
    public PharmaX.Application.Finance.JournalPoster Journals() =>
        new(Ledger, Numbering,
            new PharmaXOptions
            {
                Accounting = new AccountingOptions { AutoPost = AutoPost }
            },
            Clock);

    /// <summary>
    /// A real account service rather than a substitute, so that a sale
    /// tendered to an account genuinely moves a balance.
    /// </summary>
    public PharmaX.Application.Patients.AccountService AccountService() =>
        new(Accounts,

            // Patients are only read when opening an account, which a
            // sale never does.
            NSubstitute.Substitute.For<PharmaX.Application.Repositories
                .IPatientRepository>(),

            Journals(), Numbering, PermissionService(), Audit, UnitOfWork,
            Clock);

    public SaleService Build() =>
        new(Sales, Products, Batches, Dispensings, Quotations, Users,
            Journals(), Accounts, AccountService(), Stock, Numbering,
            PermissionService(), Audit, Outbox, UnitOfWork, User, Clock);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Reference;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Domain.Tills;
using PharmaX.Persistence.Numbering;
using PharmaX.Persistence.Outbox;

namespace PharmaX.Persistence;

public class PharmaXDbContext : DbContext
{
    public PharmaXDbContext(DbContextOptions<PharmaXDbContext> options)
        : base(options) { }

    /// <summary>
    /// For the shared-database context, which is the same model against
    /// a different provider and therefore needs its own options and its
    /// own migration history.
    /// </summary>
    protected PharmaXDbContext(DbContextOptions options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnLine> SaleReturnLines =>
        Set<SaleReturnLine>();
    public DbSet<TillSession> TillSessions => Set<TillSession>();
    public DbSet<TillDeclaration> TillDeclarations => Set<TillDeclaration>();

    // Money taken out of a drawer during its shift: drawings, lunch,
    // other expenses.
    public DbSet<TillPayout> TillPayouts => Set<TillPayout>();
    public DbSet<Denomination> Denominations => Set<Denomination>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientAttachment> PatientAttachments =>
        Set<PatientAttachment>();
    public DbSet<ClinicalProfile> ClinicalProfiles => Set<ClinicalProfile>();

    // What patients owe the pharmacy, and the movements behind it.
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountTransaction> AccountTransactions =>
        Set<AccountTransaction>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
    public DbSet<DocumentNumberBlock> DocumentNumberBlocks =>
        Set<DocumentNumberBlock>();

    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<WorkstationSetting> WorkstationSettings =>
        Set<WorkstationSetting>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    public DbSet<Domain.Dispensing.Script> Scripts =>
        Set<Domain.Dispensing.Script>();

    // The maintained lists behind the Inventory, Administration and
    // Dispensary maintenance screens.
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Prescriber> Prescribers => Set<Prescriber>();
    public DbSet<DosageCode> DosageCodes => Set<DosageCode>();
    public DbSet<WarningCode> WarningCodes => Set<WarningCode>();
    public DbSet<MedicalAidScheme> MedicalAidSchemes =>
        Set<MedicalAidScheme>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();

    // A priced list for a customer who has not bought anything: no stock
    // moves and no receipt number is consumed.
    public DbSet<Quotation> Quotations => Set<Quotation>();

    // The ledger. Journals are built from day one even where posting to
    // an accounting package is switched off, so the first period that is
    // switched on has something to hand over.
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalBatch> JournalBatches => Set<JournalBatch>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<Domain.Dispensing.Dispensing> Dispensings =>
        Set<Domain.Dispensing.Dispensing>();


    // What the medical aid schemes owe for what has already been
    // dispensed - for a dispensary, the largest debtor it has.
    public DbSet<Domain.Claims.MedicalAidClaim> MedicalAidClaims =>
        Set<Domain.Claims.MedicalAidClaim>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);

        // Picks up every IEntityTypeConfiguration in this assembly, so a new
        // aggregate needs no edit here.
        b.ApplyConfigurationsFromAssembly(typeof(PharmaXDbContext).Assembly);
        PharmaXModelConventions.UseDomainGeneratedKeys(b);
        base.OnModelCreating(b);
    }
}

/// <summary>
/// The pharmacy's shared database, on a machine every workstation can
/// reach - in a small pharmacy, usually the dispensary PC.
/// </summary>
/// <remarks>
/// The same model as the workstation context, which is the point: it
/// derives from it rather than redeclaring anything, so a table added
/// for the till exists here too and cannot drift. It is a separate type
/// only because EF Core keys a migration history to a context type, and
/// the two run on different providers - SQLite on a standalone
/// workstation, MySQL or MariaDB here - which need different SQL.
///
/// Two jobs, depending on how the site is set up. Where
/// Database:SharedConnection is set, this is the database every screen
/// actually reads and writes, and the local SQLite file is unused.
/// Where it is not, this is only what ConnectivityMonitor opens to see
/// whether a host is answering.
/// </remarks>
public sealed class HostDbContext : PharmaXDbContext
{
    public HostDbContext(DbContextOptions<HostDbContext> options)
        : base(options) { }
}

/// <summary>
/// Shared model conventions. Applied after the per-aggregate
/// configurations so that a configuration can still override one.
/// </summary>
internal static class PharmaXModelConventions
{
    /// <summary>
    /// Every identifier in PharmaX is generated by the workstation in the
    /// domain constructor, so records can be created offline without
    /// collision (NET-020). EF Core assumes a Guid key is store-generated,
    /// and therefore reads an already-set key as "this row exists" - which
    /// makes a new child discovered through a navigation come out
    /// Modified rather than Added, and issues an UPDATE against a row
    /// that was never inserted. Declaring the keys never-generated is
    /// what tells EF the truth about who assigns them.
    /// </summary>
    public static void UseDomainGeneratedKeys(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is null) continue;

            foreach (var property in key.Properties
                         .Where(p => p.ClrType == typeof(Guid)))
                property.ValueGenerated = ValueGenerated.Never;
        }
    }
}


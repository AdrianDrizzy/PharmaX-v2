using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Application.Repositories;
using PharmaX.Persistence.Interceptors;
using PharmaX.Persistence.Repositories;
using PharmaX.Persistence.Security;
using PharmaX.Persistence.Seeding;

namespace PharmaX.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// The oldest MariaDB the shared database has to work against.
    /// </summary>
    /// <remarks>
    /// Pinned rather than detected. Pomelo's ServerVersion.AutoDetect
    /// opens a connection, which at start-up means the container cannot
    /// even be built until the server answers - so a pharmacy whose
    /// dispensary PC is still booting would get a connection exception
    /// instead of the sign-in window. Raise this when the estate's
    /// minimum version moves, not before: a higher version here can emit
    /// syntax an older server rejects.
    /// </remarks>
    public static readonly ServerVersion SharedServerVersion =
        new MariaDbServerVersion(new Version(10, 11, 0));

    public static IServiceCollection AddPharmaXPersistence(
        this IServiceCollection services, PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Ambient infrastructure. Registered here rather than in the
        // Desktop project because the audit interceptor below needs both,
        // and an integration test needs them without a form in sight.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<UserSession>();
        services.AddSingleton<ICurrentUser>(sp =>
            sp.GetRequiredService<UserSession>());
        services.AddSingleton<ISessionContext>(sp =>
            sp.GetRequiredService<UserSession>());
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IWorkstationSettings,
            WorkstationSettingStore>();
        services.AddScoped<IPharmacySettings, PharmacySettingStore>();

        // Both interceptors depend only on singletons, so they can be
        // singletons themselves. That matters: the context factory below
        // is resolved from the root provider, which cannot hand out a
        // scoped service.
        services.AddSingleton<AuditInterceptor>();
        services.AddSingleton<RowVersionInterceptor>();

        // Where the screens read and write. Standalone, that is this
        // workstation's own SQLite file and the pharmacy can trade with
        // the host switched off (NET-002). Shared, it is one database on
        // a machine every till can reach, and the tills see each other's
        // work at once at the price of needing that machine up.
        if (options.Database.IsShared)
        {
            AddSharedDatabase(services, options);
        }
        else
        {
            services.AddDbContextFactory<PharmaXDbContext>((sp, builder) =>
            {
                builder.UseSqlite(options.Database.ResolvedLocalConnection);
                builder.AddInterceptors(
                    sp.GetRequiredService<AuditInterceptor>());
                builder.AddInterceptors(
                    sp.GetRequiredService<RowVersionInterceptor>());
            });

            // One context per scope for the services and screens; the
            // factory itself serves the background workers in
            // PharmaX.Sync, which have no scope of their own to take a
            // context from.
            services.AddScoped(sp => sp
                .GetRequiredService<IDbContextFactory<PharmaXDbContext>>()
                .CreateDbContext());
        }

        // The one place in the solution that names a database provider.
        // To move the host to SQL Server, swap the Pomelo package for
        // Microsoft.EntityFrameworkCore.SqlServer (Section 5.2) and
        // UseMySql for UseSqlServer here. Nothing else changes.
        //
        // Skipped on a shared database, where AddSharedDatabase has
        // already registered this factory against the connection the
        // whole pharmacy uses. Registering it twice would silently win
        // with whichever came last - and this one, with no host
        // connection set, leaves the context unconfigured, so the
        // connectivity monitor could never reach anything and every
        // screen would carry a banner saying the database was down
        // while the till was happily writing to it.
        if (!options.Database.IsShared)
        {
            services.AddDbContextFactory<HostDbContext>(builder =>
            {
                // Left unconfigured where no host connection is set, so
                // that a standalone workstation starts rather than
                // throwing on a connection string it will never use
                // (ADM-011).
                if (string.IsNullOrWhiteSpace(options.Database.HostConnection))
                    return;

                builder.UseMySql(options.Database.HostConnection,
                    ServerVersion.AutoDetect(options.Database.HostConnection));
            });
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        // Nothing reads the outbox on a shared database, so nothing
        // should write one: left registered it would add a row per
        // transaction to a table no drainer ever empties.
        if (options.Database.IsShared)
            services.AddScoped<IOutbox, Outbox.NullOutbox>();
        else
            services.AddScoped<IOutbox, Outbox.OutboxWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IDocumentNumbering,
            Numbering.DocumentNumberAllocator>();
        services.AddScoped<IStockMovementWriter, StockMovementWriter>();

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<Application.Finance.IJournalRepository,
                           Repositories.JournalRepository>();
        services.AddScoped<Application.Sales.IQuotationRepository,
                           Repositories.QuotationRepository>();
        services.AddScoped<Application.Patients.IPatientMergeRepository,
                           Repositories.PatientMergeRepository>();
        services.AddScoped<Application.Reference.IReferenceRepository,
                           Repositories.ReferenceRepository>();
        services.AddScoped<Application.Reference.ITariffRepository,
                           Repositories.TariffRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IReturnRepository, ReturnRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITillSessionRepository, TillSessionRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IDispensingRepository, DispensingRepository>();
        services.AddScoped<IScriptRepository, ScriptRepository>();
        services.AddScoped<IStocktakeRepository, StocktakeRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IPurchaseOrderRepository,
            PurchaseOrderRepository>();
        services.AddScoped<IGoodsReceiptRepository,
            GoodsReceiptRepository>();

        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<TestUserSeeder>();
        services.AddScoped<TestCatalogueSeeder>();
        services.AddScoped<DemoTradingSeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<CurrencyNormaliser>();
        services.AddScoped<PriceListSeeder>();
        services.AddScoped<ICurrencyChanger, CurrencyChanger>();

        return services;
    }

    /// <summary>
    /// Points every screen at the pharmacy's shared database.
    /// </summary>
    /// <remarks>
    /// Nothing above persistence changes: services and forms still ask
    /// for a PharmaXDbContext and still get one. What they get is a
    /// HostDbContext, which is the same model on the other provider, and
    /// which brings its own migration history so that the SQLite
    /// migrations are never run against MySQL.
    /// </remarks>
    private static void AddSharedDatabase(IServiceCollection services,
                                          PharmaXOptions options)
    {
        var connection = options.Database.SharedConnection;
        var version = ResolveServerVersion(options.Database.ServerVersion);

        services.AddDbContextFactory<HostDbContext>((sp, builder) =>
        {
            builder.UseMySql(connection, version);
            builder.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
            builder.AddInterceptors(
                sp.GetRequiredService<RowVersionInterceptor>());
        });

        services.AddSingleton<IDbContextFactory<PharmaXDbContext>>(sp =>
            new SharedContextFactory(
                sp.GetRequiredService<IDbContextFactory<HostDbContext>>()));

        services.AddScoped<PharmaXDbContext>(sp => sp
            .GetRequiredService<IDbContextFactory<HostDbContext>>()
            .CreateDbContext());
    }

    /// <summary>
    /// The configured server, or the default where a site has not said.
    /// </summary>
    /// <remarks>
    /// A version that cannot be parsed falls back rather than throwing.
    /// A typo in a settings file should not be the reason a pharmacy
    /// cannot open, and the wrong dialect announces itself quickly and
    /// visibly once somebody tries to use the till.
    /// </remarks>
    private static ServerVersion ResolveServerVersion(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return SharedServerVersion;

        try
        {
            return ServerVersion.Parse(configured);
        }
        catch (InvalidOperationException)
        {
            return SharedServerVersion;
        }
        catch (ArgumentException)
        {
            return SharedServerVersion;
        }
    }

    /// <summary>
    /// Hands the background workers a shared-database context through the
    /// factory interface they already depend on.
    /// </summary>
    private sealed class SharedContextFactory
        : IDbContextFactory<PharmaXDbContext>
    {
        private readonly IDbContextFactory<HostDbContext> _shared;

        public SharedContextFactory(IDbContextFactory<HostDbContext> shared) =>
            _shared = shared;

        public PharmaXDbContext CreateDbContext() =>
            _shared.CreateDbContext();
    }

}

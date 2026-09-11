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

        // Both interceptors depend only on singletons, so they can be
        // singletons themselves. That matters: the context factory below
        // is resolved from the root provider, which cannot hand out a
        // scoped service.
        services.AddSingleton<AuditInterceptor>();
        services.AddSingleton<RowVersionInterceptor>();

        // The workstation always talks to its own local database (NET-002).
        // The host connection is used only by the reconciler.
        services.AddDbContextFactory<PharmaXDbContext>((sp, builder) =>
        {
            builder.UseSqlite(options.Database.ResolvedLocalConnection);
            builder.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
            builder.AddInterceptors(
                sp.GetRequiredService<RowVersionInterceptor>());
        });

        // One context per scope for the services and screens; the factory
        // itself serves the background workers in PharmaX.Sync, which have
        // no scope of their own to take a context from.
        services.AddScoped(sp => sp
            .GetRequiredService<IDbContextFactory<PharmaXDbContext>>()
            .CreateDbContext());

        // The one place in the solution that names a database provider.
        // To move the host to SQL Server, swap the Pomelo package for
        // Microsoft.EntityFrameworkCore.SqlServer (Section 5.2) and
        // UseMySql for UseSqlServer here. Nothing else changes.
        services.AddDbContextFactory<HostDbContext>(builder =>
        {
            // Left unconfigured where no host connection is set, so that a
            // standalone workstation starts rather than throwing on a
            // connection string it will never use (ADM-011).
            if (string.IsNullOrWhiteSpace(options.Database.HostConnection))
                return;

            builder.UseMySql(options.Database.HostConnection,
                ServerVersion.AutoDetect(options.Database.HostConnection));
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutbox, Outbox.OutboxWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IDocumentNumbering,
            Numbering.DocumentNumberAllocator>();
        services.AddScoped<IStockMovementWriter, StockMovementWriter>();

        services.AddScoped<IProductRepository, ProductRepository>();
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
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IPurchaseOrderRepository,
            PurchaseOrderRepository>();
        services.AddScoped<IGoodsReceiptRepository,
            GoodsReceiptRepository>();

        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<TestUserSeeder>();
        services.AddScoped<TestCatalogueSeeder>();
        services.AddScoped<CurrencyNormaliser>();
        services.AddScoped<ICurrencyChanger, CurrencyChanger>();

        return services;
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaX.Adapters;
using PharmaX.Application;
using PharmaX.Application.Abstractions;
using PharmaX.Desktop;
using PharmaX.Desktop.Mvp;
using PharmaX.Persistence;
using PharmaX.Reporting;
using PharmaX.Sync;

namespace PharmaX.Tests.Unit.Architecture;

/// <summary>
/// Builds the container exactly as Program.cs does and resolves every
/// screen from it. "Unable to resolve service for type ..." is the first
/// symptom in the Section 10.2 troubleshooting table, and finding it here
/// costs a second rather than a trip to a till.
/// </summary>
public class ContainerTests
{
    /// <summary>
    /// Every form the shell can open. Kept as a list rather than found by
    /// reflection on purpose: a screen that nobody registered would then
    /// simply not be tested, which is the case that matters.
    /// </summary>
    public static TheoryData<Type> Screens() => new()
    {
        typeof(Desktop.Shell.frmMain),
        typeof(Desktop.Features.Login.frmLogin),
        typeof(Desktop.Features.Sales.frmPOS),
        typeof(Desktop.Features.Sales.frmReturn),
        typeof(Desktop.Features.Dispensing.frmDispense),
        typeof(Desktop.Features.Inventory.frmProductList),
        typeof(Desktop.Features.Inventory.frmProductEdit),
        typeof(Desktop.Features.Tills.frmTillSession),
        typeof(Desktop.Features.Reports.frmReports),
        typeof(Desktop.Features.Admin.frmUserList),
        typeof(Desktop.Features.Admin.frmUserEdit),
        typeof(Desktop.Features.Patients.frmPatientList),
        typeof(Desktop.Features.Patients.frmPatientEdit),
        typeof(Desktop.Features.Dispensing.frmScriptList),
        typeof(Desktop.Features.Dispensing.frmScriptEdit),
        typeof(Desktop.Features.Suppliers.frmSupplierList),
        typeof(Desktop.Features.Suppliers.frmSupplierEdit),
        typeof(Desktop.Features.Suppliers.frmGoodsReceipt),
        typeof(Desktop.Features.Suppliers.frmPurchaseOrderList),
        typeof(Desktop.Features.Inventory.frmStocktakeList),
        typeof(Desktop.Features.Inventory.frmStocktakeCapture)
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_screen_can_be_built_by_the_container(Type screen)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Resolving a Form constructs it, which runs InitializeComponent
        // and therefore also proves the designer code is sound.
        var resolved = scope.ServiceProvider.GetService(screen);

        resolved.Should().NotBeNull(
            $"{screen.Name} is registered but could not be constructed");

        (resolved as IDisposable)?.Dispose();
    }

    /// <summary>
    /// The whole point of Section 8: a form declares its dependencies in
    /// a constructor and the container supplies them. If ViewFactory
    /// cannot be resolved, nothing opens.
    /// </summary>
    [Fact]
    public void The_view_factory_resolves_and_scopes_per_screen()
    {
        using var provider = BuildProvider();

        var factory = provider.GetRequiredService<ViewFactory>();
        factory.Should().NotBeNull();

        // Two scopes must hand out two contexts. One shared context for
        // the life of the process is the bug this asserts against.
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<PharmaXDbContext>()
            .Should().NotBeSameAs(
                second.ServiceProvider
                      .GetRequiredService<PharmaXDbContext>());
    }

    /// <summary>
    /// Scope validation is off in Production, so a captured-dependency
    /// mistake would otherwise be invisible until it misbehaved on site.
    /// Building with validation on is how it stays visible.
    /// </summary>
    [Fact]
    public void The_container_survives_scope_validation()
    {
        var act = () => BuildProvider(validateScopes: true);

        act.Should().NotThrow();
    }

    private static ServiceProvider BuildProvider(bool validateScopes = false)
    {
        var options = new PharmaXOptions
        {
            Database = new DatabaseOptions
            {
                // Never opened: resolving a context does not connect.
                LocalConnection = "Data Source=:memory:",
                HostConnection = ""
            }
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(options);

        services.AddPharmaXPersistence(options);
        services.AddPharmaXAdapters();
        services.AddPharmaXReporting();
        services.AddPharmaXSync(options);
        services.AddPharmaXApplication();
        services.AddPharmaXDesktop();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = validateScopes,
            ValidateOnBuild = validateScopes
        });
    }
}

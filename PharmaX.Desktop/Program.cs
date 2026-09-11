using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaX.Adapters;
using PharmaX.Application;
using PharmaX.Application.Abstractions;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;
using PharmaX.Desktop.Shell;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;
using PharmaX.Persistence.Seeding;
using PharmaX.Reporting;
using PharmaX.Sync;
using Serilog;

namespace PharmaX.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var host = BuildHost();

        // The installed default, so that migration and seeding stamp the
        // right code even on a database that has no settings row yet.
        // PrepareLocalDatabase reads the till's own choice and, if there
        // is one, that wins.
        ApplyCurrency(Currencies.ByCode(
            host.Services.GetRequiredService<PharmaXOptions>()
                .Pharmacy.Currency));

        ConfigureGlobalErrorHandling(host);

        if (!PrepareLocalDatabase(host)) return;

        LoadBranding(host);

        // No scope of its own: ViewFactory gives each screen one.
        var views = host.Services.GetRequiredService<ViewFactory>();

        try
        {
            RunSignInLoop(host, views);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Sign in, work, sign out, sign in again. A shift change is the
    /// commonest thing that happens at a till, so logging off returns to
    /// the login screen rather than closing the application: the till
    /// session, the drawer and the reconciler all carry on.
    /// </summary>
    private static void RunSignInLoop(IHost host, ViewFactory views)
    {
        var started = false;

        while (true)
        {
            // Authenticate before the shell opens. Local credentials, so
            // this works with no host and no internet (SEC-002).
            using (var login = views.Create<frmLogin>())
            {
                if (login.ShowDialog() != DialogResult.OK) return;
            }

            // Started only once past the first login, so the reconciler
            // is not polling a host while the workstation is unattended.
            if (!started)
            {
                host.Start();
                started = true;
            }

            using var shell = views.Create<frmMain>();
            System.Windows.Forms.Application.Run(shell);

            if (!shell.LogOffRequested) return;
        }
    }

    private static IHost BuildHost()
    {
        // ContentRootPath, not the current directory. A shortcut on a
        // till desktop can start the application from anywhere, and the
        // default content root would then look for appsettings.json in
        // the wrong place and throw before there is a log to say so.
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory
            });

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();

        var options = builder.Configuration.Get<PharmaXOptions>()
                      ?? throw new InvalidOperationException(
                          "appsettings.json is missing or malformed.");

        builder.Services.AddSingleton(options);

        // One line per infrastructure project. Each project owns its own
        // registrations, so this file never grows.
        builder.Services.AddPharmaXPersistence(options);
        builder.Services.AddPharmaXAdapters();
        builder.Services.AddPharmaXReporting();
        builder.Services.AddPharmaXSync(options);
        builder.Services.AddPharmaXApplication();
        builder.Services.AddPharmaXDesktop();

        var logDirectory = options.Logging.ResolvedDirectory;
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "pharmax-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.Logging.RetainedFileCount)
            .CreateLogger();

        builder.Services.AddSerilog();

        return builder.Build();
    }

    /// <summary>
    /// Sets the currency every window will format against, and the code
    /// every amount will be stamped with, before any window exists. The
    /// pharmacy's currency, not the workstation's locale: a machine
    /// reinstalled with a different Windows locale must not start
    /// quoting a different currency at customers.
    /// </summary>
    private static void ApplyCurrency(CurrencyOption currency)
    {
        Money.UseCurrency(currency.Code);
        Format.UseCurrency(currency);

        System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
            Format.Culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
            Format.Culture;

        Thread.CurrentThread.CurrentCulture = Format.Culture;
        Thread.CurrentThread.CurrentUICulture = Format.Culture;
    }

    /// <summary>
    /// Reads the pharmacy's logo once, so that the shell, the sign-in
    /// window and every receipt draw the same image without any of them
    /// touching the disk.
    /// </summary>
    private static void LoadBranding(IHost host)
    {
        using var scope = host.Services.CreateScope();

        var path = scope.ServiceProvider
            .GetRequiredService<IWorkstationSettings>()
            .GetAsync(WorkstationSettingKey.LogoPath)
            .GetAwaiter().GetResult();

        host.Services.GetRequiredService<Branding>().Load(path);
    }

    /// <summary>
    /// The most common first-run failure is a missing folder for the
    /// SQLite file, so create it and apply migrations before the login
    /// form opens.
    /// </summary>
    private static bool PrepareLocalDatabase(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<PharmaXOptions>();

        var path = options.Database.ResolvedLocalConnection
            .Replace("Data Source=", string.Empty,
                     StringComparison.OrdinalIgnoreCase)
            .Trim();

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        var db = services.GetRequiredService<PharmaXDbContext>();
        db.Database.Migrate();          // idempotent; safe on every start

        // Refuse to run on a schema the build does not match (ADM-012).
        var pending = db.Database.GetPendingMigrations().ToList();

        if (pending.Count > 0)
        {
            MessageBox.Show(
                "This workstation's database is not up to date. " +
                "Ask support to run the update before trading.",
                "PharmaX", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            return false;
        }

        // The till's own currency, if it has been set here, wins over
        // the installed default. Read before seeding so that the
        // cash-up drawer seeded below is the right one.
        var chosen = services.GetRequiredService<IWorkstationSettings>()
            .GetAsync(WorkstationSettingKey.Currency)
            .GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(chosen))
            ApplyCurrency(Currencies.ByCode(chosen));

        // Every method adds only what is missing, so running the seeder
        // on every start-up is safe and a partially seeded database
        // repairs itself.
        services.GetRequiredService<ReferenceDataSeeder>()
                .SeedAsync().GetAwaiter().GetResult();

        // Off unless Development:SeedTestUsers says otherwise, and that
        // flag lives only in appsettings.Development.json.
        services.GetRequiredService<TestUserSeeder>()
                .SeedAsync().GetAwaiter().GetResult();

        // Accounts with nothing to sell are half a test environment, so
        // the same flag puts stock on the shelf.
        services.GetRequiredService<TestCatalogueSeeder>()
                .SeedAsync().GetAwaiter().GetResult();

        // Last, and every start: an amount stored under a currency this
        // installation no longer trades in cannot be added to one it
        // does, and the first cash-up to mix the two would throw. Costs
        // nothing when they already agree.
        services.GetRequiredService<CurrencyNormaliser>()
                .NormaliseAsync(Money.DefaultCurrency)
                .GetAwaiter().GetResult();

        return true;
    }

    private static void ConfigureGlobalErrorHandling(IHost host)
    {
        // Constructed here so the static handler below has a logger to
        // write to before any form has been resolved.
        host.Services.GetRequiredService<ErrorHandler>();

        System.Windows.Forms.Application.SetUnhandledExceptionMode(
            UnhandledExceptionMode.CatchException);

        System.Windows.Forms.Application.ThreadException += (_, e) =>
            ErrorHandler.Handle(e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorHandler.Handle(e.ExceptionObject as Exception);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaX.Adapters;
using PharmaX.Application;
using PharmaX.Application.Abstractions;
using PharmaX.Assistant;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Mvp;
using PharmaX.Desktop.Setup;
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
    private static void Main(string[] args)
    {
        // A second PharmaX, started by the reports screen to run the
        // assistant's model and nothing else: no window, no database, no
        // log. First, so none of that is even begun. See AssistantHost.
        if (AssistantHost.IsRequested(args))
        {
            Environment.ExitCode = AssistantHost.Run();
            return;
        }

        ApplicationConfiguration.Initialize();

        // First, before any window: Windows Forms refuses to change this
        // once a control exists on the thread, and the first-run wizard
        // makes some - so set later, it stopped every newly set-up machine
        // the moment the wizard closed. The handlers come with the host.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(
            UnhandledExceptionMode.CatchException);

        // The installer, elevated, on the pharmacy's main computer: set
        // up the database and stop. The wizard runs on the next start.
        if (args is ["--setup-server", var msi])
        {
            // Before any window exists, so nothing waits on the thread.
            Environment.ExitCode = ServerSetup.RunAsync(msi).GetAwaiter().GetResult();
            return;
        }

        // A scheduled or scripted run has nobody to answer the first-run
        // wizard - the nightly backup runs where no window can be seen - so
        // on a machine not yet set up it stops rather than waiting forever.
        if (args is ["--backup", ..] or ["--import", ..] or ["--reset-admin"]
                or ["--clear-waiting"]
            && !WorkstationProvisioning.Exists)
        {
            Environment.ExitCode = 2;
            return;
        }

        // Before anything else, and before any logging: a machine that
        // has never been set up has no database to log to and no
        // identity to stamp on what it writes.
        if (!EnsureProvisioned()) return;

        var host = BuildHost();

        // The installed default, so that migration and seeding stamp the
        // right code even on a database that has no settings row yet.
        // PrepareLocalDatabase reads the till's own choice and, if there
        // is one, that wins.
        ApplyCurrency(Currencies.ByCode(
            host.Services.GetRequiredService<PharmaXOptions>()
                .Pharmacy.Currency));

        ConfigureGlobalErrorHandling(host);

        // The nightly scheduled task the installer sets up on the main
        // computer: back the database up and stop.
        if (args is ["--backup", ..])
        {
            Environment.ExitCode = BackUp(host, "nightly") ? 0 : 1;
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            return;
        }

        if (!PrepareLocalDatabase(host))
        {
            // The installer is waiting on this. A zero would look like
            // the old queue had been cleared.
            if (args is ["--clear-waiting"]) Environment.ExitCode = 1;
            return;
        }

        // installer\PharmaX.iss runs this on an upgrade, then stops.
        // PrepareLocalDatabase has already taken the old queue off.
        if (args is ["--clear-waiting"])
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            return;
        }

        // The change-over, from the installer or a technician:
        //   PharmaX.Desktop.exe --import "D:\Exports" [more files or folders] [--dry-run]
        // loads the exported data, saves its report beside the logs and
        // stops. The exit code says whether every total agreed.
        if (args is ["--import", _, ..])
        {
            Environment.ExitCode = Import(host,
                args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList(),
                args.Contains("--dry-run"));
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            return;
        }

        // The administrator's password lost, and nobody else can reset it:
        //   PharmaX.Desktop.exe --reset-admin
        // puts the "admin" sign-in back as installed and stops.
        if (args is ["--reset-admin"])
        {
            using (var scope = host.Services.CreateScope())
                scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>()
                    .ResetAdministratorAsync().GetAwaiter().GetResult();

            Log.Warning("The {Username} sign-in was reset to its starting password",
                ReferenceDataSeeder.AdministratorUsername);
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            return;
        }

        PublishPrinters(host);

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
            LoadPharmacy(host);

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

    /// <summary>
    /// Tells the shared database which printers this computer has, so the
    /// others can list them: a till's receipt printer at the main
    /// computer, the main computer's laser at a till.
    /// </summary>
    /// <remarks>
    /// In the background, and a failure only logged: a computer that cannot
    /// say what printers it has can still sell.
    /// </remarks>
    private static void PublishPrinters(IHost host) =>
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = host.Services.CreateScope();
                await Adapters.Printing.NetworkPrinters.PublishAsync(
                    scope.ServiceProvider.GetRequiredService<IPrinterDirectory>());
            }
#pragma warning disable CA1031 // Logged; see the remarks.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Warning(ex, "Could not report this computer's printers");
            }
        });

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

            // What this machine is, as against what PharmaX is. Written
            // by the first-run wizard into ProgramData, where an upgrade
            // cannot reach it: the installer replaces everything in
            // Program Files, and a workstation identity kept beside the
            // executable would be wiped by the first update - bringing
            // the machine back with a new identity and a numbering
            // series it had already used.
            .AddJsonFile(WorkstationProvisioning.FilePath, optional: true,
                         reloadOnChange: false)

            // Last of the files, so that a developer's own overrides
            // still win on a machine that has also been provisioned.
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
        builder.Services.AddPharmaXAssistant();
        builder.Services.AddPharmaXSync(options);
        builder.Services.AddPharmaXApplication();
        builder.Services.AddPharmaXDesktop();

        var logDirectory = options.Logging.ResolvedDirectory;
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            // Every statement EF runs, at Information, is a line per row an
            // import writes: two hundred megabytes for one change-over.
            // Failed statements are still logged, as warnings and errors.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
                Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "pharmax-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.Logging.RetainedFileCount)
            .CreateLogger();

        builder.Services.AddSerilog();

        return builder.Build();
    }

    /// <summary>
    /// Runs the first-run wizard where this machine has not been set up.
    /// </summary>
    /// <remarks>
    /// Returns false where the operator quit, which is the one path that
    /// ends the application before it has started: without a profile
    /// there is no database to open and no identity to trade under.
    ///
    /// A developer's machine skips it, because appsettings.Development
    /// has already answered every question the wizard asks.
    /// </remarks>
    private static bool EnsureProvisioned()
    {
        if (WorkstationProvisioning.Exists) return true;

        if (File.Exists(Path.Combine(AppContext.BaseDirectory,
                                     "appsettings.Development.json")))
            return true;

        using var wizard = new frmFirstRun();

        if (wizard.ShowDialog() != DialogResult.OK) return false;

        ApplyCurrency(Currencies.ByCode(wizard.Profile.Currency));
        return true;
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

    private static bool BackUp(IHost host, string reason)
    {
        try
        {
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IDatabaseBackup>()
                .BackUpAsync(reason).GetAwaiter().GetResult();
            return true;
        }
#pragma warning disable CA1031 // Logged; the caller decides what a failed backup means.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error(ex, "Backup ({Reason}) failed", reason);
            return false;
        }
    }

    private static int Import(IHost host, IReadOnlyList<string> paths, bool dryRun)
    {
        using var scope = host.Services.CreateScope();

        var report = scope.ServiceProvider
            .GetRequiredService<PharmaX.Application.Import.IDataImporter>()
            .ImportAsync(paths, dryRun).GetAwaiter().GetResult();

        var saved = Features.Admin.frmDataImport.Save(report);

        Log.Information("Data import {Outcome}; report at {Report}",
            report.Succeeded ? "agreed with the export" : "did NOT agree with the export",
            saved);

        return report.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// The pharmacy's logo, colours, name and details, read again before
    /// every sign-in: the admin sets them once in System setup and System
    /// console, and every till and every user has them from their next
    /// sign-in, without a restart.
    /// </summary>
    private static void LoadPharmacy(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var branding = host.Services.GetRequiredService<Branding>();
        var installedLogo = host.Services.GetRequiredService<PharmaXOptions>()
                                .Pharmacy.LogoPath;

        // On the thread pool: after the first sign-in there is a window
        // synchronisation context, and blocking on its awaits would hang.
        var (palette, details, mode) = Task.Run(async () => (
            await branding.RefreshAsync(
                services.GetRequiredService<IPharmacySettings>(),
                services.GetRequiredService<IWorkstationSettings>(),
                installedLogo),
            await services
                .GetRequiredService<PharmaX.Application.Pharmacy.IPharmacyDetailsService>()
                .LoadAsync(),
            await services.GetRequiredService<IWorkstationSettings>()
                .GetAsync(WorkstationSettingKey.Theme))).GetAwaiter().GetResult();

        // The logo's own colours unless a palette was chosen, so a
        // pharmacy that only ever sets its logo gets matching windows -
        // light or dark as this till last chose from the Theme menu.
        PhxTheme.Use(Palette.Resolve(palette, branding.Logo),
                     ThemeMode.Resolve(mode));
        Watermark.Use(branding);

        var profile = host.Services.GetRequiredService<PharmacyProfile>();
        profile.Use(details);
        WindowTitles.Use(profile);
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

        // Only a standalone workstation has a file to make room for. On
        // a shared database there is no local file, and a site that has
        // cleared LocalConnection entirely would otherwise crash here on
        // an empty path before it ever reached the sign-in window.
        if (!options.Database.IsShared)
        {
            var path = options.Database.ResolvedLocalConnection
                .Replace("Data Source=", string.Empty,
                         StringComparison.OrdinalIgnoreCase)
                .Trim();

            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);
        }

        // Creates the database if it is not there and brings it up to
        // date either way. On a shared database this is the server's
        // schema, and the first till to start is the one that builds it.
        var db = services.GetRequiredService<PharmaXDbContext>();

        // A copy before the schema changes under a database that already
        // holds data, so an upgrade that goes wrong can be undone. Only
        // where the database lives: a till leaves it to the main computer.
        if (db.Database.GetAppliedMigrations().Any()
            && db.Database.GetPendingMigrations().Any()
            && services.GetRequiredService<IDatabaseBackup>().HoldsTheDatabase
            && !BackUp(host, "before-upgrade")
            && MessageBox.Show(
                   "PharmaX could not back the database up before updating it. The "
                   + "reason is in the log. Update it anyway?", "PharmaX",
                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

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

        // Settings written before they were kept per workstation belong
        // to this machine: claim them before anything reads one.
        ((WorkstationSettingStore)services
            .GetRequiredService<IWorkstationSettings>())
            .ClaimUnassignedAsync().GetAwaiter().GetResult();

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

        // Same flag again, and last of the seeders because it builds
        // on all of them: the maintained lists, patients, accounts and
        // a day's trading, so that every screen opens with something
        // on it rather than an empty grid.
        services.GetRequiredService<DemoDataSeeder>()
                .SeedAsync().GetAwaiter().GetResult();

        // Last, and every start: an amount stored under a currency this
        // installation no longer trades in cannot be added to one it
        // does, and the first cash-up to mix the two would throw. Costs
        // nothing when they already agree.
        services.GetRequiredService<CurrencyNormaliser>()
                .NormaliseAsync(Money.DefaultCurrency)
                .GetAwaiter().GetResult();

        // Scripts the previous system left on Scripts to Collect. Once,
        // including when the installer starts this with --clear-waiting.
        var cleared = PharmaX.Persistence.Import.ImportedWaitingScripts
            .ClearOnceAsync(db, services.GetRequiredService<IPharmacySettings>())
            .GetAwaiter().GetResult();

        if (cleared > 0)
            Log.Information(
                "Took {Count} script(s) left waiting by the previous system "
                + "off the collect queue. The dispensings stay on file.",
                cleared);

        return true;
    }

    private static void ConfigureGlobalErrorHandling(IHost host)
    {
        // Constructed here so the static handler below has a logger to
        // write to before any form has been resolved.
        host.Services.GetRequiredService<ErrorHandler>();

        System.Windows.Forms.Application.ThreadException += (_, e) =>
            ErrorHandler.Handle(e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorHandler.Handle(e.ExceptionObject as Exception);
    }
}

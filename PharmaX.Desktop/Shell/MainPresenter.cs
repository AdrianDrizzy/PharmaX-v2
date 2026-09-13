using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Alerts;
using PharmaX.Application.Offline;
using PharmaX.Application.Reference;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Desktop.Features.Admin;
using PharmaX.Desktop.Features.Dispensing;
using PharmaX.Desktop.Features.Finance;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Patients;
using PharmaX.Desktop.Features.Reference;
using PharmaX.Desktop.Features.Reports;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Features.Suppliers;
using PharmaX.Desktop.Features.Tills;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Shell;

public sealed class MainPresenter : PresenterBase<IMainView>
{
    private readonly ICurrentUser _user;
    private readonly PharmaXOptions _options;
    private readonly ITillSessionService _sessions;
    private readonly IPermissionService _permissions;
    private readonly IConnectivityMonitor _connectivity;
    private readonly IAuthenticationService _auth;
    private readonly ViewFactory _views;
    private readonly IServiceScopeFactory _scopes;

    public MainPresenter(ICurrentUser user, PharmaXOptions options,
        ITillSessionService sessions, IPermissionService permissions,
        IConnectivityMonitor connectivity, IAuthenticationService auth,
        ViewFactory views, IServiceScopeFactory scopes)
    {
        _user = user; _options = options; _sessions = sessions;
        _permissions = permissions; _connectivity = connectivity;
        _auth = auth; _views = views; _scopes = scopes;
    }

    protected override void Subscribe()
    {
        View.MenuSelected += (_, key) => _ = OnMenuAsync(key);
        View.WaitingTick += (_, _) => _ = ShowWaitingAsync();
        View.WaitingTick += (_, _) => _ = ShowAlertsAsync();
        _connectivity.StateChanged += (_, s) => View.SetConnectivity(Describe(s));
    }

    public override async Task InitialiseAsync()
    {
        View.SetPharmacy(_options.Pharmacy.TradingName);
        View.SetUser(_user.Username, string.Join(", ", RolesOf(_user)));

        // A window that is running against seeded test credentials says
        // so in its title, so a screenshot cannot be mistaken for a site.
        if (_options.Development.SeedTestUsers)
            View.MarkAsTestEnvironment();
        View.SetTill($"Branch {_options.Workstation.BranchCode} " +
                     $"Till {_options.Workstation.TillNumber}");
        View.SetConnectivity(Describe(_connectivity.State));

        var session = await _sessions.CurrentAsync();
        View.SetSession(session is null ? "No open session" : "Session open");

        // Hide what this user may not do, then let the service refuse
        // anyway if the menu is ever wrong.
        View.EnableMenu("pos",
            _permissions.Check(PermissionCode.SaleCreate)
                == PermissionOutcome.Granted);
        View.EnableMenu("returns",
            _permissions.Check(PermissionCode.ReturnAuthorise)
                != PermissionOutcome.Denied);
        View.EnableMenu("dispensary",
            _permissions.Check(PermissionCode.ScriptAuthorise)
                == PermissionOutcome.Granted);
        View.EnableMenu("inventory",
            _permissions.Check(PermissionCode.ProductRead)
                == PermissionOutcome.Granted);
        View.EnableMenu("goods-receipt",
            _permissions.Check(PermissionCode.GoodsReceive)
                == PermissionOutcome.Granted);
        View.EnableMenu("stocktake",
            _permissions.Check(PermissionCode.ProductRead)
                == PermissionOutcome.Granted);
        View.EnableMenu("reports",
            _permissions.Check(PermissionCode.ReportOperational)
                == PermissionOutcome.Granted);
        View.EnableMenu("accounts",
            _permissions.Check(PermissionCode.LedgerRead)
                == PermissionOutcome.Granted);
        View.EnableMenu("admin",
            _permissions.Check(PermissionCode.UserRoleAssign)
                == PermissionOutcome.Granted);

        await ShowWaitingAsync();
        await ShowAlertsAsync();
    }

    /// <summary>
    /// Counts the stock that needs attention and puts it in the status
    /// bar, beside the collection queue.
    /// </summary>
    /// <remarks>
    /// Its own scope and silent on failure, for the same reasons as
    /// ShowWaitingAsync above.
    /// </remarks>
    private async Task ShowAlertsAsync()
    {
        if (_permissions.Check(PermissionCode.ProductRead)
                != PermissionOutcome.Granted)
            return;

        try
        {
            using var scope = _scopes.CreateScope();

            var alerts = scope.ServiceProvider
                .GetRequiredService<IStockAlertService>();

            var result = await alerts.CurrentAsync();
            if (result.IsFailure) return;

            // The count that matters is the urgent one. A pharmacy with
            // forty slow-moving lines below reorder level would
            // otherwise show a permanent forty, and a permanent number
            // is one nobody reads.
            var critical = result.Value
                .Count(a => a.Severity == AlertSeverity.Critical);

            View.SetAlerts(critical switch
            {
                0 => "",
                1 => "1 stock alert",
                var many => $"{many} stock alerts"
            });
        }
        catch (InvalidOperationException)
        {
            // The database is unreachable, which the connectivity label
            // beside this one is already reporting.
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // As above.
        }
    }

    /// <summary>
    /// Reads how many dispensed scripts are waiting to be paid for and
    /// puts the number in the status bar.
    /// </summary>
    /// <remarks>
    /// Its own container scope every time, rather than a service held
    /// for the life of the shell. The shell is open all day, and a
    /// DbContext that lived that long would both cache the queue it is
    /// supposed to be re-reading and accumulate every entity it ever
    /// loaded.
    ///
    /// Silent on failure. This is a number in the corner of a status
    /// bar; a database that has gone away is already being reported by
    /// the connectivity label an inch to its right, and saying it twice
    /// - in a message box, over whatever the operator was doing - would
    /// be worse than not saying it here at all.
    /// </remarks>
    private async Task ShowWaitingAsync()
    {
        if (_permissions.Check(PermissionCode.SaleCreate)
                != PermissionOutcome.Granted)
            return;

        try
        {
            using var scope = _scopes.CreateScope();

            var collections = scope.ServiceProvider
                .GetRequiredService<ICollectionService>();

            var result = await collections.WaitingAsync();
            if (result.IsFailure) return;

            View.SetWaiting(result.Value.Count switch
            {
                0 => "",
                1 => "1 script waiting",
                var many => $"{many} scripts waiting"
            });
        }
        catch (InvalidOperationException)
        {
            // The scope could not be built, or the database is not
            // reachable. Neither is this label's business to report.
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // As above.
        }
    }

    private Task OnMenuAsync(string key) => GuardAsync(async () =>
    {
        switch (key)
        {
            case "pos":
                _views.ShowDialog<frmPOS>((Form)View);
                break;
            case "collect":
                _views.ShowDialog<frmCollect>((Form)View);
                await ShowWaitingAsync();
                break;
            case "returns":
                _views.ShowDialog<frmReturn>((Form)View);
                break;
            case "dispensing":
                _views.ShowDialog<frmDispense>((Form)View);
                break;
            case "products":
                _views.ShowDialog<frmProductList>((Form)View);
                break;
            case "suppliers":
                _views.ShowDialog<frmSupplierList>((Form)View);
                break;
            case "goods-receipt":
                _views.ShowDialog<frmGoodsReceipt>((Form)View);
                break;
            case "purchase-orders":
                _views.ShowDialog<frmPurchaseOrderList>((Form)View);
                break;

            // The guide's menu lists new, capture, variance and post as
            // four entries. They are four views of one count, so they
            // all reach the same hub; only "new" skips straight to the
            // scope prompt.
            case "stocktake-new":
                ShowStocktakes(startNew: true);
                break;
            case "stocktake-capture":
            case "stocktake-variance":
            case "stocktake-post":
                ShowStocktakes(startNew: false);
                break;
            case "reports":
                ShowReport(preset: null);
                break;

            // Menu entries the staff know by an incumbent name that the
            // report catalogue already answers. Opening the report
            // screen on the right preset reuses the engine rather than
            // building a second list over the same rows.
            case "stock":
                ShowReport("inv.stock.on-hand");
                break;
            case "trial-balance":
                _views.ShowDialog<frmTrialBalance>((Form)View);
                break;
            case "journal":
                _views.ShowDialog<frmJournal>((Form)View);
                break;
            case "chart-of-accounts":
                _views.ShowDialog<frmChartOfAccounts>((Form)View);
                break;
            case "alerts":
                _views.ShowDialog<frmAlerts>((Form)View);
                await ShowAlertsAsync();
                break;
            case "expired-stock":
                ShowReport("inv.stock.expiring");
                break;
            case "transfers":
                ShowReport("inv.stock.transfers");
                break;
            case "breakages":
                ShowReport("inv.stock.breakages");
                break;
            case "price-changes":
                ShowReport("inv.price.changes");
                break;
            case "vat-codes":
                ShowReport("pos.vat.summary");
                break;
            case "transaction-types":
                ShowReport("inv.stock.transactions");
                break;

            // The maintained lists. One screen, told which list it is.
            case "departments":
                ShowReferenceList(ReferenceKind.Department);
                break;
            case "manufacturers":
                ShowReferenceList(ReferenceKind.Manufacturer);
                break;
            case "branches":
                ShowReferenceList(ReferenceKind.Branch);
                break;
            case "schemes":
                ShowReferenceList(ReferenceKind.MedicalAidScheme);
                break;
            case "tariffs":
                _views.ShowDialog<frmTariffList>((Form)View);
                break;
            case "merge-patients":
                _views.ShowDialog<frmPatientMerge>((Form)View);
                break;
            case "labels":
                _views.ShowDialog<frmLabels>((Form)View);
                break;
            case "quotation":
                _views.ShowDialog<frmQuotation>((Form)View);
                break;

            // What this till is set to, and what it is doing. Two views
            // of the same read-only list.
            case "control-variables":
                ShowSystemInfo(SystemInfoMode.ControlVariables);
                break;
            case "system-console":
                ShowSystemInfo(SystemInfoMode.SystemConsole);
                break;

            // A reprint starts where the pharmacist starts: the person.
            case "reprint-script":
                ShowScriptsForChosenPatient();
                break;

            // Stock coming back from a patient is credited on the same
            // screen as anything else returned over the counter.
            case "drug-returns":
                _views.ShowDialog<frmReturn>((Form)View);
                break;
            case "till":
                _views.ShowDialog<frmTillSession>((Form)View);
                break;
            case "users":
                _views.ShowDialog<frmUserList>((Form)View);
                break;
            case "patients":
                _views.ShowDialog<frmPatientList>((Form)View);
                break;
            case "script-maintenance":
                ShowScriptsForChosenPatient();
                break;
            case "printer-setup":
                _views.ShowDialog<Features.Admin.frmPrinterSetup>((Form)View);
                return;
            case "system-setup":
                _views.ShowDialog<Features.Admin.frmSystemSetup>((Form)View);

                // Currency and logo both show on the shell, so it
                // repaints rather than waiting for a restart.
                View.RefreshBranding();
                return;
            case "change-password":
                ChangeOwnPassword();
                return;
            case "logoff":
                LogOff();
                return;
            case "about":
                ShowAbout();
                return;
            case "exit":
                if (Confirmed("Close PharmaX?")) View.CloseView(true);
                return;
            default:
                View.ShowInfo("That part of PharmaX is not built yet.");
                break;
        }

        var session = await _sessions.CurrentAsync();
        View.SetSession(session is null ? "No open session" : "Session open");
    });

    /// <summary>
    /// A script list only means anything in the context of one patient,
    /// so the menu entry picks the patient first. Two dialogs rather
    /// than one screen with a hidden mode, because that is what the
    /// pharmacist is actually doing: find the person, then their scripts.
    /// </summary>
    /// <summary>
    /// Signs out and hands the shell back to the login screen. A shift
    /// change is the commonest thing that happens at a till, and it
    /// should not need the application restarted.
    /// </summary>
    private void LogOff()
    {
        if (!Confirmed($"Sign {_user.Username} out?")) return;

        _auth.SignOut();

        // The till session deliberately survives: the drawer is still
        // open and the next operator inherits it.
        View.LogOff();
    }

    private void ChangeOwnPassword()
    {
        using var dialog = new Features.Login.frmChangePassword(
            _auth, _user.Username, currentPassword: "");

        if (dialog.ShowDialog((Form)View) == DialogResult.OK)
            View.ShowInfo("Password changed.");
    }

    private void ShowAbout() =>
        View.ShowInfo(
            $"PharmaX {typeof(MainPresenter).Assembly.GetName().Version}"
            + Environment.NewLine + Environment.NewLine
            + $"{_options.Pharmacy.TradingName}" + Environment.NewLine
            + $"Branch {_options.Workstation.BranchCode}, "
            + $"till {_options.Workstation.TillNumber}" + Environment.NewLine
            + $"Signed in as {_user.Username}");

    private bool Confirmed(string question) => View.Confirm(question);

    /// <summary>
    /// Opens the report screen, on a named preset where the menu entry
    /// stands for one particular report.
    /// </summary>
    private void ShowReport(string? preset)
    {
        using var reports = _views.Create<frmReports>();
        reports.OpenOnPreset = preset;
        reports.ShowDialog((Form)View);
    }

    private void ShowReferenceList(ReferenceKind kind)
    {
        using var list = _views.Create<frmReferenceList>();
        list.Prepare(kind);
        list.ShowDialog((Form)View);
    }

    private void ShowSystemInfo(SystemInfoMode mode)
    {
        using var window = _views.Create<frmSystemInfo>();
        window.Mode = mode;
        window.ShowDialog((Form)View);
    }

    private void ShowStocktakes(bool startNew)
    {
        using var list = _views.Create<frmStocktakeList>();
        list.StartNewImmediately = startNew;
        list.ShowDialog((Form)View);
    }

    private void ShowScriptsForChosenPatient()
    {
        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog((Form)View) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid patientId) return;

        using var scripts = _views.Create<frmScriptList>();
        scripts.LoadForPatient(patientId);
        scripts.ShowDialog((Form)View);
    }

    private static string Describe(ConnectivityState state) => state switch
    {
        ConnectivityState.Connected => "Host connected",
        ConnectivityState.Degraded => "Host slow",
        _ => "Working locally"
    };

    private static IEnumerable<string> RolesOf(ICurrentUser u) =>
        Role.Standard.All.Where(u.IsInRole);
}

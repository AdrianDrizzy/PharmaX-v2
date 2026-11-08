using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Alerts;
using PharmaX.Application.Home;
using PharmaX.Application.Offline;
using PharmaX.Application.Reference;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
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

public sealed partial class MainPresenter : PresenterBase<IMainView>
{
    private readonly ICurrentUser _user;
    private readonly PharmaXOptions _options;
    private readonly PharmacyProfile _pharmacy;
    private readonly ITillSessionService _sessions;
    private readonly IPermissionService _permissions;
    private readonly IConnectivityMonitor _connectivity;
    private readonly IAuthenticationService _auth;
    private readonly ViewFactory _views;
    private readonly IServiceScopeFactory _scopes;

    public MainPresenter(ICurrentUser user, PharmaXOptions options,
        PharmacyProfile pharmacy, ITillSessionService sessions, IPermissionService permissions,
        IConnectivityMonitor connectivity, IAuthenticationService auth,
        ViewFactory views, IServiceScopeFactory scopes,
        Features.Assistant.AssistantTasks tasks)
    {
        _user = user; _options = options; _pharmacy = pharmacy;
        _sessions = sessions;
        _permissions = permissions; _connectivity = connectivity;
        _auth = auth; _views = views; _scopes = scopes;
        _tasks = tasks;
    }

    private readonly Features.Assistant.AssistantTasks _tasks;

    protected override void Subscribe()
    {
        View.MenuSelected += (_, key) => Enqueue(() => OnMenuAsync(key));

        // A job asked of Ask PharmaX, from whichever screen it was opened over.
        _tasks.HandleWith(task => Enqueue(() => RunTaskAsync(task)));
        _tasks.OpenWith(saved => Enqueue(() => OpenSavedAsync(saved)));

        // Kept for this till, and read back before the next sign-in.
        View.ThemeChosen += (_, id) => Enqueue(() =>
            SaveAsync(WorkstationSettingKey.Theme, id));

        // The View menu's choices likewise.
        View.ZoomChosen += (_, percent) => Enqueue(() => SaveAsync(
            WorkstationSettingKey.Zoom,
            percent.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        View.WindowSizeChosen += (_, size) => Enqueue(() => SaveAsync(
            WorkstationSettingKey.WindowSize, size));
        // Not queued: each count reads through its own scope, and the
        // queue is held for as long as a menu screen is open - a tick
        // every fifteen seconds would pile up behind it all afternoon.
        View.WaitingTick += (_, _) => _ = ShowWaitingAsync();
        View.WaitingTick += (_, _) => _ = ShowAlertsAsync();

        // The tiles' figures too, so "today" stays today: another till's
        // sales, a patient added at the dispensary, and the roll-over at
        // midnight all show without waiting for a screen to close here.
        View.WaitingTick += (_, _) => _ = ShowCountsAsync();
        _connectivity.StateChanged += (_, s) => View.SetConnectivity(Describe(s));
        SubscribeStockAssistant();
    }

    /// A choice kept for this till, read back before the next sign-in.
    private Task SaveAsync(string key, string value) => GuardAsync(async () =>
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IWorkstationSettings>()
            .SetAsync(key, value);
    });

    public override async Task InitialiseAsync()
    {
        using (var scope = _scopes.CreateScope())
            View.UseWindowSize(await scope.ServiceProvider
                .GetRequiredService<IWorkstationSettings>()
                .GetAsync(WorkstationSettingKey.WindowSize));

        ShowPharmacy();
        View.SetUser(_user.Username, string.Join(", ", RolesOf(_user)));

        // A window that is running against seeded test credentials says
        // so in its title, so a screenshot cannot be mistaken for a site.
        if (_options.Development.SeedTestUsers)
            View.MarkAsTestEnvironment();
        View.SetTill($"Branch {_options.Workstation.BranchCode} " +
                     $"Till {_options.Workstation.TillNumber}");
        View.SetConnectivity(Describe(_connectivity.State));

        var session = await _sessions.CurrentAsync();
        View.SetSession(SessionStatus(session));

        // Offer each screen on its own merits, from the same policy the
        // guard in OnMenuAsync reads - and every service still refuses
        // for itself if the menu is ever wrong.
        View.ApplyAccess(key => ScreenAccess.CanOpen(key, _permissions));

        await OfferEarlierCashUpAsync(session);
        await ShowWaitingAsync();
        await ShowAlertsAsync();
        await ShowCountsAsync();
    }

    /// <summary>
    /// The pharmacy in numbers on the home tiles, each figure only on a
    /// tile this user may open: a cashier sees how many products the shop
    /// sells, and no patient count on a Patients tile they cannot use.
    /// </summary>
    /// <remarks>
    /// Its own scope and silent on failure, as the status bar counts are:
    /// figures on a home screen are not worth a message box.
    /// </remarks>
    /// A figure on a tile, where this user may open it.
    private void Show(string tile, string? count) =>
        View.SetTileCount(tile,
            ScreenAccess.CanOpen(tile, _permissions) ? count : null);

    /// A line on a tile, where this user may open it.
    private void Note(string tile, string note) =>
        View.SetTileNote(tile,
            ScreenAccess.CanOpen(tile, _permissions) ? note : null);

    private static string Number(int value) =>
        value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// "6,725", or "6,725 · 3 new today".
    private static string OnFile(int total, int addedToday) =>
        addedToday == 0 ? Number(total) : $"{Number(total)} · {Number(addedToday)} new today";

    private async Task ShowCountsAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();

            var c = await scope.ServiceProvider
                .GetRequiredService<IHomeCounts>()
                .ReadAsync(_permissions.ProductScope());

            // Each on the tile it belongs to: what is on file with what was
            // added to it today, and what has been done today. Nothing for
            // a figure that is nought and means "nothing to do", as the
            // status bar does.
            Show("patients", OnFile(c.Patients, c.PatientsAddedToday));

            // The tile counts people; the files behind them are more where
            // a phone number and address were entered more than once.
            if (c.PatientFiles > c.Patients)
                Note("patients", $"{Number(c.PatientFiles)} files, "
                    + $"{Number(c.PatientFiles - c.Patients)} repeated");
            Show("products", OnFile(c.Products, c.ProductsAddedToday));
            Show("pos", $"{Number(c.SalesToday)} today");
            Show("dispensing", $"{Number(c.ScriptsToday)} today");
            Show("returns", $"{Number(c.ReturnsToday)} today");
            Show("goods-receipt", $"{Number(c.DeliveriesToday)} today");
            Show("purchase-orders", c.OpenPurchaseOrders == 0
                ? null : $"{Number(c.OpenPurchaseOrders)} open");
            Show("stocktake-capture", c.OpenStocktakes == 0
                ? null : $"{Number(c.OpenStocktakes)} open");

            // And when the jobs that come round now and again were last
            // done, so a count or an order that is overdue is seen.
            Note("stocktake-capture", c.LastStocktakeUtc is DateTime counted
                ? $"Last posted {Format.Date(counted.ToLocalTime())}"
                : "No count posted yet");
            Note("purchase-orders", c.LastPurchaseOrderOn is DateOnly ordered
                ? $"Last raised {Format.Date(ordered)}"
                : "No order raised yet");
            Note("reports", c.LastReportRunUtc is DateTime run
                ? $"Last run {Format.DateTimeShort(run.ToLocalTime())}"
                : "No report run yet");
        }
        catch (InvalidOperationException)
        {
            // The database is unreachable, which the connectivity label
            // is already reporting.
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // As above.
        }
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
        // Only for the people who look after stock, the same people the
        // Stock Alerts screen is offered to.
        if (!ScreenAccess.CanOpen("alerts", _permissions))
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

            Show("alerts", critical == 0 ? null : $"{Number(critical)} urgent");
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

            Show("collect", result.Value.Count == 0
                ? null : $"{Number(result.Value.Count)} waiting");
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
        // The menu and tiles already hide what this user may not open,
        // so reaching here with one means a shortcut or a stale window.
        // Refused, and recorded, rather than opened and left to the
        // service to refuse after the screen has been filled in.
        if (ScreenAccess.Keys.Contains(key)
            && !ScreenAccess.CanOpen(key, _permissions))
        {
            View.ShowError("You do not have permission for this action.");
            return;
        }

        switch (key)
        {
            case "pos":
                Open<frmPOS>(key);
                break;
            case "collect":
                Open<frmCollect>(key, closed: ShowWaitingAsync);
                break;
            case "returns":
                Open<frmReturn>(key);
                break;
            case "dispensing":
                Open<frmDispense>(key);
                break;
            case "products":
                Open<frmProductList>(key);
                break;
            case "suppliers":
                Open<frmSupplierList>(key);
                break;
            case "goods-receipt":
                Open<frmGoodsReceipt>(key);
                break;
            case "purchase-orders":
                Open<frmPurchaseOrderList>(key);
                break;

            // Four views of one count, through the same hub, each opened
            // at its own step: three entries that all showed the list
            // read as one screen under three names.
            case "stocktake-new":
                ShowStocktakes(StocktakeStep.New);
                break;
            case "stocktake-capture":
                ShowStocktakes(StocktakeStep.Capture);
                break;
            case "stocktake-variance":
                ShowStocktakes(StocktakeStep.Variance);
                break;
            case "stocktake-post":
                ShowStocktakes(StocktakeStep.Post);
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

            // Breakages, expired stock and transfers recorded, not only
            // reported on (ADM-DEF-05).
            case "stock-adjust":
                Open<frmStockAdjustment>(key);
                break;
            case "prepack":
                Open<frmPrepack>(key);
                break;
            case "supplier-return":
            case "move-stock":
                Open<frmStockAdjustment>(key, adjust => adjust.Preselect(key == "move-stock"
                    ? Application.Inventory.StockAdjustmentKind.MoveToItem
                    : Application.Inventory.StockAdjustmentKind.ReturnToSupplier));
                break;
            case "trial-balance":
                Open<frmTrialBalance>(key);
                break;
            case "journal":
                Open<frmJournal>(key);
                break;
            case "accounts-receivable":
                Open<Features.Patients.frmAccounts>(key);
                break;
            case "claims":
                Open<frmClaims>(key);
                break;
            case "chart-of-accounts":
                Open<frmChartOfAccounts>(key);
                break;
            case "alerts":
                Open<frmAlerts>(key, closed: ShowAlertsAsync);
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
            case "categories":
                ShowReferenceList(ReferenceKind.Category);
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
            case "prescribers":
                ShowReferenceList(ReferenceKind.Prescriber);
                break;
            case "to-follow":
                Open<Features.Dispensing.frmToFollow>(key);
                break;
            case "dosage-codes":
                ShowReferenceList(ReferenceKind.DosageCode);
                break;
            case "tariffs":
                Open<frmTariffList>(key);
                break;
            case "merge-patients":
                Open<frmPatientMerge>(key);
                break;
            case "labels":
                Open<frmLabels>(key);
                break;
            case "quotation":
                Open<frmQuotation>(key);
                break;
            case "dispensary-quote":
                ShowDispensaryQuote();
                break;

            // What this till is set to, and what it is doing. Two views
            // of the same read-only list.
            // A day's takings, turned into a balanced journal.
            case "day-end":
                Open<frmDayEnd>(key);
                break;

            case "control-variables":
                ShowSystemInfo(SystemInfoMode.ControlVariables);
                break;
            case "system-console":
                ShowSystemInfo(SystemInfoMode.SystemConsole);
                break;

            // A reprint starts where the pharmacist starts: the person,
            // then which of their scripts - and then it prints. It used to
            // stop at the list, the same one Script Maintenance opens
            // (ADM-DEF-17).
            case "reprint-script":
                await ReprintChosenScriptAsync();
                break;

            // Stock coming back from a patient is credited on the same
            // screen as anything else returned over the counter.
            case "drug-returns":
                Open<frmReturn>(key);
                break;
            case "till":
                Open<frmTillSession>(key);
                break;
            case "cash-up":
                Open<frmCashUp>(key);
                break;
            case "end-of-day":
                Open<frmEndOfDay>(key);
                break;
            case "users":
                Open<frmUserList>(key);
                break;
            case "patients":
                Open<frmPatientList>(key);
                break;
            case "script-maintenance":
                ShowScriptsForChosenPatient();
                break;
            case "printer-setup":
                _views.ShowDialog<Features.Admin.frmPrinterSetup>((Form)View);
                return;
            case "backup":
                BackUpNow();
                return;
            case "data-import":
                _views.ShowDialog<Features.Admin.frmDataImport>((Form)View);
                break;
            case "mass-price":
                await MassPriceChangeAsync();
                return;
            case "system-setup":
                // Currency and logo both show on the shell, so it
                // repaints once setup closes rather than waiting for a restart.
                Open<Features.Admin.frmSystemSetup>(key, closed: () =>
                {
                    View.RefreshBranding();
                    return Task.CompletedTask;
                });
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
            case "receipts":
                Open<frmReceipts>(key);
                return;
            case "receipt-template":
                Open<Features.Admin.frmReceiptTemplate>(key);
                return;
            case "assistant":
                _views.ShowDialog<Features.Assistant.frmAssistant>((Form)View);
                return;
            case "exit":
                if (Confirmed("Close the application?")) View.CloseView(true);
                return;
            default:
                View.ShowInfo("That part of the system is not built yet.");
                break;
        }

        await CatchUpAsync();
    });

    /// <summary>
    /// Opens a screen beside the shell and any others already open (see
    /// ViewFactory.Show), and catches the home screen up once it closes.
    /// </summary>
    /// <param name="key">One window per key: the same entry again brings
    /// it to the front.</param>
    /// <summary>
    /// Opens the screen for a job asked of Ask PharmaX, filled in as far
    /// as the request went. The operator finishes and saves it there.
    /// </summary>
    private Task RunTaskAsync(Application.Assistant.AppTask task) => GuardAsync(async () =>
    {
        var key = task.Kind switch
        {
            Application.Assistant.AppTaskKind.NewProduct => "products",
            Application.Assistant.AppTaskKind.NewPatient => "patients",
            Application.Assistant.AppTaskKind.Sell => "pos",
            Application.Assistant.AppTaskKind.ReceiveStock => "goods-receipt",
            Application.Assistant.AppTaskKind.PurchaseOrder => "purchase-orders",
            _ => "dispensing"
        };

        // The same gate as the menu: a cashier who asks to dispense is
        // told no, not shown the screen.
        if (ScreenAccess.Keys.Contains(key) && !ScreenAccess.CanOpen(key, _permissions))
        {
            View.ShowError("You do not have permission for this action.");
            return;
        }

        switch (task.Kind)
        {
            case Application.Assistant.AppTaskKind.NewProduct:
            {
                using var editor = _views.Create<frmProductEdit>();
                editor.LoadProduct(null);
                editor.PrefillDescription(task.Item);
                editor.ShowDialog((Form)View);
                break;
            }

            case Application.Assistant.AppTaskKind.NewPatient:
            {
                using var editor = _views.Create<frmPatientEdit>();
                editor.LoadPatient(null);
                editor.PrefillName(task.Person);
                editor.ShowDialog((Form)View);
                break;
            }

            case Application.Assistant.AppTaskKind.Sell:
                var code = await ProductCodeForAsync(task.Item);
                Open<frmPOS>(key, pos => pos.StartWith(code ?? task.Item, code is not null, task.Quantity));
                break;

            case Application.Assistant.AppTaskKind.ReceiveStock:
                Open<frmGoodsReceipt>(key);
                break;

            case Application.Assistant.AppTaskKind.PurchaseOrder:
                var line = await OrderLineForAsync(task.Item, task.Quantity);
                Open<frmPurchaseOrderList>(key, list => list.RaiseOnOpen(line));
                break;

            case Application.Assistant.AppTaskKind.Dispense:
                Open<frmDispense>(key, dispense => dispense.StartFor(task.Person, task.Item));
                break;
        }

        await CatchUpAsync();
    });

    /// <summary>
    /// Opens what a guided job in Ask PharmaX saved - or the existing file
    /// it chose instead - on its own screen, for the operator to see.
    /// </summary>
    private Task OpenSavedAsync(Application.Assistant.Guided.GuidedResult saved) => GuardAsync(async () =>
    {
        switch (saved.Kind)
        {
            case Application.Assistant.AppTaskKind.NewProduct:
            {
                using var editor = _views.Create<frmProductEdit>();
                editor.LoadProduct(saved.Id);
                editor.ShowDialog((Form)View);
                break;
            }

            case Application.Assistant.AppTaskKind.NewPatient:
            {
                using var editor = _views.Create<frmPatientEdit>();
                editor.LoadPatient(saved.Id);
                editor.ShowDialog((Form)View);
                break;
            }

            // A sale finalised in the conversation: the drawer and the
            // receipt, as the till does them after taking the money. A
            // basket held for the cashier has no number and prints nothing.
            case Application.Assistant.AppTaskKind.Sell when saved.Reference.Length > 0:
                await PrintReceiptAsync(saved.Id);
                break;
        }

        await CatchUpAsync();
    });

    private async Task PrintReceiptAsync(Guid saleId)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;

        var sale = await services.GetRequiredService<Application.Sales.ISaleService>().GetAsync(saleId);
        if (sale.IsFailure) return;

        if (sale.Value.Payments?.Any(p => string.Equals(p.TenderType, "Cash",
                StringComparison.OrdinalIgnoreCase)) == true)
            await services.GetRequiredService<Application.Sales.ICashDrawer>().OpenAsync();

        var receipt = await services.GetRequiredService<Application.Sales.IReceiptParticularsService>()
            .ForReceiptAsync(sale.Value);

        var printed = await services.GetRequiredService<Application.Sales.IReceiptPrinter>().PrintAsync(
            receipt, services.GetRequiredService<ReceiptHeaderFactory>().Build());

        // A printer fault never undoes the sale.
        if (!printed.Printed)
            View.ShowError("The sale is complete but the receipt did not print. "
                           + (printed.Problem ?? "") + Environment.NewLine + Environment.NewLine
                           + "Reprint it from the till with F12 once the printer is ready.");
    }

    /// The best active product for a name the operator said, preferring
    /// one with stock; null where nothing matches.
    private async Task<Application.Inventory.Dtos.ProductListItemDto?> ProductForAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        using var scope = _scopes.CreateScope();
        var found = await scope.ServiceProvider
            .GetRequiredService<Application.Inventory.IProductService>()
            .SearchAsync(name, take: 10, includeInactive: false);

        return found.IsFailure
            ? null
            : found.Value.Items.OrderByDescending(p => p.QuantityOnHand > 0m).FirstOrDefault();
    }

    private async Task<string?> ProductCodeForAsync(string? name) =>
        (await ProductForAsync(name))?.Code;

    /// One line for a new order, at the product's cost price.
    private async Task<Application.Suppliers.Dtos.PurchaseOrderLineDto?> OrderLineForAsync(
        string? name, decimal? quantity)
    {
        if (await ProductForAsync(name) is not { } product) return null;

        using var scope = _scopes.CreateScope();
        var detail = await scope.ServiceProvider
            .GetRequiredService<Application.Inventory.IProductService>()
            .GetAsync(product.Id);

        var cost = detail.IsSuccess ? detail.Value.CostPrice : 0m;
        var qty = quantity ?? 1m;

        return new Application.Suppliers.Dtos.PurchaseOrderLineDto(1, product.Id,
            product.Description, qty, 0m, qty, cost, cost * qty, product.Code);
    }

    private void Open<TForm>(string key, Action<TForm>? prepare = null,
                             Func<Task>? closed = null)
        where TForm : Form =>
        _views.Show(key, (Form)View, prepare, () => Enqueue(() => GuardAsync(async () =>
        {
            if (closed is not null) await closed();
            await CatchUpAsync();
        })));

    /// Whatever a screen changed - a patient added, a sale rung up, the
    /// till session opened - shows on the home screen.
    private async Task CatchUpAsync()
    {
        var session = await _sessions.CurrentAsync();
        View.SetSession(SessionStatus(session));
        await ShowCountsAsync();
    }

    private static string SessionStatus(TillSessionDto? session) =>
        session is null ? "No open session"
        : session.OpenedBefore(DateOnly.FromDateTime(DateTime.Today))
            ? $"Session open since {session.OpenedAtUtc.ToLocalTime():ddd dd/MM}"
            : "Session open";

    /// <summary>
    /// A till left open from an earlier day takes today's sales into that
    /// day's shift, so sign-in says so and offers its cash-up first.
    /// </summary>
    /// <remarks>
    /// Asked, not enforced: a night shift that runs past midnight is still
    /// one shift.
    /// </remarks>
    private async Task OfferEarlierCashUpAsync(TillSessionDto? session)
    {
        if (session is null || !session.OpenedBefore(DateOnly.FromDateTime(DateTime.Today))) return;

        var open = $"Till {session.TillNumber} has been open since "
                   + $"{session.OpenedAtUtc.ToLocalTime():ddd dd/MM HH:mm} and has not been "
                   + "cashed up, so anything sold now goes into that shift.";

        if (!ScreenAccess.CanOpen("cash-up", _permissions))
            View.ShowInfo(open + " Ask whoever cashes up to close it first.");
        else if (Confirmed(open + " Cash it up now?"))
            await OnMenuAsync("cash-up");
    }

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

    /// <summary>
    /// A copy of the database now - before a stocktake, an import, an
    /// upgrade done by hand. The nightly copy is the scheduled task's.
    /// </summary>
    /// Every price in a department, or all of them, by a percentage.
    private async Task MassPriceChangeAsync()
    {
        using var scope = _scopes.CreateScope();
        var reference = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();
        var products = scope.ServiceProvider.GetRequiredService<Application.Inventory.IProductService>();

        var departments = await reference.ListAsync(ReferenceKind.Department, includeInactive: false);
        var choices = new List<(string Text, Guid Value)> { ("Every department", Guid.Empty) };
        if (departments.IsSuccess)
            choices.AddRange(departments.Value.Select(d => (d.Name, d.Id)));

        if (Controls.frmChoicePrompt.Ask((Form)View, "Mass price change", "Whose prices", choices) is not Guid department)
            return;

        if (Controls.frmNumberPrompt.Ask((Form)View, "Mass price change",
                "Percentage to put the prices up by", 0m, decimals: 2) is not decimal percent
            || percent == 0m)
            return;

        var which = choices.First(c => c.Value == department).Text.ToLowerInvariant();
        if (!Confirmed($"Put every price in {which} up by {percent:0.##}%, rounded as System setup says?"))
            return;

        var result = await products.RepriceAsync(department == Guid.Empty ? null : department, percent);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        else View.ShowInfo($"{result.Value} prices changed. They are on the Price Changes report.");
    }

    private async void BackUpNow()
    {
        using var scope = _scopes.CreateScope();
        var backup = scope.ServiceProvider.GetRequiredService<IDatabaseBackup>();

        if (!backup.HoldsTheDatabase)
        {
            View.ShowInfo("The database is on the pharmacy's main computer. "
                          + "Back it up from there.");
            return;
        }

        View.SetBusy(true);

        try
        {
            var file = await backup.BackUpAsync("manual");

            View.ShowInfo($"Backed up to {file}." + Environment.NewLine
                + Environment.NewLine
                + "The copy is on this computer's own disk. Copy it to a USB "
                + "drive or a cloud folder as well: a dead disk takes both.");
        }
#pragma warning disable CA1031 // Shown to the operator; the log has the detail.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            View.ShowError("The backup did not finish: " + ex.Message);
        }
        finally
        {
            View.SetBusy(false);
        }
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
            + $"{_pharmacy.TradingName}" + Environment.NewLine
            + $"Branch {_options.Workstation.BranchCode}, "
            + $"till {_options.Workstation.TillNumber}" + Environment.NewLine
            + $"Signed in as {_user.Username}");

    private bool Confirmed(string question) => View.Confirm(question);

    /// <summary>
    /// Opens the report screen, on a named preset where the menu entry
    /// stands for one particular report.
    /// </summary>
    /// <remarks>One window per report, so two reports can sit side by side.</remarks>
    private void ShowReport(string? preset) =>
        Open<frmReports>("report:" + preset, reports => reports.OpenOnPreset = preset);

    private void ShowReferenceList(ReferenceKind kind) =>
        Open<frmReferenceList>("list:" + kind, list => list.Prepare(kind));

    // The pharmacy's details may have been changed at the console, and
    // the home screen's heading is not a window title, so it is re-read.
    private void ShowSystemInfo(SystemInfoMode mode) =>
        Open<frmSystemInfo>("system:" + mode, window => window.Mode = mode, () =>
        {
            ShowPharmacy();
            return Task.CompletedTask;
        });

    /// <summary>
    /// Heads the home screen with the pharmacy - its trading name, and
    /// its address and VAT number - for whichever user has signed in.
    /// </summary>
    /// <remarks>
    /// "Welcome" rather than the product's name while the pharmacy has
    /// not been named at the System Console; the logo beside it still
    /// says whose till this is.
    /// </remarks>
    private void ShowPharmacy()
    {
        var details = _pharmacy.Current;

        View.SetPharmacy(PharmacyHeading.Name(details, "Welcome"),
                         PharmacyHeading.Particulars(details));
    }

    /// <remarks>
    /// One stocktake window whichever step it was opened at: two windows
    /// on the same count would be capturing over each other.
    /// </remarks>
    private void ShowStocktakes(StocktakeStep step) =>
        Open<frmStocktakeList>("stocktakes", list => list.OpenAt = step);

    /// <summary>
    /// The dispensary quote off the menu. A script it became is offered
    /// straight to dispensing, to whoever may dispense it.
    /// </summary>
    private void ShowDispensaryQuote()
    {
        Guid? patientId, scriptId;

        using (var quote = _views.Create<Features.Dispensing.frmDispensaryQuote>())
        {
            if (quote.ShowDialog((Form)View) != DialogResult.OK) return;

            patientId = quote.CreatedForPatientId;
            scriptId = quote.CreatedScriptId;
        }

        if (patientId is not Guid patient || scriptId is not Guid script) return;
        if (!ScreenAccess.CanOpen("dispensing", _permissions)) return;

        if (!View.Confirm("The script has been captured. Dispense it now?"))
            return;

        using var dispense = _views.Create<frmDispense>();
        dispense.BeginOn(patient, script);
        dispense.ShowDialog((Form)View);
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

    private async Task ReprintChosenScriptAsync()
    {
        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog((Form)View) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid patientId) return;

        using var scripts = _views.Create<frmScriptList>();
        scripts.LoadForPatient(patientId, asPicker: true);

        if (scripts.ShowDialog((Form)View) != DialogResult.OK) return;
        if (scripts.SelectedScriptId is not Guid scriptId) return;

        using var scope = _scopes.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<Application.Dispensing.IScriptService>();

        var printout = await service.PrintoutAsync(scriptId);

        if (printout.IsFailure)
        {
            View.ShowError(printout.Error.Message);
            return;
        }

        var printer = scope.ServiceProvider
            .GetRequiredService<Application.Sales.IDocumentPrinter>();

        var headers = scope.ServiceProvider
            .GetRequiredService<Features.Sales.ReceiptHeaderFactory>();

        var printed = await printer.PrintAsync(printout.Value,
            headers.Build() with { IsCopy = true });

        var what = $"The copy of {printout.Value.Title.ToLowerInvariant()}";

        if (printed.Printed) View.ShowInfo(printed.Describe(what));
        else View.ShowError(printed.Describe(what));
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

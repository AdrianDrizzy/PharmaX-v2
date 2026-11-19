using Microsoft.Extensions.DependencyInjection;
using PharmaX.Desktop.Features.Admin;
using PharmaX.Desktop.Features.Dispensing;
using PharmaX.Desktop.Features.Finance;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Login;
using PharmaX.Desktop.Features.Patients;
using PharmaX.Desktop.Features.Reference;
using PharmaX.Desktop.Features.Reports;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Features.Suppliers;
using PharmaX.Desktop.Features.Tills;
using PharmaX.Desktop.Mvp;
using PharmaX.Desktop.Shell;

namespace PharmaX.Desktop;

/// <summary>
/// Every form and presenter is registered transient, because each one is
/// created fresh and disposed with its window. Add one pair of lines per
/// screen as you build it: this file is the only place in the
/// presentation layer that knows the full list of screens.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPharmaXDesktop(this IServiceCollection s)
    {
        ArgumentNullException.ThrowIfNull(s);

        s.AddSingleton<ViewFactory>();
        s.AddSingleton<ErrorHandler>();

        // Membership codes are locked to the main computer's Pharmacy ID.
        s.AddSingleton(sp => Setup.ThisComputer.Licence(
            sp.GetRequiredService<PharmaX.Application.Abstractions.PharmaXOptions>()));

        // One logo for the whole application, loaded once at start-up.
        s.AddSingleton<Branding>();

        // Registered after AddPharmaXReporting's default, so the
        // exporters resolve the real logo rather than no logo.
        s.AddSingleton<PharmaX.Application.Reporting.IReportBranding,
            Features.Reports.ReportBranding>();
        s.AddScoped<ReceiptHeaderFactory>();

        // Shell
        s.AddTransient<frmMain>();
        s.AddTransient<MainPresenter>();

        // Login
        s.AddTransient<frmLogin>();
        s.AddTransient<LoginPresenter>();

        // Sales
        s.AddTransient<frmPOS>();
        s.AddTransient<PosPresenter>();
        s.AddTransient<frmCollect>();
        s.AddTransient<CollectPresenter>();
        // Accounts
        s.AddTransient<Features.Finance.frmTrialBalance>();
        s.AddTransient<Features.Finance.TrialBalancePresenter>();
        s.AddTransient<Features.Finance.frmJournal>();
        s.AddTransient<Features.Finance.JournalPresenter>();
        s.AddTransient<Features.Finance.frmClaims>();
        s.AddTransient<Features.Finance.ClaimsPresenter>();
        s.AddTransient<Features.Finance.frmChartOfAccounts>();
        s.AddTransient<Features.Finance.ChartPresenter>();

        s.AddTransient<Features.Inventory.frmAlerts>();
        s.AddTransient<Features.Inventory.AlertPresenter>();
        s.AddTransient<Features.Patients.frmAccountPicker>();
        s.AddTransient<Features.Dispensing.frmScriptPicker>();
        s.AddTransient<Features.Patients.frmAccounts>();
        s.AddTransient<Features.Patients.AccountsPresenter>();
        s.AddTransient<frmQuotation>();
        s.AddTransient<frmReturn>();
        s.AddTransient<ReturnPresenter>();

        // Dispensing
        s.AddTransient<frmDispense>();
        s.AddTransient<DispensePresenter>();
        s.AddTransient<frmScriptList>();
        s.AddTransient<frmDispensaryQuote>();
        s.AddTransient<ScriptListPresenter>();
        s.AddTransient<frmLabels>();
        s.AddTransient<frmScriptEdit>();
        s.AddTransient<ScriptEditPresenter>();

        // Inventory
        s.AddTransient<frmProductList>();
        s.AddTransient<ProductListPresenter>();
        s.AddTransient<frmProductEdit>();
        s.AddTransient<ProductEditPresenter>();
        s.AddTransient<frmStocktakeList>();
        s.AddTransient<StocktakeListPresenter>();
        s.AddTransient<frmStocktakeCapture>();
        s.AddTransient<frmStockAdjustment>();
        s.AddTransient<frmPrepack>();
        s.AddTransient<StocktakeCapturePresenter>();

        // Tills
        s.AddTransient<frmTillSession>();
        s.AddTransient<TillSessionPresenter>();
        s.AddTransient<frmCashUp>();
        s.AddTransient<CashUpPresenter>();
        s.AddTransient<frmCashUpHistory>();
        s.AddTransient<CashUpHistoryPresenter>();
        s.AddTransient<frmEndOfDay>();
        s.AddTransient<EndOfDayPresenter>();
        s.AddTransient<frmPayouts>();
        s.AddTransient<PayoutsPresenter>();

        // Reports
        s.AddTransient<frmReports>();
        s.AddTransient<ReportsPresenter>();

        // The day's receipts (everyone) and what a receipt prints (admin)
        s.AddTransient<frmReceipts>();
        s.AddTransient<frmReceiptTemplate>();

        // Ask PharmaX, open from every screen (PhxFormBase.AssistantKey)
        s.AddTransient<Features.Assistant.frmAssistant>();
        s.AddTransient<Features.Assistant.AssistantPresenter>();
        s.AddTransient<Features.Assistant.AssistantReports>();
        s.AddSingleton<Features.Assistant.AssistantTasks>();

        // Patients
        s.AddTransient<frmPatientList>();
        s.AddTransient<Features.Patients.frmPatientAttachments>();
        s.AddTransient<PatientListPresenter>();
        s.AddTransient<frmPatientMerge>();
        s.AddTransient<frmPatientEdit>();
        s.AddTransient<PatientEditPresenter>();

        // Suppliers, purchasing and receiving
        s.AddTransient<frmSupplierList>();
        s.AddTransient<SupplierListPresenter>();
        s.AddTransient<frmSupplierEdit>();
        s.AddTransient<SupplierEditPresenter>();
        s.AddTransient<frmGoodsReceipt>();
        s.AddTransient<GoodsReceiptPresenter>();
        s.AddTransient<frmPurchaseOrderList>();
        s.AddTransient<frmPurchaseOrderEdit>();
        s.AddTransient<frmDraftOrders>();
        s.AddTransient<PurchaseOrderListPresenter>();

        // Administration
        s.AddTransient<frmUserList>();
        s.AddTransient<UserListPresenter>();
        s.AddTransient<frmRolePermissions>();
        s.AddTransient<RolePermissionPresenter>();
        s.AddTransient<frmUserEdit>();
        s.AddTransient<UserEditPresenter>();
        s.AddTransient<frmPrinterSetup>();
        s.AddTransient<frmPrintSizes>();
        s.AddTransient<frmSystemSetup>();
        s.AddTransient<frmMembership>();
        s.AddTransient<frmDataImport>();
        s.AddTransient<Features.Dispensing.frmToFollow>();
        s.AddTransient<frmDayEnd>();
        s.AddTransient<frmSystemInfo>();
        s.AddTransient<SystemInfoPresenter>();

        // Maintained lists: departments, manufacturers, branches and
        // medical aid societies all use the one pair of screens.
        s.AddTransient<frmReferenceList>();
        s.AddTransient<ReferenceListPresenter>();
        s.AddTransient<frmReferenceEdit>();
        s.AddTransient<ReferenceEditPresenter>();
        s.AddTransient<frmTariffList>();
        s.AddTransient<TariffListPresenter>();
        s.AddTransient<frmTariffEdit>();
        s.AddTransient<TariffEditPresenter>();

        return s;
    }
}

using Microsoft.Extensions.DependencyInjection;
using PharmaX.Desktop.Features.Admin;
using PharmaX.Desktop.Features.Dispensing;
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

        // One logo for the whole application, loaded once at start-up.
        s.AddSingleton<Branding>();
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
        s.AddTransient<frmReturn>();
        s.AddTransient<ReturnPresenter>();

        // Dispensing
        s.AddTransient<frmDispense>();
        s.AddTransient<DispensePresenter>();
        s.AddTransient<frmScriptList>();
        s.AddTransient<ScriptListPresenter>();
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
        s.AddTransient<StocktakeCapturePresenter>();

        // Tills
        s.AddTransient<frmTillSession>();
        s.AddTransient<TillSessionPresenter>();

        // Reports
        s.AddTransient<frmReports>();
        s.AddTransient<ReportsPresenter>();

        // Patients
        s.AddTransient<frmPatientList>();
        s.AddTransient<PatientListPresenter>();
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
        s.AddTransient<PurchaseOrderListPresenter>();

        // Administration
        s.AddTransient<frmUserList>();
        s.AddTransient<UserListPresenter>();
        s.AddTransient<frmUserEdit>();
        s.AddTransient<UserEditPresenter>();
        s.AddTransient<frmPrinterSetup>();
        s.AddTransient<frmSystemSetup>();
        s.AddTransient<frmSystemInfo>();
        s.AddTransient<SystemInfoPresenter>();

        // Maintained lists: departments, manufacturers, branches and
        // medical aid societies all use the one pair of screens.
        s.AddTransient<frmReferenceList>();
        s.AddTransient<ReferenceListPresenter>();
        s.AddTransient<frmReferenceEdit>();
        s.AddTransient<ReferenceEditPresenter>();

        return s;
    }
}

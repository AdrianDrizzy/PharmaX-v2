using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Application.Patients;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Tills;

namespace PharmaX.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPharmaXApplication(this IServiceCollection s)
    {
        ArgumentNullException.ThrowIfNull(s);

        s.AddScoped<IAuthenticationService, AuthenticationService>();
        s.AddScoped<IPermissionService, PermissionService>();
        s.AddScoped<IUserService, UserService>();
        s.AddScoped<ISaleService, SaleService>();
        s.AddScoped<IReturnService, ReturnService>();
        s.AddScoped<IDispensingService, DispensingService>();
        s.AddScoped<IScriptService, ScriptService>();
        s.AddScoped<IProductService, ProductService>();
        s.AddScoped<Finance.IDayEndPostingService,
                    Finance.DayEndPostingService>();
        s.AddScoped<Sales.IQuotationService, Sales.QuotationService>();
        s.AddScoped<Dispensing.ILabelReprintService,
                    Dispensing.LabelReprintService>();
        s.AddScoped<Patients.IPatientMergeService,
                    Patients.PatientMergeService>();
        s.AddScoped<Reference.IReferenceDataService,
                    Reference.ReferenceDataService>();
        s.AddScoped<Reference.ITariffService, Reference.TariffService>();
        s.AddScoped<IPatientService, PatientService>();
        s.AddScoped<IStockService, StockService>();
        s.AddScoped<IStocktakeService, StocktakeService>();
        s.AddScoped<ITillSessionService, TillSessionService>();
        s.AddScoped<ICashUpService, CashUpService>();
        s.AddScoped<ISupplierService, SupplierService>();
        s.AddScoped<IReceivingService, ReceivingService>();
        s.AddScoped<IPurchasingService, PurchasingService>();
        return s;
    }
}

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

        // One set of pharmacy particulars for the whole process, so an
        // edit at the System Console reaches every receipt and window.
        s.AddSingleton<Abstractions.PharmacyProfile>();
        s.AddScoped<Pharmacy.IPharmacyDetailsService,
                    Pharmacy.PharmacyDetailsService>();

        s.AddScoped<IAuthenticationService, AuthenticationService>();
        s.AddScoped<IPermissionService, PermissionService>();
        s.AddScoped<IUserService, UserService>();
        s.AddScoped<ISaleService, SaleService>();
        s.AddScoped<ICollectionService, CollectionService>();
        s.AddScoped<IReceiptParticularsService, ReceiptParticularsService>();
        s.AddScoped<IReceiptHistoryService, ReceiptHistoryService>();
        s.AddScoped<Alerts.IStockAlertService, Alerts.StockAlertService>();
        s.AddScoped<Alerts.IStockAlertAssistant, Alerts.StockAlertAssistant>();
        s.AddScoped<Finance.ILedgerService, Finance.LedgerService>();
        s.AddScoped<Finance.IJournalPoster, Finance.JournalPoster>();
        s.AddScoped<Claims.IClaimService, Claims.ClaimService>();
        s.AddScoped<Claims.IClaimFormService, Claims.ClaimFormService>();
        s.AddScoped<Patients.IAccountService, Patients.AccountService>();
        s.AddScoped<IReturnService, ReturnService>();
        s.AddScoped<IDispensingService, DispensingService>();
        s.AddScoped<IScriptService, ScriptService>();
        s.AddScoped<IProductService, ProductService>();
        s.AddScoped<Finance.IDayEndPostingService,
                    Finance.DayEndPostingService>();
        s.AddScoped<Sales.IQuotationService, Sales.QuotationService>();
        s.AddScoped<Dispensing.ILabelReprintService,
                    Dispensing.LabelReprintService>();
        s.AddScoped<Dispensing.PrepackLabelHistory>();
        s.AddScoped<Dispensing.TemplateLabelHistory>();
        s.AddScoped<Patients.IPatientMergeService,
                    Patients.PatientMergeService>();
        s.AddScoped<Reference.IReferenceDataService,
                    Reference.ReferenceDataService>();
        s.AddScoped<Reference.ITariffService, Reference.TariffService>();
        s.AddScoped<IPatientService, PatientService>();
        s.AddScoped<Patients.IAttachmentService, Patients.AttachmentService>();
        s.AddScoped<IStockService, StockService>();
        s.AddScoped<IStockAdjustmentService, StockAdjustmentService>();
        s.AddScoped<IStocktakeService, StocktakeService>();
        s.AddScoped<ITillSessionService, TillSessionService>();
        s.AddScoped<ICashUpService, CashUpService>();
        s.AddScoped<IShiftCashUpService, ShiftCashUpService>();
        s.AddScoped<ITillPayoutService, TillPayoutService>();
        s.AddScoped<ISupplierService, SupplierService>();
        s.AddScoped<IReceivingService, ReceivingService>();
        s.AddScoped<IPurchasingService, PurchasingService>();
        s.AddScoped<Assistant.Guided.IGuidedFlows, Assistant.Guided.GuidedFlows>();
        return s;
    }
}

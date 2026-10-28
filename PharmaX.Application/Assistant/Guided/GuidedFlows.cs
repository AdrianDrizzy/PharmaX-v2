using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Inventory;
using PharmaX.Application.Patients;
using PharmaX.Application.Reference;
using PharmaX.Application.Reporting;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Tills;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// The guided conversation for a job asked of Ask PharmaX, where there is
/// one and the operator may do that job.
/// </summary>
public interface IGuidedFlows
{
    /// Null where the job has no guided flow, or the operator may not do
    /// it; the screen then opens filled in as before.
    /// <param name="prompts">The screen's sign-in prompt, for anything
    /// that needs a second person's authority.</param>
    GuidedFlow? FlowFor(AppTask task, IGuidedPrompts? prompts = null);
}

/// <remarks>
/// Resolves each flow's services from the window's own scope when the job
/// is asked, so a product question does not build the dispensary.
/// </remarks>
public sealed class GuidedFlows : IGuidedFlows
{
    private readonly IServiceProvider _services;
    private readonly IPermissionService _permissions;

    public GuidedFlows(IServiceProvider services, IPermissionService permissions)
    {
        _services = services;
        _permissions = permissions;
    }

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public GuidedFlow? FlowFor(AppTask task, IGuidedPrompts? prompts = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        prompts ??= NoPrompts.Instance;

        return task.Kind switch
        {
            AppTaskKind.NewProduct when _permissions.Has(PermissionCode.ProductMaintain) =>
                new NewProductFlow(task.Item, Get<IProductService>(), Get<IReferenceDataService>(),
                    Get<IPharmacySettings>(), _permissions, Get<IClock>()),

            AppTaskKind.NewPatient when _permissions.Has(PermissionCode.PatientMaintain) =>
                new NewPatientFlow(task.Person, Get<IPatientService>(), Get<IReferenceDataService>(), Get<IClock>()),

            AppTaskKind.PurchaseOrder when _permissions.Has(PermissionCode.PurchaseOrderRaise) =>
                new PurchaseOrderFlow(task.Item, task.Quantity, task.Person, Get<IProductService>(),
                    Get<IPurchasingService>(), Get<ISupplierService>(), Get<IClock>(), Get<ICurrentUser>(),
                    _services.GetService<IPdfWriter>(), _services.GetService<IWorkbookWriter>()),

            AppTaskKind.ReceiveStock when _permissions.Has(PermissionCode.GoodsReceive) =>
                new ReceiveStockFlow(task.Person, Get<IReceivingService>(), Get<IPurchasingService>(),
                    Get<ISupplierService>(), Get<IProductService>(), Get<IPharmacySettings>(), _permissions,
                    Get<IClock>()),

            AppTaskKind.Sell when _permissions.Has(PermissionCode.SaleCreate) =>
                new SellFlow(task.Item, task.Quantity, task.Person, Get<ISaleService>(),
                    Get<ITillSessionService>(), Get<IProductService>(), Get<IAccountService>(), Get<ICurrentUser>(),
                    _permissions, prompts),

            AppTaskKind.Dispense when _permissions.Has(PermissionCode.ScriptCapture)
                                      && _permissions.Has(PermissionCode.ScriptDispense) =>
                new DispenseFlow(task.Item, task.Quantity, task.Person, Get<IPatientService>(),
                    Get<IScriptService>(), Get<IDispensingService>(), Get<IProductService>(),
                    Get<IReferenceDataService>(), _permissions, Get<IClock>(), prompts,
                    () => FlowFor(new AppTask(AppTaskKind.NewPatient, Person: task.Person), prompts),
                    _services.GetService<ILabelPrinter>(), _services.GetService<IPharmacySettings>()),

            _ => null
        };
    }
}

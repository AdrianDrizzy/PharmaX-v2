using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public interface ISupplierListView : IView
{
    Guid? SelectedId { get; }
    bool IncludeInactive { get; }

    void ShowSuppliers(IReadOnlyList<SupplierListItemDto> suppliers);

    /// True where the screen was opened to pick a supplier for the
    /// receiving screen rather than to maintain the list.
    bool IsPickerMode { get; }

    event EventHandler? RefreshRequested;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? DeactivateRequested;
    event EventHandler? ChooseRequested;
}

public interface ISupplierEditView : IView
{
    Guid? SupplierId { get; }

    void ShowSupplier(SupplierDto supplier);
    SupplierEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

public sealed class SupplierListPresenter : PresenterBase<ISupplierListView>
{
    private readonly ISupplierService _suppliers;
    private readonly ViewFactory _views;

    public SupplierListPresenter(ISupplierService suppliers,
                                 ViewFactory views)
    {
        _suppliers = suppliers;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);
        View.DeactivateRequested += (_, _) => _ = DeactivateAsync();

        View.ChooseRequested += (_, _) =>
        {
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a supplier first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _suppliers.ListAsync(View.IncludeInactive);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowSuppliers(result.Value);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmSupplierEdit>();
        editor.LoadSupplier(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task DeactivateAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a supplier first.");
            return;
        }

        if (!View.Confirm(
                "Deactivate this supplier? Their goods receipts and the "
                + "batches taken in from them are kept; they simply stop "
                + "appearing when receiving stock."))
            return;

        var result = await _suppliers.DeactivateAsync(id);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public sealed class SupplierEditPresenter : PresenterBase<ISupplierEditView>
{
    private readonly ISupplierService _suppliers;

    public SupplierEditPresenter(ISupplierService suppliers) =>
        _suppliers = suppliers;

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        if (View.SupplierId is not Guid id) return;   // adding a new one

        var result = await _suppliers.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowSupplier(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        var result = View.SupplierId is Guid id
            ? await _suppliers.UpdateAsync(id, edit)
            : (await _suppliers.CreateAsync(edit))
                as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public interface ISupplierListView : IView
{
    Guid? SelectedId { get; }

    /// Whether the selected supplier is active, so the one button can
    /// deactivate it or bring it back.
    bool SelectedIsActive { get; }

    bool IncludeInactive { get; }

    /// The Sort and date range under the search box.
    Application.Abstractions.ListFilter Filter { get; }

    void ShowSuppliers(IReadOnlyList<SupplierListItemDto> suppliers);

    /// True where the screen was opened to pick a supplier for the
    /// receiving screen rather than to maintain the list.
    bool IsPickerMode { get; }

    event EventHandler? RefreshRequested;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? ToggleActiveRequested;
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
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.AddRequested += (_, _) => Enqueue(() => OpenEditorAsync(null));
        View.EditRequested += (_, _) => Enqueue(() => OpenEditorAsync(View.SelectedId));
        View.ToggleActiveRequested += (_, _) => Enqueue(ToggleActiveAsync);

        View.ChooseRequested += (_, _) =>
        {
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a supplier first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _suppliers.ListAsync(View.IncludeInactive, View.Filter);

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

    private Task ToggleActiveAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a supplier first.");
            return;
        }

        var reactivating = !View.SelectedIsActive;

        if (!View.Confirm(reactivating
                ? "Reactivate this supplier? They will be offered again "
                  + "when ordering and receiving stock."
                : "Deactivate this supplier? Their goods receipts and the "
                  + "batches taken in from them are kept; they simply stop "
                  + "appearing when receiving stock."))
            return;

        var result = reactivating
            ? await _suppliers.ReactivateAsync(id)
            : await _suppliers.DeactivateAsync(id);

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
        View.SaveRequested += (_, _) => Enqueue(SaveAsync);
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

using PharmaX.Application.Inventory;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

public sealed class TariffListPresenter : PresenterBase<ITariffListView>
{
    private readonly ITariffService _tariffs;
    private readonly IReferenceDataService _reference;
    private readonly ViewFactory _views;

    public TariffListPresenter(ITariffService tariffs,
        IReferenceDataService reference, ViewFactory views)
    {
        _tariffs = tariffs;
        _reference = reference;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.SearchChanged += (_, _) => _ = LoadAsync();
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);
        View.ToggleActiveRequested += (_, _) => _ = ToggleAsync();
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var schemes = await _reference.ListAsync(
            ReferenceKind.MedicalAidScheme, includeInactive: false);

        if (schemes.IsSuccess) View.ShowSchemes(schemes.Value);

        await LoadAsync();
    });

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _tariffs.ListAsync(View.SchemeFilter,
                                              View.SearchText);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowTariffs(result.Value);
        View.ShowCount(result.Value.Count);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmTariffEdit>();
        editor.Prepare(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task ToggleAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id) return;

        var active = View.SelectedIsActive;

        var question = active
            ? "Retire this tariff? It stays on past claims but will not "
              + "price anything from now on."
            : "Bring this tariff back into use?";

        if (!View.Confirm(question)) return;

        var result = await _tariffs.SetActiveAsync(id, !active);
        if (result.IsFailure) View.ShowError(result.Error.Message);

        await LoadAsync();
    });
}

public sealed class TariffEditPresenter : PresenterBase<ITariffEditView>
{
    private readonly ITariffService _tariffs;
    private readonly IReferenceDataService _reference;
    private readonly IProductService _products;

    public TariffEditPresenter(ITariffService tariffs,
        IReferenceDataService reference, IProductService products)
    {
        _tariffs = tariffs;
        _reference = reference;
        _products = products;
    }

    protected override void Subscribe()
    {
        View.ChooseProductRequested += (_, _) => _ = ChooseProductAsync();
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var schemes = await _reference.ListAsync(
            ReferenceKind.MedicalAidScheme, includeInactive: false);

        if (schemes.IsFailure)
        {
            View.ShowError(schemes.Error.Message);
            View.CloseView(false);
            return;
        }

        if (schemes.Value.Count == 0)
        {
            // A tariff with no society to price for cannot be written,
            // and saying so here beats an empty dropdown.
            View.ShowError("Add a medical aid society first: a tariff is "
                + "what one society pays, so there is nothing to price "
                + "against yet.");

            View.CloseView(false);
            return;
        }

        View.ShowSchemes(schemes.Value);

        if (View.TariffId is not Guid id) return;      // adding a new one

        var existing = await _tariffs.ListAsync();

        if (existing.IsFailure)
        {
            View.ShowError(existing.Error.Message);
            View.CloseView(false);
            return;
        }

        var tariff = existing.Value.FirstOrDefault(t => t.Id == id);

        if (tariff is null)
        {
            View.ShowError("That tariff no longer exists.");
            View.CloseView(false);
            return;
        }

        View.ShowTariff(tariff);
        View.LockSubject();
    });

    /// <summary>
    /// Picks the product through the product list in picker mode, the
    /// same way the till finds an item by name.
    /// </summary>
    private Task ChooseProductAsync() => GuardAsync(async () =>
    {
        var code = View is ITariffProductPicker picker
            ? picker.PromptProduct()
            : null;

        if (string.IsNullOrWhiteSpace(code)) return;

        var found = await _products.SearchAsync(code, take: 5);

        if (found.IsFailure)
        {
            View.ShowError(found.Error.Message);
            return;
        }

        var product = found.Value.Items.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            View.ShowError($"No product for code {code}.");
            return;
        }

        View.ShowProduct(product.Id, product.Code, product.Description);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        if (View.Collect() is not TariffEditDto edit) return;

        var result = View.TariffId is Guid id
            ? await _tariffs.UpdateAsync(id, edit)
            : (await _tariffs.CreateAsync(edit))
                as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

/// <summary>
/// The one thing the tariff editor needs a window for: choosing a
/// product. Kept off ITariffEditView so the presenter's other tests do
/// not have to answer it.
/// </summary>
public interface ITariffProductPicker
{
    string? PromptProduct();
}

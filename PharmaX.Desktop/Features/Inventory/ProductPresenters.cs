using PharmaX.Application.Inventory;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public sealed class ProductListPresenter : PresenterBase<IProductListView>
{
    private readonly IProductService _products;
    private readonly ViewFactory _views;

    public ProductListPresenter(IProductService products, ViewFactory views)
    {
        _products = products;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.SearchChanged += (_, _) => _ = LoadAsync();
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);

        View.ChooseRequested += (_, _) =>
        {
            // In picker mode the caller reads the selection off the form,
            // so all this has to do is close with a success result.
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a product first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _products.SearchAsync(View.SearchText, take: 200);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowProducts(result.Value.Items);
        View.ShowCount(result.Value.Items.Count, result.Value.TotalCount);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmProductEdit>();
        editor.LoadProduct(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });
}

public sealed class ProductEditPresenter : PresenterBase<IProductEditView>
{
    private readonly IProductService _products;

    public ProductEditPresenter(IProductService products) =>
        _products = products;

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        if (View.ProductId is not Guid id) return;   // adding a new one

        var result = await _products.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowProduct(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        var result = View.ProductId is Guid id
            ? await _products.UpdateAsync(id, edit)
            : (await _products.CreateAsync(edit)) as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

using PharmaX.Application.Inventory;
using PharmaX.Desktop.Mvp;
using PharmaX.Application.Reference;

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
        View.ViewModeChanged += (_, _) => _ = LoadAsync();
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

    private Task LoadAsync() =>
        View.BatchView ? LoadBatchesAsync() : LoadProductsAsync();

    private Task LoadProductsAsync() => GuardAsync(async () =>
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

    /// <summary>
    /// The same search, a batch at a time.
    /// </summary>
    /// <remarks>
    /// The count still reports products rather than rows. "612 of 4,318"
    /// where 4,318 is a product count and 612 is a batch count would be
    /// two different units either side of the word "of".
    /// </remarks>
    private Task LoadBatchesAsync() => GuardAsync(async () =>
    {
        var result = await _products.SearchBatchesAsync(View.SearchText,
                                                        take: 200);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowBatches(result.Value.Items);

        View.ShowCount(
            result.Value.Items.Select(r => r.ProductId).Distinct().Count(),
            result.Value.TotalCount);
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
    private readonly IReferenceDataService _reference;

    public ProductEditPresenter(IProductService products,
                                IReferenceDataService reference)
    {
        _products = products;
        _reference = reference;
    }

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        // Loaded first and for a new product as well as an existing one,
        // so the lists are there to choose from either way - and so that
        // ShowProduct below can select what the product already carries.
        await LoadFilingOptionsAsync();

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

    /// <remarks>
    /// Active entries only. A retired department is kept so that history
    /// still reads, but offering it here would let somebody file today's
    /// stock into a list the pharmacy has stopped using.
    /// </remarks>
    private async Task LoadFilingOptionsAsync()
    {
        async Task<IReadOnlyList<ReferenceItemDto>> ListAsync(
            ReferenceKind kind)
        {
            var result = await _reference.ListAsync(kind,
                includeInactive: false);

            return result.IsSuccess
                ? result.Value
                : Array.Empty<ReferenceItemDto>();
        }

        View.ShowFilingOptions(
            await ListAsync(ReferenceKind.Department),
            await ListAsync(ReferenceKind.Category),
            await ListAsync(ReferenceKind.Manufacturer));
    }

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

using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
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
        // A new search, or the other view, starts again at the top.
        View.SearchChanged += (_, _) => Enqueue(FirstPageAsync);
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.ViewModeChanged += (_, _) => Enqueue(FirstPageAsync);
        View.PageRequested += (_, move) => Enqueue(() => TurnAsync(move));
        View.AddRequested += (_, _) => Enqueue(() => OpenEditorAsync(null));
        View.EditRequested += (_, _) =>
        {
            // Nothing selected is not an invitation to add a product.
            if (View.SelectedId is not Guid id)
            {
                View.ShowInfo("Choose a product first.");
                return;
            }

            var batch = View.SelectedBatchId;
            Enqueue(() => OpenEditorAsync(id, batch));
        };
        View.DeactivateRequested += (_, _) => Enqueue(DeactivateAsync);
        View.CellEdited += (_, _) =>
        {
            if (View.PendingEdit is not { } edit) return;

            Enqueue(() => SaveCellAsync(edit));
        };

        View.ChooseRequested += (_, _) =>
        {
            // In picker mode the caller reads the selection off the form,
            // so all this has to do is close with a success result.
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a product first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    /// Leaves the product on past sales and takes it off this list.
    private Task DeactivateAsync() => GuardAsync(async () =>
    {
        var picked = View.SelectedProducts;
        if (picked.Count == 0)
        {
            View.ShowInfo("Choose a product first.");
            return;
        }

        if (!View.Confirm(picked.Count == 1
                ? "Deactivate this product? It stays on past sales and "
                  + "dispensings, and leaves this list."
                : $"Deactivate {picked.Count} products? They stay on past "
                  + "sales and dispensings, and leave this list."))
            return;

        // Each one is still the service's to allow or refuse.
        var failed = new List<string>();
        foreach (var (id, code) in picked)
        {
            var result = await _products.DeactivateAsync(id);
            if (result.IsFailure)
                failed.Add(picked.Count == 1
                    ? result.Error.Message
                    : $"{code}: {result.Error.Message}");
        }

        if (failed.Count > 0) View.ShowError(string.Join("\n", failed));

        await LoadAsync();
    });

    /// <summary>
    /// Every product the search matches, in the view on screen, for an
    /// export: the grid holds only the page.
    /// </summary>
    internal void ExportAll(Action<IReadOnlyList<object>> export) =>
        Enqueue(() => GuardAsync(async () =>
        {
            if (View.BatchView)
            {
                var batches = await _products.SearchBatchesAsync(View.SearchText, take: int.MaxValue);
                if (batches.IsFailure) View.ShowError(batches.Error.Message);
                else export(batches.Value.Items.ToList<object>());
                return;
            }

            var products = await _products.SearchAsync(View.SearchText, take: int.MaxValue,
                includeInactive: View.IncludeInactive);
            if (products.IsFailure) View.ShowError(products.Error.Message);
            else export(products.Value.Items.ToList<object>());
        }));

    /// Products to a page. Enough to fill a tall screen, few enough that
    /// a page of a 50,000-line catalogue comes back at once.
    internal const int PageSize = 100;

    /// The page on screen, from nought.
    private int _page;

    /// How many products the search matched, as of the last page read.
    private int _total;

    private Task FirstPageAsync()
    {
        _page = 0;
        return LoadAsync();
    }

    private Task TurnAsync(Controls.PageMove move)
    {
        var page = Controls.PagerBar.Target(_page, _total, PageSize, move);
        if (page == _page) return Task.CompletedTask;

        _page = page;
        return LoadAsync();
    }

    private static int LastPage(int total) =>
        Math.Max(0, (total - 1) / PageSize);

    private async Task LoadAsync()
    {
        await (View.BatchView ? LoadBatchesAsync() : LoadProductsAsync());

        // Products discontinued or recoded off the end of the last page
        // leave it empty; the page before it is the one to show.
        if (_page > LastPage(_total))
        {
            _page = LastPage(_total);
            await (View.BatchView ? LoadBatchesAsync() : LoadProductsAsync());
        }
    }

    private Task LoadProductsAsync() => GuardAsync(async () =>
    {
        var result = await _products.SearchAsync(View.SearchText,
            take: PageSize, skip: _page * PageSize,
            includeInactive: View.IncludeInactive);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _total = result.Value.TotalCount;
        View.ShowProducts(result.Value.Items);
        View.ShowPage(_page, PageSize, result.Value.Items.Count, _total);
    });

    /// <summary>
    /// The same search, a batch at a time.
    /// </summary>
    /// <remarks>
    /// The page and the count are still in products rather than rows.
    /// "612 of 4,318" where 4,318 is a product count and 612 is a batch
    /// count would be two different units either side of the word "of".
    /// </remarks>
    private Task LoadBatchesAsync() => GuardAsync(async () =>
    {
        var result = await _products.SearchBatchesAsync(View.SearchText,
            take: PageSize, skip: _page * PageSize);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _total = result.Value.TotalCount;
        View.ShowBatches(result.Value.Items);

        View.ShowPage(_page, PageSize,
            result.Value.Items.Select(r => r.ProductId).Distinct().Count(),
            _total);
    });

    private Task OpenEditorAsync(Guid? id, Guid? batchId = null) =>
        GuardAsync(async () =>
    {
        using var editor = _views.Create<frmProductEdit>();
        editor.LoadProduct(id, batchId);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    /// <summary>
    /// Writes the cell, then reloads either way so a refused value does
    /// not stay on the screen as if it had been kept.
    /// </summary>
    private Task SaveCellAsync(ProductCellEditDto edit) => GuardAsync(async () =>
    {
        try
        {
            var result = await _products.ApplyCellEditAsync(edit);

            if (result.IsFailure)
                View.ShowError(result.Error.Message);
        }
        finally
        {
            await LoadAsync();
        }
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
        View.SaveRequested += (_, _) => Enqueue(SaveAsync);
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

        _loaded = result.Value;
        View.ShowProduct(result.Value);
    });

    /// The product as it was opened. Null for a new one.
    private ProductDto? _loaded;

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

        // The same question the list asks when Active is changed there.
        if (_loaded is { IsActive: true } && edit.IsActive == false
            && !View.Confirm(
                "Discontinue this product? It stays on past sales."))
            return;

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

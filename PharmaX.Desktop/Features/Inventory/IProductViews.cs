using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public interface IProductListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }

    /// <summary>
    /// Whether the operator has asked to see the shelf a batch at a time
    /// rather than a product at a time.
    /// </summary>
    bool BatchView { get; }

    void ShowProducts(IReadOnlyList<ProductListItemDto> products);
    void ShowBatches(IReadOnlyList<ProductBatchListItemDto> rows);
    void ShowCount(int shown, int total);

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? RefreshRequested;
    event EventHandler? ChooseRequested;
    event EventHandler? ViewModeChanged;
}

public interface IProductEditView : IView
{
    /// Null for a new product. Set by the list screen before the dialog
    /// is shown, and read by the presenter in InitialiseAsync.
    Guid? ProductId { get; }

    /// <summary>
    /// The lists the product is filed against. Supplied before
    /// ShowProduct, so the editor can select what the product already
    /// carries rather than clearing it.
    /// </summary>
    void ShowFilingOptions(IReadOnlyList<ReferenceItemDto> departments,
                           IReadOnlyList<ReferenceItemDto> categories,
                           IReadOnlyList<ReferenceItemDto> manufacturers);

    void ShowProduct(ProductDto product);
    ProductEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

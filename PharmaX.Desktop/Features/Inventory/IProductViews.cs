using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public interface IProductListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }

    /// The "Show deactivated" box: deactivated products hidden until ticked.
    bool IncludeInactive { get; }

    /// The Sort and date range under the search box.
    Application.Abstractions.ListFilter Filter { get; }

    /// The batch of the selected row in the batch view; null otherwise.
    Guid? SelectedBatchId { get; }

    /// Every product picked, top to bottom, once each; the focused row
    /// when none is picked.
    IReadOnlyList<(Guid Id, string Code)> SelectedProducts { get; }

    /// <summary>
    /// Whether the operator has asked to see the shelf a batch at a time
    /// rather than a product at a time.
    /// </summary>
    bool BatchView { get; }

    void ShowProducts(IReadOnlyList<ProductListItemDto> products);
    void ShowBatches(IReadOnlyList<ProductBatchListItemDto> rows);
    /// <summary>
    /// Which page of the matches is showing, so the screen can say "101
    /// to 200 of 4,318" and offer the pages either side.
    /// </summary>
    /// <param name="page">From nought.</param>
    /// <param name="shown">Products on this page.</param>
    void ShowPage(int page, int pageSize, int shown, int total);

    /// <summary>
    /// The cell just committed, when the operator may edit the list.
    /// Null when nothing was committed.
    /// </summary>
    ProductCellEditDto? PendingEdit { get; }

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? DeactivateRequested;
    event EventHandler? RefreshRequested;
    event EventHandler? ChooseRequested;
    event EventHandler? ViewModeChanged;
    event EventHandler? CellEdited;
    event EventHandler<Controls.PageMove>? PageRequested;
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

    /// <summary>
    /// The product, with its batches when it has any on the shelf.
    /// </summary>
    void ShowProduct(ProductDto product);

    /// <summary>
    /// Everything on the form, the stock included. The quantity on hand
    /// is only sent when it was changed, and only for a product whose
    /// stock is not on batches; a batch's quantity is changed on its row.
    /// </summary>
    ProductEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

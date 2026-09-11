using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public interface IProductListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }
    void ShowProducts(IReadOnlyList<ProductListItemDto> products);
    void ShowCount(int shown, int total);

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? RefreshRequested;
}

public interface IProductEditView : IView
{
    /// Null for a new product. Set by the list screen before the dialog
    /// is shown, and read by the presenter in InitialiseAsync.
    Guid? ProductId { get; }

    void ShowProduct(ProductDto product);
    ProductEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

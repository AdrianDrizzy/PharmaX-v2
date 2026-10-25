using PharmaX.Application.Alerts;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// The pharmacy's standing list of stock problems.
/// </summary>
public interface IAlertView : Mvp.IView
{
    /// Which kinds the operator is looking at, or null for all of them.
    AlertKind? Filter { get; }

    Guid? SelectedProductId { get; }

    /// The keys of every row chosen, for deleting several at once.
    IReadOnlyList<string> SelectedKeys { get; }

    /// Whether every row on the page in view is chosen (Ctrl+A).
    bool WholePageChosen { get; }

    void ShowAlerts(IReadOnlyList<StockAlertDto> alerts);

    /// The line above the grid: how many, and how many are urgent.
    void ShowSummary(string summary);

    /// Opens the product behind the selected row.
    void OpenProduct(Guid productId);

    /// Offers Delete, to those who may clear expired and out-of-stock lines.
    void AllowDelete(bool allowed);

    event EventHandler? RefreshRequested;
    event EventHandler? FilterChanged;
    event EventHandler? OpenProductRequested;
    event EventHandler? DeleteRequested;
}

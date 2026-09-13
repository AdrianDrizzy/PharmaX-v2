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

    void ShowAlerts(IReadOnlyList<StockAlertDto> alerts);

    /// The line above the grid: how many, and how many are urgent.
    void ShowSummary(string summary);

    /// Opens the product behind the selected row.
    void OpenProduct(Guid productId);

    event EventHandler? RefreshRequested;
    event EventHandler? FilterChanged;
    event EventHandler? OpenProductRequested;
}

using PharmaX.Application.Sales;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The queue of dispensed scripts waiting at the till to be paid for.
/// </summary>
public interface ICollectView : Mvp.IView
{
    /// What the cashier typed, to narrow a long queue by name or number.
    string FilterText { get; }

    Guid? SelectedDispensingId { get; }

    void ShowWaiting(IReadOnlyList<CollectionDto> waiting);

    /// The line above the grid: how many are waiting and for how long.
    void ShowSummary(string summary);

    /// <summary>
    /// Opens the till on one script. Returns true where the patient paid,
    /// which is what tells the queue to refresh.
    /// </summary>
    bool TakePayment(Guid dispensingId, string patientName);

    event EventHandler? RefreshRequested;
    event EventHandler? FilterChanged;
    event EventHandler? CollectRequested;
}

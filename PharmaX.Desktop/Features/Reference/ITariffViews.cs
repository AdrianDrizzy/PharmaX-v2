using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

public interface ITariffListView : IView
{
    string SearchText { get; }

    /// Null means every society.
    Guid? SchemeFilter { get; }

    Guid? SelectedId { get; }
    bool SelectedIsActive { get; }

    void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes);
    void ShowTariffs(IReadOnlyList<TariffListItemDto> tariffs);
    void ShowCount(int shown);

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? ToggleActiveRequested;
    event EventHandler? RefreshRequested;
}

public interface ITariffEditView : IView
{
    /// Null for a new tariff.
    Guid? TariffId { get; }

    void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes);
    void ShowTariff(TariffListItemDto tariff);

    /// <summary>
    /// Which product this prices. Chosen through the product picker
    /// rather than typed, so a tariff cannot be written against a code
    /// that does not exist.
    /// </summary>
    void ShowProduct(Guid productId, string code, string description);

    /// <summary>
    /// The society and the product are what make a tariff unique, so
    /// neither moves once it has been saved.
    /// </summary>
    void LockSubject();

    TariffEditDto? Collect();

    event EventHandler? ChooseProductRequested;
    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

/// <summary>
/// Departments, Manufacturers, Branches and Medical Aid Societies. Four
/// menu entries, one screen: they are the same list-and-edit shape and
/// differ only in what they are called and what the extra column means.
/// </summary>
public interface IReferenceListView : IView
{
    ReferenceKind Kind { get; }
    string SearchText { get; }

    /// Retired rows stay in the database. They leave the list unless this
    /// is ticked, so they can be brought back.
    bool IncludeInactive { get; }

    /// The Sort and date range under the search box.
    Application.Abstractions.ListFilter Filter { get; }

    Guid? SelectedId { get; }
    bool SelectedIsActive { get; }

    void ShowItems(IReadOnlyList<ReferenceItemDto> items);
    void ShowCount(int shown);
    void ApplyLabels(ReferenceLabels labels);

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? ToggleActiveRequested;
    event EventHandler? RefreshRequested;
}

public interface IReferenceEditView : IView
{
    ReferenceKind Kind { get; }

    /// Null for a new entry, set by the list screen before the dialog.
    Guid? ItemId { get; }

    void ApplyLabels(ReferenceLabels labels);
    void ShowItem(ReferenceItemDto item);

    /// <summary>
    /// The code identifies the entry everywhere else in the system, so
    /// it is fixed once the entry exists.
    /// </summary>
    void LockCode();

    ReferenceEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

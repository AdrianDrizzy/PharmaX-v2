using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

public sealed class ReferenceListPresenter : PresenterBase<IReferenceListView>
{
    private readonly IReferenceDataService _reference;
    private readonly ViewFactory _views;

    public ReferenceListPresenter(IReferenceDataService reference,
                                  ViewFactory views)
    {
        _reference = reference;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.SearchChanged += (_, _) => _ = LoadAsync();
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);
        View.ToggleActiveRequested += (_, _) => _ = ToggleAsync();
    }

    public override Task InitialiseAsync()
    {
        View.ApplyLabels(ReferenceLabels.For(View.Kind));
        return LoadAsync();
    }

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _reference.ListAsync(View.Kind, View.SearchText);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowItems(result.Value);
        View.ShowCount(result.Value.Count);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmReferenceEdit>();
        editor.Prepare(View.Kind, id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    /// <summary>
    /// Retires an entry, or brings it back. Never deletes: everything
    /// that already points at it would be pointing at nothing.
    /// </summary>
    private Task ToggleAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id) return;

        var labels = ReferenceLabels.For(View.Kind);
        var active = View.SelectedIsActive;

        var question = active
            ? $"Retire this {labels.Singular}? It stays on past records "
              + "but cannot be chosen again."
            : $"Bring this {labels.Singular} back into use?";

        if (!View.Confirm(question)) return;

        var result = await _reference.SetActiveAsync(View.Kind, id, !active);

        if (result.IsFailure) View.ShowError(result.Error.Message);

        await LoadAsync();
    });
}

public sealed class ReferenceEditPresenter : PresenterBase<IReferenceEditView>
{
    private readonly IReferenceDataService _reference;

    public ReferenceEditPresenter(IReferenceDataService reference) =>
        _reference = reference;

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        View.ApplyLabels(ReferenceLabels.For(View.Kind));

        if (View.ItemId is not Guid id) return;      // adding a new one

        var result = await _reference.GetAsync(View.Kind, id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowItem(result.Value);
        View.LockCode();
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        if (string.IsNullOrWhiteSpace(edit.Code))
        {
            View.ShowError("A code is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(edit.Name))
        {
            View.ShowError("A name is required.");
            return;
        }

        var result = View.ItemId is Guid id
            ? await _reference.UpdateAsync(View.Kind, id, edit)
            : (await _reference.CreateAsync(View.Kind, edit))
                as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

public interface IPatientListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }

    void ShowPatients(IReadOnlyList<PatientListItemDto> patients);
    void ShowCount(int count);

    /// <summary>
    /// True where the screen was opened to pick a patient for another
    /// screen rather than to maintain the list. The presenter uses it to
    /// decide whether choosing a row closes the window.
    /// </summary>
    bool IsPickerMode { get; }

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? DeactivateRequested;
    event EventHandler? ChooseRequested;
}

public interface IPatientEditView : IView
{
    Guid? PatientId { get; }

    void ShowPatient(PatientDto patient);
    PatientEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

public sealed class PatientListPresenter : PresenterBase<IPatientListView>
{
    private readonly IPatientService _patients;
    private readonly ViewFactory _views;

    public PatientListPresenter(IPatientService patients, ViewFactory views)
    {
        _patients = patients;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.SearchChanged += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);
        View.DeactivateRequested += (_, _) => _ = DeactivateAsync();

        View.ChooseRequested += (_, _) =>
        {
            // In picker mode the caller reads SelectedId off the form, so
            // all this has to do is close with a success result.
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a patient first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _patients.SearchAsync(View.SearchText);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowPatients(result.Value);
        View.ShowCount(result.Value.Count);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmPatientEdit>();
        editor.LoadPatient(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task DeactivateAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a patient first.");
            return;
        }

        if (!View.Confirm(
                "Deactivate this patient? Their dispensing history is "
                + "kept - it is a legal record - but they will not appear "
                + "in searches."))
            return;

        var result = await _patients.DeactivateAsync(id);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public sealed class PatientEditPresenter : PresenterBase<IPatientEditView>
{
    private readonly IPatientService _patients;

    public PatientEditPresenter(IPatientService patients) =>
        _patients = patients;

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid id) return;   // adding a new one

        var result = await _patients.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowPatient(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        var result = View.PatientId is Guid id
            ? await _patients.UpdateAsync(id, edit)
            : (await _patients.CreateAsync(edit)) as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

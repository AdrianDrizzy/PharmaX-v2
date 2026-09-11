using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

public interface IScriptListView : IView
{
    Guid? PatientId { get; }
    Guid? SelectedScriptId { get; }

    void ShowPatient(string description);
    void ShowScripts(IReadOnlyList<ScriptDto> scripts);
    string? PromptCancelReason();

    /// True where the screen was opened to pick a script for dispensing
    /// rather than to maintain the list.
    bool IsPickerMode { get; }

    event EventHandler? RefreshRequested;
    event EventHandler? CaptureRequested;
    event EventHandler? CancelScriptRequested;
    event EventHandler? ChooseRequested;
}

public interface IScriptEditView : IView
{
    Guid PatientId { get; }

    ScriptEditDto Collect();
    void ShowPatient(string description);

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

public sealed class ScriptListPresenter : PresenterBase<IScriptListView>
{
    private readonly IScriptService _scripts;
    private readonly IPatientService _patients;
    private readonly ViewFactory _views;

    public ScriptListPresenter(IScriptService scripts,
                               IPatientService patients, ViewFactory views)
    {
        _scripts = scripts;
        _patients = patients;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.CaptureRequested += (_, _) => _ = CaptureAsync();
        View.CancelScriptRequested += (_, _) => _ = CancelAsync();

        View.ChooseRequested += (_, _) =>
        {
            if (View.SelectedScriptId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a script first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid patientId) return;

        var patient = await _patients.GetAsync(patientId);

        if (patient.IsSuccess)
            View.ShowPatient(
                $"{patient.Value.FullNameOrFile()}  ({patient.Value.FileNumber})");

        var result = await _scripts.ForPatientAsync(patientId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowScripts(result.Value);
    });

    private Task CaptureAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid patientId) return;

        using var editor = _views.Create<frmScriptEdit>();
        editor.LoadForPatient(patientId);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task CancelAsync() => GuardAsync(async () =>
    {
        if (View.SelectedScriptId is not Guid id)
        {
            View.ShowInfo("Choose a script first.");
            return;
        }

        var reason = View.PromptCancelReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _scripts.CancelAsync(id, reason);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public sealed class ScriptEditPresenter : PresenterBase<IScriptEditView>
{
    private readonly IScriptService _scripts;
    private readonly IPatientService _patients;

    public ScriptEditPresenter(IScriptService scripts,
                               IPatientService patients)
    {
        _scripts = scripts;
        _patients = patients;
    }

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var patient = await _patients.GetAsync(View.PatientId);

        if (patient.IsFailure)
        {
            View.ShowError(patient.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowPatient(
            $"{patient.Value.FullNameOrFile()}  ({patient.Value.FileNumber})");
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var result = await _scripts.CaptureAsync(View.Collect());

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

internal static class PatientDtoExtensions
{
    /// <summary>
    /// A display name that never comes out blank, because a header
    /// reading "  (P-001-000004)" tells the pharmacist nothing.
    /// </summary>
    public static string FullNameOrFile(
        this Application.Patients.Dtos.PatientDto patient)
    {
        var name = $"{patient.FirstNames} {patient.Surname}".Trim();
        return name.Length == 0 ? patient.FileNumber : name;
    }
}

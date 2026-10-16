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

    /// Moves the list onto one script, where it is there.
    void SelectScript(Guid scriptId);

    /// True where the screen was opened to pick a script for dispensing
    /// rather than to maintain the list.
    bool IsPickerMode { get; }

    event EventHandler? RefreshRequested;
    event EventHandler? CaptureRequested;
    event EventHandler? EditScriptRequested;
    event EventHandler? CancelScriptRequested;
    event EventHandler? ChooseRequested;
    event EventHandler? QuoteRequested;

    /// A new script from the chosen one, changed as needed and captured.
    event EventHandler? UseAgainRequested;
}

public interface IScriptEditView : IView
{
    Guid PatientId { get; }

    /// The script being corrected, or null for a new one.
    Guid? ScriptId { get; }

    ScriptEditDto Collect();
    void ShowPatient(string description);

    /// Fills the form with a captured script for correcting.
    void ShowScript(ScriptDetailDto script);

    /// The new script, once it is captured.
    void ScriptCaptured(Guid scriptId);

    /// Asks which of what the patient has had before go on this script.
    IReadOnlyList<SuppliedItemDto> PickFromHistory(
        IReadOnlyList<SuppliedItemDto> supplied);

    /// Puts lines on the script, to be changed like any other.
    void AddItems(IReadOnlyList<ScriptItemEditDto> items);

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
    event EventHandler? HistoryRequested;
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
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.CaptureRequested += (_, _) => Enqueue(CaptureAsync);
        View.EditScriptRequested += (_, _) => Enqueue(EditAsync);
        View.CancelScriptRequested += (_, _) => Enqueue(CancelAsync);
        View.QuoteRequested += (_, _) => Enqueue(QuoteAsync);
        View.UseAgainRequested += (_, _) => Enqueue(UseAgainAsync);

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
                patient.Value.Header());

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

    /// <summary>
    /// Prices what the patient asked about, and captures the script from
    /// it if they go ahead. The new script is left chosen, so a picker
    /// opened from dispensing needs only Choose.
    /// </summary>
    private Task QuoteAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid patientId) return;

        using var quote = _views.Create<frmDispensaryQuote>();
        quote.ForPatient(patientId);

        if (quote.ShowDialog((Form)View) != DialogResult.OK) return;

        await LoadAsync();

        if (quote.CreatedScriptId is Guid scriptId) View.SelectScript(scriptId);
    });

    /// <summary>
    /// Drafts a new script from the chosen one - imported ones included -
    /// at what was last handed over, for the operator to change and
    /// capture. The new script is left chosen, ready to dispense.
    /// </summary>
    private Task UseAgainAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid patientId) return;

        if (View.SelectedScriptId is not Guid id)
        {
            View.ShowInfo("Choose a script first.");
            return;
        }

        var draft = await _scripts.RepeatAsync(id);

        if (draft.IsFailure)
        {
            View.ShowError(draft.Error.Message);
            return;
        }

        using var editor = _views.Create<frmScriptEdit>();
        editor.LoadDraft(patientId, draft.Value);

        if (editor.ShowDialog((Form)View) != DialogResult.OK) return;

        await LoadAsync();

        if (editor.CapturedScriptId is Guid scriptId) View.SelectScript(scriptId);
    });

    /// <summary>
    /// Opens a captured script for correcting, where it can still be.
    /// </summary>
    private Task EditAsync() => GuardAsync(async () =>
    {
        if (View.PatientId is not Guid patientId) return;

        if (View.SelectedScriptId is not Guid id)
        {
            View.ShowInfo("Choose a script first.");
            return;
        }

        var script = await _scripts.GetAsync(id);

        if (script.IsFailure)
        {
            View.ShowError(script.Error.Message);
            return;
        }

        if (!script.Value.IsEditable)
        {
            View.ShowInfo($"Script {script.Value.Script.ScriptNumber} has "
                + "been dispensed against or cancelled, so it can no longer "
                + "be changed. Cancel it and capture it again.");
            return;
        }

        using var editor = _views.Create<frmScriptEdit>();
        editor.LoadScript(patientId, id);

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
        View.SaveRequested += (_, _) => Enqueue(SaveAsync);
        View.CancelRequested += (_, _) => View.CloseView(false);
        View.HistoryRequested += (_, _) => Enqueue(FromHistoryAsync);
    }

    private Task FromHistoryAsync() => GuardAsync(async () =>
    {
        var supplied = await _scripts.SuppliedAsync(View.PatientId);

        if (supplied.IsFailure)
        {
            View.ShowError(supplied.Error.Message);
            return;
        }

        if (supplied.Value.Count == 0)
        {
            View.ShowInfo("Nothing has been prescribed or dispensed for this "
                          + "patient before.");
            return;
        }

        var picked = View.PickFromHistory(supplied.Value);
        if (picked.Count > 0) View.AddItems(picked.Select(p => p.ToEdit()).ToList());
    });

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
            patient.Value.Header());

        if (View.ScriptId is not Guid scriptId) return;

        var script = await _scripts.GetAsync(scriptId);

        if (script.IsFailure)
        {
            View.ShowError(script.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowScript(script.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        if (View.ScriptId is Guid id)
        {
            var updated = await _scripts.UpdateAsync(id, View.Collect());

            if (updated.IsFailure)
            {
                View.ShowError(updated.Error.Message);
                return;
            }

            View.CloseView(true);
            return;
        }

        var captured = await _scripts.CaptureAsync(View.Collect());

        if (captured.IsFailure)
        {
            View.ShowError(captured.Error.Message);
            return;
        }

        View.ScriptCaptured(captured.Value);
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

    /// <summary>
    /// Name, file number and allergies, for the header of every screen
    /// where a script is captured or priced: the allergy has to be in
    /// front of the pharmacist before the item is chosen, not only at
    /// the dispensing screen's warning.
    /// </summary>
    public static string Header(
        this Application.Patients.Dtos.PatientDto patient)
    {
        var header = $"{patient.FullNameOrFile()}  ({patient.FileNumber})";

        return patient.Allergies.Count == 0
            ? header
            : header + "   ALLERGIES: " + string.Join(", ",
                patient.Allergies.Select(a => $"{a.Substance} ({a.Severity})"));
    }
}

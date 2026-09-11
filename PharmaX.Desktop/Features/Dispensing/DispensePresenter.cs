using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

public sealed class DispensePresenter : PresenterBase<IDispenseView>
{
    private readonly IDispensingService _dispensing;
    private readonly IScriptService _scripts;
    private readonly Application.Patients.IPatientService _patients;
    private readonly ILabelPrinter _labels;

    private Guid _dispensingId;

    public DispensePresenter(IDispensingService dispensing,
                             IScriptService scripts,
                             Application.Patients.IPatientService patients,
                             ILabelPrinter labels)
    {
        _dispensing = dispensing;
        _scripts = scripts;
        _patients = patients;
        _labels = labels;
    }

    protected override void Subscribe()
    {
        View.ScriptRequested += (_, _) => _ = BeginAsync();
        View.ProductScanned += (_, code) => _ = AddItemAsync(code);
        View.IssueRequested += (_, _) => _ = IssueAsync();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    private Task BeginAsync() => GuardAsync(async () =>
    {
        var patientId = View.PromptPatientSearch();
        if (patientId is null) return;

        var scriptId = View.PromptScriptSearch(patientId.Value);
        if (scriptId is null) return;

        var result = await _dispensing.BeginAsync(scriptId.Value);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _dispensingId = result.Value.Id;

        // The header comes from the patient and the script rather than
        // from the dispensing, because it is what the pharmacist checks
        // before the first item goes on.
        var patient = await _patients.GetAsync(patientId.Value);
        if (patient.IsSuccess)
            View.ShowPatient(ToSummary(patient.Value));

        var script = await _scripts.GetAsync(scriptId.Value);
        if (script.IsSuccess) View.ShowScript(script.Value.Script);

        Render(result.Value);
    });

    private Task AddItemAsync(string code) => GuardAsync(async () =>
    {
        if (_dispensingId == Guid.Empty)
        {
            View.ShowError("Find the patient and the script first.");
            return;
        }

        var directions = View.PromptDirections(Guid.Empty);
        if (directions is null) return;

        var result = await _dispensing.AddItemAsync(
            _dispensingId, code, directions);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        // Warnings are computed by the service from the clinical profile
        // and the interaction table; the presenter only routes them.
        Render(result.Value);
    });

    /// <summary>
    /// The patient DTO the patient screen returns carries more than the
    /// dispensing header needs, so it is narrowed here rather than the
    /// view being handed a type from another feature.
    /// </summary>
    private static PatientSummaryDto ToSummary(
        Application.Patients.Dtos.PatientDto p) =>
        new(p.Id, p.FileNumber, $"{p.FirstNames} {p.Surname}".Trim(),
            p.Age, p.Telephone, null, null,
            p.Allergies.Select(a => $"{a.Substance} ({a.Severity})").ToList(),
            p.ChronicConditions);

    private void Render(DispensingStateDto state)
    {
        View.ShowWarnings(state.Warnings);
        View.ShowItems(state.Items);
        View.ShowCoPayment(state.TariffTotal, state.ContributionTotal,
                           state.CoPaymentTotal);
    }

    private Task IssueAsync() => GuardAsync(async () =>
    {
        var pending = await _dispensing.PendingWarningsAsync(_dispensingId);

        foreach (var warning in pending)
        {
            if (warning.IsHardStop)
            {
                var reason = View.PromptWarningOverride(warning);

                if (string.IsNullOrWhiteSpace(reason))
                {
                    View.ShowError("Item not issued.");
                    return;
                }

                await _dispensing.AcknowledgeWarningAsync(warning.Id, reason);
            }
            else
            {
                await _dispensing.AcknowledgeWarningAsync(warning.Id, null);
            }
        }

        // Pharmacist authentication at the moment of issue, not earlier
        // and not delegated (DSP-006, SEC-006).
        var pharmacist = View.PromptPharmacistAuthorisation(
            "Pharmacist authorisation is required to issue this script.");

        if (pharmacist is null)
        {
            View.ShowError("Not issued: no pharmacist authorisation.");
            return;
        }

        var result = await _dispensing.IssueAsync(_dispensingId,
            pharmacistId: pharmacist.Value);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        foreach (var label in result.Value.Labels)
            _labels.Print(label);

        View.ShowInfo($"Issued. Script {result.Value.ScriptNumber}. " +
                      $"Co-payment {result.Value.CoPayment:N2} " +
                      "has been added to the sale.");

        View.CloseView(true);
    });
}

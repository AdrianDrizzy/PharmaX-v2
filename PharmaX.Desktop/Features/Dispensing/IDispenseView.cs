using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Desktop.Features.Dispensing;

public interface IDispenseView : Mvp.IView
{
    void ShowPatient(PatientSummaryDto patient);
    void ShowScript(ScriptDto script);
    void ShowItems(IReadOnlyList<DispensingLineDto> items);
    void ShowWarnings(IReadOnlyList<ClinicalWarningDto> warnings);
    void ShowCoPayment(decimal tariff, decimal contribution, decimal coPayment);

    Guid? PromptPatientSearch();
    Guid? PromptScriptSearch(Guid patientId);
    DirectionsDto? PromptDirections(Guid productId);

    /// Returns the pharmacist who authenticated, or null if cancelled.
    Guid? PromptPharmacistAuthorisation(string reasonShown);

    /// Returns the reason given, or null if the pharmacist declined to
    /// override a hard-stop warning.
    string? PromptWarningOverride(ClinicalWarningDto warning);

    event EventHandler? PatientRequested;
    event EventHandler? ScriptRequested;
    event EventHandler<string>? ProductScanned;
    event EventHandler? IssueRequested;
    event EventHandler? LabelReprintRequested;
    event EventHandler? CloseRequested;
}

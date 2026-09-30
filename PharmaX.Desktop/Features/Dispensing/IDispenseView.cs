using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Desktop.Features.Dispensing;

public interface IDispenseView : Mvp.IView
{
    void ShowPatient(PatientSummaryDto patient);
    void ShowScript(ScriptDto script);
    void ShowItems(IReadOnlyList<DispensingLineDto> items);
    void ShowWarnings(IReadOnlyList<ClinicalWarningDto> warnings);
    void ShowCoPayment(decimal tariff, decimal contribution, decimal coPayment);

    /// The item the pharmacist has selected in the grid, if any.
    int? SelectedLineNo { get; }

    Guid? PromptPatientSearch();
    Guid? PromptScriptSearch(Guid patientId);

    /// <summary>
    /// Opens the dispensary quote. The patient and the script it became
    /// where the customer went ahead, or null.
    /// </summary>
    (Guid PatientId, Guid ScriptId)? PromptQuote();

    /// <summary>
    /// A script to begin on as the screen opens - one just captured from a
    /// quote - or null to start by finding one.
    /// </summary>
    (Guid PatientId, Guid ScriptId)? StartWith { get; }

    /// <summary>
    /// Asks for the quantity and directions, starting from what the
    /// script says. Null if cancelled.
    /// </summary>
    DirectionsDto? PromptDirections(ItemProposalDto proposal);

    /// Returns the pharmacist who authenticated, or null if cancelled.
    Guid? PromptPharmacistAuthorisation(string reasonShown);

    /// Returns the reason given, or null if the pharmacist declined to
    /// override a hard-stop warning.
    string? PromptWarningOverride(ClinicalWarningDto warning);

    /// Opens the Labels screen, where any issued dispensing's labels are
    /// printed again.
    void ShowLabelScreen();

    event EventHandler? PatientRequested;
    event EventHandler? ScriptRequested;
    event EventHandler? QuoteRequested;
    event EventHandler<string>? ProductScanned;
    event EventHandler? IssueRequested;
    event EventHandler? LabelReprintRequested;
    event EventHandler? RemoveItemRequested;
    event EventHandler? CloseRequested;
}

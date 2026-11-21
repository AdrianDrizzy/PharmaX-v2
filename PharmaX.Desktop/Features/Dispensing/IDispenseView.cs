using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Desktop.Features.Dispensing;

public interface IDispenseView : Mvp.IView
{
    void ShowPatient(PatientSummaryDto patient);
    void ShowScript(ScriptDto script);
    void ShowItems(IReadOnlyList<DispensingLineDto> items);
    void ShowWarnings(IReadOnlyList<ClinicalWarningDto> warnings);
    void ShowCoPayment(decimal tariff, decimal contribution, decimal coPayment);

    /// <summary>
    /// Says what to do next and offers only the buttons that can be used
    /// now: nothing to issue before a script, nothing to change before an
    /// item.
    /// </summary>
    void ShowStage(DispenseStage stage);

    /// Whether issuing prints the item labels. Off still issues the script.
    bool PrintLabels { get; set; }

    /// Whether a trailer label follows the item labels at the issue.
    bool PrintTrailer { get; set; }

    /// The item the pharmacist has selected in the grid, if any.
    int? SelectedLineNo { get; }

    /// Every item picked, with Ctrl or Shift, for taking off together.
    IReadOnlyList<int> SelectedLineNos { get; }

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

    /// The batch number and expiry printed on the box; null if cancelled.
    (string Batch, DateOnly Expiry)? AskBatchFromBox(string productDescription);

    /// <summary>
    /// Labels about to print with no expiry. Null holds them back; an
    /// empty answer prints them as they are; otherwise the expiries typed
    /// in, by item number.
    /// </summary>
    IReadOnlyDictionary<int, DateOnly>? ConfirmMissingExpiry(IReadOnlyList<LabelDto> missing);

    /// Returns the pharmacist who authenticated, or null if cancelled.
    Guid? PromptPharmacistAuthorisation(string reasonShown);

    /// Returns whoever authorised changing the charge - someone holding
    /// dsp.charge.adjust - or null if cancelled.
    Guid? PromptChargeAuthorisation(string reasonShown);

    /// Returns the reason given, or null if the pharmacist declined to
    /// override a hard-stop warning.
    string? PromptWarningOverride(ClinicalWarningDto warning);

    /// Opens the Labels screen, where any issued dispensing's labels are
    /// printed again.
    void ShowLabelScreen();

    /// Asks how an item on the dispensing should change. Null if cancelled.
    ItemChange.Answer? PromptItemChange(IReadOnlyList<DispensingLineDto> items,
                                        int? lineNo);

    event EventHandler? PatientRequested;
    event EventHandler? ScriptRequested;
    event EventHandler? QuoteRequested;
    event EventHandler<string>? ProductScanned;
    event EventHandler? IssueRequested;
    event EventHandler? LabelReprintRequested;
    event EventHandler? RemoveItemRequested;
    event EventHandler? ChangeItemRequested;
    event EventHandler? CloseRequested;
    event EventHandler? SuspendedRequested;

    /// The operator ticked or cleared "Print labels".
    event EventHandler? PrintLabelsChanged;

    /// The operator ticked or cleared "Print trailer label".
    event EventHandler? PrintTrailerChanged;

    /// One of the scripts kept to finish later; null to cancel.
    (Guid PatientId, Guid ScriptId)? PromptSuspended(
        IReadOnlyList<SuspendedDispensingDto> kept);
}

/// <summary>Where a dispensing has got to, for what the screen offers.</summary>
public enum DispenseStage
{
    /// Nothing loaded: find the patient and the script.
    NoScript,

    /// A script with nothing on it yet: scan or type the first item.
    NoItems,

    /// Items on: check them and issue.
    Ready
}
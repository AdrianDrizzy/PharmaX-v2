using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// One row of a settings or diagnostics list: which group it belongs
/// under, what it is called, what it is set to, and where that value
/// actually lives.
/// </summary>
/// <remarks>
/// Source matters as much as value on these two screens. "Currency USD"
/// is a different fact depending on whether it came from this till's own
/// store or from the installed configuration file, and the person
/// reading the screen is usually trying to work out which.
/// </remarks>
public sealed record SystemInfoRow(string Group, string Name, string Value,
                                   string Source);

/// <summary>
/// What a workstation is configured to do (Control Variables) and what
/// it is currently doing (System Console). Both are read-only lists of
/// the same shape, so they share a view.
/// </summary>
public interface ISystemInfoView : IView
{
    /// Set by the caller before the dialog is shown.
    SystemInfoMode Mode { get; }

    void ShowCaption(string caption);
    void ShowRows(IReadOnlyList<SystemInfoRow> rows);

    /// <summary>
    /// The pharmacy's trading name, VAT number and address, editable on
    /// the System Console. The Control Variables list never shows them
    /// as fields, only as rows.
    /// </summary>
    void ShowPharmacyDetails(Application.Abstractions.PharmacyDetails details);

    /// What the operator has typed into the pharmacy details.
    Application.Abstractions.PharmacyDetails EnteredPharmacyDetails { get; }

    event EventHandler? RefreshRequested;
    event EventHandler? CloseRequested;
    event EventHandler? SavePharmacyRequested;
}

public enum SystemInfoMode
{
    /// The settings this workstation runs on.
    ControlVariables,

    /// What the workstation is doing right now: version, database,
    /// connectivity, where the logs are.
    SystemConsole
}

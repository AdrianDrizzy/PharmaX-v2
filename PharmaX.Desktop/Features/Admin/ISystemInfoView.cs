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

    event EventHandler? RefreshRequested;
    event EventHandler? CloseRequested;
}

public enum SystemInfoMode
{
    /// The settings this workstation runs on.
    ControlVariables,

    /// What the workstation is doing right now: version, database,
    /// connectivity, where the logs are.
    SystemConsole
}

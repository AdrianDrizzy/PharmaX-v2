namespace PharmaX.Desktop.Shell;

public interface IMainView : Mvp.IView
{
    void SetUser(string username, string roles);
    void SetTill(string branchAndTill);
    void SetSession(string sessionState);
    void SetConnectivity(string state);

    /// The trading name, shown on the shell's home screen.
    void SetPharmacy(string tradingName);

    /// Redraws the logo, after system setup has changed it.
    void RefreshBranding();
    void EnableMenu(string menuKey, bool enabled);

    /// Called only where test accounts have been seeded, so the window
    /// cannot be mistaken for a live till.
    void MarkAsTestEnvironment();

    /// <summary>
    /// Closes the shell in a way the host reads as "sign in again"
    /// rather than "quit", so a shift change does not need the
    /// application restarted.
    /// </summary>
    void LogOff();

    event EventHandler<string>? MenuSelected;
}

namespace PharmaX.Desktop.Shell;

public interface IMainView : Mvp.IView
{
    void SetUser(string username, string roles);
    void SetTill(string branchAndTill);
    void SetSession(string sessionState);
    void SetConnectivity(string state);

    /// The pharmacy's name, and its address and VAT number beneath it,
    /// at the head of the shell's home screen. Particulars may be empty.
    void SetPharmacy(string name, string particulars);

    /// Redraws the logo, after system setup has changed it.
    void RefreshBranding();
    /// <summary>
    /// Enables each menu entry, shortcut and home tile on its own, by its
    /// menu key; a heading is enabled while anything under it is.
    /// </summary>
    void ApplyAccess(Func<string, bool> canOpen);

    /// Called only where test accounts have been seeded, so the window
    /// cannot be mistaken for a live till.
    void MarkAsTestEnvironment();

    /// <summary>
    /// Closes the shell in a way the host reads as "sign in again"
    /// rather than "quit", so a shift change does not need the
    /// application restarted.
    /// </summary>
    void LogOff();

    /// <summary>
    /// How many dispensed scripts are waiting at the till, shown in the
    /// status bar so that a cashier who never opens the collection
    /// screen still knows somebody is standing at the counter.
    /// </summary>
    /// <param name="waiting">Empty where there is nothing waiting.</param>
    void SetWaiting(string waiting);

    /// <summary>
    /// How much stock needs attention, shown beside the waiting count.
    /// </summary>
    /// <param name="alerts">Empty where nothing is wrong.</param>
    void SetAlerts(string alerts);

    event EventHandler<string>? MenuSelected;

    /// Raised on the shell's own timer, to re-read the count above.
    event EventHandler? WaitingTick;
}

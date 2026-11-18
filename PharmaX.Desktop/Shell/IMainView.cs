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

    /// The membership banner under the menu; null hides it. Urgent is red.
    void SetMembership(string? text, bool urgent);

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

    /// <summary>
    /// The stock assistant's button at the foot of the window, saying how
    /// many draft orders are waiting to be finalised.
    /// </summary>
    /// <param name="notice">Empty hides it.</param>
    void SetAssistantNotice(string notice);

    /// The stock assistant's button was clicked.
    event EventHandler? AssistantNoticeClicked;

    /// The window is on screen, so a pop-up opened now opens over it.
    event EventHandler? Opened;

    /// <summary>
    /// A figure on the home tile that opens this menu key - "6,732" on
    /// Patients, "4 waiting" on Collect - or none.
    /// </summary>
    void SetTileCount(string menuKey, string? count);

    /// <summary>
    /// When the job on a home tile was last done - "Last posted 12/10/2026"
    /// on Stocktake - or none.
    /// </summary>
    void SetTileNote(string menuKey, string? note);

    event EventHandler<string>? MenuSelected;

    /// The id of the ThemeMode just put in use from the Theme menu.
    event EventHandler<string>? ThemeChosen;

    /// The zoom just put in use from the View menu, in per cent.
    event EventHandler<int>? ZoomChosen;

    /// The window size just chosen from the View menu: "max", or
    /// "1366x768".
    event EventHandler<string>? WindowSizeChosen;

    /// <summary>
    /// Puts the shell's window back to the size this till last chose -
    /// "max" or nothing for the whole screen, or "1366x768".
    /// </summary>
    void UseWindowSize(string? size);

    /// Raised on the shell's own timer, to re-read the count above.
    event EventHandler? WaitingTick;
}

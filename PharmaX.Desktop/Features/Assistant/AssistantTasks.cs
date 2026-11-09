using PharmaX.Application.Assistant;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Hands a job from Ask PharmaX to the main window, which opens the screen
/// for it. One for the application, because Ask PharmaX opens over any
/// screen while only the shell knows how to open the others.
/// </summary>
/// <remarks>
/// One handler, replaced rather than added to: a sign-out and back in
/// builds a new shell, and the old one must not open screens as well.
/// </remarks>
public sealed class AssistantTasks
{
    private Action<AppTask>? _handler;

    public void HandleWith(Action<AppTask> handler) => _handler = handler;

    /// True where something is listening - the shell is up.
    public bool CanRun => _handler is not null;

    public void Run(AppTask task) => _handler?.Invoke(task);

    private Action<string>? _screen;

    /// What opens a screen named on a how-to answer, by its menu key.
    public void OpenScreenWith(Action<string> handler) => _screen = handler;

    public void OpenScreen(string menuKey) => _screen?.Invoke(menuKey);

    private Action<Application.Assistant.Guided.GuidedResult>? _open;

    /// What opens a record a guided job saved (or chose instead).
    public void OpenWith(Action<Application.Assistant.Guided.GuidedResult> handler) => _open = handler;

    public void Open(Application.Assistant.Guided.GuidedResult result) => _open?.Invoke(result);
}

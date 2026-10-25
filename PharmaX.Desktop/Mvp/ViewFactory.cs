using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Application.Security;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Resolves a form from the container and attaches its presenter, so no
/// form ever constructs its own dependencies.
/// </summary>
/// <remarks>
/// One container scope per screen, disposed when the window closes, so
/// each screen gets its own DbContext and its own unit of work. The
/// obvious implementation - a singleton factory holding IServiceProvider
/// - captures the root provider instead, and because scope validation is
/// off outside Development every scoped service then quietly becomes a
/// process-lifetime singleton: one DbContext accumulating every entity
/// the till touched all day. That works in a demo and rots on site.
/// </remarks>
public sealed class ViewFactory
{
    private readonly IServiceScopeFactory _scopes;

    public ViewFactory(IServiceScopeFactory scopes) => _scopes = scopes;

    public TForm Create<TForm>() where TForm : Form
    {
        var scope = _scopes.CreateScope();

        try
        {
            var form = scope.ServiceProvider.GetRequiredService<TForm>();

            // The screen's own permission, checked before it is shown, so
            // a shortcut, a home tile or a direct call cannot open what the
            // menu hides - and the refusal is recorded (DEF-0026, DEF-0027).
            if (form is PhxFormBase gated
                && !gated.MayOpenWith(scope.ServiceProvider
                    .GetRequiredService<IPermissionService>()))
            {
                var user = scope.ServiceProvider
                    .GetRequiredService<ICurrentUser>();

                scope.ServiceProvider.GetRequiredService<IAuditWriter>()
                    .Refused(gated.PermissionToOpen ?? "screen.open",
                             typeof(TForm).Name, user.Id,
                             "Screen refused before it opened.");

                form.Dispose();

                throw new PermissionDeniedException(
                    "You do not have permission for this action.");
            }

            if (form is PhxFormBase phx)
            {
                phx.UseConnectivity(
                    scope.ServiceProvider
                         .GetRequiredService<IConnectivityMonitor>(),
                    scope.ServiceProvider
                         .GetRequiredService<PharmaXOptions>()
                         .Database.IsShared);

                // Ctrl+Shift+A on any screen: Ask PharmaX over it, in a
                // scope of its own like any other screen.
                phx.UseAssistant(owner =>
                    ShowDialog<Features.Assistant.frmAssistant>(owner));
            }

            // The scope outlives this method and dies with the window.
            form.Disposed += (_, _) => scope.Dispose();

            return form;
        }
        catch
        {
            // A form that could not be built must not leak its scope,
            // and with it a database connection, for the rest of the day.
            scope.Dispose();
            throw;
        }
    }

    public DialogResult ShowDialog<TForm>(IWin32Window owner)
        where TForm : Form
    {
        using var form = Create<TForm>();
        return form.ShowDialog(owner);
    }

    /// The screens open beside the shell, by what opened them.
    private readonly Dictionary<string, Form> _open = new(StringComparer.Ordinal);

    /// <summary>
    /// Opens a screen beside the others rather than over them, so the
    /// operator can have the till, a product list and a report open at
    /// once and move between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One window per <paramref name="key"/>: asking again brings the one
    /// already open to the front rather than starting a second, which for
    /// the till or a stocktake would be two windows working on the same
    /// thing. Different keys may open the same screen - two different
    /// reports side by side.
    /// </para>
    /// <para>
    /// Owned by the shell, so each stays above it, and closing the shell -
    /// logging off, or leaving - closes them too, each asking first if it
    /// has something unsaved. Each still has its own scope, and so its own
    /// DbContext, exactly as a dialog did. A picker or a message box opened
    /// from one of them is still modal, and while it is up Windows holds
    /// the other windows too.
    /// </para>
    /// </remarks>
    /// <param name="prepare">Settings the screen needs before it is shown.</param>
    /// <param name="closed">Called once it has closed.</param>
    public void Show<TForm>(string key, Form owner, Action<TForm>? prepare = null,
                            Action? closed = null)
        where TForm : Form
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (_open.TryGetValue(key, out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;

            existing.Activate();
            return;
        }

        var form = Create<TForm>();

        try
        {
            prepare?.Invoke(form);
        }
        catch
        {
            form.Dispose();
            throw;
        }

        _open[key] = form;
        form.FormClosed += (_, _) =>
        {
            _open.Remove(key);

            // The shell itself closing - a log-off - takes its screens with
            // it, and has nothing left to catch up on.
            if (owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated)
            {
                form.Dispose();
                return;
            }

            // Disposing ends the screen's scope; then the shell catches up
            // with whatever it changed.
            owner.BeginInvoke(() =>
            {
                form.Dispose();
                if (!owner.IsDisposed) closed?.Invoke();
            });
        };

        form.Show(owner);
    }

    /// <summary>The screens open beside the shell right now.</summary>
    public int OpenCount => _open.Count;
}

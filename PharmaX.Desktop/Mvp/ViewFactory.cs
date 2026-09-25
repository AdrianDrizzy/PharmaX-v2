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
                phx.UseConnectivity(
                    scope.ServiceProvider
                         .GetRequiredService<IConnectivityMonitor>(),
                    scope.ServiceProvider
                         .GetRequiredService<PharmaXOptions>()
                         .Database.IsShared);

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
}

namespace PharmaX.Desktop.Mvp;

/// <remarks>
/// <para>
/// A screen's operations run one at a time. Every screen has one
/// container scope, and so one DbContext, and a DbContext cannot serve two
/// queries at once. Windows Forms makes that easy to break: an event
/// handler starts an operation and returns, the operation awaits the
/// database, and while it waits the next event starts another one on the
/// same context. The medical aid claims and tariff screens did exactly
/// that on opening - filling the scheme list raised the selection event,
/// which started a load while the screen's own first load was still
/// reading - and the operator got a raw "a second operation was started on
/// this context" crash (DEF-0011).
/// </para>
/// <para>
/// So a presenter answers the view through Enqueue, which starts an
/// operation only once the one before it has finished, and GuardAsync
/// called from inside a running operation carries on inline - which is
/// what a load at the end of a save wants. An operation started by calling
/// GuardAsync directly, as InitialiseAsync is from the form's OnLoad, joins
/// the same queue.
/// </para>
/// </remarks>
public abstract class PresenterBase<TView> : IPresenter<TView>
    where TView : class, IView
{
    private Task _tail = Task.CompletedTask;
    private int _depth;

    protected TView View { get; private set; } = null!;

    public void Attach(TView view)
    {
        View = view;
        Subscribe();
    }

    /// Wire view events here. Called once, immediately after Attach.
    protected abstract void Subscribe();

    /// Load initial data here. Called by the form's OnLoad.
    public virtual Task InitialiseAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs an operation the view asked for, after whatever this screen is
    /// already doing has finished.
    /// </summary>
    /// <remarks>
    /// The event handlers' way in. Fire and forget from the view's point
    /// of view, but never concurrent with another operation on the same
    /// screen, and never able to lose an exception: anything the
    /// operation's own guard did not handle is reported here.
    /// </remarks>
    protected void Enqueue(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var previous = _tail;
        var done = new TaskCompletionSource();
        _tail = done.Task;

        _ = RunAfterAsync(previous, done, operation);
    }

    private async Task RunAfterAsync(Task previous, TaskCompletionSource done,
                                     Func<Task> operation)
    {
        try
        {
            await previous.ConfigureAwait(true);

            _depth++;

            try
            {
                await operation().ConfigureAwait(true);
            }
            finally
            {
                _depth--;
            }
        }
#pragma warning disable CA1031 // The last place an operation's exception can be seen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Report(ex);
        }
        finally
        {
            done.TrySetResult();
        }
    }

    /// Runs an operation with a busy indicator and one error path.
    protected async Task GuardAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Already inside an operation - a save that reloads, a choice
        // that previews - so this is part of it, not a second one.
        if (_depth > 0)
        {
            await RunGuardedAsync(action).ConfigureAwait(true);
            return;
        }

        // Called directly, as InitialiseAsync is: take a place in the
        // queue like anything the view raised.
        var previous = _tail;
        var done = new TaskCompletionSource();
        _tail = done.Task;

        try
        {
            await previous.ConfigureAwait(true);

            _depth++;

            try
            {
                await RunGuardedAsync(action).ConfigureAwait(true);
            }
            finally
            {
                _depth--;
            }
        }
        finally
        {
            done.TrySetResult();
        }
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            View.SetBusy(true);
            await action().ConfigureAwait(true);
        }
        catch (Application.Security.PermissionDeniedException ex)
        {
            View.ShowError(ex.Message);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            // A broken business rule is expected traffic, not a crash.
            View.ShowError(ex.Message);
        }
#pragma warning disable CA1031 // Reported, logged and survived: see Report.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Report(ex);
        }
        finally
        {
            View.SetBusy(false);
        }
    }

    /// <summary>
    /// Logs an unexpected failure and tells the operator in words, on the
    /// screen they were using.
    /// </summary>
    /// <remarks>
    /// An exception escaping an operation used to reach the framework's
    /// own "Unhandled exception - Continue or Quit" dialog, or vanish
    /// altogether with the discarded task. Neither is something a cashier
    /// can act on, and the second hides the failure from support too.
    /// </remarks>
    private void Report(Exception ex)
    {
        ErrorHandler.Log(ex);
        View.ShowError(ErrorHandler.Describe(ex));
    }
}

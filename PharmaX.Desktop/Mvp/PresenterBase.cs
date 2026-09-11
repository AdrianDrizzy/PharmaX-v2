namespace PharmaX.Desktop.Mvp;

public abstract class PresenterBase<TView> : IPresenter<TView>
    where TView : class, IView
{
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

    /// Runs an operation with a busy indicator and one error path.
    protected async Task GuardAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            View.SetBusy(true);
            await action();
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
        finally
        {
            View.SetBusy(false);
        }
    }
}

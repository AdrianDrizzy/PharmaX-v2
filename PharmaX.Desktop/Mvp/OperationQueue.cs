namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Runs a screen's asynchronous operations one after another.
/// </summary>
/// <remarks>
/// For the handful of forms that call their services directly rather than
/// through a presenter; PresenterBase does the same for the rest. A form
/// has one container scope and so one DbContext, and two button clicks or
/// a click and a polling timer must not reach it at the same time - that
/// is the "second operation started on this context" crash (DEF-0011).
/// An exception an operation did not handle is reported, not lost with
/// the discarded task.
/// </remarks>
#pragma warning disable CA1711 // It is a queue of operations, and says so; not a collection type.
public sealed class OperationQueue
#pragma warning restore CA1711
{
    private readonly Action<Exception> _report;
    private Task _tail = Task.CompletedTask;

    public OperationQueue(Action<Exception> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
    }

    /// True while an operation is running or waiting to run.
    public bool IsBusy => !_tail.IsCompleted;

    public void Enqueue(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var previous = _tail;
        var done = new TaskCompletionSource();
        _tail = done.Task;

        _ = RunAfterAsync(previous, done, operation);
    }

    /// <summary>
    /// Enqueues only if nothing is running or waiting: for a polling
    /// timer, whose next tick is as good as a queued one.
    /// </summary>
    public void EnqueueIfIdle(Func<Task> operation)
    {
        if (!IsBusy) Enqueue(operation);
    }

    private async Task RunAfterAsync(Task previous, TaskCompletionSource done,
                                     Func<Task> operation)
    {
        try
        {
            await previous.ConfigureAwait(true);
            await operation().ConfigureAwait(true);
        }
#pragma warning disable CA1031 // The last place the exception can be seen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _report(ex);
        }
        finally
        {
            done.TrySetResult();
        }
    }
}

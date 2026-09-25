using Microsoft.Extensions.Logging;
using PharmaX.Application.Security;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Desktop;

/// <summary>
/// The last line of defence. An unhandled exception at a till must show
/// something a cashier can act on and leave a log entry support can read,
/// never a stack trace and never a silent close.
/// </summary>
public sealed class ErrorHandler
{
    private static ErrorHandler? _instance;

    private readonly ILogger<ErrorHandler> _log;

    public ErrorHandler(ILogger<ErrorHandler> log)
    {
        _log = log;
        _instance = this;
    }

    public static void Handle(Exception? exception)
    {
        if (exception is null) return;

        Log(exception);

        MessageBox.Show(Describe(exception), Mvp.WindowTitles.Caption,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// Writes the failure, stack and all, where support can read it.
    public static void Log(Exception exception) =>
        _instance?._log.LogError(exception, "Unhandled exception.");

    /// <summary>
    /// What the operator is told: the failure's own words where they are
    /// meant for them, otherwise a plain sentence and where to look.
    /// </summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // A broken rule or a refusal that reached this far is still
            // the user's business, and its own message is the useful one.
            DomainException or PermissionDeniedException => exception.Message,

            // Saved by someone else at another till since this screen
            // read it: saying so is more use than a generic failure.
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                "Someone else changed this record while you had it open. "
                + "Close the screen and open it again to see their change.",

            _ => "Something went wrong and PharmaX could not complete that "
                 + $"action ({exception.GetType().Name}: {exception.Message})."
                 + "\n\nThe details have been written to the log. If it "
                 + "keeps happening, quote the time to support."
        };
    }
}

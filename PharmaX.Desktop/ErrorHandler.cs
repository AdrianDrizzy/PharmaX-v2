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

        _instance?._log.LogError(exception, "Unhandled exception.");

        // A broken rule or a refusal that reached this far is still the
        // user's business, and its own message is the useful one.
        var message = exception switch
        {
            DomainException or PermissionDeniedException => exception.Message,
            _ => "Something went wrong and PharmaX could not complete that "
                 + "action.\n\nThe details have been written to the log. "
                 + "If it keeps happening, quote the time to support."
        };

        MessageBox.Show(message, "PharmaX", MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}

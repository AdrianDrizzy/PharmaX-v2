using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Login;

public sealed class LoginPresenter : PresenterBase<ILoginView>
{
    private readonly IAuthenticationService _auth;

    private readonly Application.Abstractions.IServerClock? _server;
    private readonly Application.Abstractions.IClockCorrection? _correction;

    public LoginPresenter(IAuthenticationService auth,
        Application.Abstractions.IServerClock? server = null,
        Application.Abstractions.IClockCorrection? correction = null)
    {
        _auth = auth;
        _server = server;
        _correction = correction;
    }

    protected override void Subscribe()
    {
        View.LoginRequested += (_, _) => Enqueue(OnLoginAsync);
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    private Task OnLoginAsync() => GuardAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(View.Username))
        {
            View.ShowError("Enter your username.");
            return;
        }

        // A wrong clock puts every sale on the wrong day, so it is caught
        // before anyone is signed in.
        if (await ClockProblemAsync() is string clock)
        {
            View.ShowError(clock);
            return;
        }

        // Local credential check, so this works with no host (SEC-002).
        var result = await _auth.AuthenticateAsync(
            View.Username, View.Password);

        if (result.IsFailure)
        {
            View.ClearPassword();
            View.ShowError(result.Error.Message);
            return;
        }

        if (result.Value.MustChangePassword)
        {
            // A seeded credential must not survive into live trading.
            var changed = View.PromptPasswordChange(result.Value.Username);

            if (!changed)
            {
                View.ShowError(
                    "You must change your password before continuing.");
                return;
            }
        }

        View.CloseView(true);
    });

    /// <summary>
    /// Compares this computer with the database server and, when they
    /// agree, takes the server's time from here on. No server to ask, or
    /// one out of reach, signs in as before: working offline is allowed.
    /// </summary>
    private async Task<string?> ClockProblemAsync()
    {
        if (_server is null) return null;

        // Windows' time zone is cached for the life of the program; a zone
        // fixed after the first refusal must be seen on the next try.
        TimeZoneInfo.ClearCachedData();

        Application.Abstractions.ServerTime? server;
        try
        {
            // An offline till should not wait the full connect timeout.
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            server = await _server.ReadAsync(quick.Token);
        }
#pragma warning disable CA1031 // any failure to reach the server means "skip the check"
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }

        if (server is null) return null;

        var pcUtc = DateTime.UtcNow;
        if (ClockCheck.Problem(pcUtc, TimeZoneInfo.Local.GetUtcOffset(pcUtc), server)
            is string problem)
            return problem;

        _correction?.Correct(server.UtcNow - pcUtc);
        return null;
    }
}

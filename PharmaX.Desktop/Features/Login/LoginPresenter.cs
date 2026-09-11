using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Login;

public sealed class LoginPresenter : PresenterBase<ILoginView>
{
    private readonly IAuthenticationService _auth;

    public LoginPresenter(IAuthenticationService auth) => _auth = auth;

    protected override void Subscribe()
    {
        View.LoginRequested += (_, _) => _ = OnLoginAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    private Task OnLoginAsync() => GuardAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(View.Username))
        {
            View.ShowError("Enter your username.");
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
}

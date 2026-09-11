namespace PharmaX.Desktop.Features.Login;

public interface ILoginView : Mvp.IView
{
    string Username { get; }
    string Password { get; }
    void ClearPassword();

    /// Shown after a successful authentication where the credential is
    /// still the seeded one. Returns true only if it was actually
    /// changed (specification Section 30.3).
    bool PromptPasswordChange(string username);

    event EventHandler? LoginRequested;
    event EventHandler? CancelRequested;
}

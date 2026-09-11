using PharmaX.Application.Security;
using PharmaX.Application.Security.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

public interface IUserListView : IView
{
    Guid? SelectedId { get; }
    bool IncludeInactive { get; }

    void ShowUsers(IReadOnlyList<UserListItemDto> users);

    /// Returns the new password, or null if cancelled.
    string? PromptNewPassword(string username);

    event EventHandler? RefreshRequested;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? ResetPasswordRequested;
    event EventHandler? UnlockRequested;
    event EventHandler? ToggleActiveRequested;
}

public interface IUserEditView : IView
{
    Guid? UserId { get; }

    void ShowRoles(IReadOnlyList<string> roles);
    void ShowUser(UserDto user);
    UserEditDto Collect();

    /// Only asked for on a new account; an existing one changes its
    /// password through the reset action, which audits it separately.
    string? CollectInitialPassword();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

public sealed class UserListPresenter : PresenterBase<IUserListView>
{
    private readonly IUserService _users;
    private readonly ViewFactory _views;

    private IReadOnlyList<UserListItemDto> _shown = Array.Empty<UserListItemDto>();

    public UserListPresenter(IUserService users, ViewFactory views)
    {
        _users = users;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OpenEditorAsync(null);
        View.EditRequested += (_, _) => _ = OpenEditorAsync(View.SelectedId);
        View.ResetPasswordRequested += (_, _) => _ = ResetAsync();
        View.UnlockRequested += (_, _) => _ = UnlockAsync();
        View.ToggleActiveRequested += (_, _) => _ = ToggleActiveAsync();
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _users.ListAsync(View.IncludeInactive);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _shown = result.Value;
        View.ShowUsers(_shown);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmUserEdit>();
        editor.LoadUser(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task ResetAsync() => GuardAsync(async () =>
    {
        if (Selected() is not UserListItemDto user) return;

        var password = View.PromptNewPassword(user.Username);
        if (string.IsNullOrWhiteSpace(password)) return;

        var result = await _users.ResetPasswordAsync(user.Id, password);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo($"{user.Username} must choose a new password at "
                      + "their next sign-in.");

        await LoadAsync();
    });

    private Task UnlockAsync() => GuardAsync(async () =>
    {
        if (Selected() is not UserListItemDto user) return;

        if (!user.IsLockedOut)
        {
            View.ShowInfo($"{user.Username} is not locked out.");
            return;
        }

        var result = await _users.UnlockAsync(user.Id);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });

    private Task ToggleActiveAsync() => GuardAsync(async () =>
    {
        if (Selected() is not UserListItemDto user) return;

        var activating = !user.IsActive;

        if (!activating && !View.Confirm(
                $"Deactivate {user.Username}? They keep their history "
                + "and can be reactivated, but cannot sign in."))
            return;

        var result = await _users.SetActiveAsync(user.Id, activating);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });

    private UserListItemDto? Selected()
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a user first.");
            return null;
        }

        return _shown.FirstOrDefault(u => u.Id == id);
    }
}

public sealed class UserEditPresenter : PresenterBase<IUserEditView>
{
    private readonly IUserService _users;

    public UserEditPresenter(IUserService users) => _users = users;

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => _ = SaveAsync();
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        View.ShowRoles(_users.AvailableRoles());

        if (View.UserId is not Guid id) return;    // adding a new one

        var result = await _users.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowUser(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        if (View.UserId is Guid id)
        {
            var updated = await _users.UpdateAsync(id, edit);

            if (updated.IsFailure)
            {
                View.ShowError(updated.Error.Message);
                return;
            }

            View.CloseView(true);
            return;
        }

        var password = View.CollectInitialPassword();

        if (string.IsNullOrWhiteSpace(password))
        {
            View.ShowError("A new account needs a starting password.");
            return;
        }

        var created = await _users.CreateAsync(edit, password);

        if (created.IsFailure)
        {
            View.ShowError(created.Error.Message);
            return;
        }

        View.ShowInfo("Account created. It will ask for a new password at "
                      + "the first sign-in.");

        View.CloseView(true);
    });
}

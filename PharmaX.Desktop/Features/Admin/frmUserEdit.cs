using System.Globalization;
using PharmaX.Application.Security.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

public partial class frmUserEdit : PhxFormBase, IUserEditView
{
    private readonly UserEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.UserRoleAssign;

    /// Designer only. Never called at runtime.
    public frmUserEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        // A registration number only means anything for a pharmacist, so
        // the field appears when that role is ticked.
        lstRoles.ItemCheck += (_, _) => BeginInvoke(UpdateRegistrationField);
    }

    /// Runtime. Resolved by the container.
    public frmUserEdit(UserEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public Guid? UserId { get; private set; }

    /// <summary>
    /// Called by the list screen before ShowDialog. Null means a new
    /// account. Set before OnLoad so the presenter can read it.
    /// </summary>
    public void LoadUser(Guid? id)
    {
        UserId = id;
        Text = id is null ? "PharmaX - New user" : "PharmaX - Edit user";

        // The username is on every audit row the account ever wrote, so
        // it is set once and never changed.
        txtUsername.ReadOnly = id is not null;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowRoles(IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        lstRoles.Items.Clear();

        foreach (var role in roles) lstRoles.Items.Add(role);
    }

    public void ShowUser(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);

        txtUsername.Text = user.Username;
        txtDisplayName.Text = user.DisplayName;
        txtDiscountLimit.Amount = user.DiscountLimitPercent;
        txtRegistration.Text = user.RegistrationNumber ?? "";

        for (var i = 0; i < lstRoles.Items.Count; i++)
        {
            var role = lstRoles.Items[i]?.ToString() ?? "";
            lstRoles.SetItemChecked(i,
                user.Roles.Contains(role, StringComparer.Ordinal));
        }

        lblState.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}{1}{2}  |  failed attempts {3}  |  last sign-in {4}",
            user.IsActive ? "Active" : "Deactivated",
            user.IsLockedOut ? "  |  LOCKED OUT" : "",
            user.MustChangePassword ? "  |  must change password" : "",
            user.FailedAttempts,
            user.LastLoginAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm",
                CultureInfo.CurrentCulture) ?? "never");

        UpdateRegistrationField();
    }

    public UserEditDto Collect() => new(
        Username: txtUsername.Text.Trim(),
        DisplayName: txtDisplayName.Text.Trim(),
        Roles: lstRoles.CheckedItems.Cast<object>()
                       .Select(i => i.ToString() ?? "")
                       .ToList(),
        DiscountLimitPercent: txtDiscountLimit.Amount ?? 0m,
        RegistrationNumber: string.IsNullOrWhiteSpace(txtRegistration.Text)
            ? null
            : txtRegistration.Text.Trim());

    public string? CollectInitialPassword() =>
        frmPasswordPrompt.Ask(this, "Starting password",
            "Starting password for this account (at least "
            + $"{Application.Security.PasswordPolicy.MinimumLength} characters). "
            + "They will have to change it at their first sign-in.");

    private void UpdateRegistrationField()
    {
        var isPharmacist = lstRoles.CheckedItems.Cast<object>()
            .Any(i => string.Equals(i.ToString(),
                Domain.Security.Role.Standard.Pharmacist,
                StringComparison.Ordinal));

        lblRegistration.Visible = isPharmacist;
        txtRegistration.Visible = isPharmacist;
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

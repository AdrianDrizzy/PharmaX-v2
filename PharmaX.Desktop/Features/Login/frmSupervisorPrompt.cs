using PharmaX.Desktop.Mvp;
using PharmaX.Application.Security;

namespace PharmaX.Desktop.Features.Login;

/// <summary>
/// One small dialog serves both supervisor authorisation and pharmacist
/// authorisation, because in both cases what is needed is a second
/// person's own credentials, never a shared code (SEC-006).
/// </summary>
public partial class frmSupervisorPrompt : Form
{
    private readonly IAuthenticationService _auth;
    private readonly string _requiredRole;

    public Guid? AuthoriserId { get; private set; }

    public frmSupervisorPrompt(IAuthenticationService auth, string reason,
                               string requiredRole = "Manager",
                               string? username = null)
    {
        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        InitializeComponent();

        // A plain Form rather than PhxFormBase, so branded here.
        Text = WindowTitles.Brand(Text);
        _auth = auth;
        _requiredRole = requiredRole;
        lblReason.Text = reason;

        // One person's own sign-in, as unlocking their till is.
        if (username is not null)
        {
            txtUsername.Text = username;
            txtUsername.ReadOnly = true;
            ActiveControl = txtPassword;
        }

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        btnOk.Click += async (_, _) => await AuthenticateAsync();
    }

    private async Task AuthenticateAsync()
    {
        // AuthoriseAsync, not AuthenticateAsync: verifying the second
        // person must not sign the first one out of their own session.
        var result = await _auth.AuthoriseAsync(
            txtUsername.Text.Trim(), txtPassword.Text, PermissionForRole());

        if (result.IsFailure)
        {
            txtPassword.Clear();
            lblError.Text = result.Error.Message;
            txtPassword.Focus();
            return;
        }

        AuthoriserId = result.Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// The permission the authoriser must actually hold. Checking the
    /// permission rather than the role name means a site that renames
    /// its roles does not silently lose the control.
    /// </summary>
    private string PermissionForRole() => _requiredRole switch
    {
        "Pharmacist" => Domain.Security.PermissionCode.ScriptAuthorise,
        "Cashier" => Domain.Security.PermissionCode.SaleCreate,
        _ => Domain.Security.PermissionCode.SaleDiscountAboveLimit
    };
}

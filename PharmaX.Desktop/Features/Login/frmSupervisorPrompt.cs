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
    private readonly string _permission;

    public Guid? AuthoriserId { get; private set; }

    /// <param name="permission">
    /// The permission the authoriser must hold: the one for what is being
    /// authorised - a refund asks for the refund permission. Checking the
    /// permission rather than a role name means a site that renames its
    /// roles does not silently lose the control. Every prompt used to ask
    /// for the discount permission, whatever it was authorising, which
    /// only a manager held: a pharmacist could not authorise a refund.
    /// </param>
    public frmSupervisorPrompt(IAuthenticationService auth, string reason,
                               string permission = Domain.Security.PermissionCode.SaleDiscountAboveLimit,
                               string? username = null)
    {
        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        InitializeComponent();

        // A plain Form rather than PhxFormBase, so branded here.
        Text = WindowTitles.Brand(Text);
        _auth = auth;
        _permission = permission;
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
            txtUsername.Text.Trim(), txtPassword.Text, _permission);

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
}

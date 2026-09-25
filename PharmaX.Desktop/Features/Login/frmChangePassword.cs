using PharmaX.Desktop.Mvp;
using PharmaX.Application.Security;

namespace PharmaX.Desktop.Features.Login;

/// <summary>
/// Forces a password change before the shell opens. Shown for the seeded
/// administrator and for anyone an administrator has flagged, because a
/// known default credential on a pharmacy till is exactly the kind of
/// finding that turns a good system into an audit problem.
/// </summary>
public partial class frmChangePassword : Form
{
    private readonly IAuthenticationService _auth;
    private readonly string _username;

    public frmChangePassword(IAuthenticationService auth, string username,
                             string currentPassword)
    {
        InitializeComponent();

        // A plain Form rather than PhxFormBase, so branded here.
        Text = WindowTitles.Brand(Text);

        _auth = auth;
        _username = username;

        // Reached two ways: forced after a sign-in that must change, and
        // chosen from File. The wording has to be true in both cases.
        lblPrompt.Text = currentPassword.Length > 0
            ? $"{username}, choose a new password before continuing."
            : $"{username}, type your current password and choose a new one.";

        // Carried through from the sign-in that just succeeded, so the
        // operator does not type the password they have already proved
        // they know a second time.
        txtCurrent.Text = currentPassword;
        txtCurrent.ReadOnly = currentPassword.Length > 0;

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        btnOk.Click += async (_, _) => await ChangeAsync();
    }

    private async Task ChangeAsync()
    {
        lblError.Text = "";

        if (txtNew.Text != txtConfirm.Text)
        {
            lblError.Text = "The two new passwords do not match.";
            return;
        }

        var result = await _auth.ChangePasswordAsync(
            _username, txtCurrent.Text, txtNew.Text);

        if (result.IsFailure)
        {
            lblError.Text = result.Error.Message;
            txtNew.Clear();
            txtConfirm.Clear();
            txtNew.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

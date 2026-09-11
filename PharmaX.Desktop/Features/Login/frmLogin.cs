using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Login;

public partial class frmLogin : PhxFormBase, ILoginView
{
    private readonly LoginPresenter? _presenter;
    private readonly IAuthenticationService? _auth;
    private readonly Branding? _branding;

    /// Designer only. Never called at runtime.
    public frmLogin()
    {
        InitializeComponent();
        btnLogin.Click += (_, _) => LoginRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmLogin(LoginPresenter presenter, IAuthenticationService auth,
                    Branding branding) : this()
    {
        _presenter = presenter;
        _auth = auth;
        _branding = branding;
        _presenter.Attach(this);
    }

    private void PaintLogo(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _branding?.Draw(e.Graphics, pnlLogo.ClientRectangle);
    }

    public string Username => txtUsername.Text.Trim();
    public string Password => txtPassword.Text;

    public void ClearPassword()
    {
        txtPassword.Clear();
        txtPassword.Focus();
    }

    public bool PromptPasswordChange(string username)
    {
        if (_auth is null) return false;

        using var dialog = new frmChangePassword(_auth, username,
                                                 Password);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    public event EventHandler? LoginRequested;
    public event EventHandler? CancelRequested;
}

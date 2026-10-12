using PharmaX.Application.Abstractions;
using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Login;

public partial class frmLogin : PhxFormBase, ILoginView
{
    private readonly LoginPresenter? _presenter;
    private readonly IAuthenticationService? _auth;
    private readonly Branding? _branding;
    private readonly PharmacyProfile? _pharmacy;

    /// Designer only. Never called at runtime.
    public frmLogin()
    {
        InitializeComponent();
        btnLogin.Click += (_, _) => LoginRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmLogin(LoginPresenter presenter, IAuthenticationService auth,
                    Branding branding, PharmacyProfile pharmacy) : this()
    {
        _presenter = presenter;
        _auth = auth;
        _branding = branding;
        _pharmacy = pharmacy;
        ShowPharmacy();
        _presenter.Attach(this);
    }

    /// <summary>
    /// Heads the window with the pharmacy rather than the product: its
    /// logo, its trading name, and its address and VAT number.
    /// </summary>
    /// <remarks>
    /// This is the first screen anyone sees and every user sees it, so it
    /// reads the same details the receipts print. The logo panel is
    /// hidden when there is no logo, and the layout closes up around it.
    /// </remarks>
    private void ShowPharmacy()
    {
        pnlLogo.Visible = _branding?.HasLogo ?? false;

        if (_pharmacy is null) return;

        var details = _pharmacy.Current;

        lblTitle.Text = PharmacyHeading.Name(details, "Sign in");
        lblParticulars.Text = PharmacyHeading.Particulars(details);
        lblParticulars.Visible = lblParticulars.Text.Length > 0;
    }

    protected override void OnBrandChanged() => ShowPharmacy();

    /// The logo is already the first thing on this window.
    protected override bool ShowsWatermark => false;

    /// <remarks>
    /// White rather than the theme's surface: the logo comes on a white
    /// ground, and on the tinted surface it showed as a pasted-on box.
    /// </remarks>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        BackColor = PhxTheme.Card;
    }

    /// <summary>
    /// No function keys written on the sign-in buttons.
    /// </summary>
    /// <remarks>
    /// The window has two buttons and two keys everybody already knows:
    /// Enter signs in and Escape cancels, as they do on every sign-in
    /// window. "F6 Log in" and "Esc Cancel" taught nobody anything and
    /// made the first screen of the day read like a keyboard chart.
    /// </remarks>
    protected override bool AssignsFunctionKeys => false;

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

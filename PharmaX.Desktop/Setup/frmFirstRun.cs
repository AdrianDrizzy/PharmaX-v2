using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Primitives;

namespace PharmaX.Desktop.Setup;

/// <summary>
/// The one screen somebody sees on a machine PharmaX has never run on.
/// </summary>
/// <remarks>
/// Plain WinForms, no presenter and no container: this runs before the
/// host is built, because what it collects is what the host needs in
/// order to be built at all.
///
/// One page rather than a five-step wizard. Somebody setting up three
/// computers in an afternoon wants to see the whole question at once and
/// press one button, not to click Next twelve times; and the answers are
/// short enough to fit.
/// </remarks>
public sealed partial class frmFirstRun : Form
{
    private WorkstationProfile _profile;
    private bool _connectionProven;

    public frmFirstRun()
    {
        Icon = PhxIcons.AppIcon;
        InitializeComponent();
        PhxTheme.Apply(this);

        // An existing profile means the operator has asked to run setup
        // again, so start from what is already there rather than making
        // them retype it. A new machine gets a new identity, generated
        // here and never anywhere else.
        // Where the installer set this machine up as the main computer,
        // its database answers are already known: fill them in and test
        // them, so all that is left is the pharmacy's name.
        var installed = ServerSetup.Read();

        _profile = WorkstationProvisioning.Read()
            ?? (installed is null
                ? new WorkstationProfile()
                : new WorkstationProfile
                {
                    Kind = WorkstationKind.Server,
                    ServerPort = installed.Port,
                    DatabaseName = installed.Database,
                    DatabaseUser = installed.User
                });

        foreach (var currency in Currencies.All)
            cboCurrency.Items.Add(currency);

        // CurrencyOption.ToString already reads "US Dollar ($)", which
        // is what the list wants to say.

        Fill();

        if (installed is not null && _profile.Kind == WorkstationKind.Server)
        {
            txtPassword.Text = installed.Password;
            Shown += (_, _) => _ = TestAsync();
        }

        radStandalone.CheckedChanged += (_, _) => KindChanged();
        radServer.CheckedChanged += (_, _) => KindChanged();
        radTill.CheckedChanged += (_, _) => KindChanged();

        // Any change to the connection invalidates a test that passed
        // against different details. Without this, an operator could
        // test successfully, correct a typo in the host, and be allowed
        // to finish against a server nobody has reached.
        foreach (Control box in new Control[]
                 { txtHost, txtDatabase, txtUser, txtPassword })
            box.TextChanged += (_, _) => Untested();

        numPort.ValueChanged += (_, _) => Untested();

        btnTest.Click += (_, _) => _ = TestAsync();
        btnFinish.Click += (_, _) => _ = FinishAsync();
        btnQuit.Click += (_, _) => Close();

        KindChanged();
    }

    /// <summary>
    /// The profile the operator settled on. Only meaningful where the
    /// dialog returned OK.
    /// </summary>
    public WorkstationProfile Profile => _profile;

    private void Fill()
    {
        radStandalone.Checked = _profile.Kind == WorkstationKind.Standalone;
        radServer.Checked = _profile.Kind == WorkstationKind.Server;
        radTill.Checked = _profile.Kind == WorkstationKind.Till;

        txtHost.Text = _profile.ServerHost;
        numPort.Value = _profile.ServerPort;
        txtDatabase.Text = _profile.DatabaseName;
        txtUser.Text = _profile.DatabaseUser;

        txtBranchCode.Text = _profile.BranchCode;
        txtTillNumber.Text = _profile.TillNumber;
        txtWorkstationId.Text = _profile.WorkstationId.ToString();

        txtTradingName.Text = _profile.TradingName;
        txtVatNumber.Text = _profile.VatNumber;

        cboCurrency.SelectedItem = Currencies.All.FirstOrDefault(
            c => string.Equals(c.Code, _profile.Currency,
                               StringComparison.OrdinalIgnoreCase))
            ?? Currencies.Default;
    }

    private WorkstationKind Kind =>
        radServer.Checked ? WorkstationKind.Server
        : radTill.Checked ? WorkstationKind.Till
        : WorkstationKind.Standalone;

    /// <summary>
    /// Shows only the questions that apply to the kind of machine chosen.
    /// </summary>
    /// <remarks>
    /// The server is asked for a username and password but not for its
    /// own address: it reaches its database over the loopback, which is
    /// both correct and one fewer thing to get wrong on a network with
    /// no DNS.
    /// </remarks>
    private void KindChanged()
    {
        var shared = Kind != WorkstationKind.Standalone;

        grpDatabase.Enabled = shared;
        lblHost.Enabled = Kind == WorkstationKind.Till;
        txtHost.Enabled = Kind == WorkstationKind.Till;

        grpDatabase.Text = Kind switch
        {
            WorkstationKind.Server =>
                "Database - on this computer",
            WorkstationKind.Till =>
                "Database - on the pharmacy's main computer",
            _ => "Database - not needed for a single computer"
        };

        if (Kind == WorkstationKind.Server && txtHost.Text.Length == 0)
            txtHost.Text = "127.0.0.1";

        Untested();
    }

    private void Untested()
    {
        _connectionProven = false;
        lblTestResult.Text = "";
    }

    private string Host => Kind == WorkstationKind.Server
        ? "127.0.0.1"
        : txtHost.Text.Trim();

    private async Task TestAsync()
    {
        if (!ValidateDatabaseFields()) return;

        btnTest.Enabled = false;
        lblTestResult.ForeColor = PhxTheme.Muted;
        lblTestResult.Text = "Trying...";
        UseWaitCursor = true;

        try
        {
            var result = await ConnectionTest.TryAsync(Host,
                (int)numPort.Value, txtDatabase.Text.Trim(),
                txtUser.Text.Trim(), txtPassword.Text);

            Report(result);

            if (result.Succeeded)
            {
                _connectionProven = true;

                _profile = _profile with
                {
                    ServerVersion = result.ServerVersion
                };
            }
        }
        finally
        {
            btnTest.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void Report(ConnectionTestResult result)
    {
        lblTestResult.Text = result.Message;

        lblTestResult.ForeColor = result.Succeeded
            ? PhxTheme.Title
            : PhxTheme.ProblemText;
    }

    private bool ValidateDatabaseFields()
    {
        if (Kind == WorkstationKind.Till && txtHost.Text.Trim().Length == 0)
        {
            Complain("Type the name or address of the pharmacy's main "
                     + "computer.", txtHost);
            return false;
        }

        if (txtDatabase.Text.Trim().Length == 0)
        {
            Complain("The database needs a name.", txtDatabase);
            return false;
        }

        if (txtUser.Text.Trim().Length == 0)
        {
            Complain("The database needs a username.", txtUser);
            return false;
        }

        return true;
    }

    private async Task FinishAsync()
    {
        if (txtTradingName.Text.Trim().Length == 0)
        {
            Complain("The pharmacy needs a name. It goes on every receipt.",
                     txtTradingName);
            return;
        }

        if (txtTillNumber.Text.Trim().Length == 0)
        {
            Complain("This workstation needs a till number.", txtTillNumber);
            return;
        }

        if (Kind != WorkstationKind.Standalone)
        {
            if (!ValidateDatabaseFields()) return;

            // Refused rather than attempted. Writing the profile and
            // letting the application fail on its first query would send
            // the operator to a stack trace instead of to the one button
            // that tells them what is wrong.
            if (!_connectionProven)
            {
                Complain("Test the connection first, so that this "
                         + "computer is set up against a database it can "
                         + "actually reach.", btnTest);
                return;
            }

            if (!await EnsureDatabaseAsync()) return;
        }

        _profile = _profile with
        {
            Kind = Kind,
            ServerHost = Host,
            ServerPort = (int)numPort.Value,
            DatabaseName = txtDatabase.Text.Trim(),
            DatabaseUser = txtUser.Text.Trim(),
            DatabasePassword = txtPassword.Text,
            BranchCode = txtBranchCode.Text.Trim(),
            TillNumber = txtTillNumber.Text.Trim(),
            TradingName = txtTradingName.Text.Trim(),
            VatNumber = txtVatNumber.Text.Trim(),
            Currency = cboCurrency.SelectedItem is CurrencyOption chosen
                ? chosen.Code
                : Currencies.Default.Code
        };

        try
        {
            WorkstationProvisioning.Save(_profile);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this,
                $"PharmaX could not save its settings.\n\n{ex.Message}\n\n"
                + $"It was trying to write {WorkstationProvisioning.FilePath}.",
                "PharmaX", MessageBoxButtons.OK, MessageBoxIcon.Stop);

            return;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "PharmaX could not save its settings because Windows "
                + "refused. Run this setup once as an administrator.",
                "PharmaX", MessageBoxButtons.OK, MessageBoxIcon.Stop);

            return;
        }

        if (Kind == WorkstationKind.Server)
            ShowTillDetails();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// What every till will be asked, said once here so it can be written
    /// down. Ctrl+C on the message copies it.
    /// </summary>
    private void ShowTillDetails() =>
        MessageBox.Show(this,
            "This computer is the pharmacy's main computer. Set up each "
            + "till with these details:\n\n"
            + $"Main computer:\t{Environment.MachineName}\n"
            + $"Port:\t\t{_profile.ServerPort}\n"
            + $"Database:\t{_profile.DatabaseName}\n"
            + $"Username:\t{_profile.DatabaseUser}\n"
            + $"Password:\t{_profile.DatabasePassword}\n\n"
            + "Write them down now: PharmaX does not show them again.",
            "PharmaX - details for the tills",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>
    /// Creates the pharmacy's database where the server has not got it.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed: on a till, a missing database almost
    /// always means the operator has typed the wrong server or the wrong
    /// name, and quietly creating an empty one next to the real one
    /// would hide that until somebody noticed the shop had no stock.
    /// </remarks>
    private async Task<bool> EnsureDatabaseAsync()
    {
        var database = txtDatabase.Text.Trim();

        var check = await ConnectionTest.TryAsync(Host, (int)numPort.Value,
            database, txtUser.Text.Trim(), txtPassword.Text);

        if (!check.Succeeded)
        {
            Report(check);
            return false;
        }

        if (check.DatabaseExists) return true;

        var answer = MessageBox.Show(this,
            $"The server has no database called {database}.\n\n"
            + (Kind == WorkstationKind.Till
                ? "On a till this usually means the wrong computer or the "
                  + "wrong name has been typed. Create it anyway?"
                : "Create it now?"),
            "PharmaX", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return false;

        var created = await ConnectionTest.CreateDatabaseAsync(Host,
            (int)numPort.Value, database, txtUser.Text.Trim(),
            txtPassword.Text);

        Report(created);
        return created.Succeeded;
    }

    private void Complain(string message, Control focus)
    {
        MessageBox.Show(this, message, "PharmaX",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

        focus.Focus();
    }
}

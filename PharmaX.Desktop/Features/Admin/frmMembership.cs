using PharmaX.Application.Licensing;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// The pharmacy's membership: the Pharmacy ID to send when paying, where
/// it stands, and the box the activation code is pasted into.
/// </summary>
public sealed class frmMembership : PhxFormBase
{
    private readonly ILicenceService _licence;
    private readonly TextBox _pharmacyId = new();
    private readonly Label _status = new();
    private readonly TextBox _code = new();
    private readonly Label _result = new();
    private readonly Button _activate = new();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SettingsChange;

    public frmMembership(ILicenceService licence)
    {
        _licence = licence;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 10) };

        body.Controls.Add(new Label
        {
            AutoSize = true, Location = new Point(0, 4),
            Text = "Pharmacy ID (send this when you pay)"
        });
        _pharmacyId.ReadOnly = true;
        _pharmacyId.Font = new Font("Consolas", 11f);
        _pharmacyId.Location = new Point(0, 26);
        _pharmacyId.Size = new Size(420, 26);
        _pharmacyId.AccessibleName = "Pharmacy ID";

        var copy = new Button { Text = "&Copy", Location = new Point(428, 25), Size = new Size(80, 28) };
        copy.Click += (_, _) => Clipboard.SetText(_pharmacyId.Text);

        _status.AutoSize = false;
        _status.Font = PhxTheme.Emphasis;
        _status.Location = new Point(0, 68);
        _status.Size = new Size(508, 44);

        body.Controls.Add(new Label
        {
            AutoSize = true, Location = new Point(0, 118), Text = "&Activation code"
        });
        _code.Multiline = true;
        _code.Font = new Font("Consolas", 10f);
        _code.Location = new Point(0, 140);
        _code.Size = new Size(508, 90);
        _code.AccessibleName = "Activation code";

        _result.AutoSize = false;
        _result.Location = new Point(0, 238);
        _result.Size = new Size(508, 44);

        body.Controls.AddRange(new Control[] { _pharmacyId, copy, _status, _code, _result });
        Controls.Add(body);

        var band = PhxTheme.FooterBand(56);
        _activate.Text = "Ac&tivate";
        _activate.Location = new Point(338, 11);
        _activate.Size = new Size(90, 32);
        _activate.Click += async (_, _) => await ActivateAsync();
        PhxTheme.MakePrimary(_activate);

        var close = new Button
        {
            Text = "Close", DialogResult = DialogResult.Cancel,
            Location = new Point(436, 11), Size = new Size(90, 32)
        };
        band.Controls.Add(_activate);
        band.Controls.Add(close);
        Controls.Add(band);

        AcceptButton = _activate;
        CancelButton = close;
        ClientSize = new Size(556, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Membership";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _pharmacyId.Text = LicenceCode.FormatPharmacyId(await _licence.GetPharmacyIdAsync());
        ShowStatus(await _licence.RefreshAsync());
    }

    private async Task ActivateAsync()
    {
        _activate.Enabled = false;
        try
        {
            var result = await _licence.ActivateAsync(_code.Text);
            _result.Text = result.Message;
            _result.ForeColor = result.Ok ? PhxTheme.Title : PhxTheme.ProblemText;
            if (result.Ok) _code.Clear();
            ShowStatus(await _licence.RefreshAsync());
        }
        finally
        {
            _activate.Enabled = true;
        }
    }

    private void ShowStatus(LicenceSnapshot s) =>
        _status.Text = LicenceState.BannerText(s) ?? (s.IsTrial
            ? $"Trial, ends on {LicenceState.Day(s.ValidUntil)}."
            : $"{s.Plan} membership, active until {LicenceState.Day(s.ValidUntil)}.");
}

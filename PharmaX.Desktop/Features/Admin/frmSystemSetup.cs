using PharmaX.Application.Abstractions;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Primitives;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// What the pharmacy trades in and what it looks like: the currency
/// every figure is written in, and the logo on the shell, the sign-in
/// window and the receipt.
/// </summary>
/// <remarks>
/// Reached from Administration, Section 2.3's System Setup entry. The
/// currency is held in the workstation's own settings because that is
/// the only store a till can write to with the host away; the screen
/// says so, because a pharmacy that sets two tills differently has a
/// real problem and should be told where the setting lives.
/// </remarks>
public sealed class frmSystemSetup : PhxFormBase
{
    private readonly IWorkstationSettings _settings;
    private readonly Branding _branding;
    private readonly ICurrencyChanger _currency;

    private readonly ComboBox _currencies = new();
    private readonly Label _sample = new();
    private readonly Label _drawer = new();
    private readonly Panel _logo = new();
    private readonly Label _logoHint = new();
    private readonly Button _choose = new();
    private readonly Button _remove = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();

    private readonly Panel _body = new();
    private readonly Branding _preview = new();

    private string? _chosenLogo;
    private string? _originalCurrency;
    private bool _logoChanged;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.UserRoleAssign;

    public frmSystemSetup(IWorkstationSettings settings, Branding branding,
                          ICurrencyChanger currency)
    {
        _settings = settings;
        _branding = branding;
        _currency = currency;

        // The fill panel is added before the bands so that docking
        // gives it what is left rather than the whole window. Content
        // then sits at its own coordinates inside it, and the offline
        // banner arriving at run time cannot push a heading in under
        // the header band.
        _body.Dock = DockStyle.Fill;
        _body.Padding = new Padding(24, 10, 24, 10);
        Controls.Add(_body);

        BuildCurrency();
        BuildLogo();
        BuildHeader();
        BuildButtons();

        AcceptButton = _save;
        CancelButton = _cancel;
        ClientSize = new Size(600, 492);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - System setup";
    }

    // ---- Layout ----------------------------------------------------------

    private void BuildHeader()
    {
        var header = PhxTheme.HeaderBand(64);

        header.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8),
            Size = new Size(560, 26),
            Text = "System setup"
        });

        header.Controls.Add(new Label
        {
            AutoSize = false,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Size = new Size(560, 20),
            Text = "This workstation. Set every till in the pharmacy the "
                   + "same."
        });

        Controls.Add(header);
    }

    private void BuildCurrency()
    {
        _body.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Subheading,
            Location = new Point(0, 0),
            Size = new Size(300, 24),
            Text = "Currency"
        });

        _body.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(2, 36),
            Text = "&Trades in"
        });

        _currencies.DropDownStyle = ComboBoxStyle.DropDownList;
        _currencies.Location = new Point(108, 32);
        _currencies.Size = new Size(320, 24);
        _currencies.SelectedIndexChanged += (_, _) => ShowSample();

        foreach (var currency in Currencies.All)
            _currencies.Items.Add(currency);

        _sample.AutoSize = false;
        _sample.Font = PhxTheme.Figure;
        _sample.Location = new Point(108, 66);
        _sample.Size = new Size(440, 24);

        _drawer.AutoSize = false;
        _drawer.ForeColor = PhxTheme.Muted;
        _drawer.Location = new Point(108, 92);
        _drawer.Size = new Size(440, 58);

        _body.Controls.AddRange(new Control[]
            { _currencies, _sample, _drawer });
    }

    private void BuildLogo()
    {
        _body.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Subheading,
            Location = new Point(0, 162),
            Size = new Size(300, 24),
            Text = "Logo"
        });

        _logo.BackColor = Color.White;
        _logo.BorderStyle = BorderStyle.FixedSingle;
        _logo.Location = new Point(0, 194);
        _logo.Size = new Size(220, 96);
        _logo.Paint += (_, e) => PaintPreview(e);

        _logoHint.AutoSize = false;
        _logoHint.ForeColor = PhxTheme.Muted;
        _logoHint.Location = new Point(236, 194);
        _logoHint.Size = new Size(312, 56);
        _logoHint.Text = "Shown on the home screen, the sign-in window and "
                         + "the top of every receipt. PNG, JPG or BMP; a "
                         + "wide image reads best.";

        _choose.Location = new Point(236, 258);
        _choose.Size = new Size(148, 32);
        _choose.Text = "&Choose image...";
        _choose.Click += (_, _) => ChooseLogo();

        _remove.Location = new Point(392, 258);
        _remove.Size = new Size(100, 32);
        _remove.Text = "&Remove";
        _remove.Click += (_, _) => RemoveLogo();

        _body.Controls.AddRange(new Control[]
            { _logo, _logoHint, _choose, _remove });
    }

    private void BuildButtons()
    {
        var band = PhxTheme.FooterBand(56);

        _save.Location = new Point(400, 11);
        _save.Size = new Size(90, 32);
        _save.Text = "&Save";
        _save.Click += async (_, _) => await SaveAsync();
        PhxTheme.MakePrimary(_save);

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(498, 11);
        _cancel.Size = new Size(90, 32);
        _cancel.Text = "Cancel";

        band.Controls.Add(_save);
        band.Controls.Add(_cancel);
        Controls.Add(band);
    }

    // ---- Loading ---------------------------------------------------------

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _originalCurrency = await _settings.GetAsync(
            WorkstationSettingKey.Currency) ?? Format.Currency.Code;

        Select(Currencies.ByCode(_originalCurrency));

        _chosenLogo = _branding.LogoPath;
        _preview.Load(_chosenLogo);
        _logo.Invalidate();
        _remove.Enabled = _chosenLogo is not null;
    }

    private void Select(CurrencyOption currency)
    {
        for (var i = 0; i < _currencies.Items.Count; i++)
        {
            if (_currencies.Items[i] is CurrencyOption option
                && string.Equals(option.Code, currency.Code,
                                 StringComparison.Ordinal))
            {
                _currencies.SelectedIndex = i;
                return;
            }
        }

        _currencies.SelectedIndex = 0;
    }

    private CurrencyOption Chosen =>
        _currencies.SelectedItem as CurrencyOption ?? Currencies.Default;

    /// <summary>
    /// Shows what money will actually look like, and what the drawer
    /// will be counted in.
    /// </summary>
    /// <remarks>
    /// A code in a list tells an operator nothing. Seeing 1 234,56 turn
    /// into $1,234.56 tells them everything, before they commit to it.
    /// </remarks>
    private void ShowSample()
    {
        var currency = Chosen;
        var culture = CurrencyCulture(currency);

        _sample.Text = "Money will read  "
                       + 1234.56m.ToString("C2", culture);

        _drawer.Text = "Cash-up counts: "
                       + string.Join(", ", currency.Denominations
                           .Select(d => d.Key));
    }

    private static System.Globalization.CultureInfo CurrencyCulture(
        CurrencyOption currency)
    {
        System.Globalization.CultureInfo culture;

        try
        {
            culture = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo
                    .GetCultureInfo(currency.Culture).Clone();
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            culture = (System.Globalization.CultureInfo)
                System.Globalization.CultureInfo.InvariantCulture.Clone();
        }

        culture.NumberFormat.CurrencySymbol = currency.Symbol;
        return culture;
    }

    // ---- The logo --------------------------------------------------------

    private void PaintPreview(PaintEventArgs e)
    {
        var box = new Rectangle(6, 6, _logo.Width - 13, _logo.Height - 13);

        if (_preview.HasLogo)
        {
            _preview.Draw(e.Graphics, box);
            return;
        }

        using var brush = new SolidBrush(PhxTheme.Muted);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        e.Graphics.DrawString("No logo", PhxTheme.Body, brush, box, format);
    }

    private void ChooseLogo()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            Title = "Choose the pharmacy logo"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _chosenLogo = dialog.FileName;
        _preview.Load(_chosenLogo);
        _logoChanged = true;
        _remove.Enabled = true;
        _logo.Invalidate();
    }

    private void RemoveLogo()
    {
        _chosenLogo = null;
        _preview.Load(null);
        _logoChanged = true;
        _remove.Enabled = false;
        _logo.Invalidate();
    }

    /// <summary>
    /// Copies the chosen image into PharmaX's own folder and returns the
    /// path to the copy.
    /// </summary>
    private string? StoreLogo()
    {
        if (_chosenLogo is null) return null;

        // Already ours - the operator opened the screen, changed the
        // currency and saved without touching the logo.
        if (!_logoChanged) return _chosenLogo;

        Directory.CreateDirectory(Branding.Folder);

        var destination = Path.Combine(Branding.Folder,
            "logo" + Path.GetExtension(_chosenLogo).ToUpperInvariant()
                         .ToLowerInvariant());

        foreach (var stale in Directory.GetFiles(Branding.Folder, "logo.*"))
        {
            if (!string.Equals(stale, destination,
                               StringComparison.OrdinalIgnoreCase))
                TryDelete(stale);
        }

        File.Copy(_chosenLogo, destination, overwrite: true);
        return destination;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // An old logo still open somewhere is not worth failing a
            // save over; it is overwritten on the next change anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Dispose();
        base.Dispose(disposing);
    }

    // ---- Saving ----------------------------------------------------------

    private async Task SaveAsync()
    {
        var currency = Chosen;

        var changingCurrency = !string.Equals(currency.Code,
            _originalCurrency, StringComparison.OrdinalIgnoreCase);

        if (changingCurrency && !Confirm(
                $"Change the trading currency to {currency.Name}?"
                + Environment.NewLine + Environment.NewLine
                + "Every figure already recorded will be relabelled "
                + $"{currency.Code}. The amounts themselves do not "
                + "change: 100,00 stays 100,00, it is simply now "
                + $"{currency.Symbol}100. This is meant for correcting a "
                + "currency that was set wrong, not for revaluing a "
                + "pharmacy's stock."))
            return;

        try
        {
            SetBusy(true);

            var logo = StoreLogo();

            await _settings.SetAsync(WorkstationSettingKey.LogoPath, logo);
            _branding.Load(logo);

            if (changingCurrency)
            {
                await _settings.SetAsync(WorkstationSettingKey.Currency,
                                         currency.Code);

                // The data side: the code stamped on new amounts, the
                // drawer the till counts, and the code on rows already
                // written.
                await _currency.ApplyAsync(currency);

                // The presentation side, which is this layer's own.
                Format.UseCurrency(currency);
            }
        }
        finally
        {
            SetBusy(false);
        }

        ShowInfo(changingCurrency
            ? $"Saved. PharmaX now trades in {currency.Name}. Close and "
              + "reopen any window that is already showing figures."
            : "Saved.");

        CloseView(true);
    }
}

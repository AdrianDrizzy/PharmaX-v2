using System.Globalization;
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
    private readonly IPharmacySettings _pharmacy;
    private readonly NumericUpDown _markup = new();
    private readonly ComboBox _rounding = new();
    private readonly ComboBox _dispensingRounding = new();
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

    private readonly ComboBox _palettes = new();
    private readonly Label _paletteHint = new();

    private readonly Panel _body = new();
    private readonly Branding _preview = new();

    private string? _chosenLogo;
    private string? _originalCurrency;
    private bool _logoChanged;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SettingsChange;

    public frmSystemSetup(IWorkstationSettings settings, Branding branding,
                          ICurrencyChanger currency, IPharmacySettings pharmacy)
    {
        _settings = settings;
        _pharmacy = pharmacy;
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
        BuildColours();
        BuildPrices();
        BuildHeader();
        BuildButtons();

        AcceptButton = _save;
        CancelButton = _cancel;
        ClientSize = new Size(600, 700);
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
            Text = "For the whole pharmacy: every till, every user."
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

        // The card the logo sits on in the shell, so the preview shows it as
        // every till will: white in the light mode, the theme's card in the
        // others, with the logo made for it.
        _logo.BackColor = PhxTheme.Card;
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

    /// <summary>
    /// The windows' colours: the logo's own by default, or a named palette.
    /// </summary>
    private void BuildColours()
    {
        _body.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Subheading,
            Location = new Point(0, 306),
            Size = new Size(300, 24),
            Text = "Colours"
        });

        _body.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(2, 342),
            Text = "&Windows"
        });

        // Each entry shows its two colours, so the choice is made by eye.
        _palettes.DrawMode = DrawMode.OwnerDrawFixed;
        _palettes.DropDownStyle = ComboBoxStyle.DropDownList;
        _palettes.ItemHeight = 22;
        _palettes.Location = new Point(108, 338);
        _palettes.Name = "cboPalette";
        _palettes.Size = new Size(320, 28);
        _palettes.DrawItem += (_, e) => DrawPalette(e);

        _palettes.Items.Add(LogoChoice());

        foreach (var palette in Palette.Presets)
            _palettes.Items.Add(new PaletteChoice(palette.Id, palette.Name, palette));

        // Grows downward to fit its words at any display scaling; a fixed
        // height lost the second line at 125 per cent.
        _paletteHint.AutoSize = true;
        _paletteHint.ForeColor = PhxTheme.Muted;
        _paletteHint.Location = new Point(108, 374);
        _paletteHint.MaximumSize = new Size(440, 0);
        _paletteHint.Text = "From the logo takes the colours out of the image "
                            + "above, so a new logo brings its own colours.";

        _body.Controls.AddRange(new Control[] { _palettes, _paletteHint });
    }

    /// The pharmacy's pricing rules, the same at every till.
    private void BuildPrices()
    {
        _body.Controls.Add(new Label
        {
            AutoSize = false, Font = PhxTheme.Subheading,
            Location = new Point(0, 430), Size = new Size(300, 24), Text = "Prices"
        });

        _body.Controls.Add(new Label { AutoSize = true, Location = new Point(2, 466), Text = "&Markup %" });
        _markup.DecimalPlaces = 1;
        _markup.Location = new Point(108, 462);
        _markup.Maximum = 1000m;
        _markup.Name = "numMarkup";
        _markup.Size = new Size(90, 24);

        _body.Controls.Add(new Label { AutoSize = true, Location = new Point(220, 466), Text = "R&ound up to" });
        _rounding.DropDownStyle = ComboBoxStyle.DropDownList;
        _rounding.Items.AddRange(new object[] { "0.01", "0.05", "0.10", "0.50", "1.00" });
        _rounding.Location = new Point(340, 462);
        _rounding.Name = "cboRounding";
        _rounding.Size = new Size(90, 24);

        // A cash patient pays the pack price pro rata for the tablets handed
        // over, rounded up to this - a dollar, as the previous system charged.
        _body.Controls.Add(new Label { AutoSize = true, Location = new Point(2, 500), Text = "&Dispensing charges round up to" });
        _dispensingRounding.DropDownStyle = ComboBoxStyle.DropDownList;
        _dispensingRounding.Items.AddRange(new object[] { "0.01", "0.05", "0.10", "0.50", "1.00" });
        _dispensingRounding.Location = new Point(340, 496);
        _dispensingRounding.Name = "cboDispensingRounding";
        _dispensingRounding.Size = new Size(90, 24);

        _body.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = PhxTheme.Muted,
            Location = new Point(2, 530), MaximumSize = new Size(550, 0),
            Text = "An item with no price takes the markup on its first delivery; a dearer "
                   + "delivery keeps its margin. A dispensed part pack is charged pro rata and "
                   + "rounded up: 14 of a 100-pack at 6.66 is 0.93, charged 1.00."
        });

        _body.Controls.AddRange(new Control[] { _markup, _rounding, _dispensingRounding });
    }

    /// <summary>A palette as the list offers it.</summary>
    private sealed record PaletteChoice(string Id, string Name, Palette Colours)
    {
        public override string ToString() => Name;
    }

    /// The logo's own colours, worked out from the image being previewed.
    private PaletteChoice LogoChoice()
    {
        var derived = Palette.FromLogo(_preview.Logo);

        return new PaletteChoice(Palette.FromLogoId,
            derived is null
                ? "From the logo (none chosen: navy and orange)"
                : "From the logo",
            derived ?? Palette.NavyAndOrange);
    }

    private void DrawPalette(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || _palettes.Items[e.Index] is not PaletteChoice choice) return;

        var size = e.Bounds.Height - 8;
        var y = e.Bounds.Y + 4;

        foreach (var (colour, x) in new[]
        {
            (choice.Colours.Primary, e.Bounds.X + 4),
            (choice.Colours.Highlight, e.Bounds.X + 8 + size)
        })
        {
            using var fill = new SolidBrush(colour);
            e.Graphics.FillRectangle(fill, x, y, size, size);
            e.Graphics.DrawRectangle(Pens.White, x, y, size - 1, size - 1);
        }

        var text = new Rectangle(e.Bounds.X + 16 + size * 2, e.Bounds.Y,
                                 e.Bounds.Width - 16 - size * 2, e.Bounds.Height);

        TextRenderer.DrawText(e.Graphics, choice.Name, e.Font ?? Font, text,
            e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        e.DrawFocusRectangle();
    }

    /// <summary>
    /// Works the logo's colours out again after the logo changes, and
    /// chooses them: a new logo is the strongest hint there is of the
    /// colours the pharmacy wants.
    /// </summary>
    private void RefreshLogoChoice()
    {
        _palettes.Items[0] = LogoChoice();
        _palettes.SelectedIndex = 0;
        _palettes.Invalidate();
    }

    private PaletteChoice ChosenPalette =>
        _palettes.SelectedItem as PaletteChoice ?? LogoChoice();

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

        _palettes.Items[0] = LogoChoice();
        SelectPalette(await _pharmacy.GetAsync(PharmacySettingKey.Palette));

        var (markup, step) = await Application.Inventory.Pricing.ReadAsync(_pharmacy);
        _markup.Value = Math.Clamp(markup, _markup.Minimum, _markup.Maximum);
        _rounding.SelectedItem = (step <= 0m ? 0.01m : step).ToString("0.00", CultureInfo.InvariantCulture);
        if (_rounding.SelectedIndex < 0) _rounding.SelectedIndex = 0;

        var dispensingStep = await Application.Dispensing.DispensingCharge.RoundToAsync(_pharmacy);
        _dispensingRounding.SelectedItem = dispensingStep.ToString("0.00", CultureInfo.InvariantCulture);
        if (_dispensingRounding.SelectedIndex < 0) _dispensingRounding.SelectedIndex = _dispensingRounding.Items.Count - 1;

        _currencies.Focus();
    }

    private void SelectPalette(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? Palette.FromLogoId : id;

        for (var i = 0; i < _palettes.Items.Count; i++)
        {
            if (_palettes.Items[i] is PaletteChoice choice
                && string.Equals(choice.Id, id, StringComparison.Ordinal))
            {
                _palettes.SelectedIndex = i;
                return;
            }
        }

        _palettes.SelectedIndex = 0;
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

        // Kept in the database for every till, so it is kept small.
        if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024)
        {
            ShowError("That image is over 2 MB. Choose a smaller one; a logo "
                      + "a few hundred pixels wide is plenty for a receipt.");
            return;
        }

        _chosenLogo = dialog.FileName;
        _preview.Load(_chosenLogo);
        _logoChanged = true;
        _remove.Enabled = true;
        _logo.Invalidate();
        RefreshLogoChoice();
    }

    private void RemoveLogo()
    {
        _chosenLogo = null;
        _preview.Load(null);
        _logoChanged = true;
        _remove.Enabled = false;
        _logo.Invalidate();
        RefreshLogoChoice();
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

        return Branding.Keep(_chosenLogo);
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

        // Written the way the new currency writes it. It said "100,00"
        // whatever was chosen, a decimal comma no dollar or ZiG till uses.
        var sample = Format.Amount(100m, currency);

        if (changingCurrency && !Confirm(
                $"Change the trading currency to {currency.Name}?"
                + Environment.NewLine + Environment.NewLine
                + "Every figure already recorded will be relabelled "
                + $"{currency.Code}. The amounts themselves do not "
                + $"change: {sample} stays {sample}, it is simply now "
                + $"{currency.Symbol}{sample}. This is meant for correcting "
                + "a currency that was set wrong, not for revaluing a "
                + "pharmacy's stock."))
            return;

        try
        {
            SetBusy(true);

            var logo = StoreLogo();

            // The image itself, for every till and every user: a path means
            // nothing on another machine. Blank, not missing, once removed,
            // so no till hands its old logo back to the pharmacy.
            await _pharmacy.SetAsync(PharmacySettingKey.LogoImage,
                                     Branding.Encode(logo) ?? "");
            await _settings.SetAsync(WorkstationSettingKey.LogoPath, null);
            _branding.Load(logo);

            // Into use at once: every open window, the shell behind this
            // one included, swaps to the new colours as Save is pressed.
            var palette = ChosenPalette;
            await _pharmacy.SetAsync(PharmacySettingKey.Palette, palette.Id);

            await _pharmacy.SetAsync(PharmacySettingKey.Markup,
                _markup.Value.ToString(CultureInfo.InvariantCulture));
            await _pharmacy.SetAsync(PharmacySettingKey.Rounding, (string?)_rounding.SelectedItem);
            await _pharmacy.SetAsync(PharmacySettingKey.DispensingRounding,
                (string?)_dispensingRounding.SelectedItem);
            PhxTheme.Use(Palette.Resolve(palette.Id, _branding.Logo));

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

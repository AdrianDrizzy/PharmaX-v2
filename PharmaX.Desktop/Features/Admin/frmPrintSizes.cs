using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Paper sizes and type scale for labels, receipts and claim forms on
/// this computer. Every signed-in user may open it (same as Printer
/// setup). Changes update the live sample as they are made.
/// </summary>
public sealed class frmPrintSizes : PhxFormBase
{
    private readonly IWorkstationSettings _settings;

    private readonly TabControl _tabs = new();
    private readonly SplitContainer _split = new();
    private readonly Panel _settingsHost = new();
    private readonly Panel _previewHost = new();
    private readonly Panel _buttons = new();
    private readonly Label _current = new();
    private readonly ComboBox _preset = new();
    private readonly NumericUpDown _width = new();
    private readonly NumericUpDown _height = new();
    private readonly NumericUpDown _font = new();
    private readonly NumericUpDown _spacing = new();
    private readonly NumericUpDown _padTop = new();
    private readonly NumericUpDown _padRight = new();
    private readonly NumericUpDown _padBottom = new();
    private readonly NumericUpDown _padLeft = new();
    private readonly Label _heightCaption = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private readonly PrintSamplePreview _preview = new();

    private DocumentLayout _label = DocumentLayout.DefaultLabel;
    private DocumentLayout _receipt = DocumentLayout.DefaultReceipt;
    private DocumentLayout _claim = DocumentLayout.DefaultClaim;
    private bool _loading;
    private readonly List<Control> _settingLabels = new();

    public frmPrintSizes(IWorkstationSettings settings)
    {
        _settings = settings;

        var header = PhxTheme.HeaderBand(64);
        header.Dock = DockStyle.Top;
        header.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8),
            Size = new Size(720, 26),
            Text = "Print sizes"
        });
        header.Controls.Add(new Label
        {
            AutoSize = false,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Size = new Size(720, 20),
            Text = "This computer's label, receipt and claim-form sizes. "
                   + "The sample updates as you change them."
        });

        _buttons.Dock = DockStyle.Bottom;
        _buttons.Height = 52;
        _buttons.Padding = new Padding(PhxTheme.Gutter, 10, PhxTheme.Gutter, 10);

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Text = "Cancel";
        _cancel.Size = new Size(88, 32);
        _cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        _save.Text = "&Save";
        _save.Size = new Size(88, 32);
        _save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _save.Click += async (_, _) => await SaveAsync();

        _buttons.Controls.Add(_save);
        _buttons.Controls.Add(_cancel);
        _buttons.Resize += (_, _) =>
        {
            _cancel.Location = new Point(_buttons.ClientSize.Width - 88 - PhxTheme.Gutter, 10);
            _save.Location = new Point(_cancel.Left - 96, 10);
        };

        _tabs.Dock = DockStyle.Top;
        _tabs.Height = 36;
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(110, 28);
        _tabs.TabPages.Add("Labels");
        _tabs.TabPages.Add("Receipts");
        _tabs.TabPages.Add("Claim forms");
        _tabs.SelectedIndexChanged += (_, _) => ShowKind();

        _settingsHost.Dock = DockStyle.Fill;
        _settingsHost.AutoScroll = true;
        _settingsHost.Padding = new Padding(PhxTheme.Gutter, 8, 8, 8);

        BuildSettings();

        _previewHost.Dock = DockStyle.Fill;
        _previewHost.BackColor = Color.FromArgb(245, 245, 245);
        _previewHost.Padding = new Padding(8);
        _previewHost.Controls.Add(_preview);
        _preview.Dock = DockStyle.Fill;

        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Vertical;
        _split.SplitterWidth = 6;
        _split.Panel1MinSize = 280;
        _split.Panel2MinSize = 280;
        _split.Panel1.Controls.Add(_settingsHost);
        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_previewHost);

        Controls.Add(_split);
        Controls.Add(_buttons);
        Controls.Add(header);

        AcceptButton = _save;
        CancelButton = _cancel;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1100, 720);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Print sizes";

        Load += (_, _) =>
        {
            _split.SplitterDistance = Math.Max(320, Math.Min(380, ClientSize.Width / 3));
            _buttons.PerformLayout();
        };
    }

    private void BuildSettings()
    {
        var y = 4;
        AddCaption("Current size", _current, ref y);
        _current.AutoSize = false;
        _current.Height = 22;
        _current.ForeColor = PhxTheme.Ink;

        AddCaption("&Stock / size", _preset, ref y);
        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.SelectedIndexChanged += (_, _) => OnPresetChanged();

        AddCaption("&Width (mm)", _width, ref y);
        ConfigureMm(_width);

        _heightCaption.AutoSize = true;
        _heightCaption.Text = "&Height (mm)";
        _heightCaption.Location = new Point(0, y + 4);
        _settingsHost.Controls.Add(_heightCaption);
        _settingLabels.Add(_heightCaption);
        _height.Location = new Point(140, y);
        _height.Width = 100;
        ConfigureMm(_height);
        _settingsHost.Controls.Add(_height);
        y += PhxTheme.RowHeight;

        AddCaption("&Font size %", _font, ref y);
        ConfigurePercent(_font);

        AddCaption("Line &spacing %", _spacing, ref y);
        ConfigurePercent(_spacing);

        var padTitle = new Label
        {
            AutoSize = true,
            Location = new Point(0, y + 6),
            Text = "Padding (hundredths of an inch)",
            ForeColor = PhxTheme.Muted
        };
        _settingsHost.Controls.Add(padTitle);
        _settingLabels.Add(padTitle);
        y += 28;

        AddPad("&Top", _padTop, ref y);
        AddPad("&Right", _padRight, ref y);
        AddPad("&Bottom", _padBottom, ref y);
        AddPad("&Left", _padLeft, ref y);

        _settingsHost.Resize += (_, _) => LayoutSettingsWidth();
        LayoutSettingsWidth();
    }

    private void AddCaption(string caption, Control control, ref int y)
    {
        var label = new Label
        {
            AutoSize = true,
            Location = new Point(0, y + 4),
            Text = caption
        };
        control.Location = new Point(140, y);
        _settingsHost.Controls.Add(label);
        _settingsHost.Controls.Add(control);
        _settingLabels.Add(label);
        y += PhxTheme.RowHeight;
    }

    private void AddPad(string caption, NumericUpDown box, ref int y)
    {
        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, y + 4),
            Text = caption
        };
        box.Location = new Point(140, y);
        box.Width = 100;
        box.Minimum = 0;
        box.Maximum = 120;
        box.Value = 0;
        box.ValueChanged += (_, _) => ApplyCustomFromFields();
        _settingsHost.Controls.Add(label);
        _settingsHost.Controls.Add(box);
        _settingLabels.Add(label);
        y += PhxTheme.RowHeight;
    }

    private void LayoutSettingsWidth()
    {
        var room = Math.Max(160, _settingsHost.ClientSize.Width - 140 - 24);
        _current.Width = room;
        _preset.Width = room;
        foreach (var box in new[] { _width, _height, _font, _spacing,
                     _padTop, _padRight, _padBottom, _padLeft })
            box.Width = Math.Min(120, room);
    }

    private void ConfigureMm(NumericUpDown box)
    {
        box.DecimalPlaces = 1;
        box.Minimum = 20;
        box.Maximum = 320;
        box.Increment = 0.5m;
        box.Width = 100;
        box.ValueChanged += (_, _) => ApplyCustomFromFields();
    }

    private void ConfigurePercent(NumericUpDown box)
    {
        box.Minimum = 50;
        box.Maximum = 200;
        box.Value = 100;
        box.Width = 100;
        box.ValueChanged += (_, _) => ApplyCustomFromFields();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await PrintLayouts.LoadAsync(_settings);
        _label = PrintLayouts.Label;
        _receipt = PrintLayouts.Receipt;
        _claim = PrintLayouts.Claim;
        ShowKind();
    }

    private DocumentLayout Active
    {
        get => _tabs.SelectedIndex switch
        {
            1 => _receipt,
            2 => _claim,
            _ => _label
        };
        set
        {
            switch (_tabs.SelectedIndex)
            {
                case 1: _receipt = value; break;
                case 2: _claim = value; break;
                default: _label = value; break;
            }
        }
    }

    private IReadOnlyList<(string Id, string Name, DocumentLayout Layout)> Presets =>
        _tabs.SelectedIndex switch
        {
            1 => PrintPresets.Receipts,
            2 => PrintPresets.Claims,
            _ => PrintPresets.Labels
        };

    private void ShowKind()
    {
        _loading = true;
        try
        {
            _preset.Items.Clear();
            foreach (var item in Presets)
                _preset.Items.Add(new PresetChoice(item.Id, item.Name, item.Layout));

            var active = Active;
            var match = _preset.Items.Cast<PresetChoice>()
                .FirstOrDefault(p => p.Id == active.PresetId)
                ?? _preset.Items.Cast<PresetChoice>().First();
            _preset.SelectedItem = match;

            var roll = _tabs.SelectedIndex == 1;
            _height.Visible = !roll;
            _heightCaption.Visible = !roll;

            FillFields(active);
            RefreshPreview();
        }
        finally
        {
            _loading = false;
        }
    }

    private void FillFields(DocumentLayout layout)
    {
        _current.Text = PrintLayouts.Describe(layout)
                        + (layout.PresetId == "custom" ? " (custom)" : "");
        _width.Value = Clamp(_width, layout.WidthMm);
        if (_height.Visible && layout.HeightMm > 0)
            _height.Value = Clamp(_height, layout.HeightMm);
        _font.Value = Math.Clamp(layout.FontScalePercent, 50, 200);
        _spacing.Value = Math.Clamp(layout.LineSpacingPercent, 50, 200);
        SetPad(_padTop, layout.PaddingTop);
        SetPad(_padRight, layout.PaddingRight);
        SetPad(_padBottom, layout.PaddingBottom);
        SetPad(_padLeft, layout.PaddingLeft);
    }

    private static void SetPad(NumericUpDown box, int value) =>
        box.Value = value >= 0 ? Math.Clamp(value, 0, 120) : 0;

    private static decimal Clamp(NumericUpDown box, decimal value) =>
        Math.Min(box.Maximum, Math.Max(box.Minimum, value));

    private void OnPresetChanged()
    {
        if (_loading || _preset.SelectedItem is not PresetChoice choice) return;

        if (choice.Id == "custom")
        {
            ApplyCustomFromFields();
            return;
        }

        Active = choice.Layout with
        {
            FontScalePercent = (int)_font.Value,
            LineSpacingPercent = (int)_spacing.Value,
            PaddingTop = PadOrDefault(_padTop),
            PaddingRight = PadOrDefault(_padRight),
            PaddingBottom = PadOrDefault(_padBottom),
            PaddingLeft = PadOrDefault(_padLeft)
        };
        _loading = true;
        try { FillFields(Active); }
        finally { _loading = false; }
        RefreshPreview();
    }

    private static int PadOrDefault(NumericUpDown box) =>
        box.Value == 0 ? -1 : (int)box.Value;

    private void ApplyCustomFromFields()
    {
        if (_loading) return;

        var height = _height.Visible ? _height.Value : 0m;
        Active = Active.WithCustom(_width.Value, height,
            (int)_font.Value, (int)_spacing.Value,
            PadOrDefault(_padTop), PadOrDefault(_padRight),
            PadOrDefault(_padBottom), PadOrDefault(_padLeft));
        _current.Text = PrintLayouts.Describe(Active) + " (custom)";

        _loading = true;
        try
        {
            var custom = _preset.Items.Cast<PresetChoice>()
                .FirstOrDefault(p => p.Id == "custom");
            if (custom is not null) _preset.SelectedItem = custom;
        }
        finally { _loading = false; }

        RefreshPreview();
    }

    private void RefreshPreview() =>
        _preview.Show(_tabs.SelectedIndex, Active);

    private async Task SaveAsync()
    {
        await PrintLayouts.SaveAsync(_settings, _label, _receipt, _claim);
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record PresetChoice(string Id, string Name, DocumentLayout Layout)
    {
        public override string ToString() => Name;
    }
}

/// <summary>
/// Live full sample of a label, receipt or claim form at the chosen size.
/// </summary>
internal sealed class PrintSamplePreview : Panel
{
    private int _kind;
    private DocumentLayout _layout = DocumentLayout.DefaultLabel;
    private readonly ReceiptPreview _receipt = new();
    private readonly Panel _receiptScroll = new();
    private Bitmap? _page;

    public PrintSamplePreview()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        AutoScroll = true;

        _receiptScroll.Dock = DockStyle.Fill;
        _receiptScroll.AutoScroll = true;
        _receiptScroll.BackColor = Color.White;
        _receiptScroll.Controls.Add(_receipt);
        _receipt.Location = new Point(16, 32);
        Controls.Add(_receiptScroll);
        _receiptScroll.Visible = false;
    }

    public void Show(int kind, DocumentLayout layout)
    {
        _kind = kind;
        _layout = layout;
        _page?.Dispose();
        _page = null;

        if (kind == 1)
        {
            ShowFullReceipt();
            _receiptScroll.Visible = true;
            Invalidate();
            return;
        }

        _receiptScroll.Visible = false;
        _page = kind == 2 ? RenderClaim() : RenderLabel();
        Invalidate();
    }

    private void ShowFullReceipt()
    {
        var sale = frmReceiptTemplate.SampleSale();
        var header = new ReceiptHeaderDto(
            "Bethrapha Pharmacy", "Bethrapha Pharmacy (Pvt) Ltd",
            "28 Woodstock Road\nThorngrove, Bulawayo",
            "4123456789", "", "Branch 1 Till 1", "cashier",
            DateTime.Now, TillNumber: "1", CurrencyCode: "USD",
            Telephone: "029-123456", WhatsApp: "0771234567");

        // Apply draft width so the fitted type matches the roll under edit.
        var previous = PrintLayouts.Receipt;
        try
        {
            PrintLayouts.Receipt = _layout;
            using var document = new ReceiptDocument(sale, header);
            _receipt.Show(document.Rows(), ReceiptDocument.CharactersPerLine);
        }
        finally
        {
            PrintLayouts.Receipt = previous;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_kind == 1) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);

        var caption = _kind == 2 ? "Sample claim form (page 1)" : "Sample label";
        using var title = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        TextRenderer.DrawText(g, caption, title,
            new Point(8 - HorizontalScroll.Value, 6 - VerticalScroll.Value),
            Color.DimGray);

        if (_page is null) return;

        var area = new Rectangle(
            12 - HorizontalScroll.Value,
            28 - VerticalScroll.Value,
            ClientSize.Width - 24,
            ClientSize.Height - 40);

        // Letterbox: fit without distorting aspect ratio.
        var scale = Math.Min(
            (float)area.Width / _page.Width,
            (float)area.Height / _page.Height);
        scale = Math.Min(scale, 1.25f);
        var w = Math.Max(1, (int)(_page.Width * scale));
        var h = Math.Max(1, (int)(_page.Height * scale));
        var box = new Rectangle(
            area.Left + (area.Width - w) / 2,
            area.Top + Math.Max(0, (area.Height - h) / 2),
            w, h);

        using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
            g.FillRectangle(shadow, box.X + 3, box.Y + 3, box.Width, box.Height);
        g.FillRectangle(Brushes.White, box);
        g.DrawRectangle(Pens.Gray, box);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_page, box);

        AutoScrollMinSize = new Size(
            Math.Max(ClientSize.Width, box.Right + HorizontalScroll.Value + 20),
            Math.Max(ClientSize.Height, box.Bottom + VerticalScroll.Value + 20));
    }

    private Bitmap RenderLabel()
    {
        var stockW = Math.Max(1, _layout.WidthHundredths > 0
            ? _layout.WidthHundredths : LabelDocument.WidthHundredths);
        var stockH = Math.Max(1, _layout.HeightHundredths > 0
            ? _layout.HeightHundredths : LabelDocument.HeightHundredths);

        // Draw at a readable pixel size while keeping aspect.
        var pxW = Math.Max(stockW * 2, 400);
        var pxH = Math.Max((int)(pxW * (stockH / (double)stockW)), 200);

        var bmp = new Bitmap(pxW, pxH);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Display;

        using var doc = new LabelDocument(SampleLabel(),
            new PharmacyImprint("Bethrapha Pharmacy",
                "28 Woodstock Road, Thorngrove, Bulawayo", "029-123456"),
            layout: _layout);
        doc.RenderPage(g, new Rectangle(0, 0, pxW, pxH));
        return bmp;
    }

    private Bitmap RenderClaim()
    {
        var width = Math.Max(1, _layout.WidthHundredths > 0
            ? _layout.WidthHundredths : 827);
        var height = Math.Max(1, _layout.HeightHundredths > 0
            ? _layout.HeightHundredths : 1169);
        var margins = _layout.HasCustomPadding
            ? _layout.ToMargins(40)
            : new Margins(40, 40, 35, 35);
        var bounds = new Rectangle(
            margins.Left, margins.Top,
            Math.Max(40, width - margins.Left - margins.Right),
            Math.Max(40, height - margins.Top - margins.Bottom));

        var previous = PrintLayouts.Claim;
        var bmp = new Bitmap(width, height);
        try
        {
            PrintLayouts.Claim = _layout;
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var document = new ClaimFormDocument(SampleClaim());
            document.RenderPage(g, bounds, 0);
        }
        finally
        {
            PrintLayouts.Claim = previous;
        }

        return bmp;
    }

    private static LabelDto SampleLabel() => new(
        PatientName: "Moyo, Thandi",
        ProductDescription: "Amoxicillin 500mg Caps",
        Quantity: 21m,
        Directions: "Take one capsule three times daily after food",
        BatchNumber: "A2104",
        Expiry: new DateOnly(2027, 8, 31),
        PharmacistName: "Dr Sample",
        ScriptNumber: "SC-001-000123",
        DispensedOn: DateOnly.FromDateTime(DateTime.Today),
        Cautions: new[] { "Complete the course", "Keep out of reach of children" },
        PrescriberName: "Dr Ncube",
        PrescriberNumber: "MP123456",
        Price: 18.50m,
        SchemeName: "Sample Aid");

    private static ClaimFormDto SampleClaim() => new(
        ClaimNumber: "CLM-001-000007",
        ClaimedOn: DateOnly.FromDateTime(DateTime.Today),
        PharmacyName: "Bethrapha Pharmacy",
        PharmacyAddress: "28 Woodstock Road\nThorngrove, Bulawayo",
        PharmacyNumber: "P00123",
        VatNumber: "4123456789",
        SchemeName: "Sample Medical Aid",
        PatientName: "Thandi Moyo",
        MemberSurname: "Moyo",
        MemberInitials: "T",
        MembershipNumber: "MEM-0042",
        DependantCode: "00",
        DateOfBirth: new DateOnly(1988, 3, 14),
        Telephone: "0771234567",
        ScriptNumber: "SC-001-000123",
        DoctorName: "Dr Ncube",
        DoctorNumber: "MP123456",
        DispensingNumber: "DSP-001-000021",
        DispensedOn: DateOnly.FromDateTime(DateTime.Today),
        IsRepeat: false,
        RepeatsLeft: 2,
        Lines: new[]
        {
            new ClaimFormLineDto(1, "Amoxicillin 500mg capsules", "AMOX50",
                21m, 180m, 150m, "One capsule three times a day after food"),
            new ClaimFormLineDto(2, "Paracetamol 500mg tablets", "PARA500",
                20m, 12.50m, 10m, "One or two tablets when necessary"),
            new ClaimFormLineDto(3, "Vitamin C 1000mg tablets", "VITC1000",
                30m, 45m, 36m, "One tablet daily")
        },
        Gross: 237.50m,
        Claimed: 196m,
        CoPayment: 41.50m,
        CurrencySymbol: "$");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _page?.Dispose();
            _receipt.Dispose();
        }

        base.Dispose(disposing);
    }
}

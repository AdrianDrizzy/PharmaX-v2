using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// Absolute X for setting labels: Panel.Padding does not shift
    /// hand-placed controls, so the gutter has to be in the coordinate.
    private const int FieldLeft = 16;
    private const int FieldValueLeft = 168;

    private readonly TabControl _tabs = new();
    private readonly TableLayoutPanel _body = new();
    private readonly Panel _left = new();
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
    private readonly Button _printPreview = new();
    private readonly Button _reset = new();
    private readonly PrintSamplePreview _preview = new();
    private readonly System.Windows.Forms.Timer _claimDebounce =
        new() { Interval = 200 };

    private DocumentLayout _label = DocumentLayout.DefaultLabel;
    private DocumentLayout _receipt = DocumentLayout.DefaultReceipt;
    private DocumentLayout _claim = DocumentLayout.DefaultClaim;
    private bool _loading;
    private readonly List<Control> _settingLabels = new();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PrintSizes;

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
            Size = new Size(820, 20),
            Text = "This computer's label, receipt and claim-form sizes. "
                   + "The sample updates as you change them."
        });

        // Flow layout: FunctionKeys rewrites captions to "F6 Save" etc.,
        // so fixed Width + absolute X left the right-hand buttons clipped.
        _buttons.Dock = DockStyle.Bottom;
        _buttons.Height = 56;
        _buttons.Padding = new Padding(PhxTheme.Gutter, 10, PhxTheme.Gutter, 10);

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.AutoSize = true;
        _cancel.Text = "Cancel";
        _cancel.Margin = new Padding(8, 0, 0, 0);

        _save.AutoSize = true;
        _save.Text = "&Save";
        _save.Margin = new Padding(8, 0, 0, 0);
        _save.Click += async (_, _) => await SaveAsync();

        _printPreview.AutoSize = true;
        _printPreview.Text = "&Print preview";
        _printPreview.Margin = new Padding(8, 0, 0, 0);
        _printPreview.Click += (_, _) => Operations.Enqueue(PrintSampleAsync);

        _reset.AutoSize = true;
        _reset.Text = "&Reset to default";
        _reset.Margin = new Padding(0);
        _reset.Click += (_, _) => ResetToDefault();

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        left.Controls.Add(_reset);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        right.Controls.Add(_printPreview);
        right.Controls.Add(_save);
        right.Controls.Add(_cancel);

        _buttons.Controls.Add(right);
        _buttons.Controls.Add(left);

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
        // Padding alone does not move absolute children - FieldLeft does.
        _settingsHost.Padding = new Padding(0, 8, 8, 8);

        BuildSettings();

        _previewHost.Dock = DockStyle.Fill;
        _previewHost.BackColor = Color.FromArgb(245, 245, 245);
        _previewHost.Padding = new Padding(8);
        _previewHost.Controls.Add(_preview);
        _preview.Dock = DockStyle.Fill;

        _left.Dock = DockStyle.Fill;
        _left.Padding = new Padding(PhxTheme.Gutter, 0, 4, 0);
        _left.Controls.Add(_settingsHost);
        _left.Controls.Add(_tabs);

        _body.Dock = DockStyle.Fill;
        _body.ColumnCount = 2;
        _body.RowCount = 1;
        _body.Padding = new Padding(PhxTheme.Gutter, 0, PhxTheme.Gutter, 0);
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        _body.Controls.Add(_left, 0, 0);
        _body.Controls.Add(_previewHost, 1, 0);

        Controls.Add(_body);
        Controls.Add(_buttons);
        Controls.Add(header);

        _claimDebounce.Tick += (_, _) =>
        {
            _claimDebounce.Stop();
            RefreshPreviewNow();
        };

        AcceptButton = _save;
        CancelButton = _cancel;
        MinimumSize = new Size(960, 640);
        ClientSize = new Size(1100, 720);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Print sizes";
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
        _heightCaption.Location = new Point(FieldLeft, y + 4);
        _settingsHost.Controls.Add(_heightCaption);
        _settingLabels.Add(_heightCaption);
        _height.Location = new Point(FieldValueLeft, y);
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
            Location = new Point(FieldLeft, y + 6),
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
            Location = new Point(FieldLeft, y + 4),
            Text = caption
        };
        control.Location = new Point(FieldValueLeft, y);
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
            Location = new Point(FieldLeft + 12, y + 4),
            Text = caption
        };
        box.Location = new Point(FieldValueLeft, y);
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
        var room = Math.Max(160,
            _settingsHost.ClientSize.Width - FieldValueLeft - 24);
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

        // Named stock (including "Current"): put every field back to that
        // preset - width, height, font, spacing and padding.
        Active = choice.Layout;
        _loading = true;
        try { FillFields(Active); }
        finally { _loading = false; }
        RefreshPreview();
    }

    /// <summary>
    /// Spinner 0 means "document default" (-1), not a hard zero margin.
    /// </summary>
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

    private void ResetToDefault()
    {
        Active = _tabs.SelectedIndex switch
        {
            1 => DocumentLayout.DefaultReceipt,
            2 => DocumentLayout.DefaultClaim,
            _ => DocumentLayout.DefaultLabel
        };

        _loading = true;
        try
        {
            var match = _preset.Items.Cast<PresetChoice>()
                .FirstOrDefault(p => p.Id == Active.PresetId)
                ?? _preset.Items.Cast<PresetChoice>().First();
            _preset.SelectedItem = match;
            FillFields(Active);
        }
        finally { _loading = false; }

        RefreshPreviewNow();
    }

    private void RefreshPreview()
    {
        // Claim redraw is heavy; wait for the spinner to settle.
        if (_tabs.SelectedIndex == 2)
        {
            _claimDebounce.Stop();
            _claimDebounce.Start();
            return;
        }

        _claimDebounce.Stop();
        RefreshPreviewNow();
    }

    private void RefreshPreviewNow() =>
        _preview.Show(_tabs.SelectedIndex, Active);

    /// <summary>
    /// Sends the sample on the allocated printer for this tab - the label,
    /// receipt or document printer from Printer setup - at the sizes under
    /// edit, so what comes out matches the sample on screen.
    /// </summary>
    private async Task PrintSampleAsync()
    {
        var previousLabel = PrintLayouts.Label;
        var previousReceipt = PrintLayouts.Receipt;
        var previousClaim = PrintLayouts.Claim;

        try
        {
            PrintResult result;

            switch (_tabs.SelectedIndex)
            {
                case 1:
                {
                    PrintLayouts.Receipt = Active;
                    using var document = BuildReceiptDocument();
                    result = await Spooler.PrintAsync(document, _settings,
                        WorkstationSettingKey.ReceiptPrinter, "receipt",
                        "Print sizes sample receipt",
                        NullLogger.Instance, CancellationToken.None);
                    break;
                }
                case 2:
                {
                    PrintLayouts.Claim = Active;
                    using var document = BuildClaimDocument();
                    result = await Spooler.PrintAsync(document, _settings,
                        WorkstationSettingKey.DocumentPrinter, "claim form",
                        "Print sizes sample claim form",
                        NullLogger.Instance, CancellationToken.None);
                    break;
                }
                default:
                {
                    PrintLayouts.Label = Active;
                    var imprint = new PharmacyImprint("Bethrapha Pharmacy",
                        "28 Woodstock Road, Thorngrove, Bulawayo", "029-123456");

                    // A patient's label, then a pre-pack sticker: a pre-pack
                    // has a row more, so the size has to suit both.
                    using (var document = new LabelDocument(
                               PrintSamplePreview.SampleLabelPublic(), imprint, layout: Active))
                        result = await Spooler.PrintAsync(document, _settings,
                            WorkstationSettingKey.LabelPrinter, "label",
                            "Print sizes sample label",
                            NullLogger.Instance, CancellationToken.None,
                            orDefault: false);

                    if (result.Outcome != PrintOutcome.Printed) break;

                    using (var prepack = new LabelDocument(
                               PrintSamplePreview.SamplePrepackPublic(), imprint, layout: Active))
                        result = await Spooler.PrintAsync(prepack, _settings,
                            WorkstationSettingKey.LabelPrinter, "label",
                            "Print sizes sample pre-pack",
                            NullLogger.Instance, CancellationToken.None,
                            orDefault: false);
                    break;
                }
            }

            if (result.Outcome == PrintOutcome.Printed)
                ShowInfo(string.IsNullOrWhiteSpace(result.PrinterName)
                    ? "Sample sent to the printer."
                    : $"Sample sent to {result.PrinterName}.");
            else
                ShowError(result.Problem
                          ?? "The sample could not be printed. "
                             + "Check Printer setup for this computer.");
        }
        catch (Exception ex)
        {
            ShowError(ErrorHandler.Describe(ex));
        }
        finally
        {
            PrintLayouts.Label = previousLabel;
            PrintLayouts.Receipt = previousReceipt;
            PrintLayouts.Claim = previousClaim;
        }
    }

    private ReceiptDocument BuildReceiptDocument()
    {
        var sale = frmReceiptTemplate.SampleSale();
        var header = new ReceiptHeaderDto(
            "Bethrapha Pharmacy", "Bethrapha Pharmacy (Pvt) Ltd",
            "28 Woodstock Road\nThorngrove, Bulawayo",
            "4123456789", "", "Branch 1 Till 1", "cashier",
            DateTime.Now, TillNumber: "1", CurrencyCode: "USD",
            Telephone: "029-123456", WhatsApp: "0771234567");
        var document = new ReceiptDocument(sale, header);
        document.FitRoll();
        return document;
    }

    private ClaimFormDocument BuildClaimDocument() =>
        new(PrintSamplePreview.SampleClaimPublic());

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
            AutoScrollMinSize = Size.Empty;
            Invalidate();
            return;
        }

        _receiptScroll.Visible = false;
        _page = kind == 2 ? RenderClaim() : RenderLabel();
        SeatScrollForPage();
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
            _receipt.Show(document.Rows(), ReceiptDocument.CharactersPerLine,
                _layout.FontScale, _layout.LineSpacingScale);
        }
        finally
        {
            PrintLayouts.Receipt = previous;
        }

        // Padding is hundredths of an inch; ~2 screen pixels per unit.
        var pad = _layout.ToMargins(4);
        var scale = (float)Math.Max(0.5m, _layout.WidthMm / 75m);
        var inset = 2;
        _receipt.Location = new Point(
            16 + Math.Max(0, pad.Left) * inset,
            32 + Math.Max(0, pad.Top) * inset);
        _receipt.Width = Math.Max(80, (int)(_receipt.Width * scale));
        _receiptScroll.AutoScrollMinSize = new Size(
            _receipt.Right + 24 + Math.Max(0, pad.Right) * inset,
            _receipt.Bottom + 24 + Math.Max(0, pad.Bottom) * inset);
    }

    /// <summary>
    /// Scroll extent once when the sample bitmap changes - never inside
    /// OnPaint, which would fight the scrollbars and glitch the claim.
    /// </summary>
    private void SeatScrollForPage()
    {
        if (_page is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }

        var areaW = Math.Max(40, ClientSize.Width - 24);
        var areaH = Math.Max(40, ClientSize.Height - 40);
        var scale = Math.Min((float)areaW / _page.Width, (float)areaH / _page.Height);
        scale = Math.Min(scale, 1.25f);
        var w = Math.Max(1, (int)(_page.Width * scale));
        var h = Math.Max(1, (int)(_page.Height * scale));
        AutoScrollMinSize = new Size(
            Math.Max(ClientSize.Width, w + 40),
            Math.Max(ClientSize.Height, h + 56));
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

        // Padding is in hundredths of an inch on the stock; map into pixels.
        var margins = _layout.ToMargins(0);
        var sx = pxW / (float)stockW;
        var sy = pxH / (float)stockH;
        var padL = (int)(margins.Left * sx);
        var padT = (int)(margins.Top * sy);
        var padR = (int)(margins.Right * sx);
        var padB = (int)(margins.Bottom * sy);
        var bounds = new Rectangle(padL, padT,
            Math.Max(20, pxW - padL - padR),
            Math.Max(20, pxH - padT - padB));

        using var doc = new LabelDocument(SampleLabelPublic(),
            new PharmacyImprint("Bethrapha Pharmacy",
                "28 Woodstock Road, Thorngrove, Bulawayo", "029-123456"),
            layout: _layout);
        doc.RenderPage(g, bounds);
        return bmp;
    }

    private Bitmap RenderClaim()
    {
        // Small bitmap + debounce keeps the sample snappy while spinning.
        var widthMm = _layout.WidthMm > 0 ? _layout.WidthMm : 210m;
        var heightMm = _layout.HeightMm > 0 ? _layout.HeightMm : 297m;
        var pxW = Math.Clamp((int)(widthMm * 2.2m), 280, 640);
        var pxH = Math.Clamp((int)(pxW * (heightMm / widthMm)), 360, 900);

        var fallback = 40;
        var margins = _layout.HasCustomPadding
            ? _layout.ToMargins(fallback)
            : new Margins(fallback, fallback, 35, 35);
        var stockW = Math.Max(1, _layout.WidthHundredths > 0
            ? _layout.WidthHundredths : 827);
        var stockH = Math.Max(1, _layout.HeightHundredths > 0
            ? _layout.HeightHundredths : 1169);
        var sx = pxW / (float)stockW;
        var sy = pxH / (float)stockH;
        var padL = (int)(margins.Left * sx);
        var padT = (int)(margins.Top * sy);
        var padR = (int)(margins.Right * sx);
        var padB = (int)(margins.Bottom * sy);

        var content = new Rectangle(padL, padT,
            Math.Max(40, pxW - padL - padR),
            Math.Max(40, pxH - padT - padB));

        var previous = PrintLayouts.Claim;
        var bmp = new Bitmap(pxW, pxH);
        try
        {
            PrintLayouts.Claim = _layout;
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            // Uniform scale so font % does not stretch the form.
            var scale = Math.Max(0.5f,
                _layout.FontScale * (0.85f + 0.15f * _layout.LineSpacingScale));
            var state = g.Save();
            g.TranslateTransform(content.X, content.Y);
            g.ScaleTransform(scale, scale);
            var draw = new Rectangle(0, 0,
                Math.Max(40, (int)(content.Width / scale)),
                Math.Max(40, (int)(content.Height / scale)));
            using var document = new ClaimFormDocument(SampleClaimPublic());
            document.RenderPage(g, draw, 0);
            g.Restore(state);
        }
        finally
        {
            PrintLayouts.Claim = previous;
        }

        return bmp;
    }

    internal static LabelDto SampleLabelPublic() => SampleLabel();

    /// A pre-pack sticker as the Labels screen prints one.
    internal static LabelDto SamplePrepackPublic() => new(
        PatientName: "",
        ProductDescription: "Magnesium Trisilicate",
        Quantity: 12m,
        Directions: "Chew one tablet / two tablets when required",
        BatchNumber: "8834",
        Expiry: new DateOnly(2027, 8, 31),
        PharmacistName: "",
        ScriptNumber: "Pre-pack",
        DispensedOn: DateOnly.FromDateTime(DateTime.Today),
        Cautions: new[] { "Do not take with other medicines" },
        Price: 1.50m);

    internal static ClaimFormDto SampleClaimPublic() => SampleClaim();

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

using System.Drawing.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Which printer this till uses for receipts, which for dispensing
/// labels, and which for A4 documents such as the medical aid claim form.
/// A per-workstation choice, so it lives in the workstation's own
/// settings rather than in a file the installer rewrites.
/// </summary>
public sealed class frmPrinterSetup : PhxFormBase
{
    private readonly IWorkstationSettings _settings;
    private readonly PharmacyProfile _pharmacy;

    private readonly ComboBox _receipt = new();
    private readonly ComboBox _label = new();
    private readonly ComboBox _document = new();
    private readonly Label _status = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private readonly Button _testReceipt = new();

    public frmPrinterSetup(IWorkstationSettings settings,
                           PharmacyProfile pharmacy)
    {
        _settings = settings;
        _pharmacy = pharmacy;

        var header = PhxTheme.HeaderBand(64);

        header.Controls.Add(new Label
        {
            AutoSize = false,
            Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8),
            Size = new Size(520, 26),
            Text = "Printer setup"
        });

        header.Controls.Add(new Label
        {
            AutoSize = false,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Size = new Size(520, 20),
            Text = "This till only. Other workstations keep their own."
        });

        Field("&Receipt printer", _receipt, 0);
        Field("&Label printer", _label, 1);
        Field("&Claim forms (A4)", _document, 2);

        _status.AutoSize = false;
        _status.ForeColor = PhxTheme.Muted;
        _status.Location = new Point(24, 96 + 3 * PhxTheme.RowHeight);
        _status.Size = new Size(520, 40);

        _testReceipt.Location = new Point(24, 146 + 3 * PhxTheme.RowHeight);
        _testReceipt.Size = new Size(150, 32);
        _testReceipt.Text = "&Test page";
        _testReceipt.Click += (_, _) => PrintTestPage();

        _save.Location = new Point(378, 146 + 3 * PhxTheme.RowHeight);
        _save.Size = new Size(80, 32);
        _save.Text = "&Save";
        _save.Click += async (_, _) => await SaveAsync();

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(464, 146 + 3 * PhxTheme.RowHeight);
        _cancel.Size = new Size(80, 32);
        _cancel.Text = "Cancel";

        Controls.AddRange(new Control[]
            { _status, _testReceipt, _save, _cancel });
        Controls.Add(header);

        AcceptButton = _save;
        CancelButton = _cancel;
        ClientSize = new Size(568, 196 + 3 * PhxTheme.RowHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Printer setup";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var installed = PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();

        if (installed.Length == 0)
        {
            _status.Text =
                "No printers are installed on this workstation. PharmaX "
                + "will still trade: a receipt that cannot print is "
                + "logged and the sale stands (POS-014).";

            _receipt.Enabled = false;
            _label.Enabled = false;
            _document.Enabled = false;
            _save.Enabled = false;
            _testReceipt.Enabled = false;
            return;
        }

        foreach (var combo in new[] { _receipt, _label, _document })
        {
            // A blank entry means "whatever Windows calls the default",
            // which is the right answer on a till with one printer.
            combo.Items.Add("(Windows default)");
            combo.Items.AddRange(installed);
            combo.SelectedIndex = 0;
        }

        await SelectAsync(_receipt, WorkstationSettingKey.ReceiptPrinter);
        await SelectAsync(_label, WorkstationSettingKey.LabelPrinter);
        await SelectAsync(_document, WorkstationSettingKey.DocumentPrinter);

        _status.Text = $"{installed.Length} printer(s) installed. The "
                       + "default is "
                       + $"{new PrinterSettings().PrinterName}.";
    }

    private async Task SelectAsync(ComboBox combo, string key)
    {
        var saved = await _settings.GetAsync(key);

        if (string.IsNullOrWhiteSpace(saved)) return;

        var index = combo.Items.IndexOf(saved);

        // A printer that has since been removed leaves the choice on the
        // default rather than on a name that no longer resolves.
        combo.SelectedIndex = index >= 0 ? index : 0;

        if (index < 0)
            _status.Text = $"The saved printer '{saved}' is no longer "
                           + "installed, so the Windows default is being "
                           + "used.";
    }

    private async Task SaveAsync()
    {
        await _settings.SetAsync(WorkstationSettingKey.ReceiptPrinter,
                                 Chosen(_receipt));

        await _settings.SetAsync(WorkstationSettingKey.LabelPrinter,
                                 Chosen(_label));

        await _settings.SetAsync(WorkstationSettingKey.DocumentPrinter,
                                 Chosen(_document));

        ShowInfo("Saved. Receipts, labels and claim forms will use these "
                 + "printers from the next one printed.");

        CloseView(true);
    }

    private static string? Chosen(ComboBox combo) =>
        combo.SelectedIndex <= 0 ? null : combo.SelectedItem?.ToString();

    /// <summary>
    /// Prints one line to the chosen receipt printer. The quickest way
    /// to find out whether the till is talking to the roll printer or to
    /// the office laser two rooms away.
    /// </summary>
    private void PrintTestPage()
    {
        try
        {
            using var document = new PrintDocument();

            if (Chosen(_receipt) is string printer)
                document.PrinterSettings.PrinterName = printer;

            document.PrintPage += (_, e) =>
            {
                using var font = new Font("Consolas", 9f);

                // Headed as the receipts are, with the pharmacy's own
                // name: a till set up for the pharmacy should not print
                // the software's (ADM-OBS-01).
                e.Graphics!.DrawString(
                    $"{_pharmacy.TradingName} - printer test page\n"
                    + $"{Format.DateTimeShort(DateTime.Now)}\n"
                    + $"Printer: {document.PrinterSettings.PrinterName}\n"
                    + "If you can read this, receipts will print.",
                    font, Brushes.Black, e.MarginBounds.Left,
                    e.MarginBounds.Top);

                e.HasMorePages = false;
            };

            document.Print();
            _status.Text = "Test page sent.";
        }
#pragma warning disable CA1031 // Deliberately broad: see the comment below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // A driver can throw almost anything, and none of it should
            // take down the setup screen.
            _status.ForeColor = PhxTheme.ProblemText;
            _status.Text = "The test page did not print: " + ex.Message;
        }
    }

    private void Field(string caption, Control input, int row)
    {
        var y = 88 + row * PhxTheme.RowHeight;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(24, y + 4),
            Text = caption
        });

        if (input is ComboBox combo)
            combo.DropDownStyle = ComboBoxStyle.DropDownList;

        // Named after its caption, so a screen reader announces which
        // printer the list chooses and automation can find it.
        var name = caption.Replace("&", "", StringComparison.Ordinal);
        input.AccessibleName = name;
        input.Name = "cbo" + new string(name.Where(char.IsLetter).ToArray());

        input.Location = new Point(160, y);
        input.Size = new Size(384, 24);
        input.TabIndex = row;
        Controls.Add(input);
    }
}

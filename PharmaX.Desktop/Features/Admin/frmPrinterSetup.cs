using System.ComponentModel;
using System.Drawing.Printing;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Desktop.Setup;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Which printer this till uses for receipts, which for dispensing
/// labels, and which for A4 documents such as the medical aid claim form.
/// A per-workstation choice, so it lives in the workstation's own
/// settings rather than in a file the installer rewrites.
/// </summary>
/// <remarks>
/// Any printer in the pharmacy can be chosen, not only one plugged in
/// here: every PharmaX computer reports its printers to the shared
/// database, and the ones it shares are listed beside this computer's own.
/// The main computer can print its claim forms on the laser at the
/// dispensary, or a till its labels on the main computer's label printer.
/// </remarks>
public sealed class frmPrinterSetup : PhxFormBase
{
    private readonly IWorkstationSettings _settings;
    private readonly PharmacyProfile _pharmacy;
    private readonly IPrinterDirectory _directory;

    private readonly ComboBox _receipt = new();
    private readonly ComboBox _label = new();
    private readonly ComboBox _document = new();
    private readonly Button _share = new();
    private readonly Button _find = new();
    private readonly Label _status = new();
    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private readonly Button _testReceipt = new();

    // Printers found by asking a computer PharmaX is not running on.
    private readonly List<NetworkPrinter> _found = new();
    private bool _loaded;

    /// A printer as listed: what is saved, and what the operator reads.
    private sealed record Choice(string? Value, string Text)
    {
        public override string ToString() => Text;
    }

    private static readonly Choice WindowsDefault = new(null, "(Windows default)");

    /// Labels never go to the Windows default, which at a till is the
    /// receipt printer (PrinterResolver.Resolve), so the blank entry says
    /// what it means for them.
    private static readonly Choice NoLabelPrinter =
        new(null, "(none chosen - labels will not print)");

    public frmPrinterSetup(IWorkstationSettings settings,
                           PharmacyProfile pharmacy,
                           IPrinterDirectory directory)
    {
        _settings = settings;
        _pharmacy = pharmacy;
        _directory = directory;

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
            Size = new Size(560, 20),
            Text = "This computer's choice. Printers the other computers "
                   + "share are listed too."
        });

        Field("&Receipt printer", _receipt, 0);
        Field("&Label printer", _label, 1);
        Field("&Claim forms (A4)", _document, 2);

        var below = 92 + 3 * PhxTheme.RowHeight;

        _share.Location = new Point(160, below);
        _share.Name = "btnSharePrinters";
        _share.Size = new Size(230, 32);
        _share.Text = "S&hare this computer's printers";
        _share.Click += async (_, _) => await ShareAsync();

        _find.Location = new Point(396, below);
        _find.Name = "btnFindPrinters";
        _find.Size = new Size(148, 32);
        _find.Text = "&Find on a computer...";
        _find.Click += async (_, _) => await FindAsync();

        _status.AutoSize = false;
        _status.ForeColor = PhxTheme.Muted;
        _status.Location = new Point(24, below + 42);
        _status.Name = "lblPrinterStatus";
        _status.Size = new Size(520, 72);

        _testReceipt.Location = new Point(24, below + 122);
        _testReceipt.Size = new Size(150, 32);
        _testReceipt.Text = "&Test receipt";
        _testReceipt.Click += async (_, _) => await PrintTestPageAsync();

        _save.Location = new Point(378, below + 122);
        _save.Size = new Size(80, 32);
        _save.Text = "&Save";
        _save.Click += async (_, _) => await SaveAsync();

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(464, below + 122);
        _cancel.Size = new Size(80, 32);
        _cancel.Text = "Cancel";

        Controls.AddRange(new Control[]
            { _share, _find, _status, _testReceipt, _save, _cancel });
        Controls.Add(header);

        AcceptButton = _save;
        CancelButton = _cancel;
        ClientSize = new Size(568, below + 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Printer setup";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await RefreshAsync(note: null);
        _receipt.Focus();
    }

    /// <summary>
    /// Lists every printer this computer can use, keeping what is chosen,
    /// and says what is where.
    /// </summary>
    private async Task RefreshAsync(string? note)
    {
        UseWaitCursor = true;

        try
        {
            // This computer's own first, as they are now: what the others
            // will see of it, a printer just shared included.
            IReadOnlyList<NetworkPrinter> everyone;

            try
            {
                await NetworkPrinters.PublishAsync(_directory);
                everyone = await _directory.ListAsync();
            }
#pragma warning disable CA1031 // The local printers still list without it.
            catch (Exception)
#pragma warning restore CA1031
            {
                everyone = Array.Empty<NetworkPrinter>();
            }

            var installed = PrinterResolver.Installed();
            var elsewhere = everyone.Where(p => !IsHere(p.Computer)).ToList();

            var options = installed
                .OrderBy(p => NetworkPrinters.IsNetworkPath(p))
                .ThenBy(p => p, StringComparer.CurrentCulture)
                .Select(p => new Choice(p, NetworkPrinters.Describe(p)))
                .Concat(elsewhere.Concat(_found)
                    .Where(p => p.IsShared)
                    .OrderBy(p => p.Computer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Name, StringComparer.CurrentCulture)
                    .Select(p => new Choice(p.Path, $"{p.Name} on {p.Computer}")))
                .DistinctBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lost = new[]
            {
                await FillAsync(_receipt, WorkstationSettingKey.ReceiptPrinter, options),
                await FillAsync(_label, WorkstationSettingKey.LabelPrinter, options,
                                NoLabelPrinter),
                await FillAsync(_document, WorkstationSettingKey.DocumentPrinter, options)
            }.FirstOrDefault(problem => problem is not null);

            _loaded = true;

            _testReceipt.Enabled = options.Count > 0;
            _share.Enabled = NetworkPrinters.OnThisComputer().Any(p => !p.IsShared);

            _status.ForeColor = PhxTheme.Muted;
            _status.Text = string.Join(" ", new[]
                { lost, note ?? Describe(installed, elsewhere) }.OfType<string>());
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// What is listed, and what is not and why.
    private static string Describe(IReadOnlyList<string> installed,
                                   List<NetworkPrinter> elsewhere)
    {
        var here = installed.Count(p => !NetworkPrinters.IsNetworkPath(p));
        var computers = elsewhere.Where(p => p.IsShared)
            .Select(p => p.Computer).Distinct().ToList();

        var text = $"{here} printer(s) on this computer";

        text += computers.Count == 0
            ? "; none shared by the other computers yet."
            : $"; shared by {string.Join(", ", computers)} too.";

        var unshared = elsewhere.Where(p => !p.IsShared).ToList();

        if (unshared.Count > 0)
            text += " Not shared, so not listed: "
                    + string.Join(", ", unshared.Select(p => $"{p.Name} on {p.Computer}"))
                    + ". Press Share in Printer setup on that computer.";

        return text;
    }

    /// <summary>
    /// Refills a list, keeping its choice: the one made on this screen,
    /// else the one saved. Says so where the saved one has gone.
    /// </summary>
    private async Task<string?> FillAsync(ComboBox combo, string key,
                                          IReadOnlyList<Choice> options,
                                          Choice? blank = null)
    {
        var keep = _loaded ? Chosen(combo) : await _settings.GetAsync(key);

        combo.Items.Clear();

        // A blank entry means "whatever Windows calls the default", which
        // is the right answer on a till with one printer.
        combo.Items.Add(blank ?? WindowsDefault);
        combo.Items.AddRange(options.Cast<object>().ToArray());

        if (string.IsNullOrWhiteSpace(keep))
        {
            combo.SelectedIndex = 0;
            return null;
        }

        var match = options.FirstOrDefault(o =>
            string.Equals(o.Value, keep, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            combo.SelectedItem = match;
            return null;
        }

        // A printer on a computer that is off today is still the right
        // choice tomorrow, so it is kept rather than quietly dropped.
        if (NetworkPrinters.IsNetworkPath(keep))
        {
            var missing = new Choice(keep,
                $"{NetworkPrinters.Describe(keep)} (not found just now)");
            combo.Items.Add(missing);
            combo.SelectedItem = missing;

            return $"{NetworkPrinters.Describe(keep)} was not found just now - "
                   + $"check {NetworkPrinters.ComputerOf(keep)} is switched on.";
        }

        // A printer that has since been removed leaves the choice on the
        // default rather than on a name that no longer resolves.
        combo.SelectedIndex = 0;
        return $"The saved printer '{keep}' is no longer installed"
               + (blank is null
                   ? ", so the Windows default is being used."
                   : ". Choose another.");
    }

    private static bool IsHere(string computer) =>
        string.Equals(computer, Environment.MachineName,
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shares every printer plugged into this computer that is not shared
    /// yet, so the other computers can print to it.
    /// </summary>
    private async Task ShareAsync()
    {
        var unshared = NetworkPrinters.OnThisComputer()
            .Where(p => !p.IsShared).Select(p => p.Name).ToList();

        if (unshared.Count == 0)
        {
            await RefreshAsync("Every printer on this computer is already shared.");
            return;
        }

        if (!Confirm($"Share {string.Join(", ", unshared)} with the "
                     + "pharmacy's other computers?" + Environment.NewLine
                     + Environment.NewLine + "Windows will ask for an "
                     + "administrator's permission once."))
            return;

        var log = Path.Combine(WorkstationProvisioning.Folder, "logs",
                               "printer-sharing.log");

        UseWaitCursor = true;
        var problem = await Task.Run(() => NetworkPrinters.Share(unshared, log));
        UseWaitCursor = false;

        if (problem is not null)
        {
            ShowError(problem);
            return;
        }

        await RefreshAsync("Shared. The other computers list "
                           + string.Join(", ", unshared)
                           + " the next time their Printer setup is opened.");
    }

    /// <summary>
    /// Asks a computer PharmaX is not running on which printers it shares.
    /// </summary>
    private async Task FindAsync()
    {
        var computer = frmTextPrompt.Ask(this, "Find printers",
            "The name of the computer the printer is plugged into, as "
            + "Windows shows it in Settings > System > About.",
            hint: "e.g. OFFICE-PC");

        if (string.IsNullOrWhiteSpace(computer)) return;

        IReadOnlyList<NetworkPrinter> found;
        UseWaitCursor = true;

        try
        {
            found = await Task.Run(() => NetworkPrinters.SharedOn(computer));
        }
        catch (Win32Exception ex)
        {
            _status.ForeColor = PhxTheme.ProblemText;
            _status.Text = $"Could not reach {computer}: {ex.Message} Check "
                           + "the name, that it is switched on, and that "
                           + "its printer is shared.";
            return;
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (found.Count == 0)
        {
            _status.ForeColor = PhxTheme.ProblemText;
            _status.Text = $"{computer} answered, but shares no printers. "
                           + "Share the printer on that computer first.";
            return;
        }

        _found.AddRange(found);

        await RefreshAsync($"Found on {found[0].Computer}: "
                           + string.Join(", ", found.Select(p => p.Name))
                           + ". Choose one above and save.");
    }

    /// <summary>
    /// Connects a printer on another computer in Windows, where it is not
    /// already, so it prints like one plugged in here. False, having said
    /// why, where Windows would not.
    /// </summary>
    private async Task<bool> ConnectAsync(string? printer)
    {
        if (!NetworkPrinters.IsNetworkPath(printer)
            || PrinterResolver.Installed().Contains(printer!,
                   StringComparer.OrdinalIgnoreCase))
            return true;

        _status.ForeColor = PhxTheme.Muted;
        _status.Text = $"Connecting to {NetworkPrinters.Describe(printer!)}...";
        UseWaitCursor = true;

        var problem = await Task.Run(() => NetworkPrinters.Connect(printer!));
        UseWaitCursor = false;

        if (problem is null) return true;

        _status.ForeColor = PhxTheme.ProblemText;
        _status.Text = problem;
        ShowError(problem);
        return false;
    }

    private async Task SaveAsync()
    {
        // Labels on the receipt printer come out on the receipt roll - the
        // mistake the Windows default made for them - so it is asked.
        var receipt = Chosen(_receipt) ?? PrinterResolver.WindowsDefault();
        var label = Chosen(_label);

        if (label is not null
            && string.Equals(label, receipt, StringComparison.OrdinalIgnoreCase)
            && !Confirm($"Labels and receipts are both set to print on "
                        + $"{NetworkPrinters.Describe(label)}, so labels will "
                        + "come out on the receipt roll." + Environment.NewLine
                        + Environment.NewLine + "Save anyway?"))
            return;

        foreach (var printer in new[] { Chosen(_receipt), Chosen(_label),
                                        Chosen(_document) }.Distinct())
            if (!await ConnectAsync(printer)) return;

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
        (combo.SelectedItem as Choice)?.Value;

    /// <summary>
    /// Prints one line to the chosen receipt printer. The quickest way
    /// to find out whether the till is talking to the roll printer or to
    /// the office laser two rooms away.
    /// </summary>
    private async Task PrintTestPageAsync()
    {
        var chosen = Chosen(_receipt);
        if (!await ConnectAsync(chosen)) return;

        try
        {
            using var document = new PrintDocument();

            if (chosen is not null)
                document.PrinterSettings.PrinterName = chosen;

            // On the roll the receipts are given, so the width it reports
            // is the width a receipt gets.
            ReceiptDocument.ApplyRoll(document.DefaultPageSettings, 10);
            var reach = 0.0;

            document.PrintPage += (_, e) =>
            {
                using var font = new Font("Consolas", 9f);
                reach = e.PageSettings.PrintableArea.Width * 0.254;

                // Headed as the receipts are, with the pharmacy's own
                // name: a till set up for the pharmacy should not print
                // the software's (ADM-OBS-01).
                e.Graphics!.DrawString(
                    $"{_pharmacy.TradingName} - printer test page\n"
                    + $"{Format.DateTimeShort(DateTime.Now)}\n"
                    + $"Printer: {document.PrinterSettings.PrinterName}\n"
                    + $"Sent from: {Environment.MachineName}\n"
                    + $"Prints {reach:0} mm wide on {e.PageSettings.PaperSize.PaperName}\n"
                    + "The line below runs edge to edge.",
                    font, Brushes.Black, e.MarginBounds.Left,
                    e.MarginBounds.Top);

                // Across everything the printer reaches: where it stops
                // short of the roll's right edge, receipts do too.
                var y = e.MarginBounds.Top + 7 * font.GetHeight(e.Graphics);
                e.Graphics.DrawLine(Pens.Black, 0, y,
                    e.PageSettings.PrintableArea.Width - 1, y);

                e.HasMorePages = false;
            };

            // Off this window's thread: a printer on another computer takes
            // as long as the network does to accept the job.
            await Task.Run(document.Print);

            // A 75 mm roll is printed about 72 mm wide. Much less is a
            // printer set up for 58 mm paper, and a receipt on it fills
            // only the left of the roll.
            var narrow = reach is > 0 and < 65;

            _status.ForeColor = narrow ? PhxTheme.ProblemText : PhxTheme.Muted;
            _status.Text = "Test page sent to "
                           + NetworkPrinters.Describe(
                               document.PrinterSettings.PrinterName)
                           + (reach > 0 ? $". It prints {reach:0} mm wide" : "")
                           + (narrow
                               ? " - set it to 80 mm paper in Windows (Printing "
                                 + "preferences) so receipts fill the roll."
                               : ".");
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

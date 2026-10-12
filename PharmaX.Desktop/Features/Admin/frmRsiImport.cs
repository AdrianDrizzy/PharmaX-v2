using PharmaX.Application.Legacy;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Loads the data exported from RSI DispenseWare and Retailware: the
/// screen for the day the pharmacy changes over.
/// </summary>
/// <remarks>
/// Check first, which runs the whole import and rolls it back, so the
/// report is the real one and nothing is kept. Import then does it for
/// good. Both reports are saved beside the logs, because the totals in
/// them are what the pharmacist signs the change-over off against.
/// </remarks>
public sealed class frmRsiImport : PhxFormBase
{
    private readonly ILegacyImporter _importer;

    private readonly TextBox _folder = new();
    private readonly Button _browse = new();
    private readonly Button _check = new();
    private readonly Button _import = new();
    private readonly Button _close = new();
    private readonly Label _progress = new();
    private readonly TextBox _report = new();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SettingsChange;

    public frmRsiImport(ILegacyImporter importer)
    {
        _importer = importer;

        // Docked rather than placed: the window grows with the display's
        // scaling, and a control anchored inside a panel that is not yet
        // its final size ends up somewhere else entirely.
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

        _folder.Dock = DockStyle.Fill;
        _folder.Name = "txtFolder";
        _folder.PlaceholderText = @"The RSI export folder, e.g. C:\RSI\export";
        _folder.Text = DefaultFolder();

        _browse.Dock = DockStyle.Right;
        _browse.Text = "Choose...";
        _browse.Width = 120;
        _browse.Click += (_, _) => Browse();

        var row = new Panel { Dock = DockStyle.Top, Height = 34 };
        row.Controls.Add(_folder);
        row.Controls.Add(_browse);
        row.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Text = "&Export folder",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 110
        });

        _progress.AutoSize = false;
        _progress.Dock = DockStyle.Top;
        _progress.ForeColor = PhxTheme.Muted;
        _progress.Height = 30;
        _progress.Text = "The folder the RSI export scripts wrote: the four Excel "
                         + "workbooks and their CSV copies.";

        _report.Dock = DockStyle.Fill;
        _report.Font = new Font("Consolas", 9.75F);
        _report.Multiline = true;
        _report.Name = "txtReport";
        _report.ReadOnly = true;
        _report.ScrollBars = ScrollBars.Both;
        _report.WordWrap = false;

        // The last added docks first: the folder row at the top, then the
        // progress line, and the report takes the rest.
        body.Controls.Add(_report);
        body.Controls.Add(_progress);
        body.Controls.Add(row);
        Controls.Add(body);

        var header = PhxTheme.HeaderBand(64);
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8),
            Text = "Import from DispenseWare and Retailware"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Text = "Products, stock, patients, prescribers and a year of history. "
                   + "Check first; import once, before the pharmacy trades."
        });
        Controls.Add(header);

        _check.Size = new Size(190, 32);
        _check.Text = "Check (dry run)";
        _check.Click += async (_, _) => await RunAsync(dryRun: true);

        _import.Size = new Size(140, 32);
        _import.Text = "Import";
        _import.Click += async (_, _) => await RunAsync(dryRun: false);
        PhxTheme.MakePrimary(_import);

        _close.DialogResult = DialogResult.Cancel;
        _close.Size = new Size(110, 32);
        _close.Text = "Close";

        Controls.Add(Features.Reports.frmLegacyHistory.Footer(new Label(), _check, _import, _close));

        CancelButton = _close;
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(700, 480);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Import from RSI";
    }

    /// The export scripts' own folder on the pharmacy PC, then Downloads.
    private static string DefaultFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new[]
        {
            Path.Combine(profile, "Documents", "Exports"),
            Path.Combine(profile, "Downloads", "Exports", "Exports"),
            Path.Combine(profile, "Downloads", "Exports")
        }.FirstOrDefault(d => File.Exists(Path.Combine(d, "PRODUCTS.csv"))
                              || Directory.Exists(d) && Directory.GetFiles(d, "Products_Stock_*.xlsx").Length > 0)
            ?? "";
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "The folder the RSI export scripts wrote to",
            UseDescriptionForTitle = true,
            SelectedPath = _folder.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
    }

    private async Task RunAsync(bool dryRun)
    {
        var folder = _folder.Text.Trim();

        if (!Directory.Exists(folder))
        {
            ShowError("Choose the folder the RSI export scripts wrote to.");
            return;
        }

        if (!dryRun && !Confirm(
                "Import the RSI data into this database?" + Environment.NewLine
                + Environment.NewLine
                + "Products, prices and stock on hand become RSI's as exported, "
                + "patients and prescribers are added, and the history is replaced "
                + "with the export's. Run it once, at the change-over, after the "
                + "last Z-read in RSI."))
            return;

        _check.Enabled = _import.Enabled = _browse.Enabled = _close.Enabled = false;
        SetBusy(true);
        _report.Clear();

        try
        {
            var progress = new Progress<string>(step => _progress.Text = step + "...");
            var report = await Task.Run(() => _importer.ImportAsync(folder, dryRun, progress));

            _report.Text = report.Text;

            var saved = Save(report);
            _progress.Text = $"Report saved to {saved}";

            if (!dryRun && report.Succeeded)
                ShowInfo("Imported. Every total agrees with the export." + Environment.NewLine
                         + "Sign out and back in to see the products and patients.");
        }
        finally
        {
            SetBusy(false);
            _check.Enabled = _import.Enabled = _browse.Enabled = _close.Enabled = true;
        }
    }

    /// <summary>Saves an import report beside the logs and returns where.</summary>
    internal static string Save(LegacyImportReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var folder = Path.Combine(
            Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "PharmaX", "logs");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"rsi-import-{DateTime.Now:yyyyMMdd-HHmmss}{(report.DryRun ? "-check" : "")}.txt"));

        File.WriteAllText(path, report.Text);
        return path;
    }
}

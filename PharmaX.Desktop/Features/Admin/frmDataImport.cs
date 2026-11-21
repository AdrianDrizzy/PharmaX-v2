using System.IO.Compression;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Import;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Brings another system's data into PharmaX: a folder of exported
/// files, or the files themselves - CSV, tab-separated or other delimited
/// text, Excel workbooks, JSON.
/// </summary>
/// <remarks>
/// Check first, which runs the whole import and rolls it back, so the
/// report is the real one and nothing is kept. Import then does it for
/// good. Both reports are saved beside the logs, because the totals in
/// them are what the pharmacist signs the change-over off against.
/// </remarks>
public sealed class frmDataImport : PhxFormBase
{
    private readonly IDataImporter _importer;
    private readonly IDatabaseBackup? _backup;

    private readonly TextBox _paths = new();
    private readonly Button _folder = new();
    private readonly Button _files = new();
    private readonly Button _check = new();
    private readonly Button _import = new();
    private readonly Button _close = new();
    private readonly Label _progress = new();
    private readonly TextBox _report = new();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.DataImport;

    public frmDataImport(IDataImporter importer, IDatabaseBackup? backup = null)
    {
        _importer = importer;
        _backup = backup;

        // Docked rather than placed: the window grows with the display's
        // scaling, and a control anchored inside a panel that is not yet
        // its final size ends up somewhere else entirely.
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

        _paths.Dock = DockStyle.Fill;
        _paths.Name = "txtPaths";
        _paths.PlaceholderText = @"A folder of exported files, or files separated by ;";
        _paths.Text = DefaultFolder();

        _folder.Dock = DockStyle.Right;
        _folder.Text = "Folder...";
        _folder.Width = 110;
        _folder.Click += (_, _) => ChooseFolder();

        _files.Dock = DockStyle.Right;
        _files.Text = "Files...";
        _files.Width = 110;
        _files.Click += (_, _) => ChooseFiles();

        var row = new Panel { Dock = DockStyle.Top, Height = 34 };
        row.Controls.Add(_paths);
        row.Controls.Add(_files);
        row.Controls.Add(_folder);
        row.Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Text = "&Import from",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 110
        });

        _progress.AutoSize = false;
        _progress.Dock = DockStyle.Top;
        _progress.ForeColor = PhxTheme.Muted;
        _progress.Height = 30;
        _progress.Text = "Each table is recognised by its columns: "
                         + string.Join(", ", ImportFiles.Extensions) + " files are read.";

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
            Text = "Import data"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Text = "Products, stock, patients, prescribers, suppliers and their history. "
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

        Controls.Add(PhxTheme.Footer(new Label(), _check, _import, _close));

        CancelButton = _close;
        ClientSize = new Size(960, 640);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Import data";
    }

    /// An export folder where one is usually left, if there is one.
    private static string DefaultFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new[]
        {
            Path.Combine(profile, "Documents", "Exports"),
            Path.Combine(profile, "Downloads", "Exports", "Exports"),
            Path.Combine(profile, "Downloads", "Exports")
        }.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d)
            .Any(f => ImportFiles.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            ?? "";
    }

    private IReadOnlyList<string> Paths() =>
        _paths.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim('"'))
            .ToList();

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "The folder the exported files are in",
            UseDescriptionForTitle = true,
            SelectedPath = Paths().FirstOrDefault(Directory.Exists) ?? ""
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) _paths.Text = dialog.SelectedPath;
    }

    private void ChooseFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = ImportFiles.DialogFilter,
            Multiselect = true,
            Title = "The files to import"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _paths.Text = string.Join("; ", dialog.FileNames);
    }

    private async Task RunAsync(bool dryRun)
    {
        var paths = Paths();

        if (paths.Count == 0 || !paths.All(p => Directory.Exists(p) || File.Exists(p)))
        {
            ShowError("Choose a folder of exported files, or the files themselves.");
            return;
        }

        var sql = paths.FirstOrDefault(IsSqlBackup);
        if (sql is not null)
        {
            if (_backup is null)
            {
                ShowError("SQL restore is not available from this screen.");
                return;
            }

            if (dryRun)
            {
                ShowInfo("Check does not run an SQL restore. Import replaces the database with that file.");
                return;
            }

            if (!Confirm("Replace this database with " + Path.GetFileName(sql)
                         + "?" + Environment.NewLine + Environment.NewLine
                         + "Close the other tills first. This cannot be undone except from another backup."))
                return;

            try
            {
                SetBusy(true);
                await _backup.RestoreSqlAsync(sql);
                ShowInfo("Restored from " + sql);
            }
            catch (InvalidOperationException ex)
            {
                ShowError("The restore did not finish: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            return;
        }

        var files = ExpandZips(paths);

        if (!dryRun && !Confirm(
                "Import this data into this database?" + Environment.NewLine
                + Environment.NewLine
                + "Products, prices and suppliers are brought up to date, patients and "
                + "prescribers are added, and - into a database that has not traded yet - "
                + "the stock on hand and the trading history come in as well. Run it once, "
                + "at the change-over, after the last cash-up on the old system."))
            return;

        _check.Enabled = _import.Enabled = _folder.Enabled = _files.Enabled = _close.Enabled = false;
        SetBusy(true);
        _report.Clear();

        try
        {
            var progress = new Progress<string>(step => _progress.Text = step + "...");
            var report = await Task.Run(() => _importer.ImportAsync(files, dryRun, progress));

            _report.Text = report.Text;

            var saved = Save(report);
            _progress.Text = $"Report saved to {saved}";

            if (!dryRun && report.Succeeded)
                ShowInfo("Imported. Every total agrees with the files." + Environment.NewLine
                         + "Sign out and back in to see the products and patients.");
        }
        finally
        {
            SetBusy(false);
            _check.Enabled = _import.Enabled = _folder.Enabled = _files.Enabled = _close.Enabled = true;
        }
    }

    /// <summary>Saves an import report beside the logs and returns where.</summary>
    internal static string Save(ImportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var folder = Path.Combine(
            Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "PharmaX", "logs");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"import-{DateTime.Now:yyyyMMdd-HHmmss}{(report.DryRun ? "-check" : "")}.txt"));

        File.WriteAllText(path, report.Text);
        return path;
    }

    private static bool IsSqlBackup(string path)
    {
        if (path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return true;
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return false;

        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Any(e => e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
    }

    /// A zip of spreadsheets is imported as the files inside it.
    private static IReadOnlyList<string> ExpandZips(IReadOnlyList<string> paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                files.Add(path);
                continue;
            }

            var dir = Path.Combine(Path.GetTempPath(), "pharmax-import-" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(path, dir);
            files.AddRange(Directory.GetFiles(dir));
        }

        return files;
    }
}

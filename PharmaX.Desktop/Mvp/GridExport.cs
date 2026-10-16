using System.ComponentModel;
using System.Globalization;
using PharmaX.Application.Reporting;
using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Exports what is on a grid to a spreadsheet the operator names.
/// </summary>
/// <remarks>
/// The accounts and ledger screens are read as working documents - a
/// trial balance goes to the accountant, an age analysis to whoever is
/// chasing the money - and until now the only way off any of them was to
/// read the figures off the screen and type them again. The reports
/// screen has exported to Excel since it was written; this gives the
/// same workbook to the screens that show the books.
///
/// It takes the grid rather than the underlying DTOs on purpose. What
/// the operator sees is what they mean to send: the same columns, in the
/// order they arranged them, carrying the filter they had applied. A
/// second projection built from the DTOs would drift from the screen the
/// first time a column was added to one and not the other.
/// </remarks>
internal static class GridExport
{
    /// <summary>
    /// Asks where to save, writes the grid, and returns the path - or
    /// null if the operator thought better of it or there is nothing on
    /// the grid to send.
    /// </summary>
    /// <param name="rows">
    /// Every row the list holds, where the screen pages it from the
    /// database and the grid has only the page on screen. Left out, the
    /// grid's own pager has them all, or the grid itself does.
    /// </param>
    public static string? Offer(Form owner, IWorkbookWriter writer,
        DataGridView grid, string title, string parameters, string username,
        IReadOnlyList<object>? rows = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(grid);

        rows ??= GridPager.Of(grid)?.All;

        if ((rows?.Count ?? grid.Rows.Count) == 0) return null;

        using var save = new SaveFileDialog
        {
            FileName = SuggestedName(title),
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            Title = $"Export {title.ToLowerInvariant()}",
            DefaultExt = "xlsx",
            AddExtension = true,
            InitialDirectory = Downloads
        };

        if (save.ShowDialog(owner) != DialogResult.OK) return null;

        var path = WorkbookPath(save.FileName);

        try
        {
            writer.WriteTo(Build(grid, title, parameters, username, rows), path);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException)
        {
            // Most often the workbook is still open in Excel from the last
            // export. Said as that, not as a fault in PharmaX.
            frmMessage.Show(owner,
                $"{Path.GetFileName(path)} could not be saved: {ex.Message}"
                + "\n\nClose it if it is open in Excel, or choose another "
                + "name, and export again.",
                MessageBoxButtons.OK, MessageBoxIcon.Warning, owner.Text);

            return null;
        }

        return path;
    }

    /// <summary>
    /// Where an export is offered first: the Downloads folder, as a file
    /// from a browser lands, or Documents on a profile without one.
    /// </summary>
    public static string Downloads
    {
        get
        {
            var downloads = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), "Downloads");

            return Directory.Exists(downloads)
                ? downloads
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    /// <summary>
    /// Says where the workbook went and offers to open it or the folder
    /// it is in, so nobody has to go looking for it.
    /// </summary>
    public static void Saved(IWin32Window owner, string path)
    {
        var open = new TaskDialogButton("Open file");
        var folder = new TaskDialogButton("Show in folder");

        var chosen = TaskDialog.ShowDialog(owner, new TaskDialogPage
        {
            Caption = "PharmaX",
            Heading = "Exported",
            Text = path,
            Icon = TaskDialogIcon.Information,
            Buttons = { open, folder, TaskDialogButton.Close }
        });

        try
        {
            if (chosen == open)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            else if (chosen == folder)
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // No program for .xlsx on this till: the file is still saved.
            frmMessage.Show(owner, $"{path} is saved, but could not be opened: "
                + ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Warning,
                "PharmaX");
        }
    }

    /// <summary>
    /// The name the workbook is saved under. The dialog offers only Excel
    /// workbooks, but a name typed with another extension - "quote.csv" -
    /// came back as it was typed, and the writer then refused it with an
    /// exception the operator saw as "something went wrong".
    /// </summary>
    internal static string WorkbookPath(string chosen) =>
        string.Equals(Path.GetExtension(chosen), ".xlsx",
                      StringComparison.OrdinalIgnoreCase)
            ? chosen
            : Path.ChangeExtension(chosen, ".xlsx");

    /// <summary>
    /// The grid as a report result: its visible columns, its rows, and
    /// the provenance line every other export in PharmaX carries.
    /// </summary>
    /// <remarks>
    /// Every row of the list where there are more than the page on
    /// screen: each read through the column's bound property, or for a
    /// grid filled a row at a time, by the column's position.
    /// </remarks>
    internal static ReportResult Build(DataGridView grid, string title,
        string parameters, string username, IReadOnlyList<object>? all = null)
    {
        var columns = grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(c => c.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        var headings = columns
            .Select(c => new ReportColumn(
                Key: c.Name,
                Header: string.IsNullOrWhiteSpace(c.HeaderText)
                    ? c.Name : c.HeaderText,
                Format: Blank(c.DefaultCellStyle.Format),
                RightAlign: c.DefaultCellStyle.Alignment
                    is DataGridViewContentAlignment.MiddleRight
                    or DataGridViewContentAlignment.TopRight
                    or DataGridViewContentAlignment.BottomRight))
            .ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>(
            all?.Count ?? grid.Rows.Count);

        if (all is not null)
        {
            foreach (var item in all)
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                var properties = item is object?[] ? null : TypeDescriptor.GetProperties(item);

                foreach (var column in columns)
                    values[column.Name] = item is object?[] cells
                        ? column.Index < cells.Length ? cells[column.Index] : null
                        : properties?[column.DataPropertyName]?.GetValue(item);

                rows.Add(values);
            }

            return Result(rows, "Every row listed, not only the page on screen");
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            // The blank row a grid keeps at the bottom for new entries is
            // not data, and would export as a row of empty cells.
            if (row.IsNewRow) continue;

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var column in columns)
                values[column.Name] = row.Cells[column.Index].Value;

            rows.Add(values);
        }

        // Said plainly: this is the screen at the moment the button was
        // pressed, not a query run against the host.
        return Result(rows, "As shown on screen");

        ReportResult Result(List<IReadOnlyDictionary<string, object?>> data, string currency) => new(
            Title: title,
            Columns: headings,
            Rows: data,
            Totals: new Dictionary<string, object?>(StringComparer.Ordinal),
            Provenance: new ReportProvenance(
                RunByUsername: username,
                RunAtLocal: DateTime.Now,
                ParameterSummary: parameters,
                DataCurrency: currency));
    }

    private static string SuggestedName(string title)
    {
        var cleaned = string.Concat(title.Where(ch =>
            !Path.GetInvalidFileNameChars().Contains(ch)));

        return string.Format(CultureInfo.InvariantCulture,
            "{0}-{1:yyyyMMdd-HHmm}.xlsx", cleaned, DateTime.Now);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

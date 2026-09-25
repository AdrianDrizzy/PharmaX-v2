using System.Globalization;
using PharmaX.Application.Reporting;

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
    public static string? Offer(Form owner, IWorkbookWriter writer,
        DataGridView grid, string title, string parameters, string username)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.Rows.Count == 0) return null;

        using var save = new SaveFileDialog
        {
            FileName = SuggestedName(title),
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            Title = $"Export {title.ToLowerInvariant()}",
            DefaultExt = "xlsx",
            AddExtension = true
        };

        if (save.ShowDialog(owner) != DialogResult.OK) return null;

        var path = WorkbookPath(save.FileName);

        try
        {
            writer.WriteTo(Build(grid, title, parameters, username), path);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException)
        {
            // Most often the workbook is still open in Excel from the last
            // export. Said as that, not as a fault in PharmaX.
            MessageBox.Show(owner,
                $"{Path.GetFileName(path)} could not be saved: {ex.Message}"
                + "\n\nClose it if it is open in Excel, or choose another "
                + "name, and export again.",
                owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return null;
        }

        return path;
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
    internal static ReportResult Build(DataGridView grid, string title,
        string parameters, string username)
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
            grid.Rows.Count);

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

        return new ReportResult(
            Title: title,
            Columns: headings,
            Rows: rows,
            Totals: new Dictionary<string, object?>(StringComparer.Ordinal),
            Provenance: new ReportProvenance(
                RunByUsername: username,
                RunAtLocal: DateTime.Now,
                ParameterSummary: parameters,

                // Said plainly: this is the screen at the moment the
                // button was pressed, not a query run against the host.
                DataCurrency: "As shown on screen"));
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

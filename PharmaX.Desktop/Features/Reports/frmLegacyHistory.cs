using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Legacy;
using PharmaX.Application.Reporting;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

/// <summary>
/// The year of history taken over from DispenseWare and Retailware:
/// prescriptions, till sales, receipts, stock movements, price changes,
/// till overrides, cycle counts and cash-ups, searched in one place.
/// </summary>
/// <remarks>
/// Read-only, as the history is. A patient's old scripts are a file
/// number away - "RSI-1757" - and a product's movements a code away.
/// </remarks>
public sealed class frmLegacyHistory : PhxFormBase
{
    private static readonly (string Label, string? Kind)[] Kinds =
    {
        ("Everything", null), ("Prescriptions", "Script"), ("Till sales", "Sale"),
        ("Receipts", "Receipt"), ("Stock movements", "Stock"),
        ("Price changes", "Price"), ("Till price overrides", "Override"),
        ("Cycle counts", "Count"), ("Cash-ups", "DayEnd"), ("Z-reads", "ZRead")
    };

    private readonly ILegacyHistory _history;
    private readonly IWorkbookWriter _workbooks;
    private readonly ICurrentUser _user;

    private readonly ComboBox _kind = new();
    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();
    private readonly TextBox _text = new();
    private readonly Button _find = new();
    private readonly Button _export = new();
    private readonly Button _close = new();
    private readonly Label _count = new();
    private readonly PhxGrid _grid = new();

    private string? _patientFile;

    public frmLegacyHistory(ILegacyHistory history, IWorkbookWriter workbooks,
                            ICurrentUser user)
    {
        _history = history;
        _workbooks = workbooks;
        _user = user;

        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdHistory";
        _grid.AddColumn("When", "When", 70, "dd/MM/yyyy HH:mm");
        _grid.AddColumn("Kind", "Kind", 45);
        _grid.AddColumn("Number", "Number", 45);
        _grid.AddColumn("Product", "Product", 40);
        _grid.AddColumn("Patient", "Patient file", 50);
        _grid.AddColumn("Party", "Doctor, supplier or tender", 90);
        _grid.AddColumn("Description", "What", 220);
        _grid.AddColumn("Quantity", "Qty", 35, "0.###", rightAlign: true);
        _grid.AddColumn("Amount", "Amount", 45, "N2", rightAlign: true);
        _grid.AddColumn("Operator", "By", 40);
        Controls.Add(_grid);

        var filters = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 8, 12, 4) };

        filters.Controls.Add(new Label { AutoSize = true, Location = new Point(12, 14), Text = "&Show" });
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Location = new Point(60, 10);
        _kind.Name = "cboKind";
        _kind.Size = new Size(170, 24);
        foreach (var (label, _) in Kinds) _kind.Items.Add(label);
        _kind.SelectedIndex = 0;

        filters.Controls.Add(new Label { AutoSize = true, Location = new Point(246, 14), Text = "&From" });
        _from.Format = DateTimePickerFormat.Short;
        _from.Location = new Point(290, 10);
        _from.ShowCheckBox = true;
        _from.Checked = false;
        _from.Size = new Size(130, 24);

        filters.Controls.Add(new Label { AutoSize = true, Location = new Point(432, 14), Text = "&To" });
        _to.Format = DateTimePickerFormat.Short;
        _to.Location = new Point(458, 10);
        _to.ShowCheckBox = true;
        _to.Checked = false;
        _to.Size = new Size(130, 24);

        filters.Controls.Add(new Label { AutoSize = true, Location = new Point(604, 14), Text = "&Words" });
        _text.Location = new Point(656, 10);
        _text.Name = "txtWords";
        _text.PlaceholderText = "Product, patient, file no., doctor, e.g. RSI-1757";
        _text.Size = new Size(240, 24);

        _find.Location = new Point(910, 7);
        _find.Size = new Size(110, 30);
        _find.Text = "Find";
        _find.Click += async (_, _) => await FindAsync();
        PhxTheme.MakePrimary(_find);

        filters.Controls.AddRange(new Control[] { _kind, _from, _to, _text, _find });
        Controls.Add(filters);

        var header = PhxTheme.HeaderBand(64);
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8),
            Text = "RSI history"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Text = "What DispenseWare and Retailware recorded before PharmaX, as it "
                   + "was exported. Read only: none of it moves stock or money."
        });
        Controls.Add(header);

        Controls.Add(Footer(_count, _export, _close));

        _export.Size = new Size(130, 32);
        _export.Text = "Export";
        _export.Click += (_, _) => Export();

        _close.DialogResult = DialogResult.Cancel;
        _close.Size = new Size(110, 32);
        _close.Text = "Close";

        AcceptButton = _find;
        CancelButton = _close;
        ClientSize = new Size(1130, 640);
        MinimumSize = new Size(900, 480);
        StartPosition = FormStartPosition.CenterParent;

        // A year of history reads better across the whole screen.
        WindowState = FormWindowState.Maximized;
        Text = "PharmaX - RSI history";
    }

    /// <summary>
    /// A footer band with a message on the left and buttons on the right,
    /// docked rather than placed, so it holds together at any width.
    /// </summary>
    internal static Panel Footer(Label message, params Button[] buttons)
    {
        var footer = PhxTheme.FooterBand(56);

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        foreach (var button in buttons)
        {
            button.Margin = new Padding(8, 2, 0, 0);
            right.Controls.Add(button);
        }

        message.AutoSize = false;
        message.Dock = DockStyle.Fill;
        message.ForeColor = PhxTheme.Muted;
        message.TextAlign = ContentAlignment.MiddleLeft;

        // Fill first: docking is resolved from the last control added, so
        // the buttons take their room and the message what is left.
        footer.Controls.Add(message);
        footer.Controls.Add(right);
        return footer;
    }

    /// Opens on one patient's prescriptions, from the patient's screen.
    public void ShowPatient(string fileNumber)
    {
        _patientFile = fileNumber;
        _kind.SelectedIndex = 1;
        Text = $"PharmaX - RSI history - {fileNumber}";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await FindAsync();
    }

    private async Task FindAsync()
    {
        SetBusy(true);

        try
        {
            var query = new LegacyQuery(
                Kind: Kinds[Math.Max(0, _kind.SelectedIndex)].Kind,
                From: _from.Checked ? DateOnly.FromDateTime(_from.Value) : null,
                To: _to.Checked ? DateOnly.FromDateTime(_to.Value) : null,
                Text: _text.Text,
                PatientFile: _patientFile);

            var rows = await _history.SearchAsync(query);

            // Built first and added in one go: a thousand single Adds make
            // the grid lay itself out a thousand times.
            var built = rows.Select(r =>
            {
                var row = new DataGridViewRow();
                row.CreateCells(_grid, r.OccurredAt, KindLabel(r.Kind), r.Number,
                    r.ProductCode, r.PatientFile, r.Party, r.Description, r.Quantity,
                    r.Amount, r.Operator);
                return row;
            }).ToArray();

            _grid.Rows.Clear();
            _grid.Rows.AddRange(built);

            _count.Text = rows.Count == 0
                ? "Nothing found. The RSI history is loaded by Administration > Import from RSI."
                : rows.Count >= query.Limit
                    ? string.Create(CultureInfo.CurrentCulture,
                        $"The latest {rows.Count:N0} - narrow the search to see older ones")
                    : string.Create(CultureInfo.CurrentCulture, $"{rows.Count:N0} found");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string KindLabel(string kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind).Label ?? kind;

    private void Export()
    {
        var saved = GridExport.Offer(this, _workbooks, _grid, "RSI history",
            $"{_kind.Text} {_text.Text}".Trim(), _user.Username);

        if (saved is not null) ShowSaved(saved);
        else if (_grid.Rows.Count == 0)
            ShowInfo("There is nothing on the screen to export.");
    }
}

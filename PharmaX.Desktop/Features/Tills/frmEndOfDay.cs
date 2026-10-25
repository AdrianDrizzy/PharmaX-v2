using PharmaX.Application.Reporting;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

/// <summary>
/// End-of-Day Cash-Up: one day, every till and cashier - headline figures,
/// then a tab each for the tills, the tenders, the cashiers, every
/// transaction and the graph - and the whole of it to PDF or Excel.
/// </summary>
public sealed class frmEndOfDay : PhxFormBase, IEndOfDayView
{
    internal const string Guidance =
        "1. Every till should be cashed up (\"Still open\" counts those that are not; File > Cash Up closes one "
        + "for its cashier).  2. Money out records drawings, lunch and expenses taken from a till still open.  "
        + "3. Check each Variance: counted less expected, a minus is short.  4. Export to PDF or Excel.";

    private readonly EndOfDayPresenter? _presenter;
    private readonly ViewFactory? _views;
    private readonly DateTimePicker _day = new();
    private readonly FlowLayoutPanel _figures = new();
    private readonly TabControl _tabs = new();
    private readonly Label _status = new();

    /// Designer and tests only.
    public frmEndOfDay() => Build();

    public frmEndOfDay(EndOfDayPresenter presenter, ViewFactory views)
    {
        _presenter = presenter;
        _views = views;
        Build();
        _presenter.Attach(this);
    }

    protected override bool MayOpen(Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.SessionCloseOtherUser,
            Domain.Security.PermissionCode.ReportFinancial);

    private void Build()
    {
        Text = "PharmaX - End-of-Day Cash-Up";
        Name = "frmEndOfDay";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(860, 520);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var guide = new Label
        {
            AutoSize = false,
            BackColor = PhxTheme.Information,
            Dock = DockStyle.Top,
            Height = 64,
            Name = "lblGuide",
            Padding = new Padding(12, 6, 12, 6),
            Text = Guidance
        };

        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 2),
            WrapContents = false
        };

        _day.Format = DateTimePickerFormat.Long;
        _day.Name = "dtpDay";
        _day.Value = DateTime.Today;
        _day.Width = 230;

        var show = new Button { AutoSize = true, Name = "btnShow", Text = "&Show (F12)" };
        show.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _status.AutoSize = true;
        _status.ForeColor = PhxTheme.Muted;
        _status.Margin = new Padding(12, 7, 3, 0);
        _status.Name = "lblStatus";

        bar.Controls.Add(new Label { AutoSize = true, Margin = new Padding(3, 7, 6, 0), Text = "&Day" });
        bar.Controls.Add(_day);
        bar.Controls.Add(show);
        bar.Controls.Add(_status);

        _figures.AutoSize = true;
        _figures.Dock = DockStyle.Top;
        _figures.Name = "pnlFigures";
        _figures.Padding = new Padding(8, 4, 8, 4);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Name = "tabSections";

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 2, 10, 2) };
        body.Controls.Add(_tabs);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 6, 10, 8)
        };

        var close = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Name = "btnClose", Padding = new Padding(8, 2, 8, 2), Text = "Close" };
        close.Click += (_, _) => CloseView(false);
        var excel = new Button { AutoSize = true, Name = "btnExcel", Padding = new Padding(8, 2, 8, 2), Text = "Export &Excel" };
        excel.Click += (_, _) => ExportExcelRequested?.Invoke(this, EventArgs.Empty);
        var pdf = new Button { AutoSize = true, Name = "btnPdf", Padding = new Padding(8, 2, 8, 2), Text = "Export P&DF" };
        pdf.Click += (_, _) => ExportPdfRequested?.Invoke(this, EventArgs.Empty);
        var payouts = new Button { AutoSize = true, Name = "btnPayouts", Padding = new Padding(8, 2, 8, 2), Text = "&Money out..." };
        payouts.Click += (_, _) => PayoutsRequested?.Invoke(this, EventArgs.Empty);

        buttons.Controls.Add(close);
        buttons.Controls.Add(excel);
        buttons.Controls.Add(pdf);
        buttons.Controls.Add(payouts);

        Controls.Add(body);
        Controls.Add(_figures);
        Controls.Add(bar);
        Controls.Add(guide);
        Controls.Add(buttons);

        CancelButton = close;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        frmCashUp.FitGuide(this);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F12)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public DateOnly Day => DateOnly.FromDateTime(_day.Value);

    public void ShowReport(ReportResult report, byte[]? chartPng)
    {
        ArgumentNullException.ThrowIfNull(report);

        _status.Text = report.Provenance.DataCurrency;

        _figures.SuspendLayout();
        _figures.Controls.Clear();

        // Built after the window has scaled, so sized from the fonts in
        // use rather than in 96-dpi pixels (the 150% display clipped them).
        var big = new Font(Font.FontFamily, Font.Size * 1.35f, FontStyle.Bold);
        var tileWidth = TextRenderer.MeasureText("Account payments 0000", Font).Width + 20;

        foreach (var figure in report.Figures)
        {
            var tile = new Panel
            {
                BackColor = PhxTheme.Band,
                Margin = new Padding(4),
                Padding = new Padding(8, 4, 8, 4),
                Size = new Size(tileWidth, Font.Height + big.Height + 16)
            };
            tile.Controls.Add(new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = big,
                Text = figure.Value,
                TextAlign = ContentAlignment.MiddleLeft
            });
            tile.Controls.Add(new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                ForeColor = PhxTheme.Muted,
                Height = Font.Height + 2,
                Text = figure.Label
            });
            _figures.Controls.Add(tile);
        }

        _figures.ResumeLayout();

        foreach (TabPage old in _tabs.TabPages) old.Dispose();
        _tabs.TabPages.Clear();

        AddTable(report.RowCount == 0 ? "By till - no till traded that day" : "By till",
            new ReportSection("", report.Columns, report.Rows));

        foreach (var section in report.Sections) AddTable(section.Title, section);

        if (chartPng is not null)
        {
            var page = new TabPage("Graph") { BackColor = Color.White };
            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = Image.FromStream(new MemoryStream(chartPng)),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            page.Controls.Add(picture);
            _tabs.TabPages.Add(page);
        }
    }

    private void AddTable(string title, ReportSection section)
    {
        var page = new TabPage(title);
        var grid = new PhxGrid { Dock = DockStyle.Fill };

        foreach (var column in section.Columns)
            grid.AddColumn(column.Key, column.Header, column.Key == "Items" ? 220 : column.RightAlign ? 90 : 110,
                           column.Format, column.RightAlign);

        foreach (var row in section.Rows)
        {
            var index = grid.Rows.Add(section.Columns
                .Select(c => row.TryGetValue(c.Key, out var v) ? v : null).ToArray());

            if (row.TryGetValue("Variance", out var variance) && variance is decimal d && d < 0m)
                grid.Rows[index].Cells["Variance"].Style.ForeColor = PhxTheme.Negative;
        }

        page.Controls.Add(grid);
        _tabs.TabPages.Add(page);

        // Each column as wide as its heading and values; it scrolls sideways
        // rather than cutting "Variance" to "Vari...".
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
    }

    public string? PromptSavePath(string suggestedName, bool pdf)
    {
        using var save = new SaveFileDialog
        {
            DefaultExt = pdf ? "pdf" : "xlsx",
            FileName = suggestedName,
            Filter = pdf ? "PDF document (*.pdf)|*.pdf" : "Excel workbook (*.xlsx)|*.xlsx",
            OverwritePrompt = true,
            Title = "Export the end-of-day cash-up",
            InitialDirectory = GridExport.Downloads
        };

        return save.ShowDialog(this) == DialogResult.OK ? save.FileName : null;
    }

    public bool ShowPayouts()
    {
        if (_views is null) return false;

        using var payouts = _views.Create<frmPayouts>();
        payouts.ShowDialog(this);
        return payouts.Changed;
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? ExportPdfRequested;
    public event EventHandler? ExportExcelRequested;
    public event EventHandler? PayoutsRequested;
}

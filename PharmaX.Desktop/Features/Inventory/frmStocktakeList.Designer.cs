using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Inventory;

partial class frmStocktakeList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblSummary = null!;
    private PhxGrid grdStocktakes = null!;
    private Button btnNew = null!;
    private Button btnContinue = null!;
    private Button btnCancelStocktake = null!;
    private Button btnRefresh = null!;
    private Button btnClose = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlTop = new Panel();
        pnlBottom = new Panel();
        lblSummary = new Label();
        grdStocktakes = new PhxGrid();
        btnNew = new Button();
        btnNew.Name = "btnNew";
        btnContinue = new Button();
        btnContinue.Name = "btnContinue";
        btnCancelStocktake = new Button();
        btnCancelStocktake.Name = "btnCancelStocktake";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblSummary.AutoSize = false;
        lblSummary.Font = new Font("Segoe UI", 10F);
        lblSummary.Location = new Point(12, 14);
        lblSummary.Name = "lblSummary";
        lblSummary.Size = new Size(700, 20);

        pnlTop.Controls.Add(lblSummary);
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 42;
        pnlTop.Name = "pnlTop";

        grdStocktakes.AutoGenerateColumns = false;
        grdStocktakes.Dock = DockStyle.Fill;
        grdStocktakes.Name = "grdStocktakes";

        Bind(grdStocktakes, "StocktakeNumber", "Number", 90);
        Bind(grdStocktakes, "Scope", "Scope", 200);
        Bind(grdStocktakes, "OpenedAtUtc", "Opened", 120,
             "dd/MM/yyyy HH:mm");
        Bind(grdStocktakes, "Status", "Status", 80);
        Bind(grdStocktakes, "TotalLines", "Lines", 50, rightAlign: true);
        Bind(grdStocktakes, "CountedLines", "Counted", 60, rightAlign: true);
        Bind(grdStocktakes, "UncountedLines", "Left", 45, rightAlign: true);
        Bind(grdStocktakes, "NetVarianceValue", "Variance", 80, "N2", true);

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnNew, "&New count"), (btnContinue, "&Continue"),
            (btnCancelStocktake, "Cance&l"), (btnRefresh, "&Refresh")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(110, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 116;
        }

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(700, 8);
        btnClose.Size = new Size(110, 30);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 48;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(830, 480);
        Controls.Add(grdStocktakes);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmStocktakeList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Stocktake";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Bind(DataGridView grid, string property,
        string header, int fillWeight, string? format = null,
        bool rightAlign = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (format is not null) column.DefaultCellStyle.Format = format;

        if (rightAlign)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}

using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Dispensing;

partial class frmScriptList
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Label lblPatient = null!;
    private Label lblCount = null!;
    private PhxGrid grdScripts = null!;
    private Button btnCapture = null!;
    private Button btnCancelScript = null!;
    private Button btnEditScript = null!;
    private Button btnRefresh = null!;
    private Button btnChoose = null!;
    private Button btnQuote = null!;
    private Button btnUseAgain = null!;
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
        lblPatient = new Label();
        lblCount = new Label();
        grdScripts = new PhxGrid();
        btnCapture = new Button();
        btnCapture.Name = "btnCapture";
        btnCancelScript = new Button();
        btnCancelScript.Name = "btnCancelScript";
        btnEditScript = new Button();
        btnEditScript.Name = "btnEditScript";
        btnRefresh = new Button();
        btnRefresh.Name = "btnRefresh";
        btnChoose = new Button();
        btnChoose.Name = "btnChoose";
        btnQuote = new Button();
        btnQuote.Name = "btnQuote";
        btnUseAgain = new Button();
        btnUseAgain.Name = "btnUseAgain";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblPatient.AutoSize = false;
        lblPatient.AutoEllipsis = true;
        lblPatient.Anchor = AnchorStyles.Top | AnchorStyles.Left
                            | AnchorStyles.Right;
        lblPatient.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblPatient.Location = new Point(12, 14);
        lblPatient.Name = "lblPatient";
        lblPatient.Size = new Size(600, 24);

        lblCount.AutoSize = false;
        lblCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblCount.Location = new Point(620, 16);
        lblCount.Size = new Size(150, 20);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        pnlTop.Controls.AddRange(new Control[] { lblCount, lblPatient });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 46;
        pnlTop.Name = "pnlTop";

        grdScripts.AutoGenerateColumns = false;
        grdScripts.Dock = DockStyle.Fill;
        grdScripts.Name = "grdScripts";

        Bind(grdScripts, "ScriptNumber", "Script", 100);
        Bind(grdScripts, "PrescriberName", "Prescriber", 160);
        Bind(grdScripts, "PrescribedOn", "Prescribed", 90, "dd/MM/yyyy");
        Bind(grdScripts, "ValidUntil", "Valid until", 90, "dd/MM/yyyy");
        Bind(grdScripts, "RepeatsAuthorised", "Repeats", 60,
             rightAlign: true);
        Bind(grdScripts, "RepeatsRemaining", "Left", 50, rightAlign: true);
        Bind(grdScripts, "Status", "Status", 100);

        var x = 12;
        foreach (var (button, text) in new[]
        {
            (btnCapture, "&Capture new"), (btnEditScript, "&Edit script"),
            (btnCancelScript, "Cance&l script"), (btnRefresh, "&Refresh"),
            (btnQuote, "&Quote"), (btnUseAgain, "&Use again")
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(120, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            pnlBottom.Controls.Add(button);
            x += 126;
        }

        btnChoose.Location = new Point(x, 8);
        btnChoose.Size = new Size(120, 30);
        btnChoose.Text = "C&hoose";
        btnChoose.UseVisualStyleBackColor = true;
        btnChoose.Visible = false;
        pnlBottom.Controls.Add(btnChoose);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(930, 8);
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
        ClientSize = new Size(1060, 460);
        Controls.Add(grdScripts);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);
        Name = "frmScriptList";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Scripts";
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

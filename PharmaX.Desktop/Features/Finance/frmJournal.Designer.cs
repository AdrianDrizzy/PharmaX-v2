using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

partial class frmJournal
{
    private System.ComponentModel.IContainer components = null!;
    private Panel pnlTop = null!;
    private Panel pnlBottom = null!;
    private Panel pnlTotals = null!;
    private Label lblDate = null!;
    private Label lblNarration = null!;
    private Label lblTotals = null!;
    private DateTimePicker dtpPostingDate = null!;
    private TextBox txtNarration = null!;
    private PhxGrid grdLines = null!;
    private DataGridViewComboBoxColumn colAccount = null!;
    private Button btnPost = null!;
    private Button btnClear = null!;
    private Button btnAddLine = null!;
    private Button btnRemoveLine = null!;
    private Button btnExport = null!;
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
        pnlTotals = new Panel();
        lblDate = new Label();
        lblNarration = new Label();
        lblTotals = new Label();
        dtpPostingDate = new DateTimePicker();
        txtNarration = new TextBox();
        grdLines = new PhxGrid();
        btnPost = new Button();
        btnPost.Name = "btnPost";
        btnClear = new Button();
        btnAddLine = new Button();
        btnRemoveLine = new Button();
        btnClear.Name = "btnClear";
        btnExport = new Button();
        btnExport.Name = "btnExport";
        btnClose = new Button();
        btnClose.Name = "btnClose";
        SuspendLayout();

        lblDate.AutoSize = true;
        lblDate.Location = new Point(12, 17);
        lblDate.Text = "&Date";

        dtpPostingDate.Format = DateTimePickerFormat.Custom;
        dtpPostingDate.CustomFormat = "dd/MM/yyyy";
        dtpPostingDate.Location = new Point(90, 13);
        dtpPostingDate.Name = "dtpPostingDate";
        dtpPostingDate.Size = new Size(130, 23);
        dtpPostingDate.TabIndex = 0;

        lblNarration.AutoSize = true;
        lblNarration.Location = new Point(12, 51);
        lblNarration.Text = "&Reason";

        txtNarration.Location = new Point(90, 47);
        txtNarration.MaxLength = 300;
        txtNarration.Name = "txtNarration";
        txtNarration.PlaceholderText =
            "Why this journal is being posted - month-end stock, "
            + "correction, opening balance";
        txtNarration.Size = new Size(680, 23);
        txtNarration.TabIndex = 1;

        pnlTop.Controls.AddRange(new Control[]
            { txtNarration, lblNarration, dtpPostingDate, lblDate });
        pnlTop.Dock = DockStyle.Top;
        pnlTop.Height = 84;
        pnlTop.Name = "pnlTop";

        // Typed in, rather than bound: a journal is captured line by
        // line and the grid is the entry form.
        colAccount = new DataGridViewComboBoxColumn
        {
            HeaderText = "Account",
            Name = "Account",
            FillWeight = 300,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,

            // Typing "5900" jumps to the account rather than opening the
            // list and scrolling: a journal is keyed, not moused.
            AutoComplete = true
        };

        grdLines.AutoGenerateColumns = false;
        grdLines.AllowUserToAddRows = true;
        grdLines.AllowUserToDeleteRows = true;
        grdLines.Dock = DockStyle.Fill;
        grdLines.EditMode = DataGridViewEditMode.EditOnEnter;
        grdLines.MultiSelect = false;
        grdLines.Name = "grdLines";
        grdLines.RowTemplate.Height = 28;

        grdLines.Columns.Add(colAccount);
        Money(grdLines, "Debit");
        Money(grdLines, "Credit");

        grdLines.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Note",
            Name = "Note",
            FillWeight = 260,
            MaxInputLength = 300
        });

        // Its own row, not docked inside the button panel. Docked to the
        // top of a panel whose buttons are absolutely positioned, the
        // label spanned the full width and the Close button sat on top
        // of it, cutting the credits figure in half.
        lblTotals.AutoSize = false;
        lblTotals.Dock = DockStyle.Fill;
        lblTotals.Font = PhxTheme.Subheading;
        lblTotals.Padding = new Padding(0, 0, 16, 0);
        lblTotals.TextAlign = ContentAlignment.MiddleRight;

        pnlTotals.Controls.Add(lblTotals);
        pnlTotals.Dock = DockStyle.Bottom;
        pnlTotals.Height = 34;
        pnlTotals.Name = "pnlTotals";

        btnPost.Location = new Point(12, 8);
        btnPost.Size = new Size(150, 34);
        btnPost.Text = "&Post Journal";
        btnPost.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(btnPost);
        pnlBottom.Controls.Add(btnPost);

        btnClear.Location = new Point(170, 8);
        btnClear.Size = new Size(110, 34);
        btnClear.Text = "C&lear";
        btnClear.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClear);

        // A line keyed through a dialog, for the keyboard and for a screen
        // reader, beside the grid that takes typing.
        btnAddLine.Location = new Point(288, 8);
        btnAddLine.Name = "btnAddLine";
        btnAddLine.Size = new Size(110, 34);
        btnAddLine.Text = "&Add line...";
        btnAddLine.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnAddLine);

        btnRemoveLine.Location = new Point(406, 8);
        btnRemoveLine.Name = "btnRemoveLine";
        btnRemoveLine.Size = new Size(88, 34);
        btnRemoveLine.Text = "Re&move";
        btnRemoveLine.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnRemoveLine);

        btnExport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnExport.Location = new Point(502, 8);
        btnExport.Size = new Size(150, 34);
        btnExport.Text = "&Export to Excel";
        btnExport.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnExport);

        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(660, 8);
        btnClose.Size = new Size(110, 34);
        btnClose.Text = "&Close";
        btnClose.UseVisualStyleBackColor = true;
        pnlBottom.Controls.Add(btnClose);

        pnlBottom.Dock = DockStyle.Bottom;
        pnlBottom.Height = 52;
        pnlBottom.Name = "pnlBottom";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(790, 520);
        Controls.Add(grdLines);
        Controls.Add(pnlTop);
        Controls.Add(pnlTotals);
        Controls.Add(pnlBottom);
        Name = "frmJournal";
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Journal entry";
        ResumeLayout(false);
        PerformLayout();
    }

    private static void Money(DataGridView grid, string name)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = name,
            Name = name,
            FillWeight = 120,
            MaxInputLength = 15
        };

        column.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(column);
    }
}

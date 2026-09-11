namespace PharmaX.Desktop.Controls;

partial class ScanBar
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblPrompt = null!;
    private TextBox txtScan = null!;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblPrompt = new Label();
        txtScan = new TextBox();
        SuspendLayout();

        // lblPrompt
        lblPrompt.AutoSize = false;
        lblPrompt.Dock = DockStyle.Left;
        lblPrompt.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        lblPrompt.Location = new Point(0, 0);
        lblPrompt.Name = "lblPrompt";
        lblPrompt.Size = new Size(90, 44);
        lblPrompt.TabIndex = 0;
        lblPrompt.Text = "Scan";
        lblPrompt.TextAlign = ContentAlignment.MiddleLeft;

        // txtScan
        txtScan.BorderStyle = BorderStyle.FixedSingle;
        txtScan.Dock = DockStyle.Fill;
        txtScan.Font = new Font("Consolas", 14F);
        txtScan.Location = new Point(90, 0);
        txtScan.Name = "txtScan";
        txtScan.Size = new Size(510, 29);
        txtScan.TabIndex = 1;

        // ScanBar
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(txtScan);
        Controls.Add(lblPrompt);
        Name = "ScanBar";
        Padding = new Padding(6, 8, 6, 8);
        Size = new Size(600, 44);
        ResumeLayout(false);
        PerformLayout();
    }
}

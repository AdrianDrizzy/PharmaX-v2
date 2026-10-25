using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reports;

/// <summary>
/// A report's graph on its own, filling the screen: opened from the
/// Reports preview by Enlarge graph or a double-click. It opens maximised,
/// can be put back to a window and resized, and saves as a picture.
/// </summary>
public sealed class frmGraph : PhxFormBase
{
    private readonly PictureBox _picture = new();
    private readonly byte[] _png = Array.Empty<byte>();
    private readonly string _title = "";

    /// Designer and tests only.
    public frmGraph() => Build();

    public frmGraph(string title, byte[] png)
    {
        _title = title;
        _png = png;
        Build();
        _picture.Image = Image.FromStream(new MemoryStream(png));
    }

    private void Build()
    {
        Text = "PharmaX - Graph" + (_title.Length > 0 ? " - " + _title : "");
        Name = "frmGraph";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 560);
        MinimumSize = new Size(600, 360);
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;

        _picture.BackColor = Color.White;
        _picture.Dock = DockStyle.Fill;
        _picture.Name = "picGraph";
        _picture.SizeMode = PictureBoxSizeMode.Zoom;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 6, 10, 8)
        };

        var close = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Name = "btnClose", Padding = new Padding(8, 2, 8, 2), Text = "Close" };
        close.Click += (_, _) => Close();
        var save = new Button { AutoSize = true, Name = "btnSave", Padding = new Padding(8, 2, 8, 2), Text = "&Save picture..." };
        save.Click += (_, _) => SavePicture();

        buttons.Controls.Add(close);
        buttons.Controls.Add(save);
        buttons.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = PhxTheme.Muted,
            Margin = new Padding(3, 9, 24, 0),
            Text = "Use the window's restore button to make it smaller."
        });

        Controls.Add(_picture);
        Controls.Add(buttons);

        CancelButton = close;
    }

    /// The graph as a PNG wherever the operator chooses, for an email or a slide.
    private void SavePicture()
    {
        using var dialog = new SaveFileDialog
        {
            DefaultExt = "png",
            FileName = string.Concat((_title.Length > 0 ? _title : "Graph")
                .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))) + ".png",
            Filter = "PNG picture (*.png)|*.png",
            OverwritePrompt = true,
            Title = "Save the graph",
            InitialDirectory = GridExport.Downloads
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        File.WriteAllBytes(dialog.FileName, _png);
        ShowSaved(dialog.FileName);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _picture.Image?.Dispose();
        base.Dispose(disposing);
    }
}

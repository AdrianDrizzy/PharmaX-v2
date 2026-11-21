using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Asks for the batch number and expiry printed on a box: at dispensing,
/// when nothing is on a batch, and before a label would print without
/// an expiry.
/// </summary>
internal static class BatchPrompt
{
    /// <param name="askBatch">False to ask for the expiry alone.</param>
    public static (string Batch, DateOnly Expiry)? Ask(IWin32Window owner,
        string message, bool askBatch = true, string okText = "OK",
        string cancelText = "Cancel")
    {
        using var form = new Form
        {
            Text = WindowTitles.Brand("PharmaX - Batch on the box"),
            Icon = PhxIcons.AppIcon,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(420, askBatch ? 210 : 170)
        };
        PhxTheme.Follow(form);

        var text = new Label
        {
            Text = message, Location = new Point(16, 14),
            Size = new Size(388, 56), AutoSize = false
        };

        var y = 76;
        var batch = new TextBox { Location = new Point(120, y), Width = 180, MaxLength = 50,
                                  AccessibleName = "Batch number" };
        if (askBatch)
        {
            form.Controls.Add(new Label { Text = "&Batch", AutoSize = true,
                                          Location = new Point(16, y + 3) });
            form.Controls.Add(batch);
            y += 34;
        }

        var expiry = new DateTimePicker
        {
            Location = new Point(120, y), Width = 180, Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd/MM/yyyy", Value = DateTime.Today.AddMonths(6),
            AccessibleName = "Expiry date"
        };
        form.Controls.Add(new Label { Text = "&Expiry", AutoSize = true,
                                      Location = new Point(16, y + 3) });

        var ok = new Button { Text = okText, DialogResult = DialogResult.OK,
                              Location = new Point(196, y + 46), Size = new Size(100, 32) };
        var cancel = new Button { Text = cancelText, DialogResult = DialogResult.Cancel,
                                  Location = new Point(304, y + 46), Size = new Size(100, 32) };
        PhxTheme.MakePrimary(ok);

        form.Controls.AddRange(new Control[] { text, expiry, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        // A blank batch is not a batch; keep the box open until one is typed.
        ok.Click += (_, _) =>
        {
            if (askBatch && batch.Text.Trim().Length == 0)
            {
                form.DialogResult = DialogResult.None;
                batch.Focus();
            }
        };

        return form.ShowDialog(owner) == DialogResult.OK
            ? (batch.Text.Trim(), DateOnly.FromDateTime(expiry.Value))
            : null;
    }
}

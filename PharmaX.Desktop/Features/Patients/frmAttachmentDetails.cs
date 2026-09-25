using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Patients;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// Asks what the scan just chosen actually is, before it is filed.
/// </summary>
/// <remarks>
/// One small dialog rather than filing everything as "Other" and
/// expecting the operator to rename it afterwards. A patient file with
/// twelve documents in it is only useful if the pharmacist can see at a
/// glance which one is the prescription.
/// </remarks>
internal sealed class frmAttachmentDetails : Form
{
    private readonly ComboBox _kind = new();
    private readonly TextBox _description = new();
    private readonly Button _ok = new();
    private readonly Button _cancel = new();

    private frmAttachmentDetails(string suggestedDescription)
    {
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, 22),
            Text = "&What is it"
        });

        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Location = new Point(130, 18);
        _kind.Size = new Size(250, 24);

        foreach (var kind in Enum.GetValues<AttachmentKind>())
            _kind.Items.Add(kind);

        _kind.SelectedItem = AttachmentKind.Prescription;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(20, 62),
            Text = "&Description"
        });

        _description.Location = new Point(130, 58);
        _description.Size = new Size(250, 24);
        _description.Text = suggestedDescription;

        _ok.DialogResult = DialogResult.OK;
        _ok.Location = new Point(198, 104);
        _ok.Size = new Size(90, 32);
        _ok.Text = "&File it";
        PhxTheme.MakePrimary(_ok);

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(296, 104);
        _cancel.Size = new Size(90, 32);
        _cancel.Text = "Cancel";

        Controls.AddRange(new Control[]
            { _kind, _description, _ok, _cancel });

        AcceptButton = _ok;
        CancelButton = _cancel;
        ClientSize = new Size(408, 154);
        Font = PhxTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand("PharmaX - File a scan");
    }

    /// <summary>
    /// Returns what the operator said it is, or null if they backed out.
    /// </summary>
    internal static (AttachmentKind Kind, string Description)? Ask(
        IWin32Window owner, string suggestedDescription)
    {
        using var dialog = new frmAttachmentDetails(suggestedDescription);

        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;

        var kind = dialog._kind.SelectedItem is AttachmentKind chosen
            ? chosen
            : AttachmentKind.Other;

        var description = dialog._description.Text.Trim();

        return (kind, description.Length > 0 ? description : kind.ToString());
    }
}

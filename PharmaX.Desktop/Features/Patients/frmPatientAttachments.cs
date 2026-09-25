using System.Diagnostics;
using PharmaX.Application.Patients;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Patients;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// The scans in one patient's file: the paper prescriptions, identity
/// documents and medical aid cards that were presented at the counter.
/// </summary>
/// <remarks>
/// Built by hand rather than through a presenter, like the other small
/// administrative dialogs. There is one service call per button and no
/// state worth the ceremony of a passive view.
/// </remarks>
public sealed class frmPatientAttachments : PhxFormBase
{
    private readonly IAttachmentService _attachments;

    private readonly PhxGrid _grid = new();
    private readonly Label _patient = new();
    private readonly Label _hint = new();
    private readonly Button _add = new();
    private readonly Button _open = new();
    private readonly Button _remove = new();
    private readonly Button _close = new();

    private Guid _patientId;
    private IReadOnlyList<AttachmentDto> _rows = Array.Empty<AttachmentDto>();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PatientMaintain;

    public frmPatientAttachments(IAttachmentService attachments)
    {
        _attachments = attachments;

        BuildGrid();
        BuildHeader();
        BuildButtons();

        CancelButton = _close;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(700, 420);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Patient documents";
    }

    /// <summary>
    /// Which patient's file to show. Called before the dialog opens.
    /// </summary>
    public void LoadForPatient(Guid patientId, string patientName)
    {
        _patientId = patientId;
        _patient.Text = patientName;
    }

    // ---- Layout ----------------------------------------------------------

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdAttachments";
        _grid.AddColumn("Captured", "Filed", 120);
        _grid.AddColumn("Kind", "Kind", 100);
        _grid.AddColumn("Description", "Description", 260);
        _grid.AddColumn("Size", "Size", 70, rightAlign: true);
        _grid.AddColumn("Where", "Image", 130);
        _grid.DoubleClick += (_, _) => OpenSelected();

        Controls.Add(_grid);
    }

    private void BuildHeader()
    {
        var band = PhxTheme.HeaderBand(66);

        _patient.AutoSize = false;
        _patient.Dock = DockStyle.Top;
        _patient.Font = PhxTheme.Heading;
        _patient.Height = 28;
        _patient.Name = "lblPatient";

        _hint.AutoSize = false;
        _hint.Dock = DockStyle.Top;
        _hint.ForeColor = PhxTheme.Muted;
        _hint.Height = 20;
        _hint.Name = "lblHint";
        _hint.Text = "Scans are kept on this workstation. Include the "
                     + "PharmaX attachment folder in the pharmacy's backup.";

        band.Controls.Add(_hint);
        band.Controls.Add(_patient);

        Controls.Add(PhxTheme.Rule(DockStyle.Top));
        Controls.Add(band);
    }

    private void BuildButtons()
    {
        var band = PhxTheme.FooterBand(56);

        var x = PhxTheme.Gutter;

        foreach (var (button, text) in new[]
        {
            (_add, "&Add scan..."), (_open, "&Open"), (_remove, "&Remove")
        })
        {
            button.Location = new Point(x, 11);
            button.Size = new Size(130, 32);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            band.Controls.Add(button);
            x += 138;
        }

        _add.Click += async (_, _) => await AddAsync();
        _open.Click += (_, _) => OpenSelected();
        _remove.Click += async (_, _) => await RemoveAsync();

        PhxTheme.MakePrimary(_add);

        _close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _close.Location = new Point(700, 11);
        _close.Size = new Size(100, 32);
        _close.Text = "&Close";
        _close.UseVisualStyleBackColor = true;
        _close.Click += (_, _) => CloseView(true);
        band.Controls.Add(_close);

        Controls.Add(band);
    }

    // ---- Behaviour --------------------------------------------------------

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var result = await _attachments.ForPatientAsync(_patientId);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        _rows = result.Value;
        _grid.Rows.Clear();

        foreach (var a in _rows)
        {
            var index = _grid.Rows.Add(
                Format.DateTimeShort(a.CapturedAtUtc.ToLocalTime()),
                a.Kind.ToString(), a.Description, a.SizeForDisplay,
                a.IsOnThisWorkstation ? "On this till" : "Filed elsewhere");

            // A row whose image is on another workstation is a record of
            // something that exists, not something this till can show.
            if (!a.IsOnThisWorkstation)
                _grid.Rows[index].DefaultCellStyle.ForeColor =
                    SystemColors.GrayText;
        }

        _open.Enabled = _remove.Enabled = _rows.Count > 0;
    }

    private AttachmentDto? Selected =>
        _grid.CurrentRow is { Index: >= 0 and var i } && i < _rows.Count
            ? _rows[i]
            : null;

    private async Task AddAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Scans and photographs|*.png;*.jpg;*.jpeg;*.tif;"
                     + "*.tiff;*.bmp;*.pdf|All files|*.*",
            Title = "Choose the scanned document"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var kind = frmAttachmentDetails.Ask(this,
            Path.GetFileNameWithoutExtension(dialog.FileName));

        if (kind is null) return;

        SetBusy(true);

        var result = await _attachments.AddAsync(_patientId, dialog.FileName,
            kind.Value.Kind, kind.Value.Description);

        SetBusy(false);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Hands the file to Windows, which knows what opens a PNG or a PDF
    /// far better than a viewer written here would.
    /// </summary>
    private async void OpenSelected()
    {
        if (Selected is not AttachmentDto chosen) return;

        var located = await _attachments.LocateAsync(chosen.Id);

        if (located.IsFailure)
        {
            ShowError(located.Error.Message);
            return;
        }

        try
        {
            using var opening = Process.Start(new ProcessStartInfo
            {
                FileName = located.Value,
                UseShellExecute = true
            });
        }
#pragma warning disable CA1031 // A shell association can throw anything.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError("Windows could not open that file: " + ex.Message);
        }
    }

    private async Task RemoveAsync()
    {
        if (Selected is not AttachmentDto chosen) return;

        if (!Confirm($"Remove \"{chosen.Description}\" from this patient's "
                     + "file? The image is deleted and cannot be recovered."))
            return;

        var result = await _attachments.RemoveAsync(chosen.Id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        await RefreshAsync();
    }
}

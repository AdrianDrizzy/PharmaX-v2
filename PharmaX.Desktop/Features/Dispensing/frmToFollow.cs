using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Medicine owed to patients: issued and charged in full when the shelf
/// was short, handed over from here when the stock comes in.
/// </summary>
public sealed class frmToFollow : PhxFormBase
{
    private readonly IDispensingService _dispensing;
    private readonly ILabelPrinter _labels;
    private readonly PhxGrid _grid = new();
    private readonly Label _count = new();
    private readonly Button _give = new();
    private readonly Button _close = new();
    private IReadOnlyList<ToFollowDto> _owed = Array.Empty<ToFollowDto>();

    public frmToFollow(IDispensingService dispensing, ILabelPrinter labels)
    {
        _dispensing = dispensing;
        _labels = labels;

        _grid.AutoGenerateColumns = false;
        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdToFollow";
        _grid.AddColumn("Since", "Since", 70, "dd/MM/yyyy").DataPropertyName = nameof(ToFollowDto.Since);
        _grid.AddColumn("Patient", "Patient", 140).DataPropertyName = nameof(ToFollowDto.Patient);
        _grid.AddColumn("Telephone", "Telephone", 80).DataPropertyName = nameof(ToFollowDto.Telephone);
        _grid.AddColumn("Description", "Owed", 220).DataPropertyName = nameof(ToFollowDto.Description);
        _grid.AddColumn("Owed", "Qty", 40, "0.###", rightAlign: true).DataPropertyName = nameof(ToFollowDto.Owed);
        _grid.AddColumn("Document", "Dispensing", 70).DataPropertyName = nameof(ToFollowDto.DocumentNumber);
        Controls.Add(_grid);

        var header = PhxTheme.HeaderBand(64);
        header.Controls.Add(new Label
        {
            AutoSize = true, Font = PhxTheme.Heading,
            Location = new Point(PhxTheme.Gutter, 8), Text = "To follow"
        });
        header.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = PhxTheme.Muted,
            Location = new Point(PhxTheme.Gutter + 2, 36),
            Text = "Owed to patients when the shelf was short. Give hands it over and takes it off the shelf."
        });
        Controls.Add(header);

        Controls.Add(PhxTheme.Footer(_count, _give, _close));

        _give.Size = new Size(130, 32);
        _give.Text = "&Give";
        _give.Click += async (_, _) => await GiveAsync();
        PhxTheme.MakePrimary(_give);

        _close.DialogResult = DialogResult.Cancel;
        _close.Size = new Size(110, 32);
        _close.Text = "Close";

        AcceptButton = _give;
        CancelButton = _close;
        ClientSize = new Size(900, 520);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - To follow";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadOwedAsync();
    }

    private async Task LoadOwedAsync()
    {
        _owed = await _dispensing.ToFollowAsync();

        GridPager.Attach(_grid).Show(_owed);

        _count.Text = _owed.Count == 0 ? "Nothing is owed." : $"{_owed.Count} owed";
        _give.Enabled = _owed.Count > 0;
    }

    private async Task GiveAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ToFollowDto owed) return;
        if (!Confirm($"Give {owed.Patient} {owed.Owed:0.###} of {owed.Description}?")) return;

        var result = await _dispensing.GiveOwedAsync(owed.DispensingId, owed.LineNo);

        if (result.IsFailure) ShowError(result.Error.Message);
        else if ((await _labels.PrintAsync(result.Value)) is { Printed: false } print)
            ShowError($"Given, but the label did not print: {print.Problem}");

        await LoadOwedAsync();
    }
}

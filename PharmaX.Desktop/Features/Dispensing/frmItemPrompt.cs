using System.Globalization;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// The quantity and the directions for an item, asked together, with the
/// directions shown as the label will print them.
/// </summary>
/// <remarks>
/// They were two dialogs one after the other, so every item added meant
/// two windows, and "T1 TDS" was typed without seeing what the patient
/// would read. One window now, both answers, and the label's wording
/// under the box as it is typed. Offered from the script, so the usual
/// answer is still a single Enter.
/// </remarks>
public sealed class frmItemPrompt : Form
{
    private readonly TextBox _quantity = new();
    private readonly TextBox _directions = new();
    private readonly TextBox _reads = new();
    private readonly Label _problem = new();
    private readonly IReadOnlyDictionary<string, string> _codes;

    private frmItemPrompt(ItemProposalDto proposal,
                          IReadOnlyDictionary<string, string> codes)
    {
        _codes = codes;
        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        Name = "frmItemPrompt";

        var item = new Label
        {
            AutoSize = false,
            Font = PhxTheme.Emphasis,
            Location = new Point(16, 14),
            Name = "lblItem",
            Size = new Size(440, 40),
            Text = proposal.FromScript
                ? proposal.Description
                : $"{proposal.Description}  (not on this script)"
        };

        var quantityCaption = new Label
        {
            AutoSize = true,
            Location = new Point(16, 64),
            Text = "&Quantity"
        };

        _quantity.Location = new Point(110, 60);
        _quantity.Name = "txtQuantity";
        _quantity.Size = new Size(110, 23);
        _quantity.Text = proposal.Quantity.ToString("0.###", CultureInfo.CurrentCulture);
        _quantity.PlaceholderText = "e.g. 30";

        var directionsCaption = new Label
        {
            AutoSize = true,
            Location = new Point(16, 98),
            Text = "&Directions"
        };

        // Mandatory: a label without directions cannot be dispensed
        // (DSP-004).
        _directions.Location = new Point(110, 94);
        _directions.MaxLength = 500;
        _directions.Name = "txtDirections";
        _directions.Size = new Size(346, 23);
        _directions.Text = proposal.Directions;
        _directions.PlaceholderText = "e.g. T1 TDS, or Take ONE tablet three times a day";
        _directions.TextChanged += (_, _) => ShowWording();

        // The complete directions, as the label will print them, including
        // a sentence that wraps. The box scrolls rather than clipping.
        _reads.BackColor = SystemColors.Control;
        _reads.BorderStyle = BorderStyle.None;
        _reads.ForeColor = PhxTheme.Muted;
        _reads.Location = new Point(110, 122);
        _reads.Multiline = true;
        _reads.Name = "lblLabelReads";
        _reads.ReadOnly = true;
        _reads.ScrollBars = ScrollBars.Vertical;
        _reads.Size = new Size(346, 72);
        _reads.TabStop = false;
        _reads.WordWrap = true;

        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.Negative;
        _problem.Location = new Point(16, 202);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(270, 36);

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(300, 202),
            Name = "btnOk",
            Size = new Size(75, 28),
            Text = "OK"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(381, 202),
            Name = "btnCancel",
            Size = new Size(75, 28),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[]
        {
            item, quantityCaption, _quantity, directionsCaption, _directions,
            _reads, _problem, ok, cancel
        });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(472, 248);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand("Quantity and directions");

        Shown += (_, _) => { _quantity.Focus(); _quantity.SelectAll(); };

        // Where an answer will not do, say which and stay open.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            _problem.Text = Problem() ?? "";
            e.Cancel = _problem.Text.Length > 0;
        };

        ShowWording();
    }

    private decimal? Quantity =>
        frmNumberPrompt.Interpret(_quantity.Text, null, 3, out _);

    private string? Problem()
    {
        if (Quantity is not decimal quantity || quantity <= 0m)
            return "Enter how many to dispense.";

        return string.IsNullOrWhiteSpace(_directions.Text)
            ? "Enter the directions for the label."
            : null;
    }

    private string Expanded() =>
        DosageShorthand.Expand(_directions.Text, _codes);

    private void ShowWording()
    {
        var wording = LabelWording.Directions(Expanded());

        _reads.Text = wording.Length == 0 ? "" : wording;
    }

    /// <summary>
    /// The quantity and directions to dispense an item with, starting from
    /// what the script says; null if cancelled.
    /// </summary>
    public static DirectionsDto? Ask(IWin32Window owner, ItemProposalDto proposal,
        IReadOnlyDictionary<string, string>? codes = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        using var dialog = new frmItemPrompt(proposal,
            codes ?? DosageShorthand.Catalogue());

        return dialog.ShowDialog(owner) == DialogResult.OK
               && dialog.Quantity is decimal quantity
            ? new DirectionsDto(quantity, dialog.Expanded())
            : null;
    }
}

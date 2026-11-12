using System.Globalization;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Stickers for a run of pre-packs - twenty for twenty packs of
/// paracetamol - with what goes on them typed in, and shown as the label
/// printer will print it.
/// </summary>
/// <remarks>
/// Printing only: the packs are counted out of the bulk stock on
/// Inventory's Pre-pack screen, which moves the stock. This is for the
/// stickers themselves, laid out as the pharmacy's own: the medicine and
/// how many, the directions and any warning, the expiry and the batch,
/// the price and the date, and the pharmacy's address.
/// </remarks>
public sealed class frmPrepackStickers : Form
{
    /// More than a roll of stock holds is a typing mistake.
    private const int MostStickers = 500;

    private readonly PharmacyImprint? _pharmacy;
    private readonly IReadOnlyDictionary<string, string> _codes;

    private readonly TextBox _medicine = new();
    private readonly ComboBox _supplier = new();
    private readonly TextBox _quantity = new();
    private readonly TextBox _directions = new();
    private readonly TextBox _warning = new();
    private readonly DateTimePicker _expiry = new();
    private readonly TextBox _batch = new();
    private readonly TextBox _price = new();
    private readonly DateTimePicker _packedOn = new();
    private readonly NumericUpDown _stickers = new();
    private readonly TextBox _preview = new();
    private readonly Label _problem = new();

    private frmPrepackStickers(PharmacyImprint? pharmacy,
                               IReadOnlyDictionary<string, string> codes,
                               IReadOnlyList<string>? suppliers,
                               PrepackLabelDto? again)
    {
        _pharmacy = pharmacy;
        _codes = codes;

        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        Name = "frmPrepackStickers";

        var y = 14;

        void Row(string caption, Control input, int width, string? hint = null)
        {
            Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(16, y + 4),
                Text = caption
            });

            input.Location = new Point(150, y);
            input.Size = new Size(width, 23);
            input.TabIndex = Controls.Count;

            if (input is TextBox box)
            {
                box.PlaceholderText = hint ?? "";
                box.TextChanged += (_, _) => ShowPreview();
            }

            Controls.Add(input);
            y += 32;
        }

        _medicine.Name = "txtMedicine";
        _medicine.MaxLength = 100;
        _medicine.PlaceholderText = "e.g. Paracetamol 500mg Tab";
        _medicine.TextChanged += (_, _) => ShowPreview();
        _medicine.AccessibleName = "Medicine";

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, y + 4),
            Text = "&Medicine"
        });

        _medicine.Location = new Point(150, y);
        _medicine.Size = new Size(200, 23);
        _medicine.TabIndex = Controls.Count;
        Controls.Add(_medicine);

        // Drop-down, not a locked list: a supplier the pharmacy has not
        // bought from yet can be typed. Blank prints no brackets.
        _supplier.AccessibleName = "Supplier";
        _supplier.DropDownStyle = ComboBoxStyle.DropDown;
        _supplier.DropDownWidth = 240;
        _supplier.Location = new Point(356, y);
        _supplier.Name = "cboSupplier";
        _supplier.Size = new Size(144, 23);
        _supplier.TabIndex = Controls.Count;
        _supplier.Items.Add("");

        foreach (var name in suppliers ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name))
                _supplier.Items.Add(name.Trim());
        }

        _supplier.SelectedIndex = 0;
        _supplier.TextChanged += (_, _) => ShowPreview();
        Controls.Add(_supplier);
        y += 32;

        _quantity.Name = "txtQuantity";
        Row("&In each pack", _quantity, 90, "e.g. 20");

        _directions.Name = "txtDirections";
        _directions.MaxLength = 500;
        Row("&Directions", _directions, 350,
            "e.g. Take 1-2 tablets three times daily after food");

        _warning.Name = "txtWarning";
        _warning.MaxLength = 120;
        Row("&Warning", _warning, 350, "e.g. May cause drowsiness - or leave blank");

        // Every pre-pack carries its expiry. Left unticked until it is
        // set, so today's date is never printed as one by mistake.
        _expiry.Name = "dtpExpiry";
        _expiry.Format = DateTimePickerFormat.Custom;
        _expiry.CustomFormat = "dd/MM/yyyy";
        _expiry.ShowCheckBox = true;
        _expiry.Checked = false;
        _expiry.ValueChanged += (_, _) => ShowPreview();
        Row("&Expiry", _expiry, 140);

        _batch.Name = "txtBatch";
        _batch.MaxLength = 40;
        Row("&Batch no", _batch, 140, "e.g. 2076");

        _price.Name = "txtPrice";
        Row("P&rice", _price, 90, "e.g. 0.50");

        _packedOn.Name = "dtpPackedOn";
        _packedOn.Format = DateTimePickerFormat.Custom;
        _packedOn.CustomFormat = "dd/MM/yyyy";
        _packedOn.Value = DateTime.Today;
        _packedOn.ValueChanged += (_, _) => ShowPreview();
        Row("Packed &on", _packedOn, 140);

        _stickers.Name = "nudStickers";
        _stickers.Minimum = 1;
        _stickers.Maximum = MostStickers;
        _stickers.Value = 1;
        Row("&How many", _stickers, 90);

        // The sticker as it will print, row for row, from the document
        // that prints it.
        // 9 point is 12 pixels; three more, so the preview matches the print.
        var fixedPitch = new Font("Consolas", 11.25f);
        Disposed += (_, _) => fixedPitch.Dispose();

        _preview.Font = fixedPitch;
        _preview.Location = new Point(16, y + 4);
        _preview.Multiline = true;
        _preview.Name = "txtPreview";
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.Size = new Size(484, 200);
        _preview.TabStop = false;
        _preview.WordWrap = false;
        Controls.Add(_preview);
        y += 212;

        _problem.AutoSize = false;
        _problem.ForeColor = PhxTheme.Negative;
        _problem.Location = new Point(16, y);
        _problem.Name = "lblProblem";
        _problem.Size = new Size(300, 36);

        var print = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(334, y),
            Name = "btnPrint",
            Size = new Size(80, 30),
            Text = "&Print"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(420, y),
            Name = "btnCancel",
            Size = new Size(80, 30),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[] { _problem, print, cancel });

        AcceptButton = print;
        CancelButton = cancel;
        ClientSize = new Size(516, y + 46);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand("Pre-pack stickers");

        // Where an answer will not do, say which and stay open.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            _problem.Text = Problem() ?? "";
            e.Cancel = _problem.Text.Length > 0;
        };

        if (again is not null)
            Fill(again);

        ShowPreview();
        Shown += (_, _) => _medicine.Focus();
    }

    private void Fill(PrepackLabelDto again)
    {
        var culture = CultureInfo.CurrentCulture;

        var (medicine, supplier) = PrepackMedicine.Read(again.Medicine,
            _supplier.Items.Cast<string>());

        _medicine.Text = medicine;
        if (supplier is not null)
            _supplier.Text = supplier;
        _quantity.Text = again.Quantity.ToString("0.###", culture);
        _directions.Text = again.Directions;
        _warning.Text = again.Warning ?? "";
        _batch.Text = again.BatchNumber ?? "";
        _price.Text = again.Price?.ToString("0.00", culture) ?? "";
        _packedOn.Value = again.PackedOn.ToDateTime(TimeOnly.MinValue);
        _stickers.Value = Math.Clamp(again.Copies, (int)_stickers.Minimum,
            (int)_stickers.Maximum);

        if (again.Expiry is DateOnly expiry)
        {
            _expiry.Value = expiry.ToDateTime(TimeOnly.MinValue);
            _expiry.Checked = true;
        }
    }

    private string? ChosenSupplier()
    {
        var name = _supplier.Text.Trim();
        return name.Length == 0 ? null : name;
    }

    private decimal? Quantity =>
        frmNumberPrompt.Interpret(_quantity.Text, null, 3, out _);

    /// Blank is no price; anything else must read as an amount.
    private bool TryPrice(out decimal? price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(_price.Text)) return true;

        if (!decimal.TryParse(_price.Text.Trim(), NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount)
            || amount < 0m)
            return false;

        price = amount;
        return true;
    }

    private string? Problem()
    {
        if (Quantity is not decimal quantity || quantity <= 0m)
            return "Enter how many are in each pack.";

        if (!_expiry.Checked)
            return "Tick the expiry and set it: every pre-pack carries one.";

        return TryPrice(out _) ? null : "Enter the price as an amount, or leave it blank.";
    }

    /// <summary>
    /// The sticker as typed so far. The warning is the one typed, and none
    /// where it is left blank, as the pharmacy's paracetamol packs carry
    /// none.
    /// </summary>
    private LabelDto Sticker()
    {
        TryPrice(out var price);

        return new LabelDto(
            PatientName: "",
            ProductDescription: PrepackMedicine.Line(_medicine.Text, ChosenSupplier()),
            Quantity: Quantity ?? 0m,
            Directions: DosageShorthand.Expand(_directions.Text, _codes),
            BatchNumber: string.IsNullOrWhiteSpace(_batch.Text) ? null : _batch.Text.Trim(),
            Expiry: _expiry.Checked ? DateOnly.FromDateTime(_expiry.Value) : null,
            PharmacistName: "",
            ScriptNumber: "Pre-pack",
            DispensedOn: DateOnly.FromDateTime(_packedOn.Value),
            Cautions: new[] { _warning.Text.Trim() },
            Price: price);
    }

    private void ShowPreview()
    {
        using var document = new LabelDocument(Sticker(), _pharmacy);

        _preview.Text = string.Join(Environment.NewLine, document.Lines());
    }

    /// <summary>
    /// What to print on the stickers and how many; null if cancelled.
    /// </summary>
    public static (LabelDto Sticker, int Copies)? Ask(IWin32Window owner,
        PharmacyImprint? pharmacy,
        IReadOnlyDictionary<string, string>? codes = null,
        IReadOnlyList<string>? suppliers = null,
        PrepackLabelDto? again = null,
        Func<string, CancellationToken, Task<IReadOnlyList<Suggestion>>>? medicines = null)
    {
        using var dialog = new frmPrepackStickers(pharmacy,
            codes ?? DosageShorthand.Catalogue(), suppliers, again);

        // Products on file offered by name; any wording may still be typed.
        if (medicines is not null)
            new SearchSuggestions(dialog._medicine, medicines).Chosen +=
                (_, chosen) => dialog._medicine.Text = chosen.Text;

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? (dialog.Sticker(), (int)dialog._stickers.Value)
            : null;
    }
}

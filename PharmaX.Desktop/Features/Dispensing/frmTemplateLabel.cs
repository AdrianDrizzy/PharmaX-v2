using System.Globalization;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// A template label: the same lines as a pre-pack, and the administrator
/// chooses which of them print. The medicine and how many always print.
/// </summary>
public sealed class frmTemplateLabel : Form
{
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

    private readonly CheckBox _directionsOn = new();
    private readonly CheckBox _warningOn = new();
    private readonly CheckBox _expiryOn = new();
    private readonly CheckBox _batchOn = new();
    private readonly CheckBox _priceOn = new();
    private readonly CheckBox _showPacked = new();
    private readonly CheckBox _pharmacyOn = new();
    private readonly CheckBox _addressOn = new();

    private frmTemplateLabel(PharmacyImprint? pharmacy,
                             IReadOnlyDictionary<string, string> codes,
                             IReadOnlyList<string>? suppliers,
                             TemplateLabelDto? again)
    {
        _pharmacy = pharmacy;
        _codes = codes;

        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        Name = "frmTemplateLabel";

        var y = 14;

        void Field(string caption, Control input, int width, string? hint = null)
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

        CheckBox Option(string name, string text, int x)
        {
            var box = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Location = new Point(x, y),
                Name = name,
                Text = text
            };

            box.CheckedChanged += (_, _) => ShowPreview();
            Controls.Add(box);
            return box;
        }

        _medicine.Name = "txtMedicine";
        _medicine.MaxLength = 100;
        _medicine.AccessibleName = "Medicine";
        Field("&Medicine", _medicine, 200, "e.g. Paracetamol 500mg Tab");

        _supplier.AccessibleName = "Supplier";
        _supplier.DropDownStyle = ComboBoxStyle.DropDown;
        _supplier.Location = new Point(356, _medicine.Top);
        _supplier.Name = "cboSupplier";
        _supplier.Size = new Size(144, 23);
        _supplier.Items.Add("");

        foreach (var name in suppliers ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name))
                _supplier.Items.Add(name.Trim());
        }

        _supplier.SelectedIndex = 0;
        _supplier.TextChanged += (_, _) => ShowPreview();
        Controls.Add(_supplier);

        _quantity.Name = "txtQuantity";
        Field("&In each pack", _quantity, 90, "e.g. 20");

        _directions.Name = "txtDirections";
        _directions.MaxLength = 500;
        Field("&Directions", _directions, 350,
            "e.g. Take 2 tablets three times a day after meals");

        _warning.Name = "txtWarning";
        _warning.MaxLength = 120;
        Field("&Warning", _warning, 350, "Leave blank for none");

        _expiry.Name = "dtpExpiry";
        _expiry.Format = DateTimePickerFormat.Custom;
        _expiry.CustomFormat = "dd/MM/yyyy";
        _expiry.ValueChanged += (_, _) => ShowPreview();
        Field("&Expiry", _expiry, 140);

        _batch.Name = "txtBatch";
        _batch.MaxLength = 40;
        Field("&Batch no", _batch, 140, "e.g. 345");

        _price.Name = "txtPrice";
        Field("P&rice", _price, 90, "e.g. 1.00");

        _packedOn.Name = "dtpPackedOn";
        _packedOn.Format = DateTimePickerFormat.Custom;
        _packedOn.CustomFormat = "dd/MM/yyyy";
        _packedOn.Value = DateTime.Today;
        _packedOn.ValueChanged += (_, _) => ShowPreview();
        Field("Packed &on", _packedOn, 140);

        // The medicine line stays. Everything under it can be left off.
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, y + 2),
            Text = "Print"
        });

        _directionsOn = Option("chkDirections", "Directions", 150);
        _warningOn = Option("chkWarning", "Warning", 250);
        _expiryOn = Option("chkExpiry", "Expiry", 340);
        _batchOn = Option("chkBatch", "Batch", 420);
        y += 26;
        _priceOn = Option("chkPrice", "Price", 150);
        _showPacked = Option("chkPackedOn", "Packed on", 230);
        _pharmacyOn = Option("chkPharmacy", "Pharmacy", 330);
        _addressOn = Option("chkAddress", "Address", 430);
        y += 28;

        _stickers.Name = "nudStickers";
        _stickers.Minimum = 1;
        _stickers.Maximum = MostStickers;
        _stickers.Value = 1;
        Field("&How many", _stickers, 90);

        var fixedPitch = new Font("Consolas", 11.25f);
        Disposed += (_, _) => fixedPitch.Dispose();

        _preview.Font = fixedPitch;
        _preview.Location = new Point(16, y + 4);
        _preview.Multiline = true;
        _preview.Name = "txtPreview";
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.Size = new Size(484, 160);
        _preview.TabStop = false;
        _preview.WordWrap = false;
        Controls.Add(_preview);
        y += 172;

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
        Text = WindowTitles.Brand("Template label");

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

    private void Fill(TemplateLabelDto again)
    {
        var culture = CultureInfo.CurrentCulture;
        var omit = again.Omit ?? new LabelOmissions();

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
            _expiry.Value = expiry.ToDateTime(TimeOnly.MinValue);

        _directionsOn.Checked = !omit.Directions;
        _warningOn.Checked = !omit.Warning;
        _expiryOn.Checked = !omit.Expiry;
        _batchOn.Checked = !omit.Batch;
        _priceOn.Checked = !omit.Price;
        _showPacked.Checked = !omit.PackedOn;
        _pharmacyOn.Checked = !omit.Pharmacy;
        _addressOn.Checked = !omit.Address;
    }

    private string? ChosenSupplier()
    {
        var name = _supplier.Text.Trim();
        return name.Length == 0 ? null : name;
    }

    private decimal? Quantity =>
        frmNumberPrompt.Interpret(_quantity.Text, null, 3, out _);

    private bool TryPrice(out decimal? price)
    {
        price = null;
        if (!_priceOn.Checked || string.IsNullOrWhiteSpace(_price.Text))
            return true;

        if (!decimal.TryParse(_price.Text.Trim(), NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount)
            || amount < 0m)
            return false;

        price = amount;
        return true;
    }

    private string? Problem()
    {
        if (string.IsNullOrWhiteSpace(_medicine.Text))
            return "Enter the medicine. A label has to say what the pack is.";

        if (Quantity is not decimal quantity || quantity <= 0m)
            return "Enter how many are in each pack.";

        return TryPrice(out _) ? null : "Enter the price as an amount, or leave it blank.";
    }

    private LabelOmissions Omissions() => new(
        Directions: !_directionsOn.Checked,
        Warning: !_warningOn.Checked,
        Expiry: !_expiryOn.Checked,
        Batch: !_batchOn.Checked,
        Price: !_priceOn.Checked,
        PackedOn: !_showPacked.Checked,
        Pharmacy: !_pharmacyOn.Checked,
        Address: !_addressOn.Checked);

    private LabelDto Sticker()
    {
        TryPrice(out var price);

        return new LabelDto(
            PatientName: "",
            ProductDescription: PrepackMedicine.Line(_medicine.Text, ChosenSupplier()),
            Quantity: Quantity ?? 0m,
            Directions: DosageShorthand.Expand(_directions.Text, _codes),
            BatchNumber: string.IsNullOrWhiteSpace(_batch.Text) ? null : _batch.Text.Trim(),
            Expiry: _expiryOn.Checked ? DateOnly.FromDateTime(_expiry.Value) : null,
            PharmacistName: "",
            ScriptNumber: "Template",
            DispensedOn: DateOnly.FromDateTime(_packedOn.Value),
            Cautions: new[] { _warningOn.Checked ? _warning.Text.Trim() : "" },
            Price: price,
            Omit: Omissions());
    }

    private void ShowPreview()
    {
        using var document = new LabelDocument(Sticker(), _pharmacy);
        _preview.Text = string.Join(Environment.NewLine, document.Lines());
    }

    /// <summary>What to print, and how many; null if cancelled.</summary>
    public static (LabelDto Sticker, int Copies)? Ask(IWin32Window owner,
        PharmacyImprint? pharmacy,
        IReadOnlyDictionary<string, string>? codes = null,
        IReadOnlyList<string>? suppliers = null,
        TemplateLabelDto? again = null)
    {
        using var dialog = new frmTemplateLabel(pharmacy,
            codes ?? DosageShorthand.Catalogue(), suppliers, again);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? (dialog.Sticker(), (int)dialog._stickers.Value)
            : null;
    }
}

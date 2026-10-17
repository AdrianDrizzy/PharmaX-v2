using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public partial class frmProductEdit : PhxFormBase, IProductEditView
{
    private readonly ProductEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmProductEdit()
    {
        InitializeComponent();

        // Where the product is filed, found by any word of the name.
        foreach (var combo in new[] { cboDepartment, cboCategory, cboManufacturer })
            PharmaX.Desktop.Controls.WordFilter.Attach(combo);

        btnSave.Click += (_, _) =>
        {
            // A cell still being typed in is part of what Save means.
            if (grdBatches.IsCurrentCellInEditMode) grdBatches.EndEdit();

            SaveRequested?.Invoke(this, EventArgs.Empty);
        };
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        grdBatches.CellBeginEdit += (_, e) =>
            _before = grdBatches[e.ColumnIndex, e.RowIndex].Value;
        grdBatches.CellValueChanged += (_, e) =>
            OnBatchCellChanged(e.RowIndex, e.ColumnIndex);
        grdBatches.DefaultValuesNeeded += (_, e) => OnNewBatchRow(e.Row);
        grdBatches.UserDeletingRow += (_, e) => OnDeletingBatchRow(e);
        grdBatches.UserDeletedRow += (_, _) => ShowStock();

        // Stock on a batch is picked by batch at the till, so adding one
        // ticks the box the service would otherwise tick for it.
        grdBatches.UserAddedRow += (_, _) =>
        {
            chkBatchTracked.Checked = true;
            ShowStock();
        };

        grdBatches.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !grdBatches.ReadOnly)
                grdBatches.BeginEdit(selectAll: true);
        };
    }

    /// Runtime. Resolved by the container.
    public frmProductEdit(ProductEditPresenter presenter,
        Application.Security.IPermissionService permissions) : this()
    {
        _presenter = presenter;
        _permissions = permissions;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IPermissionService? _permissions;

    /// <summary>
    /// Opens the fields this user may change and no others, so nothing is
    /// typed that Save will then refuse.
    /// </summary>
    /// <remarks>
    /// Saving needs inv.product.maintain; changing an existing product's
    /// selling price needs inv.price.change on top; a scheduled medicine
    /// needs inv.product.dispensary. A new product's first price is set by
    /// whoever adds it - there was no price before it to change.
    /// </remarks>
    private void ApplyPermissions()
    {
        if (_permissions is null) return;

        bool Has(string code) =>
            Application.Security.PermissionServiceExtensions.Has(
                _permissions, code);

        var maintains = Has(Domain.Security.PermissionCode.ProductMaintain);

        // A count moves stock, so it stays with the stocktake permission
        // the list's own On hand cells answer to.
        _countsStock = maintains
                       && Has(Domain.Security.PermissionCode.StockTakePost);

        btnSave.Visible = maintains;
        txtCode.ReadOnly = !maintains;
        chkActive.Enabled = maintains;
        txtRetail.ReadOnly = ProductId is not null
                             && !Has(Domain.Security.PermissionCode.PriceChange);

        if (!Has(Domain.Security.PermissionCode.ProductDispensary))
            numSchedule.Maximum = 2;

        // Opened first and the quantity locked after, the order the list
        // uses: setting the whole grid read-write clears a column's lock.
        grdBatches.ReadOnly = !maintains;
        grdBatches.AllowUserToDeleteRows = maintains;

        // Without the stocktake permission a new batch can only be the
        // stock already on the product, so there has to be some.
        grdBatches.AllowUserToAddRows =
            maintains && (_countsStock || _loose > 0m);

        var quantity = grdBatches.Columns["QuantityOnHand"]!;
        quantity.ReadOnly = !_countsStock;
        quantity.ToolTipText = _countsStock
            ? ""
            : "Changing the quantity on hand needs the stocktake permission.";

        ShowStock();

        if (!maintains)
            Text = "PharmaX - Product (read only)";
    }

    public Guid? ProductId { get; private set; }

    /// <summary>
    /// Called by the list screen before ShowDialog. Null means a new
    /// product. Set before OnLoad so the presenter can read it.
    /// </summary>
    /// <param name="batchId">
    /// The batch the operator was looking at in the list's batch view,
    /// picked out on the batch grid when the form opens.
    /// </param>
    /// <remarks>
    /// The code is editable, as it is on the list: a sale keeps the
    /// product's id rather than its code, so renumbering does not rewrite
    /// anything already printed.
    /// </remarks>
    public void LoadProduct(Guid? id, Guid? batchId = null)
    {
        ProductId = id;
        _focusBatch = batchId;
        Text = id is null
            ? "PharmaX - New product"
            : "PharmaX - Edit product";
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        ApplyPermissions();
        FocusBatch();
    }

    private Guid? _focusBatch;

    private void FocusBatch()
    {
        if (_focusBatch is not Guid id) return;

        var row = BatchRows().FirstOrDefault(r => r.Tag is Guid g && g == id);
        if (row is null) return;

        grdBatches.CurrentCell = row.Cells["BatchNumber"];
        ActiveControl = grdBatches;
    }

    /// <summary>
    /// One entry in a filing list: the name is read, the id is stored.
    /// </summary>
    private sealed class Choice
    {
        public Choice(Guid? id, string label)
        {
            Id = id;
            Label = label;
        }

        public Guid? Id { get; }
        public string Label { get; }

        public override string ToString() => Label;
    }

    public void ShowFilingOptions(
        IReadOnlyList<ReferenceItemDto> departments,
        IReadOnlyList<ReferenceItemDto> categories,
        IReadOnlyList<ReferenceItemDto> manufacturers)
    {
        ArgumentNullException.ThrowIfNull(departments);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(manufacturers);

        Fill(cboDepartment, departments);
        Fill(cboCategory, categories);
        Fill(cboManufacturer, manufacturers);
    }

    /// <remarks>
    /// A blank first entry, because filing is optional: a pharmacy that
    /// has not decided where something belongs must be able to say so.
    /// </remarks>
    private static void Fill(ComboBox combo,
                             IReadOnlyList<ReferenceItemDto> entries)
    {
        combo.Items.Clear();
        combo.Items.Add(new Choice(null, "(none)"));

        foreach (var entry in entries)
            combo.Items.Add(new Choice(entry.Id, entry.Name));

        combo.SelectedIndex = 0;
    }

    private static void Select(ComboBox combo, Guid? id)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is Choice choice && choice.Id == id)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        // Filed against something since deactivated, or since deleted.
        // Falls back to blank rather than showing the wrong one.
        combo.SelectedIndex = 0;
    }

    private static Guid? Chosen(ComboBox combo) =>
        (combo.SelectedItem as Choice)?.Id;

    public void ShowProduct(ProductDto product)
    {
        ArgumentNullException.ThrowIfNull(product);

        txtCode.Text = product.Code;
        txtDescription.Text = product.Description;
        txtGenericName.Text = product.GenericName ?? "";
        txtRetail.Amount = product.RetailPrice;
        txtCost.Amount = product.CostPrice;
        txtVatRate.Amount = product.VatRatePercent;
        txtTaxCode.Text = product.TaxCode;
        numSchedule.Value = product.Schedule;
        chkBatchTracked.Checked = product.IsBatchTracked;
        txtReorderLevel.Amount = product.ReorderLevel;
        txtPackSize.Text = product.PackSize ?? "";
        txtUnitsPerPack.Amount = product.UnitsPerPack;
        txtBarcodes.Text = string.Join(", ", product.Barcodes);

        Select(cboDepartment, product.DepartmentId);
        Select(cboCategory, product.CategoryId);
        Select(cboManufacturer, product.ManufacturerId);

        chkActive.Checked = product.IsActive;

        txtOnHand.Amount = product.QuantityOnHand;
        _shownOnHand = txtOnHand.Amount;

        ShowBatches(product.QuantityOnHand,
            product.Batches ?? Array.Empty<ProductBatchDto>());
    }

    // ---- Stock -----------------------------------------------------------

    /// True while the form writes the batch cells itself.
    private bool _filling;

    /// What the cell being typed in held before, to put back if refused.
    private object? _before;

    /// On hand as it was shown, rounded the way the box shows it, so an
    /// untouched box never posts a count of the rounding.
    private decimal? _shownOnHand;

    /// Stock on the product that is on no batch.
    private decimal _loose;

    /// May change a quantity. Open until the permissions say otherwise,
    /// for the form as the designer and the tests construct it.
    private bool _countsStock = true;

    private void ShowBatches(decimal onHand,
        IReadOnlyList<ProductBatchDto> batches)
    {
        _filling = true;

        try
        {
            grdBatches.Rows.Clear();

            foreach (var batch in batches)
            {
                var index = grdBatches.Rows.Add(
                    batch.BatchNumber,
                    ExpiryText.Show(batch.Expiry),
                    batch.DaysToExpiry.ToString(CultureInfo.CurrentCulture),
                    Quantity(batch.QuantityOnHand));

                grdBatches.Rows[index].Tag = batch.BatchId;
            }
        }
        finally
        {
            _filling = false;
        }

        _loose = Math.Max(0m, onHand - batches.Sum(b => b.QuantityOnHand));
        ShowStock();
    }

    /// <summary>
    /// Keeps the On hand box in step with the batch rows.
    /// </summary>
    /// <remarks>
    /// With no batches the box is where the count is typed. With batches
    /// it is their total and read only, because two places to type one
    /// figure is two figures that disagree. A batch added here takes over
    /// the stock that was on no batch, so the total is then the rows alone.
    /// </remarks>
    private void ShowStock()
    {
        var rows = BatchRows().ToList();

        if (rows.Count == 0)
        {
            txtOnHand.ReadOnly = !_countsStock;
            lblOnHandNote.Text = _countsStock
                ? ""
                : "Counting stock needs the stocktake permission.";
            return;
        }

        var kept = rows.Where(r => r.Tag is Guid).Sum(QuantityOf);
        var adding = rows.Any(r => r.Tag is null);
        var added = rows.Where(r => r.Tag is null).Sum(QuantityOf);

        txtOnHand.ReadOnly = true;
        txtOnHand.Amount = kept + (adding ? added : _loose);

        lblOnHandNote.Text = !adding && _loose > 0m
            ? $"The batches, and {Quantity(_loose)} on no batch"
            : "The total of the batches";
    }

    private void OnBatchCellChanged(int rowIndex, int columnIndex)
    {
        if (_filling || rowIndex < 0 || columnIndex < 0) return;

        var row = grdBatches.Rows[rowIndex];
        var column = grdBatches.Columns[columnIndex].Name;
        var text = TextOf(row, column);
        var added = row.Tag is null;

        switch (column)
        {
            case "BatchNumber":
                if (text.Length == 0 && !added)
                    Refuse(row, column,
                        "A batch keeps its number. Enter the number "
                        + "printed on the pack.");
                else Put(row, column, text);
                break;

            case "Expiry":
                if (text.Length == 0 && added)
                    Put(row, "DaysLeft", "");
                else if (ExpiryText.TryParse(text, out var expiry))
                    ShowExpiry(row, expiry);
                else Refuse(row, column, "Enter the expiry as dd/MM/yyyy.");
                break;

            case "DaysLeft":
                if (text.Length == 0 && added)
                    Put(row, "Expiry", "");
                else if (int.TryParse(text, NumberStyles.Integer,
                             CultureInfo.CurrentCulture, out var days)
                         && ExpiryText.TryFromDaysLeft(days, out var moved))
                    ShowExpiry(row, moved);
                else Refuse(row, column, "Days left is a whole number.");
                break;

            case "QuantityOnHand":
                if (text.Length == 0 && added) break;

                if (TryQuantity(text, out var quantity) && quantity >= 0m)
                    Put(row, column, Quantity(quantity));
                else Refuse(row, column,
                    "Enter the quantity on hand, zero or more.");
                break;
        }

        ShowStock();
    }

    /// <summary>
    /// The quantity a new batch row starts with: the stock on no batch
    /// that no other new row has taken yet, which is what giving a
    /// product its first batch usually means.
    /// </summary>
    private void OnNewBatchRow(DataGridViewRow row)
    {
        var rows = BatchRows().ToList();

        // A count typed into the box before there were any rows is the
        // stock the first batch is being given.
        var pool = rows.Count == 0 && !txtOnHand.ReadOnly
                   && txtOnHand.Amount is decimal typed
            ? Math.Max(0m, typed)
            : _loose;

        var left = pool - rows.Where(r => r.Tag is null).Sum(QuantityOf);

        if (left > 0m) row.Cells["QuantityOnHand"].Value = Quantity(left);
    }

    private void OnDeletingBatchRow(DataGridViewRowCancelEventArgs e)
    {
        // A row typed here and not yet saved can simply go.
        if (e.Row?.Tag is not Guid) return;

        e.Cancel = true;
        ShowInfo("A batch on the shelf is not deleted. "
                 + "Set its On hand to 0 to write it off.");
    }

    private void ShowExpiry(DataGridViewRow row, DateOnly expiry)
    {
        Put(row, "Expiry", ExpiryText.Show(expiry));
        Put(row, "DaysLeft",
            ExpiryText.DaysLeft(expiry).ToString(CultureInfo.CurrentCulture));
    }

    private void Refuse(DataGridViewRow row, string column, string message)
    {
        ShowError(message);
        Put(row, column, _before);
    }

    private void Put(DataGridViewRow row, string column, object? value)
    {
        _filling = true;

        try
        {
            row.Cells[column].Value = value;
        }
        finally
        {
            _filling = false;
        }
    }

    private IEnumerable<DataGridViewRow> BatchRows() =>
        grdBatches.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow);

    private static string TextOf(DataGridViewRow row, string column) =>
        (Convert.ToString(row.Cells[column].Value, CultureInfo.CurrentCulture)
         ?? "").Trim();

    private static decimal QuantityOf(DataGridViewRow row) =>
        TryQuantity(TextOf(row, "QuantityOnHand"), out var quantity)
            ? quantity
            : 0m;

    /// The same culture the cells are written in, so what is shown reads
    /// back as the same number.
    private static bool TryQuantity(string text, out decimal quantity) =>
        decimal.TryParse(text, NumberStyles.Number,
            CultureInfo.CurrentCulture, out quantity);

    private static string Quantity(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.CurrentCulture);

    private List<ProductBatchEditDto> CollectBatches() =>
        BatchRows()
            // A new row with neither a number nor a date was never
            // really started, whatever quantity it was offered.
            .Where(r => r.Tag is Guid
                        || TextOf(r, "BatchNumber").Length > 0
                        || TextOf(r, "Expiry").Length > 0)
            .Select(r => new ProductBatchEditDto(
                r.Tag as Guid?,
                TextOf(r, "BatchNumber"),
                ExpiryText.TryParse(TextOf(r, "Expiry"), out var expiry)
                    ? expiry
                    : null,
                TryQuantity(TextOf(r, "QuantityOnHand"), out var quantity)
                    ? quantity
                    : null))
            .ToList();

    /// <summary>
    /// The count typed into On hand, when it can be typed there and was
    /// changed. Null otherwise, which the service reads as unchanged.
    /// </summary>
    private decimal? CollectOnHand() =>
        !BatchRows().Any() && !txtOnHand.ReadOnly
        && txtOnHand.Amount is decimal typed && typed != _shownOnHand
            ? typed
            : null;

    public ProductEditDto Collect() => new(
        Code: txtCode.Text.Trim(),
        Description: txtDescription.Text.Trim(),
        GenericName: Blank(txtGenericName.Text),
        RetailPrice: txtRetail.Amount ?? 0m,
        CostPrice: txtCost.Amount ?? 0m,
        VatRatePercent: txtVatRate.Amount ?? 0m,
        TaxCode: txtTaxCode.Text.Trim().ToUpperInvariant(),
        Schedule: (int)numSchedule.Value,
        IsBatchTracked: chkBatchTracked.Checked,
        ReorderLevel: txtReorderLevel.Amount ?? 0m,
        PackSize: Blank(txtPackSize.Text),
        UnitsPerPack: txtUnitsPerPack.Amount,
        DepartmentId: Chosen(cboDepartment),
        CategoryId: Chosen(cboCategory),
        ManufacturerId: Chosen(cboManufacturer),
        Barcodes: txtBarcodes.Text.Split(BarcodeSeparators,
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries),
        IsActive: chkActive.Checked,
        QuantityOnHand: CollectOnHand(),
        Batches: CollectBatches());

    /// A scanner ends a code with a return; a person types commas.
    private static readonly char[] BarcodeSeparators =
        { ',', ';', ' ', '\t', '\r', '\n' };

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

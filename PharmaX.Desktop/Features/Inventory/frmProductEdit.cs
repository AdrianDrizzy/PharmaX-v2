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

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);
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

        btnSave.Visible = maintains;
        txtRetail.ReadOnly = ProductId is not null
                             && !Has(Domain.Security.PermissionCode.PriceChange);

        if (!Has(Domain.Security.PermissionCode.ProductDispensary))
            numSchedule.Maximum = 2;

        if (!maintains)
            Text = "PharmaX - Product (read only)";
    }

    public Guid? ProductId { get; private set; }

    /// <summary>
    /// Called by the list screen before ShowDialog. Null means a new
    /// product. Set before OnLoad so the presenter can read it.
    /// </summary>
    public void LoadProduct(Guid? id)
    {
        ProductId = id;
        Text = id is null
            ? "PharmaX - New product"
            : "PharmaX - Edit product";

        // The code identifies the product on every historic document, so
        // it is set once and never changed.
        txtCode.ReadOnly = id is not null;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        ApplyPermissions();
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

        Select(cboDepartment, product.DepartmentId);
        Select(cboCategory, product.CategoryId);
        Select(cboManufacturer, product.ManufacturerId);

        lblOnHandValue.Text = Format.Quantity(product.QuantityOnHand);
    }

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
        ManufacturerId: Chosen(cboManufacturer));

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

using PharmaX.Application.Inventory.Dtos;
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
    public frmProductEdit(ProductEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
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
    }

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
        PackSize: Blank(txtPackSize.Text));

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

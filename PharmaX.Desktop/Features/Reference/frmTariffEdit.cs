using PharmaX.Application.Reference;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

public partial class frmTariffEdit : PhxFormBase, ITariffEditView,
                                     ITariffProductPicker
{
    private readonly TariffEditPresenter? _presenter;
    private readonly ViewFactory? _views;

    private Guid _productId;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PriceChange;

    /// Designer only. Never called at runtime.
    public frmTariffEdit()
    {
        InitializeComponent();

        btnChooseProduct.Click += (_, _) =>
            ChooseProductRequested?.Invoke(this, EventArgs.Empty);
        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);

        chkOpenEnded.CheckedChanged += (_, _) =>
            dtpTo.Enabled = !chkOpenEnded.Checked;

        dtpTo.Enabled = false;
        dtpFrom.Value = DateTime.Today;
        dtpTo.Value = DateTime.Today;
    }

    /// Runtime. Resolved by the container.
    public frmTariffEdit(TariffEditPresenter presenter, ViewFactory views)
        : this()
    {
        _presenter = presenter;
        _views = views;
        _presenter.Attach(this);
    }

    public Guid? TariffId { get; private set; }

    /// Called by the list screen before the dialog is shown.
    public void Prepare(Guid? id) => TariffId = id;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        cboScheme.DisplayMember = "Value";
        cboScheme.ValueMember = "Key";
        cboScheme.DataSource = schemes
            .Select(s => new KeyValuePair<Guid, string>(s.Id, s.Name))
            .ToList();
    }

    public void ShowTariff(TariffListItemDto tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);

        txtProduct.Text = $"{tariff.ProductCode}  {tariff.ProductDescription}";
        txtPrice.Amount = tariff.Price;
        txtFee.Amount = tariff.DispensingFee;
        dtpFrom.Value = tariff.EffectiveFrom.ToDateTime(TimeOnly.MinValue);

        chkOpenEnded.Checked = tariff.EffectiveTo is null;

        if (tariff.EffectiveTo is DateOnly end)
            dtpTo.Value = end.ToDateTime(TimeOnly.MinValue);

        foreach (var row in (List<KeyValuePair<Guid, string>>)
                            cboScheme.DataSource!)
        {
            if (!string.Equals(row.Value, tariff.SchemeName,
                               StringComparison.Ordinal)) continue;

            cboScheme.SelectedValue = row.Key;
            break;
        }
    }

    public void ShowProduct(Guid productId, string code, string description)
    {
        _productId = productId;
        txtProduct.Text = $"{code}  {description}";
    }

    public void LockSubject()
    {
        // Society and product are what make a tariff the tariff it is.
        // Changing either would silently reprice a different agreement.
        cboScheme.Enabled = false;
        btnChooseProduct.Enabled = false;
    }

    /// <summary>
    /// Opens the product list in picker mode, the same screen the till
    /// uses to find an item by name.
    /// </summary>
    public string? PromptProduct()
    {
        if (_views is null) return null;

        using var picker = _views.Create<frmProductList>();
        picker.UseAsPicker();

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedCode
            : null;
    }

    public TariffEditDto? Collect()
    {
        if (cboScheme.SelectedValue is not Guid schemeId)
        {
            ShowError("Choose a society.");
            return null;
        }

        if (_productId == Guid.Empty && TariffId is null)
        {
            ShowError("Choose a product.");
            return null;
        }

        if (txtPrice.Amount is not decimal price)
        {
            ShowError("Enter the price the society pays.");
            return null;
        }

        var fee = txtFee.Amount ?? 0m;
        var from = DateOnly.FromDateTime(dtpFrom.Value.Date);

        DateOnly? to = chkOpenEnded.Checked
            ? null
            : DateOnly.FromDateTime(dtpTo.Value.Date);

        if (to is DateOnly end && end < from)
        {
            ShowError("A tariff cannot stop before it starts.");
            return null;
        }

        return new TariffEditDto(schemeId, _productId, price, fee, from, to);
    }

    public event EventHandler? ChooseProductRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

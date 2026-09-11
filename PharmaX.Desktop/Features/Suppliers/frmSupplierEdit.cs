using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public partial class frmSupplierEdit : PhxFormBase, ISupplierEditView
{
    private readonly SupplierEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SupplierMaintain;

    /// Designer only. Never called at runtime.
    public frmSupplierEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmSupplierEdit(SupplierEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public Guid? SupplierId { get; private set; }

    public void LoadSupplier(Guid? id)
    {
        SupplierId = id;
        Text = id is null
            ? "PharmaX - New supplier"
            : "PharmaX - Supplier";

        // The code is on every order and receipt already raised, so it
        // is set once and never changed.
        txtCode.ReadOnly = id is not null;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowSupplier(SupplierDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        txtCode.Text = supplier.Code;
        txtName.Text = supplier.Name;
        txtContactName.Text = supplier.ContactName ?? "";
        txtTelephone.Text = supplier.Telephone ?? "";
        txtEmail.Text = supplier.Email ?? "";
        txtAccountNumber.Text = supplier.AccountNumber ?? "";
        numTermsDays.Value = supplier.TermsDays;
        numLeadTimeDays.Value = supplier.LeadTimeDays;
    }

    public SupplierEditDto Collect() => new(
        Code: txtCode.Text.Trim(),
        Name: txtName.Text.Trim(),
        ContactName: Blank(txtContactName.Text),
        Telephone: Blank(txtTelephone.Text),
        Email: Blank(txtEmail.Text),
        AccountNumber: Blank(txtAccountNumber.Text),
        TermsDays: (int)numTermsDays.Value,
        LeadTimeDays: (int)numLeadTimeDays.Value);

    private static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}

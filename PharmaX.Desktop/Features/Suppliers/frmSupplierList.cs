using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public partial class frmSupplierList : PhxFormBase, ISupplierListView
{
    private readonly SupplierListPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmSupplierList()
    {
        InitializeComponent();

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        btnEdit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        btnDeactivate.Click += (_, _) =>
            DeactivateRequested?.Invoke(this, EventArgs.Empty);
        btnChoose.Click += (_, _) =>
            ChooseRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(false);

        chkIncludeInactive.CheckedChanged += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        grdSuppliers.DoubleClick += (_, _) =>
        {
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
            else EditRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmSupplierList(SupplierListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public bool IsPickerMode { get; private set; }

    public void UseAsPicker()
    {
        IsPickerMode = true;
        Text = "PharmaX - Choose supplier";
        btnChoose.Visible = true;
        AcceptButton = btnChoose;
        btnDeactivate.Visible = false;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public Guid? SelectedId => grdSuppliers.CurrentRow?.DataBoundItem
        is SupplierListItemDto dto ? dto.Id : null;

    public bool IncludeInactive => chkIncludeInactive.Checked;

    public void ShowSuppliers(IReadOnlyList<SupplierListItemDto> suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);

        grdSuppliers.DataSource = suppliers.ToList();

        foreach (DataGridViewRow row in grdSuppliers.Rows)
        {
            if (row.DataBoundItem is SupplierListItemDto { IsActive: false })
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }

        lblCount.Text = $"{suppliers.Count} supplier(s)";
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeactivateRequested;
    public event EventHandler? ChooseRequested;
}

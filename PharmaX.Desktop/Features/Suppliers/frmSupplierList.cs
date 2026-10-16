using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Controls;
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
            ToggleActiveRequested?.Invoke(this, EventArgs.Empty);

        grdSuppliers.SelectionChanged += (_, _) => ShowSelectionState();
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

    public bool SelectedIsActive => grdSuppliers.CurrentRow?.DataBoundItem
        is not SupplierListItemDto { IsActive: false };

    /// The one button says what it will do to the selected supplier.
    private void ShowSelectionState() =>
        btnDeactivate.Text = SelectedIsActive ? "Deac&tivate" : "Re&activate";

    public bool IncludeInactive => chkIncludeInactive.Checked;

    public void ShowSuppliers(IReadOnlyList<SupplierListItemDto> suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);

        GridPager.Attach(grdSuppliers).Show(suppliers);

        foreach (DataGridViewRow row in grdSuppliers.Rows)
        {
            if (row.DataBoundItem is SupplierListItemDto { IsActive: false })
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        lblCount.Text = $"{suppliers.Count} supplier(s)";
        ShowSelectionState();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ToggleActiveRequested;
    public event EventHandler? ChooseRequested;
}

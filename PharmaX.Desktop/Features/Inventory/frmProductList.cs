using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// The CRUD template. Most of the forty screens in PharmaX are
/// list-and-edit maintenance screens, and they all take this shape: only
/// the DTO, the service and the columns change.
/// </summary>
/// <remarks>
/// The Supervising Controller variant of MVP is acceptable here - the
/// grid binds to a DTO list directly - because there are no rules to keep
/// out of the view.
/// </remarks>
public partial class frmProductList : PhxFormBase, IProductListView
{
    private readonly ProductListPresenter? _presenter;

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmProductList()
    {
        InitializeComponent();

        // Debounce typing so a 50,000-product search does not run per key.
        txtSearch.TextChanged += (_, _) => Restart();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        btnEdit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnChoose.Click += (_, _) =>
            ChooseRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(IsPickerMode ? false : true);

        // A double click means "this one" in both modes and in both
        // views; which mode it is decides whether that opens the editor
        // or answers the screen that asked for a product.
        foreach (var grid in new[] { grdProducts, grdBatches })
        {
            grid.DoubleClick += (_, _) =>
            {
                if (IsPickerMode)
                    ChooseRequested?.Invoke(this, EventArgs.Empty);
                else EditRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        chkBatches.CheckedChanged += (_, _) =>
        {
            grdProducts.Visible = !chkBatches.Checked;
            grdBatches.Visible = chkBatches.Checked;

            ViewModeChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmProductList(ProductListPresenter presenter,
        Application.Security.IPermissionService permissions) : this()
    {
        _presenter = presenter;
        _permissions = permissions;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IPermissionService? _permissions;

    /// <summary>
    /// Offered to the people whose save will be accepted. The Add button
    /// used to be there for everyone, and a stock controller filled the
    /// whole form in before Save was refused (DEF-0027).
    /// </summary>
    private void ApplyPermissions()
    {
        if (_permissions is null || IsPickerMode) return;

        if (Application.Security.PermissionServiceExtensions.Has(
                _permissions, Domain.Security.PermissionCode.ProductMaintain))
            return;

        btnAdd.Visible = false;
        btnEdit.Text = "&View";
    }

    public bool IsPickerMode { get; private set; }

    /// <summary>
    /// Called before ShowDialog by a screen that wants to pick a product
    /// rather than maintain the list - the till looking one up by name
    /// instead of scanning it, most of all.
    /// </summary>
    /// <param name="term">
    /// What the operator had already typed, so a search that started in
    /// the scan box carries on here rather than starting again.
    /// </param>
    public void UseAsPicker(string? term = null)
    {
        IsPickerMode = true;
        Text = "PharmaX - Find product";
        btnChoose.Visible = true;
        AcceptButton = btnChoose;

        // Maintenance is not on offer to a cashier who only wanted to
        // find something to sell.
        btnAdd.Visible = false;
        btnEdit.Visible = false;


        if (!string.IsNullOrWhiteSpace(term)) txtSearch.Text = term.Trim();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyPermissions();
        if (_presenter is not null) await _presenter.InitialiseAsync();

        // Typing continues where the operator left off, and the first
        // match is already selected, so a picked-up search needs one
        // keystroke to accept rather than a trip to the mouse.
        if (!IsPickerMode) return;

        txtSearch.Focus();
        txtSearch.SelectionStart = txtSearch.TextLength;
    }

    private void Restart()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public string SearchText => txtSearch.Text.Trim();

    public bool BatchView => chkBatches.Checked;

    /// <summary>
    /// The product behind the selected row, whichever view is showing.
    /// </summary>
    /// <remarks>
    /// A batch row still belongs to a product, so Edit and Choose keep
    /// working with the batch view open. Reading the selection off only
    /// the product grid would have left both buttons doing nothing the
    /// moment the box was ticked.
    /// </remarks>
    public Guid? SelectedId => Selected() switch
    {
        ProductListItemDto p => p.Id,
        ProductBatchListItemDto b => b.ProductId,
        _ => null
    };

    private object? Selected() => BatchView
        ? grdBatches.CurrentRow?.DataBoundItem
        : grdProducts.CurrentRow?.DataBoundItem;

    /// <summary>
    /// The chosen product's code, which is what a till needs: the sale
    /// service resolves a line by code or barcode, exactly as it does
    /// for a scan, so a picked product and a scanned one take the same
    /// path through the domain.
    /// </summary>
    public string? SelectedCode => Selected() switch
    {
        ProductListItemDto p => p.Code,
        ProductBatchListItemDto b => b.Code,
        _ => null
    };

    public void ShowProducts(IReadOnlyList<ProductListItemDto> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        grdProducts.DataSource = products.ToList();

        foreach (DataGridViewRow row in grdProducts.Rows)
        {
            if (row.DataBoundItem is not ProductListItemDto item) continue;

            if (!item.IsActive)
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            else if (item.IsBelowReorderLevel)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
        }
    }

    /// <summary>
    /// The shelf a batch at a time: what it is, which batch, how much is
    /// left and when it stops being sellable.
    /// </summary>
    public void ShowBatches(IReadOnlyList<ProductBatchListItemDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        grdBatches.DataSource = rows.ToList();

        foreach (DataGridViewRow row in grdBatches.Rows)
        {
            if (row.DataBoundItem is not ProductBatchListItemDto item)
                continue;

            if (!item.IsActive)
            {
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                continue;
            }

            // Already gone off and still on the shelf, which is the one
            // thing on this grid somebody has to act on today.
            if (item.IsExpired)
                row.DefaultCellStyle.BackColor =
                    Color.FromArgb(255, 221, 221);

            // The same amber the product list uses for a line that needs
            // attention but is not yet a problem.
            else if (item.DaysToExpiry is int days && days <= 90)
                row.DefaultCellStyle.BackColor =
                    Color.FromArgb(255, 244, 214);
        }
    }

    public void ShowCount(int shown, int total) =>
        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} of {1:N0}", shown, total);

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ChooseRequested;
    public event EventHandler? ViewModeChanged;
}

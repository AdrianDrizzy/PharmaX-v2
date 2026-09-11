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

        grdProducts.DoubleClick += (_, _) =>
        {
            // A double click means "this one" in both modes; which one
            // it is decides whether that opens the editor or answers
            // the screen that asked for a product.
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
            else EditRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// Runtime. Resolved by the container.
    public frmProductList(ProductListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
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

    public Guid? SelectedId => grdProducts.CurrentRow?.DataBoundItem
        is ProductListItemDto dto ? dto.Id : null;

    /// <summary>
    /// The chosen product's code, which is what a till needs: the sale
    /// service resolves a line by code or barcode, exactly as it does
    /// for a scan, so a picked product and a scanned one take the same
    /// path through the domain.
    /// </summary>
    public string? SelectedCode => grdProducts.CurrentRow?.DataBoundItem
        is ProductListItemDto dto ? dto.Code : null;

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

    public void ShowCount(int shown, int total) =>
        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} of {1:N0}", shown, total);

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ChooseRequested;
}

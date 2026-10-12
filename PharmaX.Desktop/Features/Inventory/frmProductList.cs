using System.Globalization;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Controls;
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

    /// The matching products, offered under the search box as it is
    /// typed into.
    private readonly SearchSuggestions _suggestions;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmProductList()
    {
        InitializeComponent();

        _suggestions = new SearchSuggestions(txtSearch);
        _suggestions.Chosen += (_, chosen) => OnSuggestionChosen(chosen);

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
        btnPrevious.Click += (_, _) =>
            PreviousPageRequested?.Invoke(this, EventArgs.Empty);
        btnNext.Click += (_, _) =>
            NextPageRequested?.Invoke(this, EventArgs.Empty);
        btnExport.Click += (_, _) => Export();

        // A double click means "this one" in both modes and in both
        // views; which mode it is decides whether that opens the editor
        // or answers the screen that asked for a product. Once the grid
        // itself is editable, a double click edits the cell and the Edit
        // button is how the full product form opens.
        foreach (var grid in new[] { grdProducts, grdBatches })
        {
            grid.DoubleClick += (_, _) =>
            {
                if (!grid.ReadOnly) return;

                if (IsPickerMode)
                    ChooseRequested?.Invoke(this, EventArgs.Empty);
                else EditRequested?.Invoke(this, EventArgs.Empty);
            };

            grid.CellValueChanged += (_, e) =>
                OnCellValueChanged(grid, e.RowIndex, e.ColumnIndex);
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
        Application.Security.IPermissionService permissions,
        Application.Reporting.IWorkbookWriter workbooks,
        Application.Abstractions.ICurrentUser user) : this()
    {
        _presenter = presenter;
        _permissions = permissions;
        _workbooks = workbooks;
        _user = user;
        _presenter.Attach(this);
    }

    private readonly Application.Security.IPermissionService? _permissions;
    private readonly Application.Reporting.IWorkbookWriter? _workbooks;
    private readonly Application.Abstractions.ICurrentUser? _user;

    /// <summary>
    /// The page on screen to a workbook - products or batches, whichever
    /// view is showing - with the search and the page it came from.
    /// </summary>
    private void Export()
    {
        if (_workbooks is null || _user is null) return;   // designer

        var grid = BatchView ? grdBatches : grdProducts;
        var search = txtSearch.Text.Trim();

        var saved = GridExport.Offer(this, _workbooks, grid,
            BatchView ? "Products and batches" : "Products",
            $"{(search.Length == 0 ? "All products" : $"\"{search}\"")}, "
            + lblCount.Text, _user.Username);

        if (saved is not null) ShowSaved(saved);
        else if (grid.Rows.Count == 0)
            ShowInfo("There is nothing on the screen to export.");
    }

    /// <summary>
    /// Offered to the people whose save will be accepted. The Add button
    /// used to be there for everyone, and a stock controller filled the
    /// whole form in before Save was refused (DEF-0027).
    /// </summary>
    private void ApplyPermissions()
    {
        if (_permissions is null || IsPickerMode) return;

        var maintains = Application.Security.PermissionServiceExtensions.Has(
            _permissions, Domain.Security.PermissionCode.ProductMaintain);

        if (!maintains)
        {
            btnAdd.Visible = false;
            btnEdit.Text = "&View";
            return;
        }

        // The list is the place these columns are changed. Price and
        // quantity stay on the permissions that already govern them, so
        // a stock controller can correct the shelf without being handed
        // the selling prices.
        bool Has(string code) =>
            Application.Security.PermissionServiceExtensions.Has(
                _permissions, code);

        var price = Has(Domain.Security.PermissionCode.PriceChange);
        var stock = Has(Domain.Security.PermissionCode.StockTakePost);

        Enable(grdProducts);
        Enable(grdBatches);

        Lock(grdProducts, "RetailPrice", !price,
            "Changing the selling price needs the price permission.");
        Lock(grdProducts, "QuantityOnHand", !stock,
            "Changing the quantity on hand needs the stocktake permission.");
        Lock(grdBatches, "QuantityOnHand", !stock,
            "Changing the quantity on hand needs the stocktake permission.");
    }

    private static void Enable(DataGridView grid)
    {
        grid.ReadOnly = false;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
    }

    private static void Lock(DataGridView grid, string column, bool locked,
        string reason)
    {
        if (grid.Columns[column] is not DataGridViewColumn cell) return;

        cell.ReadOnly = locked;
        cell.ToolTipText = locked ? reason : "";
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
        btnExport.Visible = false;

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
        ProductRow p => p.Id,
        BatchRow b => b.ProductId,
        _ => null
    };

    /// <summary>
    /// The batch behind the selected row in the batch view, so Edit can
    /// open the product with that batch already picked out.
    /// </summary>
    public Guid? SelectedBatchId => (Selected() as BatchRow)?.BatchId;

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
        ProductRow p => p.Code,
        BatchRow b => b.Code,
        _ => null
    };

    public ProductCellEditDto? PendingEdit { get; private set; }

    public void ShowProducts(IReadOnlyList<ProductListItemDto> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        var rows = products.Select(ProductRow.From).ToList();

        _showing = true;

        try
        {
            grdProducts.DataSource = rows;

            foreach (DataGridViewRow row in grdProducts.Rows)
            {
                if (row.DataBoundItem is not ProductRow item) continue;

                if (!item.ListedActive)
                    row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
                else if (item.IsBelowReorderLevel)
                    row.DefaultCellStyle.BackColor = PhxTheme.Attention;
            }

            Restore(grdProducts, _keepProduct, null, _scrollProducts);
        }
        finally
        {
            _showing = false;
        }

        _suggestions.Offer(products.Select(Suggest.Product));
    }

    /// <summary>
    /// A product picked from under the search box: the list moves to it,
    /// and a till looking one up takes it straight away.
    /// </summary>
    private void OnSuggestionChosen(Suggestion chosen)
    {
        var code = chosen.Value as string;

        bool Match(object? item) => item switch
        {
            ProductRow p => p.Code == code,
            BatchRow b => b.Code == code,
            _ => false
        };

        if (!Suggest.SelectRow(BatchView ? grdBatches : grdProducts, Match))
            return;

        if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The shelf a batch at a time: what it is, which batch, how much is
    /// left and when it stops being sellable.
    /// </summary>
    public void ShowBatches(IReadOnlyList<ProductBatchListItemDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var bound = rows.Select(BatchRow.From).ToList();

        _showing = true;

        try
        {
            grdBatches.DataSource = bound;

            foreach (DataGridViewRow row in grdBatches.Rows)
            {
                if (row.DataBoundItem is not BatchRow item) continue;

                if (!item.ListedActive)
                {
                    row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
                    continue;
                }

                // Already gone off and still on the shelf, which is the one
                // thing on this grid somebody has to act on today.
                if (item.IsExpired)
                    row.DefaultCellStyle.BackColor = PhxTheme.Problem;

                // The same amber the product list uses for a line that needs
                // attention but is not yet a problem.
                else if (item.DaysValue is int days && days <= 90)
                    row.DefaultCellStyle.BackColor = PhxTheme.Attention;
            }

            Restore(grdBatches, _keepProduct, _keepBatch, _scrollBatches);
        }
        finally
        {
            _showing = false;
        }

        // One suggestion per product, however many batches it is on.
        _suggestions.Offer(rows
            .GroupBy(r => r.ProductId)
            .Select(g => new Suggestion(g.First().Code, g.First().Description,
                Suggest.Details(g.First().Code,
                    $"{Format.Quantity(g.Sum(r => r.QuantityOnHand))} on hand"))));
    }

    public void ShowPage(int page, int pageSize, int shown, int total)
    {
        var first = page * pageSize + 1;
        var pages = Math.Max(1, (total + pageSize - 1) / pageSize);

        lblCount.Text = total == 0
            ? "No products match"
            : string.Format(CultureInfo.CurrentCulture,
                "{0:N0} to {1:N0} of {2:N0}", first, first + shown - 1, total);

        lblPage.Text = string.Format(CultureInfo.CurrentCulture,
            "Page {0:N0} of {1:N0}", page + 1, pages);

        btnPrevious.Enabled = page > 0;
        btnNext.Enabled = page + 1 < pages;
    }

    /// <summary>
    /// Ctrl+Page Down and Ctrl+Page Up turn the page from anywhere on the
    /// screen, the grid included, as they do in a spreadsheet.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var button = keyData switch
        {
            Keys.Control | Keys.PageDown => btnNext,
            Keys.Control | Keys.PageUp => btnPrevious,
            _ => null
        };

        if (button is { Enabled: true })
        {
            button.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ChooseRequested;
    public event EventHandler? ViewModeChanged;
    public event EventHandler? CellEdited;
    public event EventHandler? NextPageRequested;
    public event EventHandler? PreviousPageRequested;

    private bool _showing;
    private Guid? _keepProduct;
    private Guid? _keepBatch;
    private int _scrollProducts = -1;
    private int _scrollBatches = -1;

    private void OnCellValueChanged(DataGridView grid, int rowIndex,
        int columnIndex)
    {
        if (_showing || rowIndex < 0 || columnIndex < 0) return;
        if (IsPickerMode || grid.ReadOnly) return;

        var column = grid.Columns[columnIndex].DataPropertyName;
        var item = grid.Rows[rowIndex].DataBoundItem;

        if (item is ProductRow product)
        {
            NormaliseYesNo(product);
            if (product.Unchanged)
            {
                grid.InvalidateRow(rowIndex);
                return;
            }

            if (column == "Active"
                && string.Equals(product.Active, "No",
                    StringComparison.OrdinalIgnoreCase)
                && !Confirm(
                    "Discontinue this product? It stays on past sales."))
            {
                Revert(grid, rowIndex);
                return;
            }

            if (product.Edit(column) is not { } edit)
            {
                if (column is "BatchTracked" or "Active")
                {
                    ShowError("Enter Yes or No.");
                    Revert(grid, rowIndex);
                }

                return;
            }

            Raise(edit);
            return;
        }

        if (item is not BatchRow batch) return;

        if ((column is "BatchNumber" or "Expiry" or "DaysLeft")
            && batch.BatchId is null)
        {
            if (TryStageNewBatch(grid, rowIndex, batch, column))
                return;
        }

        if (batch.Unchanged) return;

        if (batch.Edit(column) is not { } change)
        {
            ShowError(column is "Expiry" or "DaysLeft"
                ? "Enter the expiry as dd/MM/yyyy, or the days left as a whole number."
                : "That value could not be saved.");
            Revert(grid, rowIndex);
            return;
        }

        Raise(change);
    }

    /// <summary>
    /// A product with stock but no batch yet. The number and the date
    /// are typed in two cells, and the batch is opened only once both
    /// are there.
    /// </summary>
    /// <returns>True when this column was the batch's identity.</returns>
    private bool TryStageNewBatch(DataGridView grid, int rowIndex,
        BatchRow batch, string column)
    {
        if (column == "BatchNumber")
            batch.DraftBatch = batch.BatchNumber.Trim();
        else if (!batch.RememberExpiry(column))
        {
            ShowError(column == "DaysLeft"
                ? "Days left is a whole number."
                : "Enter the expiry as dd/MM/yyyy.");
            Revert(grid, rowIndex);
            return true;
        }

        if (batch.DraftBatch.Length == 0 || batch.DraftExpiry is null)
        {
            ShowInfo(batch.DraftBatch.Length == 0
                ? "Enter the batch number as printed on the pack."
                : "Enter the expiry date for this batch.");
            return true;
        }

        if (batch.QuantityOnHand <= 0m)
        {
            ShowInfo("There is no stock on hand to put onto a batch.");
            Revert(grid, rowIndex);
            return true;
        }

        Raise(new ProductCellEditDto(
            batch.ProductId, null, ProductCell.OpenBatch, batch.DraftBatch,
            batch.DraftExpiry.Value.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture)));

        return true;
    }

    private void Raise(ProductCellEditDto edit)
    {
        RememberPosition();
        PendingEdit = edit;
        CellEdited?.Invoke(this, EventArgs.Empty);
    }

    private void RememberPosition()
    {
        _keepProduct = SelectedId;
        _keepBatch = (grdBatches.CurrentRow?.DataBoundItem as BatchRow)?.BatchId;
        _scrollProducts = ScrollOf(grdProducts);
        _scrollBatches = ScrollOf(grdBatches);
    }

    private static int ScrollOf(DataGridView grid)
    {
        if (!grid.Visible || grid.Rows.Count == 0) return -1;

        try
        {
            return grid.FirstDisplayedScrollingRowIndex;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private void Revert(DataGridView grid, int rowIndex)
    {
        _showing = true;

        try
        {
            switch (grid.Rows[rowIndex].DataBoundItem)
            {
                case ProductRow product:
                    product.Restore();
                    break;
                case BatchRow batch:
                    batch.Restore();
                    break;
            }

            grid.InvalidateRow(rowIndex);

            var context = grid.BindingContext;

            if (grid.DataSource is not null && context is not null
                && context[grid.DataSource] is CurrencyManager manager)
                manager.Refresh();
        }
        finally
        {
            _showing = false;
        }
    }

    private static void Restore(DataGridView grid, Guid? productId,
        Guid? batchId, int scroll)
    {
        if (grid.Rows.Count == 0) return;

        DataGridViewRow? match = null;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is BatchRow batch)
            {
                if (batchId is Guid id && batch.BatchId == id)
                {
                    match = row;
                    break;
                }

                if (match is null && productId is Guid pid
                    && batch.ProductId == pid)
                    match = row;
            }
            else if (row.DataBoundItem is ProductRow product
                     && productId is Guid wanted && product.Id == wanted)
            {
                match = row;
                break;
            }
        }

        if (match is null) return;

        var cell = match.Cells.Cast<DataGridViewCell>()
            .FirstOrDefault(c => c.Visible);

        if (cell is not null) grid.CurrentCell = cell;

        if (scroll < 0 || scroll >= grid.Rows.Count) return;

        try
        {
            grid.FirstDisplayedScrollingRowIndex = scroll;
        }
        catch (InvalidOperationException)
        {
            // The grid is not on screen yet. The selection is enough.
        }
    }

    private static void NormaliseYesNo(ProductRow row)
    {
        if (YesNo(row.BatchTracked) is bool tracked)
            row.BatchTracked = tracked ? "Yes" : "No";

        if (YesNo(row.Active) is bool active)
            row.Active = active ? "Yes" : "No";
    }

    private static bool? YesNo(string text)
    {
        var value = text.Trim();

        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value == "1")
            return true;

        if (value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value == "0")
            return false;

        return null;
    }

    private sealed class ProductRow
    {
        public Guid Id { get; init; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal RetailPrice { get; set; }
        public decimal QuantityOnHand { get; set; }
        public int Schedule { get; set; }
        public string BatchTracked { get; set; } = "No";
        public string Active { get; set; } = "Yes";
        public bool IsBelowReorderLevel { get; init; }

        public bool ListedActive =>
            string.Equals(Active, "Yes", StringComparison.OrdinalIgnoreCase);

        private string _savedCode = "";
        private string _savedDescription = "";
        private decimal _savedRetail;
        private decimal _savedQuantity;
        private int _savedSchedule;
        private string _savedBatchTracked = "No";
        private string _savedActive = "Yes";

        public static ProductRow From(ProductListItemDto item)
        {
            var row = new ProductRow
            {
                Id = item.Id,
                Code = item.Code,
                Description = item.Description,
                RetailPrice = item.RetailPrice,
                QuantityOnHand = item.QuantityOnHand,
                Schedule = item.Schedule,
                BatchTracked = item.IsBatchTracked ? "Yes" : "No",
                Active = item.IsActive ? "Yes" : "No",
                IsBelowReorderLevel = item.IsBelowReorderLevel
            };

            row.Capture();
            return row;
        }

        public bool Unchanged =>
            Code.Trim() == _savedCode
            && Description.Trim() == _savedDescription
            && RetailPrice == _savedRetail
            && QuantityOnHand == _savedQuantity
            && Schedule == _savedSchedule
            && string.Equals(BatchTracked, _savedBatchTracked,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(Active, _savedActive,
                StringComparison.OrdinalIgnoreCase);

        public void Restore()
        {
            Code = _savedCode;
            Description = _savedDescription;
            RetailPrice = _savedRetail;
            QuantityOnHand = _savedQuantity;
            Schedule = _savedSchedule;
            BatchTracked = _savedBatchTracked;
            Active = _savedActive;
        }

        public ProductCellEditDto? Edit(string column)
        {
            var invariant = CultureInfo.InvariantCulture;

            return column switch
            {
                "Code" => Cell(ProductCell.Code, Code.Trim()),
                "Description" => Cell(ProductCell.Description, Description.Trim()),
                "RetailPrice" => Cell(ProductCell.RetailPrice,
                    RetailPrice.ToString(invariant)),
                "QuantityOnHand" => Cell(ProductCell.QuantityOnHand,
                    QuantityOnHand.ToString(invariant)),
                "Schedule" => Cell(ProductCell.Schedule,
                    Schedule.ToString(invariant)),
                "BatchTracked" => YesNo(BatchTracked) is bool tracked
                    ? Cell(ProductCell.BatchTracked, tracked ? "true" : "false")
                    : null,
                "Active" => YesNo(Active) is bool active
                    ? Cell(ProductCell.Active, active ? "true" : "false")
                    : null,
                _ => null
            };
        }

        private ProductCellEditDto Cell(ProductCell column, string value) =>
            new(Id, null, column, value);

        private void Capture()
        {
            _savedCode = Code;
            _savedDescription = Description;
            _savedRetail = RetailPrice;
            _savedQuantity = QuantityOnHand;
            _savedSchedule = Schedule;
            _savedBatchTracked = BatchTracked;
            _savedActive = Active;
        }
    }

    private sealed class BatchRow
    {
        public Guid ProductId { get; init; }
        public Guid? BatchId { get; init; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string BatchNumber { get; set; } = "";
        public string Expiry { get; set; } = "";
        public string DaysLeft { get; set; } = "";
        public decimal QuantityOnHand { get; set; }
        public int? DaysValue { get; init; }
        public bool IsExpired => DaysValue < 0;
        public bool ListedActive { get; init; }

        public string DraftBatch { get; set; } = "";
        public DateOnly? DraftExpiry { get; set; }

        private string _savedCode = "";
        private string _savedDescription = "";
        private string _savedBatch = "";
        private string _savedExpiry = "";
        private string _savedDays = "";
        private decimal _savedQuantity;

        public static BatchRow From(ProductBatchListItemDto item)
        {
            var row = new BatchRow
            {
                ProductId = item.ProductId,
                BatchId = item.BatchId,
                Code = item.Code,
                Description = item.Description,
                BatchNumber = item.BatchNumber ?? "",
                Expiry = item.Expiry?.ToString("dd/MM/yyyy",
                    CultureInfo.CurrentCulture) ?? "",
                DaysLeft = item.DaysToExpiry?.ToString(CultureInfo.CurrentCulture)
                    ?? "",
                QuantityOnHand = item.QuantityOnHand,
                DaysValue = item.DaysToExpiry,
                ListedActive = item.IsActive
            };

            row.Capture();
            return row;
        }

        public bool Unchanged =>
            Code.Trim() == _savedCode
            && Description.Trim() == _savedDescription
            && BatchNumber.Trim() == _savedBatch
            && Expiry.Trim() == _savedExpiry
            && DaysLeft.Trim() == _savedDays
            && QuantityOnHand == _savedQuantity;

        public void Restore()
        {
            Code = _savedCode;
            Description = _savedDescription;
            BatchNumber = _savedBatch;
            Expiry = _savedExpiry;
            DaysLeft = _savedDays;
            QuantityOnHand = _savedQuantity;
            DraftBatch = "";
            DraftExpiry = null;
        }

        public bool RememberExpiry(string column)
        {
            if (column == "DaysLeft")
            {
                if (!int.TryParse(DaysLeft, NumberStyles.Integer,
                        CultureInfo.CurrentCulture, out var days))
                    return false;

                if (!ExpiryText.TryFromDaysLeft(days, out var fromDays))
                    return false;

                DraftExpiry = fromDays;
                return true;
            }

            if (!ExpiryText.TryParse(Expiry, out var expiry)) return false;

            DraftExpiry = expiry;
            return true;
        }

        public ProductCellEditDto? Edit(string column)
        {
            var invariant = CultureInfo.InvariantCulture;

            return column switch
            {
                "Code" => new(ProductId, null, ProductCell.Code, Code.Trim()),
                "Description" => new(ProductId, null, ProductCell.Description,
                    Description.Trim()),
                "QuantityOnHand" => new(ProductId, BatchId,
                    ProductCell.QuantityOnHand,
                    QuantityOnHand.ToString(invariant)),
                "BatchNumber" => BatchId is null
                    ? null
                    : new(ProductId, BatchId, ProductCell.BatchNumber,
                        BatchNumber.Trim()),
                "Expiry" => BatchId is Guid
                            && ExpiryText.TryParse(Expiry, out var expiry)
                    ? new(ProductId, BatchId, ProductCell.Expiry,
                        expiry.ToString("yyyy-MM-dd", invariant))
                    : null,
                "DaysLeft" => BatchId is Guid && int.TryParse(DaysLeft,
                    NumberStyles.Integer, CultureInfo.CurrentCulture, out var days)
                    ? new(ProductId, BatchId, ProductCell.DaysLeft,
                        days.ToString(invariant))
                    : null,
                _ => null
            };
        }

        private void Capture()
        {
            _savedCode = Code;
            _savedDescription = Description;
            _savedBatch = BatchNumber;
            _savedExpiry = Expiry;
            _savedDays = DaysLeft;
            _savedQuantity = QuantityOnHand;
        }
    }
}

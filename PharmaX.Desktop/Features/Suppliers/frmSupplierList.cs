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

        // A different list, so its first page.
        chkIncludeInactive.CheckedChanged += (_, _) =>
        {
            GridPager.Of(grdSuppliers)?.Restart();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        _filters = ListFilterBar.Under(pnlTop);
        _filters.Changed += (_, _) =>
        {
            GridPager.Of(grdSuppliers)?.Restart();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        grdSuppliers.DoubleClick += (_, _) =>
        {
            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
            else EditRequested?.Invoke(this, EventArgs.Empty);
        };

        // Every word typed narrows the list, and the matches come up under
        // the box: a supplier is found by a few letters of any of its
        // name, code, contact or telephone.
        _suggestions = new SearchSuggestions(txtSearch) { MinimumLength = 1 };
        _suggestions.Chosen += (_, chosen) =>
        {
            if (!Suggest.SelectRow(grdSuppliers, item =>
                    item is SupplierListItemDto s && Equals(s.Id, chosen.Value)))
                return;

            if (IsPickerMode) ChooseRequested?.Invoke(this, EventArgs.Empty);
        };

        txtSearch.TextChanged += (_, _) =>
        {
            GridPager.Of(grdSuppliers)?.Restart();
            ShowMatching();
        };
    }

    private readonly SearchSuggestions _suggestions;

    /// Sort, and Filter by when a supplier was added or changed.
    private readonly ListFilterBar _filters;

    /// Every supplier read, before the search narrows them.
    private IReadOnlyList<SupplierListItemDto> _all = Array.Empty<SupplierListItemDto>();

    /// Runtime. Resolved by the container.
    public frmSupplierList(SupplierListPresenter presenter,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) : this()
    {
        _presenter = presenter;
        _scopes = scopes;
        _presenter.Attach(this);
    }

    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopes;

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

        if (_scopes is not null && !IsPickerMode)
            await _filters.RememberSortAsync(_scopes, "suppliers");

        if (_presenter is not null) await _presenter.InitialiseAsync();
        txtSearch.Focus();
    }

    public Guid? SelectedId => grdSuppliers.CurrentRow?.DataBoundItem
        is SupplierListItemDto dto ? dto.Id : null;

    public bool SelectedIsActive => grdSuppliers.CurrentRow?.DataBoundItem
        is not SupplierListItemDto { IsActive: false };

    /// The one button says what it will do to the selected supplier.
    private void ShowSelectionState() =>
        btnDeactivate.Text = SelectedIsActive ? "Deac&tivate" : "Re&activate";

    public bool IncludeInactive => chkIncludeInactive.Checked;

    public Application.Abstractions.ListFilter Filter => _filters.Filter;

    public void ShowSuppliers(IReadOnlyList<SupplierListItemDto> suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);

        _all = suppliers;
        ShowMatching();
    }

    /// The suppliers with every word of the search, a page at a time.
    private void ShowMatching()
    {
        var search = txtSearch.Text.Trim();
        var matching = _all
            .Where(s => Application.Abstractions.SearchTerms.Matches(search,
                s.Name, s.Code, s.ContactName, s.Telephone))
            .ToList();

        GridPager.Attach(grdSuppliers).Show(matching);

        foreach (DataGridViewRow row in grdSuppliers.Rows)
        {
            if (row.DataBoundItem is SupplierListItemDto { IsActive: false })
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        lblCount.Text = search.Length == 0
            ? $"{_all.Count} supplier(s)"
            : $"{matching.Count} of {_all.Count} supplier(s)";
        ShowSelectionState();

        _suggestions.Offer(matching.Select(Suggest.Supplier));
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ToggleActiveRequested;
    public event EventHandler? ChooseRequested;
}

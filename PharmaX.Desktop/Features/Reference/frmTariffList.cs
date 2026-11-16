using System.Globalization;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

/// <summary>
/// Dispensary's Tariffs: what each medical aid society pays for a
/// product, and for how long.
/// </summary>
public partial class frmTariffList : PhxFormBase, ITariffListView
{
    private readonly TariffListPresenter? _presenter;

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    /// The matches, offered under the search box as it is typed into.
    private readonly SearchSuggestions _suggestions;

    /// The "every society" row, which has no identifier of its own.
    private static readonly Guid AllSchemes = Guid.Empty;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmTariffList()
    {
        InitializeComponent();

        _suggestions = new SearchSuggestions(txtSearch);
        _suggestions.Chosen += (_, chosen) => Suggest.SelectRow(grdTariffs,
            item => item is TariffListItemDto t && Equals(t.Id, chosen.Value));

        txtSearch.TextChanged += (_, _) => Restart();
        // A new search or scheme opens on its first matches, not the page
        // the last one was left on.
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            GridPager.Of(grdTariffs)?.Restart();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        cboScheme.SelectedIndexChanged += (_, _) =>
        {
            GridPager.Of(grdTariffs)?.Restart();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        btnEdit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        btnToggle.Click += (_, _) =>
            ToggleActiveRequested?.Invoke(this, EventArgs.Empty);
        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(true);

        chkInactive.CheckedChanged += (_, _) =>
        {
            GridPager.Of(grdTariffs)?.Restart();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        _filters = ListFilterBar.Under(pnlTop);
        _filters.Changed += (_, _) =>
        {
            GridPager.Of(grdTariffs)?.Restart();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };

        grdTariffs.DoubleClick += (_, _) =>
            EditRequested?.Invoke(this, EventArgs.Empty);

        grdTariffs.SelectionChanged += (_, _) => RefreshToggleCaption();
    }

    /// Runtime. Resolved by the container.
    public frmTariffList(TariffListPresenter presenter,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) : this()
    {
        _presenter = presenter;
        _scopes = scopes;
        _presenter.Attach(this);
    }

    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopes;

    /// Sort, and Filter by when a tariff was added or changed.
    private readonly ListFilterBar _filters;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_scopes is not null)
            await _filters.RememberSortAsync(_scopes, "tariffs");

        if (_presenter is not null) await _presenter.InitialiseAsync();
        txtSearch.Focus();
    }

    private void Restart()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public string SearchText => txtSearch.Text.Trim();

    public bool IncludeInactive => chkInactive.Checked;

    public Guid? SchemeFilter =>
        cboScheme.SelectedValue is Guid id && id != AllSchemes ? id : null;

    public Application.Abstractions.ListFilter Filter => _filters.Filter;

    public Guid? SelectedId => grdTariffs.CurrentRow?.DataBoundItem
        is TariffListItemDto dto ? dto.Id : null;

    public bool SelectedIsActive => grdTariffs.CurrentRow?.DataBoundItem
        is TariffListItemDto dto && dto.IsActive;

    public void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        var rows = new List<KeyValuePair<Guid, string>>
        {
            new(AllSchemes, "All societies")
        };

        rows.AddRange(schemes.Select(s =>
            new KeyValuePair<Guid, string>(s.Id, s.Name)));

        cboScheme.DisplayMember = "Value";
        cboScheme.ValueMember = "Key";
        cboScheme.DataSource = rows;
    }

    public void ShowTariffs(IReadOnlyList<TariffListItemDto> tariffs)
    {
        ArgumentNullException.ThrowIfNull(tariffs);

        GridPager.Attach(grdTariffs).Show(tariffs);

        foreach (DataGridViewRow row in grdTariffs.Rows)
        {
            if (row.DataBoundItem is TariffListItemDto item && !item.IsActive)
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        _suggestions.Offer(tariffs.Select(t =>
            new Suggestion(t.Id, t.ProductDescription, Suggest.Details(
                t.ProductCode, t.SchemeName, Format.Money(t.Total)))));

        RefreshToggleCaption();
    }

    public void ShowCount(int shown) =>
        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} shown", shown);

    private void RefreshToggleCaption()
    {
        var hasRow = grdTariffs.CurrentRow?.DataBoundItem is TariffListItemDto;

        btnToggle.Enabled = hasRow;
        btnToggle.Text = SelectedIsActive || !hasRow ? "Deac&tivate" : "&Bring back";
    }

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ToggleActiveRequested;
    public event EventHandler? RefreshRequested;
}

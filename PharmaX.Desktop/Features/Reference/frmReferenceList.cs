using System.Globalization;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

/// <summary>
/// The maintenance list behind four menu entries. Which list it is
/// arrives as a Kind before the dialog is shown; everything else on the
/// screen is the same whichever it is.
/// </summary>
public partial class frmReferenceList : PhxFormBase, IReferenceListView
{
    private readonly ReferenceListPresenter? _presenter;

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    /// The matches, offered under the search box as it is typed into.
    private readonly SearchSuggestions _suggestions;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ProductRead;

    /// Designer only. Never called at runtime.
    public frmReferenceList()
    {
        InitializeComponent();

        _suggestions = new SearchSuggestions(txtSearch);
        _suggestions.Chosen += (_, chosen) => Suggest.SelectRow(grdItems,
            item => item is ReferenceItemDto r && Equals(r.Id, chosen.Value));

        txtSearch.TextChanged += (_, _) => Restart();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();

            // A new search opens on its first matches, not the page the
            // last one was left on.
            GridPager.Of(grdItems)?.Restart();
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
            GridPager.Of(grdItems)?.Restart();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        grdItems.DoubleClick += (_, _) =>
            EditRequested?.Invoke(this, EventArgs.Empty);

        // The button says what it will do to the row in front of you,
        // because "Retire" on an already-retired entry reads as a bug.
        grdItems.SelectionChanged += (_, _) => RefreshToggleCaption();
    }

    /// Runtime. Resolved by the container.
    public frmReferenceList(ReferenceListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public ReferenceKind Kind { get; private set; } = ReferenceKind.Department;

    /// Called before ShowDialog to say which list this is.
    public void Prepare(ReferenceKind kind) => Kind = kind;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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

    public Guid? SelectedId => grdItems.CurrentRow?.DataBoundItem
        is ReferenceItemDto dto ? dto.Id : null;

    public bool SelectedIsActive => grdItems.CurrentRow?.DataBoundItem
        is ReferenceItemDto dto && dto.IsActive;

    public void ApplyLabels(ReferenceLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        Text = $"PharmaX - {labels.Plural}";
        grdItems.Columns["Detail"].HeaderText = labels.DetailCaption;
    }

    public void ShowItems(IReadOnlyList<ReferenceItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        GridPager.Attach(grdItems).Show(items);

        foreach (DataGridViewRow row in grdItems.Rows)
        {
            if (row.DataBoundItem is ReferenceItemDto item && !item.IsActive)
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        _suggestions.Offer(items.Select(i =>
            new Suggestion(i.Id, i.Name, Suggest.Details(i.Code, i.Detail))));

        RefreshToggleCaption();
    }

    public void ShowCount(int shown) =>
        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} shown", shown);

    private void RefreshToggleCaption()
    {
        var hasRow = grdItems.CurrentRow?.DataBoundItem is ReferenceItemDto;

        btnToggle.Enabled = hasRow;
        btnToggle.Text = SelectedIsActive || !hasRow ? "Deac&tivate" : "&Bring back";
    }

    public event EventHandler? SearchChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ToggleActiveRequested;
    public event EventHandler? RefreshRequested;
}

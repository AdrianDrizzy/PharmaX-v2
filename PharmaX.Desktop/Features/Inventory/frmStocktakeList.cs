using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public partial class frmStocktakeList : PhxFormBase, IStocktakeListView
{
    private readonly StocktakeListPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.StockTakeCapture;

    /// Designer only. Never called at runtime.
    public frmStocktakeList()
    {
        InitializeComponent();

        btnNew.Click += (_, _) =>
            OpenNewRequested?.Invoke(this, EventArgs.Empty);
        btnContinue.Click += (_, _) =>
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        btnCancelStocktake.Click += (_, _) =>
            CancelStocktakeRequested?.Invoke(this, EventArgs.Empty);
        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(true);

        grdStocktakes.DoubleClick += (_, _) =>
            ContinueRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmStocktakeList(StocktakeListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    /// <summary>
    /// Set by the shell from the menu entry chosen, so the screen opens
    /// at that step: the scope prompt for "New Stocktake", the count in
    /// progress for the others.
    /// </summary>
    public StocktakeStep OpenAt { get; set; }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is null) return;

        await _presenter.InitialiseAsync();
        await _presenter.OpenAtAsync(OpenAt);
    }

    public Guid? SelectedId => grdStocktakes.CurrentRow?.DataBoundItem
        is StocktakeListItemDto dto ? dto.Id : null;

    public Guid? InProgressId { get; private set; }

    public void ShowStocktakes(IReadOnlyList<StocktakeListItemDto> stocktakes)
    {
        ArgumentNullException.ThrowIfNull(stocktakes);

        grdStocktakes.DataSource = stocktakes.ToList();

        foreach (DataGridViewRow row in grdStocktakes.Rows)
        {
            if (row.DataBoundItem is not StocktakeListItemDto item) continue;

            // A count still open is the one that needs attention, and
            // the one blocking any new count from being started.
            if (item.Status == "Counting")
                row.DefaultCellStyle.BackColor = PhxTheme.Attention;
            else if (item.Status == "Cancelled")
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        var open = stocktakes.Count(s => s.Status == "Counting");
        InProgressId = stocktakes.FirstOrDefault(s => s.Status == "Counting")?.Id;

        lblSummary.Text = open > 0
            ? "A count is in progress. Post or cancel it before starting "
              + "another."
            : $"{stocktakes.Count} stocktake(s). None in progress.";

        btnNew.Enabled = open == 0;
    }

    public StocktakeScopeDto? PromptScope()
    {
        using var dialog = new frmStocktakeScope();

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.Scope
            : null;
    }

    public string? PromptCancelReason() =>
        frmTextPrompt.Ask(this, "Cancel stocktake",
            "Why is this count being abandoned? The count is kept and "
            + "marked cancelled; nothing it recorded is posted.",
            hint: "e.g. Counted the wrong shelf");

    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenNewRequested;
    public event EventHandler? ContinueRequested;
    public event EventHandler? CancelStocktakeRequested;
}

/// <summary>
/// Asks what is being counted. A scope is mandatory because a variance
/// against an unrecorded scope means nothing six months later.
/// </summary>
public sealed class frmStocktakeScope : Form
{
    private readonly TextBox _description = new();
    private readonly TextBox _searchTerm = new();
    private readonly CheckBox _batchTrackedOnly = new();
    private readonly CheckBox _belowReorderOnly = new();

    public frmStocktakeScope()
    {
        Icon = PhxIcons.AppIcon;
        PhxTheme.Follow(this);
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(460, 34),
            Text = "What is being counted? The description is printed on "
                   + "the variance report."
        });

        Add("&Description", _description, 0, 300);
        _description.MaxLength = 200;
        _description.PlaceholderText = "e.g. Shelf 4, cough and cold";

        Add("Product &filter", _searchTerm, 1, 220);
        _searchTerm.PlaceholderText = "Code or name, or blank for all";

        _batchTrackedOnly.AutoSize = true;
        _batchTrackedOnly.Location = new Point(150, 60 + 2 * 34);
        _batchTrackedOnly.Text = "&Batch-tracked items only";
        _batchTrackedOnly.UseVisualStyleBackColor = true;

        _belowReorderOnly.AutoSize = true;
        _belowReorderOnly.Location = new Point(150, 60 + 2 * 34 + 26);
        _belowReorderOnly.Text = "Below &reorder level only";
        _belowReorderOnly.UseVisualStyleBackColor = true;

        var ok = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(310, 60 + 4 * 34),
            Size = new Size(80, 30),
            Text = "&Open"
        };

        var cancel = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(396, 60 + 4 * 34),
            Size = new Size(80, 30),
            Text = "Cancel"
        };

        Controls.AddRange(new Control[]
            { _batchTrackedOnly, _belowReorderOnly, ok, cancel });

        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(496, 60 + 4 * 34 + 46);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = WindowTitles.Brand("PharmaX - Open stocktake");

        // A blank description would put an unlabelled variance in front
        // of whoever reads the report.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK
                || !string.IsNullOrWhiteSpace(_description.Text)) return;

            // Kept open, and told why: it used to stay put without a word.
            e.Cancel = true;
            frmMessage.Show(this, "Give the count a description. It is "
                + "printed on the variance report.",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _description.Focus();
        };
    }

    public StocktakeScopeDto Scope => new(
        Description: _description.Text.Trim(),
        SearchTerm: string.IsNullOrWhiteSpace(_searchTerm.Text)
            ? null
            : _searchTerm.Text.Trim(),
        BatchTrackedOnly: _batchTrackedOnly.Checked,
        BelowReorderOnly: _belowReorderOnly.Checked);

    private void Add(string caption, Control input, int row, int width)
    {
        var y = 60 + row * 34;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, y + 4),
            Text = caption
        });

        input.Location = new Point(150, y);
        input.Size = new Size(width, 23);
        input.TabIndex = row;
        Controls.Add(input);
    }
}

using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// Administration's Control Variables and System Console. One screen:
/// both are a read-only list of what this workstation is set to or is
/// doing, and the only difference is which rows the presenter builds.
/// </summary>
public partial class frmSystemInfo : PhxFormBase, ISystemInfoView
{
    private readonly SystemInfoPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.UserRoleAssign;

    /// Designer only. Never called at runtime.
    public frmSystemInfo()
    {
        InitializeComponent();

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmSystemInfo(SystemInfoPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    /// <summary>
    /// Which of the two lists this window is showing. Set by the caller
    /// before ShowDialog, and read by the presenter in InitialiseAsync.
    /// </summary>
    public SystemInfoMode Mode { get; set; } = SystemInfoMode.ControlVariables;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        Text = Mode == SystemInfoMode.ControlVariables
            ? "PharmaX - Control variables"
            : "PharmaX - System console";

        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public void ShowCaption(string caption) => lblCaption.Text = caption;

    public void ShowRows(IReadOnlyList<SystemInfoRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        grdRows.DataSource = rows.ToList();

        // A value the till holds itself is the one that overrode the
        // installed file, so it is worth being able to pick out.
        foreach (DataGridViewRow row in grdRows.Rows)
        {
            if (row.DataBoundItem is SystemInfoRow item
                && string.Equals(item.Source, "This workstation",
                                 StringComparison.Ordinal))
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
        }
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? CloseRequested;
}

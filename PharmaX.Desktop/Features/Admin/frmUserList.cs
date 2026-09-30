using System.Globalization;
using PharmaX.Application.Security.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

public partial class frmUserList : PhxFormBase, IUserListView
{
    private readonly UserListPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.UserRoleAssign;

    /// Designer only. Never called at runtime.
    public frmUserList()
    {
        InitializeComponent();

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        btnEdit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        btnReset.Click += (_, _) =>
            ResetPasswordRequested?.Invoke(this, EventArgs.Empty);
        btnUnlock.Click += (_, _) =>
            UnlockRequested?.Invoke(this, EventArgs.Empty);
        btnToggleActive.Click += (_, _) =>
            ToggleActiveRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(true);

        chkIncludeInactive.CheckedChanged += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        grdUsers.DoubleClick += (_, _) =>
            EditRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmUserList(UserListPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public Guid? SelectedId => grdUsers.CurrentRow?.DataBoundItem
        is UserListItemDto dto ? dto.Id : null;

    public bool IncludeInactive => chkIncludeInactive.Checked;

    public void ShowUsers(IReadOnlyList<UserListItemDto> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        grdUsers.DataSource = users.ToList();

        foreach (DataGridViewRow row in grdUsers.Rows)
        {
            if (row.DataBoundItem is not UserListItemDto user) continue;

            // Locked out is the one state the administrator is usually
            // here to fix, so it is the one that stands out.
            if (user.IsLockedOut)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 226, 226);
            else if (!user.IsActive)
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }

        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} account(s)", users.Count);

        btnToggleActive.Text = grdUsers.CurrentRow?.DataBoundItem
            is UserListItemDto current && !current.IsActive
            ? "Re&activate"
            : "Deac&tivate";
    }

    public string? PromptNewPassword(string username) =>
        frmPasswordPrompt.Ask(this, "Reset password",
            $"New password for {username} (at least 8 characters). They "
            + "will have to change it at their next sign-in.");

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ResetPasswordRequested;
    public event EventHandler? UnlockRequested;
    public event EventHandler? ToggleActiveRequested;
}

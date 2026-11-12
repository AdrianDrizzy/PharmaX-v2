using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.Admin;

public interface IRolePermissionView : IView
{
    void ShowGrants(IReadOnlyList<RoleGrants> grants);
    IReadOnlyList<RoleGrants> Collect();

    event EventHandler? SaveRequested;
}

public sealed class RolePermissionPresenter : PresenterBase<IRolePermissionView>
{
    private readonly IRolePermissionService _roles;

    public RolePermissionPresenter(IRolePermissionService roles) => _roles = roles;

    protected override void Subscribe() =>
        View.SaveRequested += (_, _) => Enqueue(SaveAsync);

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _roles.ListAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowGrants(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var result = await _roles.SaveAsync(View.Collect());

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo("Saved. Sign out and back in before the change applies.");
        await LoadAsync();
    });
}

/// <summary>
/// Every permission, with a tick for each role. People receive whatever
/// their roles have; there is no separate grant for one person.
/// </summary>
public sealed class frmRolePermissions : PhxFormBase, IRolePermissionView
{
    private readonly RolePermissionPresenter _presenter;
    private readonly DataGridView _grid = new();
    private IReadOnlyList<string> _roles = Array.Empty<string>();

    protected override string? RequiredPermission => PermissionCode.RolePermissions;

    public frmRolePermissions(RolePermissionPresenter presenter)
    {
        _presenter = presenter;
        Build();
        _presenter.Attach(this);
    }

    public event EventHandler? SaveRequested;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await _presenter.InitialiseAsync();
    }

    public void ShowGrants(IReadOnlyList<RoleGrants> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        _roles = grants.Select(g => g.Role).ToList();
        _grid.Columns.Clear();
        _grid.Rows.Clear();

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Permission",
            HeaderText = "Permission",
            ReadOnly = true,
            FillWeight = 240,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        foreach (var grant in grants)
        {
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = grant.Role,
                HeaderText = grant.Role,
                FillWeight = 80,
                MinimumWidth = 96,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        foreach (var code in PermissionCode.All)
        {
            var index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            row.Tag = code;
            row.Cells["Permission"].Value = PermissionCode.Label(code);

            foreach (var grant in grants)
            {
                row.Cells[grant.Role].Value = grant.Permissions.Contains(
                    code, StringComparer.Ordinal);
            }
        }

        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersHeight = 48;
    }

    public IReadOnlyList<RoleGrants> Collect()
    {
        _grid.EndEdit();

        var byRole = _roles.ToDictionary(
            role => role, _ => new List<string>(), StringComparer.Ordinal);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not string code) continue;

            foreach (var role in _roles)
            {
                if (row.Cells[role].Value is true)
                    byRole[role].Add(code);
            }
        }

        return _roles.Select(role => new RoleGrants(role, byRole[role])).ToList();
    }

    private void Build()
    {
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Each person has the permissions of their roles. "
                   + "Tick a role here, then give that role to them under Users. "
                   + "Sign out and back in before a change applies."
        };

        var top = new Panel { Dock = DockStyle.Top, Height = 52 };
        top.Controls.Add(hint);
        hint.Padding = new Padding(12, 8, 12, 0);

        var save = new Button
        {
            Text = "&Save",
            Location = new Point(12, 8),
            Size = new Size(110, 30)
        };

        save.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);

        var close = new Button
        {
            Text = "&Close",
            Size = new Size(110, 30)
        };

        close.Click += (_, _) => CloseView(true);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Name = "pnlBottom" };
        bottom.Controls.Add(save);
        bottom.Controls.Add(close);
        bottom.Resize += (_, _) =>
        {
            close.Top = 8;
            close.Left = bottom.ClientSize.Width - close.Width - 12;
        };

        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdPermissions";
        _grid.AccessibleName = "Permissions by role";
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.StandardTab = true;
        PhxTheme.ApplyToGrid(_grid);

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty
                && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Role permissions";
    }
}

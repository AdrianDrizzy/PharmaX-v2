using System.Globalization;
using PharmaX.Application.Patients;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// Finds the account a sale is going on.
/// </summary>
/// <remarks>
/// A dialog with no presenter, because it does one thing and the till is
/// waiting for the answer. It shows what is left on each account rather
/// than only what is owed: the cashier's question is "can this go on
/// here", and the balance alone does not answer it.
/// </remarks>
public sealed class frmAccountPicker : PhxFormBase
{
    private readonly IAccountService? _accounts;

    private readonly TextBox _search = new();
    private readonly PhxGrid _grid = new();
    private readonly Label _count = new();
    private readonly Button _choose = new();
    private readonly Button _cancel = new();

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SaleCreate;

    /// Designer only. Never called at runtime.
    public frmAccountPicker()
    {
        BuildLayout();

        _search.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Operations.Enqueue(LoadAsync);
        };

        _choose.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => CloseView(false);
        _grid.DoubleClick += (_, _) => Accept();

        PhxTheme.Apply(this);
    }

    /// Runtime. Resolved by the container.
    public frmAccountPicker(IAccountService accounts) : this() =>
        _accounts = accounts;

    /// The account the operator picked, once the dialog returns OK.
    public AccountDto? Chosen { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadAsync();
        _search.Focus();
    }

    private async Task LoadAsync()
    {
        if (_accounts is null) return;

        var result = await _accounts.SearchAsync(_search.Text.Trim());

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        _grid.DataSource = result.Value.Select(AccountRow.From).ToList();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not AccountRow account) continue;

            // An account on hold cannot be sold to, so it is coloured
            // like the refusal it will produce.
            if (account.IsOnHold) PhxTheme.Flag(row, PhxTheme.Problem);
            else if (account.Available <= 0m)
                PhxTheme.Flag(row, PhxTheme.Attention);
        }

        _count.Text = string.Format(CultureInfo.CurrentCulture,
            "{0} account(s)", result.Value.Count);

        _choose.Enabled = result.Value.Count > 0;
        PhxTheme.MakePrimary(_choose);
    }

    private void Accept()
    {
        if (_grid.CurrentRow?.DataBoundItem is not AccountRow row)
        {
            ShowInfo("Choose an account first.");
            return;
        }

        if (row.IsOnHold)
        {
            ShowError($"Account {row.AccountNumber} is on hold: "
                      + row.HoldReason);
            return;
        }

        Chosen = row.Source;
        CloseView(true);
    }

    private void BuildLayout()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 48 };

        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, 17),
            Text = "&Find"
        };

        _search.Location = new Point(70, 13);
        _search.PlaceholderText = "Account number or customer name";
        _search.Size = new Size(320, 23);

        _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _count.AutoSize = false;
        _count.Location = new Point(420, 17);
        _count.Size = new Size(150, 20);
        _count.TextAlign = ContentAlignment.MiddleRight;

        top.Controls.AddRange(new Control[] { _count, _search, label });

        _grid.AutoGenerateColumns = false;
        _grid.Dock = DockStyle.Fill;
        _grid.MultiSelect = false;

        Column("AccountNumber", "Account", 90);
        Column("Name", "Customer", 200);
        Column("BalanceText", "Owes", 90, right: true);
        Column("AvailableText", "Available", 90, right: true);
        Column("StatusText", "Status", 110);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        _choose.Location = new Point(12, 8);
        _choose.Size = new Size(130, 34);
        _choose.Text = "C&hoose";
        _choose.UseVisualStyleBackColor = true;

        _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancel.Location = new Point(450, 8);
        _cancel.Size = new Size(110, 34);
        _cancel.Text = "&Cancel";
        _cancel.UseVisualStyleBackColor = true;

        bottom.Controls.AddRange(new Control[] { _cancel, _choose });

        AcceptButton = _choose;
        CancelButton = _cancel;
        ClientSize = new Size(590, 430);
        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Find account";
    }

    private void Column(string property, string header, int weight,
                        bool right = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            Name = property,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic
        };

        if (right)
            column.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;

        _grid.Columns.Add(column);
    }

    private sealed record AccountRow(AccountDto Source, string AccountNumber,
        string Name, string BalanceText, string AvailableText,
        string StatusText, decimal Available, bool IsOnHold,
        string? HoldReason)
    {
        public static AccountRow From(AccountDto a) => new(
            a, a.AccountNumber, a.Name,
            Format.Amount(a.Balance), Format.Amount(a.Available),
            a.IsOnHold ? "On hold"
                : a.Available <= 0m ? "At its limit" : "Open",
            a.Available, a.IsOnHold, a.HoldReason);
    }
}

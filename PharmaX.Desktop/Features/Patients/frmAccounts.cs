using System.Globalization;
using System.Text;
using PharmaX.Application.Patients;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// What patients owe the pharmacy, and what is done about it.
/// </summary>
/// <remarks>
/// Hand-built rather than split into a designer file, because every
/// control on it is one row of a toolbar or one column of a grid, and a
/// second file would be a hundred lines of declaration for no gain.
/// </remarks>
public sealed class frmAccounts : PhxFormBase, IAccountsView
{
    private readonly AccountsPresenter? _presenter;

    private readonly TextBox _search = new();
    private readonly CheckBox _owingOnly = new();
    private readonly Label _totals = new();
    private readonly PhxGrid _grid = new();

    private readonly Button _open = new();
    private readonly Button _payment = new();
    private readonly Button _statement = new();
    private readonly Button _limit = new();
    private readonly Button _hold = new();
    private readonly Button _refresh = new();
    private readonly Button _close = new();

    private readonly System.Windows.Forms.Timer _debounce =
        new() { Interval = 250 };

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SaleCreate;

    /// Designer only. Never called at runtime.
    public frmAccounts()
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
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        _owingOnly.CheckedChanged += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        _open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _payment.Click += (_, _) =>
            PaymentRequested?.Invoke(this, EventArgs.Empty);

        _statement.Click += (_, _) =>
            StatementRequested?.Invoke(this, EventArgs.Empty);

        _limit.Click += (_, _) =>
            CreditLimitRequested?.Invoke(this, EventArgs.Empty);

        _hold.Click += (_, _) => HoldRequested?.Invoke(this, EventArgs.Empty);

        _refresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        _close.Click += (_, _) => CloseView(false);

        _grid.DoubleClick += (_, _) =>
            StatementRequested?.Invoke(this, EventArgs.Empty);

        _grid.SelectionChanged += (_, _) => ShowSelectionState();

        PhxTheme.Apply(this);
    }

    /// Runtime. Resolved by the container.
    public frmAccounts(AccountsPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        _search.Focus();
    }

    public string SearchText => _search.Text.Trim();
    public bool OwingOnly => _owingOnly.Checked;

    public AccountDto? Selected =>
        _grid.CurrentRow?.DataBoundItem is AccountRow row ? row.Source : null;

    public void ShowAccounts(IReadOnlyList<AccountDto> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        _grid.DataSource = accounts.Select(AccountRow.From).ToList();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not AccountRow account) continue;

            if (account.IsOnHold) PhxTheme.Flag(row, PhxTheme.Problem);
            else if (account.Available <= 0m)
                PhxTheme.Flag(row, PhxTheme.Attention);
        }

        var owed = accounts.Sum(a => a.Balance);

        _totals.Text = string.Format(CultureInfo.CurrentCulture,
            "{0} account(s)     {1} owed", accounts.Count,
            Format.Money(owed));

        _totals.ForeColor = owed > 0m ? PhxTheme.Accent : PhxTheme.Muted;

        ShowSelectionState();
    }

    /// <summary>
    /// Keeps the Hold button saying what it will actually do.
    /// </summary>
    /// <remarks>
    /// One button rather than two, because an account is either on hold
    /// or it is not and a pair of buttons where one is always disabled
    /// is a pair of buttons somebody has to read twice.
    /// </remarks>
    private void ShowSelectionState()
    {
        var account = Selected;
        var has = account is not null;

        _payment.Enabled = has;
        _statement.Enabled = has;
        _limit.Enabled = has;
        _hold.Enabled = has;

        _hold.Text = account?.IsOnHold == true ? "&Release" : "&Hold";
    }

    public (decimal Amount, string Tender, string? Reference)? PromptPayment(
        AccountDto account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var amount = frmNumberPrompt.Ask(this, "Payment on account",
            $"{account.Name} owes {Format.Money(account.Balance)}. "
            + "How much has been paid?", account.Balance, decimals: 2);

        if (amount is not decimal paid || paid <= 0m) return null;

        var tender = frmChoicePrompt.Ask(this, "Payment on account",
            "How was it paid?", new[]
            {
                ("Cash", Domain.Sales.TenderType.Cash),
                ("Card", Domain.Sales.TenderType.Card),
                ("EFT", Domain.Sales.TenderType.Eft)
            });

        if (tender is null) return null;

        var reference = frmTextPrompt.Ask(this, "Payment on account",
            "Receipt or reference number");

        return (paid, tender.Value.ToString(), reference);
    }

    public decimal? PromptCreditLimit(AccountDto account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return frmNumberPrompt.Ask(this, "Credit limit",
            $"What may {account.Name} owe at most?", account.CreditLimit,
            decimals: 2);
    }

    public string? PromptHoldReason() =>
        frmTextPrompt.Ask(this, "Put the account on hold",
            "Why? The cashier who has to refuse the customer will be "
            + "asked.");

    public void ShowStatement(StatementDto statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var text = new StringBuilder();

        text.AppendLine($"{statement.Account.AccountNumber}   "
                        + statement.Account.Name);

        text.AppendLine($"{Format.Date(statement.From)} to "
                        + Format.Date(statement.Until));

        text.AppendLine();

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0,-11} {1,-12} {2,-16} {3,12} {4,12} {5,12}",
            "Date", "What", "Reference", "Charged", "Paid", "Balance"));

        text.AppendLine(new string('-', 80));

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0,-11} {1,-12} {2,-16} {3,12} {4,12} {5,12}",
            "", "Brought forward", "", "", "",
            Format.Amount(statement.Opening)));

        foreach (var line in statement.Lines)
        {
            text.AppendLine(string.Format(CultureInfo.CurrentCulture,
                "{0,-11} {1,-12} {2,-16} {3,12} {4,12} {5,12}",
                Format.Date(line.OccurredOn), line.KindText,
                Trim(line.Reference ?? "", 16),
                line.Charged == 0m ? "" : Format.Amount(line.Charged),
                line.Paid == 0m ? "" : Format.Amount(line.Paid),
                Format.Amount(line.Balance)));
        }

        text.AppendLine(new string('-', 80));

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0,-11} {1,-12} {2,-16} {3,12} {4,12} {5,12}",
            "", "Now due", "", "", "", Format.Amount(statement.Closing)));

        if (statement.Lines.Count == 0)
            text.AppendLine().AppendLine("Nothing moved in this period.");

        frmTextViewer.Show(this,
            $"Statement - {statement.Account.Name}", text.ToString());
    }

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

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
        _search.Size = new Size(300, 23);

        _owingOnly.AutoSize = true;
        _owingOnly.Checked = true;
        _owingOnly.Location = new Point(390, 16);
        _owingOnly.Text = "&Owing only";

        _totals.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _totals.AutoSize = false;
        _totals.Font = PhxTheme.Subheading;
        _totals.Location = new Point(520, 13);
        _totals.Size = new Size(360, 26);
        _totals.TextAlign = ContentAlignment.MiddleRight;

        top.Controls.AddRange(new Control[]
            { _totals, _owingOnly, _search, label });

        _grid.AutoGenerateColumns = false;
        _grid.Dock = DockStyle.Fill;
        _grid.MultiSelect = false;

        Column("AccountNumber", "Account", 80);
        Column("Name", "Customer", 210);
        Column("BalanceText", "Owes", 90, right: true);
        Column("CreditLimitText", "Limit", 90, right: true);
        Column("AvailableText", "Available", 90, right: true);
        Column("TermsText", "Terms", 70, right: true);
        Column("StatusText", "Status", 130);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        var x = 12;

        foreach (var (button, text, width) in new[]
        {
            (_payment, "Take &Payment", 140),
            (_statement, "&Statement", 110),
            (_open, "&New Account", 130),
            (_limit, "Credit &Limit", 120),
            (_hold, "&Hold", 90),
            (_refresh, "&Refresh", 100)
        })
        {
            button.Location = new Point(x, 8);
            button.Size = new Size(width, 34);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
            bottom.Controls.Add(button);
            x += width + 8;
        }

        PhxTheme.MakePrimary(_payment);

        _close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _close.Location = new Point(790, 8);
        _close.Size = new Size(100, 34);
        _close.Text = "&Close";
        _close.UseVisualStyleBackColor = true;
        bottom.Controls.Add(_close);

        CancelButton = _close;
        ClientSize = new Size(910, 560);
        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Patient accounts";
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
        string Name, string BalanceText, string CreditLimitText,
        string AvailableText, string TermsText, string StatusText,
        decimal Available, bool IsOnHold)
    {
        public static AccountRow From(AccountDto a) => new(
            a, a.AccountNumber, a.Name,
            Format.Amount(a.Balance), Format.Amount(a.CreditLimit),
            Format.Amount(a.Available),
            a.TermsDays.ToString(CultureInfo.CurrentCulture),
            a.IsOnHold ? $"On hold - {a.HoldReason}"
                : a.Available <= 0m ? "At its limit" : "Open",
            a.Available, a.IsOnHold);
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? PaymentRequested;
    public event EventHandler? StatementRequested;
    public event EventHandler? CreditLimitRequested;
    public event EventHandler? HoldRequested;
}
